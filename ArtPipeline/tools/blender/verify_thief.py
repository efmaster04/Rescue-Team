"""Thief FBX kabul denetimi: izlenen dosya yazmaz, rapor yalnız stdout ve temp.
Çalıştır: blender -b --factory-startup --python-exit-code 1 --python tools/blender/verify_thief.py
"""
import hashlib
import json
import math
from pathlib import Path
import sys
import tempfile
import time
import traceback
sys.dont_write_bytecode = True
import bpy
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Export/Thief'
CREW = ROOT / 'Export/RescueCrew/SK_RescueCrew_Blue.fbx'
CLIP = ROOT / 'Export/Animations/Procedural/Carry_OneHand_Walk.fbx'
CLIP_FRAME = 6
PREFIX = 'mixamorig:'
VARIANTS = ('Black', 'Red', 'Green')
ERRORS = []
REPORT = {'variants': {}, 'errors': ERRORS}


def check(condition, message):
    if not condition:
        ERRORS.append(message)
        print('FAIL', message, flush=True)


def filehash(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def repository_snapshot():
    # Git komutu gerekmez: izlenenlerin üst kümesi olan bütün çalışma dosyaları.
    result = {}
    for folder in (ROOT / 'tools', ROOT / 'Export', ROOT / 'ArtSource'):
        if folder.exists():
            for path in sorted(folder.rglob('*')):
                if path.is_file():
                    result[str(path.relative_to(ROOT))] = filehash(path)
    return result


def empty_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)


def import_fbx(path, anim=False):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=anim, anim_offset=0)
    objects = list(set(bpy.data.objects) - before)
    rigs = [o for o in objects if o.type == 'ARMATURE']
    check(len(rigs) == 1, str(path.name) + ': exactly one armature required')
    return rigs[0], [o for o in objects if o.type == 'MESH'], objects


def hierarchy(rig):
    return {b.name: b.parent.name if b.parent else None for b in rig.data.bones}


def compare_bones(rig, expected, label):
    actual = hierarchy(rig)
    missing = sorted(set(expected) - set(actual))
    extra = sorted(set(actual) - set(expected))
    parents = {k: [expected[k], actual[k]] for k in set(expected) & set(actual) if expected[k] != actual[k]}
    check(not (missing or extra or parents), f'{label}: skeleton mismatch missing={missing}, extra={extra}, parents={parents}')
    return {'count': len(actual), 'missing': missing, 'extra': extra, 'parent_mismatch': parents}


def raw_fbx_colors(path):
    from io_scene_fbx import parse_fbx
    root, _ = parse_fbx.parse(str(path))
    objects = next(e for e in root.elems if e.id == b'Objects')
    result = {}
    for item in objects.elems:
        if item.id != b'Material':
            continue
        name = item.props[1].split(b'\x00')[0].decode()
        for props in item.elems:
            if props.id != b'Properties70':
                continue
            for prop in props.elems:
                if prop.id == b'P' and prop.props[0] == b'DiffuseColor':
                    result[name] = list(prop.props[-3:])
    return result


def linear(v):
    return v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4


