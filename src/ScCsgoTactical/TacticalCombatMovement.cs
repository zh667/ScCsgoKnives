using Engine;
namespace Game;

/// <summary>Short, supported combat steps. Never choose a ledge, water or an obstructed landing.</summary>
public static class TacticalCombatMovement {
    public static bool IsSmg(string name)=>name is "mac10" or "mp9" or "mp7" or "mp5sd" or "ump45" or "p90" or "bizon";
    public static bool Landing(SubsystemTerrain terrain,ComponentBody body,Vector3 candidate,out Vector3 destination){
        destination=default;
        int x=Terrain.ToCell(candidate.X),z=Terrain.ToCell(candidate.Z);
        var chunk=terrain.Terrain.GetChunkAtCell(x,z);
        if(chunk is null||chunk.State<TerrainChunkState.InvalidLight)return false;
        var hit=terrain.Raycast(candidate+Vector3.UnitY,candidate-Vector3.UnitY*1.25f,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));
        if(hit is not {} floor||CellFace.FaceToVector3(floor.CellFace.Face).Y<.5f)return false;
        var at=floor.HitPoint()+Vector3.UnitY*.05f;
        int ground=terrain.Terrain.GetCellValue(floor.CellFace.X,floor.CellFace.Y,floor.CellFace.Z);
        if(BlocksManager.Blocks[Terrain.ExtractContents(ground)].ShouldAvoid(ground))return false;
        float half=body.BoxSize.X*.5f;
        for(int cx=Terrain.ToCell(at.X-half);cx<=Terrain.ToCell(at.X+half);cx++)
            for(int cz=Terrain.ToCell(at.Z-half);cz<=Terrain.ToCell(at.Z+half);cz++){
                if(TacticalNavigation.Blocked(terrain,cx,cz,at.Y,at.Y+body.BoxSize.Y))return false;
                if(BlocksManager.Blocks[Terrain.ExtractContents(terrain.Terrain.GetCellValue(cx,Terrain.ToCell(at.Y),cz))] is FluidBlock)return false;
            }
        if(terrain.Raycast(body.Position+Vector3.UnitY*.5f,at+Vector3.UnitY*.5f,false,true,(v,d)=>ScGunRange.TerrainStopsBullet(v)).HasValue)return false;
        destination=at;return true;
    }
    public static Vector3? Strafe(SubsystemTerrain terrain,ComponentBody body,Vector3 target,int side){
        var forward=(target-body.Position).XZ;
        if(forward.LengthSquared()<.01f)return null;
        forward=Vector2.Normalize(forward);var lateral=new Vector3(-forward.Y,0,forward.X)*side*3;
        return Landing(terrain,body,body.Position+lateral,out var at)?at:null;
    }
    public static Vector3? Cover(SubsystemTerrain terrain,ComponentBody body,Vector3 source){
        Vector3? fallback=null;float best=-1;
        for(int i=0;i<12;i++){
            float angle=i*MathF.PI/6;
            if(!Landing(terrain,body,body.Position+new Vector3(MathF.Cos(angle),0,MathF.Sin(angle))*4,out var at))continue;
            if(terrain.Raycast(source+Vector3.UnitY,at+Vector3.UnitY*1.3f,false,true,(v,d)=>ScGunRange.TerrainStopsBullet(v)).HasValue)return at;
            float distance=Vector3.DistanceSquared(source,at);
            if(distance>best){best=distance;fallback=at;}
        }
        return fallback;
    }
}
