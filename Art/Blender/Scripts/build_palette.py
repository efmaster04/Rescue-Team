import glob, json, os
from pathlib import Path

root = Path(r'C:\Users\uur\oyunnn\Assets\_Project\Art')
materials = {}
for file in glob.glob(str(root / 'Models' / '**' / '*.materials.json'), recursive=True):
    with open(file, encoding='utf-8') as f:
        materials.update(json.load(f))
out = root / 'material_palette.tsv'
with open(out, 'w', encoding='utf-8') as f:
    for name, info in sorted(materials.items()):
        texture = os.path.basename(info['tex']) if info.get('tex') else ''
        vals = [name, *info['base'], info['rough'], info['metal'], info['alpha'], texture]
        f.write('\t'.join(str(v) for v in vals) + '\n')
print(len(materials), 'materials', out)
