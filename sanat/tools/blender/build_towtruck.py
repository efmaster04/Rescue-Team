"""Blender 5.2: metre ölçeğinde, parçalı ve deterministik vinçli kurtarma aracı (çekici).

Çalıştırma:
  blender -b --factory-startup --python-exit-code 1 --python tools/blender/build_towtruck.py
  (geliştirme: ... -- --round 2 --no-render)
Koordinat: Z yukarı, araç burnu -Y (minibüsle aynı), aracın solu +X, sağı -X.
Kök pivot: altı tekerleğin yere değdiği dikdörtgenin merkezi, Z=0.
Renk: hex (sRGB) -> lineer Base Color; FBX olduğu gibi dışa aktarılır (Unity Linear URP).
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import struct
import sys
import tempfile
import time
import traceback
import zlib
sys.dont_write_bytecode = True

import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'Export/TowTruck'
REF = ROOT/'ArtSource/TowTruck/references'
CONCEPT = REF/'01_turnaround_orange.jpg'
MINIVAN_FBX = ROOT/'Export/Minivan/SM_Minivan.fbx'
P = 'SM_TowTruck'
MATERIALS = {}
PALETTE = {}
ASSETS = []
PARTS = {}
WORLD_PIVOT = {}
ROUND = 3

# Ölçü sözleşmesi (metre)
CAB_X = 1.17            # kabin yan yüzü
BODY_X = 1.25           # kasa/şasi dış yüzü (genişlik 2.5 m)
WHEEL_R = .50
WHEEL_X = 1.02
AXLES = {'F': -2.60, 'R1': 1.50, 'R2': 2.60}   # ön aks + arka çift aks; dikdörtgen merkezi Y=0
DECK_Z0 = 1.12         # servis gövdesi alt kotu (tekerlek üstü)
BED_TOP = 1.75         # servis gövdesi üst plakası
TURNTABLE = Vector((0, 3.10, BED_TOP))
HINGE = Vector((0, 3.60, 3.02))  # kol yatay menteşesi
BOOM_CLOSED = 6.0
EXT_STROKE = 2.0                 # her teleskop kademesi
CONE_X, CONE_YS = .85, (.40, .88, 1.36, 1.84)


def srgb_to_linear(v):
    return v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4


def reference_palette():
    # Piksel koordinatları sol üstten; 5x5 medyan JPEG gürültüsünü bastırır.
    image = bpy.data.images.load(str(CONCEPT))
    w, h = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)
    locations = {'Paint': (880, 270), 'Trim': (225, 328), 'Tire': (130, 365), 'Rim': (840, 338),
                 'Glass': (870, 175), 'Interior': (180, 185), 'Headlight': (157, 284),
                 'Taillight': (457, 280), 'Beacon': (180, 105),
                 'ConeOrange': (481, 193), 'ConeWhite': (482, 184)}
    record = {}
    for name, (x, y) in locations.items():
        rgb = np.median(pixels[h-y-3:h-y+2, x-2:x+3, :3], axis=(0, 1))
        color = ''.join(f'{round(float(c)*255):02X}' for c in rgb)
        PALETTE[name] = color
        record[name] = {'hex': '#'+color, 'source_pixel_xy_top_left': [x, y], 'median_patch_px': 5}
    bpy.data.images.remove(image)
    return record


def make_materials():
    for name, color in PALETTE.items():
        mat = bpy.data.materials.new(f'M_TowTruck_{name}')
        mat.use_nodes = True
        rgb = tuple(srgb_to_linear(int(color[i:i+2], 16)/255) for i in (0, 2, 4))
        shader = mat.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Base Color'].default_value = (*rgb, 1)
        mat.diffuse_color = (*rgb, 1)
        shader.inputs['Roughness'].default_value = .62
        shader.inputs['Specular IOR Level'].default_value = .25
        if name == 'Glass':
            shader.inputs['Transmission Weight'].default_value = .88
            shader.inputs['Roughness'].default_value = .075
            shader.inputs['IOR'].default_value = 1.08
            shader.inputs['Alpha'].default_value = .34
            mat.diffuse_color = (*rgb, .34)
        MATERIALS[name] = mat


class Geo:
    def __init__(self, name):
        self.name, self.vertices, self.faces, self.materials = name, [], [], []
        self.tags = []
        self.tag = 'shell'

    def patch(self, vertices, faces, material='Paint'):
        offset = len(self.vertices)
        self.vertices.extend(tuple(v) for v in vertices)
        self.tags.extend([self.tag]*len(vertices))
        self.faces.extend(tuple(offset+i for i in f) for f in faces)
        self.materials.extend([material]*len(faces))

    def box(self, center, size, material='Paint', bevel=.025, rotation=None):
        # İnce parçalarda bevel yok: low poly bütçesi korunur.
        if min(size) < .08:
            bevel = 0
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1)
        for v in bm.verts:
            v.co = Vector(tuple(v.co[i]*size[i] for i in range(3)))
        if bevel:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(size)*.45),
                            segments=1, affect='EDGES', profile=.5)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.verts.ensure_lookup_table()
        bm.verts.index_update()
        rot = rotation or Matrix.Identity(3)
        self.patch([rot@v.co+Vector(center) for v in bm.verts],
                   [[v.index for v in f.verts] for f in bm.faces], material)
        bm.free()

    def span(self, lo, hi, material='Paint', bevel=.025):
        lo, hi = Vector(map(min, lo, hi)), Vector(map(max, lo, hi))
        self.box((lo+hi)/2, tuple(hi-lo), material, bevel)

    def beam(self, a, b, width, depth=None, material='Paint', bevel=0):
        delta = Vector(b)-Vector(a)
        self.box((Vector(a)+Vector(b))/2, (width, depth or width, delta.length), material,
                 bevel, rotation=delta.to_track_quat('Z', 'Y').to_matrix())

    def prism(self, polygon, offset, material='Paint'):
        n = len(polygon)
        self.patch([Vector(p) for p in polygon]+[Vector(p)+Vector(offset) for p in polygon],
                   [tuple(reversed(range(n))), tuple(range(n, 2*n))] +
                   [(i, (i+1) % n, (i+1) % n+n, i+n) for i in range(n)], material)

    def cylinder(self, center, radius, depth, material, axis=(0, 0, 1), segments=16, radius_top=None):
        rot = Vector(axis).to_track_quat('Z', 'Y').to_matrix()
        radii = (radius, radius if radius_top is None else radius_top)
        vertices = [rot@Vector((r*math.cos(2*math.pi*i/segments), r*math.sin(2*math.pi*i/segments), z))+Vector(center)
                    for z, r in zip((-depth/2, depth/2), radii) for i in range(segments)]
        self.patch(vertices, [tuple(reversed(range(segments))), tuple(range(segments, 2*segments))] +
                   [(i, (i+1) % segments, (i+1) % segments+segments, i+segments) for i in range(segments)], material)

    def finish(self, pivot=(0, 0, 0), parent=P, motion=None):
        # 0.1 mm ızgara: FBX gidiş-dönüşünde float gürültüsü kanonik özeti bozmaz.
        pivot = Vector(tuple(round(c, 4) for c in pivot))
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata([tuple(round(c, 4) for c in Vector(v)-pivot) for v in self.vertices], [], self.faces)
        used = [n for n in MATERIALS if n in self.materials]
        for name in used:
            mesh.materials.append(MATERIALS[name])
        for poly, name in zip(mesh.polygons, self.materials):
            poly.material_index = used.index(name)
        # Düz normal + gerçek üçgenler; export topolojiyi değiştirmez.
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='FIXED', ngon_method='EAR_CLIP')
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        obj = bpy.data.objects.new(self.name, mesh)
        attach(obj, pivot, parent)
        for tag in sorted(set(self.tags)):
            group = obj.vertex_groups.new(name=tag)
            group.add([i for i, t in enumerate(self.tags) if t == tag], 1, 'REPLACE')
        PARTS[self.name] = {'pivot_m': [round(c, 6) for c in pivot], 'parent': parent, 'motion': motion}
        return obj


def attach(obj, pivot, parent):
    # Dinlenme pozunda hiçbir objede dönme yok: yerel konum = dünya pivotu - ebeveyn pivotu.
    bpy.context.collection.objects.link(obj)
    WORLD_PIVOT[obj.name] = Vector(pivot)
    if parent:
        obj.parent = bpy.data.objects[parent]
        obj.location = Vector(pivot)-WORLD_PIVOT[parent]
    else:
        obj.location = pivot
    ASSETS.append(obj)


def empty(name, position, parent):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_size = .12
    attach(obj, Vector(position), parent)
    return obj


def hinge(axis, lo, hi, work, note=None):
    m = {'type': 'hinge', 'axis': list(axis), 'range_deg': [lo, hi], 'work_pose_deg': work}
    if note:
        m['note'] = note
    return m


def slide(axis, distance, work, note=None):
    m = {'type': 'slide', 'axis': list(axis), 'range_m': [0, distance], 'work_pose_m': work}
    if note:
        m['note'] = note
    return m


def spin(axis, work=0, note=None):
    m = {'type': 'rotate_continuous', 'axis': list(axis), 'range_deg': None, 'work_pose_deg': work}
    if note:
        m['note'] = note
    return m


# ---------------------------------------------------------------- gövde

def side_panel(geo, sign, yz, material='Paint', thickness=.05, outside=0, x=CAB_X):
    geo.prism([(sign*(x+outside), y, z) for y, z in yz], (-sign*thickness, 0, 0), material)


def arc(cy, cz, r, a0, a1, steps):
    # a=0 aracın önü (-Y), a=180 arkası; yay üstten geçer.
    return [(cy-r*math.cos(math.radians(a0+(a1-a0)*i/steps)), cz+r*math.sin(math.radians(a0+(a1-a0)*i/steps)))
            for i in range(steps+1)]


def build_body():
    body, glass = Geo(P+'_Body'), Geo(P+'_Glass')
    fy = AXLES['F']
    # Şasi: iki kiriş, akslar, kabin altı.
    for x in (-.45, .45):
        body.span((x-.07, -3.70, .60), (x+.07, 3.85, .80), 'Tire', 0)
        body.span((x-.07, -.95, .80), (x+.07, 3.85, DECK_Z0), 'Trim', 0)
    for y in AXLES.values():
        body.beam((-.86, y, WHEEL_R), (.86, y, WHEEL_R), .10, material='Tire')
    # Kabin ön yüzü, kaput, tampon, ızgara, farlar.
    body.span((-CAB_X, -3.95, .72), (CAB_X, -3.90, 1.28), 'Paint', 0)
    body.prism([(-CAB_X, -3.95, 1.24), (-CAB_X, -3.15, 1.41), (-CAB_X, -3.15, 1.47), (-CAB_X, -3.95, 1.30)],
               (2*CAB_X, 0, 0))
    body.span((-1.20, -4.00, .40), (1.20, -3.86, .72), 'Trim', .04)
    body.span((-.45, -3.97, .86), (.45, -3.93, 1.16), 'Trim', 0)
    for z in (.93, 1.01, 1.09):
        body.span((-.39, -3.985, z-.015), (.39, -3.965, z+.015), 'Tire', 0)
    for sign in (-1, 1):
        body.span((sign*.58, -3.97, .89), (sign*.98, -3.93, 1.17), 'Trim', 0)
        body.span((sign*.62, -3.985, .92), (sign*.94, -3.965, 1.14), 'Headlight', 0)
    # Ön cam çerçevesi (koyu conta) ve cam.
    corners = [(-1.10, -3.15, 1.47), (1.10, -3.15, 1.47), (1.10, -2.90, 2.30), (-1.10, -2.90, 2.30)]
    glass.prism(corners, (0, .012, 0), 'Glass')
    for a, b in zip(corners, corners[1:]+corners[:1]):
        body.beam(a, b, .05, .05, 'Tire')
    # Kabin yanları: ön parça (çamurluk yayı), kapı altı şeridi, arka dikme.
    for sign in (-1, 1):
        front = [(-3.95, .55)] + arc(fy, .50, .58, 6, 94.95, 8) + \
                [(-2.55, 2.32), (-2.90, 2.32), (-3.15, 1.45), (-3.95, 1.28)]
        side_panel(body, sign, front)
        lower = [(-2.55, 1.12)] + arc(fy, .50, .58, 94.95, 174, 6) + [(-1.05, .55), (-1.05, 1.12)]
        side_panel(body, sign, lower)
        side_panel(body, sign, [(-1.22, 1.12), (-1.05, 1.12), (-1.05, 2.32), (-1.22, 2.32)])
        # Çamurluk dudağı.
        lip_in, lip_out = arc(fy, .50, .58, 6, 174, 10), arc(fy, .50, .64, 6, 174, 10)
        for i in range(10):
            side_panel(body, sign, [lip_in[i], lip_in[i+1], lip_out[i+1], lip_out[i]], thickness=.07, outside=.04)
        # Ayna: menteşenin önünde, gövde parçası.
        body.beam((sign*1.15, -2.72, 1.78), (sign*1.38, -2.80, 1.86), .04, material='Trim')
        body.span((sign*1.39, -2.86, 1.70), (sign*1.47, -2.74, 2.00), 'Trim', 0)
        body.span((sign*1.40, -2.745, 1.73), (sign*1.46, -2.735, 1.97), 'Glass', 0)
    # Kabin arka duvarı (arka cam boşluğu) ve tavan.
    body.span((-CAB_X, -1.10, .55), (CAB_X, -1.05, 1.65), 'Paint', 0)
    body.span((-CAB_X, -1.10, 2.15), (CAB_X, -1.05, 2.32), 'Paint', 0)
    for sign in (-1, 1):
        body.span((sign*.70, -1.10, 1.65), (sign*CAB_X, -1.05, 2.15), 'Paint', 0)
    glass.span((-.70, -1.085, 1.65), (.70, -1.075, 2.15), 'Glass', 0)
    body.tag = 'roof_hide_for_interior'
    body.span((-CAB_X, -2.92, 2.32), (CAB_X, -1.05, 2.45), 'Paint', .04)
    body.tag = 'shell'
    # Arka servis gövdesi (konsept: kabin arkasından arkaya uzanan dolaplı kutu).
    # Üst plaka BED_TOP'ta; döner taban ve koniler bunun üstünde durur.
    body.span((-BODY_X, -.95, BED_TOP-.09), (BODY_X, 3.92, BED_TOP), 'Paint', .025)
    body.span((-.75, -.95, DECK_Z0), (.75, 3.92, BED_TOP-.09), 'Paint', 0)
    body.span((-BODY_X, -.95, DECK_Z0), (BODY_X, -.60, BED_TOP-.09), 'Paint', 0)
    body.span((-BODY_X, 3.85, .75), (BODY_X, 3.92, DECK_Z0), 'Paint', 0)
    body.span((-1.20, 3.80, .45), (1.20, 3.93, .72), 'Trim', .04)
    body.span((-.05, 3.90, .40), (.05, 3.96, .50), 'Trim', 0)
    hook_pts = [(0, 3.935+.05*math.sin(math.radians(a)), .40-.05*math.cos(math.radians(a))) for a in range(-90, 181, 45)]
    for a, b in zip(hook_pts, hook_pts[1:]):
        body.beam(a, b, .03, material='Trim')
    # Yan dolaplar: içi boş, raflı; dış yüz açık (kapaklar ayrı parça).
    top = BED_TOP-.09
    for sign in (-1, 1):
        def sx(a, b):
            return min(sign*a, sign*b), max(sign*a, sign*b)
        lo, hi = sx(.75, BODY_X)
        ilo, ihi = sx(.75, .78)
        slo, shi = sx(.78, 1.20)
        for (y0, y1), z0, shelf in (((-.60, .40), .42, 1.02), ((.92, 2.50), DECK_Z0, 1.38)):
            body.span((lo, y0, z0), (hi, y1, z0+.03), 'Paint', 0)
            body.span((ilo, y0, z0+.03), (ihi, y1, top), 'Paint', 0)
            body.span((lo, y0, z0+.03), (hi, y0+.03, top), 'Paint', 0)
            body.span((lo, y1-.03, z0+.03), (hi, y1, top), 'Paint', 0)
            body.span((slo, y0+.03, shelf), (shi, y1-.03, shelf+.02), 'Trim', 0)
        # Dolaplar arası ve arka dolu bloklar; tekerlek üstü açıklık düz kemer.
        body.span((lo, .40, .75), (hi, .92, top), 'Paint', 0)
        body.span((lo, 2.50, DECK_Z0), (hi, 3.92, top), 'Paint', 0)
        body.span((min(sign*.80, sign*1.10), 3.92, 1.28), (max(sign*.80, sign*1.10), 3.94, 1.48), 'Taillight', 0)
    # Destek ayağı kovanları: kiriş içinde kayar, içi boş dikdörtgen boru.
    for y0, y1 in ((-1.00, -.70), (3.30, 3.60)):
        body.span((-1.02, y0, .36), (1.02, y1, .385), 'Tire', 0)
        body.span((-1.02, y0, .555), (1.02, y1, .58), 'Tire', 0)
        body.span((-1.02, y0, .385), (1.02, y0+.015, .555), 'Tire', 0)
        body.span((-1.02, y1-.015, .385), (1.02, y1, .555), 'Tire', 0)
    # Koni tutucusu: gövde üstünde dört bölmeli ray (gövdenin parçası).
    y_lo, y_hi = CONE_YS[0]-.26, CONE_YS[-1]+.26
    for xa, xb in ((CONE_X-.235, CONE_X-.205), (CONE_X+.205, CONE_X+.235)):
        body.span((xa, y_lo, BED_TOP), (xb, y_hi, BED_TOP+.10), 'Trim', 0)
    for ya, yb in ((y_lo, y_lo+.03), (y_hi-.03, y_hi)):
        body.span((CONE_X-.235, ya, BED_TOP), (CONE_X+.235, yb, BED_TOP+.10), 'Trim', 0)
    for a, b in zip(CONE_YS, CONE_YS[1:]):
        m = (a+b)/2
        body.span((CONE_X-.205, m-.02, BED_TOP), (CONE_X+.205, m+.02, BED_TOP+.10), 'Trim', 0)
    body.finish()
    glass.finish()


def build_door(sign, side):
    geo = Geo(P+'_Door_F'+side)
    y0, y1, z0, z1 = -2.55, -1.22, 1.12, 2.32
    side_panel(geo, sign, [(y0, z0), (y1, z0), (y1, 1.55), (y0, 1.55)])
    wy0, wy1, wz0, wz1 = -2.47, -1.30, 1.62, 2.24
    for poly in ([(y0, 1.55), (y1, 1.55), (y1, wz0), (y0, wz0)], [(y0, wz1), (y1, wz1), (y1, z1), (y0, z1)],
                 [(y0, wz0), (wy0, wz0), (wy0, wz1), (y0, wz1)], [(wy1, wz0), (y1, wz0), (y1, wz1), (wy1, wz1)]):
        side_panel(geo, sign, poly)
    side_panel(geo, sign, [(wy0, wz0), (wy1, wz0), (wy1, wz1), (wy0, wz1)], 'Glass', .008, -.02)
    side_panel(geo, sign, [(wy0, wz0), (wy1, wz0), (wy1, wz0+.03), (wy0, wz0+.03)], 'Tire', .03, .005)
    geo.span((sign*CAB_X-.015, -1.55, 1.44), (sign*CAB_X+.015, -1.37, 1.49), 'Trim', 0)
    # Açılma: ön dikey menteşe, dışa doğru 70°.
    geo.finish((sign*CAB_X, y0, z0), motion=hinge((0, 0, -sign), 0, 70, 70,
               'Front vertical hinge on outer face; positive angle swings the rear edge outward.'))


def build_wheels():
    profile = [(-.17, .26), (-.168, .34), (-.13, .46), (-.07, .50), (.07, .50), (.13, .46), (.168, .34), (.17, .26)]
    n = 16
    for end, y in AXLES.items():
        for sign, side in ((1, 'L'), (-1, 'R')):
            center = Vector((sign*WHEEL_X, y, WHEEL_R))
            geo = Geo(f'{P}_Wheel_{end}_{side}')
            verts = [center+Vector((x, r*math.cos(2*math.pi*i/n), r*math.sin(2*math.pi*i/n)))
                     for x, r in profile for i in range(n)]
            for j in range(len(profile)-1):
                geo.patch(verts[j*n:(j+2)*n], [(i, (i+1) % n, (i+1) % n+n, i+n) for i in range(n)],
                          'Rim' if j in (0, 6) else 'Tire')
            for dx in (-.17, .17):
                geo.cylinder(center+Vector((dx, 0, 0)), .26, .008, 'Rim', (1, 0, 0), n)
                geo.cylinder(center+Vector((dx*1.05, 0, 0)), .13, .014, 'Trim', (1, 0, 0), 10)
            geo.finish(center, motion=spin((1, 0, 0), 0, 'Axle; rotation unrestricted.'))


def build_interior():
    geo = Geo(P+'_Interior')
    fy = AXLES['F']
    geo.span((-1.12, -2.00, .80), (1.12, -1.10, .86), 'Interior', 0)
    geo.span((-.82, -3.10, .80), (.82, -2.00, .86), 'Interior', 0)
    geo.span((-1.12, -3.12, .86), (1.12, -3.06, 1.30), 'Interior', 0)
    for sign in (-1, 1):
        well = [(sign*.84, y, z) for y, z in arc(fy, .50, .62, 18, 162, 8)]
        geo.prism(well, (sign*.28, 0, 0), 'Interior')
    geo.span((-1.12, -3.08, 1.28), (1.12, -2.76, 1.56), 'Interior', .05)
    # Sürücü solda (+X): direksiyon kolonu ve simidi.
    hub = Vector((.50, -2.50, 1.66))
    axis = Vector((0, -.80, .60)).normalized()
    geo.beam((.50, -2.80, 1.42), hub, .05, material='Trim')
    rot = axis.to_track_quat('Z', 'Y').to_matrix()
    ring = [hub+rot@Vector((.19*math.cos(2*math.pi*i/10), .19*math.sin(2*math.pi*i/10), 0)) for i in range(10)]
    for a, b in zip(ring, ring[1:]+ring[:1]):
        geo.beam(a, b, .035, material='Tire')
    for i in (0, 5):
        geo.beam(hub, ring[i], .025, material='Trim')
    for x in (.50, -.50):
        geo.span((x-.20, -1.82, .86), (x+.20, -1.42, 1.11), 'Trim', 0)
        geo.span((x-.275, -1.88, 1.11), (x+.275, -1.36, 1.25), 'Interior', .04)
        geo.box((x, -1.33, 1.60), (.55, .14, .70), 'Interior', .04,
                rotation=Matrix.Rotation(math.radians(-8), 3, 'X'))
        geo.span((x-.16, -1.36, 1.97), (x+.16, -1.24, 2.15), 'Interior', .03)
    geo.finish()


def build_locker_doors():
    # Konsept: öndeki uzun dolabın menteşeleri ön dikey kenarda, tekerlek üstündekinin üst kenarda.
    xo_abs = BODY_X-.02   # kapak dış yüzü 2 cm içeride; kol/menteşe çıkıntısı 2.5 m'yi aşmaz
    for sign, side in ((1, 'L'), (-1, 'R')):
        xo = sign*xo_abs
        def sx(a, b):
            return min(a, b), max(a, b)
        for index, (y0, y1, z0, z1) in enumerate(((-.565, .365, .455, 1.64), (.955, 2.465, DECK_Z0+.035, 1.64)), 1):
            geo = Geo(f'{P}_LockerDoor_{side}{index}')
            lo, hi = sx(xo, xo-sign*.03)
            geo.span((lo, y0, z0), (hi, y1, z1), 'Paint', 0)
            olo, ohi = sx(xo, xo+sign*.02)
            if index == 1:
                geo.span((olo, y1-.10, 1.00), (ohi, y1-.06, 1.20), 'Trim', 0)
                # İnce menteşe tırnakları: açılırken dolap ön duvarına girmez.
                hlo, hhi = sx(xo, xo+sign*.01)
                for zh in (z0+.15, z1-.15):
                    geo.span((hlo, y0, zh-.05), (hhi, y0+.04, zh+.05), 'Trim', 0)
                geo.finish((xo, y0, z0), motion=hinge((0, 0, -sign), 0, 90, 90,
                           'Front vertical hinge on outer face; positive angle swings the rear edge outward.'))
            else:
                yc = (y0+y1)/2
                geo.span((olo, yc-.10, z0+.05), (ohi, yc+.10, z0+.09), 'Trim', 0)
                for yh in (y0+.15, y1-.15):
                    geo.span((olo, yh-.05, z1-.04), (ohi, yh+.05, z1), 'Trim', 0)
                geo.finish((xo, yc, z1), motion=hinge((0, -sign, 0), 0, 90, 90,
                           'Top horizontal hinge on outer face; positive angle lifts the lower edge outward.'))


def build_outriggers():
    # Orta ayaklar kabin arkasında, arka ayaklar arka tekerleklerin arkasında; sol/sağ kirişler kovanda yan yana.
    for key, sign, y in (('ML', 1, -.78), ('MR', -1, -.92), ('RL', 1, 3.52), ('RR', -1, 3.38)):
        beam = Geo(f'{P}_Outrigger_{key}_Beam')
        end = sign*1.10
        beam.span((min(end, -sign*.95), y-.06, .40), (max(end, -sign*.95), y+.06, .54), 'Paint', 0)
        beam.span((end-.08, y-.08, .40), (end+.08, y+.08, 1.05), 'Paint', .02)
        beam.finish((end, y, .47), motion=slide((sign, 0, 0), .90, .90, 'Beam slides sideways out of the chassis sleeve.'))
        jack = Geo(f'{P}_Outrigger_{key}_Jack')
        jack.span((end-.05, y-.05, .34), (end+.05, y+.05, 1.00), 'Trim', 0)
        jack.span((end-.13, y-.13, .30), (end+.13, y+.13, .34), 'Tire', 0)
        jack.finish((end, y, .30), parent=f'{P}_Outrigger_{key}_Beam',
                    motion=slide((0, 0, -1), .30, .30, 'Jack lowers until the pad bottom touches Z=0.'))


def build_crane():
    t = TURNTABLE
    tt = Geo(P+'_Crane_Turntable')
    tt.cylinder(t+Vector((0, 0, .04)), .75, .08, 'Trim', segments=16)
    tt.cylinder(t+Vector((0, 0, .14)), .68, .12, 'Paint', segments=16)
    top = t.z+.20
    for sign in (-1, 1):
        prof = [(2.70, top), (3.77, top)]+[(HINGE.y+.17*math.cos(math.radians(a)), HINGE.z+.17*math.sin(math.radians(a)))
                                             for a in range(0, 181, 30)]
        tt.prism([(sign*.215, y, z) for y, z in prof], (sign*.055, 0, 0), 'Paint')
    tt.cylinder(HINGE, .075, .62, 'Trim', (1, 0, 0), 10)
    tt.span((-.70, 2.85, top), (-.66, 3.25, t.z+.60), 'Paint', 0)
    tt.finish(t, motion=spin((0, 0, 1), -90, 'Turntable; full turn. Work pose -90 turns the boom to vehicle right (-X).'))
    drum_c = Vector((-.47, 3.05, t.z+.47))
    drum = Geo(P+'_Crane_Drum')
    drum.cylinder(drum_c, .17, .30, 'Tire', (1, 0, 0), 12)
    for dx in (-.165, .165):
        drum.cylinder(drum_c+Vector((dx, 0, 0)), .25, .03, 'Trim', (1, 0, 0), 12)
    drum.finish(drum_c, parent=P+'_Crane_Turntable', motion=spin((1, 0, 0), 0, 'Cable drum spins about its own axis.'))
    empty('CableStart', drum_c+Vector((0, 0, .17)), P+'_Crane_Turntable')
    # Kol: menteşeden uç makarasına kapalı 6.0 m; iki teleskop kademesi.
    def s(v):
        return HINGE+Vector((0, -v, 0))
    boom = Geo(P+'_Crane_Boom')
    boom.span((-.20, s(3.90).y, HINGE.z-.18), (.20, s(-.25).y, HINGE.z+.18), 'Paint', .03)
    boom.finish(HINGE, parent=P+'_Crane_Turntable', motion=hinge((-1, 0, 0), 0, 70, 35, 'Horizontal hinge; positive angle raises the boom tip.'))
    e1 = Geo(P+'_Crane_BoomExt_1')
    e1.span((-.16, s(4.60).y, HINGE.z-.14), (.16, s(1.30).y, HINGE.z+.14), 'Paint', .025)
    e1.finish(s(1.30), parent=P+'_Crane_Boom',
              motion=slide((0, -1, 0), EXT_STROKE, EXT_STROKE, 'Slides along the boom axis (rest-pose -Y), stage 1.'))
    e2 = Geo(P+'_Crane_BoomExt_2')
    e2.span((-.12, s(5.75).y, HINGE.z-.10), (.12, s(1.60).y, HINGE.z+.10), 'Paint', .02)
    for sign in (-1, 1):
        e2.span((min(sign*.08, sign*.11), s(6.12).y, HINGE.z-.14), (max(sign*.08, sign*.11), s(5.70).y, HINGE.z+.14), 'Paint', 0)
    e2.cylinder(s(BOOM_CLOSED), .17, .12, 'Trim', (1, 0, 0), 12)
    e2.cylinder(s(BOOM_CLOSED), .04, .26, 'Tire', (1, 0, 0), 8)
    e2.finish(s(1.60), parent=P+'_Crane_BoomExt_1',
              motion=slide((0, -1, 0), EXT_STROKE, EXT_STROKE, 'Slides along the boom axis (rest-pose -Y), stage 2.'))
    tip = s(BOOM_CLOSED)
    empty('CableTip', tip, P+'_Crane_BoomExt_2')
    hook = Geo(P+'_Crane_Hook')
    hp = tip+Vector((0, 0, -.22))
    hook.span(hp+Vector((-.07, -.05, -.12)), hp+Vector((.07, .05, 0)), 'Trim', .02)
    hook.beam(hp+Vector((0, 0, -.12)), hp+Vector((0, 0, -.17)), .035, material='Trim')
    c = hp+Vector((0, 0, -.225))
    pts = [c+Vector((0, .055*math.sin(math.radians(a)), .055*math.cos(math.radians(a)))) for a in range(0, 271, 45)]
    for a, b in zip(pts, pts[1:]):
        hook.beam(a, b, .032, material='Trim')
    hook.finish(hp, parent='CableTip', motion={'type': 'hang', 'rest_offset_below_cable_tip_m': .22,
                'work_pose_offset_below_cable_tip_m': 2.0,
                'note': 'Hook hangs world-vertical below CableTip; rope length is driven in Unity (rope drawn by code).'})


def build_lightbar():
    geo = Geo(P+'_LightBar')
    geo.span((-.62, -2.84, 2.45), (.62, -2.64, 2.50), 'Trim', 0)
    geo.span((-.58, -2.82, 2.50), (.58, -2.66, 2.62), 'Beacon', .03)
    geo.finish((0, -2.74, 2.45), motion=None)


def build_cones():
    heights = [.05, .25, .33, .45, .53, .70]
    mats = ['ConeOrange', 'ConeWhite', 'ConeOrange', 'ConeWhite', 'ConeOrange']
    n = 12
    for index, y in enumerate(CONE_YS, 1):
        base = Vector((CONE_X, y, BED_TOP))
        geo = Geo(f'{P}_Cone_{index:02d}')
        geo.span(base+Vector((-.20, -.20, 0)), base+Vector((.20, .20, .05)), 'ConeOrange', 0)
        rings = []
        for z in heights:
            r = .18-(.18-.03)*(z-.05)/.65
            rings.append([base+Vector((r*math.cos(2*math.pi*i/n), r*math.sin(2*math.pi*i/n), z)) for i in range(n)])
        for j, mat in enumerate(mats):
            geo.patch(rings[j]+rings[j+1], [(i, (i+1) % n, (i+1) % n+n, i+n) for i in range(n)], mat)
        geo.patch(rings[-1], [tuple(range(n))], 'ConeOrange')
        geo.finish(base, motion={'type': 'free_rigid_body', 'attached_initially': True,
                                 'note': 'Standing upright in the holder; gameplay may release it.'})


# ---------------------------------------------------------------- poz, özet, render

def set_pose(meta, work):
    for name, part in meta['parts'].items():
        obj = bpy.data.objects[name]
        rest = Vector(part['pivot_m'])-(Vector(meta['parts'][part['parent']]['pivot_m']) if part['parent'] in meta['parts'] else Vector())
        if part['parent'] in ('CableTip',):
            rest = Vector(part['pivot_m'])-Vector(meta['markers']['CableTip'])
        obj.location = rest
        obj.rotation_mode = 'AXIS_ANGLE'
        obj.rotation_axis_angle = (0, 0, 0, 1)
        motion = part['motion']
        if not work or not motion:
            continue
        if motion['type'] in ('hinge', 'rotate_continuous'):
            obj.rotation_axis_angle = (math.radians(motion['work_pose_deg']), *motion['axis'])
        elif motion['type'] == 'slide':
            obj.location = rest+Vector(motion['axis'])*motion['work_pose_m']
    if work:
        # Kanca dünya düşeyinde sarkar: kol eğimini geri al, CableTip altına in.
        bpy.context.view_layer.update()
        hook = bpy.data.objects[P+'_Crane_Hook']
        tip = bpy.data.objects['CableTip'].matrix_world
        drop = meta['parts'][P+'_Crane_Hook']['motion']['work_pose_offset_below_cable_tip_m']
        hook.matrix_world = Matrix.Translation(tip.translation+Vector((0, 0, -drop)))
    bpy.context.view_layer.update()


def geometry_digest():
    signature = []
    for obj in sorted(ASSETS, key=lambda o: o.name):
        row = {'name': obj.name, 'parent': obj.parent.name if obj.parent else None,
               'pivot': [round(float(c), 7) for c in obj.location]}
        if obj.type == 'MESH':
            row.update(vertices=[[round(float(c), 7) for c in v.co] for v in obj.data.vertices],
                       faces=[list(p.vertices) for p in obj.data.polygons],
                       materials=[obj.data.materials[p.material_index].name for p in obj.data.polygons])
        signature.append(row)
    return hashlib.sha256(json.dumps(signature, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def canonical_geometry():
    data = []
    for obj in sorted((o for o in ASSETS if o.type == 'MESH'), key=lambda o: o.name):
        verts = [obj.matrix_world@v.co for v in obj.data.vertices]
        obj.data.calc_loop_triangles()
        triangles = []
        for tri in obj.data.loop_triangles:
            corners = sorted(tuple(math.floor(float(v)*10000+.371) for v in verts[i]) for i in tri.vertices)
            triangles.append([corners, obj.data.materials[tri.material_index].name])
        data.append([obj.name, sorted(triangles)])
    return hashlib.sha256(json.dumps(data, separators=(',', ':')).encode()).hexdigest()


def setup_render():
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 64
    scene.cycles.use_denoising = True
    scene.cycles.seed = 0
    scene.cycles.use_animated_seed = False
    scene.cycles.max_bounces = 8
    scene.cycles.transmission_bounces = 6
    scene.cycles.transparent_max_bounces = 12
    scene.cycles.adaptive_threshold = .025
    prefs = bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type = 'OPTIX'
    prefs.refresh_devices()
    gpu = [d for d in prefs.devices if d.type == 'OPTIX']
    for device in prefs.devices:
        device.use = device.type == 'OPTIX'
    scene.cycles.device = 'GPU' if gpu else 'CPU'
    scene['render_device'] = 'OPTIX: '+', '.join(d.name for d in gpu) if gpu else 'CPU'
    print('RENDER DEVICE', scene['render_device'], flush=True)
    scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium High Contrast'
    scene.view_settings.exposure = -.50
    world = bpy.data.worlds.new('Studio_World')
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (.82, .82, .82, 1)
    world.node_tree.nodes['Background'].inputs[1].default_value = .35
    nodes, links = world.node_tree.nodes, world.node_tree.links
    camera_background = nodes.new('ShaderNodeBackground')
    camera_background.inputs[0].default_value = (2.3, 2.3, 2.3, 1)
    camera_background.inputs[1].default_value = 1
    light_path = nodes.new('ShaderNodeLightPath')
    mix = nodes.new('ShaderNodeMixShader')
    links.new(light_path.outputs['Is Camera Ray'], mix.inputs[0])
    links.new(nodes['Background'].outputs[0], mix.inputs[1])
    links.new(camera_background.outputs[0], mix.inputs[2])
    links.new(mix.outputs[0], nodes['World Output'].inputs['Surface'])
    bpy.ops.mesh.primitive_plane_add(size=300, location=(0, 0, -.012))
    floor = bpy.context.object
    floor.name = 'RENDER_StudioFloor'
    mat = bpy.data.materials.new('RENDER_Studio')
    mat.diffuse_color = (.79, .81, .83, 1)
    floor.data.materials.append(mat)
    for name, position, power, size in [('Key', (7, -9, 12), 1700, 8), ('Fill', (-8, -4, 7), 1000, 7), ('Rim', (2, 10, 10), 1800, 6)]:
        data = bpy.data.lights.new('RENDER_'+name, 'AREA')
        obj = bpy.data.objects.new(data.name, data)
        bpy.context.collection.objects.link(obj)
        obj.location = position
        obj.rotation_euler = (Vector((0, 0, 1.5))-obj.location).to_track_quat('-Z', 'Y').to_euler()
        data.energy, data.shape, data.size = power, 'DISK', size
    data = bpy.data.cameras.new('RENDER_Camera')
    camera = bpy.data.objects.new(data.name, data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera
    data.type = 'ORTHO'
    data.clip_end = 200
    return camera


def interior_roof_mesh():
    body = bpy.data.objects[P+'_Body']
    copy = body.data.copy()
    group = body.vertex_groups['roof_hide_for_interior'].index
    remove = {v.index for v in body.data.vertices if any(g.group == group for g in v.groups)}
    bm = bmesh.new()
    bm.from_mesh(copy)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in sorted(remove)], context='VERTS')
    bm.to_mesh(copy)
    bm.free()
    return copy


def write_png(path, pixels):
    h, w = pixels.shape[:2]
    def chunk(kind, data):
        return struct.pack('!I', len(data))+kind+data+struct.pack('!I', zlib.crc32(kind+data) & 0xffffffff)
    raw = b''.join(b'\0'+pixels[y].tobytes() for y in range(h))
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR', struct.pack('!2I5B', w, h, 8, 2, 0, 0, 0)) +
                     chunk(b'IDAT', zlib.compress(raw, 6))+chunk(b'IEND', b''))


def image_array(path):
    im = bpy.data.images.load(str(path), check_existing=False)
    a = np.array(im.pixels[:], dtype=np.float32).reshape(im.size[1], im.size[0], 4)[::-1, :, :3]
    bpy.data.images.remove(im)
    return np.clip(a*255+.5, 0, 255).astype(np.uint8)


def autocrop(a, margin=30):
    # Render içindeki aracı kırpar: köşe arka plan renginden belirgin farklı pikseller.
    mask = np.abs(a.astype(np.int16)-a[2, 2].astype(np.int16)).max(axis=2) > 40
    ys, xs = np.nonzero(mask)
    if not len(ys):
        return a
    return a[max(0, ys.min()-margin):ys.max()+margin, max(0, xs.min()-margin):xs.max()+margin]


def side_by_side(left, right, path):
    canvas = np.full((1080, 1920, 3), 241, dtype=np.uint8)
    for index, a in enumerate((left, autocrop(right))):
        scale = min(930/a.shape[1], 990/a.shape[0])
        w, h = round(a.shape[1]*scale), round(a.shape[0]*scale)
        resized = a[(np.arange(h)/scale).astype(int)[:, None], (np.arange(w)/scale).astype(int)[None, :]]
        x, y = index*960+(960-w)//2, (1080-h)//2
        canvas[y:y+h, x:x+w] = resized
    canvas[:, 957:963] = (130, 147, 155)
    write_png(path, canvas)


# Konseptteki görünümlerin kırpma kutuları (x0, y0, x1, y1; sol üstten piksel).
CONCEPT_CROPS = {'front': ((90, 45, 365, 390), 'front'), 'rear': ((400, 45, 675, 390), 'rear'),
                 'side': ((730, 40, 1340, 390), 'left'), 'threequarter': ((810, 400, 1310, 730), 'threequarter_front_left')}


def comparisons(folder):
    concept = image_array(CONCEPT)
    for name, ((x0, y0, x1, y1), view) in CONCEPT_CROPS.items():
        side_by_side(concept[y0:y1, x0:x1], image_array(folder/(view+'.png')), folder/f'compare_{name}.png')


def render(folder, name, position, target, scale, durations):
    camera = bpy.context.scene.camera
    camera.location = position
    camera.rotation_euler = (Vector(target)-camera.location).to_track_quat('-Z', 'Y').to_euler()
    if name == 'interior_top':
        camera.rotation_euler = (0, 0, math.pi/2)
    camera.data.ortho_scale = scale
    bpy.context.scene.render.filepath = str(folder/(name+'.png'))
    started = time.perf_counter()
    bpy.ops.render.render(write_still=True)
    durations[name] = round(time.perf_counter()-started, 3)
    print('RENDER OK', name, durations[name], flush=True)


def render_views(meta):
    folder = OUT/'renders'/f'round_{ROUND:02d}'
    folder.mkdir(parents=True, exist_ok=True)
    setup_render()
    views = [('front', (0, -30, 1.65), (0, 0, 1.65), 6.8, False),
             ('rear', (0, 30, 1.65), (0, 0, 1.65), 6.8, False),
             ('left', (30, 0, 1.65), (0, 0, 1.65), 9.6, False),
             ('right', (-30, 0, 1.65), (0, 0, 1.65), 9.6, False),
             ('threequarter_front_left', (16, -20, 11), (0, -.2, 1.55), 10.2, False),
             ('threequarter_rear_right_work', (-22, 18, 12), (-3.2, .6, 3.4), 15.5, True),
             ('interior_top', (0, -2.25, 15), (0, -2.25, 0), 4.6, False)]
    durations = {}
    hidden_for_top = [P+'_Crane_Boom', P+'_Crane_BoomExt_1', P+'_Crane_BoomExt_2', P+'_Crane_Hook', P+'_LightBar']
    for name, position, target, scale, work in views:
        set_pose(meta, work)
        body = bpy.data.objects[P+'_Body']
        original = body.data
        if name == 'interior_top':
            body.data = interior_roof_mesh()
            for n in hidden_for_top:
                bpy.data.objects[n].hide_render = True
        render(folder, name, position, target, scale, durations)
        if name == 'interior_top':
            temporary = body.data
            body.data = original
            bpy.data.meshes.remove(temporary)
            for n in hidden_for_top:
                bpy.data.objects[n].hide_render = False
    set_pose(meta, False)
    # Ölçek karşılaştırması: minibüs yalnız render için içe aktarılır, sonra silinir.
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(MINIVAN_FBX), use_anim=False)
    imported = [o for o in bpy.data.objects if o not in before]
    for o in imported:
        if o.parent is None:
            o.location = (0, -7.0, 0)
    render(folder, 'scale_with_minivan', (30, -2.75, 1.9), (0, -2.75, 1.9), 14.6, durations)
    mats = {s.material for o in imported if o.type == 'MESH' for s in o.material_slots if s.material}
    meshes = {o.data for o in imported if o.type == 'MESH'}
    for o in imported:
        bpy.data.objects.remove(o)
    for m in meshes:
        bpy.data.meshes.remove(m)
    for m in mats:
        bpy.data.materials.remove(m)
    comparisons(folder)
    for png in sorted(folder.glob('*.png')):
        shutil.copy2(png, OUT/'renders'/png.name)
    return durations


def main():
    global ROUND
    parser = argparse.ArgumentParser()
    parser.add_argument('--round', type=int, default=ROUND)
    parser.add_argument('--no-render', action='store_true', help='Geliştirme için; kabul build varsayılanı render alır')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    ROUND = args.round
    started = time.perf_counter()
    (OUT/'renders').mkdir(parents=True, exist_ok=True)
    cache = Path(tempfile.gettempdir())/'towtruck_render_cache'
    cache.mkdir(exist_ok=True)
    for name in ('OPTIX_CACHE_PATH', 'CUDA_CACHE_PATH'):
        os.environ[name] = str(cache)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.context.preferences.filepaths.use_auto_save_temporary_files = False
    bpy.context.preferences.filepaths.file_preview_type = 'NONE'
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1
    samples = reference_palette()
    make_materials()
    root = empty(P, (0, 0, 0), None)
    build_body()
    build_interior()
    build_door(1, 'L')
    build_door(-1, 'R')
    build_wheels()
    build_lightbar()
    build_locker_doors()
    build_outriggers()
    build_crane()
    build_cones()
    bpy.context.view_layer.update()
    markers = {n: [round(c, 6) for c in WORLD_PIVOT[n]] for n in ('CableStart', 'CableTip')}
    meta = {
        'schema_version': 1, 'units': 'metres',
        'coordinates': {'up': '+Z', 'forward': '-Y', 'left': '+X', 'right': '-X',
                        'note': 'Blender coordinates; FBX exported with axis_forward=-Z, axis_up=Y, FBX_SCALE_ALL.'},
        'field_descriptions': {
            'parts': 'Object-name map. All pivots/axes are in root (Blender world) coordinates in the REST pose; no part has rest rotation.',
            'pivot_m': 'XYZ object origin in metres.',
            'parent': 'Direct FBX parent object name.',
            'motion': 'null = static. Values are applied in the parent\'s local frame (equal to root frame at rest).',
            'motion.type': 'hinge (limited rotation) / rotate_continuous / slide / hang / free_rigid_body.',
            'motion.axis': 'Unit rotation or translation axis; positive values follow the right-hand rule / axis direction.',
            'motion.range_deg': '[min, max] hinge angle in degrees; 0 = rest. null = unrestricted rotation.',
            'motion.range_m': '[min, max] slide distance in metres along axis; 0 = rest.',
            'motion.work_pose_deg / work_pose_m': 'Value used for the work-pose render and the verify collision test.',
            'motion.rest_offset_below_cable_tip_m': 'Hook pivot distance below CableTip in the rest pose.',
            'markers': 'Empties. CableStart = rope leaves the drum (child of turntable). CableTip = tip sheave centre (child of BoomExt_2).',
            'boom': 'Boom hinge to CableTip distance: closed and fully telescoped.',
            'cones': 'Cone pivots = base-bottom centres standing in the holder on the deck.',
            'measurement': 'Envelope excludes mirrors; height excludes the light bar.',
            'palette_samples': 'sRGB hex + top-left pixel coordinates (5x5 median) in the cited concept.'},
        'parts': PARTS,
        'markers': markers,
        'boom': {'hinge_m': list(HINGE), 'closed_length_m': BOOM_CLOSED,
                 'extended_length_m': BOOM_CLOSED+2*EXT_STROKE, 'stages': [P+'_Crane_BoomExt_1', P+'_Crane_BoomExt_2'],
                 'stage_stroke_m': EXT_STROKE, 'raise_range_deg': [0, 70]},
        'cones': {f'{P}_Cone_{i:02d}': [CONE_X, y, BED_TOP] for i, y in enumerate(CONE_YS, 1)},
        'measurement': {'length_m': 8.0, 'width_m': 2.5, 'height_m': 3.2, 'lightbar_top_max_m': 3.4,
                        'tolerance_m': .05, 'wheel_diameter_m': 2*WHEEL_R,
                        'axle_y_m': AXLES, 'wheel_x_m': WHEEL_X},
        'materials': {f'M_TowTruck_{n}': '#'+c for n, c in PALETTE.items()},
        'palette_reference': 'ArtSource/TowTruck/references/01_turnaround_orange.jpg',
        'palette_samples': samples, 'glass_alpha': .34,
        'unity_import': {'scale_factor': 1, 'bake_axis_conversion': False,
                         'color_note': 'FBX DiffuseColor is linear (Blender default) for the Linear URP project.',
                         'glass_note': 'FBX stores alpha; choose a transparent shader in URP.',
                         'lightbar_note': 'SM_TowTruck_LightBar is static; blink it in Unity (emission).'}}
    (OUT/'towtruck_meta.json').write_text(json.dumps(meta, indent=2), encoding='utf-8')
    counts = {o.name: len(o.data.polygons) for o in ASSETS if o.type == 'MESH'}
    total = sum(counts.values())
    print('TRIANGLES', total, json.dumps(counts), flush=True)
    if not 6000 <= total <= 9000:
        raise ValueError(f'Triangle budget: {total}')
    digest = geometry_digest()
    canonical = canonical_geometry()
    bpy.ops.object.select_all(action='DESELECT')
    for obj in ASSETS:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(OUT/'SM_TowTruck.fbx'), use_selection=True,
                             object_types={'EMPTY', 'MESH'}, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE',
                             use_mesh_modifiers=False, bake_anim=False, use_custom_props=True,
                             check_existing=False, add_leaf_bones=False)
    durations = {} if args.no_render else render_views(meta)
    set_pose(meta, False)
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'TowTruck.blend'), check_existing=False)
    stats_path = OUT/'stats.json'
    old = json.loads(stats_path.read_text(encoding='utf-8')) if stats_path.exists() else {}
    source = hashlib.sha256(Path(__file__).read_bytes().replace(b'\r\n', b'\n')).hexdigest()
    entry = {'geometry_sha256': digest, 'canonical_geometry_sha256': canonical, 'source_sha256': source,
             'round': ROUND, 'triangles': total, 'render_seconds': round(sum(durations.values()), 3),
             'camera_render_seconds': durations,
             'render_device': bpy.context.scene.get('render_device', 'not rendered'),
             'total_build_seconds': round(time.perf_counter()-started, 3)}
    history = (old.get('build_history', [])+[entry])[-6:]
    stats = {'geometry_sha256': digest, 'canonical_geometry_sha256': canonical, 'total_triangles': total,
             'object_triangles': counts, 'materials': meta['materials'], 'last_build': entry,
             'build_history': history}
    stats_path.write_text(json.dumps(stats, indent=2), encoding='utf-8')
    print('BUILD OK', digest, 'triangles', total, 'render_seconds', entry['render_seconds'], flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        print('BUILD FAILED', flush=True)
        sys.exit(1)
