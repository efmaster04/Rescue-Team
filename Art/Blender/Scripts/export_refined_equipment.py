"""Re-export the saved, editable Blender LOD meshes without rebuilding/rerendering."""
import bpy
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
for name in ('toolbox','case','parts','bin','fuel','grease','absorbent','jack'):
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/f'Art/Blender/Models/RefinedEquipment/{name}.blend'))
    bpy.ops.object.select_all(action='DESELECT')
    for level in range(3):
        obj=bpy.data.objects[f'{name}_LOD{level}'];obj.hide_set(False);obj.hide_viewport=False;obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.fbx(filepath=str(ROOT/f'Assets/_Project/Art/BlenderEquipment/Models/{name}.fbx'),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',use_space_transform=True,bake_space_transform=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',use_triangles=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
    print('EXPORTED '+name,flush=True)
