"""Deterministik Patient üretimi. Blender 5.2, metre, Z yukarı / -Y ön.
Yalnız Export/Patient yazar. --round N, --no-render geliştirme seçenekleri.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import sys
import time
import traceback
sys.dont_write_bytecode = True
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix, Quaternion
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Export/Patient'
CREW = ROOT / 'Export/RescueCrew/SK_RescueCrew_Blue.fbx'
REF = ROOT / 'ArtSource/Patient/references'
PREFIX = 'mixamorig:'
PI = math.pi
MATERIALS = {}
PALETTE = dict(Skin='BD865A', SkinLight='CD9670', Wrinkle='91603F', Hair='49352C', GrayHair='BAB9B5',
    White='F2EFE5', Pupil='151412', Mouth='59382A', Turquoise='328F8B', Pink='E67BA2', Orange='F1AB4D',
    Khaki='A69A64', Hat='CDBB92', HatBand='A48D69', Brown='735341', DarkBrown='523D31',
    Green='56864A', GreenTrim='426D3B', Denim='526C87', GrayShoe='888887', Sole='B9B6AE',
    Purple='8B68A1', PurpleDark='684880', Tee='D5C9B2', GrayPants='777A7C', Visor='477EB8',
    Yellow='EBCB43', Camera='ACA89D', Lens='333743', Strap='947757', Watch='655348')
VARIANTS = ('Tourist','Civilian','Elder')
ROUND = 1


def srgb(v):
    return v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4


def materials():
    for name, color in PALETTE.items():
        mat=bpy.data.materials.new('M_Patient_'+name)
        mat.use_nodes=True
        rgb=tuple(srgb(int(color[i:i+2],16)/255) for i in (0,2,4))
        mat.diffuse_color=(*rgb,1)
        shader=mat.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Base Color'].default_value=(*rgb,1)
        shader.inputs['Roughness'].default_value=.66
        shader.inputs['Specular IOR Level'].default_value=.25
        MATERIALS[name]=mat

class Geometry:
    def __init__(self, name):
        self.name, self.vertices, self.faces, self.face_materials = name, [], [], []
        self.weights, self.tags = [], {}

    def add(self, vertices, faces, mat, weights='Head', tag=None):
        offset = len(self.vertices)
        self.vertices.extend(vertices)
        self.faces.extend(tuple(offset+i for i in f) for f in faces)
        self.face_materials.extend(mat(tuple(sum(vertices[i][axis] for i in f)/len(f) for axis in range(3))) if callable(mat) else mat for f in faces)
        self.weights.extend(weights(p) if callable(weights) else {weights: 1} for p in vertices)
        if tag:
            self.tags.setdefault(tag, []).extend(range(offset, len(self.vertices)))

    def sphere(self, center, scale, mat, weights='Head', segments=16, rings=8, tag=None, thetas=None):
        segments = max(6, int(segments*.8))
        rings = max(4, int(rings*.9))
        # thetas: kutuptan ölçülen iç halka açıları; görünen bölgede halkaları sıklaştırır.
        thetas = thetas or [PI*j/rings for j in range(1, rings)]
        rings = len(thetas)+1
        # Tek kutup vertex'i: sıfır alanlı yüz üretmez.
        verts = [(center[0], center[1], center[2]+scale[2])]
        for theta in thetas:
            for i in range(segments):
                phi = 2*PI*i/segments
                verts.append((center[0]+scale[0]*math.sin(theta)*math.cos(phi),
                              center[1]+scale[1]*math.sin(theta)*math.sin(phi),
                              center[2]+scale[2]*math.cos(theta)))
        verts.append((center[0], center[1], center[2]-scale[2]))
        faces = [(0, 1+i, 1+(i+1)%segments) for i in range(segments)]
        for j in range(rings-2):
            for i in range(segments):
                a = 1+j*segments+i
                b = 1+j*segments+(i+1)%segments
                faces.append((a, a+segments, b+segments, b))
        end = len(verts)-1
        faces += [(end, end-1-i, end-1-(i+1)%segments) for i in range(segments)]
        self.add(verts, faces, mat, weights, tag)

    def box(self, center, size, mat, weights='Hips', bevel=.012, tag=None):
        bpy.ops.mesh.primitive_cube_add(size=1, location=center)
        obj = bpy.context.object
        obj.dimensions = size
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        if bevel:
            mod = obj.modifiers.new('RoundedEdges', 'BEVEL')
            mod.width, mod.segments = bevel, 1
            bpy.ops.object.modifier_apply(modifier=mod.name)
        self.add([tuple(obj.matrix_world @ v.co) for v in obj.data.vertices],
                 [tuple(p.vertices) for p in obj.data.polygons], mat, weights, tag)
        mesh = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(mesh)

    def loft(self, rings, mat, weights='Head', segments=20, axis='Z', cap=True, tag=None, exact=False):
        # exact: silüeti belirleyen parçalar gövde segment tavanından muaf tutulur.
        segments = min(segments, 24)
        # Halkalar: merkez, iki yarıçap. Aynı dikişte aynı ağırlık fonksiyonu kullanılır.
        verts = []
        for center, a, b in rings:
            for i in range(segments):
                t = 2*PI*i/segments
                delta = (a*math.cos(t), b*math.sin(t), 0) if axis == 'Z' else (0, a*math.cos(t), b*math.sin(t))
                verts.append(tuple(center[k]+delta[k] for k in range(3)))
        faces = []
        for j in range(len(rings)-1):
            for i in range(segments):
                a, b = j*segments+i, j*segments+(i+1)%segments
                faces.append((a, b, b+segments, a+segments))
        if cap:
            faces += [tuple(reversed(range(segments))), tuple((len(rings)-1)*segments+i for i in range(segments))]
        self.add(verts, faces, mat, weights, tag)

    def tube(self, points, radius, mat, weights='Head', sides=6, tag=None):
        verts = []
        for i, p in enumerate(points):
            tangent = Vector(points[min(i+1, len(points)-1)])-Vector(points[max(0, i-1)])
            tangent.normalize()
            axis = Vector((0, 1, 0))
            if abs(tangent.dot(axis)) > .95:
                axis = Vector((1, 0, 0))
            u = tangent.cross(axis).normalized()
            v = tangent.cross(u).normalized()
            ring_radius = radius[i] if isinstance(radius, (tuple, list)) else radius
            for k in range(sides):
                verts.append(tuple(Vector(p)+ring_radius*(u*math.cos(2*PI*k/sides)+v*math.sin(2*PI*k/sides))))
        faces = []
        for i in range(len(points)-1):
            for k in range(sides):
                a, b = i*sides+k, i*sides+(k+1)%sides
                faces.append((a, b, b+sides, a+sides))
        faces += [tuple(reversed(range(sides))), tuple((len(points)-1)*sides+k for k in range(sides))]
        self.add(verts, faces, mat, weights, tag)

    def object(self):
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        used = sorted(set(self.face_materials))
        for name in used:
            mesh.materials.append(MATERIALS[name])
        for poly, name in zip(mesh.polygons, self.face_materials):
            poly.material_index = used.index(name)
            poly.use_smooth = len(poly.vertices)<=4
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.015)
        bpy.ops.object.mode_set(mode='OBJECT')
        obj.select_set(False)
        return obj




def smooth(t):
    t=max(0.,min(1.,t))
    return t*t*(3-2*t)


def blend(a,b,t):
    t=smooth(t)
    return {k:v for k,v in ((a,1-t),(b,t)) if v>1e-7}


def torso_weights(p):
    chain=(('Hips',.46),('Spine',.57625),('Spine1',.70975),('Spine2',.82656),('Neck',.98))
    for (a,z),(b,zz) in zip(chain,chain[1:]):
        if p[2]<=zz:
            return blend(a,b,(p[2]-z)/(zz-z))
    return {'Neck':1}


def arm_weights(side):
    def weight(p):
        x=abs(p[0])
        # Geniş omuz geçişi: kol indiğinde koltuk altı gövdeye gömülmez.
        if x<.36:
            return blend(side+'Shoulder',side+'Arm',(x-.20)/.16)
        if x<.47:
            return blend(side+'Arm',side+'ForeArm',(x-.36)/.07)
        return blend(side+'ForeArm',side+'Hand',(x-.51)/.055)
    return weight


def leg_weights(side):
    def weight(p):
        z=p[2]
        if z>.41:
            return blend(side+'UpLeg','Hips',(z-.41)/.14)
        if z>.20:
            return blend(side+'Leg',side+'UpLeg',(z-.26)/.10)
        return blend(side+'Foot',side+'Leg',(z-.095)/.075)
    return weight


def foot_weights(side):
    return lambda p: blend(side+'Foot',side+'ToeBase',(-p[1]-.10)/.09)


def ribbon(geo, points, width, mat, weights, axis=(1,0,0)):
    # Düz renkli şerit, her kesitte iki kenar; ağırlıklar kesintisiz ilerler.
    verts=[]
    for point in points:
        for sign in (-1,1):
            verts.append(tuple(Vector(point)+Vector(axis)*width*sign/2))
    geo.add(verts,[(2*i,2*i+1,2*i+3,2*i+2) for i in range(len(points)-1)],mat,weights)


def head(geo, variant):
    skin='Skin' if variant=='Tourist' else 'SkinLight'
    hair='GrayHair' if variant=='Elder' else 'Hair'
    geo.sphere((0,.012,1.205),(.235,.202,.245),skin,segments=32,rings=18)
    geo.loft([((0,0,z),r,r*.85) for z,r in ((.91,.065),(.98,.075),(1.055,.068))],skin,'Neck',segments=16)
    # Saç kabuğu: önde yüksek saç çizgisi, ensede daha aşağı sınır.
    verts=[]; faces=[]; n=32
    for j in range(8):
        for i in range(n):
            phi=2*PI*i/n
            front=max(0,-math.sin(phi)); rear=max(0,math.sin(phi))
            bottom=1.23+.115*front-.16*rear
            end=math.acos((bottom-1.22)/.28)
            theta=.001+(end-.001)*j/7
            verts.append((.242*math.sin(theta)*math.cos(phi),.012+.21*math.sin(theta)*math.sin(phi),1.22+.28*math.cos(theta)))
    for j in range(7):
        for i in range(n):
            a=j*n+i; b=j*n+(i+1)%n
            faces.append((a,b,b+n,a+n))
    geo.add(verts,faces,hair)
    for s in (-1,1):
        geo.sphere((s*.232,.005,1.17),(.033,.044,.059),skin,segments=12,rings=8)
        geo.sphere((s*.099,-.168,1.233),(.096,.074,.108),'White',segments=20,rings=12)
        geo.sphere((s*.086,-.239,1.228),(.023,.012,.027),'Pupil',segments=12,rings=8)
        geo.sphere((s*.086-.006,-.250,1.239),(.005,.003,.006),'White',segments=6,rings=4)
        if variant!='Tourist':
            # Turist'te kaşların yerini alın bandajı alır (konseptteki gibi).
            points=[(s*.098+t*.061,-.168-.024*(1-t*t),1.341+.016*(1-t*t)-s*t*.008) for t in (-1,-.5,0,.5,1)]
            geo.tube(points,.009,hair,sides=6)
    geo.sphere((0,-.197,1.148),(.025,.033,.027),skin,segments=12,rings=8)
    mouth=[(t*.037,-.179-.012*(1-t*t),1.09+.009*(1-t*t)) for t in (-1,-.5,0,.5,1)]
    if variant=='Elder':
        mouth=[(x,y,z-.014*(1-(x/.037)**2)) for x,y,z in mouth]
    geo.tube(mouth,.0045,'Mouth',sides=6)
    if variant=='Elder':
        for s in (-1,1):
            for z in (1.12,1.105):
                geo.tube([(s*(.146+.019*t),-.147+.022*t,z+.008*t) for t in (0,.5,1)],.0028,'Wrinkle',sides=4)
        for z in (1.36,1.375):
            geo.tube([(x,-.159+.02*abs(x)/.055,z) for x in (-.055,0,.055)],.0028,'Wrinkle',sides=4)
    if variant=='Tourist':
        # Kafa bandajı: gözlerin hemen üstünden şapka kenarına kadar geniş beyaz bant.
        # Merkez öne alınır; önden ve yatarken üstten şapka kenarının altında okunur.
        geo.loft([((0,0,z),rx,ry) for z,rx,ry in ((1.333,.241,.218),(1.372,.239,.216),(1.411,.229,.207))],'White',segments=32,cap=False)
        # Şapka kenarı bant için yükseltildi; tepe 1,515 m (boy toleransı ±0,03 içinde).
        geo.loft([((0,.018,z),rx,ry) for z,rx,ry in ((1.405,.286,.259),(1.414,.291,.263),(1.423,.284,.257),(1.443,.244,.220),(1.494,.218,.196),(1.509,.207,.187),(1.515,.192,.173))],'Hat',segments=32)
        geo.loft([((0,.018,z),rx,ry) for z,rx,ry in ((1.443,.245,.221),(1.466,.236,.213))],'HatBand',segments=32,cap=False)
    elif variant=='Elder':
        # Siperliğin tepesinde saç açık; kapalı şapka eklenmez.
        geo.loft([((0,.012,z),rx,ry) for z,rx,ry in ((1.36,.232,.205),(1.412,.218,.198))],'Visor',segments=32,cap=False)
        verts=[]
        for radius,z in ((1,1.365),(1.05,1.361),(1.32,1.345),(1.32,1.355)):
            for i in range(21):
                phi=PI+PI*i/20
                r=1+(radius-1)*abs(math.sin(phi))**1.5
                verts.append((.232*math.cos(phi)*r,.012+.209*math.sin(phi)*r,z+.015*abs(math.cos(phi))))
        faces=[(j*21+i,j*21+i+1,(j+1)*21+i+1,(j+1)*21+i) for j in range(3) for i in range(20)]
        geo.add(verts,faces,'Visor')


def torso(geo,variant):
    shirt={'Tourist':'Turquoise','Civilian':'Green','Elder':'Purple'}[variant]
    geo.loft([((0,0,z),rx,ry) for z,rx,ry in ((.46,.198,.133),(.475,.208,.139),(.49,.209,.139),(.58,.204,.144),(.70,.191,.137),(.82,.202,.126),(.882,.215,.115),(.925,.199,.09),(.95,.145,.075),(.96,.076,.064))],shirt,torso_weights,segments=24)
    if variant=='Civilian':
        geo.loft([((0,0,z),.210,.141) for z in (.47,.505)],'GreenTrim',torso_weights,segments=24,cap=False)
    if variant=='Elder':
        # Açık hırka: orta tişört düz yüzey, kenarlarda iki mor biye.
        ribbon(geo,[(0,-.145,.49),(0,-.151,.60),(0,-.145,.72),(0,-.135,.82),(0,-.102,.92)],.15,'Tee',torso_weights)
        for s in (-1,1):
            ribbon(geo,[(s*.081,-.148,.49),(s*.080,-.154,.60),(s*.080,-.148,.72),(s*.075,-.140,.82),(s*.049,-.103,.92)],.018,'PurpleDark',torso_weights)
            for z in (.55,.62,.75):
                pts=[(s*(.115+i*.020),-.149+.025*i/3,z+(.018 if i%2 else 0)) for i in range(4)]
                geo.tube(pts,.006,'PurpleDark',torso_weights,sides=4)
        for s in (-1,1):
            ribbon(geo,[(s*.072,-.102,.94),(s*.050,-.146,.83),(s*.020,-.162,.73)],.016,'Visor',torso_weights)
        geo.box((0,-.172,.68),(.095,.018,.125),'Yellow','Spine1',bevel=.009)
    if variant=='Tourist':
        # Büyük beş yapraklı renk adaları gömlek yüzeyinde; texture yok.
        for x,z,r,col in ((-.13,.57,.056,'Pink'),(.10,.58,.054,'Orange'),(-.12,.79,.052,'Orange'),(.125,.80,.050,'Pink'),(.02,.515,.042,'Pink'),(.155,.69,.040,'Orange'),(-.165,.68,.040,'Pink')):
            flower(geo,x,z,r,col)
        for x,z,r,col in ((-.10,.58,.048,'Pink'),(.10,.73,.045,'Orange'),(-.09,.84,.043,'Pink')):
            flower(geo,x,z,r,col,back=True)
        for s in (-1,1):
            ribbon(geo,[(s*.085,-.102,.94),(s*.071,-.151,.83),(s*.062,-.164,.72)],.016,'Strap',torso_weights)
        geo.box((0,-.173,.695),(.166,.045,.10),'Camera','Spine1',bevel=.012)
        geo.sphere((0,-.205,.69),(.045,.021,.045),'DarkBrown','Spine1',segments=16,rings=8)
        geo.sphere((0,-.226,.69),(.031,.006,.031),'Lens','Spine1',segments=16,rings=8)
        geo.box((.052,-.199,.733),(.030,.009,.016),'White','Spine1',bevel=.002)


def flower(geo,x,z,r,col,back=False):
    def y_at(xx,zz):
        profile=((.49,.209,.139),(.58,.204,.144),(.70,.191,.137),(.82,.202,.126),(.882,.215,.115))
        rx,ry=.209,.139
        for (za,xa,ya),(zb,xb,yb) in zip(profile,profile[1:]):
            if zz<=zb:
                t=max(0,min(1,(zz-za)/(zb-za)))
                rx=xa+(xb-xa)*t; ry=ya+(yb-ya)*t
                break
        result=ry*math.sqrt(max(.1,1-(xx/rx)**2))+.002
        return result if back else -result
    verts=[(x,y_at(x,z),z)]
    for i in range(25):
        a=2*PI*i/25
        radius=r*(.70+.30*math.cos(5*a))
        xx=x+radius*math.cos(a); zz=z+radius*math.sin(a)
        verts.append((xx,y_at(xx,zz),zz))
    geo.add(verts,[(0,i+1,(i+1)%25+1) for i in range(25)],col,torso_weights)
    geo.sphere((x,y_at(x,z)+(.002 if back else -.002),z),(.009,.003,.009),'Orange' if col=='Pink' else 'Pink',torso_weights,segments=8,rings=4)


def arm(geo,variant,side):
    s=1 if side=='Left' else -1
    skin='Skin' if variant=='Tourist' else 'SkinLight'
    shirt={'Tourist':'Turquoise','Civilian':'Green','Elder':'Purple'}[variant]
    w=arm_weights(side)
    if variant=='Tourist':
        geo.loft([((s*x,0,.8822),ry,rz) for x,ry,rz in ((.20,.073,.072),(.215,.092,.085),(.24,.092,.087),(.32,.084,.078),(.34,.080,.075),(.343,.076,.071))],shirt,w,segments=16,axis='X')
        geo.loft([((s*x,0,.8822),ry,rz) for x,ry,rz in ((.33,.064,.061),(.38,.061,.059),(.41,.058,.057),(.48,.052,.051),(.55,.044,.044))],skin,w,segments=16,axis='X')
    else:
        geo.loft([((s*x,0,.8822),ry,rz) for x,ry,rz in ((.20,.073,.072),(.215,.092,.085),(.24,.092,.087),(.31,.079,.075),(.37,.068,.065),(.40,.065,.063),(.46,.061,.060),(.53,.053,.052),(.545,.050,.049))],shirt,w,segments=16,axis='X')
        geo.loft([((s*x,0,.8822),.054,.053) for x in (.515,.55)],'GreenTrim' if variant=='Civilian' else 'PurpleDark',w,segments=16,axis='X',cap=False)
    geo.sphere((s*.603,0,.8822),(.078,.053,.046),skin,side+'Hand',segments=16,rings=10)
    geo.sphere((s*.668,.008,.8822),(.052,.048,.039),skin,side+'HandIndex1',segments=12,rings=8)
    geo.sphere((s*.724,-.01,.886),(.035,.030,.027),skin,side+'HandIndex2',segments=12,rings=6)
    geo.tube([(s*.593,-.027,.873),(s*.617,-.069,.847),(s*.650,-.092,.838)],[.030,.031,.012],skin,lambda p:blend(side+'HandThumb1',side+'HandThumb2',(abs(p[0])-.602)/.048),sides=10)
    if variant=='Tourist' and side=='Left':
        geo.loft([((x,0,.8822),.056,.055) for x in (.49,.523)],'Watch',side+'ForeArm',segments=16,axis='X')
        geo.box((.506,-.055,.896),(.037,.013,.039),'Camera',side+'ForeArm',bevel=.006)
    if variant=='Civilian' and side=='Right':
        geo.loft([((-x,0,.8822),.071,.072) for x in (.400,.445,.50,.552)],'White',side+'ForeArm',segments=20,axis='X')


def legs(geo,variant):
    for s,side in ((1,'Left'),(-1,'Right')):
        x=s*.1344; w=leg_weights(side)
        pant={'Tourist':'Khaki','Civilian':'Denim','Elder':'GrayPants'}[variant]
        bottom=.26 if variant=='Tourist' else (.29 if variant=='Elder' and side=='Left' else .105)
        zs=sorted(set([bottom,max(bottom,.16),max(bottom,.26),.32,.40,.48,.53]))
        geo.loft([((x-s*.022*smooth((z-.40)/.13),0,z),.086 if z<.32 else .093,.084 if z<.32 else .096) for z in zs],pant,w,segments=16)
        if variant=='Tourist':
            geo.loft([((x,0,z),.063,.065) for z in (.13,.20,.28)],'Skin',w,segments=16)
            geo.loft([((x,0,z),.067,.068) for z in (.055,.12,.17)],'White',w,segments=16)
        if variant=='Elder' and side=='Left':
            geo.loft([((x,0,z),r,r*.98) for z,r in ((.08,.080),(.12,.085),(.22,.094),(.29,.101),(.315,.102))],'White',w,segments=20)
            geo.sphere((x,-.034,.063),(.085,.124,.063),'White',foot_weights(side),segments=20,rings=10)
            # Burun açıklığı ve görünür parmaklar.
            geo.sphere((x,-.144,.061),(.064,.018,.028),'SkinLight',side+'ToeBase',segments=12,rings=6)
            for dx in (-.041,-.014,.014,.039):
                geo.sphere((x+dx,-.153,.063),(.015,.014,.021),'SkinLight',side+'ToeBase',segments=8,rings=6)
        else:
            shoe='Brown' if variant!='Civilian' else 'GrayShoe'
            start=len(geo.vertices)
            geo.sphere((x,-.04,.061),(.086,.133,.055),'White' if variant=='Tourist' else shoe,foot_weights(side),segments=20,rings=10)
            for i in range(start,len(geo.vertices)):
                vx,vy,vz=geo.vertices[i]; geo.vertices[i]=(vx,vy,max(.025,vz))
            geo.loft([((x,-.04,z),.087,.134) for z in (0,.027)],'DarkBrown' if variant!='Civilian' else 'Sole',foot_weights(side),segments=20)
            if variant=='Tourist':
                # Beyaz çorap üstünde geniş kahverengi sandalet bantları.
                for yy in (-.115,-.015):
                    geo.tube([(x+.075*math.cos(t),yy,.048+.061*math.sin(t)) for t in (0,PI/6,PI/3,PI/2,2*PI/3,5*PI/6,PI)],.017,'Brown',foot_weights(side),sides=6)


def sling(geo,rig):
    # İki bacaklı askı: önkol sargısından göğüs önünden boyna çıkan şerit.
    # Ağırlık zinciri Neck -> RightArm -> RightForeArm, yumuşak geçişli.
    # Neck ile RightForeArm arasındaki dönüş askı pozunda ~180 derece olduğu için
    # ikili karışım şeridi katlıyor; ara kemik bunu önler. Şerit askı pozunda
    # istenen yere konur ve aynı ağırlıklarla ters LBS ile bind konumuna çevrilir.
    world=rig.matrix_world; inv_world=world.inverted()
    set_pose(rig,True)
    def deform(name):
        pb=rig.pose.bones[PREFIX+name]
        return world@pb.matrix@pb.bone.matrix_local.inverted()@inv_world
    bones={k:deform(k) for k in ('Neck','RightArm','RightForeArm')}
    # Gerçek askı gibi: sol boyun tarafından bilek ucuna, sağ boyun tarafından dirsek
    # ucuna iner. Alt uç askı pozunda sargının üst yüzeyine (dünya +Z) oturtulur.
    for top,bottom in (((.058,-.082,.958),-.528),((-.058,-.082,.958),-.418)):
        a=bones['Neck']@Vector(top)
        b=bones['RightForeArm']@Vector((bottom,0,.8822))+Vector((0,-.012,.069))
        side=(b-a).cross(Vector((0,-1,0))).normalized()
        verts=[]; weights=[]
        for i in range(13):
            t=i/12
            w=blend('Neck','RightArm',t*2) if t<.5 else blend('RightArm','RightForeArm',t*2-1)
            m=sum((bones[k]*v for k,v in w.items()),Matrix(((0,)*4,)*4))
            inverse=m.inverted()
            # Göğüs önünde hafif dışa kavis.
            centre=a.lerp(b,t)+Vector((0,-.03,0))*math.sin(PI*t)
            for sign in (-1,1):
                verts.append(tuple(inverse@(centre+side*.0155*sign))); weights.append(w)
        geo.add(verts,[(2*i,2*i+1,2*i+3,2*i+2) for i in range(12)],'White',lambda q,_w=iter(weights):next(_w))
    reset_pose(rig)


def rig_from_crew():
    bpy.ops.import_scene.fbx(filepath=str(CREW),use_anim=False)
    rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    # Bind matrisleri kaynaktan doğrudan kopyalanır; ek kemik üretilmez.
    for obj in list(bpy.context.scene.objects):
        if obj!=rig:
            bpy.data.objects.remove(obj,do_unlink=True)
    rig.animation_data_clear()
    return rig


def make_variant(template,variant):
    rig=bpy.data.objects.new('SK_Patient_'+variant,template.data)
    bpy.context.collection.objects.link(rig)
    rig.matrix_world=template.matrix_world.copy()
    parts=[]
    for label,fn in [('Head',lambda g:head(g,variant)),('Torso',lambda g:torso(g,variant)),('LeftArm',lambda g:arm(g,variant,'Left')),('RightArm',lambda g:arm(g,variant,'Right')),('Legs',lambda g:legs(g,variant))]+([('Sling',lambda g:sling(g,rig))] if variant=='Civilian' else []):
        geo=Geometry('Patient_'+variant+'_'+label)
        fn(geo)
        obj=geo.object()
        groups={b.name:obj.vertex_groups.new(name=b.name) for b in rig.data.bones}
        for i,weights in enumerate(geo.weights):
            total=sum(weights.values())
            for name,value in weights.items():
                groups[PREFIX+name].add([i],value/total,'REPLACE')
        obj.parent=rig
        mod=obj.modifiers.new('PatientSkin','ARMATURE'); mod.object=rig
        parts.append(obj)
    return rig,parts


def reset_pose(rig):
    rig.animation_data_clear()
    for pb in rig.pose.bones:
        pb.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()


def aim_bone(rig,name,direction):
    pb=rig.pose.bones[PREFIX+name]
    # İstenen dünya yönü, ebeveynin güncel matrisine göre yerel quaternion'a çevrilir.
    parent=pb.parent
    rest=pb.bone.matrix_local
    inherited=parent.matrix @ parent.bone.matrix_local.inverted() @ rest if parent else rest
    local=inherited.to_3x3().inverted() @ Vector(direction).normalized()
    pb.rotation_mode='QUATERNION'
    pb.rotation_quaternion=Vector((0,1,0)).rotation_difference(local)
    bpy.context.view_layer.update()


def set_pose(rig,sling_pose=False):
    reset_pose(rig)
    for s,side in ((1,'Left'),(-1,'Right')):
        aim_bone(rig,side+'Arm',(s*.25,0,-.968))
        # Bacaklar düz, bütün zincir paralel olarak hafifçe içeri gelir.
        pb=rig.pose.bones[PREFIX+side+'UpLeg']
        pb.location=pb.bone.matrix_local.to_3x3().inverted()@Vector((-s*.031,0,0))
    if sling_pose:
        # Üst kol gövde yanında hafif öne iner; önkol göğsün ortasında yatay durur,
        # el karşı kaburga hizasında dinlenir (çene/boyun bölgesinden uzak).
        aim_bone(rig,'RightArm',(-.12,-.60,-.79))
        aim_bone(rig,'RightForeArm',(.84,-.52,-.12))
        aim_bone(rig,'RightHand',(1,-.02,-.05))
    headbone=rig.pose.bones[PREFIX+'Head']
    axis=headbone.bone.matrix_local.to_3x3().inverted()@Vector((1,0,0))
    headbone.rotation_mode='QUATERNION'
    headbone.rotation_quaternion=Quaternion(axis,math.radians(7))
    bpy.context.view_layer.update()


def make_action(rig,name,sling_pose):
    set_pose(rig,sling_pose)
    action=bpy.data.actions.new(name)
    action.use_fake_user=True
    rig.animation_data_create(); rig.animation_data.action=action
    for pb in rig.pose.bones:
        pb.rotation_mode='QUATERNION'
        pb.keyframe_insert('rotation_quaternion',frame=1,group=pb.name)
        pb.keyframe_insert('location',frame=1,group=pb.name)
        pb.keyframe_insert('scale',frame=1,group=pb.name)
    return action


def export_fbx(path,rig,parts,anim=False):
    scene=bpy.context.scene
    old_name=scene.name
    if anim:
        scene.name=path.stem
    bpy.ops.object.select_all(action='DESELECT')
    for o in [rig]+parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,
        object_types={'ARMATURE','MESH'},apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',add_leaf_bones=False,use_armature_deform_only=False,
        bake_anim=anim,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,
        bake_anim_simplify_factor=0,bake_anim_force_startend_keying=True,
        mesh_smooth_type='FACE',use_mesh_modifiers=False,use_custom_props=True,check_existing=False)
    scene.name=old_name


def triangles(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def signature(parts):
    return [{'name':o.name,'verts':[[round(c,7) for c in v.co] for v in o.data.vertices],
             'faces':[list(p.vertices) for p in o.data.polygons],
             'weights':[[(o.vertex_groups[g.group].name,round(g.weight,7)) for g in v.groups] for v in o.data.vertices],
             'materials':[s.material.name for s in o.material_slots],
             'assignments':[p.material_index for p in o.data.polygons]} for o in parts]


def digest(data):
    return hashlib.sha256(json.dumps(data,sort_keys=True,separators=(',',':')).encode()).hexdigest()


def measure(rig,parts):
    points=[o.matrix_world@v.co for o in parts for v in o.data.vertices]
    hip=rig.matrix_world@rig.data.bones[PREFIX+'Hips'].head_local
    torso_obj=next(o for o in parts if o.name.endswith('_Torso'))
    # Sırt, gövde arka yüzeyinin kalçaya en yakın yatay kesiti; +Y arkadır.
    back=max(v.co.y for v in torso_obj.data.vertices if abs(v.co.z-hip.z)<.05)
    return dict(height_m=max(p.z for p in points)-min(p.z for p in points),sole_z_m=min(p.z for p in points),
                hips_to_back_m=back-hip.y,hips_to_top_m=max(p.z for p in points)-hip.z,
                hips_to_heel_m=hip.z-min(p.z for p in points),hips_bind_position_m=list(hip))


def setup_render():
    scene=bpy.context.scene
    scene.render.engine='CYCLES'; scene.cycles.samples=48
    # OIDN bu makinede bellek yetersizliğiyle çöküyor; denoise yerine daha çok örnek.
    scene.cycles.use_denoising=False; scene.cycles.seed=41
    scene.cycles.denoising_use_gpu=False
    scene.cycles.use_animated_seed=False; scene.cycles.max_bounces=5
    prefs=bpy.context.preferences.addons['cycles'].preferences
    device='CPU'
    for backend in ():
        try:
            prefs.compute_device_type=backend; prefs.refresh_devices()
            if any(d.type==backend for d in prefs.devices):
                for d in prefs.devices: d.use=d.type==backend
                scene.cycles.device='GPU'; device=backend; break
        except Exception: pass
    scene.cycles.device='CPU'
    scene.render.threads_mode='FIXED'; scene.render.threads=8
    scene['render_device']=device
    scene.render.resolution_x=1920; scene.render.resolution_y=1080
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.83,.86,.88,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.45
    scene.view_settings.view_transform='AgX'
    scene.view_settings.look='AgX - Medium High Contrast'
    scene.view_settings.exposure=-.7
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.007))
    floor=bpy.context.object; floor.name='Studio_Floor'
    mat=bpy.data.materials.new('Studio_Patient'); mat.diffuse_color=(.82,.83,.84,1)
    floor.data.materials.append(mat)
    floor.is_shadow_catcher=True
    scene.render.film_transparent=True
    group=bpy.data.node_groups.new('Patient_Studio_Compositor','CompositorNodeTree')
    scene.compositing_node_group=group
    group.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor')
    layers=group.nodes.new('CompositorNodeRLayers')
    over=group.nodes.new('CompositorNodeAlphaOver')
    over.inputs['Background'].default_value=(3,3,3,1)
    over.inputs['Factor'].default_value=1
    output=group.nodes.new('NodeGroupOutput')
    group.links.new(layers.outputs['Image'],over.inputs['Foreground'])
    group.links.new(over.outputs[0],output.inputs[0])
    camera=bpy.data.objects.new('Studio_Camera',bpy.data.cameras.new('Studio_Camera'))
    scene.collection.objects.link(camera); scene.camera=camera; camera.data.type='ORTHO'
    for name,loc,power,size in [('Key',(-3,-4,6),800,4),('Fill',(4,-2,4),500,4),('Rim',(0,3,4),700,3)]:
        data=bpy.data.lights.new('Studio_'+name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size
        obj=bpy.data.objects.new(data.name,data); scene.collection.objects.link(obj); obj.location=loc
        obj.rotation_euler=(Vector((0,0,.8))-obj.location).to_track_quat('-Z','Y').to_euler()
    return camera


def snapshot(parts,transform):
    dg=bpy.context.evaluated_depsgraph_get(); copies=[]
    for obj in parts:
        mesh=bpy.data.meshes.new_from_object(obj.evaluated_get(dg),depsgraph=dg)
        dup=bpy.data.objects.new('Render_'+obj.name,mesh)
        bpy.context.collection.objects.link(dup)
        dup.matrix_world=transform@obj.matrix_world
        copies.append(dup)
    return copies


def remove_copies(copies):
    for obj in copies:
        mesh=obj.data; bpy.data.objects.remove(obj,do_unlink=True); bpy.data.meshes.remove(mesh)


def compare_images(render_path,reference,path,crop=None):
    # Salt-okunur konsept ile gerçek render aynı 1920x1080 tuvale yerleştirilir.
    canvas=np.ones((1080,1920,4),dtype=np.float32)
    for index,source in enumerate((reference,render_path)):
        img=bpy.data.images.load(str(source),check_existing=False)
        w,h=img.size; arr=np.empty(w*h*4,dtype=np.float32); img.pixels.foreach_get(arr)
        arr=arr.reshape(h,w,4)
        if index==0 and crop:
            arr=arr[:,crop[0]:crop[1]]
        # Render'ın tekli kadrajında fazla yatay boşluğu kırp.
        if index==1 and crop:
            arr=arr[:,450:1470]
        h,w=arr.shape[:2]; scale=min(940/w,1040/h); nh,nw=int(h*scale),int(w*scale)
        resized=arr[np.minimum((np.arange(nh)/scale).astype(int),h-1)[:,None],np.minimum((np.arange(nw)/scale).astype(int),w-1)[None,:]]
        x=index*960+(960-nw)//2; y=(1080-nh)//2
        canvas[y:y+nh,x:x+nw]=resized
        bpy.data.images.remove(img)
    img=bpy.data.images.new('Comparison',1920,1080,alpha=True)
    img.pixels.foreach_set(canvas.ravel()); img.filepath_raw=str(path); img.file_format='PNG'; img.save()
    bpy.data.images.remove(img)


def renders(assets,round_number):
    start=time.perf_counter(); folder=OUT/'renders'/f'round_{round_number:02d}'; folder.mkdir(parents=True,exist_ok=True)
    camera=setup_render(); scene=bpy.context.scene; times={}
    for rig,parts in assets.values():
        for o in parts: o.hide_render=True
    def shot(name,copies,location,target,scale):
        t=time.perf_counter()
        camera.location=location; camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.ortho_scale=scale
        scene.render.filepath=str(folder/(name+'.png')); bpy.ops.render.render(write_still=True)
        times[name]=round(time.perf_counter()-t,3); remove_copies(copies)
    for variant,(rig,parts) in assets.items():
        reset_pose(rig)
        for name,loc in (('front',(0,-6,.77)),('left',(6,0,.77)),('back',(0,6,.77))):
            shot(variant.lower()+'_'+name,snapshot(parts,Matrix.Identity(4)),loc,(0,0,.77),2.95)
    copies=[]
    for x,(variant,(rig,parts)) in zip((-1.6,0,1.6),assets.items()):
        copies+=snapshot(parts,Matrix.Translation((x,0,0)))
    shot('three_front',copies,(0,-8,.78),(0,0,.78),5.25)
    copies=[]
    for x,(variant,(rig,parts)) in zip((-1.12,0,1.12),assets.items()):
        set_pose(rig,variant=='Civilian')
        copies+=snapshot(parts,Matrix.Translation((x,0,.245))@Matrix.Rotation(-PI/2,4,'X'))
        reset_pose(rig)
    shot('three_lie',copies,(2,-3.2,5),(0,.72,.15),4.1)
    # Kaynak oyuncu yalnız ölçek render'ı için içe alınır ve sonra sahneden çıkarılır.
    before=set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(CREW),use_anim=False)
    imported=set(bpy.data.objects)-before
    crewparts=[o for o in imported if o.type=='MESH']
    copies=snapshot(crewparts,Matrix.Translation((-1.0,0,0)))
    for o in imported: bpy.data.objects.remove(o,do_unlink=True)
    copies+=snapshot(assets['Civilian'][1],Matrix.Translation((1.0,0,0)))
    shot('scale_with_crew',copies,(0,-8,.78),(0,0,.78),4.2)
    compare_images(folder/'three_front.png',REF/'01_patients_three_variants.jpg',folder/'compare_three_front.png')
    for variant,crop in zip(VARIANTS,((0,560),(565,824),(820,1376))):
        compare_images(folder/(variant.lower()+'_front.png'),REF/'01_patients_three_variants.jpg',folder/('compare_'+variant.lower()+'.png'),crop)
    compare_images(folder/'scale_with_crew.png',REF/'02_rescue_crew_style.jpg',folder/'compare_crew_style.png')
    for path in folder.glob('*.png'):
        shutil.copy2(path,OUT/'renders'/path.name)
    for rig,parts in assets.values():
        for o in parts: o.hide_render=False
    return dict(render_seconds=round(time.perf_counter()-start,3),camera_render_seconds=times,render_device=scene['render_device'])


def main():
    global ROUND
    argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    parser=argparse.ArgumentParser(); parser.add_argument('--round',type=int,default=3); parser.add_argument('--no-render',action='store_true')
    args=parser.parse_args(argv); ROUND=args.round
    start=time.perf_counter(); OUT.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    bpy.context.preferences.filepaths.save_version=0
    bpy.context.scene.unit_settings.system='METRIC'; bpy.context.scene.unit_settings.scale_length=1
    bpy.context.scene.frame_start=1; bpy.context.scene.frame_end=1; bpy.context.scene.render.fps=30
    materials(); template=rig_from_crew()
    assets={v:make_variant(template,v) for v in VARIANTS}
    bpy.data.objects.remove(template,do_unlink=True)
    meta={'units':'meters','forward':'-Y','up':'Z','variants':{},'hex_colors':{'M_Patient_'+k:'#'+v for k,v in PALETTE.items()},
          'pose_convention':'Upright root/hips. Unity rotates whole object onto stretcher.',
          'skeleton_source':'Export/RescueCrew/SK_RescueCrew_Blue.fbx'}
    geometry={}
    for variant,(rig,parts) in assets.items():
        reset_pose(rig)
        counts={o.name:triangles(o) for o in parts}
        total=sum(counts.values()); measures=measure(rig,parts)
        meta['variants'][variant]={**measures,'triangles':total,'mesh_triangles':counts}
        geometry[variant]=signature(parts)
        print('PATIENT',variant,total,measures,flush=True)
        assert 5000<=total<=8000,(variant,total)
        assert 1.47<=measures['height_m']<=1.53,(variant,measures)
        export_fbx(OUT/('SK_Patient_'+variant+'.fbx'),rig,parts)
    rig=assets['Civilian'][0]
    meta['bones']={b.name:b.parent.name if b.parent else None for b in rig.data.bones}
    for name,is_sling in (('Patient_LiePose',False),('Patient_LiePose_ArmSling',True)):
        make_action(rig,name,is_sling)
        # Tek 1 mm üçgen, FBX skin cluster'larında T-poz bind matrislerini korur.
        # Mesh'siz FBX ithalatında Blender ilk animasyon karesini bind sayar.
        mesh=bpy.data.meshes.new('Patient_BindAnchor')
        mesh.from_pydata([(0,0,.457),(.001,0,.457),(0,.001,.457)],[],[(0,1,2)])
        anchor=bpy.data.objects.new('Patient_BindAnchor',mesh)
        bpy.context.collection.objects.link(anchor); anchor.parent=rig
        anchor.vertex_groups.new(name=PREFIX+'Hips').add([0,1,2],1,'REPLACE')
        mod=anchor.modifiers.new('BindAnchorSkin','ARMATURE'); mod.object=rig
        export_fbx(OUT/(name+'.fbx'),rig,[anchor],True)
        bpy.data.objects.remove(anchor,do_unlink=True); bpy.data.meshes.remove(mesh)
    reset_pose(rig)
    meta['hips_to_back_m']={k:v['hips_to_back_m'] for k,v in meta['variants'].items()}
    meta['hips_to_top_m']={k:v['hips_to_top_m'] for k,v in meta['variants'].items()}
    meta['hips_to_heel_m']={k:v['hips_to_heel_m'] for k,v in meta['variants'].items()}
    (OUT/'patient_meta.json').write_text(json.dumps(meta,indent=2),encoding='utf-8')
    hashvalue=digest(geometry)
    renderstats=renders(assets,ROUND) if not args.no_render else {'render_seconds':0,'camera_render_seconds':{}}
    # Blend'de üç varyant seçilebilir, hepsi bağ pozunda ve ortak iskelet datablock'undadır.
    for variant,(rig,parts) in assets.items():
        reset_pose(rig)
        rig.location.x=VARIANTS.index(variant)*1.6-1.6
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Patient.blend'),check_existing=False)
    old=json.loads((OUT/'stats.json').read_text()) if (OUT/'stats.json').exists() else {}
    source=hashlib.sha256(Path(__file__).read_bytes().replace(b'\r\n',b'\n')).hexdigest()
    entry={'geometry_sha256':hashvalue,'source_sha256':source,'round':ROUND,**renderstats,'build_seconds':round(time.perf_counter()-start,3)}
    stats={'geometry_sha256':hashvalue,'variants':meta['variants'],'last_build':entry,'build_history':(old.get('build_history',[])+[entry])[-12:],
           'fbx_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in OUT.glob('*.fbx')}}
    (OUT/'stats.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
    print('BUILD OK',json.dumps(entry),flush=True)


if __name__=='__main__':
    try:
        main()
    except Exception:
        traceback.print_exc(); print('BUILD FAILED',flush=True); sys.exit(1)
