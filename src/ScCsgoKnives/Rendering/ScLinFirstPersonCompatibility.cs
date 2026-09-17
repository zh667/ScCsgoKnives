using System.Reflection;

namespace Game;

/// <summary>Lin's Gun component reimplements IDrawable.Draw without the API first-person hook.
/// Keep its component/update state, but route our items through the inherited API drawing slot.</summary>
public static class ScLinFirstPersonCompatibility {
    static readonly Dictionary<ComponentFirstPersonModel, Adapter> s_adapters=[];

    public static bool Register(SubsystemDrawing drawing, IDrawable drawable, bool skippedByOtherMods) {
        if(skippedByOtherMods || drawable is not ComponentFirstPersonModel model || model.Entity is null)return false;
        Type type=model.GetType();
        if(type.FullName!="Game.ComponentNewFirstPersonModel" || type.Assembly.GetName().Name!="Gun")return false;
        // The supplied DLL hides Draw in a separate interface slot. If a later version overrides the
        // base slot instead, a base-typed call would recurse; do not guess that version's contract.
        var draw=type.GetMethod("Draw",BindingFlags.Public|BindingFlags.Instance,null,[typeof(Camera),typeof(int)],null);
        if(draw is null || draw.DeclaringType!=type || draw.GetBaseDefinition().DeclaringType==typeof(ComponentFirstPersonModel))return false;
        if(s_adapters.ContainsKey(model))return true;
        var adapter=new Adapter(drawing,model,drawable);
        s_adapters.Add(model,adapter);
        drawing.AddDrawable(adapter);
        KnifeLog.Information("lin gun compatibility: retain lin component; route CS first-person items through the API rendering hook.");
        return true;
    }

    static bool IsOurItem(int value) {
        if(value==0)return false;
        return BlocksManager.Blocks[Terrain.ExtractContents(value)] is ScGunBlock or ScKnifeBlock or ScGrenadeBlock or ScC4Block
            or ScSupplyBlock or ScGunSkinTemplateBlock or ScGunCounterTemplateBlock;
    }

    public static bool UsesApiDrawing(ComponentFirstPersonModel model) {
        if(IsOurItem(model.m_value) || IsOurItem(model.m_componentMiner?.ActiveBlockValue??0))return true;
        // A thrown grenade or planted C4 may retain a brief viewmodel after its slot is consumed.
        var project=model.Project;
        int value=project?.FindSubsystem<SubsystemScC4>()?.ViewmodelValue(model.m_componentPlayer,model.m_value)??model.m_value;
        value=project?.FindSubsystem<SubsystemScGrenades>()?.ViewmodelValue(model.m_componentPlayer,value)??value;
        return IsOurItem(value);
    }

    public static void Clear() {
        foreach(var adapter in s_adapters.Values.ToArray())adapter.Detach();
    }

    sealed class Adapter : IDrawable {
        readonly SubsystemDrawing m_drawing;
        readonly ComponentFirstPersonModel m_model;
        readonly IDrawable m_original;
        // Bound once, not reflected every frame. These are different slots in the supplied Gun.dll.
        readonly Action<Camera,int> m_apiDraw;
        readonly Action<Camera,int> m_linDraw;
        public Adapter(SubsystemDrawing drawing,ComponentFirstPersonModel model,IDrawable original) {
            m_drawing=drawing;m_model=model;m_original=original;
            m_apiDraw=model.Draw;m_linDraw=original.Draw;
            model.Entity.EntityRemoved+=Removed;
        }
        public int[] DrawOrders=>m_original.DrawOrders;
        public void Draw(Camera camera,int drawOrder) {
            if(UsesApiDrawing(m_model))m_apiDraw(camera,drawOrder);
            else m_linDraw(camera,drawOrder);
        }
        void Removed(object sender,EventArgs e)=>Detach();
        public void Detach() {
            m_model.Entity.EntityRemoved-=Removed;
            m_drawing.RemoveDrawable(this);
            s_adapters.Remove(m_model);
        }
    }
}
