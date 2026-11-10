# -*- coding: utf-8 -*-
"""Konutlar seti: Ofis haritasinin konut tarafi (3 apartman + 4 bahceli ev).

Calistirma:
  blender.exe -b --factory-startup -P build_konutlar.py -- [--no-export]

Tek betik butun seti fabrika ayarli bos sahnede sifirdan uretir:
  - Kaynak/Blender/Konutlar.blend (her varlik kendi koleksiyonunda, orijinde)
  - Oyun/Assets/OfisHaritasi/Modeller/Konutlar/<id>.fbx
  - Konutlar_manifest.json, Konutlar_malzemeler.json

Eksen: Blender X = Unity X, Blender Y = Unity Z, Blender Z = Unity Y (yukari).
Orijin parselin sokak tarafi on kenarinin ortasi, Z=0. Sokak -Y, parsel +Y.
Geometri dogrudan ucgen/dortgen olarak kurulur (bmesh/ops yok), duz golgeleme,
yazi/doku yok. Pencere = tek cerceve halkasi + 0,15 m iceride opak koyu cam.
"""
import bpy
import bmesh
import math
import json
import sys
import random
from pathlib import Path
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
NO_EXPORT = '--no-export' in ARGS

KOK = Path(r'C:\Users\uur\RescueTeam_OfisHaritasi')
OYUN = KOK / 'Oyun' / 'Assets' / 'OfisHaritasi' / 'Modeller' / 'Konutlar'
BLEND = KOK / 'Kaynak' / 'Blender' / 'Konutlar.blend'

Z = Vector((0, 0, 1))
FH = 3.0          # kat yuksekligi
GLASS_D = 0.15    # cam / ic yuzey derinligi
FRAME_W = 0.10    # pencere cevresi genisligi
FRAME_P = 0.04    # pencere cevresi cikintisi

# ---------------------------------------------------------------------------
# Malzemeler (renkler sRGB 0-1). Unity bu listeden URP/Lit kurar.
# ---------------------------------------------------------------------------
MALZEMELER = [
    # ad,            sRGB,                 metal, smooth, randomize
    ('KN_Siva',      (0.86, 0.80, 0.66),   0.00, 0.10, 'konut_siva'),
    ('KN_Bant',      (0.91, 0.90, 0.86),   0.00, 0.15, None),
    ('KN_Kiremit',   (0.43, 0.22, 0.165),   0.00, 0.18, None),
    ('KN_Cam',       (0.10, 0.12, 0.13),   0.10, 0.80, None),
    ('KN_Panjur',    (0.45, 0.50, 0.53),   0.00, 0.30, 'konut_panjur'),
    ('KN_Duvar',     (0.63, 0.60, 0.54),   0.00, 0.08, None),
    ('KN_Demir',     (0.17, 0.18, 0.18),   0.45, 0.40, None),
    ('KN_Kapi',      (0.42, 0.30, 0.21),   0.00, 0.30, None),
    ('KN_Beton',     (0.68, 0.67, 0.63),   0.00, 0.08, None),
    ('KN_Doseme',    (0.60, 0.56, 0.50),   0.00, 0.12, None),
    ('KN_Ahsap',     (0.48, 0.36, 0.26),   0.00, 0.22, None),
    ('KN_Kolektor',  (0.10, 0.14, 0.21),   0.20, 0.82, None),
    ('KN_Depo',      (0.90, 0.90, 0.87),   0.00, 0.45, None),
    ('KN_Kepenk',    (0.56, 0.58, 0.58),   0.50, 0.40, None),
    ('KN_Tabela',    (0.33, 0.41, 0.43),   0.00, 0.30, None),
]

PALETLER = {
    'konut_siva': [
        ('krem',            (0.86, 0.80, 0.66)),
        ('acik_hardal',     (0.83, 0.72, 0.47)),
        ('acik_somon',      (0.86, 0.72, 0.62)),
        ('kirik_beyaz',     (0.90, 0.88, 0.82)),
        ('acik_gri_yesil',  (0.71, 0.76, 0.67)),
        ('acik_mavi_gri',   (0.69, 0.74, 0.78)),
        ('acik_bej',        (0.82, 0.75, 0.63)),
    ],
    'konut_panjur': [
        ('koyu_yesil',  (0.33, 0.40, 0.32)),
        ('kahve',       (0.44, 0.32, 0.23)),
        ('gri_mavi',    (0.45, 0.50, 0.53)),
        ('bej',         (0.76, 0.72, 0.64)),
    ],
}

# Onizlemede (ve Unity varsayilaninda onerilen) palet secimleri
ONIZLEME_RENK = {
    'Apartman_A': ('acik_somon', 'bej'),
    'Apartman_B': ('acik_hardal', 'gri_mavi'),
    'Apartman_C': ('acik_gri_yesil', 'bej'),
    'Ev_A': ('krem', 'koyu_yesil'),
    'Ev_B': ('acik_mavi_gri', 'kahve'),
    'Ev_C': ('kirik_beyaz', 'koyu_yesil'),
    'Ev_D': ('acik_bej', 'kahve'),
}


def s2l(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


MAT = {}


def malzemeleri_kur():
    for ad, srgb, metal, smooth, _r in MALZEMELER:
        m = bpy.data.materials.new(ad)
        lin = tuple(s2l(c) for c in srgb)
        m.diffuse_color = (*lin, 1.0)
        if m.node_tree is None:
            m.use_nodes = True
        bs = m.node_tree.nodes.get('Principled BSDF')
        bs.inputs['Base Color'].default_value = (*lin, 1.0)
        bs.inputs['Metallic'].default_value = metal
        bs.inputs['Roughness'].default_value = 1.0 - smooth
        MAT[ad] = m


# ---------------------------------------------------------------------------
# Mesh kurucu
# ---------------------------------------------------------------------------
def newell(pts):
    n = Vector((0.0, 0.0, 0.0))
    k = len(pts)
    for i in range(k):
        a = pts[i]
        b = pts[(i + 1) % k]
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    return n


def plane_basis(n):
    n = n.normalized()
    t1 = n.cross(Z) if abs(n.z) < 0.9 else n.cross(Vector((1, 0, 0)))
    t1.normalize()
    t2 = n.cross(t1)
    return t1, t2


class MB:
    def __init__(self, name):
        self.name = name
        self.verts = []
        self.faces = []
        self.fmat = []
        self.mats = []

    def _mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def _add(self, pts, mat):
        b = len(self.verts)
        self.verts.extend((p.x, p.y, p.z) for p in pts)
        self.faces.append(tuple(range(b, b + len(pts))))
        self.fmat.append(self._mi(mat))

    def face(self, pts, mat, hint=None):
        pts = [Vector(p) for p in pts]
        if len(pts) < 3:
            return
        nrm = newell(pts)
        if nrm.length < 1e-10:
            return
        if hint is not None and nrm.dot(Vector(hint)) < 0:
            pts = pts[::-1]
            nrm = -nrm
        if len(pts) <= 4:
            self._add(pts, mat)
            return
        self.fill(pts, [], mat, nrm)

    def fill(self, outline, holes, mat, normal):
        normal = Vector(normal).normalized()
        t1, t2 = plane_basis(normal)
        loops = [[Vector(p) for p in outline]] + [[Vector(p) for p in h] for h in holes]
        loops2 = [[Vector((p.dot(t1), p.dot(t2), 0.0)) for p in lp] for lp in loops]
        allp = [p for lp in loops for p in lp]
        for (i, j, k) in tessellate_polygon(loops2):
            tri = [allp[i], allp[j], allp[k]]
            n = newell(tri)
            if n.length < 1e-10:
                continue
            if n.dot(normal) < 0:
                tri = tri[::-1]
            self._add(tri, mat)

    # -- temel hacimler ----------------------------------------------------
    def box(self, x0, x1, y0, y1, z0, z1, mat, skip=(), mats=None):
        if x1 - x0 < 1e-6 or y1 - y0 < 1e-6 or z1 - z0 < 1e-6:
            return
        F = {
            '-x': ([(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)], (-1, 0, 0)),
            '+x': ([(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)], (1, 0, 0)),
            '-y': ([(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], (0, -1, 0)),
            '+y': ([(x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0)], (0, 1, 0)),
            '-z': ([(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)], (0, 0, -1)),
            '+z': ([(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], (0, 0, 1)),
        }
        mats = mats or {}
        for k, (pts, h) in F.items():
            if k in skip:
                continue
            self.face(pts, mats.get(k, mat), h)

    def prism(self, poly, z0, z1, mat, top=True, bottom=False, top_mat=None,
              bot_mat=None, skip_edge=None):
        poly = ccw(poly)
        n = len(poly)
        for i in range(n):
            a = poly[i]
            b = poly[(i + 1) % n]
            if skip_edge and skip_edge(a, b):
                continue
            nrm = (b[1] - a[1], -(b[0] - a[0]), 0)
            self.face([(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)],
                      mat, nrm)
        if top:
            self.face([(x, y, z1) for x, y in poly], top_mat or mat, (0, 0, 1))
        if bottom:
            self.face([(x, y, z0) for x, y in poly], bot_mat or mat, (0, 0, -1))

    def obox(self, o, ex, ey, ez, mat, mats=None, skip=()):
        o, ex, ey, ez = Vector(o), Vector(ex), Vector(ey), Vector(ez)
        c = o + (ex + ey + ez) * 0.5
        P = lambda i, j, k: o + ex * i + ey * j + ez * k
        F = {
            '-ex': [P(0, 0, 0), P(0, 1, 0), P(0, 1, 1), P(0, 0, 1)],
            '+ex': [P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1)],
            '-ey': [P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)],
            '+ey': [P(0, 1, 0), P(1, 1, 0), P(1, 1, 1), P(0, 1, 1)],
            '-ez': [P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0)],
            '+ez': [P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1)],
        }
        mats = mats or {}
        for k, pts in F.items():
            if k in skip:
                continue
            fc = sum(pts, Vector()) / 4
            self.face(pts, mats.get(k, mat), fc - c)

    def beam(self, a, b, w, mat, caps=False):
        a, b = Vector(a), Vector(b)
        d = b - a
        side = d.cross(Z)
        if side.length < 1e-6:
            side = d.cross(Vector((1, 0, 0)))
        side.normalize()
        up = side.cross(d).normalized()
        if up.z < 0:
            up = -up
        o = a - side * (w / 2) - up * (w / 2)
        self.obox(o, side * w, d, up * w, mat, skip=() if caps else ('-ey', '+ey'))

    def cyl(self, center, axis, r, length, n, mat, caps=True, cap_mat=None):
        c = Vector(center)
        if axis == 'x':
            A, B, C = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
        elif axis == 'y':
            A, B, C = Vector((0, 1, 0)), Vector((0, 0, 1)), Vector((1, 0, 0))
        else:
            A, B, C = Vector((0, 0, 1)), Vector((1, 0, 0)), Vector((0, 1, 0))
        ring = []
        for i in range(n):
            t = 2 * math.pi * (i + 0.5) / n
            ring.append(B * (r * math.cos(t)) + C * (r * math.sin(t)))
        h = A * (length / 2)
        for i in range(n):
            p, q = ring[i], ring[(i + 1) % n]
            self.face([c - h + p, c - h + q, c + h + q, c + h + p], mat, (p + q))
        if caps:
            self.face([c + h + p for p in ring], cap_mat or mat, A)
            self.face([c - h + p for p in ring], cap_mat or mat, -A)

    def cap(self, a, b, w, h, mat):
        """Mahya/kirma basligi: a-b cizgisi boyunca ucgen kesitli prizma."""
        a, b = Vector(a), Vector(b)
        d = (b - a)
        dh = Vector((d.x, d.y, 0))
        side = Vector((-dh.y, dh.x, 0)).normalized()
        up = side.cross(d).normalized()
        if up.z < 0:
            up = -up
        L = -side * (w / 2) - up * 0.03
        R = side * (w / 2) - up * 0.03
        T = up * h
        self.face([a + L, b + L, b + T, a + T], mat, -side + up)
        self.face([a + T, b + T, b + R, a + R], mat, side + up)
        self.face([a + L, a + T, a + R], mat, -d)
        self.face([b + R, b + T, b + L], mat, d)

    def ring2(self, pout, pin, z0, z1, mat, outer=True, inner=True, top=True, bottom=True,
              top_mat=None):
        pout = ccw(pout)
        pin = ccw(pin)
        n = len(pout)
        assert n == len(pin)
        for i in range(n):
            a, b = pout[i], pout[(i + 1) % n]
            c, d = pin[i], pin[(i + 1) % n]
            if outer:
                self.face([(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)],
                          mat, (b[1] - a[1], -(b[0] - a[0]), 0))
            if inner:
                self.face([(c[0], c[1], z0), (d[0], d[1], z0), (d[0], d[1], z1), (c[0], c[1], z1)],
                          mat, (-(d[1] - c[1]), d[0] - c[0], 0))
            if top:
                self.face([(a[0], a[1], z1), (b[0], b[1], z1), (d[0], d[1], z1), (c[0], c[1], z1)],
                          top_mat or mat, (0, 0, 1))
            if bottom:
                self.face([(a[0], a[1], z0), (b[0], b[1], z0), (d[0], d[1], z0), (c[0], c[1], z0)],
                          mat, (0, 0, -1))

    def slab(self, plan, zfn, tv, top_mat, bot_mat, edge_mat, skip_edge=None):
        """Egik cati levhasi: plan poligonu, alt yuzey zfn(x,y), dusey kalinlik tv."""
        plan = ccw(plan)
        bot = [Vector((x, y, zfn(x, y))) for x, y in plan]
        top = [p + Z * tv for p in bot]
        self.face(top, top_mat, (0, 0, 1))
        self.face(bot, bot_mat, (0, 0, -1))
        n = len(plan)
        for i in range(n):
            a, b = plan[i], plan[(i + 1) % n]
            if skip_edge and skip_edge(a, b):
                continue
            j = (i + 1) % n
            self.face([bot[i], bot[j], top[j], top[i]], edge_mat, (b[1] - a[1], -(b[0] - a[0]), 0))

    def tri_count(self):
        return sum(len(f) - 2 for f in self.faces)


