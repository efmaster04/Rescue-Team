"""Blender 5.2: Bölüm 1, metre tabanlı deterministik sahne / FBX / sunum.

Normal kullanım: blender -b --factory-startup --python tools/blender/build_bolum1_ucurum.py
Görsel tur: aynı komuta -- --round 1 eklenebilir; çekici arşivi r6_round_*.
Rampa yakın planları: -- --ramp-renders-only (diğer sunum PNG'lerini korur).
"""
import argparse
import hashlib
import json
import math
import os
import random
import shutil
import sys
import time
import warnings
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector, Matrix

warnings.filterwarnings('ignore',category=DeprecationWarning)

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Export/Bolum1_Ucurum'
REF = ROOT / 'ArtSource/Bolum1_Ucurum/references'
SEED = 120126
RNG = random.Random(SEED)
MATS = {}
COLORS = {}
MODULES = []
PLACEMENTS = {}
PROBES = {}
# Kullanıcı kararı (2026-10-12): hafif S yol. Uçlardaki (±44,49) noktaları viraj sonrası ~15 m uzatmadır.
CONTROL = [(-44,49,-1.5),(-50,34,-1.5),(-56,18,-1.5),(-47,5,-1.3),(-35,-3,-1.0),(-18,3,-.6),(2,-1,0),
           (18,-2,.6),(35,6,1.5),(50,9,2.1),(56,22,2.4),(50,34,2.5),(44,49,2.6)]
CENTRAL = 46
BACK = {}


def linear(v):
    return v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4


def material(name, color, metal=0):
    mat = bpy.data.materials.new('M_'+name)
    mat.use_nodes = True
    rgb = tuple(linear(c) for c in color)
    mat.diffuse_color = (*rgb,1)
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*rgb,1)
    bsdf.inputs['Roughness'].default_value = .83 if not metal else .48
    bsdf.inputs['Metallic'].default_value = metal
    # 01'deki mat görünüm: metal dışı yüzeylerde parlama azaltılır (fren izi ile asfalt ayrışır).
    if not metal and 'Specular IOR Level' in bsdf.inputs: bsdf.inputs['Specular IOR Level'].default_value = .2
    # Tek materyal içinde yüz başına az miktarda renk değişimi; görüntü texture yok.
    attr = mat.node_tree.nodes.new('ShaderNodeVertexColor')
    attr.layer_name = 'Color'
    mat.node_tree.links.new(attr.outputs['Color'],bsdf.inputs['Base Color'])
    MATS[name] = mat
    COLORS[name] = color
    return mat


def sample_palette():
    img = bpy.data.images.load(str(REF/'01_style_main_v2_genel.jpg'),check_existing=False)
    w,h = img.size
    pixels = np.array(img.pixels[:],dtype=np.float32).reshape(h,w,4)
    # Referanstaki yüzeylerin küçük, tek renkli bölgelerinin medyanı (sRGB).
    locations = {'Rock':(.260,.280),'Dirt':(.413,.842),'Grass':(.561,.762),
                 'Asphalt':(.080,.630),'Metal':(.162,.623),'Backdrop':(.719,.211)}
    sampled = {}
    for name,(u,v) in locations.items():
        x,y = int(u*w),int((1-v)*h)
        sampled[name] = tuple(float(c) for c in np.median(pixels[y-3:y+4,x-3:x+4,:3],axis=(0,1)))
    bpy.data.images.remove(img)
    # Işık almış referans renkleri albedoya uyarlanır; ham örnekler stats'ta saklanır.
    palette = {'Asphalt':(.2,.2,.215),'LineYellow':(.91,.65,.25),'LineWhite':(.86,.84,.73),
               'Rock':(.62,.51,.365),'Dirt':(.48,.32,.20),'Gravel':(.49,.435,.34),
               'Grass':(.35,.39,.105),'Metal':(.48,.48,.46),'Sign':(.78,.08,.07),
               'Backdrop':(.55,.50,.59)}
    for name in ('Rock','Dirt','Grass'):
        palette[name] = tuple(.30*a+.70*b for a,b in zip(sampled[name],palette[name]))
    for name,color in palette.items():
        material(name,color,.65 if name=='Metal' else 0)
    return {'reference_samples_srgb':sampled,'albedo_srgb':palette,'sampling_uv':locations}


class Geo:
    def __init__(self,name):
        self.name,self.v,self.f,self.mat,self.tint = name,[],[],[],[]

    def patch(self,verts,faces,mat='Rock',shade=1):
        offset=len(self.v)
        self.v.extend([tuple(v) for v in verts])
        for face in faces:
            # Açık triangulation FBX'te ve hash'te aynı topolojiyi korur.
            for i in range(1,len(face)-1):
                self.f.append(tuple(offset+j for j in (face[0],face[i],face[i+1])))
                self.mat.append(mat)
                self.tint.append(shade)

    def quad(self,a,b,c,d,mat='Rock',shade=1):
        self.patch([a,b,c,d],[(0,1,2,3)],mat,shade)

    def box(self,center,size,mat='Rock',rotation=None,shade=1):
        c=Vector(center)
        verts=[Vector((x*size[0]/2,y*size[1]/2,z*size[2]/2))
               for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                             (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
        verts=[c+(rotation@p if rotation else p) for p in verts]
        self.patch(verts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mat,shade)

    def rock(self,center,size,mat='Rock',shade=1,n=8,rings=None):
        # Şişkin omuzlu halkalar; geniş fasetler ve farklı siluetler.
        angles=[2*math.pi*i/n+RNG.uniform(-.16,.16) for i in range(n)]
        verts=[]
        rings=rings or [(-.5,.48),(-.30,.85),(0,1),(.30,.86),(.5,.43)]
        for z,r in rings:
            for a in angles:
                rr=r*RNG.uniform(.86,1.12)
                verts.append((center[0]+math.cos(a)*size[0]*rr*.5,
                              center[1]+math.sin(a)*size[1]*rr*.5,
                              center[2]+(z+RNG.uniform(-.065,.065))*size[2]))
        last=len(rings)-1
        faces=[tuple(reversed(range(n))),tuple(range(last*n,(last+1)*n))]
        for k in range(last):
            for i in range(n):
                faces.append((k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i))
        self.patch(verts,faces,mat,shade)

    def cylinder(self,center,radius,depth,mat='Asphalt',axis='X',n=12,rotation=None):
        verts=[]
        for s in (-1,1):
            for i in range(n):
                a=2*math.pi*i/n
                p=Vector((depth*s/2,radius*math.cos(a),radius*math.sin(a)))
                if axis=='Z': p=Vector((p.y,p.z,p.x))
                verts.append(Vector(center)+(rotation@p if rotation else p))
        faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
        faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
        self.patch(verts,faces,mat)

    def finish(self,pivot=None,export=True):
        if pivot:
            # Bütün prop'larda gerçek mesh sınır kutusunun taban ortası.
            pivot=((min(v[0] for v in self.v)+max(v[0] for v in self.v))/2,
                   (min(v[1] for v in self.v)+max(v[1] for v in self.v))/2,
                   min(v[2] for v in self.v))
        mesh=bpy.data.meshes.new(self.name)
        origin=Vector(pivot or (0,0,0))
        mesh.from_pydata([Vector(v)-origin for v in self.v],[],self.f)
        mesh.update()
        for mat in MATS.values(): mesh.materials.append(mat)
        names=list(MATS)
        attr=mesh.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='CORNER')
        for face,mat,tint in zip(mesh.polygons,self.mat,self.tint):
            face.material_index=names.index(mat)
            color=tuple(linear(min(1,c*tint)) for c in COLORS[mat])+(1,)
            for loop in face.loop_indices: attr.data[loop].color=color
        obj=bpy.data.objects.new(self.name,mesh)
        bpy.context.collection.objects.link(obj)
        obj.location=origin
        obj['collider']=self.name!='SM_B1_Backdrop' and export
        obj['seed']=SEED
        if export:
            MODULES.append(obj)
            if pivot: PLACEMENTS[obj.name]={'position':list(origin),'rotation_euler':[0,0,0],'scale':[1,1,1]}
        return obj


def catmull(points,subdiv=30):
    pts=[Vector(p) for p in points]
    ext=[pts[0]*2-pts[1]]+pts+[pts[-1]*2-pts[-2]]
    result=[]
    for i in range(1,len(ext)-2):
        a,b,c,d=ext[i-1:i+3]
        for j in range(subdiv):
            t=j/subdiv
            result.append(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t))
    return result+[pts[-1]]


def resample(points,spacing=1):
    dist=[0.]
    for a,b in zip(points,points[1:]): dist.append(dist[-1]+(b-a).length)
    result=[]
    count=math.ceil(dist[-1]/spacing)
    cursor=0
    for i in range(count+1):
        s=min(i*spacing,dist[-1])
        while cursor<len(dist)-2 and dist[cursor+1]<s: cursor+=1
        t=(s-dist[cursor])/(dist[cursor+1]-dist[cursor])
        result.append(points[cursor].lerp(points[cursor+1],t))
    return result


ROAD=resample(catmull(CONTROL),.5)


def normal_at(points,i):
    delta=points[min(i+1,len(points)-1)]-points[max(0,i-1)]
    return Vector((-delta.y,delta.x,0)).normalized()


def road_at_x(x):
    for a,b in zip(ROAD,ROAD[1:]):
        if a.y<12 and b.y<12 and a.x<=x<=b.x:
            return a.lerp(b,(x-a.x)/(b.x-a.x))
    return min(ROAD,key=lambda p:abs(p.x-x)+max(0,p.y-12)*5).copy()


def strip(geo,points,width,mat,raise_z=0):
    edges=[]
    for i,p in enumerate(points):
        n=normal_at(points,i)
        edges.append((p-n*width/2+Vector((0,0,raise_z)),p+n*width/2+Vector((0,0,raise_z))))
    for (a,b),(c,d) in zip(edges,edges[1:]): geo.quad(a,c,d,b,mat)
    return edges


def make_road():
    road=Geo('SM_B1_Road')
    strip(road,ROAD,8,'Asphalt')
    road.finish()
    marks=Geo('SM_B1_RoadMarkings')
    for side in (-1,1):
        pts=[p+normal_at(ROAD,i)*side*3.62 for i,p in enumerate(ROAD)]
        strip(marks,pts,.12,'LineWhite',.016)
    for i in range(0,len(ROAD)-6,15): strip(marks,ROAD[i:i+7],.14,'LineYellow',.018)
    # Fren izleri yola göre tanımlanır: doğuya giden şeritten kırık noktaya kıvrılarak gider.
    from mathutils.bvhtree import BVHTree
    obj=bpy.data.objects['SM_B1_Road']
    tree=BVHTree.FromPolygons([v.co for v in obj.data.vertices],[tuple(p.vertices) for p in obj.data.polygons],all_triangles=True)
    lateral=[(-25,-1.75),(-17,-1.55),(-10,-2.0),(-4,-2.9),(1,-3.75)]
    for offset in (-.73,.73):
        keys=[]
        for x,l in lateral:
            p=road_at_x(x)
            i=min(range(len(ROAD)),key=lambda k:(ROAD[k]-p).length)
            q=p+normal_at(ROAD,i)*(l+offset*(1 if x<-6 else .8))
            keys.append((q.x,q.y,q.z))
        pts=catmull(keys,16)
        # İzleri asfaltın gerçek üçgenlerine oturt; interpolasyon altında kaybolmasın.
        for p in pts:
            hit,_,_,_=tree.ray_cast(Vector((p.x,p.y,10)),Vector((0,0,-1)),30)
            if hit: p.z=hit.z
        strip(marks,pts,.32,'Asphalt',.024)
        marks.tint[-2*(len(pts)-1):]=[.06]*(2*(len(pts)-1))
    marks.finish()
    PROBES['road_cross_sections']=[{'center':list(ROAD[i]),'normal':list(normal_at(ROAD,i))}
                                  for i in range(0,len(ROAD),12)]


