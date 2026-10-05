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
    /// <summary>The same point from an arm's own box (video-feedback-20260929 R2): centred across the arm, 0.39 arm
    /// widths above its lower end. For the vanilla male box this is HandEndLocal; a slimmer, shorter or longer arm
    /// (the female model, other rigid-arm models) gets its own fist instead of the male constant.</summary>
    public static Vector3 HandEndFromBox(BoundingBox arm) {
        float width = arm.Max.X - arm.Min.X;
        return new((arm.Min.X + arm.Max.X) * .5f, (arm.Min.Y + arm.Max.Y) * .5f, arm.Min.Z + .39f * width);
    }
    /// <summary>A body at least ThroughFist arm widths long lies across the fist, gripped GripAlong of its length
    /// from its base; every body sinks into the arm's lower end by SinkIntoFist of its own thickness there (at most
    /// of an arm width), never deeper than MaxSink metres: a C4 across a Classic NMM fist sank 0.053 m (c02).</summary>
    public const float ThroughFist = 1.6f, SinkIntoFist = .25f, GripAlong = .45f, MaxSink = .04f;
    /// <summary>How a rigid arm carries a throwable (video-feedback-20260929 R2): weapon-local metres to hand-bone units.
    /// The arm is a box without fingers, its lower end is the hand. A body centred on the fist is swallowed by the
    /// box, and a bottle standing upright in it runs up inside the forearm. So every throwable is carried at the
    /// arm's lower end, a quarter of its thickness sunk into it and the rest in plain sight: a small one the way the
    /// stance holds it (upright), a long one across the fist the way the game's own tools are held, base behind the
    /// fist and neck ahead of it. upright: the placement with the held point at the fist, upright at the stance.
    /// body: the throwable's solid vertices in weapon-local metres.</summary>
    public static Matrix HoldFromBox(BoundingBox arm, Matrix upright, float metresPerUnit, IReadOnlyCollection<Vector3> body) {
        if (body.Count == 0 || !(metresPerUnit > 0)) return upright;
        static (Vector3 Min, Vector3 Max) Bounds(IEnumerable<Vector3> points, Matrix m) {
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (var p in points) { var q = Vector3.Transform(p, m); min = Vector3.Min(min, q); max = Vector3.Max(max, q); }
            return (min, max);
        }
        float width = arm.Max.X - arm.Min.X;
        float Sink(float thickness) => Math.Min(SinkIntoFist * Math.Min(width, thickness), MaxSink / metresPerUnit);
        var (min, max) = Bounds(body, upright); Vector3 size = max - min;
        if (Math.Max(size.X, Math.Max(size.Y, size.Z)) < ThroughFist * width) {
            float over = max.Z - (arm.Min.Z + Sink(size.Z));
            return over > 0 ? upright * Matrix.CreateTranslation(0, 0, -over) : upright;
        }
        // The body's own length goes across the fist (hand +Y: ahead of the hanging arm, up from the raised one),
        // whatever way the viewmodel's idle pose tilts it.
        Vector3 length = LongAxis(body), side = Vector3.Cross(length, Vector3.UnitY);
        Matrix turn = Matrix.Identity;
        if (side.LengthSquared() > 1e-10f) {
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(length, Vector3.UnitY), -1, 1));
            turn = Matrix.CreateFromAxisAngle(Vector3.Normalize(side), angle);
            if (Vector3.Dot(Vector3.TransformNormal(length, turn), Vector3.UnitY) < .999f) turn = Matrix.CreateFromAxisAngle(Vector3.Normalize(side), -angle);
        }
        Matrix across = turn * Matrix.CreateScale(1 / metresPerUnit);
        var (lo, hi) = Bounds(body, across);
        return across * Matrix.CreateTranslation((arm.Min.X + arm.Max.X) * .5f - (lo.X + hi.X) * .5f,
            (arm.Min.Y + arm.Max.Y) * .5f - (lo.Y + GripAlong * (hi.Y - lo.Y)), arm.Min.Z + Sink(hi.Z - lo.Z) - hi.Z);
    }
    /// <summary>The direction a body is long in (largest spread of its vertices), pointing up rather than down.</summary>
    public static Vector3 LongAxis(IReadOnlyCollection<Vector3> body) {
        Vector3 mean = Vector3.Zero;
        foreach (var p in body) mean += p;
        mean /= Math.Max(1, body.Count);
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in body) { Vector3 d = p - mean; xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z; yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z; }
        Vector3 axis = Vector3.Normalize(new Vector3(.3f, 1, .2f));
        for (int i = 0; i < 48; i++) {
            var next = new Vector3(xx * axis.X + xy * axis.Y + xz * axis.Z, xy * axis.X + yy * axis.Y + yz * axis.Z, xz * axis.X + yz * axis.Y + zz * axis.Z);
            if (next.LengthSquared() < 1e-20f) return Vector3.UnitY;
            axis = Vector3.Normalize(next);
        }
        return axis.Y < 0 ? -axis : axis;
    }
    /// <summary>World matrix of a throwable the hand carries rigidly: its placement in the hand, then the hand bone.</summary>
    public static Matrix CarriedByHand(Matrix toHand, Vector3 handBind, Vector2 angles, Matrix body) => toHand * HandAbsolute(handBind, angles, body);
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
    /// <summary>The bore direction that sends the weapon's line from its muzzle (<paramref name="muzzleLocal"/>, held at the
    /// fist by <paramref name="gripLocal"/>) through <paramref name="target"/>, at most <paramref name="maxAngle"/> away from
    /// <paramref name="aim"/>. The muzzle moves as the weapon turns about the grip, so the direction is refined a few times.</summary>
    public static Vector3 ConvergeBore(Vector3 gripLocal, Vector3 muzzleLocal, Vector3 fistWorld, Vector3 aim, Vector3 target, float maxAngle) {
        Vector3 dir = aim;
        for (int i = 0; i < 6; i++) {
            Vector3 to = target - Vector3.Transform(muzzleLocal, WeaponWorld(gripLocal, fistWorld, dir, Vector3.UnitY));
            if (!(to.LengthSquared() > 1e-4f)) return aim;
            dir = Vector3.Normalize(to);
        }
        if (Vector3.Dot(dir, aim) >= MathF.Cos(maxAngle)) return dir;
        Vector3 axis = Vector3.Cross(aim, dir);
        return axis.LengthSquared() < 1e-10f ? aim : Vector3.Normalize(Vector3.TransformNormal(aim, Matrix.CreateFromAxisAngle(Vector3.Normalize(axis), maxAngle)));
    }
    /// <summary>Body-frame direction from a world shoulder to a world target.</summary>
    /// <summary>Angles that put a rigid arm's fist (<paramref name="fistLocal"/>, hand-bone space, off the arm's axis by
    /// half the arm's width) on a body-frame direction; AnglesToward aims the axis instead, a fist width beside the target.</summary>
    public static Vector2 AnglesPlacing(Vector3 fistLocal, Vector3 direction) {
        if (fistLocal.LengthSquared() < 1e-10f) return AnglesToward(direction);
        Vector3 want = direction.LengthSquared() > 1e-8f ? Vector3.Normalize(direction) : -Vector3.UnitZ, aim = want; Vector2 angles = AnglesToward(aim);
        for (int i = 0; i < 8; i++) {
            Vector3 got = Vector3.Normalize(Vector3.TransformNormal(fistLocal, HandLocal(angles)));
            aim = Vector3.Normalize(aim + (want - got)); angles = AnglesToward(aim);
        }
        return angles;
    }
    public static Vector3 BodyDirection(Vector3 shoulderWorld, Vector3 targetWorld, Matrix bodyAbsolute) =>
        Vector3.TransformNormal(targetWorld - shoulderWorld, Matrix.Invert(bodyAbsolute));
    public static Vector2 Approach(Vector2 current, Vector2 target, float dt) => current + Math.Min(12 * dt, 1) * (target - current);
    /// <summary>The body bone's world matrix the way vanilla places it: bind rotation/scale × RotY(yaw) × T(position), bind translation kept.</summary>
    public static Matrix BodyAbsolute(Matrix rootBind, float yaw, Vector3 position) {
        Matrix m = rootBind; Vector3 t = m.Translation; m.Translation = Vector3.Zero;
        m *= Matrix.CreateRotationY(yaw) * Matrix.CreateTranslation(position);
        m.Translation += t; return m;
    }
}

