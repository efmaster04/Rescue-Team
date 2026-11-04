"""Blender 5.2: Rescue Team çok odalı ofis kabuğu, mobilya FBX'leri, yerleşim JSON'ı ve kontrol render'ları.

Çalıştırma komutu:
  blender -b -P ArtPipeline/tools/blender/office/build_office.py -- --render-dir ArtPipeline/Export/Office/renders
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import sys
import time
import traceback

sys.dont_write_bytecode = True

import bpy
import bmesh
from mathutils import Vector, Matrix

# Proje kök dizini ve varsayılan çıktı yolları
REPO_ROOT = Path(__file__).resolve().parents[4]
ARTPIPELINE_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_OUT = ARTPIPELINE_ROOT / 'Export/Office'
PALETTE_TSV = REPO_ROOT / 'Assets/_Project/Art/material_palette.tsv'
CITY_MAP_REL = 'Assets/_Project/Art/Models/Environment/Environment_OfficeGarage.fbm/O26_city_map.png'
CITY_MAP_PATH = REPO_ROOT / CITY_MAP_REL

def srgb_to_linear(c):
    """sRGB (0..1) kanal değerini doğrusal (linear) uzaya dönüştürür."""
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_to_linear_rgb(hex_str):
    """#RRGGBB sRGB renk kodunu doğrusal (r, g, b) üçlüsüne dönüştürür."""
    h = hex_str.lstrip('#')
    sr, sg, sb = (int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    return (round(srgb_to_linear(sr), 4), round(srgb_to_linear(sg), 4), round(srgb_to_linear(sb), 4))


# İkinci tur sRGB hedef renklerinin doğrusal karşılıkları:
# Duvar sıvası tan #C7A274, alt bant koyu mavi #2C5878, ceviz zemin #94562F,
# süpürgelik/kasa koyu kahve #5E3A20, dolap petrol yeşili #2F6F6A, ekipman vurgu turuncu #E0602B
_PLASTER_RGB = hex_to_linear_rgb('#C7A274')
_PLASTER_LIGHT_RGB = hex_to_linear_rgb('#D4B489')
_WALL_BLUE_RGB = hex_to_linear_rgb('#2C5878')
_MUTED_BLUE_RGB = hex_to_linear_rgb('#38698C')
_WALNUT_RGB = hex_to_linear_rgb('#94562F')
_WALNUT_LIGHT_RGB = hex_to_linear_rgb('#9E5D34')
_WALNUT_DARK_RGB = hex_to_linear_rgb('#8A4F2A')
_DARK_BROWN_RGB = hex_to_linear_rgb('#5E3A20')
_PETROL_GREEN_RGB = hex_to_linear_rgb('#2F6F6A')
_PETROL_LIGHT_RGB = hex_to_linear_rgb('#397F79')
_ACCENT_ORANGE_RGB = hex_to_linear_rgb('#E0602B')

# Palet tablosundan alınan doğrusal (linear) renk ve yüzey değerleri
# Format: (r, g, b, roughness, metallic, alpha)
LINEAR_PALETTE = {
    'M_Plaster': (*_PLASTER_RGB, 0.7600, 0.0000, 1.0),                     # Tan duvar sıvası (#C7A274)
    'M_PlasterLight': (*_PLASTER_LIGHT_RGB, 0.7600, 0.0000, 1.0),          # Açık tan tavan/lento sıvası
    'M_WallBlue': (*_WALL_BLUE_RGB, 0.6800, 0.0600, 1.0),                  # Alt bant koyu mavi (#2C5878)
    'M_MutedBlue': (*_MUTED_BLUE_RGB, 0.7000, 0.0000, 1.0),                # Bant üst çıtası / ikincil mavi
    'M_ServiceBlue': (0.0750, 0.2400, 0.3600, 0.5800, 0.1800, 1.0),        # Doygun servis mavisi
    'M_CobaltBlue': (0.0550, 0.2300, 0.4800, 0.6800, 0.0200, 1.0),         # Kobalt mavi
    'M_RingBlue': (0.0900, 0.3200, 0.6200, 0.6400, 0.0200, 1.0),           # Ring mavi köşe ve halat
    'M_Oak': (*_WALNUT_RGB, 0.6800, 0.0000, 1.0),                          # Ceviz ahşap zemin (#94562F)
    'M_OakLight': (*_WALNUT_LIGHT_RGB, 0.6600, 0.0000, 1.0),               # Ceviz açık ton (az fark)
    'M_OakDark': (0.2100, 0.0950, 0.0400, 0.7400, 0.0000, 1.0),            # Koyu ceviz detay
    'M_Timber': (*_WALNUT_DARK_RGB, 0.7000, 0.0000, 1.0),                  # Ceviz koyu ton (az fark)
    'M_TimberEdge': (*_DARK_BROWN_RGB, 0.6800, 0.0000, 1.0),               # Süpürgelik ve kapı/pencere kasası (#5E3A20)
    'M_Fibreboard': (0.4800, 0.3100, 0.1600, 0.8200, 0.0000, 1.0),         # Sıcak delikli pano / karton
    'M_PegHole': (0.0900, 0.0550, 0.0300, 0.9500, 0.0000, 1.0),            # Pano delik gölgesi
    'M_FilingGreen': (*_PETROL_GREEN_RGB, 0.5600, 0.2400, 1.0),            # Dolaplar petrol yeşili (#2F6F6A)
    'M_TealMetal': (*_PETROL_LIGHT_RGB, 0.5800, 0.2200, 1.0),              # Dolap kapak petrol yeşili
    'M_GalvanizedSteel': (0.3600, 0.3900, 0.3800, 0.5200, 0.5800, 1.0),    # Galvaniz çelik raf
    'M_WarmGrayMetal': (0.3200, 0.3100, 0.2700, 0.4800, 0.4700, 1.0),      # Sıcak gri metal
    'M_SilverSteel': (0.4100, 0.4000, 0.3600, 0.5500, 0.3500, 1.0),        # Boyalı çelik
    'M_ToolSteel': (0.5200, 0.5800, 0.6000, 0.3200, 0.7600, 1.0),          # Parlak takım çeliği
    'M_Anthracite': (0.0680, 0.0880, 0.1020, 0.4800, 0.4200, 1.0),         # Antrasit metal iskelet
    'M_CharcoalTrim': (0.0950, 0.0920, 0.0860, 0.6000, 0.0400, 1.0),       # Koyu gri baza
    'M_CharcoalABS': (0.0320, 0.0360, 0.0400, 0.6800, 0.0000, 1.0),        # Koyu plastik
    'M_DarkRubber': (0.0420, 0.0410, 0.0380, 0.9200, 0.0000, 1.0),         # Siyah kauçuk / kablo
    'M_ToolboxRed': (0.6200, 0.0600, 0.0420, 0.3800, 0.2800, 1.0),         # Doygun takım çantası kırmızısı
    'M_ToolboxLipRed': (0.8200, 0.1100, 0.0750, 0.4200, 0.2200, 1.0),      # Ring kırmızı köşe ve vurgu kırmızısı
    'M_SafetyOrange': (*_ACCENT_ORANGE_RGB, 0.6200, 0.0400, 1.0),          # Ekipman vurguları turuncu (#E0602B)
    'M_SafetyOchre': (0.7800, 0.4400, 0.0750, 0.5600, 0.1800, 1.0),        # Sıcak sarı-okre vurgu
    'M_BeigeFabric': (0.5600, 0.4800, 0.3700, 0.8200, 0.0000, 1.0),        # Sıcak kum/karamel kanepe kumaşı
    'M_BeigeCushion': (0.6500, 0.5600, 0.4400, 0.8400, 0.0000, 1.0),       # Sıcak minder kumaşı
    'M_SoftBeige': (0.3600, 0.3000, 0.2300, 0.7200, 0.0000, 1.0),          # Çekmece önü sıcak bej
    'M_FridgeWhite': (0.8200, 0.7900, 0.7200, 0.4600, 0.0500, 1.0),        # Kırık sıcak beyaz
    'M_CreamCeramic': (0.7800, 0.7400, 0.6600, 0.2600, 0.0000, 1.0),       # Krem seramik
    'M_CreamPaper': (0.7900, 0.7200, 0.5800, 0.7400, 0.0000, 1.0),         # Sıcak kağıt
    'M_InkGray': (0.2000, 0.2600, 0.2800, 0.7200, 0.0000, 1.0),            # Mürekkep grisi
    'M_Coffee': (0.1100, 0.0520, 0.0220, 0.2800, 0.0000, 1.0),             # Kahve
    'M_Terracotta': (0.5400, 0.2600, 0.1300, 0.8000, 0.0000, 1.0),         # Terrakota saksı
    'M_PottingSoil': (0.0950, 0.0650, 0.0380, 0.9600, 0.0000, 1.0),        # Saksı toprağı
    'M_FoliageDeep': (0.1300, 0.2600, 0.1200, 0.7000, 0.0000, 1.0),        # Koyu yaprak yeşili
    'M_FoliageOlive': (0.2200, 0.3500, 0.1200, 0.7000, 0.0000, 1.0),       # Zeytin yaprak yeşili
    'M_FoliageSunlit': (0.3600, 0.4800, 0.1600, 0.7000, 0.0000, 1.0),      # Canlı yaprak yeşili
    'M_NavyCanvas': (0.0900, 0.1900, 0.2600, 0.8600, 0.0000, 1.0),         # Lacivert kanvas kumaş
    'M_ReflectiveTrim': (0.6800, 0.7200, 0.6600, 0.4200, 0.2500, 1.0),     # Reflektör şerit
    'M_ScreenDark': (0.0250, 0.0400, 0.0480, 0.1400, 0.1800, 1.0),         # Koyu ekran camı
    'M_LedGreen': (0.0300, 0.7000, 0.1600, 0.3500, 0.0000, 1.0),           # Durum LED yeşili
    'M_LightDiffuser': (0.9500, 0.7300, 0.3800, 0.2800, 0.0000, 1.0),      # Sıcak tavan armatür difüzörü
    'M_Concrete': (0.4200, 0.3900, 0.3400, 0.7600, 0.0000, 1.0),           # Alt beton plak
    'M_SmokyGlass': (0.1800, 0.3200, 0.3500, 0.1800, 0.0800, 1.0),         # Karaf/sebil yarı saydam mavi
    'M_MauveHill': (0.4800, 0.4100, 0.4600, 0.7200, 0.0000, 1.0),          # Poster/tablo mor tepe
    'M_CityMap': (0.8000, 0.7300, 0.5900, 0.9200, 0.0000, 1.0),            # Şehir haritası tabanı
    'Glass': (0.5800, 0.7400, 0.8000, 0.0800, 0.0500, 0.32),               # Pencere camı materyali ("Glass")
}

MATERIALS = {}
CITY_MAP_FOUND = False

# MetalShelf raf yükseklikleri (zemin y=0'dan metre, en üst raf <= 1.45 m)
METAL_SHELF_HEIGHTS = [0.12, 0.55, 0.98, 1.40]


def u2b_point(ux, uy, uz):
    """Unity (x, y, z) koordinatını Blender (x, y, z) koordinatına dönüştürür."""
    return Vector((float(ux), float(uz), float(uy)))


def u2b_size(sx, sy, sz):
    """Unity (x, y, z) boyutunu Blender (x, y, z) boyutuna dönüştürür."""
    return (float(sx), float(sz), float(sy))


def make_materials():
    """Doğrusal renk paletinden Blender materyallerini oluşturur; O26_city_map varsa referans bağlar."""
    global CITY_MAP_FOUND
    MATERIALS.clear()
    for name, (r, g, b, rough, metal, alpha) in LINEAR_PALETTE.items():
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Base Color'].default_value = (r, g, b, alpha)
        bsdf.inputs['Roughness'].default_value = rough
        bsdf.inputs['Metallic'].default_value = metal
        if 'Specular IOR Level' in bsdf.inputs:
            bsdf.inputs['Specular IOR Level'].default_value = 0.25
        if name == 'M_LightDiffuser' and 'Emission Color' in bsdf.inputs:
            bsdf.inputs['Emission Color'].default_value = (r, g, b, 1.0)
            bsdf.inputs['Emission Strength'].default_value = 1.6
        if name == 'Glass':
            if 'Alpha' in bsdf.inputs:
                bsdf.inputs['Alpha'].default_value = alpha
            if 'Transmission Weight' in bsdf.inputs:
                bsdf.inputs['Transmission Weight'].default_value = 0.78
            if hasattr(mat, 'blend_method'):
                mat.blend_method = 'BLEND'
        mat.diffuse_color = (r, g, b, alpha)
        MATERIALS[name] = mat

    # CityMapBoard dokusu varsa kopyalamadan doğrudan dosya yolundan referans ver
    if CITY_MAP_PATH.is_file():
        CITY_MAP_FOUND = True
        mat = MATERIALS['M_CityMap']
        nodes = mat.node_tree.nodes
        links = mat.node_tree.links
        bsdf = nodes.get('Principled BSDF')
        tex = nodes.new('ShaderNodeTexImage')
        img = bpy.data.images.load(str(CITY_MAP_PATH), check_existing=True)
        tex.image = img
        links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    else:
        CITY_MAP_FOUND = False


class Geo:
    """Bmesh tabanlı, düz gölgelendirmeli (flat shaded) ve otomatik UV'li low-poly geometri üretici."""

    def __init__(self, name):
        self.name = name
        self.vertices = []
        self.faces = []
        self.materials = []
        self.face_uvs = []

    def patch(self, vertices, faces, material='M_Plaster', uvs=None):
        offset = len(self.vertices)
        self.vertices.extend(tuple(v) for v in vertices)
        self.faces.extend(tuple(offset + i for i in f) for f in faces)
        self.materials.extend([material] * len(faces))
        if uvs is not None:
            self.face_uvs.extend(uvs)
        else:
            self.face_uvs.extend([None] * len(faces))

    def box_u(self, center_u, size_u, material='M_Plaster', bevel=0.012, segments=1, rot_y_deg=0.0, rot_x_deg=0.0, rot_z_deg=0.0):
        """Unity (x, y, z) merkez ve boyutlarıyla kutu ekler."""
        center = u2b_point(*center_u)
        size = u2b_size(*size_u)
        if min(size) < 0.025:
            bevel = 0.0
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts:
            v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        if bevel > 0.0:
            bmesh.ops.bevel(
                bm,
                geom=list(bm.edges),
                offset=min(bevel, min(size) * 0.35),
                segments=segments,
                affect='EDGES',
                profile=0.5,
            )
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.verts.ensure_lookup_table()
        bm.verts.index_update()
        rot = Matrix.Identity(3)
        if rot_x_deg:
            rot = Matrix.Rotation(math.radians(rot_x_deg), 3, 'X') @ rot
        if rot_z_deg:
            rot = Matrix.Rotation(math.radians(rot_z_deg), 3, 'Y') @ rot
        if rot_y_deg:
            rot = Matrix.Rotation(-math.radians(rot_y_deg), 3, 'Z') @ rot
        self.patch(
            [rot @ v.co + center for v in bm.verts],
            [[v.index for v in f.verts] for f in bm.faces],
            material,
        )
        bm.free()

    def span_u(self, x0, y0, z0, x1, y1, z1, material='M_Plaster', bevel=0.008):
        """Unity min-max sınırlarıyla eksen hizalı kutu ekler."""
        cx, cy, cz = (x0 + x1) * 0.5, (y0 + y1) * 0.5, (z0 + z1) * 0.5
        sx, sy, sz = abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)
        if sx < 1e-4 or sy < 1e-4 or sz < 1e-4:
            return
        self.box_u((cx, cy, cz), (sx, sy, sz), material=material, bevel=bevel)

    def cylinder_u(self, center_u, radius, height, material='M_ToolSteel', axis_u=(0, 1, 0), segments=12):
        """Unity koordinatlarında silindir ekler."""
        center = u2b_point(*center_u)
        axis_b = Vector((axis_u[0], axis_u[2], axis_u[1])).normalized()
        rot = axis_b.to_track_quat('Z', 'Y').to_matrix()
        verts = [
            rot @ Vector((radius * math.cos(2 * math.pi * i / segments),
                          radius * math.sin(2 * math.pi * i / segments),
                          z)) + center
            for z in (-height * 0.5, height * 0.5)
            for i in range(segments)
        ]
        faces = [tuple(reversed(range(segments))), tuple(range(segments, 2 * segments))]
        faces += [(i, (i + 1) % segments, (i + 1) % segments + segments, i + segments) for i in range(segments)]
        self.patch(verts, faces, material)

    def quad_u(self, corners_u, material='M_CityMap', uvs=None):
        """Unity uzayında verilen 4 köşe noktasından tek yüz (ör. harita yüzeyi) oluşturur."""
        verts = [u2b_point(*p) for p in corners_u]
        face_uv = [uvs] if uvs else None
        self.patch(verts, [(0, 1, 2, 3)], material, uvs=face_uv)

    def finish(self, pivot_u=(0.0, 0.0, 0.0)):
        """Mesh ve nesneyi oluşturur, kullanılan materyalleri ve kutu UV'lerini yazar, üçgenler."""
        pivot = u2b_point(*pivot_u)
        mesh = bpy.data.meshes.new(self.name)
        local_verts = [Vector(v) - pivot for v in self.vertices]
        mesh.from_pydata(local_verts, [], self.faces)

        used_names = []
        for mat_name in self.materials:
            if mat_name not in used_names:
                used_names.append(mat_name)
        for mat_name in used_names:
            mesh.materials.append(MATERIALS[mat_name])
        for poly, mat_name in zip(mesh.polygons, self.materials):
            poly.material_index = used_names.index(mat_name)
            poly.use_smooth = False

        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        uv_layer = bm.loops.layers.uv.new('UVMap')
        bm.faces.ensure_lookup_table()
        for f_idx, face in enumerate(bm.faces):
            explicit_uv = self.face_uvs[f_idx] if f_idx < len(self.face_uvs) else None
            if explicit_uv and len(explicit_uv) == len(face.loops):
                for loop, uv in zip(face.loops, explicit_uv):
                    loop[uv_layer].uv = uv
            else:
                n = face.normal
                ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
                mat_name = used_names[face.material_index] if face.material_index < len(used_names) else ''
                for loop in face.loops:
                    co = loop.vert.co
                    if mat_name == 'M_CityMap':
                        # Harita dokusunu pano ön yüzüne (x: [-0.635, 0.635], z: [0.045, 0.835]) tam oturt
                        u = 0.5 - (co.x / 1.27)
                        v = (co.z - 0.045) / 0.79
                        loop[uv_layer].uv = (max(0.0, min(1.0, u)), max(0.0, min(1.0, v)))
                    elif az >= ax and az >= ay:
                        loop[uv_layer].uv = (co.x * 0.5 + 0.5, co.y * 0.5 + 0.5)
                    elif ax >= ay:
                        loop[uv_layer].uv = (co.y * 0.5 + 0.5, co.z * 0.5 + 0.5)
                    else:
                        loop[uv_layer].uv = (co.x * 0.5 + 0.5, co.z * 0.5 + 0.5)

        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='FIXED', ngon_method='EAR_CLIP')
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()

        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.location = pivot
        return obj


