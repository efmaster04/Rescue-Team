"""Export a flattened Unity FBX copy of the supplied scene without saving the source file.

The supplied van is exported separately as the playable vehicle, at its original
garage position. The original lift assemblies remain in the environment.
Loose equipment is omitted so gameplay instances can be picked up without duplicates.
"""
import sys
import bpy

sys.path.insert(0, r"C:\Users\uur\oyunnn\Art\Blender\Scripts")
import firma_kit as k

k.UNITY_MODELS = r"C:\Users\uur\oyunnn\Assets\_Project\Art\Models"
k.UNITY_TEXTURES = r"C:\Users\uur\oyunnn\Assets\_Project\Art\Textures"

def make_selectable():
    for obj in bpy.data.objects:
        if bpy.context.view_layer.objects.get(obj.name) is None:
            bpy.context.scene.collection.objects.link(obj)
        obj.hide_viewport = False
        obj.hide_set(False)

make_selectable()

equipment = {
    'G09 |': 'Equipment_Case',
    'G10 |': 'Equipment_PartsCrate',
    'G11 |': 'Equipment_Toolbox',
    'G12 |': 'Equipment_StorageBin',
    'G15 |': 'Equipment_FuelCan',
    'G17 |': 'Equipment_GreaseTin',
    'G18 |': 'Equipment_AbsorbentPail',
    'G23 |': 'Equipment_SpareTire',
    'T24_01': 'Equipment_FloorJack',
}
for prefix, asset_name in equipment.items():
    roots = [o for o in bpy.data.objects if o.type == 'EMPTY' and o.name.startswith(prefix)]
    if not roots:
        print('MISSING_EQUIPMENT', asset_name)
        continue
    chosen = roots[0]
    if prefix == 'T24_01':
        handle = next((o for o in bpy.data.objects if o.type == 'EMPTY' and o.name.startswith('T25_01')), None)
        if handle:
            k.set_parent(handle, chosen)
    old_matrix = chosen.matrix_world.copy()
    chosen.matrix_world.translation = (0, 0, 0)
    k.export_fbx(chosen, 'Equipment/' + asset_name)
    chosen.matrix_world = old_matrix

excluded_prefixes = ('REUSED | V01-V22', 'G09 |', 'G10 |', 'G11 |', 'G13 |', 'G14 |', 'G15 |', 'G16 |', 'G17 |', 'G18 |', 'G23 |',
                     'T24_', 'T25_')
excluded_names = [o.name for o in bpy.data.objects if o.type == 'EMPTY' and o.name.startswith(excluded_prefixes)]
for name in excluded_names:
    obj = bpy.data.objects.get(name)
    if obj:
        for child in reversed(k.descendants(obj)):
            bpy.data.objects.remove(child, do_unlink=True)
source_meshes = [obj for obj in bpy.data.objects if obj.type == 'MESH']
root = k.root('Environment_OfficeGarage')
evaluated = bpy.context.evaluated_depsgraph_get()
flat_count = 0
for original in source_meshes:
    part = original.evaluated_get(evaluated)
    mesh = bpy.data.meshes.new_from_object(part, preserve_all_data_layers=True, depsgraph=evaluated)
    mesh.transform(original.matrix_world)
    if original.matrix_world.determinant() < 0:
        mesh.flip_normals()
    mesh.materials.clear()
    for slot in original.material_slots:
        mesh.materials.append(slot.material)
    flat = bpy.data.objects.new(original.name + '_World', mesh)
    bpy.context.scene.collection.objects.link(flat)
    flat.parent = root
    flat_count += 1
print('FLATTENED_ENVIRONMENT_MESHES', flat_count)

def reveal(layer):
    layer.exclude = False
    layer.hide_viewport = False
    for child in layer.children:
        reveal(child)

reveal(bpy.context.view_layer.layer_collection)
make_selectable()

k.export_fbx(root, 'Environment/Environment_OfficeGarage')
