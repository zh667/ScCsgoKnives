using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

// Loads the user's actual Sushi assemblies. No replacement Sushi classes: the field/property
// contract and slot forwarding must come from the third-party DLLs that exhibited the failure.
static class SushiInventoryRegression {
    internal record Result(string Name, bool Ok, string Detail);
    sealed class Inventory : IInventory {
        public readonly int[] Values = new int[12], Counts = new int[12];
        public Project Project => null;
        public int SlotsCount => 12;
        public int VisibleSlotsCount { get; set; } = 12;
        public int ActiveSlotIndex { get; set; }
        public int GetSlotValue(int i) => Values[i];
        public int GetSlotCount(int i) => Counts[i];
        public int GetSlotCapacity(int i, int v) => 100;
        public int GetSlotProcessCapacity(int i, int v) => 0;
        public void AddSlotItems(int i, int v, int n) { if (Counts[i] > 0 && Values[i] != v) throw new InvalidOperationException(); Values[i] = v; Counts[i] += n; }
        public int RemoveSlotItems(int i, int n) { n = Math.Min(n, Counts[i]); Counts[i] -= n; return n; }
        public void ProcessSlotItems(int i, int v, int count, int process, out int result, out int resultCount) { result = v; resultCount = 0; }
        public void DropAllItems(Vector3 p) => Array.Clear(Counts);
    }
    internal static List<Result> Run(Assembly mod, string modsDirectory) {
        List<Result> results = [];
        void Check(string name, bool ok, string detail = "") => results.Add(new("sushi-inventory/" + name, ok, detail));
        var registryType = mod.GetType("Game.ScGunRegistry"); var current = registryType.GetField("Current"); object saved = current.GetValue(null);
        var mutation = mod.GetType("Game.ScGunMutation"); var locator = mutation.GetField("HolderLocator"); object oldLocator = locator.GetValue(null);
        try {
            var dlls = new Dictionary<string, byte[]>();
            foreach (string path in Directory.EnumerateFiles(modsDirectory, "*.scmod")) {
                System.IO.Compression.ZipArchive opened;
                try { opened = ZipFile.OpenRead(path); }
                catch (InvalidDataException) {
                    using var source = File.OpenRead(path);
                    opened = new System.IO.Compression.ZipArchive(ModsManager.GetDecipherStream(source), ZipArchiveMode.Read);
                }
                using var zip = opened;
                foreach (var entry in zip.Entries.Where(e => Path.GetFileName(e.FullName) is "SushiBase.dll" or "SushiTool.dll")) {
                    using var stream = entry.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                    dlls.Add(Path.GetFileName(entry.FullName), bytes.ToArray());
                }
            }
            Assembly Load(string name) { using var bytes = new MemoryStream(dlls[name]); return AssemblyLoadContext.Default.LoadFromStream(bytes); }
            var sushiBase = Load("SushiBase.dll"); var sushiTool = Load("SushiTool.dll");
            Check("actual-dlls", true, string.Join("; ", dlls.Select(d => d.Key + " SHA256=" + Convert.ToHexString(SHA256.HashData(d.Value)))));
            results.AddRange(SushiSyncInventoryRegression.Run(mod, sushiBase, sushiTool));
            var total = RuntimeHelpers.GetUninitializedObject(sushiBase.GetType("Sushi.SubsystemSushiTotal", true));
            var miner = (ComponentMiner)RuntimeHelpers.GetUninitializedObject(typeof(ComponentMiner));
            var inventory = new Inventory(); miner.Inventory = inventory;
            total.GetType().GetField("ComponentMiner").SetValue(total, miner);
            var person = sushiTool.GetType("Sushi.ComponentSushiPersonBox", true);
            var aliases = Enumerable.Range(0, 6).Select(_ => {
                var proxy = (IInventory)RuntimeHelpers.GetUninitializedObject(person);
                person.GetField("m_SubsystemSushiTotal").SetValue(proxy, total); return proxy;
            }).Prepend(inventory).ToArray();
            var key = mod.GetType("Game.ScGunHolders").GetMethod("Key");
            string Key(object obj, int slot = 0) => (string)key.Invoke(null, [obj, slot]);
            Check("engine-inventory-is-property", typeof(ComponentMiner).GetProperty("Inventory") != null && typeof(ComponentMiner).GetField("Inventory") == null);
            Check("player-plus-six-person-boxes-one-holder", aliases.Select(a => Key(a)).Distinct().Count() == 1);
            Check("different-slots-remain-distinct", Key(aliases[1], 0) != Key(aliases[2], 1));
            var project = new Project(); var players = new SubsystemPlayers(); project.m_subsystems.Add(players);
            var player = (ComponentPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ComponentPlayer));
            player.ComponentMiner = miner; player.PlayerData = (PlayerData)RuntimeHelpers.GetUninitializedObject(typeof(PlayerData)); player.PlayerData.PlayerIndex = 7;
            players.m_componentPlayers.Add(player);
            var owner = mod.GetType("Game.ScGunHolders").GetMethod("RecoveryOwner");
            Check("all-proxies-use-durable-player-recovery", aliases.All(a => (string)owner.Invoke(null, [project, a]) == "player/7"));
            Check("recovery-resolves-to-current-player-inventory", ReferenceEquals(mod.GetType("Game.ScGunHolders").GetMethod("ResolveRecoveryOwner").Invoke(null, [project, "player/7"]), inventory));
            var epochs = mod.GetType("Game.ScInventoryTransaction"); epochs.GetMethod("Changed").Invoke(null, [aliases[1]]);
            Check("proxy-and-player-share-revision", aliases.All(a => (long)epochs.GetMethod("Revision").Invoke(null, [a]) == 1));

            var registry = Activator.CreateInstance(registryType); current.SetValue(null, registry);
            registryType.GetField("RecoveryOwner").SetValue(registry, (Func<IInventory, string>)(_ => "player/test"));
            var allocate = registryType.GetMethod("Allocate");
            int id = (int)allocate.Invoke(registry, [0, 19, false, 700, 2250, 0]);
            var spec = mod.GetType("Game.GunSpec");
            int value = Terrain.MakeBlockValue(512, 0, (int)spec.GetMethod("WithId").Invoke(null, [0, id]));
            inventory.AddSlotItems(0, value, 1);
            Check("actual-proxies-forward-slots", aliases.All(a => a.GetSlotValue(0) == value && a.GetSlotCount(0) == 1));
            Func<int, string, IEnumerable<string>> locate = (rid, except) => aliases
                .Where(a => a.GetSlotCount(0) > 0 && (int)spec.GetMethod("GetId").Invoke(null, [Terrain.ExtractData(a.GetSlotValue(0))]) == rid)
                .Select(a => Key(a)).Distinct().Where(k => k != except).ToArray();
            locator.SetValue(null, locate);
            var record = registryType.GetMethod("Get", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(registry, [id]);
            var rt = record.GetType(); var p = System.Linq.Expressions.Expression.Parameter(rt);
            var action = System.Linq.Expressions.Expression.Lambda(typeof(Action<>).MakeGenericType(rt), System.Linq.Expressions.Expression.Empty(), p).Compile();
            bool stable = true;
            for (int i = 0; i < 210; i++) {
                IInventory a = aliases[i % aliases.Length]; object[] args = [a, 0, Key(a), null];
                var tx = mutation.GetMethod("Prepare").Invoke(null, args);
                stable &= tx != null && mutation.GetMethod("Commit").Invoke(tx, [action, 0, 0, null]).ToString() == "Success";
            }
            Check("210-transactions-no-phantom-allocation", stable && (int)registryType.GetProperty("Next").GetValue(registry) == 2);
            // Reproduce the already damaged world's capacity without reclaiming a single old ID.
            while (!(bool)registryType.GetProperty("IsFull").GetValue(registry)) allocate.Invoke(registry, [0, 19, false, 700, 2250, 0]);
            string Apply(string typeName, object quote) => mod.GetType(typeName).GetMethod("Apply").Invoke(null, [inventory, quote, Key(inventory)]).ToString();
            var counter = mod.GetType("Game.ScGunCounter");
            var counterQuote = counter.GetMethod("Prepare").Invoke(null, [inventory, 0, true]);
            string counterResult = Apply("Game.ScGunCounter", counterQuote);
            Check("full-table-existing-gun-counter", counterResult == "Success", counterResult);
            var skins = (Array)mod.GetType("Game.ScGunSkinCatalog").GetField("All").GetValue(null);
            var fits = mod.GetType("Game.ScGunSkinCatalog").GetMethod("Fits");
            object skin = skins.Cast<object>().First(s => (bool)fits.Invoke(null, [s, 0]));
            var skinning = mod.GetType("Game.ScWeaponSkinning");
            object Quote() => skinning.GetMethod("Prepare").Invoke(null, [inventory, 0, skin, true, (Func<int, int>)(i => i)]);
            string skinResult = Apply("Game.ScWeaponSkinning", Quote());
            Check("full-table-existing-gun-skin", skinResult == "Success", skinResult);
            Check("original-ammo-durability-id-preserved", inventory.GetSlotValue(0) == value && (int)rt.GetField("Rounds").GetValue(record) == 19
                && (int)rt.GetField("Durability").GetValue(record) == 700 && (int)registryType.GetProperty("Next").GetValue(registry) == 1023);
            // A real extra holder still fails at capacity; alias support must not turn duplication protection off.
            locator.SetValue(null, (Func<int, string, IEnumerable<string>>)((rid, except) => locate(rid, except).Append("independent-copy:0")));
            object[] duplicateArgs = [inventory, 0, Key(inventory), null];
            var duplicate = mutation.GetMethod("Prepare").Invoke(null, duplicateArgs);
            Check("full-table-real-copy-still-refused", mutation.GetMethod("Commit").Invoke(duplicate, [action, 0, 0, null]).ToString() == "DuplicateUnresolved");
            locator.SetValue(null, locate);
            // Actual engine Inventory property can change when the mode/player changes; never cache its value.
            var otherInventory = new Inventory(); miner.Inventory = otherInventory;
            Check("person-box-remap-follows-engine-property", Key(aliases[1]) == Key(otherInventory) && Key(aliases[1]) != Key(inventory));
            miner.Inventory = inventory;
            for (int round = 0; round < 2; round++) {
                var data = (ValuesDictionary)registryType.GetMethod("Save").Invoke(registry, [0d]); var xml = new XElement("Values"); data.Save(xml);
                var read = new ValuesDictionary(); read.ApplyOverrides(XElement.Parse(xml.ToString()));
                registry = registryType.GetMethod("Load").Invoke(null, [read, 0d]); current.SetValue(null, registry);
                record = registryType.GetMethod("Get", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(registry, [id]);
                Check("full-table-xml-roundtrip/" + round, (int)registryType.GetProperty("Next").GetValue(registry) == 1023
                    && (int)rt.GetField("Rounds").GetValue(record) == 19 && (int)rt.GetField("Durability").GetValue(record) == 700
                    && (bool)rt.GetField("CounterInstalled").GetValue(record) && (int)rt.GetField("SkinId").GetValue(record) == (int)skin.GetType().GetProperty("PaintId").GetValue(skin));
            }
            // A full AUG magazine through the six actual proxy objects. This is
            // inventory/selection verification, not a rendered combat session.
            var aug = spec.GetMethod("ForAsset").Invoke(null,["aug"]);
            int augVariant = Array.IndexOf((Array)spec.GetField("All").GetValue(null),aug);
            rt.GetField("Variant").SetValue(record,augVariant);rt.GetField("SkinId").SetValue(record,0);rt.GetField("Rounds").SetValue(record,30);
            value = Terrain.MakeBlockValue(512, 0, (int)spec.GetMethod("WithId").Invoke(null, [augVariant, id])); inventory.Values[0]=value;
            var selection = Activator.CreateInstance(mod.GetType("Game.ScHeldWeaponSelection"));
            var observe=selection.GetType().GetMethod("Observe");observe.Invoke(selection,[inventory,0,value,true]);
            var decrease=System.Linq.Expressions.Expression.Lambda(typeof(Action<>).MakeGenericType(rt),
                System.Linq.Expressions.Expression.Block(System.Linq.Expressions.Expression.PostDecrementAssign(System.Linq.Expressions.Expression.Field(p,"Rounds")),System.Linq.Expressions.Expression.Empty()),p).Compile();
            bool magazine=true;
            for(int shot=0;shot<30;shot++) {
                var proxy=aliases[shot%aliases.Length];object[] args=[proxy,0,Key(proxy),null];
                var tx=mutation.GetMethod("Prepare").Invoke(null,args);
                magazine &= tx is not null && mutation.GetMethod("Commit").Invoke(tx,[decrease,0,0,null]).ToString()=="Success"
                    && !(bool)observe.Invoke(selection,[inventory,0,inventory.GetSlotValue(0),true]);
            }
            Check("aug-30-rounds-through-six-boxes-no-redeploy-at-full-table",magazine && (int)rt.GetField("Rounds").GetValue(record)==0
                && (int)registryType.GetProperty("Next").GetValue(registry)==1023 && (bool)spec.GetField("Automatic").GetValue(aug));
            var input=(ComponentInput)RuntimeHelpers.GetUninitializedObject(typeof(ComponentInput));player.ComponentInput=input;
            input.m_playerInput=new PlayerInput{Dig=new Ray3(Vector3.Zero,Vector3.UnitZ)};
            var loader=(ModLoader)RuntimeHelpers.GetUninitializedObject(sushiBase.GetType("Sushi.SushiBaseLoader",true));
            bool unchanged=true;
            for(int frame=0;frame<120;frame++) {
                bool operated=false;double interval=.3;loader.UpdatePlayerInputDig(player,true,ref operated,ref interval,false,out bool skip);
                unchanged &= !operated&&!skip&&interval==.3&&input.PlayerInput.Dig.HasValue;
            }
            Check("actual-sushi-dig-hook-120-held-frames-preserves-aug-input",unchanged,
                "Actual SushiBase hook and real proxy inventory; does not reproduce the complete placed-block world or Android input pipeline.");
        } catch (Exception e) { Check("exception", false, e.ToString()); }
        finally { current.SetValue(null, saved); locator.SetValue(null, oldLocator); }
        return results;
    }
}