# ==============================================================================
# 1. ODA KABUKLARI (Room_<Ad>_Floor / Walls / Ceiling + Tavan Armatürleri)
# ==============================================================================

ROOMS_SPEC = [
    {
        'id': 'Office',
        'name_tr': 'Ana Ofis',
        'x0': -4.8, 'x1': 0.0,
        'z0': -5.2, 'z1': 1.2,
        'lights': [(-2.4, -3.4), (-2.4, -0.6)],
    },
    {
        'id': 'Kitchen',
        'name_tr': 'Mutfak',
        'x0': -9.8, 'x1': -4.8,
        'z0': -5.2, 'z1': 1.2,
        'lights': [(-7.3, -3.4), (-7.3, -0.6)],
    },
    {
        'id': 'Storage',
        'name_tr': 'Depo',
        'x0': -4.8, 'x1': 0.0,
        'z0': 1.2, 'z1': 5.2,
        'lights': [(-2.4, 3.2)],
    },
    {
        'id': 'LockerRoom',
        'name_tr': 'Soyunma',
        'x0': -9.8, 'x1': -4.8,
        'z0': 1.2, 'z1': 5.2,
        'lights': [(-7.3, 3.2)],
    },
    {
        'id': 'RingRoom',
        'name_tr': 'Ring Odası',
        'x0': -9.8, 'x1': 0.0,
        'z0': 5.2, 'z1': 12.0,
        'lights': [(-7.0, 8.6), (-2.8, 8.6)],
    },
]


def add_layered_wall_x(geo, z0, z1, x_min, x_max, y_top=3.24, openings=None, trim_side=0):
    """Z ekseni boyunca uzanan duvar parçasını alt 0.96 m mavi bant, üst sıva ve kapı/pencere boşluklarıyla üretir."""
    openings = sorted(openings or [], key=lambda o: o[0])
    points = [z0]
    for o in openings:
        points.extend([max(z0, o[0]), min(z1, o[1])])
    points.append(z1)
    points = sorted(set(round(p, 4) for p in points if z0 <= p <= z1))

    for a, b in zip(points, points[1:]):
        if b - a < 1e-3:
            continue
        op = next((o for o in openings if a >= o[0] - 1e-3 and b <= o[1] + 1e-3), None)
        if op is None:
            # Tam duvar: 0..0.96 mavi bant, 0.96..3.16 sıva, 3.16..y_top üst lento
            geo.span_u(x_min, 0.00, a, x_max, 0.96, b, 'M_WallBlue', bevel=0.004)
            geo.span_u(x_min, 0.96, a, x_max, 3.16, b, 'M_Plaster', bevel=0.004)
            if y_top > 3.16:
                geo.span_u(x_min, 3.16, a, x_max, y_top, b, 'M_PlasterLight', bevel=0.003)
            if trim_side != 0:
                tx0 = x_max if trim_side > 0 else x_min - 0.012
                tx1 = x_max + 0.012 if trim_side > 0 else x_min
                geo.span_u(tx0, 0.00, a, tx1, 0.08, b, 'M_TimberEdge', bevel=0.002)
                geo.span_u(tx0, 0.95, a, tx1, 0.98, b, 'M_MutedBlue', bevel=0.002)
        elif len(op) == 3:
            # Kapı üstü lento (door_h .. y_top)
            door_h = op[2]
            geo.span_u(x_min, door_h, a, x_max, 3.16 if y_top <= 3.3 else y_top, b, 'M_Plaster', bevel=0.004)
            if y_top > 3.16 and y_top <= 3.3:
                geo.span_u(x_min, 3.16, a, x_max, y_top, b, 'M_PlasterLight', bevel=0.003)
        else:
            # Pencere boşluğu: (oz0, oz1, wy0, wy1) -> alt duvar (0..wy0) ve üst duvar (wy1..y_top)
            _, _, wy0, wy1 = op
            if wy0 > 0.0:
                geo.span_u(x_min, 0.00, a, x_max, min(wy0, 0.96), b, 'M_WallBlue', bevel=0.004)
                if wy0 > 0.96:
                    geo.span_u(x_min, 0.96, a, x_max, wy0, b, 'M_Plaster', bevel=0.004)
                if trim_side != 0:
                    tx0 = x_max if trim_side > 0 else x_min - 0.012
                    tx1 = x_max + 0.012 if trim_side > 0 else x_min
                    geo.span_u(tx0, 0.00, a, tx1, 0.08, b, 'M_TimberEdge', bevel=0.002)
            geo.span_u(x_min, wy1, a, x_max, 3.16, b, 'M_Plaster', bevel=0.004)
            if y_top > 3.16:
                geo.span_u(x_min, 3.16, a, x_max, y_top, b, 'M_PlasterLight', bevel=0.003)


def add_layered_wall_z(geo, x0, x1, z_min, z_max, y_top=3.24, openings=None, trim_side=0):
    """X ekseni boyunca uzanan duvar parçasını alt 0.96 m mavi bant, üst sıva ve kapı/pencere boşluklarıyla üretir."""
    openings = sorted(openings or [], key=lambda o: o[0])
    points = [x0]
    for o in openings:
        points.extend([max(x0, o[0]), min(x1, o[1])])
    points.append(x1)
    points = sorted(set(round(p, 4) for p in points if x0 <= p <= x1))

    for a, b in zip(points, points[1:]):
        if b - a < 1e-3:
            continue
        op = next((o for o in openings if a >= o[0] - 1e-3 and b <= o[1] + 1e-3), None)
        if op is None:
            geo.span_u(a, 0.00, z_min, b, 0.96, z_max, 'M_WallBlue', bevel=0.004)
            geo.span_u(a, 0.96, z_min, b, 3.16, z_max, 'M_Plaster', bevel=0.004)
            if y_top > 3.16:
                geo.span_u(a, 3.16, z_min, b, y_top, z_max, 'M_PlasterLight', bevel=0.003)
            if trim_side != 0:
                tz0 = z_max if trim_side > 0 else z_min - 0.012
                tz1 = z_max + 0.012 if trim_side > 0 else z_min
                geo.span_u(a, 0.00, tz0, b, 0.08, tz1, 'M_TimberEdge', bevel=0.002)
                geo.span_u(a, 0.95, tz0, b, 0.98, tz1, 'M_MutedBlue', bevel=0.002)
        elif len(op) == 3:
            door_h = op[2]
            geo.span_u(a, door_h, z_min, b, 3.16, z_max, 'M_Plaster', bevel=0.004)
            if y_top > 3.16:
                geo.span_u(a, 3.16, z_min, b, y_top, z_max, 'M_PlasterLight', bevel=0.003)
        else:
            # Pencere boşluğu: (ox0, ox1, wy0, wy1) -> alt duvar (0..wy0) ve üst duvar (wy1..y_top)
            _, _, wy0, wy1 = op
            if wy0 > 0.0:
                geo.span_u(a, 0.00, z_min, b, min(wy0, 0.96), z_max, 'M_WallBlue', bevel=0.004)
                if wy0 > 0.96:
                    geo.span_u(a, 0.96, z_min, b, wy0, z_max, 'M_Plaster', bevel=0.004)
                if trim_side != 0:
                    tz0 = z_max if trim_side > 0 else z_min - 0.012
                    tz1 = z_max + 0.012 if trim_side > 0 else z_min
                    geo.span_u(a, 0.00, tz0, b, 0.08, tz1, 'M_TimberEdge', bevel=0.002)
            geo.span_u(a, wy1, z_min, b, 3.16, z_max, 'M_Plaster', bevel=0.004)
            if y_top > 3.16:
                geo.span_u(a, 3.16, z_min, b, y_top, z_max, 'M_PlasterLight', bevel=0.003)


def add_door_frame_x(geo, x_center, half_thick, z0, z1, height, frame_w=0.055, mat='M_TimberEdge'):
    """X-sabit bir duvardaki kapı boşluğuna kanatsız kasa (sol/sağ dikme + üst başlık) ekler."""
    xm, xp = x_center - half_thick - 0.015, x_center + half_thick + 0.015
    geo.span_u(xm, 0.0, z0 - frame_w, xp, height + frame_w, z0, mat, bevel=0.006)
    geo.span_u(xm, 0.0, z1, xp, height + frame_w, z1 + frame_w, mat, bevel=0.006)
    geo.span_u(xm, height, z0, xp, height + frame_w, z1, mat, bevel=0.006)


def add_door_frame_z(geo, z_center, half_thick, x0, x1, height, frame_w=0.055, mat='M_TimberEdge'):
    """Z-sabit bir duvardaki kapı boşluğuna kanatsız kasa (sol/sağ dikme + üst başlık) ekler."""
    zm, zp = z_center - half_thick - 0.015, z_center + half_thick + 0.015
    geo.span_u(x0 - frame_w, 0.0, zm, x0, height + frame_w, zp, mat, bevel=0.006)
    geo.span_u(x1, 0.0, zm, x1 + frame_w, height + frame_w, zp, mat, bevel=0.006)
    geo.span_u(x0, height, zm, x1, height + frame_w, zp, mat, bevel=0.006)


def add_window_frame_x(geo, x_min, x_max, z0, z1, y0=0.90, y1=2.10, inside_dir=1):
    """X-sabit dış duvardaki pencere boşluğuna koyu kahve kasa, orta kayıt ve iç denizlik ekler."""
    fw = 0.055
    xm, xp = x_min - 0.012, x_max + 0.012
    # Yan dikmeler ve üst başlık
    geo.span_u(xm, y0, z0, xp, y1, z0 + fw, 'M_TimberEdge', bevel=0.005)
    geo.span_u(xm, y0, z1 - fw, xp, y1, z1, 'M_TimberEdge', bevel=0.005)
    geo.span_u(xm, y1 - fw, z0 + fw, xp, y1, z1 - fw, 'M_TimberEdge', bevel=0.005)
    # Orta dikey kayıt
    zm = (z0 + z1) * 0.5
    geo.span_u(xm + 0.02, y0 + 0.04, zm - 0.02, xp - 0.02, y1 - fw, zm + 0.02, 'M_TimberEdge', bevel=0.004)
    # Denizlik (y≈0.90, odaya doğru hafif çıkıntılı)
    sill_x0 = x_min - 0.025 if inside_dir < 0 else x_min - 0.018
    sill_x1 = x_max + 0.065 if inside_dir > 0 else x_max + 0.018
    if inside_dir < 0:
        sill_x0 = x_min - 0.065
    geo.span_u(sill_x0, y0 - 0.02, z0 - 0.04, sill_x1, y0 + 0.035, z1 + 0.04, 'M_TimberEdge', bevel=0.006)


def add_window_frame_z(geo, z_min, z_max, x0, x1, y0=0.90, y1=2.10, inside_dir=1):
    """Z-sabit dış duvardaki pencere boşluğuna koyu kahve kasa, orta kayıt ve iç denizlik ekler."""
    fw = 0.055
    zm, zp = z_min - 0.012, z_max + 0.012
    # Yan dikmeler ve üst başlık
    geo.span_u(x0, y0, zm, x0 + fw, y1, zp, 'M_TimberEdge', bevel=0.005)
    geo.span_u(x1 - fw, y0, zm, x1, y1, zp, 'M_TimberEdge', bevel=0.005)
    geo.span_u(x0 + fw, y1 - fw, zm, x1 - fw, y1, zp, 'M_TimberEdge', bevel=0.005)
    # Orta dikey kayıt
    xm = (x0 + x1) * 0.5
    geo.span_u(xm - 0.02, y0 + 0.04, zm + 0.02, xm + 0.02, y1 - fw, zp - 0.02, 'M_TimberEdge', bevel=0.004)
    # Denizlik (y≈0.90, odaya doğru hafif çıkıntılı)
    sill_z0 = z_min - 0.065 if inside_dir < 0 else z_min - 0.018
    sill_z1 = z_max + 0.065 if inside_dir > 0 else z_max + 0.018
    geo.span_u(x0 - 0.04, y0 - 0.02, sill_z0, x1 + 0.04, y0 + 0.035, sill_z1, 'M_TimberEdge', bevel=0.006)


def build_room_floor(room):
    """Oda için 0.18-0.25 m genişliğinde, renk farkı az, ucu ucuna eklenmiş ceviz tahta zemin üretir."""
    geo = Geo(f"Room_{room['id']}_Floor")
    x0, x1, z0, z1 = room['x0'], room['x1'], room['z0'], room['z1']
    # Ana döşeme plağı (üst yüzey y=-0.008)
    geo.span_u(x0, -0.12, z0, x1, -0.008, z1, 'M_Concrete', bevel=0.0)
    # 0.21 m hedef genişlikte (0.18–0.25 m aralığında), ucu ucuna şaşırtmalı dizilmiş ceviz parke tahtaları
    target_w = 0.21
    n_cols = max(1, round((x1 - x0) / target_w))
    col_w = (x1 - x0) / n_cols
    plank_mats = ('M_Oak', 'M_OakLight', 'M_Timber')
    z_len = z1 - z0
    for c in range(n_cols):
        px0 = x0 + c * col_w + 0.0012
        px1 = x0 + (c + 1) * col_w - 0.0012
        # Her sırada uç ek yerlerini şaşırtmak için başlangıç ofseti
        seg_base = 1.35
        offset = ((c * 37) % 100) / 100.0 * 0.85
        z_pts = [z0 + 0.002]
        cur_z = z0 + 0.55 + offset
        while cur_z < z1 - 0.45:
            z_pts.append(round(cur_z, 4))
            step_z = seg_base + (((c * 13 + len(z_pts) * 7) % 5) - 2) * 0.12
            cur_z += max(0.85, step_z)
        z_pts.append(z1 - 0.002)
        for r_idx, (pz0, pz1) in enumerate(zip(z_pts, z_pts[1:])):
            mat_idx = (c * 5 + r_idx * 3 + len(room['id'])) % len(plank_mats)
            geo.span_u(px0, -0.008, pz0 + 0.001, px1, 0.0, pz1 - 0.001, plank_mats[mat_idx], bevel=0.0015)
    return geo.finish()


def build_room_ceiling(room):
    """Oda için y=3.24..3.39 tavan döşemesini üretir."""
    geo = Geo(f"Room_{room['id']}_Ceiling")
    x0, x1, z0, z1 = room['x0'], room['x1'], room['z0'], room['z1']
    geo.span_u(x0, 3.24, z0, x1, 3.39, z1, 'M_PlasterLight', bevel=0.004)
    return geo.finish()


def build_room_fixtures(room):
    """Oda için tavan aydınlatma armatürlerini (Room_<Ad>_Fixtures) üretir."""
    geo = Geo(f"Room_{room['id']}_Fixtures")
    for lx, lz in room['lights']:
        # Metal armatür gövdesi ve sıcak ışık difüzörü
        geo.box_u((lx, 3.215, lz), (0.32, 0.05, 1.25), 'M_Anthracite', bevel=0.008)
        geo.box_u((lx, 3.185, lz), (0.24, 0.02, 1.16), 'M_LightDiffuser', bevel=0.004)
    return geo.finish()