RAMP_START=road_at_x(-35)+Vector((0,-4.9,-.10))
RAMP_END=Vector((-12,road_at_x(-12).y-4-7.8-.7,-9))
RAMP=[RAMP_START.lerp(RAMP_END,i/24) for i in range(25)]
RA=np.array([tuple(p) for p in ROAD])


def path_inner(x): return road_at_x(x).y-4-7.8
def path_outer(x): return path_inner(x)-5


def _hash(i,j):
    s=math.sin(i*127.1+j*311.7)*43758.5453
    return s-math.floor(s)


def vnoise(x,y):
    # Sabit, tohumsuz değer gürültüsü; aynı girdi her çalıştırmada aynı sonucu verir.
    i,j=math.floor(x),math.floor(y)
    fx,fy=x-i,y-j
    fx,fy=fx*fx*(3-2*fx),fy*fy*(3-2*fy)
    a,b,c,d=_hash(i,j),_hash(i+1,j),_hash(i,j+1),_hash(i+1,j+1)
    return (a+(b-a)*fx+(c-a)*fy+(a-b-c+d)*fx*fy)*2-1


def smooth(t):
    t=max(0.,min(1.,t))
    return t*t*(3-2*t)


def road_near(x,y):
    d2=(RA[:,0]-x)**2+(RA[:,1]-y)**2
    k=int(np.argmin(d2))
    return math.sqrt(float(d2[k])),float(RA[k,2])


def carve(x,y,start=7.5,k=1.25):
    # Yol çevresinde düz omuz, uzaklaştıkça yükselen kaya: viraj yolu bir yarmaya oturur.
    d,z=road_near(x,y)
    return z-.12+max(0.,d-start)*k


def shoulders(x):
    # Doğuda (kamera sağı) yükselen yüksek omuz, ortada/batıda uzak zirveyi gösteren eyer.
    return smooth((x-20)/40),smooth((-x-60)/80)


def west_h(x):
    # Batı ucu: kademe kademe inen sırt (masa gibi düz üst yok).
    a=max(0.,-x-CENTRAL)/16
    stair=math.floor(a)+smooth((a-math.floor(a))*3-2)
    return max(-6.,22-4.2*stair)


def bline(x):
    rz=road_at_x(max(-CENTRAL,min(CENTRAL,x))).z
    if x<-CENTRAL: return rz+west_h(x)+2*math.sin(x*.11)
    e,w=shoulders(x)
    return rz+22+18*e+8*w+2*math.sin(x*.11)


def body(x,y):
    # Yolun üstündeki yamaç y=35'te bline yüksekliğine çıkar; arkada dağ gövdesi tırmanır.
    end=smooth((abs(x)-CENTRAL)/20)
    if y<35 and x<-CENTRAL:
        h=bline(x)
        rim=max(h-14,road_at_x(-CENTRAL).z-3)
        return rim+(h-rim)*smooth(y/35)+end*4*vnoise(x*.09+1.3,y*.09)
    if y<35: return bline(x)+(y-35)*.45+end*7*vnoise(x*.07+1.3,y*.07)
    e,w=shoulders(x)
    # Ortada sırt: y≈45'te tepe, arkası iner (zirveye görüş açılır); doğu omzu ve batı sırası tırmanır.
    rise=(8+80*e)*(1-math.exp(-(y-35)/60))
    drop=34*(1-e)*smooth((y-48)/110)
    amp=min(1.,(y-35)/25)
    return bline(x)+rise-drop+5*amp*vnoise(x*.045+3.1,y*.045)


def top(x,y): return min(body(x,y),carve(x,y))


# Kenardan (yol vadi kenarı) ölçülen iniş: yamaç → -9 bandı → uçurum. Merkez kesitlerle aynı.
DROP_U=[11.9,11.3,10.3,9.5,8.3,7.8,5.5,2.9]
BACK_ROWS=[35+2.5*k for k in range(1,9)]+[59,64,70,78,88,100,115,133,155,180,210,245,285,330,380]
WALL_COLS=[-CENTRAL+4*i for i in range(24)]


def drop_profile(ztop):
    return [-80,-47,-34,-20,-9,-9,ztop+(-9-ztop)*.71,ztop+(-9-ztop)*.37]


def field_cells(grid,flip,owner,material):
    # Izgara izi kalmasın: iç noktalar düzensiz kaydırılır (paylaşılan dikiş noktaları hariç),
    # her hücrenin köşegeni ve her üçgenin tonu konumdan türeyen bağımsız rastlantıyla seçilir.
    ni,nj=len(grid),len(grid[0])
    g=[[tuple(p) for p in col] for col in grid]
    for i in range(1,ni-1):
        for j in range(1,nj-1):
            x,y,z=grid[i][j]
            if abs(abs(x)-CENTRAL)<1e-6 or abs(y-35)<1e-6: continue
            sx=min(abs(grid[i+1][j][0]-x),abs(x-grid[i-1][j][0]))
            sy=min(abs(grid[i][j+1][1]-y),abs(y-grid[i][j-1][1]))
            g[i][j]=(x+.3*sx*vnoise(x*.37+11.1,y*.41),y+.3*sy*vnoise(x*.43,y*.33+7.7),z)
    for i in range(ni-1):
        for j in range(nj-1):
            a,b,c,d=g[i][j],g[i+1][j],g[i+1][j+1],g[i][j+1]
            center=[(a[k]+b[k]+c[k]+d[k])/4 for k in range(3)]
            geo=owner(center)
            mat,shade=material(center,a,b,c,d)
            key=(round(center[0]*3.1),round(center[1]*2.7))
            tris=[(a,b,c),(a,c,d)] if _hash(*key)>.5 else [(a,b,d),(b,c,d)]
            for k,t in enumerate(tris):
                if flip: t=(t[0],t[2],t[1])
                tone=shade*(.9+.18*_hash(key[0]+17*k+.5,key[1]-31*k+.25))
                geo.patch(t,[(0,1,2)],mat,tone)


def field_material(center,a,b,c,d):
    x,y,z=center
    dist,rz=road_near(x,y)
    if dist<6.5 and abs(z-rz)<.4: return 'Gravel',1.0
    n=(Vector(c)-Vector(a)).cross(Vector(d)-Vector(b))
    flat=abs(n.normalized().z) if n.length>1e-9 else 0
    shade=.96+.05*vnoise(x*.013+2.2,y*.017)
    if y>150: shade*=.95
    return 'Rock',shade


def end_piece(side,ground,back):
    # Virajların ötesi: kaya duvarı, yamaç ve uçurum dağ gövdesine kaynaşarak sürer.
    rim=road_at_x(side*CENTRAL).y-4
    near=[CENTRAL+2.5*i for i in range(7)]
    far=[64,68,73,80,88,98,110,125,142,162,185,212,242,275,310,370,450,560,700,850,1000]
    n=math.ceil((35-rim)/2.5)
    mid=[rim+(35-rim)*k/n for k in range(n+1)]
    grid=[]
    rocks=[]
    for x in [side*v for v in near+far]:
        w=smooth((abs(x)-CENTRAL)/14)
        wob=w*(2.6*math.sin(x*.083+.7)+1.4*math.sin(x*.21))
        ztop=top(x,rim+wob)
        col=[]
        for k,(u,z) in enumerate(zip(DROP_U,drop_profile(ztop))):
            jitter=w*.7*vnoise(x*.17,k*1.7)
            if 2<=k<=5: z+=w*2.2*vnoise(x*.13+k,4.2)
            elif k>=6: z+=w*1.2*vnoise(x*.21,k)
            col.append((x,rim+wob-u+jitter,z))
        for y in mid:
            fade=1-(y-rim)/(35-rim)
            yy=y+wob*fade
            col.append((x,yy,top(x,yy)))
        for y in BACK_ROWS: col.append((x,y,top(x,y)))
        grid.append(col)
        if abs(x)>=49 and abs(x)<=380 and len(grid)%2==0:
            rocks.append((x,rim+wob,ztop))
    def owner(center): return ground if abs(center[0])<=60 and center[1]<=35 else back
    field_cells(grid,side<0,owner,field_material)
    if side<0:
        # Batı sırtında kaya kümeleri: inen kademelerin üstünde yarı gömülü bloklar.
        for k in range(30):
            a=RNG.uniform(3,260)
            x=-CENTRAL-a
            y=RNG.uniform(rim+3,31)
            if road_near(x,y)[0]<11: continue
            geo=ground if abs(x)<=60 else back
            sz=RNG.uniform(5,8.5)*(1 if a<60 else 1.5)
            for m in range(RNG.choice((1,2,3))):
                cx,cy=x+RNG.uniform(-.7,.7)*sz,y+RNG.uniform(-.5,.5)*sz
                if road_near(cx,cy)[0]<5+sz*.6: continue
                s_=sz*RNG.uniform(.55,1)
                geo.rock((cx,cy,top(cx,cy)+s_*.12),(s_*1.2,s_,s_*.8),'Rock',RNG.uniform(.88,1.06),n=7)
    for x,y,z in rocks:
        geo=ground if abs(x)<=60 else back
        s=RNG.uniform(.8,1.25)*(1 if abs(x)<90 else 1.5)
        # Yüzeye yarı gömülü iri kayalar: yamaçta ve uçurum alnında.
        cx,cy=x+RNG.uniform(-2,2),y-3.6
        while road_near(cx,cy)[0]<4.5+5*s: cy-=1
        geo.rock((cx,cy,z+(-9-z)*.45-1),(9*s,7*s,8*s),'Rock',RNG.uniform(.9,1.06),n=7)
        geo.rock((x+RNG.uniform(-3,3),y-9.3,RNG.uniform(-30,-20)),(10*s,6*s,14*s),'Rock',RNG.uniform(.88,1.02),n=6)
        if RNG.random()<.6:
            cx,cy=x+RNG.uniform(-4,4),y+RNG.uniform(8,22)
            if road_near(cx,cy)[0]>5+5*s:
                geo.rock((cx,cy,top(cx,cy)+1),(9*s,8*s,7*s),'Rock',RNG.uniform(.9,1.08),n=7)


