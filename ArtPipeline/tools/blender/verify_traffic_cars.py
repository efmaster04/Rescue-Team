"""Ba??ms?z FBX denetimi; yazma yaln?z stdout ve sistem TEMP raporu.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/verify_traffic_cars.py
"""
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import sys
import tempfile
import traceback
sys.dont_write_bytecode = True
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'Export/Traffic'

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



def collision(a,b):
    overlap=a.tree.overlap(b.tree)
    # Kapsanmış kapalı hacmi de sınar; yalnız yüzey kesişimi yeterli değildir.
    d1,w1=penetration(a,b,{i for i,_ in overlap})
    d2,w2=penetration(b,a,{j for _,j in overlap})
    return {'max_penetration_m':round(max(d1,d2),6),'surface_pairs':len(overlap),'worst_point_m':w1 if d1>=d2 else w2}



EXPECTED = {'Sedan':(4.50,1.80,1.45,.31,1.37,3000,5000),
            'Hatchback':(4.00,1.75,1.50,.31,1.22,3000,5000),
            'Pickup':(5.30,1.90,1.85,.37,1.62,3500,5500)}


def snapshot():
    # Git kullanılmaz; bütün çalışma ağacının içerik özeti ve zaman damgası korunur.
    return {str(p.relative_to(ROOT)):(p.stat().st_size,p.stat().st_mtime_ns,hashlib.sha256(p.read_bytes()).hexdigest())
            for p in ROOT.rglob('*') if p.is_file() and '.git' not in p.parts}