def ccw(poly):
    a = 0.0
    n = len(poly)
    for i in range(n):
        x0, y0 = poly[i][0], poly[i][1]
        x1, y1 = poly[(i + 1) % n][0], poly[(i + 1) % n][1]
        a += x0 * y1 - x1 * y0
    return list(poly) if a > 0 else list(poly)[::-1]


def crect(x0, x1, y0, y1, c=0.0):
    if c <= 0:
        return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    return [(x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c),
            (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c)]


def offset_poly(poly, d):
    """Konveks CCW poligonu d kadar disa kaydirir (d<0 iceri, kucuk degerlerde)."""
    poly = ccw(poly)
    n = len(poly)
    lines = []
    for i in range(n):
        a = Vector((poly[i][0], poly[i][1]))
        b = Vector((poly[(i + 1) % n][0], poly[(i + 1) % n][1]))
        t = (b - a).normalized()
        nn = Vector((t.y, -t.x))
        lines.append((a + nn * d, t))
    out = []
    for i in range(n):
        p1, t1 = lines[i - 1]
        p2, t2 = lines[i]
        den = t1.x * t2.y - t1.y * t2.x
        if abs(den) < 1e-9:
            out.append((p2.x, p2.y))
            continue
        s = ((p2.x - p1.x) * t2.y - (p2.y - p1.y) * t2.x) / den
        q = p1 + t1 * s
        out.append((q.x, q.y))
    return out


# ---------------------------------------------------------------------------
# Cephe, pencere, kapi
# ---------------------------------------------------------------------------
class Fac:
    def __init__(self, P0, U, W):
        self.P0 = Vector((P0[0], P0[1], 0.0))
        self.U = Vector(U).normalized()
        self.N = self.U.cross(Z)
        self.W = W

    def p(self, u, z, w=0.0):
        return self.P0 + self.U * u + self.N * w + Z * z

    def xy(self, u, w=0.0):
        q = self.P0 + self.U * u + self.N * w
        return (q.x, q.y)

    def box(self, mb, u0, u1, z0, z1, w0, w1, mat, skip=('-w',), mats=None):
        P = lambda u, z, w: self.p(u, z, w)
        U, N = self.U, self.N
        F = {
            '-u': ([P(u0, z0, w0), P(u0, z0, w1), P(u0, z1, w1), P(u0, z1, w0)], -U),
            '+u': ([P(u1, z0, w0), P(u1, z0, w1), P(u1, z1, w1), P(u1, z1, w0)], U),
            '-z': ([P(u0, z0, w0), P(u1, z0, w0), P(u1, z0, w1), P(u0, z0, w1)], -Z),
            '+z': ([P(u0, z1, w0), P(u1, z1, w0), P(u1, z1, w1), P(u0, z1, w1)], Z),
            '-w': ([P(u0, z0, w0), P(u1, z0, w0), P(u1, z1, w0), P(u0, z1, w0)], -N),
            '+w': ([P(u0, z0, w1), P(u1, z0, w1), P(u1, z1, w1), P(u0, z1, w1)], N),
        }
        mats = mats or {}
        for k, (pts, h) in F.items():
            if k in skip:
                continue
            mb.face(pts, mats.get(k, mat), h)


def opening(mb, fac, o, rng):
    u0, u1, z0, z1 = o['u0'], o['u1'], o['z0'], o['z1']
    kind = o.get('kind', 'pencere')
    pj = o.get('pj')
    U, N = fac.U, fac.N
    P = fac.p
    fw, fp = FRAME_W, FRAME_P
    frame_mat = 'KN_Bant'
    if kind == 'vitrin':
        fw, fp, frame_mat = 0.07, 0.03, 'KN_Kepenk'
    bottom = kind in ('pencere', 'merdiven')
    depth = {'kapi': 0.10, 'depo': 0.10}.get(kind, GLASS_D)
    # cerceve halkasi
    zb = z0 - fw if bottom else z0
    strips = [(u0 - fw, u1 + fw, z1, z1 + fw), (u0 - fw, u0, z0, z1), (u1, u1 + fw, z0, z1)]
    if bottom:
        strips.append((u0 - fw, u1 + fw, z0 - fw, z0))
    for a0, a1, b0, b1 in strips:
        mb.face([P(a0, b0, fp), P(a1, b0, fp), P(a1, b1, fp), P(a0, b1, fp)], frame_mat, N)
    mb.face([P(u0 - fw, z1 + fw, 0), P(u1 + fw, z1 + fw, 0), P(u1 + fw, z1 + fw, fp),
             P(u0 - fw, z1 + fw, fp)], frame_mat, Z)
    mb.face([P(u0 - fw, zb, 0), P(u0 - fw, z1 + fw, 0), P(u0 - fw, z1 + fw, fp),
             P(u0 - fw, zb, fp)], frame_mat, -U)
    mb.face([P(u1 + fw, zb, 0), P(u1 + fw, z1 + fw, 0), P(u1 + fw, z1 + fw, fp),
             P(u1 + fw, zb, fp)], frame_mat, U)
    if bottom:
        mb.face([P(u0 - fw, zb, 0), P(u1 + fw, zb, 0), P(u1 + fw, zb, fp), P(u0 - fw, zb, fp)],
                frame_mat, -Z)
    # ic kenarlar (kesit)
    rev = frame_mat if kind != 'vitrin' else 'KN_Kepenk'
    mb.face([P(u0, z0, fp), P(u0, z0, -depth), P(u0, z1, -depth), P(u0, z1, fp)], rev, U)
    mb.face([P(u1, z0, fp), P(u1, z0, -depth), P(u1, z1, -depth), P(u1, z1, fp)], rev, -U)
    mb.face([P(u0, z1, fp), P(u1, z1, fp), P(u1, z1, -depth), P(u0, z1, -depth)], rev, -Z)
    mb.face([P(u0, z0, fp), P(u1, z0, fp), P(u1, z0, -depth), P(u0, z0, -depth)],
            'KN_Beton' if kind in ('kapi', 'depo', 'vitrin', 'balkon') else rev, Z)
    # ic yuzey
    if kind == 'kapi':
        mb.face([P(u0, z0, -depth), P(u1, z0, -depth), P(u1, z1, -depth), P(u0, z1, -depth)],
                'KN_Kapi', N)
    elif kind == 'depo':
        um = (u0 + u1) / 2
        mb.face([P(u0, z0, -0.13), P(um, z0, -0.13), P(um, z1, -0.13), P(u0, z1, -0.13)],
                'KN_Kapi', N)
        mb.face([P(um, z0, -0.08), P(u1, z0, -0.08), P(u1, z1, -0.08), P(um, z1, -0.08)],
                'KN_Kapi', N)
        mb.face([P(um, z0, -0.13), P(um, z0, -0.08), P(um, z1, -0.08), P(um, z1, -0.13)],
                'KN_Kapi', -U)
    else:
        mb.face([P(u0, z0, -depth), P(u1, z0, -depth), P(u1, z1, -depth), P(u0, z1, -depth)],
                'KN_Cam', N)
    if kind == 'vitrin':
        # yari inik kepenk + kapi kayiti
        frac = o.get('kepenk', 0.45)
        zk = z1 - frac * (z1 - z0)
        mb.face([P(u0, zk, -0.04), P(u1, zk, -0.04), P(u1, z1, -0.04), P(u0, z1, -0.04)],
                'KN_Kepenk', N)
        mb.face([P(u0, zk, -0.07), P(u1, zk, -0.07), P(u1, zk, -0.04), P(u0, zk, -0.04)],
                'KN_Kepenk', -Z)
        um = u0 + 0.36 * (u1 - u0)
        fac.box(mb, um - 0.04, um + 0.04, z0, zk, -depth, -depth + 0.07, 'KN_Kepenk',
                skip=('-w', '-z', '+z'))
    if pj == 'rulo':
        frac = o.get('rulo', rng.uniform(0.18, 0.55))
        zr = z1 - frac * (z1 - z0)
        mb.face([P(u0, zr, -0.09), P(u1, zr, -0.09), P(u1, z1, -0.09), P(u0, z1, -0.09)],
                'KN_Panjur', N)
        mb.face([P(u0, zr, -0.12), P(u1, zr, -0.12), P(u1, zr, -0.09), P(u0, zr, -0.09)],
                'KN_Panjur', -Z)
    elif pj == 'kanat':
        lw = (u1 - u0) / 2
        g = fw + 0.02
        for a0, a1 in ((u0 - g - lw, u0 - g), (u1 + g, u1 + g + lw)):
            fac.box(mb, a0, a1, z0, z1, 0.0, 0.035, 'KN_Panjur', skip=('-w',))


