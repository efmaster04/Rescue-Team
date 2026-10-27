"""Boş sahneye FBX yükleyerek minibüsü bağımsız denetler; hata exit 1."""
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
OUT = ROOT/'Export/Minivan'
MESH_NAMES = {'SM_Minivan_'+suffix for suffix in (
    'Body','Interior','Glass','Wheel_FL','Wheel_FR','Wheel_RL','Wheel_RR',
    'Door_FL','Door_FR','Door_Slide_R','Tailgate','RackSuitcase_01','RackSuitcase_02')}
MARKER_NAMES = {'TowPoint'} | {f'Seat_{i:02d}' for i in range(1,7)}


def points(obj):
    return [obj.matrix_world @ v.co for v in obj.data.vertices]


def bounds(vertices):
    return [Vector(tuple(min(p[i] for p in vertices) for i in range(3))),
            Vector(tuple(max(p[i] for p in vertices) for i in range(3)))]


def tree(obj):
    verts = points(obj)
    obj.data.calc_loop_triangles()
    faces = [tuple(t.vertices) for t in obj.data.loop_triangles]
    return BVHTree.FromPolygons(verts,faces,all_triangles=True,epsilon=1e-7),verts,faces


def penetration(source_tree, target_tree, vertices, faces, touched):
    # BVH yüzey kesişimleri + üçgen iç noktaları. Tam içerideki bileşenler için
    # bütün verteksler de örneklenir; yalnız bbox testi geçerli sayılmaz.
    samples = list(vertices)
    for index in sorted(touched):
        a,b,c = (vertices[i] for i in faces[index])
        for u in range(7):
            for v in range(7-u):
                samples.append(a+(b-a)*(u/6)+(c-a)*(v/6))
    maximum = 0.0
    worst = None
    inside_samples = 0
    for p in samples:
        nearest,normal,index,distance = target_tree.find_nearest(p)
        if nearest is None or distance < 1e-6:
            continue
        if (p-nearest).dot(normal) < -1e-6:
            # Normal testi adaydır; çift taraflı ışınla içeride oluşu doğrulanır.
            # Birden çok kapalı kaporta bileşeni üst üste gelebilir: en yakın
            # yüzeyin gerisinde olup iki yönde yüzeye ulaşmak yeterli değildir;
            # üç farklı ışında tek sayıda yüzey geçişi aranır.
            votes = 0
            for direction in (Vector((1,.371,.217)).normalized(),
                              Vector((.253,1,.419)).normalized(),
                              Vector((.173,.281,1)).normalized()):
                origin = p.copy()
                hits = 0
                for _ in range(80):
                    loc,_,_,_ = target_tree.ray_cast(origin,direction,20)
                    if loc is None:
                        break
                    hits += 1
                    origin = loc+direction*1e-5
                votes += hits % 2
            if votes >= 2:
                inside_samples += 1
                if distance > maximum:
                    maximum,worst = distance,list(p)
    return {'max_penetration_m':round(maximum,7),'worst_point_m':worst,
            'inside_samples':inside_samples,'sample_count':len(samples)}


def set_pose(meta, closed, fraction):
    for name,part in meta['parts'].items():
        motion = part.get('motion')
        if not motion or motion['type'] not in ('hinge','slide'):
            continue
        obj = bpy.data.objects[name]
        original = closed[name]
        if motion['type'] == 'hinge':
            pivot = original.translation
            rotation = Matrix.Rotation(math.radians(motion['open_angle_deg'])*fraction,4,Vector(part['axis']))
            obj.matrix_world = Matrix.Translation(pivot) @ rotation @ Matrix.Translation(-pivot) @ original
        else:
            delta = Vector(motion['outward_direction'])*motion['outward_clearance_m']*min(4*fraction,1)
            delta += Vector(motion['slide_direction'])*motion['slide_distance_m']*max(0,(fraction-.25)/.75)
            obj.matrix_world = Matrix.Translation(delta) @ original
    bpy.context.view_layer.update()


def srgb_to_linear(v):
    return v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4


def raw_fbx_colors(path):
    # Export edilmiş gerçek DiffuseColor değerleri; Blender render renk dönüşümünden bağımsız.
    from io_scene_fbx import parse_fbx
    root,version = parse_fbx.parse(str(path))
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