def verify():
    print('VERIFY START: read-only repository snapshot',flush=True)
    before=snapshot()
    errors=[]
    def check(ok,message):
        if not ok: errors.append(message)
    stats=json.loads((OUT/'stats_cars.json').read_text(encoding='utf-8'))
    all_objects={}
    counts={}
    report={'vehicles':{},'errors':errors}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.use_auto_save_temporary_files=False
    for kind,(length,width,height,radius,axle,minimum,maximum) in EXPECTED.items():
        print('VERIFY IMPORT',kind,flush=True)
        meta=json.loads((OUT/(kind.lower()+'_meta.json')).read_text(encoding='utf-8'))
        p='SM_'+kind
        path=OUT/(p+'.fbx')
        scene=bpy.data.scenes.new('VERIFY_'+kind+'_Empty')
        bpy.context.window.scene=scene
        bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
        bpy.context.view_layer.update()
        objects={o.name:o for o in scene.objects}
        suffixes=['Body','Interior','Glass','Wheel_FL','Wheel_FR','Wheel_RL','Wheel_RR']
        if kind=='Pickup': suffixes+=['Hay_01','Hay_02']
        expected={p}|{p+'_'+s for s in suffixes}
        check(set(objects)==expected,p+': hierarchy names '+str(set(objects)^expected))
        if expected-set(objects): raise ValueError(p+': missing objects')
        root=objects[p]
        check(root.type=='EMPTY' and root.parent is None,p+': root type/parent')
        check(root.matrix_world.translation.length<1e-5,p+': root origin')
        check(meta['coordinates']=={'up':'+Z','forward':'-Y','left':'+X','right':'-X','length_axis':'Y'},p+': coordinate convention')
        points={}
        for name,obj in objects.items():
            check(all(abs(v-1)<1e-5 for v in obj.matrix_world.to_scale()),name+': scale')
            if obj==root: continue
            check(obj.type=='MESH' and obj.parent==root,name+': mesh/parent')
            check(obj.matrix_world.to_quaternion().angle<1e-4,name+': rest rotation')
            check(not obj.modifiers,name+': modifiers')
            check(all(not poly.use_smooth for poly in obj.data.polygons),name+': smooth shading')
            check(all(len(poly.vertices)==3 and poly.area>1e-10 for poly in obj.data.polygons),name+': nontriangular/degenerate polygon')
            obj.data.calc_loop_triangles()
            counts[name]=len(obj.data.loop_triangles)
            points[name]=[obj.matrix_world@v.co for v in obj.data.vertices]
            check((obj.matrix_world.translation-Vector(meta['parts'][name]['pivot_m'])).length<.0002,name+': metadata pivot')
        total=sum(counts[n] for n in points)
        check(minimum<=total<=maximum,p+f': triangle budget {total}')
        envelope=[]
        mirror=meta['mirror_exclusion']
        excluded=0
        for name,verts in points.items():
            for v in verts:
                if (name==p+'_Body' and abs(v.x)>mirror['abs_x_above_m'] and
                    mirror['y_range_m'][0]<v.y<mirror['y_range_m'][1] and mirror['z_range_m'][0]<v.z<mirror['z_range_m'][1]):
                    excluded+=1
                else: envelope.append(v)
        lo,hi=bounds(envelope)
        dims=[hi.y-lo.y,hi.x-lo.x,hi.z-lo.z]
        check(excluded>0,p+': mirrors absent')
        for actual,want,key in zip(dims,(length,width,height),('length','width','height')):
            check(abs(actual-want)<=.05001,p+f': {key} {actual:.6f}, expected {want}')
            check(meta['measurement'][key+'_m']==want,p+': metadata dimension drift')
        check(abs(lo.z)<.01,p+': ground bound')
        contacts=[]
        for end,ay in (('F',-axle),('R',axle)):
            for side,sign in (('L',1),('R',-1)):
                name=p+'_Wheel_'+end+side
                obj=objects[name]; low,high=bounds(points[name]); pivot=obj.matrix_world.translation
                wanted=Vector((sign*(width/2-.135),ay,radius))
                check((pivot-wanted).length<.0002,name+': expected axle position')
                check((pivot-(low+high)/2).length<.0002,name+': off-centre pivot')
                check(abs(low.z)<.01,name+': ground contact')
                check(abs((high.z-low.z)/2-radius)<.0002,name+': measured radius')
                check(meta['parts'][name]['motion']['axis']==[1,0,0],name+': axis')
                contacts.append(Vector((pivot.x,pivot.y,0)))
        check((sum(contacts,Vector())/4-root.matrix_world.translation).length<.0002,p+': contact rectangle centre')
        check(meta['wheel_axis']==[1,0,0] and abs(meta['wheel_radius_m']-radius)<1e-8,p+': wheel metadata')
        colors=raw_fbx_colors(path)
        for mat,value in colors.items():
            check(mat in meta['materials'],p+': material not documented '+mat)
            color=meta['materials'].get(mat,'#000000')
            linear=[srgb_to_linear(int(color[i:i+2],16)/255) for i in (1,3,5)]
            check(max(abs(a-b) for a,b in zip(value,linear))<2e-6,p+': FBX linear DiffuseColor '+mat)
        check(meta['paint_material'] in colors and meta['glass_material'] in colors,p+': paint/glass material')
        variants=meta['paint_variants']
        check(len(variants)==(1 if kind=='Pickup' else 4),p+': variant count')
        check(all(re.fullmatch(r'#[0-9A-Fa-f]{6}',v['srgb_hex']) for v in variants),p+': invalid variant hex')
        check(len({v['name'] for v in variants})==len(variants),p+': duplicate variants')
        default=next((v for v in variants if v['name']==meta['default_paint']),None)
        check(default is not None and default['srgb_hex']==meta['materials'][meta['paint_material']],p+': default paint')
        # Basit sürücü ve koltukların iç mesh içinde korunmasını materyal/topoloji ile sınar.
        interior=objects[p+'_Interior']
        used={m.name.split('.')[0] for m in interior.data.materials}
        check({'M_Traffic_Driver','M_Traffic_Seat','M_Traffic_Interior'}<=used,p+': interior materials')
        glass=objects[p+'_Glass']
        check(all(m.name.split('.')[0]=='M_Traffic_Glass' for m in glass.data.materials),p+': glass separation')
        car_report={'triangles':total,'dimensions_lwh_m':dims,'mirror_vertices_excluded':excluded,'fbx_diffuse_colors':colors}
        if kind=='Pickup':
            bed=meta['bed']
            body=Shape(objects[p+'_Body'],objects[p+'_Body'].matrix_world)
            hays=[]
            for record in bed['hay']:
                name=record['name']; obj=objects[name]
                low,high=bounds(points[name]); pivot=obj.matrix_world.translation
                check((pivot-Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z))).length<.0002,name+': bottom-centre pivot')
                check(abs(low.z-bed['floor_z_m'])<=.01001,name+': bed contact')
                check(max(abs((high-low)[i]-record['dimensions_xyz_m'][i]) for i in range(3))<.01,name+': hay dimensions')
                check(low.x>=bed['inner_x_m'][0]-.01 and high.x<=bed['inner_x_m'][1]+.01 and
                      low.y>=bed['inner_y_m'][0]-.01 and high.y<=bed['inner_y_m'][1]+.01,name+': outside bed')
                shape=Shape(obj,obj.matrix_world)
                hit=collision(shape,body)
                check(hit['max_penetration_m']<=.01001,name+': body penetration '+str(hit))
                # Gerçek FBX gövdesinde pivotun altında kasa tabanı bulunmalıdır.
                loc,_,_,_=body.tree.ray_cast(Vector((pivot.x,pivot.y,low.z+.02)),Vector((0,0,-1)),.10)
                check(loc is not None and abs(loc.z-low.z)<=.01,name+': no physical bed under hay')
                hays.append(shape)
                car_report[name]=hit
            hit=collision(*hays)
            check(hit['max_penetration_m']<=.01001,p+': hay/hay penetration '+str(hit))
            car_report['hay_pair']=hit
        report['vehicles'][kind]=car_report
        print('VERIFY ASSET',kind,total,'triangles; dimensions',dims,flush=True)
        all_objects.update(objects)
    check(counts==stats['object_triangles'],'FBX/build triangle counts differ')
    geometry=canonical(all_objects)
    check(geometry==stats['canonical_geometry_sha256'],'Reimported geometry digest differs')
    source=hashlib.sha256((ROOT/'tools/blender/build_traffic_cars.py').read_bytes().replace(b'\r\n',b'\n')).hexdigest()
    history=stats['build_history']
    check(len(history)>=2,'Need two consecutive builds')
    if len(history)>=2:
        a,b=history[-2:]
        check(a['source_sha256']==b['source_sha256']==source,'Consecutive builds must use current normalized source')
        check(a['geometry_sha256']==b['geometry_sha256']==stats['geometry_sha256'],'Consecutive build geometry SHA256 differs')
        check(a['canonical_geometry_sha256']==b['canonical_geometry_sha256']==geometry,'Consecutive canonical hashes differ')
    # İki görsel turun tüm teslimleri 1920x1080 olmalı ve karanlık kare olmamalı.
    views=[kind.lower()+'_'+view for kind in EXPECTED for view in ('threequarter_front','left','rear')]
    views+=['lineup','paint_variants','scale_with_minivan','compare_sedan','compare_hatchback','compare_pickup','compare_lineup','compare_minivan_style']
    for round_id in (1,2):
        for view in views:
            path=OUT/'renders/cars'/f'round_{round_id:02d}'/(view+'.png')
            check(path.exists(),'Missing render '+str(path.relative_to(ROOT)))
            if path.exists():
                data=path.read_bytes()
                check(data[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('!II',data[16:24])==(1920,1080),'Render resolution '+str(path))
                img=bpy.data.images.load(str(path),check_existing=False)
                check(sum(img.pixels[:3])/3>.58,'Dark frame '+str(path))
                bpy.data.images.remove(img)
    check((OUT/'visual_review_cars.md').exists(),'Missing visual review')
    after=snapshot()
    check(before==after,'Verifier modified repository files')
    report.update(object_triangles=counts,canonical_geometry_sha256=geometry,repository_unchanged=before==after,
                  repository_files_checked=len(before),ok=not errors)
    result=Path(tempfile.gettempdir())/'verify_traffic_cars_report.json'
    result.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2),flush=True)
    print('REPORT',result,flush=True)
    if errors: raise ValueError('\n'.join(errors))
    print('VERIFY OK',geometry,flush=True)


if __name__=='__main__':
    try: verify()
    except Exception:
        traceback.print_exc()
        print('VERIFY FAILED',flush=True)
        sys.exit(1)
