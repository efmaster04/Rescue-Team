"""Salt okunur FBX kabul?: stdout ve tempfile raporu; repo dosyas? yaz?lmaz."""
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
import numpy as np
from mathutils import Vector
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'Export/Traffic'
RENDERS = OUT/'renders/trucks'
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


def canonical(objects):
    data=[]
    for name,obj in sorted(objects.items()):
        if obj.type!='MESH': continue
        points=[obj.matrix_world@v.co for v in obj.data.vertices]
        obj.data.calc_loop_triangles()
        triangles=[]
        for tri in obj.data.loop_triangles:
            corners=sorted(tuple(math.floor(float(v)*10000+.371) for v in points[i]) for i in tri.vertices)
            mat=obj.data.materials[tri.material_index].name
            if mat[-4:-3]=='.' and mat[-3:].isdigit(): mat=mat[:-4]
            triangles.append([corners,mat])
        data.append([name,sorted(triangles)])
    return hashlib.sha256(json.dumps(data,separators=(',',':')).encode()).hexdigest()


def snapshot():
    # Git çalıştırmadan tüm repo dosyalarının boyut/zaman damgasını karşılaştırır.
    return {str(p.relative_to(ROOT)):(p.stat().st_size,p.stat().st_mtime_ns) for p in ROOT.rglob('*') if p.is_file()}


EXPECTED = {
    'PanelVan': {'dimensions': [5.4, 2.0, 2.4], 'budget': [3500, 5500], 'radius': .43, 'x': .84,
                 'wheels': {'FL': -1.7, 'FR': -1.7, 'RL': 1.7, 'RR': 1.7}},
    'BoxTruck': {'dimensions': [9.5, 2.5, 3.6], 'budget': [5000, 8000], 'radius': .57, 'x': 1.025,
                 'wheels': {'F_L': -3.35, 'F_R': -3.35, 'R1_L': 2.05, 'R1_R': 2.05, 'R2_L': 3.35, 'R2_R': 3.35}},
}
VIEWS = [kind+'_'+view for kind in ('panelvan', 'boxtruck') for view in ('threequarter_front', 'left', 'rear')]
VIEWS += ['two_vehicles', 'scale_with_minivan', 'compare_panelvan', 'compare_boxtruck', 'compare_lineup']


