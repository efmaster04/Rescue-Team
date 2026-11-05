"""Boş sahneye FBX yükleyerek çekiciyi bağımsız denetler; hata exit 1.

Repoda izlenen hiçbir dosyaya yazmaz: sonuç stdout'a ve tempfile.gettempdir() altına gider.
stats.json'u yalnız build_towtruck.py yazar.
"""
import hashlib
import json
import math
from pathlib import Path
import struct
import sys
import tempfile
import traceback
sys.dont_write_bytecode = True

import bpy
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'Export/TowTruck'
P = 'SM_TowTruck'
HIERARCHY = {
    P+'_Body': P, P+'_Interior': P, P+'_Glass': P, P+'_Door_FL': P, P+'_Door_FR': P,
    **{f'{P}_Wheel_{a}_{s}': P for a in ('F', 'R1', 'R2') for s in ('L', 'R')},
    P+'_LightBar': P,
    **{f'{P}_LockerDoor_{k}': P for k in ('L1', 'L2', 'R1', 'R2')},
    **{f'{P}_Outrigger_{k}_Beam': P for k in ('ML', 'MR', 'RL', 'RR')},
    **{f'{P}_Outrigger_{k}_Jack': f'{P}_Outrigger_{k}_Beam' for k in ('ML', 'MR', 'RL', 'RR')},
    P+'_Crane_Turntable': P, P+'_Crane_Drum': P+'_Crane_Turntable', 'CableStart': P+'_Crane_Turntable',
    P+'_Crane_Boom': P+'_Crane_Turntable', P+'_Crane_BoomExt_1': P+'_Crane_Boom',
    P+'_Crane_BoomExt_2': P+'_Crane_BoomExt_1', 'CableTip': P+'_Crane_BoomExt_2', P+'_Crane_Hook': 'CableTip',
    **{f'{P}_Cone_{i:02d}': P for i in range(1, 5)}}
EMPTIES = {P, 'CableStart', 'CableTip'}
VIEWS = ('front', 'rear', 'left', 'right', 'threequarter_front_left', 'threequarter_rear_right_work',
         'interior_top', 'scale_with_minivan', 'compare_front', 'compare_rear', 'compare_side', 'compare_threequarter')


def srgb_to_linear(v):
    return v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4


def bounds(points):
    return (Vector(tuple(min(p[i] for p in points) for i in range(3))),
            Vector(tuple(max(p[i] for p in points) for i in range(3))))


def raw_fbx_colors(path):
    # Export edilmiş gerçek DiffuseColor değerleri; Blender render renk dönüşümünden bağımsız.
    from io_scene_fbx import parse_fbx
    root, _ = parse_fbx.parse(str(path))
    result = {}
    objects = next(e for e in root.elems if e.id == b'Objects')
    for item in objects.elems:
        if item.id != b'Material':
            continue
        name = item.props[1].split(b'\x00')[0].decode()
        for props in item.elems:
            if props.id == b'Properties70':
                for prop in props.elems:
                    if prop.id == b'P' and prop.props[0] == b'DiffuseColor':
                        result[name] = [float(v) for v in prop.props[-3:]]
    return result


class Shape:
    """Bir mesh'in verilen dünya matrisindeki üçgenleri ve BVH ağacı."""
    def __init__(self, obj, matrix):
        self.verts = [matrix@v.co for v in obj.data.vertices]
        obj.data.calc_loop_triangles()
        self.faces = [tuple(t.vertices) for t in obj.data.loop_triangles]
        self.tree = BVHTree.FromPolygons(self.verts, self.faces, all_triangles=True, epsilon=1e-7)


