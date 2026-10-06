"""Compile current core/addon with actual split ownership, without copying bulk assets."""
import subprocess, xml.etree.ElementTree as ET
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.tmp/dev-temp/subworld-airdrops-20261006/split'

def main():
    for name in ['ScCsgoKnives','ScCsgoTactical']:
        source=ROOT/'src'/name;folder=STAGE/name;folder.mkdir(parents=True,exist_ok=True)
        tree=ET.parse(source/(name+'.csproj'));root=tree.getroot()
        early=ET.Element('PropertyGroup');ET.SubElement(early,'ScResourceZstd').text='true';root.insert(0,early)
        for group in list(root):
            if group.tag=='Target':root.remove(group);continue
            for item in list(group):
                if item.tag=='None':group.remove(item);continue
                if item.tag in ('Compile','EmbeddedResource','ProjectReference'):
                    for attr in ['Include','Update','Exclude']:
                        if item.get(attr):item.set(attr,';'.join(str(source/p.replace('\\','/')) for p in item.get(attr).split(';')))
                if item.tag=='ProjectReference' and 'ScCsgoKnives.csproj' in item.get('Include',''):
                    item.set('Include',str(STAGE/'ScCsgoKnives/ScCsgoKnives.csproj'))
        props=ET.SubElement(root,'PropertyGroup')
        ET.SubElement(props,'DefineConstants').text='SC_SPLIT;SC_RESOURCE_ZSTD'
        ET.SubElement(props,'EnableDefaultCompileItems').text='false'
        ET.SubElement(props,'SkipScmodPackaging').text='true'
        compile=ET.SubElement(root,'ItemGroup')
        if name=='ScCsgoKnives':ET.SubElement(compile,'Compile',Include=str(ROOT/'src/ScCsgoTactical/TacticalBlocks.cs'))
        else:ET.SubElement(compile,'Compile',Include=str(source/'*.cs'),Exclude=str(source/'TacticalBlocks.cs'))
        tree.write(folder/(name+'.csproj'),encoding='utf-8',xml_declaration=True)
    command=['dotnet','build',str(STAGE/'ScCsgoTactical/ScCsgoTactical.csproj'),'-c','Release','-v:q']
    result=subprocess.run(command,cwd=ROOT,capture_output=True,text=True,encoding='utf8',errors='replace')
    (STAGE/'build.log').write_text(result.stdout+result.stderr,'utf8')
    print(result.stdout[-3000:]);return result.returncode

if __name__=='__main__':raise SystemExit(main())
