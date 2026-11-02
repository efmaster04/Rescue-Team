"""Inspect the complete supplied standalone van, without changing it."""
import bpy
from mathutils import Vector

for obj in sorted(bpy.data.objects, key=lambda o: o.name):
    if obj.type != 'MESH' or not obj.name.startswith(('V01 |', 'V11 |', 'V20 |', 'V21 |')):
        continue
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = tuple(round(min(v[i] for v in corners), 2) for i in range(3))
    hi = tuple(round(max(v[i] for v in corners), 2) for i in range(3))
    print('VAN_PART', obj.name, 'faces', len(obj.data.polygons), 'bounds', lo, hi,
          'parent', obj.parent.name if obj.parent else '<none>')
    if obj.name.startswith(('V01 |', 'V20 |')):
        evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        for side in (-1, 1):
            faces = []
            for poly in mesh.polygons:
                center = obj.matrix_world @ poly.center
                normal = obj.matrix_world.to_3x3() @ poly.normal
                if center.x > -.7 and center.z > .55 and center.y * side > .75 and normal.y * side > .4:
                    faces.append(poly)
            print('SIDE', obj.name, side, len(faces), round(sum(poly.area for poly in faces), 3))
        evaluated.to_mesh_clear()