def wall_side(mb, fac, zb, top_pts, ops, rng, mat='KN_Siva'):
    """Cephe duvari: alt kenar zb, ust kontur top_pts [(u,z),...] (soldan saga)."""
    # top_pts soldan saga verilir; kontur saat yonu tersine: alt sol, alt sag, ust sag ... ust sol
    outline = [(0.0, zb), (fac.W, zb)] + sorted(top_pts, key=lambda p: -p[0])
    o3 = [fac.p(u, z) for u, z in outline]
    holes = []
    for o in ops:
        holes.append([fac.p(o['u0'], o['z0']), fac.p(o['u1'], o['z0']),
                      fac.p(o['u1'], o['z1']), fac.p(o['u0'], o['z1'])])
    mb.fill(o3, holes, mat, fac.N)
    for o in ops:
        opening(mb, fac, o, rng)


def to_u(fac, side, a):
    if side in ('S', 'N'):
        return (a - fac.P0.x) * fac.U.x
    return (a - fac.P0.y) * fac.U.y


class Mass:
    """Pahli koseli dikdortgen bina kutlesi. ops: {'S':[...], ...} dunya a-araliklari."""

    def __init__(self, x0, x1, y0, y1, c=0.08, corners=None):
        self.x0, self.x1, self.y0, self.y1 = x0, x1, y0, y1
        cs = dict(SW=c, SE=c, NE=c, NW=c)
        if corners:
            cs.update(corners)
        self.c = cs
        self.poly = self._poly()

    def _poly(self):
        x0, x1, y0, y1, c = self.x0, self.x1, self.y0, self.y1, self.c
        pts = []
        pts += [(x0 + c['SW'], y0)] if c['SW'] > 0 else [(x0, y0)]
        pts += [(x1 - c['SE'], y0), (x1, y0 + c['SE'])] if c['SE'] > 0 else [(x1, y0)]
        pts += [(x1, y1 - c['NE']), (x1 - c['NE'], y1)] if c['NE'] > 0 else [(x1, y1)]
        pts += [(x0 + c['NW'], y1), (x0, y1 - c['NW'])] if c['NW'] > 0 else [(x0, y1)]
        if c['SW'] > 0:
            pts += [(x0, y0 + c['SW'])]
        return pts

    def fac(self, side):
        x0, x1, y0, y1, c = self.x0, self.x1, self.y0, self.y1, self.c
        if side == 'S':
            return Fac((x0 + c['SW'], y0), (1, 0, 0), (x1 - c['SE']) - (x0 + c['SW']))
        if side == 'E':
            return Fac((x1, y0 + c['SE']), (0, 1, 0), (y1 - c['NE']) - (y0 + c['SE']))
        if side == 'N':
            return Fac((x1 - c['NE'], y1), (-1, 0, 0), (x1 - c['NE']) - (x0 + c['NW']))
        return Fac((x0, y1 - c['NW']), (0, -1, 0), (y1 - c['NW']) - (y0 + c['SW']))

    def build(self, mb, zb, zfn, ops, rng, sides='SENW', gable=None, mat='KN_Siva',
              chamfers=True):
        """zfn(x,y): duvar ust kotu. gable: {'side':'EW' veya 'SN', 'm': mahya koordinati}"""
        for side in sides:
            f = self.fac(side)
            pa = f.p(0, 0)
            pb = f.p(f.W, 0)
            za, zbb = zfn(pa.x, pa.y), zfn(pb.x, pb.y)
            tops = [(0.0, za), (f.W, zbb)]
            if gable and side in gable['side']:
                m = gable['m']
                if side in 'SN':
                    ua = (m - f.P0.x) * f.U.x
                    q = f.p(ua, 0)
                else:
                    ua = (m - f.P0.y) * f.U.y
                    q = f.p(ua, 0)
                tops.insert(1, (ua, zfn(q.x, q.y)))
            lst = []
            for o in ops.get(side, []):
                a0, a1 = to_u(f, side, o['a0']), to_u(f, side, o['a1'])
                d = dict(o)
                d['u0'], d['u1'] = min(a0, a1), max(a0, a1)
                lst.append(d)
            wall_side(mb, f, zb, tops, lst, rng, mat)
        if chamfers:
            P = self.poly
            n = len(P)
            for i in range(n):
                a, b = P[i], P[(i + 1) % n]
                if abs(a[0] - b[0]) > 1e-6 and abs(a[1] - b[1]) > 1e-6:
                    self._chamfer(mb, a, b, zb, zfn, mat)

    def _chamfer(self, mb, a, b, zb, zfn, mat):
        mb.face([(a[0], a[1], zb), (b[0], b[1], zb), (b[0], b[1], zfn(*b)), (a[0], a[1], zfn(*a))],
                mat, (b[1] - a[1], -(b[0] - a[0]), 0))

    def band(self, mb, z0, z1, out=0.05, mat='KN_Bant', bottom=True, top_off=0.0):
        po = offset_poly(self.poly, out)
        mb.ring2(po, self.poly, z0, z1 - top_off, mat, outer=True, inner=False, top=True,
                 bottom=bottom)


def W(a0, a1, z0, z1, kind='pencere', pj=None, **kw):
    d = dict(a0=a0, a1=a1, z0=z0, z1=z1, kind=kind, pj=pj)
    d.update(kw)
    return d


# ---------------------------------------------------------------------------
# Bina parcasi yardimcilari
# ---------------------------------------------------------------------------
def balkon(mb, m, side, a0, a1, f, depth, par_h=1.0, t=0.12, ground=False, par_mat='KN_Siva'):
    fac = m.fac(side)
    u0, u1 = sorted((to_u(fac, side, a0), to_u(fac, side, a1)))
    if ground:
        fac.box(mb, u0, u1, -0.6, f, 0.0, depth, 'KN_Duvar', skip=('-w', '-z'),
                mats={'+z': 'KN_Doseme'})
    else:
        fac.box(mb, u0, u1, f - 0.2, f, 0.0, depth, 'KN_Bant', skip=('-w',))
    U = [(u0, 0.0), (u0, depth), (u1, depth), (u1, 0.0), (u1 - t, 0.0), (u1 - t, depth - t),
         (u0 + t, depth - t), (u0 + t, 0.0)]
    poly = [fac.xy(u, w) for u, w in U]

    def on_wall(a, b):
        da = (Vector((a[0], a[1], 0)) - fac.P0).dot(fac.N)
        db = (Vector((b[0], b[1], 0)) - fac.P0).dot(fac.N)
        return abs(da) < 1e-4 and abs(db) < 1e-4

    mb.prism(poly, f, f + par_h, par_mat, top=True, top_mat='KN_Bant', skip_edge=on_wall)
    # dunya izdusumu (COL icin)
    pts = [fac.xy(u0, 0), fac.xy(u1, depth)]
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return (min(xs), max(xs), min(ys), max(ys))


def hip_roof(mb, x0, x1, y0, y1, zS, pitch, ov, t=0.18, sacak='KN_Bant'):
    X0, X1, Y0, Y1 = x0 - ov, x1 + ov, y0 - ov, y1 + ov
    s = math.tan(math.radians(pitch))
    zF = zS + t
    Lx, Ly = X1 - X0, Y1 - Y0
    if Lx >= Ly:
        h = Ly / 2
        r0 = Vector((X0 + h, (Y0 + Y1) / 2, zF + h * s))
        r1 = Vector((X1 - h, (Y0 + Y1) / 2, zF + h * s))
    else:
        h = Lx / 2
        r0 = Vector(((X0 + X1) / 2, Y0 + h, zF + h * s))
        r1 = Vector(((X0 + X1) / 2, Y1 - h, zF + h * s))
    c00, c10 = Vector((X0, Y0, zF)), Vector((X1, Y0, zF))
    c11, c01 = Vector((X1, Y1, zF)), Vector((X0, Y1, zF))
    mb.face([(X0, Y0, zS), (X1, Y0, zS), (X1, Y1, zS), (X0, Y1, zS)], sacak, (0, 0, -1))
    mb.ring2(crect(X0, X1, Y0, Y1), crect(X0, X1, Y0, Y1), zS, zF, sacak,
             outer=True, inner=False, top=False, bottom=False)
    T = 'KN_Kiremit'
    if Lx >= Ly:
        mb.face([c00, c10, r1, r0], T, (0, -1, 1))
        mb.face([c11, c01, r0, r1], T, (0, 1, 1))
        mb.face([c01, c00, r0], T, (-1, 0, 1))
        mb.face([c10, c11, r1], T, (1, 0, 1))
        hips = [(c00, r0), (c01, r0), (c10, r1), (c11, r1)]
    else:
        mb.face([c00, c10, r0], T, (0, -1, 1))
        mb.face([c11, c01, r1], T, (0, 1, 1))
        mb.face([c01, c00, r0, r1], T, (-1, 0, 1))
        mb.face([c10, c11, r1, r0], T, (1, 0, 1))
        hips = [(c00, r0), (c10, r0), (c01, r1), (c11, r1)]
    for a, b in hips:
        mb.cap(a, b, 0.20, 0.08, T)
    if (r1 - r0).length > 0.05:
        mb.cap(r0 - (r1 - r0).normalized() * 0.08, r1 + (r1 - r0).normalized() * 0.08,
               0.24, 0.10, T)
    return dict(zS=zS, zF=zF, zR=r0.z, X0=X0, X1=X1, Y0=Y0, Y1=Y1, s=s)


