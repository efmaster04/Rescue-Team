"""Bölüm 1 FBX'lerini boş sahnede yeniden yükler; ölçüm hatası exit 1."""
import json
import hashlib
import math
import struct
import sys
import tempfile
from pathlib import Path

import bpy
from mathutils import Vector, Quaternion
from mathutils.bvhtree import BVHTree

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Export/Bolum1_Ucurum'
REQUIRED_MARKERS={'PlayerSpawn','RecoveryTruckSlot','TrafficLane_EastBound','TrafficLane_WestBound',
                  'TrafficSpawn_West','TrafficSpawn_East','TrafficDespawn_West','TrafficDespawn_East',
                  'FallRespawnPlane','LedgeZone','MinivanPlacement','TowTruckPlacement'}
CORE={'SM_B1_Ground','SM_B1_Road','SM_B1_RoadMarkings','SM_B1_Guardrail','SM_B1_Vegetation','SM_B1_Backdrop'}
EXPECTED=CORE|{f'SM_B1_LooseRock_{i:02}' for i in range(1,9)}
EXPECTED|={f'SM_B1_Prop_Suitcase_{i:02}' for i in (1,2)}
EXPECTED|={f'SM_B1_Prop_Bag_{i:02}' for i in (3,4,5)}
EXPECTED|={f'SM_B1_Prop_Chevron_{side}_{i}' for side in ('West','East') for i in (1,2)}
EXPECTED.add('SM_B1_Prop_FallingRock')


def bvh(obj):
    obj.data.calc_loop_triangles()
    return BVHTree.FromPolygons([obj.matrix_world@v.co for v in obj.data.vertices],
                               [tuple(t.vertices) for t in obj.data.loop_triangles],all_triangles=True)


def hit(tree,x,y):
    p,n,index,d=tree.ray_cast(Vector((x,y,500)),Vector((0,0,-1)),1000)
    return p,n


def measure_ramp(objects,data):
    # Üretici kodundan bağımsız: sabit işaret hattında bütün mekân mesh'leri.
    ramp=[Vector(p) for p in data['geometry_probes']['ramp']]
    delta=ramp[-1]-ramp[0]
    length=math.hypot(delta.x,delta.y)
    forward=Vector((delta.x/length,delta.y/length,0))
    lateral=Vector((-forward.y,forward.x,0))
    trees={name:bvh(obj) for name,obj in objects.items() if obj.type=='MESH'}
    def cast(point):
        hits=[]
        for name,tree in trees.items():
            p,n,face,d=tree.ray_cast(Vector((point.x,point.y,500)),Vector((0,0,-1)),1000)
            if p is not None:
                hits.append({'point':list(p),'mesh':name,'triangle':face,
                             'slope_degrees':math.degrees(math.acos(min(1,abs(n.z))))})
        return max(hits,key=lambda h:h['point'][2]) if hits else None
    distances=sorted(set([-2+i*.05 for i in range(math.floor((length+4)/.05)+1)]+[0.,length,length+2]))
    lines=[]
    for offset in (-.7,0.,.7):
        samples=[]
        for s in distances:
            p=ramp[0]+forward*s+lateral*offset
            sample={'distance_m':s,'xy':[p.x,p.y],'hit':cast(p)}
            samples.append(sample)
        steps=[]
        for a,b in zip(samples,samples[1:]):
            if a['hit'] is None or b['hit'] is None: continue
            dz=b['hit']['point'][2]-a['hit']['point'][2]
            ds=b['distance_m']-a['distance_m']
            steps.append({'distance_m':b['distance_m'],'xy':b['xy'],'height_change_cm':abs(dz)*100,
                          'grade_degrees':math.degrees(math.atan2(dz,ds)),
                          'from_mesh':a['hit']['mesh'],'to_mesh':b['hit']['mesh'],
                          'triangle':b['hit']['triangle']})
        changes=[{'distance_m':b['distance_m'],'xy':b['xy'],
                  'grade_change_degrees':abs(b['grade_degrees']-a['grade_degrees'])}
                 for a,b in zip(steps,steps[1:])]
        lines.append({'offset_m':offset,'samples':samples,'steps':steps,
                      'worst_step':max(steps,key=lambda s:s['height_change_cm']),
                      'worst_slope':max((s['hit'] for s in samples if s['hit']),key=lambda h:h['slope_degrees']),
                      'worst_grade_change':max(changes,key=lambda c:c['grade_change_degrees']),
                      'missing_samples':sum(s['hit'] is None for s in samples)})
    return {'spacing_m':.05,'extension_m':2.,'length_m':length,'lines':lines}


