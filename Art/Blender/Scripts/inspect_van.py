import bpy
from mathutils import Vector

for obj in sorted(bpy.data.objects, key=lambda item: item.name):
    if not obj.name.startswith("V") or not obj.name[1:3].isdigit():
        continue
    if obj.type not in {"EMPTY", "MESH"}:
        continue
    center = obj.matrix_world.translation
    parent = obj.parent.name if obj.parent else "<root>"
    bounds = ""
    if obj.type == "MESH":
        corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
        minimum = [min(p[i] for p in corners) for i in range(3)]
        maximum = [max(p[i] for p in corners) for i in range(3)]
        bounds = "bbox=" + str(tuple(round(v, 2) for v in minimum)) + ".." + str(tuple(round(v, 2) for v in maximum))
    print("VAN", obj.type, obj.name, "parent=", parent,
          "world=", tuple(round(v, 3) for v in center), "children=", len(obj.children), bounds)
