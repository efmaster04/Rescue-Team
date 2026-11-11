# -*- coding: utf-8 -*-
"""
Donati seti: ofis kasabasinin sokak donatisi ve agaclari.

Calistirma:
  blender.exe -b --factory-startup -P build_donati.py              (FBX + JSON + .blend)
  blender.exe -b --factory-startup -P build_donati.py -- --no-export  (yalniz olcu/ucgen dokumu)

Kurallar: Kaynak/ORTAK_KURALLAR.md. Blender X = Unity X, Blender Y = Unity Z, Blender Z = Unity Y.
On yuz Blender -Y. Kollu direklerin kolu Blender -X. Orijin zemindeki taban noktasi (Z=0).
Butun nesneler donussuz/olceksiz; geometri dogrudan dunya koordinatinda kurulur.
.blend icinde nesne adlari "<varlik>|<ad>" bicimindedir (ayni adlar birden cok varlikta var);
FBX'e yazilirken gecici olarak sade ada ("Isik_Kirmizi_1", "COL_Direk" ...) cevrilir.
"""
import bpy
import bmesh
import math
import json
import random
import sys
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(r"C:\Users\uur\RescueTeam_OfisHaritasi")
OUT = ROOT / "Oyun" / "Assets" / "OfisHaritasi" / "Modeller" / "Donati"
BLEND = ROOT / "Kaynak" / "Blender" / "Donati.blend"
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
NO_EXPORT = "--no-export" in ARGS

SINK = 0.25          # direk/govde zemin alti gomme payi (egimli arazide bosluk kalmasin)
TAU = math.tau

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0

# ----------------------------------------------------------------------------------
# Malzemeler (renkler sRGB; Blender'a dogrusal donusumle girilir, JSON'a sRGB yazilir)
# ----------------------------------------------------------------------------------
M = {}
MAT_JSON = []


def srgb_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def defmat(name, color, metallic=0.0, smooth=0.3, emission=(0, 0, 0), preview_emit=0.0, **extra):
    m = bpy.data.materials.new(name)
    lin = [srgb_lin(c) for c in color]
    m.diffuse_color = (*lin, 1.0)
    bs = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value = (*lin, 1.0)
    bs.inputs['Metallic'].default_value = metallic
    bs.inputs['Roughness'].default_value = 1.0 - smooth
    if any(emission):
        bs.inputs['Emission Color'].default_value = (*[srgb_lin(c) for c in emission], 1.0)
        bs.inputs['Emission Strength'].default_value = preview_emit
    entry = {"name": name, "color": [round(c, 3) for c in color], "metallic": metallic,
             "smoothness": smooth, "emission": [round(c, 3) for c in emission], "alpha": 1.0}
    entry.update(extra)
    MAT_JSON.append(entry)
    M[name] = m
    return m


defmat('DN_Galvaniz', (0.60, 0.62, 0.61), 0.55, 0.38)
defmat('DN_Boya_Koyu', (0.20, 0.21, 0.21), 0.25, 0.40)
defmat('DN_Siyah_Mat', (0.075, 0.075, 0.072), 0.0, 0.22)
defmat('DN_Sinyal_Kirmizi', (0.30, 0.055, 0.045), 0.0, 0.70, yanik_emission=[1.0, 0.10, 0.05],
       not_="sonuk ton; Unity yanik halde emission'i yanik_emission ile acar")
defmat('DN_Sinyal_Sari', (0.36, 0.24, 0.045), 0.0, 0.70, yanik_emission=[1.0, 0.60, 0.08],
       not_="sonuk ton; Unity yanik halde emission'i yanik_emission ile acar")
defmat('DN_Sinyal_Yesil', (0.045, 0.22, 0.16), 0.0, 0.70, yanik_emission=[0.12, 1.0, 0.55],
       not_="sonuk ton; Unity yanik halde emission'i yanik_emission ile acar")
defmat('DN_Lamba', (0.98, 0.93, 0.80), 0.0, 0.50, emission=(1.0, 0.85, 0.62), preview_emit=3.0)
defmat('DN_Tabela_Kirmizi', (0.68, 0.11, 0.09), 0.0, 0.45)
defmat('DN_Tabela_Mavi', (0.08, 0.27, 0.55), 0.0, 0.45)
defmat('DN_Beyaz', (0.88, 0.88, 0.85), 0.0, 0.45)
defmat('DN_Beton', (0.63, 0.62, 0.59), 0.0, 0.12)
defmat('DN_Dokum', (0.15, 0.16, 0.15), 0.35, 0.30)
defmat('DN_Ahsap', (0.47, 0.32, 0.20), 0.0, 0.25)
defmat('DN_Cam_Opak', (0.62, 0.70, 0.74), 0.0, 0.70)
defmat('DN_Metal_Acik', (0.67, 0.68, 0.67), 0.30, 0.40)
defmat('DN_Konteyner', (0.22, 0.28, 0.24), 0.0, 0.30)
defmat('DN_Lastik', (0.065, 0.065, 0.062), 0.0, 0.10)
defmat('DN_Tas', (0.71, 0.67, 0.58), 0.0, 0.15)
defmat('DN_Tas_Koyu', (0.50, 0.47, 0.41), 0.0, 0.15)
defmat('DN_Musluk', (0.55, 0.46, 0.28), 0.70, 0.45)
defmat('DN_Izolator', (0.78, 0.77, 0.72), 0.0, 0.60)
defmat('DN_Boya_Yesil', (0.17, 0.30, 0.24), 0.20, 0.40)
defmat('DN_Oyun_Kirmizi', (0.62, 0.20, 0.15), 0.0, 0.45)
# Ofis onundeki agaclarla ayni yaprak/govde tonlari (oyundaki Olive/Sunlit/Deep green foliage, Tree_bark)
defmat('DN_Yaprak_1', (0.26, 0.36, 0.15), 0.0, 0.28)
defmat('DN_Yaprak_2', (0.42, 0.50, 0.20), 0.0, 0.28)
defmat('DN_Yaprak_3', (0.17, 0.27, 0.15), 0.0, 0.28)
defmat('DN_Govde', (0.32, 0.23, 0.16), 0.0, 0.28)
for e in MAT_JSON:
    if 'not_' in e:
        e['not'] = e.pop('not_')
LEAVES = [M['DN_Yaprak_1'], M['DN_Yaprak_2'], M['DN_Yaprak_3']]

# ----------------------------------------------------------------------------------
# Geometri yardimcilari (bmesh; nesneler donussuz kalir)
# ----------------------------------------------------------------------------------


class MB:
    """Tek nesneye birlesecek parcalar; yuz basina malzeme."""

    def __init__(self):
        self.bm = bmesh.new()
        self.mats = []

    def idx(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def add(self, src, mat):
        vm = {v: self.bm.verts.new(v.co) for v in src.verts}
        src.normal_update()
        for f in src.faces:
            m = mat(f) if callable(mat) else mat
            nf = self.bm.faces.new([vm[v] for v in f.verts])
            nf.material_index = self.idx(m)
            nf.smooth = False
        src.free()
        return self


def _xf(bm, mat4):
    bmesh.ops.transform(bm, matrix=mat4, verts=bm.verts[:])


def rot_to(d):
    return Vector((0, 0, 1)).rotation_difference(Vector(d).normalized()).to_matrix().to_4x4()


def g_box(center, size, bevel=0.0, rot=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=bm.verts[:] + bm.edges[:], offset=bevel, segments=1,
                        affect='EDGES', profile=0.5, clamp_overlap=True)
    m4 = Matrix.Translation(Vector(center))
    if rot is not None:
        m4 = m4 @ rot.to_4x4()
    _xf(bm, m4)
    return bm


def g_boxr(mn, mx, bevel=0.0):
    mn, mx = Vector(mn), Vector(mx)
    return g_box((mn + mx) / 2, mx - mn, bevel)


def g_cyl(p0, p1, r0, r1=None, sides=8, caps=True, phase=None):
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=sides, radius1=r0,
                          radius2=r0 if r1 is None else r1, depth=d.length)
    ph = (math.pi / sides) if phase is None else phase
    _xf(bm, Matrix.Rotation(ph, 4, 'Z'))
    _xf(bm, Matrix.Translation((p0 + p1) / 2) @ rot_to(d))
    return bm