def back_piece(back):
    grid=[[(x,y,top(x,y)) for y in [35]+BACK_ROWS] for x in WALL_COLS]
    field_cells(grid,False,lambda c:back,field_material)


def slope_point(x,t,edge,foot,ztop):
    return Vector((x,edge+(foot-edge)*t,ztop+(-9-ztop)*t))


def join_ramp_ends(geo):
    # X kesitleri eğik rampanın dikdörtgen uçlarını kapatamıyordu. Bağımsız
    # çakıl ağız üstte, sonraki yamaç kesiti altta yürüyüşe taşıyordu.
    # Yalnız bağlantı ayak izini kes; dışarıdaki yüzeyleri yerinde tut.
    from mathutils.bvhtree import BVHTree
    delta=RAMP_END-RAMP_START
    length=math.hypot(delta.x,delta.y)
    forward=Vector((delta.x/length,delta.y/length,0))
    lateral=Vector((-forward.y,forward.x,0))
    grade=delta.z/length
    road=bpy.data.objects['SM_B1_Road']
    road_tree=BVHTree.FromPolygons([v.co for v in road.data.vertices],
                                 [tuple(p.vertices) for p in road.data.polygons],all_triangles=True)
    original_tree=BVHTree.FromPolygons(geo.v,geo.f,all_triangles=True)
    # Dekor yerleşimi zemin ışınlarına bağlı; bu işte çimenler ve RNG sırası sabit kalır.
    BACK['vegetation_ground_tree']=original_tree
    # Son dört set kesitindeki 22 cm'lik dudak, patika ağzında kesilerek
    # ince bir kama bırakıyordu. Yalnız set sırtını mevcut uç kotuna indir;
    # yürüyüş yüzeyi, dış kaya eteği ve dekorun eski ışın ağacı korunur.
    # 3.85 m'ye yayılan iniş, kenarda da 5 cm başına 2 cm sınırını korur.
    bank_start=length*20/24
    for i,vertex in enumerate(geo.v):
        p=Vector(vertex)
        s=(p-RAMP_START).dot(forward)
        t=(p-RAMP_START).dot(lateral)
        if (bank_start-1e-5<=s<=length+1e-5 and abs(t+1.38)<1e-5 and
                abs(p.z-(RAMP_START.z+grade*s+.22))<1e-5):
            p.z-=.22*max(0.,min(1.,(s-bank_start)/(length-bank_start)))
            geo.v[i]=tuple(p)
    for face_index,vertex_index in BACK['ramp_bank_quads']:
        a,b,c,d=range(vertex_index,vertex_index+4)
        # Alçalan sırtta eski köşegen boyuna/enine eğimleri aynı üçgende
        # topluyordu. Diğer köşegen hiçbir üst yüzeyi eski şevden dik yapmaz.
        geo.f[face_index:face_index+2]=[(a,b,d),(b,c,d)]
    original_tree=BVHTree.FromPolygons(geo.v,geo.f,all_triangles=True)
    def point(s,t): return RAMP_START+forward*s+lateral*t
    def road_height(s,t):
        p=point(s,t)
        p.z=road_at_x(p.x).z
        return road_tree.find_nearest(p)[0].z-.001
    def hermite(t,a,b,da,db,span):
        return (2*t**3-3*t*t+1)*a+(t**3-2*t*t+t)*span*da+(-2*t**3+3*t*t)*b+(t**3-t*t)*span*db
    def profile(s,t):
        if s<=-.8: return road_height(s,t)
        if s<1.4:
            return hermite((s+.8)/2.2,road_height(-.8,t),RAMP_START.z+.015+grade*1.4,
                           (road_height(-.79,t)-road_height(-.81,t))/.02,grade,2.2)
        if s<=length-.2:
            return RAMP_START.z+.015+grade*s-.015*smooth((s-length+1.)/.8)
        if s<length+.2:
            return hermite((s-length+.2)/.4,-9-grade*.2,-9,grade,0,.4)
        return -9
    def height(s,t):
        z=profile(s,t)
        # Yol ağzı yandan gelir. İç kenardaki küçük kot farkını rampaya
        # yayarak hem asfalt dikişini hem enine eğimi yumuşat.
        if s<=0: correction=road_height(s,t)-z
        else: correction=(road_height(0,t)-profile(0,t))*smooth(1-s/4)
        return z+correction*smooth((t+.2)/1.0)
    # Ana şerit 1.8 m; kısa yan ağızlar mevcut asfalt/patika yüzeyine girer.
    regions=[(-2.4,4.2,-.9,.9),(-1.8,0.,.89,1.8),
             (length-1.,length+2.2,-.9,.9),(length,length+2.2,-1.8,-1.38),
             (length*22/24,length+2.2,-1.38,-.89)]
    def clip(poly,axis,bound,sign):
        inside=[]; outside=[]
        for a,b in zip(poly,poly[1:]+poly[:1]):
            va=(a-RAMP_START).dot(axis)-bound
            vb=(b-RAMP_START).dot(axis)-bound
            ia=va*sign>=0; ib=vb*sign>=0
            (inside if ia else outside).append(a)
            if ia!=ib:
                p=a.lerp(b,va/(va-vb))
                inside.append(p); outside.append(p)
        return inside,outside
    for lo,hi,left,right in regions:
        faces,mats,tints=geo.f,geo.mat,geo.tint
        geo.f=[]; geo.mat=[]; geo.tint=[]
        for face,mat,tint in zip(faces,mats,tints):
            poly=[Vector(geo.v[i]) for i in face]
            coords=[((v-RAMP_START).dot(forward),(v-RAMP_START).dot(lateral)) for v in poly]
            if (max(p[0] for p in coords)<=lo or min(p[0] for p in coords)>=hi or
                    max(p[1] for p in coords)<=left or min(p[1] for p in coords)>=right or
                    max(p.z for p in poly)<min(height(lo,0),height(hi,0))-3):
                geo.f.append(face); geo.mat.append(mat); geo.tint.append(tint)
                continue
            for axis,bound,sign in [(forward,lo,1),(forward,hi,-1),(lateral,left,1),(lateral,right,-1)]:
                poly,outside=clip(poly,axis,bound,sign)
                if len(outside)>=3: geo.patch(outside,[tuple(range(len(outside)))],mat,tint)
                if len(poly)<3: break
        # Paylaşılan sınırlar aynı fonksiyondan örneklenir; RNG tüketilmez.
        # Aynı düzleme 0.1 mm bindirme, float32 FBX kesim dikişinde ışın kaçmasını önler.
        rows=sorted(set([lo-.0001,hi+.0001]+[round(lo+i*.05,8) for i in range(1,math.ceil((hi-lo)/.05))]))
        cols=[left+(right-left)*i/6 for i in range(7)]
        bottom_bank=left==-1.38
        if bottom_bank: cols=sorted(set(cols+[-.9]))
        def surface(s,t):
            p=point(s,t); p.z=height(s,t)
            if bottom_bank:
                # Eski X kesitinin +1.5 cm'lik kenarı Hermite inişinin üstünde
                # kalmasın: set üstünü aynı iç kenara, değişmeyen sırta bağla.
                ridge=RAMP_START.z+grade*s+.22*(1-max(0.,min(1.,(s-bank_start)/(length-bank_start))))
                ridge=max(RAMP_END.z,ridge)
                p.z+=(ridge-p.z)*max(0.,min(1.,(-t-.9)/.48))
            if right>1:
                road_z=road_height(s,t)
                p.z=p.z+(road_z-p.z)*smooth((t-.9)/.9)
            return p
        for a,b in zip(rows,rows[1:]):
            for c,d in zip(cols,cols[1:]):
                geo.quad(surface(a,c),surface(b,c),surface(b,d),surface(a,d),'Dirt')
        # Birleşik ayak izinin yalnız dış kenarlarını kapat. Ortak kenara
        # eski yamaç duvarı koymak, yeni yüzeyde tekrar basamak oluştururdu.
        edges=[]
        for t in (left,right):
            edges.extend(((a,t),(b,t)) for a,b in zip(rows,rows[1:]))
        for s in (lo,hi):
            edges.extend(((s,a),(s,b)) for a,b in zip(cols,cols[1:]))
        for a,b in edges:
            mid=((a[0]+b[0])/2,(a[1]+b[1])/2)
            if any(region!=(lo,hi,left,right) and
                   region[0]-1e-6<=mid[0]<=region[1]+1e-6 and
                   region[2]-1e-6<=mid[1]<=region[3]+1e-6 for region in regions):
                continue
            pa,pb=surface(*a),surface(*b)
            old=[]
            for p in (pa,pb):
                hit,_,_,_=original_tree.ray_cast(Vector((p.x,p.y,500)),Vector((0,0,-1)),1000)
                old.append(hit if hit is not None else p)
            if max((pa-old[0]).length,(pb-old[1]).length)>.002:
                geo.quad(pa,old[0],old[1],pb,'Rock')