def mesh_data(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(dg)
    mesh = evaluated.to_mesh()
    mesh.calc_loop_triangles()
    verts = [obj.matrix_world @ v.co for v in mesh.vertices]
    faces = [tuple(p.vertices) for p in mesh.loop_triangles]
    evaluated.to_mesh_clear()
    return verts, faces


def core_component(obj):
    # En büyük bağlı bileşen ana kapalı gövde kabuğudur.
    verts, faces = mesh_data(obj)
    links = {i: set() for i in range(len(verts))}
    for a, b, c in faces:
        links[a].update((b, c))
        links[b].update((a, c))
        links[c].update((a, b))
    remaining = set(links)
    groups = []
    while remaining:
        seed = min(remaining)
        found = {seed}
        stack = [seed]
        remaining.remove(seed)
        while stack:
            for i in links[stack.pop()]:
                if i in remaining:
                    remaining.remove(i)
                    found.add(i)
                    stack.append(i)
        groups.append(found)
    core = max(groups, key=len)
    return verts, [f for f in faces if f[0] in core]


def inside(tree, point):
    votes = 0
    for direction in (Vector((1, .317, .193)).normalized(), Vector((.179, 1, .293)).normalized(), Vector((.231, .137, 1)).normalized()):
        origin = point.copy()
        hits = 0
        for _ in range(64):
            loc, normal, index, distance = tree.ray_cast(origin, direction, 10)
            if loc is None:
                break
            hits += 1
            origin = loc + direction * 1e-6
        votes += hits % 2
    return votes >= 2


def penetration(source, target):
    verts, faces = source
    targetverts, targetfaces = target
    tree = BVHTree.FromPolygons(targetverts, targetfaces, all_triangles=True, epsilon=1e-8)
    sourcetree = BVHTree.FromPolygons(verts, faces, all_triangles=True, epsilon=1e-8)
    overlaps = sourcetree.overlap(tree)
    samples = list(verts)
    # Kesişen üçgenlerin iç kısmı da örneklenir; yalnız köşe testi yeterli değildir.
    for i in sorted({i for i, j in overlaps}):
        a, b, c = (verts[k] for k in faces[i])
        for u in range(5):
            for v in range(5 - u):
                samples.append(a + (b - a) * (u / 4) + (c - a) * (v / 4))
    maximum = 0.
    worst = None
    for p in samples:
        near, normal, index, dist = tree.find_nearest(p)
        if near is None or dist <= 1e-6 or (p - near).dot(normal) >= -1e-7:
            continue
        if inside(tree, p) and dist > maximum:
            maximum = dist
            worst = list(p)
    return {'max_depth_m': maximum, 'worst_point_m': worst, 'overlap_triangles': len(overlaps), 'samples': len(samples)}


def rest_pose(rig):
    rig.animation_data_clear()
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()


def clip_pose_checks(rig, parts, expected):
    # Yürüme klibi ayrı ithal edilir; action'ı varyant iskeletine uygulanır (aynı kemik adları).
    check(CLIP.exists(), f'missing clip {CLIP}')
    if not CLIP.exists():
        return {}
    actions_before = set(bpy.data.actions)
    cliprig, clipparts, objects = import_fbx(CLIP, anim=True)
    compare_bones(cliprig, expected, CLIP.name)
    new_actions = [a for a in bpy.data.actions if a not in actions_before]
    action = cliprig.animation_data.action if cliprig.animation_data and cliprig.animation_data.action else (new_actions[0] if new_actions else None)
    check(action is not None, f'{CLIP.name}: animation action missing')
    for o in objects:
        bpy.data.objects.remove(o, do_unlink=True)
    if action is None:
        return {}
    action.use_fake_user = True
    frame = int(min(max(CLIP_FRAME, action.frame_range[0]), action.frame_range[1]))
    rest_pose(rig)
    rig.animation_data_create()
    rig.animation_data.action = action
    if action.slots:
        rig.animation_data.action_slot = action.slots[0]
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    moved = max((pb.matrix_basis.to_quaternion().angle for pb in rig.pose.bones), default=0)
    check(moved > 1e-3, f'{rig.name}/{CLIP.name}: clip did not pose the skeleton')
    torso = next(o for o in parts if o.name.endswith('_Torso'))
    target = core_component(torso)
    collisions = {}
    for obj in parts:
        if obj.name.endswith(('_LeftArm', '_RightArm', '_Sack')):
            result = penetration(mesh_data(obj), target)
            collisions[obj.name] = result
            check(result['max_depth_m'] <= .030001, f"{rig.name}/{CLIP.name}@{frame}/{obj.name}: torso penetration {result['max_depth_m']*100:.3f} cm")
    rest_pose(rig)
    return {'action_name': action.name, 'frame': frame, 'frame_range': [float(action.frame_range[0]), float(action.frame_range[1])],
            'max_bone_rotation_rad': moved, 'collisions': collisions}


def main():
    start = time.perf_counter()
    before = repository_snapshot()
    meta = json.loads((OUT / 'thief_meta.json').read_text(encoding='utf-8'))
    stats = json.loads((OUT / 'stats.json').read_text(encoding='utf-8'))
    empty_scene()
    crew, _, _ = import_fbx(CREW)
    expected = hierarchy(crew)
    check(len(expected) == 31, f'crew skeleton has {len(expected)} bones, expected 31')
    rest = {b.name: b.matrix_local.copy() for b in crew.data.bones}
    for variant in VARIANTS:
        empty_scene()
        path = OUT / f'SK_Thief_{variant}.fbx'
        check(path.exists(), f'{path.name} missing')
        if not path.exists():
            continue
        rig, parts, _ = import_fbx(path)
        data = {'skeleton': compare_bones(rig, expected, variant)}
        REPORT['variants'][variant] = data
        check(rig.name == 'SK_Thief_' + variant, f'{variant}: root name {rig.name}')
        check(not rig.animation_data or not rig.animation_data.action, f'{variant}: bind FBX contains animation')
        for b in rig.data.bones:
            if b.name in rest:
                check(max(abs(b.matrix_local[i][j] - rest[b.name][i][j]) for i in range(4) for j in range(4)) < 2e-5, f'{variant}/{b.name}: bind matrix mismatch (not T-pose bind)')
        points = []
        counts = {}
        max_influences = 0
        max_error = 0
        for obj in sorted(parts, key=lambda o: o.name):
            obj.data.calc_loop_triangles()
            counts[obj.name] = len(obj.data.loop_triangles)
            print('MESH', obj.name, counts[obj.name], flush=True)
            points.extend(obj.matrix_world @ v.co for v in obj.data.vertices)
            check(obj.parent == rig, f'{obj.name}: parent armature missing')
            check(any(m.type == 'ARMATURE' and m.object == rig for m in obj.modifiers), f'{obj.name}: armature modifier missing')
            for v in obj.data.vertices:
                weights = [g.weight for g in v.groups if g.weight > 1e-7 and obj.vertex_groups[g.group].name in expected]
                max_influences = max(max_influences, len(weights))
                max_error = max(max_error, abs(sum(weights) - 1))
                check(1 <= len(weights) <= 4, f'{obj.name} vertex {v.index}: {len(weights)} bone influences')
                check(abs(sum(weights) - 1) < 1e-5, f'{obj.name} vertex {v.index}: non-normalized {sum(weights)}')
        check(any(o.name.endswith('_Sack') for o in parts), f'{variant}: sack mesh missing')
        data.update(mesh_triangles=counts, triangles=sum(counts.values()), height_m=max(p.z for p in points) - min(p.z for p in points),
                    sole_z_m=min(p.z for p in points), max_influences=max_influences, max_weight_error=max_error)
        check(5000 <= data['triangles'] <= 8000, f'{variant}: triangle budget {data["triangles"]}')
        check(1.47 <= data['height_m'] <= 1.53, f'{variant}: height {data["height_m"]}')
        check(abs(data['sole_z_m']) <= .01, f'{variant}: sole {data["sole_z_m"]}')
        colors = raw_fbx_colors(path)
        color_errors = {}
        for name, actual in colors.items():
            check(name in meta['hex_colors'], f'{variant}: unexpected material {name}')
            if name not in meta['hex_colors']:
                continue
            h = meta['hex_colors'][name].lstrip('#')
            wanted = [linear(int(h[i:i+2], 16) / 255) for i in (0, 2, 4)]
            error = max(abs(a - b) for a, b in zip(actual, wanted))
            color_errors[name] = error
            check(error <= .01, f'{variant}/{name}: DiffuseColor {actual} expected linear {wanted}')
        check(bool(colors), f'{variant}: no raw FBX colors')
        for key in ('mask', 'pants'):
            wanted = meta['variant_materials'][variant][key]
            check(wanted in colors, f'{variant}: {key} material {wanted} missing from FBX')
        data['max_linear_color_error'] = max(color_errors.values(), default=999)
        data['materials'] = sorted(colors)
        data['clip_pose'] = clip_pose_checks(rig, parts, expected)
    history = stats.get('build_history', [])
    check(len(history) >= 2, 'Need two consecutive builds for determinism')
    if len(history) >= 2:
        a, b = history[-2:]
        check(a['geometry_sha256'] == b['geometry_sha256'], 'Consecutive geometry SHA256 mismatch')
        check(a['source_sha256'] == b['source_sha256'], 'Consecutive build sources differ')
        source = hashlib.sha256((ROOT / 'tools/blender/build_thief.py').read_bytes().replace(b'\r\n', b'\n')).hexdigest()
        check(b['source_sha256'] == source, 'Build source changed after acceptance builds')
        REPORT['determinism'] = {'geometry_sha256': b['geometry_sha256'], 'source_sha256': source, 'two_consecutive_builds': a['geometry_sha256'] == b['geometry_sha256']}
    for filename, wanted in stats.get('fbx_sha256', {}).items():
        check(filehash(OUT / filename) == wanted, f'{filename}: file changed since build')
    after = repository_snapshot()
    modified = sorted(k for k in set(before) | set(after) if before.get(k) != after.get(k))
    check(not modified, 'Verifier changed repository files: ' + str(modified))
    REPORT['repository_files_unchanged'] = not modified
    REPORT['repository_file_count'] = len(before)
    REPORT['seconds'] = round(time.perf_counter() - start, 3)
    REPORT['ok'] = not ERRORS
    destination = Path(tempfile.gettempdir()) / 'verify_thief_report.json'
    destination.write_text(json.dumps(REPORT, indent=2), encoding='utf-8')
    print(json.dumps(REPORT, indent=2), flush=True)
    print('REPORT', destination, flush=True)
    if ERRORS:
        raise RuntimeError(f'{len(ERRORS)} acceptance errors')
    print('VERIFY OK', flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        print('VERIFY FAILED', flush=True)
        sys.exit(1)
