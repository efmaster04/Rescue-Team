"""Üretilen FBX'i bağımsız boş sahnede doğrular; başarısızlık exit 1."""
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

REQUIRED_SHAPES = {'Blink_L','Blink_R','Brows_Angry','Brows_Worried','Mouth_Smile',
                   'Mouth_Open','Mouth_Frown','Pupils_Small','Eyes_Happy'}
REQUIRED_BONES = {'Hips','Spine','Spine1','Spine2','Neck','Head','HeadTop_End'}
for side in ('Left','Right'):
    REQUIRED_BONES.update(side+name for name in ('Shoulder','Arm','ForeArm','Hand',
        'HandThumb1','HandThumb2','HandIndex1','HandIndex2','UpLeg','Leg','Foot','ToeBase'))
REQUIRED_BONES = {'mixamorig:'+name for name in REQUIRED_BONES}
NAMES = {'SK_RescueCrew_Body','SK_RescueCrew_Helmet','SK_RescueCrew_Face'}


def verify(path):
    errors=[]
    def check(condition,message):
        if not condition:
            errors.append(message)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
    check({o.name for o in meshes}==NAMES,'Üç skinned mesh adı eşleşmiyor')
    check(len(rigs)==1,'Tam bir armature gerekli')
    counts={}
    positions=[]
    for obj in meshes:
        obj.data.calc_loop_triangles()
        counts[obj.name]=len(obj.data.loop_triangles)
        positions.extend(obj.matrix_world@v.co for v in obj.data.vertices)
        check(len(obj.data.uv_layers)>0,obj.name+': UV yok')
        check(len(obj.modifiers)==1 and obj.modifiers[0].type=='ARMATURE',obj.name+': modifier yapısı yanlış')
        if obj.modifiers and rigs:
            check(obj.modifiers[0].object==rigs[0],obj.name+': farklı rig')
        for vertex in obj.data.vertices:
            influences=[g for g in vertex.groups if g.weight>1e-7]
            check(1<=len(influences)<=4,f'{obj.name} vertex {vertex.index}: etki sayısı')
            check(abs(sum(g.weight for g in influences)-1)<1e-5,f'{obj.name} vertex {vertex.index}: normalize değil')
            for g in influences:
                check(obj.vertex_groups[g.group].name in REQUIRED_BONES,obj.name+': bilinmeyen ağırlık grubu')
            if obj.name.endswith(('Face','Helmet')):
                check(len(influences)==1 and obj.vertex_groups[influences[0].group].name=='mixamorig:Head',obj.name+': Head ağırlığı yanlış')
    total=sum(counts.values())
    height=max(p.z for p in positions)-min(p.z for p in positions) if positions else 0
    check(5000<=total<=10000,f'Üçgen sayısı: {total}')
    check(1.47<=height<=1.53,f'Boy: {height}')
    check(positions and abs(min(p.z for p in positions))<.001,'Taban z=0 değil')
    if rigs:
        rig=rigs[0]
        check(rig.name=='RescueCrew_Rig','Rig adı yanlış')
        actual_bones=set(rig.data.bones.keys())
        check(actual_bones==REQUIRED_BONES,'Kemik listesi değişmiş: '+', '.join(sorted(actual_bones ^ REQUIRED_BONES)))
        for side in ('Left','Right'):
            for child,parent in [('ForeArm','Arm'),('Hand','ForeArm'),('Leg','UpLeg'),('Foot','Leg'),('ToeBase','Foot')]:
                bone=rig.data.bones.get('mixamorig:'+side+child)
                check(bone and bone.parent and bone.parent.name=='mixamorig:'+side+parent,'Kemik hiyerarşisi yanlış: '+side+child)
    face=bpy.data.objects.get('SK_RescueCrew_Face')
    keys=face.data.shape_keys.key_blocks if face and face.data.shape_keys else {}
    check(set(keys.keys())==REQUIRED_SHAPES|{'Basis'},'Yüzde tam 9 shape key gerekli')
    check(all(slot.material and slot.material.name.startswith('M_') for obj in meshes for slot in obj.material_slots),
          'Materyal adları M_ ile başlamalı')
    for name in REQUIRED_SHAPES:
        if name in keys:
            check(abs(keys[name].value)<1e-6,name+': varsayılan değer sıfır değil')
            check(any((a.co-b.co).length>1e-5 for a,b in zip(keys[name].data,keys['Basis'].data)),name+': etkisiz key')
    if face:
        eye_vertices=[v for v in face.data.vertices]
        check(sum((face.matrix_world@v.co).y for v in eye_vertices)/max(1,len(eye_vertices))<0,'Yüz -Y yönünde değil')
    stats_path=path.with_name('stats.json')
    if stats_path.exists():
        stats=json.loads(stats_path.read_text(encoding='utf-8'))
        check(counts==stats['object_triangles'],'FBX üçgenleri stats.json ile farklı')
        check(abs(height-stats['height_m'])<1e-5,'FBX boyu stats.json ile farklı')
    print(json.dumps({'triangles':total,'object_triangles':counts,'height_m':round(height,6),
                      'bones':len(rigs[0].data.bones) if rigs else 0,'shape_keys':len(keys)-1},indent=2))
    if errors:
        print('VERIFY FAILED\n'+'\n'.join(errors[:30]))
        raise SystemExit(1)
    print('VERIFY OK',flush=True)


if __name__=='__main__':
    try:
        argv=sys.argv[sys.argv.index('--')+1:]
        if len(argv)!=1:
            raise ValueError('Kullanım: -- SK_RescueCrew.fbx')
        verify(Path(argv[0]).resolve())
    except Exception as error:
        print('VERIFY FAILED:',error,flush=True)
        raise SystemExit(1)
