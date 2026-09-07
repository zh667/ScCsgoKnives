using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Engine.Media;
namespace Game;

/// <summary>How a vanilla human holds one class of mod weapon (community plan F08/F11, 0.33.0). Angles are
/// vanilla hand-bone angles in radians: X raises the arm forward (π/2 = horizontal), Swing turns it toward
/// the body's centre line. Hand-authored estimates (估计); the CS2 viewmodel clips are not third-person data.</summary>
public sealed record ScThirdPersonStance(float RightRaise, float RightSwing, bool TwoHanded, bool PitchFollows, float LeftRaise = 0, float LeftSwing = 0) {
    public static readonly ScThirdPersonStance Rifle = new(1.05f, .35f, true, true);
    public static readonly ScThirdPersonStance Pistol = new(1.35f, .45f, false, true);
    public static readonly ScThirdPersonStance Dual = new(1.3f, .1f, true, true);
    public static readonly ScThirdPersonStance Knife = new(.45f, .1f, false, false);
    public static readonly ScThirdPersonStance Grenade = new(.6f, .15f, false, false);
}

/// <summary>Pure third-person pose maths, shared by the game hook, the self-test and the offline preview.
/// Frames: the vanilla human's bones are posed in the body's DAE frame (X right, Y forward, Z up, inch-like
/// units scaled 0.0241 by the root); a hand bone's bind rotation is identity, so its animation rotation
/// RotY(swing) × RotX(raise) acts in that frame and the arm hangs along local -Z.</summary>
public static class ScThirdPersonMath {
    /// <summary>Fist centre in hand-bone units: the mesh box spans x 0..4.82 (right) / -4.82..0 (left), z -22.38..2.45.</summary>
    public static Vector3 HandEndLocal(bool right) => new(right ? 2.41f : -2.41f, 0, -20.5f);
    public static Matrix HandLocal(Vector2 angles) => Matrix.CreateRotationY(angles.Y) * Matrix.CreateRotationX(angles.X);
    /// <summary>ComponentModel.ProcessBoneHierarchy for a bone whose bind rotation is identity: rotation from the
    /// animation, translation from the bind, then the parent.</summary>
    public static Matrix HandAbsolute(Vector3 bindTranslation, Vector2 angles, Matrix bodyAbsolute) {
        Matrix m = HandLocal(angles); m.Translation = bindTranslation;
        return m * bodyAbsolute;
    }
    /// <summary>Direction the arm points in the body frame for these angles (unit; -Z at rest).</summary>
    public static Vector3 ArmDirection(Vector2 angles) => Vector3.TransformNormal(-Vector3.UnitZ, HandLocal(angles));
    /// <summary>Angles that point the arm along a body-frame direction; inverse of ArmDirection.</summary>
    public static Vector2 AnglesToward(Vector3 direction) {
        Vector3 d = direction.LengthSquared() > 1e-8f ? Vector3.Normalize(direction) : -Vector3.UnitZ;
        float swing = MathF.Asin(Math.Clamp(-d.X, -1, 1));
        float raise = MathF.Atan2(d.Y, -d.Z);
        return new Vector2(raise, swing);
    }
    public static Vector3 AimDirection(Vector3 forward, float pitch) {
        Vector3 flat = new(forward.X, 0, forward.Z);
        flat = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : -Vector3.UnitZ;
        return Vector3.Normalize(flat * MathF.Cos(pitch) + Vector3.UnitY * MathF.Sin(pitch));
    }
    /// <summary>World matrix for weapon-local space (forward -Z, up +Y, metres) whose grip point sits at the fist.</summary>
    public static Matrix WeaponWorld(Vector3 gripLocal, Vector3 fistWorld, Vector3 forward, Vector3 up) {
        if (Math.Abs(Vector3.Dot(Vector3.Normalize(forward), up)) > .98f) up = Vector3.UnitX;
        return Matrix.CreateTranslation(-gripLocal) * Matrix.CreateWorld(fistWorld, forward, up);
    }
    /// <summary>Body-frame direction from a world shoulder to a world target.</summary>
    public static Vector3 BodyDirection(Vector3 shoulderWorld, Vector3 targetWorld, Matrix bodyAbsolute) =>
        Vector3.TransformNormal(targetWorld - shoulderWorld, Matrix.Invert(bodyAbsolute));
    public static Vector2 Approach(Vector2 current, Vector2 target, float dt) => current + Math.Min(12 * dt, 1) * (target - current);
}

