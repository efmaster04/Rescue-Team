"""Compare the two sides of the supplied van without editing its .blend file."""
import bpy
from mathutils import Vector

root = bpy.data.objects["REUSED | V01-V22"]
depsgraph = bpy.context.evaluated_depsgraph_get()
for obj in root.children_recursive:
    if obj.type != "MESH":
        continue
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    counts = [0, 0]
    areas = [0.0, 0.0]
    for polygon in mesh.polygons:
        center = obj.matrix_world @ polygon.center
        normal = obj.matrix_world.to_3x3() @ polygon.normal
        if -.5 < center.y < 2.5 and .5 < center.z < 2.8:
            if center.x < 3.85 and normal.x < -.45:
                counts[0] += 1
                areas[0] += polygon.area
            if center.x > 5.45 and normal.x > .45:
                counts[1] += 1
                areas[1] += polygon.area
    if sum(counts):
        print("SIDE", obj.name, counts, [round(x, 3) for x in areas])
    if obj.name.startswith(("V01 |", "V20 |")):
        print("MATERIALS", obj.name, [slot.material.name if slot.material else None for slot in obj.material_slots])
        for side in (-1,1):
            chosen=[]
            for polygon in mesh.polygons:
                center=obj.matrix_world @ polygon.center
                normal=obj.matrix_world.to_3x3() @ polygon.normal
                if center.y > -.7 and .55 < center.z < 2.75 and \
                   (center.x-4.65)*side > .75 and normal.x*side > .4:
                    chosen.append(polygon)
            print("CARGO_SIDE",side,len(chosen),round(sum(p.area for p in chosen),3),
                  sorted(set(p.material_index for p in chosen)))
        if obj.name.startswith("V01 |"):
            for limit in (.15,.3,.5):
                selection=[]
                for polygon in mesh.polygons:
                    center=obj.matrix_world @ polygon.center
                    normal=obj.matrix_world.to_3x3() @ polygon.normal
                    if center.x>5.4 and center.y>-.7 and center.z>.55 and normal.x>.4 \
                       and normal.z<limit:
                        selection.append(polygon)
                print("ROOF_FILTER",limit,len(selection),round(sum(p.area for p in selection),3))
    evaluated.to_mesh_clear()