def g_beam(p0, p1, wx, wy, bevel=0.0):
    """p0->p1 dogrultusunda kutu; kesit wx (yerel X) x wy (yerel Y)."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    bm = g_box((0, 0, 0), (wx, wy, d.length), bevel)
    _xf(bm, Matrix.Translation((p0 + p1) / 2) @ rot_to(d))
    return bm


def g_loft(rings, cap0=True, cap1=True, closed=True, apex0=None, apex1=None):
    bm = bmesh.new()
    vr = [[bm.verts.new(Vector(p)) for p in ring] for ring in rings]
    n = len(rings[0])
    rng = range(n) if closed else range(n - 1)
    for a, b in zip(vr, vr[1:]):
        for i in rng:
            j = (i + 1) % n
            bm.faces.new([a[i], a[j], b[j], b[i]])
    if apex0 is not None:
        ap = bm.verts.new(Vector(apex0))
        for i in rng:
            bm.faces.new([vr[0][(i + 1) % n], vr[0][i], ap])
    elif cap0:
        bm.faces.new(vr[0][::-1])
    if apex1 is not None:
        ap = bm.verts.new(Vector(apex1))
        for i in rng:
            bm.faces.new([vr[-1][i], vr[-1][(i + 1) % n], ap])
    elif cap1:
        bm.faces.new(vr[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def P(plane, u, v, w):
    if plane == 'XZ':
        return Vector((u, w, v))
    if plane == 'YZ':
        return Vector((w, u, v))
    return Vector((u, v, w))


def g_prism(pts, plane, w0, w1):
    return g_loft([[P(plane, u, v, w0) for u, v in pts], [P(plane, u, v, w1) for u, v in pts]])


def g_strip_prism(front, back, plane, w0, w1):
    """Ince sac profil (ornek W kiris): on ve arka cizgi arasi; kapaklar dortgen seridi."""
    bm = bmesh.new()
    F0 = [bm.verts.new(P(plane, u, v, w0)) for u, v in front]
    B0 = [bm.verts.new(P(plane, u, v, w0)) for u, v in back]
    F1 = [bm.verts.new(P(plane, u, v, w1)) for u, v in front]
    B1 = [bm.verts.new(P(plane, u, v, w1)) for u, v in back]
    n = len(front)
    for i in range(n - 1):
        bm.faces.new([F0[i], F0[i + 1], F1[i + 1], F1[i]])
        bm.faces.new([B0[i + 1], B0[i], B1[i], B1[i + 1]])
        bm.faces.new([F0[i + 1], F0[i], B0[i], B0[i + 1]])
        bm.faces.new([F1[i], F1[i + 1], B1[i + 1], B1[i]])
    bm.faces.new([F0[0], B0[0], B1[0], F1[0]])
    bm.faces.new([F0[-1], F1[-1], B1[-1], B0[-1]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def g_face(pts, normal):
    bm = bmesh.new()
    f = bm.faces.new([bm.verts.new(Vector(p)) for p in pts])
    f.normal_update()
    if f.normal.dot(Vector(normal)) < 0:
        f.normal_flip()
    return bm


def g_cup(c, r_out, r_in, z0, z1, bottom, sides=12):
    """Ici bos silindir kap (cop kutusu): dis duvar, ust halka, ic duvar, ic taban."""
    cx, cy = c
    bm = bmesh.new()

    def ring(r, z):
        return [bm.verts.new((cx + r * math.cos(TAU * i / sides), cy + r * math.sin(TAU * i / sides), z))
                for i in range(sides)]
    ob, ot, it, ib = ring(r_out, z0), ring(r_out, z1), ring(r_in, z1), ring(r_in, z0 + bottom)
    for i in range(sides):
        j = (i + 1) % sides
        bm.faces.new([ob[i], ob[j], ot[j], ot[i]])
        bm.faces.new([ot[i], ot[j], it[j], it[i]])
        bm.faces.new([it[i], it[j], ib[j], ib[i]])
    bm.faces.new(ib)
    bm.faces.new(ob[::-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def g_ring(c, r_out, r_in, z0, z1, sides=12):
    """Halka (bilezik): dis, ust, ic ve alt yuzler."""
    cx, cy = c
    bm = bmesh.new()

    def ring(r, z):
        return [bm.verts.new((cx + r * math.cos(TAU * i / sides), cy + r * math.sin(TAU * i / sides), z))
                for i in range(sides)]
    ob, ot, it, ib = ring(r_out, z0), ring(r_out, z1), ring(r_in, z1), ring(r_in, z0)
    for i in range(sides):
        j = (i + 1) % sides
        bm.faces.new([ob[i], ob[j], ot[j], ot[i]])
        bm.faces.new([ot[i], ot[j], it[j], it[i]])
        bm.faces.new([it[i], it[j], ib[j], ib[i]])
        bm.faces.new([ib[i], ib[j], ob[j], ob[i]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def g_lobe(center, radius, subdiv, rng, scale=(1, 1, 1), jitter=0.10):
    """Faset yaprak kumesi: dusuk bolumlu ikosfer, rastgele donus ve kose oynatma."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    rot = Matrix.Rotation(rng.uniform(0, TAU), 4, 'Z') @ Matrix.Rotation(rng.uniform(-0.5, 0.5), 4, 'X')
    _xf(bm, rot)
    for v in bm.verts:
        j = 1.0 + rng.uniform(-jitter, jitter)
        v.co = Vector((v.co.x * scale[0] * j, v.co.y * scale[1] * j, v.co.z * scale[2] * j))
    _xf(bm, Matrix.Translation(Vector(center)))
    return bm


def front_mat(front, other, axis=Vector((0, -1, 0)), lim=0.9):
    return lambda f: front if f.normal.dot(axis) > lim else other


# ----------------------------------------------------------------------------------
# Varlik kaydi
# ----------------------------------------------------------------------------------
ASSETS = []


class Asset:
    def __init__(self, aid, tur, budget, orijin, **meta):
        self.id, self.tur, self.budget, self.orijin = aid, tur, budget, orijin
        self.coll = bpy.data.collections.new(aid)
        scene.collection.children.link(self.coll)
        self.objects = []
        self.meta = dict(meta)
        ASSETS.append(self)

    def iname(self, name):
        return name if name == self.id else f"{self.id}|{name}"

    def _link(self, o, name, kind):
        self.coll.objects.link(o)
        o['export_name'] = name
        o['kind'] = kind
        self.objects.append(o)
        return o

    def mesh(self, mb, name=None, origin=(0, 0, 0), kind='gorsel'):
        name = name or self.id
        origin = Vector(origin)
        if origin.length > 0:
            bmesh.ops.translate(mb.bm, vec=-origin, verts=mb.bm.verts[:])
        me = bpy.data.meshes.new(self.iname(name))
        mb.bm.to_mesh(me)
        mb.bm.free()
        for m in mb.mats:
            me.materials.append(m)
        for p in me.polygons:
            p.use_smooth = False
        o = bpy.data.objects.new(self.iname(name), me)
        o.location = origin
        return self._link(o, name, kind)

    def col(self, name, mn, mx):
        mn, mx = Vector(mn), Vector(mx)
        c, s = (mn + mx) / 2, mx - mn
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts:
            v.co = Vector((v.co.x * s.x, v.co.y * s.y, v.co.z * s.z))
        me = bpy.data.meshes.new(self.iname(name))
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new(self.iname(name), me)
        o.location = c
        o.display_type = 'WIRE'
        o.hide_render = True
        return self._link(o, name, 'carpisma')

    def empty(self, name, loc):
        o = bpy.data.objects.new(self.iname(name), None)
        o.empty_display_type = 'PLAIN_AXES'
        o.empty_display_size = 0.2
        o.location = Vector(loc)
        return self._link(o, name, 'nokta')


def U(v):
    """Blender (x, y, z) -> Unity yerel [x, y, z] = (x, z, y)."""
    return [round(v[0], 3), round(v[2], 3), round(v[1], 3)]


# ----------------------------------------------------------------------------------
# Trafik isigi basligi
# ----------------------------------------------------------------------------------
HEAD = dict(bp_w=0.46, bp_h=1.02, bp_t=0.02, h_w=0.30, h_h=0.86, h_d=0.22, lens_r=0.10)


