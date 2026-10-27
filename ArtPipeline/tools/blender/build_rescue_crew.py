"""Blender 5.2: deterministik Rescue Crew üretimi, skinning, FBX ve sunum."""
import argparse
import hashlib
import json
import math
import os
import sys
import time
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector, Quaternion

PI = math.pi
PREFIX = 'mixamorig:'
SHAPES = ['Blink_L', 'Blink_R', 'Brows_Angry', 'Brows_Worried', 'Mouth_Smile',
          'Mouth_Open', 'Mouth_Frown', 'Pupils_Small', 'Eyes_Happy']
MATERIALS = {}
HEX_COLORS = {}


def character_point(p, rigid=False):
    # Aynı dönüşüm mesh, shape key ve kemiklere uygulanır; taban ve boy sabittir.
    # rigid: kafa küresi yalnızca dikey ölçeği alır, çene gövde dolgunluğuyla bozulmaz.
    x,y,z=p
    anchors=((0,0),(.20,.19),(.55,.465),(.95,.91),(1.51,1.50))
    for (a,c),(b,d) in zip(anchors,anchors[1:]):
        if z<=b:
            new_z=c+(z-a)*(d-c)/(b-a)
            break
    else:
        new_z=1.5+(z-1.51)
    if rigid:
        return (x,y,new_z)
    taper=max(0,min(1,(abs(x)-.20)/.20))
    taper=taper*taper*(3-2*taper)
    fullness=1+.12*(1-taper)*max(0,min(1,(1.0-z)/.06))
    # Derinlik artışı yumuşak rampa: z=0.94 altı aynen kalır, omuz üstünde sırt oluşmaz.
    depth=1+.10*max(0,min(1,(1.02-z)/.08))
    return (x*fullness,y*depth,new_z)


def knee_on_leg_line(hip, ankle, knee_z, forward):
    # Diz, kalça–ayak bileği doğrusunun üstünde olmalı; yoksa Unity Humanoid bacak
    # twist kaslarını bind pozunda limit dışı hesaplar. character_point bacak bölgesinde
    # x ve y'yi sabit oranla, z'yi parça parça ölçekler; bu yüzden oran son uzaydaki
    # yükseklikten alınır. forward: dizin karakterin baktığı -Y yönüne kırılması.
    zf=lambda v: character_point((0,0,v))[2]
    t=(zf(hip[2])-zf(knee_z))/(zf(hip[2])-zf(ankle[2]))
    return (hip[0]+t*(ankle[0]-hip[0]), hip[1]+t*(ankle[1]-hip[1])-forward, knee_z)


# Bacak eklemleri (sol taraf, modelleme uzayı). Diz hizalaması bu sabitlerden türetilir.
# Kalça ve bilek aynı x'te: önden bakınca bacak hattı dikey. Unity testi, hat yana açık
# olduğunda (kalça .092 -> bilek .148, ~11°; hatta 4°) bind pozunda bacak twist kaslarının
# sıfırdan saptığını ve GetHumanPose->SetHumanPose sonrası baldırın 19–55° döndüğünü gösterdi.
# Dikey hatta twist 0.00 ve dönüş 0°. Ön-arka bükülme ve diz bükülmesi x'ten bağımsızdır;
# mesh değişmez (bacak kalıbı x=.09–.147 arasında, eklem hattı ortada).
LEG_HIP=(.12,0,.555)
# Bot ölçeği (v6): bot v5 tasarımından uzunluk, genişlik ve yükseklikte orantılı küçültülür.
# v5 botu ~40 cm uzundu (Mixamo kaynak ayağının ~2.4 katı); Unity'de yürüme/koşuda hantal
# göründü ve taban -7..-9 cm gömüldü. Kullanıcı ~%35 küçültmeyi seçti (%20 ve %50 reddedildi).
# Ölçek merkezi bot ekseninin zemindeki izi: taban zeminde kalır, bot paçanın altında ortalanır.
BOOT_SCALE=.65
BOOT_AXIS_X=.148  # bot ekseni (modelleme uzayı x); paçanın alt halkası da bu eksende


def boot_point(sign, x, y, z):
    # v5 bot tasarım koordinatını (eksene göre x, y, z) ölçeklenmiş modelleme uzayına taşır.
    return (sign*(BOOT_AXIS_X+x*BOOT_SCALE), y*BOOT_SCALE, z*BOOT_SCALE)


# Ayak bileği botla birlikte iner (v5: .155). Kalça ile aynı x: bacak hattı dikey kalır.
LEG_ANKLE=(.12,0,.155*BOOT_SCALE)
# Ayak topu (ToeBase) tabana yakın olmalı. Unity Humanoid tabanı (feetBottomHeight) mesh'ten
# değil, yalnızca Foot/Toes kemiklerinden tahmin eder: ToeBase son uzayda ~4.5 cm'den yüksekse
# tabanı "ToeBase - ~1.25 cm" sayar; humanScale aynı miktarda küçülür ve karakter her klipte
# gömülür (v4: ToeBase 6.2 cm -> taban 4.95 cm havada, humanScale .5636; ölçüm FootProbe).
# Top ve burun ucu bot ile aynı oranda ölçeklenir; z (v5'te 2.5 cm) taban kalınlığıyla küçülür.
LEG_BALL=(.12,-.14*BOOT_SCALE,.025*BOOT_SCALE/.95)
LEG_TOE_END=(.12,-.214*BOOT_SCALE,LEG_BALL[2])
KNEE=knee_on_leg_line(LEG_HIP,LEG_ANKLE,.352,.014)
# Pantolon ve dizliğin modellendiği diz noktası; bacak mesh'i buradan KNEE'ye kaydırılır.
MODELED_KNEE=(.13,0,.352)

# Modelleme uzayındaki eklemler: rig ve bütün ağırlık bantlarının tek kaynağı.
JOINTS = {
    'Hips': ((0,0,.54),(0,0,.65),None),
    'Spine': ((0,0,.65),(0,0,.77),'Hips'),
    'Spine1': ((0,0,.77),(0,0,.875),'Spine'),
    'Spine2': ((0,0,.875),(0,0,.98),'Spine1'),
    'Neck': ((0,0,.98),(0,0,1.065),'Spine2'),
    'Head': ((0,0,1.065),(0,0,1.40),'Neck'),
    'HeadTop_End': ((0,0,1.4),(0,0,1.5),'Head'),
}
for sign, side in ((1,'Left'),(-1,'Right')):
    for name, head, tail, parent in [
        ('Shoulder',(.035,0,.946),(.195,0,.925),'Spine2'),
        ('Arm',(.195,0,.925),(.395,0,.925),'Shoulder'),
        ('ForeArm',(.395,0,.925),(.535,0,.925),'Arm'),
        ('Hand',(.535,0,.925),(.652,0,.925),'ForeArm'),
        ('HandThumb1',(.59,-.025,.917),(.618,-.075,.888),'Hand'),
        ('HandThumb2',(.618,-.075,.888),(.663,-.111,.884),'HandThumb1'),
        ('HandIndex1',(.649,-.050,.934),(.709,-.050,.934),'Hand'),
        ('HandIndex2',(.709,-.050,.934),(.798,-.050,.934),'HandIndex1'),
        ('UpLeg',LEG_HIP,KNEE,'Hips'),
        ('Leg',KNEE,LEG_ANKLE,'UpLeg'),
        ('Foot',LEG_ANKLE,LEG_BALL,'Leg'),
        ('ToeBase',LEG_BALL,LEG_TOE_END,'Foot')]:
        JOINTS[side+name] = (tuple((sign*v if i==0 else v) for i,v in enumerate(head)),
                            tuple((sign*v if i==0 else v) for i,v in enumerate(tail)),
                            parent if parent in ('Hips','Spine2') else side+parent)


