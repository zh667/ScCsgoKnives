using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Input;
using Game;
using GameEntitySystem;

static class ControlsScopeRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod) {
        List<Result> results=[];
        void Check(string name,bool ok,string detail="")=>results.Add(new("controls-scope-0416/"+name,ok,detail));
        var camera=mod.GetType("Game.ScScopeCamera");var bindings=mod.GetType("Game.ScGunBindings");
        var keys=(Dictionary<string,string>)bindings.GetField("Keys").GetValue(null);var oldKeys=new Dictionary<string,string>(keys);
        float view=SettingsManager.ViewAngle,sens=SettingsManager.LookSensitivity;
        try {
            foreach(float fov in new[]{.6f,1f,1.2f}) foreach(float zoom in new[]{1f,2f,4f,8f}) {
                SettingsManager.ViewAngle=fov;SettingsManager.LookSensitivity=.63f;
                var original=Matrix.CreatePerspectiveFieldOfView(MathUtils.DegToRad(80*fov),16f/9,.1f,2048f);
                var projected=(Matrix)camera.GetMethod("ZoomProjection").Invoke(null,[original,zoom]);
                float expected=1f/MathF.Tan(MathUtils.DegToRad(80*fov/zoom)*.5f);
                Check($"projection/{fov}/{zoom}",Math.Abs(projected.M22-expected)<.001f&&projected.M33==original.M33&&projected.M43==original.M43
                    &&SettingsManager.ViewAngle==fov&&SettingsManager.LookSensitivity==.63f,"only local M11/M22, globals and depth unchanged");
            }
            var behaviorType=mod.GetType("Game.SubsystemScGunBlockBehavior");var behavior=Activator.CreateInstance(behaviorType);
            var stateType=behaviorType.GetNestedType("GunState",BindingFlags.NonPublic);var state=Activator.CreateInstance(stateType,true);
            T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();player.m_entity=Blank<Entity>();player.m_entity.m_components=[];
            ((IDictionary)behaviorType.GetField("m_states",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(behavior))[player]=state;
            var specs=(Array)mod.GetType("Game.GunSpec").GetField("All").GetValue(null);
            var scopedSpec=specs.Cast<object>().First(s=>((float[])s.GetType().GetField("ZoomLevels").GetValue(s)).Length>0);
            foreach(string exit in new[]{"settings","pause","lost-focus","death","switch-item","travel","dispose"}) {
                behaviorType.GetMethod("SetZoom",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(behavior,[player,state,scopedSpec,1]);
                stateType.GetField("RescopeAt").SetValue(state,10d);
                behaviorType.GetMethod("SuspendScope").Invoke(behavior,[player]);
                Check("scope-boundary/"+exit,(int)stateType.GetField("Zoom").GetValue(state)==0&&(double)stateType.GetField("RescopeAt").GetValue(state)<0
                    &&SettingsManager.ViewAngle==1.2f&&SettingsManager.LookSensitivity==.63f,"shared boundary helper clears scope/re-scope; global settings untouched (not actual device transition)");
            }
            bindings.GetMethod("Reset").Invoke(null,null);
            Check("defaults",keys.Count==10&&keys["reload"]=="R"&&keys["inspect"]=="G"&&keys["fire"]=="");
            bool Valid(string k)=>(bool)bindings.GetMethod("Valid").Invoke(null,[k]);
            Check("key-validation",Valid("R")&&Valid("F8")&&Valid("")&&!Valid("Escape")&&!Valid("Null")&&!Valid("no-such-key"));
            bool Conflict(string a,string b)=>(bool)bindings.GetMethod("Conflict").Invoke(null,[a,b]);
            Check("conflict-groups",Conflict("fire","reload")&&Conflict("throw_strong","throw_weak")&&!Conflict("scope","silencer")&&!Conflict("reload","knife_heavy"));
            foreach(var key in Enum.GetValues<Key>()) {
                string label=(string)bindings.GetMethod("KeyLabel").Invoke(null,[key.ToString()]);
                Check("chinese-key/"+key,!string.IsNullOrEmpty(label)&&label!="未知按键"&&label.Any(c=>c>='\u4e00'&&c<='\u9fff'),label);
            }
            var selectable=(string[])bindings.GetMethod("SelectableKeys").Invoke(null,null);
            var expectedKeys=Enum.GetValues<Key>().Select(k=>k.ToString()).Where(Valid).ToArray();
            Check("all-supported-keys-selectable",selectable.Length==expectedKeys.Length&&selectable.Distinct().Count()==selectable.Length
                &&selectable.ToHashSet().SetEquals(expectedKeys)&&selectable[0]=="A"&&selectable[25]=="Z");
            Check("stable-action-identifiers",keys.Keys.ToHashSet().SetEquals(new[]{"fire","reload","scope","silencer","burst","revolver_alt","inspect","knife_heavy","throw_strong","throw_weak"}));
            var oldMapping=SettingsManager.KeyboardMappingSettings;
            try {
                SettingsManager.InitializeKeyboardMappingSettings();
                string Native(string id)=>(string)bindings.GetMethod("NativeBinding").Invoke(null,[id]);
                foreach(var id in new[]{"scope","silencer","burst","revolver_alt","knife_heavy","throw_weak"})
                    Check("native-right/"+id,Native(id)=="鼠标右键");
                foreach(var id in new[]{"fire","throw_strong"})Check("native-left/"+id,Native(id)=="鼠标左键");
                Check("keyboard-only-actions",Native("reload")==""&&Native("inspect")=="");
                SettingsManager.KeyboardMappingSettings.SetValue("Aim",Key.K);
                Check("native-remap-not-hardcoded",Native("scope")=="字母 K");
                string summary=(string)bindings.GetMethod("BindingSummary").Invoke(null,["scope","J"]);
                Check("native-and-extra-summary",summary.Contains("K")&&summary.Contains("J")&&!summary.Contains("未绑定"));
            }finally{SettingsManager.KeyboardMappingSettings=oldMapping;}
            var recovery=mod.GetType("Game.ScViewRecovery");
            SettingsManager.ViewAngle=.44444445f;SettingsManager.LookSensitivity=.22222222f;
            float oldSound=SettingsManager.SoundsVolume;
            recovery.GetMethod("ResetPreferences").Invoke(null,null);
            Check("recover-exact-user-values",SettingsManager.ViewAngle==1f&&SettingsManager.LookSensitivity==.5f&&SettingsManager.SoundsVolume==oldSound);
            bool Saved(string xml)=>(bool)recovery.GetMethod("SavedDefaults").Invoke(null,[System.Xml.Linq.XElement.Parse(xml)]);
            const string saved="<Settings><Value Name='ViewAngle' Value='1'/><Value Name='LookSensitivity' Value='0.5'/></Settings>";
            Check("recovery-save-verification",Saved(saved)&&!Saved(saved.Replace("Value='1'","Value='0.44444445'"))&&!Saved("<Settings/>")
                &&!Saved(saved.Replace("</Settings>","<Value Name='ViewAngle' Value='1'/></Settings>")));
            var windowState=typeof(Window).GetField("m_state",BindingFlags.Static|BindingFlags.NonPublic);
            var oldWindow=windowState.GetValue(null);var oldScreen=ScreensManager.CurrentScreen;var oldAnimation=ScreensManager.m_animationData;
            var oldRoot=ScreensManager.RootWidget;
            bool[] down=(bool[])typeof(Keyboard).GetField("m_keysDownArray",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).GetValue(null),
                once=(bool[])typeof(Keyboard).GetField("m_keysDownOnceArray",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).GetValue(null);
            bool oldR=down[(int)Key.R],oldJ=down[(int)Key.J],oldJOnce=once[(int)Key.J];
            var settings=mod.GetType("Game.ScUiSettings");bool oldButtons=(bool)settings.GetField("CustomButtons").GetValue(null);
            try {
                windowState.SetValue(null,Enum.Parse(windowState.FieldType,"Active"));ScreensManager.CurrentScreen=null;ScreensManager.m_animationData=null;
                ScreensManager.RootWidget=new CanvasWidget();
                player.ComponentHealth=new ComponentHealth {Health=1};player.ComponentGui=Blank<ComponentGui>();
                player.ComponentGui.m_modalPanelContainerWidget=new CanvasWidget();
                var widget=Blank<GameWidget>();widget.GuiWidget=new CanvasWidget();widget.WidgetsHierarchyInput=new WidgetInput(WidgetInputDevice.Keyboard);
                player.PlayerData.m_gameWidget=widget;
                keys["reload"]="J";down[(int)Key.R]=true;down[(int)Key.J]=false;
                bool Down(bool edge)=>(bool)bindings.GetMethod("Down").Invoke(null,[player,"reload",edge]);
                Check("old-key-unbound",!Down(false));
                down[(int)Key.J]=true;once[(int)Key.J]=true;settings.GetField("CustomButtons").SetValue(null,false);
                Check("mapped-key-independent-of-touch-toggle",Down(false)&&Down(true));
                once[(int)Key.J]=false;Check("held-vs-press-edge",Down(false)&&!Down(true));
                player.ComponentGui.m_modalPanelContainerWidget.Children.Add(new CanvasWidget());Check("inventory-blocks-binding",!Down(false));
                player.ComponentGui.m_modalPanelContainerWidget.Children.Clear();
                windowState.SetValue(null,Enum.Parse(windowState.FieldType,"Inactive"));Check("background-blocks-binding",!Down(false));
                // Real serialized settings DTO preserves the optional extension; old files still default R/G.
                var fileType=settings.GetNestedType("File",BindingFlags.NonPublic);
                object file=System.Text.Json.JsonSerializer.Deserialize("{\"Version\":1,\"KeyBindings\":{\"reload\":\"J\",\"inspect\":\"\"}}",fileType);
                var json=System.Text.Json.JsonSerializer.Serialize(file,fileType);
                var again=System.Text.Json.JsonSerializer.Deserialize(json,fileType);
                var restored=(Dictionary<string,string>)fileType.GetProperty("KeyBindings").GetValue(again);
                Check("bindings-json-roundtrip",restored["reload"]=="J"&&restored["inspect"]=="");
            }finally {
                windowState.SetValue(null,oldWindow);ScreensManager.CurrentScreen=oldScreen;ScreensManager.m_animationData=oldAnimation;ScreensManager.RootWidget=oldRoot;
                down[(int)Key.R]=oldR;down[(int)Key.J]=oldJ;once[(int)Key.J]=oldJOnce;settings.GetField("CustomButtons").SetValue(null,oldButtons);
            }
            var rig=mod.GetType("Game.Cs2Rig");var visibility=mod.GetType("Game.ScGunPartVisibility").GetMethod("Visible");
            foreach(string clip in new[]{"reload","reloadEmpty"}) {
                float detach=(float)rig.GetMethod("CzFrontDetachTime").Invoke(null,[clip]);
                Check("cz-official-detach/"+clip,float.IsFinite(detach)&&detach>.5f&&detach<1f,detach.ToString());
                Check("cz-no-post-detach-flight/"+clip,(bool)visibility.Invoke(null,["cz75a","magazine2",clip,detach-.001f,false])
                    &&!(bool)visibility.Invoke(null,["cz75a","magazine2",clip,detach,false])
                    &&!(bool)visibility.Invoke(null,["cz75a","magazine2",clip,1.2f,false])
                    &&(bool)visibility.Invoke(null,["cz75a","magazine",clip,1.2f,true]));
            }
        }catch(Exception e){Check("setup",false,e.ToString());}
        finally{keys.Clear();foreach(var p in oldKeys)keys[p.Key]=p.Value;SettingsManager.ViewAngle=view;SettingsManager.LookSensitivity=sens;}
        return results;
    }
}
