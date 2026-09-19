using Engine;
using Engine.Graphics;
using TemplatesDatabase;
using GameEntitySystem;
namespace Game;

public sealed class SubsystemScC4 : Subsystem, IUpdateable, IDrawable {
    sealed class Preparation {
        public ScThrowTransaction Transaction;
        public IInventory Inventory;
        public Vector3 PlayerStart, Position;
        public double Started;
        public int Slot, SoundIndex;
        public long AnimationSequence;
        public bool Placed;
        public float PreviousCrouch;
        public int Fuse;
    }
    public static bool IsBombDamage(Attackment attack)=>attack is BombAttack;
    sealed class BombAttack(ComponentBody body, GameEntitySystem.Entity owner, Vector3 point, Vector3 direction, float power)
        : ProjectileAttackment(body, owner, point, direction, power, null) {
        public override bool DisableFriendlyFire() => Attacker != Target && base.DisableFriendlyFire();
    }
    sealed class DelayedHit {
        public ComponentBody Body; public GameEntitySystem.Entity Owner;
        public Vector3 Point, Direction; public float Power; public double At;
    }
    /// <summary>How many hits in total a creature that caps a single hit at a fraction of its health
    /// (2-2.5 % is common in the audited mods) receives. Only such a creature is split: the first hit is measured
    /// against the uncapped injury, and the rest are queued only when the cap was seen to bite. Every other
    /// creature takes the blast in one hit exactly as before.</summary>
    const int BlastChunks = 10;
    /// <summary>Spacing between the queued hits. A per-hit cap is beaten by the count however fast the hits land;
    /// the small gap only keeps them from merging and gives the death check a frame to run.</summary>
    const double BlastChunkInterval = .1;
    readonly List<ScC4Charge> charges = [];
    readonly List<ScC4Blast> blasts = [];
    readonly List<DelayedHit> delayedHits = [];
    readonly Dictionary<ComponentPlayer, Preparation> preparations = [];
    readonly Dictionary<ComponentPlayer, bool> buttons = [], held = [];
    readonly Dictionary<ComponentPlayer, LabelWidget> progress = [];
    readonly PrimitivesRenderer3D renderer = new();
    SubsystemTime time;
    SubsystemTerrain terrain;
    SubsystemPlayers players;
    SubsystemBodies bodies;
    SubsystemAudio audio;
    SubsystemGameInfo info;
    double lastTime;
    bool ready;
    public UpdateOrder UpdateOrder => UpdateOrder.Default;
    public int[] DrawOrders => [10];
    public void SetPlantButton(ComponentPlayer player, bool pressed) => buttons[player] = pressed;
    public bool IsPlanting(ComponentPlayer player) => preparations.ContainsKey(player);
    public void ConfigureTimer(ComponentPlayer player) {
        if(!ScGunBindings.Available(player)||!ScC4Block.IsValue(player.ComponentMiner.ActiveBlockValue)||IsPlanting(player))return;
        int index=player.PlayerData.PlayerIndex;
        DialogsManager.ShowDialog(player.GuiWidget,new TextBoxDialog("C4 引爆时间（5～300 秒，仅下次安装）",ScUiSettings.C4Fuse(index).ToString(),3,text=>{
            if(text is null)return;
            if(!int.TryParse(text,out int seconds)||seconds<5||seconds>300){player.ComponentGui.DisplaySmallMessage("请输入 5～300 的整数秒数。",Color.White,false,false);return;}
            bool existed=ScUiSettings.C4Fuses.TryGetValue(index,out int prior);ScUiSettings.C4Fuses[index]=seconds;
            if(!ScUiSettings.Save()){if(existed)ScUiSettings.C4Fuses[index]=prior;else ScUiSettings.C4Fuses.Remove(index);player.ComponentGui.DisplaySmallMessage("设时未保存，请检查存储空间。",Color.White,false,false);return;}
            player.ComponentGui.DisplaySmallMessage($"下次 C4：{seconds} 秒",Color.White,false,false);
        }));
    }
    public int ViewmodelValue(ComponentPlayer player, int value) => player is not null && preparations.TryGetValue(player, out var p)
        && p.Placed && value == 0 && ReferenceEquals(player.ComponentMiner.Inventory, p.Inventory) && player.ComponentMiner.Inventory.ActiveSlotIndex == p.Slot ? ScC4Block.Value : value;
    public override void Load(ValuesDictionary values) {
        base.Load(values);
        int schema = values.GetValue<int>("Schema", 1);
        if (schema != 1) throw new InvalidOperationException("Unsupported C4 save version; world has not been rewritten.");
        var saved = values.GetValue<ValuesDictionary>("Charges", null);
        if (saved is not null) foreach (var entry in saved) charges.Add(ScC4Charge.Load((ValuesDictionary)entry.Value));
        time = Project.FindSubsystem<SubsystemTime>(true); terrain = Project.FindSubsystem<SubsystemTerrain>(true);
        players = Project.FindSubsystem<SubsystemPlayers>(true); bodies = Project.FindSubsystem<SubsystemBodies>(true);
        audio = Project.FindSubsystem<SubsystemAudio>(true); info = Project.FindSubsystem<SubsystemGameInfo>(true);
        lastTime = time.GameTime; ready = true;
    }
    public override void Save(ValuesDictionary values) {
        if (!ready) throw new InvalidOperationException("C4 subsystem did not finish loading.");
        base.Save(values); values.SetValue("Schema", 1);
        var saved = new ValuesDictionary(); for (int i = 0; i < charges.Count; i++) saved.SetValue(i.ToString(), charges[i].Save());
        values.SetValue("Charges", saved);
    }
    void Sound(string name, Vector3 at, float volume = 1, float pitch = 0) => audio.PlaySound("Audio/ScCsgoKnives/" + name, volume, pitch, at, 32, true);
    bool Down(ComponentPlayer p) => buttons.GetValueOrDefault(p) || ScGunBindings.Down(p, ScGunFunctions.Plant);
    static bool Stationary(ComponentPlayer p) => (p.ComponentBody.StandingOnValue.HasValue || p.ComponentBody.StandingOnBody is not null)
        && new Vector2(p.ComponentBody.Velocity.X, p.ComponentBody.Velocity.Z).LengthSquared() < .09f;
    bool Floor(ComponentPlayer p, out Vector3 position) {
        var hit = terrain.Raycast(p.ComponentBody.Position + Vector3.UnitY * .25f, p.ComponentBody.Position - Vector3.UnitY * .7f,
            false, true, (v, _) => BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));
        position = hit?.HitPoint() ?? Vector3.Zero;
        return hit.HasValue && CellFace.FaceToVector3(hit.Value.CellFace.Face).Y > .5f;
    }
    // Raycast intentionally ignores non-collidable terrain such as snow layers.
    // That leaves the charge at the solid block's top and the layer can hide the
    // model. Lift the visual/plant position over thin covering blocks using their
    // actual collision-box height. Treating every occupied cell as a full cube
    // was the reason C4 appeared a whole block too high on snow and carpets.
    Vector3 VisiblePlantPosition(Vector3 floor) {
        if (terrain?.Terrain is null) return floor + Vector3.UnitY * .01f;
        int x = Terrain.ToCell(floor.X), z = Terrain.ToCell(floor.Z);
        // Start just above the recorded surface. The old build stored ground+0.01,
        // so this also finds a snow layer in that same cell without treating the
        // solid support block itself as an obstruction.
        int firstCellY = Terrain.ToCell(floor.Y + .05f);
        float top = floor.Y;
        for (int y = firstCellY; y <= firstCellY + 4; y++) {
            int value = terrain.Terrain.GetCellValue(x, y, z);
            if (Terrain.ExtractContents(value) == 0) break;
            var block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
            var boxes = block.GetCustomCollisionBoxes(terrain, value);
            float height = 0;
            if (boxes is not null) foreach (var box in boxes) height = MathF.Max(height, box.Max.Y);
            // Snow (0.125), carpet (0.0625), and similar overlays are the only
            // cells that should adjust the charge. Stop at a real block or plant
            // so a nearby tall decoration cannot lift the bomb several blocks.
            if (height <= 0 || height > .25f) break;
            top = MathF.Max(top, y + height);
        }
        return new Vector3(floor.X, top + .01f, floor.Z);
    }
    void Cancel(ComponentPlayer player, Preparation p) {
        p.Transaction.Cancel(); preparations.Remove(player);
        if(player.ComponentBody.TargetCrouchFactor==1)player.ComponentBody.TargetCrouchFactor=p.PreviousCrouch;
        KnifeAnimationController.CancelC4Action(player, p.AnimationSequence);
        if (progress.TryGetValue(player, out var label)) label.IsVisible = false;
    }
    /// <summary>One blast hit, built exactly like the original single hit but delivered on its own tick.</summary>
    void ApplyBlastHit(ComponentBody body, GameEntitySystem.Entity owner, Vector3 point, Vector3 direction, float power) {
        var attack = new BombAttack(body, owner, point, direction, power) {
            ImpulseFactor = 0, StunTimeSet = 0, StunTimeAdd = 0, AllowImpulseAndStunWhenDamageIsZero = false, AttackSoundVolume = 0
        };
        ComponentMiner.AttackBody(attack);
    }
    /// <summary>Fires the C4's split hits as their times arrive; a body that died or left the world is skipped.</summary>
    void ProcessDelayedHits() {
        for (int i = delayedHits.Count - 1; i >= 0; i--) {
            var hit = delayedHits[i];
            if (time.GameTime < hit.At) continue;
            delayedHits.RemoveAt(i);
            try {
                if (hit.Body?.Entity is null) continue;
                var health = hit.Body.Entity.FindComponent<ComponentHealth>();
                if (health is null || health.Health <= 0) continue;
                ApplyBlastHit(hit.Body, hit.Owner, hit.Point, hit.Direction, hit.Power);
            }
            catch (Exception e) { KnifeDiagnostics.WarnOnce("c4-delayed-hit", "[C4] delayed hit skipped: " + e.GetBaseException().Message); }
        }
    }
    public void Update(float dt) {
        float elapsed = (float)Math.Max(0, time.GameTime - lastTime); lastTime = time.GameTime;
        ProcessDelayedHits();
        blasts.RemoveAll(b=>time.GameTime-b.Started>ScC4Blast.Lifetime);
        foreach (var c in charges.ToArray()) {
            if (c.Remaining <= 0 || c.Tick(elapsed)) { charges.Remove(c); Detonate(c); continue; }
            if(c.NextCue() is string cue) Sound(cue,c.Position,cue=="c4_warning"?.8f:.9f,0);
        }
        foreach (var p in players.ComponentPlayers) {
            bool down = Down(p), was = held.GetValueOrDefault(p); held[p] = down;
            bool available = ScGunBindings.Available(p);
            if (!preparations.TryGetValue(p, out var prep)) {
                if (!down || was || !available || !ScC4Block.IsValue(p.ComponentMiner.ActiveBlockValue) || !Stationary(p)
                    || KnifeAnimationController.IsBusy(p.Entity.FindComponent<ComponentFirstPersonModel>()) || !Floor(p, out var place)) continue;
                if (charges.Count >= 16) { p.ComponentGui.DisplaySmallMessage("活动 C4 已达 16 个，未消耗物品。", Color.White, false, false); continue; }
                prep = new Preparation { Transaction = new ScThrowTransaction(p.ComponentMiner.Inventory), Inventory = p.ComponentMiner.Inventory, PlayerStart = p.ComponentBody.Position,
                    Position = VisiblePlantPosition(place), Started = time.GameTime, Slot = p.ComponentMiner.Inventory.ActiveSlotIndex, Fuse=ScUiSettings.C4Fuse(p.PlayerData.PlayerIndex) };
                prep.PreviousCrouch=p.ComponentBody.TargetCrouchFactor;p.ComponentBody.TargetCrouchFactor=1;
                preparations[p] = prep; prep.AnimationSequence = KnifeAnimationController.C4Action(p, "plant");
            }
            float age = (float)(time.GameTime - prep.Started);
            if (!ReferenceEquals(p.ComponentMiner.Inventory, prep.Inventory)) { Cancel(p, prep); continue; }
            if (prep.Placed) { if (age >= Cs2Rig.Duration("c4", "plant") || !available || p.ComponentMiner.Inventory.ActiveSlotIndex != prep.Slot) Cancel(p, prep); continue; }
            if (!down || !available || !prep.Transaction.Valid || !Stationary(p) || Vector3.DistanceSquared(prep.PlayerStart, p.ComponentBody.Position) > .04f) { Cancel(p, prep); continue; }
            if (!progress.TryGetValue(p, out var label)) {
                label = new LabelWidget { FontScale = .65f, DropShadow = true, IsHitTestVisible = false,
                    HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Far, Margin = new Vector2(0, 96) };
                progress[p] = label; p.ComponentGui.ControlsContainerWidget.Children.Add(label);
            }
            label.IsVisible = true; label.Text = $"C4  {Math.Min(100, (int)(age / ScC4Charge.PlantSeconds * 100))}%";
            var events = Cs2Rig.Events("c4", "plant");
            while (prep.SoundIndex < events.Count && events[prep.SoundIndex].At <= age) {
                var e = events[prep.SoundIndex++];
                if (e.Name == "c4.initiate") Sound("c4_initiate", prep.Position, .799805f);
                else if (e.Name == "c4.keypressquiet") Sound("c4_key_press" + (1 + prep.SoundIndex % 7), prep.Position, .3f);
            }
            if (age < ScC4Charge.PlantSeconds) continue;
            var charge = new ScC4Charge { Owner = p.PlayerData.PlayerIndex, Position = prep.Position, Fuse=prep.Fuse,Remaining=prep.Fuse, Yaw = MathF.Atan2(p.ComponentBody.Matrix.Forward.X, p.ComponentBody.Matrix.Forward.Z) };
            bool planted = prep.Transaction.Commit(info.WorldSettings.GameMode == GameMode.Creative, () => charges.Count < 16, () => { charges.Add(charge); return true; });
            if (!planted) { Cancel(p, prep); continue; }
            prep.Placed = true; label.IsVisible = false; Sound("c4_plant", prep.Position);
        }
        foreach (var pair in preparations.ToArray()) if (!players.ComponentPlayers.Contains(pair.Key)) Cancel(pair.Key, pair.Value);
        foreach (var p in held.Keys.Where(p => !players.ComponentPlayers.Contains(p)).ToArray()) {
            held.Remove(p); buttons.Remove(p);
            if (progress.Remove(p, out var label)) label.ParentWidget?.Children.Remove(label);
        }
    }
    void Detonate(ScC4Charge c) {
        if(blasts.Count>=8)blasts.RemoveAt(0);
        blasts.Add(new ScC4Blast(c.Position,c.Radius,time.GameTime));
        var owner = players.ComponentPlayers.FirstOrDefault(p => p.PlayerData.PlayerIndex == c.Owner);
        double started = time.GameTime;
        foreach (var body in bodies.Bodies.ToArray()) {
            var target = body.Entity.FindComponent<ComponentPlayer>();
            if (target is not null && target.PlayerData.PlayerIndex != c.Owner && !info.WorldSettings.IsFriendlyFireEnabled) continue;
            Vector3 point = body.BoundingBox.Center(), delta = point - c.Position;
            float power = ScC4Charge.DamageAt(delta.Length(), c.Power, c.Radius); if (power <= 0) continue;
            Vector3 direction = delta.LengthSquared() > .0001f ? Vector3.Normalize(delta) : Vector3.UnitY;
            // The first hit is the whole blast, exactly as before. If a mod's per-hit percentage cap
            // (CalculateCreatureInjuryAmount) leaves it far below what the blast would have taken, only then is
            // the same total split into more hits. An ordinary creature takes the one hit and nothing is queued.
            var health = body.Entity.FindComponent<ComponentHealth>();
            float before = health?.Health ?? 0;
            if (health is not null && before <= 0) continue;
            var first = new BombAttack(body, owner?.Entity, point, direction, power) {
                ImpulseFactor = 0, StunTimeSet = 0, StunTimeAdd = 0, AllowImpulseAndStunWhenDamageIsZero = false, AttackSoundVolume = 0
            };
            float expected = 0;
            if (health is not null) {
                try { expected = Math.Min(before, Math.Max(0, first.CalculateInjuryAmount())); } catch { expected = 0; }
            }
            ComponentMiner.AttackBody(first);
            if (health is null) continue;
            float dealt = Math.Max(0, before - health.Health);
            bool capped = health.Health > 0 && dealt > 1e-4f && dealt < expected * .5f;
            if (!capped) continue;
            float chunk = power / BlastChunks;
            for (int i = 1; i < BlastChunks; i++)
                delayedHits.Add(new DelayedHit { Body = body, Owner = owner?.Entity, Point = point, Direction = direction, Power = chunk, At = started + i * BlastChunkInterval });
        }
        Sound("c4_explode_close_01", c.Position, 2);
        var particles = new ExplosionParticleSystem();
        var center = new Point3(Terrain.ToCell(c.Position.X), Terrain.ToCell(c.Position.Y), Terrain.ToCell(c.Position.Z));
        for (int x = -2; x <= 2; x++) for (int y = 0; y <= 3; y++) for (int z = -2; z <= 2; z++)
            if (x*x + y*y + z*z <= 9) particles.SetExplosionCell(center + new Point3(x,y,z), 1);
        Project.FindSubsystem<SubsystemParticles>(true).AddParticleSystem(particles);
    }
    public void Draw(Camera camera, int drawOrder) {
        var block = (ScC4Block)BlocksManager.Blocks[BlocksManager.GetBlockIndex<ScC4Block>(true)];
        foreach (var c in charges) {
            if (Vector3.DistanceSquared(camera.ViewPosition, c.Position) > 128 * 128) continue;
            var matrix = Matrix.CreateRotationY(c.Yaw) * Matrix.CreateTranslation(VisiblePlantPosition(c.Position));
            var env = new DrawBlockEnvironmentData { SubsystemTerrain = terrain, Light = 15 };
            block.DrawPlanted(renderer, Color.White, ref matrix, env);
        }
        foreach(var blast in blasts)blast.Draw(renderer,camera,time.GameTime);
        renderer.Flush(camera.ViewProjectionMatrix);
    }
    public override void Dispose() {
        foreach (var pair in preparations.ToArray()) Cancel(pair.Key, pair.Value);
        foreach (var label in progress.Values) label.ParentWidget?.Children.Remove(label);
        progress.Clear(); preparations.Clear(); charges.Clear(); blasts.Clear(); delayedHits.Clear(); buttons.Clear(); held.Clear(); base.Dispose();
    }
}
