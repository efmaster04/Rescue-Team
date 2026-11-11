# -*- coding: utf-8 -*-
"""Araclar seti onizlemeleri (Cycles CPU, servis minibusu studyosu).

  blender -b Kaynak/Blender/Araclar.blend -P render_araclar.py
Ortam degiskenleri:
  AR_QUICK=1            yarim cozunurluk, az ornek
  AR_ONLY=Sedan,SUV     yalniz bu araclar
  AR_SHOTS=on,arka,toplu,yan   cekimler (varsayilan: on,arka,toplu)
"""
import bpy
import math
import os
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(r'C:\Users\uur\RescueTeam_OfisHaritasi')
OUT = ROOT / 'Kaynak' / 'Onizlemeler' / 'Araclar'
OUT.mkdir(parents=True, exist_ok=True)
QUICK = os.environ.get('AR_QUICK') == '1'
ONLY = [s for s in os.environ.get('AR_ONLY', '').split(',') if s]
SHOTS = [s for s in os.environ.get('AR_SHOTS', 'on,arka,toplu').split(',') if s]


def srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


# Onizleme boyasi (yalniz gorsel; FBX'teki AR_Boya randomize edilir)
PREVIEW_PAINT = {
    'Hatchback': (0.47, 0.12, 0.11),
    'Sedan': (0.64, 0.65, 0.66),
    'SUV': (0.14, 0.18, 0.29),
    'Pickup': (0.64, 0.58, 0.46),
    'HafifTicari': (0.86, 0.86, 0.84),
    'Minibus': (0.82, 0.80, 0.74),
    'Kamyon': (0.54, 0.60, 0.65),
    'Traktor': None,
}
ORDER = ['Hatchback', 'Sedan', 'Taksi', 'SUV', 'Pickup', 'HafifTicari', 'Minibus', 'Kamyon', 'Traktor']

scene = bpy.context.scene
cols = {c.name[5:]: c for c in bpy.data.collections if c.name.startswith('Arac_')}
names = [n for n in ORDER if n in cols and (not ONLY or n in ONLY)]


def govde(short):
    return next(o for o in cols[short].objects if o.get('ad') == 'Govde')


for short in names:
    col = PREVIEW_PAINT.get(short)
    if not col:
        continue
    g = govde(short)
    for i, m in enumerate(g.data.materials):
        if m and m.name == 'AR_Boya':
            mc = m.copy()
            mc.name = 'onizleme_boya_' + short
            lin = (*(srgb_to_lin(c) for c in col), 1.0)
            mc.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = lin
            g.data.materials[i] = mc

# COL kutulari gizli
for o in bpy.data.objects:
    if o.get('ad', '').startswith('COL_'):
        o.hide_render = True

# Studyo (yalniz onizleme)
stage = bpy.data.collections.new('ONIZLEME')
scene.collection.children.link(stage)
bpy.ops.mesh.primitive_plane_add(size=400, location=(0, 0, 0))
ground = bpy.context.object
for c in list(ground.users_collection):
    c.objects.unlink(ground)
