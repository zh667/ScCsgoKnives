using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;
using GameEntitySystem;

static class SwitchAnimationRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod) {
        List<Result> results = [];
        void Test(string name, Func<bool> test) { try { results.Add(new("animation-switch/" + name, test(), name)); } catch (Exception e) { results.Add(new("animation-switch/" + name, false, e.ToString())); } }
        var ctrl = mod.GetType("Game.KnifeAnimationController"); var rig = mod.GetType("Game.CsmcKnifeRig"); var clock = mod.GetType("Game.KnifeClock");
        var savedTypes = new Dictionary<Type, int>(BlocksManager.BlockTypeToIndex); var savedNames = new Dictionary<string, int>(BlocksManager.BlockNameToIndex);
        bool oldVirtual = (bool)clock.GetField("Virtual").GetValue(null); double oldTime = (double)clock.GetField("VirtualNow").GetValue(null);
        float oldVolume = SettingsManager.SoundsVolume;
        T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        void Time(double now) => clock.GetField("VirtualNow").SetValue(null, now);
        float Duration(int variant, string alias) => (float)rig.GetMethod("GetProfileDuration").Invoke(null, [variant, alias]);
        int Value(int v) => v < 22 ? Terrain.MakeBlockValue(700, 0, v) : v < 57 ? Terrain.MakeBlockValue(701, 0, (int)mod.GetType("Game.GunSpec").GetMethod("MakeData").Invoke(null, [v - 22, 0, false])) : Terrain.MakeBlockValue(702, 0, v - 57);
        string Clip(object pose) => (string)pose.GetType().GetProperty("ClipAlias").GetValue(pose);
        float SampleTime(object pose) => (float)pose.GetType().GetProperty("RequestedTime").GetValue(pose);
        try {
            clock.GetField("Virtual").SetValue(null, true); SettingsManager.SoundsVolume = 0;
            string[] names = ["ScKnifeBlock", "ScGunBlock", "ScGrenadeBlock"];
            for (int i = 0; i < 3; i++) { BlocksManager.BlockTypeToIndex[mod.GetType("Game." + names[i])] = 700 + i; BlocksManager.BlockNameToIndex[names[i]] = 700 + i; }
            var gui = Blank<ComponentGui>(); gui.m_modalPanelContainerWidget = new CanvasWidget();
            var gameWidget = Blank<GameWidget>(); gameWidget.GuiWidget = new CanvasWidget(); var data = Blank<PlayerData>(); data.m_gameWidget = gameWidget;
            var player = Blank<ComponentPlayer>(); player.ComponentGui = gui; player.PlayerData = data; player.ComponentMiner = Blank<ComponentMiner>();
            var inventory = new ComponentCreativeInventory { OpenSlotsCount = 10 }; inventory.m_slots.Add(0); player.ComponentMiner.Inventory = inventory;
            var entity = Blank<Entity>(); player.m_entity = entity;
            (ComponentFirstPersonModel Model, object State) Setup(int v, string action, string alias, bool menu) {
                Time(0); inventory.m_slots[0] = Value(v);
                gui.m_modalPanelContainerWidget.Children.Clear(); if (menu) gui.m_modalPanelContainerWidget.Children.Add(new CanvasWidget());
                var model = Blank<ComponentFirstPersonModel>(); model.m_componentPlayer = player; entity.m_components = [model];
                var state = ctrl.GetMethod("StateFor", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, [model]); var t = state.GetType();
                t.GetField("Variant").SetValue(state, v); t.GetField("ClipAlias").SetValue(state, alias);
                t.GetField("Action").SetValue(state, Enum.Parse(t.GetField("Action").FieldType, action));
                t.GetField("Pose").SetValue(state, rig.GetMethod("Sample").Invoke(null, [v, alias, 0f, false]));
                return (model, state);
            }
            object Update(ComponentFirstPersonModel model, int v) => ctrl.GetMethod("Update").Invoke(null, [model, Value(v)]);
            foreach (int gun in new[] { 22, 23, 24 }) Test("same-model-slot-deploy/" + gun, () => {
                inventory.ActiveSlotIndex = 0;
                var (model, state) = Setup(gun, "Inspect", "inspect", true);
                Update(model, gun);
                if (inventory.m_slots.Count < 2) inventory.m_slots.Add(Value(gun)); else inventory.m_slots[1] = Value(gun);
                inventory.ActiveSlotIndex = 1; Time(.2);
                var pose = Update(model, gun);
                inventory.ActiveSlotIndex = 0;
                return Clip(pose).StartsWith("deploy") && SampleTime(pose) == 0;
            });
            Test("instance-not-ammo-or-light", () => {
                var type = mod.GetType("Game.ScHeldWeaponSelection"); var observer = Activator.CreateInstance(type);
                bool Observe(object inv, int slot, int value) => (bool)type.GetMethod("Observe").Invoke(observer, [inv, slot, value, true]);
                int fresh = Value(23), data = Terrain.ExtractData(fresh), instance = (data & ~(1023 << 6)) | (12 << 6);
                int v = Terrain.MakeBlockValue(701, 0, instance);
                if (Observe(inventory,0,fresh) || Observe(inventory,0,v) || Observe(inventory,0,Terrain.MakeBlockValue(701, 8, instance))) return false;
                if (!Observe(inventory,0,Terrain.MakeBlockValue(701,0,(instance & ~(1023 << 6)) | (13 << 6)))) return false;
                return Observe(new object(),0,v);
            });
            Test("cancel-old-reload-preserves-new-deploy", () => {
                inventory.ActiveSlotIndex = 0;
                var (model, state) = Setup(23,"Reload","reload",false); Update(model,23);
                var txType=mod.GetType("Game.ScReloadTransaction");
                var tx=Activator.CreateInstance(txType,[inventory,0,Value(23),901,0,20]);
                inventory.m_slots[1]=Value(23); inventory.ActiveSlotIndex=1; Time(.1);
                var pose=Update(model,23);
                var behaviorType=mod.GetType("Game.SubsystemScGunBlockBehavior");
                var gs=Activator.CreateInstance(behaviorType.GetNestedType("GunState",BindingFlags.NonPublic),true);
                gs.GetType().GetField("Reload").SetValue(gs,tx);
                behaviorType.GetMethod("CancelReload",BindingFlags.NonPublic|BindingFlags.Instance)
                    .Invoke(RuntimeHelpers.GetUninitializedObject(behaviorType),[player,gs,false]);
                bool ok=Clip(pose).StartsWith("deploy") && state.GetType().GetField("Action").GetValue(state).ToString()=="Draw"
                    && (bool)txType.GetProperty("Cancelled").GetValue(tx) && inventory.GetSlotValue(0)==Value(23) && inventory.GetSlotValue(1)==Value(23);
                inventory.ActiveSlotIndex=0; return ok;
            });
            for (int v = 0; v < 63; v++) foreach (string alias in new[] { "deploy", "inspect" }) {
                int variant = v; float duration = Duration(v, alias); if (duration <= 0) continue;
                Test($"menu-advances/{variant}/{alias}", () => {
                    var (model, state) = Setup(variant, alias == "deploy" ? "Draw" : "Inspect", alias, true);
                    foreach (float fraction in new[] { .2f, .55f }) {
                        float at = duration * fraction; Time(at); var pose = Update(model, variant);
                        if (pose is null || Clip(pose) != alias || Math.Abs(SampleTime(pose) - at) > .001) return false;
                    }
                    gui.m_modalPanelContainerWidget.Children.Clear(); Time(duration * .6);
                    var closed = Update(model, variant);
                    if (Clip(closed) != alias || Math.Abs(SampleTime(closed) - duration * .6) > .001) return false;
                    Time(duration + .01); Update(model, variant);
                    return state.GetType().GetField("Action").GetValue(state).ToString() == "Idle";
                });
                if (alias == "deploy") Test($"menu-switch-starts-deploy/{variant}", () => {
                    var (model, state) = Setup((variant + 1) % 63, "Inspect", "inspect", true);
                    inventory.m_slots[0] = Value(variant); Time(.03); var pose = Update(model, variant);
                    return pose is not null && Clip(pose).StartsWith("deploy") && SampleTime(pose) == 0
                        && (int)state.GetType().GetField("Variant").GetValue(state) == variant;
                });
            }
            void QuickSwitch(int gun, int knife, bool scoped, bool heavy) {
                Test($"shot-to-knife/{gun}/{knife}/{scoped}/{heavy}", () => {
                    var (model, state) = Setup(gun, "Shoot", "shoot1", false); var type = state.GetType(); type.GetField("Scoped").SetValue(state, scoped);
                    inventory.m_slots[0] = Value(knife); Time(.03);
                    bool Attack() => (bool)ctrl.GetMethod("TriggerKnifeAttack").Invoke(null, [player, heavy]);
                    if (Attack() || (int)type.GetField("Variant").GetValue(state) != gun) return false;
                    var pose = Update(model, knife); string deploy = Clip(pose);
                    if (!deploy.StartsWith("deploy") || (bool)type.GetField("Scoped").GetValue(state) || Attack()) return false;
                    Time(.03 + Duration(knife, deploy) + .01); Update(model, knife);
                    return Attack(); // Legitimate knife attacks still work after deploying.
                });
            }
            for (int gun = 22; gun < 57; gun++) foreach (bool scoped in new[] { false, true }) QuickSwitch(gun, 2, scoped, false);
            for (int knife = 0; knife < 22; knife++) foreach (bool heavy in new[] { false, true }) QuickSwitch(24, knife, true, heavy);
            Test("cz-detach-persists-across-equip-but-not-another-gun",()=> {
                var registryType=mod.GetType("Game.ScGunRegistry");var current=registryType.GetField("Current");var old=current.GetValue(null);
                try {
                    var registry=Activator.CreateInstance(registryType);current.SetValue(null,registry);
                    registryType.GetMethod("Allocate").Invoke(registry,[15,4,false,400,1500,0]);
                    registryType.GetMethod("Allocate").Invoke(registry,[15,4,false,400,1500,0]);
                    const int variant=22+15;
                    var (model,state)=Setup(variant,"Reload","reload",false);
                    int value=Terrain.MakeBlockValue(701,0,(1<<6)|15);
                    inventory.ActiveSlotIndex=0;inventory.m_slots[0]=value;
                    object Step(double t,int item) {Time(t);return ctrl.GetMethod("Update").Invoke(null,[model,item]);}
                    Step(0,value);
                    float detach=(float)mod.GetType("Game.Cs2Rig").GetMethod("CzFrontDetachTime").Invoke(null,["reload"]);
                    Step(detach+.002,value);
                    bool Removed()=>(bool)ctrl.GetMethod("HideCzFront").Invoke(null,[model]);
                    if(!Removed())return false;
                    inventory.m_slots[0]=Value(23);Step(1,Value(23));inventory.m_slots[0]=value;Step(2,value);
                    if(!Removed()||(string)ctrl.GetMethod("ReloadForPlayer").Invoke(null,[player,variant,false])!="reloadFollowup")return false;
                    value=Terrain.MakeBlockValue(701,0,(2<<6)|15);inventory.m_slots[0]=value;Step(3,value);
                    return !Removed()&&(string)ctrl.GetMethod("ReloadForPlayer").Invoke(null,[player,variant,false])=="reload";
                }finally{current.SetValue(null,old);}
            });
            for (int v = 0; v < 63; v++) {
                int variant = v;
                Test($"inspect-after-menu-switch/{variant}", () => {
                    var (model, state) = Setup((variant + 1) % 63, "Shoot", "shoot1", true); inventory.m_slots[0] = Value(variant); Time(.03);
                    if (!(bool)ctrl.GetMethod("TriggerInspect").Invoke(null, [player])) return false;
                    string deploy = (string)state.GetType().GetField("ClipAlias").GetValue(state);
                    if (!deploy.StartsWith("deploy") || !(bool)state.GetType().GetField("PendingInspect").GetValue(state)) return false;
                    Time(.03 + Duration(variant, deploy) + .01); var pose = Update(model, variant);
                    return Clip(pose).StartsWith("inspect");
                });
            }
        } finally {
            clock.GetField("Virtual").SetValue(null, oldVirtual); clock.GetField("VirtualNow").SetValue(null, oldTime); SettingsManager.SoundsVolume = oldVolume;
            BlocksManager.BlockTypeToIndex.Clear(); foreach (var p in savedTypes) BlocksManager.BlockTypeToIndex[p.Key] = p.Value;
            BlocksManager.BlockNameToIndex.Clear(); foreach (var p in savedNames) BlocksManager.BlockNameToIndex[p.Key] = p.Value;
        }
        return results;
    }
}