def measure_ramp_bottom_corner(obj,data):
    # Üreticiden bağımsız, yeniden içe alınmış gerçek üçgenler. Üst kenar
    # şevi (-1.38..-.9) ve yürüyüş şeridi birlikte, iki eksende taranır.
    # -1.38'in dışındaki uçurum eteği yürünebilir üst yüzeye dahil değildir.
    ramp=[Vector(p) for p in data['geometry_probes']['ramp']]
    delta=ramp[-1]-ramp[0]
    length=math.hypot(delta.x,delta.y)
    forward=Vector((delta.x/length,delta.y/length,0))
    lateral=Vector((-forward.y,forward.x,0))
    tree=bvh(obj)
    worst={'longitudinal':{'height_change_cm':0.},'transverse':{'height_change_cm':0.}}
    maximum={'slope_degrees':0.}
    missing=0; samples=0
    # Kaydırılmış ikinci ızgara, kesit/köşegen üstüne denk gelen ışınların
    # tesadüfen bir kusuru atlamasını önler. Aralıklar en fazla 5 cm'dir.
    for phase in (0.,.025):
        distances=sorted(set([-1.5,1.5]+[-1.5+phase+i*.05 for i in range(61)
                                                       if -1.5<-1.5+phase+i*.05<1.5]))
        offsets=sorted(set([-1.375,.875]+[-1.375+phase+i*.05 for i in range(46)
                                                       if -1.375<-1.375+phase+i*.05<.875]))
        grid={}
        for i,s in enumerate(distances):
            for j,t in enumerate(offsets):
                q=ramp[-1]+forward*s+lateral*t
                # Yakın ışın başlangıcı, float32'de 500 m çıkarımının cm
                # sınırındaki yuvarlama hatasını ölçüme taşımamasını sağlar.
                p,n,face,_=tree.ray_cast(Vector((q.x,q.y,ramp[-1].z+10)),Vector((0,0,-1)),30)
                samples+=1
                if p is None:
                    missing+=1
                    continue
                entry={'point':list(p),'distance_from_end_m':s,'offset_m':t,'triangle':face,
                       'slope_degrees':math.degrees(math.acos(min(1.,abs(n.z))))}
                grid[i,j]=entry
                if entry['slope_degrees']>maximum['slope_degrees']: maximum=entry
                for axis,key in [('longitudinal',(i-1,j)),('transverse',(i,j-1))]:
                    previous=grid.get(key)
                    if previous is None: continue
                    dz=100*abs(p.z-previous['point'][2])
                    if dz>worst[axis]['height_change_cm']:
                        worst[axis]={'height_change_cm':dz,'from':previous,'to':entry}
    # Değişmeyen set: 22 cm yükseliş / 48 cm en + rampanın boyuna eğimi.
    bank_slope=math.degrees(math.atan(math.hypot(delta.z/length,.22/.48)))
    return {'spacing_m':.05,'end_range_m':[-1.5,1.5],'lateral_range_m':[-1.375,.875],
            'samples':samples,'missing_samples':missing,'worst_steps':worst,
            'worst_slope':maximum,'original_bank_slope_degrees':bank_slope}


def verify_ramp_bottom_corner(check,objects,data):
    measured=measure_ramp_bottom_corner(objects['SM_B1_Ground'],data)
    check(measured['missing_samples']==0,'Rampa alt köşesinde boş ışın')
    for axis,step in measured['worst_steps'].items():
        check(step['height_change_cm']<=2.001,
              f"Rampa alt köşesi {axis}: 5 cm örnekte {step['height_change_cm']:.4f} cm > 2 cm")
    check(measured['worst_slope']['slope_degrees']<=measured['original_bank_slope_degrees']+.01,
          'Rampa alt köşesinin üst yüzeyi mevcut kenar şevinden dik')
    return measured


def verify_ramp(check,objects,data):
    measured=measure_ramp(objects,data)
    # Tüm mesh ölçümü saklanır. Collider dışı süslerin etkisi ayrıca görünür;
    # hiçbir mesh sessizce kabul ölçümünden çıkarılmaz.
    collision=measure_ramp({name:obj for name,obj in objects.items() if name not in
                           ('SM_B1_Vegetation','SM_B1_Backdrop','SM_B1_RoadMarkings')},data)
    def summary(report):
        return {**{k:v for k,v in report.items() if k!='lines'},
                'lines':[{k:v for k,v in line.items() if k not in ('samples','steps')} for line in report['lines']]}
    for line in measured['lines']:
        prefix=f"Rampa {line['offset_m']:+.1f} m hattı: "
        check(line['missing_samples']==0,prefix+f"{line['missing_samples']} boş ışın")
        check(line['worst_step']['height_change_cm']<=2.001,
              prefix+f"5 cm örnekte {line['worst_step']['height_change_cm']:.4f} cm sıçrama > 2 cm")
        check(line['worst_slope']['slope_degrees']<=25.001,
              prefix+f"{line['worst_slope']['slope_degrees']:.4f} derece eğim > 25")
        check(all(abs(s['grade_degrees'])<=25.001 for s in line['steps']),prefix+'boyuna eğim > 25 derece')
    ramp=[Vector(p) for p in data['geometry_probes']['ramp']]
    delta=ramp[-1]-ramp[0]
    forward=Vector((delta.x,delta.y,0)).normalized()
    lateral=Vector((-forward.y,forward.x,0))
    ground=bvh(objects['SM_B1_Ground']); road=bvh(objects['SM_B1_Road'])
    joints=[]
    # Üst ağız yandan asfalta girer: temas dikişi ve üç enine yaklaşım hattı.
    for s in (-1.5,-1.,-.5):
        p=ramp[0]+forward*s+lateral*1.6
        g,_=hit(ground,p.x,p.y); r,_=hit(road,p.x,p.y)
        gap=abs(g.z-r.z) if g is not None and r is not None else None
        joints.append({'end':'road','xy':[p.x,p.y],'gap_cm':gap*100 if gap is not None else None})
        check(gap is not None and gap<=.02001,f'Rampa/asfalt dikişi: {gap} m')
        previous=None
        for i in range(59):
            p=ramp[0]+forward*s+lateral*(-.7+i*.05)
            hits=[h for h in (hit(ground,p.x,p.y)[0],hit(road,p.x,p.y)[0]) if h is not None]
            check(bool(hits),'Rampa yol ağzında boş ışın')
            if not hits: continue
            z=max(h.z for h in hits)
            if previous is not None: check(abs(z-previous)<=.02001,'Rampa yol ağzında enine basamak > 2 cm')
            previous=z
    for offset in (-.7,0,.7):
        p=ramp[-1]+lateral*offset
        g,_=hit(ground,p.x,p.y)
        # Aynı bitiş kesitindeki mevcut düz patika, rampanın dış yanında.
        reference=ramp[-1]-lateral*2.5
        path,_=hit(ground,reference.x,reference.y)
        gap=abs(g.z-path.z) if g is not None and path is not None else None
        joints.append({'end':'path','xy':[p.x,p.y],'gap_cm':gap*100 if gap is not None else None})
        check(gap is not None and gap<=.02001,f'Rampa/patika dikişi: {gap} m')
    return {'all_meshes':summary(measured),'collision_surfaces':summary(collision),'junctions':joints}


