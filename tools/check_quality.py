"""Scoped, non-installing checks. Run on Windows through tools/dev.ps1.

Every executed command has a log, exit code, duration and artifact identities. Missing
dependencies and unselected checks are not passes. Never runs packaging/install/cleanup tools.
"""
import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--group', choices=['core', 'builds', 'behavior', 'mp-quality', 'mp', 'variants', 'all'], default='all')
parser.add_argument('--out', type=Path, default=ROOT / '.tmp/dev-temp/quality-check')
parser.add_argument('--mp-refs', type=Path)
parser.add_argument('--mods', type=Path, help='Read-only API 1.9.3.1 Mods inputs for Sushi regression')
parser.add_argument('--appearance-refs', type=Path, help='Original sc-nekomekomodel.dll and neorxna.dll for offline integration')
args = parser.parse_args()
OUT = args.out.resolve()
OUT.mkdir(parents=True, exist_ok=True)
rows = []
built = {}
PRODUCTS = ['ScCsgoResources', 'ScCsgoKnives', 'ScCsgoBundle', 'ScCsgoTactical', 'ScCsgoAppearance',
            'ScCsgoVoice', 'ScCsgoDeathmatch', 'ScCsgoResourceCodec', 'ScCsgoNet', 'ScCsgoNetCompat']
GROUPS = ['core', 'builds', 'behavior', 'mp', 'variants']
requested = set(GROUPS if args.group == 'all' else ['mp'] if args.group == 'mp-quality' else [args.group])


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save():
    (OUT / 'execution.json').write_text(json.dumps({'group': args.group, 'results': rows,
        'products': PRODUCTS,
        'notExecuted': ['real Windows game', 'real multiplayer', 'Android', 'user observation', 'release compatibility matrix']}, indent=2), encoding='utf-8')


def run(name, command):
    command = [str(x) for x in command]
    started = time.monotonic()
    with (OUT / (name + '.log')).open('w', encoding='utf-8') as log:
        result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
    rows.append(dict(name=name, status='passed' if result.returncode == 0 else 'failed',
                     command=command, exit=result.returncode, seconds=round(time.monotonic()-started, 3)))
    save()
    print(name, rows[-1]['status'], rows[-1]['seconds'], flush=True)
    if result.returncode:
        print((OUT / (name + '.log')).read_text('utf-8')[-3500:], flush=True)
    return result.returncode == 0


def unavailable(name, detail, status='missing-dependency'):
    rows.append(dict(name=name, status=status, detail=detail)); save()
    print(name, status, detail, flush=True)


def embedded(project):
    for item in ET.parse(project).findall('.//EmbeddedResource'):
        for pattern in item.attrib['Include'].split(';'):
            yield from project.parent.glob(pattern.replace('\\', '/'))


def check_inputs():
    # Hash maintained code and the actual embedded/behavior data; do not traverse/copy entire Assets trees.
    paths = set()
    for directory in ['src', 'tools']:
        for p in (ROOT / directory).rglob('*'):
            if p.is_file() and p.suffix in {'.cs', '.csproj', '.py', '.ps1', '.targets'} and not {'bin', 'obj', 'archive', 'reference', '__pycache__'}.intersection(p.relative_to(ROOT).parts):
                paths.add(p)
    for p in (ROOT / 'src').glob('*/*.csproj'):
        paths.update(embedded(p))
    paths.update((ROOT / 'tools/fixtures').rglob('*.gz'))
    paths.update((ROOT / 'src/ScCsgoKnives/Assets/Models/ScCsgoKnives').glob('*.obj'))
    paths.update((ROOT / 'src/ScCsgoVoice/Assets').rglob('*.ogg'))
    paths.add(ROOT / 'src/ScCsgoVoice/Assets/ScAgentVoices.json')
    paths.add(ROOT / '.github/workflows/build.yml')
    paths.add(ROOT / '.tmp/creature-audit-20260917/10/neorxna.dll')
    for directory in ['.tmp/nmm-player-appearance-audit-20260920', '.tmp/third-party-refs/ZstdSharp-2cd0c019693bc786a5fe5c3be94e107b24e7267e/src/ZstdSharp']:
        paths.update(p for p in (ROOT / directory).rglob('*.cs') if not {'bin', 'obj'}.intersection(p.relative_to(ROOT / directory).parts))
    identities = {str(p.relative_to(ROOT)): sha(p) for p in sorted(paths) if p.is_file()}
    (OUT / 'inputs.json').write_text(json.dumps(identities, indent=2), encoding='utf-8')
    return identities