def make_terrain():
    geo=Geo('SM_B1_Ground')
    back=Geo('SM_B1_Backdrop')
    BACK['geo']=back
    # Yol altlığı ile 1 m dağ tarafı hendeği eğriyi birlikte izler.
    strip(geo,ROAD,8.05,'Gravel',-.09)
    ditch=[p+normal_at(ROAD,i)*4.5+Vector((0,0,-.08)) for i,p in enumerate(ROAD)]
    strip(geo,ditch,1,'Gravel')
    # Merkez kesitler: yol kenarı → yamaç (rampa oyulur) → patika/-9 bandı → uçurum.
    xs=sorted(set([-46,-43,-40,-37,-35,-33,-30,-27,-24,-21,-18,-15,-14,-12,-10,-7,-4,-2,0,2,4,5,7,10,
        13,15,17,20,23,25,28,31,34,37,40,43,46]+[-44.5,-38.5,-31.5,-25.5,-19.5,-8.5,-5.5,-3,1,3,
        8.5,11.5,14,18.5,21.5,26.5,29.5,32.5,35.5,38.5,41.5,44.5]))
    profiles=[]
    PROBES['path']=[]
    PROBES['slope']=[]
    rn=normal_at(RAMP,12)
    rz=(RAMP_END.z-RAMP_START.z)/(RAMP_END.x-RAMP_START.x)
    for x in xs:
        p=road_at_x(x)
        edge=p.y-4
        if 5<=x<=25: edge-=5
        path=-14<=x<=15
        foot=path_inner(x) if path else edge-7.8
        z=p.z-.08
        if -35<=x<=-12:
            center=RAMP_START.lerp(RAMP_END,(x+35)/23)
            half=.9/rn.y
            upper=Vector((x,min(edge-.05,center.y+half),center.z-rn.x*half*rz+.015))
            lower=Vector((x,center.y-half,center.z+rn.x*half*rz+.015))
            foot=min(foot,lower.y-.6)
            above=Vector((x,(edge+upper.y)/2+RNG.uniform(-.2,.2),(z+upper.z)/2+RNG.uniform(.1,.6)))
            below=Vector((x,(lower.y+foot)/2,(lower.z-9)/2+RNG.uniform(-.2,.2)))
            slope=[above,upper,lower,below]
        else:
            jx=0 if abs(x)==CENTRAL else 1
            slope=[Vector((x+jx*RNG.uniform(-.7,.7),edge+(foot-edge)*f+RNG.uniform(-.25,.25),z+(-9-z)*f+RNG.uniform(-.45,.45)))
                   for f in (.2,.42,.63,.83)]
        outer=path_outer(x) if path else foot-.5
        jx=0 if abs(x)==CENTRAL else 1
        profiles.append([Vector((x,edge,z)),*slope,Vector((x,foot,-9)),Vector((x,outer,-9)),
                         Vector((x+jx*RNG.uniform(-1.3,1.3),outer-1.2+RNG.uniform(-1.2,.6),-20+RNG.uniform(-3,3))),
                         Vector((x+jx*RNG.uniform(-1.3,1.3),outer+RNG.uniform(-2.5,1.5),-34+RNG.uniform(-3,3))),
                         Vector((x,outer-3,-47)),Vector((x,outer-3.6,-80))])
    for i,(left,right) in enumerate(zip(profiles,profiles[1:])):
        middle=(xs[i]+xs[i+1])/2
        for j in range(10):
            if j==2 and -35<=middle<=-12: continue
            if j<5:
                # Kaya yamaç; kaza hattı (minibüsün kaydığı iz) ve seyrek oluklar toprak.
                if -1.5<=middle<=5: mat,shade='Dirt',RNG.uniform(.9,1.02)
                else: mat,shade='Rock',RNG.uniform(.84,1.02)
            elif j==5:
                mat,shade=('Dirt',RNG.uniform(.96,1.04)) if -14<=middle<=15 else ('Rock',.95)
            else: mat,shade='Rock',RNG.uniform(.86,1.02)
            a,b,c,d=left[j],left[j+1],right[j+1],right[j]
            geo.quad(a,b,c,d,mat,shade)
        if -12<middle<14:
            foot=(left[5]+right[5])/2
            PROBES['path'].append([middle,float(foot.y-2.5),-9])
        if -10<middle<4:
            top_=(left[0]+right[0])/2
            bottom=(left[5]+right[5])/2
            PROBES['slope'].append({'top':list(top_),'bottom':list(bottom)})
    strip(geo,RAMP,1.8,'Dirt',.015)
    # Yol ile iniş arasında kısa çakıl ağız; ilk metreyi yol altlığından ayırır.
    a=road_at_x(-36)+Vector((0,-4,-.01))
    b=road_at_x(-34)+Vector((0,-4,-.01))
    geo.quad(a,a+Vector((0,-.70,-.08)),b+Vector((0,-.70,-.08)),b,'Gravel')
    PROBES['ramp']=[list(p+Vector((0,0,.015))) for p in RAMP]
    # Dış kenarda alçak toprak set; altındaki kaya eteği yamaca bağlanır.
    ramp_n=normal_at(RAMP,12)
    BACK['ramp_bank_quads']=[]
    for i,(a,b) in enumerate(zip(RAMP,RAMP[1:])):
        ea,eb=a-ramp_n*.9,b-ramp_n*.9
        da,db=ea-ramp_n*.48+Vector((0,0,.22)),eb-ramp_n*.48+Vector((0,0,.22))
        if i>=20: BACK['ramp_bank_quads'].append((len(geo.f),len(geo.v)))
        geo.quad(ea,da,db,eb,'Dirt',1.08)
        geo.quad(da,da-Vector((0,0,3.5)),db-Vector((0,0,3.5)),db,'Rock',.96)
    for i in range(2,23,3):
        p=RAMP[i]-ramp_n*1.25+Vector((0,0,.10))
        geo.rock(p,(.7,.42,.38),'Rock',RNG.uniform(.92,1.08),n=6)
    # Araç cebi; asfalt ile aynı yükseliş, vadiye doğru tam 5 m.
    for x in range(5,25):
        p,q=road_at_x(x),road_at_x(x+1)
        geo.quad((x,p.y-4,p.z-.01),(x,p.y-9,p.z-.01),
                 (x+1,q.y-9,q.z-.01),(x+1,q.y-4,q.z-.01),'Gravel',RNG.uniform(.94,1.04))
    # Dağ duvarı: arka sırası dağ gövdesiyle aynı fonksiyondan gelir, dikiş açılmaz.
    wall=[]
    for x in WALL_COLS:
        p=road_at_x(x)
        base=p.y+5.0
        back_z=top(x,35)
        climb=back_z-p.z
        jx=0 if abs(x)==CENTRAL else 1
        # Yoldan dik kalkan kaya yamacı: dört kırık nokta, arkası dağ gövdesine bağlanır.
        row=[Vector((x,base,p.z-.15))]
        for dy,f in ((2.2,.36),(5.5,.64),(11,.84),(20,.95)):
            y=base+dy+RNG.uniform(-.6,.6)
            z=p.z+climb*f+RNG.uniform(-1.2,1.2)
            if -12<=x<=17 and y<14: z=min(z,9.3)
            row.append(Vector((x+jx*RNG.uniform(-1.4,1.4),min(y,33.5),z)))
        row.append(Vector((x,35,back_z)))
        if abs(x)>=36:
            for k in range(1,5): row[k].z=min(row[k].z,max(p.z+.5,carve(row[k].x,row[k].y,5,2.2)))
        wall.append(row)
    for a,b in zip(wall,wall[1:]):
        for j in range(5): geo.quad(a[j],b[j],b[j+1],a[j+1],'Rock',RNG.uniform(.88,1.04))
    # Viraj içlerindeki çıkıntılar trafik uçlarını gizler; yol yüzeyine taşmaz.
    for s in (-1,1):
        geo.rock((s*42,22,6),(15,22,22),'Rock',1.0,n=9)
        geo.rock((s*37,39,8),(10,10,20),'Rock',.95,n=8)
        geo.rock((s*40,30,15),(12,12,12),'Rock',1.04,n=8)
    # Gevşek taş rafı: üst düzlem Z=10, derinlik 4 m.
    shelf=[(-10,9.5,10),(15,9.5,10),(16,12,10),(12,13.5,10),(-8,13.5,10),(-11,11,10)]
    geo.patch(shelf,[tuple(range(6))],'Rock')
    for a,b in zip(shelf,shelf[1:]+shelf[:1]):
        geo.patch([a,b,(2.5,12,7.9)],[(0,2,1)],'Rock',.98)
    geo.rock((-8,13,5),(7,8,12),'Rock',.97)
    geo.rock((12,14,5),(8,7,13),'Rock',1.06)
    # Alt kurtarma çıkıntısı; iç kenarı uçurum yüzüne gömülü, üstü düz.
    li,ri=path_outer(-4)-.4,path_outer(10)-.4
    ledge=[(-4,li,-14),(10,ri,-14),(10,ri-3.5,-14),(6,ri-4.0,-14),(-3,li-3.5,-14)]
    geo.patch(ledge,[tuple(reversed(range(5)))],'Rock',1.08)
    under=(3,(li+ri)/2+.6,-18)
    for a,b in zip(ledge,ledge[1:]+ledge[:1]): geo.patch([a,b,under],[(0,1,2)],'Rock',.91)
    PROBES['ledge']={'min':[-4,min(p[1] for p in ledge),-14.1],'max':[10,max(p[1] for p in ledge),-13.9]}
    # Yamaçta iri kaya kümeleri: yüzeye yarı gömülü, aralarında toprak; kaza hattı ve rampa boş.
    for k in range(19):
        x=-46+k*5.1+RNG.uniform(-1.2,1.2)
        if -38<x<-10.5 or -4<x<6.5: continue
        p=road_at_x(x)
        edge=p.y-4-(5 if 5<=x<=25 else 0)
        foot=path_inner(x) if -14<=x<=15 else edge-7.8
        ztop=p.z-.08
        down=Vector((0,foot-edge,-9-ztop)).normalized()
        out=Vector((0,down.z,-down.y))
        if out.z<0: out=-out
        t=RNG.uniform(.35,.55)
        size=Vector((RNG.uniform(3.4,4.8),RNG.uniform(2.8,3.6),RNG.uniform(3.2,4.4)))
        c=slope_point(x,t,edge,foot,ztop)-out*size.y*.22
        geo.rock(c,tuple(size),'Rock',RNG.uniform(.92,1.06),n=9)
        for m in range(RNG.choice((2,3,4))):
            dx=RNG.choice((-1,1))*RNG.uniform(.45,.75)*size.x
            dt=RNG.uniform(-.22,.18)
            s=RNG.uniform(.45,.7)
            c2=slope_point(x+dx,max(.18,min(.78,t+dt)),edge,foot,ztop)-out*size.y*s*.18
            geo.rock(c2,(size.x*s,size.y*s,size.z*s),'Rock',RNG.uniform(.88,1.08),n=7+m)
    # Rampanın üstündeki yamaç bandında kümeler; rampa yürüme bandına taşmaz.
    for x in (-31,-25.5,-20,-15.5):
        p=road_at_x(x)
        edge=p.y-4
        center=RAMP_START.lerp(RAMP_END,(x+35)/23)
        inner=center.y+.9
        room=edge-inner
        if room<2.6: continue
        size=min(3.4,room*.62)
        t=.45
        c=Vector((x,edge-room*t,(p.z-.08)+(center.z-(p.z-.08))*t))
        geo.rock(c-Vector((0,-.35,.55)),(size*1.25,size*.8,size*.95),'Rock',RNG.uniform(.9,1.05),n=8)
        geo.rock(c+Vector((size*.7,.2,-.7)),(size*.7,size*.55,size*.7),'Rock',RNG.uniform(.88,1.05),n=7)
    # Kaza ağzının iki yanında, yol kenarına yakın kümeler (02'deki gibi); kayma izi açık kalır.
    for x,t,sz in ((-6.5,.16,3.0),(-9,.3,2.4),(7.2,.14,2.8),(9,.3,2.2)):
        p=road_at_x(x)
        edge=p.y-4-(5 if 5<=x<=25 else 0)
        foot=path_inner(x)
        c=slope_point(x,t,edge,foot,p.z-.08)
        geo.rock(c+Vector((0,.4,-.5)),(sz*1.2,sz*.85,sz),'Rock',RNG.uniform(.9,1.06),n=8)
    # Dikişleri örten iri kayalar: merkez kesitlerin uçlarında yamaç ve uçurum.
    for s in (-1,1):
        x=s*CENTRAL
        p=road_at_x(x)
        edge=p.y-4
        geo.rock((x,edge-3.6,p.z-4.4),(7,6,7),'Rock',.98,n=8)
        geo.rock((x,edge-9.6,-24),(8,4.5,16),'Rock',.94,n=7)
    # Duvar eteği ve duvar yüzünde kümeler (yola taşmaz).
    for x in (-39,-28,-18,21,32,41):
        p=road_at_x(x)
        geo.rock((x,p.y+6,p.z+1.7),(RNG.uniform(2.6,4),2.7,4),'Rock',RNG.uniform(.91,1.08))
    for x in (-42,-35,-23,-16,-5,5,17,29,40):
        p=road_at_x(x)
        geo.rock((x,p.y+7.7,p.z+4),(RNG.uniform(3.5,5.5),4.5,RNG.uniform(6,9)),'Rock',RNG.uniform(.90,1.08))
    from mathutils.bvhtree import BVHTree
    wall_tree=BVHTree.FromPolygons([Vector(v) for v in geo.v],geo.f,all_triangles=True)
    for row in range(4):
        for i in range(24):
            x=RNG.uniform(-43,43)
            p=road_at_x(x)
            z=p.z+RNG.uniform(2.8,14)
            y=p.y+6.7+(z-p.z)*.24
            if row>=2:
                y=p.y+RNG.uniform(15,26)
                hit,_,_,_=wall_tree.ray_cast(Vector((x,y,70)),Vector((0,0,-1)),100)
                if hit: z=hit.z+.4
            if -12<x<17 and 7<y<15 and z>6: continue
            geo.rock((x,y,z),(RNG.uniform(5,9),RNG.uniform(4.1,6),RNG.uniform(5.5,9)),
                     'Rock',RNG.uniform(.86,1.13),n=8+i%3)
    # Patika altındaki uçurum alnı: üst üste binen iri bloklar.
    for x in (-40,-25,-5,15,30,43):
        y=path_outer(max(-14,min(15,x))) if -14<=x<=15 else road_at_x(x).y-12.3
        geo.rock((x,y-.4,-24),(RNG.uniform(5,8),3,RNG.uniform(9,14)),'Rock',.94)
    # Uçurum alnı: birbirine kaynayan dikey kaya sütunları (tek tek benek değil).
    for row in range(2):
        for i in range(30):
            x=-45+i*3.1+RNG.uniform(-.8,.8)
            outer=path_outer(x) if -14<=x<=15 else road_at_x(x).y-12.3
            z=(-19 if row==0 else -36)+RNG.uniform(-3,3)
            if -7<x<13 and z>-24: continue
            geo.rock((x,outer+.4-row*.8,z),(RNG.uniform(5.5,8),RNG.uniform(3.6,5),RNG.uniform(11,16)),
                     'Rock',RNG.uniform(.86,1.06),n=7+i%2)
    end_piece(-1,geo,back)
    end_piece(1,geo,back)
    back_piece(back)
    # Sırt üstünde yarı gömülü iri kayalar: kubbe yerine kayalık sırt silueti.
    for k in range(16):
        x=-44+k*5.6+RNG.uniform(-1.5,1.5)
        y=RNG.uniform(37,58)
        s_=RNG.uniform(6,10)
        back.rock((x,y,top(x,y)-s_*.15),(s_*1.2,s_,s_*.75),'Rock',RNG.uniform(.88,1.06),n=7)
    join_ramp_ends(geo)
    geo.finish()


