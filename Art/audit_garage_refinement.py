"""Read-only proof that the workshop pass leaves the supplied office intact."""
import hashlib
import json
import re
from pathlib import Path

root=Path(__file__).resolve().parent.parent

def records(path):
    text=path.read_text(encoding='utf-8-sig')
    names={}; renderers={}; meshes={}
    for kind,identifier,body in re.findall(r'^--- !u!(\d+) &(\d+).*?\n(.*?)(?=^--- !u!|\Z)',text,re.M|re.S):
        if kind=='1':
            match=re.search(r'^  m_Name: (.*)$',body,re.M)
            if match: names[identifier]=match.group(1)
        elif kind in ('23','33'):
            go=re.search(r'm_GameObject: \{fileID: (\d+)\}',body).group(1)
            if kind=='23':
                renderers[go]=re.search(r'^  m_Materials:\n((?:  - .*\n)*)',body,re.M).group(1)
            else:
                meshes[go]=re.search(r'^  m_Mesh: (.*)$',body,re.M).group(1)
    protected={go:name for go,name in names.items() if re.match(r'^O\d\d|^A(?:01|06|11|12|13|14|15|21|22|34)_',name)
        or (name.startswith('A03_') and 'garage side' not in name)}
    return {go:(name,renderers.get(go),meshes.get(go)) for go,name in protected.items() if go in renderers}

before=records(root/'Documentation/Backups/GarageDoorRefinement/OfficeGarage.unity')
after=records(root/'Assets/_Project/Scenes/OfficeGarage.unity')
changed=[value[0] for go,value in before.items() if after.get(go)!=value]
model=root/'Assets/_Project/Art/Models/Environment/Environment_OfficeGarage.fbx'
digest=hashlib.sha256(model.read_bytes()).hexdigest().upper()
report={'protected_office_renderers_checked':len(before),'changed_office_mesh_or_material_references':changed,
    'source_fbx_unchanged':digest=='2DA10567574E9A75FE2C0C9AF79F9AD0FC57F7D262898874BF8E10733980BD4C'}
(root/'Logs/garage_office_preservation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
if not before or changed or not report['source_fbx_unchanged']:
    raise SystemExit('Office preservation audit failed')