def knee_follow(p, sign):
    # Bacak mesh'i eklemi takip eder: kalça ve bilekte sıfır, dizde tam kayma (çadır profili).
    hip_z,knee_z,ankle_z=LEG_HIP[2],KNEE[2],LEG_ANKLE[2]
    z=p[2]
    if z>=hip_z or z<=ankle_z:
        w=0
    elif z>=knee_z:
        w=(hip_z-z)/(hip_z-knee_z)
    else:
        w=(z-ankle_z)/(knee_z-ankle_z)
    return (p[0]+sign*(KNEE[0]-MODELED_KNEE[0])*w, p[1]+(KNEE[1]-MODELED_KNEE[1])*w, p[2])


def smooth(t):
    t=max(0.,min(1.,t))
    return t*t*(3-2*t)


def joint_blend(p, a, b, joint, axis, fraction=.22):
    head,tail,_=JOINTS[joint]
    center=head[axis]
    width=(Vector(tail)-Vector(head)).length*fraction
    value=(p[axis]-center)/width
    return blend_weights(a,b,.5+.5*value)
EXPRESSIONS = {
    'NEUTRAL': {},
    'SURPRISED': {'Mouth_Open': 1, 'Pupils_Small': .3, 'Brows_Worried': .35},
    'HAPPY': {'Eyes_Happy': 1, 'Mouth_Smile': 1},
    'SCARED': {'Mouth_Open': .7, 'Mouth_Frown': .65, 'Brows_Worried': 1, 'Pupils_Small': .65},
    'ANGRY': {'Brows_Angry': 1, 'Mouth_Frown': 1, 'Pupils_Small': .15},
}


def srgb(v):
    return v / 12.92 if v < .04045 else ((v + .055) / 1.055) ** 2.4


def material(name, color, roughness=.58, metal=0):
    mat = bpy.data.materials.new('M_' + name)
    mat.use_nodes = True
    rgb = tuple(srgb(int(color[i:i+2], 16) / 255) for i in (0, 2, 4))
    mat.diffuse_color = (*rgb, 1)
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*rgb, 1)
    shader.inputs['Roughness'].default_value = roughness
    shader.inputs['Metallic'].default_value = metal
    mat.use_fake_user = True
    MATERIALS[name] = mat
    HEX_COLORS[mat.name] = color
    return mat


def build_materials():
    colors = dict(Skin='BD865A', Hair='302823', Jacket='252E46',
                  Reflective_Orange='F47727', Reflective_Silver='C7C9BF', Pants='2D3650',
                  Boots='584034', Sole='282421', Gloves='252627', Belt='654636', Radio='30373A',
                  RadioScreen='829C64', Helmet='D94310', HelmetStrap='777A7E',
                  Lamp='FFF0CA', EyeWhite='DDD9CE', Pupil='111211', Mouth='3E211B')
    for name, color in colors.items():
        roughness = .8 if name in ('Jacket','Pants','Gloves','Hair') else .5
        if name in ('Helmet','EyeWhite','Pupil','Lamp') or name.startswith('Reflective'):
            roughness=.3
        material(name, color, roughness)
    for variant, color in dict(Yellow='F1C52D', Blue='376BE0', Green='53A855').items():
        material('Helmet_' + variant, color, .3)
        material('Reflective_' + variant, color,.32)
    for name,mat in MATERIALS.items():
        if name.startswith('Helmet') and name!='HelmetStrap':
            mat.node_tree.nodes['Principled BSDF'].inputs['Coat Weight'].default_value=.22


def blend_weights(a, b, t):
    t = max(0., min(1., t))
    t = t * t * (3 - 2 * t)
    return {k: v for k, v in ((a, 1-t), (b, t)) if v > 1e-8}


def torso_weights(p):
    z = p[2]
    chain = [(name,JOINTS[name][0][2]) for name in ('Hips','Spine','Spine1','Spine2','Neck','Head')]
    for (a, za), (b, zb) in zip(chain, chain[1:]):
        if z <= zb:
            return blend_weights(a, b, (z-za)/(zb-za))
    return {'Head': 1}


def shoulder_blend(p, side, base, torso=False):
    # Gövde -> köprücük (Shoulder) -> üst kol geçişi, en çok 4 etki.
    # Geçiş geniş tutulur: kol inince ceket koltuk altında dar bir bantta
    # sıkışmaz, omuz üstünde de gerilme geniş alana yayılır.
    x,z=abs(p[0]),p[2]
    clavicle,joint=JOINTS['LeftShoulder'][0],JOINTS['LeftArm'][0]
    arm_length=abs(JOINTS['LeftArm'][1][0]-joint[0])
    under=smooth((joint[2]-z)/.065)
    center=joint[0]+arm_length*(SHOULDER_TOP_SHIFT+(SHOULDER_UNDER_SHIFT-SHOULDER_TOP_SHIFT)*under)
    width=arm_length*(SHOULDER_TOP_WIDTH+(SHOULDER_UNDER_WIDTH-SHOULDER_TOP_WIDTH)*under)
    arm=smooth(.5+.5*(x-center)/width)
    if torso:
        # Gövde yan yüzü koltuk altının altında kola bağlanmaz: kol inince gövde gerilmez.
        arm*=smooth((z-(joint[2]-SHOULDER_TORSO_DEPTH))/.05)
        arm*=1-smooth((z-COLLAR_FADE[0])/COLLAR_FADE[1])  # yaka kolla birlikte çekilmez
    # Köprücük payı omuz kubbesinde; göğüs ve koltuk altı gövdede kalır.
    clav=smooth((x-clavicle[0])/(joint[0]-clavicle[0]))*smooth((z-(joint[2]-.07))/.06)
    clav*=1-smooth((z-COLLAR_FADE[0])/COLLAR_FADE[1])  # yaka boyunla kalır
    rest=1-arm
    weights={k:v*rest*(1-clav) for k,v in base.items()}
    weights[side+'Shoulder']=weights.get(side+'Shoulder',0)+rest*clav
    weights[side+'Arm']=weights.get(side+'Arm',0)+arm
    weights={k:v for k,v in weights.items() if v>1e-3}
    # En güçlü 4 etki tutulur (Unity Skin Weights: 4 Bones).
    return dict(sorted(weights.items(),key=lambda kv:-kv[1])[:4])


