"""Deterministik Thief (hırsız) üretimi. Blender 5.2, metre, Z yukarı / -Y ön.
Yalnız Export/Thief yazar. --no-render geliştirme seçeneği.
İskelet RescueCrew ile birebir aynı (31 kemik, mixamorig:), bind T-poz.
Çalıştır: blender -b --factory-startup --python-exit-code 1 --python tools/blender/build_thief.py
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
import time
import traceback
sys.dont_write_bytecode = True
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Export/Thief'
CREW = ROOT / 'Export/RescueCrew/SK_RescueCrew_Blue.fbx'
REF = ROOT / 'ArtSource/Thief/references'
PREFIX = 'mixamorig:'
PI = math.pi
MATERIALS = {}
# Varyantta yalnız maske ve pantolon değişir; kalanı ortak.
PALETTE = dict(Mask_Black='1E1E22', Pants_Black='4A4D52', Mask_Red='7A1E22', Pants_Red='2A3350',
    Mask_Green='2E5A2E', Pants_Green='5A3E2A', ShirtBlack='1A1A1A', ShirtWhite='F2F2F2',
    GloveShoe='222226', Sole='3A3A3E', Sack='B08A5A', Skin='C68A62')
VARIANTS = ('Black', 'Red', 'Green')
# Kafa elipsoidi: merkez ve yarıçaplar (tepe 1,50 m).
HEAD_C = (0., .01, 1.235)
HEAD_R = (.235, .215, .265)
# Tişört silüeti: z, rx, ry. Halkalar bant sınırlarına (.455 + k*.07) oturur.
TORSO = ((.455, .195, .135), (.49, .205, .140), (.525, .207, .142), (.56, .207, .142), (.595, .206, .142),
    (.63, .204, .141), (.665, .201, .139), (.70, .198, .137), (.735, .197, .134), (.77, .197, .131),
    (.805, .200, .127), (.84, .206, .124), (.875, .218, .119), (.91, .212, .108), (.93, .185, .100),
    (.945, .150, .095), (.96, .120, .090))
STRIPE = .07


def srgb(v):
    # sRGB hex -> lineer Base Color (Linear URP; FBX olduğu gibi yazılır).
    return v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4


def materials():
    for name, color in PALETTE.items():
        mat = bpy.data.materials.new('M_Thief_' + name)
        mat.use_nodes = True
        rgb = tuple(srgb(int(color[i:i+2], 16) / 255) for i in (0, 2, 4))
        mat.diffuse_color = (*rgb, 1)
        mat.roughness = .7
        shader = mat.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Base Color'].default_value = (*rgb, 1)
        shader.inputs['Roughness'].default_value = .7
        shader.inputs['Specular IOR Level'].default_value = .25
        MATERIALS[name] = mat


class Geometry:
    def __init__(self, name):
        self.name, self.vertices, self.faces, self.face_materials = name, [], [], []
        self.weights = []

    def add(self, vertices, faces, mat, weights='Head'):
        offset = len(self.vertices)
        self.vertices.extend(tuple(float(c) for c in v) for v in vertices)
        self.faces.extend(tuple(offset + i for i in f) for f in faces)
        self.face_materials.extend(mat(tuple(sum(vertices[i][axis] for i in f) / len(f) for axis in range(3))) if callable(mat) else mat for f in faces)
        self.weights.extend(weights(p) if callable(weights) else {weights: 1} for p in vertices)

    def sphere(self, center, scale, mat, weights='Head', segments=16, rings=8):
        # Tek kutup vertex'i: sıfır alanlı yüz üretmez.
        verts = [(center[0], center[1], center[2] + scale[2])]
        for j in range(1, rings):
            theta = PI * j / rings
            for i in range(segments):
                phi = 2 * PI * i / segments
                verts.append((center[0] + scale[0] * math.sin(theta) * math.cos(phi),
                              center[1] + scale[1] * math.sin(theta) * math.sin(phi),
                              center[2] + scale[2] * math.cos(theta)))
        verts.append((center[0], center[1], center[2] - scale[2]))
        faces = [(0, 1 + i, 1 + (i + 1) % segments) for i in range(segments)]
        for j in range(rings - 2):
            for i in range(segments):
                a = 1 + j * segments + i
                b = 1 + j * segments + (i + 1) % segments
                faces.append((a, a + segments, b + segments, b))
        end = len(verts) - 1
        faces += [(end, end - 1 - i, end - 1 - (i + 1) % segments) for i in range(segments)]
        self.add(verts, faces, mat, weights)

    def loft(self, rings, mat, weights='Head', segments=20, axis='Z', cap=True):
        # Halkalar: merkez, iki yarıçap. Z ekseninde (rx, ry), X ekseninde (ry, rz).
        verts = []
        for center, a, b in rings:
            for i in range(segments):
                t = 2 * PI * i / segments
                delta = (a * math.cos(t), b * math.sin(t), 0) if axis == 'Z' else (0, a * math.cos(t), b * math.sin(t))
                verts.append(tuple(center[k] + delta[k] for k in range(3)))
        faces = []
        for j in range(len(rings) - 1):
            for i in range(segments):
                a, b = j * segments + i, j * segments + (i + 1) % segments
                faces.append((a, b, b + segments, a + segments))
        if cap:
            faces += [tuple(reversed(range(segments))), tuple((len(rings) - 1) * segments + i for i in range(segments))]
        self.add(verts, faces, mat, weights)

    def tube(self, points, radius, mat, weights='Head', sides=6, cap=True):
        verts = []
        for i, p in enumerate(points):
            tangent = Vector(points[min(i + 1, len(points) - 1)]) - Vector(points[max(0, i - 1)])
            tangent.normalize()
            axis = Vector((0, 1, 0))
            if abs(tangent.dot(axis)) > .95:
                axis = Vector((1, 0, 0))
            u = tangent.cross(axis).normalized()
            v = tangent.cross(u).normalized()
            ring_radius = radius[i] if isinstance(radius, (tuple, list)) else radius
            for k in range(sides):
                verts.append(tuple(Vector(p) + ring_radius * (u * math.cos(2 * PI * k / sides) + v * math.sin(2 * PI * k / sides))))
        faces = []
        for i in range(len(points) - 1):
            for k in range(sides):
                a, b = i * sides + k, i * sides + (k + 1) % sides
                faces.append((a, b, b + sides, a + sides))
        if cap:
            faces += [tuple(reversed(range(sides))), tuple((len(points) - 1) * sides + k for k in range(sides))]
        self.add(verts, faces, mat, weights)

    def object(self):
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        used = sorted(set(self.face_materials))
        for name in used:
            mesh.materials.append(MATERIALS[name])
        for poly, name in zip(mesh.polygons, self.face_materials):
            poly.material_index = used.index(name)
            poly.use_smooth = len(poly.vertices) <= 4
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.015)
        bpy.ops.object.mode_set(mode='OBJECT')
        obj.select_set(False)
        return obj


def smooth(t):
    t = max(0., min(1., t))
    return t * t * (3 - 2 * t)


def blend(a, b, t):
    t = smooth(t)
    return {k: v for k, v in ((a, 1 - t), (b, t)) if v > 1e-7}


def chain_weights(chain):
    def weight(p):
        for (a, z), (b, zz) in zip(chain, chain[1:]):
            if p[2] <= zz:
                return blend(a, b, (p[2] - z) / (zz - z))
        return {chain[-1][0]: 1}
    return weight


torso_weights = chain_weights((('Hips', .46), ('Spine', .57625), ('Spine1', .70975), ('Spine2', .82656), ('Neck', .98)))
# Çuval boyna değil omurgaya bağlı: zincir Spine2'de biter, sarkmaz.
sack_weights = chain_weights((('Hips', .46), ('Spine', .57625), ('Spine1', .70975), ('Spine2', .86)))


def arm_weights(side):
    def weight(p):
        x = abs(p[0])
        # Geniş omuz geçişi: kol indiğinde koltuk altı gövdeye gömülmez.
        if x < .36:
            return blend(side + 'Shoulder', side + 'Arm', (x - .20) / .16)
        if x < .47:
            return blend(side + 'Arm', side + 'ForeArm', (x - .36) / .07)
        return blend(side + 'ForeArm', side + 'Hand', (x - .51) / .055)
    return weight


def leg_weights(side):
    def weight(p):
        z = p[2]
        if z > .41:
            return blend(side + 'UpLeg', 'Hips', (z - .41) / .14)
        if z > .20:
            return blend(side + 'Leg', side + 'UpLeg', (z - .26) / .10)
        return blend(side + 'Foot', side + 'Leg', (z - .095) / .075)
    return weight


def foot_weights(side):
    return lambda p: blend(side + 'Foot', side + 'ToeBase', (-p[1] - .10) / .09)


def ribbon(geo, points, width, mat, weights, axis=(1, 0, 0)):
    # Düz renkli şerit, her kesitte iki kenar; ağırlıklar kesintisiz ilerler.
    verts = []
    for point in points:
        for sign in (-1, 1):
            verts.append(tuple(Vector(point) + Vector(axis) * width * sign / 2))
    geo.add(verts, [(2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2) for i in range(len(points) - 1)], mat, weights)


def head_point(theta, phi, factor=1.):
    # Kafa elipsoidi, kutup önde (-Y). theta: ön kutuptan açı, phi: +X'ten +Z'ye doğru.
    cx, cy, cz = HEAD_C
    rx, ry, rz = HEAD_R
    return (cx + factor * rx * math.sin(theta) * math.cos(phi),
            cy - factor * ry * math.cos(theta),
            cz + factor * rz * math.sin(theta) * math.sin(phi))


def hole_angle(phi, a=.86, b=.46):
    # Göz deliği açı uzayında elips: yatay yarı açı a, dikey b.
    return a * b / math.sqrt((b * math.cos(phi)) ** 2 + (a * math.sin(phi)) ** 2)


def mask_head(geo, mask):
    # Ön kutuplu küre: delik içi halkalar ten (içe çökük), kenar duvarı ve kalanı maske.
    n = 36
    recess = .94
    inner = (.42, .76, 1.0)
    outer = (.06, .14, .24, .36, .49, .62, .75, .87)
    verts = [head_point(0, 0, recess)]
    rings = []
    for t in inner:
        rings.append([head_point(hole_angle(2 * PI * i / n) * t, 2 * PI * i / n, recess) for i in range(n)])
    rings.append([head_point(hole_angle(2 * PI * i / n), 2 * PI * i / n) for i in range(n)])
    for s in outer:
        rings.append([head_point(h + (PI - h) * s, 2 * PI * i / n) for i, h in ((i, hole_angle(2 * PI * i / n)) for i in range(n))])
    for ring in rings:
        verts.extend(ring)
    verts.append(head_point(PI, 0))
    faces = [(0, 1 + i, 1 + (i + 1) % n) for i in range(n)]
    mats = ['Skin'] * n
    for j in range(len(rings) - 1):
        for i in range(n):
            a = 1 + j * n + i
            b = 1 + j * n + (i + 1) % n
            faces.append((a, a + n, b + n, b))
            mats.append('Skin' if j < len(inner) - 1 else mask)
    end = len(verts) - 1
    faces += [(end, end - 1 - i, end - 1 - (i + 1) % n) for i in range(n)]
    mats += [mask] * n
    geo.add(verts, faces, lambda c, _m=iter(mats): next(_m))


def head(geo, variant):
    mask = 'Mask_' + variant
    mask_head(geo, mask)
    # Maske yakası: omuzlara yayılan etek, tişört yakasını örter.
    geo.loft([((0, 0, z), r, r * .88) for z, r in ((.905, .142), (.94, .131), (.98, .120), (1.03, .118))], mask, 'Neck', segments=20)
    for s in (-1, 1):
        geo.sphere((s * .098, -.172, 1.245), (.097, .078, .105), 'ShirtWhite', segments=20, rings=12)
        geo.sphere((s * .088, -.248, 1.248), (.026, .012, .030), 'ShirtBlack', segments=12, rings=8)
        geo.sphere((s * .088 - s * .008, -.259, 1.260), (.006, .003, .007), 'ShirtWhite', segments=6, rings=4)
    # Burun göz deliğinin alt kenarına oturur ve ağız deliğinin üstüne değer (konsept).
    geo.sphere((0, -.212, 1.118), (.034, .032, .028), 'Skin', segments=12, rings=8)
    # Ağız deliği: yüzeye 6 mm bindirilmiş geniş ten yaması + ince gülümseme.
    tm, ta, tb = .57, .22, .09
    verts = [head_point(tm, -PI / 2, 1.025)]
    for i in range(16):
        alpha = 2 * PI * i / 16
        theta = tm + tb * math.sin(alpha)
        verts.append(head_point(theta, -PI / 2 + ta * math.cos(alpha) / math.sin(theta), 1.025))
    geo.add(verts, [(0, 1 + i, 1 + (i + 1) % 16) for i in range(16)], 'Skin')
    smile = []
    for t in (-1, -.5, 0, .5, 1):
        theta = tm + .03 - .04 * t * t
        smile.append(head_point(theta, -PI / 2 + .12 * t / math.sin(theta), 1.04))
    geo.tube(smile, .004, 'ShirtBlack', sides=6)


def stripe_z(center):
    z = center[2]
    if z >= .945:
        return 'ShirtBlack'
    return 'ShirtBlack' if int((z - .455) / STRIPE) % 2 == 0 else 'ShirtWhite'


def torso_radius(z):
    for (za, xa, ya), (zb, xb, yb) in zip(TORSO, TORSO[1:]):
        if z <= zb:
            t = max(0., min(1., (z - za) / (zb - za)))
            return xa + (xb - xa) * t, ya + (yb - ya) * t
    return TORSO[-1][1:]


def torso_surface(x, z, offset, front=True):
    rx, ry = torso_radius(z)
    y = ry * math.sqrt(max(.05, 1 - (x / rx) ** 2)) + offset
    return (x, -y if front else y, z)


def torso(geo, variant):
    geo.loft([((0, 0, z), rx, ry) for z, rx, ry in TORSO], stripe_z, torso_weights, segments=24)


def arm(geo, variant, side):
    s = 1 if side == 'Left' else -1
    w = arm_weights(side)
    band = .061
    def stripe_x(center):
        return 'ShirtBlack' if int((abs(center[0]) - .20) / band) % 2 == 0 else 'ShirtWhite'
    xs = (.20, .2305, .261, .2915, .322, .3525, .383, .4135, .444, .4745, .505)
    radii = {.20: (.080, .078), .2305: (.093, .090), .261: (.094, .091), .2915: (.091, .089), .322: (.088, .086),
             .3525: (.085, .083), .383: (.082, .080), .4135: (.080, .078), .444: (.078, .076), .4745: (.076, .074), .505: (.074, .072)}
    geo.loft([((s * x, 0, .8822), *radii[x]) for x in xs], stripe_x, w, segments=16, axis='X')
    # Eldiven: kısa manşet + tek parça mitten + öne bakan başparmak; hepsi siyah.
    geo.loft([((s * x, 0, .8822), r, r * .97) for x, r in ((.495, .078), (.52, .081), (.55, .079), (.565, .070))], 'GloveShoe', w, segments=16, axis='X')
    geo.sphere((s * .628, 0, .880), (.092, .064, .057), 'GloveShoe', lambda p: blend(side + 'Hand', side + 'HandIndex1', (abs(p[0]) - .62) / .07), segments=18, rings=10)
    geo.tube([(s * .600, -.030, .872), (s * .625, -.075, .852), (s * .655, -.098, .845)], [.030, .030, .015], 'GloveShoe',
             lambda p: blend(side + 'HandThumb1', side + 'HandThumb2', (abs(p[0]) - .602) / .048), sides=10)


def legs(geo, variant):
    pants = 'Pants_' + variant
    for s, side in ((1, 'Left'), (-1, 'Right')):
        x = s * .1344
        w = leg_weights(side)
        zs = (.105, .16, .22, .28, .34, .40, .46, .52)
        geo.loft([((x - s * .022 * smooth((z - .40) / .13), 0, z), .089 if z < .32 else .097, .087 if z < .32 else .099) for z in zs], pants, w, segments=16)
        # Spor ayakkabı: yuvarlak gövde, altta gri taban; taban Z=0.
        start = len(geo.vertices)
        geo.sphere((x, -.04, .062), (.088, .136, .058), 'GloveShoe', foot_weights(side), segments=20, rings=10)
        for i in range(start, len(geo.vertices)):
            vx, vy, vz = geo.vertices[i]
            geo.vertices[i] = (vx, vy, max(.026, vz))
        geo.loft([((x, -.04, z), .090, .138) for z in (0, .028)], 'Sole', foot_weights(side), segments=20)


def sack(geo):
    # Sırt çuvalı: gövdeye en çok ~1,5 cm girer, omurga zinciriyle hareket eder.
    # Gövde sağ tarafına kaymış: önden bakınca omuz yanından taşar (konsept).
    geo.sphere((-.11, .268, .72), (.215, .16, .26), 'Sack', sack_weights, segments=24, rings=14)
    # Büzülmüş boğaz sağ omuzun üstünden kafanın yanına çıkar; bağ bandı en dar yerde.
    neck = [(-.15, .245, .91), (-.205, .215, .995), (-.248, .198, 1.07), (-.268, .19, 1.12), (-.278, .185, 1.16)]
    geo.tube(neck, [.10, .074, .050, .058, .066], 'Sack', sack_weights, sides=12)
    geo.tube([(-.243, .200, 1.062), (-.252, .196, 1.078)], [.054, .054], 'GloveShoe', sack_weights, sides=12)
    # Askı: çuvaldan sırtı çaprazlayıp sol omuz üstünden göğsün sol kenarından iner.
    points = [torso_surface(-.04, .80, .006, False), torso_surface(.06, .86, .006, False), torso_surface(.13, .905, .006, False),
              (.15, 0, .953), torso_surface(.158, .92, .006), torso_surface(.165, .86, .006), torso_surface(.170, .78, .006),
              torso_surface(.172, .70, .006), torso_surface(.170, .62, .006), torso_surface(.165, .54, .006), torso_surface(.16, .47, .006)]
    ribbon(geo, points, .040, 'Sack', torso_weights)


def rig_from_crew():
    bpy.ops.import_scene.fbx(filepath=str(CREW), use_anim=False)
    rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    # Bind matrisleri kaynaktan doğrudan kopyalanır; ek kemik üretilmez.
    for obj in list(bpy.context.scene.objects):
        if obj != rig:
            bpy.data.objects.remove(obj, do_unlink=True)
    rig.animation_data_clear()
    return rig


PARTS = (('Head', head), ('Torso', torso), ('LeftArm', lambda g, v: arm(g, v, 'Left')),
         ('RightArm', lambda g, v: arm(g, v, 'Right')), ('Legs', legs), ('Sack', lambda g, v: sack(g)))


def make_variant(template, variant):
    rig = bpy.data.objects.new('SK_Thief_' + variant, template.data)
    bpy.context.collection.objects.link(rig)
    rig.matrix_world = template.matrix_world.copy()
    # Yeni bağlanan armature objesinin pose verisi ancak depsgraph güncellemesiyle oluşur.
    bpy.context.view_layer.update()
    parts = []
    for label, fn in PARTS:
        geo = Geometry('Thief_' + variant + '_' + label)
        fn(geo, variant)
        obj = geo.object()
        groups = {b.name: obj.vertex_groups.new(name=b.name) for b in rig.data.bones}
        for i, weights in enumerate(geo.weights):
            total = sum(weights.values())
            for name, value in weights.items():
                groups[PREFIX + name].add([i], value / total, 'REPLACE')
        obj.parent = rig
        mod = obj.modifiers.new('ThiefSkin', 'ARMATURE')
        mod.object = rig
        parts.append(obj)
    return rig, parts


def reset_pose(rig):
    rig.animation_data_clear()
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def export_fbx(path, rig, parts):
    bpy.ops.object.select_all(action='DESELECT')
    for o in [rig] + parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', add_leaf_bones=False, use_armature_deform_only=False,
        bake_anim=False, mesh_smooth_type='FACE', use_mesh_modifiers=False, use_custom_props=True, check_existing=False)


def triangles(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def geometry_signature(assets):
    # Köşe koordinatları (5 ondalık) + üçgen indeksleri, varyant ve parça sırası sabit.
    data = []
    for variant in VARIANTS:
        for obj in assets[variant][1]:
            obj.data.calc_loop_triangles()
            data.append([obj.name, [[round(c, 5) for c in v.co] for v in obj.data.vertices],
                         [list(t.vertices) for t in obj.data.loop_triangles]])
    return hashlib.sha256(json.dumps(data, separators=(',', ':')).encode()).hexdigest()


def measure(rig, parts):
    points = [o.matrix_world @ v.co for o in parts for v in o.data.vertices]
    spine1 = rig.matrix_world @ rig.data.bones[PREFIX + 'Spine1'].head_local
    sack_obj = next(o for o in parts if o.name.endswith('_Sack'))
    sack_pts = [o for o in (sack_obj.matrix_world @ v.co for v in sack_obj.data.vertices)]
    centre = sum(sack_pts, Vector()) / len(sack_pts)
    return dict(height_m=max(p.z for p in points) - min(p.z for p in points), sole_z_m=min(p.z for p in points),
                head_top_z_m=max(p.z for p in points), sack_centre_m=list(centre),
                sack_offset_from_spine1_m=list(centre - spine1))


def setup_render():
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    shading = scene.display.shading
    shading.light = 'STUDIO'
    shading.color_type = 'MATERIAL'
    shading.show_shadows = True
    shading.shadow_intensity = .3
    shading.show_cavity = True
    shading.cavity_type = 'BOTH'
    shading.curvature_ridge_factor = .6
    shading.curvature_valley_factor = .8
    shading.show_specular_highlight = True
    shading.show_object_outline = False
    scene.display.render_aa = '16'
    scene.render.resolution_x = 1920
    scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = 'PNG'
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0
    scene.world.use_nodes = False
    scene.world.color = (.80, .82, .84)
    camera = bpy.data.objects.new('Studio_Camera', bpy.data.cameras.new('Studio_Camera'))
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera.data.type = 'ORTHO'
    return camera


def snapshot(parts, transform):
    dg = bpy.context.evaluated_depsgraph_get()
    copies = []
    for obj in parts:
        mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(dg), depsgraph=dg)
        dup = bpy.data.objects.new('Render_' + obj.name, mesh)
        bpy.context.collection.objects.link(dup)
        dup.matrix_world = transform @ obj.matrix_world
        copies.append(dup)
    return copies


def remove_copies(copies):
    for obj in copies:
        mesh = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(mesh)


def compare_images(render_path, reference, path):
    # Salt-okunur konsept (sol) ile render (sağ) aynı 1920x1080 tuvale yerleştirilir.
    canvas = np.ones((1080, 1920, 4), dtype=np.float32)
    for index, source in enumerate((reference, render_path)):
        img = bpy.data.images.load(str(source), check_existing=False)
        w, h = img.size
        arr = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(arr)
        arr = arr.reshape(h, w, 4)
        h, w = arr.shape[:2]
        scale = min(940 / w, 1040 / h)
        nh, nw = int(h * scale), int(w * scale)
        resized = arr[np.minimum((np.arange(nh) / scale).astype(int), h - 1)[:, None], np.minimum((np.arange(nw) / scale).astype(int), w - 1)[None, :]]
        x = index * 960 + (960 - nw) // 2
        y = (1080 - nh) // 2
        canvas[y:y + nh, x:x + nw] = resized
        bpy.data.images.remove(img)
    img = bpy.data.images.new('Comparison', 1920, 1080, alpha=True)
    img.pixels.foreach_set(canvas.ravel())
    img.filepath_raw = str(path)
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def renders(assets):
    start = time.perf_counter()
    folder = OUT / 'renders'
    folder.mkdir(parents=True, exist_ok=True)
    camera = setup_render()
    scene = bpy.context.scene
    for rig, parts in assets.values():
        for o in parts:
            o.hide_render = True
    def shot(name, copies, location, target, scale):
        camera.location = location
        camera.rotation_euler = (Vector(target) - camera.location).to_track_quat('-Z', 'Y').to_euler()
        camera.data.ortho_scale = scale
        scene.render.filepath = str(folder / (name + '.png'))
        bpy.ops.render.render(write_still=True)
        remove_copies(copies)
    for variant, (rig, parts) in assets.items():
        reset_pose(rig)
        for name, loc in (('front', (0, -6, .77)), ('left', (6, 0, .77)), ('back', (0, 6, .77))):
            shot(variant.lower() + '_' + name, snapshot(parts, Matrix.Identity(4)), loc, (0, 0, .77), 2.95)
    copies = []
    for x, (variant, (rig, parts)) in zip((-1.6, 0, 1.6), assets.items()):
        copies += snapshot(parts, Matrix.Translation((x, 0, 0)))
    shot('three_front', copies, (0, -8, .78), (0, 0, .78), 5.25)
    copies = []
    for x, (variant, (rig, parts)) in zip((-1.6, 0, 1.6), assets.items()):
        copies += snapshot(parts, Matrix.Translation((x, 0, 0)) @ Matrix.Rotation(-PI / 5, 4, 'Z'))
    shot('three_quarter', copies, (0, -8, .78), (0, 0, .78), 5.25)
    # Kaynak oyuncu yalnız ölçek render'ı için içe alınır ve sonra sahneden çıkarılır.
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(CREW), use_anim=False)
    imported = set(bpy.data.objects) - before
    crewparts = [o for o in imported if o.type == 'MESH']
    copies = snapshot(crewparts, Matrix.Translation((-1.0, 0, 0)))
    for o in imported:
        bpy.data.objects.remove(o, do_unlink=True)
    copies += snapshot(assets['Black'][1], Matrix.Translation((1.0, 0, 0)))
    shot('scale_with_crew', copies, (0, -8, .78), (0, 0, .78), 4.2)
    compare_images(folder / 'three_front.png', REF / '01_thief_three_variants.jpg', folder / 'compare_concept.png')
    for rig, parts in assets.values():
        for o in parts:
            o.hide_render = False
    return dict(render_seconds=round(time.perf_counter() - start, 3), render_engine='BLENDER_WORKBENCH')


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument('--no-render', action='store_true')
    args = parser.parse_args(argv)
    start = time.perf_counter()
    OUT.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 1
    bpy.context.scene.render.fps = 30
    materials()
    template = rig_from_crew()
    assets = {v: make_variant(template, v) for v in VARIANTS}
    bpy.data.objects.remove(template, do_unlink=True)
    meta = {'units': 'meters', 'forward': '-Y', 'up': 'Z', 'bind_pose': 'T-pose', 'variants': {},
            'hex_colors': {'M_Thief_' + k: '#' + v for k, v in PALETTE.items()},
            'variant_materials': {v: {'mask': 'M_Thief_Mask_' + v, 'pants': 'M_Thief_Pants_' + v} for v in VARIANTS},
            'color_note': 'FBX DiffuseColor is the linear equivalent of the sRGB hex (Blender default) for the Linear URP project.',
            'skeleton_source': 'Export/RescueCrew/SK_RescueCrew_Blue.fbx',
            'sack': {'mesh_suffix': '_Sack', 'bone': PREFIX + 'Spine1', 'weights': 'Hips/Spine/Spine1/Spine2 chain by height (no Neck)'}}
    for variant, (rig, parts) in assets.items():
        reset_pose(rig)
        counts = {o.name: triangles(o) for o in parts}
        total = sum(counts.values())
        measures = measure(rig, parts)
        meta['variants'][variant] = {**measures, 'triangles': total, 'mesh_triangles': counts}
        print('THIEF', variant, total, json.dumps(measures), flush=True)
        assert 5000 <= total <= 8000, (variant, total)
        assert 1.47 <= measures['height_m'] <= 1.53, (variant, measures)
        assert abs(measures['sole_z_m']) <= .01, (variant, measures)
        export_fbx(OUT / ('SK_Thief_' + variant + '.fbx'), rig, parts)
    rig = assets['Black'][0]
    meta['bones'] = {b.name: b.parent.name if b.parent else None for b in rig.data.bones}
    meta['bone_count'] = len(rig.data.bones)
    meta['sack']['offset_from_spine1_m'] = meta['variants']['Black']['sack_offset_from_spine1_m']
    meta['height_m'] = meta['variants']['Black']['height_m']
    meta['head_top_z_m'] = meta['variants']['Black']['head_top_z_m']
    (OUT / 'thief_meta.json').write_text(json.dumps(meta, indent=2), encoding='utf-8')
    hashvalue = geometry_signature(assets)
    renderstats = renders(assets) if not args.no_render else {'render_seconds': 0}
    # Blend'de üç varyant yan yana, hepsi bağ pozunda ve ortak iskelet datablock'undadır.
    for variant, (rig, parts) in assets.items():
        reset_pose(rig)
        rig.location.x = VARIANTS.index(variant) * 1.6 - 1.6
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'Thief.blend'), check_existing=False)
    old = json.loads((OUT / 'stats.json').read_text()) if (OUT / 'stats.json').exists() else {}
    source = hashlib.sha256(Path(__file__).read_bytes().replace(b'\r\n', b'\n')).hexdigest()
    entry = {'geometry_sha256': hashvalue, 'source_sha256': source, **renderstats, 'build_seconds': round(time.perf_counter() - start, 3)}
    stats = {'geometry_sha256': hashvalue, 'variants': meta['variants'], 'last_build': entry,
             'build_history': (old.get('build_history', []) + [entry])[-12:],
             'fbx_sha256': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(OUT.glob('*.fbx'))}}
    (OUT / 'stats.json').write_text(json.dumps(stats, indent=2), encoding='utf-8')
    print('GEOMETRY_SHA256: ' + hashvalue, flush=True)
    print('BUILD OK', json.dumps(entry), flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        print('BUILD FAILED', flush=True)
        sys.exit(1)
