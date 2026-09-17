using System.IO.Compression;
using ZipArchive=System.IO.Compression.ZipArchive;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Game;
using GameEntitySystem;

static class LinFirstPersonRegression {
    internal record Result(string Name,bool Ok,string Detail);
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static List<Result> Run(Assembly mod,string linPackage,string vanillaContent) {
        List<Result> results=[];
        void Test(string name,Func<bool> action){try{results.Add(new("lin-first-person/"+name,action(),""));}catch(Exception e){results.Add(new("lin-first-person/"+name,false,e.ToString()));}}
        var bridge=mod.GetType("Game.ScLinFirstPersonCompatibility",true);
        var oldBlocks=BlocksManager.Blocks.ToArray();
        var hookSnapshot=ModsManager.ModHooks.ToArray();
        var substitutions=ModsManager.ClassSubstitutes.ToDictionary(p=>p.Key,p=>p.Value.ToList());
        try {
            using var raw=File.OpenRead(linPackage);using var decoded=ModsManager.GetDecipherStream(raw);using var archive=new ZipArchive(decoded);
            using var dll=archive.GetEntry("Gun.dll").Open();using var bytes=new MemoryStream();dll.CopyTo(bytes);bytes.Position=0;
            var lin=new PackageContext("lin-gun-04").LoadFromStream(bytes);
            var type=lin.GetType("Game.ComponentNewFirstPersonModel",true);
            var baseDraw=typeof(ComponentFirstPersonModel).GetMethod("Draw");
            var linDraw=type.GetMethod("Draw",[typeof(Camera),typeof(int)]);
            Test("actual-dll-bypasses-api-hook",()=>
                linDraw.DeclaringType==type && linDraw.GetBaseDefinition()!=baseDraw
                && !CombatRegression.Calls(linDraw).Any(m=>m.DeclaringType==typeof(ModsManager)&&m.Name=="HookAction")
                && CombatRegression.Calls(baseDraw).Any(m=>m.DeclaringType==typeof(ModsManager)&&m.Name=="HookAction"));
            if(vanillaContent is not null)Test("actual-xdb-registers-player-component-substitution",()=>{
                using var vanilla=ZipFile.OpenRead(vanillaContent);using var source=vanilla.GetEntry("Assets/Database.xml").Open();var xml=XElement.Load(source);
                foreach(var entry in archive.Entries.Where(e=>e.FullName.EndsWith(".xdb"))){using var input=entry.Open();ModsManager.CombineDataBase(xml,input,"Lin's gun");}
                return ModsManager.ClassSubstitutes.TryGetValue("bb0d545a-b76e-48a9-8966-022d79b4769a",out var candidates)&&candidates.Any(c=>c.ClassName==type.FullName);
            });
            var loader=(ModLoader)Activator.CreateInstance(mod.GetType("Game.ScCsgoKnivesModLoader"));
            var hook=new ModsManager.ModHook("OnIDrawableAdded");hook.Add(loader);ModsManager.ModHooks["OnIDrawableAdded"]=hook;
            var drawing=new SubsystemDrawing();
            ComponentFirstPersonModel Model() {
                var model=(ComponentFirstPersonModel)RuntimeHelpers.GetUninitializedObject(type);
                var entity=new Entity{m_project=new Project()};entity.m_components=[model];model.m_entity=entity;
                var inventory=new ComponentInventory();inventory.m_slots.Add(new());
                model.m_componentMiner=new ComponentMiner{Inventory=inventory};
                model.m_componentPlayer=new ComponentPlayer{ComponentMiner=model.m_componentMiner,ComponentHealth=new ComponentHealth{Health=0}};
                return model;
            }
            var model=Model();
            Test("honor-other-mod-ownership",()=>{loader.OnIDrawableAdded(drawing,(IDrawable)model,true,out bool skip);return !skip&&drawing.m_drawables.Count==0;});
            Test("native-component-untouched",()=>{var native=(ComponentFirstPersonModel)RuntimeHelpers.GetUninitializedObject(typeof(ComponentFirstPersonModel));native.m_entity=new Entity();loader.OnIDrawableAdded(drawing,native,false,out bool skip);return !skip&&drawing.m_drawables.Count==0;});
            Test("engine-registers-one-adapter",()=>{drawing.OnEntityAdded(model.Entity);return drawing.m_drawables.Count==1&&!drawing.m_drawables.ContainsKey((IDrawable)model);});
            var adapter=drawing.m_drawables.Keys.Single();
            var apiField=adapter.GetType().GetField("m_apiDraw",Fields);var linField=adapter.GetType().GetField("m_linDraw",Fields);
            Test("bound-delegates-use-distinct-real-slots",()=>((Delegate)apiField.GetValue(adapter)).Method==baseDraw&&((Delegate)linField.GetValue(adapter)).Method==linDraw);
            Test("repeated-register-does-not-double-draw",()=>{drawing.OnEntityAdded(model.Entity);return drawing.m_drawables.Count==1;});
            int apiCalls=0,linCalls=0;
            apiField.SetValue(adapter,(Action<Camera,int>)((_,_)=>apiCalls++));linField.SetValue(adapter,(Action<Camera,int>)((_,_)=>linCalls++));
            string[] blockNames=["ScGunBlock","ScKnifeBlock","ScGrenadeBlock","ScC4Block","ScAmmoBlock","ScWeaponMaterialBlock","ScGunSkinTemplateBlock","ScGunCounterTemplateBlock"];
            for(int i=0;i<blockNames.Length;i++)BlocksManager.Blocks[700+i]=(Block)Activator.CreateInstance(mod.GetType("Game."+blockNames[i]));
            BlocksManager.Blocks[710]=new DirtBlock();
            BlocksManager.Blocks[711]=(Block)RuntimeHelpers.GetUninitializedObject(lin.GetType("Game.AKM突击步枪",true));
            void Route(int shown,int selected,bool api) {
                model.m_value=shown;
                var inv=(ComponentInventory)model.m_componentMiner.Inventory;inv.m_slots[0].Value=selected;inv.m_slots[0].Count=selected==0?0:1;
                int beforeApi=apiCalls,beforeLin=linCalls;
                adapter.Draw(null,1);
                if(apiCalls-beforeApi!=(api?1:0)||linCalls-beforeLin!=(api?0:1))throw new Exception("wrong dispatch route");
            }
            foreach(int i in Enumerable.Range(0,blockNames.Length))Test("cs-item-routes-to-api/"+blockNames[i],()=>{Route(Terrain.MakeBlockValue(700+i),Terrain.MakeBlockValue(700+i),true);return true;});
            foreach(int value in new[]{0,710,711})Test("non-cs-item-retains-lin/"+value,()=>{Route(value,value,false);return true;});
            Test("switch-in-and-out-keeps-outgoing-route",()=>{Route(711,700,true);Route(700,711,true);Route(711,711,false);Route(700,0,true);Route(0,0,false);return true;});
            Test("entity-removal-cleans-adapter",()=>{drawing.OnEntityRemoved(model.Entity);model.Entity.FireEntityRemovedEvent();return drawing.m_drawables.Count==0;});
            Test("same-entity-readded-and-two-players",()=>{drawing.OnEntityAdded(model.Entity);drawing.OnEntityAdded(Model().Entity);return drawing.m_drawables.Count==2;});
            Test("world-dispose-removes-all-adapters",()=>{bridge.GetMethod("Clear").Invoke(null,null);return drawing.m_drawables.Count==0;});
        }catch(Exception e){results.Add(new("lin-first-person/setup",false,e.ToString()));}
        finally {
            bridge.GetMethod("Clear").Invoke(null,null);
            for(int i=0;i<oldBlocks.Length;i++)BlocksManager.Blocks[i]=oldBlocks[i];
            ModsManager.ModHooks.Clear();foreach(var p in hookSnapshot)ModsManager.ModHooks[p.Key]=p.Value;
            ModsManager.ClassSubstitutes.Clear();foreach(var p in substitutions)ModsManager.ClassSubstitutes[p.Key]=p.Value;
        }
        return results;
    }
}
