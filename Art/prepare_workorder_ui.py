"""Fetch the upstream type/icon assets and rasterize Phosphor at 4x UI size."""
from pathlib import Path
import hashlib
import json
import subprocess
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
FONTS = ROOT / 'Assets/_Project/Resources/Fonts'
SOURCES = ROOT / 'Art/UI/Phosphor'
ICONS = ROOT / 'Assets/_Project/Resources/OfficeIcons'
NODE = r'C:\Users\uur\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
SHARP = 'C:/Users/uur/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp'
for folder in (FONTS, SOURCES, ICONS):
    folder.mkdir(parents=True, exist_ok=True)
manifest = []

def fetch(url, path):
    data = urllib.request.urlopen(url, timeout=40).read()
    path.write_bytes(data)
    manifest.append(dict(path=str(path.relative_to(ROOT)), url=url, sha256=hashlib.sha256(data).hexdigest()))

for weight in ('Regular', 'Semibold'):
    fetch(f'https://raw.githubusercontent.com/adobe-fonts/source-sans/release/TTF/SourceSans3-{weight}.ttf', FONTS / f'SourceSans3-{weight}.ttf')
fetch('https://raw.githubusercontent.com/adobe-fonts/source-sans/release/LICENSE.md', FONTS / 'SourceSans3-OFL.md')
fetch('https://raw.githubusercontent.com/phosphor-icons/core/main/LICENSE', SOURCES / 'LICENSE.txt')
names = ['clipboard-text', 'truck', 'package', 'toolbox', 'calculator', 'buildings', 'envelope-simple',
         'desktop', 'floppy-disk', 'power', 'minus', 'square', 'x', 'check', 'circle', 'wrench',
         'tire', 'hammer', 'clock', 'map-pin', 'arrow-left', 'gear-six', 'keyboard', 'shield-check', 'file-text']
for name in names:
    fetch(f'https://raw.githubusercontent.com/phosphor-icons/core/main/assets/regular/{name}.svg', SOURCES / f'{name}.svg')
script = """
const fs=require('fs'); const sharp=require(process.argv[1]);
const source=process.argv[2], target=process.argv[3];
(async()=>{ for(const f of fs.readdirSync(source).filter(f=>f.endsWith('.svg'))) {
 let svg=fs.readFileSync(source+'/'+f,'utf8').replaceAll('currentColor','#ffffff');
 svg=svg.replaceAll('fill="black"','fill="#ffffff"').replaceAll('fill="#000"','fill="#ffffff"');
 await sharp(Buffer.from(svg)).resize(192,192).png().toFile(target+'/'+f.replace('.svg','.png'));
}})().catch(e=>{process.stderr.write(String(e));process.exit(1)});
"""
subprocess.run([NODE, '-e', script, SHARP, str(SOURCES), str(ICONS)], check=True)
(ROOT / 'Art/UI/asset-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(f'Prepared 2 Source Sans 3 fonts and {len(names)} Phosphor Regular icons.')
