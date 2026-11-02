import bpy
from collections import Counter

print('ROOTS')
for o in bpy.data.objects:
    if o.parent is None and o.type == 'EMPTY':
        print(o.name, 'at', tuple(round(v,2) for v in o.location), 'children', len(o.children))
print('COLLECTIONS')
for c in bpy.data.collections:
    if len(c.objects) > 5:
        print(c.name, len(c.objects))
print('KEY ROOTS')
for o in bpy.data.objects:
    if o.type == 'EMPTY' and (o.name.startswith(('V','O01','O02','O03','T','A11','A13','G09','G11'))):
        print(o.name, 'at', tuple(round(v,2) for v in o.matrix_world.translation), 'children', len(o.children))
print('OFFICE ROOTS')
for o in bpy.data.objects:
    if o.parent is None and o.type == 'EMPTY' and o.name.startswith('O'):
        print(o.name, 'at', tuple(round(v,2) for v in o.matrix_world.translation))