def penetration(source, target, touched):
    # BVH yüzey kesişimi + üçgen iç örnekleri; en yakın yüzeyin arkasındaki noktalar
    # üç ışında tek sayıda yüzey geçişiyle "içeride" doğrulanır (minibüs yöntemi).
    samples = list(source.verts)
    for index in sorted(touched):
        a, b, c = (source.verts[i] for i in source.faces[index])
        for u in range(7):
            for v in range(7-u):
                samples.append(a+(b-a)*(u/6)+(c-a)*(v/6))
    maximum, worst = 0.0, None
    for p in samples:
        nearest, normal, _, distance = target.tree.find_nearest(p)
        if nearest is None or distance < 1e-6 or (p-nearest).dot(normal) >= -1e-6:
            continue
        votes = 0
        for direction in (Vector((1, .371, .217)).normalized(), Vector((.253, 1, .419)).normalized(),
                          Vector((.173, .281, 1)).normalized()):
            origin, hits = p.copy(), 0
            for _ in range(80):
                loc, _, _, _ = target.tree.ray_cast(origin, direction, 40)
                if loc is None:
                    break
                hits += 1
                origin = loc+direction*1e-5
            votes += hits % 2
        if votes >= 2 and distance > maximum:
            maximum, worst = distance, [round(c, 4) for c in p]
    return maximum, worst


def mutual_penetration(a, b):
    overlap = a.tree.overlap(b.tree)
    if not overlap:
        return 0.0, None, 0
    d1, w1 = penetration(a, b, {i for i, _ in overlap})
    d2, w2 = penetration(b, a, {j for _, j in overlap})
    return (d1, w1, len(overlap)) if d1 >= d2 else (d2, w2, len(overlap))


def posed_matrices(objects, meta, work, overrides=None):
    """Dinlenme dünya matrislerinden, ebeveyn zinciri boyunca hareket uygulanmış dünya matrisleri."""
    overrides = overrides or {}
    rest = {n: o.matrix_world.copy() for n, o in objects.items()}
    delta = {P: Matrix.Identity(4)}
    result = {}
    def solve(name):
        if name in result:
            return result[name]
        parent = objects[name].parent.name
        solve(parent) if parent != P else None
        own = Matrix.Identity(4)
        part = meta['parts'].get(name)
        motion = part and part['motion']
        value = overrides.get(name)
        if motion and (work or value is not None):
            pivot = rest[name].translation
            if motion['type'] in ('hinge', 'rotate_continuous'):
                angle = value if value is not None else motion['work_pose_deg']
                own = (Matrix.Translation(pivot)@Matrix.Rotation(math.radians(angle), 4, Vector(motion['axis']))
                       @ Matrix.Translation(-pivot))
            elif motion['type'] == 'slide':
                own = Matrix.Translation(Vector(motion['axis'])*(value if value is not None else motion['work_pose_m']))
        parent_delta = delta[parent] if parent in delta else Matrix.Identity(4)
        # Hareket ekseni ebeveynin (hareket etmiş) çerçevesindedir.
        world = parent_delta@own@rest[name]
        if motion and motion['type'] == 'hang' and work:
            tip = result['CableTip'].translation
            world = Matrix.Translation(tip+Vector((0, 0, -motion['work_pose_offset_below_cable_tip_m'])))
        delta[name] = world@rest[name].inverted()
        result[name] = world
        return world
    for name in objects:
        if name != P:
            solve(name)
    result[P] = rest[P]
    return result


