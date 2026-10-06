using Engine;
using Engine.Graphics;
using TemplatesDatabase;
namespace Game;

/// <summary>Native chest entities own inventory, saving, opening and destruction drops.</summary>
public sealed class SubsystemScAirdropChest : SubsystemChestBlockBehavior {
    public override int[] HandledBlocks=>[]; // selected once via the block's Behaviors property
    public override void OnBlockAdded(int value,int oldValue,int x,int y,int z){
        if(ScNet.IsAuthority)base.OnBlockAdded(value,oldValue,x,y,z);
    }
    public override void OnBlockRemoved(int value,int newValue,int x,int y,int z){
        if(ScNet.IsAuthority)base.OnBlockRemoved(value,newValue,x,y,z);
    }
}
