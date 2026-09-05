"""Append genuine CS2 light-hit and heavy-hit/miss clips without rewriting existing poses."""
import argparse
import hashlib
import json
from pathlib import Path

import cs2_knife_rig as knife
import cs2_viewmodel as vm

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--exports', required=True, type=Path)
    args = parser.parse_args()
    vm.ROOTS = [args.exports / '08_first_person', args.exports / '09_knives']
    vm.ANALYSIS = vm.ROOTS[0]
    vm.ANIM = vm.ANALYSIS / 'decompiled/animation'
    vm.CLIPS = vm.ANIM / 'anims/viewmodel'
    aliases = [('stab', ['heavy_miss1_%s']), ('stabHit', ['heavy_hit1_%s']),
               ('slashHit1', ['light_hit1_%s']), ('slashHit2', ['light_hit2_%s'])]
    knife.ALIASES.extend(aliases)
    audit = []
    for name in knife.knife_names():
        converted = knife.convert(name)
        path = knife.DATA / (name + '.cs2.animation.json')
        existing = json.loads(path.read_text('utf-8'))
        assert existing['Skeleton'] == converted['Skeleton'], name + ': changed skeleton'
        wanted = {a for a, _ in aliases}
        additions = {k: v for k, v in converted['Clips'].items() if v.get('Alias') in wanted}
        if 'stabHit' not in {v['Alias'] for v in additions.values()}:
            heavy = next(v for v in additions.values() if v['Alias'] == 'stab')
            hit = dict(heavy, Alias='stabHit')
            additions[heavy['SourceName'] + '_hit_alias'] = hit
            existing['Source']['heavyHitUsesMissClip'] = True
        assert {v['Alias'] for v in additions.values()} == wanted, name + ': incomplete attacks'
        existing['Clips'].update(additions)
        existing['Source']['survivalAttackClips'] = list(additions)
        existing['Source']['clipsNotUsed'] = [s for s in existing['Source'].get('clipsNotUsed', []) if s not in additions]
        path.write_bytes(json.dumps(existing, ensure_ascii=False, separators=(',', ':')).encode('utf-8'))
        cfg = knife.config(name)
        for stem, clip in additions.items():
            source = vm.clip_path(cfg['folder'], clip['SourceName'])
            audit.append({'knife': name, 'alias': clip['Alias'], 'source': source.relative_to(args.exports).as_posix(),
                          'sha256': hashlib.sha256(source.read_bytes()).hexdigest(), 'duration': clip['Duration'], 'events': clip['Events']})
        print(name, 'appended', len(additions), flush=True)
    (knife.ROOT / 'docs/survival-knife-attack-sources.json').write_bytes(json.dumps(audit, ensure_ascii=False, indent=2).encode('utf-8'))

if __name__ == '__main__':
    main()