def build_room_walls(room_id):
    """Her odanın duvarlarını, kapı/pencere boşluklarını, kasalarını ve özel x=0 geçişlerini üretir."""
    geo = Geo(f'Room_{room_id}_Walls')

    if room_id == 'Office':
        # Güney dış duvar (z=-5.2): ön kapı x[-3.72, -2.64] ve pencere x[-2.00, -0.40] (w=1.60, y=0.90..2.10)
        add_layered_wall_z(
            geo, -4.8, 0.0, -5.42, -5.18, y_top=3.24,
            openings=[(-3.72, -2.64, 2.25), (-2.00, -0.40, 0.90, 2.10)],
            trim_side=1,
        )
        add_window_frame_z(geo, -5.42, -5.18, -2.00, -0.40, y0=0.90, y1=2.10, inside_dir=1)
        # Batı iç duvar (x=-4.8, ofis yüzü [-4.80, -4.72]): ofis-mutfak kapısı z[-2.6, -1.4], y<=2.18
        add_layered_wall_x(geo, -5.2, 1.2, -4.80, -4.72, y_top=3.24,
                           openings=[(-2.60, -1.40, 2.18)], trim_side=1)
        add_door_frame_x(geo, -4.80, 0.08, -2.60, -1.40, 2.18)
        # Kuzey iç duvar (z=1.2, ofis yüzü [1.12, 1.20]): ofis-depo kapısı x[-3.0, -1.8], y<=2.18
        add_layered_wall_z(geo, -4.8, 0.0, 1.12, 1.20, y_top=3.24,
                           openings=[(-3.00, -1.80, 2.18)], trim_side=-1)
        add_door_frame_z(geo, 1.20, 0.08, -3.00, -1.80, 2.18)
        # Doğu duvarı (x=0): garaj FBX'inde kalır; ofis tarafına x=-0.08 civarı kaplama yüzey + garaj kapısı boşluğu z[-3.4, -2.36], y<=2.18
        add_layered_wall_x(geo, -5.2, 1.2, -0.095, -0.065, y_top=3.24,
                           openings=[(-3.40, -2.36, 2.18)], trim_side=-1)

    elif room_id == 'Kitchen':
        # Güney dış duvar (z=-5.2)
        add_layered_wall_z(geo, -9.8, -4.8, -5.44, -5.20, y_top=3.24, trim_side=1)
        # Batı dış duvar (x=-9.8): pencere z[-3.80, -2.20] (w=1.60, y=0.90..2.10)
        add_layered_wall_x(
            geo, -5.2, 1.2, -10.04, -9.80, y_top=3.24,
            openings=[(-3.80, -2.20, 0.90, 2.10)],
            trim_side=1,
        )
        add_window_frame_x(geo, -10.04, -9.80, -3.80, -2.20, y0=0.90, y1=2.10, inside_dir=1)
        # Doğu iç duvar (x=-4.8, mutfak yüzü [-4.88, -4.80]): ofis-mutfak kapısı z[-2.6, -1.4]
        add_layered_wall_x(geo, -5.2, 1.2, -4.88, -4.80, y_top=3.24,
                           openings=[(-2.60, -1.40, 2.18)], trim_side=-1)
        # Kuzey iç duvar (z=1.2, mutfak yüzü [1.12, 1.20]): mutfak-soyunma kapısı x[-7.9, -6.7]
        add_layered_wall_z(geo, -9.8, -4.8, 1.12, 1.20, y_top=3.24,
                           openings=[(-7.90, -6.70, 2.18)], trim_side=-1)
        add_door_frame_z(geo, 1.20, 0.08, -7.90, -6.70, 2.18)

    elif room_id == 'Storage':
        # Depoda pencere yok
        # Güney iç duvar (z=1.2, depo yüzü [1.20, 1.28]): ofis-depo kapısı x[-3.0, -1.8]
        add_layered_wall_z(geo, -4.8, 0.0, 1.20, 1.28, y_top=3.24,
                           openings=[(-3.00, -1.80, 2.18)], trim_side=1)
        # Batı iç duvar (x=-4.8, depo yüzü [-4.80, -4.72])
        add_layered_wall_x(geo, 1.2, 5.2, -4.80, -4.72, y_top=3.24, trim_side=1)
        # Kuzey iç duvar (z=5.2, depo yüzü [5.12, 5.20])
        add_layered_wall_z(geo, -4.8, 0.0, 5.12, 5.20, y_top=3.24, trim_side=-1)
        # Doğu duvarı (x=0): z[1.2, 1.4] bağlantı + x∈[-0.12, 0.12] z[1.4, 5.2] y<=4.0 iki yüzünde mavi bantlı duvar, z[1.6, 3.6] y<=2.5 geniş geçiş
        add_layered_wall_x(geo, 1.2, 1.4, -0.12, 0.00, y_top=3.24, trim_side=-1)
        for z_a, z_b in [(1.4, 1.6), (3.6, 5.2)]:
            geo.span_u(-0.12, 0.00, z_a, 0.12, 0.96, z_b, 'M_WallBlue', bevel=0.004)
            geo.span_u(-0.12, 0.96, z_a, 0.12, 4.00, z_b, 'M_Plaster', bevel=0.004)
            for sx in (-0.126, 0.120):
                geo.span_u(sx, 0.00, z_a, sx + 0.006, 0.08, z_b, 'M_TimberEdge', bevel=0.002)
                geo.span_u(sx, 0.95, z_a, sx + 0.006, 0.98, z_b, 'M_MutedBlue', bevel=0.002)
        geo.span_u(-0.12, 2.50, 1.6, 0.12, 4.00, 3.6, 'M_Plaster', bevel=0.004)
        add_door_frame_x(geo, 0.0, 0.12, 1.60, 3.60, 2.50, frame_w=0.065, mat='M_TimberEdge')

    elif room_id == 'LockerRoom':
        # Güney iç duvar (z=1.2, soyunma yüzü [1.20, 1.28]): mutfak-soyunma kapısı x[-7.9, -6.7]
        add_layered_wall_z(geo, -9.8, -4.8, 1.20, 1.28, y_top=3.24,
                           openings=[(-7.90, -6.70, 2.18)], trim_side=1)
        # Batı dış duvar (x=-9.8): pencere z[2.40, 4.00] (w=1.60, y=0.90..2.10)
        add_layered_wall_x(
            geo, 1.2, 5.2, -10.04, -9.80, y_top=3.24,
            openings=[(2.40, 4.00, 0.90, 2.10)],
            trim_side=1,
        )
        add_window_frame_x(geo, -10.04, -9.80, 2.40, 4.00, y0=0.90, y1=2.10, inside_dir=1)
        # Doğu iç duvar (x=-4.8, soyunma yüzü [-4.88, -4.80])
        add_layered_wall_x(geo, 1.2, 5.2, -4.88, -4.80, y_top=3.24, trim_side=-1)
        # Kuzey iç duvar (z=5.2, soyunma yüzü [5.12, 5.20]): soyunma-ring kapısı x[-7.9, -6.7]
        add_layered_wall_z(geo, -9.8, -4.8, 5.12, 5.20, y_top=3.24,
                           openings=[(-7.90, -6.70, 2.18)], trim_side=-1)
        add_door_frame_z(geo, 5.20, 0.08, -7.90, -6.70, 2.18)

    elif room_id == 'RingRoom':
        # Güney iç duvar (z=5.2, ring yüzü [5.20, 5.28]): soyunma-ring kapısı x[-7.9, -6.7]
        add_layered_wall_z(geo, -9.8, 0.0, 5.20, 5.28, y_top=3.24,
                           openings=[(-7.90, -6.70, 2.18)], trim_side=1)
        # Batı dış duvar (x=-9.8): pencere z[7.70, 9.50] (w=1.80, y=0.90..2.10)
        add_layered_wall_x(
            geo, 5.2, 12.0, -10.04, -9.80, y_top=3.24,
            openings=[(7.70, 9.50, 0.90, 2.10)],
            trim_side=1,
        )
        add_window_frame_x(geo, -10.04, -9.80, 7.70, 9.50, y0=0.90, y1=2.10, inside_dir=1)
        # Kuzey dış duvar (z=12.0): pencere x[-5.80, -4.00] (w=1.80, y=0.90..2.10)
        add_layered_wall_z(
            geo, -9.8, 0.0, 12.00, 12.24, y_top=3.24,
            openings=[(-5.80, -4.00, 0.90, 2.10)],
            trim_side=-1,
        )
        add_window_frame_z(geo, 12.00, 12.24, -5.80, -4.00, y0=0.90, y1=2.10, inside_dir=-1)
        # Doğu dış/sınır duvarı (x=0)
        add_layered_wall_x(geo, 5.2, 12.0, -0.12, 0.12, y_top=3.24, trim_side=-1)

    return geo.finish()


def build_room_windows(room_id):
    """Pencereli odalar için ayrı cam nesnesi (Room_<Ad>_Windows) ve 'Glass' materyali üretir."""
    if room_id == 'Storage':
        return None
    geo = Geo(f'Room_{room_id}_Windows')
    if room_id == 'Office':
        # Ön duvar pencere camı (z=-5.30, x[-1.94, -0.46], y[0.93, 2.05])
        geo.span_u(-1.94, 0.93, -5.31, -0.46, 2.05, -5.29, 'Glass', bevel=0.0)
    elif room_id == 'Kitchen':
        # Batı duvar pencere camı (x=-9.92, z[-3.74, -2.26], y[0.93, 2.05])
        geo.span_u(-9.93, 0.93, -3.74, -9.91, 2.05, -2.26, 'Glass', bevel=0.0)
    elif room_id == 'LockerRoom':
        # Batı duvar pencere camı (x=-9.92, z[2.46, 3.94], y[0.93, 2.05])
        geo.span_u(-9.93, 0.93, 2.46, -9.91, 2.05, 3.94, 'Glass', bevel=0.0)
    elif room_id == 'RingRoom':
        # Batı duvar (x=-9.92, z[7.76, 9.44]) ve kuzey duvar (z=12.12, x[-5.74, -4.06]) camları
        geo.span_u(-9.93, 0.93, 7.76, -9.91, 2.05, 9.44, 'Glass', bevel=0.0)
        geo.span_u(-5.74, 0.93, 12.11, -4.06, 2.05, 12.13, 'Glass', bevel=0.0)
    return geo.finish()


# ==============================================================================
# 2. MOBİLYA VE EŞYA MODELLERİ (Pivot = Taban Ortası (0, 0, 0), Üçgen <= 3000)
# ==============================================================================

def build_desk():
    """Ofis: Desk (y≈0.54 m, sıcak ahşap tabla, metal ayaklar ve sağ çekmece bloğu)."""
    geo = Geo('Desk')
    # Masa tablası (üst yüzey y=0.54)
    geo.box_u((0.0, 0.52, 0.0), (1.20, 0.04, 0.62), 'M_Oak', bevel=0.010)
    geo.box_u((0.0, 0.52, 0.0), (1.206, 0.022, 0.626), 'M_OakLight', bevel=0.005)
    # Sol çelik ayak çerçevesi
    for z in (-0.25, 0.25):
        geo.box_u((-0.54, 0.25, z), (0.045, 0.50, 0.045), 'M_Anthracite', bevel=0.005)
    geo.box_u((-0.54, 0.06, 0.0), (0.04, 0.035, 0.50), 'M_Anthracite', bevel=0.004)
    # Arka perde paneli
    geo.box_u((-0.12, 0.34, -0.23), (0.80, 0.28, 0.025), 'M_Timber', bevel=0.004)
    # Sağ çekmece ünitesi
    geo.box_u((0.36, 0.26, 0.0), (0.38, 0.48, 0.54), 'M_Timber', bevel=0.008)
    for dy, z_off in [(0.17, 0.275), (0.36, 0.285)]:
        geo.box_u((0.36, dy, z_off), (0.33, 0.16, 0.025), 'M_SoftBeige', bevel=0.005)
        geo.box_u((0.36, dy + 0.02, z_off + 0.018), (0.10, 0.02, 0.02), 'M_ToolSteel', bevel=0.003)
    return geo.finish((0.0, 0.0, 0.0)), (1.21, 0.54, 0.63)


def build_chair():
    """Ofis: Chair (chibi çalışma koltuğu, y≈0.68 m, oturma yüksekliği 0.34 m)."""
    geo = Geo('Chair')
    # 5 kollu yıldız taban ve tekerlekler
    geo.cylinder_u((0.0, 0.17, 0.0), 0.028, 0.24, 'M_Anthracite', segments=10)
    for i in range(5):
        ang = 2 * math.pi * i / 5
        cx, cz = 0.11 * math.sin(ang), 0.11 * math.cos(ang)
        tx, tz = 0.21 * math.sin(ang), 0.21 * math.cos(ang)
        geo.box_u((cx, 0.055, cz), (0.035, 0.03, 0.22), 'M_Anthracite', bevel=0.004, rot_y_deg=math.degrees(ang))
        geo.cylinder_u((tx, 0.025, tz), 0.025, 0.025, 'M_DarkRubber', axis_u=(1, 0, 0), segments=8)
    # Oturak minderi ve sırtlık
    geo.box_u((0.0, 0.32, 0.02), (0.42, 0.07, 0.40), 'M_ServiceBlue', bevel=0.022)
    geo.box_u((0.0, 0.52, -0.16), (0.38, 0.30, 0.065), 'M_ServiceBlue', bevel=0.020, rot_x_deg=-6)
    geo.box_u((0.0, 0.40, -0.18), (0.06, 0.22, 0.03), 'M_Anthracite', bevel=0.005)
    # Kolçaklar
    for sx in (-0.21, 0.21):
        geo.box_u((sx, 0.39, 0.01), (0.03, 0.12, 0.03), 'M_Anthracite', bevel=0.004)
        geo.box_u((sx, 0.45, 0.02), (0.05, 0.025, 0.22), 'M_CharcoalABS', bevel=0.006)
    return geo.finish((0.0, 0.0, 0.0)), (0.48, 0.68, 0.48)


def build_monitor():
    """Ofis: Monitor (chibi masaüstü ekranı, y≈0.32 m)."""
    geo = Geo('Monitor')
    # Taban ve boyun
    geo.box_u((0.0, 0.012, -0.02), (0.18, 0.024, 0.14), 'M_CharcoalABS', bevel=0.005)
    geo.box_u((0.0, 0.09, -0.05), (0.045, 0.15, 0.03), 'M_Anthracite', bevel=0.004, rot_x_deg=-8)
    # Ekran kasası ve panel
    geo.box_u((0.0, 0.20, 0.0), (0.44, 0.25, 0.035), 'M_CharcoalABS', bevel=0.008)
    geo.box_u((0.0, 0.20, 0.016), (0.40, 0.21, 0.008), 'M_ScreenDark', bevel=0.002)
    # Ekran üzerinde stilize durum blokları ve güç ışığı
    geo.box_u((-0.08, 0.22, 0.021), (0.18, 0.11, 0.004), 'M_MutedBlue', bevel=0.0)
    geo.box_u((0.11, 0.23, 0.021), (0.12, 0.06, 0.004), 'M_SafetyOrange', bevel=0.0)
    geo.box_u((0.18, 0.088, 0.019), (0.012, 0.008, 0.004), 'M_LedGreen', bevel=0.0)
    return geo.finish((0.0, 0.0, 0.0)), (0.44, 0.33, 0.16)


def build_pc():
    """Ofis: PC (kule kasa, y≈0.36 m)."""
    geo = Geo('PC')
    geo.box_u((0.0, 0.18, 0.0), (0.18, 0.36, 0.38), 'M_Anthracite', bevel=0.010)
    geo.box_u((0.0, 0.18, 0.188), (0.15, 0.32, 0.012), 'M_CharcoalABS', bevel=0.004)
    # Ön havalandırma ızgaraları ve güç düğmesi
    for y in (0.08, 0.12, 0.16, 0.20):
        geo.box_u((0.0, y, 0.194), (0.11, 0.018, 0.006), 'M_DarkRubber', bevel=0.0)
    geo.box_u((0.0, 0.30, 0.195), (0.035, 0.018, 0.008), 'M_SafetyOrange', bevel=0.002)
    geo.box_u((0.04, 0.30, 0.195), (0.010, 0.010, 0.006), 'M_LedGreen', bevel=0.0)
    return geo.finish((0.0, 0.0, 0.0)), (0.18, 0.36, 0.40)


def build_filing_cabinet():
    """Ofis: FilingCabinet (yeşil-mavi metal dosya dolabı, y≈1.48 m)."""
    geo = Geo('FilingCabinet')
    geo.box_u((0.0, 0.74, 0.0), (0.48, 1.48, 0.52), 'M_FilingGreen', bevel=0.012)
    # 4 çekmece; 3. çekmece hafif aralık (yaşayan ofis havası)
    drawer_ys = [0.20, 0.55, 0.90, 1.25]
    for idx, dy in enumerate(drawer_ys):
        z_pull = 0.045 if idx == 2 else 0.0
        geo.box_u((0.0, dy, 0.255 + z_pull), (0.42, 0.29, 0.035), 'M_TealMetal', bevel=0.006)
        if z_pull > 0:
            geo.box_u((0.0, dy - 0.03, 0.14 + z_pull * 0.5), (0.39, 0.20, 0.22), 'M_WarmGrayMetal', bevel=0.004)
            geo.box_u((0.0, dy + 0.05, 0.18 + z_pull * 0.5), (0.34, 0.08, 0.12), 'M_CreamPaper', bevel=0.002)
        # Etiketlik ve kulp
        geo.box_u((0.0, dy + 0.06, 0.275 + z_pull), (0.11, 0.045, 0.008), 'M_CreamPaper', bevel=0.002)
        geo.box_u((0.0, dy - 0.03, 0.282 + z_pull), (0.12, 0.022, 0.022), 'M_ToolSteel', bevel=0.004)
    return geo.finish((0.0, 0.0, 0.0)), (0.48, 1.48, 0.58)


