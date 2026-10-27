"""FBX kabul denetimi. Hiçbir repo dosyasını yazmaz; rapor yalnız stdout ve TEMP.
Blender --python-exit-code 1 ile çalıştırılmalıdır. stats.json yalnız build tarafından yazılır.
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
OUT = ROOT/'Export/Ambulance'
STRETCHER_OUT = ROOT/'Export/Stretcher'
P, S = 'SM_Ambulance', 'SM_Stretcher'
MESH_NAMES = [P+'_'+suffix for suffix in ('Body','Interior','Glass','Door_RL','Door_RR','Step_Rear','Wheel_FL','Wheel_FR','Wheel_RL','Wheel_RR','Lightbar','Light_RearL','Light_RearR')]
MARKERS = ['PatientSlot_01','PatientSlot_02','LoadPoint','SirenPoint']
VIEWS = ('front','rear','left','right','threequarter_front_left','threequarter_rear_open','interior_top','scale_with_minivan','compare_front','compare_rear','compare_side','compare_rear_open')
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


def box_shape(lo,hi):
    vertices=[Vector((x,y,z)) for x in (lo[0],hi[0]) for y in (lo[1],hi[1]) for z in (lo[2],hi[2])]
    # Dışa yönlü altı yüz; iki üçgen/yüz.
    quads=[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]
    shape=object.__new__(Shape)
    shape.verts=vertices
    shape.faces=[t for a,b,c,d in quads for t in ((a,b,c),(a,c,d))]
    shape.tree=BVHTree.FromPolygons(vertices,shape.faces,all_triangles=True)
    return shape


def collision(a,b):
    overlap=a.tree.overlap(b.tree)
    # Kapsanmış kapalı hacmi de sınar; yalnız yüzey kesişimi yeterli değildir.
    d1,w1=penetration(a,b,{i for i,_ in overlap})
    d2,w2=penetration(b,a,{j for _,j in overlap})
    return {'max_penetration_m':round(max(d1,d2),6),'surface_pairs':len(overlap),'worst_point_m':w1 if d1>=d2 else w2}


def subshape(obj, predicate):
    verts=[obj.matrix_world@v.co for v in obj.data.vertices]
    obj.data.calc_loop_triangles()
    faces,used,subverts=[],{},[]
    for tri in obj.data.loop_triangles:
        mat=obj.data.materials[tri.material_index].name
        if mat[-4:-3]=='.' and mat[-3:].isdigit(): mat=mat[:-4]
        center=(verts[tri.vertices[0]]+verts[tri.vertices[1]]+verts[tri.vertices[2]])/3
        if predicate(mat,center):
            face=[]
            for idx in tri.vertices:
                if idx not in used:
                    used[idx]=len(subverts)
                    subverts.append(verts[idx])
                face.append(used[idx])
            faces.append(tuple(face))
    shape=object.__new__(Shape)
    shape.verts=subverts
    shape.faces=faces
    shape.tree=BVHTree.FromPolygons(subverts,faces,all_triangles=True,epsilon=1e-7)
    return shape


def verify():
    before=snapshot()
    errors=[]
    def check(condition,message):
        if not condition: errors.append(message)
    meta=json.loads((OUT/'ambulance_meta.json').read_text(encoding='utf-8'))
    smeta=json.loads((STRETCHER_OUT/'stretcher_meta.json').read_text(encoding='utf-8'))
    stats=json.loads((OUT/'stats.json').read_text(encoding='utf-8'))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.use_auto_save_temporary_files=False
    bpy.ops.import_scene.fbx(filepath=str(OUT/(P+'.fbx')),use_anim=False)
    ambulance={o.name:o for o in bpy.context.scene.objects}
    expected=set(MESH_NAMES+MARKERS+[P])
    check(set(ambulance)==expected,'Ambulance hierarchy names differ: '+str(set(ambulance)^expected))
    if expected-set(ambulance): raise ValueError('Missing required ambulance objects')
    # Sedye de bağımsız boş sahneye yüklenir; ilk sahne bellekte saklanır.
    ascene=bpy.context.scene
    sscene=bpy.data.scenes.new('VERIFY_Stretcher_Empty')
    bpy.context.window.scene=sscene
    bpy.ops.import_scene.fbx(filepath=str(STRETCHER_OUT/(S+'.fbx')),use_anim=False)
    stretcher={o.name:o for o in sscene.objects}
    check(set(stretcher)=={S,S+'_Mesh','Handle_Front','Handle_Rear','PatientAnchor'},'Stretcher hierarchy names differ')
    bpy.context.window.scene=ascene
    bpy.context.view_layer.update()
    # Diğer sahnenin transformları da değerlendirilir.
    bpy.context.window.scene=sscene
    bpy.context.view_layer.update()
    bpy.context.window.scene=ascene
    objects={**ambulance,**stretcher}
    for group,root_name,mesh_names in ((ambulance,P,set(MESH_NAMES)),(stretcher,S,{S+'_Mesh'})):
        root=group[root_name]
        check(root.parent is None and root.type=='EMPTY',root_name+': root type/parent')
        check(root.matrix_world.translation.length<1e-5,root_name+': root pivot not at ground origin')
        for name,obj in group.items():
            if name!=root_name: check(obj.parent==root,name+': incorrect parent')
            check(obj.type==('MESH' if name in mesh_names else 'EMPTY'),name+': incorrect type')
            check(all(abs(s-1)<1e-5 for s in obj.matrix_world.to_scale()),name+': non-unit scale')
            if obj.type=='MESH':
                check(obj.matrix_world.to_euler().to_quaternion().angle<1e-4,name+': rest rotation')
                check(not obj.modifiers,name+': modifier remains')
                check(all(not p.use_smooth for p in obj.data.polygons),name+': flat normals missing')
    meshes={n:o for n,o in objects.items() if o.type=='MESH'}
    points={n:[o.matrix_world@v.co for v in o.data.vertices] for n,o in meshes.items()}
    counts={}
    for name,obj in meshes.items():
        obj.data.calc_loop_triangles()
        counts[name]=len(obj.data.loop_triangles)
    total=sum(counts[n] for n in MESH_NAMES)
    stotal=counts[S+'_Mesh']
    check(6000<=total<=10500,f'Ambulance triangles {total}')
    check(600<=stotal<=1000,f'Stretcher triangles {stotal}')
    check(counts==stats['object_triangles'],'FBX/build per-part triangles differ')
    lights={P+'_Lightbar',P+'_Light_RearL',P+'_Light_RearR'}
    envelope=[]
    mirror_count=0
    for name in MESH_NAMES:
        for pt in points[name]:
            if name==P+'_Body' and abs(pt.x)>1.01 and -2.3<pt.y<-1.9 and 1.5<pt.z<1.9:
                mirror_count+=1
            else: envelope.append(pt)
    lo,hi=bounds(envelope)
    dims={'length_m':hi.y-lo.y,'width_m':hi.x-lo.x,'height_m':max(pt.z for n in MESH_NAMES if n not in lights for pt in points[n]),
          'beacon_top_m':max(pt.z for n in lights for pt in points[n])}
    check(mirror_count>0,'No mirrors found for dimension exclusion')
    for key,value in (('length_m',6.0),('width_m',2.0),('height_m',2.65)):
        check(abs(dims[key]-value)<=.05001,f'{key}: {dims[key]:.5f}')
    check(dims['beacon_top_m']<=2.80001,'Beacon height above 2.80 m')
    check(abs(lo.z)<.01,'Vehicle lowest point is not ground')
    pivots={}
    contacts=[]
    for name,part in meta['parts'].items():
        obj=objects[name]
        pivot=obj.matrix_world.translation
        low,high=bounds(points[name])
        check((pivot-Vector(part['pivot_m'])).length<.0002,name+': metadata pivot differs')
        pivots[name]=list(pivot)
        if '_Wheel_' in name:
            check((pivot-(low+high)/2).length<.002,name+': wheel pivot off centre')
            check(abs(low.z)<.01,name+': wheel ground contact')
            check(abs(high.z/2-.43)<.002,name+': wheel radius')
            check(part['motion']['axis']==[1,0,0],name+': axle direction')
            contacts.append(Vector((pivot.x,pivot.y,0)))
        if '_Door_R' in name:
            sign=1 if name.endswith('L') else -1
            check(abs(pivot.x-sign*.92)<.002 and abs(pivot.y-2.94)<.002,name+': outer rear hinge location')
            check(abs(pivot.z-.694)<.002,name+': hinge foot')
            motion=part['motion']
            check(motion['axis']==[0,0,-sign] and motion['range_deg']==[0,260] and motion['work_pose_deg']==100,name+': door motion contract')
        if name==P+'_Step_Rear':
            check((pivot-Vector((0,3,.34))).length<.002,'Step hinge location')
            check(part['motion']['axis']==[-1,0,0] and part['motion']['range_deg']==[0,90] and part['motion']['work_pose_deg']==90,'Step motion contract')
    check((sum(contacts,Vector())/4).length<.001,'Root is not centre of four contact points')
    # Empties: konumlar ve yerel +Y baş yönü gerçek FBX transformundan okunur.
    for marker_meta in (meta['markers'],smeta['markers']):
        for name,record in marker_meta.items():
            obj=objects[name]
            check((obj.matrix_world.translation-Vector(record['position_m'])).length<.0002,name+': marker position')
            direction=(obj.matrix_world.to_3x3()@Vector((0,1,0))).normalized()
            check((direction-Vector((0,-1,0))).length<.0002,name+': local +Y must face -Y')
    interior=Shape(objects[P+'_Interior'],objects[P+'_Interior'].matrix_world)
    bed_gaps={}
    for name in ('PatientSlot_01','PatientSlot_02'):
        origin=objects[name].matrix_world.translation
        hit,_,_,_=interior.tree.ray_cast(origin,Vector((0,0,-1)),.5)
        gap=origin.z-hit.z if hit is not None else -1
        bed_gaps[name]=gap
        check(abs(gap-.05)<=.02,name+': marker is not 5 cm above mattress')
    # Gerçek arka tampon yalnız gövdenin alçak arka bölümünden ölçülür.
    bumper_y=max(pt.y for pt in points[P+'_Body'] if .32<pt.z<.59 and pt.y>2.6)
    load=objects['LoadPoint'].matrix_world.translation
    check(abs(load.z)<1e-5 and abs(load.x)<1e-5,'LoadPoint is not on ground centreline')
    check(abs((load.y-bumper_y)-1.2)<=.05,'LoadPoint rear bumper distance')
    check((objects['SirenPoint'].matrix_world.translation-objects[P+'_Lightbar'].matrix_world.translation).length<.001,'SirenPoint lightbar centre')
    # Kapalı kapılar açıklığı kaplar: merkez ve çerçeve boşlukları 1 cm altında.
    dl,dh=bounds(points[P+'_Door_RL'])
    rl,rh=bounds(points[P+'_Door_RR'])
    seam=dl.x-rh.x
    check(0<=seam<=.01001,f'Closed centre seam {seam}')
    check(abs(dh.x-.918)<.03 and abs(rl.x+.918)<.03,'Rear side door coverage')
    check(abs(dl.z-.69)<=.01 and abs(dh.z-2.55)<=.01,'Rear door vertical coverage')
    # Açıklık üzerinde 10 mm aralıklı ışın taraması: cam yüzleri de kapı parçası.
    closed=[Shape(objects[n],objects[n].matrix_world) for n in (P+'_Door_RL',P+'_Door_RR')]
    uncovered=[]
    for ix in range(182):
        x=-.905+ix*.01
        if abs(x)<.0041: continue
        for iz in range(185):
            z=.70+iz*.01
            if all(shape.tree.ray_cast(Vector((x,3.2,z)),Vector((0,-1,0)),.5)[0] is None for shape in closed):
                uncovered.append([round(x,3),round(z,3)])
    check(not uncovered,'Closed rear doors leave holes: '+str(uncovered[:5]))
    shapes={n:Shape(objects[n],objects[n].matrix_world) for n in MESH_NAMES}
    posed={}
    collisions={}
    for name in (P+'_Door_RL',P+'_Door_RR',P+'_Step_Rear'):
        obj=objects[name]
        motion=meta['parts'][name]['motion']
        pivot=obj.matrix_world.translation
        matrix=Matrix.Translation(pivot)@Matrix.Rotation(math.radians(motion['work_pose_deg']),4,Vector(motion['axis']))@Matrix.Translation(-pivot)@obj.matrix_world
        posed[name]=Shape(obj,matrix)
        result=collision(posed[name],shapes[P+'_Body'])
        collisions[name+'_vs_body']=result
        check(result['max_penetration_m']<=.02001,name+': open pose body penetration '+str(result))
    step_lo,step_hi=bounds(posed[P+'_Step_Rear'].verts)
    check(step_hi.z-step_lo.z<.05 and step_hi.y>3.25,'Open step is not horizontal/rearward')
    for name in (P+'_Door_RL',P+'_Door_RR'):
        low,high=bounds(posed[name].verts)
        check(high.y>3.70,name+': door opens forward/inward')
    # Sedye boyutları, 4 ayrı ayağın gerçek temas noktaları, taşıma sapları.
    slo,shi=bounds(points[S+'_Mesh'])
    check(abs((shi.y-slo.y)-2.10)<.002,'Stretcher pole length')
    for x in (-.255,.255):
        for y in (-.66,.66):
            near=[pt.z for pt in points[S+'_Mesh'] if abs(pt.x-x)<.018 and abs(pt.y-y)<.018]
            check(near and abs(min(near))<.01,f'Stretcher foot at {x,y} not grounded')
    canvas=[]
    sobj=objects[S+'_Mesh']
    for poly in sobj.data.polygons:
        if 'Canvas' in sobj.data.materials[poly.material_index].name:
            canvas.extend(sobj.matrix_world@sobj.data.vertices[i].co for i in poly.vertices)
    clo,chi=bounds(canvas)
    check(abs(chi.x-clo.x-.55)<.002 and abs(chi.y-clo.y-1.85)<.002,'Canvas dimensions')
    sshape=Shape(sobj,sobj.matrix_world)
    anchor=objects['PatientAnchor'].matrix_world.translation
    hit,_,_,_=sshape.tree.ray_cast(anchor,Vector((0,0,-1)),.3)
    check(hit is not None and abs(anchor.z-hit.z-.05)<.02,'PatientAnchor above canvas')
    for name,y in (('Handle_Front',-.97),('Handle_Rear',.97)):
        check((objects[name].matrix_world.translation-Vector((0,y,.13))).length<.002,name+': grip midpoint')
    loaded=Shape(sobj,Matrix.Translation(load)@sobj.matrix_world)
    for name,shape in posed.items():
        result=collision(loaded,shape)
        collisions['loaded_stretcher_vs_'+name]=result
        check(result['max_penetration_m']<=.01001,'Stretcher at LoadPoint collides with '+name)
    # Tüm geçiş boyunca süpürülmüş 0.55 x 0.50 m kutu (yatak yüzey kotundan başlar).
    corridor=box_shape((-.275,.10,.99),(.275,3.65,1.49))
    for name in (P+'_Body',P+'_Interior',P+'_Door_RL',P+'_Door_RR',P+'_Step_Rear'):
        result=collision(corridor,posed.get(name,shapes[name]))
        collisions['loading_corridor_vs_'+name]=result
        check(result['surface_pairs']==0 and result['max_penetration_m']<.001,'Loading box blocked by '+name)
    # Şoför ve hemşire: toplam <= 3000 üçgen; koltuk, direksiyon, yatak ve gövde ile <= 2 cm iç içe geçme.
    iobj=objects[P+'_Interior']
    crew_mats={'M_Ambulance_'+n for n in ('Suit','StripeYellow','StripeSilver','Skin','SkinLight','HairDark','HairBrown','EyeWhite','Pupil','Mouth')}
    driver=subshape(iobj,lambda m,c: m in crew_mats and c.y<-.9)
    nurse=subshape(iobj,lambda m,c: m in crew_mats and c.y>=-.9)
    crew_triangles=len(driver.faces)+len(nurse.faces)
    check(len(driver.faces)>0 and len(nurse.faces)>0,'Missing driver or nurse geometry in Interior')
    check(crew_triangles<=3000,f'Crew triangles {crew_triangles} exceed 3000')
    seat_shape=subshape(iobj,lambda m,c: m not in crew_mats and c.y>-1.72 and (m=='M_Ambulance_Seat' or (m=='M_Ambulance_Trim' and c.z<1.05)))
    steering_shape=subshape(iobj,lambda m,c: m not in crew_mats and c.y<-1.50 and c.z>1.20 and m in ('M_Ambulance_Tire','M_Ambulance_Trim'))
    bed_shape=subshape(iobj,lambda m,c: m not in crew_mats and c.y>.15 and c.z>.68 and abs(c.x)<.90)
    for cname,cshape in (('driver',driver),('nurse',nurse)):
        for tname,tshape in (('seat',seat_shape),('steering',steering_shape),('bed',bed_shape),('body',shapes[P+'_Body'])):
            result=collision(cshape,tshape)
            collisions[f'{cname}_vs_{tname}']=result
            check(result['max_penetration_m']<=.02001,f'{cname} vs {tname} penetration {result}')
    colors={}
    for path,record in ((OUT/(P+'.fbx'),meta),(STRETCHER_OUT/(S+'.fbx'),smeta)):
        raw=raw_fbx_colors(path)
        for name,color in record['materials'].items():
            c=color.lstrip('#')
            expected_color=[srgb_to_linear(int(c[i:i+2],16)/255) for i in (0,2,4)]
            actual=raw.get(name)
            check(actual is not None,name+': missing FBX DiffuseColor')
            if actual is not None: check(max(abs(a-b) for a,b in zip(actual,expected_color))<1e-5,name+': FBX is not expected linear color')
            colors[name]={'hex':color,'expected_linear':expected_color,'fbx_diffuse':actual}
    fbx_digest=canonical(objects)
    check(fbx_digest==stats['canonical_geometry_sha256'],'FBX/build geometry hash differs')
    history=stats['build_history']
    source=hashlib.sha256((ROOT/'tools/blender/build_ambulance.py').read_bytes().replace(b'\r\n',b'\n')).hexdigest()
    deterministic=len(history)>=2 and history[-1]['geometry_sha256']==history[-2]['geometry_sha256'] and history[-1]['source_sha256']==history[-2]['source_sha256']==source
    check(deterministic,'Two consecutive identical current-source builds required')
    renders={}
    for view in VIEWS+('crew_front','crew_rear_open'):
        path=OUT/'renders'/(view+'.png')
        size=list(struct.unpack('!II',path.read_bytes()[16:24])) if path.exists() else None
        renders[view]=size
        check(size==[1920,1080],view+': missing/wrong-resolution render')
    for view in ('crew_front','crew_rear_open'):
        r5=OUT/'renders/round_05'/(view+'.png')
        check(r5.exists() and list(struct.unpack('!II',r5.read_bytes()[16:24]))==[1920,1080],'round_05/'+view+': missing/wrong-resolution render')
    spng=STRETCHER_OUT/'renders/stretcher_threequarter.png'
    check(spng.exists() and struct.unpack('!II',spng.read_bytes()[16:24])==(1920,1080),'Stretcher render')
    rounds=[]
    for folder in sorted((OUT/'renders').glob('round_*')):
        if all((folder/(v+'.png')).exists() for v in VIEWS): rounds.append(folder.name)
    check(len(rounds)>=3,'Three complete visual rounds required')
    check((OUT/'Ambulance.blend').exists() and (OUT/'visual_review.md').exists(),'Blend/visual review missing')
    after=snapshot()
    check(before==after,'Verifier changed a repository file')
    report={'verified':not errors,'errors':errors,'total_triangles':total,'crew_triangles':crew_triangles,'stretcher_triangles':stotal,'object_triangles':counts,
            'dimensions':dims,'stretcher_dimensions_m':list(shi-slo),'pivots':pivots,'bed_marker_gaps_m':bed_gaps,
            'closed_door_seam_m':seam,'uncovered_door_samples':uncovered[:10],'collisions':collisions,'colors':colors,
            'canonical_geometry_sha256':fbx_digest,'two_consecutive_builds_identical':bool(deterministic),
            'renders':renders,'rounds':rounds,'repository_snapshot_unchanged':before==after,'snapshot_file_count':len(before)}
    path=Path(tempfile.gettempdir())/('verify_ambulance_'+hashlib.sha256(str(ROOT).encode()).hexdigest()[:12]+'.json')
    path.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('REPORT',path,flush=True)
    print(json.dumps(report,indent=2),flush=True)
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