# Omuz geçişi ayarları (üst kol boyuna oranla). Unity'de kollar inik pozda ölçülerek
# seçildi: geçiş eklemden içeri kayar ve üst kolun %60'ına yayılır (eski: eklemde, %30).
SHOULDER_TOP_SHIFT=-.15
SHOULDER_UNDER_SHIFT=-.10
SHOULDER_TOP_WIDTH=.60
SHOULDER_UNDER_WIDTH=.60
SHOULDER_TORSO_DEPTH=.03  # gövde yan yüzünde kol etkisinin indiği derinlik (m)
COLLAR_FADE=(.88,.16)  # omuz payının boyuna doğru sönümü (başlangıç z, genişlik); baş dönünce yaka yumuşak gerilir


def upper_body_weights(p):
    # Gövde kabuğu: omuz bölgesinde köprücük ve üst kol etkisi de alır.
    base=torso_weights(p)
    if abs(p[0])<.06 or p[2]<.80:
        return base
    return shoulder_blend(p,'Left' if p[0]>0 else 'Right',base,torso=True)


def arm_weights(side):
    def weights(p):
        x = abs(p[0])
        shoulder=abs(JOINTS[side+'Arm'][0][0])
        elbow=abs(JOINTS[side+'ForeArm'][0][0])
        wrist=abs(JOINTS[side+'Hand'][0][0])
        if x<(shoulder+elbow)/2:
            return shoulder_blend(p,side,{'Spine2':1})
        p=(x,p[1],p[2])
        if x<(elbow+wrist)/2:
            return joint_blend(p,side+'Arm',side+'ForeArm','LeftForeArm',0,.28)
        return joint_blend(p,side+'ForeArm',side+'Hand','LeftHand',0,.25)
    return weights


# Bot ve paça ağırlık bantları (modelleme uzayı, m). Bantlar eklem sabitlerine göre konumlanır.
TOE_BLEND=.08*BOOT_SCALE        # ToeBase ekleminden ileri doğru burun bükülme bandı (y ekseni)
SHIN_FOOT_REACH=.04*BOOT_SCALE  # paçanın bilekten bu yüksekliğe kadarı tamamen Foot'ta (m)
SHIN_FADE=.06         # Foot payının diz altında sönümlendiği bant (m)
PELVIS_BLEND=.45      # kalça geçişinin yarı genişliği (uyluk boyuna oranla)
KNEE_BLEND_SHIFT=.09  # dizin ön yüzünde geçiş merkezinin yukarı kayması (m)
KNEE_BLEND_WIDTH=.14  # diz geçişinin yarı genişliği (m)


def foot_weights(side):
    # Bot rijit parça: tamamı Foot'a bağlı. Yalnızca ToeBase ekleminin önündeki burun
    # ToeBase ile bükülür (Unity yalnızca ağırlık taşıyan kemikleri otomatik eşler).
    def weights(p):
        ball=JOINTS[side+'ToeBase'][0][1]
        toe=smooth((ball-p[1])/TOE_BLEND)  # ileri yön -Y; bükülme eklemden başlar
        return {name:w for name,w in ((side+'ToeBase',toe),(side+'Foot',1-toe)) if w>1e-7}
    return weights


def leg_weights(side):
    def weights(p):
        z = p[2]
        hip=JOINTS[side+'UpLeg'][0][2]
        knee=JOINTS[side+'Leg'][0][2]
        ankle=JOINTS[side+'Foot'][0][2]
        # Paça botun yakasında botla (Foot) gider. Yukarıda Foot payı w=c/d (d: bilekten
        # yükseklik): ayak eğilince baldır noktaları w*d*açı kadar kayar ve w*d sabit
        # kaldığı için kumaş makaslanmaz, yalnızca hafifçe öteleniyor. Dizin altında sönümlenir.
        d=z-ankle
        foot=1. if d<=SHIN_FOOT_REACH else SHIN_FOOT_REACH/d
        foot*=1-smooth((z-(knee-SHIN_FADE))/SHIN_FADE)
        shin=1-foot
        result=foot_weights(side)(p)
        result={name:weight*(1-shin) for name,weight in result.items()}
        result[side+'Leg']=shin
        # Diz geçişi: ön yüzde dizliğin üstüne kayar; dizlik bölgesi tamamen Leg'de kalır.
        front=smooth((-p[1]-.015)/.055)
        center=knee+KNEE_BLEND_SHIFT*front
        width=KNEE_BLEND_WIDTH
        upper=smooth(.5+.5*(z-center)/width)
        result={name:weight*(1-upper) for name,weight in result.items()}
        result[side+'UpLeg']=upper
        pelvis=smooth(.5+.5*(z-hip)/((hip-knee)*PELVIS_BLEND))
        result={name:weight*(1-pelvis) for name,weight in result.items()}
        result['Hips']=pelvis
        result={name:weight for name,weight in result.items() if weight>1e-7}
        return dict(sorted(result.items(),key=lambda kv:-kv[1])[:4])
    return weights



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
        if self.name.endswith('Body') and mat not in ('Skin',):
            segments = max(8, int(segments*.75))
            rings = max(4, int(rings*.75))
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
        if self.name.endswith('Body') and not exact:
            segments = min(segments, 14)
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


def build_head(body, face):
    # Üst yarı kask ve saç altında: seyrek halka. Yanak ve çenede 7.5° adım, silüet yuvarlak kalır.
    head_thetas = [math.radians(a) for a in (30, 52, 66)]+[math.radians(75+7.5*k) for k in range(14)]
    body.sphere((0, .005, 1.185), (.235, .196, .235), 'Skin', segments=32, tag='rigid', thetas=head_thetas)
    verts,faces=[],[]
    # Enseye inen kabuk: ön sınır siperlikte, yan sınır kulak üstünde kalır.
    for j in range(7):
        for i in range(32):
            phi=2*PI*i/32
            rear=max(0,math.sin(phi))
            front=max(0,-math.sin(phi))
            bottom=1.205-.175*rear**.55+.140*front
            end=math.acos((bottom-1.185)/.243)
            theta=.025+(end-.060-.025)*min(j,5)/5
            thickness=.008
            if j==6:
                theta=end
                thickness=.001
            verts.append(((.235+thickness)*math.sin(theta)*math.cos(phi),
                          .005+(.196+thickness)*math.sin(theta)*math.sin(phi),
                          1.185+(.235+thickness)*math.cos(theta)))
    for j in range(6):
        for i in range(32):
            a=j*32+i
            b=j*32+(i+1)%32
            faces.append((a,b,b+32,a+32))
    body.add(verts,faces,'Hair')
    # Saçın ön kısmı tenin arkasında kalır; ense ve şakaklar görünür.
    for s in (-1, 1):
        body.sphere((s*.23, .005, 1.16), (.038, .052, .067), 'Skin', segments=12, rings=7)
        body.sphere((s*.213, -.014, 1.263), (.021, .04, .038), 'Hair', segments=10, rings=6)
    body.sphere((0, -.195, 1.128), (.026, .035, .029), 'Skin', segments=10, rings=6)
    # Boyun yakanın içine uzanır ve yaka ağzını doldurur: baş dönünce yaka içinde boşluk görünmez.
    body.loft([((0,0,z),rx,ry) for z,rx,ry in NECK_RINGS],'Skin',torso_weights,segments=12)
    for s, side in ((1, 'L'), (-1, 'R')):
        x = s*.101
        face.sphere((x, -.16, 1.214), (.094, .071, .102), 'EyeWhite', segments=16, rings=10, tag='eye_'+side)
        face.sphere((x-s*.012, -.225, 1.204), (.025, .014, .030), 'Pupil', segments=12, rings=6, tag='pupil_'+side)
        face.sphere((x-s*.018, -.238, 1.218), (.006, .003, .007), 'EyeWhite', segments=6, rings=3, tag='glint_'+side)
        points = [(x+.068*t, -.176-.022*(1-t*t), 1.292+.009*(1-t*t)) for t in [-1, -.66, -.33, 0, .33, .66, 1]]
        face.tube(points, .008, 'Hair', sides=4, tag='brow_'+side)
        # Göz kapağı çizgileri Basis'te başın içinde; ilgili key yüzeye çıkarır.
        points = [(x+.069*t, -.065, 1.206+.026*(1-t*t)) for t in [-1, -.75, -.5, -.25, 0, .25, .5, .75, 1]]
        face.tube(points, .007, 'Hair', sides=4, tag='lid_'+side)
    # Dolu ağız diski: açılma ve gülme aynı topoloji üzerinde yapılır.
    verts = [(0, -.188, 1.077)]
    for i in range(32):
        t = 2*PI*i/32
        x = .046*math.cos(t)
        verts.append((x, -.189+.011*(x/.046)**2, 1.077+.012*(x/.046)**2+.0035*math.sin(t)))
    face.add(verts, [(0, 1+i, 1+(i+1)%32) for i in range(32)], 'Mouth', tag='mouth')


