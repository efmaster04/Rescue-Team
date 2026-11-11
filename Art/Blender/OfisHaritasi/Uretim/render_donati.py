# -*- coding: utf-8 -*-
"""
Donati onizlemeleri. build_donati.py'nin yazdigi Donati.blend uzerinde calisir; .blend'i degistirmez.

  blender.exe -b Kaynak/Blender/Donati.blend -P render_donati.py -- [--quick] [--only A,B] [--no-singles]
          [--no-toplu] [--no-kose]

Cycles CPU, 48 ornek + denoise, 1400x950; gri studyo zemini + iki yumusak alan isigi (servis minibusu gibi).
"""
import bpy
import math
import sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(r"C:\Users\uur\RescueTeam_OfisHaritasi")
OUT = ROOT / "Kaynak" / "Onizlemeler" / "Donati"
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
QUICK = "--quick" in ARGS
ONLY = None
for i, a in enumerate(ARGS):
    if a == "--only":
        ONLY = set(ARGS[i + 1].split(","))
OUT.mkdir(parents=True, exist_ok=True)

scene = bpy.context.scene
vl = bpy.context.view_layer
ASSET_COLLS = [c for c in scene.collection.children]
RES = (1400, 950)

# ---------------------------------------------------------------- studyo
stage = bpy.data.collections.new('ONIZLEME_SAHNE')
scene.collection.children.link(stage)


def link(o):
    stage.objects.link(o)
    return o


def pmat(name, color, rough=0.9, metallic=0.0):
    m = bpy.data.materials.new(name)
    bs = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value = (*color, 1)
    bs.inputs['Roughness'].default_value = rough
    bs.inputs['Metallic'].default_value = metallic
    return m


floor_mat = pmat('onizleme zemin', (0.48, 0.46, 0.41), 0.93)
me = bpy.data.meshes.new('onizleme zemin')
S = 400
me.from_pydata([(-S, -S, -0.002), (S, -S, -0.002), (S, S, -0.002), (-S, S, -0.002)], [], [(0, 1, 2, 3)])
me.materials.append(floor_mat)
floor = link(bpy.data.objects.new('onizleme zemin', me))

world = bpy.data.worlds.new('onizleme dunya')
scene.world = world
bg = world.node_tree.nodes['Background']
bg.inputs['Color'].default_value = (0.70, 0.69, 0.65, 1)
bg.inputs['Strength'].default_value = 0.48

lights = []
for nm, col in [('anahtar', (1.0, 0.88, 0.74)), ('dolgu', (0.80, 0.88, 1.0))]:
    ld = bpy.data.lights.new(nm, 'AREA')
    ld.shape = 'DISK'
    ld.color = col
    lights.append(link(bpy.data.objects.new(nm, ld)))

camd = bpy.data.cameras.new('onizleme kamera')
cam = link(bpy.data.objects.new('onizleme kamera', camd))
scene.camera = cam
camd.clip_start = 0.05
camd.clip_end = 2000

r = scene.render
r.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 12 if QUICK else 48
scene.cycles.use_denoising = True
r.resolution_x, r.resolution_y = RES
r.resolution_percentage = 50 if QUICK else 100
r.image_settings.file_format = 'PNG'
scene.view_settings.view_transform = 'AgX'


def look(o, target):
    o.rotation_euler = (Vector(target) - o.location).to_track_quat('-Z', 'Y').to_euler()


def place_lights(center, size):
    d = 1.8 * size + 2.0
    for o, off, k in [(lights[0], Vector((-0.55, -0.75, 0.95)), 11.0), (lights[1], Vector((0.6, 0.55, 0.75)), 6.5)]:
        o.location = Vector(center) + off.normalized() * d
        o.data.size = 0.6 * d
        o.data.energy = k * d * d
        look(o, center)


def vis_objects(coll):
    return [o for o in coll.all_objects if o.type == 'MESH' and not o.name.split('|')[-1].startswith('COL_')]


def bounds(objs, clamp_ground=True):
    mn = Vector((1e9, 1e9, 1e9))
    mx = -mn
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            mn = Vector(map(min, mn, w))
            mx = Vector(map(max, mx, w))
    if clamp_ground:
        mn.z = max(mn.z, 0.0)
    return mn, mx


