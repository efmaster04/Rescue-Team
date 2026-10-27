"""V2 sunumunu ve kaydedilen oyun geometrisinin temizliğini doğrular."""
import importlib.util
import json
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def verify(out):
    bpy.ops.wm.open_mainfile(filepath=str(out/'SK_RescueCrew.blend'))
    scene=bpy.context.scene
    rig=bpy.data.objects['RescueCrew_Rig']
    objects=[bpy.data.objects['SK_RescueCrew_'+part] for part in ('Body','Helmet','Face')]
    stats=json.loads((out/'stats.json').read_text(encoding='utf-8'))
    for obj in objects:
        assert len(obj.modifiers)==1 and obj.modifiers[0].type=='ARMATURE', obj.name
        assert not obj.hide_render
        obj.data.calc_loop_triangles()
        assert len(obj.data.loop_triangles)==stats['object_triangles'][obj.name]
    assert not any(mod.type=='SUBSURF' for obj in scene.objects for mod in obj.modifiers)
    assert all(abs(key.value)<1e-8 for key in objects[2].data.shape_keys.key_blocks if key.name!='Basis')
    assert rig.location.length<1e-8
    assert all((bone.matrix_basis.to_translation()).length<1e-8 and
               abs(bone.matrix_basis.to_quaternion().angle)<1e-6 for bone in rig.pose.bones)
    assert scene.render.engine=='CYCLES' and scene.cycles.use_denoising
    assert scene.view_settings.view_transform=='AgX'
    for name in ('M_Helmet','M_Helmet_Yellow','M_Helmet_Blue','M_Helmet_Green'):
        assert name in bpy.data.materials
    # Kaydedilen kemikler ortak eklem kaynağındaki dönüştürülmüş konumlarla eşleşmeli.
    sys.dont_write_bytecode=True
    spec=importlib.util.spec_from_file_location('crew_builder',Path(__file__).with_name('build_rescue_crew.py'))
    builder=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    for name,(head,tail,parent) in builder.JOINTS.items():
        bone=rig.data.bones[builder.PREFIX+name]
        assert (bone.head_local-Vector(builder.character_point(head))).length<1e-6, name
        assert (bone.tail_local-Vector(builder.character_point(tail))).length<1e-6, name
    dimensions={name:(1024,1024) for name in ('front','back','side_left','three_quarter','pose_test')}
    dimensions.update(hero=(1536,1536),expressions=(2048,512),variants=(2048,1024),compare=(2048,1024))
    for name,expected in dimensions.items():
        data=(out/'renders'/(name+'.png')).read_bytes()
        assert data[:8]==b'\x89PNG\r\n\x1a\n', name
        assert struct.unpack('>II',data[16:24])==expected, name
        assert len(data)>10000, name
    print('PRESENTATION OK: 9 PNG, temiz rest pose, subdivision yok, ortak eklemler eşleşiyor',flush=True)


if __name__=='__main__':
    verify(Path(sys.argv[sys.argv.index('--')+1]).resolve())