def rail_section(geo,a,b,bent=0):
    direction=(b-a).normalized()
    n=Vector((-direction.y,direction.x,0)).normalized()
    # Gerçek W enkesiti, yaklaşık 31 cm levha yüksekliği.
    profile=[(-.15,.035),(-.085,-.035),(0,.035),(.085,-.035),(.15,.035)]
    rows=[]
    for i in range(4):
        t=i/3
        p=a.lerp(b,t)+n*bent*t*t+Vector((0,0,-abs(bent)*.3*t*t))
        rows.append([p+n*y+Vector((0,0,.60+z)) for z,y in profile])
    for row,next_row in zip(rows,rows[1:]):
        for j in range(4): geo.quad(row[j],next_row[j],next_row[j+1],row[j+1],'Metal')
        back=[p+n*.022 for p in row]
        next_back=[p+n*.022 for p in next_row]
        for j in range(4): geo.quad(back[j+1],next_back[j+1],next_back[j],back[j],'Metal')
        for j in (0,4): geo.quad(row[j],back[j],next_back[j],next_row[j],'Metal')
    for row in (rows[0],rows[-1]):
        for j in range(4): geo.quad(row[j],row[j+1],row[j+1]+n*.022,row[j]+n*.022,'Metal')


def make_guardrail():
    geo=Geo('SM_B1_Guardrail')
    pts=[p-normal_at(ROAD,i)*4.05 for i,p in enumerate(ROAD)]
    for i in range(0,len(pts)-4,4):
        a,b=pts[i],pts[i+4]
        x=(a.x+b.x)/2
        central=(a.y<8 and b.y<8)
        if central and ((-2<x<4) or (5<x<25) or (-36<x<-34)): continue
        rail_section(geo,a,b)
        geo.box(a+Vector((0,0,.355)),(.12,.14,.71),'Metal')
    # Kopan uçlar aşağı ve vadiye bükülü; arada açık 6 m kaza ağzı.
    for x,direction in [(-2,-1),(4,1)]:
        p=road_at_x(x)
        i=min(range(len(ROAD)),key=lambda k:(ROAD[k]-p).length)
        p=p-normal_at(ROAD,i)*4.05
        tangent=(ROAD[i+1]-ROAD[i-1]).normalized()
        start=p+tangent*direction*1.6
        rail_section(geo,start,p,1.5*direction)
        geo.box(start+Vector((0,0,.29)),(.13,.14,.68),'Metal',Matrix.Rotation(direction*.25,3,'Y'))
    # Cebin dış sınırında koruma; ağız boyunca yol erişimi açıktır.
    for x in range(5,25,2):
        a,b=road_at_x(x)+Vector((0,-9,0)),road_at_x(x+2)+Vector((0,-9,0))
        rail_section(geo,a,b)
        geo.box(a+Vector((0,0,.355)),(.12,.14,.71),'Metal')
    geo.finish()


LOOSE_RINGS=[
    [(-.5,.50),(-.28,.92),(.05,1),(.32,.78),(.5,.30)],
    [(-.5,.80),(-.2,1),(.2,.95),(.5,.62)],
    [(-.5,.42),(-.25,.85),(0,1),(.22,.9),(.38,.6),(.5,.18)],
    [(-.5,.95),(-.1,1),(.3,.75),(.5,.40)],
    [(-.5,.55),(-.15,1),(.15,.88),(.5,.50)],
    [(-.5,.35),(-.3,.78),(-.05,1),(.25,.82),(.5,.25)],
    [(-.5,.70),(-.18,.95),(.18,1),(.5,.70)],
    [(-.5,.60),(-.32,.90),(-.05,1),(.2,.92),(.4,.55),(.5,.12)],
]


def make_loose_and_props():
    for i in range(8):
        x=-8+i*3
        size=.65+i*.115
        geo=Geo(f'SM_B1_LooseRock_{i+1:02}')
        # Her kaya farklı halka profili, yüz sayısı ve oranlarla: sekiz ayrı şekil.
        geo.rock((x,10.8+RNG.uniform(-.7,.7),10+size*.5),(size*RNG.uniform(.8,1),size*RNG.uniform(.6,1),size*RNG.uniform(.55,.95)),
                 'Rock',RNG.uniform(.86,1.08),n=5+i,rings=LOOSE_RINGS[i])
        center=Vector(tuple(sum(v[k] for v in geo.v)/len(geo.v) for k in range(3)))
        longest=max(max(v[k] for v in geo.v)-min(v[k] for v in geo.v) for k in range(3))
        geo.v=[tuple(center+(Vector(v)-center)*size/longest) for v in geo.v]
        low=min(v[2] for v in geo.v)
        for k,v in enumerate(geo.v): geo.v[k]=(v[0],v[1],v[2]+10-low)
        pivot=((min(v[0] for v in geo.v)+max(v[0] for v in geo.v))/2,
               (min(v[1] for v in geo.v)+max(v[1] for v in geo.v))/2,10)
        geo.finish(pivot)
    # Dökülen eşya hattı yola göre: kırık noktadan minibüse.
    relative=[(0,-6.5),(1,-8.5),(2,-11),(-1,-14),(5.5,-14.5)]
    positions=[(x,road_at_x(x).y+dy,0) for x,dy in relative]
    from mathutils.bvhtree import BVHTree
    ground=bpy.data.objects['SM_B1_Ground']
    tree=BVHTree.FromPolygons([v.co for v in ground.data.vertices],
                             [tuple(p.vertices) for p in ground.data.polygons],all_triangles=True)
    for i,p in enumerate(positions):
        surface,_,_,_=tree.ray_cast(Vector((p[0],p[1],100)),Vector((0,0,-1)),200)
        if surface: p=(p[0],p[1],surface.z+.015)
        kind='Suitcase' if i<2 else 'Bag'
        geo=Geo(f'SM_B1_Prop_{kind}_{i+1:02}')
        dims=(.75,.35,.52) if i<2 else (.55,.32,.36)
        # Tek kutu yerine pahlanmış bagaj; 100–300 üçgen içinde.
        w,d,h=dims
        bevel=.05
        outline=[(-w/2+bevel,-d/2),(w/2-bevel,-d/2),(w/2,-d/2+bevel),(w/2,d/2-bevel),
                 (w/2-bevel,d/2),(-w/2+bevel,d/2),(-w/2,d/2-bevel),(-w/2,-d/2+bevel)]
        verts=[]
        for z,inset in [(0,True),(bevel,False),(h-bevel,False),(h,True)]:
            for x,y in outline:
                verts.append((p[0]+x*((w-2*bevel)/w if inset else 1),
                              p[1]+y*((d-2*bevel)/d if inset else 1),p[2]+z))
        faces=[tuple(reversed(range(8))),tuple(range(24,32))]
        for row in range(3):
            for j in range(8): faces.append((row*8+j,row*8+(j+1)%8,(row+1)*8+(j+1)%8,(row+1)*8+j))
        geo.patch(verts,faces,'Dirt' if i%2==0 else 'Grass',.75)
        for offset in (-dims[0]*.26,dims[0]*.26):
            geo.box((p[0]+offset,p[1],p[2]+dims[2]/2),(.04,dims[1]+.015,dims[2]+.01),'Metal',shade=.5)
        for offset in (-.11,.11): geo.box((p[0]+offset,p[1],p[2]+dims[2]+.06),(.035,.05,.13),'Asphalt')
        geo.box((p[0],p[1],p[2]+dims[2]+.12),(.24,.05,.04),'Asphalt')
        geo.finish(p)
    # Chevron'lar virajın dış (vadi) kenarında, yaklaşan trafiğe bakar; oklar dönüş yönünde.
    for side,name,targets in [(-1,'West',((-46.5,4.5),(-49.5,7.5))),(1,'East',((50.5,9.5),(54,15)))]:
        approach=road_at_x(side*28)
        for j,(tx,ty) in enumerate(targets):
            i=min(range(len(ROAD)),key=lambda k:(ROAD[k].x-tx)**2+(ROAD[k].y-ty)**2)
            p=ROAD[i]-normal_at(ROAD,i)*5.4
            _,zr=road_near(p.x,p.y)
            make_sign(f'Chevron_{name}_{j+1}',(p.x,p.y,zr-.12),False,approach,side)
    p=road_at_x(-20)
    make_sign('FallingRock',(-20,p.y+5.2,p.z),True,road_at_x(-8)+Vector((0,-1.5,0)),1)