def build_document_shelf():
    """Ofis: DocumentShelf (ahşap evrak rafı ve renkli klasörler, y≈1.34 m)."""
    geo = Geo('DocumentShelf')
    # Yan dikmeler, arka panel ve 4 raf
    for sx in (-0.42, 0.42):
        geo.box_u((sx, 0.67, 0.0), (0.04, 1.34, 0.34), 'M_Oak', bevel=0.006)
    geo.box_u((0.0, 0.67, -0.155), (0.84, 1.32, 0.025), 'M_Timber', bevel=0.004)
    shelf_ys = [0.08, 0.46, 0.84, 1.28]
    for sy in shelf_ys:
        geo.box_u((0.0, sy, 0.0), (0.84, 0.032, 0.33), 'M_OakLight', bevel=0.005)
    # Raflarda dizili stilize klasör blokları
    binders = [
        (-0.30, 0.22, 'M_ServiceBlue', 0.0),
        (-0.22, 0.22, 'M_ServiceBlue', 0.0),
        (-0.12, 0.22, 'M_SafetyOrange', -8.0),
        (0.18, 0.22, 'M_ToolboxRed', 0.0),
        (0.26, 0.22, 'M_FilingGreen', 0.0),
        (-0.28, 0.60, 'M_FilingGreen', 0.0),
        (-0.19, 0.60, 'M_SafetyOchre', 0.0),
        (0.05, 0.60, 'M_ServiceBlue', 6.0),
        (0.24, 0.60, 'M_CreamPaper', 0.0),
        (-0.20, 0.98, 'M_ToolboxRed', 0.0),
        (-0.11, 0.98, 'M_SafetyOrange', 0.0),
        (0.15, 0.98, 'M_ServiceBlue', 0.0),
    ]
    for bx, by, bmat, tilt in binders:
        geo.box_u((bx, by, 0.01), (0.065, 0.24, 0.24), bmat, bevel=0.004, rot_z_deg=tilt)
    return geo.finish((0.0, 0.0, 0.0)), (0.88, 1.34, 0.34)


def build_city_map_board():
    """Ofis: CityMapBoard (O26_city_map dokulu şehir haritası panosu, y≈0.88 m)."""
    geo = Geo('CityMapBoard')
    w, h = 1.36, 0.88
    # Arka pano ve ahşap çerçeve
    geo.box_u((0.0, h * 0.5, -0.012), (w, h, 0.024), 'M_Timber', bevel=0.005)
    geo.box_u((0.0, 0.025, 0.012), (w, 0.045, 0.045), 'M_Oak', bevel=0.005)
    geo.box_u((0.0, h - 0.025, 0.012), (w, 0.045, 0.045), 'M_Oak', bevel=0.005)
    for sx in (-w * 0.5 + 0.022, w * 0.5 - 0.022):
        geo.box_u((sx, h * 0.5, 0.012), (0.045, h, 0.045), 'M_Oak', bevel=0.005)
    # Harita yüzeyi (+Z yönüne bakan kapalı ince plaka; UV koordinatları finish() içinde hesaplanır)
    geo.box_u((0.0, h * 0.5, 0.004), (1.27, 0.79, 0.008), 'M_CityMap', bevel=0.0)
    # Küçük harita raptiyeleri (kırmızı/turuncu/mavi)
    for px, py, pmat in [(-0.28, 0.52, 'M_ToolboxLipRed'), (0.14, 0.38, 'M_SafetyOrange'), (0.32, 0.62, 'M_RingBlue')]:
        geo.box_u((px, py, 0.014), (0.026, 0.026, 0.018), pmat, bevel=0.004)
    return geo.finish((0.0, 0.0, 0.0)), (1.36, 0.88, 0.07)


def build_clipboard():
    """Ofis: Clipboard (mandallı sekreterlik ve form kağıdı)."""
    geo = Geo('Clipboard')
    geo.box_u((0.0, 0.006, 0.0), (0.22, 0.012, 0.30), 'M_Fibreboard', bevel=0.003)
    geo.box_u((0.0, 0.014, 0.01), (0.19, 0.005, 0.25), 'M_CreamPaper', bevel=0.001)
    geo.box_u((0.0, 0.020, -0.12), (0.09, 0.014, 0.035), 'M_ToolSteel', bevel=0.003)
    for z in (-0.06, -0.02, 0.02, 0.06):
        geo.box_u((0.0, 0.017, z), (0.14, 0.002, 0.010), 'M_InkGray', bevel=0.0)
    return geo.finish((0.0, 0.0, 0.0)), (0.22, 0.03, 0.30)


def build_plant():
    """Ofis: Plant (terrakota saksıda stilize low-poly bitki, y≈0.62 m)."""
    geo = Geo('Plant')
    geo.cylinder_u((0.0, 0.13, 0.0), 0.13, 0.26, 'M_Terracotta', segments=10)
    geo.cylinder_u((0.0, 0.24, 0.0), 0.145, 0.045, 'M_Terracotta', segments=10)
    geo.cylinder_u((0.0, 0.255, 0.0), 0.125, 0.02, 'M_PottingSoil', segments=10)
    geo.cylinder_u((0.0, 0.36, 0.0), 0.024, 0.22, 'M_TimberEdge', segments=6)
    leaves = [
        ((0.0, 0.48, 0.0), (0.22, 0.16, 0.22), 'M_FoliageDeep', 0, 0),
        ((-0.09, 0.42, 0.06), (0.16, 0.12, 0.16), 'M_FoliageOlive', 18, 25),
        ((0.10, 0.44, -0.05), (0.17, 0.13, 0.15), 'M_FoliageSunlit', -15, -30),
        ((0.02, 0.56, 0.04), (0.15, 0.12, 0.15), 'M_FoliageSunlit', 12, 40),
    ]
    for pos, sz, mat, rx, ry in leaves:
        geo.box_u(pos, sz, mat, bevel=0.035, rot_x_deg=rx, rot_y_deg=ry)
    return geo.finish((0.0, 0.0, 0.0)), (0.38, 0.62, 0.38)


def build_rug():
    """Ofis: Rug (sıcak bordürlü düz ofis halısı, 2.00 x 1.40 m)."""
    geo = Geo('Rug')
    geo.box_u((0.0, 0.005, 0.0), (2.00, 0.010, 1.40), 'M_MutedBlue', bevel=0.003)
    geo.box_u((0.0, 0.008, 0.0), (1.76, 0.008, 1.16), 'M_BeigeFabric', bevel=0.002)
    geo.box_u((0.0, 0.010, 0.0), (1.46, 0.006, 0.86), 'M_SafetyOchre', bevel=0.002)
    return geo.finish((0.0, 0.0, 0.0)), (2.00, 0.015, 1.40)


def build_mug():
    """Ofis: Mug (turuncu seramik kahve kupası)."""
    geo = Geo('Mug')
    geo.cylinder_u((0.0, 0.048, 0.0), 0.040, 0.096, 'M_SafetyOrange', segments=12)
    geo.cylinder_u((0.0, 0.088, 0.0), 0.033, 0.018, 'M_Coffee', segments=12)
    geo.box_u((0.046, 0.048, 0.0), (0.024, 0.056, 0.014), 'M_CreamCeramic', bevel=0.004)
    return geo.finish((0.0, 0.0, 0.0)), (0.10, 0.10, 0.09)


def build_sofa():
    """Mutfak: Sofa (bej kanepe, y≈0.66 m, oturma yüksekliği 0.32 m)."""
    geo = Geo('Sofa')
    # Ahşap ayaklar ve alt baza
    for sx in (-0.68, 0.68):
        for sz in (-0.28, 0.28):
            geo.box_u((sx, 0.05, sz), (0.055, 0.10, 0.055), 'M_Timber', bevel=0.006)
    geo.box_u((0.0, 0.16, 0.0), (1.52, 0.14, 0.68), 'M_BeigeFabric', bevel=0.025)
    # İki geniş oturma minderi ve sırt minderleri
    for sx in (-0.33, 0.33):
        geo.box_u((sx, 0.27, 0.04), (0.64, 0.12, 0.56), 'M_BeigeCushion', bevel=0.030)
        geo.box_u((sx, 0.49, -0.20), (0.63, 0.34, 0.18), 'M_BeigeCushion', bevel=0.032, rot_x_deg=-6)
    # Yuvarlatılmış kolçaklar
    for sx in (-0.70, 0.70):
        geo.box_u((sx, 0.34, 0.0), (0.14, 0.32, 0.68), 'M_BeigeFabric', bevel=0.030)
    return geo.finish((0.0, 0.0, 0.0)), (1.56, 0.66, 0.72)


def build_side_table():
    """Mutfak: SideTable (ahşap yan sehpa, y≈0.42 m)."""
    geo = Geo('SideTable')
    geo.box_u((0.0, 0.40, 0.0), (0.50, 0.04, 0.50), 'M_Oak', bevel=0.010)
    geo.box_u((0.0, 0.14, 0.0), (0.42, 0.025, 0.42), 'M_OakLight', bevel=0.006)
    for sx in (-0.20, 0.20):
        for sz in (-0.20, 0.20):
            geo.box_u((sx, 0.20, sz), (0.04, 0.38, 0.04), 'M_Timber', bevel=0.005)
    return geo.finish((0.0, 0.0, 0.0)), (0.50, 0.42, 0.50)


def build_kitchen_counter():
    """Mutfak: KitchenCounter (y≈0.60 m ahşap tezgâh, eviye ve alt dolaplar)."""
    geo = Geo('KitchenCounter')
    # Alt baza ve dolap gövdesi
    geo.box_u((0.0, 0.04, -0.02), (1.74, 0.08, 0.50), 'M_CharcoalTrim', bevel=0.004)
    geo.box_u((0.0, 0.31, 0.0), (1.76, 0.46, 0.54), 'M_FilingGreen', bevel=0.010)
    # Ahşap tezgâh üstü (y=0.60)
    geo.box_u((0.0, 0.57, 0.01), (1.80, 0.06, 0.58), 'M_Oak', bevel=0.012)
    # 3 kapak ve kulplar
    for cx in (-0.56, 0.0, 0.56):
        geo.box_u((cx, 0.29, 0.275), (0.52, 0.38, 0.022), 'M_TealMetal', bevel=0.006)
        geo.box_u((cx + 0.18, 0.36, 0.292), (0.022, 0.10, 0.020), 'M_ToolSteel', bevel=0.003)
    # Sağ tarafta çelik eviye tablası ve musluk (sol taraf CoffeeMachine için düz)
    geo.box_u((0.46, 0.602, 0.02), (0.54, 0.016, 0.38), 'M_ToolSteel', bevel=0.004)
    geo.box_u((0.46, 0.606, 0.02), (0.44, 0.012, 0.28), 'M_Anthracite', bevel=0.002)
    geo.cylinder_u((0.46, 0.68, -0.14), 0.016, 0.15, 'M_ToolSteel', segments=8)
    geo.box_u((0.46, 0.74, -0.09), (0.026, 0.026, 0.12), 'M_ToolSteel', bevel=0.004)
    return geo.finish((0.0, 0.0, 0.0)), (1.80, 0.60, 0.58)


def build_coffee_machine():
    """Mutfak: CoffeeMachine (kırmızı-antrasit kahve makinesi, y≈0.30 m)."""
    geo = Geo('CoffeeMachine')
    geo.box_u((0.0, 0.02, 0.0), (0.22, 0.04, 0.25), 'M_CharcoalABS', bevel=0.006)
    geo.box_u((0.0, 0.15, -0.05), (0.21, 0.24, 0.14), 'M_ToolboxRed', bevel=0.012)
    geo.box_u((0.0, 0.26, 0.01), (0.21, 0.06, 0.24), 'M_ToolboxLipRed', bevel=0.010)
    # Demlik / karaf
    geo.cylinder_u((0.0, 0.11, 0.04), 0.065, 0.13, 'M_SmokyGlass', segments=12)
    geo.box_u((0.075, 0.11, 0.04), (0.025, 0.09, 0.022), 'M_CharcoalABS', bevel=0.004)
    # Ön kontrol düğmeleri
    geo.box_u((-0.05, 0.26, 0.132), (0.022, 0.022, 0.010), 'M_SafetyOrange', bevel=0.002)
    geo.box_u((0.05, 0.26, 0.132), (0.022, 0.022, 0.010), 'M_LedGreen', bevel=0.002)
    return geo.finish((0.0, 0.0, 0.0)), (0.24, 0.30, 0.26)


def build_fridge():
    """Mutfak: Fridge (beyaz çift kapılı buzdolabı, y≈1.25 m)."""
    geo = Geo('Fridge')
    geo.box_u((0.0, 0.625, -0.02), (0.64, 1.23, 0.56), 'M_FridgeWhite', bevel=0.022)
    # Alt soğutucu kapısı ve üst dondurucu kapısı
    geo.box_u((0.0, 0.45, 0.275), (0.61, 0.80, 0.045), 'M_FridgeWhite', bevel=0.018)
    geo.box_u((0.0, 1.05, 0.275), (0.61, 0.35, 0.045), 'M_FridgeWhite', bevel=0.018)
    # Metal kulplar
    geo.box_u((-0.24, 0.58, 0.31), (0.028, 0.24, 0.028), 'M_ToolSteel', bevel=0.005)
    geo.box_u((-0.24, 1.03, 0.31), (0.028, 0.16, 0.028), 'M_ToolSteel', bevel=0.005)
    return geo.finish((0.0, 0.0, 0.0)), (0.64, 1.25, 0.62)


def build_picture_frame():
    """Mutfak: PictureFrame (çerçeveli stilize doğa tablosu, y≈0.54 m)."""
    geo = Geo('PictureFrame')
    w, h = 0.76, 0.54
    geo.box_u((0.0, h * 0.5, 0.0), (w, h, 0.032), 'M_Timber', bevel=0.006)
    geo.box_u((0.0, h * 0.5, 0.015), (w - 0.08, h - 0.08, 0.010), 'M_CreamPaper', bevel=0.002)
    # Stilize manzara katmanları (göl, tepe, dağ ve güneş)
    geo.box_u((0.0, 0.18, 0.020), (w - 0.14, 0.12, 0.006), 'M_MutedBlue', bevel=0.0)
    geo.box_u((-0.08, 0.27, 0.021), (0.42, 0.10, 0.006), 'M_FoliageOlive', bevel=0.0)
    geo.box_u((0.10, 0.33, 0.020), (0.40, 0.14, 0.005), 'M_MauveHill', bevel=0.0)
    geo.box_u((-0.18, 0.39, 0.022), (0.08, 0.08, 0.006), 'M_SafetyOchre', bevel=0.012)
    return geo.finish((0.0, 0.0, 0.0)), (0.76, 0.54, 0.04)


def build_metal_shelf():
    """Depo: MetalShelf (gri metal raf, en üst raf y=1.40 <= 1.45 m)."""
    geo = Geo('MetalShelf')
    w, d, h = 1.20, 0.45, 1.45
    # 4 köşe L-profil dikme
    for sx in (-w * 0.5 + 0.025, w * 0.5 - 0.025):
        for sz in (-d * 0.5 + 0.025, d * 0.5 - 0.025):
            geo.box_u((sx, h * 0.5, sz), (0.042, h, 0.042), 'M_GalvanizedSteel', bevel=0.004)
    # 4 raf tablası (üst yüzeyleri METAL_SHELF_HEIGHTS = [0.12, 0.55, 0.98, 1.40])
    for sy in METAL_SHELF_HEIGHTS:
        geo.box_u((0.0, sy - 0.018, 0.0), (w - 0.02, 0.036, d - 0.02), 'M_WarmGrayMetal', bevel=0.005)
    # Yan çapraz gergi çubukları
    for sx in (-w * 0.5 + 0.025, w * 0.5 - 0.025):
        geo.box_u((sx, 0.76, 0.0), (0.018, 0.95, 0.028), 'M_SilverSteel', bevel=0.002, rot_x_deg=22)
    return geo.finish((0.0, 0.0, 0.0)), (1.20, 1.45, 0.45)


def build_repair_bench():
    """Depo: RepairBench (ağır hizmet tamir tezgâhı ve mengene, y≈0.58 m)."""
    geo = Geo('RepairBench')
    # Çelik ayaklar ve alt raf
    for sx in (-0.64, 0.64):
        for sz in (-0.25, 0.25):
            geo.box_u((sx, 0.27, sz), (0.055, 0.54, 0.055), 'M_Anthracite', bevel=0.005)
    geo.box_u((0.0, 0.14, 0.0), (1.30, 0.03, 0.50), 'M_WarmGrayMetal', bevel=0.004)
    # Kalın ahşap çalışma tablası (üst y=0.58)
    geo.box_u((0.0, 0.555, 0.0), (1.40, 0.05, 0.60), 'M_Timber', bevel=0.010)
    # Kırmızı alet çekmecesi
    geo.box_u((0.34, 0.45, 0.02), (0.48, 0.14, 0.50), 'M_ToolboxRed', bevel=0.006)
    geo.box_u((0.34, 0.45, 0.275), (0.44, 0.11, 0.02), 'M_ToolboxLipRed', bevel=0.004)
    geo.box_u((0.34, 0.45, 0.290), (0.14, 0.02, 0.02), 'M_ToolSteel', bevel=0.003)
    # Sol ön köşede mavi mengene
    geo.box_u((-0.50, 0.62, 0.22), (0.14, 0.08, 0.16), 'M_CobaltBlue', bevel=0.006)
    geo.box_u((-0.50, 0.66, 0.28), (0.12, 0.04, 0.04), 'M_ToolSteel', bevel=0.004)
    return geo.finish((0.0, 0.0, 0.0)), (1.40, 0.68, 0.60)


