"""Taşınabilir halat kangalı; metre, Blender Z yukarı. Yalnız RopeCoil çıktıları."""
import hashlib
import json
import math
from pathlib import Path
import sys
import traceback
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
from mathutils import Vector
from build_rope_anchor import Geo, COLORS, material, tri_count, mesh_digest

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Export/Props/RopeCoil'
NAME = 'SM_RopeCoil'
GRIPS = {'Grip_L': (0.204, 0, 0), 'Grip_R': (-0.204, 0, 0)}
ROPE_RADIUS = .015
TURNS = 6
# Kısa uç: ön iç sarımın (yarıçap .168, derinlik .040) bir köşe açısından başlar. Sarım 11-gen + 45° twist,
# k=-2 → -20.45°; böylece uç köşeleri sarım köşelerine ≤3 cm yakındır (verify "kopuk uç" kontrolü).
_TAIL_A = math.radians(45) - 2*2*math.pi/11
TAIL_PATH = [(.168*math.cos(_TAIL_A), .168*math.sin(_TAIL_A), .040),
             (.176, -.108, .041), (.190, -.152, .043), (.182, -.196, .039)]
TAIL_LENGTH = round(sum((Vector(b)-Vector(a)).length for a, b in zip(TAIL_PATH, TAIL_PATH[1:])), 4)


def make_coil():
    g = Geo()
    # İki radyal sıra × üç derinlik: önden de kalın demet okunur, tek halka silueti oluşmaz.
    # Hafif el sarımı düzensizliği; kapak, merkez pivot ve mevcut grip noktaları korunur.
    for k, (depth, radius) in enumerate(((-.040,.170),(-.040,.204),
                                       (0,.172),(0,.204),(.040,.168),(.040,.202))):
        start = len(g.v)
        g.torus((0, 0, depth), radius, ROPE_RADIUS, 11, 5, 0, twist=math.pi/4)
        for i in range(start, len(g.v)):
            x, y, z = g.v[i]
            a = math.atan2(y, x)
            wobble = .0015 * math.sin(3*a + k*.7)
            g.v[i] = (x + wobble*math.cos(a), y + wobble*math.sin(a),
                      z + .001*math.sin(2*a+k))
    # İç sarımdan çıkıp alt-sağ siluetin dışına kıvrılan kısa uç; aynı rijit gövdeye ait.
    g.tube(TAIL_PATH, ROPE_RADIUS, 4, 0)
    g.f += [tuple(range(len(g.v)-4,len(g.v)))]
    g.v = [(x, -z, y) for x, y, z in g.v]
    me = bpy.data.meshes.new(NAME)
    me.from_pydata(g.v, [], g.f)
    for face in me.polygons:
        face.use_smooth = len(face.vertices)==4
    me.materials.append(material('Rope', COLORS['Rope'], 0))
    me.update()
    obj = bpy.data.objects.new(NAME, me)
    bpy.context.collection.objects.link(obj)
    markers = []
    for name, pos in GRIPS.items():
        e = bpy.data.objects.new(name, None)
        bpy.context.collection.objects.link(e)
        e.parent, e.location = obj, pos
        e.empty_display_size = 0.025
        markers.append(e)
    return obj, markers


def choose_slot():
    meta = json.loads((ROOT / 'Export/TowTruck/towtruck_meta.json').read_text(encoding='utf-8'))
    # Bölüm 1: çekici Z=+90 derece, dolayısıyla yerel +X yol/dağ yönü +Y'ye bakar.
    # İçe aktarılan kapak geometrisi tabanı ölçer; üst menteşe yüksekliği taban sanılmaz.
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(ROOT / 'Export/TowTruck/SM_TowTruck.fbx'), use_anim=False)
    objs = [o for o in bpy.data.objects if o not in before]
    candidates = []
    for name, part in meta['parts'].items():
        if '_LockerDoor_L' not in name:
            continue
        obj = next(o for o in objs if o.name == name)
        pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
        lo = [min(p[i] for p in pts) for i in range(3)]
        hi = [max(p[i] for p in pts) for i in range(3)]
        if lo[2] <= 1.2:
            candidates.append((lo[2], name, lo, hi, part))
    floor, name, lo, hi, part = min(candidates)
    slot = {'cabinet_part': name, 'parent': 'SM_TowTruck', 'position_m': [1.10, round((lo[1]+hi[1])/2, 4), 1.08],
        'rotation_euler_deg': [0, 0, 90], 'rotation_order': 'XYZ', 'up': 'Z', 'units': 'meters',
        'floor_m': round(floor, 4), 'door_bounds_min_m': lo, 'door_bounds_max_m': hi,
        'side': 'road_shoulder', 'side_root_normal': [1, 0, 0], 'door_motion': part['motion'],
        'placement_evidence': 'build_bolum1_ucurum.py place_towtruck: Z=+90deg, road_at_x(15)+(0,-6.5,0); +X becomes +Y toward road/mountain',
        'note': 'En alçak yol tarafı L1; L2 teker üstünde. Merkez z=1.08 m, ön yüz kapak iç yüzünden içeride. Dikey kangal dolap içinde askıda saklanır.'}
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)
    return slot