def gable_roof(mb, x0, x1, y0, y1, zE, pitch, ridge, ov_e, ov_v, t=0.18, sacak='KN_Bant',
               extra_plans=None):
    """Besik cati: duvar cizgisinde alt yuzey zE. ridge 'X' veya 'Y'."""
    s = math.tan(math.radians(pitch))
    tv = t / math.cos(math.radians(pitch))
    T = 'KN_Kiremit'
    if ridge == 'X':
        ym = (y0 + y1) / 2
        f1 = lambda x, y: zE + (y - y0) * s
        f2 = lambda x, y: zE + (y1 - y) * s
        X0, X1 = x0 - ov_v, x1 + ov_v
        p1 = crect(X0, X1, y0 - ov_e, ym)
        p2 = crect(X0, X1, ym, y1 + ov_e)
        rs = lambda a, b: abs(a[1] - ym) < 1e-6 and abs(b[1] - ym) < 1e-6
        mb.slab(p1, f1, tv, T, sacak, sacak, rs)
        mb.slab(p2, f2, tv, T, sacak, sacak, rs)
        zr = zE + (ym - y0) * s + tv
        mb.cap((X0 - 0.05, ym, zr), (X1 + 0.05, ym, zr), 0.26, 0.11, T)
        zfn = lambda x, y: zE + ((y1 - y0) / 2 - abs(y - ym)) * s
        return dict(zfn=zfn, m=ym, zR=zr, s=s, tv=tv)
    xm = (x0 + x1) / 2
    f1 = lambda x, y: zE + (x - x0) * s
    f2 = lambda x, y: zE + (x1 - x) * s
    Y0, Y1 = y0 - ov_v, y1 + ov_v
    p1 = crect(x0 - ov_e, xm, Y0, Y1)
    p2 = crect(xm, x1 + ov_e, Y0, Y1)
    rs = lambda a, b: abs(a[0] - xm) < 1e-6 and abs(b[0] - xm) < 1e-6
    mb.slab(p1, f1, tv, T, sacak, sacak, rs)
    mb.slab(p2, f2, tv, T, sacak, sacak, rs)
    zr = zE + (xm - x0) * s + tv
    mb.cap((xm, Y0 - 0.05, zr), (xm, Y1 + 0.05, zr), 0.26, 0.11, T)
    zfn = lambda x, y: zE + ((x1 - x0) / 2 - abs(x - xm)) * s
    return dict(zfn=zfn, m=xm, zR=zr, s=s, tv=tv)


def baca(mb, cx, cy, z0, z1, a=0.30, b=0.40):
    mb.box(cx - a, cx + a, cy - b, cy + b, z0, z1, 'KN_Siva', skip=('-z', '+z'))
    mb.box(cx - a - 0.06, cx + a + 0.06, cy - b - 0.06, cy + b + 0.06, z1, z1 + 0.10, 'KN_Bant')
    mb.box(cx - a + 0.08, cx + a - 0.08, cy - b + 0.08, cy + b - 0.08, z1 + 0.10, z1 + 0.32,
           'KN_Bant', skip=('-z',))


def sacak_kutu(mb, x0, x1, y0, y1, z0, z1, mat='KN_Bant'):
    mb.prism(crect(x0, x1, y0, y1, 0.04), z0, z1, mat, top=True, bottom=True,
             skip_edge=lambda a, b: abs(a[1] - y1) < 1e-6 and abs(b[1] - y1) < 1e-6)


def gunes_unitesi(mb, cx, yf, zroof):
    """Turk tipi gunes enerjisi: iki egik kolektor + ustte yatay beyaz depo, onu -Y."""
    th = math.radians(40)
    L = 1.85
    d = Vector((0, math.cos(th), math.sin(th)))
    n = Vector((0, -math.sin(th), math.cos(th)))
    zf = zroof(yf) + 0.18
    for k in range(2):
        xl = cx - 0.98 + k * 1.01
        mb.obox((xl, yf, zf), (0.95, 0, 0), d * L, n * 0.06, 'KN_Kepenk',
                mats={'+ez': 'KN_Kolektor'})
    yt = yf + L * math.cos(th)
    zt = zf + L * math.sin(th)
    mb.cyl((cx, yt + 0.12, zt + 0.24), 'x', 0.28, 2.10, 10, 'KN_Depo')
    for x in (cx - 0.86, cx + 0.86):
        mb.beam((x, yf + 0.06, zroof(yf + 0.06) - 0.02), (x, yf + 0.06, zf + 0.02), 0.05,
                'KN_Demir')
        mb.beam((x, yt + 0.02, zroof(yt + 0.02) - 0.02), (x, yt + 0.02, zt + 0.04), 0.05,
                'KN_Demir')
        mb.beam((x, yt + 0.05, zt - 0.02), (x, yt + 0.22, zt + 0.02), 0.05, 'KN_Demir')


def basamaklar(A, x0, x1, y_on, y_arka, z_ust, n, mat='KN_Beton'):
    """Sokak yonune (-Y) inen, birbirine binmeyen n basamak; en ust basamak z_ust."""
    d = (y_arka - y_on) / n
    for k in range(n):
        ya, yb = y_on + k * d, y_on + (k + 1) * d
        zt = z_ust * (k + 1) / n
        A.bina.box(x0, x1, ya, yb, -0.6, zt, mat, skip=('-z', '+y'))
        A.col(f'Basamak_{k + 1}', x0, x1, ya, yb, -0.6, zt)


def doseme(mb, x0, x1, y0, y1, mat='KN_Beton'):
    mb.box(x0, x1, y0, y1, -0.6, 0.04, mat, skip=('-z',))


# ---------------------------------------------------------------------------
# Bahce duvari: 0,5 m taban + 0,7 m dikey kare demir parmaklik, kagir ayaklar
# ---------------------------------------------------------------------------
PH = 0.17           # ayak yari genisligi (0,34)
P_C = 0.20          # ayak merkezinin sinirdan iceri uzakligi
Q_WALL = (0.09, 0.31)
Q_COP = (0.06, 0.34)
Q_RAIL = (0.18, 0.22)
Q_BAR = (0.19, 0.21)
Z_TABAN = 0.44
Z_COP = 0.50
Z_RB = 0.54
Z_RT0, Z_RT1 = 1.16, 1.20
Z_PIL, Z_CAP = 1.35, 1.43
BAR_SPACING = 0.22
MAX_PANEL = 3.2


def run_map(side, G, D, t0, t1, q0, q1):
    if side == 'On':
        return t0, t1, q0, q1, ('-x', '+x')
    if side == 'Arka':
        return t0, t1, D - q1, D - q0, ('-x', '+x')
    if side == 'Sol':
        return -G / 2 + q0, -G / 2 + q1, t0, t1, ('-y', '+y')
    return G / 2 - q1, G / 2 - q0, t0, t1, ('-y', '+y')


def run_box(mb, side, G, D, t0, t1, q0, q1, z0, z1, mat, skip_t=True, skip=(), mats=None):
    x0, x1, y0, y1, td = run_map(side, G, D, t0, t1, q0, q1)
    sk = set(skip)
    if skip_t:
        sk |= set(td)
    mb.box(x0, x1, y0, y1, z0, z1, mat, skip=sk, mats=mats)


def bahce_duvari(mb, cols, aid, side, G, D, gates=(), own_start=True, own_end=True):
    ts, te = (-G / 2, G / 2) if side in ('On', 'Arka') else (0.0, D)
    pil = [(ts + P_C, own_start), (te - P_C, own_end)]
    for g0, g1 in gates:
        for tc in (g0 - PH, g1 + PH):
            if not any(abs(tc - p) < 0.06 for p, _ in pil):
                pil.append((tc, True))
    pil.sort()
    allp = []
    panels = []
    runs = []
    cur = None
    for (pa, oa), (pb, ob) in zip(pil, pil[1:]):
        fa, fb = pa + PH, pb - PH
        gate = any(abs(fa - g0) < 0.07 and abs(fb - g1) < 0.07 for g0, g1 in gates)
        if gate:
            if cur:
                runs.append(cur)
            cur = None
            continue
        n = max(1, math.ceil((pb - pa) / MAX_PANEL))
        pts = [pa + (pb - pa) * k / n for k in range(n + 1)]
        for k in range(1, n):
            allp.append((pts[k], True))
        for p, q in zip(pts, pts[1:]):
            panels.append((p + PH, q - PH))
        s0 = pa - PH if oa else pa + PH
        s1 = pb + PH if ob else pb - PH
        if cur is None:
            cur = [s0, s1]
        else:
            cur[1] = s1
    if cur:
        runs.append(cur)
    allp += pil
    ntri0 = mb.tri_count()
    for tc, owned in allp:
        if not owned:
            continue
        run_box(mb, side, G, D, tc - PH, tc + PH, P_C - PH, P_C + PH, -0.6, Z_PIL, 'KN_Duvar',
                skip_t=False, skip=('-z', '+z'))
        run_box(mb, side, G, D, tc - 0.20, tc + 0.20, P_C - 0.20, P_C + 0.20, Z_PIL, Z_CAP,
                'KN_Beton', skip_t=False, skip=('-z',))
    nbars = 0
    for a, b in panels:
        L = b - a
        if L < 0.05:
            continue
        run_box(mb, side, G, D, a, b, *Q_WALL, -0.6, Z_TABAN, 'KN_Duvar', skip=('-z', '+z'))
        run_box(mb, side, G, D, a, b, *Q_COP, Z_TABAN, Z_COP, 'KN_Beton')
        run_box(mb, side, G, D, a, b, *Q_RAIL, Z_COP, Z_RB, 'KN_Demir', skip=('-z',))
        run_box(mb, side, G, D, a, b, *Q_RAIL, Z_RT0, Z_RT1, 'KN_Demir')
        nb = max(0, round(L / BAR_SPACING) - 1)
        for k in range(1, nb + 1):
            t = a + L * k / (nb + 1)
            run_box(mb, side, G, D, t - 0.01, t + 0.01, *Q_BAR, Z_RB, Z_RT0, 'KN_Demir',
                    skip_t=False, skip=('-z', '+z'))
        nbars += nb
    for i, (s0, s1) in enumerate(runs):
        x0, x1, y0, y1, _ = run_map(side, G, D, s0, s1, P_C - PH, P_C + PH)
        cols.append((f'COL_{aid}_Duvar_{side}_{i + 1}', (x0, x1, y0, y1, -0.6, Z_CAP)))
    return dict(kapilar=list(gates), parmak=nbars, ucgen=mb.tri_count() - ntri0)


# ---------------------------------------------------------------------------
# Varliklar
# ---------------------------------------------------------------------------
class Asset:
    def __init__(self, aid, G, D):
        self.id = aid
        self.G, self.D = G, D
        self.rng = random.Random('konut-' + aid)
        self.bina = MB(f'{aid}_Bina')
        self.bahce = MB(f'{aid}_Bahce')
        self.duvarlar = {}
        self.cols = []
        self.izler = []
        self.giris = []
        self.park = []
        self.agac = []
        self.notlar = {}

    def col(self, name, x0, x1, y0, y1, z0, z1):
        self.cols.append((f'COL_{self.id}_{name}', (x0, x1, y0, y1, z0, z1)))

    def iz(self, ad, x0, x1, y0, y1, h, tur='bina'):
        self.izler.append(dict(ad=ad, tur=tur, min=[r3(x0), r3(y0)], max=[r3(x1), r3(y1)],
                               yukseklik=r3(h)))

    def duvar(self, side, gates=(), own_start=True, own_end=True):
        mb = MB(f'{self.id}_Duvar_{side}')
        info = bahce_duvari(mb, self.cols, self.id, side, self.G, self.D, gates, own_start,
                            own_end)
        self.duvarlar[side] = (mb, info)
        return info

    def tum_duvarlar(self, front_gates):
        self.duvar('On', front_gates)
        self.duvar('Arka')
        self.duvar('Sol', own_start=False, own_end=False)
        self.duvar('Sag', own_start=False, own_end=False)

    def park_yeri(self, cx, cy, yon=0):
        self.park.append(dict(konum=[r3(cx), 0.04, r3(cy)], yon_derece=yon, boyut=[2.5, 5.0]))


