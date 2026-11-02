import bpy,json
from pathlib import Path
root=Path(__file__).resolve().parents[3]
rows=[]
for name in ('toolbox','case','parts','bin','fuel','grease','absorbent','jack'):
    bpy.ops.wm.open_mainfile(filepath=str(root/f'Art/Blender/Models/RefinedEquipment/{name}.blend'))
    for level in range(3):
        mesh=bpy.data.objects[f'{name}_LOD{level}'].data;mesh.calc_loop_triangles()
        row={'model':name,'lod':level,'raw':len(mesh.loop_triangles),'area_zero':sum(t.area<1e-14 for t in mesh.loop_triangles),'area_tiny':sum(t.area<1e-12 for t in mesh.loop_triangles)}
        print(json.dumps(row),flush=True);rows.append(row)
(root/'Logs/blender_equipment_triangle_audit.json').write_text(json.dumps(rows,indent=2))
