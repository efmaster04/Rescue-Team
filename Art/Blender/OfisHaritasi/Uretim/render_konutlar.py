# -*- coding: utf-8 -*-
"""Konutlar onizlemeleri: her varlik icin on 3/4 + arka 3/4, ayrica sokak dizisi toplu goruntu.

Calistirma:
  blender.exe -b --factory-startup -P render_konutlar.py -- [--quick] [--only Ev_A,Ev_B] [--no-toplu]
                                                           [--yalniz-toplu]
Konutlar.blend acilir; sahne yalniz onizleme icin kurulur, .blend kaydedilmez.
Stuedyo: Cycles CPU, gri zemin, iki yumusak alan isigi, ortografik kamera (servis minibusu gibi).
"""
import bpy
import math
import sys
from pathlib import Path
from mathutils import Vector

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
QUICK = '--quick' in ARGS
ONLY = None
if '--only' in ARGS:
    ONLY = ARGS[ARGS.index('--only') + 1].split(',')
NO_TOPLU = '--no-toplu' in ARGS
YALNIZ_TOPLU = '--yalniz-toplu' in ARGS

KOK = Path(r'C:\Users\uur\RescueTeam_OfisHaritasi')
BLEND = KOK / 'Kaynak' / 'Blender' / 'Konutlar.blend'
OUT = KOK / 'Kaynak' / 'Onizlemeler' / 'Konutlar'
OUT.mkdir(parents=True, exist_ok=True)

SIRA = ['Apartman_A', 'Apartman_B', 'Apartman_C', 'Ev_A', 'Ev_B', 'Ev_C', 'Ev_D']
PARSEL_G = {'Apartman_A': 22, 'Apartman_B': 22, 'Apartman_C': 22, 'Ev_A': 18, 'Ev_B': 18,
            'Ev_C': 18, 'Ev_D': 18}
FIGUR_X = {'Apartman_A': 1.6, 'Apartman_B': 2.0, 'Apartman_C': 2.6, 'Ev_A': 1.6, 'Ev_B': 3.4,
           'Ev_C': 0.2, 'Ev_D': 4.2}

bpy.ops.wm.open_mainfile(filepath=str(BLEND))
sc = bpy.context.scene

# palet ve onizleme secimleri build ciktilarindan okunur
import json
_OYUN = KOK / 'Oyun' / 'Assets' / 'OfisHaritasi' / 'Modeller' / 'Konutlar'
_mj = json.loads((_OYUN / 'Konutlar_malzemeler.json').read_text(encoding='utf-8'))
_mf = json.loads((_OYUN / 'Konutlar_manifest.json').read_text(encoding='utf-8'))
PALET = {}
for _k, _lst in _mj['palettes'].items():
    for _e in _lst:
        PALET[_e['ad']] = tuple(_e['color'])
SECIM = {v['id']: (v['onizleme_renkleri']['konut_siva'], v['onizleme_renkleri']['konut_panjur'])
         for v in _mf['varliklar']}