def verify():
    errors = []
    def check(condition, message):
        if not condition:
            errors.append(message)
    path = OUT/'SM_TowTruck.fbx'
    meta = json.loads((OUT/'towtruck_meta.json').read_text(encoding='utf-8'))
    stats_file = json.loads((OUT/'stats.json').read_text(encoding='utf-8'))
    history = stats_file['build_history']
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    bpy.context.view_layer.update()
    objects = {o.name: o for o in bpy.context.scene.objects}
    expected = set(HIERARCHY) | {P}
    missing = expected-set(objects)
    if missing:
        raise ValueError('Required objects absent: '+str(sorted(missing)))
    check(set(objects) == expected, 'Unexpected objects: '+str(sorted(set(objects)-expected)))
    for name, parent in HIERARCHY.items():
        check(objects[name].parent is not None and objects[name].parent.name == parent, name+': wrong parent')
        check((objects[name].type == 'EMPTY') == (name in EMPTIES), name+': wrong object type')
    root = objects[P]
    check(root.parent is None and root.type == 'EMPTY', 'Root must be a parentless empty')
    check(root.matrix_world.translation.length < 1e-5, 'Root pivot not at origin')
    meshes = {n: o for n, o in objects.items() if o.type == 'MESH'}
    # Dinlenme pozunda hiçbir parça dönmüş olmamalı (meta eksenleri kök çerçevesinde).
    for name, obj in objects.items():
        check(all(abs(obj.matrix_world[i][j]-(1 if i == j else 0)) < 1e-4 for i in range(3) for j in range(3)),
              name+': rest rotation/scale is not identity')
    points = {n: [o.matrix_world@v.co for v in o.data.vertices] for n, o in meshes.items()}
    counts = {}
    for name, obj in meshes.items():
        obj.data.calc_loop_triangles()
        counts[name] = len(obj.data.loop_triangles)
        check(not obj.modifiers, name+': unexpected modifier')
        check(all(not p.use_smooth for p in obj.data.polygons), name+': flat shading lost')
    total = sum(counts.values())
    check(6000 <= total <= 9000, f'Triangle budget: {total}')
    check(counts == stats_file['object_triangles'], 'FBX/build per-object triangles differ')
    # Ölçüler: aynalar hariç zarf; yükseklik çakar hariç.
    envelope, mirror_points = [], 0
    for name, pts in points.items():
        for p in pts:
            if name == P+'_Body' and abs(p.x) > 1.255 and -2.95 < p.y < -2.6 and 1.6 < p.z < 2.1:
                mirror_points += 1
            else:
                envelope.append(p)
    lo, hi = bounds(envelope)
    no_bar = [p for n, pts in points.items() if n != P+'_LightBar' for p in pts]
    dims = {'length_m': hi.y-lo.y, 'width_m': hi.x-lo.x, 'height_m': max(p.z for p in no_bar),
            'lightbar_top_m': max(p.z for p in points[P+'_LightBar']), 'min_z_m': lo.z}
    check(mirror_points > 0, 'Mirror exclusion mask matched nothing')
    for field, value in (('length_m', 8.0), ('width_m', 2.5), ('height_m', 3.2)):
        check(abs(dims[field]-value) <= .05001, f'{field}: {dims[field]:.4f}')
    check(dims['lightbar_top_m'] <= 3.40001, 'Light bar top above 3.4 m')
    check(abs(lo.z) <= .01, f'Lowest point not at ground: {lo.z:.4f}')
    # Pivotlar.
    pivots = {}
    wheel_contacts = []
    for name, part in meta['parts'].items():
        obj = objects[name]
        pivot = obj.matrix_world.translation
        low, high = bounds(points[name])
        check((pivot-Vector(part['pivot_m'])).length < 1e-4, name+': pivot differs from metadata')
        check(obj.parent.name == part['parent'], name+': metadata parent mismatch')
        pivots[name] = {'pivot_m': [round(c, 4) for c in pivot], 'bbox_min_m': [round(c, 4) for c in low],
                        'bbox_max_m': [round(c, 4) for c in high]}
        if '_Wheel_' in name:
            check((pivot-(low+high)/2).length < 2e-3, name+': wheel pivot not at wheel centre')
            check(abs(low.z) <= .01, name+': wheel bottom not at Z=0')
            check(part['motion']['axis'] == [1, 0, 0], name+': axle axis must be X')
            wheel_contacts.append(Vector((pivot.x, pivot.y, 0)))
        elif '_Door_F' in name:
            sign = 1 if name.endswith('L') else -1
            check(abs(pivot.y-low.y) < .012, name+': pivot not on front edge')
            check(abs(pivot.x-(high.x if sign == 1 else low.x)) < .02, name+': hinge not on outer face')
            check(part['motion']['axis'] == [0, 0, -sign], name+': hinge axis must be vertical, opening outward')
        elif '_LockerDoor_' in name:
            # 1 = öndeki uzun dolap (ön dikey menteşe), 2 = tekerlek üstü dolap (üst menteşe).
            sign = 1 if '_LockerDoor_L' in name else -1
            check(abs(pivot.x-(high.x if sign == 1 else low.x)) < .025, name+': hinge not on outer face')
            if name.endswith('1'):
                check(abs(pivot.y-low.y) < .012 and abs(pivot.z-low.z) < .012, name+': hinge not on front edge')
                check(part['motion']['axis'] == [0, 0, -sign], name+': front locker hinge axis')
            else:
                check(abs(pivot.z-high.z) < .012, name+': hinge not at top edge')
                check(part['motion']['axis'] == [0, -sign, 0], name+': top locker hinge axis')
        elif name.endswith('_Jack'):
            check(abs(pivot.z-low.z) < 1e-3, name+': jack pivot not at pad bottom')
        elif '_Cone_' in name:
            check(abs(pivot.z-low.z) < 1e-3 and abs(pivot.x-(low.x+high.x)/2) < 2e-3 and abs(pivot.y-(low.y+high.y)/2) < 2e-3,
                  name+': cone pivot not at base-bottom centre')
        elif name.endswith('_Crane_Turntable'):
            check(part['motion']['axis'] == [0, 0, 1] and part['motion']['range_deg'] is None, name+': must turn freely about Z')
        elif name.endswith('_Crane_Drum'):
            check(abs((pivot-(low+high)/2).length) < 2e-3, name+': drum pivot not at its centre')
        elif name.endswith('_Crane_Boom'):
            check(part['motion']['range_deg'] == [0, 70], name+': raise range must be 0-70 deg')
        elif name.endswith('_Crane_Hook'):
            check(abs(pivot.z-high.z) < 1e-3, name+': hook pivot not at hang point (top)')
    # Kök = altı tekerlek temas noktasını çevreleyen dikdörtgenin merkezi.
    c_lo, c_hi = bounds(wheel_contacts) if wheel_contacts else (Vector(), Vector((1, 1, 1)))
    check(len(wheel_contacts) == 6 and ((c_lo+c_hi)/2).length < 1e-3, 'Root not at wheel-contact rectangle centre')
    check(len({round(c.y, 4) for c in wheel_contacts}) == 3, 'Expected three axles')
    for name in ('CableStart', 'CableTip'):
        check((objects[name].matrix_world.translation-Vector(meta['markers'][name])).length < 1e-4, name+': metadata mismatch')
    # Kol uzunluğu: menteşe -> CableTip, kapalı ve tam açık teleskop.
    hinge = objects[P+'_Crane_Boom'].matrix_world.translation
    closed_len = (objects['CableTip'].matrix_world.translation-hinge).length
    ext = {P+'_Crane_BoomExt_1': meta['boom']['stage_stroke_m'], P+'_Crane_BoomExt_2': meta['boom']['stage_stroke_m']}
    open_len = (posed_matrices(objects, meta, False, ext)['CableTip'].translation-hinge).length
    raised = posed_matrices(objects, meta, False, {**ext, P+'_Crane_Boom': 70})
    raised_len = (raised['CableTip'].translation-raised[P+'_Crane_Boom'].translation).length
    check(abs(closed_len-6.0) <= .1, f'Closed boom length {closed_len:.4f}')
    check(abs(open_len-10.0) <= .1, f'Extended boom length {open_len:.4f}')
    check(abs(raised_len-open_len) < 1e-3 and raised['CableTip'].translation.z > hinge.z+9, 'Boom does not raise about its hinge')
    # Çalışma pozu: her hareketli parça gövdeye <= 2 cm girer; kriko tabanları Z=0.
    work = posed_matrices(objects, meta, True)
    body = Shape(meshes[P+'_Body'], work[P+'_Body'])
    collisions = {}
    for name, part in meta['parts'].items():
        if not part['motion'] or part['motion']['type'] == 'free_rigid_body' or '_Wheel_' in name:
            continue
        depth, worst, pairs = mutual_penetration(Shape(meshes[name], work[name]), body)
        collisions[name] = {'max_penetration_m': round(depth, 5), 'worst_point_m': worst, 'overlap_pairs': pairs}
        check(depth <= .02001, f'Work pose: {name} penetrates body {depth:.4f} m')
    jacks = {}
    for key in ('ML', 'MR', 'RL', 'RR'):
        name = f'{P}_Outrigger_{key}_Jack'
        z = min((work[name]@v.co).z for v in meshes[name].data.vertices)
        jacks[name] = round(z, 5)
        check(abs(z) <= .02, f'{name}: open jack pad not on ground ({z:.4f})')
        beam = work[f'{P}_Outrigger_{key}_Beam'].translation
        check(abs(beam.x) > 1.9, f'{key}: beam not extended sideways')
    # Kapı ve dolap kapakları dinlenmede de gövdeye girmez.
    rest = posed_matrices(objects, meta, False)
    for name in meta['parts']:
        if '_Door_' in name or '_LockerDoor_' in name or '_Outrigger_' in name or '_Crane_' in name:
            depth, _, _ = mutual_penetration(Shape(meshes[name], rest[name]), Shape(meshes[P+'_Body'], rest[P+'_Body']))
            check(depth <= .02001, f'Rest pose: {name} penetrates body {depth:.4f} m')
    # Koniler: boy, birbirine ve tutucuya (gövde) <= 2 cm.
    cones = sorted(n for n in meshes if '_Cone_' in n)
    check(len(cones) == 4, 'Exactly four cones required')
    cone_stats = {}
    shapes = {n: Shape(meshes[n], rest[n]) for n in cones}
    body_rest = Shape(meshes[P+'_Body'], rest[P+'_Body'])
    for i, name in enumerate(cones):
        low, high = bounds(points[name])
        tris = counts[name]
        depth_body, _, _ = mutual_penetration(shapes[name], body_rest)
        others = [mutual_penetration(shapes[name], shapes[o])[0] for o in cones[i+1:]]
        cone_stats[name] = {'height_m': round(high.z-low.z, 4), 'triangles': tris,
                            'base_m': [round(high.x-low.x, 4), round(high.y-low.y, 4)],
                            'into_body_m': round(depth_body, 5), 'into_other_cones_m': [round(d, 5) for d in others]}
        check(abs(high.z-low.z-.70) <= .02, name+': height out of tolerance')
        check(100 <= tris <= 300, name+': triangle count outside 100-300')
        check(depth_body <= .02001, name+': penetrates holder/body')
        check(all(d <= .02001 for d in others), name+': penetrates another cone')
        check(not shapes[name].tree.overlap(Shape(meshes[cones[(i+1) % 4]], rest[cones[(i+1) % 4]]).tree), name+': touches neighbour cone')
    # Renkler: FBX DiffuseColor = hex'in lineer karşılığı (sRGB düzeltmesi yok).
    colors = raw_fbx_colors(path)
    color_stats = {}
    for name, color in meta['materials'].items():
        expected_rgb = [srgb_to_linear(int(color[i:i+2], 16)/255) for i in (1, 3, 5)]
        actual = colors.get(name)
        check(actual is not None and max(abs(a-b) for a, b in zip(actual, expected_rgb)) < 1e-5, name+': FBX linear color mismatch')
        mat = bpy.data.materials.get(name)
        imported = list(mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value[:3]) if mat else None
        check(imported is not None and max(abs(a-b) for a, b in zip(imported, expected_rgb)) < 1e-5, name+': reimported color mismatch')
        color_stats[name] = {'hex': color, 'fbx_diffuse_linear': actual}
    check(set(colors) == set(meta['materials']), 'FBX material set differs from metadata')
    glass_alpha = bpy.data.materials['M_TowTruck_Glass'].node_tree.nodes['Principled BSDF'].inputs['Alpha'].default_value
    check(abs(glass_alpha-meta['glass_alpha']) < 1e-5, 'Glass alpha not preserved')
    check(all(node.type != 'TEX_IMAGE' for mat in bpy.data.materials if mat.node_tree for node in mat.node_tree.nodes),
          'Texture nodes found; flat colour asset required')
    # Determinizm: son iki build aynı kaynak + aynı geometri; FBX geometrisi build ile aynı.
    deterministic = len(history) >= 2 and history[-1]['geometry_sha256'] == history[-2]['geometry_sha256'] \
        and history[-1]['source_sha256'] == history[-2]['source_sha256']
    check(deterministic, 'Two consecutive identical builds have not been recorded')
    source = hashlib.sha256((ROOT/'tools/blender/build_towtruck.py').read_bytes().replace(b'\r\n', b'\n')).hexdigest()
    check(history[-1]['source_sha256'] == source, 'Build script changed after last build')
    check(history[-1]['geometry_sha256'] == stats_file['geometry_sha256'], 'Stale stats.json')
    data = []
    for name in sorted(meshes):
        obj = meshes[name]
        triangles = []
        for tri in obj.data.loop_triangles:
            corners = sorted(tuple(math.floor(float(v)*10000+.371) for v in points[name][i]) for i in tri.vertices)
            triangles.append([corners, obj.data.materials[tri.material_index].name])
        data.append([name, sorted(triangles)])
    fbx_digest = hashlib.sha256(json.dumps(data, separators=(',', ':')).encode()).hexdigest()
    check(fbx_digest == stats_file['canonical_geometry_sha256'], 'FBX geometry differs from build geometry')
    renders = {}
    for view in VIEWS:
        png = OUT/'renders'/(view+'.png')
        renders[view] = list(struct.unpack('!II', png.read_bytes()[16:24])) if png.exists() else None
        check(renders[view] == [1920, 1080], view+': missing or wrong render resolution')
    rounds = sorted(p.name for p in (OUT/'renders').glob('round_*') if list(p.glob('compare_*.png')))
    check(len(rounds) >= 3, 'At least three archived visual rounds required')
    check((OUT/'TowTruck.blend').exists() and (OUT/'visual_review.md').exists(), 'Blend or visual_review.md missing')
    report = {'verified': not errors, 'errors': errors, 'total_triangles': total, 'object_triangles': counts,
              'dimensions': {k: round(v, 4) for k, v in dims.items()}, 'mirror_points_excluded': mirror_points,
              'boom_length_m': {'closed': round(closed_len, 4), 'extended': round(open_len, 4)},
              'pivots': pivots, 'work_pose_collisions': collisions, 'jack_pad_min_z_m': jacks,
              'cones': cone_stats, 'colors': color_stats, 'glass_alpha': glass_alpha,
              'two_consecutive_builds_identical': bool(deterministic), 'fbx_canonical_sha256': fbx_digest,
              'renders': renders, 'archived_visual_rounds': rounds, 'last_build': history[-1]}
    report_path = Path(tempfile.gettempdir())/('verify_towtruck_'+hashlib.sha256(str(ROOT).encode()).hexdigest()[:12]+'.json')
    report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('REPORT '+str(report_path), flush=True)
    print(json.dumps({'total_triangles': total, 'dimensions': report['dimensions'], 'boom_length_m': report['boom_length_m'],
                      'max_work_penetration_m': max((c['max_penetration_m'] for c in collisions.values()), default=0),
                      'jack_pad_min_z_m': jacks, 'deterministic': bool(deterministic), 'errors': errors}, indent=2), flush=True)
    if errors:
        print('VERIFY FAILED', flush=True)
        sys.exit(1)
    print('VERIFY OK', flush=True)


if __name__ == '__main__':
    try:
        verify()
    except Exception:
        traceback.print_exc()
        print('VERIFY FAILED', flush=True)
        sys.exit(1)