def build_pegboard():
    """Depo: Pegboard (tamir tezgâhı üstü delikli alet panosu, y≈0.72 m)."""
    geo = Geo('Pegboard')
    w, h = 1.36, 0.72
    geo.box_u((0.0, h * 0.5, -0.01), (w, h, 0.025), 'M_Anthracite', bevel=0.005)
    geo.box_u((0.0, h * 0.5, 0.005), (w - 0.06, h - 0.06, 0.016), 'M_Fibreboard', bevel=0.003)
    # Delik izleri ve asılı stilize el aletleri (anahtar, çekiç, tornavida)
    for gx in (-0.48, -0.24, 0.0, 0.24, 0.48):
        for gy in (0.18, 0.36, 0.54):
            geo.box_u((gx, gy, 0.014), (0.022, 0.022, 0.006), 'M_PegHole', bevel=0.0)
    # İngiliz anahtarı
    geo.box_u((-0.36, 0.38, 0.022), (0.04, 0.26, 0.016), 'M_ToolSteel', bevel=0.004)
    geo.box_u((-0.36, 0.51, 0.022), (0.08, 0.06, 0.018), 'M_ToolSteel', bevel=0.004)
    # Çekiç
    geo.box_u((0.0, 0.36, 0.022), (0.032, 0.28, 0.022), 'M_OakLight', bevel=0.004)
    geo.box_u((0.0, 0.50, 0.024), (0.14, 0.048, 0.036), 'M_Anthracite', bevel=0.005)
    # Turuncu saplı tornavida
    geo.box_u((0.34, 0.44, 0.024), (0.042, 0.11, 0.032), 'M_SafetyOrange', bevel=0.006)
    geo.box_u((0.34, 0.31, 0.022), (0.014, 0.16, 0.014), 'M_ToolSteel', bevel=0.002)
    return geo.finish((0.0, 0.0, 0.0)), (1.36, 0.72, 0.05)


def build_locker_closed():
    """Soyunma: LockerClosed (yeşil-mavi kapalı metal soyunma dolabı, y≈1.50 m)."""
    geo = Geo('LockerClosed')
    geo.box_u((0.0, 0.75, 0.0), (0.45, 1.50, 0.48), 'M_FilingGreen', bevel=0.010)
    geo.box_u((0.0, 0.76, 0.242), (0.39, 1.38, 0.022), 'M_TealMetal', bevel=0.006)
    # Havalandırma panjurları, isimlik ve kulp
    for ly in (1.28, 1.33, 1.38, 0.20, 0.25):
        geo.box_u((0.0, ly, 0.255), (0.24, 0.018, 0.008), 'M_Anthracite', bevel=0.0)
    geo.box_u((0.0, 1.12, 0.255), (0.11, 0.05, 0.008), 'M_CreamPaper', bevel=0.002)
    geo.box_u((-0.14, 0.78, 0.262), (0.024, 0.12, 0.024), 'M_ToolSteel', bevel=0.004)
    return geo.finish((0.0, 0.0, 0.0)), (0.45, 1.50, 0.50)


def build_locker_open():
    """Soyunma: LockerOpen (kapısı açık metal soyunma dolabı, y≈1.50 m)."""
    geo = Geo('LockerOpen')
    # Arka, üst, alt ve yan paneller (içi boşluklu)
    geo.box_u((0.0, 0.75, -0.22), (0.45, 1.50, 0.04), 'M_FilingGreen', bevel=0.006)
    for sx in (-0.205, 0.205):
        geo.box_u((sx, 0.75, 0.0), (0.04, 1.50, 0.48), 'M_FilingGreen', bevel=0.006)
    geo.box_u((0.0, 0.03, 0.0), (0.45, 0.06, 0.48), 'M_FilingGreen', bevel=0.006)
    geo.box_u((0.0, 1.47, 0.0), (0.45, 0.06, 0.48), 'M_FilingGreen', bevel=0.006)
    # İç üst raf ve askı borusu
    geo.box_u((0.0, 1.22, 0.0), (0.37, 0.025, 0.42), 'M_WarmGrayMetal', bevel=0.003)
    geo.cylinder_u((0.0, 1.15, 0.02), 0.012, 0.36, 'M_ToolSteel', axis_u=(1, 0, 0), segments=8)
    # Sağ menteşeden dışarı doğru açılmış kapak
    geo.box_u((0.28, 0.76, 0.40), (0.025, 1.38, 0.39), 'M_TealMetal', bevel=0.006, rot_y_deg=-25)
    return geo.finish((0.0, 0.0, 0.0)), (0.62, 1.50, 0.65)


def build_bench():
    """Soyunma: Bench (ahşap çıtalı soyunma sırası, y≈0.32 m)."""
    geo = Geo('Bench')
    for sz in (-0.11, 0.0, 0.11):
        geo.box_u((0.0, 0.30, sz), (1.30, 0.035, 0.095), 'M_Oak', bevel=0.006)
    for sx in (-0.52, 0.0, 0.52):
        geo.box_u((sx, 0.15, 0.0), (0.045, 0.28, 0.32), 'M_Anthracite', bevel=0.005)
    return geo.finish((0.0, 0.0, 0.0)), (1.30, 0.32, 0.36)


def build_hanging_uniform():
    """Soyunma: HangingUniform (askıda kurtarma ekibi üniforması)."""
    geo = Geo('HangingUniform')
    # Askı kancası ve omuzluk
    geo.box_u((0.0, 0.64, 0.0), (0.018, 0.07, 0.018), 'M_ToolSteel', bevel=0.002)
    geo.box_u((0.0, 0.58, 0.0), (0.34, 0.035, 0.028), 'M_OakLight', bevel=0.006)
    # Üst ceket gövdesi, kollar ve reflektör şeritler
    geo.box_u((0.0, 0.34, 0.0), (0.28, 0.44, 0.11), 'M_NavyCanvas', bevel=0.020)
    geo.box_u((0.0, 0.38, 0.005), (0.29, 0.26, 0.12), 'M_SafetyOchre', bevel=0.015)
    geo.box_u((0.0, 0.34, 0.008), (0.295, 0.035, 0.124), 'M_ReflectiveTrim', bevel=0.004)
    for sx, tilt in [(-0.17, 14), (0.17, -14)]:
        geo.box_u((sx, 0.36, 0.0), (0.08, 0.34, 0.09), 'M_SafetyOrange', bevel=0.014, rot_z_deg=tilt)
    return geo.finish((0.0, 0.0, 0.0)), (0.40, 0.68, 0.14)


def build_boxing_ring():
    """Ring: BoxingRing (4.6x4.6 m, platform y≈0.35 m, köşede tek direk ve direğe tam oturan kırmızı/mavi minderler)."""
    geo = Geo('BoxingRing')
    # Alt çelik etek ve mavi sünger platform (üst y=0.35 m)
    geo.box_u((0.0, 0.145, 0.0), (4.52, 0.29, 4.52), 'M_Anthracite', bevel=0.015)
    geo.box_u((0.0, 0.32, 0.0), (4.60, 0.06, 4.60), 'M_RingBlue', bevel=0.018)
    geo.box_u((0.0, 0.348, 0.0), (4.10, 0.008, 4.10), 'M_CobaltBlue', bevel=0.002)

    # 4 köşe: her köşede tek çelik direk ve direğe tam oturan kırmızı/mavi köşe minderi
    corners = [
        (-2.10, -2.10, 'M_ToolboxLipRed', 45.0),
        (2.10, 2.10, 'M_RingBlue', 225.0),
        (-2.10, 2.10, 'M_ToolboxLipRed', 135.0),
        (2.10, -2.10, 'M_RingBlue', -45.0),
    ]
    for cx, cz, cmat, c_ang in corners:
        # Köşede tek ana direk
        geo.cylinder_u((cx, 0.855, cz), 0.060, 1.01, 'M_Anthracite', segments=12)
        # Direğe tam oturan iç köşe koruma minderi (direk merkezine bitişik)
        dx = -0.042 if cx > 0 else 0.042
        dz = -0.042 if cz > 0 else 0.042
        geo.box_u((cx + dx, 0.90, cz + dz), (0.16, 0.80, 0.16), cmat, bevel=0.030, rot_y_deg=c_ang)

    # 3 sıra halat (y = 0.62, 0.90, 1.18 m; kırmızı-beyaz-mavi)
    rope_heights = [0.62, 0.90, 1.18]
    rope_mats = ['M_ToolboxLipRed', 'M_FridgeWhite', 'M_RingBlue']
    span = 4.12
    for ry, rmat in zip(rope_heights, rope_mats):
        # Kuzey ve Güney halatları (X boyunca, direklere doğrudan bağlanır)
        for sz in (-2.06, 2.06):
            geo.cylinder_u((0.0, ry, sz), 0.018, span, rmat, axis_u=(1, 0, 0), segments=8)
        # Batı ve Doğu halatları (Z boyunca)
        for sx in (-2.06, 2.06):
            geo.cylinder_u((sx, ry, 0.0), 0.018, span, rmat, axis_u=(0, 0, 1), segments=8)
    return geo.finish((0.0, 0.0, 0.0)), (4.60, 1.36, 4.60)


# --- İKİNCİ TUR EKLENEN EŞYALAR (Pivot = Taban Ortası (0, 0, 0), Üçgen <= 1500, Yazı/Logo Yok) ---

def build_paper_stack():
    """Ofis dağınıklığı: PaperStack (hafif dağınık kağıt ve klasör yığını, y≈0.14 m)."""
    geo = Geo('PaperStack')
    geo.box_u((0.0, 0.022, 0.0), (0.24, 0.044, 0.31), 'M_CreamPaper', bevel=0.004, rot_y_deg=4.0)
    geo.box_u((0.01, 0.056, -0.01), (0.25, 0.022, 0.32), 'M_SafetyOrange', bevel=0.004, rot_y_deg=-8.0)
    geo.box_u((-0.01, 0.088, 0.01), (0.23, 0.040, 0.30), 'M_CreamPaper', bevel=0.004, rot_y_deg=11.0)
    geo.box_u((0.02, 0.118, 0.0), (0.21, 0.018, 0.28), 'M_ServiceBlue', bevel=0.003, rot_y_deg=-5.0)
    return geo.finish((0.0, 0.0, 0.0)), (0.28, 0.14, 0.34)


def build_cardboard_box():
    """Ofis/Depo dağınıklığı: CardboardBox (bantlı ve kulplu karton koli, y≈0.34 m)."""
    geo = Geo('CardboardBox')
    geo.box_u((0.0, 0.16, 0.0), (0.42, 0.32, 0.36), 'M_Fibreboard', bevel=0.012)
    geo.box_u((0.0, 0.31, 0.0), (0.435, 0.045, 0.375), 'M_OakLight', bevel=0.008)
    # Üst koli bandı ve yan taşıma oyukları
    geo.box_u((0.0, 0.332, 0.0), (0.06, 0.006, 0.376), 'M_SafetyOchre', bevel=0.001)
    for sx in (-0.212, 0.212):
        geo.box_u((sx, 0.21, 0.0), (0.008, 0.032, 0.09), 'M_PegHole', bevel=0.0)
    # Yan turuncu etiket şeridi (yazısız)
    geo.box_u((0.10, 0.16, 0.181), (0.11, 0.07, 0.006), 'M_SafetyOrange', bevel=0.001)
    return geo.finish((0.0, 0.0, 0.0)), (0.44, 0.34, 0.38)


def build_pen_holder():
    """Ofis dağınıklığı: PenHolder (masaüstü kalemlik ve renkli kalemler, y≈0.15 m)."""
    geo = Geo('PenHolder')
    geo.cylinder_u((0.0, 0.052, 0.0), 0.042, 0.104, 'M_TealMetal', segments=10)
    geo.cylinder_u((0.0, 0.098, 0.0), 0.035, 0.016, 'M_Anthracite', segments=10)
    pens = [
        ((-0.014, 0.095, 0.008), (0.012, 0.11, 0.012), 'M_SafetyOrange', 10, -8),
        ((0.015, 0.100, -0.006), (0.011, 0.12, 0.011), 'M_ToolboxLipRed', -8, 12),
        ((0.002, 0.092, -0.015), (0.012, 0.10, 0.012), 'M_RingBlue', -12, -5),
        ((-0.008, 0.098, -0.004), (0.022, 0.11, 0.006), 'M_SafetyOchre', 6, 6),
    ]
    for pos, sz, mat, rx, rz in pens:
        geo.box_u(pos, sz, mat, bevel=0.002, rot_x_deg=rx, rot_z_deg=rz)
    return geo.finish((0.0, 0.0, 0.0)), (0.10, 0.16, 0.10)


def build_trash_bin():
    """Ofis dağınıklığı: TrashBin (silindirik çöp kovası ve buruşuk kağıtlar, y≈0.32 m)."""
    geo = Geo('TrashBin')
    geo.cylinder_u((0.0, 0.15, 0.0), 0.13, 0.30, 'M_Anthracite', segments=12)
    geo.cylinder_u((0.0, 0.29, 0.0), 0.142, 0.028, 'M_TealMetal', segments=12)
    # İçindeki buruşuk kağıt topları
    geo.box_u((-0.03, 0.27, 0.02), (0.075, 0.065, 0.075), 'M_CreamPaper', bevel=0.020, rot_x_deg=18, rot_y_deg=25)
    geo.box_u((0.04, 0.28, -0.02), (0.068, 0.060, 0.068), 'M_CreamPaper', bevel=0.018, rot_z_deg=-22)
    geo.box_u((0.0, 0.26, -0.04), (0.060, 0.055, 0.060), 'M_SafetyOrange', bevel=0.016, rot_x_deg=-15)
    return geo.finish((0.0, 0.0, 0.0)), (0.29, 0.32, 0.29)


def build_floor_cables():
    """Ofis dağınıklığı: FloorCables (zemin kablo kanalı, kablolar ve priz kutusu, y≈0.035 m)."""
    geo = Geo('FloorCables')
    # Ana kauçuk kablo koruyucu ve çoklu priz
    geo.box_u((0.0, 0.008, 0.0), (0.68, 0.016, 0.09), 'M_DarkRubber', bevel=0.005, rot_y_deg=8.0)
    geo.box_u((0.0, 0.015, 0.0), (0.66, 0.005, 0.028), 'M_SafetyOrange', bevel=0.001, rot_y_deg=8.0)
    geo.box_u((-0.18, 0.018, 0.12), (0.24, 0.036, 0.075), 'M_CreamCeramic', bevel=0.006, rot_y_deg=-14.0)
    geo.box_u((-0.26, 0.036, 0.14), (0.024, 0.008, 0.024), 'M_ToolboxLipRed', bevel=0.002, rot_y_deg=-14.0)
    # Kıvrımlı kablo uzantıları
    geo.box_u((0.12, 0.007, 0.11), (0.38, 0.014, 0.022), 'M_CharcoalABS', bevel=0.004, rot_y_deg=22.0)
    geo.box_u((-0.05, 0.007, -0.10), (0.42, 0.014, 0.020), 'M_DarkRubber', bevel=0.004, rot_y_deg=-18.0)
    return geo.finish((0.0, 0.0, 0.0)), (0.72, 0.04, 0.32)


def build_dining_table():
    """Mutfak oturma: DiningTable (sıcak ahşap mutfak yemek masası, y≈0.56 m)."""
    geo = Geo('DiningTable')
    geo.box_u((0.0, 0.535, 0.0), (1.16, 0.05, 0.72), 'M_Oak', bevel=0.014)
    geo.box_u((0.0, 0.49, 0.0), (1.02, 0.045, 0.58), 'M_TimberEdge', bevel=0.006)
    for sx in (-0.48, 0.48):
        for sz in (-0.26, 0.26):
            geo.box_u((sx, 0.245, sz), (0.055, 0.49, 0.055), 'M_Anthracite', bevel=0.006)
    geo.box_u((0.0, 0.12, 0.0), (0.94, 0.032, 0.04), 'M_Anthracite', bevel=0.004)
    return geo.finish((0.0, 0.0, 0.0)), (1.16, 0.56, 0.72)


def build_stool():
    """Mutfak oturma: Stool (yuvarlak minderli mutfak taburesi, y≈0.36 m)."""
    geo = Geo('Stool')
    geo.cylinder_u((0.0, 0.33, 0.0), 0.165, 0.06, 'M_SafetyOrange', segments=12)
    geo.cylinder_u((0.0, 0.295, 0.0), 0.155, 0.02, 'M_Oak', segments=12)
    for sx in (-0.10, 0.10):
        for sz in (-0.10, 0.10):
            geo.box_u((sx, 0.145, sz), (0.034, 0.29, 0.034), 'M_Anthracite', bevel=0.004)
    geo.box_u((0.0, 0.11, 0.0), (0.21, 0.024, 0.21), 'M_Anthracite', bevel=0.003)
    return geo.finish((0.0, 0.0, 0.0)), (0.34, 0.36, 0.34)


