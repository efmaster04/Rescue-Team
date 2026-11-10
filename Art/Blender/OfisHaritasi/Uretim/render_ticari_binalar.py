# -*- coding: utf-8 -*-
"""Ticari_Binalar onizlemeleri.

  blender.exe -b -P render_ticari_binalar.py -- [--quick] [--only Atolye_A] [--no-toplu] [--toplu-only]

Her varlik icin on 3/4 ve arka 3/4 (ortografik, servis minibusu stüdyosu gibi gri zemin + iki yumusak isik)
ve hepsini yan yana gosteren Ticari_Binalar_toplu.png. Olcek icin 1,8 m silindir insan (yalniz onizleme).
"""
import bpy
import math
import sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(r"C:\Users\uur\RescueTeam_OfisHaritasi")
BLEND = ROOT / "Kaynak" / "Blender" / "Ticari_Binalar.blend"
OUT = ROOT / "Kaynak" / "Onizlemeler" / "Ticari_Binalar"
ARGV = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
QUICK = '--quick' in ARGV
ONLY = ARGV[ARGV.index('--only') + 1] if '--only' in ARGV else None
NO_TOPLU = '--no-toplu' in ARGV
TOPLU_ONLY = '--toplu-only' in ARGV
VIEWS = ARGV[ARGV.index('--views') + 1].split(',') if '--views' in ARGV else ['on_34', 'arka_34']

IDS = ['Atolye_A', 'Atolye_B', 'Atolye_C', 'Benzinlik', 'Market']
PEOPLE = {'Atolye_A': [(-4.2, 3.0), (2.6, 11.9)], 'Atolye_B': [(-1.0, 6.0)], 'Atolye_C': [(1.8, 5.0), (-6.5, 9.5)],
          'Benzinlik': [(-2.6, 12.6), (3.0, 20.0)], 'Market': [(3.6, 16.9), (-1.0, 9.0)]}
DIRS = {'on_34': Vector((-0.75, -1.0, 0.6)), 'arka_34': Vector((0.75, 1.0, 0.6)), 'on': Vector((0.0, -1.0, 0.32)),
        'yan': Vector((1.0, 0.0, 0.35)), 'ust': Vector((-0.3, -0.5, 1.6))}


def lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def mat(name, srgb, rough=0.9):
    m = bpy.data.materials.new(name)
    bs = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value = (*[lin(c) for c in srgb], 1)
    bs.inputs['Roughness'].default_value = rough
    return m


def setup():
    bpy.ops.wm.open_mainfile(filepath=str(BLEND))
    sc = bpy.context.scene
    stage = bpy.data.collections.new('ONIZLEME_SAHNESI')
    sc.collection.children.link(stage)
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 0, -0.002))
    fl = bpy.context.object
    fl.scale = (1200, 1200, 1)
    for c in list(fl.users_collection):
        c.objects.unlink(fl)
    stage.objects.link(fl)
    fl.data.materials.append(mat('onizleme_zemin', (0.47, 0.46, 0.43), 0.95))
    w = sc.world or bpy.data.worlds.new('World')
    sc.world = w
    bg = w.node_tree.nodes.get('Background')
    bg.inputs['Color'].default_value = (lin(0.70), lin(0.71), lin(0.72), 1)
    bg.inputs['Strength'].default_value = 0.55
    for nm, frm, strength, ang, col in (('anahtar', (-0.55, -0.85, 0.95), 3.3, 9.0, (1.0, 0.95, 0.88)),
                                        ('dolgu', (0.85, 0.45, 0.55), 0.9, 25.0, (0.85, 0.9, 1.0))):
        ld = bpy.data.lights.new(nm, 'SUN')
        ld.energy = strength
        ld.angle = math.radians(ang)
        ld.color = col
        lo = bpy.data.objects.new(nm, ld)
        stage.objects.link(lo)
        lo.rotation_euler = (-Vector(frm)).to_track_quat('-Z', 'Y').to_euler()
    cd = bpy.data.cameras.new('onizleme_kamera')
    cd.type = 'ORTHO'
    cd.clip_end = 2000
    cam = bpy.data.objects.new('onizleme_kamera', cd)
    stage.objects.link(cam)
    sc.camera = cam
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 16 if QUICK else 56
    sc.cycles.use_denoising = True
    try:
        sc.cycles.denoiser = 'OPENIMAGEDENOISE'
    except Exception:
        pass
    sc.cycles.max_bounces = 6
    sc.render.resolution_percentage = 50 if QUICK else 100
    sc.render.image_settings.file_format = 'PNG'
    sc.view_settings.view_transform = 'AgX'
    sc.view_settings.look = 'AgX - Base Contrast' if 'AgX - Base Contrast' in [i.identifier for i in sc.view_settings.bl_rna.properties['look'].enum_items] else 'None'
    pm = mat('onizleme_insan', (0.24, 0.33, 0.45), 0.8)
    hm = mat('onizleme_insan_bas', (0.62, 0.50, 0.40), 0.8)
    people = {}
    for aid, pts in PEOPLE.items():
        people[aid] = []
        for x, y in pts:
            bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.23, depth=1.55, location=(x, y, 0.04 + 0.775))
            b = bpy.context.object
            b.data.materials.append(pm)
            bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.12, depth=0.25, location=(x, y, 0.04 + 1.55 + 0.125))
            h = bpy.context.object
            h.data.materials.append(hm)
            for o in (b, h):
                for c in list(o.users_collection):
                    c.objects.unlink(o)
                stage.objects.link(o)
                o.name = f'onizleme_olcek_{aid}'
                people[aid].append(o)
    for o in bpy.data.objects:
        if o.name.startswith('COL_'):
            o.hide_render = True
            o.hide_viewport = True
    return sc, cam, people