# Boyun halkaları (z, rx, ry): yaka üst halkası z=1.042'de .093 x .081.
NECK_RINGS=((.935,.080,.070),(.99,.082,.072),(1.035,.080,.070),(1.075,.071,.063),(1.105,.052,.047))


def build_helmet(helmet):
    # Arkaya doğru alçalan kubbe; siperlik gözlerin hemen üzerinde kalır.
    def shell(t, phi, inflate=0):
        base=1.274-.049*math.sin(phi)
        return ((.257+inflate)*math.cos(t)*math.cos(phi),
                .016+(.220+inflate)*math.cos(t)*math.sin(phi),
                base+(1.494-base)*math.sin(t))
    verts,faces=[],[]
    for j in range(11):
        t=(PI/2-.002)*j/10
        verts.extend(shell(t,2*PI*i/24) for i in range(24))
    for j in range(10):
        for i in range(24):
            a=j*24+i; b=j*24+(i+1)%24
            faces.append((a,b,b+24,a+24))
    faces.append(tuple(240+i for i in range(24)))
    helmet.add(verts,faces,'Helmet')
    # Önde uzun, arkada enseyi örten kalın kenar.
    verts,faces=[],[]
    for dr,dz in ((0,.01),(.019,0),(.021,.024),(0,.036)):
        for i in range(32):
            phi=2*PI*i/32
            front=max(0,-math.sin(phi))
            verts.append(((.256+dr)*math.cos(phi),
                          .016+(.225+dr+.035*front)*math.sin(phi),
                          1.258-.049*math.sin(phi)+dz))
    for j in range(3):
        for i in range(32):
            a=j*32+i; b=j*32+(i+1)%32
            faces.append((a,b,b+32,a+32))
    helmet.add(verts,faces,'Helmet')
    # Üç kapalı kaburga: orta sırt geniş, yan sırtlar daha incedir.
    for x,width,lift in ((0,.035,.016),(-.139,.011,.008),(.139,.011,.008)):
        verts,faces=[],[]
        radius=math.sqrt(1-(x/.257)**2)
        for j in range(15):
            t=PI*j/14
            y=.016-.220*radius*math.cos(t)
            base=1.274+.049*math.cos(t)
            z=base+(1.494-base)*radius*math.sin(t)
            for dx,dz in ((-width,0),(-width*.8,lift),(width*.8,lift),(width,0)):
                verts.append((x+dx,y,z+dz))
        for j in range(14):
            # Alt yüz (k=3) kabuğun içinde kalır, görünmeyen üçgen üretilmez.
            for k in range(3):
                a=j*4+k; b=j*4+(k+1)%4
                faces.append((a,b,b+4,a+4))
        faces.extend([(3,2,1,0),tuple(56+k for k in range(4))])
        helmet.add(verts,faces,'Helmet')
    helmet.box((0,-.209,1.384),(.090,.056,.090),'Radio','Head',.017)
    # Önden görünen disk kenarını halkalar çizer; çevre segmenti az olabilir.
    helmet.sphere((0,-.242,1.384),(.033,.012,.033),'Lamp',segments=10,rings=8)
    for s in (-1, 1):
        points = [(s*.237,-.022,1.306),(s*.225,-.07,1.218),(s*.204,-.095,1.118),(s*.145,-.138,1.058),(s*.059,-.16,1.036),(0,-.17,1.036)]
        helmet.tube(points, .012, 'HelmetStrap', sides=8)
    helmet.box((0, -.173, 1.04), (.089, .026, .03), 'HelmetStrap', 'Head', .011)


def torso_rings(zs, extra=0):
    profile = [(.55,.184,.118),(.60,.188,.12),(.70,.173,.116),(.80,.163,.115),(.89,.175,.112),(.94,.174,.105),(.975,.150,.100),(.99,.120,.095)]
    result = []
    for z in zs:
        for i in range(len(profile)-1):
            a, b = profile[i:i+2]
            if a[0] <= z <= b[0]:
                t = (z-a[0])/(b[0]-a[0])
                result.append(((0,0,z), a[1]*(1-t)+b[1]*t+extra, a[2]*(1-t)+b[2]*t+extra))
                break
    return result