def build_water_dispenser():
    """Mutfak oturma: WaterDispenser (üst damacanalı su sebili, y≈1.18 m)."""
    geo = Geo('WaterDispenser')
    # Alt gövde
    geo.box_u((0.0, 0.41, 0.0), (0.34, 0.82, 0.34), 'M_FridgeWhite', bevel=0.016)
    geo.box_u((0.0, 0.56, 0.165), (0.24, 0.22, 0.02), 'M_Anthracite', bevel=0.004)
    # Kırmızı (sıcak) ve mavi (soğuk) musluk mandalları ve damlalık
    geo.box_u((-0.05, 0.62, 0.178), (0.032, 0.045, 0.024), 'M_ToolboxLipRed', bevel=0.003)
    geo.box_u((0.05, 0.62, 0.178), (0.032, 0.045, 0.024), 'M_RingBlue', bevel=0.003)
    geo.box_u((0.0, 0.46, 0.178), (0.22, 0.025, 0.05), 'M_TealMetal', bevel=0.004)
    # Üst mavi su damacanası
    geo.cylinder_u((0.0, 0.84, 0.0), 0.07, 0.06, 'M_CobaltBlue', segments=10)
    geo.cylinder_u((0.0, 1.01, 0.0), 0.135, 0.30, 'M_SmokyGlass', segments=12)
    geo.cylinder_u((0.0, 1.165, 0.0), 0.12, 0.03, 'M_CobaltBlue', segments=12)
    return geo.finish((0.0, 0.0, 0.0)), (0.34, 1.18, 0.36)


def build_wall_cabinet():
    """Mutfak: WallCabinet (duvara monte çift kapaklı üst mutfak dolabı, y≈0.52 m)."""
    geo = Geo('WallCabinet')
    geo.box_u((0.0, 0.26, 0.0), (1.10, 0.52, 0.32), 'M_FilingGreen', bevel=0.010)
    geo.box_u((0.0, 0.51, 0.005), (1.13, 0.03, 0.34), 'M_Oak', bevel=0.005)
    for sx, hx in [(-0.265, -0.05), (0.265, 0.05)]:
        geo.box_u((sx, 0.255, 0.162), (0.50, 0.46, 0.022), 'M_TealMetal', bevel=0.006)
        geo.box_u((hx, 0.16, 0.180), (0.022, 0.11, 0.022), 'M_ToolSteel', bevel=0.003)
    return geo.finish((0.0, 0.0, 0.0)), (1.13, 0.53, 0.35)


def build_cup_shelf():
    """Mutfak: CupShelf (duvar tipi ahşap kupa/kahve rafı ve renkli kupalar, y≈0.36 m)."""
    geo = Geo('CupShelf')
    geo.box_u((0.0, 0.18, -0.08), (0.76, 0.34, 0.024), 'M_Timber', bevel=0.005)
    for sy in (0.04, 0.22):
        geo.box_u((0.0, sy, 0.0), (0.78, 0.03, 0.18), 'M_Oak', bevel=0.005)
    # Alt ve üst raftaki renkli kupalar ve kavanozlar
    for cx, mat in [(-0.24, 'M_SafetyOrange'), (-0.06, 'M_CreamCeramic'), (0.14, 'M_RingBlue'), (0.28, 'M_ToolboxLipRed')]:
        geo.cylinder_u((cx, 0.095, 0.01), 0.036, 0.08, mat, segments=10)
    for cx, mat in [(-0.20, 'M_TealMetal'), (0.02, 'M_SafetyOchre'), (0.22, 'M_FilingGreen')]:
        geo.box_u((cx, 0.285, 0.01), (0.09, 0.10, 0.09), mat, bevel=0.012)
    return geo.finish((0.0, 0.0, 0.0)), (0.78, 0.36, 0.20)


def build_punching_bag():
    """Spor/Ring: PunchingBag (tavandan zincirle asılı ağır kum torbası, toplam boy y=2.28 m)."""
    geo = Geo('PunchingBag')
    # Kum torbası gövdesi (alt y=0.0, üst y=1.18)
    geo.cylinder_u((0.0, 0.59, 0.0), 0.19, 1.12, 'M_ToolboxRed', segments=12)
    geo.cylinder_u((0.0, 0.04, 0.0), 0.17, 0.08, 'M_Anthracite', segments=12)
    geo.cylinder_u((0.0, 1.14, 0.0), 0.175, 0.08, 'M_Anthracite', segments=12)
    # Orta vurgu şeritleri
    geo.cylinder_u((0.0, 0.78, 0.0), 0.194, 0.09, 'M_SafetyOrange', segments=12)
    geo.cylinder_u((0.0, 0.38, 0.0), 0.194, 0.07, 'M_FridgeWhite', segments=12)
    # 4 askı kolonu, orta halka ve tavana kadar (y=2.28) uzanan çelik zincir + tavan flanşı
    for ang_deg in (0, 90, 180, 270):
        rad = math.radians(ang_deg)
        sx, sz = 0.08 * math.cos(rad), 0.08 * math.sin(rad)
        geo.box_u((sx, 1.28, sz), (0.024, 0.24, 0.024), 'M_ToolSteel', bevel=0.003)
    geo.cylinder_u((0.0, 1.72, 0.0), 0.022, 0.96, 'M_ToolSteel', segments=8)
    for cy in (1.48, 1.66, 1.84, 2.02, 2.16):
        geo.box_u((0.0, cy, 0.0), (0.048, 0.065, 0.048), 'M_GalvanizedSteel', bevel=0.008, rot_y_deg=cy * 90)
    geo.cylinder_u((0.0, 2.255, 0.0), 0.11, 0.05, 'M_Anthracite', segments=10)
    return geo.finish((0.0, 0.0, 0.0)), (0.40, 2.28, 0.40)


def build_corner_stool():
    """Spor/Ring: CornerStool (ring köşesi için üç ayaklı boksör taburesi, y≈0.34 m)."""
    geo = Geo('CornerStool')
    geo.cylinder_u((0.0, 0.31, 0.0), 0.155, 0.055, 'M_ToolboxLipRed', segments=12)
    geo.cylinder_u((0.0, 0.275, 0.0), 0.14, 0.02, 'M_Oak', segments=12)
    for i in range(3):
        ang = 2 * math.pi * i / 3
        lx, lz = 0.095 * math.sin(ang), 0.095 * math.cos(ang)
        geo.box_u((lx, 0.14, lz), (0.032, 0.28, 0.032), 'M_TimberEdge', bevel=0.004)
    geo.cylinder_u((0.0, 0.11, 0.0), 0.105, 0.022, 'M_Anthracite', segments=10)
    return geo.finish((0.0, 0.0, 0.0)), (0.32, 0.34, 0.32)


def build_towel_stack():
    """Spor/Soyunma: TowelStack (katlanmış renkli spor havluları yığını, y≈0.18 m)."""
    geo = Geo('TowelStack')
    geo.box_u((0.0, 0.03, 0.0), (0.32, 0.058, 0.24), 'M_FridgeWhite', bevel=0.018)
    geo.box_u((0.01, 0.085, -0.01), (0.30, 0.055, 0.23), 'M_RingBlue', bevel=0.018, rot_y_deg=6.0)
    geo.box_u((-0.01, 0.14, 0.01), (0.28, 0.052, 0.21), 'M_SafetyOrange', bevel=0.016, rot_y_deg=-5.0)
    return geo.finish((0.0, 0.0, 0.0)), (0.34, 0.18, 0.26)


def build_gym_bag():
    """Spor/Soyunma: GymBag (fermuarlı ve askılı silindirik spor çantası, y≈0.28 m)."""
    geo = Geo('GymBag')
    geo.cylinder_u((0.0, 0.13, 0.0), 0.13, 0.48, 'M_ServiceBlue', axis_u=(1, 0, 0), segments=10)
    for sx in (-0.23, 0.23):
        geo.cylinder_u((sx, 0.13, 0.0), 0.125, 0.04, 'M_SafetyOrange', axis_u=(1, 0, 0), segments=10)
    # Üst fermuar şeridi ve taşıma kulpları
    geo.box_u((0.0, 0.255, 0.0), (0.42, 0.016, 0.03), 'M_CharcoalABS', bevel=0.003)
    for sz in (-0.06, 0.06):
        geo.box_u((0.0, 0.21, sz), (0.22, 0.14, 0.024), 'M_Anthracite', bevel=0.004)
    # Ön cep
    geo.box_u((0.0, 0.11, 0.125), (0.20, 0.11, 0.035), 'M_TealMetal', bevel=0.008)
    return geo.finish((0.0, 0.0, 0.0)), (0.52, 0.28, 0.30)


def build_shoe_pair():
    """Spor/Soyunma: ShoePair (bir çift stilize spor/boks ayakkabısı, y≈0.14 m)."""
    geo = Geo('ShoePair')
    for sx, rot_y in [(-0.085, 6.0), (0.085, -10.0)]:
        # Beyaz kauçuk taban
        geo.box_u((sx, 0.016, 0.0), (0.105, 0.032, 0.26), 'M_FridgeWhite', bevel=0.008, rot_y_deg=rot_y)
        # Gövde ve bilek kısmı
        geo.box_u((sx, 0.055, -0.01), (0.098, 0.055, 0.23), 'M_SafetyOrange', bevel=0.015, rot_y_deg=rot_y)
        geo.box_u((sx, 0.095, -0.055), (0.090, 0.060, 0.11), 'M_Anthracite', bevel=0.012, rot_y_deg=rot_y)
        # Ön burun ve bağcık şeridi
        geo.box_u((sx, 0.048, 0.085), (0.092, 0.038, 0.065), 'M_FridgeWhite', bevel=0.010, rot_y_deg=rot_y)
    return geo.finish((0.0, 0.0, 0.0)), (0.30, 0.14, 0.28)


def build_poster_a():
    """Duvar: PosterA (yazısız, stilize kurtarma aracı ve şehir silüeti temalı poster, y≈0.68 m)."""
    geo = Geo('PosterA')
    w, h = 0.52, 0.68
    geo.box_u((0.0, h * 0.5, 0.0), (w, h, 0.020), 'M_Anthracite', bevel=0.004)
    geo.box_u((0.0, h * 0.5, 0.009), (w - 0.05, h - 0.05, 0.008), 'M_CreamPaper', bevel=0.001)
    # Arka plan gökyüzü ve şehir kuleleri silüeti (yazısız)
    geo.box_u((0.0, 0.44, 0.014), (w - 0.09, 0.30, 0.005), 'M_MutedBlue', bevel=0.0)
    for bx, bh, bw in [(-0.14, 0.18, 0.09), (-0.03, 0.24, 0.10), (0.09, 0.16, 0.08), (0.16, 0.21, 0.07)]:
        geo.box_u((bx, 0.29 + bh * 0.5, 0.016), (bw, bh, 0.006), 'M_WallBlue', bevel=0.0)
    # Ön planda stilize turuncu kurtarma minibüsü silüeti
    geo.box_u((0.0, 0.18, 0.018), (0.28, 0.11, 0.008), 'M_SafetyOrange', bevel=0.003)
    geo.box_u((0.05, 0.20, 0.020), (0.12, 0.05, 0.006), 'M_CreamPaper', bevel=0.001)
    for wx in (-0.08, 0.08):
        geo.box_u((wx, 0.12, 0.020), (0.05, 0.05, 0.008), 'M_CharcoalABS', bevel=0.008)
    return geo.finish((0.0, 0.0, 0.0)), (0.52, 0.68, 0.03)


def build_poster_b():
    """Duvar: PosterB (yazısız, soyut geometrik halkalar ve boks/enerji temalı poster, y≈0.64 m)."""
    geo = Geo('PosterB')
    w, h = 0.50, 0.64
    geo.box_u((0.0, h * 0.5, 0.0), (w, h, 0.020), 'M_TimberEdge', bevel=0.004)
    geo.box_u((0.0, h * 0.5, 0.009), (w - 0.05, h - 0.05, 0.008), 'M_CreamPaper', bevel=0.001)
    # Soyut geometrik renk blokları ve enerji diyagonalleri (yazısız)
    geo.box_u((-0.07, 0.38, 0.015), (0.24, 0.24, 0.006), 'M_SafetyOrange', bevel=0.020)
    geo.box_u((0.08, 0.24, 0.016), (0.22, 0.22, 0.006), 'M_RingBlue', bevel=0.018)
    geo.box_u((0.0, 0.32, 0.018), (0.34, 0.05, 0.006), 'M_ToolboxLipRed', bevel=0.002, rot_z_deg=28.0)
    geo.box_u((0.0, 0.26, 0.018), (0.30, 0.035, 0.006), 'M_SafetyOchre', bevel=0.002, rot_z_deg=28.0)
    return geo.finish((0.0, 0.0, 0.0)), (0.50, 0.64, 0.03)


def build_wall_clock():
    """Duvar: WallClock (yuvarlak çerçeveli, rakamsız/yazısız duvar saati, y≈0.34 m)."""
    geo = Geo('WallClock')
    geo.cylinder_u((0.0, 0.17, 0.0), 0.17, 0.045, 'M_ToolboxRed', axis_u=(0, 0, 1), segments=16)
    geo.cylinder_u((0.0, 0.17, 0.016), 0.145, 0.020, 'M_CreamPaper', axis_u=(0, 0, 1), segments=16)
    # 4 ana yön çizgisi ve akrep/yelkovan (yazı/rakam yok)
    for sx, sy, sw, sh in [(0.0, 0.285, 0.012, 0.03), (0.0, 0.055, 0.012, 0.03), (-0.115, 0.17, 0.03, 0.012), (0.115, 0.17, 0.03, 0.012)]:
        geo.box_u((sx, sy, 0.026), (sw, sh, 0.006), 'M_Anthracite', bevel=0.0)
    geo.box_u((0.0, 0.21, 0.028), (0.014, 0.09, 0.006), 'M_Anthracite', bevel=0.001)
    geo.box_u((0.035, 0.17, 0.029), (0.075, 0.012, 0.006), 'M_SafetyOrange', bevel=0.001)
    return geo.finish((0.0, 0.0, 0.0)), (0.34, 0.34, 0.05)


def build_wall_board():
    """Duvar: WallBoard (mantar/not panosu ve renkli kağıt kartları, yazı/logo yok, y≈0.68 m)."""
    geo = Geo('WallBoard')
    w, h = 0.96, 0.68
    geo.box_u((0.0, h * 0.5, 0.0), (w, h, 0.028), 'M_TimberEdge', bevel=0.005)
    geo.box_u((0.0, h * 0.5, 0.012), (w - 0.07, h - 0.07, 0.012), 'M_Fibreboard', bevel=0.002)
    # İğnelenmiş renkli kağıt ve şema kartları (yazısız)
    notes = [
        ((-0.28, 0.44), (0.18, 0.20), 'M_CreamPaper', 5.0),
        ((-0.05, 0.46), (0.16, 0.16), 'M_SafetyOchre', -6.0),
        ((0.22, 0.43), (0.22, 0.18), 'M_CreamPaper', 3.0),
        ((-0.22, 0.21), (0.15, 0.15), 'M_SafetyOrange', -8.0),
        ((0.04, 0.22), (0.20, 0.16), 'M_RingBlue', 4.0),
        ((0.28, 0.20), (0.14, 0.16), 'M_CreamPaper', -5.0),
    ]
    for (nx, ny), (nw, nh), nmat, tilt in notes:
        geo.box_u((nx, ny, 0.020), (nw, nh, 0.006), nmat, bevel=0.001, rot_z_deg=tilt)
        geo.box_u((nx, ny + nh * 0.36, 0.025), (0.018, 0.018, 0.010), 'M_ToolboxLipRed', bevel=0.002)
    return geo.finish((0.0, 0.0, 0.0)), (0.96, 0.68, 0.04)


ASSET_BUILDERS = [
    ('Desk', build_desk),
    ('Chair', build_chair),
    ('Monitor', build_monitor),
    ('PC', build_pc),
    ('FilingCabinet', build_filing_cabinet),
    ('DocumentShelf', build_document_shelf),
    ('CityMapBoard', build_city_map_board),
    ('Clipboard', build_clipboard),
    ('Plant', build_plant),
    ('Rug', build_rug),
    ('Mug', build_mug),
    ('Sofa', build_sofa),
    ('SideTable', build_side_table),
    ('KitchenCounter', build_kitchen_counter),
    ('CoffeeMachine', build_coffee_machine),
    ('Fridge', build_fridge),
    ('PictureFrame', build_picture_frame),
    ('MetalShelf', build_metal_shelf),
    ('RepairBench', build_repair_bench),
    ('Pegboard', build_pegboard),
    ('LockerClosed', build_locker_closed),
    ('LockerOpen', build_locker_open),
    ('Bench', build_bench),
    ('HangingUniform', build_hanging_uniform),
    ('BoxingRing', build_boxing_ring),
    ('PaperStack', build_paper_stack),
    ('CardboardBox', build_cardboard_box),
    ('PenHolder', build_pen_holder),
    ('TrashBin', build_trash_bin),
    ('FloorCables', build_floor_cables),
    ('DiningTable', build_dining_table),
    ('Stool', build_stool),
    ('WaterDispenser', build_water_dispenser),
    ('WallCabinet', build_wall_cabinet),
    ('CupShelf', build_cup_shelf),
    ('PunchingBag', build_punching_bag),
    ('CornerStool', build_corner_stool),
    ('TowelStack', build_towel_stack),
    ('GymBag', build_gym_bag),
    ('ShoePair', build_shoe_pair),
    ('PosterA', build_poster_a),
    ('PosterB', build_poster_b),
    ('WallClock', build_wall_clock),
    ('WallBoard', build_wall_board),
]