def prerequisites(name):
    missing = []
    if name not in ['ScCsgoResourceCodec', 'ScCsgoNet', 'ScCsgoNetCompat', 'NetLoopCheck', 'InventoryCheck', 'AppearanceNetCheck']:
        if not any((ROOT / 'src/ScCsgoKnives/AnimationData').glob('*.skin')):
            missing.append('Windows AnimationData/*.skin (required real resource inputs)')
    if name == 'ScCsgoAppearance':
        if not (ROOT / '.tmp/creature-audit-20260917/10/neorxna.dll').is_file(): missing.append('original NEO reference')
        if not any((ROOT / '.tmp/nmm-player-appearance-audit-20260920').rglob('*.cs')): missing.append('existing NekoMeko reference sources')
    if name == 'ScCsgoResourceCodec' and not (ROOT / '.tmp/third-party-refs/ZstdSharp-2cd0c019693bc786a5fe5c3be94e107b24e7267e/src/ZstdSharp/Decompressor.cs').is_file():
        missing.append('pinned ZstdSharp source (no automatic fetch)')
    if name in ['BalanceCheck', 'InventoryCheck'] and not any((ROOT / 'src/ScCsgoKnives/Assets/Models/ScCsgoKnives').glob('*.obj')):
        missing.append('original source OBJ models')
    if name == 'FeedbackCheck' and not any((ROOT / 'src/ScCsgoVoice/Assets').rglob('*.ogg')): missing.append('original voice Ogg data')
    return missing


def build(name, folder='src', extra=()):
    key = (name, tuple(extra))
    if key in built: return built[key]
    built[key] = None
    if missing := prerequisites(name):
        unavailable('build-' + name, '; '.join(missing)); return None
    project = ROOT / folder / name / (name + '.csproj')
    properties = ['-p:SkipScmodPackaging=true', '-p:CustomAfterMicrosoftCommonTargets=' + str(ROOT / 'tools/quality-no-assets.targets'), *extra]
    if not run('build-' + name, ['dotnet', 'build', project, '-c', 'Release', '--nologo', '-v:minimal', *properties]):
        return None
    # Resolve the evaluated output, never infer net10.0 vs explicit OutputPath from a neighbouring build.
    command = ['dotnet', 'msbuild', str(project), '-p:Configuration=Release', '-getProperty:TargetPath', *properties]
    path = Path(subprocess.check_output(command, cwd=ROOT, text=True).strip())
    rows[-1]['artifact'] = dict(path=str(path), sha256=sha(path))
    rows[-1]['resolvedBy'] = command
    save()
    built[key] = path
    return path


def core():
    exe = build('QualityCheck', 'tools')
    if exe:
        run('quality-core', ['dotnet', exe, OUT / 'core.json'])
def multiplayer():
    origin = args.mp_refs.resolve() if args.mp_refs else None
    required = ['Survivalcraft.dll', 'Engine.dll', 'EntitySystem.dll', 'Survivalcraft.Multiplayer.dll']
    if origin is None or any(not (origin / n).is_file() for n in required):
        for name in ['build-ScCsgoNet', 'build-ScCsgoNetCompat', 'MP behavior']:
            unavailable(name, '--mp-refs must contain API 1.9.3.2_MP and Multiplayer DLLs')
    else:
        refs = OUT / 'mp-refs'; refs.mkdir(exist_ok=True)
        # Only managed/native dependency DLLs, never a resource tree or a copied world.
        for path in origin.glob('*.dll'):
            if not path.name.startswith('ScCsgo'):
                shutil.copy2(path, refs / path.name)
        products = {name: build(name) for name in ['ScCsgoKnives', 'ScCsgoTactical', 'ScCsgoDeathmatch']}
        resources = build('ScCsgoResources')
        if resources:
            shutil.copy2(resources, refs / resources.name)
        if all(products.values()):
            for path in products.values():
                shutil.copy2(path, refs / path.name)
            (OUT / 'references.json').write_text(json.dumps({p.name: sha(p) for p in refs.glob('*.dll')}, indent=2), encoding='utf-8')
            extra = ['-p:Refs=' + str(refs)]
            adapter = build('ScCsgoNet', extra=extra)
            compat = None
            if args.group != 'mp-quality':
                if (refs / 'Survivalcraft.CompatNet.dll').is_file(): compat = build('ScCsgoNetCompat', extra=extra)
                else: unavailable('build-ScCsgoNetCompat', 'Survivalcraft.CompatNet.dll required to compile the maintained optional adapter')
            if args.group == 'builds': return
            exe = build('NetLoopCheck', 'tools', extra)
            if adapter and exe:
                modes = ['quality'] if args.group == 'mp-quality' else ['transport', 'state', 'gunloop', 'dmloop', 'quality']
                for mode in modes:
                    tail = [] if mode == 'transport' else ['--' + mode, '--modules=' + ';'.join(str(refs / n) for n in ['ScCsgoTactical.dll', 'ScCsgoDeathmatch.dll'])]
                    run('net-' + mode, ['dotnet', exe, refs, adapter, '-', OUT / ('net-' + mode + '.json'), *tail])
                if args.group != 'mp-quality' and compat:
                    run('net-compat', ['dotnet', exe, refs, adapter, compat, OUT / 'net-compat.json', '--with-compatnet'])
            else: unavailable('MP behavior', 'adapter/runner build did not succeed', 'not-executed')
        else: unavailable('MP behavior', 'a required product build did not succeed', 'not-executed')