def fit_ortho(mn, mx, direction=Vector((0.62, -1.0, 0.50)), margin=1.14):
    c = (mn + mx) / 2
    size = (mx - mn).length
    cam.data.type = 'ORTHO'
    cam.location = c + direction.normalized() * (size * 2 + 20)
    look(cam, c)
    vl.update()
    R, Uv = cam.matrix_world.col[0].xyz, cam.matrix_world.col[1].xyz
    xs, ys = [], []
    for i in range(8):
        p = Vector((mx.x if i & 1 else mn.x, mx.y if i & 2 else mn.y, mx.z if i & 4 else mn.z)) - c
        xs.append(p.dot(R))
        ys.append(p.dot(Uv))
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    asp = RES[0] / RES[1]
    cam.data.ortho_scale = max(w, h * asp) * margin
    # cerceveyi dikeyde ortala
    cam.data.shift_x = -((max(xs) + min(xs)) / 2) / cam.data.ortho_scale
    cam.data.shift_y = -((max(ys) + min(ys)) / 2) / cam.data.ortho_scale
    place_lights(c, max(mx - mn))


def show_only(colls):
    for lc in vl.layer_collection.children:
        if lc.collection in ASSET_COLLS:
            lc.exclude = lc.collection not in colls


def render(path):
    r.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    print("RENDER", path)


SIG = {'DN_Sinyal_Kirmizi': (1.0, 0.10, 0.05), 'DN_Sinyal_Sari': (1.0, 0.60, 0.08), 'DN_Sinyal_Yesil': (0.12, 1.0, 0.55)}