# Küçük süs/dağınıklık ve yüksek duvar eşyaları için collider null bırakılır
NULL_COLLIDER_ASSETS = {
    'Clipboard', 'Mug', 'PaperStack', 'PenHolder', 'FloorCables',
    'TowelStack', 'ShoePair', 'HangingUniform',
    'CityMapBoard', 'PictureFrame', 'Pegboard',
    'WallCabinet', 'CupShelf', 'PosterA', 'PosterB', 'WallClock', 'WallBoard',
}


# ==============================================================================
# 3. YERLEŞİM PLANI, KAPI BOŞLUKLARI VE DUVAR ÇARPIŞMA KUTULARI (office_layout.json)
# ==============================================================================

LAYOUT_PLACEMENTS = [
    # Ana Ofis (x[-4.8, 0], z[-5.2, 1.2]) - kapı önleri, pencere (z=-5.2, x[-2.0, -0.4]) ve >=1.2 m koridorlar açık
    {'id': 'Desk_01', 'asset': 'Desk', 'room': 'Office', 'pos': [-1.05, 0.0, -0.10], 'rot_y': 180.0},
    {'id': 'Chair_01', 'asset': 'Chair', 'room': 'Office', 'pos': [-1.05, 0.0, 0.48], 'rot_y': 180.0},
    {'id': 'Monitor_01', 'asset': 'Monitor', 'room': 'Office', 'pos': [-1.05, 0.54, -0.02], 'rot_y': 0.0},
    {'id': 'Clipboard_01', 'asset': 'Clipboard', 'room': 'Office', 'pos': [-0.68, 0.54, -0.18], 'rot_y': 15.0},
    {'id': 'Mug_01', 'asset': 'Mug', 'room': 'Office', 'pos': [-1.42, 0.54, -0.16], 'rot_y': 35.0},
    {'id': 'PenHolder_01', 'asset': 'PenHolder', 'room': 'Office', 'pos': [-1.48, 0.54, 0.06], 'rot_y': 10.0},
    {'id': 'PaperStack_01', 'asset': 'PaperStack', 'room': 'Office', 'pos': [-0.62, 0.54, 0.08], 'rot_y': -12.0},
    {'id': 'PC_01', 'asset': 'PC', 'room': 'Office', 'pos': [-0.30, 0.0, -0.10], 'rot_y': 180.0},
    {'id': 'TrashBin_01', 'asset': 'TrashBin', 'room': 'Office', 'pos': [-1.82, 0.0, -0.12], 'rot_y': 0.0},
    {'id': 'CardboardBox_01', 'asset': 'CardboardBox', 'room': 'Office', 'pos': [-0.38, 0.0, -0.68], 'rot_y': 165.0},
    {'id': 'FloorCables_01', 'asset': 'FloorCables', 'room': 'Office', 'pos': [-0.68, 0.0, -0.52], 'rot_y': 12.0},
    {'id': 'CityMapBoard_01', 'asset': 'CityMapBoard', 'room': 'Office', 'pos': [-0.12, 1.02, -0.10], 'rot_y': 270.0},
    {'id': 'PosterA_01', 'asset': 'PosterA', 'room': 'Office', 'pos': [-0.12, 1.18, -1.45], 'rot_y': 270.0},
    {'id': 'WallBoard_01', 'asset': 'WallBoard', 'room': 'Office', 'pos': [-0.95, 1.16, 1.09], 'rot_y': 180.0},
    {'id': 'WallClock_01', 'asset': 'WallClock', 'room': 'Office', 'pos': [-4.77, 1.92, -0.65], 'rot_y': 90.0},
    {'id': 'DocumentShelf_01', 'asset': 'DocumentShelf', 'room': 'Office', 'pos': [-4.50, 0.0, 0.15], 'rot_y': 90.0},
    {'id': 'FilingCabinet_01', 'asset': 'FilingCabinet', 'room': 'Office', 'pos': [-4.42, 0.0, 0.82], 'rot_y': 90.0},
    {'id': 'FilingCabinet_02', 'asset': 'FilingCabinet', 'room': 'Office', 'pos': [-4.42, 0.0, -3.55], 'rot_y': 90.0},
    {'id': 'PaperStack_02', 'asset': 'PaperStack', 'room': 'Office', 'pos': [-4.38, 0.0, -0.48], 'rot_y': 75.0},
    {'id': 'PaperStack_03', 'asset': 'PaperStack', 'room': 'Office', 'pos': [-4.42, 1.48, -3.55], 'rot_y': 82.0},
    {'id': 'CardboardBox_02', 'asset': 'CardboardBox', 'room': 'Office', 'pos': [-3.85, 0.0, 0.86], 'rot_y': 15.0},
    {'id': 'TrashBin_02', 'asset': 'TrashBin', 'room': 'Office', 'pos': [-4.42, 0.0, -4.12], 'rot_y': 20.0},
    {'id': 'FloorCables_02', 'asset': 'FloorCables', 'room': 'Office', 'pos': [-4.12, 0.0, -3.05], 'rot_y': 95.0},
    {'id': 'Plant_01', 'asset': 'Plant', 'room': 'Office', 'pos': [-2.28, 0.0, -4.85], 'rot_y': 0.0},
    {'id': 'Plant_02', 'asset': 'Plant', 'room': 'Office', 'pos': [-4.35, 0.0, -4.75], 'rot_y': 45.0},
    {'id': 'Rug_01', 'asset': 'Rug', 'room': 'Office', 'pos': [-2.15, 0.0, -1.95], 'rot_y': 0.0},

    # Mutfak (x[-9.8, -4.8], z[-5.2, 1.2]) - oturma alanı bir arada, kanepe odaya (+Z) bakar
    {'id': 'KitchenCounter_01', 'asset': 'KitchenCounter', 'room': 'Kitchen', 'pos': [-9.42, 0.0, -1.05], 'rot_y': 90.0},
    {'id': 'CoffeeMachine_01', 'asset': 'CoffeeMachine', 'room': 'Kitchen', 'pos': [-9.40, 0.60, -0.60], 'rot_y': 90.0},
    {'id': 'WallCabinet_01', 'asset': 'WallCabinet', 'room': 'Kitchen', 'pos': [-9.60, 1.38, -1.05], 'rot_y': 90.0},
    {'id': 'Fridge_01', 'asset': 'Fridge', 'room': 'Kitchen', 'pos': [-9.40, 0.0, 0.35], 'rot_y': 90.0},
    {'id': 'WaterDispenser_01', 'asset': 'WaterDispenser', 'room': 'Kitchen', 'pos': [-8.75, 0.0, 0.85], 'rot_y': 180.0},
    {'id': 'CupShelf_01', 'asset': 'CupShelf', 'room': 'Kitchen', 'pos': [-5.80, 1.32, 1.00], 'rot_y': 180.0},
    {'id': 'TrashBin_03', 'asset': 'TrashBin', 'room': 'Kitchen', 'pos': [-9.45, 0.0, -2.18], 'rot_y': 90.0},
    {'id': 'Sofa_01', 'asset': 'Sofa', 'room': 'Kitchen', 'pos': [-7.35, 0.0, -4.70], 'rot_y': 0.0},
    {'id': 'SideTable_01', 'asset': 'SideTable', 'room': 'Kitchen', 'pos': [-6.15, 0.0, -4.65], 'rot_y': 0.0},
    {'id': 'DiningTable_01', 'asset': 'DiningTable', 'room': 'Kitchen', 'pos': [-7.35, 0.0, -3.35], 'rot_y': 0.0},
    {'id': 'Stool_01', 'asset': 'Stool', 'room': 'Kitchen', 'pos': [-7.65, 0.0, -2.72], 'rot_y': 180.0},
    {'id': 'Stool_02', 'asset': 'Stool', 'room': 'Kitchen', 'pos': [-7.05, 0.0, -2.72], 'rot_y': 180.0},
    {'id': 'Stool_03', 'asset': 'Stool', 'room': 'Kitchen', 'pos': [-8.18, 0.0, -3.35], 'rot_y': 90.0},
    {'id': 'PictureFrame_01', 'asset': 'PictureFrame', 'room': 'Kitchen', 'pos': [-7.35, 1.15, -5.16], 'rot_y': 0.0},
    {'id': 'WallClock_02', 'asset': 'WallClock', 'room': 'Kitchen', 'pos': [-4.91, 1.85, -3.60], 'rot_y': 270.0},
    {'id': 'PosterB_01', 'asset': 'PosterB', 'room': 'Kitchen', 'pos': [-4.91, 1.18, -0.35], 'rot_y': 270.0},

    # Depo (x[-4.8, 0], z[1.2, 5.2]) - raflar duvar boyunca, pencere yok
    {'id': 'MetalShelf_01', 'asset': 'MetalShelf', 'room': 'Storage', 'pos': [-3.85, 0.0, 4.82], 'rot_y': 180.0},
    {'id': 'MetalShelf_02', 'asset': 'MetalShelf', 'room': 'Storage', 'pos': [-2.40, 0.0, 4.82], 'rot_y': 180.0},
    {'id': 'MetalShelf_03', 'asset': 'MetalShelf', 'room': 'Storage', 'pos': [-0.95, 0.0, 4.82], 'rot_y': 180.0},
    {'id': 'RepairBench_01', 'asset': 'RepairBench', 'room': 'Storage', 'pos': [-4.34, 0.0, 2.65], 'rot_y': 90.0},
    {'id': 'Pegboard_01', 'asset': 'Pegboard', 'room': 'Storage', 'pos': [-4.68, 0.98, 2.65], 'rot_y': 90.0},
    {'id': 'CardboardBox_03', 'asset': 'CardboardBox', 'room': 'Storage', 'pos': [-4.25, 0.0, 4.15], 'rot_y': 20.0},
    {'id': 'CardboardBox_04', 'asset': 'CardboardBox', 'room': 'Storage', 'pos': [-0.85, 0.0, 4.18], 'rot_y': -15.0},
    {'id': 'FloorCables_03', 'asset': 'FloorCables', 'room': 'Storage', 'pos': [-4.15, 0.0, 1.75], 'rot_y': 85.0},
    {'id': 'WallBoard_02', 'asset': 'WallBoard', 'room': 'Storage', 'pos': [-0.95, 1.18, 1.31], 'rot_y': 0.0},

    # Soyunma (x[-9.8, -4.8], z[1.2, 5.2]) - batı penceresi z[2.4, 4.0] açık, spor eşyaları serpiştirilmiş
    {'id': 'LockerClosed_01', 'asset': 'LockerClosed', 'room': 'LockerRoom', 'pos': [-9.50, 0.0, 1.65], 'rot_y': 90.0},
    {'id': 'LockerClosed_02', 'asset': 'LockerClosed', 'room': 'LockerRoom', 'pos': [-9.50, 0.0, 4.55], 'rot_y': 90.0},
    {'id': 'LockerClosed_03', 'asset': 'LockerClosed', 'room': 'LockerRoom', 'pos': [-5.18, 0.0, 2.50], 'rot_y': 270.0},
    {'id': 'LockerOpen_01', 'asset': 'LockerOpen', 'room': 'LockerRoom', 'pos': [-5.18, 0.0, 3.10], 'rot_y': 270.0},
    {'id': 'HangingUniform_01', 'asset': 'HangingUniform', 'room': 'LockerRoom', 'pos': [-5.18, 0.52, 3.10], 'rot_y': 270.0},
    {'id': 'LockerClosed_04', 'asset': 'LockerClosed', 'room': 'LockerRoom', 'pos': [-5.18, 0.0, 1.90], 'rot_y': 270.0},
    {'id': 'LockerClosed_05', 'asset': 'LockerClosed', 'room': 'LockerRoom', 'pos': [-5.18, 0.0, 3.70], 'rot_y': 270.0},
    {'id': 'Bench_01', 'asset': 'Bench', 'room': 'LockerRoom', 'pos': [-8.85, 0.0, 3.20], 'rot_y': 90.0},
    {'id': 'TowelStack_01', 'asset': 'TowelStack', 'room': 'LockerRoom', 'pos': [-8.85, 0.32, 2.85], 'rot_y': 15.0},
    {'id': 'GymBag_01', 'asset': 'GymBag', 'room': 'LockerRoom', 'pos': [-8.85, 0.0, 2.22], 'rot_y': 25.0},
    {'id': 'ShoePair_01', 'asset': 'ShoePair', 'room': 'LockerRoom', 'pos': [-5.65, 0.0, 2.50], 'rot_y': 260.0},
    {'id': 'ShoePair_02', 'asset': 'ShoePair', 'room': 'LockerRoom', 'pos': [-8.85, 0.0, 4.02], 'rot_y': 80.0},
    {'id': 'PosterA_02', 'asset': 'PosterA', 'room': 'LockerRoom', 'pos': [-5.85, 1.25, 1.31], 'rot_y': 0.0},
    {'id': 'PosterB_02', 'asset': 'PosterB', 'room': 'LockerRoom', 'pos': [-5.85, 1.25, 5.09], 'rot_y': 180.0},

    # Ring Odası (x[-9.8, 0], z[5.2, 12.0]) - ring ortada, kum torbası tavandan zincirli, 2 köşe taburesi
    {'id': 'BoxingRing_01', 'asset': 'BoxingRing', 'room': 'RingRoom', 'pos': [-4.90, 0.0, 8.60], 'rot_y': 0.0},
    {'id': 'CornerStool_01', 'asset': 'CornerStool', 'room': 'RingRoom', 'pos': [-6.65, 0.35, 6.85], 'rot_y': 45.0},
    {'id': 'CornerStool_02', 'asset': 'CornerStool', 'room': 'RingRoom', 'pos': [-3.15, 0.35, 10.35], 'rot_y': 225.0},
    {'id': 'PunchingBag_01', 'asset': 'PunchingBag', 'room': 'RingRoom', 'pos': [-1.25, 0.96, 9.80], 'rot_y': 0.0},
    {'id': 'GymBag_02', 'asset': 'GymBag', 'room': 'RingRoom', 'pos': [-9.25, 0.0, 6.40], 'rot_y': 75.0},
    {'id': 'TowelStack_02', 'asset': 'TowelStack', 'room': 'RingRoom', 'pos': [-4.90, 0.35, 6.38], 'rot_y': 0.0},
    {'id': 'ShoePair_03', 'asset': 'ShoePair', 'room': 'RingRoom', 'pos': [-1.25, 0.0, 11.20], 'rot_y': 210.0},
    {'id': 'PosterA_03', 'asset': 'PosterA', 'room': 'RingRoom', 'pos': [-2.20, 1.25, 11.97], 'rot_y': 180.0},
    {'id': 'PosterB_03', 'asset': 'PosterB', 'room': 'RingRoom', 'pos': [-7.80, 1.25, 11.97], 'rot_y': 180.0},
    {'id': 'WallBoard_03', 'asset': 'WallBoard', 'room': 'RingRoom', 'pos': [-0.15, 1.20, 7.20], 'rot_y': 270.0},
    {'id': 'WallClock_03', 'asset': 'WallClock', 'room': 'RingRoom', 'pos': [-0.15, 2.05, 8.60], 'rot_y': 270.0},
]

DOOR_OPENINGS = [
    {
        'id': 'Door_Front_Preserved',
        'type': 'preserved',
        'connects': ['Exterior', 'Office'],
        'center': [-3.18, 1.125, -5.18],
        'size': [1.08, 2.25, 0.24],
        'bounds_min': [-3.72, 0.0, -5.30],
        'bounds_max': [-2.64, 2.25, -5.06],
    },
    {
        'id': 'Door_Garage_Preserved',
        'type': 'preserved',
        'connects': ['Office', 'Garage'],
        'center': [0.0, 1.09, -2.88],
        'size': [0.16, 2.18, 1.04],
        'bounds_min': [-0.08, 0.0, -3.40],
        'bounds_max': [0.08, 2.18, -2.36],
    },
    {
        'id': 'Door_Office_Kitchen',
        'type': 'new_framed_doorless',
        'connects': ['Office', 'Kitchen'],
        'center': [-4.80, 1.09, -2.00],
        'size': [0.16, 2.18, 1.20],
        'bounds_min': [-4.88, 0.0, -2.60],
        'bounds_max': [-4.72, 2.18, -1.40],
    },
    {
        'id': 'Door_Office_Storage',
        'type': 'new_framed_doorless',
        'connects': ['Office', 'Storage'],
        'center': [-2.40, 1.09, 1.20],
        'size': [1.20, 2.18, 0.16],
        'bounds_min': [-3.00, 0.0, 1.12],
        'bounds_max': [-1.80, 2.18, 1.28],
    },
    {
        'id': 'Door_Kitchen_LockerRoom',
        'type': 'new_framed_doorless',
        'connects': ['Kitchen', 'LockerRoom'],
        'center': [-7.30, 1.09, 1.20],
        'size': [1.20, 2.18, 0.16],
        'bounds_min': [-7.90, 0.0, 1.12],
        'bounds_max': [-6.70, 2.18, 1.28],
    },
    {
        'id': 'Door_LockerRoom_RingRoom',
        'type': 'new_framed_doorless',
        'connects': ['LockerRoom', 'RingRoom'],
        'center': [-7.30, 1.09, 5.20],
        'size': [1.20, 2.18, 0.16],
        'bounds_min': [-7.90, 0.0, 5.12],
        'bounds_max': [-6.70, 2.18, 5.28],
    },
    {
        'id': 'Passage_Storage_Garage',
        'type': 'wide_framed_doorless',
        'connects': ['Storage', 'Garage'],
        'center': [0.0, 1.25, 2.60],
        'size': [0.24, 2.50, 2.00],
        'bounds_min': [-0.12, 0.0, 1.60],
        'bounds_max': [0.12, 2.50, 3.60],
    },
]


