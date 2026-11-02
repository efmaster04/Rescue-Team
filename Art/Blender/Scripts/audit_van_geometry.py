import bpy, json
from pathlib import Path
from mathutils import Vector

report=[]
for obj in sorted(bpy.data.objects,key=lambda o:o.name):
    if obj.type!='MESH' or not obj.name.startswith('V'): continue
    evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh=evaluated.to_mesh(); points=[obj.matrix_world@v.co for v in mesh.vertices]
    lo=[min(p[i] for p in points) for i in range(3)]; hi=[max(p[i] for p in points) for i in range(3)]
    normal_counts=[0]*6
    for poly in mesh.polygons:
        n=obj.matrix_world.to_3x3().inverted().transposed()@poly.normal
        axis=max(range(3),key=lambda i:abs(n[i]))
        normal_counts[axis*2+(1 if n[axis]>0 else 0)]+=1
    report.append({'name':obj.name,'lo':lo,'hi':hi,'matrix':[list(row) for row in obj.matrix_world],
        'faces':len(mesh.polygons),'normal_counts':normal_counts})
    evaluated.to_mesh_clear()
out=Path(r'C:/Users/uur/oyunnn/Logs')/('van_geometry_'+('standalone' if 'Lake_Workshop_Van' in bpy.data.filepath else 'scene')+'.json')
out.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('AUDIT',out,len(report))