/// <summary>A mod weapon baked for third person: geometry in weapon-local metres (forward -Z, up +Y) in its idle
/// pose, and where the CS2 rig puts each hand on it (wpnHand_R / wpnHand_L relative to the weapon root).</summary>
public sealed class ScThirdPersonWeapon {
    public sealed record Group(BlockMesh Mesh, string Texture, bool Silencer = false, string Bone = null, Matrix BindInverse = default) {
        public string WorldBone;
        public Matrix WorldInverse;
        public string[] VertexBones;
        public Matrix[] VertexInverses;
        public BlockMesh WorldScratch;
    }
    public string Asset;
    public Group[] Groups = [];
    public Vector3 GripRight, GripLeft, Muzzle;
    /// <summary>The Dual Berettas' left gun's muzzle; every other weapon's is <see cref="Muzzle"/>.</summary>
    public Vector3 MuzzleLeft;
    public bool HasLeftGrip, HasRightGrip;
    /// <summary>Throwables: the rig bone that carries the body in the hand; GripRight is its position.</summary>
    public string HoldBone;
    Vector3? bodyCentre;
    /// <summary>Centre of <see cref="Body"/>'s bounds, weapon-local metres: where a rigid left fist reaches for the pin.</summary>
    public Vector3 BodyCentre => bodyCentre ??= Body().Aggregate((Min: new Vector3(float.MaxValue), Max: new Vector3(float.MinValue)), (b, p) => (Vector3.Min(b.Min, p), Vector3.Max(b.Max, p)), b => b.Min.X <= b.Max.X ? (b.Min + b.Max) * .5f : Vector3.Zero);
    /// <summary>The solid body in weapon-local metres; the molotov's flame and liquid sprites are not part of it.</summary>
    public IEnumerable<Vector3> Body() {
        foreach (var group in Groups) {
            if (group.Texture is "weapon_molotov_flame" or "weapon_molotov_liquid") continue;
            foreach (var v in group.Mesh.Vertices) yield return v.Position;
        }
    }
    public int Vertices;
    // Converts the idle bake back to the source weapon frame for world animation.
    public Matrix WorldRootInverse {get;private set;}
    Matrix idleRootPlacement;
    static Matrix Root(Cs2Rig.Pose p) {
        foreach(string name in new[]{"weapon","weapon_offset","root_motion"})if(p.Bones.TryGetValue(name,out var m))return m;
        return Matrix.Identity;
    }
    public Cs2Rig.Pose ActionPose(ScWeaponAction action, bool normalized=false) {
        if(!action.Active||action.Asset!=Asset)return null;
        // Character and viewmodel clips have separate durations. Player ClipTime is already
        // remapped for shell loops; NPC actions use their gameplay-normalized phase.
        string clip=action.Kind==ScWeaponActionKind.Draw?"deploy":action.Clip;
        if(clip==null||!Cs2Rig.HasAlias(Asset,clip))return null;
        float seconds=normalized?action.Progress*Cs2Rig.Duration(Asset,clip):action.ClipTime;
        return Cs2Rig.Sample(Asset,clip,Math.Clamp(seconds,0,Cs2Rig.Duration(Asset,clip)));
    }
    public Matrix PartTransform(Group group,Cs2Rig.Pose pose) {
        if(pose==null||group.Bone==null)return Matrix.Identity;
        Matrix animated=group.Bone.StartsWith("@")?pose.GetPart(group.Bone[1..]):pose.Bones.GetValueOrDefault(group.Bone,Matrix.Identity);
        return group.BindInverse*animated*Matrix.Invert(Root(pose))*idleRootPlacement;
    }
    public bool ShowPart(Group group,Cs2Rig.Pose pose,ScWeaponAction action) {
        string bone=group.Bone?.TrimStart('@');
        if(bone==null)return true;
        if(!ScGunPartVisibility.Visible(Asset,bone,action.Active?action.Clip:"idle",pose?.Time??0,false))return false;
        if(bone=="shell") {
            if(!action.Active||action.Kind!=ScWeaponActionKind.Reload)return false;
            // Some viewmodel clips hide helper shells by moving them far outside the camera.
            // A world camera can see that area, so keep only the shell actually near the hands.
            var center=group.Mesh.CalculateBoundingBox().Center();
            return Vector3.Distance(Vector3.Transform(center,PartTransform(group,pose)),GripRight)<.8f;
        }
        return true;
    }
    static readonly string[] RootBones = ["weapon_offset", "weapon", "root_motion"];
    /// <summary>Headless hosts (PackageCheck) supply the OBJ pieces here; the game reads them through ContentManager.</summary>
    public static Func<string, string, (float[] Positions, float[] Uvs, int[] Indices)> ObjProvider;
    static readonly ScResourceCache<string, ScThirdPersonWeapon> s_cache = new("third-person-weapons", 12, 2000);

    public static ScThirdPersonWeapon For(string asset, bool legacy = false) {
        if (asset is null) return null;
        string cacheKey = asset + (legacy ? "/legacy" : "");
        if (s_cache.TryGetValue(cacheKey, out var hit)) return hit;
        ScThirdPersonWeapon built = null;
        try { built = Build(asset, legacy); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("third-person-" + asset, $"third person {asset}: {e.Message}"); }
        s_cache[cacheKey] = built;
        return built;
    }