def make_sign(name,position,triangle,look,side):
    geo=Geo('SM_B1_Prop_'+name)
    x,y,z=position
    base=Vector(position)
    facing=Vector((look.x-x,look.y-y,0)).normalized()
    right=Vector((-facing.y,facing.x,0))
    geo.box((x,y,z+1),(.085,.085,2),'Metal')
    def put(u,v,depth): return base+right*u+facing*depth+Vector((0,0,v))
    if triangle:
        # Tehlike üçgeni: kırmızı çerçeve, beyaz zemin, siyah düşen kaya sembolü; yazı yok.
        geo.patch([put(.66,1.58,.05),put(-.66,1.58,.05),put(0,2.72,.05)],[(0,1,2)],'Metal',.6)
        geo.patch([put(-.66,1.58,.06),put(.66,1.58,.06),put(0,2.72,.06)],[(0,1,2)],'Sign')
        geo.patch([put(-.48,1.68,.07),put(.48,1.68,.07),put(0,2.51,.07)],[(0,1,2)],'LineWhite')
        geo.patch([put(-.34,1.75,.08),put(-.08,1.75,.08),put(-.34,2.15,.08)],[(0,1,2)],'Asphalt')
        for du,dv,s in [(.05,2.02,.07),(.15,1.90,.09),(.12,1.78,.06)]:
            geo.patch([put(du-s,dv-s,.085),put(du+s,dv-s,.085),put(du+s,dv+s,.085),put(du-s,dv+s,.085)],
                      [(0,1,2,3)],'Asphalt')
    else:
        # Viraj levhası: kırmızı zemin, beyaz oklar dönüş yönüne bakar; arka yüz metal.
        w,h=1.1,.68
        geo.patch([put(w/2,1.8-h/2,.04),put(-w/2,1.8-h/2,.04),put(-w/2,1.8+h/2,.04),put(w/2,1.8+h/2,.04)],[(0,1,2,3)],'Metal',.6)
        geo.patch([put(-w/2,1.8-h/2,.05),put(w/2,1.8-h/2,.05),put(w/2,1.8+h/2,.05),put(-w/2,1.8+h/2,.05)],[(0,1,2,3)],'Sign')
        sign_dir=-side  # sürücünün sağına (+1) / soluna (-1)
        for offset in (-.27,.24):
            coords=[(offset-.17,-.26),(offset+.03,-.26),(offset+.24,0),(offset+.03,.26),(offset-.17,.26),(offset+.04,0)]
            pts=[put(sign_dir*du,1.8+dv,.06) for du,dv in coords]
            faces=[(0,1,2,5),(5,2,3,4)] if sign_dir>0 else [(5,2,1,0),(4,3,2,5)]
            geo.patch(pts,faces,'LineWhite')
    geo.finish(position)


def grass_overlaps_ramp(verts,faces):
    # Tutamın XY izdüşümünü yürüyüş dikdörtgenine kırp; dışarıdaki tutamlar korunur.
    delta=RAMP_END-RAMP_START
    length=math.hypot(delta.x,delta.y)
    forward=Vector((delta.x/length,delta.y/length,0))
    lateral=Vector((-forward.y,forward.x,0))
    points=[((Vector(v)-RAMP_START).dot(forward),
             (Vector(v)-RAMP_START).dot(lateral)) for v in verts]
    for face in faces:
        polygon=[points[i] for i in face]
        for axis,bound,sign in ((0,-1.,1),(0,length+1.,-1),(1,-.9,1),(1,.9,-1)):
            clipped=[]
            for a,b in zip(polygon,polygon[1:]+polygon[:1]):
                da,db=(a[axis]-bound)*sign,(b[axis]-bound)*sign
                if da>=0: clipped.append(a)
                if (da>=0)!=(db>=0):
                    t=da/(da-db)
                    clipped.append(tuple(a[k]+t*(b[k]-a[k]) for k in range(2)))
            polygon=clipped
            if not polygon: break
        if polygon: return True
    return False


def make_vegetation():
    geo=Geo('SM_B1_Vegetation')
    spots=[]
    for i in range(60):
        x=RNG.uniform(-43,43)
        p=road_at_x(x)
        spots.append((x,p.y+5.15+RNG.uniform(.15,.8),p.z+.08))
    for i in range(16):
        x=RNG.uniform(-13,14)
        spots.append((x,path_outer(x)-.12,-9.1))
    tree=BACK['vegetation_ground_tree']
    # Vadi kenarında korkuluk altı ve rampa seti boyunca seyrek tutamlar (01'deki yeşil şeritler).
    candidates=[]
    for i in range(30):
        x=RNG.uniform(-44,44)
        if -37<x<-33 or -3<x<8 or 4<x<26: continue
        p=road_at_x(x)
        candidates.append((x,p.y-4.6-RNG.uniform(0,.8)))
    ramp_n=normal_at(RAMP,12)
    for i in range(2,23,2):
        q=RAMP[i]-ramp_n*1.6
        candidates.append((q.x+RNG.uniform(-.4,.4),q.y))
    for x,y in candidates:
        hit,normal,_,_=tree.ray_cast(Vector((x,y,60)),Vector((0,0,-1)),200)
        if hit and normal.z>.45: spots.append((x,y,hit.z-.02))
    # Yolun üstündeki yamaçta kaya araları: düzensiz, seyrek tutam kümeleri (01 yoğunluğu).
    for k in range(30):
        cx=RNG.uniform(-44,44)
        cy=road_at_x(cx).y+RNG.uniform(7,30)
        for m in range(RNG.choice((1,2,3,4))):
            x,y=cx+RNG.uniform(-1.8,1.8),min(34.5,cy+RNG.uniform(-1.4,1.4))
            hit,normal,_,_=tree.ray_cast(Vector((x,y,90)),Vector((0,0,-1)),200)
            if hit and normal.z>.5: spots.append((x,y,hit.z-.02))
    removed=[]
    for i,p in enumerate(spots):
        vertex_start,face_start=len(geo.v),len(geo.f)
        scale=RNG.uniform(.30,.75)
        if i%4==0:
            geo.rock(Vector(p)+Vector((0,0,scale*.37)),(scale*1.2,scale,scale*.8),'Grass',RNG.uniform(.8,1.1))
        else:
            for j in range(5):
                a=RNG.uniform(0,2*math.pi)
                direction=Vector((math.cos(a),math.sin(a),0))
                cross=Vector((-direction.y,direction.x,0))*.06
                base=Vector(p)+direction*.1
                tip=base+direction*scale*.4+Vector((0,0,scale*RNG.uniform(.5,1.1)))
                geo.patch([base-cross,base+cross,tip,base+Vector((0,0,.025))],
                          [(0,1,2),(0,2,3),(1,3,2)],'Grass',RNG.uniform(.8,1.1))
        # Rastgele sayı akışını ve tutam indekslerini değiştirmeden yalnız örtüşeni çıkar.
        faces=[tuple(index-vertex_start for index in face) for face in geo.f[face_start:]]
        if grass_overlaps_ramp(geo.v[vertex_start:],faces):
            removed.append({'index':i,'position':list(p),
                            'vertices':len(geo.v)-vertex_start,'triangles':len(faces)})
            del geo.v[vertex_start:]
            del geo.f[face_start:]
            del geo.mat[face_start:]
            del geo.tint[face_start:]
    BACK['ramp_grass_exclusion']={'half_width_m':.9,'end_extension_m':1.,
                                  'removed_tufts':len(removed),'remaining_tufts':len(spots)-len(removed),
                                  'removed':removed}
    geo.finish()


