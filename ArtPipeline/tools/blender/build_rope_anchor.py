"""Halat kazığı prop'u: zemine çakılı kısa metal kazık + yanında sarılı halat yığını (SM_RopeAnchor).
Blender 5.2, metre, Z yukarı / -Y ön. Yalnız Export/Props/RopeAnchor yazar. Düz renk, doku yok.
Pivot: kazığın zemine girdiği nokta (0, 0, 0). Boş obje RopeAttach: halatın bağlandığı yaka (tepenin hemen altı).
Kullanım: blender -b --factory-startup --python build_rope_anchor.py --python-exit-code 1 [-- --no-render]
"""
import hashlib
import json
import math
from pathlib import Path
import sys
import traceback
sys.dont_write_bytecode = True
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Export/Props/RopeAnchor'
NAME = 'SM_RopeAnchor'
TAU = 2 * math.pi

STAKE_R = 0.022          # kazık gövdesi yarıçapı
STAKE_BURIED = 0.10      # zeminin altında kalan kısım (çakılmış görünsün)
STAKE_TOP = 0.30         # gövde tepesi (başlık bunun üstünde)
CAP_R, CAP_H = 0.036, 0.03
COLLAR_Z, COLLAR_R, COLLAR_T = 0.27, 0.031, 0.009   # halat yakası (RopeAttach burada)
COIL_C = (0.21, 0.02)    # halat yığını merkezi (kazığın sağı, +X)
COIL_RINGS = ((0.125, 0.022), (0.112, 0.064), (0.098, 0.104))   # (büyük yarıçap, merkez z)
COIL_MINOR = 0.021
COLORS = {'Metal': (0.30, 0.31, 0.33), 'Rope': (0.95, 0.62, 0.12)}   # sRGB


class Geo:
    def __init__(self):
        self.v, self.f, self.m = [], [], []

    def add(self, verts, faces, mat):
        base = len(self.v)
        self.v.extend(tuple(round(c, 6) for c in p) for p in verts)
        self.f.extend(tuple(base + i for i in face) for face in faces)
        self.m.extend([mat] * len(faces))

    def cylinder(self, center, r, z0, z1, sides, mat, caps=(True, True)):
        cx, cy = center
        ring = lambda z: [(cx + r * math.cos(TAU * i / sides + math.pi / sides), cy + r * math.sin(TAU * i / sides + math.pi / sides), z) for i in range(sides)]
        verts = ring(z0) + ring(z1)
        faces = [(i, (i + 1) % sides, sides + (i + 1) % sides, sides + i) for i in range(sides)]
        if caps[0]:
            faces.append(tuple(range(sides - 1, -1, -1)))
        if caps[1]:
            faces.append(tuple(range(sides, 2 * sides)))
        self.add(verts, faces, mat)

    def torus(self, center, major, minor, maj_seg, min_seg, mat, twist=0.0):
        cx, cy, cz = center
        verts = []
        for i in range(maj_seg):
            a = TAU * i / maj_seg + twist
            for j in range(min_seg):
                b = TAU * j / min_seg
                rr = major + minor * math.cos(b)
                verts.append((cx + rr * math.cos(a), cy + rr * math.sin(a), cz + minor * math.sin(b)))
        faces = []
        for i in range(maj_seg):
            for j in range(min_seg):
                a, b = i * min_seg + j, i * min_seg + (j + 1) % min_seg
                c, d = ((i + 1) % maj_seg) * min_seg + (j + 1) % min_seg, ((i + 1) % maj_seg) * min_seg + j
                faces.append((a, d, c, b))
        self.add(verts, faces, mat)

    def tube(self, pts, r, sides, mat):
        pts = [Vector(p) for p in pts]
        verts, faces = [], []
        for k, p in enumerate(pts):
            d = (pts[min(k + 1, len(pts) - 1)] - pts[max(k - 1, 0)]).normalized()
            u = d.cross(Vector((0, 0, 1)))
            u = (u if u.length > 1e-4 else d.cross(Vector((1, 0, 0)))).normalized()
            w = d.cross(u).normalized()
            verts.extend(tuple(p + (u * math.cos(TAU * i / sides) + w * math.sin(TAU * i / sides)) * r) for i in range(sides))
        for k in range(len(pts) - 1):
            for i in range(sides):
                a, b = k * sides + i, k * sides + (i + 1) % sides
                faces.append((a, b, b + sides, a + sides))
        self.add(verts, faces, mat)


def linear(c):
    return c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4


def material(name, srgb, metal):
    mat = bpy.data.materials.new('M_RopeAnchor_' + name)
    rgb = tuple(linear(c) for c in srgb)
    mat.diffuse_color = (*rgb, 1)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*rgb, 1)
    bsdf.inputs['Metallic'].default_value = metal
    bsdf.inputs['Roughness'].default_value = 0.45 if metal else 0.85
    return mat