def build_body(body):
    # Şeritler kumaşın aynı yüzeyindeki materyal bölgeleri: pozu takip eder, yüzmez.
    def jacket_material(p):
        for low,high in ((.56,.618),(.785,.865)):
            if low<p[2]<high:
                return 'Reflective_Silver' if low+.018<p[2]<high-.018 else 'Reflective_Orange'
        return 'Jacket'
    # Düz kumaşta gereksiz halkalar yerine başın silüetine üçgen ayır.
    # Gövde, omuz kubbesi ve dik yaka tek loft: ayrı kabuk bırakmaz, omuzda kutu kapağı
    # gibi çıkıntı oluşmaz. Üst halkalar boyna doğru yuvarlak daralır.
    zs=[.55,.56,.578,.600,.618,.785,.803,.847,.865,.94]
    shoulder=[((0,0,z),rx,ry) for z,rx,ry in ((.958,.170,.106),(.975,.158,.104),(.99,.138,.100),(1.003,.118,.095),(1.015,.105,.090),(1.03,.099,.086),(1.042,.093,.081))]
    body.loft(torso_rings(zs)+shoulder,jacket_material,upper_body_weights,segments=16,exact=True)
    front_profile=torso_rings([.55,.565,.618,.70,.785,.865,.94,.96,.975],.002)
    vertices=[(x,-ry,center[2]) for center,rx,ry in front_profile for x in (-.017,.017)]
    body.add(vertices,[(2*i,2*i+1,2*i+3,2*i+2) for i in range(len(front_profile)-1)],'Jacket',torso_weights)
    body.tube([(0,-ry-.002,center[2]) for center,rx,ry in front_profile],.002,'Radio',torso_weights,sides=4)
    body.box((.012,-.134,.918), (.014,.01,.025), 'HelmetStrap', 'Spine2', .003)
    # 16 segmentli gövde köşeleri kemer kenarından taşmasın diye 2 mm pay.
    body.loft([((0,0,.661),.182,.126),((0,0,.711),.182,.126)], 'Belt', torso_weights, segments=24)
    body.box((0,-.138,.686),(.065,.025,.049),'HelmetStrap','Hips',.009)
    body.box((0,-.154,.686),(.036,.008,.03),'Radio','Hips',.004)
    for x,y in ((-.157,-.095),(.166,.032)):
        body.box((x,y,.652),(.085,.075,.106),'Belt','Hips',.016)
        body.box((x,y-.041,.679),(.08,.012,.044),'Belt','Hips',.008)
        body.sphere((x,y-.05,.672),(.009,.006,.009),'HelmetStrap','Hips',segments=8,rings=4)
    body.box((.135,-.161,.66),(.077,.042,.117),'Radio','Hips',.009)
    body.box((.135,-.185,.684),(.049,.009,.032),'RadioScreen','Hips',.003)
    body.box((.115,-.154,.75),(.009,.012,.078),'Radio','Hips',.003)
    for z in (.646,.63,.614):
        for x in (.117,.135,.153):
            body.box((x,-.186,z),(.009,.006,.009),'HelmetStrap','Hips',0)
    body.loft([((0,0,.505),.146,.094),((0,0,.555),.174,.112),((0,0,.59),.168,.108)],'Pants','Hips',segments=16)
    for s,side in ((1,'Left'),(-1,'Right')):
        aw = arm_weights(side)
        # Gövdeye gömülü kökten alçak, yuvarlak omuz başına; kolun üst çizgisi omuz eğimine akar.
        rings = [((s*x,0,.925),ry,rz) for x,ry,rz in [(.12,.040,.036),(.15,.066,.060),(.19,.074,.067),(.23,.071,.066),(.26,.0665,.0635),(.277,.0630,.0615),(.293,.0615,.0605),(.32,.059,.058),(.336,.058,.0575),(.42,.055,.054),(.48,.050,.049),(.525,.046,.0455)]]
        def sleeve_material(p):
            x=abs(p[0])
            if .277<x<.336:
                return 'Reflective_Silver' if .293<x<.32 else 'Reflective_Orange'
            return 'Jacket'
        body.loft(rings,sleeve_material,aw,segments=16,axis='X',exact=True)
        body.loft([((s*x,0,.925),r,r) for x,r in ((.511,.067),(.519,.074),(.558,.074),(.568,.065))],'Gloves',aw,segments=16,axis='X',exact=True)
        lw = leg_weights(side)
        leg_start=len(body.vertices)
        # Paça küçülen bota iner: manşet botun v5 z=.188 halkasının ölçeklenmiş yüksekliğinde,
        # bot yüzeyinden v5'teki payla büyük (dış .014/.016, içe kıvrım .005/.007). Baldırdan
        # manşete paça hafifçe daralır; ara halka bilek bükülmesinde kumaşı yumuşatır.
        cuff_z=.185*BOOT_SCALE
        boot_rx,boot_ry=.079*BOOT_SCALE,.075*BOOT_SCALE
        lower=[(.25,.141,.086,.088),((.25+cuff_z)/2,.144,(.086+boot_rx+.014)/2+.002,(.088+boot_ry+.016)/2+.002),
               (cuff_z,BOOT_AXIS_X-.001,boot_rx+.014,boot_ry+.016),(cuff_z+.007,BOOT_AXIS_X-.001,boot_rx+.005,boot_ry+.007)]
        body.loft([((s*x,0,z),rx,ry) for z,x,rx,ry in [(.565,.09,.076,.079),(.52,.10,.084,.096),(.45,.116,.09,.101),(.415,.12,.082,.078),(.39,.124,.079,.066),(.37,.128,.076,.06),(.352,.13,.075,.058),(.334,.133,.077,.062),(.31,.136,.081,.07),(.28,.139,.084,.081)]+lower], 'Pants',lw,segments=12)
        # Cepler bacağın eliptik yan yüzeyini izler; gövdeye gömülü kenarları vardır.
        for z0,z1,mat,depth in ((.408,.514,'Pants',.014),(.493,.522,'Jacket',.018)):
            verts,faces=[],[]
            for z in (z0,(z0+z1)/2,z1):
                for k in range(5):
                    phi=-.58+k*.29
                    verts.append((s*(.111+(.087+depth)*math.cos(phi)),.101*math.sin(phi),z))
            for j in range(2):
                for k in range(4):
                    a=j*5+k; faces.append((a,a+1,a+6,a+5))
            body.add(verts,faces,mat,lw)
        # Dizlik rijit parça: tek kemiğe (Leg) bağlı, diz bükülünce uzamaz.
        body.sphere((s*.13,-.065,KNEE[2]-.012),(.063,.025,.046),'Radio',side+'Leg',segments=16,rings=8)
        body.sphere((s*.13,-.082,KNEE[2]-.012),(.049,.016,.035),'Pants',side+'Leg',segments=12,rings=6)
        # Pantolon, cepler ve dizlik modellendikleri dizden eklem sabitindeki dize kayar.
        for i in range(leg_start,len(body.vertices)):
            body.vertices[i]=knee_follow(body.vertices[i],s)
        fw=foot_weights(side)
        # Bot gövdesi ve yakası tek loft: ayrı kabuklar arasında kesişme dikişi kalmaz.
        # Paça manşeti yakanın üstünde biter; yaka paçanın içe kıvrımından dar, iki yüzey çakışmaz.
        # Bot v5 tasarım ölçüleriyle yazılır ve boot_point/BOOT_SCALE ile orantılı küçülür
        # (burun, topuk, taban kalınlığı ve kabara dahil).
        B=BOOT_SCALE
        body.loft([(boot_point(s,0,y,z),rx*B,ry*B) for z,y,rx,ry in [(.035,-.041,.103,.15),(.065,-.054,.117,.173),(.105,-.051,.119,.169),(.14,-.038,.11,.145),(.160,-.014,.093,.100),(.174,-.003,.088,.087),(.188,0,.079,.075),(.22,0,.071,.066)]], 'Boots',fw,segments=12)
        body.loft([(boot_point(s,0,-.041,z),rx*B,ry*B) for z,rx,ry in [(0,.111,.169),(.012,.119,.177),(.044,.119,.177),(.053,.113,.17)]], 'Sole',fw,segments=12)
        for y in (-.16,-.08,0,.08):
            edge=.100*math.sqrt(1-((y+.041)/.177)**2)
            for dx in (-edge,edge):
                body.box(boot_point(s,s*dx,y,.022),(.022*B,.035*B,.028*B),'Sole',fw,0)
        for z,y in ((.167,-.105),(.137,-.138)):
            body.box(boot_point(s,0,y,z),(.084*B,.014*B,.014*B),'Belt',fw,0)