/// <summary>A mod weapon baked for third person: geometry in weapon-local metres (forward -Z, up +Y) in its idle
/// pose, and where the CS2 rig puts each hand on it (wpnHand_R / wpnHand_L relative to the weapon root).</summary>
public sealed class ScThirdPersonWeapon {
    public sealed record Group(BlockMesh Mesh, string Texture);
    public string Asset;
    public Group[] Groups = [];
    public Vector3 GripRight, GripLeft, Muzzle;
    public bool HasLeftGrip, HasRightGrip;
    public int Vertices;
    static readonly string[] RootBones = ["weapon_offset", "weapon", "root_motion"];
    /// <summary>Headless hosts (PackageCheck) supply the OBJ pieces here; the game reads them through ContentManager.</summary>
    public static Func<string, string, (float[] Positions, float[] Uvs, int[] Indices)> ObjProvider;
    static readonly ScResourceCache<string, ScThirdPersonWeapon> s_cache = new("third-person-weapons", 12, 2000);

    public static ScThirdPersonWeapon For(string asset) {
        if (asset is null) return null;
        if (s_cache.TryGetValue(asset, out var hit)) return hit;
        ScThirdPersonWeapon built = null;
        try { built = Build(asset); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("third-person-" + asset, $"third person {asset}: {e.Message}"); }
        s_cache[asset] = built;
        return built;
    }
    /// <summary>Rig space translated so the weapon root is the origin, then inches/axes to engine metres.</summary>
    public static Matrix LocalPlacement(Cs2Rig.Pose pose) {
        foreach (string name in RootBones)
            if (pose.Bones.TryGetValue(name, out Matrix root)) return Matrix.CreateTranslation(-root.Translation) * Cs2Placement.RigToEngine;
        return Cs2Placement.RigToEngine;
    }
    static ScThirdPersonWeapon Build(string asset) {
        var pose = Cs2Rig.Sample(asset, "idle", 0) ?? throw new InvalidOperationException("no idle pose");
        Matrix placement = LocalPlacement(pose);
        var result = new ScThirdPersonWeapon { Asset = asset };
        result.HasRightGrip = pose.Bones.TryGetValue("wpnHand_R", out Matrix right);
        result.HasLeftGrip = pose.Bones.TryGetValue("wpnHand_L", out Matrix left);
        result.GripRight = result.HasRightGrip ? Vector3.Transform(right.Translation, placement) : Vector3.Zero;
        result.GripLeft = result.HasLeftGrip ? Vector3.Transform(left.Translation, placement) : Vector3.Zero;
        result.Muzzle = pose.Bones.TryGetValue("muzzle", out Matrix muzzle) ? Vector3.Transform(muzzle.Translation, placement)
            : pose.Bones.TryGetValue("wpnTip", out Matrix tip) ? Vector3.Transform(tip.Translation, placement) : Vector3.Zero;
        // Dual pistols: each gun is its own bone in the hands; wpnHand_* sit between them and the weapon chain's muzzle is elsewhere.
        if (pose.Bones.TryGetValue("weapon_r", out Matrix pistolRight) && pose.Bones.TryGetValue("weapon_l", out Matrix pistolLeft)) {
            result.GripRight = Vector3.Transform(pistolRight.Translation, placement); result.GripLeft = Vector3.Transform(pistolLeft.Translation, placement);
            result.HasRightGrip = result.HasLeftGrip = true; result.Muzzle = result.GripRight + new Vector3(0, 0, -.2f);
        }
        int variant = Array.FindIndex(Enumerable.Range(0, CsmcKnifeRig.AssetCount).ToArray(), v => CsmcKnifeRig.GetAssetName(v) == asset);
        bool gun = variant >= 0 && CsmcKnifeRig.IsGun(variant), grenade = variant >= 0 && CsmcKnifeRig.IsGrenade(variant);
        var groups = new Dictionary<string, BlockMesh>(StringComparer.Ordinal);
        BlockMesh Group(string texture) { if (!groups.TryGetValue(texture, out var m)) groups[texture] = m = new BlockMesh(); return m; }
        var objParts = Cs2Rig.GetMeshParts(asset);
        if (gun && objParts.Count > 0) {
            // The AK-47 / M4A1-S / AWP ship as normalised OBJ pieces; the pose's part matrix (binding) puts each back into rig inches.
            string texture = asset + "_hd";
            foreach (string part in objParts) {
                Matrix world = pose.GetPart(part) * placement;
                if (ObjProvider is not null) {
                    var (positions, uvs, indices) = ObjProvider(asset, part);
                    var vertices = new Cs2SkinnedMesh.Vertex[positions.Length / 3];
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = new Cs2SkinnedMesh.Vertex { Position = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]), TextureCoordinate = new Vector2(uvs[i * 2], uvs[i * 2 + 1]) };
                    Append(Group(texture), vertices, indices, world, ref result.Vertices);
                    continue;
                }
                ObjModel model = ContentManager.Get<ObjModel>($"Models/ScCsgoKnives/{asset}_cs2_{part}");
                var target = Group(texture); int before = target.Vertices.Count;
                foreach (ModelMesh mesh in model.Meshes) foreach (ModelMeshPart meshPart in mesh.MeshParts)
                    target.AppendModelMeshPart(meshPart, BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone) * world, false, false, true, false, Color.White);
                result.Vertices += target.Vertices.Count - before;
            }
        }
        else if (gun && Cs2RigidMesh.For(asset) is Cs2RigidMesh rigid && rigid.SetPose(pose, placement)) {
            string texture = asset + "_hd";
            foreach (var part in rigid.Parts) {
                if (!rigid.TryPartWorld(part, out Matrix world)) continue;
                Append(Group(texture), rigid.Vertices, part.Indices, world, ref result.Vertices);
            }
            if (rigid.BlendedParts is { Length: > 0 }) {
                rigid.SkinBlended();
                foreach (var part in rigid.BlendedParts) Append(Group(texture), rigid.BlendedSkinned, part.Indices, Matrix.Identity, ref result.Vertices);
            }
        }
        else {
            var mesh = Cs2SkinnedMesh.Weapon(asset) ?? throw new InvalidOperationException("no weapon mesh");
            if (!mesh.SetPose(pose, placement)) throw new InvalidOperationException("pose not applied");
            mesh.Skin();
            if (grenade) {
                var geometry = ScGrenadeWorldMesh.Build(mesh, asset == "grenade_molotov", false);
                foreach (var part in geometry.Parts) Append(Group(ScGrenadeBlock.MaterialKey(asset, part.Material)), geometry.Vertices, part.Indices, Matrix.Identity, ref result.Vertices);
            }
            else foreach (var part in mesh.Primitives) Append(Group(gun ? asset + "_hd" : asset + "_cs2"), mesh.Skinned, part.Indices, Matrix.Identity, ref result.Vertices);
        }
        result.Groups = groups.Select(g => new Group(g.Value, g.Key)).ToArray();
        if (result.Vertices == 0) throw new InvalidOperationException("no geometry");
        return result;
    }
    /// <summary>Real-scale append (no unit-cube normalisation), compacting to the vertices the indices use.</summary>
    static void Append(BlockMesh target, Cs2SkinnedMesh.Vertex[] vertices, int[] indices, Matrix world, ref int count) {
        var map = new Dictionary<int, ushort>();
        foreach (int index in indices) {
            if (!map.TryGetValue(index, out ushort mapped)) {
                if (target.Vertices.Count >= ushort.MaxValue) throw new InvalidOperationException("too many vertices for one mesh");
                mapped = (ushort)target.Vertices.Count; map[index] = mapped;
                var v = vertices[index];
                target.Vertices.Add(new BlockMeshVertex { Position = Vector3.Transform(v.Position, world), Color = Color.White, TextureCoordinates = v.TextureCoordinate });
                count++;
            }
            target.Indices.Add(mapped);
        }
    }
}