def build_geo(bare=False):
    g = Geo()
    # Kazık: 8 köşeli gövde, alt ucu sivri (zemine çakılı), üstte kalın başlık ve halat yakası.
    g.cylinder((0, 0), STAKE_R, -STAKE_BURIED + 0.03, STAKE_TOP, 8, 0, caps=(False, False))
    tip = [(STAKE_R * math.cos(TAU * i / 8 + math.pi / 8), STAKE_R * math.sin(TAU * i / 8 + math.pi / 8), -STAKE_BURIED + 0.03) for i in range(8)]
    g.add(tip + [(0, 0, -STAKE_BURIED)], [(i, 8, (i + 1) % 8) for i in range(8)], 0)
    g.cylinder((0, 0), CAP_R, STAKE_TOP, STAKE_TOP + CAP_H, 8, 0)
    g.torus((0, 0, COLLAR_Z), COLLAR_R, COLLAR_T, 8, 4, 0)
    if bare:
        return g
    # Sarılı halat yığını: üst üste üç halka, her biri biraz kaydırılmış (elle atılmış gibi).
    for k, (major, z) in enumerate(COIL_RINGS):
        g.torus((COIL_C[0] + 0.006 * (k - 1), COIL_C[1] - 0.004 * k, z), major, COIL_MINOR, 12, 4, 1, twist=0.4 * k)
    # Yığından yakaya uzanan kısa halat parçası (yakanın etrafından dolanır).
    top = COIL_RINGS[-1]
    start = (COIL_C[0] - top[0] - 0.01, COIL_C[1], top[1] + 0.012)
    g.tube([start, (0.07, 0.0, 0.17), (0.04, -0.012, COLLAR_Z - 0.004), (0.0, -0.04, COLLAR_Z)], 0.013, 6, 1)
    return g


def make_object(g, name=NAME):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(g.v, [], g.f)
    for p, m in zip(mesh.polygons, g.m):
        p.material_index = m
        p.use_smooth = False
    mesh.materials.append(material('Metal', COLORS['Metal'], 0.8))
    if 1 in g.m:
        mesh.materials.append(material('Rope', COLORS['Rope'], 0.0))
    mesh.validate()
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    attach = bpy.data.objects.new('RopeAttach', None)
    attach.empty_display_type = 'SPHERE'
    attach.empty_display_size = 0.03
    attach.location = (0, 0, COLLAR_Z)
    bpy.context.collection.objects.link(attach)
    attach.parent = obj
    return obj, attach


def tri_count(mesh):
    mesh.calc_loop_triangles()
    return len(mesh.loop_triangles)


def mesh_digest(mesh):
    data = [[round(c, 5) for c in v.co] for v in mesh.vertices] + [list(p.vertices) + [p.material_index] for p in mesh.polygons]
    return hashlib.sha256(json.dumps(data, separators=(',', ':')).encode()).hexdigest()


