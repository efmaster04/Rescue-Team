"""Deterministik sivil trafik ara?lar?. Blender 5.2; metre, Z yukar?, burun -Y.
Geli?tirme: -- --round 1 --no-render. Varsay?lan tam ?retim tur 2.
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
CONCEPT = ROOT/'ArtSource/Traffic/references/01_traffic_lineup.jpg'
MINIVAN_FBX = ROOT/'Export/Minivan/SM_Minivan.fbx'
MATERIALS, PALETTE, PARTS, WORLD_PIVOT = {}, {}, {}, {}
ASSETS = []
P = ''
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
        parent = parent or P
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
    scene.cycles.samples = 48
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
    scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium High Contrast'
    scene.view_settings.exposure = -.25
    world = bpy.data.worlds.new('Studio_World')
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (.82, .82, .82, 1)
    world.node_tree.nodes['Background'].inputs[1].default_value = .35
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
    mat.use_nodes = True
    studio = mat.node_tree.nodes.get('Principled BSDF')
    studio.inputs['Base Color'].default_value = (.79, .81, .83, 1)
    studio.inputs['Emission Color'].default_value = (.79, .81, .83, 1)
    studio.inputs['Emission Strength'].default_value = .45
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
        scene.cycles.samples = 48 if attempt == 0 else 192
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



# Boyutlar brief'e bağlıdır; stil oranları her gövdede ayrı tanımlıdır.
SPECS = {
    'Sedan': dict(length=4.50, width=1.80, height=1.45, radius=.31, axle=1.37,
                  belt=.86, front=-1.03, roof_front=-.57, roof_back=.55, back=1.20, paint='Mustard'),
    'Hatchback': dict(length=4.00, width=1.75, height=1.50, radius=.31, axle=1.22,
                      belt=.88, front=-.94, roof_front=-.49, roof_back=1.40, back=1.77, paint='Sage'),
    'Pickup': dict(length=5.30, width=1.90, height=1.85, radius=.37, axle=1.62,
                   belt=1.06, front=-1.22, roof_front=-.84, roof_back=.05, back=.26, paint='Burgundy'),
}
VARIANTS = []
SAMPLES = {}


def palette():
    # Konseptin alt sırasından 5x5 medyan; füme nötr siluet bölgesinden seçilir.
    a = image_array(CONCEPT)
    locations = {'Mustard': (170, 620), 'Sage': (413, 620), 'Burgundy': (652, 613), 'Charcoal': (262, 570)}
    for name, (x, y) in locations.items():
        rgb = np.median(a[y-2:y+3, x-2:x+3], axis=(0, 1))
        value = ''.join(f'{int(c):02X}' for c in rgb)
        SAMPLES[name] = {'srgb_hex': '#'+value, 'source_pixel_xy_top_left': [x, y], 'median_patch_px': 5}
        VARIANTS.append({'name': name, 'srgb_hex': '#'+value})
    PALETTE.update({n: v['srgb_hex'][1:] for n, v in SAMPLES.items()})
    PALETTE.update(Trim='85868A', Tire='292C30', Rim='777A80', Glass='A3BBC0',
                   Interior='4B5053', Seat='737276', Driver='3E514D', Headlight='E6DEB9',
                   Tail='943D3C', Amber='BE9450', Hay='BDA666', HayDark='95804C', Strap='5B5141')
    for name, color in PALETTE.items():
        mat = bpy.data.materials.new('M_Traffic_'+name)
        mat.use_nodes = True
        rgb = [srgb_to_linear(int(color[i:i+2], 16)/255) for i in (0, 2, 4)]
        mat.diffuse_color = (*rgb, 1)
        bs = mat.node_tree.nodes.get('Principled BSDF')
        bs.inputs['Base Color'].default_value = (*rgb, 1)
        bs.inputs['Roughness'].default_value = .66
        bs.inputs['Specular IOR Level'].default_value = .23
        if name == 'Glass':
            bs.inputs['Transmission Weight'].default_value = .85
            bs.inputs['Alpha'].default_value = .30
            bs.inputs['Roughness'].default_value = .10
            bs.inputs['IOR'].default_value = 1.06
            mat.diffuse_color = (*rgb, .30)
        MATERIALS[name] = mat


def side_panel(g, sign, poly, half, material='Paint', depth=.045):
    g.prism([(sign*half, y, z) for y, z in poly], (-sign*depth, 0, 0), material)


def wheel_arc(y, r, floor, wheel_r, steps=12):
    a = math.asin((floor-wheel_r)/r)
    return [(y-r*math.cos(a+(math.pi-2*a)*i/steps), wheel_r+r*math.sin(a+(math.pi-2*a)*i/steps)) for i in range(steps+1)]


def build_wheels(s):
    r, x, n = s['radius'], s['width']/2-.135, 20
    # Gerçek halka süpürme: pahlar lastiğin yüzeyinde, dekoratif subdiv yok.
    profile = [(-.12, r*.63), (-.115, r*.82), (-.075, r), (.075, r), (.115, r*.82), (.12, r*.63)]
    for end, y in (('F', -s['axle']), ('R', s['axle'])):
        for sign, side_name in ((1, 'L'), (-1, 'R')):
            c = Vector((sign*x, y, r))
            g = Geo(f'{P}_Wheel_{end}{side_name}')
            rings = [[c+Vector((dx, rad*math.cos(2*math.pi*i/n), rad*math.sin(2*math.pi*i/n))) for i in range(n)] for dx, rad in profile]
            for j in range(len(rings)-1):
                g.patch(rings[j]+rings[j+1], [(i, (i+1)%n, (i+1)%n+n, i+n) for i in range(n)], 'Tire')
            for dx in (-.123, .123):
                g.cylinder(c+Vector((dx, 0, 0)), r*.63, .006, 'Rim', (1, 0, 0), n)
                g.cylinder(c+Vector((dx*1.05, 0, 0)), r*.34, .012, 'Trim', (1, 0, 0), 10)
            g.finish(c, motion={'type': 'axle', 'axis': [1, 0, 0], 'radius_m': r})


def build_car(kind, s):
    global P
    P = 'SM_'+kind
    root = empty(P, (0, 0, 0), None)
    template = MATERIALS[s['paint']]
    paint = template.copy()
    paint.name = 'M_'+kind+'_Paint_Surface'
    MATERIALS['Paint'] = paint
    body, glass = Geo(P+'_Body'), Geo(P+'_Glass')
    half = s['width']/2-.015
    L, H, belt = s['length'], s['height'], s['belt']
    cf, rf, rb, cb = (s[n] for n in ('front', 'roof_front', 'roof_back', 'back'))
    front, rear, floor = -L/2+.085, L/2-.085, .29 if kind != 'Pickup' else .36
    arch_r = s['radius']+.065
    hood_path = [(front, floor+.10), (front, belt-.15), (front+.03, belt-.08), (front+.10, belt-.045), (cf, belt)]
    hood_edge = sweep(body, hood_path, [.045]*4+[.025], ['Paint']*4, half=half, thickness=.035)
    if kind == 'Pickup':
        top_path = hood_edge+[(rear, belt)]
    else:
        trunk_path = [(cb, belt), (rear-.12, belt-.015), (rear-.025, belt-.045), (rear, belt-.10), (rear, floor+.12)]
        trunk_edge = sweep(body, trunk_path, [.025, .04, .04, .04, .04], ['Paint']*4, half=half, thickness=.035)
        top_path = hood_edge+[(cb, belt)]+trunk_edge[1:]
    lower = [(front, floor)]
    for ay in (-s['axle'], s['axle']):
        a = wheel_arc(ay, arch_r, floor, s['radius'])
        lower += [(a[0][0], floor)]+a+[(a[-1][0], floor)]
    lower += [(rear, floor)]
    for sign in (-1, 1):
        # Tek yan siluet; alt kemerler gerçekten boş, üst bant kırpılarak pah ile birleşir.
        outline = top_path+list(reversed(lower))
        for z0, z1 in ((floor-.01, belt-.12), (belt-.12, 3)):
            part = clip_band(outline, z0, z1)
            if part: side_panel(body, sign, part, half)
        for ay in (-s['axle'], s['axle']):
            inner = wheel_arc(ay, arch_r-.009, floor, s['radius'])
            outer = wheel_arc(ay, arch_r+.027, floor, s['radius'])
            for j in range(len(inner)-1):
                side_panel(body, sign, [inner[j], inner[j+1], outer[j+1], outer[j]], half+.015, depth=.05)
            # İç davlumbaz: kabinden kemer açıklığı boyunca zemin görünmez.
            well = wheel_arc(ay, arch_r+.009, floor, s['radius'])
            side_panel(body, sign, well, half-.25, 'Interior', depth=.02)
            for a,b in zip(well,well[1:]):
                body.patch([(sign*(half-.25),*a),(sign*(half-.025),*a),
                            (sign*(half-.025),*b),(sign*(half-.25),*b)],[(0,1,2,3)],'Interior')
        # Sade etek, kapı sınırları ve gri kollar, tümü gövde mesh'inde.
        doors = [(cf+.06, -.02), (.03, min(cb-.12,s['axle']-.10))] if kind != 'Pickup' else [(cf+.05, cb-.06)]
        for ya, yb in doors:
            # Kapı çizgisi kemerin içinden geçmez.
            zlow=floor+.045
            def door_bottom(y):
                dy=y-s['axle']; r=arch_r+.045
                return max(zlow,s['radius']+math.sqrt(max(0,r*r-dy*dy))) if abs(dy)<r else zlow
            path=[(ya,belt-.015),(ya,door_bottom(ya))]
            bend=s['axle']-(arch_r+.045)
            if ya<bend<yb:
                path.append((bend,zlow))
                path.extend((bend+(yb-bend)*i/4,door_bottom(bend+(yb-bend)*i/4)) for i in range(1,5))
            else: path.append((yb,door_bottom(yb)))
            path.append((yb,belt-.015))
            for a,b in zip(path,path[1:]):
                body.beam((sign*(half+.006), *a), (sign*(half+.006), *b), .007, material='Interior')
            body.box((sign*(half+.006), yb-.12, belt-.10), (.018, .14, .025), 'Trim', 0)
    # Şasi orta bölgesi lastiklerin hacmine girmez.
    body.box((0, 0, floor+.015), (s['width']-.49, L-.32, .08), 'Interior', 0)
    body.box((0,0,floor+.055),(s['width']-.07,2*(s['axle']-arch_r-.02),.035),'Interior',0)
    for y in (-s['axle'], s['axle']):
        body.cylinder((0, y, s['radius']), .035, s['width']-.25, 'Tire', (1, 0, 0), 10)
    # Tamponlar boyun ölçü sınırını tanımlar.
    for y in (-L/2+.075, L/2-.075):
        body.box((0, y, floor+.08), (s['width']-.03, .15, .15), 'Trim', .035)
    body.box((0, front-.012, belt-.20), (.72, .026, .18), 'Trim', .02)
    for z in (belt-.255, belt-.20, belt-.145):
        body.box((0, front-.030, z), (.65, .013, .018), 'Interior', 0)
    for sign in (-1, 1):
        body.box((sign*(half-.19), front-.018, belt-.20), (.25, .035, .19), 'Trim', .025)
        body.box((sign*(half-.19), front-.039, belt-.20), (.20, .012, .14), 'Headlight', 0)
        body.box((sign*(half-.12), front-.025, belt-.34), (.10, .02, .034), 'Amber', 0)
        body.box((sign*(half-.10), rear+.008, belt-.17), (.12, .035, .23), 'Tail', .015)
        body.box((sign*(half-.10), rear+.031, belt-.22), (.10, .012, .035), 'Headlight', 0)
    body.box((0, rear+.008, floor+.16), (.37, .018, .09), 'Trim', 0)
    # Kabin yanları yukarı doğru daralır; camlar gövde levhasından bağımsızdır.
    top = H-.065
    def side_x(z): return half-.11*max(0, min(1, (z-belt)/(top-belt)))
    def cabin_patch(sign, poly, mat='Paint', target=None, outset=0):
        (target or body).prism([(sign*(side_x(z)+outset), y, z) for y, z in poly], (-sign*.025, 0, 0), mat)
    def side_window(sign, poly):
        # Cam çerçeveden 6 mm içeri gömülür; eş düzlemli yüz yok.
        glass.prism([(sign*(side_x(z)-.006), y, z) for y,z in poly], (-sign*.006,0,0), 'Glass')
        for a,b in zip(poly, poly[1:]+poly[:1]):
            body.beam((sign*(side_x(a[1])+.003),*a),(sign*(side_x(b[1])+.003),*b), .022, material='Interior')
    for sign in (-1, 1):
        # Kapı üst eşiği camın alt kenarını kapatır; açık yatay ışık yarığı bırakılmaz.
        cabin_patch(sign, [(cf,belt-.04),(cf,belt+.04),(cb,belt+.04),(cb,belt-.04)], outset=.006)
        cabin_patch(sign, [(rf,top-.015),(rf,H-.028),(rb,H-.028),(rb,top-.015)], outset=.006)
        cabin_patch(sign, [(cf,belt), (rf,top), (rf+.075,top), (cf+.085,belt)])
        cabin_patch(sign, [(rb-.08,top),(rb,top),(cb,belt),(cb-.12,belt)])
        if kind == 'Pickup':
            side_window(sign, [(cf+.10,belt+.035),(rf+.09,top-.018),(rb-.10,top-.018),(cb-.13,belt+.035)])
        else:
            mid = -.05 if kind == 'Sedan' else .10
            cabin_patch(sign, [(mid-.045,belt),(mid-.045,top),(mid+.045,top),(mid+.045,belt)])
            side_window(sign, [(cf+.10,belt+.035),(rf+.09,top-.018),(mid-.06,top-.018),(mid-.06,belt+.035)])
            side_window(sign, [(mid+.06,belt+.035),(mid+.06,top-.018),(rb-.10,top-.018),(cb-.13,belt+.035)])
        # Aynalar Body'dedir, ölçü denetiminde yalnız bu belirlenmiş bölge dışlanır.
        body.tag = 'mirrors'
        body.beam((sign*half,cf+.06,belt+.09),(sign*(half+.15),cf+.04,belt+.13),.025,material='Trim')
        body.box((sign*(half+.17),cf+.03,belt+.17),(.10,.14,.15),'Trim',.022)
        body.box((sign*(half+.17),cf+.108,belt+.17),(.075,.008,.11),'Rim',0)
        body.tag = 'shell'
    # Ön ve arka camın dört köşesi yan camlarla aynı daralan kesiti izler.
    for y0,y1 in ((cf,rf),(cb,rb)):
        bottom_x,top_x=side_x(belt+.025)-.018,side_x(top-.015)-.018
        poly=[(-bottom_x,y0,belt+.025),(bottom_x,y0,belt+.025),
              (top_x,y1,top-.015),(-top_x,y1,top-.015)]
        glass.prism(poly,(0,.006 if y0==cf else -.006,0),'Glass')
        for a,b in zip(poly,poly[1:]+poly[:1]): body.beam(a,b,.026,material='Interior')
    roof_path=[(rf-.025,top-.02),(rf-.022,H-.04),(rf+.04,H-.008),(rf+.12,H),
               (rb-.10,H),(rb-.025,H-.012),(rb+.025,H-.045),(rb+.025,top-.02)]
    sweep(body,roof_path,[.035]*len(roof_path),['Paint']*(len(roof_path)-1),half=half-.105,thickness=.028)
    if kind == 'Pickup':
        # Açık kasa: iç yüzler gövdeye, iki bağımsız balya kendi taban pivotuna aittir.
        bed_top=.86
        body.box((0,1.43,bed_top-.04),(1.65,2.20,.08),'Paint',0)
        body.box((0,.34,1.00),(1.70,.10,.28),'Paint',.02)
        body.box((0,rear-.025,.98),(1.70,.07,.29),'Paint',.02)
        body.box((0,rear-.025,(floor+.835)/2),(1.70,.075,.835-floor),'Paint',0)
        body.box((0,cb-.015,(floor+belt)/2),(1.70,.055,belt-floor),'Paint',0)
        for sign in (-1,1):
            body.box((sign*(half-.035),1.44,1.04),(.085,2.20,.11),'Paint',.02)
        for i,y in enumerate((.93,1.96),1):
            g=Geo(f'{P}_Hay_{i:02d}')
            size=(1.30,.87,.62)
            g.box((0,y,bed_top+size[2]/2),size,'Hay',.08)
            # Az sayıda iri lif çizgisi, sabit düzen: iki balya ayrı okunur.
            for j in range(5):
                z=bed_top+.12+j*.09
                for sign in (-1,1):
                    g.box((sign*.652,y,z),(.006,.66,.009),'HayDark',0)
            g.finish((0,y,bed_top),motion={'type':'free_rigid_body','attached_initially':True})
            # İki kayış balyayı sarar, kasa kenarına kadar iner; Body'nin parçasıdır.
            for x in (-.36,.36):
                body.box((x,y,bed_top+.626),(.055,.75,.012),'Strap',0)
                for sy in (-1,1):
                    body.beam((x,y+sy*.365,bed_top+.62),(x,y+sy*.442,bed_top+.54),.055,.012,'Strap')
                    body.box((x,y+sy*.442,bed_top+.26),(.055,.012,.54),'Strap',0)
        s['bed']={'floor_z_m':bed_top,'inner_x_m':[-.84,.84],'inner_y_m':[.39,2.48],
                  'hay':[{'name':f'{P}_Hay_{i:02d}','position_m':[0,y,bed_top],'dimensions_xyz_m':[1.30,.87,.62]} for i,y in enumerate((.93,1.96),1)]}
    body.finish()
    glass.finish()
    build_interior(s)
    build_wheels(s)
    return root


def build_interior(s):
    g=Geo(P+'_Interior')
    belt,H,cf=s['belt'],s['height'],s['front']
    y=cf+.48
    seat_z=.53 if H<1.6 else .79
    for x in (-.40,.40):
        g.box((x,y,seat_z),(.44,.43,.13),'Seat',.035)
        g.box((x,y+.18,seat_z+.23),(.43,.12,.42),'Seat',.035)
        g.box((x,y+.18,seat_z+.48),(.24,.12,.15),'Seat',.025)
    g.box((0,cf+.10,belt-.04),(s['width']-.20,.23,.15),'Interior',.025)
    # Yalnız kafa+gövde; yüz, el, rig ve ek yolcu yok.
    g.box((.40,y-.02,seat_z+.25),(.30,.22,.36),'Driver',.06)
    g.cylinder((.40,y-.03,seat_z+.52),.105,.20,'Driver',segments=10,radius_top=.088)
    # Basit halka direksiyon, sürücüye doğru eğimli.
    center=Vector((.40,cf+.25,belt+.055))
    axis=Vector((0,-.70,.71))
    rot=axis.to_track_quat('Z','Y').to_matrix()
    n=16
    verts=[center+rot@Vector((r*math.cos(2*math.pi*i/n),r*math.sin(2*math.pi*i/n),z)) for z,r in ((-.012,.135),(-.012,.112),(.012,.112),(.012,.135)) for i in range(n)]
    faces=[]
    for j in range(4):
        for i in range(n): faces.append((j*n+i,j*n+(i+1)%n,((j+1)%4)*n+(i+1)%n,((j+1)%4)*n+i))
    g.patch(verts,faces,'Interior')
    for d in ((-.11,0,0),(.11,0,0),(0,-.10,0)):
        g.beam(center,center+rot@Vector(d),.016,material='Interior')
    g.finish()


def car_meta(kind,s):
    p='SM_'+kind
    mats={m.name:'#'+PALETTE[n] for n,m in MATERIALS.items() if n!='Paint' and n not in ('Mustard','Sage','Burgundy','Charcoal')}
    paint=bpy.data.objects[p+'_Body'].data.materials.get('M_'+kind+'_Paint_Surface')
    mats[paint.name]='#'+PALETTE[s['paint']]
    return {'schema_version':1,'units':'metres',
            'coordinates':{'up':'+Z','forward':'-Y','left':'+X','right':'-X','length_axis':'Y'},
            'field_descriptions':{'measurement':'Body envelope in metres, excludes mirrors only; height from ground.',
                'parts':'Direct root children; pivot_m is XYZ in Blender root space.',
                'axles_y_m':'Front negative Y, rear positive Y; wheel rotation axis +X.',
                'paint_variants':'sRGB hex values; convert once to linear Base Color for Linear URP.',
                'bed':'Inner free box, floor top Z, hay bottom-centre positions and XYZ dimensions; all metres.',
                'palette_samples':'5x5 median on concept; XY uses top-left image origin.'},
            'measurement':{'length_m':s['length'],'width_m':s['width'],'height_m':s['height'],'tolerance_m':.05},
            'wheel_radius_m':s['radius'],'wheel_x_m':s['width']/2-.135,'axles_y_m':{'F':-s['axle'],'R':s['axle']},'wheel_axis':[1,0,0],
            'parts':{n:d for n,d in PARTS.items() if d['parent']==p},
            'mirror_exclusion':{'abs_x_above_m':s['width']/2-.02,'y_range_m':[s['front']-.08,s['front']+.16],
                                'z_range_m':[s['belt']+.06,s['belt']+.26]},
            'glass_material':'M_Traffic_Glass','paint_material':paint.name,
            'paint_variants':VARIANTS if kind!='Pickup' else [next(v for v in VARIANTS if v['name']=='Burgundy')],
            'default_paint':s['paint'],'materials':mats,'palette_samples':SAMPLES,
            'palette_reference':str(CONCEPT.relative_to(ROOT)).replace('\\','/'),
            'bed':s.get('bed'),
            'unity_import':{'scale_factor':1,'bake_axis_conversion':False,'color_note':'sRGB hex -> linear Base Color -> FBX DiffuseColor unchanged.',
                            'glass_note':'URP transparent, alpha 0.30; separate glass object.'}}


def render_views():
    folder=OUT/'renders/cars'/f'round_{ROUND:02d}'
    folder.mkdir(parents=True,exist_ok=True)
    setup_render()
    durations={}
    roots={k:bpy.data.objects['SM_'+k] for k in SPECS}
    def visible(only=None):
        for k,root in roots.items():
            root.location=(0,0,0)
            for o in root.children: o.hide_render=only is not None and k!=only
        bpy.context.view_layer.update()
    for kind,s in SPECS.items():
        visible(kind)
        scale=s['length']*1.38
        for view,position in [('threequarter_front',(9,-12,6)),('left',(20,0,s['height']*.49)),('rear',(0,20,s['height']*.49))]:
            render(folder,kind.lower()+'_'+view,position,(0,0,s['height']*.49),scale,durations)
    visible()
    # Kameranın sağ vektörü +X: soldan Sedan, Hatchback, Pickup.
    for i,root in enumerate(roots.values()): root.location=((i-1)*3.35,0,0)
    render(folder,'lineup',(9,-20,10),(0,0,.75),13.0,durations)
    visible()
    # 2 sıra x 4 renk; geometri kopyası yalnız render içindir.
    for o in ASSETS:
        if o.type=='MESH': o.hide_render=True
    copies=[]
    for row,kind in enumerate(('Sedan','Hatchback')):
        for i,v in enumerate(VARIANTS):
            offset=Vector(((i-1.5)*2.9,(row-.5)*5.0,0))
            for original in roots[kind].children:
                obj=original.copy(); obj.data=original.data.copy(); obj.parent=None
                bpy.context.collection.objects.link(obj)
                obj.location=original.location+offset
                obj.hide_render=False
                for j,m in enumerate(obj.data.materials):
                    if m.name.startswith('M_'+kind+'_Paint'): obj.data.materials[j]=MATERIALS[v['name']]
                copies.append(obj)
    render(folder,'paint_variants',(7,-18,20),(0,0,.5),17.0,durations)
    for o in copies: bpy.data.objects.remove(o,do_unlink=True)
    visible()
    # Aynı kamera, zemin ve metre ölçeğinde minibüsle karşılaştırma.
    before=set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(MINIVAN_FBX),use_anim=False)
    imported=[o for o in bpy.data.objects if o not in before]
    for i,root in enumerate(roots.values()): root.location=(0,(i-1.5)*5.9,0)
    for o in imported:
        if o.parent is None: o.location=(0,8.85,0)
    render(folder,'scale_with_minivan',(30,0,6),(0,0,1),25.2,durations)
    for o in imported: bpy.data.objects.remove(o,do_unlink=True)
    visible()
    concept=image_array(CONCEPT)
    for kind,box in {'Sedan':(17,516,411,700),'Hatchback':(332,520,616,695),'Pickup':(536,491,860,688)}.items():
        x0,y0,x1,y1=box
        side_by_side(concept[y0:y1,x0:x1],image_array(folder/(kind.lower()+'_threequarter_front.png')),folder/('compare_'+kind.lower()+'.png'))
    side_by_side(concept[490:704,15:862],image_array(folder/'lineup.png'),folder/'compare_lineup.png')
    side_by_side(image_array(ROOT/'ArtSource/Traffic/references/02_minivan_style_match.png'),image_array(folder/'sedan_threequarter_front.png'),folder/'compare_minivan_style.png')
    for png in folder.glob('*.png'): shutil.copy2(png,folder.parent/png.name)
    return durations


def main():
    global ROUND
    parser=argparse.ArgumentParser()
    parser.add_argument('--round',type=int,default=2)
    parser.add_argument('--no-render',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    ROUND=args.round
    started=time.perf_counter()
    source=hashlib.sha256(Path(__file__).read_bytes().replace(b'\r\n',b'\n')).hexdigest()
    OUT.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.context.preferences.filepaths.use_auto_save_temporary_files=False
    bpy.context.preferences.filepaths.file_preview_type='NONE'
    bpy.context.scene.unit_settings.system='METRIC'
    palette()
    roots={kind:build_car(kind,s) for kind,s in SPECS.items()}
    bpy.context.view_layer.update()
    counts={o.name:len(o.data.polygons) for o in ASSETS if o.type=='MESH'}
    totals={kind:sum(v for k,v in counts.items() if k.startswith('SM_'+kind+'_')) for kind in SPECS}
    print('TRIANGLES',json.dumps(totals),json.dumps(counts),flush=True)
    for kind,total in totals.items():
        low,high=(3500,5500) if kind=='Pickup' else (3000,5000)
        if not low<=total<=high: raise ValueError(f'{kind}: triangle budget {total}, expected {low}..{high}')
    digest,canonical=geometry_digest(),canonical_geometry()
    for kind,s in SPECS.items():
        (OUT/(kind.lower()+'_meta.json')).write_text(json.dumps(car_meta(kind,s),indent=2),encoding='utf-8')
        export_asset(roots[kind],OUT/('SM_'+kind+'.fbx'))
    durations={} if args.no_render else render_views()
    # Blend açıldığında üç araç üst üste gelmez; FBX'ler önceden sıfır kökte yazıldı.
    for i,root in enumerate(roots.values()): root.location=((i-1)*3.35,0,0)
    for o in ASSETS: o.hide_render=False
    if bpy.context.scene.camera:
        camera=bpy.context.scene.camera
        camera.location=(9,-20,10)
        camera.rotation_euler=(Vector((0,0,.75))-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.ortho_scale=13.0
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'TrafficCars.blend'),check_existing=False)
    stats_path=OUT/'stats_cars.json'
    old=json.loads(stats_path.read_text(encoding='utf-8')) if stats_path.exists() else {}
    entry={'geometry_sha256':digest,'canonical_geometry_sha256':canonical,'source_sha256':source,'round':ROUND,
           'triangles':totals,'render_seconds':round(sum(durations.values()),3),'camera_render_seconds':durations,
           'total_build_seconds':round(time.perf_counter()-started,3)}
    stats={'geometry_sha256':digest,'canonical_geometry_sha256':canonical,'vehicle_triangles':totals,
           'object_triangles':counts,'last_build':entry,'build_history':(old.get('build_history',[])+[entry])[-12:]}
    stats_path.write_text(json.dumps(stats,indent=2),encoding='utf-8')
    print('BUILD OK',digest,json.dumps(totals),'render_seconds',entry['render_seconds'],flush=True)


if __name__=='__main__':
    try: main()
    except Exception:
        traceback.print_exc()
        print('BUILD FAILED',flush=True)
        sys.exit(1)