def preview(obj):
    import numpy as np
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    sh = scene.display.shading
    sh.light, sh.color_type, sh.background_type = 'STUDIO', 'MATERIAL', 'WORLD'
    sh.show_shadows, sh.show_cavity, sh.show_object_outline = True, True, True
    scene.world = bpy.data.worlds.new('World')
    scene.world.color = (0.62, 0.66, 0.70)
    scene.view_settings.view_transform = 'Standard'
    scene.render.resolution_x, scene.render.resolution_y = 480, 480
    scene.render.resolution_percentage = 100
    cam = bpy.data.objects.new('PreviewCamera', bpy.data.cameras.new('PreviewCamera'))
    bpy.context.collection.objects.link(cam)
    cam.location = (0.65, -1, 0.50)
    cam.rotation_euler = (-cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type, cam.data.ortho_scale = 'ORTHO', 0.62
    scene.camera = cam
    path = OUT / (NAME + '_preview.png')
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(str(path))
    px = np.empty(img.size[0]*img.size[1]*4,dtype=np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(img.size[1], img.size[0], 4)
    bright = max(float(p[:3].mean())*255 for p in (px[0, 0], px[0, -1], px[-1, 0], px[-1, -1]))
    if bright < 150:
        raise RuntimeError('karanlik onizleme %.1f' % bright)
    bpy.data.images.remove(img)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    OUT.mkdir(parents=True, exist_ok=True)
    slot = choose_slot()
    obj, markers = make_coil()
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT')
    for o in [obj] + markers:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.context.scene.render.fps = 30
    bpy.ops.export_scene.fbx(filepath=str(OUT / (NAME + '.fbx')), use_selection=True,
        object_types={'MESH', 'EMPTY'}, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        mesh_smooth_type='FACE', use_mesh_modifiers=False, bake_anim=False, use_custom_props=True, check_existing=False)
    lo = [min(v.co[i] for v in obj.data.vertices) for i in range(3)]
    hi = [max(v.co[i] for v in obj.data.vertices) for i in range(3)]
    meta = {'name': NAME, 'file': NAME+'.fbx', 'units': 'meters', 'up': 'Z', 'forward': '-Y',
        'triangles': tri_count(obj.data), 'dimensions_m': [round(b-a, 6) for a, b in zip(lo, hi)],
        'bounds_min_m': lo, 'bounds_max_m': hi, 'pivot_m': [0, 0, 0],
        'grips': {n: {'position_m': list(p), 'parent': NAME} for n, p in GRIPS.items()},
        'rope_radius_m': ROPE_RADIUS, 'turn_count': TURNS,
        'bundle_depth_m': round(hi[1]-lo[1],6), 'tail_length_m': TAIL_LENGTH,
        'tail_note': 'İç sarımdan alt-sağ siluetin dışına çıkan kısa uç aynı rijit mesh içindedir.',
        'color_hex': '#F29E1F', 'color_srgb': list(COLORS['Rope']),
        'color_note': 'Eski kazıkla aynı kesin sRGB (0.95,0.62,0.12); hex en yakın 8-bit gösterim. Base Color lineer.',
        'towtruck_slot': slot, 'mesh_sha256': mesh_digest(obj.data),
        'source_sha256': hashlib.sha256(Path(__file__).read_bytes().replace(b'\r\n', b'\n')).hexdigest()}
    (OUT / 'rope_coil_meta.json').write_text(json.dumps(meta, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')
    (OUT / 'README.md').write_text('# SM_RopeCoil\n\n%d üçgen; altı görünür sarım, çap ~0.44 m, halat kalınlığı 0.030 m; demet derinliği ~0.113 m. İç sarımdan alt-sağ siluetin dışına kıvrılan kısa uç rijit gövdeye bağlıdır. Pivot merkez; Grip_L/R x=±0.204 m.\n\n' % meta['triangles'] +
        'Dolap: **SM_TowTruck_LockerDoor_L1**, taban %.3f m. Bölüm 1 yerleşiminde çekici +90° Z dönük, '
        'yerel +X yol/dağ tarafına (+Y) bakar. L1 tabanı L2 teker üstü dolabından alçaktır ve 1.2 m erişim sınırını sağlar. '
        'Kangal kök-yerel (1.10, %.3f, 1.08) m, XYZ Euler (0,0,90)°; dolap içinde dik ve askıda. '
        'L1 kapağı (0,0,-1) ekseninde 90° açılır. Kamyon yeniden yönlendirilirse yol tarafını oyun kodu yeniden değerlendirmelidir.\n\n'
        'Unity: Scale Factor 1, Convert Units açık, Bake Axis Conversion kapalı. Grip noktaları el IK hedefidir. '
        'Kangal rijit prop olarak karaktere bağlanır; klip FBX’inde mesh bulunmaz.\n' % (slot['floor_m'], slot['position_m'][1]), encoding='utf-8')
    if '--no-render' not in sys.argv:
        preview(obj)
    print('ROPE_COIL tris=%d slot=%s floor=%.3f' % (meta['triangles'], slot['cabinet_part'], slot['floor_m']), flush=True)
    print('BUILD OK', flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        print('BUILD FAILED', flush=True)
        sys.exit(1)
