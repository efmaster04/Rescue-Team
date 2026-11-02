"""Read-only bounds of substantial source scene groups for gameplay collision."""
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

for root in sorted((o for o in bpy.data.objects if o.parent is None and o.type == 'EMPTY'),
                   key=lambda o: o.name):
    meshes = [o for o in descendants(root) if o.type == 'MESH']
    if not meshes:
        continue
    lows, highs = zip(*(bounds(o) for o in meshes))
    lo = [min(v[i] for v in lows) for i in range(3)]
    hi = [max(v[i] for v in highs) for i in range(3)]
    if root.name.startswith('A'):
        print('ARCHITECTURE', root.name,
              tuple(round(v, 2) for v in lo), tuple(round(v, 2) for v in hi), len(meshes))
    if hi[0]-lo[0] < .3 or hi[1]-lo[1] < .3 or hi[2] < .5 or lo[2] > .3:
        continue
    if lo[0] < -6 or hi[0] > 10 or lo[1] < -5.5 or hi[1] > 5.5:
        continue
    print('CANDIDATE', root.name,
          tuple(round(v, 2) for v in lo), tuple(round(v, 2) for v in hi), len(meshes))