def place_towtruck():
    # Ayrı asset: geometri, parça pivotları ve dinlenme pozu değiştirilmez.
    from mathutils.bvhtree import BVHTree
    source=ROOT/'Export/TowTruck/SM_TowTruck.fbx'
    meta=json.loads((source.parent/'towtruck_meta.json').read_text(encoding='utf-8'))
    bpy.ops.import_scene.fbx(filepath=str(source),use_anim=False)
    root=bpy.data.objects['SM_TowTruck']
    slot=road_at_x(15)+Vector((0,-6.5,0))
    ground=bpy.data.objects['SM_B1_Ground']
    tree=BVHTree.FromPolygons([ground.matrix_world@v.co for v in ground.data.vertices],
                             [tuple(p.vertices) for p in ground.data.polygons],all_triangles=True)
    wheels=[bpy.data.objects['SM_TowTruck_Wheel_'+axle+'_'+side]
            for axle in ('F','R1','R2') for side in ('L','R')]
    # Banket enine düz; dingillerin örneklediği boyuna eğime en küçük karelerle uy.
    xs=[slot.x-meta['parts'][obj.name]['pivot_m'][1] for obj in wheels]
    zs=[tree.ray_cast(Vector((x,slot.y,100)),Vector((0,0,-1)),200)[0].z for x in xs]
    slope=float(np.linalg.lstsq(np.array([[x-slot.x,1] for x in xs]),np.array(zs),rcond=None)[0][0])
    root.rotation_mode='XYZ'
    root.rotation_euler=(-math.atan(slope),0,math.pi/2)
    root.location=(slot.x,slot.y,0)
    bpy.context.view_layer.update()
    # Altı lastiğin tamamında yüzeyin altına giren köşe kalmayacak en küçük Z.
    offsets=[]
    for obj in wheels:
        for vertex in obj.data.vertices:
            p=obj.matrix_world@vertex.co
            surface,_,_,_=tree.ray_cast(Vector((p.x,p.y,100)),Vector((0,0,-1)),200)
            if surface is None: raise RuntimeError('Çekici tekeri banket dışında: '+obj.name)
            offsets.append(surface.z-p.z)
    root.location.z=max(offsets)
    bpy.context.view_layer.update()
    # Kaynak betikteki lineer malzemeleri sunum sahnesinde yeniden kur.
    for name,color in meta['materials'].items():
        mat=bpy.data.materials[name]
        rgb=tuple(linear(int(color[i:i+2],16)/255) for i in (1,3,5))
        shader=mat.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Base Color'].default_value=(*rgb,1)
        mat.diffuse_color=(*rgb,1)
        shader.inputs['Roughness'].default_value=.62
        shader.inputs['Specular IOR Level'].default_value=.25
        if name=='M_TowTruck_Glass':
            shader.inputs['Transmission Weight'].default_value=.88
            shader.inputs['Roughness'].default_value=.075
            shader.inputs['IOR'].default_value=1.08
            shader.inputs['Alpha'].default_value=meta['glass_alpha']
            mat.diffuse_color=(*rgb,meta['glass_alpha'])
    PLACEMENTS['TowTruckPlacement']={
        'asset':'Export/TowTruck/SM_TowTruck.fbx','root_object':root.name,
        'coordinate_system':'Blender: X east, Y mountain, Z up; meters',
        'position':list(root.location),'rotation_quaternion_wxyz':list(root.rotation_euler.to_quaternion()),
        'rotation_euler_degrees':[math.degrees(v) for v in root.rotation_euler],
        'euler_order':'XYZ','scale':list(root.scale),
        'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest()}


def place_minivan():
    # Ayrı asset: MODULES listesine eklenmez, yalnız sunum sahnesinde bulunur.
    source=ROOT/'Export/Minivan/SM_Minivan.fbx'
    meta=json.loads((source.parent/'minivan_meta.json').read_text(encoding='utf-8'))
    bpy.ops.import_scene.fbx(filepath=str(source),use_anim=False)
    root=bpy.data.objects['SM_Minivan']
    root.rotation_mode='XYZ'
    root.rotation_euler=(math.radians(28),0,0)
    bpy.context.view_layer.update()
    rear=[bpy.data.objects['SM_Minivan_Wheel_'+side] for side in ('RL','RR')]
    points=[obj.matrix_world@v.co for obj in rear for v in obj.data.vertices]
    bottom=min(p.z for p in points)
    contacts=[p for p in points if p.z<bottom+1e-5]
    # İki tekerin de tabanı kenarın en az 12 cm içinde; Z gerçek mesh'ten.
    root.location=(4,max(path_outer(4+p.x)+.12-p.y for p in contacts),-9-bottom)
    root['nose_down_degrees']=28
    bpy.context.view_layer.update()
    # Sunum için kaynak betiğin malzeme ayarları (pürüzlülük, cam) hex'ten yeniden kurulur.
    for name,color in meta['materials'].items():
        mat=bpy.data.materials[name]
        rgb=tuple(linear(int(color[i:i+2],16)/255) for i in (1,3,5))
        shader=mat.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Base Color'].default_value=(*rgb,1)
        mat.diffuse_color=(*rgb,1)
        shader.inputs['Roughness'].default_value=.62
        shader.inputs['Specular IOR Level'].default_value=.25
        if name=='M_Minivan_Glass':
            shader.inputs['Transmission Weight'].default_value=.88
            shader.inputs['Roughness'].default_value=.075
            shader.inputs['IOR'].default_value=1.08
            shader.inputs['Alpha'].default_value=meta['glass_alpha']
            mat.diffuse_color=(*rgb,meta['glass_alpha'])
    PLACEMENTS['MinivanPlacement']={
        'asset':'Export/Minivan/SM_Minivan.fbx','root_object':root.name,
        'coordinate_system':'Blender: X east, Y mountain, Z up; meters',
        'position':list(root.location),'rotation_quaternion_wxyz':list(root.rotation_euler.to_quaternion()),
        'rotation_euler_degrees': [math.degrees(v) for v in root.rotation_euler],
        'euler_order':'XYZ','scale':list(root.scale),
        'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest()}


def mountain(geo,center,radius,height,main=False,streak=11):
    n=40 if main else 24
    angles=[2*math.pi*i/n+RNG.uniform(-.1,.1) for i in range(n)]
    rings=[]
    steps=20 if main else 10
    for k in range(steps+1):
        z=k/steps
        r=max(.001,(1-z)**1.35)
        row=[]
        for i,a in enumerate(angles):
            rr=radius*r*(1+.13*math.sin(a*7)+.08*math.sin(a*13+.3))*(1+.035*math.sin(k*1.9+i*2.1))
            row.append((center[0]+math.cos(a)*rr*(1 if main else 1.7)+math.sin(z*math.pi)*7,
                        center[1]+math.sin(a)*rr*(1 if main else .6),
                        center[2]+z*height))
        rings.append(row)
    for k in range(steps):
        for i in range(n):
            mat='Gravel' if main else 'Backdrop'
            shade=(1.22 if main else 1)+.06*math.sin(i*1.4+k*.4)
            # Koyu kaya çizgileri: zirveden aşağı kıvrılan şeritler.
            if main and (i+int(1.8*math.sin(k*.35)))%streak==2 and k>3:
                mat='Backdrop'; shade=.55
            geo.quad(rings[k][i],rings[k][(i+1)%n],rings[k+1][(i+1)%n],rings[k+1][i],mat,shade)


def make_backdrop():
    geo=BACK['geo']
    # Zirveler dağ gövdesine gömülü tabandan yükselir; ikinci zirve sağda ve küçük (04).
    # Kullanıcı kararı (2026-10-13): zirve ~y+400'e, uzak dağ ölçeğinde; ikinci küçük zirve sağında.
    mountain(geo,PEAK['main'][0],PEAK['main'][1],PEAK['main'][2],True,9)
    mountain(geo,PEAK['second'][0],PEAK['second'][1],PEAK['second'][2],True,7)
    # Vadinin karşısında, sisin içinden yükselen soluk uzak sırtlar.
    for x,y,r,h in [(-300,-340,105,92),(-110,-390,100,84),(90,-360,105,105),(290,-320,115,96)]:
        mountain(geo,(x,y,-80),r,h)
    obj=geo.finish()
    obj['environment_extension']=True


def empty(name,position,display='PLAIN_AXES',size=1):
    obj=bpy.data.objects.new(name,None)
    bpy.context.collection.objects.link(obj)
    obj.location=position
    obj.empty_display_type=display
    obj.empty_display_size=size
    return obj


def make_markers():
    markers={}
    def add(name,position,**extras):
        empty(name,position)
        markers[name]={'position':list(position),**extras}
    add('PlayerSpawn',road_at_x(-45)+Vector((0,-1.75,.15)))
    add('RecoveryTruckSlot',road_at_x(15)+Vector((0,-6.5,0)))
    for name,side,reverse in [('TrafficLane_EastBound',-1,False),('TrafficLane_WestBound',1,True)]:
        points=resample([p+normal_at(ROAD,i)*side*1.75 for i,p in enumerate(ROAD)],1)
        if reverse: points.reverse()
        add(name,(0,0,0),polyline=[list(p) for p in points],spacing_m=1,direction='East' if not reverse else 'West')
        bpy.data.objects[name]['polyline_json']=json.dumps(markers[name]['polyline'])
        if not reverse:
            add('TrafficSpawn_West',points[0]); add('TrafficDespawn_East',points[-1])
        else:
            add('TrafficSpawn_East',points[0]); add('TrafficDespawn_West',points[-1])
    add('FallRespawnPlane',(0,0,-60),bounds={'min':[-60,-45,-60],'max':[60,35,-60]})
    add('LedgeZone',(3,-21,-14),bounds=PROBES['ledge'])
    entry=PLACEMENTS['MinivanPlacement']
    add('MinivanPlacement',**entry)
    bpy.data.objects['MinivanPlacement'].rotation_euler=bpy.data.objects['SM_Minivan'].rotation_euler
    add('TowTruckPlacement',**PLACEMENTS['TowTruckPlacement'])
    bpy.data.objects['TowTruckPlacement'].rotation_euler=bpy.data.objects['SM_TowTruck'].rotation_euler
    data={'schema_version':1,'units':'meters','coordinate_system':'Blender: X east, Y mountain, Z up',
          'markers':markers,'placements':PLACEMENTS,'geometry_probes':PROBES,
          'road_control_points':CONTROL,'no_collider':['SM_B1_Backdrop'],
          'fbx_assembly':'Static modules world origin; loose rocks/props keep listed world placement.'}
    (OUT/'markers.json').write_text(json.dumps(data,indent=2),encoding='utf-8')


def camera(name,location,target,lens,ortho=None):
    data=bpy.data.cameras.new(name)
    obj=bpy.data.objects.new(name,data)
    bpy.context.collection.objects.link(obj)
    obj.location=location
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    data.lens=lens
    data.clip_end=2000
    if ortho: data.type='ORTHO'; data.ortho_scale=ortho
    return obj


def setup_render(round_number):
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.samples=64
    scene.cycles.use_denoising=True
    scene.cycles.seed=SEED
    scene.cycles.use_animated_seed=False
    scene.cycles.max_bounces=6
    scene.cycles.volume_bounces=1
    scene.cycles.adaptive_threshold=.03
    prefs=bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type='OPTIX'
    prefs.refresh_devices()
    gpu=[d for d in prefs.devices if d.type=='OPTIX']
    for d in prefs.devices: d.use=d.type=='OPTIX'
    scene.cycles.device='GPU' if gpu else 'CPU'
    scene['render_device']='OPTIX: '+', '.join(d.name for d in gpu) if gpu else 'CPU'
    scene.render.resolution_x=1920
    scene.render.resolution_y=1080
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.view_settings.view_transform='AgX'
    scene.view_settings.look='AgX - Medium High Contrast'
    scene.view_settings.exposure=.85
    world=bpy.data.worlds.new('Sunset_Orange_Purple')
    scene.world=world
    world.use_nodes=True
    nodes=world.node_tree.nodes
    links=world.node_tree.links
    bg=nodes.get('Background')
    bg.inputs['Strength'].default_value=.65
    tex=nodes.new('ShaderNodeTexCoord')
    sep=nodes.new('ShaderNodeSeparateXYZ')
    remap=nodes.new('ShaderNodeMapRange')
    remap.inputs['From Min'].default_value=.08
    remap.inputs['From Max'].default_value=-.7
    ramp=nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].color=(.72,.27,.075,1)
    ramp.color_ramp.elements[1].color=(.09,.055,.19,1)
    ramp.color_ramp.elements.new(.28).color=(.42,.16,.18,1)
    links.new(tex.outputs['Normal'],sep.inputs[0]); links.new(sep.outputs['Z'],remap.inputs[0])
    links.new(remap.outputs[0],ramp.inputs[0]); links.new(ramp.outputs[0],bg.inputs[0])
    ambient=nodes.new('ShaderNodeBackground')
    ambient.inputs[0].default_value=(.47,.56,.76,1)
    ambient.inputs[1].default_value=.32
    ray=nodes.new('ShaderNodeLightPath')
    mix=nodes.new('ShaderNodeMixShader')
    links.new(ray.outputs['Is Camera Ray'],mix.inputs[0])
    links.new(ambient.outputs[0],mix.inputs[1]); links.new(bg.outputs[0],mix.inputs[2])
    links.new(mix.outputs[0],nodes.get('World Output').inputs['Surface'])
    light=bpy.data.lights.new('Sun_Golden_10deg','SUN')
    obj=bpy.data.objects.new(light.name,light); bpy.context.collection.objects.link(obj)
    light.energy=4.5
    light.color=(1,.80,.54)
    light.angle=math.radians(3)
    direction=Vector((-.7,.7,-math.tan(math.radians(10))))
    obj.rotation_euler=direction.to_track_quat('-Z','Y').to_euler()
    # Üstü -40 m çevresinde dalgalanan, vadiye sınırlı render hacmi.
    fog_geo=Geo('RENDER_FogSea')
    fog_geo.box((0,-450,-112),(1900,1000,180),'Backdrop')
    fog=fog_geo.finish(export=False)
    fog.data.materials.clear()
    mat=bpy.data.materials.new('RENDER_FogVolume'); mat.use_nodes=True
    ns=mat.node_tree.nodes; ns.clear()
    output=ns.new('ShaderNodeOutputMaterial')
    vol=ns.new('ShaderNodeVolumePrincipled')
    vol.inputs['Density'].default_value=.055
    vol.inputs['Color'].default_value=FOG['color']
    vol.inputs['Anisotropy'].default_value=FOG['anisotropy']
    vol.inputs['Emission Strength'].default_value=.003
    vol.inputs['Emission Color'].default_value=FOG['emission_color']
    coords=ns.new('ShaderNodeTexCoord')
    noise=ns.new('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value=.018
    noise.inputs['Detail'].default_value=3
    mat.node_tree.links.new(coords.outputs['Object'],noise.inputs['Vector'])
    puff=ns.new('ShaderNodeTexNoise')
    puff.inputs['Scale'].default_value=.05
    puff.inputs['Detail'].default_value=2
    mat.node_tree.links.new(coords.outputs['Object'],puff.inputs['Vector'])
    sep=ns.new('ShaderNodeSeparateXYZ'); mat.node_tree.links.new(coords.outputs['Object'],sep.inputs[0])
    def mathnode(operation,a,b):
        node=ns.new('ShaderNodeMath'); node.operation=operation
        for i,v in enumerate((a,b)):
            if isinstance(v,(int,float)): node.inputs[i].default_value=v
            else: mat.node_tree.links.new(v,node.inputs[i])
        return node.outputs[0]
    # Kabarık bulut denizi: iki ölçekli gürültüyle dalgalı üst yüzey, yumuşak geçiş bandı.
    cloudtop=mathnode('ADD',mathnode('ADD',mathnode('MULTIPLY',noise.outputs['Fac'],FOG['swell']),
                      mathnode('MULTIPLY',puff.outputs['Fac'],FOG['puff'])),FOG['base'])
    depth=mathnode('SUBTRACT',cloudtop,sep.outputs['Z'])
    fade=mathnode('MINIMUM',mathnode('MAXIMUM',mathnode('DIVIDE',depth,FOG['soft']),0),1)
    valley=mathnode('MINIMUM',mathnode('MAXIMUM',mathnode('DIVIDE',mathnode('SUBTRACT',20,sep.outputs['Y']),30),0),1)
    density=mathnode('MULTIPLY',mathnode('MULTIPLY',fade,valley),FOG['density'])
    mat.node_tree.links.new(density,vol.inputs['Density'])
    mat.node_tree.links.new(mathnode('MULTIPLY',density,FOG['glow']),vol.inputs['Emission Strength'])
    mat.node_tree.links.new(vol.outputs['Volume'],output.inputs['Volume'])
    fog.data.materials.append(mat)
    for name,(location,target,lens,ortho) in CAMERAS.items(): camera(name,location,target,lens,ortho)
    scene.camera=bpy.data.objects['CAM_Genel']


# Sunum ayarları (FBX geometrisine girmez).
FOG={'color':(.95,.84,.90,1),'emission_color':(1.0,.60,.62,1),'anisotropy':.3,
     'swell':18,'puff':10,'base':-52,'soft':5,'density':.22,'glow':.12}
def CAM_FROM(location,yaw,pitch,lens):
    # Konum + yön açısı/eğim (derece) → hedef noktası; kadraj izdüşümle ayarlandı.
    d=Vector((math.cos(math.radians(yaw)),math.sin(math.radians(yaw)),math.tan(math.radians(pitch))))
    return (location,tuple(Vector(location)+d*50),lens,None)


PEAK={'main':((-180,420,-20),110,170),'second':((-135,470,-10),62,115)}
CAMERAS={'CAM_Genel':((38.1,-39.3,22.1),(4,-19.6,-8.7),18,None),  # hedef: minibüs gövde merkezi (izdüşüm + görünürlük ışını taraması)
         'CAM_Plan':((0,-65,180),(0,6,-2),48,175),
         'CAM_Yakin':((-20,-30,16),(1,-12,-5),20,None),
         'CAM_Levha':CAM_FROM((8,-3,2.2),170,-1.5,70)}


def geometry_digest():
    signature=[]
    for obj in sorted(MODULES,key=lambda o:o.name):
        signature.append({'name':obj.name,'location':[round(c,7) for c in obj.location],
                          'vertices':[[round(c,7) for c in v.co] for v in obj.data.vertices],
                          'faces':[list(p.vertices) for p in obj.data.polygons],
                          'materials':[p.material_index for p in obj.data.polygons]})
    return hashlib.sha256(json.dumps(signature,separators=(',',':')).encode()).hexdigest()


def export_modules():
    # Malzemeler lineer Base Color taşır; FBX olduğu gibi yazılır (Unity Linear URP gamma'yı kendisi uygular).
    for obj in MODULES:
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active=obj
        bpy.ops.export_scene.fbx(filepath=str(OUT/(obj.name+'.fbx')),use_selection=True,
            object_types={'MESH'},apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',
            mesh_smooth_type='FACE',use_mesh_modifiers=False,bake_anim=False,
            use_custom_props=True,check_existing=False,colors_type='SRGB')


def compare(render_path,reference_path,target):
    # Referans ve gerçek render yan yana; yeniden renk derecelendirme uygulanmaz.
    canvas=np.ones((1080,3840,4),dtype=np.float32)
    for x,path in [(0,reference_path),(1920,render_path)]:
        img=bpy.data.images.load(str(path),check_existing=False)
        img.scale(1920,1080)
        canvas[:,x:x+1920,:]=np.array(img.pixels[:],dtype=np.float32).reshape(1080,1920,4)
        bpy.data.images.remove(img)
    img=bpy.data.images.new('Reference_Left_Render_Right',3840,1080,alpha=True)
    img.pixels.foreach_set(canvas.ravel())
    img.filepath_raw=str(target); img.file_format='PNG'; img.save()
    bpy.data.images.remove(img)


def render_ramp_ends(prefix=''):
    # Önce/sonra aynı kamera ve aynı Cycles ayarlarıyla karşılaştırılır.
    scene=bpy.context.scene
    for label,location,target in [('top',(-40,-18,6),(-34.8,-7.6,-1.5)),
                                  ('bottom',(-6,-20,-1),(-12.2,-11,-8.5))]:
        name='CAM_Ramp_'+label
        scene.camera=bpy.data.objects.get(name) or camera(name,location,target,50,8)
        scene.render.filepath=str(OUT/'renders'/('ramp_'+prefix+label+'.png'))
        bpy.ops.render.render(write_still=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--round',type=int,default=0)
    parser.add_argument('--skip-renders',action='store_true')
    parser.add_argument('--ramp-renders-only',action='store_true')
    parser.add_argument('--cpu-render',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    OUT.mkdir(parents=True,exist_ok=True)
    (OUT/'renders').mkdir(exist_ok=True)
    (OUT/'cache').mkdir(exist_ok=True)
    os.environ['OPTIX_CACHE_PATH']=str(OUT/'cache')
    os.environ['CUDA_CACHE_PATH']=str(OUT/'cache')
    os.chdir(OUT)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.context.preferences.filepaths.use_auto_save_temporary_files=False
    bpy.context.preferences.filepaths.file_preview_type='NONE'
    bpy.context.scene.unit_settings.system='METRIC'
    bpy.context.scene.unit_settings.scale_length=1
    previous=json.loads((OUT/'stats.json').read_text(encoding='utf-8')) if (OUT/'stats.json').exists() else {}
    started=time.perf_counter()
    palette=sample_palette()
    make_road(); make_terrain(); make_guardrail(); make_loose_and_props()
    make_vegetation(); make_backdrop(); place_minivan(); place_towtruck(); make_markers()
    setup_render(args.round)
    if args.cpu_render:
        bpy.context.scene.cycles.device='CPU'
        bpy.context.scene['render_device']='CPU'
    digest=geometry_digest()
    placement_digest=hashlib.sha256(json.dumps(PLACEMENTS['MinivanPlacement'],sort_keys=True).encode()).hexdigest()
    towtruck_digest=hashlib.sha256(json.dumps(PLACEMENTS['TowTruckPlacement'],sort_keys=True).encode()).hexdigest()
    counts={o.name:len(o.data.polygons) for o in MODULES}
    props=sum(v for k,v in counts.items() if '_Prop_' in k or '_LooseRock_' in k)
    backdrop=counts['SM_B1_Backdrop']
    playable=sum(counts.values())-props-backdrop
    assert playable<=60000 and backdrop<=30000
    export_modules()
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'B1_Ucurum.blend'))
    render_started=time.perf_counter()
    durations={}
    if not args.skip_renders:
        views=[] if args.ramp_renders_only else [('Genel','01_style_main_v2_genel.jpg'),('Plan','03_layout_topdown_v2.jpg'),('Yakin','02_style_closeup.jpg'),('Levha',None)]
        for name,ref in views:
            ts=time.perf_counter()
            scene=bpy.context.scene
            scene.camera=bpy.data.objects['CAM_'+name]
            path=OUT/'renders'/('CAM_'+name+'.png')
            scene.render.filepath=str(path)
            bpy.ops.render.render(write_still=True)
            durations[name]=round(time.perf_counter()-ts,3)
            if ref: compare(path,REF/ref,OUT/'renders'/('compare_'+name.lower()+'.png'))
        render_ramp_ends()
        if args.round:
            archive=OUT/'renders'/f'r6_round_{args.round:02}'
            archive.mkdir(exist_ok=True)
            for path in [*(OUT/'renders').glob('CAM_*.png'),*(OUT/'renders').glob('compare_*.png')]: shutil.copy2(path,archive/path.name)
    stats={'seed':SEED,'geometry_sha256':digest,'previous_geometry_sha256':previous.get('geometry_sha256'),
           'minivan_placement_sha256':placement_digest,
           'towtruck_placement_sha256':towtruck_digest,
           'matches_previous_towtruck_placement':previous.get('towtruck_placement_sha256')==towtruck_digest,
           'matches_previous_placement':previous.get('minivan_placement_sha256')==placement_digest,
           'matches_previous_geometry':previous.get('geometry_sha256')==digest,
           'consecutive_identical_builds':previous.get('consecutive_identical_builds',1)+1 if previous.get('geometry_sha256')==digest else 1,
           'ramp_grass_exclusion':BACK['ramp_grass_exclusion'],
           'object_triangles':counts,'playable_triangles':playable,'backdrop_triangles':backdrop,
           'prop_triangles':props,'total_triangles':sum(counts.values()),
           'budgets':{'playable':60000,'backdrop':30000,'luggage_each':[100,300]},
           'palette':palette,'render_device':bpy.context.scene['render_device'],'samples':64,
           'render_seconds':round(time.perf_counter()-render_started,3),'camera_render_seconds':durations,
           'total_build_seconds':round(time.perf_counter()-started,3),'visual_round':args.round,
           'sign_colors':'Tehlike üçgeni: beyaz zemin, kırmızı çerçeve, siyah sembol. Chevron: kırmızı zemin, beyaz ok.'}
    (OUT/'stats.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
    print('BUILD OK',digest,json.dumps({'playable':playable,'backdrop':backdrop,'props':props,'render_seconds':stats['render_seconds']}),flush=True)


if __name__=='__main__':
    try: main()
    except Exception:
        import traceback
        traceback.print_exc()
        sys.stdout.flush(); sys.stderr.flush()
        sys.exit(1)