def build_hands(body):
    for s,side in ((1,'Left'),(-1,'Right')):
        body.sphere((s*.627,.002,.925),(.088,.075,.065),'Gloves',side+'Hand',segments=16,rings=10)
        # Üç parmak içe kıvrık: kısa eldiven, işaret parmağının ucundan önce biter.
        body.sphere((s*.684,.033,.909),(.055,.058,.046),'Gloves',side+'Hand',segments=16,rings=6)
        def finger_weights(kind):
            first,second=side+'Hand'+kind+'1',side+'Hand'+kind+'2'
            start=Vector(JOINTS[first][0]); end=Vector(JOINTS[second][1])
            axis=(end-start).normalized()
            joint=Vector(JOINTS[second][0])
            first_length=(joint-start).length
            width=(end-joint).length*.28
            def weights(p):
                distance=(Vector(p)-start).dot(axis)
                if distance<first_length*.30:
                    return blend_weights(side+'Hand',first,distance/(first_length*.30))
                return blend_weights(first,second,.5+.5*(Vector(p)-joint).dot(axis)/width)
            return weights
        for kind in ('Index','Thumb'):
            start=Vector(JOINTS[side+'Hand'+kind+'1'][0])
            joint=Vector(JOINTS[side+'Hand'+kind+'2'][0])
            end=Vector(JOINTS[side+'Hand'+kind+'2'][1])
            first_axis=(joint-start).normalized()
            last_axis=(end-joint).normalized()
            # Kesintisiz kapsül ve kademeli ağırlıklar: parmak avuçtan kopmaz.
            if kind=='Index':
                points=[start-first_axis*.020,start,joint,end-last_axis*.030,
                        end-last_axis*.016,end-last_axis*.005,end+last_axis*.002]
                radii=[.028,.036,.036,.036,.031,.019,.002]
            else:
                points=[start-first_axis*.012,start,joint,end-last_axis*.015,
                        end,end+last_axis*.018,end+last_axis*.029]
                radii=[.024,.034,.035,.035,.030,.020,.003]
            body.tube(points,radii,'Gloves',finger_weights(kind),sides=10)


def build_rig():
    data = bpy.data.armatures.new('RescueCrew_Rig')
    rig = bpy.data.objects.new('RescueCrew_Rig',data)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active=rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    def bone(name,head,tail,parent=None):
        b=data.edit_bones.new(PREFIX+name)
        b.head,b.tail=head,tail
        if parent:
            b.parent=data.edit_bones[PREFIX+parent]
            b.use_connect=(b.head-b.parent.tail).length<1e-5
        b.use_deform=True
    for name,(head,tail,parent) in JOINTS.items():
        bone(name,character_point(head),character_point(tail),parent)
    bpy.ops.object.mode_set(mode='OBJECT')
    rig.show_in_front=True
    data.display_type='OCTAHEDRAL'
    rig.select_set(False)
    return rig


def assign_weights(obj, geometry, rig):
    groups = {b.name:obj.vertex_groups.new(name=b.name) for b in rig.data.bones}
    for index, weights in enumerate(geometry.weights):
        assert 1 <= len(weights) <= 4
        total=sum(weights.values())
        for name,w in weights.items():
            groups[PREFIX+name].add([index],w/total,'REPLACE')
    obj.parent=rig
    modifier=obj.modifiers.new('Armature','ARMATURE')
    modifier.object=rig
    modifier.use_deform_preserve_volume=False


def build_shape_keys(obj, geo):
    obj.shape_key_add(name='Basis',from_mix=False)
    for name in SHAPES:
        key=obj.shape_key_add(name=name,from_mix=False)
        key.value=0
        key.slider_min,key.slider_max=0,1
        for side,s in (('L',1),('R',-1)):
            x=s*.101
            if name in ('Blink_'+side,'Eyes_Happy'):
                for tag in ('eye_','pupil_','glint_'):
                    for i in geo.tags[tag+side]:
                        v=key.data[i].co
                        v.y += .16
                        v.z=1.21+(v.z-1.21)*.02
                for i in geo.tags['lid_'+side]:
                    v=key.data[i].co
                    v.y-=.145
                    if name.startswith('Blink'):
                        v.z=1.206+(v.z-1.206)*.12
            if name=='Pupils_Small':
                for tag in ('pupil_','glint_'):
                    for i in geo.tags[tag+side]:
                        v=key.data[i].co
                        v.x=x-s*.012+(v.x-(x-s*.012))*.48
                        v.z=1.204+(v.z-1.204)*.48
            if name in ('Brows_Angry','Brows_Worried'):
                for i in geo.tags['brow_'+side]:
                    v=key.data[i].co
                    t=(v.x-x)*s/.068
                    v.z += (.026*t-.007) if name=='Brows_Angry' else (-.020*t+.008)
                    v.y-=.055
        if name.startswith('Mouth_'):
            for i in geo.tags['mouth']:
                v=key.data[i].co
                u=v.x/.046
                if name=='Mouth_Smile':
                    v.z+=.014*u*u-.004
                    v.x*=1.12
                elif name=='Mouth_Frown':
                    v.z-=.029*u*u-.005
                else:
                    residual=v.z-(1.077+.012*u*u)
                    v.z=1.081+residual*12
                    v.x*=.85
                    v.y=-.205+.01*u*u
    obj['expression_presets']=json.dumps(EXPRESSIONS)