stage.objects.link(ground)
gm = bpy.data.materials.new('onizleme_zemin')
gm.use_nodes = True
gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*(srgb_to_lin(c) for c in (0.62, 0.61, 0.58)), 1)
gm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.93
ground.data.materials.append(gm)
world = scene.world or bpy.data.worlds.new('World')
scene.world = world
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (.70, .69, .65, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = .48

lights = []
for nm, power, size, color in [('anahtar', 1100, 6, (1.0, .88, .74)), ('dolgu', 670, 5, (.80, .88, 1.0))]:
    ld = bpy.data.lights.new(nm, 'AREA')
    ld.energy = power
    ld.shape = 'DISK'
    ld.size = size
    ld.color = color
    lo = bpy.data.objects.new(nm, ld)
    stage.objects.link(lo)
    lights.append(lo)

camd = bpy.data.cameras.new('kamera')
camd.type = 'ORTHO'
cam = bpy.data.objects.new('kamera', camd)
stage.objects.link(cam)
scene.camera = cam

scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 16 if QUICK else 56
scene.cycles.use_denoising = True
scene.render.resolution_x = 1400
scene.render.resolution_y = 950
scene.render.resolution_percentage = 50 if QUICK else 100
scene.render.image_settings.file_format = 'PNG'
scene.view_settings.view_transform = 'AgX'
scene.render.film_transparent = False


def aim(o, target):
    o.rotation_euler = (Vector(target) - o.location).to_track_quat('-Z', 'Y').to_euler()


def setup(target, cam_dir, scale, dist=30.0, key_side=1):
    t = Vector(target)
    d = Vector(cam_dir).normalized()
    cam.location = t + d * dist
    aim(cam, t)
    camd.ortho_scale = scale
    camd.clip_end = 400
    # isiklar kameraya gore: anahtar kamera tarafinda onde-yukarida, dolgu karsi tarafta
    side = Vector((d.x, d.y, 0)).normalized()
    perp = Vector((-side.y, side.x, 0)) * key_side
    lights[0].location = t + side * 5.5 + perp * 3.0 + Vector((0, 0, 8))
    lights[1].location = t - side * 2.0 - perp * 4.5 + Vector((0, 0, 7))
    for lo in lights:
        aim(lo, t + Vector((0, 0, 1.0)))


def show_only(keep):
    for short, c in cols.items():
        hide = short not in keep
        c.hide_render = hide


def bbox(short):
    pts = []
    for o in cols[short].objects:
        if o.get('ad', '').startswith('COL_'):
            continue
        pts += [o.matrix_world @ Vector(b) for b in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


def render(path):
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    print('RENDER', path)


for short in names:
    show_only([short])
    mn, mx = bbox(short)
    size = mx - mn
    tgt = ((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, size.z * 0.42)
    scale = max(size.y * 1.30, size.z * 2.3, 5.2)
    if 'on' in SHOTS:
        setup(tgt, (0.62, 0.50, 0.40), scale, key_side=1)
        render(OUT / f'{short}_on_yan.png')
    if 'arka' in SHOTS:
        setup(tgt, (-0.62, -0.50, 0.40), scale, key_side=1)
        render(OUT / f'{short}_arka_yan.png')
    if 'profil' in SHOTS:
        setup(tgt, (1.0, 0.0, 0.08), max(size.y * 1.15, 5.0), key_side=1)
        render(OUT / f'{short}_yan.png')

def move_vehicle(short, M):
    for o in cols[short].objects:
        o.matrix_world = M @ o.matrix_world


if ('toplu' in SHOTS or 'yan' in SHOTS) and len(names) > 1:
    show_only(names)
    bpy.context.view_layer.update()
    base = {s: [o.matrix_world.copy() for o in cols[s].objects] for s in names}

    def reset():
        for s in names:
            for o, M in zip(cols[s].objects, base[s]):
                o.matrix_world = M
        bpy.context.view_layer.update()

if 'toplu' in SHOTS and len(names) > 1:
    # iki sira; her arac 35 derece donuk (on + sag yan gorunur), hepsi ayni olcekte
    th = math.radians(35)
    rot = Matrix.Rotation(th, 4, 'Z')
    rows = [[n for n in names if n in ('Kamyon', 'Minibus', 'HafifTicari', 'Traktor')],
            [n for n in names if n not in ('Kamyon', 'Minibus', 'HafifTicari', 'Traktor')]]
    row_y = [0.0, -9.0]
    extent = []
    for ri, row in enumerate(rows):
        x = 0.0
        placed = []
        for short in row:
            mn, mx = bbox(short)
            L, W = mx.y - mn.y, mx.x - mn.x
            fw = L * math.sin(th) + W * math.cos(th)
            x += fw / 2
            placed.append((short, x))
            x += fw / 2 + 1.0
        total = x - 1.0
        for short, px in placed:
            move_vehicle(short, Matrix.Translation((px - total / 2, row_y[ri], 0)) @ rot)
        extent.append(total)
    bpy.context.view_layer.update()
    tgt = (0.0, -4.5, 0.6)
    setup(tgt, (0.0, 1.0, 0.50), max(extent) * 1.04, dist=80.0, key_side=1)
    scene.render.resolution_y = 1000
    render(OUT / 'Araclar_toplu.png')
    scene.render.resolution_y = 950
    reset()

if 'yan' in SHOTS and len(names) > 1:
    # siluet karsilastirmasi: hepsi soldan profil, iki sira
    rows = [[n for n in names if n in ('Kamyon', 'Minibus', 'HafifTicari', 'Pickup', 'Traktor')],
            [n for n in names if n not in ('Kamyon', 'Minibus', 'HafifTicari', 'Pickup', 'Traktor')]]
    widest = 0.0
    for ri, row in enumerate(rows):
        y = 0.0
        placed = []
        for short in row:
            mn, mx = bbox(short)
            L = mx.y - mn.y
            y += L / 2
            placed.append((short, y))
            y += L / 2 + 1.2
        total = y - 1.2
        widest = max(widest, total)
        for short, py in placed:
            move_vehicle(short, Matrix.Translation((7.0 if ri == 0 else 0.0, -(py - total / 2), 0)))
    bpy.context.view_layer.update()
    setup((3.5, 0.0, 1.4), (-1.0, 0.0, 0.42), widest * 1.04, dist=80.0, key_side=-1)
    render(OUT / 'Araclar_siluet.png')
    reset()
