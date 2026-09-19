"""Stable independent entity/subsystem templates; no vanilla template replacement."""
from pathlib import Path
import uuid,xml.etree.ElementTree as E
root=Path(__file__).resolve().parent.parent
def node(parent,tag,name,inherit=None,**attrs):
    path=parent.get('Guid','root')+'/'+tag+'/'+name
    n=E.SubElement(parent,tag,Name=name,Guid=str(uuid.uuid5(uuid.NAMESPACE_URL,'zh667.ScCsgoKnives/'+path)),**attrs)
    if inherit:n.set('InheritanceParent',inherit)
    return n
def params(parent,values):
    for name,(value,typ) in values.items():node(parent,'Parameter',name,Value=str(value),Type=typ)
r=E.Element('Mod')
p=E.SubElement(r,'ProjectTemplate',Name='Project',Guid='85023bf8-1c90-4dd1-9442-e6c13691d078')
s=node(p,'MemberSubsystemTemplate','ScChicken','fefb9590-4972-4893-b02a-76063611b745');params(s,{'Class':('Game.SubsystemScChicken','string')})
c=node(r,'EntityTemplate','ScCsgoChicken','548cf071-2593-45c2-b1c5-623b7c473612')
groups={
 'Body':{'BoxSize':('0.48,0.58,0.55','Vector3'),'Mass':(2,'float'),'Density':(.8,'float')},
 'Creature':{'DisplayName':('CS 小鸡','string'),'Description':('不会主动攻击。按 E／交互键切换跟随。枪杀会爆炸，刀杀正常死亡。','string'),'Category':('LandOther','Game.CreatureCategory'),'KillVerbs':('pecked','string')},
 'Locomotion':{'WalkSpeed':(3,'float'),'FlySpeed':(0,'float'),'SwimSpeed':(.5,'float'),'TurnSpeed':(8,'float'),'JumpSpeed':(4,'float')},
 'Health':{'AttackResilience':(3,'float'),'FireResilience':(3,'float'),'CorpseDuration':(2,'float')},
 'CreatureSounds':{'IdleSound':('Audio/ScCsgoKnives/Chicken/idle','string'),'IdleSoundMinDistance':(3,'float'),'PainSound':('','string'),'MoanSound':('','string'),'AttackSound':('','string'),'RareIdleSound':('','string')},
 'FlightlessBirdModel':{'ModelName':('Models/ScCsgoKnives/chicken','string'),'TextureOverride':('Textures/ScCsgoKnives/chicken','string'),'AnimationConfigPath':('Animations/ScChicken','string'),'ModelScale':(1.6,'float'),'BoundingSphereRadius':(1.28,'float'),'WalkBobHeight':(0,'float'),'WalkLegsAngle':(0,'float')},
 'LayEggBehavior':{'LayFrequency':(0,'float')},
}
for name,values in groups.items():params(node(c,'MemberComponentTemplate',name),values)
params(node(c,'ParameterSet','CreatureEggData'),{'EggTypeIndex':(-1,'int'),'ShowEgg':('False','bool')})
params(node(c,'MemberComponentTemplate','ScChicken','b05700ed-7e4e-4679-98f5-b597f421496b'),{'Class':('Game.ComponentScChicken','string')})
loot=node(c,'MemberComponentTemplate','Loot')
params(node(loot,'ParameterSet','Loot'),{'1':('RawBirdBlock;1;1','string'),'2':('FeatherBlock;1;2','string')})
params(node(loot,'ParameterSet','LootOnFire'),{'1':('CookedBirdBlock;1;1','string')})
E.indent(r,space='  ')
(root/'src/ScCsgoKnives/Assets/ScChicken.xdb').write_text(E.tostring(r,encoding='unicode')+'\n',encoding='utf-8')