def r3(v):
    return round(float(v), 3)


def rulo(o):
    o['pj'] = 'rulo'
    return o


def apt_windows(f, xs, pj='rulo', h=1.4, sill=0.9, kind='pencere'):
    return [W(a0, a1, f + sill, f + sill + h, kind, pj) for a0, a1 in xs]


# ---------------------------- Apartman_A ------------------------------------
def apartman_a():
    A = Asset('Apartman_A', 22.0, 22.0)
    mb, rng = A.bina, A.rng
    x0, x1, y0, y1 = -6.0, 6.0, 5.0, 15.0
    F = [0.45, 3.45, 6.45]
    zS = 9.45
    m = Mass(x0, x1, y0, y1)
    ops = {'S': [], 'E': [], 'N': [], 'W': []}
    for i, f in enumerate(F):
        for s in (-1, 1):
            ops['S'].append(W(*sorted((s * 5.0, s * 4.1)), f, f + 2.2, 'balkon', 'rulo'))
            ops['S'].append(W(*sorted((s * 3.6, s * 2.4)), f + 0.9, f + 2.3, 'pencere', 'rulo'))
        for side in 'EW':
            ops[side] += apt_windows(f, [(6.6, 7.8), (11.6, 12.8)])
            ops[side].append(W(9.7, 10.3, f + 1.6, f + 2.2))
        ops['N'] += apt_windows(f, [(-4.9, -3.7), (-2.0, -0.8), (0.8, 2.0), (3.7, 4.9)])
    ops['S'].append(W(-0.6, 0.6, F[0], 2.75, 'kapi'))
    ops['S'].append(W(-0.5, 0.5, 4.5, 5.9, 'merdiven'))
    ops['S'].append(W(-0.5, 0.5, 7.5, 8.9, 'merdiven'))
    m.build(mb, -0.6, lambda x, y: zS, ops, rng)
    m.band(mb, -0.6, F[0], out=0.04, mat='KN_Duvar', bottom=False, top_off=0.01)
    for f in F[1:]:
        m.band(mb, f - 0.22, f - 0.01)
    for i, f in enumerate(F):
        for ad, (a0, a1) in (('Sol', (-5.5, -1.9)), ('Sag', (1.9, 5.5))):
            bb = balkon(mb, m, 'S', a0, a1, f, 1.3, ground=(i == 0))
            if i == 0:
                A.col(f'Balkon_{ad}', bb[0], bb[1], bb[2], bb[3], -0.6, f + 1.0)
    sacak_kutu(mb, -1.3, 1.3, 3.9, 5.0, 2.86, 3.0)
    basamaklar(A, -1.0, 1.0, 4.1, 5.0, 0.45, 3)
    r = hip_roof(mb, x0, x1, y0, y1, zS, 30, 0.6)
    baca(mb, -3.2, 11.6, zS, r['zR'] + 0.55)
    baca(mb, 3.2, 11.6, zS, r['zR'] + 0.55)
    A.col('Bina', x0, x1, y0, y1, -0.6, zS)
    A.col('Cati', x0, x1, y0, y1, zS, zS + (r['zR'] - zS) * 0.6)
    A.iz('ana_kutle', x0, x1, y0, y1, r['zR'])
    # bahce
    A.duvar('On', [(-0.6, 0.6), (6.6, 9.6)])
    doseme(A.bahce, -0.6, 0.6, 0.0, 4.1)
    doseme(A.bahce, 6.6, 9.6, 0.0, 11.6)
    A.giris.append([6.6, 9.6])
    A.park_yeri(8.1, 3.3)
    A.park_yeri(8.1, 8.8)
    A.agac += [(-8.4, 2.2), (3.6, 1.9), (-6.5, 18.8), (4.5, 19.0)]
    A.notlar = dict(kat=3, kat_yuksekligi=FH, zemin_kotu=F[0], sacak_kotu=zS,
                    cati='kirma kiremit', bahce_duvari='yalniz on')
    return A


# ---------------------------- Apartman_B ------------------------------------
def apartman_b():
    A = Asset('Apartman_B', 22.0, 22.0)
    mb, rng = A.bina, A.rng
    x0, x1, y0, y1 = -7.0, 7.0, 4.0, 15.0
    F = [0.15, 3.75, 6.75]
    zR, zP = 9.75, 10.65
    bx0, bx1, by0 = -2.8, 2.8, 3.2       # ust katlarda one cikan orta cikma
    zc = 3.45                            # cikmanin alt kotu
    m = Mass(x0, x1, y0, y1)
    ops = {'S': [], 'E': [], 'N': [], 'W': []}
    ops['S'].append(W(-6.5, -1.4, F[0], 3.0, 'vitrin', kepenk=0.42))
    ops['S'].append(W(1.4, 6.5, F[0], 3.0, 'vitrin', kepenk=0.55))
    ops['S'].append(W(-0.6, 0.6, F[0], 2.55, 'kapi'))
    for f in F[1:]:
        for s in (-1, 1):
            ops['S'].append(W(*sorted((s * 6.1, s * 5.2)), f, f + 2.2, 'balkon', 'rulo'))
            ops['S'].append(W(*sorted((s * 4.6, s * 3.4)), f + 0.9, f + 2.3, 'pencere', 'rulo'))
        for side in 'EW':
            ops[side] += apt_windows(f, [(7.0, 8.2), (11.2, 12.4)])
        ops['N'] += apt_windows(f, [(-5.2, -4.0), (-2.6, -1.4), (1.4, 2.6), (4.0, 5.2)])
    # merdiven arkada: sahanlik pencereleri arka cephede
    ops['N'].append(W(-0.45, 0.45, 4.7, 6.0, 'merdiven'))
    ops['N'].append(W(-0.45, 0.45, 7.7, 9.0, 'merdiven'))
    for side in 'EW':
        ops[side].append(W(12.2, 13.4, 1.5, 2.7, 'pencere', 'rulo'))
    ops['N'] += [W(-4.6, -3.4, 1.4, 2.6, 'pencere', 'rulo'), W(3.4, 4.6, 1.4, 2.6, 'pencere', 'rulo'),
                 W(-0.5, 0.5, F[0], 2.4, 'kapi')]
    m.build(mb, -0.6, lambda x, y: zP, ops, rng)
    m.band(mb, -0.6, F[0], out=0.04, mat='KN_Duvar', bottom=False, top_off=0.01)
    m.band(mb, F[1] - 0.25, F[1] - 0.01)
    m.band(mb, F[2] - 0.22, F[2] - 0.01)
    # cikma (kapali balkon): iki ust kat boyunca 0,8 m one tasar
    cm = Mass(bx0, bx1, by0, y0, c=0.06, corners=dict(NW=0.0, NE=0.0))
    cops = {'S': []}
    for f in F[1:]:
        cops['S'] += [W(-2.2, -0.75, f + 0.9, f + 2.3, 'pencere', 'rulo'),
                      W(0.75, 2.2, f + 0.9, f + 2.3, 'pencere', 'rulo')]
    cm.build(mb, zc, lambda x, y: zP, cops, rng, sides='SEW')
    mb.face([(x, y, zc) for x, y in cm.poly], 'KN_Bant', (0, 0, -1))
    cm.band(mb, F[1] - 0.25, F[1] - 0.01)
    cm.band(mb, F[2] - 0.22, F[2] - 0.01)
    mb.box(bx0 - 0.05, bx1 + 0.05, by0 - 0.05, y0 - 0.05, zP, zP + 0.10, "KN_Bant")
    # tabela bantlari (bos)
    for a0, a1 in ((-6.65, -1.25), (1.25, 6.65)):
        mb.box(a0, a1, y0 - 0.12, y0, 3.04, 3.40, 'KN_Tabela', skip=('+y',))
    sacak_kutu(mb, -0.95, 0.95, 3.2, 4.0, 2.72, 2.86)
    for f in F[1:]:
        for a0, a1 in ((-6.6, -3.0), (3.0, 6.6)):
            balkon(mb, m, 'S', a0, a1, f, 1.3)
    # parapet ici, harpusta, cati yuzeyi
    pin = crect(x0 + 0.2, x1 - 0.2, y0 + 0.2, y1 - 0.2, 0.03)
    mb.ring2(m.poly, pin, zR, zP, 'KN_Siva', outer=False, inner=True, top=False, bottom=False)
    mb.ring2(offset_poly(m.poly, 0.05), crect(x0 + 0.17, x1 - 0.17, y0 + 0.17, y1 - 0.17, 0.03),
             zP, zP + 0.10, 'KN_Bant')
    mb.face([(x, y, zR) for x, y in pin], 'KN_Doseme', (0, 0, 1))
    # merdiven kulesi (arka ortada, merdiven boslugunun ustu)
    tyo, ty1 = 11.0, 14.4
    tw = Mass(-1.5, 1.5, tyo, ty1, c=0.05)
    zt = 12.35
    tw.build(mb, zR, lambda x, y: zt, {'S': [W(-0.45, 0.45, zR, zR + 2.1, 'kapi')],
                                       'E': [W(12.3, 13.1, 10.9, 11.7)]}, rng)
    mb.box(-1.68, 1.68, tyo - 0.18, ty1 + 0.18, zt, zt + 0.16, 'KN_Bant')
    # gunes enerjisi + su deposu
    for cx in (-4.6, 0.0, 4.6):
        gunes_unitesi(mb, cx, 6.4, lambda y: zR)
    mb.box(-5.6, -4.0, 12.4, 14.0, zR, zR + 0.22, 'KN_Beton', skip=('-z',))
    mb.cyl((-4.8, 13.2, zR + 0.22 + 0.7), 'z', 0.62, 1.4, 12, 'KN_Depo')
    A.col('Bina', x0, x1, y0, y1, -0.6, zP + 0.1)
    A.col('Merdiven_Kulesi', -1.5, 1.5, tyo, ty1, zR, zt + 0.16)
    A.iz('ana_kutle', x0, x1, y0, y1, zP + 0.1)
    A.iz('cikma', bx0, bx1, by0, y0, zP + 0.1, 'ust_kat_cikmasi')
    A.iz('merdiven_kulesi', -1.5, 1.5, tyo, ty1, zt + 0.16, 'cati_ustu')
    # dukkan onu doseme + arka kapi onu
    doseme(A.bahce, -11.0, 11.0, 0.0, 4.0, 'KN_Doseme')
    doseme(A.bahce, -1.4, 1.4, 15.0, 16.6)
    A.agac += [(-6.0, 18.6), (5.5, 19.0)]
    A.notlar = dict(kat=3, kat_yuksekligi=FH, zemin_kat_yuksekligi=3.6, cati='duz, parapetli',
                    dukkan=2, gunes_kolektoru=3, cikma='orta, 5,6 x 0,8 m',
                    bahce_duvari='yok')
    return A