def behavior():
    for name in ['DeathmatchCheck', 'BalanceCheck', 'FeedbackCheck']:
        exe = build(name, 'tools')
        if exe: run(name, ['dotnet', exe, OUT / (name + '.json')])
        else: unavailable(name, 'runner build unavailable', 'not-executed')
    # The historical DLL regeneration matrix and optional private world probes have separate prerequisites.
    unavailable('historical-DLL regeneration / ProtectedLoadCheck', 'Requires original versioned binaries and separate explicit legacyRoot; fixed historical XML inputs do execute in BalanceCheck', 'not-executed')
    for name in ['World7-Project.bak', 'World-Project.xml', *[f'World{i}-Project.xml' for i in range(1, 7)]]:
        p = ROOT / '.tmp/migration-20260925' / name
        rows.append(dict(name='optional integrity copy/' + name,
                         status='passed' if p.is_file() and any(r['name'] == 'BalanceCheck' and r['status'] == 'passed' for r in rows) else 'not-executed',
                         detail='Covered by BalanceCheck only when that runner passes: ' + str(p), sha256=sha(p) if p.is_file() else None))
    if args.mods is None or not args.mods.is_dir():
        unavailable('InventoryCheck', '--mods API 1.9.3.1 Mods directory required for original Sushi provider')
    else:
        dll = build('ScCsgoKnives'); exe = build('InventoryCheck', 'tools')
        if dll and exe: run('InventoryCheck', ['dotnet', exe, dll, args.mods.resolve(), OUT / 'InventoryCheck.json'])
        else: unavailable('InventoryCheck', 'core/runner build unavailable', 'not-executed')
    original = args.appearance_refs.resolve() if args.appearance_refs else None
    if original is None or not all((original / n).is_file() for n in ['sc-nekomekomodel.dll', 'neorxna.dll']):
        unavailable('AppearanceNetCheck', '--appearance-refs must contain original NMM/NEO providers')
    else:
        refs = OUT / 'appearance-refs'; refs.mkdir(exist_ok=True)
        modules = [build(n) for n in ['ScCsgoKnives', 'ScCsgoResources', 'ScCsgoTactical', 'ScCsgoAppearance']]
        if all(modules):
            for p in [*modules, original / 'sc-nekomekomodel.dll', original / 'neorxna.dll']: shutil.copy2(p, refs / p.name)
            (OUT / 'appearance-inputs.json').write_text(json.dumps({p.name: sha(p) for p in refs.glob('*.dll')}, indent=2), encoding='utf-8')
            exe = build('AppearanceNetCheck', 'tools', ['-p:Refs=' + str(refs)])
            if exe: run('AppearanceNetCheck', ['dotnet', exe, OUT / 'appearance.json'])
        else: unavailable('AppearanceNetCheck', 'product build unavailable', 'not-executed')
    if shutil.which('pwsh'):
        run('dev-arguments', ['pwsh', '-NoProfile', '-File', ROOT / 'tools/check_dev_arguments.ps1', '-Report', OUT / 'dev-arguments.json'])
    else: unavailable('dev-arguments', 'PowerShell 7 required')