    /// <summary>Grid fraction of the model's longest side for the low-detail dropped-item mesh: vertices closer than
    /// this merge, and the triangles that collapse are dropped. Kept separate from the held mesh so the device only
    /// pays for it on the (many, tiny, on-screen) dropped items.</summary>
    public static float DropMeshGrid = .006f;
    static readonly ScResourceCache<string, ScThirdPersonWeapon> s_dropCache = new("drop-weapons", 12, 2000);
    /// <summary>A reduced-precision copy of the assembled weapon for dropped items. The held and third-person
    /// meshes are untouched; only this copy is decimated, once per model, then cached.</summary>
    public static ScThirdPersonWeapon ForDrop(string asset, bool legacy = false) {
        if (asset is null) return null;
        string cacheKey = asset + (legacy ? "/legacy" : "");
        if (s_dropCache.TryGetValue(cacheKey, out var hit)) return hit;
        ScThirdPersonWeapon built = null;
        try {
            var full = For(asset, legacy);
            if (full is not null) {
                built = new ScThirdPersonWeapon {
                    Asset = full.Asset, GripRight = full.GripRight, GripLeft = full.GripLeft, Muzzle = full.Muzzle,
                    HasLeftGrip = full.HasLeftGrip, HasRightGrip = full.HasRightGrip
                };
                built.Groups = full.Groups.Select(g => g with { Mesh = Decimate(g.Mesh) }).ToArray();
                built.Vertices = built.Groups.Sum(g => g.Mesh.Vertices.Count);
            }
        }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("drop-weapon-" + asset, $"drop weapon {asset}: {e.Message}"); }
        s_dropCache[cacheKey] = built;
        return built;
    }
    /// <summary>Vertex clustering: merge vertices that fall in the same grid cell and drop the triangles that
    /// collapse. Cheap, allocation-light and applied once per drop model, so a low-end device draws far fewer
    /// triangles for every loose gun while the silhouette stays recognisable.</summary>
    static BlockMesh Decimate(BlockMesh source) {
        if (source is null || source.Vertices.Count == 0 || source.Indices.Count < 3) return source;
        var box = source.CalculateBoundingBox();
        var size = box.Max - box.Min;
        float grid = Math.Max(size.X, Math.Max(size.Y, size.Z)) * DropMeshGrid;
        if (!(grid > 0f)) return source;
        var cells = new Dictionary<(int, int, int), int>(source.Vertices.Count);
        var vertices = new List<BlockMeshVertex>(source.Vertices.Count);
        var remap = new int[source.Vertices.Count];
        for (int i = 0; i < source.Vertices.Count; i++) {
            var p = source.Vertices[i].Position - box.Min;
            var key = ((int)MathF.Floor(p.X / grid), (int)MathF.Floor(p.Y / grid), (int)MathF.Floor(p.Z / grid));
            if (!cells.TryGetValue(key, out int mapped)) { mapped = vertices.Count; cells[key] = mapped; vertices.Add(source.Vertices[i]); }
            remap[i] = mapped;
        }
        var mesh = new BlockMesh();
        foreach (var v in vertices) mesh.Vertices.Add(v);
        for (int i = 0; i + 2 < source.Indices.Count; i += 3) {
            int a = remap[source.Indices[i]], b = remap[source.Indices[i + 1]], c = remap[source.Indices[i + 2]];
            if (a == b || b == c || a == c) continue;
            mesh.Indices.Add(a); mesh.Indices.Add(b); mesh.Indices.Add(c);
        }
        return mesh;
    }
    /// <summary>Rig space translated so the weapon root is the origin, then inches/axes to engine metres.</summary>
    public static Matrix LocalPlacement(Cs2Rig.Pose pose) {
        foreach (string name in RootBones)
            if (pose.Bones.TryGetValue(name, out Matrix root)) return Matrix.CreateTranslation(-root.Translation) * Cs2Placement.RigToEngine;
        return Cs2Placement.RigToEngine;
    }
    static ScThirdPersonWeapon Build(string asset, bool legacy) {
        var pose = Cs2Rig.Sample(asset, "idle", 0) ?? throw new InvalidOperationException("no idle pose");
        Matrix placement = LocalPlacement(pose);
        var result = new ScThirdPersonWeapon { Asset = asset };
        result.idleRootPlacement=Root(pose)*placement;
        result.WorldRootInverse=Matrix.Invert(result.idleRootPlacement)*Matrix.CreateScale(.0254f);
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
            result.MuzzleLeft = result.GripLeft + new Vector3(0, 0, -.2f);
        }
        else result.MuzzleLeft = result.Muzzle;
        int variant = Array.FindIndex(Enumerable.Range(0, CsmcKnifeRig.AssetCount).ToArray(), v => CsmcKnifeRig.GetAssetName(v) == asset);
        bool gun = variant >= 0 && CsmcKnifeRig.IsGun(variant), grenade = variant >= 0 && CsmcKnifeRig.IsGrenade(variant);
        // A throwable is held by the bone its own rig attaches to the hand (video-feedback-20260929 R2), not by the
        // viewmodel's wrist bone wpnHand_R, which lies 11-21 cm away from these small meshes. HE, flashbang, smoke,
        // decoy and incendiary carry the body on "weapon" (their attachHand_R coincides with it). The molotov's
        // "weapon" stays at the rig origin; its bottle hangs on "molotov" under weapon_hand_r.
        string holdBone = !grenade ? null : pose.Bones.ContainsKey("molotov") ? "molotov" : pose.Bones.ContainsKey("weapon") ? "weapon" : null;
        if (holdBone is not null) { result.GripRight = Vector3.Transform(pose.Bones[holdBone].Translation, placement); result.HasRightGrip = true; result.HoldBone = holdBone; }
        var groups = new Dictionary<(string Texture, bool Silencer, string Bone), BlockMesh>();
        var mixedGroups = new List<Group>();
        string Canonical(string bone) => bone?.StartsWith("@")==true?Cs2Rig.MeshPartBone(asset,bone[1..]):bone??"weapon";
        Matrix WorldInverse(string bone) => Matrix.Invert(pose.Bones.GetValueOrDefault(bone,Root(pose))*placement)*Matrix.CreateScale(.0254f);
        BlockMesh Group(string texture, bool silencer = false, string bone=null) { if (!groups.TryGetValue((texture, silencer,bone), out var m)) groups[(texture, silencer,bone)] = m = new BlockMesh(); return m; }
        var objParts = Cs2Rig.GetMeshParts(asset);
        if (gun && legacy) {
            foreach (var part in ScGunNativeMesh.Parts(asset)) {
                if(!ScGunPartVisibility.Visible(asset,part.Bone,"idle",0,false))continue;
                Matrix world = part.World(pose) * placement;
                string texture = part.Material ?? asset + "_hd";
                if (ObjProvider is not null) {
                    var (positions, uvs, indices) = ObjProvider(asset + "_legacy", part.Name);
                    var vertices = new Cs2SkinnedMesh.Vertex[positions.Length / 3];
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = new Cs2SkinnedMesh.Vertex {
                        Position = new Vector3(positions[i*3], positions[i*3+1], positions[i*3+2]), TextureCoordinate = new Vector2(uvs[i*2], uvs[i*2+1]) };
                    Append(Group(texture, part.Bone == "silencer",part.Bone), vertices, indices, world, ref result.Vertices);
                }
                else {
                    var target = Group(texture, part.Bone == "silencer",part.Bone); int before = target.Vertices.Count;
                    foreach (ModelMesh mesh in part.Model.Meshes) foreach (ModelMeshPart piece in mesh.MeshParts)
                        target.AppendModelMeshPart(piece, BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone) * world, false, false, true, false, Color.White);
                    result.Vertices += target.Vertices.Count - before;
                }
            }
        }
        else if (gun && objParts.Count > 0) {
            // The AK-47 / M4A1-S / AWP ship as normalised OBJ pieces; the pose's part matrix (binding) puts each back into rig inches.
            string texture = asset + "_hd";
            foreach (string part in objParts) {
                Matrix world = pose.GetPart(part) * placement;
                if (ObjProvider is not null) {
                    var (positions, uvs, indices) = ObjProvider(asset, part);
                    var vertices = new Cs2SkinnedMesh.Vertex[positions.Length / 3];
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = new Cs2SkinnedMesh.Vertex { Position = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]), TextureCoordinate = new Vector2(uvs[i * 2], uvs[i * 2 + 1]) };
                    Append(Group(texture, part == "silencer","@"+part), vertices, indices, world, ref result.Vertices);
                    continue;
                }
                ObjModel model = ContentManager.Get<ObjModel>($"Models/ScCsgoKnives/{asset}_cs2_{part}");
                var target = Group(texture, part == "silencer","@"+part); int before = target.Vertices.Count;
                foreach (ModelMesh mesh in model.Meshes) foreach (ModelMeshPart meshPart in mesh.MeshParts)
                    target.AppendModelMeshPart(meshPart, BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone) * world, false, false, true, false, Color.White);
                result.Vertices += target.Vertices.Count - before;
            }
        }
        else if (gun && Cs2RigidMesh.For(asset) is Cs2RigidMesh rigid && rigid.SetPose(pose, placement)) {
            string texture = asset + "_hd";
            foreach (var part in rigid.Parts) {
                if(!ScGunPartVisibility.Visible(asset,rigid.Joints[part.Joint],"idle",0,false))continue;
                if (!rigid.TryPartWorld(part, out Matrix world)) continue;
                string bone=rigid.Joints[part.Joint];
                if(!pose.Bones.ContainsKey(bone))bone=RootBones.FirstOrDefault(pose.Bones.ContainsKey);
                Append(Group(texture, rigid.Joints[part.Joint] == "silencer",bone), rigid.Vertices, part.Indices, world, ref result.Vertices);
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
                // The world prop bone of the same name carries the mesh on a CS actor (cswp_grenade/weapon, cswp_molotov/molotov).
                foreach (var part in geometry.Parts) Append(Group(ScGrenadeBlock.MaterialKey(asset, part.Material), false, holdBone == "weapon" ? null : holdBone), geometry.Vertices, part.Indices, Matrix.Identity, ref result.Vertices);
            }
            else if(!gun)foreach(var part in mesh.Primitives){
                var rigidIndices=new Dictionary<int,List<int>>();var mixed=new List<int>();
                for(int i=0;i<part.Indices.Length;i+=3){
                    int joint=mesh.RigidJoint(part.Indices[i]);
                    if(joint>=0&&joint==mesh.RigidJoint(part.Indices[i+1])&&joint==mesh.RigidJoint(part.Indices[i+2])){
                        if(!rigidIndices.TryGetValue(joint,out var list))rigidIndices[joint]=list=[];
                        list.AddRange(part.Indices.AsSpan(i,3));
                    }else mixed.AddRange(part.Indices.AsSpan(i,3));
                }
                foreach(var entry in rigidIndices)Append(Group(asset+"_cs2",false,mesh.Joints[entry.Key]),mesh.Skinned,entry.Value.ToArray(),Matrix.Identity,ref result.Vertices);
                if(mixed.Count>0){
                    var target=new BlockMesh();Append(target,mesh.Skinned,mixed.ToArray(),Matrix.Identity,ref result.Vertices);
                    var bones=mixed.Distinct().Select(i=>mesh.RigidJoint(i)>=0?mesh.Joints[mesh.RigidJoint(i)]:throw new InvalidOperationException("blended knife vertex")).ToArray();
                    var scratch=new BlockMesh();foreach(var v in target.Vertices)scratch.Vertices.Add(v);foreach(var index in target.Indices)scratch.Indices.Add(index);
                    mixedGroups.Add(new Group(target,asset+"_cs2"){VertexBones=bones,VertexInverses=bones.Select(WorldInverse).ToArray(),WorldScratch=scratch});
                }
            }
            else foreach(var part in mesh.Primitives)Append(Group(asset+"_hd"),mesh.Skinned,part.Indices,Matrix.Identity,ref result.Vertices);
        }
        result.Groups = groups.Select(g => new Group(g.Value, g.Key.Texture, g.Key.Silencer,g.Key.Bone,
            g.Key.Bone==null?Matrix.Identity:Matrix.Invert((g.Key.Bone.StartsWith("@")?pose.GetPart(g.Key.Bone[1..]):pose.Bones[g.Key.Bone])*placement)){
                WorldBone=Canonical(g.Key.Bone),WorldInverse=WorldInverse(Canonical(g.Key.Bone))
            }).Concat(mixedGroups).ToArray();
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
    sealed class State { public ScThirdPersonWeapon Weapon; public Matrix World; public bool Valid, Legacy; public string Asset; public int Skin; public Texture2D GunTexture; public Vector2 Right, Left; public Vector3 Fist; public int Frame; public long ActionSequence; public bool Released; public int Value; public float Converge; }
    /// <summary>The right fist solved from the body alone (position, yaw, crouch) and a stance: the logic pose, independent
    /// of whether any camera drew this human. Used for the grenade's start point in first and third person alike.</summary>
    public static bool FistFromLogic(ComponentHumanModel human, ComponentBody body, ScThirdPersonStance stance, out Vector3 fist) {
        fist = default;
        if (human?.Model is null || body is null || human.m_hand2Bone is null) return false;
        float yaw = body.Rotation.ToYawPitchRoll().X;
        Matrix bodyAbsolute = ScThirdPersonMath.BodyAbsolute(human.Model.RootBone.Transform, yaw, body.Position - Vector3.UnitY * MathUtils.Lerp(0, .7f, MathUtils.Sigmoid(body.CrouchFactor, 4)));
        Matrix hand = ScThirdPersonMath.HandAbsolute(human.m_hand2Bone.Transform.Translation, new Vector2(stance.RightRaise, stance.RightSwing), bodyAbsolute);
        fist = Vector3.Transform(ScThirdPersonMath.HandEndLocal(true), hand);
        return ScGrenadeState.Finite(fist);
    }
    static readonly ConditionalWeakTable<ComponentHumanModel, State> s_states = new();
    sealed class Fists { public Vector3? Right, Left; public readonly Dictionary<string, Matrix> Holds = []; }
    static readonly ConditionalWeakTable<Model, Fists> s_fists = new();
    /// <summary>Fist centre of this model's own arm, in hand-bone units: from the arm mesh box where the model has one
    /// (vanilla male and female, rigid-arm models of other mods), else the measured male constant, said once.</summary>
    public static Vector3 FistLocal(Model model, ModelBone hand, bool right) {
        var fists = s_fists.GetOrCreateValue(model);
        if ((right ? fists.Right : fists.Left) is { } known) return known;
        Vector3 fist;
        if (ArmBox(model, hand) is { } arm) fist = ScThirdPersonMath.HandEndFromBox(arm);
        else {
            fist = ScThirdPersonMath.HandEndLocal(right);
            KnifeDiagnostics.WarnOnce("third-person-fist-" + (right ? "right" : "left"), $"third person: no usable arm box on bone {hand?.Name}; the vanilla male fist point is used.");
        }
        if (right) fists.Right = fist; else fists.Left = fist;
        return fist;
    }
    /// <summary>The box of the whole arm the hand bone moves, in that bone's units, where it is one: longer than twice
    /// its width. The arm is every mesh on the bone and on its descendants, placed by their bind transforms: the vanilla
    /// human keeps its arm on Hand1/Hand2, while NMM's bone sets (r2-c4-completion-20260929: NekoMeko Model 1.1
    /// Minecraft Classic/Slim and ScMale/ScFemale) rotate an upper arm Arm1/Arm2 whose child Hand1/Hand2 carries the
    /// forearm and fist. A box thinner than a hundredth of its length is not an arm.</summary>
    public static BoundingBox? ArmBox(Model model, ModelBone hand) {
        if (model is null || hand is null) return null;
        BoundingBox? box = null;
        Matrix? Relative(ModelBone bone) {
            Matrix m = Matrix.Identity;
            for (var b = bone; b != hand; b = b.ParentBone) { if (b is null) return null; m *= b.Transform; }
            return m;
        }
        foreach (var mesh in model.Meshes) {
            if (Relative(mesh.ParentBone) is not { } m) continue;
            var bb = mesh.BoundingBox;
            var placed = new BoundingBox(from x in new[] { bb.Min.X, bb.Max.X } from y in new[] { bb.Min.Y, bb.Max.Y } from z in new[] { bb.Min.Z, bb.Max.Z } select Vector3.Transform(new Vector3(x, y, z), m));
            box = box is { } b ? BoundingBox.Union(b, placed) : placed;
        }
        if (box is not { } arm) return null;
        float width = arm.Max.X - arm.Min.X, length = arm.Max.Z - arm.Min.Z;
        return width > .01f * length && length > 2 * width ? arm : null;
    }
    /// <summary>How this model's right arm carries this throwable, weapon-local metres to hand-bone units: its own arm
    /// box and the throwable's own body decide (ScThirdPersonMath.HoldFromBox), once per model and throwable.
    /// Without a usable arm box the held point sits at the fist, upright at the stance.</summary>
    public static Matrix HoldInHand(Model model, ModelBone body, ModelBone hand, ScThirdPersonWeapon weapon, ScThirdPersonStance stance) {
        var fists = s_fists.GetOrCreateValue(model);
        if (fists.Holds.TryGetValue(weapon.Asset, out var known)) return known;
        // The stance at rest, body in its bind pose facing -Z: the hand carries the throwable rigidly from there. The
        // hand's own parent chain (Body, or Chest on NMM's bone sets) places the shoulder.
        Matrix bind = Matrix.Identity;
        for (var parent = hand.ParentBone ?? body; parent is not null; parent = parent.ParentBone) bind *= parent.Transform;
        Matrix rest = ScThirdPersonMath.HandAbsolute(hand.Transform.Translation, new Vector2(stance.RightRaise, stance.RightSwing), bind);
        Matrix hold = ScThirdPersonMath.WeaponWorld(weapon.GripRight, Vector3.Transform(FistLocal(model, hand, true), rest), -Vector3.UnitZ, Vector3.UnitY) * Matrix.Invert(rest);
        if (ArmBox(model, hand) is { } arm) hold = ScThirdPersonMath.HoldFromBox(arm, hold, Vector3.TransformNormal(Vector3.UnitX, rest).Length(), weapon.Body().ToArray());
        fists.Holds[weapon.Asset] = hold;
        return hold;
    }
    /// <summary>The item a human is seen holding and the throw it is part of: the held item, or the grenade just
    /// thrown while its action still runs (the last one of a stack leaves an empty slot behind).</summary>
    public static int PresentedValue(ComponentHumanModel human, out ScThrowPhase phase) {
        int value = human.m_componentMiner?.ActiveBlockValue ?? 0; phase = default;
        var player = PlayerOf(human);
        if (player is null) return value;
        // The plant tail does not depend on the grenade subsystem being present.
        var throwing = human.Project?.FindSubsystem<SubsystemScGrenades>(false)?.ThrowPhase(player) ?? default;
        // Multiplayer: a player this process does not simulate shows the throw its own process reported (ScNetPresentation).
        if (!throwing.Active) throwing = ScNetPresentation.ThrowOf(player);
        if (!throwing.Active) return value == 0 && PlantPhaseOf(human) is { Active: true, Placed: true } planted ? planted.Value : value;
        if (value == 0 && throwing.Released) { phase = throwing; return throwing.Value; }
        if (AssetFor(value, out _) == throwing.Asset) phase = throwing;
        return value;
    }
    /// <summary>The player this human model belongs to, from the model's own references (the r2-06 lookup through
    /// Entity.FindComponent threw on the C4 regression's bare fixture entity; it failed c4/local-fpp-suppresses-extra-hand-weapon-only).</summary>
    static ComponentPlayer PlayerOf(ComponentHumanModel human) => human is null ? null : human.m_componentPlayer ?? human.m_componentMiner?.ComponentPlayer;
    /// <summary>The C4 this human's player is planting (default when none): the same gameplay clock that commits the
    /// charge. The last C4 of a stack stays the presented item until the recovery ends, with an empty hand.</summary>
    public static ScPlantPhase PlantPhaseOf(ComponentHumanModel human) {
        var player = PlayerOf(human);
        if (player is null) return default;
        var plant = human.Project?.FindSubsystem<SubsystemScC4>(false)?.PlantPhase(player) ?? default;
        // Multiplayer: a player this process does not simulate shows the plant its own process reported (ScNetPresentation).
        return plant.Active ? plant : ScNetPresentation.PlantOf(player);
    }
    /// <summary>Positive vanilla LookAngles.Y is looking up (measured on 1.9.3.1, 2026-10-01: LookAngles.Y 0.5 tilts the weapon's
    /// forward to y 0.479, vanilla and NekoMeko models alike, tools/MpM0/sp_tp_pitch.py).</summary>
    public static float PitchSign = 1;
    /// <summary>Where a held gun's barrel points (first-person-eye-shot-20261001, 2026-10-01): the shot leaves the eye along the
    /// character's look, the gun hangs about 0.45 m lower, so a barrel parallel to the look sent the tracer (muzzle → impact)
    /// across it, up to 12° at a ground hit 3 m ahead. The barrel aims at what the eye's line meets instead (terrain and
    /// bodies, as the shot traces them), no nearer than <see cref="ConvergeMin"/> and at most <see cref="ConvergeMaxAngle"/>
    /// off the look; nothing within <see cref="ConvergeRange"/> means that far. The distance is smoothed so the gun does not
    /// jump as the look sweeps over an edge.</summary>
    public const float ConvergeRange = 64, ConvergeMin = 1.5f, ConvergeMaxAngle = .35f;
    static float AimDistance(ComponentHumanModel human, Vector3 eye, Vector3 look) {
        float distance = ConvergeRange;
        try {
            if (human.m_subsystemTerrain is { } terrain && ScGunRange.TraceBullet(terrain, eye, look, ConvergeRange) is { } cell) distance = Math.Min(distance, cell.Distance);
            var entity = human.Entity;
            if (human.Project?.FindSubsystem<SubsystemBodies>(false)?.Raycast(eye, eye + look * distance, .35f, (b, d) => b.Entity != entity) is { } body)
                distance = Math.Min(distance, body.Distance);
        }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("third-person-converge", "[ScCsgoKnives] third-person aim point unavailable, barrel kept parallel: " + e.Message); }
        return Math.Max(distance, ConvergeMin);
    }

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

    /// <summary>OnModelCalculateBones: runs after the model animated (SCAPI 1.9.2.1 drives the human through an
    /// AnimationController, so OnModelAnimate never fires for it) and before the absolute matrices are composed.
    /// Once per frame the pose is solved from the animated body bone; every camera's call re-applies the hand bones.
    /// Returns false to leave vanilla untouched.</summary>
    public static bool Pose(ComponentHumanModel human, float dt) {
        if (human.Model?.HasSkin == true) { s_states.Remove(human); return false; }
        if (human.m_componentMiner is null || human.m_hand1Bone is null || human.m_hand2Bone is null || human.m_bodyBone is null || human.m_boneTransforms is null) return false;
        if (human.m_componentCreature?.ComponentHealth?.Health <= 0 || human.m_lieDownFactorModel > 0) return false;
        int value = PresentedValue(human, out var phase);
        string asset = AssetFor(value, out var stance);
        if (asset is null) return false;
        int skin = Terrain.ExtractContents(value) == BlocksManager.GetBlockIndex<ScGunBlock>(true) ? ScGunBlock.SkinOf(value) : stance == ScThirdPersonStance.Knife ? ScKnifeBlock.SkinOf(value) : 0;
        Texture2D gunTexture = null;
        bool legacy = stance != ScThirdPersonStance.Knife && stance != ScThirdPersonStance.Grenade
            && ScGunNativeMesh.Resolve(asset, skin, out gunTexture, out _) is not null;
        var weapon = ScThirdPersonWeapon.For(asset, legacy);
        if (weapon is null || !weapon.HasRightGrip) return false;
        var state = s_states.GetOrCreateValue(human);
        var action=ScNetPresentation.ActionOf(human.Entity);
        if(action.Asset!=asset)action=default;
        bool throwable = stance == ScThirdPersonStance.Grenade;
        var planting = asset == "c4" ? PlantPhaseOf(human) : default;
        if (state.Valid && state.Asset == asset && state.Skin == skin && state.Frame == Time.FrameIndex && state.ActionSequence==action.Sequence) {
            human.SetBoneTransform(human.m_hand2Bone.Index, ScThirdPersonMath.HandLocal(state.Right));
            human.SetBoneTransform(human.m_hand1Bone.Index, ScThirdPersonMath.HandLocal(state.Left));
            return true;
        }
        if (!human.m_boneTransforms[human.m_bodyBone.Index].HasValue) return false; // the body was not placed this frame
        var absolute = new Matrix[human.Model.Bones.Count];
        human.ProcessBoneHierarchy(human.Model.RootBone, Matrix.Identity, absolute);
        Matrix body = absolute[human.m_bodyBone.Index];
        // Each arm hangs from its own parent: Body on the vanilla human, Chest on NMM's bone sets (r2-c4-completion-20260929:
        // placing NMM's Arm2 on Body put held items about 0.8 m below its hand on the Minecraft skeletons).
        Matrix shoulder2 = human.m_hand2Bone.ParentBone is { } p2 ? absolute[p2.Index] : body, shoulder1 = human.m_hand1Bone.ParentBone is { } p1 ? absolute[p1.Index] : body;
        var locomotion = human.m_componentCreature.ComponentLocomotion;
        float pitch = stance.PitchFollows && locomotion is not null ? Math.Clamp(PitchSign * locomotion.LookAngles.Y, -1.2f, 1.2f) : 0;
        Vector2 rightTarget = new(stance.RightRaise + pitch, stance.RightSwing);
        var thrown = throwable ? ScThirdPersonMotion.Throw(phase, stance) : planting.Active ? ScThirdPersonMotion.Plant(planting, stance) : default;
        if (thrown.Active) rightTarget = thrown.Right; else rightTarget+=ScThirdPersonMotion.Right(action);
        // The throw itself follows the gameplay clock directly: smoothing would let the arm lag behind the release.
        Vector2 right = state.Asset != asset || thrown.Direct ? rightTarget : ScThirdPersonMath.Approach(state.Right, rightTarget, dt);
        Matrix hand2 = ScThirdPersonMath.HandAbsolute(human.m_hand2Bone.Transform.Translation, right, shoulder2);
        Vector3 fistLocal = FistLocal(human.Model, human.m_hand2Bone, true);
        Vector3 fist = Vector3.Transform(fistLocal, hand2);
        Vector3 forward = human.m_componentCreature.ComponentBody.Matrix.Forward;
        Vector3 aim = ScThirdPersonMath.AimDirection(forward, pitch);
        Matrix world;
        // A throwable, and a C4 being planted, move rigidly with the hand.
        if (throwable || planting.Active) world = ScThirdPersonMath.CarriedByHand(HoldInHand(human.Model, human.m_bodyBone, human.m_hand2Bone, weapon, stance), human.m_hand2Bone.Transform.Translation, right, shoulder2);
        else {
            Vector3 bore = aim;
            if (stance.PitchFollows) {
                Vector3 eye = human.EyePosition, look = Matrix.CreateFromQuaternion(human.EyeRotation).Forward;
                float distance = AimDistance(human, eye, look);
                state.Converge = state.Asset == asset && state.Converge > 0 ? state.Converge + Math.Min(12 * dt, 1) * (distance - state.Converge) : distance;
                bore = ScThirdPersonMath.ConvergeBore(weapon.GripRight, weapon.Muzzle, fist, aim, eye + look * state.Converge, ConvergeMaxAngle);
            }
            world = ScThirdPersonMath.WeaponWorld(weapon.GripRight, fist, bore, Vector3.UnitY);
            world=Matrix.CreateTranslation(-weapon.GripRight)*ScThirdPersonMotion.WeaponRotation(action)*Matrix.CreateTranslation(weapon.GripRight)*world;
        }
        Vector2 left = human.m_handAngles1;
        // Pulling the pin, the left fist goes to the throwable where this model's right hand carries it (the authored
        // chest angles left McSlim's fist 0.127 m from the bottle, c02).
        if (throwable && thrown.Active && phase.Stage == 0)
            thrown = thrown with { Left = ScThirdPersonMotion.PullLeft(phase, LeftReach(human.Model, human.m_hand1Bone, shoulder1, Vector3.Transform(weapon.BodyCentre, world))) };
        if (thrown.Active && thrown.LeftUsed) left = state.Asset != asset || thrown.Direct ? thrown.Left : ScThirdPersonMath.Approach(state.Left, thrown.Left, dt);
        else if (stance.TwoHanded && weapon.HasLeftGrip) {
            Vector3 target = Vector3.Transform(weapon.GripLeft, world);
            Vector3 shoulder = Vector3.Transform(human.m_hand1Bone.Transform.Translation, shoulder1);
            Vector2 leftTarget = ScThirdPersonMath.AnglesToward(ScThirdPersonMath.BodyDirection(shoulder, target, shoulder1));
            leftTarget+=ScThirdPersonMotion.Left(action);
            left = state.Asset == asset ? ScThirdPersonMath.Approach(state.Left, leftTarget, dt) : leftTarget;
        }
        human.m_handAngles2 = right; human.m_handAngles1 = left;
        human.SetBoneTransform(human.m_hand2Bone.Index, ScThirdPersonMath.HandLocal(right));
        human.SetBoneTransform(human.m_hand1Bone.Index, ScThirdPersonMath.HandLocal(left));
        state.Weapon = weapon; state.World = world; state.Valid = true; state.Asset = asset; state.Right = right; state.Left = left; state.Fist = fist; state.Frame = Time.FrameIndex;
        state.Skin = skin; state.GunTexture = gunTexture; state.Legacy = legacy;
        state.ActionSequence=action.Sequence; state.Released = throwable && phase.Active && phase.Released || planting.Active && planting.Placed; state.Value = value;
        return true;
    }

    static Texture2D Load(string name) {
        try { return ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" + name); }
        catch { return null; }
    }

    /// <summary>Angles putting the left fist of <paramref name="hand"/> (hanging from <paramref name="parent"/>) on a world point.</summary>
    static Vector2 LeftReach(Model model, ModelBone hand, Matrix parent, Vector3 target) =>
        ScThirdPersonMath.AnglesPlacing(FistLocal(model, hand, false), ScThirdPersonMath.BodyDirection(Vector3.Transform(hand.Transform.Translation, parent), target, parent));

    /// <summary>The muzzle of the third-person weapon this player's body carries, as placed this frame or the last: the
    /// tracer's start when the first-person weapon is not the one on screen (another camera, or another player in this
    /// process). The Dual Berettas answer their left gun for its own bone (post-mp-bugs-20260930 review B).</summary>
    sealed class Reported { public string Asset; public Vector3 Muzzle, MuzzleLeft; public int Frame; }
    static readonly ConditionalWeakTable<ComponentHumanModel, Reported> s_reported = new();
    /// <summary>A renderer that draws this human's held weapon itself — the CS player appearance on a skinned model, which
    /// <see cref="Pose"/> leaves alone — reports the muzzles it drew this frame, so the shot's tracer leaves them too.</summary>
    public static void ReportMuzzle(ComponentHumanModel human, string asset, Vector3 muzzle, Vector3 muzzleLeft) {
        if (human is null || asset is null || !ScGrenadeState.Finite(muzzle)) return;
        var r = s_reported.GetOrCreateValue(human);
        r.Asset = asset; r.Muzzle = muzzle; r.MuzzleLeft = muzzleLeft; r.Frame = Time.FrameIndex;
    }
    public static bool TryGetMuzzleWorld(ComponentPlayer player, string gun, string bone, out Vector3 world) {
        world = default;
        if (player?.Entity?.FindComponent<ComponentHumanModel>() is not { } human) return false;
        bool left = bone is not null && GunSpec.ForAsset(gun)?.LeftMuzzleBone == bone;
        if (!s_states.TryGetValue(human, out var state)) {
            if (!s_reported.TryGetValue(human, out var r) || r.Asset != gun || Time.FrameIndex - r.Frame > 2) return false;
            world = left ? r.MuzzleLeft : r.Muzzle;
            return ScGrenadeState.Finite(world);
        }
        if (!state.Valid || state.Weapon is null || state.Asset != gun || state.Released || Time.FrameIndex - state.Frame > 2) return false;
        world = Vector3.Transform(left ? state.Weapon.MuzzleLeft : state.Weapon.Muzzle, state.World);
        return ScGrenadeState.Finite(world);
    }
    /// <summary>OnModelDrawExtra: draw the baked weapon at the world matrix the animate step chose.</summary>
    public static bool Draw(ComponentHumanModel human, Camera camera) {
        if (human.Model?.HasSkin == true) { s_states.Remove(human); return false; }
        if(human.m_componentCreature?.ComponentHealth?.Health<=0){s_states.Remove(human);return false;}
        // Other mods can make the local body visible in FPP. Its extra hand item
        // must still be suppressed because our viewmodel already draws the weapon.
        int held = human.m_componentMiner is null ? 0 : PresentedValue(human, out _);
        if (camera.GameWidget.IsEntityFirstPersonTarget(human.Entity)
            && human.m_componentMiner is not null && AssetFor(held,out _) is not null) return true;
        if (!s_states.TryGetValue(human, out var state) || !state.Valid || state.Weapon is null) return false;
        if (human.m_componentMiner is null || AssetFor(held, out var heldStance) != state.Asset) { state.Valid = false; return false; }
        // Thrown or planted: the hand is empty until the action ends, whether or not the stack still holds another one.
        if (state.Released) return true;
        // The charge committed at the plant spot starts where this bomb was last drawn (ScC4Handoff).
        if (state.Asset == "c4" && PlantPhaseOf(human) is { Active: true, Placed: false } plant)
            ScC4Handoff.Holding(plant.Position, Vector3.Transform(state.Weapon.BodyCentre, state.World), human.Project.FindSubsystem<SubsystemTime>(true).GameTime);
        var terrain = human.m_subsystemTerrain;
        Vector3 at = state.World.Translation;
        int x = Terrain.ToCell(at.X), y = Terrain.ToCell(at.Y), z = Terrain.ToCell(at.Z);
        var env = new DrawBlockEnvironmentData {
            DrawBlockMode = DrawBlockMode.ThirdPerson, InWorldMatrix = state.World, SubsystemTerrain = terrain, Owner = human.Entity,
            Light = terrain.Terrain.GetCellLight(x, y, z), Humidity = terrain.Terrain.GetSeasonalHumidity(x, z),
            Temperature = terrain.Terrain.GetSeasonalTemperature(x, z) + SubsystemWeather.GetTemperatureAdjustmentAtHeight(y), BillboardDirection = -Vector3.UnitZ,
        };
        Matrix view = state.World * camera.ViewMatrix;
        int data = Terrain.ExtractData(held);
        bool silencerOff = ScGunBlock.SpecOf(held) is { HasSilencer: true } && GunSpec.GetSilencerOff(data);
        int skin = Terrain.ExtractContents(held) == BlocksManager.GetBlockIndex<ScGunBlock>(true) ? ScGunBlock.SkinOf(held) : Terrain.ExtractContents(held) == BlocksManager.GetBlockIndex<ScKnifeBlock>(true) ? ScKnifeBlock.SkinOf(held) : 0;
        if (skin != state.Skin) { state.Valid = false; return false; }
        var action=ScNetPresentation.ActionOf(human.Entity);
        // A throwable is carried by the hand; its viewmodel clip moves in the first-person camera's space and is not world data.
        var weaponPose=heldStance==ScThirdPersonStance.Grenade?null:state.Weapon.ActionPose(action);
        foreach (var group in state.Weapon.Groups) {
            if(!state.Weapon.ShowPart(group,weaponPose,action))continue;
            if (group.Silencer && silencerOff) continue; // the detached silencer is not on the gun in third person either
            // "<gun>_hd" is the factory set; a finish redirects it, and an unreadable finish falls back
            // to the factory texture so the gun is still drawn.
            Texture2D texture = group.Texture == state.Asset + "_hd"
                ? state.GunTexture : Load(group.Texture == state.Asset + "_cs2" && Terrain.ExtractContents(held) == BlocksManager.GetBlockIndex<ScKnifeBlock>(true)
                    ? ScKnifeSkinCatalog.Texture(state.Asset, skin, ScKnifeBlock.GetVariant(held)) : group.Texture);
            if (texture is null) continue;
            var partView=state.Weapon.PartTransform(group,weaponPose)*view;
            if (state.Legacy) ScGunNativeMesh.DrawWorld(human.m_subsystemModelsRenderer.PrimitivesRenderer, group.Mesh, texture, Color.White, 1f, ref partView, env);
            else BlocksManager.DrawMeshBlock(human.m_subsystemModelsRenderer.PrimitivesRenderer, group.Mesh, texture, Color.White, 1f, ref partView, env);
        }
        ScStatTrakRenderer.DrawThirdPerson(held, state.Asset, state.Legacy, view, human.m_subsystemModelsRenderer.PrimitivesRenderer,
            LightingManager.LightIntensityByLightValue[Math.Clamp(env.Light,0,15)]);
        return true;
    }

    /// <summary>Offline evidence for tools (video-feedback-20260929 R2): a rigid-arm human model holding a throwable at
    /// one moment of a throw, solved with the functions the game's Pose uses (FistLocal, ScThirdPersonMotion.Throw,
    /// HoldInHand, CarriedByHand) but without its frame-to-frame smoothing. The body stands at the origin facing -Z turned by yaw.</summary>
    public static (Matrix?[] Local, Matrix World, Vector3 Fist, Vector3 LeftFist, Vector2 Right, Vector2 Left, bool Shown, Matrix Hold) ThrowPreview(Model human, string asset, ScThrowPhase phase, float yaw) {
        var local = new Matrix?[human.Bones.Count];
        local[human.FindBone("Body", true).Index] = Matrix.CreateRotationY(yaw);
        return ActionPreview(human, "Hand2", "Hand1", asset, phase, default, local);
    }
    /// <summary>As ThrowPreview for any rigid-arm skeleton and posture (r2-c4-completion-20260929): <paramref name="posed"/>
    /// holds the body's animated local transforms (body, head, legs as the game's human controller left them for a
    /// crouch, a run or a jump); the arms named by <paramref name="right"/> / <paramref name="left"/> (Hand2/Hand1 on the
    /// vanilla human, Arm2/Arm1 on NMM's bone sets, as NMM assigns them) are solved for a throw or a C4 plant.</summary>
    public static (Matrix?[] Local, Matrix World, Vector3 Fist, Vector3 LeftFist, Vector2 Right, Vector2 Left, bool Shown, Matrix Hold) ActionPreview(Model human, string right, string left, string asset, ScThrowPhase phase, ScPlantPhase plant, Matrix?[] posed) {
        var weapon = ScThirdPersonWeapon.For(asset) ?? throw new InvalidOperationException("no third-person weapon for " + asset);
        var stance = asset == "c4" ? ScThirdPersonStance.Knife : ScThirdPersonStance.Grenade;
        ModelBone bodyBone = human.FindBone("Body", true), hand1 = human.FindBone(left, true), hand2 = human.FindBone(right, true);
        var local = (Matrix?[])posed.Clone();
        Matrix Absolute(ModelBone bone) {
            Matrix m = bone.Transform;
            if (local[bone.Index] is { } animation) { Vector3 t = m.Translation; m.Translation = Vector3.Zero; m *= animation; m.Translation += t; }
            return bone.ParentBone is null ? m : m * Absolute(bone.ParentBone);
        }
        Matrix body = Absolute(bodyBone);
        var thrown = plant.Active ? ScThirdPersonMotion.Plant(plant, stance) : ScThirdPersonMotion.Throw(phase, stance);
        Vector2 angles = thrown.Active ? thrown.Right : new Vector2(stance.RightRaise, stance.RightSwing), other = thrown.Active && thrown.LeftUsed ? thrown.Left : Vector2.Zero;
        Vector3 fistLocal = FistLocal(human, hand2, true), leftLocal = FistLocal(human, hand1, false);
        Matrix hold = HoldInHand(human, bodyBone, hand2, weapon, stance);
        // The arm bones hang from the body's parent chain (NMM: Body > Chest > Arm2), placed like the game's hook does.
        Matrix parent2 = hand2.ParentBone == bodyBone ? body : Absolute(hand2.ParentBone), parent1 = hand1.ParentBone == bodyBone ? body : Absolute(hand1.ParentBone);
        Matrix world = ScThirdPersonMath.CarriedByHand(hold, hand2.Transform.Translation, angles, parent2);
        if (!plant.Active && thrown.Active && phase.Stage == 0) other = ScThirdPersonMotion.PullLeft(phase, LeftReach(human, hand1, parent1, Vector3.Transform(weapon.BodyCentre, world)));
        local[hand2.Index] = ScThirdPersonMath.HandLocal(angles); local[hand1.Index] = ScThirdPersonMath.HandLocal(other);
        Matrix handWorld = ScThirdPersonMath.HandAbsolute(hand2.Transform.Translation, angles, parent2);
        Vector3 fist = Vector3.Transform(fistLocal, handWorld);
        Vector3 leftFist = Vector3.Transform(leftLocal, ScThirdPersonMath.HandAbsolute(hand1.Transform.Translation, other, parent1));
        bool shown = !(phase.Active && phase.Released) && !(plant.Active && plant.Placed);
        return (local, world, fist, leftFist, angles, other, shown, hold);
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
