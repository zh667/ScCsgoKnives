"""Build self-contained Full / Optimized512 scmods from the current Release build.

Use dev.ps1. Full first, then derive resources with optimize_weapon_resources.py,
then Optimized512. Both editions ship identical gameplay DLL bytes.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
import zipfile
from pack_scmod import ROOT, SOURCE, BUILD, OUT, check_catalog, newest_source_mtime, is_resource_asset


def sha(data):
    return hashlib.sha256(data).hexdigest()


def filename(version, edition):
    label = '全量版' if edition == 'Full' else '512轻量版'
    return OUT / f'[API1.9]CS武器{version}-{label}.scmod'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--edition', choices=['Full', 'Optimized512'], required=True)
    parser.add_argument('--stage', type=Path, default=ROOT/'.tmp/standalone110-derived')
    parser.add_argument('--report', type=Path, default=OUT/'release-1.1.0')
    args = parser.parse_args()
    args.report.mkdir(parents=True, exist_ok=True)
    meta = json.loads((BUILD/'modinfo.json').read_text('utf-8-sig'))
    if meta.get('Dependencies'):
        raise SystemExit('Standalone package must not require an external resource mod.')
    version = meta['Version']
    if (BUILD/'ScCsgoKnives.dll').stat().st_mtime < newest_source_mtime():
        raise SystemExit('Rebuild current sources before packaging.')
    check_catalog()
    if args.edition == 'Full':
        entries = {n: (BUILD/n).read_bytes() for n in [
            'ScCsgoKnives.dll', 'ScCsgoResources.dll', 'modinfo.json',
            'LICENSE', 'THIRD_PARTY_NOTICES.md', 'ASSET_SOURCES.md']}
        for p in sorted((SOURCE/'Assets').rglob('*')):
            if not p.is_file():
                continue
            n = p.relative_to(SOURCE).as_posix()
            if (BUILD/n).read_bytes() != p.read_bytes():
                raise SystemExit('Stale built asset: '+n)
            entries[n] = p.read_bytes()
    else:
        with zipfile.ZipFile(filename(version, 'Full')) as archive:
            entries = {n: archive.read(n) for n in archive.namelist()}
        full_dll = entries['ScCsgoKnives.dll']
        if full_dll != (BUILD/'ScCsgoKnives.dll').read_bytes():
            raise SystemExit('Full and Lite must use the same current DLL.')
        rows = json.loads((args.report/'assets.json').read_text('utf-8'))
        derived = {r['path']: r for r in rows}
        for n in list(entries):
            if not (n.endswith('.png') or n.startswith('Assets/Models/') and n.endswith('.obj')):
                continue
            row = derived[n]
            if sha(entries[n]) != row['sourceSha256']:
                raise SystemExit('Derived asset source differs: '+n)
            target = n[:-4]+'.webp' if n.endswith('.png') else n
            b = (args.stage/target).read_bytes()
            if sha(b) != row['sha256']:
                raise SystemExit('Derived bytes differ: '+target)
            del entries[n]
            entries[target] = b
        for p in (SOURCE/'AnimationData').iterdir():
            if p.name.endswith('.cs2.animation.json'):
                assert (args.stage/'AnimationData'/p.name).read_bytes() == p.read_bytes()
            elif p.suffix in ('.skin', '.parts'):
                row = derived['AnimationData/'+p.name]
                assert row['sourceSha256'] == sha(p.read_bytes())
        resource_meta = json.loads((ROOT/'src/ScCsgoResources/modinfo.json').read_text('utf-8'))
        project = args.stage/'ScCsgoResources.csproj'
        project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<TargetFramework>net10.0</TargetFramework><AssemblyName>ScCsgoResources</AssemblyName>
<RootNamespace>ScCsgoResources</RootNamespace><Version>{resource_meta['Version']}</Version>
<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
<InformationalVersion>{resource_meta['Version']}+standalone-512</InformationalVersion>
<Deterministic>true</Deterministic><GenerateDependencyFile>false</GenerateDependencyFile>
</PropertyGroup><ItemGroup><EmbeddedResource Include="AnimationData/*.cs2.animation.json;AnimationData/*.skin;AnimationData/*.parts">
<LogicalName>Game.AnimationData.%(Filename)%(Extension)</LogicalName></EmbeddedResource>
</ItemGroup></Project>''', encoding='utf-8')
        (args.stage/'ResourceMarker.cs').write_bytes((ROOT/'src/ScCsgoResources/ResourceMarker.cs').read_bytes())
        subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '--verbosity', 'quiet'], check=True)
        entries['ScCsgoResources.dll'] = (args.stage/'bin/Release/net10.0/ScCsgoResources.dll').read_bytes()
        entries['Assets/ScCsgoDerivedResources.json'] = (args.report/'assets.json').read_bytes()
        meta['Name'] = 'CS武器 · 512轻量版'
        meta['Description'] = '内置全部玩法、动画和音频；512 上限 WebP Q85 纹理、既有模型减面方案，特殊图集保留。与全量版二选一安装，无需资源前置包。'
        entries['modinfo.json'] = (json.dumps(meta, ensure_ascii=False, indent=2)+'\n').encode('utf-8')
        assert entries['ScCsgoKnives.dll'] == full_dll
    resource_version = json.loads((ROOT/'src/ScCsgoResources/modinfo.json').read_text('utf-8'))['Version']
    entries['Assets/ScCsgoKnivesEdition.xml'] = f'<Edition Name="{args.edition}" />\n'.encode()
    marker = ET.Element('Resources', Version=resource_version, Format='1', Edition=args.edition)
    for name, data in sorted(entries.items()):
        if is_resource_asset(name):
            ET.SubElement(marker, 'File', Path=name, Sha256=sha(data))
    entries['Assets/ScCsgoResources.xml'] = ET.tostring(marker, encoding='utf-8')
    target = filename(version, args.edition)
    pending = target.with_suffix('.pending')
    with zipfile.ZipFile(pending, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(entries.items()):
            item = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            item.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(item, data)
    pending.replace(target)
    result = dict(path=target.name, version=version, edition=args.edition,
        bytes=target.stat().st_size, sha256=sha(target.read_bytes()),
        coreDllSha256=sha(entries['ScCsgoKnives.dll']), resourceDllSha256=sha(entries['ScCsgoResources.dll']),
        entries=len(entries), resourceVersion=resource_version)
    (args.report/f'{args.edition}.json').write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n','utf-8')
    print(json.dumps(result, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