def build_wall_colliders():
    """Kapı boşluklarını açık bırakan eksen hizalı duvar BoxCollider listesini üretir."""
    boxes = []

    def add_seg(name, x0, y0, z0, x1, y1, z1):
        boxes.append({
            'name': name,
            'center': [round((x0 + x1) * 0.5, 4), round((y0 + y1) * 0.5, 4), round((z0 + z1) * 0.5, 4)],
            'size': [round(abs(x1 - x0), 4), round(abs(y1 - y0), 4), round(abs(z1 - z0), 4)],
        })

    # Dış batı duvarı (x=-9.8, z[-5.2, 12.0])
    add_seg('Wall_West_Exterior', -10.04, 0.0, -5.2, -9.80, 3.24, 12.0)
    # Dış kuzey duvarı (z=12.0, x[-9.8, 0.0])
    add_seg('Wall_North_Exterior', -9.8, 0.0, 12.0, 0.0, 3.24, 12.24)
    # Güney dış duvarı (z=-5.2, ön kapı x[-3.72, -2.64] boşluklu)
    add_seg('Wall_South_Kitchen', -9.8, 0.0, -5.44, -4.8, 3.24, -5.20)
    add_seg('Wall_South_Office_W', -4.8, 0.0, -5.42, -3.72, 3.24, -5.18)
    add_seg('Wall_South_Office_E', -2.64, 0.0, -5.42, 0.0, 3.24, -5.18)
    add_seg('Wall_South_Office_Lintel', -3.72, 2.25, -5.42, -2.64, 3.24, -5.18)
    # İç duvar x=-4.8 (z[-5.2, 5.2], ofis-mutfak kapısı z[-2.6, -1.4] boşluklu)
    add_seg('Wall_X48_South', -4.88, 0.0, -5.2, -4.72, 3.24, -2.60)
    add_seg('Wall_X48_DoorLintel', -4.88, 2.18, -2.60, -4.72, 3.24, -1.40)
    add_seg('Wall_X48_North', -4.88, 0.0, -1.40, -4.72, 3.24, 5.20)
    # İç duvar z=1.2 (x[-9.8, 0.0], iki kapı boşluklu: x[-7.9, -6.7] ve x[-3.0, -1.8])
    add_seg('Wall_Z12_W', -9.8, 0.0, 1.12, -7.90, 3.24, 1.28)
    add_seg('Wall_Z12_DoorKitchenLintel', -7.90, 2.18, 1.12, -6.70, 3.24, 1.28)
    add_seg('Wall_Z12_Mid', -6.70, 0.0, 1.12, -3.00, 3.24, 1.28)
    add_seg('Wall_Z12_DoorOfficeLintel', -3.00, 2.18, 1.12, -1.80, 3.24, 1.28)
    add_seg('Wall_Z12_E', -1.80, 0.0, 1.12, 0.0, 3.24, 1.28)
    # İç duvar z=5.2 (x[-9.8, 0.0], soyunma-ring kapısı x[-7.9, -6.7] boşluklu)
    add_seg('Wall_Z52_W', -9.8, 0.0, 5.12, -7.90, 3.24, 5.28)
    add_seg('Wall_Z52_DoorRingLintel', -7.90, 2.18, 5.12, -6.70, 3.24, 5.28)
    add_seg('Wall_Z52_E', -6.70, 0.0, 5.12, 0.0, 3.24, 5.28)
    # Doğu duvarı x=0 (ofis kaplama, depo-garaj z[1.6, 3.6] geçişli y<=4.0 duvar ve ring doğu duvarı)
    add_seg('Wall_East_Office_S', -0.095, 0.0, -5.2, -0.065, 3.24, -3.40)
    add_seg('Wall_East_Office_Lintel', -0.095, 2.18, -3.40, -0.065, 3.24, -2.36)
    add_seg('Wall_East_Office_N', -0.095, 0.0, -2.36, -0.065, 3.24, 1.20)
    add_seg('Wall_East_Storage_S', -0.12, 0.0, 1.20, 0.12, 4.00, 1.60)
    add_seg('Wall_East_Storage_Lintel', -0.12, 2.50, 1.60, 0.12, 4.00, 3.60)
    add_seg('Wall_East_Storage_N', -0.12, 0.0, 3.60, 0.12, 4.00, 5.20)
    add_seg('Wall_East_RingRoom', -0.12, 0.0, 5.20, 0.12, 3.24, 12.00)
    return boxes


# ==============================================================================
# 4. FBX DIŞA AKTARMA VE SAHNE YERLEŞİMİ
# ==============================================================================

def export_fbx_objects(objects, filepath):
    """Seçilen nesneleri build_minivan.py ile birebir aynı FBX ayarlarıyla dışa aktarır."""
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=str(filepath),
        use_selection=True,
        object_types={'EMPTY', 'MESH'},
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        mesh_smooth_type='FACE',
        use_mesh_modifiers=False,
        bake_anim=False,
        use_custom_props=True,
        check_existing=False,
        add_leaf_bones=False,
        path_mode='RELATIVE',
    )


def place_scene_instances(asset_objects):
    """Render ve .blend sahnesi için mobilyaları office_layout.json konumlarına yerleştirir."""
    placed = []
    used_assets = set()
    for item in LAYOUT_PLACEMENTS:
        asset_name = item['asset']
        base_obj = asset_objects[asset_name]
        if asset_name not in used_assets:
            obj = base_obj
            used_assets.add(asset_name)
        else:
            obj = bpy.data.objects.new(item['id'], base_obj.data)
            bpy.context.collection.objects.link(obj)
        ux, uy, uz = item['pos']
        obj.location = u2b_point(ux, uy, uz)
        obj.rotation_euler = (0.0, 0.0, -math.radians(item['rot_y']))
        placed.append(obj)
    bpy.context.view_layer.update()
    return placed


# ==============================================================================
# 5. GÖZ HİZASI (1.09 m) ODA RENDER'LARI VE ÇATISIZ İZOMETRİK RENDER
# ==============================================================================

def render_office_views(render_dir, ceiling_objects):
    """Her oda için y=1.09 m göz hizası PNG ve tüm yapı için çatısız izometrik PNG üretir."""
    render_dir = Path(render_dir)
    render_dir.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 64
    scene.cycles.use_denoising = True
    scene.cycles.seed = 0
    scene.cycles.use_animated_seed = False
    scene.cycles.max_bounces = 6
    scene.cycles.adaptive_threshold = 0.03

    prefs = bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type = 'OPTIX'
    prefs.refresh_devices()
    gpu = [d for d in prefs.devices if d.type == 'OPTIX']
    for device in prefs.devices:
        device.use = device.type == 'OPTIX'
    scene.cycles.device = 'GPU' if gpu else 'CPU'

    scene.render.resolution_x = 1920
    scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium High Contrast'
    scene.view_settings.exposure = -0.35

    world = bpy.data.worlds.new('Office_World')
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get('Background')
    bg.inputs[0].default_value = (0.58, 0.50, 0.40, 1.0)
    bg.inputs[1].default_value = 0.45

    render_helpers = []
    # Oda içi sıcak tavan ışıkları + genel güneş/alan ışığı (duvarları patlatmayan sıcak aydınlatma)
    room_lights = [
        ('Light_Office', (-2.4, 2.92, -2.0), 115.0, 3.4),
        ('Light_Kitchen', (-7.3, 2.92, -2.0), 115.0, 3.4),
        ('Light_Storage', (-2.4, 2.92, 3.2), 105.0, 3.0),
        ('Light_Locker', (-7.3, 2.92, 3.2), 105.0, 3.0),
        ('Light_Ring_1', (-6.8, 2.92, 8.6), 140.0, 4.0),
        ('Light_Ring_2', (-3.0, 2.92, 8.6), 140.0, 4.0),
        ('Light_IsoKey', (6.0, 14.0, -6.0), 820.0, 9.0),
    ]
    for lname, lpos_u, power, lsize in room_lights:
        ldata = bpy.data.lights.new(lname, 'AREA')
        ldata.energy = power
        ldata.shape = 'DISK'
        ldata.size = lsize
        ldata.color = (1.0, 0.86, 0.68)
        lobj = bpy.data.objects.new(lname, ldata)
        bpy.context.collection.objects.link(lobj)
        lobj.location = u2b_point(*lpos_u)
        render_helpers.append(lobj)

    cam_data = bpy.data.cameras.new('RENDER_Camera')
    cam_obj = bpy.data.objects.new('RENDER_Camera', cam_data)
    bpy.context.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    render_helpers.append(cam_obj)

    # 5 oda için chibi göz hizası (y = 1.09 m) kameraları
    eye_views = [
        ('room_office_eye', (-3.35, 1.09, -4.35), (-1.20, 0.95, -0.10), 24.0),
        ('room_kitchen_eye', (-5.45, 1.09, -1.90), (-8.20, 0.85, -2.85), 24.0),
        ('room_storage_eye', (-0.75, 1.09, 1.85), (-3.10, 0.95, 3.85), 24.0),
        ('room_locker_eye', (-7.30, 1.09, 1.50), (-7.30, 0.92, 3.40), 24.0),
        ('room_ring_eye', (-8.40, 1.09, 5.75), (-4.60, 0.95, 8.80), 24.0),
    ]
    durations = {}
    for vname, pos_u, tgt_u, lens in eye_views:
        cam_data.type = 'PERSP'
        cam_data.lens = lens
        cam_obj.location = u2b_point(*pos_u)
        cam_obj.rotation_euler = (u2b_point(*tgt_u) - cam_obj.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(render_dir / f'{vname}.png')
        t0 = time.perf_counter()
        bpy.ops.render.render(write_still=True)
        durations[vname] = round(time.perf_counter() - t0, 3)
        print('RENDER OK', vname, durations[vname], flush=True)

    # Bütün yapı için çatısız izometrik PNG (tavanlar ve armatürler geçici gizlenir)
    for cobj in ceiling_objects:
        cobj.hide_render = True
    try:
        cam_data.type = 'ORTHO'
        cam_data.ortho_scale = 21.5
        iso_pos = u2b_point(9.5, 16.5, -9.5)
        iso_tgt = u2b_point(-4.9, 0.8, 3.4)
        cam_obj.location = iso_pos
        cam_obj.rotation_euler = (iso_tgt - cam_obj.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(render_dir / 'isometric_roofless.png')
        t0 = time.perf_counter()
        bpy.ops.render.render(write_still=True)
        durations['isometric_roofless'] = round(time.perf_counter() - t0, 3)
        print('RENDER OK isometric_roofless', durations['isometric_roofless'], flush=True)
    finally:
        for cobj in ceiling_objects:
            cobj.hide_render = False
        for hobj in render_helpers:
            bpy.data.objects.remove(hobj, do_unlink=True)

    return durations


# ==============================================================================
# 6. ANA ÜRETİM AKIŞI
# ==============================================================================

def main():
    parser = argparse.ArgumentParser(description='Rescue Team çok odalı ofis ve mobilya üreticisi')
    parser.add_argument('--output-dir', type=str, default=str(DEFAULT_OUT), help='FBX ve JSON çıktı klasörü')
    parser.add_argument('--render-dir', type=str, default=None, help='Göz hizası ve izometrik PNG çıktı klasörü')
    parser.add_argument('--no-render', action='store_true', help='Render adımını atla')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])

    t_start = time.perf_counter()
    out_dir = Path(args.output_dir).resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    cache_dir = out_dir / 'cache'
    cache_dir.mkdir(exist_ok=True)
    for env_key in ('OPTIX_CACHE_PATH', 'CUDA_CACHE_PATH', 'TMP', 'TEMP'):
        os.environ[env_key] = str(cache_dir)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.context.preferences.filepaths.use_auto_save_temporary_files = False
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1.0

    make_materials()

    # 1. Her eşyayı ayrı FBX olarak (pivot taban ortası (0,0,0)) üret ve dışa aktar
    asset_objects = {}
    asset_bounds = {}
    triangle_counts = {}

    for asset_name, builder_fn in ASSET_BUILDERS:
        obj, size_u = builder_fn()
        obj.location = (0.0, 0.0, 0.0)
        obj.rotation_euler = (0.0, 0.0, 0.0)
        bpy.context.view_layer.update()
        tri_count = len(obj.data.polygons)
        if tri_count > 1500:
            raise ValueError(f'{asset_name} üçgen bütçesini aştı: {tri_count} > 1500')
        triangle_counts[asset_name] = tri_count
        asset_objects[asset_name] = obj
        asset_bounds[asset_name] = size_u
        export_fbx_objects([obj], out_dir / f'{asset_name}.fbx')

    # 2. Oda kabuklarını (Room_<Ad>_Floor/Walls/Windows/Ceiling + Fixtures) üret ve tek FBX (Office_Shell.fbx) olarak aktar
    shell_objects = []
    ceiling_objects = []
    rooms_meta = []

    for room in ROOMS_SPEC:
        rid = room['id']
        f_obj = build_room_floor(room)
        w_obj = build_room_walls(rid)
        win_obj = build_room_windows(rid)
        c_obj = build_room_ceiling(room)
        l_obj = build_room_fixtures(room)

        room_shell_list = [f_obj, w_obj, c_obj, l_obj]
        if win_obj is not None:
            room_shell_list.append(win_obj)
        for sobj in room_shell_list:
            triangle_counts[sobj.name] = len(sobj.data.polygons)
            shell_objects.append(sobj)
        ceiling_objects.extend([c_obj, l_obj])

        rooms_meta.append({
            'id': rid,
            'name_tr': room['name_tr'],
            'bounds_min': [room['x0'], 0.0, room['z0']],
            'bounds_max': [room['x1'], 3.16, room['z1']],
            'center': [round((room['x0'] + room['x1']) * 0.5, 4), 1.58, round((room['z0'] + room['z1']) * 0.5, 4)],
            'size': [round(room['x1'] - room['x0'], 4), 3.16, round(room['z1'] - room['z0'], 4)],
            'shell_objects': {
                'floor': f_obj.name,
                'walls': w_obj.name,
                'windows': win_obj.name if win_obj is not None else None,
                'ceiling': c_obj.name,
                'fixtures': l_obj.name,
            },
        })

    export_fbx_objects(shell_objects, out_dir / 'Office_Shell.fbx')

    # 3. Sahne yerleşimini kur ve office_layout.json dosyasını yaz
    place_scene_instances(asset_objects)

    layout_items = []
    for item in LAYOUT_PLACEMENTS:
        aname = item['asset']
        sx, sy, sz = asset_bounds[aname]
        collider = None if aname in NULL_COLLIDER_ASSETS else {
            'center': [0.0, round(sy * 0.5, 4), 0.0],
            'size': [round(sx, 4), round(sy, 4), round(sz, 4)],
        }
        entry = {
            'id': item['id'],
            'asset': aname,
            'fbx': f'{aname}.fbx',
            'room': item['room'],
            'position': [round(c, 4) for c in item['pos']],
            'rotation_y': round(float(item['rot_y']), 2),
            'box_collider': collider,
        }
        if aname == 'MetalShelf':
            entry['shelf_heights_y_m'] = METAL_SHELF_HEIGHTS
        if aname == 'CityMapBoard':
            entry['texture_path'] = CITY_MAP_REL
            entry['texture_found'] = CITY_MAP_FOUND
        layout_items.append(entry)

    layout_data = {
        'schema_version': 1,
        'units': 'metres',
        'coordinates': {'system': 'Unity', 'x': 'east(+), west(-)', 'y': 'up(+), floor=0', 'z': 'north/back(+), south/front(-)'},
        'character_eye_height_m': 1.09,
        'shell_fbx': 'Office_Shell.fbx',
        'wall_heights_m': {
            'floor_y': 0.0,
            'blue_band_height_m': 0.96,
            'inner_wall_height_m': 3.16,
            'ceiling_slab_min_y': 3.24,
            'ceiling_slab_max_y': 3.39,
            'storage_garage_wall_top_y': 4.0,
        },
        'metal_shelf_heights_m': METAL_SHELF_HEIGHTS,
        'city_map_texture': {'path': CITY_MAP_REL, 'found': CITY_MAP_FOUND},
        'rooms': rooms_meta,
        'door_openings': DOOR_OPENINGS,
        'wall_colliders': build_wall_colliders(),
        'items': layout_items,
    }
    (out_dir / 'office_layout.json').write_text(json.dumps(layout_data, indent=2, ensure_ascii=False), encoding='utf-8')

    # 4. İstenmişse (--render-dir veya varsayılan) göz hizası ve çatısız izometrik PNG'leri üret
    render_durations = {}
    if args.render_dir or not args.no_render:
        target_render_dir = Path(args.render_dir).resolve() if args.render_dir else (out_dir / 'renders')
        render_durations = render_office_views(target_render_dir, ceiling_objects)

    bpy.ops.wm.save_as_mainfile(filepath=str(out_dir / 'Office.blend'), check_existing=False)

    manifest = {
        'total_triangles': sum(triangle_counts.values()),
        'object_triangles': triangle_counts,
        'city_map_texture_found': CITY_MAP_FOUND,
        'render_seconds': render_durations,
        'total_build_seconds': round(time.perf_counter() - t_start, 3),
    }
    (out_dir / 'build_manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print('BUILD OK triangles:', manifest['total_triangles'], 'city_map:', CITY_MAP_FOUND, flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        print('BUILD FAILED', flush=True)
        sys.exit(1)
