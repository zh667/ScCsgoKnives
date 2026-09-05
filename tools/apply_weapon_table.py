"""Write AnimationData/weapon_table.json from CS:MC's decoded weapon registration table.

Source: CSMCReverse/work/decompile/cfr/.../ҷ.java (the registration rows) plus the
string table decoded at runtime by the harness (scratchpad/table_strings.txt, via
Decode2's Unsafe read of ҷ.a). Each row: hip offset xyz, aim offset xyz, roll degrees,
hip FOV, aim FOV (knife rows carry hip only, no roll, one FOV).

    python3 tools/apply_weapon_table.py [table_strings.txt]
"""
import json, os, re, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JAVA = '/home/dev/workspaces/CSMCReverse/work/decompile/cfr/me/fadeorite/csmcmod/ҷ.java'
STRINGS = sys.argv[1] if len(sys.argv) > 1 else '/tmp/claude-1000/-home-dev/584a761a-993d-4689-a30e-a9182a581a55/scratchpad/table_strings.txt'
OUT = os.path.join(ROOT, 'src/ScCsgoKnives/AnimationData/weapon_table.json')

def main():
    strs = {}
    for line in open(STRINGS, encoding='utf-8', errors='replace'):
        m = re.match(r'\s*\[(\d+)\] (.*)$', line)
        if m: strs[int(m.group(1))] = m.group(2)
    table = {}
    for line in open(JAVA, encoding='utf-8'):
        family = 'b$4bh.a(' in line
        if not family and 'b$4bg.a(' not in line: continue
        ids = [int(x) for x in re.findall(r'var8_6\[(\d+)\]', line)]
        names = [strs.get(k, '?') for k in ids[:3]]
        nums = [float(x[:-1]) for x in re.findall(r'-?\d+\.\d+f', line)]
        if len(nums) >= 9: hip, aim, roll, fov_hip, fov_aim = nums[-9:-6], nums[-6:-3], nums[-3], nums[-2], nums[-1]
        elif len(nums) >= 7: hip, aim, roll, fov_hip, fov_aim = nums[-7:-4], nums[-4:-1], 0.0, nums[-1], nums[-1]
        else: continue
        key = names[1]                     # csmcmod item id without the weapon_ prefix, e.g. knife_m9, ak47
        table[key] = {'Id': names[0], 'Model': names[2], 'Family': family, 'Hip': hip, 'Aim': aim, 'RollDegrees': roll, 'FovHip': fov_hip, 'FovAim': fov_aim}
    json.dump(table, open(OUT, 'w'), indent=1)
    print(f'{len(table)} rows -> {OUT}')
    for k in ('knife_m9', 'knife_karambit', 'knife_butterfly', 'ak47', 'awp', 'm4a1_silencer'):
        print(' ', k, table.get(k))
    fam = [k for k, v in table.items() if v['Family']]; print('  family rows:', fam, [table[k]['Hip'] for k in fam])

if __name__ == '__main__':
    main()
