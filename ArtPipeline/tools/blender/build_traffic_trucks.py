"""Deterministik trafik ticari ara?lar?; Blender 5.2, metre, Z yukar?, burun -Y.
Kullan?m: blender -b --factory-startup --python-exit-code 1 --python tools/blender/build_traffic_trucks.py
Geli?tirme: -- --round 1 --no-render. Varsay?lan tur 2.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import struct
import sys
import tempfile
import time
import traceback
import zlib
sys.dont_write_bytecode = True
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'Export/Traffic'
RENDERS = OUT/'renders/trucks'
CONCEPT = ROOT/'ArtSource/Traffic/references/01_traffic_lineup.jpg'
MINIVAN_FBX = ROOT/'Export/Minivan/SM_Minivan.fbx'
P = 'SM_PanelVan'
MATERIALS, PALETTE, PARTS, WORLD_PIVOT = {}, {}, {}, {}
ASSETS = []
ROUND = 2
class Geo:
    def __init__(self, name):
        self.name, self.vertices, self.faces, self.materials = name, [], [], []
        self.tags = []
        self.tag = 'shell'

    def patch(self, vertices, faces, material='Paint'):
        offset = len(self.vertices)
        self.vertices.extend(tuple(v) for v in vertices)
        self.tags.extend([self.tag]*len(vertices))
        self.faces.extend(tuple(offset+i for i in f) for f in faces)
        self.materials.extend([material]*len(faces))

    def box(self, center, size, material='Paint', bevel=.025, rotation=None):
        # İnce parçalarda bevel yok: low poly bütçesi korunur.
        if min(size) < .08:
            bevel = 0
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1)
        for v in bm.verts:
            v.co = Vector(tuple(v.co[i]*size[i] for i in range(3)))
        if bevel:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(size)*.45),
                            segments=1, affect='EDGES', profile=.5)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.verts.ensure_lookup_table()
        bm.verts.index_update()
        rot = rotation or Matrix.Identity(3)
        self.patch([rot@v.co+Vector(center) for v in bm.verts],
                   [[v.index for v in f.verts] for f in bm.faces], material)
        bm.free()

    def span(self, lo, hi, material='Paint', bevel=.025):
        lo, hi = Vector(map(min, lo, hi)), Vector(map(max, lo, hi))
        self.box((lo+hi)/2, tuple(hi-lo), material, bevel)

    def beam(self, a, b, width, depth=None, material='Paint', bevel=0):
        delta = Vector(b)-Vector(a)
        self.box((Vector(a)+Vector(b))/2, (width, depth or width, delta.length), material,
                 bevel, rotation=delta.to_track_quat('Z', 'Y').to_matrix())

    def prism(self, polygon, offset, material='Paint'):
        n = len(polygon)
        self.patch([Vector(p) for p in polygon]+[Vector(p)+Vector(offset) for p in polygon],
                   [tuple(reversed(range(n))), tuple(range(n, 2*n))] +
                   [(i, (i+1) % n, (i+1) % n+n, i+n) for i in range(n)], material)

    def cylinder(self, center, radius, depth, material, axis=(0, 0, 1), segments=16, radius_top=None):
        rot = Vector(axis).to_track_quat('Z', 'Y').to_matrix()
        radii = (radius, radius if radius_top is None else radius_top)
        vertices = [rot@Vector((r*math.cos(2*math.pi*i/segments), r*math.sin(2*math.pi*i/segments), z))+Vector(center)
                    for z, r in zip((-depth/2, depth/2), radii) for i in range(segments)]
        self.patch(vertices, [tuple(reversed(range(segments))), tuple(range(segments, 2*segments))] +
                   [(i, (i+1) % segments, (i+1) % segments+segments, i+segments) for i in range(segments)], material)

    def finish(self, pivot=(0, 0, 0), parent=None, motion=None):
        # 0.1 mm ızgara: FBX gidiş-dönüşünde float gürültüsü kanonik özeti bozmaz.
        parent = parent or self.name.split("_Body")[0].split("_Glass")[0].split("_Interior")[0].split("_Wheel")[0]
        pivot = Vector(tuple(round(c, 4) for c in pivot))
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata([tuple(round(c, 4) for c in Vector(v)-pivot) for v in self.vertices], [], self.faces)
        used = [n for n in MATERIALS if n in self.materials]
        for name in used:
            mesh.materials.append(MATERIALS[name])
        for poly, name in zip(mesh.polygons, self.materials):
            poly.material_index = used.index(name)
        # Düz normal + gerçek üçgenler; export topolojiyi değiştirmez.
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='FIXED', ngon_method='EAR_CLIP')
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        obj = bpy.data.objects.new(self.name, mesh)
        attach(obj, pivot, parent)
        for tag in sorted(set(self.tags)):
            group = obj.vertex_groups.new(name=tag)
            group.add([i for i, t in enumerate(self.tags) if t == tag], 1, 'REPLACE')
        PARTS[self.name] = {'pivot_m': [round(c, 6) for c in pivot], 'parent': parent, 'motion': motion}
        return obj


def attach(obj, pivot, parent):
    # Dinlenme pozunda hiçbir objede dönme yok: yerel konum = dünya pivotu - ebeveyn pivotu.
    bpy.context.collection.objects.link(obj)
    WORLD_PIVOT[obj.name] = Vector(pivot)
    if parent:
        obj.parent = bpy.data.objects[parent]
        obj.location = Vector(pivot)-WORLD_PIVOT[parent]
    else:
        obj.location = pivot
    ASSETS.append(obj)


def empty(name, position, parent):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_size = .12
    attach(obj, Vector(position), parent)
    return obj


def hinge(axis, lo, hi, work, note=None):
    m = {'type': 'hinge', 'axis': list(axis), 'range_deg': [lo, hi], 'work_pose_deg': work}
    if note:
        m['note'] = note
    return m


def geometry_digest():
    signature = []
    for obj in sorted(ASSETS, key=lambda o: o.name):
        row = {'name': obj.name, 'parent': obj.parent.name if obj.parent else None,
               'pivot': [round(float(c), 7) for c in obj.location]}
        if obj.type == 'MESH':
            row.update(vertices=[[round(float(c), 7) for c in v.co] for v in obj.data.vertices],
                       faces=[list(p.vertices) for p in obj.data.polygons],
                       materials=[obj.data.materials[p.material_index].name for p in obj.data.polygons])
        signature.append(row)
    return hashlib.sha256(json.dumps(signature, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def canonical_geometry():
    data = []
    for obj in sorted((o for o in ASSETS if o.type == 'MESH'), key=lambda o: o.name):
        verts = [obj.matrix_world@v.co for v in obj.data.vertices]
        obj.data.calc_loop_triangles()
        triangles = []
        for tri in obj.data.loop_triangles:
            corners = sorted(tuple(math.floor(float(v)*10000+.371) for v in verts[i]) for i in tri.vertices)
            triangles.append([corners, obj.data.materials[tri.material_index].name])
        data.append([obj.name, sorted(triangles)])
    return hashlib.sha256(json.dumps(data, separators=(',', ':')).encode()).hexdigest()


def setup_render():
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.cycles.denoising_use_gpu = False
    # Tile dosyası yok: düşük sistem belleğinde EXR tile okuma hatası olmasın.
    scene.cycles.use_auto_tile = False
    scene.cycles.tile_size = 256
    scene.cycles.seed = 0
    scene.cycles.use_animated_seed = False
    scene.cycles.max_bounces = 8
    scene.cycles.transmission_bounces = 6
    scene.cycles.transparent_max_bounces = 12
    scene.cycles.adaptive_threshold = .025
    # Paylaşılan GPU dolu olsa bile arayüzsüz kabul komutu güvenilir çalışır.
    scene.cycles.device = 'CPU'
    scene['render_device'] = 'CPU (predictable memory use)'
    print('RENDER DEVICE', scene['render_device'], flush=True)
    scene.render.threads_mode = 'FIXED'
    scene.render.threads = 6
    scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium High Contrast'
    scene.view_settings.exposure = 0
    world = bpy.data.worlds.new('Studio_World')
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (.82, .82, .82, 1)
    world.node_tree.nodes['Background'].inputs[1].default_value = .85
    nodes, links = world.node_tree.nodes, world.node_tree.links
    camera_background = nodes.new('ShaderNodeBackground')
    camera_background.inputs[0].default_value = (2.3, 2.3, 2.3, 1)
    camera_background.inputs[1].default_value = 1
    light_path = nodes.new('ShaderNodeLightPath')
    mix = nodes.new('ShaderNodeMixShader')
    links.new(light_path.outputs['Is Camera Ray'], mix.inputs[0])
    links.new(nodes['Background'].outputs[0], mix.inputs[1])
    links.new(camera_background.outputs[0], mix.inputs[2])
    links.new(mix.outputs[0], nodes['World Output'].inputs['Surface'])
    bpy.ops.mesh.primitive_plane_add(size=300, location=(0, 0, -.012))
    floor = bpy.context.object
    floor.name = 'RENDER_StudioFloor'
    mat = bpy.data.materials.new('RENDER_Studio')
    mat.diffuse_color = (.79, .81, .83, 1)
    floor.data.materials.append(mat)
    for name, position, power, size in [('Key', (7, -9, 12), 1700, 8), ('Fill', (-8, -4, 7), 1000, 7), ('Rim', (2, 10, 10), 1800, 6)]:
        data = bpy.data.lights.new('RENDER_'+name, 'AREA')
        obj = bpy.data.objects.new(data.name, data)
        bpy.context.collection.objects.link(obj)
        obj.location = position
        obj.rotation_euler = (Vector((0, 0, 1.5))-obj.location).to_track_quat('-Z', 'Y').to_euler()
        data.energy, data.shape, data.size = power, 'DISK', size
    data = bpy.data.cameras.new('RENDER_Camera')
    camera = bpy.data.objects.new(data.name, data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera
    data.type = 'ORTHO'
    data.clip_end = 200
    return camera


def write_png(path, pixels):
    h, w = pixels.shape[:2]
    def chunk(kind, data):
        return struct.pack('!I', len(data))+kind+data+struct.pack('!I', zlib.crc32(kind+data) & 0xffffffff)
    raw = b''.join(b'\0'+pixels[y].tobytes() for y in range(h))
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR', struct.pack('!2I5B', w, h, 8, 2, 0, 0, 0)) +
                     chunk(b'IDAT', zlib.compress(raw, 6))+chunk(b'IEND', b''))


def image_array(path):
    im = bpy.data.images.load(str(path), check_existing=False)
    pixels = np.empty(im.size[0]*im.size[1]*4, dtype=np.float32)
    im.pixels.foreach_get(pixels)
    a = pixels.reshape(im.size[1], im.size[0], 4)[::-1, :, :3]
    bpy.data.images.remove(im)
    return np.clip(a*255+.5, 0, 255).astype(np.uint8)


def autocrop(a, margin=30):
    # Render içindeki aracı kırpar: köşe arka plan renginden belirgin farklı pikseller.
    mask = np.abs(a.astype(np.int16)-a[2, 2].astype(np.int16)).max(axis=2) > 40
    ys, xs = np.nonzero(mask)
    if not len(ys):
        return a
    return a[max(0, ys.min()-margin):ys.max()+margin, max(0, xs.min()-margin):xs.max()+margin]


def side_by_side(left, right, path):
    canvas = np.full((1080, 1920, 3), 241, dtype=np.uint8)
    for index, a in enumerate((left, autocrop(right))):
        scale = min(930/a.shape[1], 990/a.shape[0])
        w, h = round(a.shape[1]*scale), round(a.shape[0]*scale)
        resized = a[(np.arange(h)/scale).astype(int)[:, None], (np.arange(w)/scale).astype(int)[None, :]]
        x, y = index*960+(960-w)//2, (1080-h)//2
        canvas[y:y+h, x:x+w] = resized
    canvas[:, 957:963] = (130, 147, 155)
    write_png(path, canvas)


def render(folder, name, position, target, scale, durations):
    camera = bpy.context.scene.camera
    camera.location = position
    camera.rotation_euler = (Vector(target)-camera.location).to_track_quat('-Z', 'Y').to_euler()
    if name == 'interior_top':
        camera.rotation_euler = (0, 0, math.pi/2)
    camera.data.ortho_scale = scale
    bpy.context.scene.render.filepath = str(folder/(name+'.png'))
    started = time.perf_counter()
    # Bellek darlığında Cycles bozuk (karanlık) kare yazabilir: açık stüdyo
    # arka planı köşede okunmuyorsa render en fazla 3 kez tekrarlanır.
    # Tekrarlarda bellek isteyen OIDN kapatılır, gürültüyü örnek sayısı karşılar.
    scene = bpy.context.scene
    for attempt in range(3):
        scene.cycles.use_denoising = attempt == 0
        scene.cycles.samples = 32 if attempt == 0 else 192
        bpy.ops.render.render(write_still=True)
        if image_array(folder/(name+'.png'))[2, 2].mean() > 150:
            break
        print('RENDER RETRY', name, attempt+1, flush=True)
    else:
        raise RuntimeError('Render failed (dark frame): '+name)
    durations[name] = round(time.perf_counter()-started, 3)
    print('RENDER OK', name, durations[name], flush=True)



def srgb_to_linear(v):
    return v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4


def clip_band(poly, z0, z1):
    # Sutherland-Hodgman: (y, z) poligonunu z0 <= z <= z1 bandına kırpar.
    def clip(points, keep, z):
        out = []
        for i, a in enumerate(points):
            b = points[(i+1) % len(points)]
            if keep(a):
                out.append(a)
            if keep(a) != keep(b):
                out.append((a[0]+(b[0]-a[0])*(z-a[1])/(b[1]-a[1]), z))
        return out
    points = clip(list(poly), lambda p: p[1] >= z0, z0)
    points = clip(points, lambda p: p[1] <= z1, z1) if points else []
    result = []
    for p in points:
        if not result or abs(p[0]-result[-1][0])+abs(p[1]-result[-1][1]) > 1e-6:
            result.append(p)
    if len(result) > 1 and abs(result[0][0]-result[-1][0])+abs(result[0][1]-result[-1][1]) < 1e-6:
        result.pop()
    # Kırpma çizgisi üzerinde kalan doğrusal ara noktalar sıfır alanlı üçgen üretir; onları at.
    changed = True
    while changed and len(result) >= 3:
        changed = False
        for i in range(len(result)):
            a, b, c = result[i-1], result[i], result[(i+1) % len(result)]
            if abs((b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])) < 1e-9:
                result.pop(i)
                changed = True
                break
    return result if len(result) >= 3 else []


def sweep(geo, path, chamfers, materials, half=.985, thickness=.05):
    """Yan siluet yolu (y, z) boyunca pahlı kabuk (kaput, tavan şapkası, tavan).
    Her halka: iki yanda pah + üst yüz; içe doğru kalınlık verilerek kapalı katı olur.
    Dönüş: pahın dış kenarı (yan levhaların üst sınırı)."""
    n = len(path)
    edge, verts = [], []
    for k, (y, z) in enumerate(path):
        t = (Vector(path[min(k+1, n-1)])-Vector(path[max(k-1, 0)])).normalized()
        inward = Vector((t[1], -t[0]))
        c = chamfers[k]
        o = Vector((y, z))+inward*c
        b = Vector((y, z))+inward*(c+thickness)
        edge.append((round(o[0], 5), round(o[1], 5)))
        inner = half-.02
        # Yan iç yüzler yan levha hacminin içinde kalır: eş düzlemli kararma olmaz.
        verts += [(-half, *o), (-half+c, y, z), (half-c, y, z), (half, *o),
                  (inner, *o), (inner, *b), (-inner, *b), (-inner, *o)]
    # Tek bağlı kabuk (normaller tutarlı hesaplanır); segment başına materyal.
    base = len(geo.vertices)
    geo.vertices.extend(verts)
    geo.tags.extend([geo.tag]*len(verts))
    for k in range(n-1):
        for j in range(8):
            geo.faces.append(tuple(base+i for i in (k*8+j, k*8+(j+1) % 8, (k+1)*8+(j+1) % 8, (k+1)*8+j)))
            geo.materials.append(materials[k])
    geo.faces += [tuple(base+i for i in range(8)), tuple(base+(n-1)*8+i for i in reversed(range(8)))]
    geo.materials += [materials[0], materials[-1]]
    return edge


def export_asset(root, path):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in ASSETS:
        if obj==root or obj.parent==root: obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'EMPTY','MESH'},
        apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',mesh_smooth_type='FACE',
        use_mesh_modifiers=False,bake_anim=False,use_custom_props=True,check_existing=False,add_leaf_bones=False)


SPECS = {
    'PanelVan': {'dimensions': [5.4, 2.0, 2.4], 'radius': .43, 'wheel_x': .84,
                 'axles': {'F': -1.70, 'R': 1.70}, 'budget': [3500, 5500]},
    'BoxTruck': {'dimensions': [9.5, 2.5, 3.6], 'radius': .57, 'wheel_x': 1.025,
                 'axles': {'F': -3.35, 'R1': 2.05, 'R2': 3.35}, 'budget': [5000, 8000]},
}


def make_materials():
    # Ana referansın alt sırasındaki boyalar; diğer yüzeyler de aynı görselden.
    locations = {'VanPaint': (987, 591), 'TruckPaint': (1175, 574),
                 'Cargo': (1126, 439), 'Trim': (1276, 638), 'Tire': (1270, 649),
                 'Rim': (1152, 650), 'Glass': (1186, 505), 'Seat': (1230, 524),
                 'Headlight': (1225, 602), 'Amber': (1227, 623)}
    a = image_array(CONCEPT)
    samples = {}
    for name, (x, y) in locations.items():
        rgb = np.median(a[y-2:y+3, x-2:x+3], axis=(0, 1))
        PALETTE[name] = ''.join(f'{round(float(c)):02X}' for c in rgb)
        samples[name] = {'hex': '#'+PALETTE[name], 'source_pixel_xy_top_left': [x, y], 'median_patch_px': 5}
    for name, color in {'Chassis': '343940', 'Interior': '66656A', 'Driver': '39494A',
                        'Taillight': '783F40', 'Seal': '353A40'}.items():
        PALETTE[name] = color
        samples[name] = {'hex': '#'+color, 'source': 'neutral assumption; not visible as a flat sample in reference'}
    for name, color in PALETTE.items():
        mat = bpy.data.materials.new('M_TrafficTrucks_'+name)
        mat.use_nodes = True
        rgb = tuple(srgb_to_linear(int(color[i:i+2], 16)/255) for i in (0, 2, 4))
        mat.diffuse_color = (*rgb, 1)
        bsdf = mat.node_tree.nodes['Principled BSDF']
        bsdf.inputs['Base Color'].default_value = (*rgb, 1)
        bsdf.inputs['Roughness'].default_value = .65
        bsdf.inputs['Specular IOR Level'].default_value = .25
        if name == 'Glass':
            bsdf.inputs['Transmission Weight'].default_value = .85
            bsdf.inputs['Roughness'].default_value = .14
            bsdf.inputs['Specular IOR Level'].default_value = .10
            bsdf.inputs['IOR'].default_value = 1.06
            bsdf.inputs['Alpha'].default_value = .30
            mat.diffuse_color = (*rgb, .30)
        MATERIALS[name] = mat
    return samples


def side(geo, sign, poly, mat, x, depth=.045):
    geo.prism([(sign*x, y, z) for y, z in poly], (-sign*depth, 0, 0), mat)


def rect(geo, sign, y0, y1, z0, z1, mat, x, depth=.045):
    side(geo, sign, [(y0, z0), (y1, z0), (y1, z1), (y0, z1)], mat, x, depth)


def arch(y, radius, wheel_r, floor, steps=12):
    t = math.asin((floor-wheel_r)/radius)
    return [(y-radius*math.cos(t+(math.pi-2*t)*i/steps),
             wheel_r+radius*math.sin(t+(math.pi-2*t)*i/steps)) for i in range(steps+1)]


def lower_sides(body, sign, start, end, floor, top, axles, radius, mat, x):
    # Yan panelin alt sınırı gerçek kemer yayıdır; tekerlek önünde dolu levha yok.
    cursor = start
    for y in axles:
        a = arch(y, radius+.065, radius, floor)
        rect(body, sign, cursor, a[0][0], floor, top, mat, x)
        side(body, sign, a+[(a[-1][0], top), (a[0][0], top)], mat, x)
        outer = arch(y, radius+.105, radius, floor)
        for i in range(len(a)-1):
            side(body, sign, [a[i], a[i+1], outer[i+1], outer[i]], mat, x+.015, .065)
        cursor = a[-1][0]
    rect(body, sign, cursor, end, floor, top, mat, x)


def windows(body, glass, sign, poly, x):
    side(glass, sign, poly, 'Glass', x-.012, .008)
    for a, b in zip(poly, poly[1:]+poly[:1]):
        body.beam((sign*(x+.003), *a), (sign*(x+.003), *b), .030, material='Seal')


def front_glass(body, glass, half, y0, z0, y1, z1):
    poly = [(-half, y0, z0), (half, y0, z0), (half, y1, z1), (-half, y1, z1)]
    glass.prism(poly, (0, .008, 0), 'Glass')
    for a, b in zip(poly, poly[1:]+poly[:1]):
        body.beam(a, b, .035, material='Seal')


def mirrors(body, glass, x, y, z):
    body.tag = 'mirrors_excluded_from_dimensions'
    glass.tag = 'mirrors_excluded_from_dimensions'
    for sign in (-1, 1):
        body.beam((sign*x, y+.10, z-.10), (sign*(x+.18), y, z), .035, material='Trim')
        body.box((sign*(x+.22), y, z+.08), (.12, .14, .29), 'Trim', .027)
        glass.box((sign*(x+.22), y+.076, z+.08), (.080, .009, .23), 'Glass', 0)
    body.tag = glass.tag = 'shell'


def wheels(kind):
    spec = SPECS[kind]
    radius, x = spec['radius'], spec['wheel_x']
    width = .30 if kind == 'PanelVan' else .40
    n = 16 if kind == 'PanelVan' else 20
    # Halkalar yanak, omuz ve lastik sırtını taşır; radyal normal düz/fasetli.
    profile = [(-.50, .53), (-.51, .65), (-.44, .80), (-.30, .96),
               (-.20, 1), (.20, 1), (.30, .96), (.44, .80), (.51, .65), (.50, .53)]
    for axle, y in spec['axles'].items():
        for sign, label in ((1, 'L'), (-1, 'R')):
            suffix = axle+label if kind == 'PanelVan' else axle+'_'+label
            geo = Geo('SM_'+kind+'_Wheel_'+suffix)
            center = Vector((sign*x, y, radius))
            rings = [[center+Vector((dx*width, math.sin(2*math.pi*i/n)*rr*radius,
                                    math.cos(2*math.pi*i/n)*rr*radius)) for i in range(n)] for dx, rr in profile]
            for j in range(len(rings)-1):
                geo.patch(rings[j]+rings[j+1], [(i, (i+1)%n, (i+1)%n+n, i+n) for i in range(n)], 'Tire')
            for s in (-1, 1):
                geo.cylinder(center+Vector((s*width*.505, 0, 0)), radius*.53, .012, 'Rim', (1, 0, 0), n)
                geo.cylinder(center+Vector((s*(width*.505+.009), 0, 0)), radius*.30, .018, 'Trim', (1, 0, 0), 12)
            geo.finish(center, motion={'type': 'continuous_rotation', 'axis': [1, 0, 0], 'radius_m': radius})


def steering(geo, center, radius):
    # Direksiyon halkası ve üç sade kol; rig veya hareketli mekanizma yok.
    center = Vector(center)
    rot = Vector((0, -.6, .8)).to_track_quat('Z', 'Y').to_matrix()
    verts = []
    n, m = 12, 6
    for i in range(n):
        a = 2*math.pi*i/n
        for j in range(m):
            b = 2*math.pi*j/m
            verts.append(center+rot@Vector(((radius+.017*math.cos(b))*math.cos(a),
                                           (radius+.017*math.cos(b))*math.sin(a), .017*math.sin(b))))
    geo.patch(verts, [(i*m+j, ((i+1)%n)*m+j, ((i+1)%n)*m+(j+1)%m, i*m+(j+1)%m)
                      for i in range(n) for j in range(m)], 'Seal')
    for angle in (0, 2*math.pi/3, 4*math.pi/3):
        geo.beam(center, center+rot@Vector((radius*math.cos(angle), radius*math.sin(angle), 0)), .025, material='Seal')


def interior(kind, seat_y, seat_z, half, dashboard_y):
    geo = Geo('SM_'+kind+'_Interior')
    for sign in (-1, 1):
        x = sign*half*.49
        geo.box((x, seat_y, seat_z-.19), (.36, .40, .25), 'Chassis', .025)
        geo.box((x, seat_y, seat_z), (.49, .49, .14), 'Seat', .045)
        geo.box((x, seat_y+.20, seat_z+.32), (.49, .15, .58), 'Seat', .045)
        geo.box((x, seat_y+.20, seat_z+.71), (.27, .14, .19), 'Seat', .03)
    geo.box((0, dashboard_y, seat_z+.26), (half*1.85, .27, .20), 'Interior', .05)
    geo.tag = 'driver_torso'
    geo.box((half*.49, seat_y-.035, seat_z+.40), (.34, .25, .48), 'Driver', .07)
    geo.tag = 'driver_head'
    geo.box((half*.49, seat_y-.045, seat_z+.75), (.22, .23, .26), 'Driver', .055)
    geo.tag = 'steering'
    steering(geo, (half*.49, seat_y-.39, seat_z+.41), .19)
    geo.finish()


def build_van():
    name, paint, half = 'SM_PanelVan', 'VanPaint', .985
    empty(name, (0, 0, 0), None)
    body, glass = Geo(name+'_Body'), Geo(name+'_Glass')
    body.box((0, 0, .54), (1.75, 5.08, .12), 'Chassis', .025)
    for y in (-1.7, 1.7):
        body.cylinder((0, y, .43), .055, 1.68, 'Chassis', (1, 0, 0), 12)
    body.box((0, -2.60, .49), (1.98, .20, .23), 'Trim', .045)
    body.box((0, 2.60, .49), (1.98, .20, .22), 'Trim', .045)
    hood = [(-2.63, .64), (-2.63, .98), (-2.61, 1.06), (-2.56, 1.12), (-2.45, 1.17), (-1.85, 1.28)]
    edge = sweep(body, hood, [.055]*5+[.025], [paint]*5, half=half)
    # Tek ön kapı çifti, arka yan cam yok. Dikişler gövde mesh'inin parçası.
    for sign in (-1, 1):
        lower_sides(body, sign, -2.45, 2.59, .53, 1.04, [-1.7, 1.7], .43, paint, half)
        front_side = edge+[(edge[-1][0], 1.04), (-2.45, 1.04), (-2.45, .53), (edge[0][0], .53)]
        for z0, z1 in ((.53, 1.04), (1.04, 1.30)):
            side(body, sign, clip_band(front_side, z0, z1), paint, half)
        side(body, sign, [(edge[-1][0], 1.04), (2.59, 1.04), (2.59, 1.28), (-1.85, 1.28), edge[-1]], paint, half)
        side(body, sign, [(-1.85, 1.28), (-1.30, 2.16), (-1.19, 2.16), (-1.74, 1.28)], paint, half)
        windows(body, glass, sign, [(-1.72, 1.31), (-1.19, 2.16), (-.52, 2.16), (-.52, 1.31)], half)
        rect(body, sign, -.50, 2.59, 1.28, 2.16, paint, half)
        # Aynı düzleme çizmek yerine 6 mm dışarı taşan ince kapı çizgisi.
        body.beam((sign*.991, -.50, .60), (sign*.991, -.50, 2.16), .009, material='Trim')
        body.box((sign*.998, -.70, 1.18), (.026, .17, .037), 'Trim', 0)
    front_glass(body, glass, .937, -1.85, 1.28, -1.30, 2.16)
    roof = [(-1.30, 2.16), (-1.32, 2.22), (-1.30, 2.30), (-1.24, 2.36),
            (-1.14, 2.40), (2.45, 2.40), (2.54, 2.38), (2.60, 2.32), (2.63, 2.21)]
    edge = sweep(body, roof, [.015, .04, .06, .07, .07, .07, .06, .04, .015], [paint]*8, half=half)
    for sign in (-1, 1):
        poly = [(-1.30, 2.16)]+edge[1:]+[(2.59, 2.16)]
        side(body, sign, clip_band(poly, 2.16, 2.4), paint, half)
    # Arka çift kapı kapalı, sabit; ayrı çocuk obje ve yan cam oluşturulmaz.
    body.box((0, 2.595, 1.38), (1.95, .07, 1.67), paint, .035)
    body.box((0, 2.637, 1.38), (.009, .009, 1.56), 'Trim', 0)
    for sign in (-1, 1):
        body.box((sign*.13, 2.644, 1.23), (.12, .024, .035), 'Trim', 0)
        body.box((sign*.89, 2.641, .98), (.11, .026, .28), 'Taillight', 0)
        body.box((sign*.89, 2.658, 1.04), (.10, .011, .065), 'Amber', 0)
        for z in (.85, 1.98):
            body.box((sign*.87, 2.64, z), (.11, .025, .045), 'Trim', 0)
        body.box((sign*.70, -2.639, .90), (.34, .028, .24), 'Trim', 0)
        body.box((sign*.70, -2.660, .90), (.28, .018, .18), 'Headlight', 0)
        body.box((sign*.74, -2.656, .725), (.13, .018, .052), 'Amber', 0)
    body.box((0, -2.645, .90), (.84, .03, .23), 'Trim', 0)
    for z in (.83, .90, .97):
        body.box((0, -2.667, z), (.72, .012, .021), 'Seal', 0)
    mirrors(body, glass, half, -1.73, 1.50)
    body.finish()
    glass.finish()
    wheels('PanelVan')
    interior('PanelVan', -.96, .95, half, -1.63)


def build_truck():
    name, paint, half = 'SM_BoxTruck', 'TruckPaint', 1.15
    empty(name, (0, 0, 0), None)
    body, glass = Geo(name+'_Body'), Geo(name+'_Glass')
    # Tek kamyon şasisi; çekici/römork bağlantısı yok.
    for sign in (-1, 1):
        body.box((sign*.68, .02, 1.04), (.15, 9.05, .56), 'Chassis', .025)
    for y in (-3.35, 2.05, 3.35):
        body.cylinder((0, y, .57), .085, 2.05, 'Chassis', (1, 0, 0), 12)
        body.box((0, y, .75), (1.84, .16, .36), 'Chassis', .025)
    body.box((0, -3.35, .78), (2.12, 2.53, .14), 'Chassis', .025)
    body.box((0, -4.63, .64), (2.34, .24, .28), 'Trim', .045)
    body.box((0, 4.62, .63), (2.34, .26, .24), 'Trim', .04)
    nose = [(-4.59, .82), (-4.59, 1.49), (-4.56, 1.57), (-4.51, 1.63)]
    edge = sweep(body, nose, [.05, .05, .04, .02], [paint]*3, half=half)
    for sign in (-1, 1):
        lower_sides(body, sign, -4.36, -2.08, .68, 1.40, [-3.35], .57, paint, half)
        front_side = edge+[(edge[-1][0], 1.40), (-4.36, 1.40), (-4.36, .68), (edge[0][0], .68)]
        for z0, z1 in ((.68, 1.40), (1.40, 1.63)):
            side(body, sign, clip_band(front_side, z0, z1), paint, half)
        side(body, sign, [(edge[-1][0], 1.40), (-2.08, 1.40), (-2.08, 1.63), (-4.51, 1.63), edge[-1]], paint, half)
        side(body, sign, [(-4.51, 1.63), (-4.15, 2.68), (-4.03, 2.68), (-4.39, 1.63)], paint, half)
        windows(body, glass, sign, [(-4.37, 1.67), (-4.02, 2.68), (-2.52, 2.68), (-2.52, 1.67)], half)
        rect(body, sign, -2.50, -2.08, 1.63, 2.68, paint, half)
        body.beam((sign*1.157, -2.50, .84), (sign*1.157, -2.50, 2.68), .012, material='Seal')
        body.box((sign*1.166, -2.72, 1.48), (.03, .20, .045), 'Trim', 0)
    front_glass(body, glass, 1.095, -4.51, 1.63, -4.15, 2.68)
    roof = [(-4.15, 2.68), (-4.17, 2.75), (-4.13, 2.85), (-4.04, 2.92),
            (-2.22, 2.92), (-2.12, 2.88), (-2.08, 2.78), (-2.08, 2.68)]
    edge = sweep(body, roof, [.015, .04, .07, .08, .08, .06, .035, .015], [paint]*7, half=half)
    for sign in (-1, 1):
        side(body, sign, [(-4.15, 2.68)]+edge[1:], paint, half)
    body.box((0, -2.10, 1.76), (2.23, .08, 1.90), paint, .03)
    body.box((0, -4.603, 1.15), (.95, .038, .66), 'Trim', .018)
    body.box((0, -4.63, 1.15), (.80, .022, .54), 'Seal', 0)
    for i in range(7):
        body.box((0, -4.647, .91+i*.08), (.75, .012, .029), 'Trim', 0)
    for sign in (-1, 1):
        body.box((sign*.88, -4.607, 1.02), (.33, .04, .29), 'Trim', .015)
        body.box((sign*.88, -4.636, 1.02), (.26, .02, .22), 'Headlight', 0)
        body.box((sign*.88, -4.62, .81), (.15, .02, .065), 'Amber', 0)
    mirrors(body, glass, half, -4.12, 1.99)
    # Kasa boyu 6.6 m. Tek kapalı hacim; gri kenar profilleri beyaz panelden dışarıdadır.
    body.box((0, 1.34, 2.46), (2.476, 6.62, 2.28), 'Cargo', .065)
    body.box((0, 4.60, 1.095), (2.25, .12, .27), 'Chassis', .025)
    for sign in (-1, 1):
        for y in (-1.925, 4.605):
            body.box((sign*1.232, y, 2.46), (.036, .085, 2.23), 'Trim', 0)
        for z in (1.37, 3.55):
            body.box((sign*1.244, 1.34, z), (.012, 6.51, .068), 'Trim', 0)
        # Arka tandem üzerinde sade çamurluk; tekerlek boşluğu gerçek, kapatılmaz.
        for y in (2.05, 3.35):
            inner = arch(y, .65, .57, .64)
            outer = arch(y, .72, .57, .64)
            for i in range(len(inner)-1):
                side(body, sign, [inner[i], inner[i+1], outer[i+1], outer[i]], paint, 1.235, .19)
        body.box((sign*.96, 4.666, 1.09), (.22, .030, .13), 'Taillight', 0)
        body.box((sign*.96, 4.684, 1.09), (.065, .012, .12), 'Amber', 0)
    # Arka sabit çift yük kapısı: yüzeyden 6 mm ayrılan dikiş ve kilit kolları.
    body.box((0, 4.658, 2.43), (.012, .012, 2.06), 'Trim', 0)
    for sign in (-1, 1):
        body.box((sign*.29, 4.665, 2.40), (.025, .022, 1.83), 'Trim', 0)
        body.box((sign*.35, 4.681, 2.02), (.15, .025, .04), 'Trim', 0)
        for z in (1.62, 3.28):
            body.box((sign*1.12, 4.653, z), (.17, .022, .055), 'Trim', 0)
    body.finish()
    glass.finish()
    wheels('BoxTruck')
    interior('BoxTruck', -3.32, 1.45, half, -4.12)


def render_views():
    folder = RENDERS/f'round_{ROUND:02d}'
    folder.mkdir(parents=True, exist_ok=True)
    setup_render()
    durations = {}
    for kind, spec in SPECS.items():
        name = 'SM_'+kind
        for obj in ASSETS:
            obj.hide_render = not obj.name.startswith(name)
        length, width, height = spec['dimensions']
        scale = length*1.22
        # Kısa ve yüksek araçta (PanelVan) tavan kadraja sığsın diye yüksekliğe göre de genişlet.
        render(folder, kind.lower()+'_threequarter_front', (length*1.65, -length*2.1, height*2.1),
               (0, 0, height*.48), max(scale, height*3.1), durations)
        render(folder, kind.lower()+'_left', (20, 0, height*.5), (0, 0, height*.5), scale, durations)
        render(folder, kind.lower()+'_rear', (0, length*2, height*.52), (0, 0, height*.52), height*2.05, durations)
    for obj in ASSETS:
        obj.hide_render = False
    van, truck = bpy.data.objects['SM_PanelVan'], bpy.data.objects['SM_BoxTruck']
    van.location = (2.0, -1.6, 0)
    truck.location = (-1.7, 0, 0)
    render(folder, 'two_vehicles', (18, -24, 13), (0, 0, 1.5), 13.0, durations)
    # Yan görünümde üç araç art arda: perspektif ölçek farkı yok, hepsi Z=0.
    van.location = (0, -8.0, 0)
    truck.location = (0, 0, 0)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(MINIVAN_FBX), use_anim=False)
    imported = [o for o in bpy.data.objects if o not in before]
    for obj in imported:
        if obj.parent is None:
            obj.location = (0, 8.0, 0)
    render(folder, 'scale_with_minivan', (30, 0, 1.8), (0, 0, 1.8), 24.0, durations)
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)
    van.location = truck.location = (0, 0, 0)
    a = image_array(CONCEPT)
    for kind, crop in [('panelvan', a[473:690, 740:1072]), ('boxtruck', a[388:687, 889:1349])]:
        # Referans alt sırada ters yöne baktığı için yalnız karşılaştırma kırpımı aynalanır.
        side_by_side(crop[:, ::-1], image_array(folder/(kind+'_threequarter_front.png')), folder/('compare_'+kind+'.png'))
    side_by_side(a, image_array(folder/'two_vehicles.png'), folder/'compare_lineup.png')
    for png in folder.glob('*.png'):
        shutil.copy2(png, RENDERS/png.name)
    return durations


def metadata(kind, samples):
    name, spec = 'SM_'+kind, SPECS[kind]
    objects = [o for o in ASSETS if o.parent and o.parent.name == name]
    mats = {m.name for o in objects if o.type == 'MESH' for m in o.data.materials}
    axles = list(spec['axles'].values())
    return {
        'schema_version': 1, 'units': 'metres',
        'coordinates': {'up': '+Z', 'forward': '-Y', 'left': '+X', 'right': '-X', 'length_axis': 'Y'},
        'field_descriptions': {
            'dimensions_m': 'Length Y, width X excluding mirrors, height Z above ground; metres.',
            'axles_y_m': 'Axle centres along Blender root Y; all wheels lie at Z=radius.',
            'wheelbase_m': 'Front axle to rear tandem midpoint (BoxTruck); front to rear axle (PanelVan).',
            'contact_rectangle_wheelbase_m': 'Frontmost to rearmost axle; its midpoint is root Y=0.',
            'pivot_m': 'XYZ object origin in root space; wheel origins at geometric axle centre.',
            'axis': 'Signed unit spin axis in Blender root coordinates; right-hand rule.',
            'materials': 'Material name to sRGB hex. FBX DiffuseColor contains its linear RGB conversion.',
            'palette_samples': '5x5 median; reference pixel XY measured from top left; assumptions labelled.'},
        'dimensions_m': dict(zip(('length', 'width', 'height'), spec['dimensions'])),
        'dimension_exclusion_vertex_group': 'mirrors_excluded_from_dimensions',
        'wheel_radius_m': spec['radius'], 'wheel_x_m': spec['wheel_x'],
        'axles_y_m': spec['axles'], 'wheelbase_m': (sum(axles[1:])/len(axles[1:]))-axles[0],
        'contact_rectangle_wheelbase_m': axles[-1]-axles[0],
        'rear_tandem_spacing_m': axles[-1]-axles[-2] if len(axles) == 3 else None,
        'root_pivot': 'Centre of wheel contact rectangle, Z=0.',
        'parts': {n: p for n, p in PARTS.items() if p['parent'] == name},
        'materials': {m.name: '#'+PALETTE[k] for k, m in MATERIALS.items() if m.name in mats},
        'paint_materials': [MATERIALS[k].name for k in (('VanPaint',) if kind == 'PanelVan' else ('TruckPaint', 'Cargo', 'Chassis'))],
        'glass_material': MATERIALS['Glass'].name,
        'palette_samples': samples, 'palette_reference': str(CONCEPT.relative_to(ROOT)).replace('\\', '/'),
        'driver': {'object': name+'_Interior', 'components': ['head', 'torso'], 'rigged': False, 'face': False},
        'unity_import': {'scale_factor': 1, 'bake_axis_conversion': False,
                         'color_note': 'sRGB hex -> linear Base Color -> unchanged FBX DiffuseColor; Linear URP.',
                         'glass_note': 'Use transparent URP material; exported alpha 0.30.'},
    }


def main():
    global ROUND
    parser = argparse.ArgumentParser()
    parser.add_argument('--round', type=int, default=2)
    parser.add_argument('--no-render', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    ROUND = args.round
    started = time.perf_counter()
    source = hashlib.sha256(Path(__file__).read_bytes().replace(b'\r\n', b'\n')).hexdigest()
    RENDERS.mkdir(parents=True, exist_ok=True)
    cache = Path(tempfile.gettempdir())/'traffic_trucks_render_cache'
    cache.mkdir(exist_ok=True)
    for key in ('OPTIX_CACHE_PATH', 'CUDA_CACHE_PATH'):
        os.environ[key] = str(cache)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.context.preferences.filepaths.use_auto_save_temporary_files = False
    bpy.context.preferences.filepaths.file_preview_type = 'NONE'
    bpy.context.scene.unit_settings.system = 'METRIC'
    samples = make_materials()
    build_van()
    build_truck()
    bpy.context.view_layer.update()
    counts = {o.name: len(o.data.polygons) for o in ASSETS if o.type == 'MESH'}
    totals = {kind: sum(v for n, v in counts.items() if n.startswith('SM_'+kind)) for kind in SPECS}
    print('TRIANGLES', json.dumps(totals), json.dumps(counts), flush=True)
    for kind, spec in SPECS.items():
        if not spec['budget'][0] <= totals[kind] <= spec['budget'][1]:
            raise ValueError(kind+' triangle budget: '+str(totals[kind]))
    digest, canonical = geometry_digest(), canonical_geometry()
    for kind in SPECS:
        name = 'SM_'+kind
        (OUT/(kind.lower()+'_meta.json')).write_text(json.dumps(metadata(kind, samples), indent=2), encoding='utf-8')
        export_asset(bpy.data.objects[name], OUT/(name+'.fbx'))
    durations = {} if args.no_render else render_views()
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'TrafficTrucks.blend'), check_existing=False)
    stats_path = OUT/'stats_trucks.json'
    old = json.loads(stats_path.read_text(encoding='utf-8')) if stats_path.exists() else {}
    entry = {'geometry_sha256': digest, 'canonical_geometry_sha256': canonical, 'source_sha256': source,
             'round': ROUND, 'triangles': totals, 'render_seconds': round(sum(durations.values()), 3),
             'camera_render_seconds': durations, 'total_build_seconds': round(time.perf_counter()-started, 3)}
    if durations:
        manifest = dict(entry)
        manifest['images_sha256'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                     for p in sorted((RENDERS/f'round_{ROUND:02d}').glob('*.png'))}
        (RENDERS/f'round_{ROUND:02d}'/'build_manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    stats = {'geometry_sha256': digest, 'canonical_geometry_sha256': canonical, 'object_triangles': counts,
             'total_triangles': totals, 'last_build': entry, 'build_history': (old.get('build_history', [])+[entry])[-12:]}
    stats_path.write_text(json.dumps(stats, indent=2), encoding='utf-8')
    print('BUILD OK', digest, 'triangles', totals, 'render_seconds', entry['render_seconds'], flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        print('BUILD FAILED', flush=True)
        sys.exit(1)