def variants():
    # The same supported type ownership as stage_split_build.py / stage_minimal_core.py,
    # linked to live source so no copied-source snapshot can drift or carry old files.
    core_dir = ROOT / 'src/ScCsgoKnives'
    sources = [p for p in core_dir.rglob('*.cs') if not {'bin', 'obj'}.intersection(p.relative_to(core_dir).parts)]
    resources = list(embedded(core_dir / 'ScCsgoKnives.csproj'))
    base = build('ScCsgoResources'); codec = build('ScCsgoResourceCodec')
    def project(label, assembly, files, constants, references, resource_files, resource_root):
        folder = OUT / 'variants' / label; folder.mkdir(parents=True, exist_ok=True)
        doc = ET.Element('Project', Sdk='Microsoft.NET.Sdk'); props = ET.SubElement(doc, 'PropertyGroup')
        for k, v in dict(TargetFramework='net10.0', AssemblyName=assembly, RootNamespace='Game', EnableDefaultCompileItems='false',
                         GenerateAssemblyInfo='false', Nullable='disable', LangVersion='preview', ImplicitUsings='enable', DefineConstants=constants).items():
            ET.SubElement(props, k).text = v
        group = ET.SubElement(doc, 'ItemGroup'); ET.SubElement(group, 'PackageReference', Include='SurvivalcraftAPI.Survivalcraft', Version='1.9.3.1')
        for p in files: ET.SubElement(group, 'Compile', Include=str(p))
        for p in references:
            reference = ET.SubElement(group, 'Reference', Include=p.stem)
            ET.SubElement(reference, 'HintPath').text = str(p)
            ET.SubElement(reference, 'Private').text = 'false'  # compile-only; do not duplicate the large immutable resource DLL
        for p in resource_files:
            ET.SubElement(ET.SubElement(group, 'EmbeddedResource', Include=str(p)), 'LogicalName').text = 'Game.' + p.relative_to(resource_root).as_posix().replace('/', '.')
        path = folder / (assembly + '.csproj'); ET.indent(doc); ET.ElementTree(doc).write(path, encoding='utf-8', xml_declaration=True)
        if not run('variant-' + label, ['dotnet', 'build', path, '-c', 'Release', '--nologo', '-v:minimal']): return None
        dll = folder / 'bin/Release/net10.0' / (assembly + '.dll'); rows[-1]['artifact'] = dict(path=str(dll), sha256=sha(dll)); save()
        return dll
    for label, constants in [('lite-deflate', 'SC_SPLIT'), ('lite-zstd', 'SC_SPLIT;SC_RESOURCE_ZSTD'), ('mini', 'SC_MINIMAL'), ('mini-inspect', 'SC_MINIMAL;SC_MINIMAL_INSPECT')]:
        if base is None or ('ZSTD' in constants and codec is None):
            unavailable('variant-' + label, 'resource/codec prerequisite unavailable', 'not-executed'); continue
        references = [base] + ([codec] if 'ZSTD' in constants else [])
        extra = [ROOT / 'src/ScCsgoTactical/TacticalBlocks.cs'] if 'SPLIT' in constants else []
        dll = project(label, 'ScCsgoKnives', sources + extra, constants, references, resources, core_dir)
        if 'SPLIT' in constants:
            if dll is None: unavailable('variant-' + label + '-agents', 'core variant failed', 'not-executed'); continue
            tactical = ROOT / 'src/ScCsgoTactical'
            project(label + '-agents', 'ScCsgoTactical', [p for p in tactical.glob('*.cs') if p.name != 'TacticalBlocks.cs'], constants, [dll],
                    [p for p in (tactical / 'ArmData').glob('*') if p.is_file()], tactical)


input_identities = check_inputs()
if not shutil.which('dotnet'):
    unavailable('all requested .NET checks', '.NET 10 SDK unavailable')
else:
    if 'builds' in requested:
        inventory = {p.stem for p in (ROOT / 'src').glob('*/*.csproj')}
        if inventory != set(PRODUCTS): unavailable('product inventory', 'Maintained csproj set changed; update explicit check inventory', 'failed')
        for name in PRODUCTS:
            if name not in ['ScCsgoNet', 'ScCsgoNetCompat']: build(name)
        if 'mp' not in requested: multiplayer()
    if 'core' in requested: core()
    if 'behavior' in requested: behavior()
    if 'mp' in requested: multiplayer()
    if 'variants' in requested: variants()
for group in set(GROUPS) - requested: unavailable('group/' + group, 'not selected', 'not-executed')
drift = [p for p, expected in input_identities.items() if not (ROOT / p).is_file() or sha(ROOT / p) != expected]
if drift: unavailable('input stability', 'Inputs changed while checks ran: ' + ', '.join(drift), 'failed')
else: rows.append(dict(name='input stability', status='passed', detail=f'{len(input_identities)} input hashes unchanged during execution'))

save()
sys.exit(1 if any(r['status'] == 'failed' for r in rows) else 2 if any(r['status'] == 'missing-dependency' for r in rows) else 0)