def render_preview(obj, path):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    sh = scene.display.shading
    sh.light, sh.color_type, sh.show_shadows, sh.show_object_outline = 'STUDIO', 'MATERIAL', True, True
    scene.world = bpy.data.worlds.new('World')
    scene.world.color = (0.62, 0.66, 0.70)
    sh.background_type = 'WORLD'
    scene.view_settings.view_transform = 'Standard'
    scene.render.resolution_x, scene.render.resolution_y = 480, 360
    scene.render.image_settings.file_format = 'PNG'
    fm = bpy.data.meshes.new('Floor')
    fm.from_pydata([(-.3, -.3, 0), (.5, -.3, 0), (.5, .3, 0), (-.3, .3, 0)], [], [(0, 1, 2, 3)])
    fmat = bpy.data.materials.new('M_Floor')
    fmat.diffuse_color = (0.50, 0.52, 0.50, 1)
    fm.materials.append(fmat)
    floor = bpy.data.objects.new('Floor', fm)
    bpy.context.collection.objects.link(floor)
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam'))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 0.78
    bpy.context.collection.objects.link(cam)
    cam.location = (0.75, -0.95, 0.62)
    target = (0,0,.145) if obj.name.endswith('_Bare') else (.12,0,.12)
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    import numpy as np
    img = bpy.data.images.load(str(path))
    px = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(img.size[1], img.size[0], 4)
    # Byte PNG'nin Image.pixels değerleri sRGB / 255'tir; ikinci gamma uygulanmaz.
    lum = max(float(p[:3].mean()) * 255 for p in (px[0, 0], px[0, -1], px[-1, 0], px[-1, -1]))
    bpy.data.images.remove(img)
    for o in (floor, cam):
        bpy.data.objects.remove(o, do_unlink=True)
    if lum < 150:
        raise RuntimeError('karanlik onizleme %.3f' % lum)
    return lum


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1
    scene.render.fps = 30
    OUT.mkdir(parents=True, exist_ok=True)
    obj, attach = make_object(build_geo())
    tris = tri_count(obj.data)
    if mesh_digest(obj.data) != '861db6e1b6fc6149102b22564f0f4d4dff3fee0f849f3037cb75ab1005da9d15':
        raise RuntimeError('eski SM_RopeAnchor geometrisi degisti')
    if not 200 <= tris <= 600:
        raise RuntimeError('ucgen sayisi %d (200-600 bekleniyor)' % tris)
    lo = [min(v.co[i] for v in obj.data.vertices) for i in range(3)]
    hi = [max(v.co[i] for v in obj.data.vertices) for i in range(3)]
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    attach.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=str(OUT / (NAME + '.fbx')), use_selection=True,
        object_types={'EMPTY', 'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        mesh_smooth_type='FACE', use_mesh_modifiers=False, bake_anim=False, use_custom_props=True,
        check_existing=False, colors_type='SRGB')
    meta = {
        'name': NAME, 'units': 'meters', 'forward': '-Y', 'up': 'Z', 'file': NAME + '.fbx',
        'triangles': tris, 'vertices': len(obj.data.vertices),
        'pivot': 'Kazığın zemine girdiği nokta (0, 0, 0); zemin z=0. Kazığın %.2f m\'si zemin altında.' % STAKE_BURIED,
        'bounds_min_m': [round(c, 4) for c in lo], 'bounds_max_m': [round(c, 4) for c in hi],
        'stake': {'radius_m': STAKE_R, 'top_z_m': round(STAKE_TOP + CAP_H, 4), 'buried_m': STAKE_BURIED},
        'rope_coil': {'center_xy_m': list(COIL_C), 'outer_radius_m': round(COIL_RINGS[0][0] + COIL_MINOR, 4),
                      'height_m': round(COIL_RINGS[-1][1] + COIL_MINOR, 4)},
        'empties': {'RopeAttach': {'position_m': [0.0, 0.0, COLLAR_Z], 'parent': NAME,
                                   'note': 'Halatın bağlandığı yaka, kazık tepesinin hemen altı. Unity LineRenderer '
                                           'halatı bu noktadan başlatır (Unity: yerel (0, %.2f, 0)).' % COLLAR_Z}},
        'materials': {'M_RopeAnchor_Metal': {'srgb': list(COLORS['Metal']), 'metallic': 0.8},
                      'M_RopeAnchor_Rope': {'srgb': list(COLORS['Rope']), 'metallic': 0.0}},
        'textures': 'yok (düz renk)',
        'unity_import': 'Scale Factor 1, Convert Units açık, Bake Axis Conversion kapalı; Materials: Import via '
                        'MaterialDescription. Collider gerekmez (dekor); istenirse kazığa CapsuleCollider (r 0.03, h 0.33).',
        'mesh_sha256': mesh_digest(obj.data)}
    (OUT / 'rope_anchor_meta.json').write_text(json.dumps(meta, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print('TRIS', tris, 'BOUNDS', meta['bounds_min_m'], meta['bounds_max_m'], flush=True)
    print('MESH_SHA256', meta['mesh_sha256'], flush=True)
    if '--no-render' not in argv:
        lum = render_preview(obj, OUT / (NAME + '_preview.png'))
        print('PREVIEW lum=%.3f' % lum, flush=True)
    obj.hide_render = True
    # Ayrı export: eski prop'un köşeleri, yüzleri ve pivotu aynen korunur.
    attach.name = 'Full_RopeAttach'
    bare, ba = make_object(build_geo(bare=True), NAME + '_Bare')
    ba.name = 'RopeAttach'
    bpy.ops.object.select_all(action='DESELECT')
    for o in (bare, ba):
        o.select_set(True)
    bpy.context.view_layer.objects.active = bare
    scene.render.fps = 30
    bpy.ops.export_scene.fbx(filepath=str(OUT / (NAME + '_Bare.fbx')), use_selection=True,
        object_types={'EMPTY', 'MESH'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        mesh_smooth_type='FACE', use_mesh_modifiers=False, bake_anim=False, use_custom_props=True,
        check_existing=False, colors_type='SRGB')
    meta['bare_variant'] = {'file': NAME + '_Bare.fbx', 'triangles': tri_count(bare.data),
        'mesh_sha256': mesh_digest(bare.data), 'pivot_m': [0, 0, 0], 'RopeAttach': [0, 0, COLLAR_Z]}
    meta['source_sha256'] = hashlib.sha256(Path(__file__).read_bytes().replace(b'\r\n', b'\n')).hexdigest()
    (OUT / 'rope_anchor_meta.json').write_text(json.dumps(meta, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    if '--no-render' not in argv:
        render_preview(bare, OUT / (NAME + '_Bare_preview.png'))
    print('BUILD OK', flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        print('BUILD FAILED', flush=True)
        sys.exit(1)
