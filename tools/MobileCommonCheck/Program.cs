using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

Dispatcher.Initialize();string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);}
BlocksManager.Blocks[0]=new AirBlock{BlockIndex=0};
var terrain=new Floor();var bodies=new SubsystemBodies();
var entity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));
var body=new ComponentBody{Position=Vector3.Zero,BoxSize=new(.65f,1.8f,.65f)};body.m_entity=entity;entity.m_components=[body];
bodies.AddBody(body);
var subsystem=new SubsystemScGrenades();
void Field(string n,object value)=>typeof(SubsystemScGrenades).GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(subsystem,value);
Field("m_terrain",terrain);Field("m_bodies",bodies);
var move=typeof(SubsystemScGrenades).GetMethod("Move",BindingFlags.Instance|BindingFlags.NonPublic)!;
ScGrenadeState Roundtrip(ScGrenadeState s){
    var x=new XElement("Values");s.Save().Save(x);var v=new ValuesDictionary();v.ApplyOverrides(XElement.Parse(x.ToString()));return ScGrenadeState.Load(v);
}
foreach(float frame in new[]{1f/60,1f/20,.5f}){
    var s=new ScGrenadeState{Kind=2,Owner=-2,Id=12,Position=new(0,2.1f,0),Velocity=new(0,-3,0),Age=.4f,NextBounceSound=float.MaxValue};
    bool didRoundtrip=false;int hits=0;float elapsed=0;
    while(elapsed<10&&!ScGrenadeBallistics.Settled(s)){
        float step=ScGrenadeBallistics.Step(frame);s.Age+=step;elapsed+=step;
        for(float left=step;left>0;){float dt=Math.Min(left,.02f);bool before=s.SmokeBodyBounceUsed;move.Invoke(subsystem,[s,dt]);left-=dt;if(!before&&s.SmokeBodyBounceUsed)hits++;}
        if(s.SmokeBodyBounceUsed&&!didRoundtrip){s=Roundtrip(Roundtrip(s));s.NextBounceSound=float.MaxValue;didRoundtrip=true;}
    }
    Check("smoke one native body hit "+frame,hits==1&&didRoundtrip);
    Check("smoke passes head and settles on actual floor "+frame,ScGrenadeBallistics.Settled(s)&&s.Position.Y<.1f&&elapsed<4);
    Check("body policy survives two XML save reloads "+frame,s.SmokeBodyBounceUsed&&!ScGrenadeBallistics.BodyCollisionAllowed(s));
}
var prior=new ScGrenadeState{Kind=2,Position=new(0,3,0),Velocity=Vector3.UnitX}.Save();prior.Remove("SmokeBodyBounceUsed");
Check("legacy save defaults to one body hit available",ScGrenadeBallistics.BodyCollisionAllowed(ScGrenadeState.Load(prior)));
foreach(int kind in new[]{0,1,3,4,5}){
    var s=new ScGrenadeState{Kind=kind,Velocity=new(2,-3,1)};
    ScGrenadeBallistics.BounceFromBody(s,Vector3.Zero);
    Check("other grenade retains bounce "+kind,ScGrenadeBallistics.BodyCollisionAllowed(s)&&!s.SmokeBodyBounceUsed&&s.Velocity==-(new Vector3(2,-3,1))*.3f);
}
var wall=new ScGrenadeState{Kind=2,SmokeBodyBounceUsed=true,Position=new(0,3,0),Velocity=new(10,0,0),Age=1,NextBounceSound=float.MaxValue};
for(int i=0;i<8;i++)move.Invoke(subsystem,[wall,.02f]);
Check("spent body hit still collides with wall",wall.Position.X<1&&wall.Velocity.X<0&&!wall.Effect);
var midair=new ScGrenadeState{Kind=2,SmokeBodyBounceUsed=true,Age=20,Remaining=0};
Check("no new airborne timeout detonation",!ScGrenadeBallistics.Settled(midair));
Check("settle hold retained",ScGrenadeBallistics.SettleHold==.15f&&ScGrenadeBallistics.SettleTimeout==12);
File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{failed=0,checks,
    core=typeof(ScGrenadeState).Assembly.ManifestModule.ModuleVersionId,scope="Native body raycast and production grenade motion; deterministic floor/wall fixture, no player world."},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"PASS {checks.Count} grenade checks");

sealed class Floor:SubsystemTerrain {
    public Floor(){Terrain=new Terrain();}
    public override TerrainRaycastResult? Raycast(Vector3 start,Vector3 end,bool interaction,bool air,Func<int,float,bool> filter){
        float best=float.MaxValue;int face=4;Vector3 d=end-start;
        if(start.Y>=0&&end.Y<=0&&d.Y<0)best=-start.Y/d.Y;
        if(start.X<=1&&end.X>=1&&d.X>0){float t=(1-start.X)/d.X;if(t<best){best=t;face=3;}}
        if(best==float.MaxValue)return null;
        float length=d.Length();return new TerrainRaycastResult{Ray=new Ray3(start,Vector3.Normalize(d)),Distance=length*best,CellFace=new CellFace(0,0,0,face),Value=1};
    }
}