def verify_minivan(check,data):
    entry=data['markers'].get('MinivanPlacement')
    root=bpy.data.objects.get('SM_Minivan')
    check(entry is not None,'MinivanPlacement kaydı eksik')
    check(root is not None and root.type=='EMPTY','SM_Minivan kökü eksik')
    check('PH_Minivan' not in bpy.data.objects,'Eski PH_Minivan kalmış')
    if entry is None or root is None: return {}
    check(entry==data['placements'].get('MinivanPlacement'),'MinivanPlacement kayıtları farklı')
    check(entry.get('coordinate_system','').startswith('Blender:'),'Minibüs koordinat sistemi')
    check(entry.get('euler_order')=='XYZ','Minibüs Euler sırası')
    check((root.location-Vector(entry['position'])).length<1e-5,'Minibüs kök konumu')
    quat=Quaternion(entry['rotation_quaternion_wxyz'])
    check(root.matrix_world.to_quaternion().rotation_difference(quat).angle<1e-4,'Minibüs quaternion')
    check(all(abs(math.degrees(a)-b)<1e-4 for a,b in zip(root.rotation_euler,entry['rotation_euler_degrees'])),
          'Minibüs Euler derece')
    check(all(abs(v-1)<1e-5 for v in root.scale),'Minibüs ölçeği')
    direction=root.matrix_world.to_3x3()@Vector((0,-1,0))
    pitch=math.degrees(math.atan2(-direction.z,math.hypot(direction.x,direction.y)))
    check(25<=pitch<=30 and direction.y<0,'Minibüs burnu -Y yönünde 25–30 derece sarkmalı')
    marker=bpy.data.objects.get('MinivanPlacement')
    check(marker is not None and marker.matrix_world.to_quaternion().rotation_difference(quat).angle<1e-4,
          'Yerleşim empty dönüşü farklı')
    meshes=[obj for obj in root.children_recursive if obj.type=='MESH']
    tree=bvh(bpy.data.objects['SM_B1_Ground'])
    maximum=0.
    worst=None
    samples=0
    # Üçgen içlerini de 2 cm aralıklarla örnekle: yalnız köşe/AABB kontrolü değil.
    for obj in meshes:
        obj.data.calc_loop_triangles()
        verts=[obj.matrix_world@v.co for v in obj.data.vertices]
        for tri in obj.data.loop_triangles:
            a,b,c=[verts[i] for i in tri.vertices]
            steps=max(1,math.ceil(max((a-b).length,(a-c).length,(b-c).length)/.02))
            for i in range(steps+1):
                for j in range(steps-i+1):
                    p=a+(b-a)*(i/steps)+(c-a)*(j/steps)
                    surface,_=hit(tree,p.x,p.y)
                    samples+=1
                    depth=max(0.,surface.z-p.z) if surface is not None else 0.
                    if depth>maximum:
                        maximum=depth
                        worst={'object':obj.name,'point':list(p),'surface':list(surface)}
    check(maximum<=.020001,f'Minibüs zemine gömülmesi: {maximum:.6f} m > 0.02 m')
    contacts={}
    for side in ('RL','RR'):
        obj=bpy.data.objects.get('SM_Minivan_Wheel_'+side)
        check(obj is not None,'Arka teker eksik: '+side)
        if obj is None: continue
        points=[obj.matrix_world@v.co for v in obj.data.vertices]
        bottom=min(p.z for p in points)
        lowest=[p for p in points if p.z<bottom+1e-5]
        gaps=[]
        for p in lowest:
            surface,normal=hit(tree,p.x,p.y)
            check(surface is not None and abs(surface.z+9)<.03 and normal.z>.99,
                  side+': arka tekerin alt noktası patika üstünde değil')
            if surface is not None: gaps.append(p.z-surface.z)
        check(bool(gaps) and max(abs(g) for g in gaps)<=.030001,side+': arka teker teması ±3 cm dışında')
        contacts[side]={'bottom_z_m':bottom,'surface_gaps_m':gaps,'bottom_points': [list(p) for p in lowest]}
    # Kaynak FBX'le kök uzayında tüm parçaları karşılaştır: kapılar, bagaj, koltuklar korunmalı.
    inverse=root.matrix_world.inverted()
    actual={obj.name:{'matrix':inverse@obj.matrix_world,
                     'vertices':[v.co.copy() for v in obj.data.vertices] if obj.type=='MESH' else None}
            for obj in root.children_recursive}
    source=ROOT/'Export/Minivan/SM_Minivan.fbx'
    check(entry.get('source_sha256')==hashlib.sha256(source.read_bytes()).hexdigest(),'Minibüs kaynak SHA256 farklı')
    before=set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(source),use_anim=False)
    imported=set(bpy.data.objects)-before
    source_root=next(obj for obj in imported if obj.parent is None)
    source_inverse=source_root.matrix_world.inverted()
    expected={obj.name.removesuffix('.001'):obj for obj in source_root.children_recursive}
    check(set(actual)==set(expected),'Minibüs hiyerarşisi kaynakla farklı')
    for name,obj in expected.items():
        if name not in actual: continue
        delta=actual[name]['matrix']-(source_inverse@obj.matrix_world)
        check(max(abs(v) for row in delta for v in row)<1e-4,name+': kapalı kaynak pozu farklı')
        if obj.type=='MESH':
            verts=actual[name]['vertices']
            check(verts is not None and len(verts)==len(obj.data.vertices) and
                  all((a-b.co).length<1e-5 for a,b in zip(verts,obj.data.vertices)),name+': kaynak mesh farklı')
    return {'nose_down_degrees':pitch,'max_penetration_m':maximum,'worst_penetration':worst,
            'surface_sample_spacing_m':.02,'surface_samples':samples,'rear_wheel_contacts':contacts}


