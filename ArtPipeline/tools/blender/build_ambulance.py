"""Deterministik ambulans ve kanvas sedye. Blender 5.2, Z yukarı, burun -Y.
Çalıştırma: blender -b --factory-startup --python-exit-code 1 --python tools/blender/build_ambulance.py
Geliştirme seçenekleri: -- --round 1 --no-render. Varsayılan tur 5.
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
OUT = ROOT/'Export/Ambulance'
STRETCHER_OUT = ROOT/'Export/Stretcher'
CONCEPT = ROOT/'ArtSource/Ambulance/references/01_turnaround_white_red.jpg'
MINIVAN_FBX = ROOT/'Export/Minivan/SM_Minivan.fbx'
P, S = 'SM_Ambulance', 'SM_Stretcher'
MATERIALS, PALETTE, PARTS, WORLD_PIVOT = {}, {}, {}, {}
ASSETS = []
ROUND = 5
WHEEL_R, WHEEL_X = .43, .86
ARCH_R = .51
AXLES = {'F': -1.85, 'R': 1.85}
BED_TOP = .98
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
        if isinstance(material, str):
            self.materials.extend([material]*len(faces))
        else:
            self.materials.extend(material)

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

    def torus(self, center, major, minor, material, axis=(0, 0, 1), rings=12, sides=5):
        rot = Vector(axis).to_track_quat('Z', 'Y').to_matrix()
        verts = [rot@Vector(((major+minor*math.cos(2*math.pi*j/sides))*math.cos(2*math.pi*i/rings),
                             (major+minor*math.cos(2*math.pi*j/sides))*math.sin(2*math.pi*i/rings),
                             minor*math.sin(2*math.pi*j/sides)))+Vector(center)
                 for i in range(rings) for j in range(sides)]
        self.patch(verts, [(i*sides+j, ((i+1) % rings)*sides+j,
                            ((i+1) % rings)*sides+(j+1) % sides, i*sides+(j+1) % sides)
                           for i in range(rings) for j in range(sides)], material)

    def sphere(self, center, scale, material, segments=16, rings=7):
        # Tek kutup vertex'i (build_patient / build_rescue_crew chibi kafa/göz dili).
        verts = [(center[0], center[1], center[2]+scale[2])]
        for j in range(1, rings):
            theta = math.pi*j/rings
            for i in range(segments):
                phi = 2*math.pi*i/segments
                verts.append((center[0]+scale[0]*math.sin(theta)*math.cos(phi),
                              center[1]+scale[1]*math.sin(theta)*math.sin(phi),
                              center[2]+scale[2]*math.cos(theta)))
        verts.append((center[0], center[1], center[2]-scale[2]))
        faces = [(0, 1+i, 1+(i+1) % segments) for i in range(segments)]
        for j in range(rings-2):
            for i in range(segments):
                a, b = 1+j*segments+i, 1+j*segments+(i+1) % segments
                faces.append((a, a+segments, b+segments, b))
        end = len(verts)-1
        faces += [(end, end-1-i, end-1-(i+1) % segments) for i in range(segments)]
        self.patch(verts, faces, material)

    def loft(self, rings, material, segments=10):
        verts = []
        for center, rx, ry in rings:
            for i in range(segments):
                t = 2*math.pi*i/segments
                verts.append((center[0]+rx*math.cos(t), center[1]+ry*math.sin(t), center[2]))
        faces, mats = [], []
        band_mats = [material]*(len(rings)-1) if isinstance(material, str) else list(material)
        for j, mat in enumerate(band_mats):
            for i in range(segments):
                a, b = j*segments+i, j*segments+(i+1) % segments
                faces.append((a, b, b+segments, a+segments))
                mats.append(mat)
        faces += [tuple(reversed(range(segments))), tuple((len(rings)-1)*segments+i for i in range(segments))]
        mats += [band_mats[0], band_mats[-1]]
        self.patch(verts, faces, mats)

    def tube(self, points, radius, material, sides=6):
        verts = []
        for i, p in enumerate(points):
            tangent = (Vector(points[min(i+1, len(points)-1)])-Vector(points[max(0, i-1)])).normalized()
            axis = Vector((0, 1, 0)) if abs(tangent.dot(Vector((0, 1, 0)))) <= .95 else Vector((1, 0, 0))
            u = tangent.cross(axis).normalized()
            v = tangent.cross(u).normalized()
            r = radius[i] if isinstance(radius, (tuple, list)) else radius
            for k in range(sides):
                a = 2*math.pi*k/sides
                verts.append(tuple(Vector(p)+r*(u*math.cos(a)+v*math.sin(a))))
        faces, mats = [], []
        band_mats = [material]*(len(points)-1) if isinstance(material, str) else list(material)
        for i, mat in enumerate(band_mats):
            for k in range(sides):
                a, b = i*sides+k, i*sides+(k+1) % sides
                faces.append((a, b, b+sides, a+sides))
                mats.append(mat)
        faces += [tuple(reversed(range(sides))), tuple((len(points)-1)*sides+k for k in range(sides))]
        mats += [band_mats[0], band_mats[-1]]
        self.patch(verts, faces, mats)

    def finish(self, pivot=(0, 0, 0), parent=P, motion=None):
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
    prefs = bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type = 'OPTIX'
    prefs.refresh_devices()
    gpu = [d for d in prefs.devices if d.type == 'OPTIX']
    for device in prefs.devices:
        device.use = device.type == 'OPTIX'
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


def interior_roof_mesh():
    body = bpy.data.objects[P+'_Body']
    copy = body.data.copy()
    group = body.vertex_groups['roof_hide_for_interior'].index
    remove = {v.index for v in body.data.vertices if any(g.group == group for g in v.groups)}
    bm = bmesh.new()
    bm.from_mesh(copy)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in sorted(remove)], context='VERTS')
    bm.to_mesh(copy)
    bm.free()
    return copy


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
        # Yakın planlarda (crew_front) bir köşe koyu araç içi olabilir; gerçek
        # karanlık karede dört köşe de koyudur, bu yüzden en açık köşeye bakılır.
        img = image_array(folder/(name+'.png'))
        if max(img[2, 2].mean(), img[2, -3].mean(), img[-3, 2].mean(), img[-3, -3].mean()) > 150:
            break
        print('RENDER RETRY', name, attempt+1, flush=True)
    else:
        raise RuntimeError('Render failed (dark frame): '+name)
    durations[name] = round(time.perf_counter()-started, 3)
    print('RENDER OK', name, durations[name], flush=True)



def srgb_to_linear(v):
    return v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4


def reference_palette():
    image = bpy.data.images.load(str(CONCEPT))
    w, h = image.size
    pixels = np.empty(w*h*4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(h, w, 4)
    locations = {'Paint': (1159, 191), 'Red': (1155, 253), 'Trim': (553, 327),
                 'Tire': (1190, 350), 'Rim': (1168, 344), 'Glass': (1040, 187),
                 'Beacon': (251, 63), 'Interior': (880, 621), 'Seat': (1100, 547),
                 'Headlight': (159, 279), 'Amber': (130, 283), 'Metal': (1252, 338)}
    samples = {}
    for name, (x, y) in locations.items():
        rgb = np.median(pixels[h-y-3:h-y+2, x-2:x+3, :3], axis=(0, 1))
        color = ''.join(f'{round(float(c)*255):02X}' for c in rgb)
        PALETTE[name] = color
        samples[name] = {'hex': '#'+color, 'source_pixel_xy_top_left': [x, y], 'median_patch_px': 5}
    bpy.data.images.remove(image)
    crew_colors = {'Canvas': 'D97836', 'Aluminium': 'AAB3B8', 'Suit': '1F4D3A',
                   'StripeYellow': 'F1C52D', 'StripeSilver': 'C7C9BF', 'Skin': 'BD865A',
                   'SkinLight': 'CD9670', 'HairDark': '302823', 'HairBrown': '6E4732',
                   'EyeWhite': 'F2EFE5', 'Pupil': '151412', 'Mouth': '59382A'}
    for name, color in crew_colors.items():
        PALETTE[name] = color
        samples[name] = {'hex': '#'+color, 'source': 'neutral brief assumption'}
    return samples


def make_materials():
    for name, color in PALETTE.items():
        mat = bpy.data.materials.new(('M_Stretcher_' if name in ('Canvas', 'Aluminium') else 'M_Ambulance_')+name)
        mat.use_nodes = True
        rgb = tuple(srgb_to_linear(int(color[i:i+2], 16)/255) for i in (0, 2, 4))
        mat.diffuse_color = (*rgb, 1)
        shader = mat.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Base Color'].default_value = (*rgb, 1)
        shader.inputs['Roughness'].default_value = .63
        shader.inputs['Specular IOR Level'].default_value = .25
        if name == 'Glass':
            shader.inputs['Transmission Weight'].default_value = .85
            shader.inputs['Roughness'].default_value = .09
            shader.inputs['IOR'].default_value = 1.06
            shader.inputs['Alpha'].default_value = .3
            mat.diffuse_color = (*rgb, .3)
        if name == 'Beacon':
            shader.inputs['Roughness'].default_value = .22
        MATERIALS[name] = mat


def side(geo, sign, poly, mat='Paint', x=.985, depth=.045):
    geo.prism([(sign*x, y, z) for y, z in poly], (-sign*depth, 0, 0), mat)


def rectangle(geo, sign, y0, y1, z0, z1, mat='Paint', x=.985, depth=.045):
    side(geo, sign, [(y0, z0), (y1, z0), (y1, z1), (y0, z1)], mat, x, depth)


def arc(y, radius, steps=14, floor=.54):
    # Kemer gövde alt çizgisinde (z=.54) başlar; gövdenin altına sarkmaz.
    t0 = math.asin((floor-WHEEL_R)/radius)
    return [(y-radius*math.cos(t0+(math.pi-2*t0)*i/steps), WHEEL_R+radius*math.sin(t0+(math.pi-2*t0)*i/steps)) for i in range(steps+1)]


def window(geo, glass, sign, poly):
    side(glass, sign, poly, 'Glass', .972, .008)
    for a, b in zip(poly, poly[1:]+poly[:1]):
        geo.beam((sign*.986, *a), (sign*.986, *b), .035, material='Trim')


# Yan şerit düzeni: (z alt, z üst, materyal). Yan levhalar bu bantlara kırpılır.
BANDS = [(-1, 1.15, 'Paint'), (1.15, 1.43, 'Red'), (1.43, 2.38, 'Paint'), (2.38, 2.52, 'Red'), (2.52, 9, 'Paint')]


def windshield_y(z):
    # Ön cam çizgisi: taban (y -2.08, z 1.30), üst (y -1.50, z 2.30).
    return -2.08+(z-1.30)*.58


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


def banded(geo, sign, poly, x=.985, depth=.045):
    # Tek yan levha, renk bantlarına bölünerek aynı düzlemde çakışmadan çizilir.
    for z0, z1, mat in BANDS:
        part = clip_band(poly, z0, z1)
        if part:
            side(geo, sign, part, mat, x, depth)


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


def build_body():
    body, glass = Geo(P+'_Body'), Geo(P+'_Glass')
    # Şasi ve taban: ambulans arka hacmi gerçekten boş.
    body.box((0, .15, .57), (1.80, 5.47, .12), 'Trim', .025)
    for y in AXLES.values():
        body.cylinder((0, y, WHEEL_R), .055, 1.72, 'Tire', (1, 0, 0), 12)
        # Koyu tekerlek havuzu: kemer boşluğundan iç taban (bej) görünmez.
        # Üst yüzü yatak çerçevesinin (z .87) altında; arka yatak bunun üstüne oturur.
        for sign in (-1, 1):
            body.box((sign*.8175, y, .7025), (.235, 2*ARCH_R-.02, .325), 'Tire', 0)
            # Dış ince astar kemer tepesine kadar çıkar (yatak çerçevesi x<=.895'te kalır).
            body.box((sign*.9175, y, .7475), (.035, 2*ARCH_R-.02, .415), 'Tire', 0)
    # Tampon yan panelden 5 mm taşar: eş düzlemli yüz kararması olmaz.
    body.box((0, -2.91, .52), (1.98, .18, .28), 'Trim', .045)
    body.box((0, 2.88, .46), (1.95, .20, .24), 'Trim', .045)
    # Uzatılmış, öne eğimli ve yuvarlatılmış kaput (konsept): pahlı kabuk.
    hood_path = [(-2.945, .66), (-2.945, 1.04), (-2.93, 1.10), (-2.885, 1.15), (-2.80, 1.19), (-2.60, 1.235), (-2.08, 1.30)]
    hood_edge = sweep(body, hood_path, [.05]*6+[.02], ['Paint']*6, thickness=.06)
    body.box((0, -2.951, .92), (.89, .018, .22), 'Trim', 0)
    for z in (.865, .925, .985):
        body.box((0, -2.966, z), (.77, .012, .022), 'Tire', 0)
    body.box((0, -3.001, .52), (.45, .008, .12), 'Paint', 0)
    w152, w154, w230 = windshield_y(1.52), windshield_y(1.54), windshield_y(2.30)
    for sign in (-1, 1):
        # Kaput yanı: bel bandı kaputa döner; kemer bölgesinde levha 1.15'ten başlar.
        y_end = hood_edge[-1][0]
        banded(body, sign, hood_edge+[(y_end, 1.15), (-2.36, 1.15), (-2.36, .66)])
        # Ön/arka kemerler: alt panelin sınırı yayı izler, tekerleği kapatmaz.
        rectangle(body, sign, -2.94, -2.36, .54, .66)
        for ya, yb in ((-1.34, 1.34), (2.36, 2.77)):
            rectangle(body, sign, ya, yb, .54, 1.15)
        for y in AXLES.values():
            a = arc(y, ARCH_R)
            side(body, sign, [(y-ARCH_R, .54)]+a+[(y+ARCH_R, .54), (y+ARCH_R, 1.15), (y-ARCH_R, 1.15)])
            inner, outer = arc(y, ARCH_R-.015), arc(y, ARCH_R+.035)
            for i in range(len(inner)-1):
                side(body, sign, [inner[i], inner[i+1], outer[i+1], outer[i]], 'Paint', 1.0, .06)
        # Kabin yanı: ön cam çizgisinden arkaya, bel bandı + beyaz şerit.
        banded(body, sign, [(y_end, 1.15), (2.77, 1.15), (2.77, 1.52), (w152, 1.52), (y_end, hood_edge[-1][1])])
        # A sütunu ve ön kapı camı. Ön kapı gövdeye birleşiktir.
        side(body, sign, [(w152, 1.52), (w230, 2.30), (w230+.09, 2.30), (w152+.09, 1.52)])
        window(body, glass, sign, [(w154+.09, 1.54), (w230+.09, 2.30), (-1.04, 2.30), (-1.04, 1.54)])
        rectangle(body, sign, -1.03, -.89, 1.52, 2.30)
        rectangle(body, sign, -.89, -.69, 1.52, 2.30)
        rectangle(body, sign, .73, 2.77, 1.52, 2.30)
        rectangle(body, sign, -.69, .73, 2.23, 2.30)
        window(body, glass, sign, [(-.69, 1.52), (.73, 1.52), (.73, 2.23), (-.69, 2.23)])
        body.beam((sign*.986, .02, 1.53), (sign*.986, .02, 2.22), .025, material='Trim')
        # Kapı çizgisi ve kol; yanda başka kapı yok.
        for a, b in (((-1.03, .58), (-1.03, 1.51)), ((-1.03, .58), (-1.36, .58))):
            body.beam((sign*.988, *a), (sign*.988, *b), .009, material='Trim')
        body.box((sign*.995, -1.22, 1.35), (.025, .18, .036), 'Trim', 0)
        body.beam((sign*.98, -1.93, 1.56), (sign*1.16, -1.98, 1.61), .035, material='Trim')
        body.box((sign*1.19, -2.00, 1.70), (.11, .15, .28), 'Trim', .03)
        body.box((sign*1.19, -1.919, 1.70), (.075, .009, .22), 'Glass', 0)
        body.box((sign*.72, -2.951, .92), (.32, .024, .24), 'Headlight', 0)
        body.box((sign*.90, -2.957, .92), (.065, .028, .22), 'Amber', 0)
        # Arka dış dikme: dikey köşe pahı, şeritler köşeyi döner; stoplar ve menteşe yatakları.
        post = [(sign*.918, 2.77), (sign*.985, 2.77), (sign*.985, 2.84), (sign*.955, 2.87), (sign*.918, 2.87)]
        for z0, z1, mat in BANDS:
            z0, z1 = max(z0, .54), min(z1, 2.55)
            body.prism([(x, y, z0) for x, y in post], (0, 0, z1-z0), mat)
        body.box((sign*.948, 2.885, 1.12), (.072, .025, .37), 'Red', 0)
        body.box((sign*.948, 2.901, 1.12), (.074, .012, .08), 'Amber', 0)
    windshield = [(-.94, -2.08, 1.30), (.94, -2.08, 1.30), (.94, w230, 2.30), (-.94, w230, 2.30)]
    glass.prism(windshield, (0, .008, 0), 'Glass')
    for a, b in zip(windshield, windshield[1:]+windshield[:1]):
        body.beam(a, b, .045, material='Trim')
    # Yüksek tavan: ön camın üstünde öne taşan yuvarlak şapka, düz tavan, kavisli arka kenar.
    # İç görünümde yalnız bu vertex grubu gizlenir.
    body.tag = 'roof_hide_for_interior'
    roof_path = [(w230, 2.30), (-1.57, 2.34), (-1.60, 2.38), (-1.615, 2.45), (-1.60, 2.52), (-1.55, 2.585),
                 (-1.46, 2.63), (-1.33, 2.65), (2.70, 2.65), (2.78, 2.643), (2.83, 2.625), (2.86, 2.595), (2.873, 2.55)]
    roof_chamfer = [.01, .03, .045, .06, .06, .06, .06, .06, .06, .055, .045, .03, .01]
    roof_mats = ['Paint', 'Paint', 'Red', 'Red']+['Paint']*8
    roof_edge = sweep(body, roof_path, roof_chamfer, roof_mats)
    body.tag = 'shell'
    for sign in (-1, 1):
        # Tavan yan levhası pah kenarına kadar; ince üst bant şapkanın önüne döner.
        banded(body, sign, [(w230, 2.30)]+roof_edge[1:]+[(2.77, roof_edge[-1][1]), (2.77, 2.30)])
    body.box((0, 2.825, .65), (1.84, .07, .08), 'Trim', 0)
    body.finish()
    glass.finish()


def build_wheels():
    s = WHEEL_R/.39
    profile = [(-.14, .22*s), (-.137, .29*s), (-.105, .36*s), (-.065, WHEEL_R), (.065, WHEEL_R), (.105, .36*s), (.137, .29*s), (.14, .22*s)]
    n = 24
    for end, y in AXLES.items():
        for sign, side_name in ((1, 'L'), (-1, 'R')):
            center = Vector((sign*WHEEL_X, y, WHEEL_R))
            geo = Geo(f'{P}_Wheel_{end}{side_name}')
            rings = [[center+Vector((dx, r*math.cos(2*math.pi*i/n), r*math.sin(2*math.pi*i/n))) for i in range(n)] for dx, r in profile]
            for j in range(len(rings)-1):
                geo.patch(rings[j]+rings[j+1], [(i, (i+1)%n, (i+1)%n+n, i+n) for i in range(n)], 'Tire')
            for dx in (-.14, .14):
                geo.cylinder(center+Vector((dx, 0, 0)), .22*s, .006, 'Rim', (1, 0, 0), n)
                geo.cylinder(center+Vector((dx*1.025, 0, 0)), .12*s, .009, 'Trim', (1, 0, 0), 12)
            geo.finish(center, motion={'type': 'rotate_continuous', 'axis': [1, 0, 0], 'range_deg': None, 'work_pose_deg': 0, 'radius_m': WHEEL_R})


def build_doors():
    for sign, side_name in ((1, 'L'), (-1, 'R')):
        geo = Geo(P+'_Door_R'+side_name)
        def panel(xa, xb, za, zb, mat='Paint', y=2.901, depth=.062):
            geo.box((sign*(xa+xb)/2, y, (za+zb)/2), (xb-xa, depth, zb-za), mat, 0)
        panel(.004, .915, .694, 1.15)
        panel(.004, .915, 1.15, 1.43, 'Red')
        panel(.004, .915, 1.43, 1.53)
        panel(.004, .915, 2.22, 2.38)
        panel(.004, .915, 2.38, 2.52, 'Red')
        panel(.004, .915, 2.52, 2.55)
        panel(.004, .12, 1.53, 2.22)
        panel(.81, .915, 1.53, 2.22)
        panel(.12, .81, 1.53, 2.22, 'Glass', 2.907, .008)
        for xa, xb, za, zb in ((.10,.83,1.51,1.54),(.10,.83,2.21,2.24),(.10,.13,1.54,2.21),(.80,.83,1.54,2.21)):
            panel(xa, xb, za, zb, 'Trim', 2.936, .012)
        geo.box((sign*.17, 2.945, 1.39), (.19, .025, .03), 'Trim', 0)
        for z in (.83, 2.33):
            geo.cylinder((sign*.92, 2.94, z), .025, .12, 'Metal', (0,0,1), 12)
        if sign == 1:
            panel(.25, .69, .81, .94, 'Paint', 2.941, .012)
        geo.finish((sign*.92, 2.94, .694), motion=hinge((0,0,-sign), 0, 260, 100, '0 closed; 100 displayed open; signed axis swings outward.'))
    step = Geo(P+'_Step_Rear')
    step.box((0, 3.0, .48), (1.23, .026, .28), 'Metal', 0)
    for z in (.39, .46, .53, .59):
        step.box((0, 3.016, z), (1.13, .008, .012), 'Trim', 0)
    step.cylinder((0, 3.0, .34), .018, 1.25, 'Trim', (1,0,0), 16)
    step.finish((0,3.0,.34), motion=hinge((-1,0,0),0,90,90,'0 folded upward; +90 along -X opens horizontally rearward.'))


def build_crew_member(geo, x0, y0, z0, s_dir, role):
    geo.tag = role
    skin = 'Skin' if role == 'driver' else 'SkinLight'
    hair = 'HairDark' if role == 'driver' else 'HairBrown'
    lz_floor = .6775 - z0
    pt = lambda lx, ly, lz: (x0+s_dir*lx, y0+s_dir*ly, z0+lz)
    # Kafa ve boyun: chibi oranları (build_patient / build_rescue_crew dili), kafa tepesi minderden ~0.95 m.
    geo.sphere(pt(0, -.060, .685), (.218, .186, .225), skin, 16, 7)
    geo.loft([(pt(0, -.010, lz), .064, .058) for lz in (.415, .495)], skin, 8)
    # Kapalı saç kabuğu: önde kaşların üstünde, ensede daha aşağıda; iç kutupla kapalı hacim.
    n = 14
    verts = [pt(0, -.060, .950)]
    for j in (1, 2, 3):
        for i in range(n):
            phi = 2*math.pi*i/n
            front, rear = max(0, -math.sin(phi)), max(0, math.sin(phi))
            bottom = .715+.115*front-.135*rear
            theta = math.acos((bottom-.700)/.250)*j/3
            verts.append(pt(.224*math.sin(theta)*math.cos(phi),
                            -.060+.192*math.sin(theta)*math.sin(phi),
                            .700+.250*math.cos(theta)))
    verts.append(pt(0, -.060, .660))
    faces = [(0, 1+i, 1+(i+1) % n) for i in range(n)]
    for j in range(2):
        for i in range(n):
            a, b = 1+j*n+i, 1+j*n+(i+1) % n
            faces.append((a, a+n, b+n, b))
    end = len(verts)-1
    faces += [(end, end-1-i, end-1-(i+1) % n) for i in range(n)]
    geo.patch(verts, faces, hair)
    if role == 'driver':
        for s in (-1, 1):
            geo.sphere(pt(s*.205, -.072, .720), (.020, .035, .036), hair, 6, 3)
    else:
        geo.cylinder(pt(0, -.020, .885), .042, .022, 'StripeYellow', segments=8)
        geo.sphere(pt(0, -.020, .930), (.078, .078, .060), hair, 8, 4)
    for s in (-1, 1):
        geo.sphere(pt(s*.212, -.055, .650), (.032, .042, .054), skin, 6, 3)
        geo.sphere(pt(s*.092, -.212, .710), (.088, .065, .098), 'EyeWhite', 12, 5)
        geo.sphere(pt(s*.080, -.270, .705), (.022, .012, .026), 'Pupil', 8, 3)
        geo.sphere(pt(s*.080-.006, -.279, .715), (.005, .003, .006), 'EyeWhite', 4, 2)
        geo.tube([pt(s*.090+t*.056, -.212-.022*(1-t*t), .812+.014*(1-t*t)-s*t*.006) for t in (-1, 0, 1)], .008, hair, 4)
    geo.sphere(pt(0, -.242, .632), (.024, .031, .025), skin, 6, 3)
    geo.tube([pt(t*.035, -.228-.010*(1-t*t), .575+.008*(1-t*t)) for t in (-1, 0, 1)], .0045, 'Mouth', 3)
    # Gövde: koyu yeşil paramedik tulumu + bel hizasında sarı-gri-sarı yansıtıcı şeritler.
    torso_rings = [(pt(0, .020, lz), rx, ry) for lz, rx, ry in (
        (.005, .165, .105), (.110, .172, .108), (.130, .170, .107), (.160, .166, .105),
        (.180, .164, .104), (.360, .170, .102), (.430, .085, .074))]
    geo.loft(torso_rings, ['Suit', 'StripeYellow', 'StripeSilver', 'StripeYellow', 'Suit', 'Suit'], 10)
    sleeve_mats = ['Suit', 'StripeYellow', 'StripeSilver', 'StripeYellow', 'Suit']
    for s in (-1, 1):
        if role == 'driver':
            arm_pts = [pt(s*lx, ly, lz) for lx, ly, lz in (
                (.155, .010, .365), (.182, -.045, .332), (.190, -.075, .315),
                (.195, -.110, .298), (.196, -.140, .284), (.180, -.220, .260))]
            hand_pos, thumb_pos = pt(s*.172, -.260, .255), pt(s*.142, -.232, .278)
        else:
            arm_pts = [pt(s*lx, ly, lz) for lx, ly, lz in (
                (.155, .010, .365), (.182, -.030, .318), (.188, -.060, .285),
                (.188, -.095, .250), (.182, -.125, .220), (.135, -.190, .165))]
            hand_pos, thumb_pos = pt(s*.115, -.225, .155), pt(s*.088, -.215, .166)
        geo.tube(arm_pts, [.052, .050, .048, .046, .045, .040], sleeve_mats, 6)
        geo.sphere(hand_pos, (.036, .044, .034), skin, 8, 4)
        geo.sphere(thumb_pos, (.018, .022, .018), skin, 5, 3)
        leg_pts = [pt(s*.095, ly, lz) for ly, lz in (
            (-.020, .068), (-.245, .068), (-.308, .012), (-.308, lz_floor+.065))]
        geo.tube(leg_pts, [.064, .062, .058, .052], 'Suit', 6)
        geo.sphere(pt(s*.095, -.335, lz_floor+.048), (.066, .092, .045), 'HairDark', 8, 4)


def build_interior():
    geo = Geo(P+'_Interior')
    geo.tag = 'floor'
    geo.box((0, .18, .65), (1.84, 5.30, .055), 'Interior', 0)
    # İki yan yatak; ortada yükleme koridoru bırakılır.
    for sign in (-1, 1):
        x = sign*.61
        geo.tag = 'bed'
        # İnce çerçeve: büyütülmüş arka tekerlek havuzunun üstünde kalır.
        geo.box((x, 1.21, .885), (.57, 2.05, .03), 'Metal', 0)
        geo.box((x, 1.21, .935), (.55, 2.00, .09), 'Interior', .025)
        for y in (.42, 2.0):
            for dx in (-.20, .20):
                geo.cylinder((x+dx, y, .774), .028, .19, 'Metal', segments=12)
        geo.tag = 'floor'
        # Yan iç kaplama tekerlek boşlukları üstünde kalır.
        geo.box((sign*.93, .80, 1.11), (.025, 3.95, .12), 'Interior', 0)
    # Ön iki koltuk (-Y'ye bakar) ve arka refakatçi koltuğu (+Y'ye, hasta bölmesine bakar).
    geo.tag = 'seat'
    for x, y, z, dy in ((.48, -1.44, .99, .20), (-.48, -1.44, .99, .20), (.58, -.33, .83, -.20)):
        geo.box((x, y, z-.12), (.38, .40, .25), 'Trim', .025)
        geo.box((x, y, z+.07), (.48, .48, .14), 'Seat', .035)
        geo.box((x, y+dy, z+.42), (.48, .13, .61), 'Seat', .035)
        geo.box((x, y+dy, z+.81), (.27, .12, .19), 'Seat', .025)
    geo.tag = 'dashboard'
    geo.box((0, -1.90, 1.31), (1.82, .22, .20), 'Seat', .045)
    # Direksiyon: sol sürücü (+X) önünde, torpidodan çıkan mil + simit + 3 kol.
    geo.tag = 'steering'
    sw_center = Vector((.48, -1.70, 1.38))
    sw_axis = Vector((0, -.78, .626)).normalized()
    sw_rot = sw_axis.to_track_quat('Z', 'Y').to_matrix()
    geo.cylinder((.48, -1.75, 1.345), .022, .12, 'Trim', sw_axis, 8)
    geo.cylinder(sw_center, .046, .030, 'Trim', sw_axis, 8)
    geo.torus(sw_center, .125, .016, 'Tire', sw_axis, 12, 5)
    for angle in (-math.pi/3, -2*math.pi/3, math.pi/2):
        spoke_end = sw_center+sw_rot@Vector((.112*math.cos(angle), .112*math.sin(angle), 0))
        geo.beam(sw_center, spoke_end, .020, material='Trim')
    # Oturan chibi şoför ve arka bölme refakatçi koltuğundaki hemşire/paramedik.
    build_crew_member(geo, .48, -1.44, 1.13, 1, 'driver')
    build_crew_member(geo, .58, -.33, .97, -1, 'nurse')
    # Tek kapalı duvar dolabı: baş tarafında, yatak/koridor geçişini kesmez.
    geo.tag = 'cabinet'
    geo.box((-.785, -.33, 1.91), (.27, .62, .47), 'Interior', .025)
    geo.box((-.638, -.33, 1.91), (.018, .58, .43), 'Paint', 0)
    geo.box((-.623, -.12, 1.90), (.018, .025, .11), 'Trim', 0)
    geo.finish()


def build_lights():
    geo = Geo(P+'_Lightbar')
    geo.box((0,-1.12,2.682),(1.40,.25,.064),'Trim',0)
    geo.box((0,-1.12,2.733),(.30,.225,.10),'Trim',.02)
    for sign in (-1,1):
        geo.box((sign*.46,-1.12,2.733),(.58,.235,.12),'Beacon',.025)
    geo.finish((0,-1.12,2.723), motion={'type':'blink','axis':None,'range_deg':None,'note':'Separate object; emission toggled by gameplay.'})
    for sign, label in ((1,'L'),(-1,'R')):
        geo = Geo(P+'_Light_Rear'+label)
        geo.box((sign*.79,2.60,2.672),(.16,.18,.04),'Trim',0)
        geo.cylinder((sign*.79,2.60,2.736),.075,.10,'Beacon',segments=12,radius_top=.055)
        geo.finish((sign*.79,2.60,2.72), motion={'type':'blink','axis':None,'range_deg':None})


def build_stretcher():
    root = empty(S, (0,0,0), None)
    geo = Geo(S+'_Mesh')
    for sign in (-1,1):
        geo.cylinder((sign*.293,0,.13),.018,2.10,'Aluminium',(0,1,0),16)
        for y in (-.97,.97):
            geo.cylinder((sign*.293,y,.13),.022,.16,'Trim',(0,1,0),12)
        for y in (-.66,.66):
            geo.cylinder((sign*.255,y,.055),.015,.11,'Aluminium',segments=12)
    for y in (-.66,.66):
        geo.cylinder((0,y,.090),.013,.55,'Aluminium',(1,0,0),12)
    # İnce çift yüzlü kanvas; hafif enine sarkma gerçek geometri.
    xs = np.linspace(-.275,.275,7)
    ys = np.linspace(-.925,.925,9)
    vertices = [(float(x),float(y),.12-.008*(1-(float(x)/.275)**2)+dz) for dz in (0,-.006) for y in ys for x in xs]
    nx, ny, layer = len(xs),len(ys),len(xs)*len(ys)
    faces=[]
    for j in range(ny-1):
        for i in range(nx-1):
            a=j*nx+i
            faces.extend([(a,a+1,a+nx+1,a+nx),(a+layer+nx,a+layer+nx+1,a+layer+1,a+layer)])
    boundary=list(range(nx))+[j*nx+nx-1 for j in range(1,ny)]+list(range(layer-2,layer-nx-1,-1))+[j*nx for j in range(ny-2,0,-1)]
    for a,b in zip(boundary,boundary[1:]+boundary[:1]):
        faces.append((a,b,b+layer,a+layer))
    geo.patch(vertices,faces,'Canvas')
    geo.finish(parent=S)
    for name, pos in {'Handle_Front':(0,-.97,.13),'Handle_Rear':(0,.97,.13),'PatientAnchor':(0,0,.162)}.items():
        marker = empty(name,pos,S)
        marker.rotation_euler[2]=math.pi
    return root


def set_pose(meta, work):
    for name, part in meta['parts'].items():
        obj=bpy.data.objects[name]
        obj.rotation_mode='AXIS_ANGLE'
        obj.rotation_axis_angle=(0,0,0,1)
        motion=part['motion']
        if work and motion and motion['type']=='hinge':
            obj.rotation_axis_angle=(math.radians(motion['work_pose_deg']),*motion['axis'])
    bpy.context.view_layer.update()


def comparisons(folder):
    concept=image_array(CONCEPT)
    crops={'front':((84,37,373,390),'front'),'rear':((403,37,689,390),'rear'),
           'side':((788,39,1314,392),'left'),'rear_open':((725,389,1300,764),'threequarter_rear_open')}
    for name,((x0,y0,x1,y1),view) in crops.items():
        side_by_side(concept[y0:y1,x0:x1],image_array(folder/(view+'.png')),folder/f'compare_{name}.png')


def render_views(meta):
    folder=OUT/'renders'/f'round_{ROUND:02d}'
    sfolder=STRETCHER_OUT/'renders'/f'round_{ROUND:02d}'
    folder.mkdir(parents=True,exist_ok=True)
    sfolder.mkdir(parents=True,exist_ok=True)
    setup_render()
    stretcher=bpy.data.objects[S]
    smesh=bpy.data.objects[S+'_Mesh']
    smesh.hide_render=True
    durations={}
    views=[('front',(0,-30,1.39),(0,0,1.39),5.65),('rear',(0,30,1.39),(0,0,1.39),5.65),
           ('left',(30,0,1.39),(0,0,1.39),7.3),('right',(-30,0,1.39),(0,0,1.39),7.3),
           ('threequarter_front_left',(13,-17,9),(0,0,1.32),8.0),
           ('threequarter_rear_open',(12,16,9),(0,.8,1.18),9.0),
           ('interior_top',(0,0,15),(0,0,0),7.2),
           ('crew_front',(2.2,-12.0,2.35),(.35,-1.52,1.62),2.35),
           ('crew_rear_open',(-2.4,11.5,2.25),(.20,.60,1.42),3.1)]
    for name,position,target,scale in views:
        set_pose(meta,name in ('threequarter_rear_open','crew_rear_open'))
        smesh.hide_render=name!='threequarter_rear_open'
        stretcher.location=(0,4.18,0)
        body=bpy.data.objects[P+'_Body']
        original=body.data
        markers=[]
        hidden=[]
        if name=='interior_top':
            body.data=interior_roof_mesh()
            hidden=[bpy.data.objects[n] for n in (P+'_Lightbar',P+'_Light_RearL',P+'_Light_RearR')]
            for obj in hidden: obj.hide_render=True
            # Eksenler sadece render sahnesindedir; FBX'e dahil edilmez.
            for slot in ('PatientSlot_01','PatientSlot_02'):
                pos=bpy.data.objects[slot].location
                for axis,mat in (((1,0,0),'Red'),((0,-1,0),'Beacon'),((0,0,1),'Canvas')):
                    geo=Geo('RENDER_'+slot+mat)
                    geo.beam(pos,Vector(pos)+Vector(axis)*.20,.015,material=mat)
                    obj=geo.finish(parent=None)
                    ASSETS.remove(obj)
                    PARTS.pop(obj.name)
                    markers.append(obj)
        render(folder,name,position,target,scale,durations)
        if name=='interior_top':
            temp=body.data
            body.data=original
            bpy.data.meshes.remove(temp)
            for obj in hidden: obj.hide_render=False
            for obj in markers: bpy.data.objects.remove(obj,do_unlink=True)
    set_pose(meta,False)
    smesh.hide_render=True
    before=set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(MINIVAN_FBX),use_anim=False)
    imported=[o for o in bpy.data.objects if o not in before]
    for obj in imported:
        if obj.parent is None: obj.location=(0,-6.30,0)
    render(folder,'scale_with_minivan',(30,-2.8,1.4),(0,-2.8,1.4),13.0,durations)
    for obj in imported: bpy.data.objects.remove(obj,do_unlink=True)
    for obj in ASSETS:
        if obj.type=='MESH' and obj.parent and obj.parent.name==P: obj.hide_render=True
    smesh.hide_render=False
    stretcher.location=(0,0,0)
    render(sfolder,'stretcher_threequarter',(3,-4,3),(0,0,.08),2.9,durations)
    for obj in ASSETS: obj.hide_render=False
    smesh.hide_render=True
    comparisons(folder)
    for source, target in ((folder,OUT/'renders'),(sfolder,STRETCHER_OUT/'renders')):
        for png in source.glob('*.png'): shutil.copy2(png,target/png.name)
    return durations


def export_asset(root, path):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in ASSETS:
        if obj==root or obj.parent==root: obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'EMPTY','MESH'},
        apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',mesh_smooth_type='FACE',
        use_mesh_modifiers=False,bake_anim=False,use_custom_props=True,check_existing=False,add_leaf_bones=False)


def main():
    global ROUND
    parser=argparse.ArgumentParser()
    parser.add_argument('--round',type=int,default=5)
    parser.add_argument('--no-render',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    ROUND=args.round
    started=time.perf_counter()
    for out in (OUT,STRETCHER_OUT): (out/'renders').mkdir(parents=True,exist_ok=True)
    cache=Path(tempfile.gettempdir())/'ambulance_render_cache'
    cache.mkdir(exist_ok=True)
    for name in ('OPTIX_CACHE_PATH','CUDA_CACHE_PATH'): os.environ[name]=str(cache)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.context.preferences.filepaths.use_auto_save_temporary_files=False
    bpy.context.preferences.filepaths.file_preview_type='NONE'
    bpy.context.scene.unit_settings.system='METRIC'
    samples=reference_palette()
    make_materials()
    root=empty(P,(0,0,0),None)
    build_body()
    build_wheels()
    build_doors()
    build_interior()
    build_lights()
    positions={'PatientSlot_01':(.61,1.21,BED_TOP+.05),'PatientSlot_02':(-.61,1.21,BED_TOP+.05),
               'LoadPoint':(0,4.18,0),'SirenPoint':(0,-1.12,2.723)}
    for name,pos in positions.items():
        obj=empty(name,pos,P)
        obj.rotation_euler[2]=math.pi
    sroot=build_stretcher()
    bpy.context.view_layer.update()
    marker_data={n:{'position_m':list(p),'head_direction':[0,-1,0],'local_positive_axis':'+Y','rotation_euler_deg':[0,0,180]} for n,p in positions.items()}
    coords={'up':'+Z','forward':'-Y','left':'+X','right':'-X','length_axis':'Y',
            'marker_direction':'Marker local +Y points toward vehicle front (-Y world). All positions/axes below are Blender root coordinates.'}
    fields={'pivot_m':'Object origin XYZ in metres, rest pose.', 'motion':'Positive angle uses right-hand rule around signed axis; rest=0 deg.',
            'axis':'Unit rotation axis in Blender root coordinates; null for blinking lights.', 'range_deg':'Mechanical min/max angle; null means continuous spin or no mechanical motion.',
            'work_pose_deg':'Open demonstration/test pose: doors 100, step 90.', 'markers':'Empty position, local +Y heading and rest Euler rotation in Blender coordinates.',
            'palette_samples':'5x5 median at top-left-origin source pixel coordinates; assumed colors explicitly labelled.',
            'measurement':'Body envelope excludes mirrors; body height excludes three beacons; ground Z=0.'}
    parts={n:p for n,p in PARTS.items() if p['parent']==P}
    meta={'schema_version':1,'units':'metres','coordinates':coords,'field_descriptions':fields,'parts':parts,'markers':marker_data,
          'measurement':{'length_m':6.0,'width_m':2.0,'height_m':2.65,'beacon_top_max_m':2.8,'tolerance_m':.05,
                         'wheel_radius_m':WHEEL_R,'wheel_x_m':WHEEL_X,'axles_y_m':AXLES,'rear_bumper_max_y_m':2.98},
          'rear_opening':{'x_m':[-.918,.918],'z_m':[.69,2.55],'rear_plane_y_m':2.87,'closed_seam_max_m':.01},
          'beds':{'surface_z_m':BED_TOP,'centres_xy_m':[[.61,1.21],[-.61,1.21]],'length_m':2.0,'width_m':.55},
          'materials':{m.name:'#'+PALETTE[n] for n,m in MATERIALS.items() if n not in ('Canvas','Aluminium')},
          'palette_samples':samples,'palette_reference':str(CONCEPT.relative_to(ROOT)).replace('\\','/'),
          'unity_import':{'scale_factor':1,'bake_axis_conversion':False,'color_note':'Hex sRGB -> linear Base Color -> unchanged FBX DiffuseColor; Linear URP.',
                          'glass_note':'Set transparent URP material using exported alpha 0.3.', 'light_note':'Three separate blue light objects; blink via game code.'}}
    smeta={'schema_version':1,'units':'metres','coordinates':coords,'field_descriptions':fields,
           'parts':{S+'_Mesh':PARTS[S+'_Mesh']},'required_game_instances':2,'asset_note':'Instantiate this same reusable FBX twice; no duplicated geometry asset.',
           'dimensions':{'pole_length_m':2.10,'canvas_width_m':.55,'canvas_length_m':1.85,'canvas_edge_height_m':.12,'canvas_centre_height_m':.112,
                         'handle_spacing_m':.586,'foot_rectangle_m':[.51,1.32]},
           'markers':{n:{'position_m':list(WORLD_PIVOT[n]),'head_direction':[0,-1,0],'local_positive_axis':'+Y','rotation_euler_deg':[0,0,180]} for n in ('Handle_Front','Handle_Rear','PatientAnchor')},
           'materials':{MATERIALS[n].name:'#'+PALETTE[n] for n in ('Canvas','Aluminium','Trim')}}
    (OUT/'ambulance_meta.json').write_text(json.dumps(meta,indent=2),encoding='utf-8')
    (STRETCHER_OUT/'stretcher_meta.json').write_text(json.dumps(smeta,indent=2),encoding='utf-8')
    counts={o.name:len(o.data.polygons) for o in ASSETS if o.type=='MESH'}
    total=sum(v for k,v in counts.items() if k.startswith(P))
    stotal=counts[S+'_Mesh']
    print('TRIANGLES',total,stotal,json.dumps(counts),flush=True)
    if not 6000<=total<=10500: raise ValueError(f'Ambulance triangle budget: {total}')
    if not 600<=stotal<=1000: raise ValueError(f'Stretcher triangle budget: {stotal}')
    digest,canonical=geometry_digest(),canonical_geometry()
    export_asset(root,OUT/(P+'.fbx'))
    export_asset(sroot,STRETCHER_OUT/(S+'.fbx'))
    durations={} if args.no_render else render_views(meta)
    set_pose(meta,False)
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Ambulance.blend'),check_existing=False)
    stats_path=OUT/'stats.json'
    old=json.loads(stats_path.read_text(encoding='utf-8')) if stats_path.exists() else {}
    source=hashlib.sha256(Path(__file__).read_bytes().replace(b'\r\n',b'\n')).hexdigest()
    entry={'geometry_sha256':digest,'canonical_geometry_sha256':canonical,'source_sha256':source,'round':ROUND,
           'triangles':total,'stretcher_triangles':stotal,'render_seconds':round(sum(durations.values()),3),
           'camera_render_seconds':durations,'total_build_seconds':round(time.perf_counter()-started,3)}
    stats={'geometry_sha256':digest,'canonical_geometry_sha256':canonical,'total_triangles':total,'stretcher_triangles':stotal,
           'object_triangles':counts,'last_build':entry,'build_history':(old.get('build_history',[])+[entry])[-10:]}
    stats_path.write_text(json.dumps(stats,indent=2),encoding='utf-8')
    print('BUILD OK',digest,'triangles',total,stotal,'render_seconds',entry['render_seconds'],flush=True)


if __name__=='__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        print('BUILD FAILED',flush=True)
        sys.exit(1)
