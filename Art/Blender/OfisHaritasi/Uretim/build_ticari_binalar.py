# -*- coding: utf-8 -*-
"""Ticari_Binalar seti (Rescue Team ofis haritasi, sanayi seridi).

Tek betik bes varligi sifirdan uretir: Atolye_A, Atolye_B, Atolye_C, Benzinlik, Market.
Calistirma:
  blender.exe -b -P build_ticari_binalar.py -- [--no-export]

Eksen: Blender X = Unity X, Blender Y = Unity Z, Blender Z = Unity yukari.
Orijin: parselin sokak tarafindaki on kenarinin ortasi, Z=0. Sokak -Y, parsel +Y'ye uzanir.
Butun geometri elle hesaplanir (boolean / modifier yok); duz golgeleme, tek segment pah.
"""
import bpy
import math
import json
import sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(r"C:\Users\uur\RescueTeam_OfisHaritasi")
SET = "Ticari_Binalar"
OUT = ROOT / "Oyun" / "Assets" / "OfisHaritasi" / "Modeller" / SET
BLEND = ROOT / "Kaynak" / "Blender" / (SET + ".blend")

ARGV = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
NO_EXPORT = '--no-export' in ARGV

DU = 0.04        # doseme ust kotu
TEMEL = -0.6     # bina / duvar tabani

# ---------------------------------------------------------------------------
# Malzemeler (renkler sRGB 0-1; Unity JSON ile ayni). Blender'a lineer cevrilir.
# ---------------------------------------------------------------------------
MATS = [
    # ad, renk, metallic, smoothness, emission, alpha, randomize, aciklama
    ("TB_Kaplama",      (0.70, 0.71, 0.70), 0.35, 0.40, None, 1.0, "atolye_kaplama", "Atolye trapez celik kaplama (her kopyada palete gore)"),
    ("TB_Siva",         (0.80, 0.74, 0.61), 0.00, 0.12, None, 1.0, None, "Dukkan / market / atolye krem siva"),
    ("TB_Beton_Boya",   (0.67, 0.67, 0.65), 0.00, 0.15, None, 1.0, None, "Boyali beton: kolon, kiris, harpusta, subasman"),
    ("TB_Trim",         (0.19, 0.20, 0.22), 0.30, 0.45, None, 1.0, None, "Antrasit trim: kose profili, oluk, cerceve"),
    ("TB_Trim_Mavi",    (0.29, 0.36, 0.43), 0.25, 0.40, None, 1.0, None, "Mavi-gri celik kapi"),
    ("TB_Kepenk",       (0.61, 0.62, 0.61), 0.55, 0.45, None, 1.0, None, "Galvaniz sarmal kepenk ve kutusu"),
    ("TB_Cam",          (0.10, 0.12, 0.13), 0.10, 0.85, None, 1.0, None, "Opak koyu cam (ici gorunmez)"),
    ("TB_Cam_Vitrin",   (0.13, 0.16, 0.17), 0.10, 0.90, None, 0.50, None, "Yari saydam koyu vitrin cami (market, benzinlik)"),
    ("TB_Ic_Koyu",      (0.15, 0.15, 0.14), 0.00, 0.15, None, 1.0, None, "Vitrin arkasi koyu ic yuzey"),
    ("TB_Ic_Raf",       (0.45, 0.42, 0.36), 0.00, 0.20, None, 1.0, None, "Ic yuzeyde raf bantlari"),
    ("TB_Ic_Acik",      (0.78, 0.78, 0.74), 0.00, 0.20, (0.30, 0.30, 0.28), 1.0, None, "Ic tavan (aydinlik, hafif isik)"),
    ("TB_Beton",        (0.57, 0.56, 0.53), 0.00, 0.12, None, 1.0, None, "Avlu betonu"),
    ("TB_Asfalt",       (0.23, 0.23, 0.23), 0.00, 0.12, None, 1.0, None, "Asfalt"),
    ("TB_Beton_Duvar",  (0.61, 0.60, 0.57), 0.00, 0.12, None, 1.0, None, "Bahce duvari / dusuk beton duvar"),
    ("TB_Bordur",       (0.69, 0.68, 0.65), 0.00, 0.18, None, 1.0, None, "Bordur, ada kenari"),
    ("TB_Kaldirim",     (0.63, 0.60, 0.56), 0.00, 0.15, None, 1.0, None, "Market onu kaldirim"),
    ("TB_Cim",          (0.29, 0.37, 0.20), 0.00, 0.05, None, 1.0, None, "Cimlik ada / tarh"),
    ("TB_Cit",          (0.25, 0.28, 0.28), 0.45, 0.40, None, 1.0, None, "Boyali celik cit"),
    ("TB_Cati_Sac",     (0.53, 0.55, 0.56), 0.45, 0.35, None, 1.0, None, "Trapez cati saci"),
    ("TB_Cati_Isik",    (0.63, 0.67, 0.68), 0.00, 0.60, None, 1.0, None, "Cati isiklik seridi"),
    ("TB_Cati_Membran", (0.38, 0.38, 0.37), 0.00, 0.10, None, 1.0, None, "Duz cati membrani"),
    ("TB_Boya_Beyaz",   (0.86, 0.85, 0.80), 0.00, 0.30, None, 1.0, None, "Yol cizgi boyasi"),
    ("TB_Beyaz",        (0.86, 0.85, 0.81), 0.10, 0.45, None, 1.0, None, "Kirik beyaz panel (kanopi, pompa, klima)"),
    ("TB_Petrol",       (0.08, 0.28, 0.29), 0.10, 0.45, None, 1.0, None, "Koyu petrol yesili tek serit"),
    ("TB_Tabela",       (0.84, 0.83, 0.78), 0.05, 0.40, None, 1.0, "ticari_tabela", "Bos tabela paneli (yazisiz)"),
    ("TB_Tabela_2",     (0.18, 0.25, 0.37), 0.05, 0.40, None, 1.0, "ticari_tabela", "Ikinci dukkan icin bos tabela paneli"),
    ("TB_Kaucuk",       (0.06, 0.06, 0.06), 0.00, 0.10, None, 1.0, None, "Hortum, tekerlek, lastik"),
    ("TB_Palet",        (0.50, 0.40, 0.27), 0.00, 0.08, None, 1.0, None, "Ahsap palet"),
    ("TB_Varil_Mavi",   (0.17, 0.26, 0.37), 0.30, 0.40, None, 1.0, None, "Yag varili mavi"),
    ("TB_Varil_Kirmizi",(0.43, 0.16, 0.12), 0.30, 0.40, None, 1.0, None, "Yag varili kiremit"),
    ("TB_Konteyner",    (0.25, 0.31, 0.28), 0.20, 0.35, None, 1.0, None, "Cop konteyneri"),
    ("TB_Galvaniz",     (0.58, 0.59, 0.59), 0.60, 0.40, None, 1.0, None, "Galvaniz boru, braket, sepet"),
    ("TB_Plastik",      (0.45, 0.18, 0.15), 0.00, 0.35, None, 1.0, None, "Donuk kirmizi plastik (araba sapi)"),
    ("TB_Lamba",        (0.95, 0.94, 0.90), 0.00, 0.30, (0.80, 0.78, 0.72), 1.0, None, "Kanopi tavan lambasi"),
]
PALETTES = {
    "atolye_kaplama": [[0.70, 0.71, 0.70], [0.80, 0.79, 0.75], [0.53, 0.59, 0.63],
                       [0.67, 0.64, 0.57], [0.59, 0.64, 0.59]],
    "ticari_tabela": [[0.84, 0.83, 0.78], [0.18, 0.25, 0.37], [0.41, 0.16, 0.14],
                      [0.66, 0.53, 0.21], [0.10, 0.30, 0.31]],
}
MAT = {}


def srgb2lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def build_materials():
    for name, col, met, smo, emi, alpha, rnd, _ in MATS:
        m = bpy.data.materials.new(name)
        lin = [srgb2lin(c) for c in col]
        m.diffuse_color = (*lin, alpha)
        m.metallic = met
        m.roughness = 1.0 - smo
        nt = m.node_tree
        if nt is None:
            m.use_nodes = True
            nt = m.node_tree
        bs = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bs is None:
            nt.nodes.clear()
            bs = nt.nodes.new('ShaderNodeBsdfPrincipled')
            out = nt.nodes.new('ShaderNodeOutputMaterial')
            nt.links.new(bs.outputs[0], out.inputs[0])
        bs.inputs['Base Color'].default_value = (*lin, 1.0)
        bs.inputs['Metallic'].default_value = met
        bs.inputs['Roughness'].default_value = 1.0 - smo
        if emi:
            bs.inputs['Emission Color'].default_value = (*[srgb2lin(c) for c in emi], 1.0)
            bs.inputs['Emission Strength'].default_value = 1.0
        if alpha < 1.0:
            bs.inputs['Alpha'].default_value = alpha
            try:
                m.surface_render_method = 'BLENDED'
            except Exception:
                pass
        MAT[name] = m


# ---------------------------------------------------------------------------
# Mesh kurucu ve ilkel sekiller
# ---------------------------------------------------------------------------
class MB:
    """Tek nesnelik mesh verisi; yuzler disari bakan yon ipucuyla eklenir."""

    def __init__(self, name):
        self.name = name
        self.v = []
        self.f = []
        self.fm = []
        self.mats = []

    def vert(self, p):
        self.v.append((float(p[0]), float(p[1]), float(p[2])))
        return len(self.v) - 1

    def face(self, idx, mat, out):
        pts = [Vector(self.v[i]) for i in idx]
        n = Vector((0.0, 0.0, 0.0))
        for i in range(len(pts)):
            a = pts[i]
            b = pts[(i + 1) % len(pts)]
            n.x += (a.y - b.y) * (a.z + b.z)
            n.y += (a.z - b.z) * (a.x + b.x)
            n.z += (a.x - b.x) * (a.y + b.y)
        if n.length < 1e-10:
            return
        if n.dot(Vector(out)) < 0:
            idx = list(idx)[::-1]
        if mat not in self.mats:
            self.mats.append(mat)
        self.f.append(tuple(idx))
        self.fm.append(self.mats.index(mat))

    def poly(self, pts, mat, out):
        self.face([self.vert(p) for p in pts], mat, out)

    def tris(self):
        return sum(len(f) - 2 for f in self.f)


SIDE_DIR = {'-x': (-1, 0, 0), '+x': (1, 0, 0), '-y': (0, -1, 0), '+y': (0, 1, 0), '-z': (0, 0, -1), '+z': (0, 0, 1)}


def box(mb, x0, x1, y0, y1, z0, z1, mat, skip=(), sm=None):
    sm = sm or {}
    X, Y, Z = (x0, x1), (y0, y1), (z0, z1)
    cache = {}

    def V(i, j, k):
        if (i, j, k) not in cache:
            cache[i, j, k] = mb.vert((X[i], Y[j], Z[k]))
        return cache[i, j, k]
    S = {'-x': [(0, 0, 0), (0, 1, 0), (0, 1, 1), (0, 0, 1)], '+x': [(1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)],
         '-y': [(0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)], '+y': [(0, 1, 0), (1, 1, 0), (1, 1, 1), (0, 1, 1)],
         '-z': [(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)], '+z': [(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)]}
    for s, q in S.items():
        if s in skip:
            continue
        mb.face([V(*t) for t in q], sm.get(s, mat), SIDE_DIR[s])