def verify_towtruck(check,data):
    entry=data['markers'].get('TowTruckPlacement')
    root=bpy.data.objects.get('SM_TowTruck')
    check(entry is not None,'TowTruckPlacement kaydı eksik')
    check(root is not None and root.type=='EMPTY','SM_TowTruck kökü eksik')
    check('PH_TowTruck' not in bpy.data.objects,'Eski PH_TowTruck kalmış')
    if entry is None or root is None: return {}
    check(entry==data['placements'].get('TowTruckPlacement'),'TowTruckPlacement kayıtları farklı')
    check(entry.get('coordinate_system','').startswith('Blender:'),'Çekici koordinat sistemi')
    check(entry.get('asset')=='Export/TowTruck/SM_TowTruck.fbx' and entry.get('root_object')==root.name,
          'Çekici kaynak/kök kaydı')
    check(entry.get('euler_order')=='XYZ','Çekici Euler sırası')
    check((root.location-Vector(entry['position'])).length<1e-5,'Çekici kök konumu')
    quat=Quaternion(entry['rotation_quaternion_wxyz'])
    check(root.matrix_world.to_quaternion().rotation_difference(quat).angle<1e-4,'Çekici quaternion')
    check(all(abs(math.degrees(a)-b)<1e-4 for a,b in zip(root.rotation_euler,entry['rotation_euler_degrees'])),
          'Çekici Euler derece')
    check(all(abs(v-1)<1e-5 for v in root.scale) and entry.get('scale')==[1,1,1],'Çekici ölçeği')
    direction=root.matrix_world.to_3x3()@Vector((0,-1,0))
    check(direction.x>.99 and abs(direction.y)<1e-5,'Çekici burnu doğuya (+X) bakmalı')
    marker=bpy.data.objects.get('TowTruckPlacement')
    check(marker is not None and marker.matrix_world.to_quaternion().rotation_difference(quat).angle<1e-4,
          'Çekici yerleşim empty dönüşü farklı')
    meshes=[obj for obj in root.children_recursive if obj.type=='MESH']
    ground=bpy.data.objects['SM_B1_Ground']
    ground_tree=bvh(ground)
    road_tree=bvh(bpy.data.objects['SM_B1_Road'])
    rail_tree=bvh(bpy.data.objects['SM_B1_Guardrail'])
    # Banketin gerçek 1 x 5 m çakıl şeritlerini mesh'ten ayır; üretici fonksiyonunu kullanma.
    vertices=[ground.matrix_world@v.co for v in ground.data.vertices]
    faces=[]
    for tri in ground.data.loop_triangles:
        pts=[vertices[i] for i in tri.vertices]
        if (ground.material_slots[tri.material_index].material.name=='M_Gravel'
                and all(5-1e-5<=p.x<=25+1e-5 for p in pts)
                and abs(max(p.x for p in pts)-min(p.x for p in pts)-1)<1e-4
                and 4.5<max(p.y for p in pts)-min(p.y for p in pts)<6):
            faces.append(tuple(tri.vertices))
    check(len(faces)==40,'Banketin 40 yüzey üçgeni bulunamadı')
    bank_tree=BVHTree.FromPolygons(vertices,faces,all_triangles=True)
    maximum=0.; worst=None; samples=0; outside=0; road_overlap=0
    rail_intersections=0
    for obj in meshes:
        rail_intersections+=len(bvh(obj).overlap(rail_tree))
        verts=[obj.matrix_world@v.co for v in obj.data.vertices]
        # Köşelerle birlikte üçgen içlerini 2 cm aralıkla tara; ince taşmalar da yakalanır.
        for tri in obj.data.loop_triangles:
            a,b,c=[verts[i] for i in tri.vertices]
            steps=max(1,math.ceil(max((a-b).length,(a-c).length,(b-c).length)/.02))
            for i in range(steps+1):
                for j in range(steps-i+1):
                    p=a+(b-a)*(i/steps)+(c-a)*(j/steps)
                    surface,_=hit(ground_tree,p.x,p.y)
                    bank,_=hit(bank_tree,p.x,p.y)
                    road,_=hit(road_tree,p.x,p.y)
                    samples+=1
                    outside+=bank is None
                    road_overlap+=road is not None
                    depth=max(0.,surface.z-p.z) if surface is not None else 0.
                    if depth>maximum:
                        maximum=depth
                        worst={'object':obj.name,'point':list(p),'surface':list(surface)}
    check(maximum<=.020001,f'Çekici zemine gömülmesi: {maximum:.6f} m > 0.02 m')
    check(outside==0,f'Çekici banket dışına taşıyor: {outside} örnek')
    check(road_overlap==0,f'Çekici yola taşıyor: {road_overlap} örnek')
    check(rail_intersections==0,f'Çekici korkuluğa giriyor: {rail_intersections} üçgen çifti')
    contacts={}
    for axle in ('F','R1','R2'):
        for side in ('L','R'):
            name=axle+'_'+side
            obj=bpy.data.objects.get('SM_TowTruck_Wheel_'+name)
            check(obj is not None,'Çekici tekeri eksik: '+name)
            if obj is None: continue
            points=[obj.matrix_world@v.co for v in obj.data.vertices]
            bottom=min(p.z for p in points)
            lowest=[p for p in points if p.z<bottom+1e-5]
            gaps=[]
            for p in lowest:
                surface,normal=hit(bank_tree,p.x,p.y)
                check(surface is not None and normal.z>.99,name+': teker tabanı banket üstünde değil')
                if surface is not None: gaps.append(p.z-surface.z)
            check(bool(gaps) and max(abs(g) for g in gaps)<=.030001,name+': teker teması ±3 cm dışında')
            contacts[name]={'bottom_z_m':bottom,'surface_gaps_m':gaps,'bottom_points':[list(p) for p in lowest]}
    # Kaynakla birebir parça/poz karşılaştırması: yatık vinç, kapalı kapılar, toplu ayaklar ve dört koni.
    inverse=root.matrix_world.inverted()
    actual={obj.name:{'matrix':inverse@obj.matrix_world,'parent':obj.parent.name,
                     'vertices':[v.co.copy() for v in obj.data.vertices] if obj.type=='MESH' else None}
            for obj in root.children_recursive}
    source=ROOT/'Export/TowTruck/SM_TowTruck.fbx'
    check(entry.get('source_sha256')==hashlib.sha256(source.read_bytes()).hexdigest(),'Çekici kaynak SHA256 farklı')
    before=set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(source),use_anim=False)
    imported=set(bpy.data.objects)-before
    source_root=next(obj for obj in imported if obj.parent is None)
    source_inverse=source_root.matrix_world.inverted()
    expected={obj.name.removesuffix('.001'):obj for obj in source_root.children_recursive}
    check(set(actual)==set(expected),'Çekici hiyerarşisi kaynakla farklı')
    for name,obj in expected.items():
        if name not in actual: continue
        delta=actual[name]['matrix']-(source_inverse@obj.matrix_world)
        check(max(abs(v) for row in delta for v in row)<1e-4,name+': dinlenme pozu farklı')
        check(actual[name]['parent']==obj.parent.name.removesuffix('.001'),name+': parent farklı')
        if obj.type=='MESH':
            verts=actual[name]['vertices']
            check(verts is not None and len(verts)==len(obj.data.vertices) and
                  all((a-b.co).length<1e-5 for a,b in zip(verts,obj.data.vertices)),name+': kaynak mesh farklı')
    return {'max_penetration_m':maximum,'worst_penetration':worst,'surface_sample_spacing_m':.02,
            'surface_samples':samples,'wheel_contacts':contacts,'outside_bank_samples':outside,
            'road_overlap_samples':road_overlap,'guardrail_intersection_pairs':rail_intersections,
            'slot_offset_m':list(root.location-Vector(data['markers']['RecoveryTruckSlot']['position']))}