# ---------------------------- Apartman_C ------------------------------------
def apartman_c():
    A = Asset('Apartman_C', 22.0, 24.0)
    mb, rng = A.bina, A.rng
    x0, x1, y0, y1 = -6.5, 6.5, 5.0, 15.0
    F = [0.9, 3.9, 6.9]
    zE = 7.6
    pitch = 30
    rf = gable_roof(mb, x0, x1, y0, y1, zE, pitch, 'Y', 0.6, 0.5)
    zfn = rf['zfn']
    m = Mass(x0, x1, y0, y1)
    ops = {'S': [], 'E': [], 'N': [], 'W': []}
    ops['S'] += apt_windows(F[0], [(-5.4, -4.2), (-3.0, -1.8), (-0.6, 0.6)])
    ops['S'].append(W(3.5, 4.5, F[0], 3.1, 'kapi'))
    ops['S'] += apt_windows(F[1], [(-5.4, -4.2), (-3.0, -1.8), (-0.6, 0.6), (3.4, 4.6)])
    ops['S'] += [W(-3.4, -2.4, 7.5, 8.7, 'pencere', 'rulo'), W(-0.7, 0.7, 7.5, 9.1, 'pencere', 'rulo'),
                 W(2.4, 3.4, 7.5, 8.7, 'pencere', 'rulo')]
    ops['E'] += apt_windows(F[0], [(6.4, 7.6), (9.4, 10.6), (12.4, 13.6)])
    ops['E'] += [W(8.4, 9.3, F[1], F[1] + 2.2, 'balkon', 'rulo'),
                 W(10.3, 11.5, F[1] + 0.9, F[1] + 2.3, 'pencere', 'rulo'),
                 W(13.0, 14.2, F[1] + 0.9, F[1] + 2.3, 'pencere', 'rulo')]
    for f in F[:2]:
        ops['W'] += apt_windows(f, [(6.8, 8.0), (11.6, 12.8)])
        ops['W'].append(W(9.8, 10.4, f + 1.6, f + 2.2))
        ops['N'] += apt_windows(f, [(-4.6, -3.4), (-1.2, 0.0), (3.4, 4.6)])
    ops['N'].append(W(-0.6, 0.6, 7.5, 8.9, 'pencere', 'rulo'))
    m.build(mb, -0.6, zfn, ops, rng, gable={'side': 'SN', 'm': rf['m']})
    m.band(mb, -0.6, F[0], out=0.04, mat='KN_Duvar', bottom=False, top_off=0.01)
    for f in F[1:]:
        m.band(mb, f - 0.22, f - 0.01)
    balkon(mb, m, 'E', 7.9, 12.0, F[1], 1.3)
    # giris merdiveni: sahanlik + facade boyunca -X'e inen 5 basamak + dolu korkuluk
    mb.box(3.1, 5.0, 3.75, 5.0, -0.6, F[0], 'KN_Duvar', skip=('-z', '+y'),
           mats={'+z': 'KN_Beton'})
    A.col('Sahanlik', 3.1, 5.0, 3.75, 5.0, -0.6, F[0])
    for k in range(1, 6):
        xa, xb = 3.1 - 0.3 * k, 3.1 - 0.3 * (k - 1)
        zt = F[0] - 0.15 * k
        mb.box(xa, xb, 3.9, 5.0, -0.6, zt, 'KN_Duvar', skip=('-z', '+y', '+x', '-y'),
               mats={'+z': 'KN_Beton'})
        A.col(f'Basamak_{k}', xa, xb, 3.9, 5.0, -0.6, zt)
    # korkuluk: sahanlik onu + yan, merdiven boyunca egimli dolu duvar
    mb.prism(crect(3.1, 5.0, 3.75, 3.9), F[0], F[0] + 0.9, 'KN_Siva', top_mat='KN_Bant')
    mb.prism(crect(4.85, 5.0, 3.9, 5.0), F[0], F[0] + 0.9, 'KN_Siva', top_mat='KN_Bant',
             skip_edge=lambda a, b: abs(a[1] - 5.0) < 1e-6 and abs(b[1] - 5.0) < 1e-6)
    prof = [(1.6, -0.6), (3.1, -0.6), (3.1, F[0] + 0.9), (1.6, 0.15 + 0.9)]
    vs = [Vector((x, 3.75, z)) for x, z in prof]
    ve = [Vector((x, 3.9, z)) for x, z in prof]
    mb.face(vs, 'KN_Siva', (0, -1, 0))
    mb.face(ve, 'KN_Siva', (0, 1, 0))
    mb.face([vs[3], vs[2], ve[2], ve[3]], 'KN_Bant', (0, 0, 1))
    mb.face([vs[0], vs[3], ve[3], ve[0]], 'KN_Siva', (-1, 0, 0))
    A.col('Korkuluk', 1.6, 5.0, 3.75, 3.9, -0.6, F[0] + 0.9)
    sacak_kutu(mb, 3.0, 5.1, 3.7, 5.0, 3.32, 3.46)
    baca(mb, -3.0, 12.6, zE, zfn(-3.0, 12.6) + 1.4)
    baca(mb, 3.0, 7.4, zE, zfn(3.0, 7.4) + 1.4)
    A.col('Bina', x0, x1, y0, y1, -0.6, zE)
    A.col('Cati', x0 + 1.5, x1 - 1.5, y0, y1, zE, zE + (rf['zR'] - zE) * 0.6)
    A.iz('ana_kutle', x0, x1, y0, y1, rf['zR'])
    A.iz('yan_balkon', 6.5, 7.8, 7.9, 12.0, F[1] + 1.0, 'balkon')
    # bahce: on + yan duvarlar (arka acik)
    A.duvar('On', [(-10.63, -7.63), (0.4, 1.6)])
    A.duvar('Sol', own_start=False, own_end=True)
    A.duvar('Sag', own_start=False, own_end=True)
    doseme(A.bahce, 0.4, 1.6, 0.0, 5.0)
    doseme(A.bahce, -10.63, -7.63, 0.0, 6.4)
    A.giris.append([-10.63, -7.63])
    A.park_yeri(-9.13, 3.3)
    A.agac += [(-3.4, 2.4), (8.6, 2.6), (-5.0, 19.6), (4.8, 20.2)]
    A.notlar = dict(kat='zemin + 1 + cati kati', kat_yuksekligi=FH, zemin_kotu=F[0],
                    sacak_kotu=zE, cati='besik kiremit (mahya Y, alinlik sokaga)',
                    bahce_duvari='on + yanlar')
    return A


# ---------------------------- Ev_A ------------------------------------------
def kanatli(f, xs, h=1.4, sill=0.9):
    return [W(a0, a1, f + sill, f + sill + h, 'pencere', 'kanat') for a0, a1 in xs]


def ev_a():
    A = Asset('Ev_A', 18.0, 22.0)
    mb, rng = A.bina, A.rng
    x0, x1, y0, y1 = -4.5, 4.5, 6.0, 14.0
    f0 = 0.6
    zS = 3.5
    m = Mass(x0, x1, y0, y1)
    ops = {
        'S': [W(-0.5, 0.5, f0, 2.8, 'kapi')] + kanatli(f0, [(-3.3, -2.1), (2.1, 3.3)]),
        'E': kanatli(f0, [(7.8, 9.0), (11.2, 12.4)]),
        'W': kanatli(f0, [(7.8, 9.0), (11.2, 12.4)]),
        'N': kanatli(f0, [(-3.2, -2.0), (1.9, 3.1)]) + [W(-0.6, 0.0, f0 + 1.6, f0 + 2.2)],
    }
    m.build(mb, -0.6, lambda x, y: zS, ops, rng)
    m.band(mb, -0.6, f0, out=0.04, mat='KN_Duvar', bottom=False, top_off=0.01)
    # veranda
    mb.box(x0, x1, 4.0, 6.0, -0.6, f0, 'KN_Duvar', skip=('-z', '+y'), mats={'+z': 'KN_Doseme'})
    for cx in (-4.3, -1.4, 1.4, 4.3):
        mb.prism(crect(cx - 0.12, cx + 0.12, 4.08, 4.32, 0.03), f0, zS, 'KN_Bant', top=False)
        A.col(f'Kolon_{cx:+.1f}', cx - 0.12, cx + 0.12, 4.08, 4.32, f0, zS)
    basamaklar(A, -1.0, 1.0, 3.1, 4.0, 0.45, 3)
    r = hip_roof(mb, x0, x1, 4.0, y1, zS, 28, 0.55)
    baca(mb, 2.0, 11.2, zS, r['zR'] + 0.4, 0.25, 0.32)
    A.col('Bina', x0, x1, y0, y1, -0.6, zS)
    A.col('Veranda', x0, x1, 4.0, 6.0, -0.6, f0)
    A.col('Cati', x0, x1, y0, y1, zS, zS + (r['zR'] - zS) * 0.6)
    A.iz('ana_kutle', x0, x1, y0, y1, r['zR'])
    A.iz('veranda', x0, x1, 4.0, 6.0, zS, 'veranda')
    A.tum_duvarlar([(-8.63, -5.63), (-0.6, 0.6)])
    doseme(A.bahce, -0.6, 0.6, 0.0, 3.1)
    doseme(A.bahce, -8.63, -5.63, 0.0, 6.2)
    A.giris.append([-8.63, -5.63])
    A.park_yeri(-7.13, 3.3)
    A.agac += [(5.8, 2.4), (6.9, 10.0), (-5.8, 17.8), (2.6, 18.6)]
    A.notlar = dict(kat=1, zemin_kotu=f0, sacak_kotu=zS, cati='kirma kiremit',
                    veranda='4 kolon, 3 basamak')
    return A