def triangles(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def measure(objects):
    points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
    return max(p.z for p in points)-min(p.z for p in points)


def export(out, rig, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [rig]+objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active=rig
    # Lineer Base Color FBX'e olduğu gibi yazılır (Unity Linear URP).
    bpy.ops.export_scene.fbx(filepath=str(out/'SK_RescueCrew.fbx'),use_selection=True,
        object_types={'ARMATURE','MESH'},apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',add_leaf_bones=False,use_armature_deform_only=True,
        bake_anim=False,mesh_smooth_type='FACE',use_mesh_modifiers=False,
        use_custom_props=True,check_existing=False)
    # Dosya zaman damgasından bağımsız deterministik geometri parmak izi.
    signature=[]
    for obj in objects:
        signature.append({'name':obj.name,'vertices':[[round(c,7) for c in v.co] for v in obj.data.vertices],
            'faces':[list(p.vertices) for p in obj.data.polygons],
            'weights':[[(g.group,round(g.weight,7)) for g in v.groups] for v in obj.data.vertices],
            'keys':{k.name:[[round(c,7) for c in v.co] for v in k.data] for k in obj.data.shape_keys.key_blocks} if obj.data.shape_keys else {}})
    digest=hashlib.sha256(json.dumps(signature,separators=(',',':')).encode()).hexdigest()
    stats={'total_triangles':sum(triangles(o) for o in objects),
           'object_triangles':{o.name:triangles(o) for o in objects},
           'height_m':round(measure(objects),6),'bones':[b.name for b in rig.data.bones],
           'shape_keys':SHAPES,'materials':sorted(m.name for m in bpy.data.materials),
           'units':'meters','forward':'-Y','max_vertex_influences':max(len(v.groups) for o in objects for v in o.data.vertices),
           'expression_presets':EXPRESSIONS,'geometry_sha256':digest}
    (out/'stats.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
    print('BUILD STATS',json.dumps(stats),flush=True)
    chin=character_point((0,0,1.185-.235))[2]
    hip=character_point(JOINTS['LeftUpLeg'][0])[2]
    shoulder=character_point(JOINTS['LeftShoulder'][0])[2]
    stats['proportions']={name:round(value/stats['height_m'],4) for name,value in
        {'head_helmet':stats['height_m']-chin,'shoulder_hip':shoulder-hip,'legs_boots':hip}.items()}
    (out/'stats.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
    assert 5000<=stats['total_triangles']<=10000,stats['total_triangles']
    assert 1.47<=stats['height_m']<=1.53,stats['height_m']


def point_camera(camera, location, target, ortho=None):
    camera.location=location
    camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO' if ortho else 'PERSP'
    camera.data.ortho_scale=ortho or 1.8
    camera.data.lens=52


def set_expression(face, values):
    for key in face.data.shape_keys.key_blocks:
        if key.name!='Basis':
            key.value=values.get(key.name,0)
    bpy.context.view_layer.update()


def setup_render():
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.samples=64
    scene.cycles.use_denoising=True
    scene.cycles.seed=0
    scene.cycles.use_animated_seed=False
    scene.cycles.max_bounces=6
    scene.cycles.adaptive_threshold=.045
    prefs=bpy.context.preferences.addons['cycles'].preferences
    device_label='CPU'
    for backend in ('OPTIX','CUDA'):
        try:
            prefs.compute_device_type=backend
            prefs.refresh_devices()
            devices=[d for d in prefs.devices if d.type==backend]
            if devices:
                for device in prefs.devices:
                    device.use=device.type==backend
                scene.cycles.device='GPU'
                device_label=backend+': '+', '.join(d.name for d in devices)
                break
        except (TypeError,RuntimeError) as error:
            print('DEVICE FALLBACK',backend,str(error),flush=True)
    if device_label=='CPU':
        scene.cycles.device='CPU'
    scene['render_device']=device_label
    print('RENDER DEVICE',device_label,flush=True)
    scene.render.resolution_x=scene.render.resolution_y=1024
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.film_transparent=True
    scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(1,1,1,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.35
    scene.view_settings.view_transform='AgX'
    scene.view_settings.look='AgX - Medium High Contrast'
    scene.view_settings.exposure=-.6
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.004))
    floor=bpy.context.object
    floor.name='Studio_Floor'
    floor.is_shadow_catcher=True
    floor.data.materials.append(material('StudioFloor','F3F3F3',.85))
    # Beyaz fon üzerinde Cycles'ın gerçek yumuşak temas gölgesi.
    group=bpy.data.node_groups.new('Studio_Compositor','CompositorNodeTree')
    scene.compositing_node_group=group
    group.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor')
    layers=group.nodes.new('CompositorNodeRLayers')
    over=group.nodes.new('CompositorNodeAlphaOver')
    over.inputs['Background'].default_value=(4,4,4,1)
    over.inputs['Factor'].default_value=1
    output=group.nodes.new('NodeGroupOutput')
    group.links.new(layers.outputs['Image'],over.inputs['Foreground'])
    group.links.new(over.outputs[0],output.inputs[0])
    bpy.ops.object.camera_add()
    camera=bpy.context.object
    camera.name='Presentation_Camera'
    scene.camera=camera
    for name,loc,power,size in [('Key',(-3,-4,5),650,4),('Fill',(3,-2,3),330,3),('Rim',(0,3,4),800,3)]:
        data=bpy.data.lights.new(name,'AREA')
        data.energy,data.shape,data.size=power,'DISK',size
        obj=bpy.data.objects.new(name,data)
        scene.collection.objects.link(obj)
        obj.location=loc
        obj.rotation_euler=(Vector((0,0,.8))-obj.location).to_track_quat('-Z','Y').to_euler()
    return camera


def render_image(out,name):
    bpy.context.scene.render.filepath=str(out/'renders'/name)
    bpy.ops.render.render(write_still=True)
    print('RENDER',name,flush=True)


def snapshot(objects, offset, head_only=False):
    copies=[]
    deps=bpy.context.evaluated_depsgraph_get()
    for obj in objects:
        mesh=bpy.data.meshes.new_from_object(obj.evaluated_get(deps),depsgraph=deps)
        if head_only and obj.name.endswith('Body'):
            group=obj.vertex_groups[PREFIX+'Head'].index
            keep={v.index for v in obj.data.vertices if any(g.group==group and g.weight>.999 for g in v.groups)}
            bm=bmesh.new()
            bm.from_mesh(mesh)
            bmesh.ops.delete(bm,geom=[v for v in bm.verts if v.index not in keep],context='VERTS')
            bm.to_mesh(mesh)
            bm.free()
        copy=bpy.data.objects.new('Preview_'+obj.name,mesh)
        bpy.context.scene.collection.objects.link(copy)
        copy.location.x=offset
        copies.append(copy)
    return copies


def clear_copies(objects):
    for obj in objects:
        data=obj.data
        bpy.data.objects.remove(obj,do_unlink=True)
        if data.users==0:
            bpy.data.meshes.remove(data)


def label(text,x,z,size=.04):
    data=bpy.data.curves.new('Caption','FONT')
    data.body=text
    data.align_x='CENTER'
    data.size=size
    data.materials.append(MATERIALS['Jacket'])
    obj=bpy.data.objects.new('Caption',data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location=(x,-.31,z)
    obj.rotation_euler=(PI/2,0,0)
    return obj


def pose_rotation(rig,name,axis,degrees):
    bone=rig.pose.bones[PREFIX+name]
    local_axis=bone.bone.matrix_local.to_3x3().inverted()@Vector(axis)
    bone.rotation_mode='QUATERNION'
    bone.rotation_quaternion=Quaternion(local_axis,math.radians(degrees))


def render(out,rig,objects,preview=False):
    started=time.perf_counter()
    camera=setup_render()
    scene=bpy.context.scene
    if preview:
        scene.render.resolution_x=scene.render.resolution_y=640
    face=objects[2]
    for name,loc,target,ortho in [
        ('front.png',(0,-4,.78),(0,0,.76),1.78),
        ('side_left.png',(4,0,.78),(0,0,.76),1.78),
        ('back.png',(0,4,.78),(0,0,.76),1.78),
        ('three_quarter.png',(1.8,-2.8,1.7),(0,0,.78),None)]:
        point_camera(camera,loc,target,ortho)
        render_image(out,name)
    if preview:
        return
    pose_rotation(rig,'LeftArm',(0,1,0),60)
    pose_rotation(rig,'RightArm',(0,1,0),-60)
    pose_rotation(rig,'LeftForeArm',(0,0,1),-90)
    pose_rotation(rig,'RightUpLeg',(1,0,0),-22)
    pose_rotation(rig,'RightLeg',(1,0,0),60)
    pose_rotation(rig,'LeftUpLeg',(1,0,0),12)
    bpy.context.view_layer.update()
    deps=bpy.context.evaluated_depsgraph_get()
    lowest=min((obj.matrix_world@v.co).z for obj in objects for v in obj.evaluated_get(deps).data.vertices)
    rig.location.z=-min(0,lowest)
    point_camera(camera,(2,-3.6,1.8),(0,0,.77),1.8)
    render_image(out,'pose_test.png')
    for bone in rig.pose.bones:
        bone.matrix_basis.identity()
    rig.location.z=0
    bpy.context.view_layer.update()
    copies,labels=[],[]
    for i,(name,values) in enumerate(EXPRESSIONS.items()):
        set_expression(face,values)
        x=(i-2)*.60
        copies.extend(snapshot(objects,x,head_only=True))
        labels.append(label(name,x,.87,.044))
    set_expression(face,{})
    for obj in objects:
        obj.hide_render=True
    scene.render.resolution_x,scene.render.resolution_y=2048,512
    point_camera(camera,(0,-5,1.20),(0,0,1.20),3.18)
    # Kare kontakt sayfası; yüzler üst üste gelmez.
    render_image(out,'expressions.png')
    clear_copies(copies)
    for obj in labels:
        bpy.data.objects.remove(obj,do_unlink=True)
    copies,labels=[],[]
    for obj in objects:
        obj.hide_render=False
    pose_rotation(rig,'LeftArm',(0,1,0),68)
    pose_rotation(rig,'RightArm',(0,1,0),-68)
    bpy.context.view_layer.update()
    for i,name in enumerate(('Orange','Yellow','Blue','Green')):
        x=(i-1.5)*.94
        batch=snapshot(objects,x)
        if name!='Orange':
            for obj in batch:
                for slot in obj.material_slots:
                    if slot.material.name=='M_Helmet':
                        slot.material=MATERIALS['Helmet_'+name]
                    elif slot.material.name=='M_Reflective_Orange':
                        slot.material=MATERIALS['Reflective_'+name]
        copies.extend(batch)
        labels.append(label(name.upper(),x,-.09,.06))
    for obj in objects:
        obj.hide_render=True
    scene.render.resolution_x,scene.render.resolution_y=2048,1024
    point_camera(camera,(0,-8,.72),(0,0,.72),3.96)
    render_image(out,'variants.png')
    clear_copies(copies)
    for obj in labels:
        bpy.data.objects.remove(obj,do_unlink=True)
    for obj in objects:
        obj.hide_render=False
    # Vitrin için geçici subdiv; kayıt ve FBX yalnızca oyun geometrisini taşır.
    scene.render.resolution_x=scene.render.resolution_y=1536
    scene.cycles.samples=96
    set_expression(face,{'Mouth_Smile':.5})
    modifiers=[]
    try:
        for obj in objects:
            mod=obj.modifiers.new('HeroOnly_Subdivision','SUBSURF')
            mod.levels=1
            mod.render_levels=1
            modifiers.append((obj,mod))
        point_camera(camera,(2.1,-3.5,1.9),(0,0,.78),None)
        camera.data.lens=68
        render_image(out,'hero.png')
    finally:
        for obj,mod in modifiers:
            obj.modifiers.remove(mod)
        for bone in rig.pose.bones:
            bone.matrix_basis.identity()
        set_expression(face,{})
    make_comparison(out)
    elapsed=time.perf_counter()-started
    stats_path=out/'stats.json'
    stats=json.loads(stats_path.read_text(encoding='utf-8'))
    stats['render']={'engine':'CYCLES','device':scene['render_device'],'samples':64,
                     'hero_samples':96,'seconds':round(elapsed,2),'view_transform':'AgX'}
    stats_path.write_text(json.dumps(stats,indent=2),encoding='utf-8')
    print('RENDER TOTAL SECONDS',round(elapsed,2),flush=True)
    scene.cycles.samples=64
    point_camera(camera,(1.8,-2.8,1.7),(0,0,.78),None)


def make_comparison(out):
    # Referans salt okunur; ilk görünüş kırpılıp Blender piksel tamponunda birleştirilir.
    import numpy as np
    reference=bpy.data.images.load(str(out/'ref'/'konsept_rescue_crew.jpg'),check_existing=False)
    front=bpy.data.images.load(str(out/'renders'/'front.png'),check_existing=False)
    def pixels(img):
        values=np.empty(img.size[0]*img.size[1]*4,dtype=np.float32)
        img.pixels.foreach_get(values)
        return values.reshape(img.size[1],img.size[0],4)
    left=pixels(reference)[:,:570,:]
    right=pixels(front)
    canvas=np.ones((1024,2048,4),dtype=np.float32)
    for source,start in ((left,0),(right,1024)):
        h,w=source.shape[:2]
        scale=min(960/w,960/h)
        nh,nw=int(h*scale),int(w*scale)
        resized=source[np.minimum((np.arange(nh)/scale).astype(int),h-1)[:,None],
                       np.minimum((np.arange(nw)/scale).astype(int),w-1)[None,:]]
        y=(1024-nh)//2; x=start+(1024-nw)//2
        canvas[y:y+nh,x:x+nw]=resized
    image=bpy.data.images.new('Concept_Comparison',2048,1024,alpha=True)
    image.pixels.foreach_set(canvas.ravel())
    image.filepath_raw=str(out/'renders'/'compare.png')
    image.file_format='PNG'
    image.save()
    for img in (image,reference,front):
        bpy.data.images.remove(img)



def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--out',type=Path,required=True)
    parser.add_argument('--preview',action='store_true')
    parser.add_argument('--skip-renders',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    out=args.out.resolve()
    # GPU önbelleği yalnızca izin verilen araç dizininde tutulur.
    os.environ['OPTIX_CACHE_PATH']=str(Path(__file__).resolve().parent)
    os.chdir(Path(__file__).resolve().parent)
    out.mkdir(parents=True,exist_ok=True)
    (out/'renders').mkdir(exist_ok=True)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    # factory-startup dahil tüm kullanılmayan materyaller temizlenir.
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)
    bpy.context.scene.unit_settings.system='METRIC'
    bpy.context.scene.unit_settings.scale_length=1
    bpy.context.preferences.filepaths.save_version=0
    bpy.context.preferences.filepaths.use_auto_save_temporary_files=False
    bpy.context.preferences.filepaths.file_preview_type='NONE'
    build_materials()
    body,helmet,face=[Geometry('SK_RescueCrew_'+name) for name in ('Body','Helmet','Face')]
    build_head(body,face)
    build_helmet(helmet)
    build_body(body)
    build_hands(body)
    rig=build_rig()
    objects=[]
    for geometry in (body,helmet,face):
        obj=geometry.object()
        assign_weights(obj,geometry,rig)
        objects.append(obj)
    build_shape_keys(objects[2],face)
    # Shape key dahil bütün noktaları aynı oran dönüşümünden geçir.
    for obj in objects:
        if obj.data.shape_keys:
            for key in obj.data.shape_keys.key_blocks:
                for vertex in key.data:
                    vertex.co=character_point(vertex.co)
            for vertex,basis in zip(obj.data.vertices,obj.data.shape_keys.key_blocks['Basis'].data):
                vertex.co=basis.co
        else:
            rigid=set(body.tags.get('rigid',())) if obj.name==body.name else set()
            for vertex in obj.data.vertices:
                vertex.co=character_point(vertex.co,vertex.index in rigid)
    export(out,rig,objects)
    if not args.skip_renders:
        render(out,rig,objects,args.preview)
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.wm.save_as_mainfile(filepath=str(out/'SK_RescueCrew.blend'),check_existing=False)
    print('BUILD OK',flush=True)


if __name__=='__main__':
    main()