def verify():
    errors=[]
    def check(condition,message):
        if not condition: errors.append(message)
    stats=json.loads((OUT/'stats.json').read_text(encoding='utf-8'))
    data=json.loads((OUT/'markers.json').read_text(encoding='utf-8'))
    markers=data['markers']
    check(set(markers)==REQUIRED_MARKERS,'İşaret adları eksik/fazla')
    # Blend kayıt ayarları, işaret ve yer tutucular FBX kontrolünden ayrı sınanır.
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'B1_Ucurum.blend'))
    scene=bpy.context.scene
    baseline=json.loads((OUT/'ramp_baseline.json').read_text(encoding='utf-8'))
    check(hashlib.sha256((OUT/'markers.json').read_bytes()).hexdigest()==
          'eba21e8287c9c17ca5f72b4de3ddbb422b78807c1b94f9e7c75ee65d4f4132f6',
          'Rampa düzeltmesi işaret dosyasını değiştirmiş')
    # r3 alt köşe zemini; r2'nin çimensiz şeridi ve diğer modüller aynen korunur.
    ramp_expected={'SM_B1_Ground':'a09fb6158d1eb6cbc3b5f2a5c0db3c272f5b0da143f8fdd4be1b3ab833313f86',
                   'SM_B1_Vegetation':'d9fd7e7f3fb3025319d73622bfad4e2f856faa0a584ef7067f00c684c8b48138'}
    for name,expected in baseline['objects'].items():
        expected=ramp_expected.get(name,expected)
        obj=bpy.data.objects.get(name)
        check(obj is not None,'Kapsam dışı modül eksik: '+name)
        if obj is None: continue
        signature={'v':[list(v.co) for v in obj.data.vertices],
                   'f':[list(p.vertices) for p in obj.data.polygons],
                   'm':[p.material_index for p in obj.data.polygons],
                   'matrix':[list(r) for r in obj.matrix_world]}
        check(hashlib.sha256(json.dumps(signature).encode()).hexdigest()==expected,
              'Rampa dışında geometri/yerleşim değişmiş: '+name)
    check(scene.render.engine=='CYCLES' and scene.cycles.samples==64 and scene.cycles.use_denoising,'Cycles 64 + denoiser')
    check(scene.view_settings.view_transform=='AgX','AgX görünüm dönüşümü')
    check((scene.render.resolution_x,scene.render.resolution_y)==(1920,1080),'Render çözünürlüğü')
    check(scene.unit_settings.scale_length==1,'Metre ölçeği')
    for name,entry in markers.items():
        obj=bpy.data.objects.get(name)
        check(obj is not None and obj.type=='EMPTY',name+': Blend empty eksik')
        if obj:
            check((obj.location-Vector(entry['position'])).length<1e-4,name+': Blend/JSON konumu farklı')
            if 'polyline' in entry:
                check(json.loads(obj['polyline_json'])==entry['polyline'],name+': polyline farklı')
    for name in ('SM_Minivan','SM_TowTruck','RENDER_FogSea','CAM_Genel','CAM_Plan','CAM_Yakin','CAM_Levha'):
        check(name in bpy.data.objects,name+': Blend objesi eksik')
    backdrop_obj=bpy.data.objects.get('SM_B1_Backdrop')
    check(backdrop_obj is not None and not backdrop_obj.get('collider',True),'Arka plan collider kapalı olmalı')
    minivan_measured=verify_minivan(check,data)
    towtruck_measured=verify_towtruck(check,data)
    check(abs(markers['FallRespawnPlane']['position'][2]+60)<1e-5,'Respawn Z=-60')
    for name in ('TrafficLane_EastBound','TrafficLane_WestBound'):
        points=[Vector(p) for p in markers[name]['polyline']]
        gaps=[(b-a).length for a,b in zip(points,points[1:])]
        check(all(.96<=g<=1.001 for g in gaps[1:-1]) and all(0<g<=1.001 for g in (gaps[0],gaps[-1])),name+': 1 m örnekleme')
        check(points[0].y>45 and points[-1].y>45,name+': trafik uçları viraj ötesinde değil')
    # Bütün FBX'ler import edilir; ölçümler üreticinin iddialarından değil mesh'ten.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    actual_files={p.stem for p in OUT.glob('*.fbx')}
    check(actual_files==EXPECTED,'FBX modül listesi farklı: '+str(actual_files^EXPECTED))
    counts={}
    imported={}
    loose_signatures=set()
    for path in sorted(OUT.glob('*.fbx')):
        before=set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
        objects=list(set(bpy.data.objects)-before)
        meshes=[o for o in objects if o.type=='MESH']
        check(len(meshes)==1,path.name+': tam bir mesh gerekli')
        check(len(objects)==1,path.name+': beklenmeyen obje')
        if not meshes: continue
        obj=meshes[0]
        imported[path.stem]=obj
        check(obj.name==path.stem,path.name+': obje adı')
        obj.data.calc_loop_triangles()
        count=len(obj.data.loop_triangles)
        counts[path.stem]=count
        check(count==stats['object_triangles'].get(path.stem),path.name+': export üçgen sayısı farklı')
        limit=30000 if path.stem=='SM_B1_Backdrop' else 60000
        check(count<=limit,path.name+': modül bütçesi')
        check(not any(p.use_smooth for p in obj.data.polygons),path.name+': flat shading değil')
        check(not obj.modifiers,path.name+': modifier kalmış')
        check(all(s.material and s.material.name.startswith('M_') for s in obj.material_slots),path.name+': materyal adı')
        if path.stem in CORE:
            check(obj.location.length<1e-4,path.name+': modül pivotu orijinde değil')
        else:
            expected=Vector(data['placements'][path.stem]['position'])
            check((obj.location-expected).length<1e-4,path.name+': prop yerleşimi')
            verts=[v.co for v in obj.data.vertices]
            mins=Vector(tuple(min(v[i] for v in verts) for i in range(3)))
            maxs=Vector(tuple(max(v[i] for v in verts) for i in range(3)))
            check(abs(mins.z)<.001,path.name+': pivot tabanda değil')
            check(abs(mins.x+maxs.x)<.04 and abs(mins.y+maxs.y)<.04,path.name+': pivot taban ortasında değil')
        if '_Suitcase_' in path.stem or '_Bag_' in path.stem:
            check(100<=count<=300,path.name+': bagaj 100–300 üçgen değil')
        if '_LooseRock_' in path.stem:
            signature=json.dumps([[round(c,5) for c in v.co] for v in obj.data.vertices])
            loose_signatures.add(hashlib.sha256(signature.encode()).hexdigest())
            dimensions=[max(v.co[i] for v in obj.data.vertices)-min(v.co[i] for v in obj.data.vertices) for i in range(3)]
            check(.60-1e-4<=max(dimensions)<=1.50+1e-4,path.name+': gevşek kaya ölçüsü')
    props=sum(v for k,v in counts.items() if '_Prop_' in k or '_LooseRock_' in k)
    backdrop=counts.get('SM_B1_Backdrop',0)
    playable=sum(counts.values())-props-backdrop
    check(playable<=60000,f'Oynanabilir bütçe: {playable}')
    check(backdrop<=30000,f'Arka plan bütçe: {backdrop}')
    check(len(loose_signatures)==8,'Sekiz gevşek kaya farklı mesh olmalı')
    check(playable==stats['playable_triangles'] and props==stats['prop_triangles'],'Toplam bütçe kaydı farklı')
    check(not any(name.startswith(('PH_','RENDER_','CAM_')) for name in imported),'FBX içine sunum objesi girmiş')
    measured={'minivan':minivan_measured,'towtruck':towtruck_measured}
    measured['ramp_continuity']=verify_ramp(check,imported,data)
    measured['ramp_bottom_corner']=verify_ramp_bottom_corner(check,imported,data)
    measured['unique_loose_rock_meshes']=len(loose_signatures)
    measured['r2_playable_target_met']=30000<=playable<=45000
    measured['r2_backdrop_target_met']=8000<=backdrop<=20000
    road_tree=bvh(imported['SM_B1_Road'])
    widths=[]
    for sample in data['geometry_probes']['road_cross_sections'][1:-1]:
        c,n=Vector(sample['center']),Vector(sample['normal'])
        distances=[]
        for sign in (-1,1):
            lo,hi=0.,5.
            for _ in range(20):
                mid=(lo+hi)/2
                p=c+n*sign*mid
                point,_=hit(road_tree,p.x,p.y)
                if point is not None: lo=mid
                else: hi=mid
            distances.append((lo+hi)/2)
        widths.append(sum(distances))
    measured['road_width_min_m']=min(widths)
    measured['road_width_max_m']=max(widths)
    check(all(7.8<=w<=8.2 for w in widths),'Yol genişliği 8 ±0.2 dışında')
    ground_tree=bvh(imported['SM_B1_Ground'])
    visibility=[]
    observers=[Vector(markers['PlayerSpawn']['position'])+Vector((0,0,1.7)),
               Vector(markers['RecoveryTruckSlot']['position'])+Vector((0,0,1.7)),
               Vector((0,0,1.7)),Vector((4,-15,-7.3))]
    for name in ('TrafficSpawn_West','TrafficSpawn_East','TrafficDespawn_West','TrafficDespawn_East'):
        target=Vector(markers[name]['position'])+Vector((0,0,1))
        for observer in observers:
            delta=target-observer
            point,_,_,distance=ground_tree.ray_cast(observer,delta.normalized(),delta.length)
            occluded=point is not None and distance<delta.length-.5
            visibility.append(occluded)
            check(occluded,name+': oyun alanındaki bakıştan kaya arkasında değil')
    measured['traffic_occlusion_rays_passed']=sum(visibility)
    measured['traffic_occlusion_rays_total']=len(visibility)
    path_z=[]
    for sample in data['geometry_probes']['path']:
        p,n=hit(ground_tree,sample[0],sample[1])
        check(p is not None,'Patika yüzeyi bulunamadı')
        if p: path_z.append(p.z)
    measured['path_z_range_m']=[min(path_z),max(path_z)] if path_z else []
    check(path_z and all(-9.6<=z<=-8.4 for z in path_z),'Patika Z=-9 ±0.6 dışında')
    ledge,normal=hit(ground_tree,3,-21)
    check(ledge is not None and abs(ledge.z+14)<.01 and normal.z>.999,'Alt çıkıntı Z=-14 düz yüzey değil')
    # Yüzeyin merkez ve iki yanda örneklenmesi rampayı gizleyen kayayı da yakalar.
    ramp=[Vector(p) for p in data['geometry_probes']['ramp']]
    tangent=(ramp[-1]-ramp[0]); lateral=Vector((-tangent.y,tangent.x,0)).normalized()
    slopes=[]
    for sample in ramp[1:-1]:
        for offset in (-.65,0,.65):
            test=sample+lateral*offset
            p,n=hit(ground_tree,test.x,test.y)
            check(p is not None,'Rampa yüzeyi bulunamadı')
            if p:
                check(abs(p.z-test.z)<.12,f'Rampa yürüme bandında çakışan yüzey: {list(test)} -> {p.z:.4f}')
                slopes.append(math.degrees(math.acos(min(1,abs(n.z)))))
    measured['ramp_max_slope_degrees']=max(slopes) if slopes else 90
    check(slopes and max(slopes)<=22.001,'Rampada 22 derece aşıldı')
    slope_angles=[]
    for sample in data['geometry_probes']['slope']:
        top,bottom=Vector(sample['top']),Vector(sample['bottom'])
        # Sınırdaki sayısal belirsizlikten kaçınmak için kesitin %5/%95'i.
        a,b=top.lerp(bottom,.05),top.lerp(bottom,.95)
        pa,_=hit(ground_tree,a.x,a.y); pb,_=hit(ground_tree,b.x,b.y)
        if pa and pb:
            slope_angles.append(math.degrees(math.atan2(abs(pa.z-pb.z),math.hypot(pa.x-pb.x,pa.y-pb.y))))
    measured['slope_mean_degrees']=sum(slope_angles)/len(slope_angles) if slope_angles else 0
    check(measured['slope_mean_degrees']>=45,'Yamaç ortalama eğimi 45 derece altı')
    for name in ('Genel','Plan','Yakin'):
        for filename,dimensions in [('CAM_'+name+'.png',(1920,1080)),('compare_'+name.lower()+'.png',(3840,1080))]:
            path=OUT/'renders'/filename
            check(path.exists(),filename+': yok')
            if path.exists():
                raw=path.read_bytes()
                check(raw[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',raw[16:24])==dimensions,filename+': PNG ölçüsü')
                check(len(raw)>20000,filename+': şüpheli küçük görüntü')
    for filename in ('ramp_top.png','ramp_bottom.png','ramp_before_top.png','ramp_before_bottom.png'):
        path=OUT/'renders'/filename
        check(path.exists(),filename+': yok')
        if path.exists():
            raw=path.read_bytes()
            check(raw[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',raw[16:24])==(1920,1080),filename+': PNG ölçüsü')
            check(len(raw)>20000,filename+': şüpheli küçük görüntü')
    # Levha kontrol render'ı (3. tur): referans karşılaştırması yok, yalnız görüntünün kendisi.
    path=OUT/'renders'/'CAM_Levha.png'
    check(path.exists(),'CAM_Levha.png: yok')
    if path.exists():
        raw=path.read_bytes()
        check(raw[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',raw[16:24])==(1920,1080),'CAM_Levha.png: PNG ölçüsü')
        check(len(raw)>20000,'CAM_Levha.png: şüpheli küçük görüntü')
    check(stats.get('consecutive_identical_builds',0)>=2 and stats.get('matches_previous_geometry'),
          'İki ardışık üretimde aynı geometri SHA256 henüz doğrulanmadı')
    check(stats.get('matches_previous_placement'),'İki ardışık üretimde aynı minibüs yerleşimi doğrulanmadı')
    check(stats.get('matches_previous_towtruck_placement'),'İki ardışık üretimde aynı çekici yerleşimi doğrulanmadı')
    # Rampa uçları ve çimensiz yürüyüş şeridi; bütün modüller ayrıca imzalarıyla sınanır.
    check(stats.get('geometry_sha256')=='25dba8e8b155c8ef016cb452c738dac6d28fb568a5e1bcfbe5b74ac945fcaef5',
          'Mekân modüllerinin rampa düzeltmesi geometri SHA256 değeri değişti')
    report={'passed':not errors,'errors':errors,'measured':measured,
                           'reimported_object_triangles':counts,'reimported_playable_triangles':playable,
                           'reimported_backdrop_triangles':backdrop,'reimported_prop_triangles':props}
    # Kabul kontrolü repoya hiçbir şey yazmaz; stats.json yalnız build'e aittir.
    report_path=Path(tempfile.gettempdir())/('verify_bolum1_ucurum_'+hashlib.sha256(str(ROOT).encode()).hexdigest()[:12]+'.json')
    report_path.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2),flush=True)
    print('Verification report:',report_path,flush=True)
    print('VERIFY OK' if not errors else 'VERIFY FAILED',flush=True)
    return 0 if not errors else 1


if __name__=='__main__':
    try: result=verify()
    except Exception:
        import traceback
        traceback.print_exc()
        result=1
    if result:
        sys.stdout.flush(); sys.stderr.flush()
        sys.exit(result)