def s2l(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def mat(name, srgb, rough=0.8, metal=0.0):
    m = bpy.data.materials.new(name)
    if m.node_tree is None:
        m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    lin = tuple(s2l(c) for c in srgb)
    bs.inputs['Base Color'].default_value = (*lin, 1)
    bs.inputs['Roughness'].default_value = rough
    bs.inputs['Metallic'].default_value = metal
    m.diffuse_color = (*lin, 1)
    return m


def recolor(aid):
    siva, panjur = SECIM[aid]
    ms = bpy.data.materials['KN_Siva'].copy()
    ms.name = f'onizleme_siva_{aid}'
    ms.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (
        *[s2l(c) for c in PALET[siva]], 1)
    mp = bpy.data.materials['KN_Panjur'].copy()
    mp.name = f'onizleme_panjur_{aid}'
    mp.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (
        *[s2l(c) for c in PALET[panjur]], 1)
    for o in list(bpy.data.collections[aid].objects):
        if o.type != 'MESH':
            continue
        for sl in o.material_slots:
            if sl.material and sl.material.name == 'KN_Siva':
                sl.material = ms
            elif sl.material and sl.material.name == 'KN_Panjur':
                sl.material = mp


for aid in SIRA:
    recolor(aid)

stage = bpy.data.collections.new('ONIZLEME_SAHNESI')
sc.collection.children.link(stage)


def link(o):
    stage.objects.link(o)
    return o


# zemin
me = bpy.data.meshes.new('onizleme_zemin')
S = 900
me.from_pydata([(-S, -S, -0.02), (S, -S, -0.02), (S, S, -0.02), (-S, S, -0.02)], [],
               [(0, 1, 2, 3)])
zemin = link(bpy.data.objects.new('onizleme_zemin', me))
zemin.data.materials.append(mat('onizleme_zemin', (0.46, 0.45, 0.42), 0.95))

# olcek figuru (1,8 m)
fig_m = mat('onizleme_figur', (0.30, 0.36, 0.44), 0.8)


def figur(name, x, y):
    import bmesh
    bm = bmesh.new()

    def bx(x0, x1, y0, y1, z0, z1):
        r = bmesh.ops.create_cube(bm, size=1.0)
        for v in r['verts']:
            v.co.x = x0 + (v.co.x + 0.5) * (x1 - x0)
            v.co.y = y0 + (v.co.y + 0.5) * (y1 - y0)
            v.co.z = z0 + (v.co.z + 0.5) * (z1 - z0)
    bx(-0.17, -0.03, -0.09, 0.09, 0.0, 0.86)
    bx(0.03, 0.17, -0.09, 0.09, 0.0, 0.86)
    bx(-0.21, 0.21, -0.12, 0.12, 0.84, 1.50)
    bx(-0.29, -0.21, -0.07, 0.07, 0.86, 1.46)
    bx(0.21, 0.29, -0.07, 0.07, 0.86, 1.46)
    bx(-0.06, 0.06, -0.06, 0.06, 1.48, 1.56)
    bx(-0.11, 0.11, -0.12, 0.11, 1.56, 1.80)
    m2 = bpy.data.meshes.new(name)
    bm.to_mesh(m2)
    bm.free()
    o = link(bpy.data.objects.new(name, m2))
    o.data.materials.append(fig_m)
    o.location = (x, y, 0.0)
    return o


wo = sc.world or bpy.data.worlds.new('w')
sc.world = wo
if wo.node_tree is None:
    wo.use_nodes = True
bg = wo.node_tree.nodes['Background']
bg.inputs['Color'].default_value = (0.70, 0.69, 0.65, 1)
bg.inputs['Strength'].default_value = 0.55

lights = []
for nm, power, size, col in (('anahtar', 1.0, 40, (1.0, 0.90, 0.78)),
                             ('dolgu', 0.55, 34, (0.82, 0.88, 1.0))):
    d = bpy.data.lights.new(nm, 'AREA')
    d.shape = 'DISK'
    d.size = size
    d.color = col
    o = link(bpy.data.objects.new(nm, d))
    lights.append((o, power))

camd = bpy.data.cameras.new('onizleme_kamera')
camd.type = 'ORTHO'
cam = link(bpy.data.objects.new('onizleme_kamera', camd))
sc.camera = cam

sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 16 if QUICK else 56
sc.cycles.use_denoising = True
try:
    sc.cycles.denoiser = 'OPENIMAGEDENOISE'
except Exception:
    pass
sc.render.image_settings.file_format = 'PNG'
sc.view_settings.view_transform = 'AgX'
sc.view_settings.look = 'None'
sc.render.film_transparent = False


def set_res(w, h):
    f = 0.5 if QUICK else 1.0
    sc.render.resolution_x = int(w * f)
    sc.render.resolution_y = int(h * f)
    sc.render.resolution_percentage = 100


def vis_objs(aid):
    return [o for o in bpy.data.collections[aid].objects
            if o.type == 'MESH' and not o.name.startswith('COL_')]


def show_only(aids):
    for a in SIRA:
        lc = bpy.context.view_layer.layer_collection.children[a]
        lc.exclude = a not in aids
    for a in SIRA:
        for o in bpy.data.collections[a].objects:
            if o.name.startswith('COL_'):
                o.hide_render = True


def frame(objs, az, el, w, h, margin=1.06, target_z=None):
    d = Vector((-math.sin(math.radians(az)), -math.cos(math.radians(az)),
                math.tan(math.radians(el)))).normalized()
    pts = []
    for o in objs:
        for c in o.bound_box:
            pts.append(o.matrix_world @ Vector(c))
    ctr = sum(pts, Vector()) / len(pts)
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr = (mn + mx) / 2
    cam.location = ctr + d * 200
    q = (-d).to_track_quat('-Z', 'Y')
    cam.rotation_euler = q.to_euler()
    right = q @ Vector((1, 0, 0))
    up = q @ Vector((0, 1, 0))
    us = [(p - ctr).dot(right) for p in pts]
    vs = [(p - ctr).dot(up) for p in pts]
    cu = (max(us) + min(us)) / 2
    cv = (max(vs) + min(vs)) / 2
    cam.location = cam.location + right * cu + up * cv
    wu = max(us) - min(us)
    hv = max(vs) - min(vs)
    aspect = w / h
    camd.ortho_scale = max(wu, hv * aspect) * margin
    camd.clip_end = 1000
    # isiklar kameraya gore: anahtar sol-ust, dolgu sag
    tgt = ctr
    for (o, pw), (daz, del_, dist) in zip(lights, ((-38, 58, 70), (70, 30, 75))):
        a = math.radians(az + daz)
        e = math.radians(del_)
        v = Vector((-math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        o.location = tgt + v * dist
        o.rotation_euler = (-v).to_track_quat('-Z', 'Y').to_euler()
        o.data.energy = pw * 85000 * (dist / 70) ** 2


def render(path):
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    print('RENDER', path.name)


figs = {}
for aid in SIRA:
    figs[aid] = figur(f'figur_{aid}', FIGUR_X[aid], -0.9)
    figs[aid].hide_render = True

if not YALNIZ_TOPLU:
    for aid in SIRA:
        if ONLY and aid not in ONLY:
            continue
        show_only([aid])
        for a, f in figs.items():
            f.hide_render = a != aid
            f.location = (FIGUR_X[a], -0.9, 0)
        objs = vis_objs(aid) + [figs[aid]]
        set_res(1400, 950)
        frame(objs, 38, 30, 1400, 950)
        render(OUT / f'{aid}_on.png')
        frame(objs, 38 + 180, 30, 1400, 950)
        render(OUT / f'{aid}_arka.png')

if not NO_TOPLU and not ONLY:
    show_only(SIRA)
    xo = 0.0
    total = sum(PARSEL_G[a] for a in SIRA)
    x = -total / 2
    allobjs = []
    for aid in SIRA:
        g = PARSEL_G[aid]
        cx = x + g / 2
        for o in vis_objs(aid):
            o.location.x = cx
        figs[aid].location = (cx + FIGUR_X[aid], -0.9, 0)
        figs[aid].hide_render = False
        allobjs += vis_objs(aid) + [figs[aid]]
        x += g
    bpy.context.view_layer.update()
    set_res(2800, 1050)
    frame(allobjs, 16, 22, 2800, 1050, margin=1.03)
    render(OUT / 'Konutlar_toplu.png')
print('BITTI')