def canonical_geometry(meshes):
    # FBX yeniden indeksleme ve yüz köşe sırası değişimlerine dayanıklı imza.
    data = []
    for obj in sorted(meshes,key=lambda o:o.name):
        verts = points(obj)
        obj.data.calc_loop_triangles()
        triangles = []
        for tri in obj.data.loop_triangles:
            corners = sorted(tuple(math.floor(float(v)*10000+.371) for v in verts[i]) for i in tri.vertices)
            mat = obj.data.materials[tri.material_index].name
            triangles.append([corners,mat])
        data.append([obj.name,sorted(triangles)])
    return hashlib.sha256(json.dumps(data,separators=(',',':')).encode()).hexdigest()


def verify():
    errors = []
    def check(condition,message):
        if not condition:
            errors.append(message)
    path = OUT/'SM_Minivan.fbx'
    meta = json.loads((OUT/'minivan_meta.json').read_text(encoding='utf-8'))
    manifest = json.loads((OUT/'build_manifest.json').read_text(encoding='utf-8'))
    history = json.loads((OUT/'build_history.json').read_text(encoding='utf-8'))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
    bpy.context.view_layer.update()
    objects = {o.name:o for o in bpy.context.scene.objects}
    meshes = [o for o in objects.values() if o.type=='MESH']
    check(set(objects) == MESH_NAMES|MARKER_NAMES|{'SM_Minivan'},'FBX object names/hierarchy differ from specification')
    check({o.name for o in meshes} == MESH_NAMES,'Mesh name set mismatch')
    if not MESH_NAMES|MARKER_NAMES|{'SM_Minivan'} <= set(objects):
        raise ValueError('Required objects absent: '+str((MESH_NAMES|MARKER_NAMES|{'SM_Minivan'})-set(objects)))
    root = objects['SM_Minivan']
    check(root.parent is None and root.type=='EMPTY','Root must be a parentless empty')
    check(root.matrix_world.translation.length<1e-5,'Root pivot is not ground rectangle centre')
    for obj in objects.values():
        if obj != root:
            check(obj.parent==root,obj.name+': wrong parent')
    counts = {}
    all_points = []
    for obj in meshes:
        obj.data.calc_loop_triangles()
        counts[obj.name] = len(obj.data.loop_triangles)
        all_points.extend(points(obj))
        check(not obj.modifiers,obj.name+': unexpected modifier')
        check(all(not p.use_smooth for p in obj.data.polygons),obj.name+': flat shading lost')
    total = sum(counts.values())
    check(4000<=total<=7000,f'Triangle budget: {total}')
    check(counts==manifest['object_triangles'],'FBX/build per-object triangles differ')
    # Ölçüden aynalar, raf/bavul ve çeki gözü çıkarılır; maskeler dar fiziksel bölgeler.
    envelope = []
    excluded = {'mirrors':0,'rack':0,'tow':0}
    for name in ('SM_Minivan_Body','SM_Minivan_Door_FL','SM_Minivan_Door_FR',
                 'SM_Minivan_Door_Slide_R','SM_Minivan_Tailgate'):
        for p in points(objects[name]):
            if name == 'SM_Minivan_Body' and abs(p.x)>.96 and -2.08<p.y<-1.70 and 1.18<p.z<1.53:
                excluded['mirrors'] += 1
            elif name == 'SM_Minivan_Body' and p.z>2.0001:
                excluded['rack'] += 1
            elif name == 'SM_Minivan_Body' and p.y>2.501 and .40<p.z<.60:
                excluded['tow'] += 1
            else:
                envelope.append(p)
    lo,hi = bounds(envelope)
    dimensions = {'length_m':hi.y-lo.y,'width_m':hi.x-lo.x,'ground_to_roof_m':hi.z}
    for field,expected in [('length_m',5),('width_m',1.9),('ground_to_roof_m',2)]:
        check(abs(dimensions[field]-expected)<=.05001,f'Body {field}: {dimensions[field]}')
    check(max(p.z for p in all_points)<=2.45001,'Rack and luggage exceed 2.45 m')
    pivots = {}
    wheel_centres = []
    for name,part in meta['parts'].items():
        obj = objects[name]
        pivot = obj.matrix_world.translation
        low,high = bounds(points(obj))
        check((pivot-Vector(part['pivot_m'])).length<1e-4,name+': pivot differs from metadata')
        detail = {'position_m':list(pivot),'bbox_min_m':list(low),'bbox_max_m':list(high)}
        if 'Wheel_' in name:
            check((pivot-(low+high)/2).length<1e-4,name+': wheel pivot not at bbox centre')
            check(abs(low.z)<=.01,name+': wheel contact not Z=0')
            check(.70<=high.z-low.z<=.80,name+': wheel diameter out of range')
            check(part['axis']==[1,0,0],name+': axle axis must be X')
            wheel_centres.append(pivot)
        elif name.endswith(('Door_FL','Door_FR')):
            check(abs(pivot.y-low.y)<.012,name+': pivot not on front hinge edge')
            side = 1 if name.endswith('FL') else -1
            check(abs(pivot.x-(high.x if side==1 else low.x))<.03,name+': hinge not on outer face')
            check(low.z<=pivot.z<=high.z,name+': hinge height outside door')
            check(part['axis']==[0,0,1] and part['motion']['open_angle_deg']==-side*70,name+': wrong hinge axis/limit')
        elif name.endswith('Door_Slide_R'):
            check(abs(pivot.y-low.y)<.012 and abs(pivot.z-low.z)<.012,name+': pivot not front/bottom corner')
            check(high.x<0,name+': sliding door is not vehicle-right (-X)')
            check(part['motion']['slide_direction']==[0,1,0],name+': slide must travel rearward (+Y)')
        elif name.endswith('Tailgate'):
            check(abs(pivot.z-high.z)<.01,name+': hinge not at top edge')
            check(abs(pivot.x)<.01,name+': hinge must be centred')
            check(part['axis']==[1,0,0] and part['motion']['open_angle_deg']==85,name+': wrong tailgate opening')
        elif 'RackSuitcase' in name:
            check(abs(pivot.z-low.z)<.012,name+': suitcase pivot not at base')
            check(abs(pivot.x-(low.x+high.x)/2)<.025 and abs(pivot.y-(low.y+high.y)/2)<.025,
                  name+': suitcase pivot not base centre')
        pivots[name] = detail
    centre = sum(wheel_centres,Vector())/4
    check(abs(centre.x)<1e-5 and abs(centre.y)<1e-5,'Root not centred between wheel contacts')
    marker_stats = {}
    interior_low,interior_high = map(Vector,meta['measurement']['interior_bounds_m'])
    for name in MARKER_NAMES:
        p = objects[name].matrix_world.translation
        check((p-Vector(meta['markers'][name])).length<1e-4,name+': metadata mismatch')
        if name.startswith('Seat'):
            check(all(interior_low[i]<p[i]<interior_high[i] for i in range(3)),name+': outside interior')
            # Oturma işareti gerçek minder yüzeyine dayanır.
            seat_tree,_,_ = tree(objects['SM_Minivan_Interior'])
            loc,_,_,distance = seat_tree.ray_cast(p+Vector((0,0,.08)),Vector((0,0,-1)),.20)
            check(loc is not None and abs(loc.z-p.z)<.025,name+': no cushion below marker')
        else:
            check(abs(p.x)<.005 and 2.50<p.y<2.67 and .42<p.z<.62,'TowPoint not centred behind rear bumper')
        marker_stats[name] = list(p)
    check(objects['Seat_01'].matrix_world.translation.x>0,'Driver must be left (+X)')
    colors = raw_fbx_colors(path)
    color_stats = {}
    for name,color in meta['materials'].items():
        # FBX DiffuseColor hex'in lineer karşılığıdır (Blender varsayılanı, Unity Linear URP).
        expected = [srgb_to_linear(int(color[i:i+2],16)/255) for i in (1,3,5)]
        actual = colors.get(name)
        check(actual is not None and max(abs(a-b) for a,b in zip(actual,expected))<1e-5,name+': FBX linear color mismatch')
        mat = bpy.data.materials.get(name)
        imported = list(mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value[:3]) if mat else None
        check(imported is not None and max(abs(a-b) for a,b in zip(imported,expected))<1e-5,name+': reimported color mismatch')
        color_stats[name] = {'expected_hex':color,'fbx_diffuse_rgb':actual,'imported_rgb':imported}
    check(len(colors)==len(meta['materials']),'Unexpected FBX materials')
    glass_material = bpy.data.materials['M_Minivan_Glass']
    glass_alpha = glass_material.node_tree.nodes['Principled BSDF'].inputs['Alpha'].default_value
    check(abs(glass_alpha-meta['glass_alpha'])<1e-5,'Glass alpha not preserved in FBX')
    check(all(node.type!='TEX_IMAGE' for mat in bpy.data.materials if mat.use_nodes for node in mat.node_tree.nodes),
          'Texture nodes found; flat color asset required')
    # Son iki gerçek başarılı build aynı kaynak ve geometriyle çalışmış olmalı.
    deterministic = len(history)>=2 and history[-1]['geometry_sha256']==history[-2]['geometry_sha256']
    deterministic = deterministic and history[-1]['source_sha256']==history[-2]['source_sha256']
    check(deterministic,'Two consecutive identical builds have not yet completed')
    check(history[-1]['geometry_sha256']==manifest['geometry_sha256'],'Stale build manifest')
    # Satir sonu normalize edilir: git autocrlf temiz kopyada LF'yi CRLF'ye cevirince ayni kod "degismis" sayilmasin
    check(history[-1]['source_sha256']==hashlib.sha256((ROOT/'tools/blender/build_minivan.py').read_bytes().replace(b'\r\n',b'\n')).hexdigest(),
          'Build script changed after last successful build')
    fbx_digest = canonical_geometry(meshes)
    check(fbx_digest==manifest.get('canonical_geometry_sha256'),'FBX geometry differs from build geometry')
    # Açık limitlere ek olarak hareket yolu on adımda kontrol edilir.
    closed = {name:obj.matrix_world.copy() for name,obj in objects.items()}
    body_tree,body_verts,body_faces = tree(objects['SM_Minivan_Body'])
    collision_stats = {}
    moving = [name for name,p in meta['parts'].items() if p.get('motion') and p['motion']['type'] in ('hinge','slide')]
    for fraction in (.1,.2,.25,.3,.4,.5,.6,.7,.8,.9,1.0):
        set_pose(meta,closed,fraction)
        for name in moving:
            door_tree,verts,faces = tree(objects[name])
            overlap = door_tree.overlap(body_tree)
            forward = penetration(door_tree,body_tree,verts,faces,{a for a,b in overlap})
            reverse = penetration(body_tree,door_tree,body_verts,body_faces,{b for a,b in overlap}) if overlap else {'max_penetration_m':0}
            depth = max(forward['max_penetration_m'],reverse['max_penetration_m'])
            collision_stats.setdefault(name,[]).append({'open_fraction':fraction,'bvh_overlap_triangle_pairs':len(overlap),
                'max_penetration_m':depth,'door_into_body':forward,'body_into_door':reverse})
            check(depth<=.02001,f'{name} pose {fraction}: penetration {depth:.5f} m > 0.02 m')
    set_pose(meta,closed,0)
    render_stats = {}
    for view in ('front','rear','left','right','threequarter_front_left','threequarter_rear_right_open','interior_top'):
        png = OUT/'renders'/(view+'.png')
        resolution = list(struct.unpack('!II',png.read_bytes()[16:24])) if png.exists() else None
        render_stats[view] = resolution
        check(resolution==[1920,1080],view+': missing or wrong render resolution')
    rounds = sorted(p.name for p in (OUT/'renders').glob('round_*') if (p/'compare_concept.png').exists())
    check(len(rounds)>=3,'At least three archived visual comparison rounds required')
    check((OUT/'Minivan.blend').exists(),'Blend source missing')
    stats = {'verified':not errors,'errors':errors,'total_triangles':total,'object_triangles':counts,
             'dimensions':dimensions,'dimension_exclusions':excluded,'overall_height_m':max(p.z for p in all_points),
             'pivots':pivots,'markers':marker_stats,'colors':color_stats,
             'geometry_sha256':manifest['geometry_sha256'],'fbx_canonical_sha256':fbx_digest,
             'two_consecutive_builds_identical':bool(deterministic),
             'renders':render_stats,'archived_visual_rounds':rounds,'glass_alpha':glass_alpha,
             'collision_method':'BVHTree triangle overlap, barycentric/vertex samples, nearest surface depth + 3-ray inside vote; reciprocal test at 11 poses.',
             'open_pose_tests':collision_stats,'last_build':history[-1]}
    # Izlenen dosya yazilmaz: sonuc gecici klasore gider (stats.json'u yalniz build_minivan.py yazar)
    report_path = Path(tempfile.gettempdir())/('verify_minivan_'+hashlib.sha256(str(ROOT).encode()).hexdigest()[:12]+'.json')
    report_path.write_text(json.dumps(stats,indent=2),encoding='utf-8')
    print('REPORT '+str(report_path),flush=True)
    print(json.dumps({'total_triangles':total,'object_triangles':counts,'dimensions':dimensions,
                      'deterministic':bool(deterministic),'errors':errors},indent=2),flush=True)
    if errors:
        print('VERIFY FAILED',flush=True)
        sys.exit(1)
    print('VERIFY OK',flush=True)


if __name__=='__main__':
    try:
        verify()
    except Exception:
        traceback.print_exc()
        print('VERIFY FAILED',flush=True)
        sys.exit(1)