/// <summary>The per-human third-person state and the two hooks' work: pose the arms after vanilla's animation,
/// remember where the weapon is, draw it instead of vanilla's shrunken block. One state per model, computed once
/// per frame in the animate hook, so several cameras in one frame draw the same thing.</summary>
public static class ScThirdPerson {
    sealed class State { public ScThirdPersonWeapon Weapon; public Matrix World; public bool Valid; public string Asset; public Vector2 Right, Left; public bool Logged; public Vector3 Fist; public int Frame; }
    /// <summary>The right fist's world position from the last frame this human was posed (third person only); false in first person.</summary>
    public static bool TryGetFist(ComponentHumanModel human, out Vector3 fist) {
        fist = default;
        if (human is null || !s_states.TryGetValue(human, out var state) || !state.Valid || Time.FrameIndex - state.Frame > 2) return false;
        fist = state.Fist; return true;
    }
    static readonly ConditionalWeakTable<ComponentHumanModel, State> s_states = new();
    /// <summary>估计: positive vanilla LookAngles.Y is looking up; flip here if the device shows the gun dipping when the player looks up.</summary>
    public static float PitchSign = 1;

    public static string AssetFor(int value, out ScThirdPersonStance stance) {
        stance = null;
        int variant = KnifeAnimationController.ResolveVariant(value);
        if (variant < 0) return null;
        string asset = CsmcKnifeRig.GetAssetName(variant);
        if (CsmcKnifeRig.IsGrenade(variant)) stance = ScThirdPersonStance.Grenade;
        else if (CsmcKnifeRig.IsGun(variant)) stance = StanceForGun(asset);
        else stance = ScThirdPersonStance.Knife;
        return asset;
    }
    public static ScThirdPersonStance StanceForGun(string gun) => gun switch {
        "elite" => ScThirdPersonStance.Dual,
        _ => ScGunDurability.ClassOf(gun) is ScGunDurability.Class.Pistol or ScGunDurability.Class.Taser ? ScThirdPersonStance.Pistol : ScThirdPersonStance.Rifle
    };

