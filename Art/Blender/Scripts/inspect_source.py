import bpy
from collections import Counter
from mathutils import Vector

print('SCENE_OBJECTS', len(bpy.data.objects))
print('TYPES', dict(Counter(o.type for o in bpy.data.objects)))
for o in sorted(bpy.data.objects, key=lambda o: o.name):
    if o.type != 'MESH':
        continue
    name = o.name
    if name.startswith(('O', 'G', 'V', 'T', 'A11', 'A13', 'A18')) or any(s in name.lower() for s in ('desk', 'computer', 'monitor', 'workbench', 'door')):
        corners = [o.matrix_world @ Vector(c) for c in o.bound_box]
        lo = [min(c[i] for c in corners) for i in range(3)]
        hi = [max(c[i] for c in corners) for i in range(3)]
        print('OBJ', name, '|', ','.join(f'{v:.2f}' for v in o.location), '|', ','.join(f'{v:.2f}' for v in lo), '|', ','.join(f'{v:.2f}' for v in hi))