def verify():
    before = snapshot()
    errors, all_objects, assets, colors = [], {}, {}, {}
    def check(condition, message):
        if not condition:
            errors.append(message)
    stats = json.loads((OUT/'stats_trucks.json').read_text(encoding='utf-8'))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for kind, spec in EXPECTED.items():
        name = 'SM_'+kind
        path = OUT/(name+'.fbx')
        meta = json.loads((OUT/(kind.lower()+'_meta.json')).read_text(encoding='utf-8'))
        core_palette = {'VanPaint': '#C5C5C6', 'TruckPaint': '#2B3963', 'Cargo': '#DCD5C9',
                        'Chassis': '#343940', 'Tire': '#2C2A2F'}
        for suffix, color in core_palette.items():
            mat_name = 'M_TrafficTrucks_'+suffix
            if mat_name in meta['materials']:
                check(meta['materials'][mat_name] == color, name+': approved palette mismatch '+suffix)
        existing = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
        objects = {o.name: o for o in bpy.data.objects if o not in existing}
        all_objects.update(objects)
        expected_names = {name, name+'_Body', name+'_Interior', name+'_Glass'} | {name+'_Wheel_'+s for s in spec['wheels']}
        check(set(objects) == expected_names, name+': exact hierarchy names differ '+str(sorted(set(objects)^expected_names)))
        if not expected_names <= set(objects):
            continue
        root = objects[name]
        check(root.type == 'EMPTY' and root.parent is None, name+': root type/parent')
        check(root.matrix_world.translation.length < .0001, name+': root pivot is not origin')
        check(max(abs(s-1) for s in root.scale) < .0001, name+': root scale')
        points, counts = {}, {}
        for n, obj in objects.items():
            if obj is root:
                continue
            check(obj.type == 'MESH' and obj.parent == root, n+': must be direct mesh child')
            check(not obj.modifiers and not obj.animation_data, n+': unexpected modifier/animation')
            check(max(abs(s-1) for s in obj.scale) < .0001, n+': unapplied scale')
            check(max(abs(a) for a in obj.rotation_euler) < .0001, n+': unexpected rotation')
            obj.data.calc_loop_triangles()
            counts[n] = len(obj.data.loop_triangles)
            check(all(len(p.vertices) == 3 for p in obj.data.polygons), n+': non-triangle face')
            check(all(t.area > 1e-10 for t in obj.data.loop_triangles), n+': degenerate triangle')
            points[n] = [obj.matrix_world@v.co for v in obj.data.vertices]
            pivot = obj.matrix_world.translation
            check((pivot-Vector(meta['parts'][n]['pivot_m'])).length < .001, n+': metadata pivot mismatch')
            if '_Wheel_' not in n:
                check(pivot.length < .001, n+': non-wheel origin must equal root')
        total = sum(counts.values())
        check(spec['budget'][0] <= total <= spec['budget'][1], name+': triangle budget '+str(total))
        check(total == stats['total_triangles'][kind], name+': stats triangle total mismatch')
        for n, value in counts.items():
            check(value == stats['object_triangles'][n], n+': stats triangle count mismatch')
        # Aynaların dış X uzantısı ölçüye katılmaz; Y ve Z dış zarfı aynalardan etkilenmez.
        envelope = [p for values in points.values() for p in values if abs(p.x) <= spec['dimensions'][1]/2+.0005]
        lo, hi = bounds(envelope)
        dims = [hi.y-lo.y, hi.x-lo.x, hi.z-lo.z]
        check(all(abs(a-b) <= .05 for a, b in zip(dims, spec['dimensions'])), name+': dimensions '+str(dims))
        check(abs(lo.z) <= .01, name+': ground plane')
        wheel_info, contacts = {}, []
        for suffix, y in spec['wheels'].items():
            n = name+'_Wheel_'+suffix
            obj = objects[n]
            pos = obj.matrix_world.translation
            sign = 1 if suffix.endswith('L') else -1
            expected_pivot = Vector((sign*spec['x'], y, spec['radius']))
            check((pos-expected_pivot).length < .001, n+': wrong axle pivot')
            low, high = bounds(points[n])
            check(abs(low.z) <= .01, n+': wheel bottom Z')
            check(((low+high)/2-pos).length < .001, n+': pivot not wheel centre')
            radius = (high.z-low.z)/2
            check(abs(radius-spec['radius']) < .001, n+': wheel radius')
            motion = meta['parts'][n]['motion']
            check(motion['axis'] == [1, 0, 0], n+': spin axis not X')
            contacts.extend(p for p in points[n] if p.z < low.z+.001)
            wheel_info[n] = {'pivot_m': list(pos), 'bottom_z_m': low.z, 'radius_m': radius}
        low, high = bounds(contacts)
        centre = (low+high)/2
        check(abs(centre.x) < .001 and abs(centre.y) < .001 and abs(centre.z) < .01, name+': contact rectangle not centred at root')
        axle_count = len({round(o['pivot_m'][1], 3) for o in wheel_info.values()})
        check(axle_count == (3 if kind == 'BoxTruck' else 2), name+': axle count')
        check(len(wheel_info) == (6 if kind == 'BoxTruck' else 4), name+': wheel count')
        check(meta['coordinates']['forward'] == '-Y' and meta['coordinates']['up'] == '+Z', name+': coordinate contract')
        # Cam ve şoför gerçekten ayrı malzeme taşıyor; kapalı yük bölümünde yan cam yok.
        interior = objects[name+'_Interior']
        driver_faces = [p for p in interior.data.polygons if 'Driver' in interior.data.materials[p.material_index].name]
        check(len(driver_faces) >= 40, name+': driver head/torso missing')
        glass = objects[name+'_Glass']
        check(all('Glass' in m.name for m in glass.data.materials), name+': non-glass material in glass object')
        if kind == 'PanelVan':
            check(all(p.y < -.40 for p in points[name+'_Glass']), name+': cargo-side glazing forbidden')
        # Raw FBX, yeniden import sırasında Blender'ın yaptığı dönüşümlerden bağımsız.
        raw = raw_fbx_colors(path)
        for mat, color in meta['materials'].items():
            c = color.lstrip('#')
            expected = [srgb_to_linear(int(c[i:i+2], 16)/255) for i in (0, 2, 4)]
            actual = raw.get(mat)
            check(actual is not None, name+': material missing '+mat)
            if actual is not None:
                check(max(abs(a-b) for a, b in zip(actual, expected)) < 1e-5, name+': non-linear FBX color '+mat)
            colors[kind+'/'+mat] = {'hex': color, 'expected_linear': expected, 'fbx_diffuse': actual}
        assets[kind] = {'triangles': total, 'object_triangles': counts, 'dimensions_LWH_m': dims,
                        'wheels': wheel_info, 'axle_count': axle_count, 'contact_rectangle_centre_m': list(centre)}
    digest = canonical(all_objects)
    check(digest == stats['canonical_geometry_sha256'], 'FBX roundtrip canonical geometry differs from build')
    history = stats.get('build_history', [])
    source = hashlib.sha256((ROOT/'tools/blender/build_traffic_trucks.py').read_bytes().replace(b'\r\n', b'\n')).hexdigest()
    deterministic = (len(history) >= 2 and history[-1]['geometry_sha256'] == history[-2]['geometry_sha256']
                     and history[-1]['source_sha256'] == history[-2]['source_sha256'] == source)
    check(deterministic, 'Two consecutive identical current-source builds required')
    rounds, render_info = [], {}
    for folder in [RENDERS]+sorted(RENDERS.glob('round_*')):
        complete = True
        for view in VIEWS:
            path = folder/(view+'.png')
            size = list(struct.unpack('!II', path.read_bytes()[16:24])) if path.exists() else None
            check(size == [1920, 1080], str(path.relative_to(ROOT))+': missing/wrong resolution')
            complete &= size == [1920, 1080]
            render_info[str(path.relative_to(RENDERS))] = size
        if folder != RENDERS and complete:
            rounds.append(folder.name)
    check(len(rounds) >= 2, 'Two complete visual rounds required')
    latest = RENDERS/f"round_{stats['last_build']['round']:02d}"
    manifest_path = latest/'build_manifest.json'
    check(manifest_path.exists(), 'Current render manifest missing')
    if manifest_path.exists():
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
        check(manifest['source_sha256'] == source, 'Current renders were made by a different source')
        check(manifest['canonical_geometry_sha256'] == digest, 'Current renders show different geometry')
        for view in VIEWS:
            filename = view+'.png'
            for path in (latest/filename, RENDERS/filename):
                if path.exists():
                    check(hashlib.sha256(path.read_bytes()).hexdigest() == manifest['images_sha256'].get(filename),
                          str(path.relative_to(ROOT))+': render/manifest mismatch')
    brightness = {}
    for view in VIEWS[:8]:
        path = RENDERS/(view+'.png')
        if not path.exists():
            continue
        im = bpy.data.images.load(str(path), check_existing=False)
        pixels = np.empty(im.size[0]*im.size[1]*4, dtype=np.float32)
        im.pixels.foreach_get(pixels)
        rgb = pixels.reshape(im.size[1], im.size[0], 4)[::-1, :, :3]
        corner = float(rgb[2, 2].mean()*255)
        brightness[view] = corner
        check(corner > 150, view+': dark-frame guard')
        bpy.data.images.remove(im)
    review = OUT/'visual_review_trucks.md'
    check(review.exists(), 'Visual review missing')
    if review.exists():
        text = review.read_text(encoding='utf-8')
        check('round_01' in text and 'round_02' in text, 'Visual round notes missing')
    check((OUT/'TrafficTrucks.blend').exists(), 'Blend source missing')
    after = snapshot()
    check(before == after, 'Verifier changed repository files')
    report = {'verified': not errors, 'errors': errors, 'assets': assets, 'colors': colors,
              'canonical_geometry_sha256': digest, 'two_consecutive_builds_identical': bool(deterministic),
              'rounds': rounds, 'renders': render_info, 'repository_snapshot_unchanged': before == after,
              'snapshot_file_count': len(before), 'render_corner_brightness_255': brightness}
    path = Path(tempfile.gettempdir())/('verify_traffic_trucks_'+hashlib.sha256(str(ROOT).encode()).hexdigest()[:12]+'.json')
    path.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2), flush=True)
    print('REPORT', path, flush=True)
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


