"""Two-process API 1.9.3.2_MP proof: host-only landing/loot and client presentation."""
import json,sys,time
from pathlib import Path
import m0
from m0 import poll,to_menu,join,RESULTS,RUNS,sha
from mp_m1 import MAIN,KEEP_ACTIVE,HANDSHAKE,build_arena,teleport
from sp_airdrops import AD,STATE

def main(label,package):
    case=RUNS/f'airdrops-mp-{label}-{time.strftime("%Y%m%d-%H%M%S")}';case.mkdir(parents=True)
    report={'packageSha256':sha(package),'checks':[]};server=client=None
    def check(name,ok,detail=None):
        report['checks'].append(dict(name=name,ok=bool(ok),detail=detail));print(('PASS ' if ok else 'FAIL ')+name,flush=True)
        if not ok:raise AssertionError(name+': '+str(detail))
    try:
        server=m0.game('server',case,[package]);report['serverEngine']=server.engine_info();to_menu(server);m0.enter_world(server);server.func(KEEP_ACTIVE)
        x,y,z=map(int,build_arena(server).split());server.func(teleport(x,y,z))
        server.func(MAIN+f'var t=project.FindSubsystem<Game.SubsystemTerrain>(true);for(int dy=0;dy<40;dy++)t.ChangeCell({x},{y}+dy,{z+9},0);return "clear";')
        client=m0.game('client1',case,[package]);report['clientEngine']=client.engine_info();to_menu(client);join(client,server)
        check('client handshake accepts same candidate',poll(client,HANDSHAKE,lambda s:s=='Accepted',30)=='Accepted')
        for g in [server,client]:g.func(KEEP_ACTIVE)
        server.func(MAIN+'foreach(var p in players.ComponentPlayers)p.ComponentHealth.AttackResilience=10000;return "durable fixtures";')
        client.func(teleport(x,y,z));time.sleep(2)
        start=AD+f'return ad.TryStart(new Engine.Vector3({x}f+.5f,{y}f,{z+9}f+.5f)).ToString();'
        check('remote client cannot generate drops or rewards',client.func(start)=='False')
        budget=int(server.func(AD+'return project.FindSubsystem<Game.SubsystemCreatureSpawn>(true).CountCreatures(false).ToString();'))
        limit=int(server.func('return Game.SubsystemCreatureSpawn.m_totalLimit.ToString();'))
        report['initialNativePopulation']={'count':budget,'limit':limit}
        if budget+5>limit:
            check('full native population refuses flight before spawning any crate',server.func(start)=='False')
            check('population refusal leaves no partial guard squad',json.loads(server.func(STATE))=={'drops':[],'guards':[],'chests':[]})
        # This fresh fixture's wild animals are outside the airdrop scenario. Clear them
        # after proving the real population refusal; never increase/disable the native cap.
        report['arenaPopulationSetup']=server.func(MAIN+'int removed=0;foreach(var e in project.Entities.ToArray())if(e.FindComponent<Game.ComponentCreature>()!=null&&e.FindComponent<Game.ComponentPlayer>()==null&&e.FindComponent<Game.ComponentTacticalEnemy>()==null&&e.FindComponent<Game.ComponentTacticalCompanion>()==null){project.RemoveEntity(e,true);removed++;}return removed.ToString();')
        check('arena population leaves five genuine slots',int(server.func(AD+'return project.FindSubsystem<Game.SubsystemCreatureSpawn>(true).CountCreatures(false).ToString();'))+5<=limit)
        check('host can begin delivery',server.func(start)=='True')
        check('client sees airborne delivery',poll(client,AD+'return (ad.Drops.Count==1&&!ad.Drops[0].Landed).ToString();',lambda s:s=='True',10)=='True')
        check('host lands delivery',poll(server,AD+'return (ad.Drops.Count==1&&ad.Drops[0].Landed).ToString();',lambda s:s=='True',40)=='True')
        check('client receives landed state',poll(client,AD+'return (ad.Drops.Count==1&&ad.Drops[0].Landed).ToString();',lambda s:s=='True',10)=='True')
        chest=MAIN+f'var c=project.FindSubsystem<Game.SubsystemBlockEntities>(true).GetBlockEntity({x},{y},{z+9})?.Entity.FindComponent<Game.ComponentChest>(); '
        count=chest+'return c==null?"none":string.Join(",",Enumerable.Range(0,c.SlotsCount).Select(i=>c.GetSlotCount(i)));'
        host_stock=server.func(count)
        check('client native chest inventory equals host',poll(client,count,lambda s:s==host_stock,15)==host_stock,[host_stock,client.func(count)])
        guards=AD+'return project.FindSubsystem<Game.SubsystemTacticalEnemies>(true).Enemies.Count(e=>e.State?.Squad?.StartsWith("airdrop-")==true).ToString();'
        check('exactly five guards on host and client',server.func(guards)=='5' and poll(client,guards,lambda s:s=='5',10)=='5')
        server.func(chest+'c.RemoveSlotItems(4,1);return "withdrawn";');after=server.func(count)
        check('partial withdrawal replicates without refill',poll(client,count,lambda s:s==after,10)==after,[after,client.func(count)])
        server.func(chest+'for(int i=0;i<c.SlotsCount;i++)c.RemoveSlotItems(i,c.GetSlotCount(i));return "empty";')
        server.func(MAIN+f'project.FindSubsystem<Game.SubsystemTerrain>(true).ChangeCell({x},{y},{z+9},0);return "removed";')
        check('removed crate clears client smoke',poll(client,AD+'return ad.Drops.Count.ToString();',lambda s:s=='0',10)=='0')
        report['errors']={'server':server.errors(),'client':client.errors()};check('no game errors',not server.errors() and not client.errors(),report['errors'])
    except Exception as e:
        report['failure']=str(e);print('FAILURE',e,flush=True)
        if server is not None:
            try:
                report['hostState']=server.func(STATE)
                report['hostDiagnostics']=server.func(AD+'var d=ad.Drops.FirstOrDefault();var t=project.FindSubsystem<Game.SubsystemTerrain>(true).Terrain;var en=project.FindSubsystem<Game.SubsystemTacticalEnemies>(true);return System.Text.Json.JsonSerializer.Serialize(new {budget=en.Budget,cap=en.MaxActive,natural=en.NaturalCount,role=Game.ScNet.Role.ToString(),clock=project.FindSubsystem<Game.SubsystemTime>(true).GameTime,drop=d==null?"none":d.Ground.ToString(),column=d==null?"":string.Join(",",Enumerable.Range((int)d.Ground.Y-1,38).Select(y=>t.GetCellContents((int)d.Ground.X,y,(int)d.Ground.Z)))});')
                print(report['hostState'],report['hostDiagnostics'],flush=True)
            except Exception as detail:report['diagnosticError']=str(detail)
        report['errors']={g.name:g.errors() for g in [server,client] if g is not None}
    finally:
        for g in [client,server]:
            if g is not None:g.close()
        m0.RUNTIME_OWNER.release()
        out=RESULTS/f'airdrops-mp-{label}.json';out.write_text(json.dumps(report,indent=2)+'\n','utf8');print(out,flush=True)
    return 1 if report.get('failure') else 0

if __name__=='__main__':sys.exit(main(sys.argv[1],Path(sys.argv[2])))
