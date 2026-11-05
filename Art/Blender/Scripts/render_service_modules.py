"""Preview renders of service_modules.blend (Cycles, GPU when available).

Run:  blender -b Art/Blender/Models/ServiceModules/service_modules.blend -P render_service_modules.py
Env:  SM_OUT=<folder> (default Documentation), SM_QUICK=1 draft, SM_SHOTS=a,b subset.
"""
import bpy, os, math
from pathlib import Path
from mathutils import Vector

REPO = Path(__file__).resolve().parents[3]
OUT = Path(os.environ.get('SM_OUT', REPO/'Documentation'))
QUICK = os.environ.get('SM_QUICK') == '1'
ONLY = [s for s in os.environ.get('SM_SHOTS', '').split(',') if s]
scene = bpy.context.scene
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences
    for kind in ('OPTIX', 'CUDA'):
        try:
            prefs.compute_device_type = kind; prefs.get_devices()
            if any(d.type == kind for d in prefs.devices):
                for d in prefs.devices: d.use = d.type == kind
                scene.cycles.device = 'GPU'; break
        except TypeError:
            continue
except KeyError:
    pass
scene.cycles.samples = 16 if QUICK else 96
scene.render.resolution_percentage = 50 if QUICK else 100
cam = scene.camera
O = bpy.data.objects
IDS = ('towing', 'water', 'fire', 'tech', 'cargo')
NAMES = {'towing': 'cekici', 'water': 'su_tahliye', 'fire': 'yangin', 'tech': 'teknik_servis', 'cargo': 'nakliye'}
roots = {id: O[f'Modul | {id}'] for id in IDS}
centre = Vector((0, 0, .22))
LODV = int(os.environ.get('SM_LOD', '0'))
if LODV:
    for id in IDS:
        for c in roots[id].children:
            if c.data and c.data.name.startswith(id+'_LOD'): c.data = bpy.data.meshes[f'{id}_LOD{LODV}']

def shot(name, target, direction, scale, w=1400, h=1000):
    if ONLY and name not in ONLY: return
    scene.render.resolution_x, scene.render.resolution_y = w, h
    d = Vector(direction).normalized()
    cam.location = Vector(target)+d*6
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cam.data.ortho_scale = scale
    scene.render.filepath = str(OUT/f'servis_modulu_{name}{f"_lod{LODV}" if LODV else ""}{"_taslak" if QUICK else ""}.png')
    bpy.ops.render.render(write_still=True)
    print('RENDERED', name, flush=True)

FRONT_RIGHT = (.95, 1.45, .70)     # same side as the Unity preview camera
FRONT_LEFT = (-1.2, 1.35, .75)     # service port side
REAR = (.9, -1.4, .8)
for id in IDS:
    c = roots[id].location+centre
    shot(NAMES[id], c, FRONT_RIGHT, .98)
    shot(NAMES[id]+'_port_tarafi', c, FRONT_LEFT, .98)
keep = {id: roots[id].location.copy() for id in IDS}
for i, id in enumerate(IDS): roots[id].location.x = (i-2)*.78
shot('hepsi', Vector((0, 0, .2)), (.32, 1.5, .58), 3.75, 1800, 760)
for id in IDS: roots[id].location = keep[id]
shot('arka', roots['water'].location+centre, REAR, .98)