def cbox(mb, x0, x1, y0, y1, z0, z1, mat, w=0.03, wb=0.0, bottom=False, sm=None):
    """Pahli kutu: ust ve dikey kenarlar tek segment pahli. wb>0 ise alt kenarlar da pahli."""
    sm = sm or {}
    hx, hy, hz = (x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2
    w = min(w, hx * 0.45, hy * 0.45, hz * 0.45)
    wb = min(wb, w)
    if w < 1e-4:
        box(mb, x0, x1, y0, y1, z0, z1, mat, skip=() if bottom else ('-z',), sm=sm)
        return
    cx, cy, cz = (x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2
    C = Vector((cx, cy, cz))
    V = {}
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                wz = w if sz > 0 else wb
                V['x', sx, sy, sz] = mb.vert((cx + sx * hx, cy + sy * (hy - w), cz + sz * (hz - wz)))
                V['y', sx, sy, sz] = mb.vert((cx + sx * (hx - w), cy + sy * hy, cz + sz * (hz - wz)))
                V['z', sx, sy, sz] = mb.vert((cx + sx * (hx - w), cy + sy * (hy - w), cz + sz * hz))

    def F(keys, m):
        ids = [V[k] for k in keys]
        cen = sum((Vector(mb.v[i]) for i in ids), Vector()) / len(ids)
        mb.face(ids, m, cen - C)
    for sx in (-1, 1):
        F([('x', sx, -1, -1), ('x', sx, 1, -1), ('x', sx, 1, 1), ('x', sx, -1, 1)], sm.get('+x' if sx > 0 else '-x', mat))
    for sy in (-1, 1):
        F([('y', -1, sy, -1), ('y', 1, sy, -1), ('y', 1, sy, 1), ('y', -1, sy, 1)], sm.get('+y' if sy > 0 else '-y', mat))
    F([('z', -1, -1, 1), ('z', 1, -1, 1), ('z', 1, 1, 1), ('z', -1, 1, 1)], sm.get('+z', mat))
    for sx in (-1, 1):
        for sy in (-1, 1):
            F([('x', sx, sy, -1), ('x', sx, sy, 1), ('y', sx, sy, 1), ('y', sx, sy, -1)], mat)
    zs = (1, -1) if wb > 0 else (1,)
    for sz in zs:
        for sy in (-1, 1):
            F([('y', -1, sy, sz), ('y', 1, sy, sz), ('z', 1, sy, sz), ('z', -1, sy, sz)], mat)
        for sx in (-1, 1):
            F([('x', sx, -1, sz), ('x', sx, 1, sz), ('z', sx, 1, sz), ('z', sx, -1, sz)], mat)
        for sx in (-1, 1):
            for sy in (-1, 1):
                F([('x', sx, sy, sz), ('y', sx, sy, sz), ('z', sx, sy, sz)], mat)
    if bottom:
        if wb > 0:
            F([('z', -1, -1, -1), ('z', 1, -1, -1), ('z', 1, 1, -1), ('z', -1, 1, -1)], sm.get('-z', mat))
        else:
            F([('x', 1, -1, -1), ('x', 1, 1, -1), ('y', 1, 1, -1), ('y', -1, 1, -1),
               ('x', -1, 1, -1), ('x', -1, -1, -1), ('y', -1, -1, -1), ('y', 1, -1, -1)], sm.get('-z', mat))


def hexa(mb, P, mat, skip=(), sm=None):
    """8 koseli genel kutu. P[(i,j,k)] noktalari; yuzler '-i','+i','-j','+j','-k','+k'."""
    sm = sm or {}
    ids = {key: mb.vert(p) for key, p in P.items()}
    C = sum((Vector(p) for p in P.values()), Vector()) / 8
    S = {'-i': [(0, 0, 0), (0, 1, 0), (0, 1, 1), (0, 0, 1)], '+i': [(1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)],
         '-j': [(0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)], '+j': [(0, 1, 0), (1, 1, 0), (1, 1, 1), (0, 1, 1)],
         '-k': [(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)], '+k': [(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)]}
    for s, q in S.items():
        if s in skip:
            continue
        vv = [ids[t] for t in q]
        cen = sum((Vector(mb.v[i]) for i in vv), Vector()) / 4
        mb.face(vv, sm.get(s, mat), cen - C)


def prism(mb, poly, z0, z1, mat, top=True, bottom=False, top_mat=None):
    """XY'de saat yonu tersi cokgen, Z boyunca ekstrude."""
    n = len(poly)
    b = [mb.vert((x, y, z0)) for x, y in poly]
    t = [mb.vert((x, y, z1)) for x, y in poly]
    for i in range(n):
        j = (i + 1) % n
        dx = poly[j][0] - poly[i][0]
        dy = poly[j][1] - poly[i][1]
        mb.face([b[i], b[j], t[j], t[i]], mat, (dy, -dx, 0))
    if top:
        mb.face(t, top_mat or mat, (0, 0, 1))
    if bottom:
        mb.face(b, mat, (0, 0, -1))


def oct_rect(x0, x1, y0, y1, c):
    return [(x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c), (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c)]


def basis(axis):
    a = Vector(axis).normalized()
    t = Vector((0, 0, 1)) if abs(a.z) < 0.9 else Vector((1, 0, 0))
    u = a.cross(t).normalized()
    v = a.cross(u).normalized()
    return a, u, v


def cyl(mb, base, axis, r, h, n, mat, top=True, bot=False, top_mat=None, phase=0.0):
    a, u, v = basis(axis)
    B = Vector(base)
    angs = [phase + 2 * math.pi * i / n for i in range(n)]
    r0 = [mb.vert(B + (u * math.cos(t) + v * math.sin(t)) * r) for t in angs]
    r1 = [mb.vert(B + a * h + (u * math.cos(t) + v * math.sin(t)) * r) for t in angs]
    for i in range(n):
        j = (i + 1) % n
        tm = (angs[i] + angs[i] + 2 * math.pi / n) / 2
        mb.face([r0[i], r0[j], r1[j], r1[i]], mat, u * math.cos(tm) + v * math.sin(tm))
    if top:
        mb.face(r1, top_mat or mat, a)
    if bot:
        mb.face(r0, mat, -a)


def ring(mb, base, r_out, r_in, h, n, mat):
    """Z ekseninde halka (lastik): dis, ic ve ust yuz."""
    B = Vector(base)
    angs = [2 * math.pi * (i + 0.5) / n for i in range(n)]
    P = lambda r, t, z: B + Vector((r * math.cos(t), r * math.sin(t), z))
    ob = [mb.vert(P(r_out, t, 0)) for t in angs]
    ot = [mb.vert(P(r_out, t, h)) for t in angs]
    ib = [mb.vert(P(r_in, t, 0)) for t in angs]
    it = [mb.vert(P(r_in, t, h)) for t in angs]
    for i in range(n):
        j = (i + 1) % n
        tm = angs[i] + math.pi / n
        d = Vector((math.cos(tm), math.sin(tm), 0))
        mb.face([ob[i], ob[j], ot[j], ot[i]], mat, d)
        mb.face([ib[i], ib[j], it[j], it[i]], mat, -d)
        mb.face([ot[i], ot[j], it[j], it[i]], mat, (0, 0, 1))


def tube(mb, pts, r, n, mat, ref):
    """Duzlemsel yol boyunca boru; ref = yol duzlemine dik birim vektor."""
    ref = Vector(ref).normalized()
    rings = []
    for i, p in enumerate(pts):
        if i == 0:
            tg = pts[1] - pts[0]
        elif i == len(pts) - 1:
            tg = pts[-1] - pts[-2]
        else:
            tg = pts[i + 1] - pts[i - 1]
        tg.normalize()
        u = ref
        v = tg.cross(u).normalized()
        rings.append([mb.vert(p + (u * math.cos(2 * math.pi * k / n) + v * math.sin(2 * math.pi * k / n)) * r) for k in range(n)])
    for i in range(len(pts) - 1):
        for k in range(n):
            l = (k + 1) % n
            c = (Vector(mb.v[rings[i][k]]) + Vector(mb.v[rings[i + 1][l]])) / 2
            cen = (pts[i] + pts[i + 1]) / 2
            mb.face([rings[i][k], rings[i][l], rings[i + 1][l], rings[i + 1][k]], mat, c - cen)


def extrude_y(mb, prof, y0, y1, mat, caps=True):
    """XZ profilini Y boyunca ekstrude et (dis bukey profil)."""
    n = len(prof)
    a = [mb.vert((x, y0, z)) for x, z in prof]
    b = [mb.vert((x, y1, z)) for x, z in prof]
    cx = sum(p[0] for p in prof) / n
    cz = sum(p[1] for p in prof) / n
    for i in range(n):
        j = (i + 1) % n
        mx = (prof[i][0] + prof[j][0]) / 2 - cx
        mz = (prof[i][1] + prof[j][1]) / 2 - cz
        mb.face([a[i], a[j], b[j], b[i]], mat, (mx, 0, mz))
    if caps:
        mb.face(a, mat, (0, -1, 0))
        mb.face(b, mat, (0, 1, 0))


# ---------------------------------------------------------------------------
# Cephe cercevesi ve cephe elemanlari
# ---------------------------------------------------------------------------
class Frame:
    """Cephe yerel koordinati: u cephe boyunca (dis taraftan bakana soldan saga), z yukari,
    d iceri derinlik (negatif = disari cikinti)."""

    def __init__(self, O, U, N, L):
        self.O = Vector(O)
        self.U = Vector(U)
        self.N = Vector(N)
        self.L = L

    def p(self, u, z, d=0.0):
        return self.O + self.U * u + Vector((0, 0, z)) - self.N * d


def frames(x0, x1, y0, y1):
    return {'on': Frame((x0, y0, 0), (1, 0, 0), (0, -1, 0), x1 - x0),
            'sag': Frame((x1, y0, 0), (0, 1, 0), (1, 0, 0), y1 - y0),
            'arka': Frame((x1, y1, 0), (-1, 0, 0), (0, 1, 0), x1 - x0),
            'sol': Frame((x0, y1, 0), (0, -1, 0), (-1, 0, 0), y1 - y0)}


class Op:
    def __init__(self, u0, u1, v0, v1, kind='', tw=0.0, **kw):
        self.u0, self.u1, self.v0, self.v1, self.kind, self.tw = u0, u1, v0, v1, kind, tw
        self.__dict__.update(kw)


def fbox(mb, fr, u0, u1, z0, z1, d0, d1, mat, w=0.0, **kw):
    pts = [fr.p(u, 0, d) for u in (u0, u1) for d in (d0, d1)]
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    if w > 0:
        cbox(mb, min(xs), max(xs), min(ys), max(ys), z0, z1, mat, w=w, **kw)
    else:
        box(mb, min(xs), max(xs), min(ys), max(ys), z0, z1, mat, **kw)


def fquad(mb, fr, u0, u1, z0, z1, d, mat):
    mb.poly([fr.p(u0, z0, d), fr.p(u1, z0, d), fr.p(u1, z1, d), fr.p(u0, z1, d)], mat, fr.N)


def vkey(v):
    v = Vector(v)
    i = max(range(3), key=lambda k: abs(v[k]))
    return ('+' if v[i] > 0 else '-') + 'xyz'[i]


def fskip(fr, names):
    m = {'front': vkey(fr.N), 'back': vkey(-fr.N), 'left': vkey(-fr.U), 'right': vkey(fr.U), 'top': '+z', 'bottom': '-z'}
    return tuple(m[n] for n in names)


def frame_ring(mb, fr, outer, inner, da, db, mat, outer_faces=True, inner_faces=True, outer_bottom=False):
    """Cerceve halkasi: on yuz (4 parca) + ic ve dis kenar yuzleri. Dikdortgen kutulardan ~yarisi kadar ucgen."""
    ua, ub, va, vb = outer
    ia, ib, ja, jb = inner
    has_bottom = ja > va + 1e-6
    fquad(mb, fr, ua, ia, va, vb, da, mat)
    fquad(mb, fr, ib, ub, va, vb, da, mat)
    fquad(mb, fr, ia, ib, jb, vb, da, mat)
    if has_bottom:
        fquad(mb, fr, ia, ib, va, ja, da, mat)
    P = fr.p
    if inner_faces:
        mb.poly([P(ia, ja, da), P(ia, jb, da), P(ia, jb, db), P(ia, ja, db)], mat, fr.U)
        mb.poly([P(ib, ja, da), P(ib, jb, da), P(ib, jb, db), P(ib, ja, db)], mat, -fr.U)
        mb.poly([P(ia, jb, da), P(ib, jb, da), P(ib, jb, db), P(ia, jb, db)], mat, (0, 0, -1))
        if has_bottom:
            mb.poly([P(ia, ja, da), P(ib, ja, da), P(ib, ja, db), P(ia, ja, db)], mat, (0, 0, 1))
    if outer_faces:
        mb.poly([P(ua, va, da), P(ua, vb, da), P(ua, vb, db), P(ua, va, db)], mat, -fr.U)
        mb.poly([P(ub, va, da), P(ub, vb, da), P(ub, vb, db), P(ub, va, db)], mat, fr.U)
        mb.poly([P(ua, vb, da), P(ub, vb, da), P(ub, vb, db), P(ua, vb, db)], mat, (0, 0, 1))
        if has_bottom or outer_bottom:
            if has_bottom:
                mb.poly([P(ua, va, da), P(ub, va, da), P(ub, va, db), P(ua, va, db)], mat, (0, 0, -1))
            else:
                mb.poly([P(ua, va, da), P(ia, va, da), P(ia, va, db), P(ua, va, db)], mat, (0, 0, -1))
                mb.poly([P(ib, va, da), P(ub, va, da), P(ub, va, db), P(ib, va, db)], mat, (0, 0, -1))


def facade_wall(mb, fr, zb, zt, ops, mat, bands=(), ua=0.0, ub=None):
    """Acikliklari olan duz cephe: tam izgara (T-baglantisiz) dortgenler."""
    ub = fr.L if ub is None else ub
    us = sorted(set([ua, ub] + [o.u0 for o in ops] + [o.u1 for o in ops]))
    vs = sorted(set([zb, zt] + [o.v0 for o in ops] + [o.v1 for o in ops] + [b[0] for b in bands] + [b[1] for b in bands]))
    us = [u for u in us if ua - 1e-6 <= u <= ub + 1e-6]
    vs = [v for v in vs if zb - 1e-6 <= v <= zt + 1e-6]
    grid = {}

    def gv(i, j):
        if (i, j) not in grid:
            grid[i, j] = mb.vert(fr.p(us[i], vs[j]))
        return grid[i, j]
    for i in range(len(us) - 1):
        for j in range(len(vs) - 1):
            uc = (us[i] + us[i + 1]) / 2
            vc = (vs[j] + vs[j + 1]) / 2
            if any(o.u0 < uc < o.u1 and o.v0 < vc < o.v1 for o in ops):
                continue
            m = mat
            for b in bands:
                if b[0] < vc < b[1]:
                    m = b[2]
            mb.face([gv(i, j), gv(i + 1, j), gv(i + 1, j + 1), gv(i, j + 1)], m, fr.N)


def reveal(mb, fr, o, segs):
    """Aciklik ic yuzleri. segs: (d0, d1, yan_mat, ust_mat, alt_mat)."""
    for s in segs:
        da, db, ms = s[0], s[1], s[2]
        mh = s[3] if len(s) > 3 else ms
        ml = s[4] if len(s) > 4 else ms
        mb.poly([fr.p(o.u0, o.v0, da), fr.p(o.u0, o.v1, da), fr.p(o.u0, o.v1, db), fr.p(o.u0, o.v0, db)], ms, fr.U)
        mb.poly([fr.p(o.u1, o.v0, da), fr.p(o.u1, o.v1, da), fr.p(o.u1, o.v1, db), fr.p(o.u1, o.v0, db)], ms, -fr.U)
        mb.poly([fr.p(o.u0, o.v1, da), fr.p(o.u1, o.v1, da), fr.p(o.u1, o.v1, db), fr.p(o.u0, o.v1, db)], mh, (0, 0, -1))
        mb.poly([fr.p(o.u0, o.v0, da), fr.p(o.u1, o.v0, da), fr.p(o.u1, o.v0, db), fr.p(o.u0, o.v0, db)], ml, (0, 0, 1))


def pencere(mb, fr, o, wall_mat, d=0.12, mull=(), trans=(), glass='TB_Cam', frame_mat='TB_Trim',
            sill_mat=None, fw=0.05):
    reveal(mb, fr, o, [(0.0, d + 0.02, wall_mat)])
    fquad(mb, fr, o.u0, o.u1, o.v0, o.v1, d, glass)
    a = d - 0.05
    frame_ring(mb, fr, (o.u0, o.u1, o.v0, o.v1), (o.u0 + fw, o.u1 - fw, o.v0 + fw, o.v1 - fw), a, d, frame_mat,
               outer_faces=False)
    sk = fskip(fr, ('back', 'top', 'bottom'))
    for u in mull:
        fbox(mb, fr, u - 0.03, u + 0.03, o.v0 + fw, o.v1 - fw, a + 0.005, d, frame_mat, skip=sk)
    sk2 = fskip(fr, ('back', 'left', 'right'))
    for z in trans:
        fbox(mb, fr, o.u0 + fw, o.u1 - fw, z - 0.03, z + 0.03, a + 0.005, d, frame_mat, skip=sk2)
    if sill_mat:
        fbox(mb, fr, o.u0 - 0.06, o.u1 + 0.06, o.v0 - 0.06, o.v0, -0.07, 0.0, sill_mat, skip=fskip(fr, ('back',)))


def kapi(mb, fr, o, wall_mat, d=0.12, panel='TB_Trim_Mavi', frame_mat='TB_Trim', glass=None, step=False):
    reveal(mb, fr, o, [(0.0, d + 0.02, wall_mat)])
    a, b = d - 0.05, d + 0.02
    fw = 0.06
    if glass:
        fquad(mb, fr, o.u0, o.u1, o.v0, o.v1, d, glass)
        sk = fskip(fr, ('back', 'left', 'right'))
        fbox(mb, fr, o.u0 + fw, o.u1 - fw, o.v0 + 0.95, o.v0 + 1.05, a, d, frame_mat, skip=sk)
        fbox(mb, fr, o.u0 + fw, o.u1 - fw, o.v0, o.v0 + 0.12, a, d, frame_mat, skip=sk)
    else:
        fquad(mb, fr, o.u0, o.u1, o.v0, o.v1, d, panel)
    frame_ring(mb, fr, (o.u0, o.u1, o.v0, o.v1), (o.u0 + fw, o.u1 - fw, o.v0, o.v1 - fw), a, d, frame_mat,
               outer_faces=False)
    # kol: tek yatay cubuk
    fbox(mb, fr, o.u1 - 0.3, o.u1 - 0.12, o.v0 + 1.02, o.v0 + 1.06, d - 0.07, d, 'TB_Galvaniz')
    if step:
        fbox(mb, fr, o.u0 - 0.25, o.u1 + 0.25, -0.2, o.v0, -0.7, 0.0, 'TB_Beton', w=0.02)


def trims(mb, fr, o, w=0.09, out=0.06, mat='TB_Trim', sill=True):
    frame_ring(mb, fr, (o.u0 - w, o.u1 + w, o.v0, o.v1 + w), (o.u0, o.u1, o.v0, o.v1), -out, 0.0, mat,
               outer_bottom=not sill)
    if sill:
        fbox(mb, fr, o.u0 - w - 0.03, o.u1 + w + 0.03, o.v0 - 0.07, o.v0, -out - 0.03, 0.0, mat, skip=fskip(fr, ('back',)))


def kepenk(bina, fr, o, name, wall_mat, housing=True, housing_mat='TB_Kepenk', dc=0.14, rails_mat='TB_Trim'):
    """Sarmal kepenk: aciklik ic yuzleri + raylar + kutu (bina nesnesinde), perde ayri nesne."""
    reveal(bina, fr, o, [(0.0, dc + 0.08, wall_mat)])
    fquad(bina, fr, o.u0, o.u1, o.v0, o.v1, dc + 0.08, 'TB_Ic_Koyu')
    fbox(bina, fr, o.u0, o.u0 + 0.075, o.v0, o.v1, dc - 0.06, dc + 0.06, rails_mat)
    fbox(bina, fr, o.u1 - 0.075, o.u1, o.v0, o.v1, dc - 0.06, dc + 0.06, rails_mat)
    if housing:
        fbox(bina, fr, o.u0 - 0.1, o.u1 + 0.1, o.v1, o.v1 + 0.46, -0.40, 0.02, housing_mat, w=0.03)
    kc = MB(name)
    n = max(10, round((o.v1 - o.v0) / 0.17))
    h = (o.v1 - o.v0) / n
    ua, ub = o.u0 + 0.05, o.u1 - 0.05
    prof = []
    for k in range(n):
        za = o.v0 + k * h
        prof += [(za, dc), (za + h * 0.6, dc - 0.024)]
    prof.append((o.v1, dc))
    L = [kc.vert(fr.p(ua, z, d)) for z, d in prof]
    R = [kc.vert(fr.p(ub, z, d)) for z, d in prof]
    for i in range(len(prof) - 1):
        kc.face([L[i], R[i], R[i + 1], L[i + 1]], 'TB_Kepenk', fr.N)
    fbox(kc, fr, ua - 0.02, ub + 0.02, o.v0, o.v0 + 0.09, dc - 0.045, dc + 0.02, 'TB_Trim')
    pivot = fr.p((o.u0 + o.u1) / 2, o.v1, dc)
    return kc, pivot


def plinth(mb, fr, ops, z_top, ua, ub, out=0.05, mat='TB_Beton_Boya'):
    cuts = sorted([(o.u0 - o.tw, o.u1 + o.tw) for o in ops if o.v0 < z_top])
    segs = subtract([(ua, ub)], cuts)
    for a, b in segs:
        if b - a > 0.05:
            fbox(mb, fr, a, b, TEMEL, z_top, -out, 0.0, mat, w=0.02)


def subtract(segs, cuts):
    out = list(segs)
    for c0, c1 in cuts:
        nxt = []
        for a, b in out:
            if c1 <= a or c0 >= b:
                nxt.append((a, b))
                continue
            if c0 > a:
                nxt.append((a, c0))
            if c1 < b:
                nxt.append((c1, b))
        out = nxt
    return out


def ribs(mb, fr, z_lo, top, ops, spacing, ua, ub, mat='TB_Kaplama', margin=0.11):
    """Trapez sac nervurleri: acikliklarin cevresinde kesilir."""
    bw, fwid, t = 0.075, 0.03, 0.035
    n = int(round((ub - ua) / spacing))
    for k in range(n + 1):
        u = ua + (ub - ua) * k / max(n, 1)
        zt = top(u)
        segs = [(z_lo, zt)]
        for o in ops:
            m = max(margin, o.tw + 0.02)
            if o.u0 - m - bw < u < o.u1 + m + bw:
                hi = o.v1 + (0.1 if o.kind == 'kepenk' else m)
                segs = subtract(segs, [(o.v0 - m, hi)])
        for za, zb in segs:
            if zb - za < 0.25:
                continue
            B1, F1, F2, B2 = (u - bw, 0.0), (u - fwid, -t), (u + fwid, -t), (u + bw, 0.0)
            prof = [B1, F1, F2, B2]
            lo = [mb.vert(fr.p(pu, za, pd)) for pu, pd in prof]
            hi = [mb.vert(fr.p(pu, zb, pd)) for pu, pd in prof]
            dirs = [(-fr.U + fr.N), fr.N, (fr.U + fr.N)]
            for i in range(3):
                mb.face([lo[i], lo[i + 1], hi[i + 1], hi[i]], mat, dirs[i])
            if za > z_lo + 1e-3:
                mb.face(list(lo), mat, (0, 0, -1))
            if zb < zt - 1e-3:
                mb.face(list(hi), mat, (0, 0, 1))


def flat_roof(mb, x0, x1, y0, y1, z_roof, z_top, inset=0.22, out=0.08, roof_mat='TB_Cati_Membran',
              inner_mat='TB_Siva', coping_mat='TB_Beton_Boya'):
    xa, xb, ya, yb = x0 + inset, x1 - inset, y0 + inset, y1 - inset
    mb.poly([(xa, ya, z_roof), (xb, ya, z_roof), (xb, yb, z_roof), (xa, yb, z_roof)], roof_mat, (0, 0, 1))
    mb.poly([(xa, ya, z_roof), (xb, ya, z_roof), (xb, ya, z_top), (xa, ya, z_top)], inner_mat, (0, 1, 0))
    mb.poly([(xa, yb, z_roof), (xb, yb, z_roof), (xb, yb, z_top), (xa, yb, z_top)], inner_mat, (0, -1, 0))
    mb.poly([(xa, ya, z_roof), (xa, yb, z_roof), (xa, yb, z_top), (xa, ya, z_top)], inner_mat, (1, 0, 0))
    mb.poly([(xb, ya, z_roof), (xb, yb, z_roof), (xb, yb, z_top), (xb, ya, z_top)], inner_mat, (-1, 0, 0))
    c = 0.03
    zc = z_top + 0.07
    cbox(mb, x0 - out, x1 + out, y0 - out, ya + c, z_top, zc, coping_mat, w=0.015)
    cbox(mb, x0 - out, x1 + out, yb - c, y1 + out, z_top, zc, coping_mat, w=0.015)
    cbox(mb, x0 - out, xa + c, ya + c, yb - c, z_top, zc, coping_mat, w=0.015)
    cbox(mb, xb - c, x1 + out, ya + c, yb - c, z_top, zc, coping_mat, w=0.015)


def slab(mb, x0, x1, y0, y1, mat='TB_Beton', top=DU):
    cbox(mb, x0, x1, y0, y1, TEMEL, top, mat, w=0.02)


def klima(mb, fr, u0, z0):
    """Duvara asili klima dis unitesi (iki braketli)."""
    fbox(mb, fr, u0, u0 + 0.84, z0, z0 + 0.58, -0.30, 0.01, 'TB_Beyaz', w=0.025)
    c = fr.p(u0 + 0.31, z0 + 0.29, -0.30)
    cyl(mb, c, fr.N, 0.2, 0.012, 12, 'TB_Trim')
    for du in (0.1, 0.7):
        fbox(mb, fr, u0 + du, u0 + du + 0.04, z0 - 0.05, z0, -0.33, 0.01, 'TB_Galvaniz')


def drum(mb, x, y, mat, z0=DU):
    cyl(mb, (x, y, z0), (0, 0, 1), 0.29, 0.88, 10, mat, phase=0.3)
    for zz in (0.29, 0.59):
        cyl(mb, (x, y, z0 + zz), (0, 0, 1), 0.302, 0.035, 10, mat, top=False, phase=0.3)


def pallet(mb, x, y, z, along_x=True):
    lx, ly = (1.2, 0.8) if along_x else (0.8, 1.2)
    box(mb, x - lx / 2, x + lx / 2, y - ly / 2, y + ly / 2, z + 0.122, z + 0.144, 'TB_Palet', skip=('-z',))
    for k in (-1, 0, 1):
        if along_x:
            yy = y + k * (ly / 2 - 0.05)
            box(mb, x - lx / 2, x + lx / 2, yy - 0.05, yy + 0.05, z, z + 0.122, 'TB_Palet', skip=('-z', '+z'))
        else:
            xx = x + k * (lx / 2 - 0.05)
            box(mb, xx - 0.05, xx + 0.05, y - ly / 2, y + ly / 2, z, z + 0.122, 'TB_Palet', skip=('-z', '+z'))


def pallet_stack(mb, x, y, n, along_x=True, z0=DU):
    for i in range(n):
        dx = 0.02 * ((i * 7) % 3 - 1)
        dy = 0.015 * ((i * 5) % 3 - 1)
        pallet(mb, x + dx, y + dy, z0 + i * 0.144, along_x)
    lx, ly = (1.2, 0.8) if along_x else (0.8, 1.2)
    return (x - lx / 2 - 0.03, x + lx / 2 + 0.03, y - ly / 2 - 0.03, y + ly / 2 + 0.03, 0.0, z0 + n * 0.144)


def tire_stack(mb, x, y, n, z0=DU):
    for i in range(n):
        ring(mb, (x + 0.02 * ((i % 2) * 2 - 1), y, z0 + i * 0.205), 0.31, 0.19, 0.2, 10, 'TB_Kaucuk')
    return (x - 0.35, x + 0.35, y - 0.33, y + 0.33, 0.0, z0 + n * 0.205)


# ---------------------------------------------------------------------------
# Cit ve duvar
# ---------------------------------------------------------------------------
def fence_run(mb, axis, c, a0, a1, wall_h=0.4, top=1.8, t=0.2, bar=0.025, spacing=0.25, post_step=2.5,
              end_posts=(True, True)):
    """0,4 m beton duvar + uzeri kare kesitli dikey cubuklu celik cit. axis: 'x' veya 'y' boyunca."""
    def B(a_lo, a_hi, w_lo, w_hi, z0, z1, mat, w=0.0, **kw):
        if axis == 'x':
            (cbox if w else box)(mb, a_lo, a_hi, c + w_lo, c + w_hi, z0, z1, mat, **({'w': w} if w else {}), **kw)
        else:
            (cbox if w else box)(mb, c + w_lo, c + w_hi, a_lo, a_hi, z0, z1, mat, **({'w': w} if w else {}), **kw)
    B(a0, a1, -t / 2, t / 2, TEMEL, wall_h, 'TB_Beton_Duvar', w=0.02)
    L = a1 - a0
    npost = max(1, int(math.ceil(L / post_step)))
    posts = [a0 + L * i / npost for i in range(npost + 1)]
    if not end_posts[0]:
        posts = posts[1:]
    if not end_posts[1]:
        posts = posts[:-1]
    pw = 0.06
    for p in posts:
        pa = min(max(p - pw / 2, a0), a1 - pw)
        B(pa, pa + pw, -pw / 2, pw / 2, wall_h, top + 0.04, 'TB_Cit', skip=('-z',))
    ra, rb = a0 + 0.01, a1 - 0.01
    B(ra, rb, -0.02, 0.02, wall_h + 0.1, wall_h + 0.15, 'TB_Cit')
    B(ra, rb, -0.02, 0.02, top - 0.12, top - 0.07, 'TB_Cit')
    nb = int(L / spacing)
    for i in range(1, nb + 1):
        p = a0 + L * i / (nb + 1)
        if any(abs(p - q) < 0.08 for q in posts):
            continue
        B(p - bar / 2, p + bar / 2, -bar / 2, bar / 2, wall_h, top, 'TB_Cit', skip=('-z', '+z'))


def solid_wall(mb, axis, c, a0, a1, h, inward, t=0.2, step=3.2, coping=(None, None), mat='TB_Beton_Duvar'):
    """Dolu bahce duvari: harpusta + ic tarafta payandalar. inward: ic tarafin isareti (+1/-1)."""
    def B(a_lo, a_hi, w_lo, w_hi, z0, z1, m, w=0.0, skip=None):
        if skip is not None:
            if axis == 'x':
                box(mb, a_lo, a_hi, c + w_lo, c + w_hi, z0, z1, m, skip=skip)
            else:
                box(mb, c + w_lo, c + w_hi, a_lo, a_hi, z0, z1, m, skip=skip)
        elif axis == 'x':
            cbox(mb, a_lo, a_hi, c + w_lo, c + w_hi, z0, z1, m, w=w)
        else:
            cbox(mb, c + w_lo, c + w_hi, a_lo, a_hi, z0, z1, m, w=w)
    B(a0, a1, -t / 2, t / 2, TEMEL, h, mat, w=0.015)
    ca = coping[0] if coping[0] is not None else a0
    cb = coping[1] if coping[1] is not None else a1
    B(ca, cb, -t / 2 - 0.02, t / 2 + 0.02, h, h + 0.06, 'TB_Beton_Boya', w=0.012)
    L = a1 - a0
    n = max(1, int(L / step))
    for i in range(1, n):
        p = a0 + L * i / n
        back = ('-' if inward > 0 else '+') + ('y' if axis == 'x' else 'x')
        if inward > 0:
            B(p - 0.15, p + 0.15, t / 2 - 0.01, t / 2 + 0.07, TEMEL, h, mat, skip=('-z', back))
        else:
            B(p - 0.15, p + 0.15, -t / 2 - 0.07, -t / 2 + 0.01, TEMEL, h, mat, skip=('-z', back))


def gate_pillar(mb, x0, x1, y0, y1, h=2.0):
    cbox(mb, x0 + 0.02, x1 - 0.02, y0 + 0.02, y1 - 0.02, TEMEL, h, 'TB_Beton_Boya', w=0.02)
    cbox(mb, x0, x1, y0, y1, h, h + 0.08, 'TB_Beton_Duvar', w=0.015)


def front_fence_with_gates(mb, A, G, gates, y_line=0.12, label='On'):
    """On kenar: kapi bosluklari arasinda duvar + cit, bosluk kenarlarinda beton baba."""
    half = G / 2 - 0.02
    edges = [-half]
    for g0, g1 in gates:
        edges += [g0, g1]
    edges.append(half)
    for k in range(0, len(edges), 2):
        a, b = edges[k], edges[k + 1]
        left_gate = k > 0
        right_gate = k + 1 < len(edges) - 1
        aa = a + (0.38 if left_gate else 0.0)
        bb = b - (0.38 if right_gate else 0.0)
        if bb - aa > 0.3:
            fence_run(mb, 'x', y_line, aa, bb)
            A.col(f'Cit_{label}_{k // 2 + 1}', aa, bb, y_line - 0.1, y_line + 0.1, 0.0, 1.84)
    n = 0
    for g0, g1 in gates:
        for a, b in ((g0 - 0.4, g0), (g1, g1 + 0.4)):
            n += 1
            gate_pillar(mb, a, b, 0.0, 0.40)
            A.col(f'Kapi_Babasi_{n}', a, b, 0.0, 0.40, 0.0, 2.08)


# ---------------------------------------------------------------------------
# Catilar
# ---------------------------------------------------------------------------
def gable_roof(mb, x0, x1, y0, y1, eave, tn, oe=0.35, og=0.3, t=0.18, rib_step=1.0, skylights=()):
    xm = (x0 + x1) / 2

    def zb(x):
        return eave + min(x - x0, x1 - x) * tn

    def zt(x):
        return zb(x) + t
    ya, yb = y0 - og, y1 + og
    for s in (-1, 1):
        xa = x0 - oe if s < 0 else x1 + oe
        P = {}
        for i, x in enumerate((xa, xm)):
            for j, y in enumerate((ya, yb)):
                P[i, j, 0] = (x, y, zb(x))
                P[i, j, 1] = (x, y, zt(x))
        hexa(mb, P, 'TB_Cati_Sac', skip=('+i',))
        # nervurler (egim boyunca)
        xr = xm + s * 0.28
        nrib = int((yb - ya - 0.5) / rib_step)
        for k in range(nrib + 1):
            yc = ya + 0.25 + (yb - ya - 0.5) * k / nrib
            if any(abs(yc - sc) < 0.62 for sc in skylights):
                continue
            prof = [(-0.07, 0.0), (-0.025, 0.04), (0.025, 0.04), (0.07, 0.0)]
            A_ = [mb.vert((xa, yc + dy, zt(xa) + dz)) for dy, dz in prof]
            B_ = [mb.vert((xr, yc + dy, zt(xr) + dz)) for dy, dz in prof]
            dirs = [(0, -1, 1), (0, 0, 1), (0, 1, 1)]
            for i in range(3):
                mb.face([A_[i], A_[i + 1], B_[i + 1], B_[i]], 'TB_Cati_Sac', dirs[i])
            mb.face(A_, 'TB_Cati_Sac', (s, 0, 0))
        for sc in skylights:
            xs0 = xa - s * 0.9
            xs1 = xm + s * 0.7
            Q = {}
            for i, x in enumerate((xs0, xs1)):
                for j, y in enumerate((sc - 0.42, sc + 0.42)):
                    Q[i, j, 0] = (x, y, zt(x) + 0.004)
                    Q[i, j, 1] = (x, y, zt(x) + 0.03)
            hexa(mb, Q, 'TB_Cati_Isik', skip=('-k',))
        # oluk
        gx0, gx1 = (xa - 0.16, xa + 0.01) if s < 0 else (xa - 0.01, xa + 0.16)
        cbox(mb, gx0, gx1, ya, yb, zb(xa) - 0.15, zb(xa) + t * 0.55, 'TB_Trim', w=0.02, wb=0.02, bottom=True)
        # alin (rüzgar) profilleri
        for yy0, yy1 in ((ya - 0.04, ya + 0.09), (yb - 0.09, yb + 0.04)):
            R = {}
            for i, x in enumerate((xa, xm)):
                for j, y in enumerate((yy0, yy1)):
                    R[i, j, 0] = (x, y, zb(x) - 0.13)
                    R[i, j, 1] = (x, y, zt(x) + 0.05)
            hexa(mb, R, 'TB_Trim', skip=('+i',))
    # mahya
    zr = zt(xm)
    prof = [(xm - 0.3, zt(xm - 0.3) - 0.01), (xm + 0.3, zt(xm + 0.3) - 0.01), (xm + 0.3, zt(xm + 0.3) + 0.05),
            (xm, zr + 0.11), (xm - 0.3, zt(xm - 0.3) + 0.05)]
    extrude_y(mb, prof, ya - 0.05, yb + 0.05, 'TB_Trim')
    return zb, zt


def downpipe(mb, x_wall_out, y, z_top, x_gutter, s):
    """Oluk ucundan duvara donen ve inen kare kesitli yagmur borusu. s: cephe yonu (-1 sol, +1 sag)."""
    q = 0.05
    # gutter -> asagi kisa parca
    xg0, xg1 = x_gutter - q, x_gutter + q
    box(mb, xg0, xg1, y - q, y + q, z_top - 0.32, z_top, 'TB_Trim', skip=('+z',))
    # yatay donus
    xw = x_wall_out + s * 0.07
    lo, hi = sorted((xw - q, x_gutter + q * (1 if s > 0 else -1)))
    box(mb, lo, hi, y - q, y + q, z_top - 0.42, z_top - 0.32, 'TB_Trim')
    # duvar boyunca inen
    box(mb, xw - q, xw + q, y - q, y + q, DU + 0.06, z_top - 0.42, 'TB_Trim')


def downpipe_flat(mb, fr, u, z_top, z_bot=DU + 0.06):
    """Duz catida parapetten cikan kucuk ciki + duvar boyunca boru."""
    fbox(mb, fr, u - 0.09, u + 0.09, z_top - 0.12, z_top, -0.26, 0.0, 'TB_Trim')
    fbox(mb, fr, u - 0.055, u + 0.055, z_bot, z_top - 0.12, -0.16, -0.05, 'TB_Trim')


def water_tank(mb, x, y, z0, r=0.6, h=1.15):
    cbox(mb, x - r - 0.08, x + r + 0.08, y - r - 0.08, y + r + 0.08, z0, z0 + 0.3, 'TB_Trim', w=0.02)
    cyl(mb, (x, y, z0 + 0.3), (0, 0, 1), r, h, 12, 'TB_Beyaz')
    cyl(mb, (x, y, z0 + 0.3 + h), (0, 0, 1), 0.2, 0.07, 8, 'TB_Beyaz')


def roof_ac(mb, x, y, z0, along_x=True):
    lx, ly = (1.3, 0.85) if along_x else (0.85, 1.3)
    cbox(mb, x - lx / 2, x + lx / 2, y - ly / 2, y + ly / 2, z0, z0 + 0.95, 'TB_Beyaz', w=0.03)
    cyl(mb, (x, y, z0 + 0.95), (0, 0, 1), 0.3, 0.02, 12, 'TB_Trim')
    for k in (-1, 1):
        if along_x:
            box(mb, x - lx / 2 + 0.05, x + lx / 2 - 0.05, y + k * (ly / 2 - 0.08) - 0.04, y + k * (ly / 2 - 0.08) + 0.04, z0 - 0.1, z0, 'TB_Galvaniz')
        else:
            box(mb, x + k * (lx / 2 - 0.08) - 0.04, x + k * (lx / 2 - 0.08) + 0.04, y - ly / 2 + 0.05, y + ly / 2 - 0.05, z0 - 0.1, z0, 'TB_Galvaniz')


def planter(mb, A, label, x0, x1, y0, y1, cw=0.15, top=DU + 0.15):
    cbox(mb, x0, x1, y0, y0 + cw, -0.2, top, 'TB_Bordur', w=0.02)
    cbox(mb, x0, x1, y1 - cw, y1, -0.2, top, 'TB_Bordur', w=0.02)
    cbox(mb, x0, x0 + cw, y0 + cw, y1 - cw, -0.2, top, 'TB_Bordur', w=0.02)
    cbox(mb, x1 - cw, x1, y0 + cw, y1 - cw, -0.2, top, 'TB_Bordur', w=0.02)
    mb.poly([(x0 + cw, y0 + cw, top - 0.03), (x1 - cw, y0 + cw, top - 0.03), (x1 - cw, y1 - cw, top - 0.03), (x0 + cw, y1 - cw, top - 0.03)], 'TB_Cim', (0, 0, 1))
    A.col('Ada_' + label, x0, x1, y0, y1, 0.0, top)


def paint_line(mb, x0, x1, y0, y1):
    box(mb, x0, x1, y0, y1, DU, DU + 0.01, 'TB_Boya_Beyaz', skip=('-z',))


# ---------------------------------------------------------------------------
# Varlik kaydi
# ---------------------------------------------------------------------------
class Asset:
    def __init__(self, aid, G, D, budget):
        self.id, self.G, self.D, self.budget = aid, G, D, budget
        self.mbs = []
        self.extra = []      # (MB, origin)
        self.cols = []
        self.park = []
        self.gates = []
        self.trees = []
        self.foot = []
        self.moving = []
        self.checks = {}

    def mb(self, suffix):
        m = MB(f'{self.id}_{suffix}')
        self.mbs.append(m)
        return m

    def add(self, mb, origin):
        self.extra.append((mb, origin))

    def col(self, label, x0, x1, y0, y1, z0, z1):
        self.cols.append((f'COL_{self.id}_{label}', min(x0, x1), max(x0, x1), min(y0, y1), max(y0, y1), z0, z1))

    def spot(self, x, y, yaw=0.0):
        self.park.append((x, y, yaw))


def make_object(mb, coll, origin=(0, 0, 0)):
    o = Vector(origin)
    used = sorted({i for f in mb.f for i in f})
    remap = {old: new for new, old in enumerate(used)}
    me = bpy.data.meshes.new(mb.name)
    me.from_pydata([tuple(Vector(mb.v[i]) - o) for i in used], [], [tuple(remap[i] for i in f) for f in mb.f])
    for name in mb.mats:
        me.materials.append(MAT[name])
    for p, mi in zip(me.polygons, mb.fm):
        p.material_index = mi
    me.validate(clean_customdata=False)
    me.update()
    ob = bpy.data.objects.new(mb.name, me)
    ob.location = o
    coll.objects.link(ob)
    return ob


def make_col(coll, name, x0, x1, y0, y1, z0, z1):
    c = Vector(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2))
    hx, hy, hz = (x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2
    v = [(sx * hx, sy * hy, sz * hz) for sz in (-1, 1) for sy in (-1, 1) for sx in (-1, 1)]
    f = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    me = bpy.data.meshes.new(name)
    me.from_pydata(v, [], f)
    me.update()
    ob = bpy.data.objects.new(name, me)
    ob.location = c
    coll.objects.link(ob)
    return ob


# ===========================================================================
# 1) ATOLYE_A : celik kaplamali oto tamir atolyesi
# ===========================================================================
def atolye_a():
    A = Asset('Atolye_A', 24.0, 32.0, 6000)
    bina, cati, dos, cit, esya = A.mb('Bina'), A.mb('Cati'), A.mb('Doseme'), A.mb('Cit'), A.mb('Esya')
    x0, x1, y0, y1 = -10.0, 4.0, 11.0, 29.0
    EAVE = 5.5
    tn = math.tan(math.radians(10.0))
    L = x1 - x0
    RB = EAVE + L / 2 * tn
    T = 0.18
    PL = 0.9
    F = frames(x0, x1, y0, y1)
    kep = Op(2.5, 6.5, DU, DU + 4.2, 'kepenk', tw=0.13)
    kapi_on = Op(8.0, 9.0, DU, DU + 2.1, 'kapi', tw=0.09)
    ofis = Op(10.0, 12.4, 1.0, 2.2, 'pencere', tw=0.09)
    bant = Op(7.6, 13.2, 3.0, 3.8, 'pencere', tw=0.09)
    side = [(1.8, 8.2), (9.8, 16.2)]
    kapi_ar = Op(9.8, 10.8, DU, DU + 2.1, 'kapi', tw=0.09)
    ops = {'on': [kep, kapi_on, ofis, bant],
           'sag': [Op(a, b, 3.5, 4.3, 'pencere', tw=0.09) for a, b in side],
           'sol': [Op(a, b, 3.5, 4.3, 'pencere', tw=0.09) for a, b in side],
           'arka': [kapi_ar, Op(2.0, 4.4, 3.3, 4.1, 'pencere', tw=0.09), Op(6.0, 8.4, 3.3, 4.1, 'pencere', tw=0.09)]}
    for k, fr in F.items():
        facade_wall(bina, fr, TEMEL, EAVE, ops[k], 'TB_Kaplama')
        if k in ('on', 'arka'):
            bina.poly([fr.p(0, EAVE), fr.p(fr.L, EAVE), fr.p(fr.L / 2, RB)], 'TB_Kaplama', fr.N)
            top = (lambda u, Lf=fr.L: EAVE + (Lf / 2 - abs(u - Lf / 2)) * tn - 0.08)
            plinth(bina, fr, ops[k], PL, -0.05, fr.L + 0.05)
        else:
            top = (lambda u: EAVE - 0.04)
            plinth(bina, fr, ops[k], PL, 0.0, fr.L)
        ribs(bina, fr, PL, top, ops[k], 0.75, 0.3, fr.L - 0.3)
        for o in ops[k]:
            if o.kind == 'pencere':
                pencere(bina, fr, o, 'TB_Kaplama', mull=[o.u0 + (o.u1 - o.u0) * i / max(1, round((o.u1 - o.u0) / 1.2)) for i in range(1, max(1, round((o.u1 - o.u0) / 1.2)))])
                trims(bina, fr, o)
            elif o.kind == 'kapi':
                kapi(bina, fr, o, 'TB_Kaplama', step=(k == 'arka'))
                trims(bina, fr, o, sill=False)
    # kepenk
    fr = F['on']
    kc, piv = kepenk(bina, fr, kep, 'Atolye_A_Kepenk_1', 'TB_Beton_Boya')
    A.add(kc, piv)
    A.moving.append({'ad': 'Atolye_A_Kepenk_1', 'tur': 'sarmal_kepenk', 'genislik': 4.0, 'yukseklik': 4.2})
    fbox(bina, fr, kep.u0 - 0.13, kep.u0, kep.v0, kep.v1, -0.07, 0.0, 'TB_Trim')
    fbox(bina, fr, kep.u1, kep.u1 + 0.13, kep.v0, kep.v1, -0.07, 0.0, 'TB_Trim')
    # kose profilleri
    for cx, cy in ((x0, y0), (x1, y0), (x1, y1), (x0, y1)):
        sx = -1 if cx == x0 else 1
        sy = -1 if cy == y0 else 1
        xa, xb = sorted((cx + sx * 0.065, cx - sx * 0.11))
        ya, yb = sorted((cy + sy * 0.065, cy - sy * 0.11))
        cbox(bina, xa, xb, ya, yb, PL, EAVE - 0.02, 'TB_Trim', w=0.015)
    klima(bina, F['sag'], 0.95, 2.45)
    # cati
    zb, zt = gable_roof(cati, x0, x1, y0, y1, EAVE, tn, rib_step=1.25, skylights=(y0 + 4.6, y1 - 4.6))
    for s, xw, xg in ((-1, x0 - 0.065, x0 - 0.35 - 0.075), (1, x1 + 0.065, x1 + 0.35 + 0.075)):
        for yy in (y0 + 0.4, y1 - 0.4):
            downpipe(cati, xw, yy, zb(x0 - 0.35) - 0.15, xg, s)
    # doseme
    slab(dos, -11.78, 11.78, 0.0, y0)
    slab(dos, x1, 11.78, y0, 31.78)
    A.col('Doseme_On', -11.78, 11.78, 0.0, y0, TEMEL, DU)
    A.col('Doseme_Yan', x1, 11.78, y0, 31.78, TEMEL, DU)
    # cit + duvarlar
    gl, gr = -8.5, -2.5
    front_fence_with_gates(cit, A, 24.0, [(gl, gr)])
    for sx in (-1, 1):
        c = sx * 11.88
        fence_run(cit, 'y', c, 0.22, y0, end_posts=(True, True))
        A.col(f'Cit_Yan_{"Sol" if sx < 0 else "Sag"}', c - 0.1, c + 0.1, 0.22, y0, 0.0, 1.84)
        solid_wall(cit, 'y', c, y0, 31.78, 2.0, -sx, coping=(y0, 31.76))
        A.col(f'Duvar_Yan_{"Sol" if sx < 0 else "Sag"}', c - 0.12, c + 0.12, y0, 31.78, 0.0, 2.06)
    solid_wall(cit, 'x', 31.88, -11.98, 11.98, 2.0, -1)
    A.col('Duvar_Arka', -11.98, 11.98, 31.76, 32.0, 0.0, 2.06)
    # esyalar
    for i, (x, y, n, ax) in enumerate(((10.3, 30.9, 5, True), (-9.35, 9.9, 4, True), (10.6, 29.2, 2, False))):
        b = pallet_stack(esya, x, y, n, ax)
        A.col(f'Palet_{i + 1}', *b)
    for x, y, m in ((4.75, 11.75, 'TB_Varil_Mavi'), (5.42, 11.72, 'TB_Varil_Mavi'), (4.82, 12.42, 'TB_Varil_Kirmizi')):
        drum(esya, x, y, m)
    A.col('Variller', 4.44, 5.73, 11.43, 12.73, 0.0, 0.94)
    # bina carpisma ve veriler
    A.col('Bina', x0, x1, y0, y1, 0.0, RB + T)
    A.foot.append(('Atolye_A_Bina', x0, x1, y0, y1, round(RB + T + 0.11, 2)))
    A.gates.append((gl, gr))
    for x, y in ((0.9, 7.0), (3.6, 7.0), (7.0, 18.5), (9.8, 18.5)):
        A.spot(x, y, 0)
    A.trees += [(-6.0, 30.6), (0.5, 30.6)]
    A.checks = {'govde': [14.0, 18.0], 'sacak': EAVE, 'kepenk': [4.0, 4.2], 'kapi_boslugu': 6.0}
    return A


# ===========================================================================
# 2) ATOLYE_B : sivali tugla atolye, duz cati + parapet, 2 kepenk
# ===========================================================================
def atolye_b():
    A = Asset('Atolye_B', 20.0, 28.0, 6000)
    bina, cati, dos, cit, esya = A.mb('Bina'), A.mb('Cati'), A.mb('Doseme'), A.mb('Cit'), A.mb('Esya')
    x0, x1, y0, y1 = -9.6, 0.4, 14.0, 26.0
    ZR, ZP = 4.45, 5.0
    PL = 0.45
    F = frames(x0, x1, y0, y1)
    k1 = Op(0.5, 3.7, DU, DU + 3.6, 'kepenk', tw=0.02)
    k2 = Op(4.3, 7.5, DU, DU + 3.6, 'kepenk', tw=0.02)
    kp = Op(8.3, 9.25, DU, DU + 2.1, 'kapi', tw=0.02)
    pk = Op(8.3, 9.5, 2.6, 3.3, 'pencere')
    ops = {'on': [k1, k2, kp, pk],
           'sag': [Op(a, b, 1.9, 2.9, 'pencere') for a, b in ((1.6, 2.8), (3.8, 5.0), (7.0, 8.2), (9.2, 10.4))],
           'sol': [Op(a, b, 2.6, 3.2, 'pencere') for a, b in ((2.5, 3.5), (8.5, 9.5))],
           'arka': [Op(6.5, 7.4, DU, DU + 2.1, 'kapi', tw=0.02), Op(2.0, 3.2, 2.4, 3.2, 'pencere')]}
    mids = {'on': [(3.72, 4.28), (7.52, 8.12)], 'sag': [(5.75, 6.25)], 'sol': [(5.75, 6.25)], 'arka': [(4.75, 5.25)]}
    for k, fr in F.items():
        facade_wall(bina, fr, TEMEL, ZP, ops[k], 'TB_Siva')
        if k in ('on', 'arka'):
            plinth(bina, fr, ops[k], PL, -0.04, fr.L + 0.04, out=0.04)
        else:
            plinth(bina, fr, ops[k], PL, 0.0, fr.L, out=0.04)
        for a, b in mids[k]:
            fbox(bina, fr, a, b, TEMEL, ZP, -0.06, 0.02, 'TB_Beton_Boya', w=0.02)
        fbox(bina, fr, 0.36, fr.L - 0.36, 4.1, 4.45, -0.04, 0.02, 'TB_Beton_Boya', w=0.015)
        for o in ops[k]:
            if o.kind == 'pencere':
                pencere(bina, fr, o, 'TB_Siva', mull=[(o.u0 + o.u1) / 2] if o.u1 - o.u0 > 1.1 else [], sill_mat='TB_Beton_Boya')
            elif o.kind == 'kapi':
                kapi(bina, fr, o, 'TB_Siva', step=(k == 'arka'))
    for cx, cy in ((x0, y0), (x1, y0), (x1, y1), (x0, y1)):
        sx = -1 if cx == x0 else 1
        sy = -1 if cy == y0 else 1
        xa, xb = sorted((cx + sx * 0.06, cx - sx * 0.36))
        ya, yb = sorted((cy + sy * 0.06, cy - sy * 0.36))
        cbox(bina, xa, xb, ya, yb, TEMEL, ZP, 'TB_Beton_Boya', w=0.02)
    fr = F['on']
    for i, o in enumerate((k1, k2)):
        kc, piv = kepenk(bina, fr, o, f'Atolye_B_Kepenk_{i + 1}', 'TB_Siva')
        A.add(kc, piv)
        A.moving.append({'ad': f'Atolye_B_Kepenk_{i + 1}', 'tur': 'sarmal_kepenk', 'genislik': 3.2, 'yukseklik': 3.6})
    fbox(bina, fr, 8.12, 9.62, 2.25, 2.35, -0.8, 0.02, 'TB_Beton_Boya', w=0.02)  # kapi saçagi
    # cati
    flat_roof(cati, x0, x1, y0, y1, ZR, ZP)
    water_tank(cati, -2.6, 23.6, ZR)
    for u in (0.7, 11.3):
        downpipe_flat(cati, F['sag'], u, ZP - 0.1)
    # doseme + duvarlar
    slab(dos, -9.78, 9.78, 0.0, y0)
    slab(dos, x1 + 0.06, 9.78, y0, 27.78)
    A.col('Doseme_On', -9.78, 9.78, 0.0, y0, TEMEL, DU)
    A.col('Doseme_Yan', x1 + 0.06, 9.78, y0, 27.78, TEMEL, DU)
    for sx in (-1, 1):
        c = sx * 9.88
        solid_wall(cit, 'y', c, 0.0, 27.78, 1.9, -sx, coping=(0.0, 27.76))
        A.col(f'Duvar_Yan_{"Sol" if sx < 0 else "Sag"}', c - 0.12, c + 0.12, 0.0, 27.78, 0.0, 1.96)
    solid_wall(cit, 'x', 27.88, -9.98, 9.98, 1.9, -1)
    A.col('Duvar_Arka', -9.98, 9.98, 27.76, 28.0, 0.0, 1.96)
    # yan avluda celik arac sundurmasi (binaya yaslanan tek egimli)
    sund = A.mb('Sundurma')
    sx0, sx1, sy0, sy1 = x1 + 0.005, 8.45, 15.0, 25.0
    zh, zl = 3.75, 3.15
    ztop = lambda x: zh + (zl - zh) * (x - sx0) / (sx1 - sx0)
    Q = {}
    for i, x in enumerate((sx0, sx1)):
        for j, y in enumerate((sy0, sy1)):
            Q[i, j, 0] = (x, y, ztop(x))
            Q[i, j, 1] = (x, y, ztop(x) + 0.06)
    hexa(sund, Q, 'TB_Cati_Sac', skip=('-i',))
    for k in range(9):
        yc = sy0 + 0.6 + (sy1 - sy0 - 1.2) * k / 8
        prof = [(-0.06, 0.0), (-0.02, 0.035), (0.02, 0.035), (0.06, 0.0)]
        a_ = [sund.vert((sx0, yc + dy, ztop(sx0) + 0.06 + dz)) for dy, dz in prof]
        b_ = [sund.vert((sx1, yc + dy, ztop(sx1) + 0.06 + dz)) for dy, dz in prof]
        for i, dd in enumerate(((0, -1, 1), (0, 0, 1), (0, 1, 1))):
            sund.face([a_[i], a_[i + 1], b_[i + 1], b_[i]], 'TB_Cati_Sac', dd)
        sund.face(b_, 'TB_Cati_Sac', (1, 0, 0))
    cbox(sund, sx1 - 0.02, sx1 + 0.12, sy0, sy1, zl - 0.14, zl + 0.08, 'TB_Trim', w=0.015, wb=0.015, bottom=True)
    cbox(sund, sx1 - 0.3, sx1 - 0.1, sy0, sy1, zl - 0.2, zl - 0.01, 'TB_Cit', w=0.01, wb=0.01, bottom=True)
    cbox(sund, sx0, sx0 + 0.14, sy0, sy1, zh - 0.2, zh, 'TB_Cit', w=0.01, wb=0.01, bottom=True)
    for i, py in enumerate((sy0 + 0.2, (sy0 + sy1) / 2, sy1 - 0.2)):
        box(sund, sx1 - 0.26, sx1 - 0.14, py - 0.06, py + 0.06, DU, zl - 0.2, 'TB_Cit', skip=('-z',))
        cbox(sund, sx1 - 0.32, sx1 - 0.08, py - 0.12, py + 0.12, DU, DU + 0.12, 'TB_Beton_Boya', w=0.015)
        A.col(f'Sundurma_Direk_{i + 1}', sx1 - 0.32, sx1 - 0.08, py - 0.12, py + 0.12, 0.0, zl - 0.2)
    # esyalar
    A.col('Lastik_1', *tire_stack(esya, 1.35, 13.35, 4))
    A.col('Lastik_2', *tire_stack(esya, 2.1, 13.4, 2))
    A.col('Palet_1', *pallet_stack(esya, 8.75, 26.95, 5, True))
    for x, y, m in ((7.4, 27.1, 'TB_Varil_Mavi'), (6.75, 27.15, 'TB_Varil_Kirmizi')):
        drum(esya, x, y, m)
    A.col('Variller', 6.44, 7.71, 26.79, 27.46, 0.0, 0.94)
    A.col('Bina', x0, x1, y0, y1, 0.0, ZP + 0.07)
    A.foot.append(('Atolye_B_Bina', x0, x1, y0, y1, round(ZP + 0.07, 2)))
    A.gates.append((-9.5, 9.5))
    for x, y in ((-7.5, 9.0), (-3.7, 9.0), (6.0, 8.0), (2.9, 20.0), (5.9, 20.0)):
        A.spot(x, y, 0)
    A.trees += [(-5.0, 27.0)]
    A.checks = {'govde': [10.0, 12.0], 'kepenk_sayisi': 2}
    return A


# ===========================================================================
# 3) ATOLYE_C : iki dukkanli sanayi blogu
# ===========================================================================
def atolye_c():
    A = Asset('Atolye_C', 30.0, 28.0, 6000)
    bina, cati, dos, cit, esya = A.mb('Bina'), A.mb('Cati'), A.mb('Doseme'), A.mb('Cit'), A.mb('Esya')
    x0, x1, y0, y1 = -11.0, 11.0, 12.0, 26.0
    ZR, ZP = 6.05, 6.6
    PL = 0.45
    F = frames(x0, x1, y0, y1)
    on_ops, keps = [], []
    for s0 in (0.0, 11.0):
        kp = Op(s0 + 0.9, s0 + 4.9, DU, DU + 4.4, 'kepenk', tw=0.02)
        keps.append(kp)
        on_ops += [kp,
                   Op(s0 + 5.7, s0 + 6.75, DU, DU + 2.5, 'camkapi', tw=0.02),
                   Op(s0 + 6.95, s0 + 10.15, 0.6, 2.54, 'vitrin'),
                   Op(s0 + 5.7, s0 + 10.15, 3.1, 4.3, 'ust')]
    ops = {'on': on_ops,
           'sag': [Op(2.2, 3.6, 1.9, 3.0, 'pencere'), Op(8.0, 9.4, 1.9, 3.0, 'pencere')],
           'sol': [Op(4.6, 6.0, 1.9, 3.0, 'pencere'), Op(10.4, 11.8, 1.9, 3.0, 'pencere')],
           'arka': []}
    for s0 in (0.0, 11.0):
        ops['arka'] += [Op(s0 + 1.4, s0 + 3.6, 3.4, 4.4, 'pencere'), Op(s0 + 5.0, s0 + 7.2, 3.4, 4.4, 'pencere'),
                        Op(s0 + 8.6, s0 + 9.6, DU, DU + 2.1, 'kapi', tw=0.02)]
    mids = {'on': [(10.75, 11.25)], 'sag': [(6.75, 7.25)], 'sol': [(6.75, 7.25)], 'arka': [(10.75, 11.25)]}
    for k, fr in F.items():
        facade_wall(bina, fr, TEMEL, ZP, ops[k], 'TB_Siva')
        if k in ('on', 'arka'):
            plinth(bina, fr, ops[k], PL, -0.04, fr.L + 0.04, out=0.04)
        else:
            plinth(bina, fr, ops[k], PL, 0.0, fr.L, out=0.04)
        for a, b in mids[k]:
            fbox(bina, fr, a, b, TEMEL, ZP, -0.06, 0.02, 'TB_Beton_Boya', w=0.02)
        if k != 'on':
            fbox(bina, fr, 0.45, fr.L - 0.45, 5.0, 5.35, -0.04, 0.02, 'TB_Beton_Boya', w=0.015)
        for o in ops[k]:
            if o.kind == 'pencere':
                pencere(bina, fr, o, 'TB_Siva', mull=[(o.u0 + o.u1) / 2] if o.u1 - o.u0 > 1.1 else [], sill_mat='TB_Beton_Boya')
            elif o.kind == 'vitrin':
                pencere(bina, fr, o, 'TB_Siva', d=0.1, mull=[o.u0 + 1.07, o.u0 + 2.13], fw=0.06)
                fbox(bina, fr, o.u0 - 0.04, o.u1 + 0.04, o.v0 - 0.05, o.v0, -0.06, 0.0, 'TB_Beton_Boya', w=0.012)
            elif o.kind == 'ust':
                pencere(bina, fr, o, 'TB_Siva', d=0.1, mull=[o.u0 + (o.u1 - o.u0) * i / 4 for i in (1, 2, 3)], fw=0.06,
                        sill_mat='TB_Beton_Boya')
            elif o.kind == 'camkapi':
                kapi(bina, fr, o, 'TB_Siva', d=0.1, glass='TB_Cam')
            elif o.kind == 'kapi':
                kapi(bina, fr, o, 'TB_Siva', step=True)
    for cx, cy in ((x0, y0), (x1, y0), (x1, y1), (x0, y1)):
        sx = -1 if cx == x0 else 1
        sy = -1 if cy == y0 else 1
        xa, xb = sorted((cx + sx * 0.06, cx - sx * 0.45))
        ya, yb = sorted((cy + sy * 0.06, cy - sy * 0.45))
        cbox(bina, xa, xb, ya, yb, TEMEL, ZP, 'TB_Beton_Boya', w=0.02)
    fr = F['on']
    for i, o in enumerate(keps):
        kc, piv = kepenk(bina, fr, o, f'Atolye_C_Kepenk_{i + 1}', 'TB_Siva')
        A.add(kc, piv)
        A.moving.append({'ad': f'Atolye_C_Kepenk_{i + 1}', 'tur': 'sarmal_kepenk', 'genislik': 4.0, 'yukseklik': 4.4})
    # sacak (sundurma) ve tabelalar
    fbox(bina, fr, -0.12, fr.L + 0.12, 5.0, 5.22, -1.5, 0.02, 'TB_Beton_Boya', w=0.03)
    for s0, tm in ((0.0, 'TB_Tabela'), (11.0, 'TB_Tabela_2')):
        fbox(bina, fr, s0 + 0.85, s0 + 10.15, 5.42, 6.28, -0.09, 0.02, tm, w=0.025)
        fbox(bina, fr, s0 + 0.8, s0 + 10.2, 5.36, 5.42, -0.11, 0.02, 'TB_Trim')
    klima(bina, F['sag'], 1.0, 2.4)
    klima(bina, F['sol'], 12.2, 2.4)
    # cati
    flat_roof(cati, x0, x1, y0, y1, ZR, ZP)
    water_tank(cati, -6.0, 24.2, ZR)
    water_tank(cati, 5.0, 24.2, ZR)
    for u in (0.75, 21.25):
        downpipe_flat(cati, F['arka'], u, ZP - 0.1)
    # doseme
    slab(dos, -14.78, 14.78, 0.0, y0)
    slab(dos, -14.78, x0 - 0.06, y0, y1)
    slab(dos, x1 + 0.06, 14.78, y0, y1)
    A.col('Doseme_On', -14.78, 14.78, 0.0, y0, TEMEL, DU)
    A.col('Doseme_Yan_Sol', -14.78, x0 - 0.06, y0, y1, TEMEL, DU)
    A.col('Doseme_Yan_Sag', x1 + 0.06, 14.78, y0, y1, TEMEL, DU)
    gates = [(-11.1, -5.1), (-0.1, 5.9)]
    front_fence_with_gates(cit, A, 30.0, gates)
    for sx in (-1, 1):
        c = sx * 14.88
        fence_run(cit, 'y', c, 0.22, y0)
        A.col(f'Cit_Yan_{"Sol" if sx < 0 else "Sag"}', c - 0.1, c + 0.1, 0.22, y0, 0.0, 1.84)
        solid_wall(cit, 'y', c, y0, 27.78, 2.0, -sx, coping=(y0, 27.76))
        A.col(f'Duvar_Yan_{"Sol" if sx < 0 else "Sag"}', c - 0.12, c + 0.12, y0, 27.78, 0.0, 2.06)
    solid_wall(cit, 'x', 27.88, -14.98, 14.98, 2.0, -1)
    A.col('Duvar_Arka', -14.98, 14.98, 27.76, 28.0, 0.0, 2.06)
    A.col('Palet_1', *pallet_stack(esya, 13.6, 25.1, 4, False))
    for x, y, m in ((-13.9, 25.3, 'TB_Varil_Mavi'), (-13.25, 25.35, 'TB_Varil_Mavi')):
        drum(esya, x, y, m)
    A.col('Variller', -14.21, -12.94, 24.99, 25.66, 0.0, 0.94)
    A.col('Bina', x0, x1, y0, y1, 0.0, ZP + 0.07)
    A.col('Sacak', x0 - 0.12, x1 + 0.12, y0 - 1.5, y0, 5.0, 5.22)
    A.foot.append(('Atolye_C_Bina', x0, x1, y0, y1, round(ZP + 0.07, 2)))
    A.gates += gates
    for x, y in ((-3.05, 7.5), (7.95, 7.5), (12.9, 17.0), (-12.9, 17.0)):
        A.spot(x, y, 0)
    A.trees += [(-6.0, 27.0), (6.0, 27.0)]
    A.checks = {'govde': [22.0, 14.0], 'kepenk_sayisi': 2}
    return A


# ===========================================================================
# 4) BENZINLIK
# ===========================================================================
def pompa(mb, cx, cy):
    z0 = DU + 0.2
    cbox(mb, cx - 0.31, cx + 0.31, cy - 0.57, cy + 0.57, z0 - 0.02, z0 + 0.18, 'TB_Trim', w=0.03)
    cbox(mb, cx - 0.25, cx + 0.25, cy - 0.5, cy + 0.5, z0 + 0.18, 1.85, 'TB_Beyaz', w=0.035)
    cbox(mb, cx - 0.28, cx + 0.28, cy - 0.53, cy + 0.53, 1.85, 2.24, 'TB_Petrol', w=0.03)
    cbox(mb, cx - 0.26, cx + 0.26, cy - 0.51, cy + 0.51, 2.24, 2.30, 'TB_Beyaz', w=0.02)
    for sx in (-1, 1):
        hx0, hx1 = sorted((cx + sx * 0.24, cx + sx * 0.37))
        cbox(mb, hx0, hx1, cy + 0.16, cy + 0.38, 1.02, 1.34, 'TB_Trim', w=0.02, wb=0.02, bottom=True)
        pts = [Vector((cx + sx * 0.26, cy - 0.32, 1.95))]
        for i in range(9):
            t = i / 8
            pts.append(Vector((cx + sx * 0.33, cy - 0.32 + 0.6 * t, 1.92 + (1.22 - 1.92) * t - 0.95 * math.sin(math.pi * t))))
        tube(mb, pts, 0.034, 6, 'TB_Kaucuk', (1, 0, 0))


def kanopi(mb, x0, x1, y0, y1, zb=5.2, zt=6.2, ch=0.3):
    outer = oct_rect(x0, x1, y0, y1, ch)
    for za, zz, m in ((zb, 5.53, 'TB_Beyaz'), (5.53, 5.83, 'TB_Petrol'), (5.83, zt, 'TB_Beyaz')):
        prism(mb, outer, za, zz, m, top=False)
    mb.poly([(x, y, zb) for x, y in outer], 'TB_Beyaz', (0, 0, -1))
    inner = oct_rect(x0 + 0.15, x1 - 0.15, y0 + 0.15, y1 - 0.15, ch * 0.55)
    zd = zt - 0.14
    n = len(outer)
    for i in range(n):
        j = (i + 1) % n
        mb.poly([(*outer[i], zt), (*outer[j], zt), (*inner[j], zt), (*inner[i], zt)], 'TB_Beyaz', (0, 0, 1))
        dx = inner[j][0] - inner[i][0]
        dy = inner[j][1] - inner[i][1]
        mb.poly([(*inner[i], zd), (*inner[j], zd), (*inner[j], zt), (*inner[i], zt)], 'TB_Beyaz', (-dy, dx, 0))
    mb.poly([(x, y, zd) for x, y in inner], 'TB_Cati_Membran', (0, 0, 1))


def benzinlik():
    A = Asset('Benzinlik', 36.0, 30.0, 10000)
    kan, lamba, ada, mkt, mcati, dos, pilon, hs = (A.mb('Kanopi'), A.mb('Kanopi_Lamba'), A.mb('Pompa_Adalari'),
                                                  A.mb('Market'), A.mb('Market_Cati'), A.mb('Doseme'), A.mb('Pilon'),
                                                  A.mb('Hava_Su'))
    H = 18.0
    # --- doseme: kanopi alti beton, kalani asfalt
    mx0, mx1, my0, my1 = -6.0, 6.0, 21.5, 29.5
    slab(dos, -10.0, 10.0, 6.0, 18.0, 'TB_Beton')
    regions = [(-H, H, 0.0, 6.0), (-H, -10.0, 6.0, 18.0), (10.0, H, 6.0, 18.0), (-H, mx0 - 0.5, 18.0, 30.0),
               (mx1 + 0.5, H, 18.0, 30.0), (mx0 - 0.5, mx1 + 0.5, 18.0, 20.3), (mx0 - 0.5, mx1 + 0.5, my1, 30.0),
               (mx0 - 0.5, mx0, 20.3, my1), (mx1, mx1 + 0.5, 20.3, my1)]
    for i, (a, b, c, d) in enumerate(regions):
        slab(dos, a, b, c, d, 'TB_Asfalt')
        A.col(f'Doseme_{i + 1}', a, b, c, d, TEMEL, DU)
    A.col('Doseme_Beton', -10.0, 10.0, 6.0, 18.0, TEMEL, DU)
    # kaldirim (market onu)
    cbox(dos, mx0 - 0.5, mx1 + 0.5, 20.3, my0, TEMEL, DU + 0.15, 'TB_Kaldirim', w=0.03)
    A.col('Kaldirim', mx0 - 0.5, mx1 + 0.5, 20.3, my0, 0.0, DU + 0.15)
    # on adalar (cimlik) ve kose tarhlari
    planter(dos, A, 'Orta', -6.5, 6.5, 0.3, 3.0)
    planter(dos, A, 'Kose_Sol', -17.95, -15.5, 0.3, 3.0)
    planter(dos, A, 'Kose_Sag', 15.5, 17.95, 0.3, 3.0)
    # cevre bordur (yan ve arka)
    for sx in (-1, 1):
        a, b = sorted((sx * 17.95, sx * 17.75))
        cbox(dos, a, b, 3.0, 29.75, -0.2, DU + 0.15, 'TB_Bordur', w=0.02)
        A.col(f'Bordur_{"Sol" if sx < 0 else "Sag"}', a, b, 3.0, 29.75, 0.0, DU + 0.15)
    cbox(dos, -17.95, 17.95, 29.75, 29.95, -0.2, DU + 0.15, 'TB_Bordur', w=0.02)
    A.col('Bordur_Arka', -17.95, 17.95, 29.75, 29.95, 0.0, DU + 0.15)
    # park cizgileri (market yanlari)
    for x in (7.0, 9.5, 12.0, 14.5, -7.0, -9.5, -12.0):
        paint_line(dos, x - 0.05, x + 0.05, 22.0, 27.0)
    # --- kanopi
    kx0, kx1, ky0, ky1 = -9.0, 9.0, 7.0, 17.0
    kanopi(kan, kx0, kx1, ky0, ky1)
    A.col('Kanopi', kx0, kx1, ky0, ky1, 5.2, 6.2)
    for lx in (-6.0, 0.0, 6.0):
        for ly in (9.4, 14.6):
            box(lamba, lx - 0.8, lx + 0.8, ly - 0.3, ly + 0.3, 5.185, 5.205, 'TB_Lamba', skip=('+z',))
    # --- pompa adalari
    for i, ix in enumerate((-3.6, 3.6)):
        poly = oct_rect(ix - 0.65, ix + 0.65, 8.5, 15.5, 0.35)
        prism(ada, poly, 0.0, DU + 0.2, 'TB_Bordur')
        for py in (9.2, 14.8):
            cbox(ada, ix - 0.25, ix + 0.25, py - 0.25, py + 0.25, DU + 0.2, 5.21, 'TB_Beyaz', w=0.04)
            cbox(ada, ix - 0.29, ix + 0.29, py - 0.29, py + 0.29, DU + 0.2, 0.95, 'TB_Trim', w=0.03)
            A.col(f'Kolon_{i * 2 + (1 if py < 12 else 2)}', ix - 0.29, ix + 0.29, py - 0.29, py + 0.29, 0.0, 5.2)
        for py in (10.5, 13.5):
            pompa(ada, ix, py)
        A.col(f'Ada_Pompa_{i + 1}', ix - 0.65, ix + 0.65, 8.5, 15.5, 0.0, 2.31)
    # --- market binasi
    F = frames(mx0, mx1, my0, my1)
    ZP = 4.0
    v_on = [Op(0.45, 7.3, 0.45, 2.85, 'vitrin'), Op(7.5, 9.4, DU + 0.15, 2.6, 'kapi'), Op(9.6, 11.55, 0.45, 2.85, 'vitrin')]
    ops = {'on': v_on, 'sag': [Op(3.0, 5.0, 1.3, 2.3, 'pencere')], 'sol': [Op(5.6, 6.5, DU, DU + 2.1, 'kapi')],
           'arka': [Op(2.5, 3.4, DU, DU + 2.1, 'kapi')]}
    for k, fr in F.items():
        facade_wall(mkt, fr, TEMEL, ZP, ops[k], 'TB_Siva')
        plinth(mkt, fr, ops[k], 0.40, -0.04 if k in ('on', 'arka') else 0.0, fr.L + (0.04 if k in ('on', 'arka') else 0.0),
               out=0.04, mat='TB_Trim')
    fr = F['on']
    for o in v_on:
        if o.kind == 'vitrin':
            vitrin(mkt, fr, o, 'TB_Siva', step=1.4)
        else:
            camkapi_otomatik(mkt, fr, o, 'TB_Siva')
    pencere(mkt, F['sag'], ops['sag'][0], 'TB_Siva', sill_mat='TB_Beton_Boya', mull=[4.0])
    kapi(mkt, F['sol'], ops['sol'][0], 'TB_Siva')
    kapi(mkt, F['arka'], ops['arka'][0], 'TB_Siva')
    # on alin bandi: beyaz + petrol serit (kanopiyle ayni dil)
    for za, zz, m in ((3.0, 3.36, 'TB_Beyaz'), (3.36, 3.62, 'TB_Petrol'), (3.62, 4.2, 'TB_Beyaz')):
        fbox(mkt, fr, -0.3, fr.L + 0.3, za, zz, -0.32, 0.02, m)
    flat_roof(mcati, mx0, mx1, my0, my1, 3.6, ZP, inset=0.2, coping_mat='TB_Beyaz')
    roof_ac(mcati, 2.5, 27.0, 3.7)
    downpipe_flat(mcati, F['arka'], 0.6, ZP - 0.1)
    downpipe_flat(mcati, F['arka'], 11.4, ZP - 0.1)
    A.col('Market', mx0, mx1, my0, my1, 0.0, 4.2)
    A.col('Market_Alin', mx0 - 0.3, mx1 + 0.3, my0 - 0.32, my0, 3.0, 4.2)
    A.foot.append(('Benzinlik_Market', mx0, mx1, my0, my1, 4.2))
    A.foot.append(('Benzinlik_Kanopi', kx0, kx1, ky0, ky1, 6.2))
    # --- fiyat pilonu (bos paneller)
    px, py = 4.3, 1.65
    cbox(pilon, px - 0.36, px + 0.36, py - 1.0, py + 1.0, TEMEL, 0.6, 'TB_Beton_Boya', w=0.03)
    cbox(pilon, px - 0.2, px + 0.2, py - 0.85, py + 0.85, 0.6, 6.0, 'TB_Beyaz', w=0.04)
    cbox(pilon, px - 0.225, px + 0.225, py - 0.875, py + 0.875, 5.05, 5.75, 'TB_Petrol', w=0.03)
    for k in range(4):
        z0 = 1.45 + k * 0.84
        for sx in (-1, 1):
            a, b = sorted((px + sx * 0.19, px + sx * 0.225))
            box(pilon, a, b, py - 0.7, py + 0.7, z0, z0 + 0.64, 'TB_Trim')
    A.col('Pilon', px - 0.36, px + 0.36, py - 1.0, py + 1.0, 0.0, 6.0)
    # --- hava-su diregi (sol arka kose)
    hx, hy = -16.1, 26.0
    cbox(hs, hx - 0.8, hx + 0.8, hy - 0.8, hy + 0.8, -0.2, DU + 0.15, 'TB_Bordur', w=0.02)
    cbox(hs, hx - 0.17, hx + 0.17, hy - 0.17, hy + 0.17, DU + 0.15, 1.5, 'TB_Trim_Mavi', w=0.03)
    cbox(hs, hx - 0.23, hx + 0.23, hy - 0.23, hy + 0.23, 1.5, 1.68, 'TB_Beyaz', w=0.03)
    cyl(hs, (hx + 0.17, hy, 1.05), (1, 0, 0), 0.14, 0.07, 10, 'TB_Kaucuk')
    cyl(hs, (hx, hy - 0.17, 1.05), (0, -1, 0), 0.14, 0.07, 10, 'TB_Kaucuk')
    A.col('Hava_Su', hx - 0.8, hx + 0.8, hy - 0.8, hy + 0.8, 0.0, 1.68)
    # --- veriler
    A.gates += [(-15.5, -7.5), (7.5, 15.5)]
    for x in (-5.85, -1.35, 1.35, 5.85):
        A.spot(x, 12.0, 0)
    for x in (8.25, 10.75, 13.25, -8.25, -10.75):
        A.spot(x, 24.5, 0)
    A.trees += [(-16.7, 1.65), (16.7, 1.65), (-2.0, 1.65)]
    A.checks = {'kanopi': [18.0, 10.0], 'kanopi_alt': 5.2, 'market': [12.0, 8.0], 'kolon_sayisi': 4, 'ada_sayisi': 2,
                'pompa_sayisi': 4}
    return A


def vitrin(mb, fr, o, wall_mat, d=0.1, depth=0.3, step=1.4):
    """Yari saydam vitrin + 0,3 m iceride koyu ic yuzey (tavan acik, raf bantli arka yuzey)."""
    back = d + depth
    reveal(mb, fr, o, [(0.0, d, wall_mat), (d, back, 'TB_Ic_Koyu', 'TB_Ic_Acik', 'TB_Ic_Koyu')])
    h = o.v1 - o.v0
    bands = [(o.v0 + h * 0.18, o.v0 + h * 0.24, 'TB_Ic_Raf'), (o.v0 + h * 0.46, o.v0 + h * 0.52, 'TB_Ic_Raf'),
             (o.v0 + h * 0.74, o.v0 + h * 0.80, 'TB_Ic_Raf')]
    zs = sorted({o.v0, o.v1} | {b[0] for b in bands} | {b[1] for b in bands})
    for za, zz in zip(zs, zs[1:]):
        m = 'TB_Ic_Koyu'
        for b in bands:
            if b[0] <= za < b[1]:
                m = b[2]
        fquad(mb, fr, o.u0, o.u1, za, zz, back, m)
    fquad(mb, fr, o.u0, o.u1, o.v0, o.v1, d, 'TB_Cam_Vitrin')
    fw = 0.06
    a, b = d - 0.05, d + 0.03
    fbox(mb, fr, o.u0, o.u0 + fw, o.v0, o.v1, a, b, 'TB_Trim')
    fbox(mb, fr, o.u1 - fw, o.u1, o.v0, o.v1, a, b, 'TB_Trim')
    fbox(mb, fr, o.u0 + fw, o.u1 - fw, o.v1 - fw, o.v1, a, b, 'TB_Trim')
    fbox(mb, fr, o.u0 + fw, o.u1 - fw, o.v0, o.v0 + 0.08, a, b, 'TB_Trim')
    n = max(1, round((o.u1 - o.u0) / step))
    for i in range(1, n):
        u = o.u0 + (o.u1 - o.u0) * i / n
        fbox(mb, fr, u - 0.035, u + 0.035, o.v0 + 0.08, o.v1 - fw, a + 0.005, b, 'TB_Trim')
    fbox(mb, fr, o.u0 - 0.03, o.u1 + 0.03, o.v0 - 0.05, o.v0, -0.06, 0.0, 'TB_Beton_Boya', w=0.012)


def camkapi_otomatik(mb, fr, o, wall_mat, d=0.1, depth=0.45):
    """Otomatik surgulu cam kapi boslugu: iki cam kanat + ust sabit cam, arkasinda koyu ruzgarlik."""
    back = d + depth
    reveal(mb, fr, o, [(0.0, d, wall_mat), (d, back, 'TB_Ic_Koyu', 'TB_Ic_Acik', 'TB_Kaldirim')])
    fquad(mb, fr, o.u0, o.u1, o.v0, o.v1, back, 'TB_Ic_Koyu')
    fquad(mb, fr, o.u0, o.u1, o.v0, o.v1, d, 'TB_Cam_Vitrin')
    a, b = d - 0.05, d + 0.03
    fw = 0.07
    um = (o.u0 + o.u1) / 2
    zt = o.v1 - 0.35
    for ua, ub in ((o.u0, o.u0 + fw), (o.u1 - fw, o.u1), (um - 0.035, um + 0.035)):
        fbox(mb, fr, ua, ub, o.v0, zt, a, b, 'TB_Trim')
    fbox(mb, fr, o.u0, o.u1, o.v1 - fw, o.v1, a, b, 'TB_Trim')
    fbox(mb, fr, o.u0, o.u1, zt - 0.03, zt + 0.06, a - 0.04, b, 'TB_Trim')   # mekanizma kutusu
    fbox(mb, fr, o.u0 + fw, o.u1 - fw, o.v0, o.v0 + 0.06, a, b, 'TB_Trim')
    fbox(mb, fr, o.u0, o.u0 + fw, zt, o.v1, a, b, 'TB_Trim')
    fbox(mb, fr, o.u1 - fw, o.u1, zt, o.v1, a, b, 'TB_Trim')


# ===========================================================================
# 5) MARKET
# ===========================================================================
def cart_row(mb, x, y0, n=4):
    """Ic ice gecmis alisveris arabalari (on -Y, uzunluk +Y)."""
    L = 0.95 + 0.26 * (n - 1)
    y1 = y0 + L
    P = {}
    for i, xx in enumerate((-1, 1)):
        for j, yy in enumerate((y0, y1)):
            P[i, j, 0] = (x + xx * 0.21, yy + (0.08 if j == 0 else -0.04), 0.5)
            P[i, j, 1] = (x + xx * 0.28, yy, 0.98)
    hexa(mb, P, 'TB_Galvaniz', skip=('-k',))
    box(mb, x - 0.2, x + 0.2, y0 + 0.08, y1 - 0.12, 0.13, 0.17, 'TB_Galvaniz')
    for k in range(n):
        box(mb, x - 0.27, x + 0.27, y1 - 0.26 * k + 0.02, y1 - 0.26 * k + 0.07, 1.0, 1.05, 'TB_Plastik')
    for yy in (y0 + 0.12, y1 - 0.16):
        for xx in (-0.17, 0.17):
            box(mb, x + xx - 0.03, x + xx + 0.03, yy - 0.05, yy + 0.05, DU, 0.13, 'TB_Kaucuk')
    for xx in (-0.17, 0.17):
        box(mb, x + xx - 0.015, x + xx + 0.015, y1 - 0.17, y1 - 0.14, 0.13, 0.5, 'TB_Galvaniz')


def konteyner(mb, x, y):
    P = {}
    for i, sx in enumerate((-1, 1)):
        for j, sy in enumerate((-1, 1)):
            P[i, j, 0] = (x + sx * 0.6, y + sy * 0.47, 0.2)
            P[i, j, 1] = (x + sx * 0.68, y + sy * 0.55, 1.18)
    hexa(mb, P, 'TB_Konteyner')
    Q = {}
    for i, sx in enumerate((-1, 1)):
        for j, sy in enumerate((-1, 1)):
            zt = 1.27 if sy < 0 else 1.36
            Q[i, j, 0] = (x + sx * 0.71, y + sy * 0.6, zt - 0.07)
            Q[i, j, 1] = (x + sx * 0.71, y + sy * 0.6, zt)
    hexa(mb, Q, 'TB_Trim')
    box(mb, x - 0.66, x + 0.66, y - 0.56, y - 0.5, 1.12, 1.18, 'TB_Trim')
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(mb, x + sx * 0.5 - 0.05, x + sx * 0.5 + 0.05, y + sy * 0.37 - 0.05, y + sy * 0.37 + 0.05, DU, 0.2, 'TB_Kaucuk')


def market():
    A = Asset('Market', 30.0, 32.0, 8000)
    bina, cati, dos, sund, kont = A.mb('Bina'), A.mb('Cati'), A.mb('Doseme'), A.mb('Araba_Sundurmasi'), A.mb('Konteyner_Alani')
    x0, x1, y0, y1 = -10.0, 10.0, 17.7, 31.7
    ZR, ZP = 4.0, 4.5
    F = frames(x0, x1, y0, y1)
    SW = DU + 0.15
    v1 = Op(0.8, 11.6, 0.45, 3.0, 'vitrin')
    dr = Op(11.8, 14.2, SW, 3.0, 'kapi')
    v2 = Op(14.4, 17.6, 0.45, 3.0, 'vitrin')
    ops = {'on': [v1, dr, v2],
           'sag': [Op(2.2, 3.2, DU, DU + 2.1, 'kapi'), Op(4.5, 7.5, DU, DU + 3.2, 'kepenk', tw=0.02)],
           'sol': [Op(3.0, 4.0, DU, DU + 2.1, 'kapi'), Op(6.0, 8.0, 2.4, 3.2, 'pencere'), Op(9.5, 11.5, 2.4, 3.2, 'pencere')],
           'arka': []}
    for k, fr in F.items():
        facade_wall(bina, fr, TEMEL, ZP, ops[k], 'TB_Siva')
        plinth(bina, fr, ops[k], 0.40, -0.04 if k in ('on', 'arka') else 0.0, fr.L + (0.04 if k in ('on', 'arka') else 0.0),
               out=0.04, mat='TB_Beton_Boya')
        for o in ops[k]:
            if o.kind == 'pencere':
                pencere(bina, fr, o, 'TB_Siva', sill_mat='TB_Beton_Boya', mull=[(o.u0 + o.u1) / 2])
            elif o.kind == 'kapi' and k != 'on':
                kapi(bina, fr, o, 'TB_Siva', step=(k in ('arka', 'sol')))
        if k != 'on':
            fbox(bina, fr, 0.4, fr.L - 0.4, 3.7, 4.0, -0.04, 0.02, 'TB_Beton_Boya', w=0.015)
    fr = F['on']
    vitrin(bina, fr, v1, 'TB_Siva', step=1.36)
    vitrin(bina, fr, v2, 'TB_Siva', step=1.07)
    camkapi_otomatik(bina, fr, dr, 'TB_Siva')
    kc, piv = kepenk(bina, F['sag'], ops['sag'][1], 'Market_Kepenk_1', 'TB_Siva')
    A.add(kc, piv)
    A.moving.append({'ad': 'Market_Kepenk_1', 'tur': 'sarmal_kepenk', 'genislik': 3.0, 'yukseklik': 3.2})
    # kose kolonlari
    for cx, cy in ((x0, y0), (x1, y0), (x1, y1), (x0, y1)):
        sx = -1 if cx == x0 else 1
        sy = -1 if cy == y0 else 1
        xa, xb = sorted((cx + sx * 0.06, cx - sx * 0.4))
        ya, yb = sorted((cy + sy * 0.06, cy - sy * 0.4))
        cbox(bina, xa, xb, ya, yb, TEMEL, ZP, 'TB_Beton_Boya', w=0.02)
    # sacak + tabela bandi
    fbox(bina, fr, 0.45, 18.0, 3.15, 3.33, -1.3, 0.02, 'TB_Trim', w=0.03)
    fbox(bina, fr, 0.45, 18.0, 3.46, 4.36, -0.13, 0.02, 'TB_Tabela', w=0.03)
    fbox(bina, fr, 0.4, 18.05, 3.4, 3.46, -0.15, 0.02, 'TB_Trim')
    fbox(bina, fr, 0.4, 18.05, 4.36, 4.42, -0.15, 0.02, 'TB_Trim')
    # giris ustunde yukseltilmis parapet (silueti kirar)
    fbox(bina, fr, 10.3, 15.7, ZP - 0.2, ZP + 0.85, -0.14, 0.3, 'TB_Siva', w=0.03)
    fbox(bina, fr, 10.22, 15.78, ZP + 0.85, ZP + 0.93, -0.2, 0.36, 'TB_Beton_Boya', w=0.015)
    # cati
    flat_roof(cati, x0, x1, y0, y1, ZR, ZP)
    roof_ac(cati, -4.0, 27.5, ZR + 0.1)
    roof_ac(cati, 1.5, 27.5, ZR + 0.1)
    cbox(cati, 5.5, 6.4, 24.0, 24.9, ZR, ZR + 0.45, 'TB_Trim', w=0.02)
    for u in (0.8, 13.2):
        downpipe_flat(cati, F['sol'], u, ZP - 0.1)
    # doseme, kaldirim, tarhlar
    slab(dos, -15.0, 15.0, 0.0, 16.5, 'TB_Asfalt')
    slab(dos, x1 + 0.06, 15.0, 16.5, 32.0, 'TB_Asfalt')
    A.col('Doseme_On', -15.0, 15.0, 0.0, 16.5, TEMEL, DU)
    A.col('Doseme_Yan', x1 + 0.06, 15.0, 16.5, 32.0, TEMEL, DU)
    cbox(dos, -10.6, x1 + 0.06, 16.5, y0, TEMEL, SW, 'TB_Kaldirim', w=0.03)
    A.col('Kaldirim', -10.6, x1 + 0.06, 16.5, y0, 0.0, SW)
    planter(dos, A, 'On_Serit', -15.0, -4.5, 0.0, 0.8)
    planter(dos, A, 'On_Tarh', -4.5, 6.0, 0.0, 5.8)
    planter(dos, A, 'Kose', 14.0, 15.0, 0.0, 0.8)
    # park cizgileri
    for x in (-9.5, -7.0, -4.5, -2.0, 0.5, 3.0, 5.5):
        paint_line(dos, x - 0.05, x + 0.05, 11.6, 16.4)
    for x in (-14.5, -12.0, -9.5, -7.0, -4.5):
        paint_line(dos, x - 0.05, x + 0.05, 0.9, 5.8)
    paint_line(dos, -9.5, 5.5, 11.5, 11.6)
    # alisveris arabasi sundurmasi
    sx0, sx1, sy0, sy1 = 6.4, 9.6, 12.6, 16.2
    for px in (sx0, sx1 - 0.08):
        for py in (sy0, sy1 - 0.08):
            box(sund, px, px + 0.08, py, py + 0.08, DU, 2.3, 'TB_Galvaniz', skip=('-z',))
    cbox(sund, sx0 - 0.12, sx1 + 0.12, sy0 - 0.15, sy1 + 0.1, 2.3, 2.4, 'TB_Trim', w=0.02, wb=0.02, bottom=True)
    box(sund, sx0 + 0.08, sx1 - 0.08, sy1 - 0.06, sy1 - 0.03, 0.35, 2.2, 'TB_Galvaniz')
    for px in (sx0 + 0.02, sx1 - 0.06):
        box(sund, px, px + 0.03, sy0 + 0.08, sy1 - 0.08, 0.9, 2.2, 'TB_Galvaniz')
    cart_row(sund, 7.4, 13.0, 4)
    cart_row(sund, 8.6, 13.4, 3)
    A.col('Araba_Sundurmasi', sx0 - 0.12, sx1 + 0.12, sy0 - 0.15, sy1 + 0.1, 0.0, 2.4)
    # cop konteyner alani (sag arka kose)
    cx0, cx1, cy0, cy1 = 11.0, 14.8, 27.6, 31.8
    cbox(kont, cx0, cx1, cy1 - 0.2, cy1, TEMEL, 1.5, 'TB_Beton_Duvar', w=0.015)
    cbox(kont, cx0, cx0 + 0.2, cy0, cy1 - 0.2, TEMEL, 1.5, 'TB_Beton_Duvar', w=0.015)
    cbox(kont, cx1 - 0.2, cx1, cy0, cy1 - 0.2, TEMEL, 1.5, 'TB_Beton_Duvar', w=0.015)
    for a, b, c, d in ((cx0 - 0.02, cx1, cy1 - 0.22, cy1), (cx0 - 0.02, cx0 + 0.22, cy0, cy1 - 0.22), (cx1 - 0.22, cx1, cy0, cy1 - 0.22)):
        cbox(kont, a, b, c, d, 1.5, 1.56, 'TB_Beton_Boya', w=0.01)
    konteyner(kont, 12.05, 30.55)
    konteyner(kont, 13.7, 30.55)
    A.col('Konteyner_Duvar_Arka', cx0, cx1, cy1 - 0.22, cy1, 0.0, 1.56)
    A.col('Konteyner_Duvar_Sol', cx0 - 0.02, cx0 + 0.22, cy0, cy1 - 0.22, 0.0, 1.56)
    A.col('Konteyner_Duvar_Sag', cx1 - 0.22, cx1, cy0, cy1 - 0.22, 0.0, 1.56)
    A.col('Konteynerler', 11.3, 14.45, 29.9, 31.2, 0.0, 1.4)
    A.col('Bina', x0, x1, y0, y1, 0.0, ZP)
    A.col('Sacak', x0 + 0.45, x0 + 18.0, y0 - 1.3, y0, 3.15, 3.33)
    A.col('Giris_Parapeti', x0 + 10.22, x0 + 15.78, y0 - 0.2, y0 + 0.36, ZP - 0.2, ZP + 0.93)
    A.foot.append(('Market_Bina', x0, x1, y0, y1, ZP))
    A.gates.append((6.0, 14.0))
    for x in (-8.25, -5.75, -3.25, -0.75, 1.75, 4.25):
        A.spot(x, 14.0, 0)
    for x in (-13.25, -10.75, -8.25, -5.75):
        A.spot(x, 3.3, 180)
    A.trees += [(-1.0, 3.0), (3.5, 3.0), (-12.6, 20.5), (-12.6, 27.5)]
    A.checks = {'govde': [20.0, 14.0], 'yukseklik': 4.5, 'park_aralik': [8, 10]}
    return A


# ===========================================================================
# Uretim, disa aktarim, manifest
# ===========================================================================
def u3(x, y, z=0.0):
    return [round(x, 3), round(z, 3), round(y, 3)]


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0
    build_materials()
    OUT.mkdir(parents=True, exist_ok=True)
    BLEND.parent.mkdir(parents=True, exist_ok=True)
    assets = [atolye_a(), atolye_b(), atolye_c(), benzinlik(), market()]
    manifest = {"set": SET, "surum": 1,
                "eksen": "Unity yerel: x = Blender X, y = yukari (Blender Z), z = Blender Y. On cephe ve sokak -z yonunde.",
                "orijin": "Parselin sokak tarafindaki on kenarinin ortasi, zemin kotu 0.",
                "doseme_ust_kotu": DU, "temel_alt_kotu": TEMEL,
                "col_kurali": "COL_ onekli eksen hizali kutular BoxCollider olur ve gizlenir. COL_*_Doseme_* kutulari surulebilir doseme (ust 0,04).",
                "varliklar": []}
    for A in assets:
        coll = bpy.data.collections.new(A.id)
        scene.collection.children.link(coll)
        objs = []
        for m in A.mbs:
            if m.f:
                objs.append(make_object(m, coll))
        for m, org in A.extra:
            objs.append(make_object(m, coll, org))
        cols = [make_col(coll, *c) for c in A.cols]
        bpy.context.view_layer.update()
        vis_tris = sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons)
        col_tris = sum(len(p.vertices) - 2 for o in cols for p in o.data.polygons)
        mn = Vector((1e9, 1e9, 1e9))
        mx = Vector((-1e9, -1e9, -1e9))
        for o in objs:
            for v in o.data.vertices:
                w = o.matrix_world @ v.co
                mn = Vector((min(mn[i], w[i]) for i in range(3)))
                mx = Vector((max(mx[i], w[i]) for i in range(3)))
        print(f'{A.id}: {vis_tris} gorsel ucgen (+{col_tris} COL), butce {A.budget}, '
              f'X {mn.x:.2f}..{mx.x:.2f}  Y {mn.y:.2f}..{mx.y:.2f}  Z {mn.z:.2f}..{mx.z:.2f}')
        for o in objs:
            print(f'   {o.name}: {sum(len(p.vertices) - 2 for p in o.data.polygons)}')
        entry = {
            "id": A.id, "fbx": f"{A.id}.fbx",
            "boyut": [round(mx.x - mn.x, 2), round(mx.z, 2), round(mx.y - mn.y, 2)],
            "boyut_notu": "[X genislik, zemin ustu yukseklik, Z derinlik]; temeller -0,6'ya iner",
            "ucgen": vis_tris, "col_ucgen": col_tris, "ucgen_butcesi": A.budget,
            "parsel": [A.G, A.D],
            "parsel_sinirlari": {"x": [-A.G / 2, A.G / 2], "z": [0.0, A.D]},
            "bina_izleri": [{"ad": n, "min": u3(a, c, 0.0), "max": u3(b, d, h), "boyut": [round(b - a, 2), h, round(d - c, 2)]}
                            for n, a, b, c, d, h in A.foot],
            "giris_kesimleri": [[round(a, 2), round(b, 2)] for a, b in A.gates],
            "park_yerleri": [{"konum": u3(x, y, DU), "yon_derece": yaw, "boyut": [2.5, 5.0]} for x, y, yaw in A.park],
            "agac_noktalari": [u3(x, y, 0.0) for x, y in A.trees],
            "hareketli": A.moving,
            "nesneler": sorted(o.name for o in objs),
            "col_kutulari": sorted(o.name for o in cols),
            "hedef_olculer": A.checks,
        }
        manifest["varliklar"].append(entry)
        if not NO_EXPORT:
            bpy.ops.object.select_all(action='DESELECT')
            for o in objs + cols:
                o.select_set(True)
            bpy.context.view_layer.objects.active = objs[0]
            bpy.ops.export_scene.fbx(filepath=str(OUT / f"{A.id}.fbx"), use_selection=True, object_types={'MESH', 'EMPTY'},
                                     axis_forward='Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                                     use_space_transform=True, bake_space_transform=True, use_mesh_modifiers=True,
                                     mesh_smooth_type='FACE', use_triangles=True, add_leaf_bones=False, bake_anim=False,
                                     path_mode='AUTO')
    mats = {"materials": [], "palettes": PALETTES}
    for name, col, met, smo, emi, alpha, rnd, desc in MATS:
        e = {"name": name, "color": list(col), "metallic": met, "smoothness": smo,
             "emission": list(emi) if emi else [0, 0, 0], "alpha": alpha}
        if rnd:
            e["randomize"] = rnd
        e["aciklama"] = desc
        mats["materials"].append(e)
    if not NO_EXPORT:
        (OUT / f"{SET}_manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
        (OUT / f"{SET}_malzemeler.json").write_text(json.dumps(mats, ensure_ascii=False, indent=2), encoding='utf-8')
    bpy.ops.object.select_all(action='DESELECT')
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    print('TAMAM', 'disa aktarim yok' if NO_EXPORT else str(OUT))


main()
