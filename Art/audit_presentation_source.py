"""Read-only parity check for the supplied environment during lighting edits."""
import hashlib
import json
import re
from pathlib import Path

root=Path(__file__).resolve().parent.parent
before=root/'Documentation/Backups/GameFeelRefresh/OfficeGarage.unity'
after=root/'Assets/_Project/Scenes/OfficeGarage.unity'

def transforms(path):
    text=path.read_text(encoding='utf-8-sig')
    records={}
    names={}
    for match in re.finditer(r'^--- !u!(\d+) &(\d+).*?\n(.*?)(?=^--- !u!|\Z)',text,re.M|re.S):
        kind,identifier,body=match.groups()
        if kind=='1':
            name=re.search(r'^  m_Name: (.*)$',body,re.M)
            if name: names[identifier]=name.group(1)
        if kind=='4':
            game_object=re.search(r'm_GameObject: \{fileID: (\d+)\}',body)
            if game_object:
                values={field:re.search(r'^  '+field+r': (.*)$',body,re.M).group(1)
                    for field in ('m_LocalRotation','m_LocalPosition','m_LocalScale','m_Father')}
                records[identifier]=(game_object.group(1),values)
    return {identifier:(names.get(go,''),values) for identifier,(go,values) in records.items()}

old,new=transforms(before),transforms(after)
# Runtime upgrade copies are generated furniture, not protected source objects.
# Their names reuse G01/O28 but must remain independently repairable.
source={key:value for key,value in old.items()
        if re.match(r'^["\']?(?:A|O|T|G|V)\d\d',value[0]) and '(Clone)' not in value[0]}
changed=[name for key,(name,values) in source.items() if key not in new or new[key][1]!=values]
model=root/'Assets/_Project/Art/Models/Environment/Environment_OfficeGarage.fbx'
digest=hashlib.sha256(model.read_bytes()).hexdigest().upper()
report={'source_transforms_checked':len(source),'changed_source_transforms':changed,
    'environment_fbx_sha256':digest,
    'environment_fbx_unchanged':digest=='2DA10567574E9A75FE2C0C9AF79F9AD0FC57F7D262898874BF8E10733980BD4C'}
(root/'Logs/presentation_source_parity.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
if changed or not report['environment_fbx_unchanged'] or not source:
    raise SystemExit('Source environment parity failed')