# ---------------------------- Ev_B ------------------------------------------
def ev_b():
    A = Asset('Ev_B', 18.0, 22.0)
    mb, rng = A.bina, A.rng
    x0, x1, y0, y1 = -4.0, 4.0, 5.0, 13.0
    f0, f1 = 0.3, 3.3
    zS = 6.1
    m = Mass(x0, x1, y0, y1)
    ops = {
        'S': [W(1.3, 2.3, f0, 2.5, 'kapi')] + kanatli(f0, [(-3.0, -1.8)]) +
             [W(-3.1, -2.2, f1, f1 + 2.2, 'balkon'), W(-1.6, -0.6, f1 + 0.9, f1 + 2.3, 'pencere')] +
             kanatli(f1, [(1.2, 2.4)]),
        'E': kanatli(f0, [(6.6, 7.8), (10.2, 11.4)]) + kanatli(f1, [(6.6, 7.8), (10.2, 11.4)]),
        'W': kanatli(f0, [(6.6, 7.8), (10.2, 11.4)]) + kanatli(f1, [(6.6, 7.8), (10.2, 11.4)]),
        'N': kanatli(f0, [(-3.0, -1.8), (1.2, 2.4)]) + [W(-0.5, 0.1, f0 + 1.6, f0 + 2.2)] +
             kanatli(f1, [(-3.1, -2.1), (-0.5, 0.5), (2.1, 3.1)]),
    }
    m.build(mb, -0.6, lambda x, y: zS, ops, rng)
    m.band(mb, -0.6, f0, out=0.04, mat='KN_Duvar', bottom=False, top_off=0.01)
    m.band(mb, f1 - 0.22, f1 - 0.01)
    balkon(mb, m, 'S', -3.6, -0.2, f1, 1.2)
    sacak_kutu(mb, 0.9, 2.7, 4.0, 5.0, 2.66, 2.8)
    basamaklar(A, 1.1, 2.5, 4.4, 5.0, 0.30, 2)
    r = hip_roof(mb, x0, x1, y0, y1, zS, 30, 0.6)
    zr = lambda y: r['zF'] + (y - r['Y0']) * r['s']
    gunes_unitesi(mb, 0.0, 6.3, zr)
    baca(mb, -2.2, 10.6, zS, r['zR'] + 0.3, 0.25, 0.32)
    A.col('Bina', x0, x1, y0, y1, -0.6, zS)
    A.col('Cati', x0 + 1, x1 - 1, y0 + 1, y1 - 1, zS, zS + (r['zR'] - zS) * 0.6)
    A.iz('ana_kutle', x0, x1, y0, y1, r['zR'])
    A.tum_duvarlar([(1.2, 2.4), (5.63, 8.63)])
    doseme(A.bahce, 1.2, 2.4, 0.0, 4.4)
    doseme(A.bahce, 5.63, 8.63, 0.0, 6.2)
    A.giris.append([5.63, 8.63])
    A.park_yeri(7.13, 3.3)
    A.agac += [(-6.8, 2.6), (6.9, 11.0), (-6.4, 16.6), (3.0, 18.0)]
    A.notlar = dict(kat=2, kat_yuksekligi=FH, zemin_kotu=f0, sacak_kotu=zS,
                    cati='kirma kiremit (piramit) + gunes enerjisi')
    return A


# ---------------------------- Ev_C ------------------------------------------
def ev_c():
    A = Asset('Ev_C', 18.0, 22.0)
    mb, rng = A.bina, A.rng
    f0 = 0.3
    zE = 3.2
    pitch = 33
    # ana kanat (mahya X) ve sokaga bakan kanat (mahya Y)
    mx0, mx1, my0, my1 = -7.5, 3.5, 9.0, 14.0
    wx0, wx1, wy0, wy1 = -7.5, -2.5, 5.0, 9.0
    s = math.tan(math.radians(pitch))
    tv = 0.18 / math.cos(math.radians(pitch))
    ov = 0.5
    T = 'KN_Kiremit'
    # ana kanat cati
    ym = (my0 + my1) / 2
    fa = lambda x, y: zE + (y - my0) * s
    fb = lambda x, y: zE + (my1 - y) * s
    rs = lambda a, b: abs(a[1] - ym) < 1e-6 and abs(b[1] - ym) < 1e-6
    mb.slab(crect(mx0 - ov, mx1 + ov, my0 - ov, ym), fa, tv, T, 'KN_Bant', 'KN_Bant', rs)
    mb.slab(crect(mx0 - ov, mx1 + ov, ym, my1 + ov), fb, tv, T, 'KN_Bant', 'KN_Bant', rs)
    zR = zE + (ym - my0) * s + tv
    mb.cap((mx0 - ov - 0.05, ym, zR), (mx1 + ov + 0.05, ym, zR), 0.26, 0.11, T)
    # kanat cati (dere hatti ile kesilmis)
    xm = (wx0 + wx1) / 2
    fw1 = lambda x, y: zE + (x - wx0) * s
    fw2 = lambda x, y: zE + (wx1 - x) * s
    yv = my0 + (wx1 - wx0) / 2   # mahyalarin bulustugu nokta (11.5)
    pw1 = [(wx0 - ov, wy0 - ov), (xm, wy0 - ov), (xm, yv), (wx0, my0), (wx0 - ov, my0 - ov)]
    pw2 = [(xm, wy0 - ov), (wx1 + ov, wy0 - ov), (wx1 + ov, my0 - ov), (wx1, my0), (xm, yv)]
    on_r = lambda a, b: abs(a[0] - xm) < 1e-6 and abs(b[0] - xm) < 1e-6
    mb.slab(pw1, fw1, tv, T, 'KN_Bant', 'KN_Bant', on_r)
    mb.slab(pw2, fw2, tv, T, 'KN_Bant', 'KN_Bant', on_r)
    zRw = zE + (xm - wx0) * s + tv
    mb.cap((xm, wy0 - ov - 0.05, zRw), (xm, yv, zRw), 0.26, 0.11, T)
    zf_main = lambda x, y: zE + ((my1 - my0) / 2 - abs(y - ym)) * s
    zf_wing = lambda x, y: zE + ((wx1 - wx0) / 2 - abs(x - xm)) * s
    mm = Mass(mx0, mx1, my0, my1, corners=dict(SW=0.0))
    wm = Mass(wx0, wx1, wy0, wy1, corners=dict(NW=0.0, NE=0.0))
    ops_m = {
        'S': [W(-1.9, -0.9, f0, 2.5, 'kapi')] + kanatli(f0, [(0.5, 1.7)]),
        'E': kanatli(f0, [(10.9, 12.1)]),
        'W': kanatli(f0, [(11.0, 12.2)]),
        'N': kanatli(f0, [(-6.4, -5.2), (-3.4, -2.2), (1.4, 2.5)]) + [W(-0.2, 0.4, 1.9, 2.5)],
    }
    ops_w = {
        'S': kanatli(f0, [(-5.7, -4.3)]),
        'W': kanatli(f0, [(6.4, 7.6)]),
        'E': kanatli(f0, [(6.1, 7.1)], h=1.2, sill=1.1),
    }
    # ana kanadin S cephesi kanat icinde kalan kisimda insa edilmez: ayri cephe
    mm.build(mb, -0.6, zf_main, {k: v for k, v in ops_m.items() if k != 'S'}, rng, sides='ENW',
             gable={'side': 'EW', 'm': ym})
    fS = Fac((wx1, my0), (1, 0, 0), (mx1 - mm.c['SE']) - wx1)
    lst = []
    for o in ops_m['S']:
        d = dict(o)
        d['u0'], d['u1'] = o['a0'] - wx1, o['a1'] - wx1
        lst.append(d)
    wall_side(mb, fS, -0.6, [(0.0, zE), (fS.W, zE)], lst, rng)
    wm.build(mb, -0.6, zf_wing, ops_w, rng, sides='SEW', gable={'side': 'S', 'm': xm})
    mm.band(mb, -0.6, f0, out=0.04, mat='KN_Duvar', bottom=False, top_off=0.01)
    wm.band(mb, -0.6, f0, out=0.04, mat='KN_Duvar', bottom=False, top_off=0.01)
    # giris: kosede sacak + 2 basamak
    sacak_kutu(mb, -2.45, -0.35, 8.1, 9.0, 2.62, 2.76)
    basamaklar(A, -2.2, -0.6, 8.4, 9.0, 0.30, 2)
    baca(mb, 1.2, 12.6, zE, zR + 0.5, 0.25, 0.32)
    # ahsap cardak / araba sundurmasi
    cx0, cx1, cy0, cy1 = 4.1, 7.7, 5.4, 11.0
    posts = [(cx0 + 0.15, cy0 + 0.15), (cx1 - 0.15, cy0 + 0.15), (cx0 + 0.15, cy1 - 0.15),
             (cx1 - 0.15, cy1 - 0.15), (cx1 - 0.15, (cy0 + cy1) / 2)]
    for i, (px, py) in enumerate(posts):
        mb.box(px - 0.075, px + 0.075, py - 0.075, py + 0.075, 0.04, 2.36, 'KN_Ahsap',
               skip=('-z', '+z'))
        A.col(f'Cardak_Direk_{i + 1}', px - 0.075, px + 0.075, py - 0.075, py + 0.075, 0.0, 2.36)
    for px in (cx0 + 0.15, cx1 - 0.15):
        mb.box(px - 0.08, px + 0.08, cy0, cy1, 2.36, 2.56, 'KN_Ahsap')
    for k in range(7):
        py = cy0 + 0.1 + (cy1 - cy0 - 0.2) * k / 6
        mb.box(cx0 - 0.15, cx1 + 0.15, py - 0.04, py + 0.04, 2.56, 2.66, 'KN_Ahsap')
    mb.box(cx0 - 0.2, cx1 + 0.2, cy0 - 0.15, cy1 + 0.15, 2.66, 2.70, 'KN_Kepenk')
    A.col('Bina', mx0, mx1, my0, my1, -0.6, zE)
    A.col('Kanat', wx0, wx1, wy0, my0, -0.6, zE)
    A.col('Cati', mx0 + 0.5, mx1 - 0.5, my0 + 1.0, my1 - 1.0, zE, zE + (zR - zE) * 0.6)
    A.iz('ana_kanat', mx0, mx1, my0, my1, zR)
    A.iz('on_kanat', wx0, wx1, wy0, wy1, zRw)
    A.iz('cardak', cx0, cx1, cy0, cy1, 2.7, 'cardak')
    A.tum_duvarlar([(-2.0, -0.8), (4.4, 7.4)])
    doseme(A.bahce, -2.0, -0.8, 0.0, 8.4)
    doseme(A.bahce, 4.4, 7.4, 0.0, 11.0)
    A.giris.append([4.4, 7.4])
    A.park_yeri(5.9, 8.0)
    A.agac += [(-5.4, 2.3), (1.6, 5.2), (-4.2, 18.0), (4.2, 17.6)]
    A.notlar = dict(kat=1, zemin_kotu=f0, sacak_kotu=zE, plan='L 11 x 9',
                    cati='besik kiremit, iki mahya', cardak='ahsap araba sundurmasi')
    return A


