using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Engine;
using Engine.Input;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

static class PlanDllRegression {
    internal record Result(string Name, bool Ok, string Detail);
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Fields).SetValue(obj, value);
    internal static List<Result> Run(Assembly mod, ThirdPartyDlls dlls) {
        List<Result> results = [];
        void Check(string name, bool ok, string detail = "") => results.Add(new("plan-dll/" + name, ok, detail));
        void Section(string name, Action test) { try { test(); } catch (Exception e) { Check(name + "/exception", false, e.ToString()); } }
        Section("settings",()=>{
            var file=mod.GetType("Game.ScUiSettings").GetNestedType("File",BindingFlags.NonPublic);
            object Read(string json)=>System.Text.Json.JsonSerializer.Deserialize(json,file);
            var legacy=Read("{\"Version\":1}");var shape=file.GetProperty("CrosshairShape").GetValue(legacy);
            Check("settings/legacy-shape-defaults",(float)shape.GetType().GetProperty("Width").GetValue(shape)==2&&(float)shape.GetType().GetProperty("Length").GetValue(shape)==8&&(float)shape.GetType().GetProperty("Gap").GetValue(shape)==3);
            var custom=Read("{\"Version\":1,\"CrosshairShape\":{\"Width\":4,\"Length\":12,\"Gap\":6,\"Scale\":1.5,\"Dot\":5},\"GamepadBindings\":{\"reload\":\"X\",\"inspect\":\"LeftShoulder\"},\"GamepadTriggerThreshold\":0.7}");
            var again=Read(System.Text.Json.JsonSerializer.Serialize(custom,file));var pad=(Dictionary<string,string>)file.GetProperty("GamepadBindings").GetValue(again);
            shape=file.GetProperty("CrosshairShape").GetValue(again);
            Check("settings/shape-and-pad-roundtrip",pad["reload"]=="X"&&pad["inspect"]=="LeftShoulder"&&(float)file.GetProperty("GamepadTriggerThreshold").GetValue(again)==.7f&&(float)shape.GetType().GetProperty("Scale").GetValue(shape)==1.5f&&(float)shape.GetType().GetProperty("Dot").GetValue(shape)==5);
        });
        Section("gamepad", () => {
            var type=mod.GetType("Game.ScGamepadBindings"); var keys=(Dictionary<string,string>)type.GetField("Keys").GetValue(null); var original=new Dictionary<string,string>(keys);
            var stateArray=(Array)typeof(GamePad).GetField("m_states",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            object state=stateArray.GetValue(0);var connected=state.GetType().GetField("IsConnected",Fields);object oldConnected=connected.GetValue(state);
            var triggers=(float[])state.GetType().GetField("Triggers",Fields).GetValue(state); var buttons=(bool[])state.GetType().GetField("Buttons",Fields).GetValue(state);
            var oldTriggers=(float[])triggers.Clone();var oldButtons=(bool[])buttons.Clone();
            var frame=typeof(Time).GetProperty("FrameIndex");int oldFrame=(int)frame.GetValue(null);
            float threshold=(float)type.GetField("Threshold").GetValue(null);
            var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();var widget=Blank<GameWidget>();
            widget.WidgetsHierarchyInput=new WidgetInput(WidgetInputDevice.GamePad1);player.PlayerData.m_gameWidget=widget;
            bool Down(bool once)=>(bool)type.GetMethod("Down").Invoke(null,[player,"reload",once]);
            void Next()=>frame.SetValue(null,(int)frame.GetValue(null)+1);
            try {
                connected.SetValue(state,true);type.GetField("Threshold").SetValue(null,.5f);keys["reload"]="TriggerRight";
                foreach(var (position,want,edge) in new[]{(.49f,false,false),(.51f,true,true),(.45f,true,false),(.39f,false,false),(.45f,false,false),(.5f,true,true)}) {
                    triggers[1]=position;Next();Check($"gamepad/real-trigger-hysteresis/{position}/{want}",Down(false)==want&&Down(true)==edge);
                }
                connected.SetValue(state,false);Next();Check("gamepad/disconnect-drops-input",!Down(false)&&(int)type.GetMethod("ConnectedMask").Invoke(null,[player])==0);
                connected.SetValue(state,true);triggers[1]=0;Next();
                foreach(var button in Enum.GetValues<GamePadButton>().Where(b=>b!=GamePadButton.Null)) {
                    keys["reload"]=button.ToString();buttons[(int)button]=false;Next();Down(false);
                    buttons[(int)button]=true;Next();bool pressed=Down(true)&&Down(false);Next();bool held=Down(false)&&!Down(true);
                    buttons[(int)button]=false;Next();Check("gamepad/press-hold-release/"+button,pressed&&held&&!Down(true)&&!Down(false));
                }
                var gateType=mod.GetType("Game.ScTriggerReleaseGate");var gate=Activator.CreateInstance(gateType);var observe=gateType.GetMethod("ObserveSources");var inv=new object();
                bool Gate(int slot,int down,bool available,int request)=>(bool)observe.Invoke(gate,[inv,slot,slot,down,available,request]);
                Check("gamepad/source-release-is-independent",Gate(0,0,true,0)&&!Gate(1,8,true,8)&&Gate(1,12,true,4)&&!Gate(1,12,true,8)&&Gate(1,4,true,4)&&Gate(1,12,true,8));
                Check("gamepad/reconnect-held-requires-release",!Gate(1,8,false,8)&&!Gate(1,8,true,8)&&Gate(1,0,true,0)&&Gate(1,8,true,8));
            } finally {keys.Clear();foreach(var p in original)keys[p.Key]=p.Value;connected.SetValue(state,oldConnected);oldTriggers.CopyTo(triggers,0);oldButtons.CopyTo(buttons,0);frame.SetValue(null,oldFrame);type.GetField("Threshold").SetValue(null,threshold);}
        });
        Section("missing-record-display", () => {
            var registryType=mod.GetType("Game.ScGunRegistry"); var current=registryType.GetField("Current"); var saved=current.GetValue(null);
            try {
                var registry=Activator.CreateInstance(registryType);current.SetValue(null,registry);
                var block=(Block)Activator.CreateInstance(mod.GetType("Game.ScGunBlock")); int value=Terrain.MakeBlockValue(512,0,64);
                Check("missing-record-display/reliable-model-name",block.GetDisplayName(null,value).Contains("AK-47")&&block.GetDisplayName(null,value).Contains("状态待恢复"));
                Check("missing-record-display/no-new-world-advice",!block.GetDescription(value).Contains("新世界")&&!(bool)block.GetType().GetMethod("IsKnown").Invoke(null,[value]));
                registryType.GetMethod("Allocate").Invoke(registry,[1,2,false,700,1500,0]);
                Check("missing-record-display/model-mismatch-diagnosis",block.GetDisplayName(null,value).Contains("型号与记录不匹配")&&!(bool)block.GetType().GetMethod("IsKnown").Invoke(null,[value]));
                var future=new ValuesDictionary();future.SetValue("Schema",99);current.SetValue(null,registryType.GetMethod("Load").Invoke(null,[future,0d]));
                Check("missing-record-display/unknown-layout-no-guessed-model",!block.GetDisplayName(null,value).Contains("AK-47"));
            } finally {current.SetValue(null,saved);}
        });
        Section("economy", () => {
            // Explicit final golden costs for all 35 models, independent of production class derivation.
            string[] groups = ["glock18,hkp2000,p250,usp_silencer:4,3,2,2,0,0,0", "fiveseven,tec9,cz75a:4,3,3,2,0,0,0",
                "deagle,revolver,elite:4,5,3,2,0,0,0", "mac10,mp9,mp7,ump45,mp5sd:6,5,3,2,0,0,0",
                "mag7,xm1014:6,5,3,2,0,0,0", "p90,bizon:6,6,3,2,0,0,0", "galilar,famas:8,6,3,2,0,0,0",
                "nova,sawedoff:6,5,2,2,0,0,0", "ak47,m4a4,m4a1s:8,6,5,2,0,0,0", "aug,sg556:8,6,5,2,2,0,0",
                "ssg08:10,5,3,2,2,0,0", "awp:10,8,5,2,2,1,0", "scar20,g3sg1:10,8,6,2,2,1,0",
                "m249,negev:10,9,6,2,0,1,0", "taser:12,9,9,2,0,6,12"];
            var expected = groups.SelectMany(g => g.Split(':')[0].Split(',').Select(n => (n, costs: g.Split(':')[1].Split(',').Select(int.Parse).ToArray()))).ToDictionary(p => p.n, p => p.costs);
            var craft = mod.GetType("Game.ScWeaponCrafting");
            var guns = ((Array)craft.GetField("All").GetValue(null)).Cast<object>().Where(e => !(bool)e.GetType().GetProperty("Knife").GetValue(e)).ToArray();
            Check("economy/35-models", guns.Length == 35 && expected.Count == 35);
            foreach (var gun in guns) {
                var t = gun.GetType(); string name = (string)t.GetProperty("Name").GetValue(gun); int[] want = expected[name];
                int[] actual = new[] { "Level", "B", "M", "H", "O", "Diamond", "Germanium" }.Select(f => (int)t.GetProperty(f).GetValue(gun)).ToArray();
                Check("economy/assembly/" + name, actual.SequenceEqual(want), string.Join(',', actual));
                var repair = mod.GetType("Game.ScWeaponRepair");
                foreach (int missing in new[] { 0, 1, 499, 500, 999, 1000 }) {
                    var costs = (Dictionary<int, int>)repair.GetMethod("Cost").Invoke(null, [gun, 1000 - missing, 1000]);
                    int b = (((want[1] + 1) / 2) * missing + 999) / 1000, m = (((want[2] + 1) / 2) * missing + 999) / 1000;
                    Check($"economy/repair/{name}/{missing}", costs.GetValueOrDefault(0) == b && costs.GetValueOrDefault(1) == m && costs.Keys.All(k => k is 0 or 1));
                }
            }
            var material = (Block)Activator.CreateInstance(mod.GetType("Game.ScWeaponMaterialBlock"));
            Check("economy/no-cheap-grid-components", !material.GetProceduralCraftingRecipes().Any());
        });
        Section("legacy-growth", () => {
            var rt = mod.GetType("Game.ScGunRegistry");
            // Public-beta Lv10/19/20/29/30 (rules 2, schema 4) converted to the fifty-level curve: the displayed
            // kills survive, the applied level is the power-equivalent one, and a half charge keeps its fraction.
            foreach (var (level, kills, gl, d, m, c, rc) in new[] {
                (10, 1050L, 38, 64, 214, 0.8, 1.6),
                (19, 1950L, 49, 74, 247, 0.525, 1.05),
                (20, 2000L, 49, 74, 247, 0.525, 1.05),
                (29, 2950L, 50, 75, 250, 0.5, 1.0),
                (30, 3000L, 50, 75, 250, 0.5, 1.0) }) {
                var rows = new ValuesDictionary();
                // Half-charged legacy Zeus. Raw kills and granted levels must survive; no refill or repair.
                double cycle = level <= 10 ? 5 : level <= 20 ? 5 - (level - 10) * .25 : 2.5 - (level - 20) * .15;
                string row = FormattableString.Invariant($"v=34,r=0,s=0,d=75,m=250,n=7,c={cycle / 2},p=0,ct=1,k={kills},gl={level},gp=-1,gv=2,rc={cycle},ov=0");
                rows.SetValue("1", row); var source = new ValuesDictionary(); source.SetValue("Schema", 4); source.SetValue("Next", 2); source.SetValue("GrowthMode", "CountAndGrow"); source.SetValue("Records", rows);
                object registry = rt.GetMethod("Load").Invoke(null, [source, 0d]);
                for (int round = 0; round < 3; round++) {
                    var saved = (ValuesDictionary)rt.GetMethod("Save").Invoke(registry, [0d]);
                    var fields = saved.GetValue<ValuesDictionary>("Records").GetValue<string>("1").Split(',').Select(x => x.Split('=')).ToDictionary(x => x[0], x => x[1]);
                    Check($"legacy-growth/state/{level}/{round}", fields["k"] == kills.ToString() && fields["gl"] == gl.ToString()
                        && fields["kc"] == "0" && fields["d"] == d.ToString() && fields["m"] == m.ToString() && fields["r"] == "0" && fields["n"] == "7"
                        && Math.Abs(double.Parse(fields["c"], System.Globalization.CultureInfo.InvariantCulture) - c) < 1e-5
                        && Math.Abs(double.Parse(fields["rc"], System.Globalization.CultureInfo.InvariantCulture) - rc) < 1e-5,
                        saved.GetValue<ValuesDictionary>("Records").GetValue<string>("1"));
                    var xml = new XElement("Values"); saved.Save(xml); var read = new ValuesDictionary(); read.ApplyOverrides(XElement.Parse(xml.ToString()));
                    registry = rt.GetMethod("Load").Invoke(null, [read, 0d]);
                }
                Check("legacy-growth/source-unchanged/" + level, rows.GetValue<string>("1") == row && source.GetValue<int>("Schema") == 4);
            }
            foreach (string bad in new[] { "99", "-1", "CountAndGrow,CountOnly" }) {
                var d = new ValuesDictionary(); d.SetValue("Schema", 5); d.SetValue("GrowthMode", bad);
                var r = rt.GetMethod("Load").Invoke(null, [d, 0d]);
                Check("legacy-growth/invalid-mode/" + bad, (bool)rt.GetProperty("Disabled").GetValue(r) && ReferenceEquals(rt.GetMethod("Save").Invoke(r, [0d]), d));
            }
        });
        Section("staged-growth", () => {
            var rt=mod.GetType("Game.ScGunRegistry");
            // Schema 5 (rules 3) rows: real kills, the old credit and the half charge all survive the conversion.
            foreach(var (kills,oldCredit,level,gl,d,m,c,rc) in new[]{
                (1050L,25L,10,38,64,214,0.8,1.6),
                (1000L,1500L,20,37,63,211,0.825,1.65),
                (1050L,1450L,20,38,64,214,0.8,1.6),
                (3000L,1500L,30,50,75,250,0.5,1.0)}) {
                var source=new ValuesDictionary();source.SetValue("Schema",5);source.SetValue("GrowthMode","CountAndGrow");source.SetValue("Next",2);
                var rows=new ValuesDictionary();rows.SetValue("1",$"v=34,r=0,s=0,d=75,m=250,n=7,c=1,p=0,ct=1,k={kills},gl={level},gp=-1,gv=3,rc=2,ov=0,kc={oldCredit}");source.SetValue("Records",rows);
                var registry=rt.GetMethod("Load").Invoke(null,[source,0d]);
                for(int round=0;round<2;round++) {
                    var save=(ValuesDictionary)rt.GetMethod("Save").Invoke(registry,[0d]);
                    var fields=save.GetValue<ValuesDictionary>("Records").GetValue<string>("1").Split(',').Select(x=>x.Split('=')).ToDictionary(x=>x[0],x=>x[1]);
                    Check($"staged-growth/credit-charge/{kills}/{oldCredit}/{round}",fields["k"]==kills.ToString()&&fields["gl"]==gl.ToString()
                        &&fields["kc"]=="0"&&fields["d"]==d.ToString()&&fields["m"]==m.ToString()&&fields["r"]=="0"&&fields["n"]=="7"
                        &&Math.Abs(double.Parse(fields["c"],System.Globalization.CultureInfo.InvariantCulture)-c)<1e-5
                        &&Math.Abs(double.Parse(fields["rc"],System.Globalization.CultureInfo.InvariantCulture)-rc)<1e-5);
                    var xml=new XElement("Values");save.Save(xml);var reload=new ValuesDictionary();reload.ApplyOverrides(XElement.Parse(xml.ToString()));registry=rt.GetMethod("Load").Invoke(null,[reload,0d]);
                }
            }
        });
        Section("logistics", () => {
            var a = dlls.Load("Logistics"); Check("logistics/actual-dll", true, dlls.Hash("Logistics"));
            Type vaultType = a.GetType("Logistics.StorageVault", true), unitType = a.GetType("Logistics.ComponentStorageUnit", true), systemType = a.GetType("Logistics.SubsystemStorageVaults", true);
            object system = Activator.CreateInstance(systemType); var vaults = (IDictionary)systemType.GetField("m_vaults", Fields).GetValue(system);
            Guid id = Guid.NewGuid(); object vault = Activator.CreateInstance(vaultType, [id, 64]); vaults[id] = vault;
            var project = new Project(); project.m_subsystems.Add((Subsystem)system);
            var units = Enumerable.Range(0, 7).Select(i => {
                var unit = (IInventory)RuntimeHelpers.GetUninitializedObject(unitType); unitType.GetField("m_subsystemStorageVaults", Fields).SetValue(unit, system); unitType.GetProperty("VaultGuid").SetValue(unit, id);
                var entity = Blank<Entity>(); entity.m_project = project; entity.m_components = [(Component)unit]; ((Component)unit).m_entity = entity; project.m_entities[entity] = true; return unit;
            }).ToArray();
            var key = mod.GetType("Game.ScGunHolders").GetMethod("Key"); string Key(IInventory inv, int slot) => (string)key.Invoke(null, [inv, slot]);
            Check("logistics/seven-entry-canonical-key", units.Select(u => Key(u, 0)).Distinct().Count() == 1);
            // Set a real third-party slot directly for fixture setup; all reads/scans are production DLL methods.
            var slots = (IList)vaultType.GetField("m_slots", Fields).GetValue(vault); object slot = slots[0]; Set(slot, "Value", Terrain.MakeBlockValue(512, 0, 64)); Set(slot, "Count", 1); slots[0] = slot;
            var scan = mod.GetType("Game.ScGunHolders").GetMethod("Scan");
            int Scan() => ((IEnumerable)scan.Invoke(null, [project, 512])).Cast<object>().Count();
            Check("logistics/actual-world-scan-one-holder", Scan() == 1);
            var timings = new List<double>(); for (int i = 0; i < 210; i++) { long start = Stopwatch.GetTimestamp(); int count = Scan(); if (i >= 10) timings.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); if (count != 1) throw new InvalidOperationException("holder count changed"); }
            timings.Sort(); Check("logistics/scan-p50-p95", true, $"7 real entries x {units[0].SlotsCount} slots; 200 measured scans p50={timings[99]:F4}ms p95={timings[189]:F4}ms max={timings[^1]:F4}ms; offline scanner only");
            Guid second = Guid.NewGuid(); object other = Activator.CreateInstance(vaultType, [second, 1]); vaults[second] = other;
            unitType.GetProperty("VaultGuid").SetValue(units[0], second);
            Check("logistics/split-remaps-immediately", Key(units[0], 0) != Key(units[1], 0));
            unitType.GetProperty("VaultGuid").SetValue(units[0], id);
            Check("logistics/merge-remaps-immediately", Key(units[0], 0) == Key(units[1], 0));
            for (int round = 0; round < 2; round++) {
                var data = new ValuesDictionary(); vaultType.GetMethod("Write").Invoke(vault, [data]); var xml = new XElement("Values"); data.Save(xml);
                var read = new ValuesDictionary(); read.ApplyOverrides(XElement.Parse(xml.ToString())); vault = vaultType.GetMethod("Read").Invoke(null, [read]); vaults[id] = vault;
                Check("logistics/vault-xml-reload/" + round, Scan() == 1 && units.All(u => u.GetSlotValue(0) == Terrain.MakeBlockValue(512, 0, 64)));
            }
        });
        Section("subnautica", () => {
            var a = dlls.Load("sc-subnautica-mod"); Check("subnautica/actual-dll", true, dlls.Hash("sc-subnautica-mod"));
            var healthType = a.GetType("Subnautica.ComponentHealthExt", true); var immunityType = a.GetType("Subnautica.ComponentImmunity", true);
            var apply = mod.GetType("Game.ScProjectileDefense").GetMethod("Apply");
            var loader = (ModLoader)RuntimeHelpers.GetUninitializedObject(a.GetType("Subnautica.SubnauticaLoader", true));
            foreach (var (name, interval, multiplier, cap) in new[] { ("GiantTurtle", .2, .5f, 1f), ("Kraken", .7, .8f, .02f), ("BlueWhale", 4d, .8f, .025f) }) {
                var entity = Blank<Entity>(); var database = Blank<DatabaseObject>(); database.m_name = name; entity.m_valuesDictionary = new ValuesDictionary { DatabaseObject = database };
                var body = Blank<ComponentBody>(); body.m_entity = entity; var health = Blank<ComponentHealth>(); health.m_entity = entity;
                var ext = (Component)RuntimeHelpers.GetUninitializedObject(healthType); ext.m_entity = entity;
                var immune = (Component)RuntimeHelpers.GetUninitializedObject(immunityType); immune.m_entity = entity;
                entity.m_components = [body, health, ext, immune]; var time = new SubsystemTime { m_gameTime = 10 };
                Set(ext, "m_subsystemTime", time); Set(ext, "ProjectileInjureInterval", interval); Set(ext, "m_lastProjectileTime", 0d);
                Attackment Hit(float power = 100) { var hit = new ProjectileAttackment(body, null, Vector3.Zero, Vector3.UnitX, power, null); apply.Invoke(null, [body, hit]); return hit; }
                Check("subnautica/reduction/" + name, Math.Abs(Hit().AttackPower - 100 * multiplier) < .001f);
                Check("subnautica/interval-block/" + name, Hit().AttackPower == 0);
                time.m_gameTime += interval + .001;
                Check("subnautica/interval-release/" + name, Hit().AttackPower > 0);
                Check("subnautica/shares-clock-with-native-projectiles/" + name, !(bool)healthType.GetMethod("CanProjectileAttack").Invoke(ext, null));
                // Invoke the original generic injury hook, independently of our interval adapter.
                var injury = Blank<Injury>(); injury.ComponentHealth = health; injury.Amount = .9f; loader.CalculateCreatureInjuryAmount(injury);
                Check("subnautica/original-generic-cap/" + name, Math.Abs(injury.Amount - Math.Min(.9f, cap)) < .00001f);
                if (name == "GiantTurtle") { Set(immune, "m_immunityTimer", 2f); time.m_gameTime += 1; Check("subnautica/turtle-immunity", Hit().AttackPower == 0); }
                if (name == "Kraken") { time.m_gameTime += 1; Check("subnautica/kraken-low-power-rule", Hit(5).AttackPower == 1); }
            }
        });
        return results;
    }
}