def signals(on):
    for n, c in SIG.items():
        bs = next(x for x in bpy.data.materials[n].node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
        bs.inputs['Emission Color'].default_value = (*c, 1)
        bs.inputs['Emission Strength'].default_value = 0.85 if on else 0.0


# ---------------------------------------------------------------- tekil 3/4 gorunumler
if "--no-singles" not in ARGS:
    for coll in ASSET_COLLS:
        if ONLY and coll.name not in ONLY:
            continue
        show_only([coll])
        vl.update()
        mn, mx = bounds(vis_objects(coll))
        fit_ortho(mn, mx)
        render(OUT / f"{coll.name}.png")
        if coll.name.startswith('Trafik_Isigi'):
            signals(True)
            render(OUT / f"{coll.name}_yanik.png")
            signals(False)

# ---------------------------------------------------------------- toplu gorunum
inst_coll = bpy.data.collections.new('ONIZLEME_KOPYALAR')
scene.collection.children.link(inst_coll)


def instance(name, loc, rot_z=0.0):
    e = bpy.data.objects.new(f'kopya {name}', None)
    e.instance_type = 'COLLECTION'
    e.instance_collection = bpy.data.collections[name]
    e.location = loc
    e.rotation_euler = (0, 0, rot_z)
    inst_coll.objects.link(e)
    return e


def clear_instances():
    for o in list(inst_coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)


if "--no-toplu" not in ARGS:
    show_only([])
    rows = [
        ['Baba', 'Beton_Bariyer', 'Bariyer_Otoyol', 'Bank', 'Cop_Kutusu', 'Cop_Konteyneri', 'Cesme',
         'Oyun_Kaydirak', 'Oyun_Salincak', 'Cali_Yuvarlak', 'Cali_Cit'],
        ['Tabela_YolVer', 'Tabela_Dur', 'Tabela_YayaGecidi', 'Tabela_Park', 'Trafik_Isigi_Direk',
         'Trafik_Isigi_Kollu', 'Sokak_Lambasi', 'Park_Lambasi', 'Elektrik_Diregi', 'Otobus_Duragi'],
        ['Agac_Meyve', 'Agac_Cam', 'Agac_Cinar', 'Agac_Kavak']]
    ys = [0.0, 7.0, 17.0]
    allmn, allmx = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    for row, y in zip(rows, ys):
        widths = []
        for n in row:
            mn, mx = bounds(vis_objects(bpy.data.collections[n]))
            widths.append((mn, mx))
        gap = 0.9 if y < 1 else 1.4
        total = sum(mx.x - mn.x for mn, mx in widths) + gap * (len(row) - 1)
        x = -total / 2
        for n, (mn, mx) in zip(row, widths):
            ox = x - mn.x
            instance(n, (ox, y, 0))
            allmn = Vector(map(min, allmn, mn + Vector((ox, y, 0))))
            allmx = Vector(map(max, allmx, mx + Vector((ox, y, 0))))
            x += (mx.x - mn.x) + gap
    vl.update()
    fit_ortho(allmn, allmx, Vector((0.35, -1.0, 0.42)), 1.04)
    render(OUT / "Donati_toplu.png")
    clear_instances()

# ---------------------------------------------------------------- sokak kosesi kompozisyonu
if "--no-kose" not in ARGS:
    show_only([])
    comp = bpy.data.collections.new('ONIZLEME_KOSE')
    scene.collection.children.link(comp)
    asphalt = pmat('asfalt', (0.045, 0.045, 0.044), 0.92)
    curbm = pmat('kaldirim', (0.42, 0.41, 0.38), 0.9)
    paint = pmat('yol cizgisi', (0.75, 0.75, 0.72), 0.8)
    grass = pmat('cim', (0.16, 0.22, 0.08), 0.95)

    def slab(name, mn, mx, mat):
        bpy.ops.mesh.primitive_cube_add(size=1)
        o = bpy.context.object
        for c in list(o.users_collection):
            c.objects.unlink(o)
        comp.objects.link(o)
        o.name = name
        o.location = (Vector(mn) + Vector(mx)) / 2
        o.scale = Vector(mx) - Vector(mn)
        o.data.materials.append(mat)
        return o
    # yol Y boyunca (x -3.5..3.5), kesisen yol X boyunca (y 6..13)
    slab('yol', (-40, -60, -0.05), (40, 60, 0.0), asphalt)
    for x0, x1 in [(3.5, 3.75), (-3.75, -3.5)]:
        slab('bordur', (x0, -60, 0.0), (x1, 6.0, 0.15), curbm)
    slab('kaldirim sag', (3.75, -60, 0), (9.0, 5.75, 0.15), curbm)
    slab('kaldirim sol', (-9.0, -60, 0), (-3.75, 5.75, 0.15), curbm)
    slab('bordur kose', (3.5, 5.75, 0), (40, 6.0, 0.15), curbm)
    slab('kaldirim karsi', (3.5, 13.0, 0), (40, 18.0, 0.15), curbm)
    slab('cim sag', (9.0, -60, 0), (40, 5.75, 0.08), grass)
    slab('cim sol', (-40, -60, 0), (-9.0, 5.75, 0.08), grass)
    slab('cim karsi', (-40, 18.0, 0), (40, 60, 0.08), grass)
    for k in range(-12, 2):
        slab('orta cizgi', (-0.06, k * 4.0, 0.0), (0.06, k * 4.0 + 2.0, 0.004), paint)
    for k in range(6):
        slab('yaya gecidi', (-3.2 + k * 1.15, 1.2, 0.0), (-2.6 + k * 1.15, 4.4, 0.004), paint)
    slab('dur cizgisi', (0.1, 0.6, 0.0), (3.5, 0.9, 0.004), paint)
    K = 0.15
    instance('Trafik_Isigi_Kollu', (4.15, 0.2, K))
    instance('Trafik_Isigi_Direk', (-4.15, 5.2, K), math.pi)
    instance('Tabela_YayaGecidi', (4.1, -3.5, K))
    instance('Sokak_Lambasi', (4.15, -16.0, K))
    instance('Otobus_Duragi', (6.9, -9.0, K), -math.pi / 2)
    instance('Cop_Kutusu', (4.4, -12.0, K), -math.pi / 2)
    instance('Bank', (7.7, -14.2, K), -math.pi / 2)
    instance('Baba', (3.95, 4.4, K))
    instance('Baba', (3.95, 2.9, K))
    instance('Elektrik_Diregi', (-4.4, -4.0, K))
    instance('Agac_Cinar', (16.0, 17.0, 0.08))
    instance('Agac_Kavak', (11.0, -3.0, 0.08))
    instance('Agac_Kavak', (11.4, 0.5, 0.08))
    instance('Agac_Cam', (-8.0, 9.0, 0.08))
    instance('Agac_Meyve', (12.0, -12.5, 0.08))
    instance('Agac_Cam', (-13.0, 22.0, 0.08))
    instance('Cali_Yuvarlak', (10.4, -18.5, 0.08))
    instance('Cali_Yuvarlak', (10.3, -6.6, 0.08))
    for k in range(4):
        instance('Cali_Cit', (9.4, -16.0 + k * 2.0, 0.08), math.pi / 2)
    vl.update()
    cam.data.type = 'PERSP'
    cam.data.lens = 24
    cam.data.shift_x = cam.data.shift_y = 0
    cam.location = (-5.0, -24.0, 4.0)
    look(cam, (3.5, -6.0, 3.0))
    place_lights(Vector((2, -8, 2)), 24)
    render(OUT / "Donati_sokak_kosesi.png")
print("BITTI")
