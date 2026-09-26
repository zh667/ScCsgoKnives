"""Read-only size budgets: cosmetic removal is a counterfactual, not a playable build."""
import argparse
import collections
import json
import zipfile
from pathlib import Path

MAIN = {'ak47','aug','famas','galilar','m4a1s','m4a4','sg556','awp','g3sg1','scar20','ssg08','m249','negev'}

def audit(package):
    groups=collections.defaultdict(int); skins=collections.defaultdict(lambda:collections.defaultdict(int))
    root=Path(__file__).resolve().parents[1]
    catalogue=json.loads((root/'tools/gun_skins_catalog.json').read_text(encoding='utf-8'))['skins']+json.loads((root/'src/ScCsgoKnives/AnimationData/gun_additional_skins.json').read_text(encoding='utf-8'))
    def source_names(folder):
        base=root/folder; names=set()
        for path in base.rglob('*'):
            if path.is_file():
                name='Assets/'+path.relative_to(base).as_posix();names.add(name)
                if name.endswith('.png'): names.add(name[:-4]+'.webp')
        return names
    core=source_names('src/ScCsgoKnives/Assets')
    tactical=source_names('src/ScCsgoTactical/Assets')|source_names('src/ScCsgoAppearance/Assets')
    with zipfile.ZipFile(package) as z:
        for i in z.infolist():
            n=i.filename; stem=Path(n).stem
            cosmetic=n.startswith('Assets/Textures/ScCsgoKnives/') and any(k in stem for k in ('_hd__','_slot__','_finish__'))
            if n=='ScCsgoResources.dll':kind='Embedded weapon animation/geometry DLL'
            elif n.startswith('Assets/Models/ScCsgoTactical/Weapons/'):kind='NPC weapon caches'
            elif n.endswith('.scanim'):kind='Actor animation caches'
            elif n.endswith('.obj'):kind='OBJ geometry'
            elif n.endswith('.glb'):kind='GLB models'
            elif cosmetic:kind='Optional gun/knife cosmetic textures'
            elif n.startswith('Assets/Textures/'):kind='Other textures'
            elif n.startswith('Assets/Audio/'):kind='Audio'
            elif n.endswith(('.dll','.bin')):kind='Other code/embedded resources'
            else:kind='Other metadata/resources'
            groups[kind]+=i.compress_size
            if cosmetic:
                gun,finish=stem.split('__',1)
                gun=gun.removesuffix('_hd').removesuffix('_slot').removesuffix('_finish')
                finish=finish.removesuffix('_normal').removesuffix('_orm')
                # Extra materials, e.g. SSG08 scope, belong to their parent finish.
                matches=[s['key'] for s in catalogue if s['gun']==gun and (finish==s['key'] or finish.startswith(s['key']+'_'))]
                if matches: finish=max(matches,key=len)
                skins[gun][finish]+=i.compress_size
        # Retain one finish for EACH rifle, sniper rifle and machine gun; all factory assets remain.
        retained={gun:min(skins[gun],key=skins[gun].get) for gun in sorted(MAIN) if skins[gun]}
        optional_gun_bytes=sum(sum(v.values()) for gun,v in skins.items() if any(i.filename.startswith(f'Assets/Textures/ScCsgoKnives/{gun}_hd__') for i in z.infolist()))
        retained_bytes=sum(skins[g][f] for g,f in retained.items())
        size=package.stat().st_size
        floor=sum(groups[k] for k in ['Embedded weapon animation/geometry DLL','NPC weapon caches','Actor animation caches'])
        remove={n for n in z.namelist() if n in tactical and n not in core}
        remove.update(n for n in z.namelist() if n in ['ScCsgoTactical.dll','ScCsgoBundle.dll','Integrations/ScCsgoAppearance.bin'] or n.startswith('Integrations/ScCsgoTactical'))
        no_agents=size-sum(z.getinfo(n).compress_size for n in remove)
        return dict(package=package.name,bytes=size,limitBytes=30_000_000,compressedCategories=dict(groups),
            zipHeadersBytes=size-sum(groups.values()),
            retainedMainWeaponFinishes=retained,retainedMainWeaponFinishBytes=retained_bytes,
            allOptionalGunTextureBytes=optional_gun_bytes,
            removeOtherGunFinishesBytes=size-optional_gun_bytes+retained_bytes,
            removeAllOptionalGunAndKnifeTexturesBytes=size-groups['Optional gun/knife cosmetic textures'],
            threeResourceGroupsAloneBytes=floor,
            removeEntireTacticalAndAppearanceBytes=no_agents,
            removeEntireTacticalAndAppearanceAndOtherGunFinishesBytes=no_agents-optional_gun_bytes+retained_bytes,
            noAgentsScope='Conservative source-ownership budget for removing the entire tactical/appearance module, broader than agents alone. Retains shared core assets. Requires bundle initialization and dormant world-data support; direct DLL deletion is not sufficient.',
            caveat='Deletion budgets only, before tiny ZIP header savings. No catalogue/fallback code changes; NOT valid playable packages. Cosmetic IDs and saved values must be preserved by a future minimal edition.')

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('package',type=Path);p.add_argument('report',type=Path)
    a=p.parse_args();r=audit(a.package);a.report.write_text(json.dumps(r,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(r,ensure_ascii=False,indent=2))
