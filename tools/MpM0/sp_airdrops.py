"""Isolated API 1.9.3.1 airdrop lifecycle probe. No original worlds or Mods writes."""
import json, sys, time
from pathlib import Path
import m0
from m0 import poll, to_menu, sha, RESULTS, RUNS
from mp_m1 import MAIN, KEEP_ACTIVE, build_arena, teleport, SURVIVAL
from sp_subworld import STATE as GUN_STATE, RAY

AD=MAIN+'var ad=project.FindSubsystem<Game.SubsystemScAirdrops>(true); '
STATE=AD+('var be=project.FindSubsystem<Game.SubsystemBlockEntities>(true); '
    'var es=project.FindSubsystem<Game.SubsystemTacticalEnemies>(true).Enemies.Where(e=>e.State.Squad.StartsWith("airdrop-")).ToArray(); '
    'return System.Text.Json.JsonSerializer.Serialize(new {drops=ad.Drops.Select(d=>new {d.Id,d.Fall,d.Landed,d.Smoke}), '
    'guards=es.Select(e=>new {e.State.Squad,target=e.TargetBody==pl.ComponentBody,e.Retaliating,hp=e.Creature.ComponentHealth.Health}), '
    'chests=be.m_blockEntities.Values.Select(e=>e.Entity.FindComponent<Game.ComponentChest>()).Where(c=>c!=null).Select(c=>Enumerable.Range(0,c.SlotsCount).Select(i=>c.GetSlotCount(i)).ToArray())});')

