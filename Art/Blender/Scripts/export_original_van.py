"""Extract the supplied V01–V22 van without changing the source .blend file.

The van's Blender world centre is (4.65, -0.18, 0). Re-centering that hierarchy
lets Unity place the original art at the same garage coordinates as the source.
"""
import sys
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, r"C:\Users\uur\oyunnn\Art\Blender\Scripts")
import firma_kit as kit

kit.UNITY_MODELS = r"C:\Users\uur\oyunnn\Assets\_Project\Art\Models"
kit.UNITY_TEXTURES = r"C:\Users\uur\oyunnn\Assets\_Project\Art\Textures"

original = bpy.data.objects.get("REUSED | V01-V22")
if original is None:
    raise RuntimeError("Original V01–V22 van root was not found")

root = kit.root("Vehicle_ServiceVan_Original")
origin = Vector((4.65, -0.18, 0))
offset = Matrix.Translation(-origin)
evaluated = bpy.context.evaluated_depsgraph_get()
minimum = Vector((1e9, 1e9, 1e9))
maximum = Vector((-1e9, -1e9, -1e9))
copied = 0
for original_part in kit.descendants(original):
    if original_part.type != "MESH":
        continue
    part = original_part.evaluated_get(evaluated)
    mesh = bpy.data.meshes.new_from_object(part, preserve_all_data_layers=True, depsgraph=evaluated)
    mesh.transform(offset @ original_part.matrix_world)
    if (offset @ original_part.matrix_world).determinant() < 0:
        mesh.flip_normals()
    mesh.materials.clear()
    for slot in original_part.material_slots:
        mesh.materials.append(slot.material)
    flat = bpy.data.objects.new("Original_" + original_part.name, mesh)
    bpy.context.scene.collection.objects.link(flat)
    flat.parent = root
    copied += 1
    for vertex in mesh.vertices:
        minimum = Vector(tuple(min(minimum[i], vertex.co[i]) for i in range(3)))
        maximum = Vector(tuple(max(maximum[i], vertex.co[i]) for i in range(3)))

print("ORIGINAL_VAN_PARTS", copied,
      "LOCAL_BOUNDS", tuple(round(v, 3) for v in minimum),
      tuple(round(v, 3) for v in maximum))

# The supplied vehicle is a presentation cutaway on its office-facing cargo side.
# Rebuild only that missing exterior skin from the intact opposite side, preserving
# the original .blend and every existing vehicle part.
for prefix in ("V01 | Ana araç gövdesi", "V20 | Yan mavi boyalı şerit"):
    source = next(o for o in kit.descendants(original)
                  if o.type == "MESH" and o.name.startswith(prefix))
    evaluated_part = source.evaluated_get(evaluated)
    source_mesh = evaluated_part.to_mesh()
    vertices, faces, material_ids, smooth = [], [], [], []
    for polygon in source_mesh.polygons:
        world_center = source.matrix_world @ polygon.center
        world_normal = source.matrix_world.to_3x3() @ polygon.normal
        if not (world_center.x > 5.40 and world_center.y > -.70 and
                world_center.z > .55 and world_normal.x > .40):
            continue
        if prefix.startswith("V01 |") and abs(world_normal.z) >= .15:
            continue
        face = []
        for index in reversed(polygon.vertices):
            world = source.matrix_world @ source_mesh.vertices[index].co
            face.append(len(vertices))
            vertices.append((origin.x - world.x - .003, world.y - origin.y, world.z))
        faces.append(face)
        material_ids.append(polygon.material_index)
        smooth.append(polygon.use_smooth)
    rebuilt = bpy.data.meshes.new("RestoredCargoSide_" + source.name)
    rebuilt.from_pydata(vertices, [], faces)
    for slot in source.material_slots:
        rebuilt.materials.append(slot.material)
    for polygon, material_id, use_smooth in zip(rebuilt.polygons, material_ids, smooth):
        polygon.material_index = material_id
        polygon.use_smooth = use_smooth
    restored = bpy.data.objects.new(rebuilt.name, rebuilt)
    bpy.context.scene.collection.objects.link(restored)
    restored.parent = root
    evaluated_part.to_mesh_clear()
    print("RESTORED_CARGO_SIDE", prefix, len(faces))

kit.export_fbx(root, "Vehicles/Vehicle_ServiceVan_Original")