def visible_bbox(objs):
    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            mn = Vector(map(min, mn, w))
            mx = Vector(map(max, mx, w))
    mn.z = max(mn.z, 0.0)
    return mn, mx


def frame(sc, cam, mn, mx, d, margin=1.06):
    d = d.normalized()
    q = (-d).to_track_quat('-Z', 'Y')
    cam.rotation_euler = q.to_euler()
    R = q.to_matrix()
    right, up = R.col[0], R.col[1]
    corners = [Vector((x, y, z)) for x in (mn.x, mx.x) for y in (mn.y, mx.y) for z in (mn.z, mx.z)]
    us = [c.dot(right) for c in corners]
    vs = [c.dot(up) for c in corners]
    cu, cv = (min(us) + max(us)) / 2, (min(vs) + max(vs)) / 2
    w, h = max(us) - min(us), max(vs) - min(vs)
    aspect = sc.render.resolution_x / sc.render.resolution_y
    cam.data.ortho_scale = max(w, h * aspect) * margin
    centre = (mn + mx) / 2
    off = right * (cu - centre.dot(right)) + up * (cv - centre.dot(up))
    cam.location = centre + off + d * 400


def show_only(aid, people):
    for c in bpy.data.collections:
        if c.name in IDS:
            vis = (aid is None) or (c.name == aid)
            for o in c.objects:
                o.hide_render = (not vis) or o.name.startswith('COL_')
    for k, lst in people.items():
        for o in lst:
            o.hide_render = not (aid is None or k == aid)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    sc, cam, people = setup()
    ids = [ONLY] if ONLY else IDS
    if not TOPLU_ONLY:
        sc.render.resolution_x, sc.render.resolution_y = 1400, 950
        for aid in ids:
            show_only(aid, people)
            objs = [o for o in bpy.data.collections[aid].objects if not o.name.startswith('COL_')]
            mn, mx = visible_bbox(objs)
            if '--focus' in ARGV:
                f = [float(t) for t in ARGV[ARGV.index('--focus') + 1].split(',')]
                mn, mx = Vector(f[:3]), Vector(f[3:6])
                d = Vector([float(t) for t in ARGV[ARGV.index('--dir') + 1].split(',')]) if '--dir' in ARGV else Vector((-0.6, -1, 0.3))
                frame(sc, cam, mn, mx, d, margin=1.0)
                sc.render.filepath = str(Path(ARGV[ARGV.index('--out') + 1]))
                bpy.ops.render.render(write_still=True)
                print('RENDER', sc.render.filepath)
                continue
            for v in VIEWS:
                frame(sc, cam, mn, mx, DIRS[v])
                sc.render.filepath = str(OUT / f'{aid}_{v}.png')
                bpy.ops.render.render(write_still=True)
                print('RENDER', sc.render.filepath)
    if not NO_TOPLU and not ONLY:
        show_only(None, people)
        x = 0.0
        gap = 4.0
        offsets = {}
        widths = {'Atolye_A': 24, 'Atolye_B': 20, 'Atolye_C': 30, 'Benzinlik': 36, 'Market': 30}
        for aid in IDS:
            cx = x + widths[aid] / 2
            offsets[aid] = cx
            x += widths[aid] + gap
        shift = -(x - gap) / 2
        moved = []
        for aid in IDS:
            dx = offsets[aid] + shift
            for o in list(bpy.data.collections[aid].objects) + people[aid]:
                o.location.x += dx
                moved.append((o, dx))
        bpy.context.view_layer.update()
        objs = [o for aid in IDS for o in bpy.data.collections[aid].objects if not o.name.startswith('COL_')]
        mn, mx = visible_bbox(objs)
        sc.render.resolution_x, sc.render.resolution_y = 2600, 1000
        frame(sc, cam, mn, mx, Vector((-0.55, -1.0, 0.72)), margin=1.03)
        sc.render.filepath = str(OUT / 'Ticari_Binalar_toplu.png')
        bpy.ops.render.render(write_still=True)
        print('RENDER', sc.render.filepath)
        for o, dx in moved:
            o.location.x -= dx


main()
