using Engine;
using Engine.Graphics;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

/// <summary>The terrain as the arena checks read it.</summary>
public sealed class DmTerrainProbe(SubsystemTerrain terrain) : IDmWorldProbe {
    int Value(Point3 c) => terrain.Terrain.GetCellValue(c.X, c.Y, c.Z);
    static Block BlockOf(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)];
    public bool Loaded(Point3 c) => c.Y is >= 0 and < 256 && terrain.Terrain.GetChunkAtCell(c.X, c.Z) is { } chunk && chunk.State >= TerrainChunkState.InvalidLight;
    public bool Solid(Point3 c) { int value = Value(c); return Terrain.ExtractContents(value) != 0 && BlockOf(value) is { } block && block.IsCollidable_(value); }
    public bool Hazard(Point3 c) => BlockOf(Value(c)) is FireBlock or MagmaBlock or CactusBlock or SpikedPlankBlock;
    public bool Fluid(Point3 c) => BlockOf(Value(c)) is FluidBlock;
}

/// <summary>The deathmatch of one world (deathmatch-addon). The package adds this subsystem to every world it is loaded
/// with, and in every world but an arena world it does nothing: no rules, no HUD, no messages, nothing saved but its
/// "off" marker. The host turns a world into an arena world explicitly (DM-01); from then on the world carries the
/// mode's marker (ScWorldModes) and this group's data, which builds without the package keep as they are (design §14).
///
/// On the authority (a single-player game, or the host) it owns the match (DmMatch), the armoury (DmArmory) and every
/// change to a participant's position, inventory, health and armour. A remote client holds a DmView filled from the
/// server's messages and decides nothing.</summary>
public sealed class SubsystemScDeathmatch : Subsystem, IUpdateable, IDrawable {
    public UpdateOrder UpdateOrder => UpdateOrder.Default;
    public int[] DrawOrders => s_drawOrders;
    static readonly int[] s_drawOrders = [1100];
    readonly PrimitivesRenderer3D m_primitives = new();
    /// <summary>The map author's preview (design §4.1): while the host edits the map - and only then, only on the host's
    /// own screen - the arena's box, the preparation place and every respawn point with its facing are drawn as lines.
    /// Nothing is placed in the world and nothing is drawn once the lobby opens.</summary>
    public void Draw(Camera camera, int drawOrder) {
        if (!(Enabled || View.Enabled) || Frozen is not null) return;   // a client knows from the server's view (its world copy may predate the arena)
        if (View.Phase == DmPhase.Running) DrawProtection(camera);
        if (ShowsEditing) DrawEditing(camera);
        else DrawBounds(camera);
    }
    /// <summary>The arena's edge for everybody in the world (round 3, the user: "竞技区域要有一个高亮的提示（对于房间里的
    /// 所有人）", then "这个高亮显示太遮挡了，会盖住人物或枪械，最好就是玩家靠近边界再亮起提示"): only when this view's player
    /// comes within <see cref="EdgeNear"/> blocks of a wall of the region, a patch of that wall around the nearest point
    /// lights up as a grid, brighter the closer the player is (with the square of the nearness: a respawn point a block or two from a wall shows it
    /// only faintly) and fading towards its rim. Depth-tested: terrain, players
    /// and the held weapon in front of it hide it. From the region the server sent (a client's world copy may be older).
    /// The host's editing overlay draws its own box while it is shown.</summary>
    void DrawBounds(Camera camera) {
        var arena = Authority ? Arena : View.Arena;
        if (arena is not { HasRegion: true } || camera.GameWidget?.PlayerData?.ComponentPlayer?.ComponentBody is not { } body) return;
        Vector3 p = body.Position + new Vector3(0, .9f, 0), min = new(arena.MinX, arena.MinY, arena.MinZ), max = new(arena.MaxX + 1, arena.MaxY + 1, arena.MaxZ + 1);
        FlatBatch3D batch = null;
        // the four walls: the plane, whether it runs along X (a Z plane), its extent along the wall
        foreach (var (alongX, plane, lo, hi) in new[] { (false, min.X, min.Z, max.Z), (false, max.X, min.Z, max.Z), (true, min.Z, min.X, max.X), (true, max.Z, min.X, max.X) }) {
            float across = alongX ? p.Z : p.X, d = MathF.Abs(across - plane), u = alongX ? p.X : p.Z;
            if (d >= EdgeNear || u < lo - EdgeRadius || u > hi + EdgeRadius || p.Y < min.Y - EdgeRadius || p.Y > max.Y + EdgeRadius) continue;
            batch ??= m_primitives.FlatBatch(0, DepthStencilState.DepthRead, RasterizerState.CullNoneScissor, BlendState.AlphaBlend);
            float strength = (1 - d / EdgeNear) * (1 - d / EdgeNear), toward = across < plane ? -.03f : .03f;   // a hair towards the player: no fight with a block face on the plane
            Vector3 At(float uu, float yy) => alongX ? new Vector3(uu, yy, plane + toward) : new Vector3(plane + toward, yy, uu);
            float Falloff(float uu, float yy) => Math.Clamp(1 - MathF.Sqrt((uu - u) * (uu - u) + (yy - p.Y) * (yy - p.Y)) / EdgeRadius, 0, 1) * strength;
            Color Fill(float uu, float yy) => RegionColor * (.12f * Falloff(uu, yy));   // premultiplied (AlphaBlend)
            Color Line(float uu, float yy) => RegionColor * (.55f * Falloff(uu, yy));
            float u0 = Math.Max(lo, u - EdgeRadius), u1 = Math.Min(hi, u + EdgeRadius), y0 = Math.Max(min.Y, p.Y - EdgeRadius), y1 = Math.Min(max.Y, p.Y + EdgeRadius);
            for (float a0 = u0; a0 < u1; a0 += .5f) for (float b0 = y0; b0 < y1; b0 += .5f) {
                float a1 = Math.Min(a0 + .5f, u1), b1 = Math.Min(b0 + .5f, y1);
                batch.QueueQuad(At(a0, b0), At(a1, b0), At(a1, b1), At(a0, b1), Fill(a0, b0), Fill(a1, b0), Fill(a1, b1), Fill(a0, b1));
            }
            const float w = .014f;   // grid lines on whole blocks
            for (float g = MathF.Ceiling(u0); g <= u1; g++) for (float b0 = y0; b0 < y1; b0 += .5f) {
                float b1 = Math.Min(b0 + .5f, y1);
                batch.QueueQuad(At(g - w, b0), At(g + w, b0), At(g + w, b1), At(g - w, b1), Line(g, b0), Line(g, b0), Line(g, b1), Line(g, b1));
            }
            for (float g = MathF.Ceiling(y0); g <= y1; g++) for (float a0 = u0; a0 < u1; a0 += .5f) {
                float a1 = Math.Min(a0 + .5f, u1);
                batch.QueueQuad(At(a0, g - w), At(a1, g - w), At(a1, g + w), At(a0, g + w), Line(a0, g), Line(a1, g), Line(a1, g), Line(a0, g));
            }
        }
        if (batch is not null) m_primitives.Flush(camera.ViewProjectionMatrix);
    }
    /// <summary>How near a wall of the region the player must be for it to light up, and the radius of the lit patch.</summary>
    public const float EdgeNear = 3, EdgeRadius = 3;
    /// <summary>The host sees the arena's markers while the map is being built and while the lobby waits (round 2, R2-4:
    /// "重生点，圈定范围什么的也不明显"): drawn through terrain, as thick lines, tall beams and labels, never on another
    /// player's screen and never during a match.</summary>
    public bool ShowsEditing => Enabled && Frozen is null && Authority && Match is { Phase: DmPhase.Editing or DmPhase.Lobby };
    static readonly Color RegionColor = new(90, 214, 232), SpawnColor = new(110, 230, 110), BadColor = new(240, 72, 60), LobbyColor = new(104, 150, 236), CornerColor = new(240, 182, 52);
    /// <summary>The corner beams' height, metres (2026-10-06, the user: "角点的高度可以再高些"; the respawn and lobby beams stay 7).</summary>
    const float CornerBeamHeight = 18f;
    void DrawEditing(Camera camera) {
        Vector3 eye = camera.ViewPosition;
        var flat = m_primitives.FlatBatch(0, DepthStencilState.None, RasterizerState.CullNoneScissor, BlendState.AlphaBlend);
        var flatTop = m_primitives.FlatBatch(1, DepthStencilState.None, RasterizerState.CullNoneScissor, BlendState.AlphaBlend);
        var font = m_primitives.FontBatch(LabelWidget.BitmapFont, 2, DepthStencilState.None, RasterizerState.CullNoneScissor, BlendState.AlphaBlend, SamplerState.LinearClamp);
        void Thick(Vector3 a, Vector3 b, Color color, float width) {
            Vector3 mid = (a + b) * .5f, along = b - a, toEye = eye - mid;
            Vector3 side = Vector3.Cross(along, toEye); if (side.LengthSquared() < 1e-6f) return;
            side = Vector3.Normalize(side) * (width * Math.Max(1f, Vector3.Distance(eye, mid) / 14f));
            flat.QueueQuad(a - side, b - side, b + side, a + side, color);
        }
        void Box(Vector3 min, Vector3 max, Color color, float width) {
            Vector3[] c = [new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z), new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z)];
            for (int i = 0; i < 4; i++) { Thick(c[i], c[(i + 1) % 4], color, width); Thick(c[i + 4], c[(i + 1) % 4 + 4], color, width); Thick(c[i], c[i + 4], color, width); }
            // the floor of the box, faintly filled: from above the arena reads as an area, not just a frame
            Color floor = color * .16f; float y = min.Y + .03f;   // premultiplied: the batch blends with AlphaBlend
            flat.QueueQuad(new Vector3(min.X, y, min.Z), new Vector3(max.X, y, min.Z), new Vector3(max.X, y, max.Z), new Vector3(min.X, y, max.Z), floor);
        }
        // a label on a dark pixel plate, facing the camera and growing with distance, so it reads over the bright floor
        // fill and the sky alike
        void Label(string text, Vector3 at, Color color) {
            float d = Vector3.Distance(eye, at), scale = .014f * Math.Max(1f, d / 7f);
            Vector2 size = LabelWidget.BitmapFont.MeasureText(text, Vector2.One, Vector2.Zero);
            // the screen's own up, perpendicular to the view: camera.ViewUp is not (r2 frames: looking down at the arena, the
            // labels below the camera were drawn edge-on)
            Vector3 screenRight = Vector3.Normalize(camera.ViewRight), screenUp = Vector3.Normalize(Vector3.Cross(screenRight, camera.ViewDirection));
            Vector3 right = screenRight * scale, up = screenUp * scale;
            Vector3 left = at - right * (size.X / 2 + 6), rightEdge = at + right * (size.X / 2 + 6), top = up * (size.Y + 6), bottom = -up * 4;
            flatTop.QueueQuad(left + bottom, rightEdge + bottom, rightEdge + top, left + top, new Color(10, 12, 14) * .82f);
            flatTop.QueueQuad(left + bottom, rightEdge + bottom, rightEdge + bottom + up * 2, left + bottom + up * 2, color);
            font.QueueText(text, at + up * 2, right, -up, color, TextAnchor.HorizontalCenter | TextAnchor.Bottom);
        }
        void Beam(Vector3 feet, float yaw, Color color, string label, bool facing, float height = 7f) {
            // standing on the point itself: only its ring and label, not a beam and an arrow through the camera
            bool here = new Vector2(eye.X - feet.X, eye.Z - feet.Z).Length() < 1.2f && MathF.Abs(eye.Y - feet.Y - 1.6f) < 1.5f;
            if (!here) Thick(feet, feet + new Vector3(0, height, 0), color * .6f, .09f);
            const int Sides = 12;   // a ring on the ground where the body stands
            for (int i = 0; i < Sides; i++) {
                float a0 = MathF.PI * 2 * i / Sides, a1 = MathF.PI * 2 * (i + 1) / Sides;
                Thick(feet + new Vector3(MathF.Cos(a0) * .45f, .05f, MathF.Sin(a0) * .45f), feet + new Vector3(MathF.Cos(a1) * .45f, .05f, MathF.Sin(a1) * .45f), color, .035f);
            }
            if (facing && !here) {   // an arrow at chest height: where the player will look when the life begins
                Vector3 f = new(-MathF.Sin(yaw), 0, -MathF.Cos(yaw)), r = new(f.Z, 0, -f.X), chest = feet + new Vector3(0, 1.1f, 0);
                Thick(chest, chest + f * 1.2f, color, .05f); Thick(chest + f * 1.2f, chest + f * .85f + r * .3f, color, .05f); Thick(chest + f * 1.2f, chest + f * .85f - r * .3f, color, .05f);
            }
            Label(label, feet + new Vector3(0, height + .4f, 0), color);
        }
        if (Arena.HasRegion) {
            Box(new Vector3(Arena.MinX, Arena.MinY, Arena.MinZ), new Vector3(Arena.MaxX + 1, Arena.MaxY + 1, Arena.MaxZ + 1), RegionColor, .05f);
            Label($"竞技区域 {Arena.MaxX - Arena.MinX + 1}×{Arena.MaxZ - Arena.MinZ + 1}（高 {Arena.MaxY - Arena.MinY + 1}）", new Vector3((Arena.MinX + Arena.MaxX + 1) * .5f, Arena.MaxY + 1.6f, (Arena.MinZ + Arena.MaxZ + 1) * .5f), RegionColor);
        }
        // a corner that waits for its partner: the box it would make with the place the host stands now
        for (int i = 0; i < 2; i++) if (m_corners[i] is { } corner) {
            Vector3 c = new(corner.X + .5f, corner.Y, corner.Z + .5f);
            Beam(c, 0, CornerColor, $"角点 {i + 1}", false, CornerBeamHeight);   // taller than the points' beams: a corner is found from across the arena
            if (m_corners[1 - i] is null && m_players.PlayersData.Select(d => d.ComponentPlayer).FirstOrDefault(pl => pl is not null && ScNet.IsLocal(pl)) is { } host) {
                var here = Cell(host.ComponentBody.Position); var (lo, hi) = RegionOf(corner, here);
                Box(new Vector3(lo.X, lo.Y, lo.Z), new Vector3(hi.X + 1, hi.Y + 1, hi.Z + 1), CornerColor * .6f, .03f);
                Label($"走到对角，按“角点 {2 - i}”确定", new Vector3(here.X + .5f, here.Y + 2.4f, here.Z + .5f), CornerColor);
            }
        }
        var bad = ArenaIssuesCached().Where(i => i.SpawnId > 0).Select(i => i.SpawnId).ToHashSet();
        foreach (var spawn in Arena.Spawns) Beam(spawn.Position, spawn.Yaw, bad.Contains(spawn.Id) ? BadColor : SpawnColor, bad.Contains(spawn.Id) ? $"复活点 #{spawn.Id}（不可用）" : $"复活点 #{spawn.Id}", true);
        if (Arena.HasLobby) Beam(Arena.Lobby, Arena.LobbyYaw, LobbyColor, "准备/观战点", true);
        m_primitives.Flush(camera.ViewProjectionMatrix);
    }
    /// <summary>The protection as others see it (DM-05, design §10): a pale, slowly pulsing shell around a protected life,
    /// drawn the same way for every character model and on every platform (plain coloured faces, no shader of its own).
    /// It is this package's own marker - CS2's white look is rendered by CS2's code and was not found as a resource to
    /// port - and it is gone the moment the server says the protection is over. The protected player's own first-person
    /// view is left clear: the HUD tells that player.</summary>
    void DrawProtection(Camera camera) {
        bool any = false; FlatBatch3D batch = null;
        foreach (var row in View.Rows) {
            if (row.Phase != DmPlayerPhase.SpawnProtected || row.PlayerIndex < 0) continue;
            var player = m_players.PlayersData.FirstOrDefault(d => d.PlayerIndex == row.PlayerIndex)?.ComponentPlayer;
            if (player?.ComponentBody is not { } body) continue;
            if (camera is FppCamera && ReferenceEquals(camera.GameWidget?.PlayerData?.ComponentPlayer, player)) continue;
            batch ??= m_primitives.FlatBatch(1, DepthStencilState.DepthRead, RasterizerState.CullNoneScissor, BlendState.AlphaBlend);
            float pulse = .5f + .5f * MathF.Sin((float)(Now * 5)); Color color = Color.White * (.16f + .12f * pulse);
            Vector3 feet = body.Position; const int Sides = 10; const float Radius = .58f, Height = 1.9f;
            for (int i = 0; i < Sides; i++) {
                float a0 = MathF.PI * 2 * i / Sides, a1 = MathF.PI * 2 * (i + 1) / Sides;
                Vector3 p0 = feet + new Vector3(MathF.Cos(a0) * Radius, 0, MathF.Sin(a0) * Radius), p1 = feet + new Vector3(MathF.Cos(a1) * Radius, 0, MathF.Sin(a1) * Radius);
                batch.QueueQuad(p0, p1, p1 + new Vector3(0, Height, 0), p0 + new Vector3(0, Height, 0), color);
            }
            any = true;
        }
        if (any) m_primitives.Flush(camera.ViewProjectionMatrix);
    }
    readonly Dictionary<int, DmPlayerPhase> m_shownPhase = []; DmPhase m_shownMatch = DmPhase.Editing;
    /// <summary>Sounds of what the server said, once each, on every process: a life that begins is heard where it stands, the
    /// end of a match by everybody.</summary>
    void Present() {
        foreach (var row in View.Rows) {
            if (row.PlayerIndex < 0) continue;
            bool was = m_shownPhase.TryGetValue(row.PlayerIndex, out var former) && former == DmPlayerPhase.SpawnProtected;
            if (row.Phase == DmPlayerPhase.SpawnProtected && !was && m_players.PlayersData.FirstOrDefault(d => d.PlayerIndex == row.PlayerIndex)?.ComponentPlayer?.ComponentBody is { } body) DmArt.Play(Project, "respawn", body.Position);
            m_shownPhase[row.PlayerIndex] = row.Phase;
        }
        if (View.Phase == DmPhase.Results && m_shownMatch == DmPhase.Running) DmArt.Play(Project, "match_end", null);
        m_shownMatch = View.Phase;
    }
    List<DmArenaIssue> m_issues = []; double m_issuesAt = double.NegativeInfinity; int m_issuesRevision = -1;
    List<DmArenaIssue> ArenaIssuesCached() {
        if (Now - m_issuesAt > 1 || m_issuesRevision != Arena.Revision) { m_issues = ArenaIssues(); m_issuesAt = Now; m_issuesRevision = Arena.Revision; }
        return m_issues;
    }
    SubsystemPlayers m_players; SubsystemTime m_time; SubsystemTerrain m_terrain; SubsystemBodies m_bodies; SubsystemGameInfo m_info;
    DmTerrainProbe m_probe;
    readonly Random m_random = new();
    ValuesDictionary m_raw;

    /// <summary>The host made this world an arena world.</summary>
    public bool Enabled { get; private set; }
    /// <summary>The saved data is of a layout this build does not know, or could not be read: kept exactly as loaded,
    /// written back unchanged, and the mode is not run in this session.</summary>
    public string Frozen { get; private set; }
    public DmArenaDefinition Arena { get; private set; } = new();
    public DmArmory Armory { get; private set; } = new();
    /// <summary>The match (authority only; null on a remote client and in a world that is not an arena world).</summary>
    public DmMatch Match { get; private set; }
    public DmMode Mode { get; private set; }
    public readonly DmView View = new();
    /// <summary>CS2's spray patterns of this world's players (round 5; the core asks through DmMode.Recoil).</summary>
    public readonly DmRecoil Recoil = new();
    /// <summary>CS2's bullet penetration of this world's players (round 6; the core asks through DmMode.Penetration).</summary>
    public readonly DmPenetration Penetration = new();
    public double Now => m_time?.GameTime ?? 0;
    public bool Authority => ScNet.IsAuthority;
    public IDmWorldProbe Probe => m_probe;
    /// <summary>The preparation place's leash: a player without a life stays within this of it while the lobby is open.</summary>
    public const float LobbyRadius = 8;

    // ---------------------------------------------------------------- load / save
    public override void Load(ValuesDictionary values) {
        m_players = Project.FindSubsystem<SubsystemPlayers>(true); m_time = Project.FindSubsystem<SubsystemTime>(true);
        m_terrain = Project.FindSubsystem<SubsystemTerrain>(true); m_bodies = Project.FindSubsystem<SubsystemBodies>(true); m_info = Project.FindSubsystem<SubsystemGameInfo>(true);
        m_probe = new DmTerrainProbe(m_terrain);
        m_raw = new ValuesDictionary();
        foreach (var pair in values) if (pair.Key != "Class") m_raw.SetValue(pair.Key, pair.Value);
        Mode = new DmMode(this);
        int schema = values.GetValue("Schema", 0);
        if (schema == 0) return;                                        // a world this package never wrote to
        if (schema > DmIds.Schema) { Frozen = $"本世界的死亡竞赛数据来自更新的版本（{schema}），已原样保留；请更新死亡竞赛拓展后再使用"; return; }
        Enabled = values.GetValue("Enabled", false);
        bool arena = DmArenaDefinition.TryDecode(values.GetValue("Arena", ""), out var definition);
        var armory = DmArmory.Decode(values.GetValue("Armory", ""));
        bool match = DmMatch.TryDecode(values.GetValue("Match", ""), out var loaded);
        if (!arena || !match || armory.Unreadable is not null) { Frozen = "本世界的死亡竞赛数据无法读取，已原样保留、未作任何修改；竞技功能本次不启用"; Enabled = false; return; }
        Arena = definition; Armory = armory;
        if (Enabled) Activate(loaded);
    }
    public override void Save(ValuesDictionary values) {
        if (Frozen is not null || !Enabled && m_raw.GetValue("Schema", 0) == 0) {
            foreach (var pair in m_raw) values.SetValue(pair.Key, pair.Value);   // untouched: nothing of ours, or data we must not rewrite
            return;
        }
        values.SetValue("Schema", DmIds.Schema); values.SetValue("Enabled", Enabled);
        values.SetValue("Arena", Arena.Encode()); values.SetValue("Armory", Armory.Encode());
        // a remote client's copy of a world is the server's to save; its match text is what it was sent with
        values.SetValue("Match", Match is not null ? Match.Encode(Now) : m_raw.GetValue("Match", ""));
    }
    public override void Dispose() { if (Mode is not null) ScModes.Unregister(Project, Mode); }
    /// <summary>The key of the armoury's parked records among the gun holders (registered once, by the mod loader: it asks
    /// whichever world is being scanned for its own deathmatch subsystem).</summary>
    public const string HolderSource = "zh667.ScCsgoDeathmatch/armoury";

    void Activate(DmMatch match) {
        Enabled = true; View.Enabled = true;
        if (Arena.HasRegion) { m_corners[0] = new Point3(Arena.MinX, Arena.MinY, Arena.MinZ); m_corners[1] = new Point3(Arena.MaxX, Arena.MinY, Arena.MaxZ); }
        if (Authority) {
            Match = match ?? new DmMatch();
            // What a closed session left in players' hands is not a lease any more: every record is parked again and the
            // inventories are cleared as the players arrive (design §14: next time the lobby, no life is replayed).
            Armory.ReturnEverything();
        }
        ScModes.Register(Project, Mode);
    }
    /// <summary>The armoury's records that nobody has out: held by the armoury (design §6), so the gun scans see them as
    /// owned items of this world and not as records that lost their gun.</summary>
    public IEnumerable<(string Key, int Value, int Count)> ParkedItems() {
        if (!Enabled || Frozen is not null || !Authority || !BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunBlock), out int gun)) yield break;
        foreach (int id in Armory.Parked) yield return ($"dm-armoury:{id}", Terrain.MakeBlockValue(gun, 0, GunSpec.WithId(Armory.VariantOf(id), id)), 1);
    }

    // ---------------------------------------------------------------- the host
    /// <summary>Why the host cannot do something now, or null.</summary>
    public string HostBlocked() => !Authority ? "只有房主可以设置竞技地图" : Frozen;
    /// <summary>The host makes this world an arena world (DM-01). Never automatic, never on a world's first load.</summary>
    public string EnableArena() {
        if (HostBlocked() is { } blocked) return blocked;
        if (Enabled) return null;
        if (!DmWeapons.Ready) return "竞技武器数据未能加载：" + DmWeapons.LoadError;
        if (!DmHitboxes.Ready) return "CS2 命中盒数据未能加载：" + DmHitboxes.LoadError;
        if (!DmPenetrationRules.Ready) return "CS2 穿透数据未能加载：" + DmPenetrationRules.LoadError;
        if (ScNet.Peers.Count > 0) return "启用竞技世界时不能有其他玩家在线：请让他们先退出，启用后再加入";
        if (ScGunRegistry.Current is null || ScGunRegistry.Current.Disabled) return "本世界的枪械数据是旧格式，不能作为竞技世界";
        if (!ScWorldModes.Mark(Project, new ScWorldMode(DmIds.Mode, DmIds.ModeName, true))) return "无法写入世界的模式标记（本世界的兼容数据不可用）";
        Activate(null); Log("arena enabled");
        return null;
    }
    public string SetRegion(Point3 a, Point3 b) { var (lo, hi) = RegionOf(a, b); return EditArena(arena => arena.WithRegion(lo, hi)); }
    // ---- the two corners (round 2: the first corner used to live in the menu and was lost when the menu closed, so a host
    // walking to the second corner never got a region and "开始比赛" was refused with a message nobody saw)
    readonly Point3?[] m_corners = new Point3?[2];
    public static Point3 Cell(Vector3 p) => new(Terrain.ToCell(p.X), Terrain.ToCell(p.Y), Terrain.ToCell(p.Z));
    /// <summary>The cells a region spans for two corners where the host stood: everything between them, and at least four
    /// cells above the higher one (a player standing on the floor - feet, head, a jump - is inside).</summary>
    public static (Point3 Min, Point3 Max) RegionOf(Point3 a, Point3 b) {
        int top = Math.Max(Math.Max(a.Y, b.Y), Math.Min(a.Y, b.Y) + 3) + 1;
        return (new Point3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)), new Point3(Math.Max(a.X, b.X), top, Math.Max(a.Z, b.Z)));
    }
    public Point3? Corner(int i) => m_corners[i];
    /// <summary>Corner 1 or 2 at this cell. The region is made as soon as the other corner is set too. When both corners
    /// are already known (an arena that has a region), a corner starts a new region: the other corner is forgotten until it
    /// is set again, and the region stays as it was until then (round 3, the user's phone: re-setting corner 1 of an
    /// existing arena made a region with the old corner 2 at once - a one-block strip - and the menu showed both corners as
    /// set, which read as "set corner 1 again").</summary>
    public string SetCorner(int i, Point3 cell) {
        if (HostBlocked() is { } blocked) return blocked;
        if (!Enabled) return "先把本世界启用为竞技世界";
        if (Match.Phase is not (DmPhase.Editing or DmPhase.Lobby)) return "比赛进行中不能修改竞技区域";
        bool restart = m_corners[0] is not null && m_corners[1] is not null;
        m_corners[i] = cell;
        if (restart) m_corners[1 - i] = null;
        Log($"corner {i + 1} at {cell.X},{cell.Y},{cell.Z}{(restart ? " (a new region: the other corner is set again)" : "")}");
        if (m_corners[1 - i] is { } other) return SetRegion(cell, other);
        return null;
    }
    static void Log(string text) => KnifeLog.Information("[CS_DM] " + text);
    public string SetLobby(Vector3 position, float yaw) => EditArena(arena => arena.WithLobby(position, yaw));
    public string AddSpawn(Vector3 position, float yaw, string label = "") => EditArena(arena => arena.Spawns.Count >= DmArenaRules.MaxSpawns ? null : arena.AddSpawn(position, yaw, label));
    public string RemoveSpawn(int id) => EditArena(arena => arena.RemoveSpawn(id));
    /// <summary>The arena changes only while the host edits the map or the lobby waits: never under a running match.</summary>
    string EditArena(Func<DmArenaDefinition, DmArenaDefinition> edit) {
        if (HostBlocked() is { } blocked) return blocked;
        if (!Enabled) return "先把本世界启用为竞技世界";
        if (Match.Phase is not (DmPhase.Editing or DmPhase.Lobby)) return "比赛进行中不能修改竞技区域和复活点";
        if (edit(Arena) is not { } changed) return "已达到复活点数量上限";
        Arena = changed; m_stateDirty = true;
        Log($"arena revision {Arena.Revision}: region {(Arena.HasRegion ? $"{Arena.MinX},{Arena.MinY},{Arena.MinZ}..{Arena.MaxX},{Arena.MaxY},{Arena.MaxZ}" : "none")}, lobby {Arena.HasLobby}, spawns {Arena.Spawns.Count}");
        return null;
    }
    public List<DmArenaIssue> ArenaIssues() => DmArenaRules.Validate(Arena, m_probe);
    public string SetRules(DmRules rules) {
        if (HostBlocked() is { } blocked) return blocked;
        if (!Enabled) return "先把本世界启用为竞技世界";
        if (!Match.SetRules(rules)) return "比赛进行中不能修改规则";
        m_stateDirty = true; return null;
    }
    public string OpenLobby() => HostCommand(() => Match.OpenLobby(Now) ? null : "大厅已经开放");
    public string EditMap() => HostCommand(() => Match.Edit(Now) ? null : "先结束比赛再编辑地图");
    /// <summary>Starts a match from map editing or the lobby in one step. Every refusal says what to do.</summary>
    public string StartMatch() => HostCommand(() => {
        if (!DmWeapons.Ready) return "竞技武器数据未能加载：" + DmWeapons.LoadError;
        if (!DmHitboxes.Ready) return "CS2 命中盒数据未能加载：" + DmHitboxes.LoadError;
        if (!DmPenetrationRules.Ready) return "CS2 穿透数据未能加载：" + DmPenetrationRules.LoadError;
        if (Match.Phase is DmPhase.Countdown or DmPhase.Running) return "比赛已经在进行";
        if (Match.Phase == DmPhase.Results) return "结算结束后回到大厅才能开始下一局";
        var issues = ArenaIssues();
        if (issues.FirstOrDefault(i => i.Blocks) is { } blocking) { Log("start refused: " + blocking.Code); return "不能开始：" + blocking.Message; }
        if (!Match.Players.Any(p => p.Connected && p.Entered)) {
            Log("start refused: nobody entered");
            return Match.Phase == DmPhase.Editing ? "不能开始：先点“开放大厅”，大家配装（B）并点“准备”后再开始" : "不能开始：还没有玩家准备（配装后点“准备”）";
        }
        if (Match.Phase == DmPhase.Editing) Match.OpenLobby(Now);
        string refused = Match.Start(issues, Now);
        Log(refused is null ? $"match {Match.MatchId + 1} countdown, {Match.Players.Count(p => p.Connected && p.Entered)} entered" : "start refused: " + refused);
        return refused;
    });
    public int EnteredCount => Match?.Players.Count(p => p.Connected && p.Entered) ?? View.Rows.Count(r => r.Playing && r.Connected);
    public string StopMatch() => HostCommand(() => Match.Stop(Now) ? null : "现在没有比赛");
    string HostCommand(Func<string> command) {
        if (HostBlocked() is { } blocked) return blocked;
        if (!Enabled) return "先把本世界启用为竞技世界";
        return command();
    }

    /// <summary>Whether a change of this cell is refused now: from the countdown to the results nothing in the arena (and
    /// two cells around it) or around the preparation place changes - on the authority and, with the same answer, on a
    /// client that predicts its own digging.</summary>
    public bool ProtectsCell(int x, int y, int z) {
        if (!Enabled || Frozen is not null) return false;
        var phase = Authority ? Match?.Phase ?? DmPhase.Editing : View.Phase;
        if (phase is DmPhase.Editing or DmPhase.Lobby) return false;
        if (Arena.HasRegion && x >= Arena.MinX - 2 && x <= Arena.MaxX + 2 && y >= Arena.MinY - 2 && y <= Arena.MaxY + 2 && z >= Arena.MinZ - 2 && z <= Arena.MaxZ + 2) return true;
        return Arena.HasLobby && Math.Abs(x - Arena.LobbyX) <= LobbyRadius + 2 && Math.Abs(y - Arena.LobbyY) <= LobbyRadius + 2 && Math.Abs(z - Arena.LobbyZ) <= LobbyRadius + 2;
    }

    // ---------------------------------------------------------------- players
    readonly Dictionary<ComponentPlayer, string> m_keys = [];
    readonly Dictionary<int, string> m_keyOfIndex = [];
    /// <summary>The stable identity of a player: the platform's account id for a remote client, the local seat for the
    /// host's own players. Never the connection slot (a reused slot is another player).</summary>
    public string KeyOf(ComponentPlayer player) {
        if (player?.PlayerData is null) return null;
        if (m_keys.TryGetValue(player, out string key)) return key;
        // (a remote player whose handshake is not accepted yet has no key: it is not in the match until it is)
        key = ScNet.PeerOf(player) is { } peer ? peer.Guid.ToString("N") : ScNet.IsLocal(player) || !ScNet.IsHost ? "local:" + player.PlayerData.PlayerIndex : null;
        if (key is not null) m_keys[player] = key;
        return key;
    }
    public ComponentPlayer PlayerOf(string key) {
        foreach (var data in m_players.PlayersData) if (data.ComponentPlayer is { } player && KeyOf(player) == key) return player;
        return null;
    }
    public string KeyOfIndex(int playerIndex) => m_keyOfIndex.GetValueOrDefault(playerIndex);
    public DmPlayer StateOf(ComponentPlayer player) => Match?.Find(KeyOf(player));
    /// <summary>The mode's rules apply to this player now.</summary>
    public bool Governs(ComponentPlayer player) => Enabled && Frozen is null && (Authority ? Match is { Governing: true } : View.Governing) && player is not null;
    public bool IsParticipantEntity(Entity entity) => entity?.FindComponent<ComponentPlayer>() is { } player && Governs(player);

    void SyncPlayers(double now) {
        var present = new HashSet<string>();
        foreach (var data in m_players.PlayersData) {
            if (data.ComponentPlayer is not { } player || player.ComponentHealth is null) continue;
            if (KeyOf(player) is not { } key) continue;
            present.Add(key); m_keyOfIndex[data.PlayerIndex] = key;
            if (Match.Find(key) is not { Connected: true }) {
                Match.Join(key, data.Name, now);
                if (Match.Governing) { Strip(player, key); ToLobby(player, key); }
                m_selfDirty.Add(key); m_stateFor.Add(key); m_playersDirty = true;
            }
        }
        foreach (var gone in Match.Players.Where(p => p.Connected && !present.Contains(p.Key)).Select(p => p.Key).ToList()) {
            Match.Leave(gone, now); ReturnLeases(gone); m_playersDirty = true;
        }
        foreach (var stale in m_keys.Keys.Where(p => p.PlayerData is null || !m_players.PlayersData.Contains(p.PlayerData) || !ReferenceEquals(p.PlayerData.ComponentPlayer, p)).ToList()) m_keys.Remove(stale);
    }
    internal void PeerAccepted(ScNetPeer peer) {
        if (!Enabled || !Authority || peer.Player(Project) is not { } player) return;
        m_keys.Remove(player);                                          // the key of a remote player is its account id from now on
        if (KeyOf(player) is not { } key) return;
        m_stateFor.Add(key); m_selfDirty.Add(key); m_playersDirty = true;
    }
    internal void PeerLeft(ScNetPeer peer) { if (peer.Player(Project) is { } player) m_keys.Remove(player); }

    // ---------------------------------------------------------------- requests (the authority's entry points)
    public void RequestLoadout(ComponentPlayer player, DmLoadout loadout) {
        if (!Authority) { DmNet.SendLoadout(loadout); return; }
        if (Match is null || KeyOf(player) is not { } key) return;
        var (outcome, error) = Match.RequestLoadout(key, loadout, Now);
        if (ScNet.PeerOf(player) is { } peer) ScNet.SendTo(peer, DmNet.OpAnswer, w => w.Byte((byte)outcome).Byte((byte)error));
        else View.Of(player).Answer = (outcome, error, Now);
        m_selfDirty.Add(key);
    }
    public void RequestEnter(ComponentPlayer player, bool confirmEmpty) {
        if (!Authority) { DmNet.SendEnter(confirmEmpty); return; }
        if (Match is null || KeyOf(player) is not { } key) return;
        if (Foreign(player)) { Tell(key, ForeignText); return; }
        Match.Enter(key, confirmEmpty, Now); m_selfDirty.Add(key);
    }
    public void RequestSpectate(ComponentPlayer player) {
        if (!Authority) { DmNet.SendSpectate(); return; }
        if (Match is null || KeyOf(player) is not { } key) return;
        Match.Spectate(key, Now); m_selfDirty.Add(key);
    }
    internal void RemoteReady(ComponentPlayer player, int lifeId) { if (Authority && Match is not null && KeyOf(player) is { } key) Ready(key, lifeId); }
    void Ready(string key, int lifeId) => Match.Ready(key, lifeId, Now, point => DmSpawnSelector.Acceptable(point, SpawnContext(key, false)));

    // ---------------------------------------------------------------- frame
    readonly HashSet<string> m_selfDirty = [], m_stateFor = [];
    bool m_playersDirty, m_stateDirty; double m_stateSentAt = double.NegativeInfinity, m_creatureSweepAt;
    readonly Dictionary<string, (Vector3 Target, float Yaw, double Until, double SentAt)> m_placing = [];
    readonly Dictionary<string, int> m_localPending = [];
    readonly Dictionary<string, double> m_outsideTold = [];

    public void Update(float dt) {
        UpdateMovement();
        if (!Enabled || Frozen is not null) return;
        if (!Authority) { ClientUpdate(); return; }
        double now = Now;
        SyncPlayers(now);
        if (Match.Governing) {
            foreach (var data in m_players.PlayersData) {
                if (data.ComponentPlayer is not { } player || Match.Find(KeyOf(player)) is not { } p) continue;
                Vector3 feet = player.ComponentBody.Position;
                if (p.CanFight) {
                    bool inside = Arena.Contains(feet);
                    Match.SetInside(p.Key, inside || m_placing.ContainsKey(p.Key), now);
                    if (!inside && !m_placing.ContainsKey(p.Key) && now - m_outsideTold.GetValueOrDefault(p.Key, double.NegativeInfinity) > 2) { m_outsideTold[p.Key] = now; Tell(p.Key, $"已离开竞技区域，{DmFixed.OutOfBoundsSeconds:0} 秒内返回"); }
                    EnforceKit(player, p);
                    if (player.ComponentLocomotion is { IsCreativeFlyEnabled: true } locomotion) locomotion.IsCreativeFlyEnabled = false;
                }
                else if (p.Phase != DmPlayerPhase.SpawnPending || p.Pending is null) {
                    // without a life: at the preparation place, carrying nothing of the mode's
                    if (Arena.HasLobby && !m_placing.ContainsKey(p.Key) && Vector3.Distance(feet, Arena.Lobby) > LobbyRadius) ToLobby(player, p.Key);
                    if (Carried(player).Any(v => IsModeItem(v))) Strip(player, p.Key);
                }
                MirrorHealth(player, p);
            }
            KeepPlaced(now);
            if (Match.Running && now - m_creatureSweepAt >= 1) { m_creatureSweepAt = now; SweepCreatures(); }
        }
        LocalReady();
        Match.Tick(now, p => DmSpawnSelector.Choose(SpawnContext(p.Key, true)));
        Drain(now);
        Publish(now);
        Present();
    }

    /// <summary>The running speed of the weapon in hand (round 5, DmMovement) on every player this process moves, while the
    /// player fights in the match; the player's own speed otherwise - also when the mode stops in this world.</summary>
    void UpdateMovement() {
        var guns = Project.FindSubsystem<SubsystemScGunBlockBehavior>(false);
        foreach (var player in LocalPlayers) {
            bool fighting = Governs(player) && View.Of(player).Fighting && DmWeapons.Ready;
            DmMovement.Apply(player, fighting ? DmMovement.Ratio(player.ComponentMiner.ActiveBlockValue, guns?.IsScoped(player) == true) : null);
        }
    }

    void LocalReady() {
        foreach (var pending in m_localPending.ToList()) {
            if (Match.Find(pending.Key) is not { Phase: DmPlayerPhase.SpawnPending, Pending: { } point } p || p.LifeId != pending.Value) { m_localPending.Remove(pending.Key); continue; }
            if (!m_probe.Loaded(point.Cell)) continue;
            m_localPending.Remove(pending.Key); Ready(pending.Key, pending.Value);
        }
    }
    void MirrorHealth(ComponentPlayer player, DmPlayer p) {
        // The engine's health is a picture of the mode's: full for a player without a life, hp/100 for a life. It is
        // never the source of anything here and never reaches zero through this package.
        float wanted = p.CanFight ? Math.Max(.01f, p.Health / (float)DmFixed.Health) : 1f;
        if (player.ComponentHealth is { } health && MathF.Abs(health.Health - wanted) > .0005f) health.Health = wanted;
    }
    void SweepCreatures() {
        foreach (var body in m_bodies.Bodies.ToArray()) {
            if (!Arena.Contains(body.Position) || body.Entity.FindComponent<ComponentPlayer>() is not null) continue;
            if (body.Entity.FindComponent<ComponentCreature>() is null) continue;
            body.Entity.FindComponent<ComponentSpawn>()?.Despawn();
        }
    }

    DmSpawnSelector.Context SpawnContext(string forKey, bool withRandom) {
        var enemies = new List<DmEnemy>(); var occupied = new List<Vector3>();
        foreach (var data in m_players.PlayersData) {
            if (data.ComponentPlayer is not { } other || Match.Find(KeyOf(other)) is not { } state || state.Key == forKey) continue;
            if (state.CanFight || state.Phase == DmPlayerPhase.SpawnPending && state.Pending is not null) occupied.Add(other.ComponentBody.Position);
            if (state.CanFight) enemies.Add(new(other.ComponentBody.Position, other.ComponentBody.Position + new Vector3(0, DmSpawnSelector.EyeHeight, 0)));
        }
        var fires = Project.FindSubsystem<SubsystemScGrenades>(false);
        return new DmSpawnSelector.Context { Arena = Arena, World = m_probe, Enemies = enemies, Occupied = occupied, Now = Now, LastUsed = Match.SpawnUsed,
            Reserved = Match.Players.Where(p => p.Pending is not null && p.Key != forKey).Select(p => p.Pending.Id).ToHashSet(),
            Sees = (from, to) => m_terrain.Raycast(from, to, false, true, (value, _) => Terrain.ExtractContents(value) != 0 && BlocksManager.Blocks[Terrain.ExtractContents(value)].IsCollidable_(value)) is null,
            Danger = position => fires?.FireAreas().Any(f => Vector3.Distance(f.Position, position) < f.Radius + 1) == true,
            Random = withRandom ? () => m_random.Float(0, .9999f) : () => 0 };
    }

    // ---------------------------------------------------------------- events
    void Drain(double now) {
        while (Match.TryDequeue(out var e)) {
            switch (e) {
                case DmPhaseEvent phase:
                    m_stateDirty = true; m_playersDirty = true; Log($"phase {phase.Phase}{(phase.Reason.Length > 0 ? " (" + phase.Reason + ")" : "")}, match {Match.MatchId}");
                    if (phase.Phase == DmPhase.Lobby) foreach (var data in m_players.PlayersData) if (data.ComponentPlayer is { } player && KeyOf(player) is { } key) { Strip(player, key); ToLobby(player, key); }
                    break;
                case DmPlayerEvent changed: m_selfDirty.Add(changed.Key); m_playersDirty = true; break;
                case DmPrepareEvent prepare when PlayerOf(prepare.Key) is { } player:
                    Place(player, prepare.Key, prepare.Spawn.Position, prepare.Spawn.Yaw, false);
                    if (ScNet.PeerOf(player) is { } peer) ScNet.SendTo(peer, DmNet.OpPrepare, w => w.Int(prepare.LifeId).Vector3(prepare.Spawn.Position).Float(prepare.Spawn.Yaw));
                    else m_localPending[prepare.Key] = prepare.LifeId;
                    break;
                case DmCommitEvent commit when PlayerOf(commit.Key) is { } player:
                    Log($"life {commit.LifeId} of {commit.Key} at spawn #{commit.Spawn.Id}: {commit.Loadout.Encode()}");
                    if (Issue(player, Match.Find(commit.Key), DmLoadout.Empty, commit.Loadout) is { } failed) {
                        Strip(player, commit.Key); Tell(commit.Key, failed + "；已转为观战"); Match.Spectate(commit.Key, now); break;
                    }
                    if (ScNet.PeerOf(player) is { } owner) ScNet.SendTo(owner, DmNet.OpCommit, w => w.Int(commit.LifeId).Vector3(commit.Spawn.Position).Float(commit.Spawn.Yaw));
                    else { DmDeathCamera.End(player); SelectFirst(player); }
                    m_selfDirty.Add(commit.Key);
                    break;
                case DmReissueEvent reissue when PlayerOf(reissue.Key) is { } player:
                    if (Issue(player, Match.Find(reissue.Key), reissue.Former, reissue.Loadout) is { } refused) Tell(reissue.Key, refused + "；保留原装备");
                    m_selfDirty.Add(reissue.Key);
                    break;
                case DmDeathEvent death: Log($"kill #{death.Kill.Sequence}: {death.Kill.KillerKey ?? "-"} -> {death.Kill.VictimKey} ({death.Kill.Cause}, {death.Kill.Weapon})"); Died(death.Kill); break;
                case DmStripEvent strip when PlayerOf(strip.Key) is { } player: Strip(player, strip.Key); ToLobby(player, strip.Key); m_selfDirty.Add(strip.Key); break;
                case DmStripEvent strip: ReturnLeases(strip.Key); break;
                case DmProtectionEvent protection: m_selfDirty.Add(protection.Key); m_playersDirty = true; break;
                case DmNoticeEvent notice: Tell(notice.Key, notice.Text); break;
                case DmResultEvent result:
                    ScNet.Broadcast(DmNet.OpResult, w => w.String(DmNet.Encode(result.Result)));
                    ShowResult(result.Result);
                    break;
            }
        }
    }
    void Died(DmKill kill) {
        var victim = PlayerOf(kill.VictimKey); var killer = kill.KillerKey is null ? null : PlayerOf(kill.KillerKey);
        Vector3 eye = victim is null ? Vector3.Zero : victim.ComponentBody.Position + new Vector3(0, DmSpawnSelector.EyeHeight, 0);
        Vector3? killerEye = killer is null ? null : killer.ComponentBody.Position + new Vector3(0, DmSpawnSelector.EyeHeight, 0);
        int index = victim?.PlayerData.PlayerIndex ?? -1;
        ScNet.Broadcast(DmNet.OpDeath, w => DmNet.WriteDeath(w, kill, index, eye, killerEye));
        ShowDeath(kill, index, eye, killerEye);
        if (victim is not null) {
            victim.Entity.FindComponent<ComponentOnFire>()?.Extinguish();
            Strip(victim, kill.VictimKey); ToLobby(victim, kill.VictimKey);
        }
        else ReturnLeases(kill.VictimKey);
        m_selfDirty.Add(kill.VictimKey); if (kill.KillerKey is not null) m_selfDirty.Add(kill.KillerKey);
        m_playersDirty = true;
    }

    // ---------------------------------------------------------------- positions
    static float YawOf(ComponentBody body) { Vector3 f = body.Rotation.GetForwardVector(); return MathF.Atan2(-f.X, -f.Z); }
    public static (Vector3 Position, float Yaw) Stand(ComponentPlayer player) => (player.ComponentBody.Position, YawOf(player.ComponentBody));
    static void Put(ComponentPlayer player, Vector3 position, float yaw) {
        var body = player.ComponentBody;
        body.Position = position; body.Velocity = Vector3.Zero; body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        if (player.ComponentLocomotion is { } locomotion) locomotion.LookAngles = Vector2.Zero;
    }
    /// <summary>Moves a player. The host's own player is moved at once; a remote player moves itself when told (its own
    /// process owns its movement), and until its reports arrive from the new place the server's copy is held there.</summary>
    void Place(ComponentPlayer player, string key, Vector3 position, float yaw, bool tell) {
        Put(player, position, yaw);
        if (ScNet.PeerOf(player) is not { } peer) return;
        m_placing[key] = (position, yaw, Now + 3, Now);
        if (tell) ScNet.SendTo(peer, DmNet.OpPlace, w => w.Vector3(position).Float(yaw));
    }
    void ToLobby(ComponentPlayer player, string key) { if (Arena.HasLobby) Place(player, key, Arena.Lobby, Arena.LobbyYaw, true); }
    void KeepPlaced(double now) {
        foreach (var placing in m_placing.ToList()) {
            if (PlayerOf(placing.Key) is not { } player || now > placing.Value.Until) { m_placing.Remove(placing.Key); continue; }
            if (Vector3.Distance(player.ComponentBody.Position, placing.Value.Target) > 3) { player.ComponentBody.Position = placing.Value.Target; player.ComponentBody.Velocity = Vector3.Zero; }
            else if (now - placing.Value.SentAt > .5) m_placing.Remove(placing.Key);
        }
    }

    // ---------------------------------------------------------------- inventories
    readonly Dictionary<string, Dictionary<int, (int Value, int Count)>> m_issued = [];
    readonly Dictionary<int, (string Key, int Variant, bool Show)> m_counters = [];
    static int GunIndex => BlocksManager.GetBlockIndex<ScGunBlock>(true);
    static int Slots(IInventory inventory) => inventory is ComponentCreativeInventory creative ? creative.OpenSlotsCount : inventory.SlotsCount;
    static IEnumerable<int> Carried(ComponentPlayer player) {
        var inventory = player.ComponentMiner.Inventory;
        for (int slot = 0; slot < Slots(inventory); slot++) yield return inventory.GetSlotCount(slot) > 0 ? inventory.GetSlotValue(slot) : 0;
    }
    bool IsModeItem(int value) => value != 0 && Terrain.ExtractContents(value) == GunIndex && Armory.Owns(GunSpec.GetId(Terrain.ExtractData(value)));
    static bool IsCsWeapon(int value) => value != 0 && BlocksManager.Blocks[Terrain.ExtractContents(value)] is ScGunBlock or ScKnifeBlock or ScGrenadeBlock;
    static void Write(IInventory inventory, int slot, int value, int count) {
        if (inventory is ComponentCreativeInventory creative) creative.m_slots[slot] = count > 0 ? value : 0;
        else if (inventory is ComponentInventoryBase ordinary) { ordinary.m_slots[slot].Value = count > 0 ? value : 0; ordinary.m_slots[slot].Count = Math.Max(0, count); }
        else { inventory.RemoveSlotItems(slot, inventory.GetSlotCount(slot)); if (count > 0) inventory.AddSlotItems(slot, value, count); }
    }
    void ReturnLeases(string key) {
        foreach (int id in Armory.ReturnAll(key)) m_counters.Remove(id);
        m_issued.Remove(key);
    }
    /// <summary>Takes back everything the mode gave this player. A creative inventory's ten slots are the mode's while
    /// the lobby is open and are emptied whole; an ordinary inventory loses only what was issued, CS weapons included
    /// wherever they were moved - nothing else a player owns is ever deleted here.</summary>
    void Strip(ComponentPlayer player, string key) {
        var inventory = player.ComponentMiner.Inventory; bool changed = false;
        for (int slot = 0; slot < Slots(inventory); slot++) {
            int value = inventory.GetSlotCount(slot) > 0 ? inventory.GetSlotValue(slot) : 0;
            if (value == 0) continue;
            bool issued = m_issued.TryGetValue(key, out var kit) && kit.TryGetValue(slot, out var item) && item.Value == value;
            if (inventory is ComponentCreativeInventory || issued || IsModeItem(value)) { Write(inventory, slot, 0, 0); changed = true; }
        }
        ReturnLeases(key);
        if (changed) ScNetSlots.Changed(inventory);
    }
    int Lease(DmPlayer p, DmGunChoice gun, out bool reused) {
        var registry = ScGunRegistry.Current; reused = false;
        if (registry is null || registry.Disabled) return -1;
        for (int attempt = 0; attempt < 4; attempt++) {
            if (!Armory.TryLease(gun.Variant, p.Key, Match.MatchId, p.LifeId, variant => registry.Allocate(variant, ScGunGrowth.Capacity(variant, 0), false, ScGunDurability.Full(variant)), out int id, out reused)) return -1;
            if (registry.Restock(id, gun.Variant, gun.SkinId, gun.SilencerOff)) return id;
            // the record is not a stock gun any more (somebody grew it in a session without this package): it leaves the
            // pool as it is and another record is lent
            Armory.Quarantine(id);
            KnifeLog.Warning($"[CS_DM] armoury record {id} is no stock gun any more; kept as it is and taken out of the pool");
        }
        return -1;
    }
    const string ForeignText = "背包里还有其他物品：竞技世界不带入生存物品，请先放进箱子再准备";
    /// <summary>An ordinary (not creative) inventory holds something the mode did not give: the mode never deletes it, so
    /// the player cannot be given a kit over it.</summary>
    bool Foreign(ComponentPlayer player) {
        var inventory = player.ComponentMiner.Inventory;
        if (inventory is ComponentCreativeInventory) return false;
        m_issued.TryGetValue(KeyOf(player) ?? "", out var kit);
        for (int slot = 0; slot < Slots(inventory); slot++) {
            int value = inventory.GetSlotCount(slot) > 0 ? inventory.GetSlotValue(slot) : 0;
            if (value != 0 && !IsModeItem(value) && !(kit is not null && kit.TryGetValue(slot, out var item) && item.Value == value)) return true;
        }
        return false;
    }
    /// <summary>Gives a life its loadout (or exchanges it inside the protection), all of it or nothing: every gun is
    /// leased first, and only then are the slots written and published once. Returns why not, or null.</summary>
    string Issue(ComponentPlayer player, DmPlayer p, DmLoadout former, DmLoadout loadout) {
        var inventory = player.ComponentMiner.Inventory;
        if (inventory is not ComponentCreativeInventory && Slots(inventory) < DmSlots.Count) return "这个背包放不下竞技装备";
        if (Foreign(player)) return ForeignText;
        var wanted = new Dictionary<int, (int Value, int Count)>();
        var formerGuns = former.Guns().ToDictionary(g => g.Slot, g => g.Gun); var kept = new HashSet<int>(); var fresh = new List<int>(); var held = new HashSet<int>();
        m_issued.TryGetValue(p.Key, out var present);
        foreach (var (slot, gun) in loadout.Guns()) {
            if (formerGuns.TryGetValue(slot, out var same) && same == gun && present is not null && present.TryGetValue(slot, out var item) && inventory.GetSlotValue(slot) == item.Value) { wanted[slot] = item; kept.Add(slot); held.Add(GunSpec.GetId(Terrain.ExtractData(item.Value))); continue; }
            int id = Lease(p, gun, out bool reused);
            if (id < 0) { foreach (int taken in fresh) { Armory.Return(taken); m_counters.Remove(taken); } return "装备发放失败（竞技枪池已满或世界枪械数据不可用）"; }
            if (!reused) fresh.Add(id);
            held.Add(id); m_counters[id] = (p.Key, gun.Variant, gun.Counter);
            wanted[slot] = (Terrain.MakeBlockValue(GunIndex, 0, GunSpec.WithId(gun.Variant, id)), 1);
        }
        if (loadout.Knife is { } knife) wanted[DmSlots.Knife] = (Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScKnifeBlock>(true), 0, ScKnifeSkinCatalog.With(knife.Variant, knife.Finish)), 1);
        for (int kind = 0; kind < DmCatalogue.GrenadeKinds; kind++) if (p.GrenadesLeft(kind) > 0) wanted[DmSlots.Grenade(kind)] = (ScGrenadeBlock.Value(kind), p.GrenadesLeft(kind));
        // records the former kit held and the new one does not go back to the pool
        foreach (int id in Armory.LeasedTo(p.Key).Where(id => !held.Contains(id)).ToList()) { Armory.Return(id); m_counters.Remove(id); }
        for (int slot = 0; slot < DmSlots.Count; slot++) {
            var item = wanted.GetValueOrDefault(slot);
            int count = inventory.GetSlotCount(slot), value = count > 0 ? inventory.GetSlotValue(slot) : 0;
            if (value != item.Value || inventory is not ComponentCreativeInventory && count != item.Count) Write(inventory, slot, item.Value, item.Count);
        }
        m_issued[p.Key] = wanted;
        ScNetSlots.Changed(inventory);
        return null;
    }
    /// <summary>Every frame for a life: its slots hold what was issued and nothing else of the mode's or of CS (the
    /// authority's answer to a slot a client or another mod rewrote).</summary>
    void EnforceKit(ComponentPlayer player, DmPlayer p) {
        if (!m_issued.TryGetValue(p.Key, out var kit)) return;
        var inventory = player.ComponentMiner.Inventory; bool creative = inventory is ComponentCreativeInventory, changed = false;
        for (int slot = 0; slot < Slots(inventory); slot++) {
            int count = inventory.GetSlotCount(slot), value = count > 0 ? inventory.GetSlotValue(slot) : 0;
            var item = kit.GetValueOrDefault(slot);
            bool grenade = item.Value != 0 && BlocksManager.Blocks[Terrain.ExtractContents(item.Value)] is ScGrenadeBlock;
            int left = grenade ? p.GrenadesLeft(ScGrenadeBlock.Kind(item.Value)) : item.Count;
            if (left <= 0) item = default;
            bool ok = item.Value == 0
                ? value == 0 || !creative && !IsCsWeapon(value)
                : value == item.Value && (creative || count == left);
            if (!ok) { Write(inventory, slot, item.Value, item.Value == 0 ? 0 : left); changed = true; }
        }
        if (changed) ScNetSlots.Changed(inventory);
    }
    static void SelectFirst(ComponentPlayer player) {
        var inventory = player.ComponentMiner.Inventory;
        foreach (int slot in new[] { DmSlots.Primary, DmSlots.Secondary, DmSlots.Knife, DmSlots.Zeus })
            if (inventory.GetSlotCount(slot) > 0) { inventory.ActiveSlotIndex = slot; return; }
    }
    /// <summary>A throwable left the hand of a life (DmMode.AttackCommitted).</summary>
    internal void Thrown(ComponentPlayer player, int value) {
        if (StateOf(player) is not { } p) return;
        Match.GrenadeThrown(p.Key, ScGrenadeBlock.Kind(value));
        m_selfDirty.Add(p.Key);                                         // (the slot follows in EnforceKit)
    }
    /// <summary>The competitive counter of a gun item (ScStatTrakRenderer.CounterSource): shown when its loadout asked for
    /// it, counting this match's confirmed kills of its holder with this model. Null for a gun that is not the mode's.</summary>
    public (bool Show, long Kills)? Counter(int value) {
        if (!Enabled || Terrain.ExtractContents(value) != GunIndex) return null;
        int id = GunSpec.GetId(Terrain.ExtractData(value));
        if (!Authority) { foreach (var self in View.Selves) if (self.Counters.TryGetValue(id, out var seen)) return seen; return null; }
        if (!m_counters.TryGetValue(id, out var counter)) return Armory.Owns(id) ? (false, 0) : null;
        return (counter.Show, Match.Find(counter.Key)?.GunKills.GetValueOrDefault(counter.Variant) ?? 0);
    }

    // ---------------------------------------------------------------- injuries
    readonly Dictionary<(string Victim, string Attacker), float> m_burn = [];
    /// <summary>The engine is about to apply an injury (ModLoader.CalculateCreatureInjuryAmount): for a governed player the
    /// mode settles it - once, here - and the engine applies the picture of the result. A lethal result ends the life
    /// in the mode and leaves the engine's health alone: the engine never sees a death (design §8.1).</summary>
    public void OnInjury(Injury injury) {
        if (!Enabled || Frozen is not null || !Authority || Match is not { Governing: true }) return;
        var health = injury.ComponentHealth;
        if (health?.Entity.FindComponent<ComponentPlayer>() is not { } victim || Match.Find(KeyOf(victim)) is not { } p) return;
        float raw = injury.Amount; injury.Amount = 0;                    // nothing reaches the engine but what is settled below
        if (!Match.Running || p.Phase != DmPlayerPhase.Alive || !(raw > 0)) return;
        var attack = injury.Attackment; var facts = (attack as IScAttackFacts)?.Facts;
        DmSettlement? settled;
        if (facts is not null) {
            string attackerKey = attack.Attacker?.FindComponent<ComponentPlayer>() is { } shooter ? KeyOf(shooter) : KeyOfIndex(facts.AttackerPlayer);
            if (attackerKey is null) return;                            // a CS attack nobody in this match made
            var hit = new DmHit { AttackerKey = attackerKey, Kind = facts.Kind, Weapon = facts.Weapon ?? "", ArmourRatio = DmWeapons.ArmourRatio(facts.Weapon),
                ThroughSmoke = facts.ThroughSmoke, AttackerBlind = facts.AttackerBlind, Penetrations = facts.Penetrations };
            switch (facts.Kind) {
                case ScAttackKind.Shot or ScAttackKind.Zeus:
                    hit.GunVariant = Array.FindIndex(GunSpec.All, g => g.Name == facts.Weapon);
                    var hits = (attack as ScSurvivalBalance.BulletAttack)?.Hits;
                    float total = hits?.Total ?? 0, scale = total > 0 ? raw / total : 0;
                    hit.Regions = hits is null || total <= 0 ? [(ScHitPart.Body, raw)] : hits.Regions().Select(r => (r.Part, r.Power * scale)).ToArray();
                    hit.NoScope = facts.HasScope && !facts.Scoped && hit.GunVariant >= 0 && DmCatalogue.ClassOf(hit.GunVariant) is ScGunDurability.Class.BoltSniper or ScGunDurability.Class.AutoSniper;
                    break;
                case ScAttackKind.Knife:
                    bool behind = Vector3.Dot(Vector3.Normalize(victim.ComponentBody.Rotation.GetForwardVector() * new Vector3(1, 0, 1)), Vector3.Normalize(attack.HitDirection * new Vector3(1, 0, 1))) > DmWeapons.BackstabDot;
                    hit.Regions = [(ScHitPart.Body, DmWeapons.KnifeDamage(facts.Heavy, behind))];
                    break;
                case ScAttackKind.Fire:
                    // the core's fire speaks in its own units per frame: scaled to CS2's per-second figure, and whole points
                    // are settled as they accumulate so that no frame rate rounds a burn away
                    float burn = m_burn.GetValueOrDefault((p.Key, attackerKey)) + raw * (DmWeapons.FirePerSecond(facts.Weapon) / 6f);
                    int whole = (int)burn; m_burn[(p.Key, attackerKey)] = burn - whole;
                    if (whole <= 0) return;
                    hit.Regions = [(ScHitPart.Body, whole)];
                    break;
                default:
                    hit.Regions = [(ScHitPart.Body, raw * (DmWeapons.HeDamage / ScGrenadeState.HeDamage))];
                    break;
            }
            settled = Match.Hurt(p.Key, hit, Now);
            if (settled is { Lethal: true }) facts.Lethal = true;
        }
        else if (attack?.Attacker?.FindComponent<ComponentPlayer>() is not null) return;   // a player's other attack (a fist, another mod's weapon): not a match weapon
        else settled = Match.HurtByWorld(p.Key, raw * DmFixed.Health, Now);                  // a fall, the world's fire, a creature: health only
        if (settled is not { } result || result.Lethal) return;
        float wanted = Math.Max(.01f, result.Health / (float)DmFixed.Health);
        if (health.Health > wanted) { injury.Amount = health.Health - wanted; injury.IgnoreInvulnerability = true; }
    }

    // ---------------------------------------------------------------- publication
    void Tell(string key, string text) {
        if (key is null) { ScNet.Broadcast(DmNet.OpNotice, w => w.String(text)); foreach (var local in LocalPlayers) View.Of(local).Notice = (text, Now); return; }
        if (PlayerOf(key) is not { } player) return;
        if (ScNet.PeerOf(player) is { } peer) ScNet.SendTo(peer, DmNet.OpNotice, w => w.String(text));
        else View.Of(player).Notice = (text, Now);
    }
    double Remaining(double now) => Match.Phase switch {
        DmPhase.Running => Match.SecondsLeft(now), DmPhase.Countdown => Math.Max(0, DmFixed.CountdownSeconds - (now - Match.PhaseSince)),
        DmPhase.Results => Math.Max(0, DmFixed.ResultsSeconds - (now - Match.PhaseSince)), _ => 0 };
    List<DmView.Row> Rows() {
        var board = Match.Scoreboard(); var rows = new List<DmView.Row>(board.Count);
        foreach (var score in board) {
            var p = Match.Find(score.Key);
            rows.Add(new(PlayerOf(score.Key)?.PlayerData.PlayerIndex ?? -1, score.Key, score.Name, p.Phase, score.Kills, score.Deaths, score.Assists, score.Connected, score.Playing));
        }
        return rows;
    }
    Dictionary<int, (bool Show, long Kills)> CountersOf(DmPlayer p) =>
        m_counters.Where(c => c.Value.Key == p.Key).ToDictionary(c => c.Key, c => (c.Value.Show, (long)p.GunKills.GetValueOrDefault(c.Value.Variant)));
    void Publish(double now) {
        bool periodic = now - m_stateSentAt >= 5;
        if (m_stateDirty || periodic) {
            m_stateDirty = false; m_stateSentAt = now; m_stateFor.Clear();
            ScNet.Broadcast(DmNet.OpState, w => DmNet.WriteState(w, Enabled, Match.Phase, Match.MatchId, Remaining(now), Match.Rules, Arena, Match.LastResult?.Reason));
            ApplyState(Enabled, Match.Phase, Match.MatchId, Remaining(now), Match.Rules, Arena, Match.LastResult?.Reason, DmWeapons.Fingerprint);
        }
        foreach (string key in m_stateFor) if (PlayerOf(key) is { } joined && ScNet.PeerOf(joined) is { } peer) {
            ScNet.SendTo(peer, DmNet.OpState, w => DmNet.WriteState(w, Enabled, Match.Phase, Match.MatchId, Remaining(now), Match.Rules, Arena, Match.LastResult?.Reason));
            if (Match.LastResult is { } last && Match.Phase == DmPhase.Results) ScNet.SendTo(peer, DmNet.OpResult, w => w.String(DmNet.Encode(last)));
        }
        m_stateFor.Clear();
        if (m_playersDirty) {
            m_playersDirty = false;
            var rows = Rows();
            ScNet.Broadcast(DmNet.OpPlayers, w => DmNet.WritePlayers(w, rows));
            View.Rows = rows;
        }
        foreach (string key in m_selfDirty) {
            if (Match.Find(key) is not { } p || PlayerOf(key) is not { } player) continue;
            var counters = CountersOf(p);
            if (ScNet.PeerOf(player) is { } peer) ScNet.SendTo(peer, DmNet.OpSelf, w => DmNet.WriteSelf(w, p, now, counters));
            else {
                var self = View.Of(player);
                self.Phase = p.Phase; self.LifeId = p.LifeId; self.Health = p.Health; self.Armour = p.Armour; self.Helmet = p.Helmet; self.Entered = p.Entered;
                self.ProtectedUntil = p.ProtectedUntil; self.Desired = p.Desired; self.DesiredRevision = p.DesiredRevision; self.Active = p.Active; self.ActiveRevision = p.ActiveRevision;
                for (int kind = 0; kind < self.GrenadesLeft.Length; kind++) self.GrenadesLeft[kind] = p.GrenadesLeft(kind);
                self.Counters = counters;
            }
        }
        m_selfDirty.Clear();
    }

    // ---------------------------------------------------------------- the view (every process)
    internal void ApplyState(bool enabled, DmPhase phase, int matchId, double remaining, DmRules rules, DmArenaDefinition arena, string reason, string fingerprint) {
        if (!Authority) {
            if (enabled && !Enabled && Frozen is null) Activate(null);
            if (enabled && fingerprint != DmWeapons.Fingerprint) ShowNotice("本机的死亡竞赛规则数据与服务器不一致：请安装与服务器同一次发布的拓展包");
            if (!enabled) View.Rows = [];
            Arena = arena;
        }
        View.Enabled = enabled; View.Phase = phase; View.MatchId = matchId; View.PhaseEndsAt = Now + remaining; View.Rules = rules; View.Arena = arena; View.Reason = reason ?? "";
    }
    internal void ShowNotice(string text) { foreach (var local in LocalPlayers) View.Of(local).Notice = (text, Now); }
    IEnumerable<ComponentPlayer> LocalPlayers => m_players.PlayersData.Select(d => d.ComponentPlayer).Where(p => p is not null && ScNet.IsLocal(p));
    /// <summary>A remote client's own player's part of the view (null before the player exists).</summary>
    public DmView.Self LocalSelf => LocalPlayer is { } player ? View.Of(player) : null;
    internal void ShowResult(DmResult result) { View.Result = result; View.ResultAt = Now; }
    internal void ShowDeath(DmKill kill, int victimIndex, Vector3 victimEye, Vector3? killerEye) {
        if (!View.AddKill(kill, Now)) return;
        if (LocalPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == victimIndex) is not { } local) return;
        var self = View.Of(local);
        self.OwnDeath = kill; self.OwnDeathAt = Now; self.DeathEye = victimEye; self.KillerEye = killerEye;
        DmDeathCamera.Show(local, victimEye, killerEye, DmFixed.DeathViewSeconds);
    }

    // ---------------------------------------------------------------- a remote client
    int m_clientPending = -1; Vector3 m_clientPoint;
    ComponentPlayer LocalPlayer => m_players.PlayersData.Select(d => d.ComponentPlayer).FirstOrDefault(p => p is not null && ScNet.IsLocal(p));
    internal void ClientPrepare(int lifeId, Vector3 position, float yaw) {
        if (LocalPlayer is not { } player) return;
        Put(player, position, yaw); m_clientPending = lifeId; m_clientPoint = position;
    }
    internal void ClientCommit(int lifeId, Vector3 position, float yaw) {
        if (LocalPlayer is not { } player) return;
        if (Vector3.Distance(player.ComponentBody.Position, position) > 3) Put(player, position, yaw);
        m_clientPending = -1; DmDeathCamera.End(player); SelectFirst(player);
    }
    internal void ClientPlace(Vector3 position, float yaw) { if (LocalPlayer is { } player) Put(player, position, yaw); }
    void ClientUpdate() {
        Present();
        if (LocalPlayer is not { } player) return;
        // the engine's health bar is a picture of the mode's figures on this side as well
        if (View.Governing && player.ComponentHealth is { } health) {
            var self = View.Of(player); float wanted = self.Fighting ? Math.Max(.01f, self.Health / (float)DmFixed.Health) : 1f;
            if (MathF.Abs(health.Health - wanted) > .0005f) health.Health = wanted;
            // a life walks: this process owns its player's movement, so the creative flight is switched off here as well
            if (self.Fighting && player.ComponentLocomotion is { IsCreativeFlyEnabled: true } locomotion) locomotion.IsCreativeFlyEnabled = false;
        }
        if (m_clientPending < 0) return;
        // ready when the ground this life will stand on is there: the server starts the three seconds only then
        var cell = new Point3(Terrain.ToCell(m_clientPoint.X), Terrain.ToCell(m_clientPoint.Y), Terrain.ToCell(m_clientPoint.Z));
        if (m_terrain.Terrain.GetChunkAtCell(cell.X, cell.Z) is not { State: TerrainChunkState.Valid }) return;
        if (Vector3.Distance(player.ComponentBody.Position, m_clientPoint) > 3) Put(player, m_clientPoint, YawOf(player.ComponentBody));
        if (DmNet.SendReady(m_clientPending)) m_clientPending = -1;
    }
}
