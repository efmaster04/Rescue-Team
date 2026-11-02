"""Repair/export the user's complete original van. Never save either source blend."""
import bpy, bmesh, json, math, sys
from pathlib import Path
from mathutils import Matrix, Vector

ROOT=Path(r'C:/Users/uur/oyunnn')
sys.path.insert(0,str(ROOT/'Art/Blender/Scripts'))
import firma_kit as kit
kit.UNITY_MODELS=str(ROOT/'Assets/_Project/Art/Models')
kit.UNITY_TEXTURES=str(ROOT/'Assets/_Project/Art/Textures')

root=kit.root('Vehicle_ServiceVan_Complete')
turn=Matrix.Rotation(math.pi/2,4,'Z')
report=[]
for source in sorted(list(bpy.data.objects),key=lambda o:o.name):
    if source.type!='MESH' or not source.name.startswith('V') or not source.name[1:3].isdigit(): continue
    evaluated=source.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh=bpy.data.meshes.new_from_object(evaluated,preserve_all_data_layers=True,depsgraph=bpy.context.evaluated_depsgraph_get())
    mesh.transform(source.matrix_world)
    bm=bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    remaining=set(bm.faces); flipped=0; open_islands=0
    while remaining:
        seed=remaining.pop(); island={seed}; queue=[seed]
        while queue:
            face=queue.pop()
            for edge in face.edges:
                for neighbor in edge.link_faces:
                    if neighbor in remaining:
                        remaining.remove(neighbor); island.add(neighbor); queue.append(neighbor)
        closed=all(len(e.link_faces)==2 for face in island for e in face.edges)
        if closed:
            # Signed volume about each island's own centre avoids cancellation
            # at wheel nuts / distant small components.
            center=sum((v.co for f in island for v in f.verts),Vector())/sum(len(f.verts) for f in island)
            volume=0
            for face in island:
                points=[v.co-center for v in face.verts]
                for i in range(1,len(points)-1): volume+=points[0].dot(points[i].cross(points[i+1]))/6
            reverse=volume<0
        else:
            open_islands+=1; alignment=0
            for face in island:
                center=face.calc_center_median(); normal=face.normal
                if source.name.startswith(('V01','V03','V20','V08')):
                    if abs(normal.y)>.5: expected=Vector((0,1 if center.y>0 else -1,0))
                    elif abs(normal.z)>.5: expected=Vector((0,0,1 if center.z>1 else -1))
                    else: expected=Vector((-1 if center.x<0 else 1,0,0))
                elif source.name.startswith('V04'): expected=Vector((-1,0,.25))
                elif source.name.startswith('V19'): expected=Vector((0,0,1))
                elif source.name.startswith('V14'):
                    axle=-1.96 if center.x<0 else 1.78
                    expected=Vector((center.x-axle,0,center.z-.42))
                    if abs(normal.y)>.5: expected=Vector((0,1 if center.y>0 else -1,0))
                else: expected=normal
                alignment+=normal.dot(expected.normalized())*face.calc_area()
            reverse=alignment<0
        if reverse:
            bmesh.ops.reverse_faces(bm,faces=list(island)); flipped+=len(island)
    bm.to_mesh(mesh); bm.free(); mesh.update()
    # Discard imported split/custom normals after correcting winding.
    for polygon in mesh.polygons: polygon.use_smooth=False
    mesh.transform(turn)
    obj=bpy.data.objects.new('Complete_'+source.name,mesh); bpy.context.scene.collection.objects.link(obj); obj.parent=root
    report.append({'part':source.name,'faces':len(mesh.polygons),'flipped_faces':flipped,'open_islands':open_islands})

# Close the narrow manufacturing returns between the side wall, shoulder and
# roof. These are real closing faces, with two millimetres of sheet thickness.
for side in (-1,1):
    points=[(-.66,side*.975,2.49),(2.66,side*.975,2.49),(2.68,side*.93,2.49),(-.71,side*.93,2.49),
            (-.72,side*.97,2.62),(-.71,side*.93,2.62),(2.68,side*.97,2.67)]
    closing=bpy.data.meshes.new('CargoShoulderReturns')
    closing.from_pydata(points,[],[(0,1,2,3),(4,5,6)])
    closing.materials.append(bpy.data.materials['01 Warm ivory paint'])
    bm=bmesh.new(); bm.from_mesh(closing)
    for face in bm.faces:
        if face.normal.z<0: face.normal_flip()
    bm.to_mesh(closing); bm.free(); closing.transform(turn)
    return_obj=bpy.data.objects.new('Complete_V01_ShoulderSeal_'+str(side),closing)
    bpy.context.scene.collection.objects.link(return_obj); return_obj.parent=root
    thickness=return_obj.modifiers.new('Sheet metal return thickness','SOLIDIFY'); thickness.thickness=.002; thickness.offset=-1

# A real seal behind the two loading leaves prevents a view through their seam.
seal=kit.box('RearDoorCentreSeal',(.024,.022,1.90),(0,2.67,1.55),kit.mat('07 Dark charcoal rubber'),bevel=0)
seal.parent=root
kit.export_fbx(root,'Vehicles/Vehicle_ServiceVan_Complete')
(ROOT/'Logs/van_complete_geometry_repair.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('COMPLETE_ORIGINAL_VAN',len(report),'parts; corrected faces',sum(x['flipped_faces'] for x in report))