    /// <summary>OnModelAnimate: vanilla first, then both hands. Returns false to let vanilla run untouched.</summary>
    public static bool Animate(ComponentHumanModel human, float dt) {
        if (human.m_componentMiner is null || human.m_hand1Bone is null || human.m_hand2Bone is null || human.m_bodyBone is null) return false;
        if (human.m_componentCreature?.ComponentHealth?.Health <= 0 || human.m_lieDownFactorModel > 0) return false;
        int value = human.m_componentMiner.ActiveBlockValue;
        string asset = AssetFor(value, out var stance);
        if (asset is null) return false;
        var weapon = ScThirdPersonWeapon.For(asset);
        if (weapon is null || !weapon.HasRightGrip) return false;
        human.AnimateCreature();
        var state = s_states.GetOrCreateValue(human);
        var absolute = new Matrix[human.Model.Bones.Count];
        human.ProcessBoneHierarchy(human.Model.RootBone, Matrix.Identity, absolute);
        Matrix body = absolute[human.m_bodyBone.Index];
        var locomotion = human.m_componentCreature.ComponentLocomotion;
        float pitch = stance.PitchFollows && locomotion is not null ? Math.Clamp(PitchSign * locomotion.LookAngles.Y, -1.2f, 1.2f) : 0;
        Vector2 rightTarget = new(stance.RightRaise + pitch, stance.RightSwing);
        Vector2 right = state.Asset == asset ? ScThirdPersonMath.Approach(state.Right, rightTarget, dt) : rightTarget;
        Matrix hand2 = ScThirdPersonMath.HandAbsolute(human.m_hand2Bone.Transform.Translation, right, body);
        Vector3 fist = Vector3.Transform(ScThirdPersonMath.HandEndLocal(true), hand2);
        Vector3 forward = human.m_componentCreature.ComponentBody.Matrix.Forward;
        Vector3 aim = ScThirdPersonMath.AimDirection(forward, pitch);
        Matrix world = ScThirdPersonMath.WeaponWorld(weapon.GripRight, fist, aim, Vector3.UnitY);
        Vector2 left = human.m_handAngles1;
        if (stance.TwoHanded && weapon.HasLeftGrip) {
            Vector3 target = Vector3.Transform(weapon.GripLeft, world);
            Vector3 shoulder = Vector3.Transform(human.m_hand1Bone.Transform.Translation, body);
            Vector2 leftTarget = ScThirdPersonMath.AnglesToward(ScThirdPersonMath.BodyDirection(shoulder, target, body));
            left = state.Asset == asset ? ScThirdPersonMath.Approach(state.Left, leftTarget, dt) : leftTarget;
        }
        human.m_handAngles2 = right; human.m_handAngles1 = left;
        human.SetBoneTransform(human.m_hand2Bone.Index, ScThirdPersonMath.HandLocal(right));
        human.SetBoneTransform(human.m_hand1Bone.Index, ScThirdPersonMath.HandLocal(left));
        state.Weapon = weapon; state.World = world; state.Valid = true; state.Asset = asset; state.Right = right; state.Left = left; state.Fist = fist; state.Frame = Time.FrameIndex;
        if (!state.Logged) {
            state.Logged = true;
            KnifeLog.Information($"third person {asset}: {weapon.Vertices} vertices in {weapon.Groups.Length} group(s); grips R {weapon.GripRight} L {weapon.GripLeft} (left {(weapon.HasLeftGrip ? "used" : "absent")}); right arm {right} left arm {left}; fist {fist}");
        }
        return true;
    }

