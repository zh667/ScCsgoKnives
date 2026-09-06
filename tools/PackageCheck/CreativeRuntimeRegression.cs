using System.Reflection;
using Engine;
using Game;

// Exercise real API creative slots, whose counts and writes differ from IInventory mocks.
static class CreativeRuntimeRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod) {
        List<Result> results=[];
        void Test(string name,Func<bool> test) {try{results.Add(new("runtime-regression/"+name,test(),name));}catch(Exception e){results.Add(new("runtime-regression/"+name,false,e.ToString()));}}
        var spec=mod.GetType("Game.GunSpec");var tx=mod.GetType("Game.ScReloadTransaction");
        int Data(int variant,int rounds)=>(int)spec.GetMethod("MakeData").Invoke(null,[variant,rounds,false]);
        int R(int value)=>(int)spec.GetMethod("GetRounds").Invoke(null,[Terrain.ExtractData(value)]);
        ComponentCreativeInventory Inventory(int value) {
            var inv=new ComponentCreativeInventory {OpenSlotsCount=10};
            for(int i=0;i<10;i++)inv.m_slots.Add(0);
            inv.AddSlotItems(0,value,1);inv.m_slots.Add(value); // read-only catalog slot
            return inv;
        }
        object Tx(ComponentCreativeInventory inv,int capacity,int cost=0,int slot=0)=>Activator.CreateInstance(tx,[inv,slot,inv.GetSlotValue(slot),901,cost,capacity]);
        bool Call(object t,string method)=>(bool)tx.GetMethod(method).Invoke(t,null);
        bool Valid(object t)=>(bool)tx.GetProperty("Valid").GetValue(t);
        var guns=(Array)spec.GetField("All").GetValue(null);
        for(int v=0;v<guns.Length;v++) {
            object gun=guns.GetValue(v);string name=(string)spec.GetField("Name").GetValue(gun);
            if(name=="taser")continue;
            int capacity=(int)spec.GetField("Magazine").GetValue(gun),variant=v;
            foreach(int initial in new[]{0,Math.Max(1,capacity/2)}) Test($"creative-reload/{name}/{initial}",()=>{
                int value=Terrain.MakeBlockValue(512,0,Data(variant,initial));var inv=Inventory(value);var t=Tx(inv,capacity);
                if(inv.GetSlotCount(0)<=1 || !Valid(t))return false;
                if(name is "nova" or "xm1014" or "sawedoff") {
                    for(int n=initial;n<capacity;n++)if(!Call(t,"InsertShell") || !Valid(t) || R(inv.GetSlotValue(0))!=n+1)return false;
                    if(Call(t,"InsertShell"))return false;
                } else if(!Call(t,"Discard") || !Valid(t) || R(inv.GetSlotValue(0))!=initial || !Call(t,"InsertMagazine") || Call(t,"InsertMagazine"))return false;
                return R(inv.GetSlotValue(0))==capacity && inv.GetSlotCount(0)==ComponentCreativeInventory.m_largeNumber
                    && inv.GetSlotValue(1)==0 && inv.GetSlotValue(10)==value && Valid(t);
            });
        }
        int sample=Terrain.MakeBlockValue(512,0,Data(0,8));
        Test("creative-switch-cancels",()=>{var inv=Inventory(sample);var t=Tx(inv,30);inv.ActiveSlotIndex=1;return !Call(t,"Discard") && inv.GetSlotValue(0)==sample;});
        Test("creative-replacement-cancels",()=>{var inv=Inventory(sample);var t=Tx(inv,30);inv.AddSlotItems(0,Terrain.MakeBlockValue(512,0,Data(1,0)),1);return !Valid(t) && !Call(t,"Discard");});
        Test("creative-catalog-readonly",()=>{var inv=Inventory(sample);var t=Tx(inv,30,0,10);return !Valid(t) && !Call(t,"Discard") && inv.GetSlotValue(10)==sample;});
        Test("creative-paid-mode-rejected",()=>{var inv=Inventory(sample);var t=Tx(inv,30,1);return Call(t,"Discard") && !Call(t,"InsertMagazine") && R(inv.GetSlotValue(0))==8;});
        Test("creative-cancel-after-discard",()=>{var inv=Inventory(sample);var t=Tx(inv,30);bool dropped=Call(t,"Discard");tx.GetMethod("Cancel").Invoke(t,null);return dropped && !Call(t,"InsertMagazine") && R(inv.GetSlotValue(0))==8;});
        var grenade=(Block)Activator.CreateInstance(mod.GetType("Game.ScGrenadeBlock"));
        for(int kind=0;kind<6;kind++) {
            int value=Terrain.MakeBlockValue(700,0,kind);
            Test("grenade-full-image-uv/"+kind,()=>grenade.GetTextureSlotCount(value)==1 && grenade.GetFaceTextureSlot(-1,value)==0
                && grenade.GetIconViewOffset(value,new DrawBlockEnvironmentData {DrawBlockMode=DrawBlockMode.UI})==Vector3.UnitZ);
        }
        var bench=(Block)Activator.CreateInstance(mod.GetType("Game.ScWeaponWorkbenchBlock"));
        Test("workbench-items-category",()=>bench.GetCategory(0)=="Items");
        var build=mod.GetType("Game.ScSurvivalMesh").GetMethod("Build");
        foreach(var item in new[]{(Type:"ScWeaponWorkbenchBlock",Kind:6),(Type:"ScAmmoBlock",Kind:0),(Type:"ScWeaponMaterialBlock",Kind:2)}) Test("first-person-outside-camera/"+item.Type,()=>{
            var block=(Block)Activator.CreateInstance(mod.GetType("Game."+item.Type));var mesh=(BlockMesh)build.Invoke(null,[item.Kind]);
            var rotation=block.GetFirstPersonRotation(0)*(MathF.PI/180);
            Matrix m=Matrix.CreateScale(block.GetFirstPersonScale(0))*Matrix.CreateFromYawPitchRoll(rotation.Y,rotation.X,rotation.Z)*Matrix.CreateTranslation(block.GetFirstPersonOffset(0));
            return mesh.Vertices.All(v=>Vector3.Transform(v.Position,m).Z<-.1f) && block.GetFirstPersonScale(0)<.6f && block.InHandScale<.7f;
        });
        return results;
    }
}
