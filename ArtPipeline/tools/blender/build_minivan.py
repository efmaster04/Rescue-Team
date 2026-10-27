"""Blender 5.2: metre ölçeğinde, parçalı ve deterministik mahsur minibüs."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import struct
import sys
import time
import traceback
import zlib
sys.dont_write_bytecode = True

import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Export/Minivan'
REF = ROOT / 'ArtSource/Minivan/references'
MATERIALS = {}
ASSETS = []
PARTS = {}
PALETTE = {}
ROUND = 4


def srgb_to_linear(v):
    return v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4


def reference_palette():
    # Piksel koordinatları sol üstten; 5x5 medyan JPEG gürültüsünü bastırır.
    # Renk seçenekleri gün batımıyla sarartılmıştır; ana turnaround nötr albedo kaynağıdır.
    image = bpy.data.images.load(str(REF / '01_turnaround_light_blue.jpg'))
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(image.size[1], image.size[0], 4)
    locations = {'Paint': (210, 655), 'Trim': (237, 680), 'Tire': (987, 718),
                 'Rim': (1001, 710), 'Glass': (238, 180), 'Interior': (194, 207),
                 'Headlight': (159, 286), 'Taillight': (324, 642),
                 'Suitcase_A': (1030, 418), 'Suitcase_B': (1066, 439),
                 'Strap': (1099, 419), 'Damage': (309, 278)}
    record = {}
    for name, (x, y) in locations.items():
        rgb = np.median(pixels[image.size[1]-y-3:image.size[1]-y+2, x-2:x+3, :3], axis=(0, 1))
        color = ''.join(f'{round(float(c)*255):02X}' for c in rgb)
        PALETTE[name] = color
        record[name] = {'hex': '#' + color, 'source_pixel_xy_top_left': [x, y], 'median_patch_px': 5}
    bpy.data.images.remove(image)
    return record


def make_materials():
    for name, color in PALETTE.items():
        mat = bpy.data.materials.new('M_Minivan_' + name)
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
        self.tags.extend([self.tag] * len(vertices))
        self.faces.extend(tuple(offset+i for i in f) for f in faces)
        self.materials.extend([material] * len(faces))

    def box(self, center, size, material='Paint', bevel=.025, segments=1, rotation=None):
        # Küçük çerçeve/kayışlarda bevel yerine keskin low-poly kenar bütçeyi korur.
        segments = 1
        if min(size) < .08:
            bevel = 0
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1)
        for v in bm.verts:
            v.co = Vector(tuple(v.co[i]*size[i] for i in range(3)))
        if bevel:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(size)*.45),
                            segments=segments, affect='EDGES', profile=.5)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.verts.ensure_lookup_table()
        bm.verts.index_update()
        rot = rotation or Matrix.Identity(3)
        self.patch([rot @ v.co + Vector(center) for v in bm.verts],
                   [[v.index for v in f.verts] for f in bm.faces], material)
        bm.free()

    def beam(self, a, b, width, depth=None, material='Paint', bevel=.006):
        delta = Vector(b)-Vector(a)
        self.box((Vector(a)+Vector(b))/2, (width, depth or width, delta.length), material,
                 bevel, rotation=delta.to_track_quat('Z', 'Y').to_matrix())

    def prism(self, polygon, offset, material='Paint'):
        # Çokgenin ekstrüzyonu: kaporta levhaları ve kapı çerçeveleri gerçek hacimlidir.
        n = len(polygon)
        self.patch(list(polygon)+[Vector(p)+Vector(offset) for p in polygon],
                   [tuple(reversed(range(n))), tuple(range(n, 2*n))] +
                   [(i, (i+1)%n, (i+1)%n+n, i+n) for i in range(n)], material)

    def cylinder(self, center, radius, depth, material, axis=(0, 0, 1), segments=16):
        rot = Vector(axis).to_track_quat('Z', 'Y').to_matrix()
        vertices = [rot @ Vector((radius*math.cos(2*math.pi*i/segments),
                                  radius*math.sin(2*math.pi*i/segments), z)) + Vector(center)
                    for z in (-depth/2, depth/2) for i in range(segments)]
        self.patch(vertices, [tuple(reversed(range(segments))), tuple(range(segments, 2*segments))] +
                   [(i, (i+1)%segments, (i+1)%segments+segments, i+segments) for i in range(segments)], material)

    def torus(self, center, major, minor, material, axis=(0, 0, 1), rings=16, sides=6):
        rot = Vector(axis).to_track_quat('Z', 'Y').to_matrix()
        verts = []
        for i in range(rings):
            a = 2*math.pi*i/rings
            for j in range(sides):
                b = 2*math.pi*j/sides
                verts.append(rot @ Vector(((major+minor*math.cos(b))*math.cos(a),
                                          (major+minor*math.cos(b))*math.sin(a), minor*math.sin(b)))+Vector(center))
        self.patch(verts, [(i*sides+j, ((i+1)%rings)*sides+j,
                           ((i+1)%rings)*sides+(j+1)%sides, i*sides+(j+1)%sides)
                          for i in range(rings) for j in range(sides)], material)

    def finish(self, pivot=(0, 0, 0), axis=None, motion=None):
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata([Vector(v)-Vector(pivot) for v in self.vertices], [], self.faces)
        names = list(MATERIALS)
        for mat in MATERIALS.values():
            mesh.materials.append(mat)
        for poly, name in zip(mesh.polygons, self.materials):
            poly.material_index = names.index(name)
        # Düz normal + gerçek üçgenler; export sırasında topoloji değişmez.
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='FIXED', ngon_method='EAR_CLIP')
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.location = pivot
        obj.parent = bpy.data.objects['SM_Minivan']
        # İç mekân render'ında yalnız çatı/raf yüzleri geçici çıkarılır.
        for tag in sorted(set(self.tags)):
            group = obj.vertex_groups.new(name=tag)
            group.add([i for i, t in enumerate(self.tags) if t == tag], 1, 'REPLACE')
        ASSETS.append(obj)
        PARTS[self.name] = {'pivot_m': list(pivot), 'axis': list(axis) if axis else None,
                            'motion': motion, 'parent': 'SM_Minivan'}
        return obj


def empty(name, position, parent):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.location = position
    obj.empty_display_size = .12
    obj.parent = parent
    ASSETS.append(obj)
    return obj


def side_x(sign, z):
    return sign*(.95 - max(0, z-1.15)*.125)


def side_panel(geo, sign, yz, material='Paint', thickness=.045, outside=0):
    geo.prism([(side_x(sign, z)+sign*outside, y, z) for y, z in yz],
              (-sign*thickness, 0, 0), material)


def side_window(geo, sign, y0, y1, z0=1.22, z1=1.82):
    side_panel(geo, sign, [(y0, z0), (y1, z0), (y1, z1), (y0, z1)], 'Glass', .008, -.016)


def arch_panel(geo, sign, center_y, y0, y1, top=1.10):
    radius, zc = .425, .375
    pts = [(y0, .52), (center_y-radius, .52)]
    pts += [(center_y-radius*math.cos(math.pi*i/10), zc+radius*math.sin(math.pi*i/10)) for i in range(1, 10)]
    pts += [(center_y+radius, .52), (y1, .52), (y1, top), (y0, top)]
    side_panel(geo, sign, pts)
    # İnce dış çamurluk dudağı; tekerlek boşluğu kapatılmaz.
    for i in range(10):
        a, b = math.pi*i/10, math.pi*(i+1)/10
        r0, r1 = .415, .458
        poly = [(center_y-r0*math.cos(a), zc+r0*math.sin(a)),
                (center_y-r0*math.cos(b), zc+r0*math.sin(b)),
                (center_y-r1*math.cos(b), zc+r1*math.sin(b)),
                (center_y-r1*math.cos(a), zc+r1*math.sin(a))]
        side_panel(geo, sign, poly, thickness=.06)


def build_body():
    body, glass = Geo('SM_Minivan_Body'), Geo('SM_Minivan_Glass')
    # Kısa kaput: 0.68 m; cam altı geride, yüksek ve hafif daralan tavan.
    body.box((0, -2.07, .89), (1.84, .68, .51), bevel=.10, segments=2)
    body.box((0, -1.93, 1.135), (1.81, .91, .12), bevel=.055)
    # Ön yüz radyatör ızgarası, markasız, plakasız.
    body.box((0, -2.423, .90), (.85, .045, .255), 'Trim', .025)
    for z in (.83, .90, .97):
        body.box((0, -2.451, z), (.72, .014, .016), 'Damage', .004)
    for sign in (-1, 1):
        body.box((sign*.665, -2.428, .944), (.39, .055, .30), 'Trim', .05, segments=2)
        if sign == 1:
            # Kırık far koyu boşluk ve kenarda kalan iki iri cam parçasıdır.
            body.box((sign*.665, -2.460, .944), (.30, .012, .215), 'Damage', .025)
            body.prism([(.52, -2.471, .855), (.80, -2.471, .855), (.80, -2.471, .940),
                        (.733, -2.471, .923), (.685, -2.471, .984), (.625, -2.471, .931),
                        (.57, -2.471, 1.029), (.52, -2.471, 1.029)], (0, .007, 0), 'Headlight')
        else:
            body.box((sign*.665, -2.461, .944), (.30, .017, .215), 'Headlight', .025)
    # Tampon ortası kıvrık; uzunluk sınırı tam -2.5 m.
    body.box((-.43, -2.409, .602), (.99, .18, .18), 'Trim', .04,
             rotation=Matrix.Rotation(math.radians(-2), 3, 'Y'))
    body.box((.43, -2.409, .577), (.93, .18, .18), 'Trim', .04,
             rotation=Matrix.Rotation(math.radians(7), 3, 'Y'))
    # Ön cam etrafında gerçek açık çerçeve.
    corners = [(-.81, -1.74, 1.18), (.81, -1.74, 1.18), (.78, -1.27, 1.885), (-.78, -1.27, 1.885)]
    glass.prism(corners, (0, .010, 0), 'Glass')
    for a, b in zip(corners, corners[1:]+corners[:1]):
        body.beam(a, b, .045, .045, 'Tire')
    for sign in (-1, 1):
        body.beam((sign*.89, -1.80, 1.11), (sign*.844, -1.28, 1.94), .09, .07)
        side_panel(body,sign,[(-.552,.57),(-.483,.57),(-.483,1.91),(-.552,1.91)],thickness=.08)
        # Kapı aralığının arkasındaki koyu ince conta, fondan ışık sızmasını keser.
        door_outline = [(-1.68,.815),(-1.52,.806),(-1.29,.69),(-1.17,.574),
                        (-.573,.574),(-.573,1.885),(-1.219,1.885),(-1.68,1.18)]
        centre = Vector((-1.105,1.23))
        outer,inner = [],[]
        for yz in door_outline:
            delta = (Vector(yz)-centre).normalized()
            outer.append(Vector(yz)+delta*.052)
            inner.append(Vector(yz)-delta*.010)
        for i in range(len(outer)):
            j = (i+1)%len(outer)
            side_panel(body,sign,[outer[i],outer[j],inner[j],inner[i]],'Tire',.012,-.062)
        body.box((sign*.85, .55, 1.95), (.14, 3.55, .10), bevel=.033)
        side_panel(body,sign,[(2.235,.52),(2.365,.52),(2.365,1.94),(2.235,1.94)],thickness=.040)
        # Konsept gibi kapalı arka çeyrek; arka sıra bagaj camından görünür.
        y0 = .99 if sign == -1 else -.43
        y1 = 2.23
        side_panel(body, sign, [(y0, 1.08), (y1, 1.08), (y1, 1.215), (y0, 1.215)])
        side_panel(body, sign, [(y0, 1.82), (y1, 1.82), (y1, 1.9), (y0, 1.9)])
        # Sabit cam çerçeveleri ve kolonları.
        spans = [(y0+.055, .82)] if sign == 1 else []
        side_panel(body, sign, [(.98, 1.09), (2.25, 1.09), (2.25, 1.90), (.98, 1.90)])
        for a, b in spans:
            side_window(glass, sign, a, b)
            for p, q in [((a, 1.22), (b, 1.22)), ((b, 1.22), (b, 1.82)),
                         ((b, 1.82), (a, 1.82)), ((a, 1.82), (a, 1.22))]:
                body.beam((side_x(sign, p[1]), p[0], p[1]),
                          (side_x(sign, q[1]), q[0], q[1]), .035, .027, 'Tire')
        if sign == 1:
            side_panel(body,sign,[(-.483,1.08),(-.372,1.08),(-.372,1.91),(-.483,1.91)])
            side_panel(body, sign, [(.83, 1.08), (.98, 1.08), (.98, 1.91), (.83, 1.91)])
            # Sol yandaki küçük gerçek göçük: kenar yüzeyinde sıfır, merkezde 4 cm içeri.
            ys, zs = [-.483, -.22, .12, .45, .941], [.54, .71, .89, 1.085]
            verts = []
            for j, z in enumerate(zs):
                for i, y in enumerate(ys):
                    dent = .042 if (i, j) == (2, 2) else (.017 if 0<i<4 and 0<j<3 else 0)
                    verts.append((.95-dent, y, z))
            faces = [(j*5+i, j*5+i+1, (j+1)*5+i+1, (j+1)*5+i) for j in range(3) for i in range(4)]
            # Arkası kapalı, BVH iç/dış testi anlamlı kalır.
            body.patch(verts, faces)
            back = [(x-.045, y, z) for x, y, z in verts]
            body.patch(back, [tuple(reversed(f)) for f in faces])
            boundary = [0,1,2,3,4,9,14,19,18,17,16,15,10,5]
            for a, b in zip(boundary, boundary[1:]+boundary[:1]):
                body.patch([verts[a], verts[b], back[b], back[a]], [(0,1,2,3)])
            for y, z, length in [(-.05, .89, .44), (.06, .84, .37), (.16, .79, .26)]:
                body.prism([(.928, y, z), (.928, y+length, z-.055), (.928, y+length-.04, z-.067),
                            (.928, y-.018, z-.006)], (-.008, 0, 0), 'Damage')
        else:
            side_panel(body,sign,[(.917,.55),(.998,.55),(.998,1.91),(.917,1.91)])
            # Sağ sürgü arkaya gitmeden önce 10 cm dışarı çıkar (metadata).
            body.box((-.953, 1.32, 1.09), (.015, 1.81, .024), 'Trim', .006)
        arch_panel(body, sign, 1.60, .94, 2.37)
        # Ön çamurluk kapı boşluğuna taşmaz: yalnız kaput tarafındaki yarı.
        radius = .43
        arc = [(-2.36, .52), (-2.03, .52)]
        arc += [(-1.60-radius*math.cos(math.pi*i/10), .375+radius*math.sin(math.pi*i/10)) for i in range(1, 5)]
        arc += [(-1.705, .80), (-1.705, 1.08), (-2.36, 1.08)]
        side_panel(body, sign, arc)
        # Alt eşik tekerlekler arasındadır.
        body.box((sign*.91, -.055, .535), (.075, 2.04, .07), 'Paint', .015)
        # Aynalar gövde parçasında ve menteşenin önünde.
        body.beam((sign*.91, -1.84, 1.26), (sign*1.05, -1.91, 1.32), .043, material='Trim')
        body.box((sign*1.072, -1.925, 1.37), (.18, .13, .225), 'Trim', .035, segments=2)
        body.box((sign*1.074, -1.851, 1.37), (.135, .012, .163), 'Glass', .015)
        body.box((sign*.879, 2.381, .97), (.125, .064, .355), 'Trim', .018)
        body.box((sign*.879, 2.420, .97), (.092, .022, .285), 'Taillight', .015)
    body.box((0, 2.415, .55), (1.87, .17, .18), 'Trim', .038)
    body.box((0, 2.355, 1.924), (1.70, .10, .095), bevel=.025)
    # Çeki gözü yatay halka: arka tampon merkezinde, +Y'den halat erişimi.
    body.beam((0, 2.40, .50), (0, 2.53, .50), .06, material='Trim')
    body.torus((0, 2.565, .50), .064, .015, 'Trim', rings=16)
    body.tag = 'roof_hide_for_interior'
    body.box((0, .51, 1.96), (1.78, 3.76, .08), bevel=.035, segments=2)
    # Raf ve iki bavul toplam yüksekliği 2.45 m altında kalır.
    for x in (-.69, .69):
        for y in (-.40, 1.28):
            body.box((x, y, 2.045), (.075, .08, .15), 'Trim', .012)
        body.box((x, .44, 2.135), (.075, 2.12, .075), 'Trim', .015)
    for y in (-.59, .02, .81, 1.47):
        body.box((0, y, 2.10), (1.43, .065, .065), 'Trim', .012)
    body.finish()
    glass.finish()


def build_front_door(sign, side):
    geo = Geo('SM_Minivan_Door_F' + side)
    # Ön kenar dikey menteşe; üst ön köşe A sütununu izler.
    lower = [(-1.68, .815), (-1.52, .806), (-1.29, .69), (-1.17, .574),
             (-.573, .574), (-.573, 1.18), (-1.68, 1.18)]
    side_panel(geo, sign, lower, thickness=.046)
    # Boyanmış çerçeve dışından iç cam poligonuna kapalı halka.
    outer = [(-1.675, 1.177), (-.573, 1.177), (-.573, 1.885), (-1.219, 1.885)]
    inner = [(-1.576, 1.253), (-.655, 1.253), (-.655, 1.812), (-1.186, 1.812)]
    for i in range(4):
        side_panel(geo, sign, [outer[i], outer[(i+1)%4], inner[(i+1)%4], inner[i]], thickness=.045)
        p, q = inner[i], inner[(i+1)%4]
        geo.beam((side_x(sign,p[1]),p[0],p[1]), (side_x(sign,q[1]),q[0],q[1]), .025, .025, 'Tire')
    side_panel(geo, sign, inner, 'Glass', .008, -.018)
    geo.box((sign*.957, -.743, 1.087), (.028, .20, .055), 'Trim', .012)
    geo.box((sign*.881, -.765, .949), (.044, .31, .065), 'Interior', .018)
    geo.finish((sign*.963, -1.68, .815), (0,0,1),
               {'type': 'hinge', 'open_angle_deg': -sign*70, 'limit_abs_deg': 70})


def build_sliding_door():
    geo = Geo('SM_Minivan_Door_Slide_R')
    sign = -1
    for poly in [[(-.459,.58),(.89,.58),(.89,1.20),(-.459,1.20)],
                 [(-.459,1.20),(-.385,1.20),(-.385,1.885),(-.459,1.885)],
                 [(.808,1.20),(.89,1.20),(.89,1.885),(.808,1.885)],
                 [(-.385,1.81),(.808,1.81),(.808,1.885),(-.385,1.885)]]:
        side_panel(geo, sign, poly)
    side_window(geo, sign, -.365, .788, 1.24, 1.79)
    for p,q in [((-.376,1.224),(.798,1.224)), ((.798,1.224),(.798,1.803)),
                ((.798,1.803),(-.376,1.803)), ((-.376,1.803),(-.376,1.224))]:
        geo.beam((side_x(sign,p[1]),p[0],p[1]),(side_x(sign,q[1]),q[0],q[1]),.027,.027,'Tire')
    geo.box((-.964, -.225, 1.09), (.027,.22,.055), 'Trim', .01)
    geo.finish((-.95,-.459,.58), (0,1,0),
               {'type':'slide', 'outward_direction':[-1,0,0], 'outward_clearance_m':.12,
                'slide_direction':[0,1,0], 'slide_distance_m':1.39,
                'sequence':'First outward clearance, then rearward slide; reverse to close.'})


def build_tailgate():
    geo = Geo('SM_Minivan_Tailgate')
    geo.box((0,2.36,.903), (1.61,.065,.575), bevel=.025)
    geo.box((0,2.36,1.847), (1.61,.065,.075), bevel=.015)
    for x in (-.765,.765):
        geo.box((x,2.36,1.51), (.08,.065,.60), bevel=.015)
    geo.box((0,2.36,1.204), (1.51,.065,.065), bevel=.012)
    for a,b in [((-.715,2.397,1.24),(.715,2.397,1.24)), ((.715,2.397,1.24),(.715,2.397,1.799)),
                ((.715,2.397,1.799),(-.715,2.397,1.799)), ((-.715,2.397,1.799),(-.715,2.397,1.24))]:
        geo.beam(a,b,.025,.025,'Tire')
    geo.box((0,2.368,1.519), (1.412,.008,.545), 'Glass', .004)
    geo.box((-.46,2.414,1.098), (.23,.025,.055), 'Trim', .012)
    geo.finish((0,2.403,1.8845), (1,0,0),
               {'type':'hinge', 'open_angle_deg':85, 'limit_abs_deg':85})


def build_wheels():
    for sign, side in ((1,'L'),(-1,'R')):
        for y, end in ((-1.60,'F'),(1.60,'R')):
            center = Vector((sign*.83,y,.375))
            geo = Geo('SM_Minivan_Wheel_'+end+side)
            profile = [(-.12,.18),(-.119,.245),(-.090,.337),(-.045,.375),
                       (.045,.375),(.090,.337),(.119,.245),(.12,.18)]
            n = 20
            verts = [center+Vector((x,r*math.cos(2*math.pi*i/n),r*math.sin(2*math.pi*i/n)))
                     for x,r in profile for i in range(n)]
            for j in range(len(profile)-1):
                geo.patch(verts[j*n:(j+2)*n], [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],
                          'Rim' if j in (0,6) else 'Tire')
            for dx in (-.12,.12):
                geo.cylinder(center+Vector((dx,0,0)), .184, .007, 'Rim', (1,0,0), 20)
                geo.cylinder(center+Vector((dx*1.055,0,0)), .112, .012, 'Trim', (1,0,0), 12)
            geo.finish(center, (1,0,0), {'type':'axle', 'rotation_limit_deg':None})


def build_interior(root):
    geo = Geo('SM_Minivan_Interior')
    geo.box((0,.39,.503), (1.68,3.82,.10), 'Interior', .025)
    for sign in (-1,1):
        # Lastiğin kabine girmesini örten basit iç çamurluk hacmi.
        arc = [(sign*.701,1.60-.44*math.cos(.30+(math.pi-.60)*i/10),
                .375+.44*math.sin(.30+(math.pi-.60)*i/10)) for i in range(11)]
        geo.prism(arc,(sign*.204,0,0),'Interior')
    geo.box((0,-1.398,1.123), (1.60,.41,.23), 'Interior', .075, segments=2)
    geo.box((.45,-1.172,1.247), (.35,.035,.12), 'Trim', .018)
    # Sol direksiyon: +X; direksiyon simidi araç önüne dik, hafif yatırılmış.
    geo.beam((.45,-1.34,.99),(.45,-.98,1.245), .046, material='Trim')
    axis = Vector((0,-.82,.57)).normalized()
    geo.torus((.45,-.98,1.245), .161,.019,'Tire',axis,16,6)
    geo.cylinder((.45,-.98,1.245),.056,.033,'Trim',axis,12)
    rot = axis.to_track_quat('Z','Y').to_matrix()
    for angle in (0,2*math.pi/3,4*math.pi/3):
        geo.beam((.45,-.98,1.245), Vector((.45,-.98,1.245))+rot@Vector((.15*math.cos(angle),.15*math.sin(angle),0)),
                 .025, material='Trim')
    seats = {}
    for row,y in enumerate((-.68,.48,1.57)):
        for col,x in enumerate((.43,-.43)):
            index = row*2+col+1
            geo.box((x,y,.695), (.53,.53,.16), 'Interior', .055, segments=2)
            geo.box((x,y+.235,.991), (.54,.135,.55), 'Interior', .055, segments=2,
                    rotation=Matrix.Rotation(math.radians(-5),3,'X'))
            geo.box((x,y+.248,1.346), (.32,.15,.18), 'Interior', .035)
            geo.box((x,y,.581), (.30,.32,.11), 'Trim', .012)
            position = (x,y-.03,.781)
            seats['Seat_'+f'{index:02d}'] = list(position)
            empty('Seat_'+f'{index:02d}', position, root)
    geo.finish()
    return seats


def build_luggage():
    for index, (x,y,size,material) in enumerate([(-.36,.20,(.54,.88,.27),'Suitcase_A'),
                                                (.31,.67,(.55,.70,.255),'Suitcase_B')],1):
        geo = Geo(f'SM_Minivan_RackSuitcase_{index:02d}')
        base = 2.1375
        geo.box((x,y,base+size[2]/2),size,material,.055,segments=2)
        # Bavul kendi kayışlarını taşır; fizik başladığında tek obje düşebilir.
        for dy in (-size[1]*.27,size[1]*.27):
            geo.box((x,y+dy,base+size[2]+.006), (size[0]-.035,.045,.012),'Strap',.004)
            for sx in (-1,1):
                geo.box((x+sx*(size[0]/2+.003),y+dy,base+size[2]/2),(.012,.045,size[2]-.025),'Strap',.004)
            geo.box((x,y+dy,base+.002), (size[0]-.025,.045,.012),'Strap',.004)
        geo.box((x,y-size[1]/2-.012,base+size[2]*.55),(.16,.025,.035),'Trim',.008)
        geo.finish((x,y,base), (0,0,1), {'type':'free_rigid_body', 'attached_initially':True})


def set_open(meta, fraction=1):
    for name, part in meta['parts'].items():
        motion = part.get('motion')
        if not motion:
            continue
        obj = bpy.data.objects[name]
        obj.location = part['pivot_m']
        obj.rotation_euler = (0,0,0)
        if motion['type'] == 'hinge':
            obj.rotation_mode = 'AXIS_ANGLE'
            obj.rotation_axis_angle = (math.radians(motion['open_angle_deg'])*fraction, *part['axis'])
        elif motion['type'] == 'slide':
            obj.location += Vector(motion['outward_direction'])*motion['outward_clearance_m']*min(fraction*4,1)
            obj.location += Vector(motion['slide_direction'])*motion['slide_distance_m']*max(0,(fraction-.25)/.75)
    bpy.context.view_layer.update()


def geometry_digest():
    signature = []
    for obj in sorted(ASSETS,key=lambda o:o.name):
        row = {'name':obj.name,'parent':obj.parent.name if obj.parent else None,
               'pivot':[round(float(c),7) for c in obj.location]}
        if obj.type == 'MESH':
            row.update(vertices=[[round(float(c),7) for c in v.co] for v in obj.data.vertices],
                       faces=[list(p.vertices) for p in obj.data.polygons],
                       materials=[p.material_index for p in obj.data.polygons])
        signature.append(row)
    return hashlib.sha256(json.dumps(signature,sort_keys=True,separators=(',',':')).encode()).hexdigest()


def canonical_geometry():
    data = []
    for obj in sorted((o for o in ASSETS if o.type=='MESH'),key=lambda o:o.name):
        verts = [obj.matrix_world@v.co for v in obj.data.vertices]
        obj.data.calc_loop_triangles()
        triangles = []
        for tri in obj.data.loop_triangles:
            corners = sorted(tuple(math.floor(float(v)*10000+.371) for v in verts[i]) for i in tri.vertices)
            triangles.append([corners,obj.data.materials[tri.material_index].name])
        data.append([obj.name,sorted(triangles)])
    return hashlib.sha256(json.dumps(data,separators=(',',':')).encode()).hexdigest()


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
    print('RENDER DEVICE',scene['render_device'],flush=True)
    scene.render.resolution_x,scene.render.resolution_y = 1920,1080
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium High Contrast'
    scene.view_settings.exposure = -.50
    world = bpy.data.worlds.new('Studio_World')
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (.82,.82,.82,1)
    world.node_tree.nodes['Background'].inputs[1].default_value = .35
    # Ortografik yan/ön kameralarda ufuk açık gri; aydınlatmanın enerjisi değişmez.
    nodes,links = world.node_tree.nodes,world.node_tree.links
    camera_background = nodes.new('ShaderNodeBackground')
    camera_background.inputs[0].default_value = (2.3,2.3,2.3,1)
    camera_background.inputs[1].default_value = 1
    light_path = nodes.new('ShaderNodeLightPath')
    mix = nodes.new('ShaderNodeMixShader')
    links.new(light_path.outputs['Is Camera Ray'],mix.inputs[0])
    links.new(nodes['Background'].outputs[0],mix.inputs[1])
    links.new(camera_background.outputs[0],mix.inputs[2])
    links.new(mix.outputs[0],nodes['World Output'].inputs['Surface'])
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.012))
    floor = bpy.context.object
    floor.name = 'RENDER_StudioFloor'
    floor.is_shadow_catcher = False
    mat = bpy.data.materials.new('RENDER_Studio')
    mat.diffuse_color = (.79,.81,.83,1)
    floor.data.materials.append(mat)
    for name,position,power,size in [('Key',(4,-5,7),600,5),('Fill',(-4,-2,4),350,4),('Rim',(1,5,6),700,3.5)]:
        data = bpy.data.lights.new('RENDER_'+name,'AREA')
        obj = bpy.data.objects.new(data.name,data)
        bpy.context.collection.objects.link(obj)
        obj.location = position
        obj.rotation_euler = (Vector((0,0,1))-obj.location).to_track_quat('-Z','Y').to_euler()
        data.energy,data.shape,data.size = power,'DISK',size
    data = bpy.data.cameras.new('RENDER_Camera')
    camera = bpy.data.objects.new(data.name,data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera
    data.type = 'ORTHO'
    data.lens = 48
    return camera


def interior_roof_mesh():
    body = bpy.data.objects['SM_Minivan_Body']
    copy = body.data.copy()
    group = body.vertex_groups['roof_hide_for_interior'].index
    remove_indices = {v.index for v in body.data.vertices if any(g.group==group for g in v.groups)}
    bm = bmesh.new()
    bm.from_mesh(copy)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm,geom=[bm.verts[i] for i in sorted(remove_indices)],context='VERTS')
    bm.to_mesh(copy)
    bm.free()
    return copy


def write_png(path, pixels):
    # Karşılaştırma için sadece mevcut görseller yan yana dizilir; yeni görsel üretilmez.
    h,w = pixels.shape[:2]
    def chunk(kind,data):
        return struct.pack('!I',len(data))+kind+data+struct.pack('!I',zlib.crc32(kind+data)&0xffffffff)
    raw = b''.join(b'\0'+pixels[y].tobytes() for y in range(h))
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!2I5B',w,h,8,2,0,0,0))+
                     chunk(b'IDAT',zlib.compress(raw,6))+chunk(b'IEND',b''))


def image_array(path):
    im = bpy.data.images.load(str(path),check_existing=False)
    a = np.array(im.pixels[:],dtype=np.float32).reshape(im.size[1],im.size[0],4)[::-1,:,:3]
    bpy.data.images.remove(im)
    return np.clip(a*255+.5,0,255).astype(np.uint8)


def comparison(render_dir):
    concept = image_array(REF/'01_turnaround_light_blue.jpg')
    render = image_array(render_dir/'threequarter_front_left.png')
    # Her iki tarafta aynı üç çeyrek açı; konseptin sağ alt görünümü.
    sources = [concept[387:758,788:1260],render]
    canvas = np.full((1080,1920,3),241,dtype=np.uint8)
    for index,a in enumerate(sources):
        scale = min(930/a.shape[1],990/a.shape[0])
        w,h = round(a.shape[1]*scale),round(a.shape[0]*scale)
        resized = a[(np.arange(h)/scale).astype(int)[:,None],(np.arange(w)/scale).astype(int)[None,:]]
        x,y = index*960+(960-w)//2,(1080-h)//2
        canvas[y:y+h,x:x+w] = resized
    canvas[:,957:963] = (130,147,155)
    write_png(render_dir/'compare_concept.png',canvas)


def render_views(meta, preview=False):
    folder = OUT/'renders'/f'round_{ROUND:02d}'
    folder.mkdir(parents=True,exist_ok=True)
    camera = setup_render()
    views = [('front',(0,-10,1.18),(0,0,1.18),5.5),
             ('rear',(0,10,1.18),(0,0,1.18),5.5),
             ('left',(10,0,1.23),(0,0,1.23),6.0),
             ('right',(-10,0,1.23),(0,0,1.23),6.0),
             ('threequarter_front_left',(7,-9,4.5),(0,0,1.05),6.5),
             ('threequarter_rear_right_open',(-8,9,6),(0,.1,1.10),7.8),
             ('interior_top',(0,0,12),(0,0,0),6.1)]
    if preview:
        views = [v for v in views if v[0] in ('threequarter_front_left','threequarter_rear_right_open','interior_top')]
    durations = {}
    for name,position,target,scale in views:
        set_open(meta,1 if name.endswith('_open') else 0)
        body = bpy.data.objects['SM_Minivan_Body']
        original = body.data
        if name == 'interior_top':
            body.data = interior_roof_mesh()
            for obj in ASSETS:
                if 'RackSuitcase' in obj.name:
                    obj.hide_render = True
        camera.location = position
        camera.rotation_euler = (Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
        if name == 'interior_top':
            camera.rotation_euler = (0,0,math.pi/2)
        camera.data.ortho_scale = scale
        bpy.context.scene.render.filepath = str(folder/(name+'.png'))
        started = time.perf_counter()
        bpy.ops.render.render(write_still=True)
        durations[name] = round(time.perf_counter()-started,3)
        if name == 'interior_top':
            temporary = body.data
            body.data = original
            bpy.data.meshes.remove(temporary)
            for obj in ASSETS:
                obj.hide_render = False
        shutil.copy2(folder/(name+'.png'),OUT/'renders'/(name+'.png'))
        print('RENDER OK',name,durations[name],flush=True)
    set_open(meta,0)
    comparison(folder)
    shutil.copy2(folder/'compare_concept.png',OUT/'renders'/f'compare_round_{ROUND:02d}.png')
    return durations


def main():
    global ROUND
    parser = argparse.ArgumentParser()
    parser.add_argument('--round',type=int,default=4)
    parser.add_argument('--preview',action='store_true',help='Üç denetim açısı, yine 1920x1080/64 sample')
    parser.add_argument('--no-render',action='store_true',help='Geliştirme için; kabul build varsayılanı render alır')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    ROUND = args.round
    started = time.perf_counter()
    OUT.mkdir(parents=True,exist_ok=True)
    (OUT/'renders').mkdir(exist_ok=True)
    (OUT/'cache').mkdir(exist_ok=True)
    for name in ('OPTIX_CACHE_PATH','CUDA_CACHE_PATH','TMP','TEMP'):
        os.environ[name] = str(OUT/'cache')
    os.chdir(OUT)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.context.preferences.filepaths.use_auto_save_temporary_files = False
    bpy.context.preferences.filepaths.file_preview_type = 'NONE'
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1
    sample_record = reference_palette()
    make_materials()
    root = empty('SM_Minivan',(0,0,0),None)
    build_body()
    build_front_door(1,'L')
    build_front_door(-1,'R')
    build_sliding_door()
    build_tailgate()
    build_wheels()
    seats = build_interior(root)
    build_luggage()
    tow = [0,2.565,.50]
    empty('TowPoint',tow,root)
    bpy.context.view_layer.update()
    meta = {'schema_version':1,'units':'metres','coordinates':{'up':'+Z','forward':'-Y','left':'+X','right':'-X'},
            'field_descriptions':{
                'parts':'Object-name map; all values use the CLOSED Blender pose.',
                'pivot_m':'XYZ object origin in root/Blender world coordinates, metres.',
                'axis':'Unit XYZ vector of hinge/axle/slide axis in root coordinates; null for static parts.',
                'motion.type':'hinge / slide / axle / free_rigid_body; null means static.',
                'motion.open_angle_deg':'Signed right-hand rotation about axis; closed angle is zero.',
                'motion.limit_abs_deg':'Maximum absolute hinge angle (degrees).',
                'motion.outward_direction':'Unit vector for first sliding-door clearance phase.',
                'motion.outward_clearance_m':'Translate this distance outward before rearward motion.',
                'motion.slide_direction':'Unit rearward translation vector after outward clearance.',
                'motion.slide_distance_m':'Rearward travel in metres, added to outward translation.',
                'motion.sequence':'Required ordering of slide phases.',
                'motion.rotation_limit_deg':'Null: wheel rotation is unrestricted.',
                'motion.attached_initially':'Suitcase starts attached; gameplay can release its rigid body.',
                'parent':'Direct FBX parent object name.',
                'markers':'XYZ root-space locations in metres; seats mark pelvis/cushion contact, TowPoint is ring centre.',
                'measurement':'Body envelope excludes mirrors, rack, luggage and rear tow ring; roof height is measured from ground.',
                'palette_samples':'sRGB hex + top-left pixel coordinates and median patch size in the cited concept.'},
            'parts':PARTS,'markers':{**seats,'TowPoint':tow},
            'measurement':{'length_m':5.0,'width_m':1.9,'ground_to_roof_m':2.0,'tolerance_m':.05,
                           'wheel_diameter_m':.75,'wheel_centres_y_m':[-1.60,1.60],
                           'interior_bounds_m':[[-.85,-1.65,.50],[.85,2.25,1.88]]},
            'materials':{'M_Minivan_'+n:'#'+c for n,c in PALETTE.items()},
            'palette_reference':'ArtSource/Minivan/references/01_turnaround_light_blue.jpg',
            'palette_samples':sample_record,'glass_alpha':.34,
            'unity_import':{'scale_factor':1,'bake_axis_conversion':False,
                            'glass_note':'FBX stores alpha; choose a transparent shader in the target render pipeline.'}}
    (OUT/'minivan_meta.json').write_text(json.dumps(meta,indent=2),encoding='utf-8')
    counts = {o.name:len(o.data.polygons) for o in ASSETS if o.type=='MESH'}
    total = sum(counts.values())
    print('TRIANGLES',total,json.dumps(counts),flush=True)
    if not 4000 <= total <= 7000:
        raise ValueError(f'Triangle budget: {total}')
    digest = geometry_digest()
    canonical_digest = canonical_geometry()
    bpy.ops.object.select_all(action='DESELECT')
    for obj in ASSETS:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    # Lineer Base Color FBX'e olduğu gibi yazılır (Unity Linear URP).
    bpy.ops.export_scene.fbx(filepath=str(OUT/'SM_Minivan.fbx'),use_selection=True,
                             object_types={'EMPTY','MESH'},apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z',axis_up='Y',mesh_smooth_type='FACE',
                             use_mesh_modifiers=False,bake_anim=False,use_custom_props=True,
                             check_existing=False,add_leaf_bones=False)
    durations = render_views(meta,args.preview) if not args.no_render else {}
    # Blend açıldığında oyun asset'i kapalı pozdadır; stüdyo objeleri FBX'e gitmez.
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Minivan.blend'),check_existing=False)
    history_path = OUT/'build_history.json'
    history = json.loads(history_path.read_text(encoding='utf-8')) if history_path.exists() else []
    history.append({'geometry_sha256':digest,'source_sha256':hashlib.sha256(Path(__file__).read_bytes().replace(b'\r\n',b'\n')).hexdigest(),
                    'round':ROUND,'triangles':total,'render_seconds':round(sum(durations.values()),3),
                    'camera_render_seconds':durations,'render_device':bpy.context.scene.get('render_device','not rendered'),
                    'total_build_seconds':round(time.perf_counter()-started,3)})
    history_path.write_text(json.dumps(history,indent=2),encoding='utf-8')
    (OUT/'build_manifest.json').write_text(json.dumps({'geometry_sha256':digest,'canonical_geometry_sha256':canonical_digest,'object_triangles':counts,
        'total_triangles':total,'materials':meta['materials'],'last_build':history[-1]},indent=2),encoding='utf-8')
    print('BUILD OK',digest,'triangles',total,'render_seconds',round(sum(durations.values()),3),flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        print('BUILD FAILED',flush=True)
        sys.exit(1)