# ---------------------------- Ev_D ------------------------------------------
def ev_d():
    A = Asset('Ev_D', 18.0, 22.0)
    mb, rng = A.bina, A.rng
    x0, x1, y0, y1 = -4.0, 5.0, 6.0, 15.0
    f0, f1 = 0.25, 3.1
    zE = 5.9
    rf = gable_roof(mb, x0, x1, y0, y1, zE, 30, 'X', 0.8, 0.6, sacak='KN_Ahsap')
    zfn = rf['zfn']
    m = Mass(x0, x1, y0, y1)
    ops = {
        'S': [W(-3.75, -2.15, f0, 2.3, 'depo'), W(-1.5, -0.6, f1, f1 + 2.1, 'kapi')] +
             kanatli(f1, [(-3.3, -2.4)], h=1.3) + kanatli(f1, [(0.9, 1.9), (3.2, 4.2)], h=1.3),
        'E': [W(8.0, 8.8, 1.4, 2.2)] + kanatli(f1, [(9.9, 11.1)]),
        'W': [W(10.2, 11.0, 1.4, 2.2)] + kanatli(f1, [(7.6, 8.6), (12.4, 13.4)]) +
             [W(10.0, 11.0, 6.4, 7.2)],
        'N': [W(0.0, 1.0, f0, 2.3, 'kapi'), W(-2.6, -1.8, 1.4, 2.2), W(3.0, 3.8, 1.4, 2.2)] +
             kanatli(f1, [(-2.6, -1.6), (1.0, 2.0), (3.2, 4.1)]),
    }
    ops['E'].append(W(10.0, 11.0, 6.4, 7.2))
    m.build(mb, -0.6, zfn, ops, rng, gable={'side': 'EW', 'm': rf['m']})
    m.band(mb, -0.6, f0, out=0.05, mat='KN_Duvar', bottom=False, top_off=0.01)
    m.band(mb, f1 - 0.2, f1, mat='KN_Ahsap')
    # dis merdiven: sahanlik + 17 basamak (+X yonune iner), tas kagir govde, demir korkuluk
    yq0, yq1 = 4.75, 6.0
    lx0, lx1 = -1.9, -0.1
    rise = (f1 - 0.0) / 18
    run = 0.27
    mb.box(lx0, lx1, yq0, yq1, -0.6, f1, 'KN_Duvar', skip=('-z', '+y'), mats={'+z': 'KN_Beton'})
    A.col('Sahanlik', lx0, lx1, yq0, yq1, -0.6, f1)
    for k in range(1, 18):
        xa, xb = lx1 + run * (k - 1), lx1 + run * k
        zt = f1 - rise * k
        mb.box(xa, xb, yq0, yq1, -0.6, zt, 'KN_Duvar', skip=('-z', '+y', '-x'),
               mats={'+z': 'KN_Beton'})
        A.col(f'Basamak_{k}', xa, xb, yq0, yq1, -0.6, zt)
    xe = lx1 + run * 17
    zl = lambda x: f1 - (x - lx1) / run * rise
    yr = yq0 + 0.05
    H = 0.95
    for k in (2, 5, 8, 11, 14, 17):
        xk = lx1 + run * (k - 0.5)
        mb.beam((xk, yr, f1 - rise * k), (xk, yr, zl(xk) + H), 0.04, 'KN_Demir')
    for px, py in ((lx1 - 0.05, yr), (lx0 + 0.05, yr), (lx0 + 0.05, yq1 - 0.08)):
        mb.beam((px, py, f1), (px, py, f1 + H), 0.04, 'KN_Demir')
    for dz in (H, H * 0.5):
        x_a = lx1 + run * 16.5
        mb.beam((x_a, yr, zl(x_a) + dz), (lx1, yr, f1 + dz), 0.05, 'KN_Demir')
        mb.beam((lx1 + 0.025, yr, f1 + dz), (lx0 + 0.025, yr, f1 + dz), 0.05, 'KN_Demir')
        mb.beam((lx0 + 0.05, yr - 0.025, f1 + dz), (lx0 + 0.05, yq1, f1 + dz), 0.05, 'KN_Demir')
    A.col('Korkuluk', lx0, xe, yq0, yq0 + 0.1, -0.6, f1 + H)
    baca(mb, -2.4, 12.2, zE, rf['zR'] + 0.2, 0.25, 0.32)
    A.col('Bina', x0, x1, y0, y1, -0.6, zE)
    A.col('Cati', x0, x1, y0 + 1.0, y1 - 1.0, zE, zE + (rf['zR'] - zE) * 0.6)
    A.iz('ana_kutle', x0, x1, y0, y1, rf['zR'])
    A.iz('dis_merdiven', lx0, xe, yq0, yq1, f1 + 0.9, 'merdiven')
    A.tum_duvarlar([(-8.63, -5.63), (5.2, 6.4)])
    doseme(A.bahce, 5.2, 6.4, 0.0, 6.0)
    doseme(A.bahce, xe, 5.2, yq0, yq1)
    doseme(A.bahce, -8.63, -5.63, 0.0, 6.0)
    doseme(A.bahce, -5.63, -1.9, 3.6, 6.0, 'KN_Doseme')
    A.giris.append([-8.63, -5.63])
    A.park_yeri(-7.13, 3.3)
    A.agac += [(1.8, 2.4), (-5.8, 18.2), (2.0, 19.0), (7.0, 13.0)]
    A.notlar = dict(kat=2, kat_yuksekligi=FH, zemin_kotu=f0, sacak_kotu=zE,
                    cati='besik kiremit, genis ahsap sacak', merdiven='on cephede dis merdiven',
                    zemin='depo (cift kanat ahsap kapi)')
    return A


# ---------------------------------------------------------------------------
# Sahneye kurulum, sayim, disa aktarma
# ---------------------------------------------------------------------------
def build_object(mb, coll):
    me = bpy.data.meshes.new(mb.name)
    me.from_pydata(mb.verts, [], mb.faces)
    for mn in mb.mats:
        me.materials.append(MAT[mn])
    for p, mi in zip(me.polygons, mb.fmat):
        p.material_index = mi
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    me.update()
    ob = bpy.data.objects.new(mb.name, me)
    coll.objects.link(ob)
    return ob


def build_col(name, b, coll):
    x0, x1, y0, y1, z0, z1 = b
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
         (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    me = bpy.data.meshes.new(name)
    me.from_pydata(v, [], f)
    me.update()
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    ob.display_type = 'WIRE'
    ob.hide_render = True
    return ob


def tri_of(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def bbox(obs):
    xs, ys, zs = [], [], []
    for o in obs:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            xs.append(w.x)
            ys.append(w.y)
            zs.append(w.z)
    return (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))


def export_fbx(path, obs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'MESH', 'EMPTY'},
                             axis_forward='Z', axis_up='Y', apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_ALL', use_space_transform=True,
                             bake_space_transform=True, use_mesh_modifiers=True,
                             mesh_smooth_type='FACE', use_triangles=True, add_leaf_bones=False,
                             bake_anim=False, path_mode='AUTO')


BUTCE = {'Apartman': 8000, 'Ev': 6000}


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    malzemeleri_kur()
    OYUN.mkdir(parents=True, exist_ok=True)
    BLEND.parent.mkdir(parents=True, exist_ok=True)
    assets = [apartman_a(), apartman_b(), apartman_c(), ev_a(), ev_b(), ev_c(), ev_d()]
    manifest = dict(
        set='Konutlar', surum=1, birim='metre',
        eksen=('Unity yerel: x = Blender x, y = yukari (Blender z), z = Blender y. '
               'Orijin parselin sokak tarafi on kenarinin ortasi; sokak -Z, parsel +Z.'),
        notlar=('bina_izleri min/max = Unity [x, z]. park_yerleri konum = Unity [x, y, z], '
                'yon_derece 0 = arac onu +Z. agac_noktalari Unity [x, 0, z]. '
                'giris_kesimleri = on kenarda (z=0) bordurun alcaltilacagi Unity x araliklari. '
                'Duvar nesneleri ayri: komsu parsellerle ortak sinirda <id>_Duvar_Sol/Sag ve '
                'ilgili COL_<id>_Duvar_Sol/Sag_* birlikte kapatilabilir.'),
        varliklar=[])
    rapor = []
    for A in assets:
        coll = bpy.data.collections.new(A.id)
        bpy.context.scene.collection.children.link(coll)
        vis = [build_object(A.bina, coll)]
        if A.bahce.faces:
            vis.append(build_object(A.bahce, coll))
        for side in ('On', 'Sol', 'Sag', 'Arka'):
            if side in A.duvarlar:
                vis.append(build_object(A.duvarlar[side][0], coll))
        cols = [build_col(n, b, coll) for n, b in A.cols]
        tri_vis = sum(tri_of(o) for o in vis)
        tri_col = sum(tri_of(o) for o in cols)
        bb = bbox(vis)
        butce = BUTCE['Apartman' if A.id.startswith('Apartman') else 'Ev']
        per = {o.name: tri_of(o) for o in vis}
        rapor.append((A.id, tri_vis, tri_col, butce, per))
        mats_used = sorted({ms.material.name for o in vis for ms in o.material_slots})
        entry = dict(
            id=A.id, fbx=f'{A.id}.fbx',
            boyut=[r3(bb[1] - bb[0]), r3(bb[5] - bb[4]), r3(bb[3] - bb[2])],
            yukseklik_zemin_ustu=r3(bb[5]),
            sinir_kutusu=dict(min=[r3(bb[0]), r3(bb[4]), r3(bb[2])],
                              max=[r3(bb[1]), r3(bb[5]), r3(bb[3])]),
            ucgen=tri_vis, ucgen_carpisma=tri_col, ucgen_butce=butce,
            parsel=[A.G, A.D],
            bina_izleri=A.izler,
            giris_kesimleri=[[r3(a), r3(b)] for a, b in A.giris],
            park_yerleri=A.park,
            agac_noktalari=[[r3(x), 0.0, r3(y)] for x, y in A.agac],
            nesneler=[o.name for o in vis],
            carpisma=[o.name for o in cols],
            malzemeler=mats_used,
            onizleme_renkleri=dict(konut_siva=ONIZLEME_RENK[A.id][0],
                                   konut_panjur=ONIZLEME_RENK[A.id][1]),
            bahce_duvari={k: dict(kapilar=[[r3(a), r3(b)] for a, b in v[1]['kapilar']],
                                  parmaklik_cubugu=v[1]['parmak']) for k, v in A.duvarlar.items()},
            ozellikler=A.notlar,
        )
        manifest['varliklar'].append(entry)
        A._vis, A._cols = vis, cols
    for aid, tv, tc, b, per in rapor:
        print(f'UCGEN {aid}: gorunur {tv} / {b}  (COL {tc})  ' +
              ', '.join(f'{k}={v}' for k, v in per.items()))
    mats_json = dict(materials=[], palettes={})
    for ad, srgb, metal, smooth, rnd in MALZEMELER:
        d = dict(name=ad, color=list(srgb), metallic=metal, smoothness=smooth,
                 emission=[0, 0, 0], alpha=1.0)
        if rnd:
            d['randomize'] = rnd
        mats_json['materials'].append(d)
    for k, lst in PALETLER.items():
        mats_json['palettes'][k] = [dict(ad=a, color=list(c)) for a, c in lst]
    (OYUN / 'Konutlar_manifest.json').write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    (OYUN / 'Konutlar_malzemeler.json').write_text(
        json.dumps(mats_json, ensure_ascii=False, indent=2), encoding='utf-8')
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    if not NO_EXPORT:
        for A in assets:
            export_fbx(OYUN / f'{A.id}.fbx', A._vis + A._cols)
            print('FBX', A.id)
    print('BITTI')


main()