def main(label, package):
    case=RUNS/f'airdrops-{label}-{time.strftime("%Y%m%d-%H%M%S")}'
    case.mkdir(parents=True,exist_ok=True);g=None
    result={'package':str(package),'sha256':sha(package),'checks':[],'states':{}}
    def check(name, ok, detail=None):
        result['checks'].append(dict(name=name,ok=bool(ok),detail=detail));print(('PASS ' if ok else 'FAIL ')+name,flush=True)
        if not ok: raise AssertionError(name+': '+str(detail))
    def state(name):
        s=json.loads(g.func(STATE));result['states'][name]=s;print(name,json.dumps(s),flush=True);return s
    def reload():
        mark=g.mark();g.cmd('EXEC var wi=Game.GameManager.WorldInfo; Game.GameManager.SaveProject(true,true); Game.GameManager.DisposeProject(); Game.ScreensManager.SwitchScreen("GameLoading",wi,null);')
        g.wait('Entered screen "Game"',120,mark);poll(g,m0.PLAYER_SPAWNED,lambda x:x=='True',60);g.func(KEEP_ACTIVE)
        g.func(MAIN+'pl.ComponentHealth.AttackResilience=10000;return "durable inventory fixture restored";')
    try:
        g=m0.game('server',case,[Path(package)]);result['engine']=g.engine_info();to_menu(g)
        mark=g.mark();g.cmd('CLICK_WIDGET Play');g.wait('Entered screen "Play"',60,mark)
        mark=g.mark();g.cmd('CLICK_WIDGET NewWorld');g.wait('Entered screen "NewWorld"',60,mark)
        poll(g,SURVIVAL,lambda x:x=='Survival',20)
        mark=g.mark();g.cmd('CLICK_WIDGET Play');g.wait('Entered screen "Player"',120,mark)
        mark=g.mark();g.cmd('CLICK_WIDGET PlayButton');g.wait('Entered screen "Game"',180,mark)
        poll(g,m0.PLAYER_SPAWNED,lambda x:x=='True',120);g.func(KEEP_ACTIVE)
        # Keep this inventory persistence fixture alive while five guards shoot it.
        # Combat acquisition is checked independently below; never grant a target in setup.
        g.func(MAIN+'pl.ComponentHealth.AttackResilience=10000;return "durable inventory fixture";')
        x,y,z=map(int,build_arena(g).split());g.func(teleport(x,y,z))
        # Clear the actual descent corridor, preserving the grounded arena used by the guards.
        g.func(MAIN+f'var t=project.FindSubsystem<Game.SubsystemTerrain>(true); for(int dy=0;dy<40;dy++) t.ChangeCell({x},{y}+dy,{z+9},0); return "corridor";')
        # Empty the disposable arena's ambient population; MP probe separately checks
        # refusal at the real native cap. Never change the cap to make guards fit.
        g.func(MAIN+'foreach(var e in project.Entities.ToArray())if(e.FindComponent<Game.ComponentCreature>()!=null&&e.FindComponent<Game.ComponentPlayer>()==null)project.RemoveEntity(e,true);return "empty arena";')
        started=g.func(AD+f'return ad.TryStart(new Engine.Vector3({x}f+.5f,{y}f,{z+9}f+.5f)).ToString();')
        check('delivery accepted on clear terrain',started=='True',started)
        check('same site cannot create duplicate delivery',g.func(AD+f'return ad.TryStart(new Engine.Vector3({x}f+.5f,{y}f,{z+9}f+.5f)).ToString();')=='False')
        g.func(MAIN+f'pl.ComponentBody.Position=new Engine.Vector3({x}f+.5f,project.FindSubsystem<Game.SubsystemTerrain>(true).Terrain.GetTopHeight({x},{z-42})+2,{z-42}f+.5f);pl.ComponentBody.Velocity=Engine.Vector3.Zero;return "observe descent outside perception";')
        falling=state('falling');check('starts airborne without loot',not falling['drops'][0]['Landed'] and not falling['chests'])
        reload();resumed=state('resumed');check('fall survives save/reload with same identity',resumed['drops'][0]['Id']==falling['drops'][0]['Id'] and resumed['drops'][0]['Fall']<=falling['drops'][0]['Fall'])
        poll(g,AD+'return (ad.Drops.Count==1&&ad.Drops[0].Landed).ToString();',lambda x:x=='True',45)
        landed=state('landed');stocks=landed['chests']
        check('native chest has one gun and five modest resource stacks',len(stocks)==1 and len(stocks[0])==16
            and stocks[0][0]==1 and stocks[0][1]==2 and 1<=stocks[0][2]<=3 and stocks[0][3]==1 and stocks[0][4:6]==[2,2] and all(n==0 for n in stocks[0][6:]),stocks)
        check('five guards created once',len(landed['guards'])==5,landed)
        patrol=AD+'return System.Text.Json.JsonSerializer.Serialize(project.FindSubsystem<Game.SubsystemTacticalEnemies>(true).Enemies.Where(e=>e.State.Squad.StartsWith("airdrop-")).Select(e=>new {id=e.Entity.Id,x=e.Creature.ComponentBody.Position.X,z=e.Creature.ComponentBody.Position.Z}));'
        g.func(MAIN+f'pl.ComponentBody.Position=new Engine.Vector3({x}f+.5f,project.FindSubsystem<Game.SubsystemTerrain>(true).Terrain.GetTopHeight({x},{z-42})+2,{z-42}f+.5f);pl.ComponentBody.Velocity=Engine.Vector3.Zero;return "outside perception";')
        before_patrol=json.loads(g.func(patrol));time.sleep(7);after_patrol=json.loads(g.func(patrol))
        moved=any((a['x']-b['x'])**2+(a['z']-b['z'])**2>1 for a in after_patrol for b in before_patrol if a['id']==b['id'])
        check('idle guards actually walk their patrol',moved,[before_patrol,after_patrol]);g.func(teleport(x,y,z))
        # Watch their production sensing, without injecting a target or damaging them.
        poll(g,AD+'return project.FindSubsystem<Game.SubsystemTacticalEnemies>(true).Enemies.Any(e=>e.State.Squad.StartsWith("airdrop-")&&e.TargetBody==pl.ComponentBody&&!e.Retaliating).ToString();',lambda x:x=='True',15)
        check('guard acquires nearby player without provocation',True)
        # Remove half the first stack through the native chest API, then reload and verify no refill.
        take=MAIN+f'var c=project.FindSubsystem<Game.SubsystemBlockEntities>(true).GetBlockEntity({x},{y},{z+9}).Entity.FindComponent<Game.ComponentChest>(true); '
        check('native partial withdrawal',g.func(take+'return c.RemoveSlotItems(4,1).ToString();')=='1')
        reload();saved=state('partial-reload');check('reload neither refills nor duplicates chest',len(saved['chests'])==1 and saved['chests'][0][4]==1 and len(saved['guards'])==5,saved)
        transfer=g.func(take+'int value=c.GetSlotValue(0);var inv=pl.ComponentMiner.Inventory;inv.RemoveSlotItems(0,inv.GetSlotCount(0));inv.AddSlotItems(0,value,1);if(inv.GetSlotCount(0)!=1||inv.GetSlotValue(0)!=value)return "refused";c.RemoveSlotItems(0,1);inv.ActiveSlotIndex=0;return Game.ScGunBlock.IsKnown(value).ToString();')
        check('airdrop gun can be withdrawn as a usable gun',transfer=='True',transfer)
        time.sleep(1.5);before_gun=json.loads(g.func(GUN_STATE));ray=g.func(RAY);g.cmd('DIG_RAY '+ray+' 20');time.sleep(2)
        fired=json.loads(g.func(GUN_STATE));check('withdrawn gun actually fires',fired['usable'] and fired['rounds']<before_gun['rounds'],[before_gun,fired])
        reload();gun_saved=json.loads(g.func(GUN_STATE));check('used airdrop gun persists without replenishment',gun_saved['usable'] and gun_saved['id']==fired['id'] and gun_saved['rounds']==fired['rounds'],gun_saved)
        g.func(take+'for(int i=0;i<c.SlotsCount;i++)c.RemoveSlotItems(i,c.GetSlotCount(i));return "empty";')
        reload();empty=state('empty-reload');check('empty crate stays empty after reload',len(empty['chests'])==1 and sum(empty['chests'][0])==0,empty)
        g.func(MAIN+f'project.FindSubsystem<Game.SubsystemTerrain>(true).ChangeCell({x},{y},{z+9},0);return "removed";')
        time.sleep(1);gone=state('removed');check('removing empty crate clears smoke and chest',not gone['drops'] and not gone['chests'],gone)
        result['gameErrors']=g.errors();check('no game errors',not result['gameErrors'],result['gameErrors'])
    except Exception as e:
        result['failure']=str(e);print('FAILURE',str(e),flush=True)
        if g is not None:result['gameErrors']=g.errors()
    finally:
        if g is not None:g.close()
        m0.RUNTIME_OWNER.release()
        out=RESULTS/f'airdrops-{label}.json';out.write_text(json.dumps(result,indent=2)+'\n','utf8');print(out,flush=True)
    return 1 if result.get('failure') else 0

if __name__=='__main__':sys.exit(main(sys.argv[1],Path(sys.argv[2])))
