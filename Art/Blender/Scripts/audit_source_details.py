"""Read-only inspection of supplied source parts used by the playable scene."""
import bpy
from mathutils import Vector

def descendants(obj):
    for child in obj.children:
        yield child
        yield from descendants(child)

def bounds(obj):
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    return ([min(v[i] for v in corners) for i in range(3)],
            [max(v[i] for v in corners) for i in range(3)])

root = bpy.data.objects.get('REUSED | V01-V22')
for obj in sorted(descendants(root), key=lambda o: o.name):
    if not obj.name.startswith(('V01 |', 'V11 |', 'V21 |', 'V06 |')):
        continue
    print('PART', obj.name, obj.type, 'children', len(obj.children))
    for child in (obj, *descendants(obj)):
        if child.type != 'MESH':
            continue
        lo, hi = bounds(child)
        print('  MESH', child.name, 'faces', len(child.data.polygons),
              'bounds', tuple(round(v, 2) for v in lo), tuple(round(v, 2) for v in hi),
              'materials', [slot.material.name if slot.material else '' for slot in child.material_slots])
        if child.name.startswith('V11 |'):
            for mat_index, slot in enumerate(child.material_slots):
                faces = [p for p in child.data.polygons if p.material_index == mat_index]
                print('    MATERIAL', slot.material.name if slot.material else '', 'faces', len(faces))
        if child.name.startswith('V21 |'):
            for mat_index, slot in enumerate(child.material_slots):
                faces = [p for p in child.data.polygons if p.material_index == mat_index]
                if not faces:
                    continue
                centers = [child.matrix_world @ p.center for p in faces]
                lo = tuple(round(min(v[i] for v in centers), 2) for i in range(3))
                hi = tuple(round(max(v[i] for v in centers), 2) for i in range(3))
                print('    REAR_MATERIAL', slot.material.name if slot.material else '', len(faces), lo, hi)

for label, region in (
    ('A11', (-.2, .2, -3.5, -2.3, .8, 1.5)),
    ('A13', (-3.8, -2.5, -5.35, -5.0, .8, 1.5)),
):
    x0, x1, y0, y1, z0, z1 = region
    print('REGION', label)
    for obj in sorted(bpy.data.objects, key=lambda o: o.name):
        if obj.type != 'MESH':
            continue
        lo, hi = bounds(obj)
        if lo[0] > x1 or hi[0] < x0 or lo[1] > y1 or hi[1] < y0 or lo[2] > z1 or hi[2] < z0:
            continue
        ancestry = []
        parent = obj.parent
        while parent:
            ancestry.append(parent.name)
            parent = parent.parent
        print('  NEAR', obj.name, 'parent', ' > '.join(ancestry[:3]),
              'bounds', tuple(round(v, 2) for v in lo), tuple(round(v, 2) for v in hi))

colliders = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('COL_')]
print('SOURCE_COLLISION_MESHES', len(colliders))
for obj in colliders[:30]:
    lo, hi = bounds(obj)
    print('  SOURCE_COLLIDER', obj.name, tuple(round(v, 2) for v in lo), tuple(round(v, 2) for v in hi))

for obj in sorted(bpy.data.objects, key=lambda o: o.name):
    if obj.type != 'EMPTY':
        continue
    name = obj.name.lower()
    if any(term in name for term in ('tezgah', 'workbench', 'shelf', 'raf', 'cabinet', 'dolap', 'lift', 'masa')):
        print('SCENE_GROUP', obj.name, 'parent', obj.parent.name if obj.parent else '<none>')
    if obj.name.startswith(('T01_01_', 'T02_01_', 'G02 |', 'O20 |', 'O22 |', 'G01 |')):
        meshes = [child for child in descendants(obj) if child.type == 'MESH']
        if meshes:
            lows, highs = zip(*(bounds(child) for child in meshes))
            lo = tuple(round(min(v[i] for v in lows), 2) for i in range(3))
            hi = tuple(round(max(v[i] for v in highs), 2) for i in range(3))
            print('GROUP_BOUNDS', obj.name, lo, hi, len(meshes))