def g_visor(c, ri, ro, length, a0=-25.0, a1=205.0, seg=8):
    """Mercek siperi: ust yaya acik kavisli kabuk, ust kisim uzun, yanlar kisalir."""
    cx, cy, cz = c
    bm = bmesh.new()
    ang = [math.radians(a0 + (a1 - a0) * i / seg) for i in range(seg + 1)]

    def L(t):
        return length * (0.40 + 0.60 * (math.sin(t) + 1) / 2)
    Ob = [bm.verts.new((cx + ro * math.cos(t), cy, cz + ro * math.sin(t))) for t in ang]
    Ib = [bm.verts.new((cx + ri * math.cos(t), cy, cz + ri * math.sin(t))) for t in ang]
    Of = [bm.verts.new((cx + ro * math.cos(t), cy - L(t), cz + ro * math.sin(t))) for t in ang]
    If = [bm.verts.new((cx + ri * math.cos(t), cy - L(t), cz + ri * math.sin(t))) for t in ang]
    for i in range(seg):
        bm.faces.new([Ob[i], Ob[i + 1], Of[i + 1], Of[i]])
        bm.faces.new([Ib[i + 1], Ib[i], If[i], If[i + 1]])
        bm.faces.new([Of[i], Of[i + 1], If[i + 1], If[i]])
        bm.faces.new([Ob[i + 1], Ob[i], Ib[i], Ib[i + 1]])
    bm.faces.new([Ob[0], Of[0], If[0], Ib[0]])
    bm.faces.new([Ob[-1], Ib[-1], If[-1], Of[-1]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def signal_head(a, mb, cx, bottom, back_y, suffix):
    """-Y'ye bakan 3 mercekli baslik. bottom = arka plakanin alt kenari."""
    h = HEAD
    mb.add(g_box((cx, back_y - h['bp_t'] / 2, bottom + h['bp_h'] / 2), (h['bp_w'], h['bp_t'], h['bp_h']), 0.006),
           M['DN_Siyah_Mat'])
    hb = bottom + (h['bp_h'] - h['h_h']) / 2
    hy0 = back_y - h['bp_t']
    front = hy0 - h['h_d']
    mb.add(g_box((cx, hy0 - h['h_d'] / 2, hb + h['h_h'] / 2), (h['h_w'], h['h_d'], h['h_h']), 0.02),
           M['DN_Boya_Koyu'])
    names = {}
    for col, dz in [('Yesil', 0.155), ('Sari', 0.43), ('Kirmizi', 0.705)]:
        zc = hb + dz
        lb = MB().add(g_cyl((cx, front + 0.008, zc), (cx, front - 0.012, zc), h['lens_r'], sides=12),
                      M[f'DN_Sinyal_{col}'])
        nm = f"Isik_{col}_{suffix}"
        a.mesh(lb, nm, origin=(cx, front - 0.012, zc), kind='lens')
        names[col] = nm
        mb.add(g_visor((cx, front + 0.004, zc), 0.116, 0.126, 0.21), M['DN_Siyah_Mat'])
    return names, front


# ----------------------------------------------------------------------------------
# 1-2 Trafik isiklari
# ----------------------------------------------------------------------------------
def pole_base(mb, r_cover, h_cover, flange=0.40):
    mb.add(g_box((0, 0, 0.015), (flange, flange, 0.03), 0.008), M['DN_Galvaniz'])
    mb.add(g_cyl((0, 0, 0.0), (0, 0, h_cover), r_cover, r_cover * 0.75, sides=8), M['DN_Galvaniz'])


def build_trafik_kollu():
    a = Asset('Trafik_Isigi_Kollu', 'trafik', 2500, 'direk ekseni, zemin (Z=0)')
    mb = MB()
    pole_r0, pole_r1, H = 0.105, 0.075, 6.5
    mb.add(g_cyl((0, 0, -SINK), (0, 0, H), pole_r0, pole_r1, sides=12), M['DN_Galvaniz'])
    mb.add(g_cyl((0, 0, H), (0, 0, H + 0.05), 0.082, 0.03, sides=12), M['DN_Galvaniz'])
    pole_base(mb, 0.17, 0.50, 0.42)
    # kol (-X), kok bilezigi, destek cubugu
    arm0, arm1 = Vector((0, 0, 6.18)), Vector((-4.0, 0, 6.32))
    mb.add(g_cyl((0, 0, 6.04), (0, 0, 6.32), 0.118, sides=12), M['DN_Galvaniz'])
    mb.add(g_cyl(arm0, arm1, 0.07, 0.048, sides=10), M['DN_Galvaniz'])
    mb.add(g_cyl(arm1, arm1 + Vector((-0.04, 0, 0.0)), 0.05, 0.03, sides=10), M['DN_Galvaniz'])
    mb.add(g_cyl((0, 0, 5.40), (0, 0, 5.52), 0.105, sides=12), M['DN_Galvaniz'])
    mb.add(g_cyl((-0.05, 0, 5.46), (-1.70, 0, 6.245), 0.032, sides=8), M['DN_Galvaniz'])
    # kol ucundaki baslik: alt kenar 5.40; kol baslik arka plakasinin arkasindan gecer
    hx = -3.72
    arm_z = arm0.z + (arm1.z - arm0.z) * (abs(hx) / 4.0)
    arm_r = 0.07 + (0.048 - 0.07) * (abs(hx) / 4.0)
    back_y = -(arm_r + 0.012)
    n1, f1 = signal_head(a, mb, hx, 5.40, back_y, 1)
    mb.add(g_box((hx, back_y / 2, arm_z), (0.14, abs(back_y) + 0.03, 0.16), 0.01), M['DN_Boya_Koyu'])
    # direk uzerindeki baslik: alt kenar 2.60, diregin onunde
    r_at = pole_r0 + (pole_r1 - pole_r0) * ((3.1 + SINK) / (H + SINK))
    back_y2 = -(r_at + 0.04)
    n2, f2 = signal_head(a, mb, 0.0, 2.60, back_y2, 2)
    for z in (2.85, 3.40):
        mb.add(g_box((0, back_y2 / 2, z), (0.12, abs(back_y2), 0.09), 0.01), M['DN_Boya_Koyu'])
    a.mesh(mb)
    a.col('COL_Direk', (-0.21, -0.21, 0), (0.21, 0.21, H))
    a.meta.update(
        lensler={"kirmizi": [n1['Kirmizi'], n2['Kirmizi']], "sari": [n1['Sari'], n2['Sari']],
                 "yesil": [n1['Yesil'], n2['Yesil']]},
        basliklar=[{"ad": "kol_basligi", "lens_eki": "_1", "alt_kenar": 5.40, "merkez_x": hx},
                   {"ad": "direk_basligi", "lens_eki": "_2", "alt_kenar": 2.60, "merkez_x": 0.0}],
        kol={"yon_unity": [-1, 0, 0], "uzunluk": 4.0, "yukseklik": round(arm1.z, 3)},
        baslik_yonu_unity=[0, 0, -1], lens_yaricapi=HEAD['lens_r'], direk_yuksekligi=H)
    return a


def build_trafik_direk():
    a = Asset('Trafik_Isigi_Direk', 'trafik', 2500, 'direk ekseni, zemin (Z=0)')
    mb = MB()
    H, r0, r1 = 3.2, 0.075, 0.065
    mb.add(g_cyl((0, 0, -SINK), (0, 0, H), r0, r1, sides=12), M['DN_Galvaniz'])
    mb.add(g_cyl((0, 0, H), (0, 0, H + 0.04), 0.07, 0.025, sides=12), M['DN_Galvaniz'])
    pole_base(mb, 0.13, 0.40, 0.32)
    r_at = r0 + (r1 - r0) * ((3.0 + SINK) / (H + SINK))
    back_y = -(r_at + 0.04)
    n, f = signal_head(a, mb, 0.0, 2.60, back_y, 1)
    for z in (2.85, 3.10):
        mb.add(g_box((0, back_y / 2, z), (0.12, abs(back_y), 0.09), 0.01), M['DN_Boya_Koyu'])
    a.mesh(mb)
    a.col('COL_Direk', (-0.17, -0.17, 0), (0.17, 0.17, H))
    a.meta.update(lensler={"kirmizi": [n['Kirmizi']], "sari": [n['Sari']], "yesil": [n['Yesil']]},
                  basliklar=[{"ad": "direk_basligi", "lens_eki": "_1", "alt_kenar": 2.60, "merkez_x": 0.0}],
                  baslik_yonu_unity=[0, 0, -1], lens_yaricapi=HEAD['lens_r'], direk_yuksekligi=H)
    return a


# ----------------------------------------------------------------------------------
# 3 Tabelalar
# ----------------------------------------------------------------------------------
SIGN_H = 2.8
PL_BACK = -0.045    # plaka arka yuzu (direk r=0.035 + 1 cm)
PL_T = 0.02


def sign_pole(mb, z_clamps):
    mb.add(g_cyl((0, 0, -SINK), (0, 0, SIGN_H), 0.035, sides=8), M['DN_Galvaniz'])
    mb.add(g_cyl((0, 0, SIGN_H), (0, 0, SIGN_H + 0.03), 0.039, 0.015, sides=8), M['DN_Galvaniz'])
    for z in z_clamps:
        mb.add(g_box((0, (PL_BACK - 0.0) / 2 - 0.004, z), (0.11, abs(PL_BACK) + 0.02, 0.05), 0.006),
               M['DN_Galvaniz'])


def plate(mb, pts_xz, front):
    """Arka yuzu galvaniz, on yuzu renkli 2 cm sac plaka."""
    mb.add(g_prism(pts_xz, 'XZ', PL_BACK, PL_BACK - PL_T), front_mat(front, M['DN_Galvaniz']))


def layer(mb, pts_xz, mat, k):
    y = PL_BACK - PL_T - 0.005 * k
    mb.add(g_face([(x, y, z) for x, z in pts_xz], (0, -1, 0)), mat)


def ngon(cx, cz, R, n, phase):
    return [(cx + R * math.cos(phase + TAU * i / n), cz + R * math.sin(phase + TAU * i / n)) for i in range(n)]


def build_tabela(kind):
    aid = {'yolver': 'Tabela_YolVer', 'dur': 'Tabela_Dur', 'yaya': 'Tabela_YayaGecidi', 'park': 'Tabela_Park'}[kind]
    a = Asset(aid, 'trafik', 800, 'direk ekseni, zemin (Z=0)')
    mb = MB()
    if kind == 'yolver':
        s, top = 0.90, 2.78
        h = s * math.sqrt(3) / 2
        tri = [(-s / 2, top), (0, top - h), (s / 2, top)]
        cz = top - h / 3
        plate(mb, tri, M['DN_Tabela_Kirmizi'])
        layer(mb, [(0 + (x - 0) * 0.62, cz + (z - cz) * 0.62) for x, z in tri], M['DN_Beyaz'], 1)
        sign_pole(mb, [top - 0.12, top - 0.50])
        face_c, face_size = (0, cz), (s, h)
    elif kind == 'dur':
        R, cz = 0.75 / 2 / math.cos(math.pi / 8), 2.40
        plate(mb, ngon(0, cz, R, 8, math.pi / 8), M['DN_Beyaz'])
        layer(mb, ngon(0, cz, R - 0.035, 8, math.pi / 8), M['DN_Tabela_Kirmizi'], 1)
        sign_pole(mb, [cz + 0.22, cz - 0.22])
        face_c, face_size = (0, cz), (0.75, 0.75)
    else:
        w, cz = 0.60, 2.47
        sq = [(-w / 2, cz - w / 2), (w / 2, cz - w / 2), (w / 2, cz + w / 2), (-w / 2, cz + w / 2)]
        plate(mb, sq, M['DN_Beyaz'])
        i = w / 2 - 0.018
        layer(mb, [(-i, cz - i), (i, cz - i), (i, cz + i), (-i, cz + i)], M['DN_Tabela_Mavi'], 1)
        if kind == 'yaya':
            s = 0.46
            h = s * math.sqrt(3) / 2
            b = cz - h / 3
            layer(mb, [(-s / 2, b), (s / 2, b), (0, b + h)], M['DN_Beyaz'], 2)
        else:
            fw, fh, t = 0.30, 0.40, 0.06
            x0, x1, z0, z1 = -fw / 2, fw / 2, cz - fh / 2, cz + fh / 2
            for q in [[(x0, z0), (x1, z0), (x1, z0 + t), (x0, z0 + t)],
                      [(x0, z1 - t), (x1, z1 - t), (x1, z1), (x0, z1)],
                      [(x0, z0 + t), (x0 + t, z0 + t), (x0 + t, z1 - t), (x0, z1 - t)],
                      [(x1 - t, z0 + t), (x1, z0 + t), (x1, z1 - t), (x1 - t, z1 - t)]]:
                layer(mb, q, M['DN_Beyaz'], 2)
        sign_pole(mb, [cz + 0.18, cz - 0.18])
        face_c, face_size = (0, cz), (w, w)
    a.mesh(mb)
    a.col('COL_Direk', (-0.06, -0.06, 0), (0.06, 0.06, SIGN_H))
    a.meta.update(tabela_yuzu_yonu_unity=[0, 0, -1], tabela_merkezi=U((face_c[0], PL_BACK - PL_T, face_c[1])),
                  tabela_olcusu=[round(face_size[0], 3), round(face_size[1], 3)], direk_yuksekligi=SIGN_H)
    return a


# ----------------------------------------------------------------------------------
# 4-6 Bariyerler, baba
# ----------------------------------------------------------------------------------
def build_bariyer_otoyol():
    a = Asset('Bariyer_Otoyol', 'trafik', 800, 'segment basi (X=0), ray hatti, zemin; segment +X')
    mb = MB()
    L = 4.0
    front = [(-0.020, 0.44), (-0.083, 0.49), (-0.083, 0.535), (-0.030, 0.595), (-0.083, 0.655),
             (-0.083, 0.70), (-0.020, 0.75)]
    back = [(y + 0.008, z) for y, z in front]
    mb.add(g_strip_prism(front, back, 'YZ', 0.0, L), M['DN_Galvaniz'])
    for x in (1.0, 3.0):
        mb.add(g_boxr((x - 0.05, 0.14, -SINK), (x + 0.05, 0.24, 0.72), 0.008), M['DN_Galvaniz'])
        mb.add(g_boxr((x - 0.04, -0.022, 0.50), (x + 0.04, 0.14, 0.69), 0.006), M['DN_Galvaniz'])
    a.mesh(mb)
    a.col('COL_Bariyer', (0.0, -0.09, 0.0), (L, 0.25, 0.75))
    a.meta.update(segment_uzunlugu=L, segment_yonu_unity=[1, 0, 0], dikme_x=[1.0, 3.0],
                  not_="Uc uca dizerken her kopyayi +X'te 4.0 m kaydir; dikme araligi 2 m kesintisiz.")
    return a


def build_beton_bariyer():
    a = Asset('Beton_Bariyer', 'trafik', 800, 'taban merkezi, zemin; uzunluk X ekseninde')
    mb = MB()
    prof = [(-0.305, 0.0), (0.305, 0.0), (0.305, 0.075), (0.15, 0.33), (0.075, 0.81), (-0.075, 0.81),
            (-0.15, 0.33), (-0.305, 0.075)]

    def inset(p):
        y, z = p
        return (y - math.copysign(0.025, y), z if z < 0.5 else z - 0.025)
    ins = [inset(p) for p in prof]
    rings = [[P('YZ', y, z, x) for y, z in pr] for x, pr in [(-1.0, ins), (-0.975, prof), (0.975, prof), (1.0, ins)]]
    mb.add(g_loft(rings), M['DN_Beton'])
    a.mesh(mb)
    a.col('COL_Govde', (-1.0, -0.305, 0), (1.0, 0.305, 0.81))
    a.meta.update(segment_uzunlugu=2.0, segment_yonu_unity=[1, 0, 0],
                  not_="Orijin ortada; uc uca dizerken 2.0 m aralik.")
    return a


def build_baba():
    a = Asset('Baba', 'trafik', 800, 'eksen, zemin (Z=0)')
    mb = MB()
    mb.add(g_cyl((0, 0, -SINK), (0, 0, 0.05), 0.088, sides=10), M['DN_Dokum'])
    mb.add(g_cyl((0, 0, 0.05), (0, 0, 0.80), 0.068, 0.062, sides=10), M['DN_Dokum'])
    ring = lambda r, z: [(r * math.cos(TAU * i / 10 + math.pi / 10), r * math.sin(TAU * i / 10 + math.pi / 10), z)
                         for i in range(10)]
    mb.add(g_loft([ring(0.062, 0.80), ring(0.052, 0.86), ring(0.03, 0.893)], cap0=True, cap1=False,
                  apex1=(0, 0, 0.90)), M['DN_Dokum'])
    mb.add(g_cyl((0, 0, 0.64), (0, 0, 0.71), 0.0675, sides=10), M['DN_Beyaz'])
    a.mesh(mb)
    a.col('COL_Govde', (-0.09, -0.09, 0), (0.09, 0.09, 0.90))
    return a


# ----------------------------------------------------------------------------------
# 7-9 Aydinlatma ve sebeke
# ----------------------------------------------------------------------------------
def build_sokak_lambasi():
    a = Asset('Sokak_Lambasi', 'aydinlatma', 1000, 'direk ekseni, zemin (Z=0)')
    mb = MB()
    H = 8.0
    mb.add(g_cyl((0, 0, -SINK), (0, 0, H), 0.10, 0.055, sides=10), M['DN_Galvaniz'])
    mb.add(g_cyl((0, 0, H), (0, 0, H + 0.035), 0.06, 0.02, sides=10), M['DN_Galvaniz'])
    mb.add(g_box((0, 0, 0.015), (0.40, 0.40, 0.03), 0.008), M['DN_Galvaniz'])
    mb.add(g_cyl((0, 0, 0), (0, 0, 0.70), 0.17, 0.13, sides=8), M['DN_Galvaniz'])
    mb.add(g_cyl((0, 0, 0.70), (0, 0, 0.76), 0.14, 0.115, sides=8), M['DN_Galvaniz'])
    # kol -X 1.8 m, hafif yukselir; alt destek
    mb.add(g_cyl((0, 0, 7.70), (-1.80, 0, 7.90), 0.05, 0.04, sides=8), M['DN_Galvaniz'])
    mb.add(g_cyl((0, 0, 7.30), (-0.62, 0, 7.77), 0.024, sides=6), M['DN_Galvaniz'])
    mb.add(g_cyl((0, 0, 7.62), (0, 0, 7.80), 0.075, sides=10), M['DN_Galvaniz'])
    # armatur: yassi, arkada dar boyunlu
    z0 = 7.84

    def sec(x, w, h):
        return [(x, -w / 2, z0), (x, w / 2, z0), (x, w / 2, z0 + 0.55 * h), (x, 0.28 * w, z0 + h),
                (x, -0.28 * w, z0 + h), (x, -w / 2, z0 + 0.55 * h)]
    mb.add(g_loft([sec(-1.70, 0.13, 0.10), sec(-1.86, 0.30, 0.15), sec(-2.28, 0.32, 0.13),
                   sec(-2.42, 0.22, 0.08)]), M['DN_Boya_Koyu'])
    a.mesh(mb)
    lb = MB().add(g_boxr((-2.30, -0.12, z0 - 0.012), (-1.90, 0.12, z0 + 0.002)), M['DN_Lamba'])
    lc = Vector((-2.10, 0.0, z0 - 0.012))
    a.mesh(lb, 'Lamba_Isik', origin=lc, kind='isik')
    a.col('COL_Direk', (-0.18, -0.18, 0), (0.18, 0.18, H))
    a.meta.update(isik_nesnesi='Lamba_Isik', isik_noktasi=U(lc + Vector((0, 0, -0.05))),
                  isik_yonu_unity=[0, -1, 0], isik_turu='spot (asagi)',
                  kol={"yon_unity": [-1, 0, 0], "uzunluk": 1.8}, direk_yuksekligi=H)
    return a


def build_park_lambasi():
    a = Asset('Park_Lambasi', 'aydinlatma', 1000, 'direk ekseni, zemin (Z=0)')
    mb = MB()
    D = M['DN_Dokum']
    mb.add(g_cyl((0, 0, -SINK), (0, 0, 0.08), 0.19, sides=8), D)
    mb.add(g_cyl((0, 0, 0.08), (0, 0, 0.46), 0.15, 0.085, sides=8), D)
    mb.add(g_cyl((0, 0, 0.46), (0, 0, 0.53), 0.10, sides=8), D)
    mb.add(g_cyl((0, 0, 0.53), (0, 0, 2.80), 0.055, 0.045, sides=8), D)
    mb.add(g_cyl((0, 0, 2.62), (0, 0, 2.70), 0.068, sides=8), D)
    mb.add(g_cyl((0, 0, 2.76), (0, 0, 2.87), 0.07, 0.125, sides=8), D)

    def sq(h, z):
        return [(-h, -h, z), (h, -h, z), (h, h, z), (-h, h, z)]
    zb, zt = 2.87, 3.25
    for sx in (-1, 1):
        for sy in (-1, 1):
            mb.add(g_beam((sx * 0.108, sy * 0.108, zb), (sx * 0.162, sy * 0.162, zt), 0.024, 0.024), D)
    mb.add(g_loft([sq(0.125, zb - 0.005), sq(0.125, zb + 0.025)]), D)
    mb.add(g_loft([sq(0.215, zt - 0.01), sq(0.215, zt + 0.035)], apex1=None), D)
    mb.add(g_loft([sq(0.215, zt + 0.035), sq(0.05, 3.42)], cap0=True, cap1=True), D)
    mb.add(g_cyl((0, 0, 3.42), (0, 0, 3.50), 0.03, 0.006, sides=6), D)
    a.mesh(mb)
    lb = MB().add(g_loft([sq(0.098, zb + 0.02), sq(0.150, zt - 0.01)]), M['DN_Lamba'])
    lc = Vector((0, 0, (zb + zt) / 2))
    a.mesh(lb, 'Lamba_Isik', origin=lc, kind='isik')
    a.col('COL_Direk', (-0.19, -0.19, 0), (0.19, 0.19, 3.5))
    a.meta.update(isik_nesnesi='Lamba_Isik', isik_noktasi=U(lc), isik_turu='nokta (her yon)',
                  direk_yuksekligi=3.5)
    return a


def build_elektrik_diregi():
    a = Asset('Elektrik_Diregi', 'aydinlatma', 1000, 'direk ekseni, zemin (Z=0)')
    mb = MB()
    H = 9.0

    def rect(hx, hy, ch, z):
        return [(-hx + ch, -hy, z), (hx - ch, -hy, z), (hx, -hy + ch, z), (hx, hy - ch, z),
                (hx - ch, hy, z), (-hx + ch, hy, z), (-hx, hy - ch, z), (-hx, -hy + ch, z)]
    mb.add(g_loft([rect(0.15, 0.11, 0.03, -0.30), rect(0.08, 0.065, 0.015, H)]), M['DN_Beton'])
    # travers (X boyunca, teller Y boyunca gider) + iki egik destek
    za = 8.72
    mb.add(g_box((0, 0, za + 0.035), (1.90, 0.075, 0.07), 0.01), M['DN_Galvaniz'])
    mb.add(g_box((0, 0, za - 0.02), (0.22, 0.17, 0.09), 0.01), M['DN_Galvaniz'])
    for s in (-1, 1):
        mb.add(g_beam((s * 0.06, 0, 8.20), (s * 0.68, 0, za + 0.01), 0.035, 0.035), M['DN_Galvaniz'])
    tops = []

    def insulator(x, zb):
        mb.add(g_cyl((x, 0, zb), (x, 0, zb + 0.06), 0.014, sides=6), M['DN_Galvaniz'])
        mb.add(g_cyl((x, 0, zb + 0.05), (x, 0, zb + 0.20), 0.045, 0.034, sides=8), M['DN_Izolator'])
        mb.add(g_cyl((x, 0, zb + 0.085), (x, 0, zb + 0.115), 0.068, sides=8), M['DN_Izolator'])
        tops.append(Vector((x, 0, zb + 0.20)))
    insulator(-0.85, za + 0.07)
    mb.add(g_box((0, 0, H + 0.02), (0.13, 0.11, 0.04), 0.008), M['DN_Galvaniz'])
    insulator(0.0, H + 0.04)
    insulator(0.85, za + 0.07)
    a.mesh(mb)
    pts = []
    for nm, p in zip(['Tel_1', 'Tel_2', 'Tel_3'], [tops[0], tops[1], tops[2]]):
        a.empty(nm, p)
        pts.append({"ad": nm, "konum": U(p)})
    a.col('COL_Direk', (-0.16, -0.12, 0), (0.16, 0.12, H))
    a.meta.update(tel_noktalari=pts, tel_yonu_unity=[0, 0, 1], direk_yuksekligi=H,
                  not_="Teller Unity Z (Blender Y) boyunca, sokaga paralel uzanir; Tel_1 -X, Tel_2 ust, Tel_3 +X.")
    return a


# ----------------------------------------------------------------------------------
# 10-16 Sokak esyasi
# ----------------------------------------------------------------------------------
def build_otobus_duragi():
    a = Asset('Otobus_Duragi', 'esya', 2000, 'taban izinin merkezi, zemin; acik on yuz -Y')
    mb = MB()
    K = M['DN_Boya_Koyu']
    xs, yr, yf = 2.02, 0.70, -0.68
    for x in (-xs, 0.0, xs):
        mb.add(g_boxr((x - 0.04, yr - 0.04, -0.20), (x + 0.04, yr + 0.04, 2.47), 0.008), K)
    for x in (-xs, xs):
        mb.add(g_boxr((x - 0.04, yf - 0.04, -0.20), (x + 0.04, yf + 0.04, 2.47), 0.008), K)
    # ust kirisler
    mb.add(g_boxr((-xs - 0.04, yr - 0.04, 2.36), (xs + 0.04, yr + 0.04, 2.46), 0.008), K)
    mb.add(g_boxr((-xs - 0.04, yf - 0.04, 2.36), (xs + 0.04, yf + 0.04, 2.46), 0.008), K)
    for x in (-xs, xs):
        mb.add(g_boxr((x - 0.04, yf, 2.36), (x + 0.04, yr, 2.46), 0.0), K)
    # cati: one dogru yukselen ince levha + on alin bandi
    roof = [(-0.80, 2.47), (0.80, 2.47), (0.80, 2.53), (-0.80, 2.60)]
    mb.add(g_prism(roof, 'YZ', -2.10, 2.10), M['DN_Metal_Acik'])
    mb.add(g_boxr((-2.10, -0.82, 2.44), (2.10, -0.78, 2.60), 0.006), K)
    # arka opak cam paneller + alt rayi
    G = M['DN_Cam_Opak']
    for x0, x1 in [(-xs + 0.05, -0.05), (0.05, xs - 0.05)]:
        mb.add(g_boxr((x0, yr - 0.012, 0.16), (x1, yr + 0.012, 2.30)), G)
    mb.add(g_boxr((-xs, yr - 0.03, 0.08), (xs, yr + 0.03, 0.16), 0.006), K)
    mb.add(g_boxr((-xs, yr - 0.03, 2.30), (xs, yr + 0.03, 2.36), 0.0), K)
    # yan panel (+X ucu)
    mb.add(g_boxr((xs - 0.012, yf + 0.05, 0.16), (xs + 0.012, yr - 0.05, 2.30)), G)
    mb.add(g_boxr((xs - 0.03, yf, 0.08), (xs + 0.03, yr, 0.16), 0.006), K)
    mb.add(g_boxr((xs - 0.03, yf, 2.30), (xs + 0.03, yr, 2.36), 0.0), K)
    # ic bank: iki ayak + uc ahsap cita
    for x in (-0.85, 0.85):
        mb.add(g_boxr((x - 0.025, 0.28, 0.0), (x + 0.025, 0.62, 0.43), 0.006), K)
        mb.add(g_boxr((x - 0.025, 0.62, 0.30), (x + 0.025, yr - 0.012, 0.40), 0.0), K)
    for yc in (0.33, 0.45, 0.57):
        mb.add(g_box((0, yc, 0.448), (2.0, 0.10, 0.035), 0.008), M['DN_Ahsap'])
    a.mesh(mb)
    a.col('COL_ArkaPanel', (-2.10, 0.64, 0), (2.10, 0.76, 2.47))
    a.col('COL_YanPanel', (1.96, -0.73, 0), (2.08, 0.76, 2.47))
    a.col('COL_Direk_On', (-2.08, -0.73, 0), (-1.96, -0.63, 2.47))
    a.col('COL_Bank', (-1.0, 0.27, 0), (1.0, 0.64, 0.47))
    a.col('COL_Cati', (-2.10, -0.82, 2.44), (2.10, 0.80, 2.60))
    return a


def build_bank():
    a = Asset('Bank', 'esya', 800, 'taban izinin merkezi, zemin; oturma yonu -Y')
    mb = MB()
    D = M['DN_Dokum']
    for x in (-0.80, 0.80):
        mb.add(g_beam((x, -0.22, 0.004), (x, -0.20, 0.62), 0.05, 0.055, 0.008), D)
        mb.add(g_beam((x, 0.20, 0.008), (x, 0.13, 0.43), 0.05, 0.055, 0.008), D)
        mb.add(g_beam((x, 0.13, 0.40), (x, 0.265, 0.88), 0.05, 0.05, 0.008), D)
        mb.add(g_boxr((x - 0.025, -0.24, 0.39), (x + 0.025, 0.20, 0.425)), D)
        mb.add(g_boxr((x - 0.03, -0.27, 0.62), (x + 0.03, 0.20, 0.66), 0.01), D)
    W = M['DN_Ahsap']
    for yc in (-0.19, -0.08, 0.03, 0.14):
        zc = 0.445 - 0.03 * (yc + 0.19) / 0.33
        mb.add(g_box((0, yc, zc), (1.80, 0.095, 0.035), 0.008), W)
    d = Vector((0, 0.135, 0.48)).normalized()
    nrm = Vector((0, -d.z, d.y))
    th = math.atan2(d.y, d.z)
    for zc in (0.55, 0.675, 0.80):
        t = (zc - 0.40) / d.z
        c = Vector((0, 0.13, 0.40)) + d * t + nrm * 0.045
        mb.add(g_box(c, (1.80, 0.032, 0.10), 0.008, rot=Matrix.Rotation(-th, 3, 'X')), W)
    a.mesh(mb)
    a.col('COL_Bank', (-0.90, -0.28, 0), (0.90, 0.32, 0.88))
    return a


def build_cop_kutusu():
    a = Asset('Cop_Kutusu', 'esya', 800, 'direk ekseni, zemin; kutu diregin onunde (-Y)')
    mb = MB()
    mb.add(g_cyl((0, 0, -SINK), (0, 0, 1.05), 0.03, sides=8), M['DN_Galvaniz'])
    mb.add(g_cyl((0, 0, 1.05), (0, 0, 1.075), 0.034, 0.012, sides=8), M['DN_Galvaniz'])
    cy = -0.26
    inner = M['DN_Boya_Koyu']
    outer = M['DN_Boya_Yesil']

    def cupmat(f):
        c = f.calc_center_median()
        rr = math.hypot(c.x, c.y - cy)
        if f.normal.z > 0.9 and rr < 0.19:
            return inner
        if abs(f.normal.z) < 0.5 and (f.normal.x * (c.x) + f.normal.y * (c.y - cy)) < 0:
            return inner
        return outer
    mb.add(g_cup((0, cy), 0.21, 0.196, 0.42, 0.94, 0.03, sides=12), cupmat)
    # ust agiz bilezigi (halka)
    mb.add(g_ring((0, cy), 0.222, 0.199, 0.905, 0.955, sides=12), outer)
    for z in (0.56, 0.84):
        mb.add(g_box((0, -0.026, z), (0.07, 0.048, 0.05), 0.006), M['DN_Galvaniz'])
    a.mesh(mb)
    a.col('COL_Govde', (-0.23, -0.49, 0), (0.23, 0.04, 1.08))
    return a


def build_cop_konteyneri():
    a = Asset('Cop_Konteyneri', 'esya', 800, 'taban izinin merkezi, zemin; on -Y, kapak mentesesi arkada')
    mb = MB()
    C = M['DN_Konteyner']

    def rr(hx, hy, ch, z):
        return [(-hx + ch, -hy, z), (hx - ch, -hy, z), (hx, -hy + ch, z), (hx, hy - ch, z),
                (hx - ch, hy, z), (-hx + ch, hy, z), (-hx, hy - ch, z), (-hx, -hy + ch, z)]
    mb.add(g_loft([rr(0.575, 0.425, 0.06, 0.245), rr(0.65, 0.50, 0.07, 1.10), rr(0.65, 0.50, 0.07, 1.13)]), C)
    mb.add(g_loft([rr(0.665, 0.515, 0.075, 1.10), rr(0.685, 0.535, 0.08, 1.135), rr(0.685, 0.535, 0.08, 1.20)]), C)
    # yan kaldirma pimleri
    for s in (-1, 1):
        mb.add(g_cyl((s * 0.64, 0, 0.98), (s * 0.70, 0, 0.98), 0.04, sides=8), C)
    # tekerlekler (4 doner teker)
    for x in (-0.48, 0.48):
        for y in (-0.33, 0.33):
            mb.add(g_box((x, y, 0.235), (0.16, 0.16, 0.02)), M['DN_Galvaniz'])
            mb.add(g_boxr((x - 0.045, y - 0.04, 0.200), (x + 0.045, y + 0.04, 0.228)), M['DN_Galvaniz'])
            for sx in (-1, 1):
                mb.add(g_boxr((x + sx * 0.03 - 0.006, y - 0.03, 0.09), (x + sx * 0.03 + 0.006, y + 0.05, 0.205)),
                       M['DN_Galvaniz'])
            mb.add(g_cyl((x - 0.022, y + 0.02, 0.10), (x + 0.022, y + 0.02, 0.10), 0.10, sides=10), M['DN_Lastik'])
    a.mesh(mb)
    # kapak: ayri nesne, orijin arka ust mentese hattinda (Blender X ekseni)
    kb = MB()
    kb.add(g_loft([rr(0.69, 0.54, 0.08, 1.20), rr(0.69, 0.54, 0.08, 1.255), rr(0.60, 0.45, 0.07, 1.33)]), C)
    kb.add(g_boxr((-0.30, -0.585, 1.18), (0.30, -0.54, 1.235), 0.008), C)
    for s in (-1, 1):
        kb.add(g_cyl((s * 0.45 - 0.07, 0.55, 1.215), (s * 0.45 + 0.07, 0.55, 1.215), 0.025, sides=8),
               M['DN_Galvaniz'])
    hinge = Vector((0, 0.55, 1.215))
    a.mesh(kb, 'Kapak', origin=hinge, kind='hareketli')
    a.col('COL_Govde', (-0.70, -0.55, 0), (0.70, 0.55, 1.33))
    a.meta.update(hareketli_parcalar=[{"ad": "Kapak", "mafsal": U(hinge), "eksen_unity": [1, 0, 0],
                                       "acilma": "Unity yerel X etrafinda +110 derece (on kenar kalkar)"}])
    return a


def build_cesme():
    a = Asset('Cesme', 'esya', 800, 'taban izinin merkezi, zemin; on yuz (nis + tekne) -Y')
    mb = MB()
    T, TK = M['DN_Tas'], M['DN_Tas_Koyu']
    mb.add(g_boxr((-0.78, -0.40, -0.20), (0.78, 0.40, 0.12), 0.015), T)          # kaide
    mb.add(g_boxr((-0.72, 0.05, 0.12), (0.72, 0.38, 1.75), 0.015), T)           # arka govde
    yf, yb = -0.11, 0.05
    mb.add(g_boxr((-0.72, yf, 0.12), (-0.38, yb, 1.75), 0.012), T)              # sol ayak
    mb.add(g_boxr((0.38, yf, 0.12), (0.72, yb, 1.75), 0.012), T)                # sag ayak
    mb.add(g_boxr((-0.38, yf, 0.12), (0.38, yb, 0.44), 0.0), T)                 # nis alti
    # sivri kemer: merkezler x=+-0.15, r=0.53, uzengi z=1.0
    zs, top, wc = 1.0, 1.75, 0.15
    rad = 0.38 + wc
    a_apex = math.atan2(math.sqrt(rad ** 2 - wc ** 2), -wc)
    arc_l = [(wc + rad * math.cos(t), zs + rad * math.sin(t))
             for t in [math.pi + (a_apex - math.pi) * i / 4 for i in range(5)]]
    arc = arc_l + [(-x, z) for x, z in reversed(arc_l[:-1])]
    bm = bmesh.new()
    Af = [bm.verts.new((x, yf, z)) for x, z in arc]
    Ab = [bm.verts.new((x, yb, z)) for x, z in arc]
    Tf = [bm.verts.new((x, yf, top)) for x, z in arc]
    Tb = [bm.verts.new((x, yb, top)) for x, z in arc]
    for i in range(len(arc) - 1):
        bm.faces.new([Af[i], Af[i + 1], Tf[i + 1], Tf[i]])
        bm.faces.new([Ab[i + 1], Ab[i], Tb[i], Tb[i + 1]])
        bm.faces.new([Af[i + 1], Af[i], Ab[i], Ab[i + 1]])
        bm.faces.new([Tf[i], Tf[i + 1], Tb[i + 1], Tb[i]])
    bm.faces.new([Af[0], Tf[0], Tb[0], Ab[0]])
    bm.faces.new([Af[-1], Ab[-1], Tb[-1], Tf[-1]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    mb.add(bm, T)
    # nis arka yuzu (koyu tas), kemer bicimli tek yuz
    niche = [(-0.38, 0.44), (0.38, 0.44)] + [(x, z) for x, z in reversed(arc)]
    mb.add(g_face([(x, yb - 0.004, z) for x, z in niche], (0, -1, 0)), TK)
    # kemer ustu duz ayna tasi (yazisiz), sacak ve basik cati
    mb.add(g_boxr((-0.26, yf - 0.02, 1.56), (0.26, yf, 1.70), 0.006), TK)
    mb.add(g_boxr((-0.80, -0.17, 1.75), (0.80, 0.40, 1.85), 0.02), T)
    mb.add(g_loft([[(-0.76, -0.13, 1.85), (0.76, -0.13, 1.85), (0.76, 0.38, 1.85), (-0.76, 0.38, 1.85)],
                   [(-0.52, 0.0, 2.0), (0.52, 0.0, 2.0), (0.52, 0.25, 2.0), (-0.52, 0.25, 2.0)]]), T)
    # musluk blogu
    mb.add(g_boxr((-0.045, yb - 0.06, 0.74), (0.045, yb, 0.84), 0.008), M['DN_Musluk'])
    mb.add(g_cyl((0, yb - 0.05, 0.78), (0, yb - 0.21, 0.765), 0.014, sides=6), M['DN_Musluk'])
    mb.add(g_cyl((0, yb - 0.21, 0.778), (0, yb - 0.21, 0.73), 0.014, sides=6), M['DN_Musluk'])
    # tekne: on duvar, yan duvarlar, taban (ic yuzler koyu)
    ty0, ty1, tz = -0.40, yf, 0.50
    mb.add(g_boxr((-0.64, ty0, 0.12), (0.64, ty0 + 0.07, tz), 0.012), T)
    mb.add(g_boxr((-0.64, ty0 + 0.07, 0.12), (-0.57, ty1, tz), 0.008), T)
    mb.add(g_boxr((0.57, ty0 + 0.07, 0.12), (0.64, ty1, tz), 0.008), T)
    mb.add(g_boxr((-0.57, ty0 + 0.07, 0.12), (0.57, ty1, 0.30), 0.0), TK)
    a.mesh(mb)
    a.col('COL_Govde', (-0.80, -0.11, 0), (0.80, 0.40, 2.0))
    a.col('COL_Tekne', (-0.64, -0.40, 0), (0.64, -0.11, 0.50))
    return a


def build_kaydirak():
    a = Asset('Oyun_Kaydirak', 'esya', 800, 'taban izinin merkezi, zemin; kayma yonu -Y, merdiven +Y')
    mb = MB()
    F = M['DN_Boya_Yesil']
    yc, hx, hy, zd = 0.55, 0.36, 0.36, 1.20
    for x in (-hx, hx):
        for y in (yc - hy, yc + hy):
            mb.add(g_cyl((x, y, -0.20), (x, y, 1.95), 0.04, sides=8), F)
            mb.add(g_cyl((x, y, 1.95), (x, y, 1.99), 0.042, 0.02, sides=8), F)
    mb.add(g_boxr((-hx, yc - hy, zd - 0.05), (hx, yc + hy, zd), 0.01), M['DN_Ahsap'])
    for x in (-hx, hx):            # yan korkuluklar
        for z in (1.55, 1.90):
            mb.add(g_cyl((x, yc - hy, z), (x, yc + hy, z), 0.025, sides=6), F)
    mb.add(g_cyl((-hx, yc + hy, 1.90), (hx, yc + hy, 1.90), 0.025, sides=6), F)
    # egik merdiven
    for x in (-0.22, 0.22):
        mb.add(g_cyl((x, 1.48, -0.10), (x, yc + hy + 0.02, zd + 0.02), 0.028, sides=6), F)
    for k in range(4):
        t = (k + 1) / 5
        y = 1.48 + (yc + hy + 0.02 - 1.48) * t
        z = -0.10 + (zd + 0.12) * t
        mb.add(g_cyl((-0.22, y, z), (0.22, y, z), 0.02, sides=6), M['DN_Galvaniz'])
    # kaydirak olugu: U kesit, yol boyunca
    path = [Vector((0, yc - hy + 0.02, zd - 0.02)), Vector((0, -0.05, 1.06)), Vector((0, -1.50, 0.40)),
            Vector((0, -1.80, 0.32)), Vector((0, -2.10, 0.30))]
    sec = [(-0.30, 0.16), (-0.30, -0.03), (0.30, -0.03), (0.30, 0.16), (0.26, 0.16), (0.26, 0.0),
           (-0.26, 0.0), (-0.26, 0.16)]
    rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        n = Vector((0, t.z, -t.y))
        if n.z < 0:
            n = -n
        rings.append([p + Vector((x, 0, 0)) + n * z for x, z in sec])
    mb.add(g_loft(rings), M['DN_Oyun_Kirmizi'])
    for x in (-0.22, 0.22):
        mb.add(g_cyl((x, -1.60, -0.15), (x, -1.60, 0.36), 0.03, sides=6), F)
    a.mesh(mb)
    a.col('COL_Kule', (-0.40, yc - hy - 0.04, 0), (0.40, yc + hy + 0.04, 1.99))
    a.col('COL_Kaydirak_Ust', (-0.31, -1.0, 0), (0.31, yc - hy, 1.20))
    a.col('COL_Kaydirak_Alt', (-0.31, -2.10, 0), (0.31, -1.0, 0.75))
    return a


def build_salincak():
    a = Asset('Oyun_Salincak', 'esya', 800, 'taban izinin merkezi, zemin; salinim Y ekseninde')
    mb = MB()
    F = M['DN_Boya_Yesil']
    zt, xe, sp = 2.20, 1.35, 0.85
    mb.add(g_cyl((-xe - 0.10, 0, zt), (xe + 0.10, 0, zt), 0.05, sides=8), F)
    for x in (-xe, xe):
        for s in (-1, 1):
            mb.add(g_cyl((x, s * sp * (1 + SINK / zt), -SINK), (x, s * 0.03, zt + 0.02), 0.045, sides=8), F)
        mb.add(g_cyl((x, -sp * 0.62, 0.85), (x, sp * 0.62, 0.85), 0.03, sides=6), F)
        mb.add(g_box((x, 0, zt), (0.13, 0.16, 0.16), 0.01), F)
    a.mesh(mb)
    piv = []
    for k, xs in enumerate((-0.55, 0.55), start=1):
        sb = MB()
        zp = zt - 0.05
        for dx in (-0.20, 0.20):
            sb.add(g_box((xs + dx, 0, zp + 0.01), (0.04, 0.05, 0.05), 0.005), M['DN_Galvaniz'])
            sb.add(g_cyl((xs + dx, 0, zp), (xs + dx, 0, 0.47), 0.011, sides=4), M['DN_Galvaniz'])
        sb.add(g_box((xs, 0, 0.45), (0.46, 0.18, 0.04), 0.01), M['DN_Lastik'])
        p = Vector((xs, 0, zp))
        a.mesh(sb, f'Oturak_{k}', origin=p, kind='hareketli')
        piv.append({"ad": f"Oturak_{k}", "mafsal": U(p), "eksen_unity": [1, 0, 0],
                    "salinim": "Unity yerel X etrafinda +-35 derece"})
    a.col('COL_Ayak_Sol', (-xe - 0.08, -sp - 0.06, 0), (-xe + 0.08, sp + 0.06, zt + 0.06))
    a.col('COL_Ayak_Sag', (xe - 0.08, -sp - 0.06, 0), (xe + 0.08, sp + 0.06, zt + 0.06))
    a.meta.update(hareketli_parcalar=piv)
    return a


# ----------------------------------------------------------------------------------
# 17-22 Bitki ortusu (faset low-poly)
# ----------------------------------------------------------------------------------
def trunk(mb, zr, sides, rng, lean=(0.0, 0.0), wob=0.03):
    rings = []
    for z, r in zr:
        k = max(0.0, z) / zr[-1][0]
        cx, cy = lean[0] * k, lean[1] * k
        ph = rng.uniform(0, 0.4)
        rings.append([(cx + r * (1 + rng.uniform(-wob, wob)) * math.cos(TAU * i / sides + ph),
                       cy + r * (1 + rng.uniform(-wob, wob)) * math.sin(TAU * i / sides + ph), z)
                      for i in range(sides)])
    mb.add(g_loft(rings), M['DN_Govde'])


def finish_tree(a, mb, col_half, col_h, rng_seed):
    o = a.mesh(mb)
    leaf_idx = {i for i, m in enumerate(o.data.materials) if m.name.startswith('DN_Yaprak')}
    rmax, zmin_leaf = 0.0, 1e9
    for p in o.data.polygons:
        if p.material_index in leaf_idx:
            for vi in p.vertices:
                v = o.data.vertices[vi].co
                rmax = max(rmax, math.hypot(v.x, v.y))
                zmin_leaf = min(zmin_leaf, v.z)
    zmax = max(v.co.z for v in o.data.vertices)
    a.col('COL_Govde', (-col_half, -col_half, 0), (col_half, col_half, col_h))
    a.meta.update(tac_yaricapi=round(rmax, 2), yukseklik=round(zmax, 2), tac_alt_yuksekligi=round(zmin_leaf, 2))


def build_cinar():
    a = Asset('Agac_Cinar', 'bitki', 1500, 'govde ekseni, zemin (Z=0)')
    rng = random.Random(17)
    mb = MB()
    trunk(mb, [(-0.30, 0.56), (0.15, 0.48), (0.9, 0.40), (3.0, 0.33)], 9, rng)
    for p1 in [(1.8, 0.6, 5.0), (-1.6, 1.0, 5.2), (0.2, -1.7, 5.3), (-0.7, -0.9, 5.9), (0.3, 0.4, 6.4)]:
        mb.add(g_cyl((0, 0, 2.8), p1, 0.26, 0.10, sides=6), M['DN_Govde'])
    lobes = [((0, 0, 6.7), 2.6, (1.15, 1.15, 0.72), 2, 1)]
    for i in range(6):
        t = TAU * i / 6 + 0.25
        lobes.append(((2.75 * math.cos(t), 2.75 * math.sin(t), 5.7 + rng.uniform(-0.3, 0.3)),
                      rng.uniform(1.7, 2.0), (1, 1, 0.78), 2, [0, 2, 0, 1, 2, 0][i]))
    for i in range(8):
        t = TAU * i / 8 + 0.6
        lobes.append(((3.65 * math.cos(t), 3.65 * math.sin(t), 4.75 + rng.uniform(-0.15, 0.35)),
                      rng.uniform(1.0, 1.3), (1, 1, 0.8), 1, [2, 0, 2, 1, 2, 0, 2, 0][i]))
    for i in range(3):
        t = TAU * i / 3 + 1.1
        lobes.append(((1.1 * math.cos(t), 1.1 * math.sin(t), 8.0 + rng.uniform(0, 0.3)),
                      rng.uniform(1.3, 1.5), (1, 1, 0.85), 2, [1, 0, 1][i]))
    for c, r, sc, sd, li in lobes:
        mb.add(g_lobe(c, r, sd, rng, sc, 0.10), LEAVES[li])
    finish_tree(a, mb, 0.50, 3.4, 17)
    return a


def build_kavak():
    a = Asset('Agac_Kavak', 'bitki', 1500, 'govde ekseni, zemin (Z=0)')
    rng = random.Random(23)
    mb = MB()
    trunk(mb, [(-0.30, 0.28), (0.15, 0.25), (2.5, 0.19), (5.0, 0.13)], 8, rng)
    col = [(2.55, 0.95, 1.45, 1), (3.75, 1.20, 1.55, 2), (5.15, 1.35, 1.6, 2), (6.65, 1.35, 1.6, 2),
           (8.10, 1.25, 1.6, 2), (9.45, 1.10, 1.6, 2), (10.70, 0.90, 1.6, 2), (11.80, 0.68, 1.6, 1),
           (12.55, 0.42, 1.25, 1)]
    for k, (z, r, sz, sd) in enumerate(col):
        off = (rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15))
        mb.add(g_lobe((off[0], off[1], z), r, sd, rng, (1, 1, sz), 0.09), LEAVES[[0, 1, 0, 2, 1, 0, 1, 0, 1][k]])
    for i in range(6):
        t = TAU * i / 6 + 0.4
        z = 4.0 + i * 1.15
        rr = 1.0 if z < 8 else 0.8
        mb.add(g_lobe((rr * math.cos(t), rr * math.sin(t), z), 0.62, 1, rng, (1, 1, 1.5), 0.10),
               LEAVES[[2, 1, 0][i % 3]])
    finish_tree(a, mb, 0.28, 2.3, 23)
    return a


def build_cam():
    a = Asset('Agac_Cam', 'bitki', 1500, 'govde ekseni, zemin (Z=0)')
    rng = random.Random(31)
    mb = MB()
    trunk(mb, [(-0.30, 0.24), (0.10, 0.21), (3.0, 0.15), (8.4, 0.05)], 7, rng)
    tiers = [(1.95, 2.35, 2.05), (3.20, 2.05, 1.95), (4.40, 1.72, 1.85), (5.55, 1.38, 1.70),
             (6.65, 1.02, 1.55), (7.65, 0.66, 1.37)]
    n = 9
    for k, (zb, r, h) in enumerate(tiers):
        ph = rng.uniform(0, TAU)
        rim = []
        sh = []
        for i in range(n):
            t = ph + TAU * i / n
            rj = r * rng.uniform(0.88, 1.08)
            rim.append((rj * math.cos(t), rj * math.sin(t), zb + rng.uniform(-0.10, 0.06)))
            t2 = t + rng.uniform(-0.08, 0.08)
            sr = rj * 0.70
            sh.append((sr * math.cos(t2), sr * math.sin(t2), zb + h * 0.30 + rng.uniform(-0.05, 0.08)))
        bm = g_loft([rim, sh], apex0=(0, 0, zb + 0.32), apex1=(rng.uniform(-0.05, 0.05), 0, zb + h))
        mb.add(bm, M['DN_Yaprak_3'] if k < 3 else M['DN_Yaprak_1'])
    finish_tree(a, mb, 0.25, 2.0, 31)
    return a


def build_meyve():
    a = Asset('Agac_Meyve', 'bitki', 1500, 'govde ekseni, zemin (Z=0)')
    rng = random.Random(41)
    mb = MB()
    trunk(mb, [(-0.25, 0.15), (0.10, 0.135), (1.25, 0.10)], 7, rng, lean=(0.05, 0.0))
    for p1 in [(0.65, 0.25, 2.25), (-0.55, 0.40, 2.30), (0.05, -0.60, 2.35)]:
        mb.add(g_cyl((0.05, 0, 1.15), p1, 0.09, 0.04, sides=5), M['DN_Govde'])
    lobes = [((0.0, 0.0, 2.85), 1.05, (1, 1, 0.85), 2), ((0.75, 0.3, 2.45), 0.78, (1, 1, 0.85), 2),
             ((-0.7, 0.35, 2.5), 0.75, (1, 1, 0.85), 2), ((0.05, -0.75, 2.5), 0.72, (1, 1, 0.85), 1)]
    for i in range(5):
        t = TAU * i / 5 + 0.2
        lobes.append(((1.1 * math.cos(t), 1.1 * math.sin(t), 2.15 + rng.uniform(0, 0.35)), 0.5, (1, 1, 0.85), 1))
    lobes.append(((0.25, 0.1, 3.55), 0.58, (1, 1, 0.8), 1))
    for k, (c, r, s, sd) in enumerate(lobes):
        mb.add(g_lobe(c, r, sd, rng, s, 0.10), LEAVES[[1, 0, 1, 2, 1, 0, 1, 2, 0, 1][k]])
    finish_tree(a, mb, 0.16, 1.2, 41)
    return a


def build_cali_yuvarlak():
    a = Asset('Cali_Yuvarlak', 'bitki', 400, 'taban merkezi, zemin (Z=0)')
    rng = random.Random(53)
    mb = MB()
    lobes = [((0, 0, 0.50), 0.58, (1.1, 1.1, 0.86), 2, 0)]
    for i in range(5):
        t = TAU * i / 5 + 0.3
        lobes.append(((0.46 * math.cos(t), 0.46 * math.sin(t), rng.uniform(0.30, 0.36)), rng.uniform(0.36, 0.42),
                      (1, 1, 0.86), 1, [2, 0, 2, 0, 2][i]))
    lobes.append(((0.06, 0.04, 0.90), 0.36, (1, 1, 0.9), 1, 1))
    for c, r, sc, sd, li in lobes:
        mb.add(g_lobe(c, r, sd, rng, sc, 0.08), LEAVES[li])
    o = a.mesh(mb)
    for v in o.data.vertices:          # tabani zemine yasla (en fazla 4 cm gomulu)
        if v.co.z < -0.04:
            v.co.z = -0.04
    rmax = max(math.hypot(v.co.x, v.co.y) for v in o.data.vertices)
    a.meta.update(tac_yaricapi=round(rmax, 2), yukseklik=round(max(v.co.z for v in o.data.vertices), 2))
    return a


def build_cali_cit():
    a = Asset('Cali_Cit', 'bitki', 400, 'segment basi (X=0), cit ekseni, zemin; segment +X')
    rng = random.Random(61)
    mb = MB()
    L = 2.0
    prof = [(-0.36, -0.05), (-0.37, 0.30), (-0.37, 0.62), (-0.355, 0.88), (-0.30, 1.02), (-0.17, 1.08),
            (0.0, 1.10), (0.17, 1.08), (0.30, 1.02), (0.355, 0.88), (0.37, 0.62), (0.37, 0.30), (0.36, -0.05)]
    nx = 8
    rings = []
    for i in range(nx + 1):
        x = L * i / nx
        ring = []
        for j, (y, z) in enumerate(prof):
            if 0 < i < nx and 0 < j < len(prof) - 1:
                k = 1 + rng.uniform(-0.05, 0.05)
                ring.append((x + rng.uniform(-0.06, 0.06), y * k, z + rng.uniform(-0.04, 0.04)))
            else:
                ring.append((x, y, z))
        rings.append(ring)
    # taban acik (zemine gomulu); uc kesitler duz ve ozdes -> uc uca dikis gorunmez
    bm = g_loft(rings, cap0=True, cap1=True, closed=False)
    mb.add(bm, lambda f: M['DN_Yaprak_1'] if f.normal.z > 0.55 else M['DN_Yaprak_3'])
    a.mesh(mb)
    a.meta.update(segment_uzunlugu=L, segment_yonu_unity=[1, 0, 0], yukseklik=1.10, tac_yaricapi=0.37,
                  not_="Uc uca dizerken her kopyayi +X'te 2.0 m kaydir; uc kesitler ayni oldugu icin dikis gorunmez.")
    return a


# ----------------------------------------------------------------------------------
# Uretim
# ----------------------------------------------------------------------------------
build_trafik_kollu()
build_trafik_direk()
for k in ('yolver', 'dur', 'yaya', 'park'):
    build_tabela(k)
build_bariyer_otoyol()
build_beton_bariyer()
build_baba()
build_sokak_lambasi()
build_park_lambasi()
build_elektrik_diregi()
build_otobus_duragi()
build_bank()
build_cop_kutusu()
build_cop_konteyneri()
build_cesme()
build_kaydirak()
build_salincak()
build_cinar()
build_kavak()
build_cam()
build_meyve()
build_cali_yuvarlak()
build_cali_cit()

VISUAL = ('gorsel', 'lens', 'isik', 'hareketli')


def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def world_bounds(objs):
    mn = Vector((1e9, 1e9, 1e9))
    mx = -mn
    for o in objs:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            mn = Vector(map(min, mn, w))
            mx = Vector(map(max, mx, w))
    return mn, mx


bpy.context.view_layer.update()
manifest = {"set": "Donati", "surum": 1,
            "eksen": "Koordinatlar Unity yerel ekseninde: [x, y, z] = Blender (x, z, y). On yuz Unity -Z.",
            "fbx_ayari": "axis_forward=Z, axis_up=Y, bake_space_transform, FBX_SCALE_ALL, use_triangles",
            "malzemeler": "Donati_malzemeler.json", "varliklar": []}
report = []
for a in ASSETS:
    vis = [o for o in a.objects if o['kind'] in VISUAL]
    tris = sum(tri_count(o) for o in vis)
    mn, mx = world_bounds(vis)
    e = {"id": a.id, "fbx": f"{a.id}.fbx", "tur": a.tur,
         "boyut": [round(mx.x - mn.x, 3), round(mx.z - max(0.0, mn.z), 3), round(mx.y - mn.y, 3)],
         "ucgen": tris, "ucgen_butcesi": a.budget, "orijin": a.orijin,
         "sinirlar_unity": {"min": U(mn), "max": U(mx)},
         "zemin_alti": round(max(0.0, -mn.z), 3),
         "nesneler": [{"ad": o['export_name'], "tur": o['kind'], "konum": U(o.location)} for o in a.objects],
         "carpisma": [o['export_name'] for o in a.objects if o['kind'] == 'carpisma']}
    for k, v in a.meta.items():
        e['not' if k == 'not_' else k] = v
    manifest["varliklar"].append(e)
    report.append(f"{a.id:22s} tri={tris:5d}/{a.budget:<5d} boyut(UnityXYZ)={e['boyut']} zemin_alti={e['zemin_alti']}")
print("\n".join(report))
print("TOPLAM_UCGEN", sum(e['ucgen'] for e in manifest['varliklar']))

if not NO_EXPORT:
    OUT.mkdir(parents=True, exist_ok=True)
    BLEND.parent.mkdir(parents=True, exist_ok=True)
    vl = bpy.context.view_layer
    for a in ASSETS:
        bpy.ops.object.select_all(action='DESELECT')
        for o in a.objects:
            o.name = o['export_name']
            if o.type == 'MESH':
                o.data.name = o['export_name']
            assert o.name == o['export_name'], (a.id, o.name)
            o.select_set(True)
        vl.objects.active = a.objects[0]
        bpy.ops.export_scene.fbx(filepath=str(OUT / f"{a.id}.fbx"), use_selection=True,
                                 object_types={'MESH', 'EMPTY'}, axis_forward='Z', axis_up='Y',
                                 apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                                 use_space_transform=True, bake_space_transform=True, use_mesh_modifiers=True,
                                 mesh_smooth_type='FACE', use_triangles=True, add_leaf_bones=False,
                                 bake_anim=False, path_mode='AUTO')
        for o in a.objects:
            o.name = a.iname(o['export_name'])
            if o.type == 'MESH':
                o.data.name = a.iname(o['export_name'])
    (OUT / "Donati_manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    (OUT / "Donati_malzemeler.json").write_text(json.dumps({"materials": MAT_JSON}, ensure_ascii=False, indent=2),
                                                 encoding='utf-8')
    bpy.ops.object.select_all(action='DESELECT')
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    print("YAZILDI", OUT)