    /// <summary>OnModelDrawExtra: draw the baked weapon at the world matrix the animate step chose.</summary>
    public static bool Draw(ComponentHumanModel human, Camera camera) {
        if (!s_states.TryGetValue(human, out var state) || !state.Valid || state.Weapon is null) return false;
        if (human.m_componentMiner is null || AssetFor(human.m_componentMiner.ActiveBlockValue, out _) != state.Asset) { state.Valid = false; return false; }
        var terrain = human.m_subsystemTerrain;
        Vector3 at = state.World.Translation;
        int x = Terrain.ToCell(at.X), y = Terrain.ToCell(at.Y), z = Terrain.ToCell(at.Z);
        var env = new DrawBlockEnvironmentData {
            DrawBlockMode = DrawBlockMode.ThirdPerson, InWorldMatrix = state.World, SubsystemTerrain = terrain, Owner = human.Entity,
            Light = terrain.Terrain.GetCellLight(x, y, z), Humidity = terrain.Terrain.GetSeasonalHumidity(x, z),
            Temperature = terrain.Terrain.GetSeasonalTemperature(x, z) + SubsystemWeather.GetTemperatureAdjustmentAtHeight(y), BillboardDirection = -Vector3.UnitZ,
        };
        Matrix view = state.World * camera.ViewMatrix;
        foreach (var group in state.Weapon.Groups) {
            Texture2D texture;
            try { texture = ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" + group.Texture); }
            catch { continue; }
            BlocksManager.DrawMeshBlock(human.m_subsystemModelsRenderer.PrimitivesRenderer, group.Mesh, texture, Color.White, 1f, ref view, env);
        }
        return true;
    }

    /// <summary>Offline preview for tools: the vanilla human (Content.zip ModelData) posed with the mod's own stance and
    /// weapon placement, as world-space triangles. Yaw and pitch in radians; the body stands at the origin.</summary>
    public static string PreviewJson(ModelData human, string asset, float yaw, float pitch) {
        var stance = asset.StartsWith("grenade_") ? ScThirdPersonStance.Grenade : GunSpec.ForAsset(asset) is not null ? StanceForGun(asset) : ScThirdPersonStance.Knife;
        var weapon = ScThirdPersonWeapon.For(asset) ?? throw new InvalidOperationException("no third-person weapon for " + asset);
        int Bone(string name) => human.Bones.FindIndex(b => b.Name == name);
        int bodyIndex = Bone("Body"), hand1 = Bone("Hand1"), hand2 = Bone("Hand2");
        var anim = new Matrix?[human.Bones.Count];
        anim[bodyIndex] = Matrix.CreateRotationY(yaw);
        float p = Math.Clamp(pitch, -1.2f, 1.2f);
        Vector2 right = new(stance.RightRaise + (stance.PitchFollows ? p : 0), stance.RightSwing);
        anim[hand2] = ScThirdPersonMath.HandLocal(right);
        Matrix[] absolute = Compose(human, anim);
        Matrix body = absolute[bodyIndex];
        Vector3 fist = Vector3.Transform(ScThirdPersonMath.HandEndLocal(true), absolute[hand2]);
        Vector3 forward = Matrix.CreateRotationY(yaw).Forward;
        Matrix world = ScThirdPersonMath.WeaponWorld(weapon.GripRight, fist, ScThirdPersonMath.AimDirection(forward, stance.PitchFollows ? p : 0), Vector3.UnitY);
        Vector2 left = new(0, 0);
        if (stance.TwoHanded && weapon.HasLeftGrip) {
            Vector3 target = Vector3.Transform(weapon.GripLeft, world), shoulder = Vector3.Transform(human.Bones[hand1].Transform.Translation, body);
            left = ScThirdPersonMath.AnglesToward(ScThirdPersonMath.BodyDirection(shoulder, target, body));
        }
        anim[hand1] = ScThirdPersonMath.HandLocal(left);
        absolute = Compose(human, anim);
        var meshes = new List<object>();
        foreach (var mesh in human.Meshes) {
            Matrix m = absolute[mesh.ParentBoneIndex];
            foreach (var part in mesh.MeshParts) {
                var buffer = human.Buffers[part.BuffersDataIndex];
                int stride = buffer.VertexDeclaration.VertexStride;
                var position = buffer.VertexDeclaration.VertexElements.First(e => e.Semantic.StartsWith("POSITION"));
                int count = buffer.Vertices.Length / stride;
                var positions = new float[count * 3];
                for (int i = 0; i < count; i++) {
                    var v = Vector3.Transform(new Vector3(BitConverter.ToSingle(buffer.Vertices, i * stride + position.Offset), BitConverter.ToSingle(buffer.Vertices, i * stride + position.Offset + 4), BitConverter.ToSingle(buffer.Vertices, i * stride + position.Offset + 8)), m);
                    positions[i * 3] = v.X; positions[i * 3 + 1] = v.Y; positions[i * 3 + 2] = v.Z;
                }
                int total = human.Meshes.SelectMany(m2 => m2.MeshParts).Where(p2 => p2.BuffersDataIndex == part.BuffersDataIndex).Max(p2 => p2.StartIndex + p2.IndicesCount);
                int bytesPerIndex = buffer.Indices.Length >= total * 4 ? 4 : 2; // Collada buffers carry 32-bit indices
                var indices = new int[part.IndicesCount];
                for (int i = 0; i < part.IndicesCount; i++) indices[i] = bytesPerIndex == 4 ? BitConverter.ToInt32(buffer.Indices, (part.StartIndex + i) * 4) : BitConverter.ToUInt16(buffer.Indices, (part.StartIndex + i) * 2);
                meshes.Add(new { name = mesh.Name, positions, indices });
            }
        }
        foreach (var group in weapon.Groups) {
            var positions = new float[group.Mesh.Vertices.Count * 3];
            for (int i = 0; i < group.Mesh.Vertices.Count; i++) {
                var v = Vector3.Transform(group.Mesh.Vertices[i].Position, world);
                positions[i * 3] = v.X; positions[i * 3 + 1] = v.Y; positions[i * 3 + 2] = v.Z;
            }
            meshes.Add(new { name = "weapon:" + group.Texture, positions, indices = group.Mesh.Indices.Select(i => (int)i).ToArray() });
        }
        var points = new Dictionary<string, float[]> {
            ["fist"] = [fist.X, fist.Y, fist.Z],
            ["gripLeft"] = Vec(Vector3.Transform(weapon.GripLeft, world)), ["muzzle"] = Vec(Vector3.Transform(weapon.Muzzle, world)),
            ["shoulderL"] = Vec(absolute[hand1].Translation), ["shoulderR"] = Vec(absolute[hand2].Translation),
        };
        return System.Text.Json.JsonSerializer.Serialize(new { asset, yaw, pitch, right = new[] { right.X, right.Y }, left = new[] { left.X, left.Y }, twoHanded = stance.TwoHanded && weapon.HasLeftGrip, points, meshes });
        static float[] Vec(Vector3 v) => [v.X, v.Y, v.Z];
    }
    /// <summary>ComponentModel.ProcessBoneHierarchy on model data: bind rotation × animation, bind translation kept, then the parent.</summary>
    public static Matrix[] Compose(ModelData model, Matrix?[] anim, float modelScale = 1) {
        var result = new Matrix[model.Bones.Count]; var done = new bool[model.Bones.Count];
        Matrix Absolute(int i) {
            if (done[i]) return result[i];
            var bone = model.Bones[i];
            Matrix m = bone.Transform;
            if (anim[i].HasValue) { Vector3 t = m.Translation; m.Translation = Vector3.Zero; m *= anim[i].Value; m.Translation += t; }
            if (bone.ParentBoneIndex < 0 && modelScale != 1) m = Matrix.CreateScale(modelScale) * m;
            result[i] = bone.ParentBoneIndex < 0 ? m : m * Absolute(bone.ParentBoneIndex);
            done[i] = true; return result[i];
        }
        for (int i = 0; i < model.Bones.Count; i++) Absolute(i);
        return result;
    }
}
