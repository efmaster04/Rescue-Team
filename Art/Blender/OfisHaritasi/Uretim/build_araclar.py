# -*- coding: utf-8 -*-
"""Ofis haritasi trafik araclari (set: Araclar).

Tek betik butun seti sifirdan uretir:
  blender -b -P build_araclar.py -- [--no-export] [--only=Hatchback,Sedan]

Eksen: on = Blender +Y (Unity +Z), sag = +X, yukari = +Z. Orijin taban izinin
ortasi, lastik alti Z=0. Govde tek mesh, dort teker ayri (orijin teker merkezinde,
kendi X ekseni etrafinda donel simetrik), COL_ kutulari eksenlere hizali.
Stil: servis minibusu (build_van.py) dili - duz golgeleme, tek segment pah,
donuk renkler, yazi/plaka/logo yok.
"""
import bpy
import bmesh
import math
import json
import sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
NO_EXPORT = '--no-export' in ARGS
ONLY = []
for a in ARGS:
    if a.startswith('--only='):
        ONLY = [s.strip() for s in a.split('=', 1)[1].split(',') if s.strip()]

ROOT = Path(r'C:\Users\uur\RescueTeam_OfisHaritasi')
OUT = ROOT / 'Oyun' / 'Assets' / 'OfisHaritasi' / 'Modeller' / 'Araclar'
BLEND = ROOT / 'Kaynak' / 'Blender' / 'Araclar.blend'

bpy.ops.wm.read_factory_settings(use_empty=True)
SCENE = bpy.context.scene
SCENE.unit_settings.system = 'METRIC'
SCENE.unit_settings.scale_length = 1.0

# ---------------------------------------------------------------------------
# Malzemeler (sRGB renkler; Blender'a dogrusal olarak yazilir)
# ---------------------------------------------------------------------------
PALETTES = {
    'arac_boya': [
        [0.86, 0.86, 0.84],   # beyaz
        [0.82, 0.80, 0.74],   # kirik beyaz
        [0.64, 0.65, 0.66],   # gumus
        [0.35, 0.36, 0.38],   # fume
        [0.10, 0.10, 0.11],   # siyah
        [0.14, 0.18, 0.29],   # lacivert
        [0.47, 0.12, 0.11],   # koyu kirmizi
        [0.72, 0.66, 0.54],   # bej
        [0.18, 0.28, 0.22],   # koyu yesil
        [0.54, 0.60, 0.65],   # acik mavi-gri
        [0.36, 0.11, 0.14],   # bordo
        [0.64, 0.58, 0.46],   # kum
    ],
    'kamyon_kasa': [
        [0.84, 0.84, 0.82],   # beyaz
        [0.66, 0.67, 0.67],   # acik gri
        [0.40, 0.47, 0.55],   # mavi-gri
        [0.80, 0.76, 0.65],   # krem
    ],
    'traktor_boya': [
        [0.55, 0.15, 0.11],   # donuk kirmizi
        [0.17, 0.30, 0.48],   # donuk mavi
        [0.22, 0.36, 0.19],   # donuk yesil
    ],
}

# ad: (renk sRGB, metallic, smoothness, emission sRGB, randomize)
MAT_DEFS = [
    ('AR_Boya',         (0.64, 0.65, 0.66), 0.25, 0.55, (0, 0, 0), 'arac_boya'),
    ('AR_Cam',          (0.10, 0.11, 0.12), 0.10, 0.82, (0, 0, 0), None),
    ('AR_Trim',         (0.085, 0.085, 0.09), 0.0, 0.32, (0, 0, 0), None),
    ('AR_Krom',         (0.68, 0.68, 0.67), 0.85, 0.72, (0, 0, 0), None),
    ('AR_Far',          (0.93, 0.90, 0.80), 0.0, 0.80, (0.30, 0.27, 0.20), None),
    ('AR_Stop',         (0.50, 0.055, 0.045), 0.0, 0.70, (0, 0, 0), None),
    ('AR_Sinyal',       (0.86, 0.45, 0.10), 0.0, 0.70, (0, 0, 0), None),
    ('AR_Lastik',       (0.065, 0.065, 0.065), 0.0, 0.12, (0, 0, 0), None),
    ('AR_Jant',         (0.55, 0.56, 0.56), 0.55, 0.50, (0, 0, 0), None),
    ('AR_Sasi',         (0.13, 0.13, 0.13), 0.30, 0.28, (0, 0, 0), None),
    ('AR_Taksi_Sari',   (0.80, 0.62, 0.17), 0.20, 0.55, (0, 0, 0), None),
    ('AR_Taksi_Lamba',  (0.88, 0.85, 0.72), 0.0, 0.60, (0.10, 0.09, 0.06), None),
    ('AR_Kasa',         (0.84, 0.84, 0.82), 0.10, 0.40, (0, 0, 0), 'kamyon_kasa'),
    ('AR_Kasa_Ici',     (0.22, 0.21, 0.20), 0.0, 0.18, (0, 0, 0), None),
    ('AR_Traktor_Boya', (0.55, 0.15, 0.11), 0.15, 0.45, (0, 0, 0), 'traktor_boya'),
]
MAT_ORDER = [m[0] for m in MAT_DEFS]
MAT_IDX = {n: i for i, n in enumerate(MAT_ORDER)}


def srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


MAT = {}
for name, col, met, smo, em, rnd in MAT_DEFS:
    m = bpy.data.materials.new(name)
    lin = tuple(srgb_to_lin(c) for c in col)
    m.diffuse_color = (*lin, 1.0)
    m.metallic = met
    m.roughness = 1.0 - smo
    m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*lin, 1.0)
    bs.inputs['Metallic'].default_value = met
    bs.inputs['Roughness'].default_value = 1.0 - smo
    if any(em):
        bs.inputs['Emission Color'].default_value = (*(srgb_to_lin(c) for c in em), 1.0)
        bs.inputs['Emission Strength'].default_value = 1.0
    MAT[name] = m

# ---------------------------------------------------------------------------
# Genel mesh yardimcilari
# ---------------------------------------------------------------------------
COLL = None   # o anda kurulan aracin koleksiyonu


class MB:
    """Python listelerinde mesh biriktirici (kapali kabuklar)."""

    def __init__(self):
        self.v = []
        self.f = []
        self.m = []

    def add(self, verts, faces, mat):
        o = len(self.v)
        self.v.extend(tuple(p) for p in verts)
        for i, fc in enumerate(faces):
            self.f.append(tuple(o + k for k in fc))
            self.m.append(mat if isinstance(mat, str) else mat[i])

    def add_bm(self, bm, mat):
        bm.verts.index_update()
        verts = [v.co.copy() for v in bm.verts]
        faces = [[v.index for v in f.verts] for f in bm.faces]
        mats = mat if isinstance(mat, str) else [MAT_ORDER[f.material_index] for f in bm.faces]
        self.add(verts, faces, mats)
        bm.free()

    def empty(self):
        return not self.f

    def to_object(self, name, recalc=True):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.v, [], self.f)
        for mn in MAT_ORDER:
            me.materials.append(MAT[mn])
        for p, mn in zip(me.polygons, self.m):
            p.material_index = MAT_IDX[mn]
        if recalc:
            bm = bmesh.new()
            bm.from_mesh(me)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            orient_islands(bm)
            bm.to_mesh(me)
            bm.free()
        me.update()
        o = bpy.data.objects.new(name, me)
        COLL.objects.link(o)
        return o


def orient_islands(bm):
    """Her kapali parcanin isaretli hacmi pozitif olsun (normaller disa)."""
    bm.faces.index_update()
    seen = set()
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, isl = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            isl.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        vol = 0.0
        for g in isl:
            vs = [v.co for v in g.verts]
            for i in range(1, len(vs) - 1):
                vol += vs[0].dot(vs[i].cross(vs[i + 1]))
        if vol < 0:
            bmesh.ops.reverse_faces(bm, faces=isl)


def bm_box(c, s):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((c[0] + v.co.x * s[0], c[1] + v.co.y * s[1], c[2] + v.co.z * s[2]))
    return bm


def bevel_all(bm, w):
    if w > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=w, segments=1,
                        affect='EDGES', clamp_overlap=True)
    return bm


def box(mb, c, s, mat, bev=0.0):
    mb.add_bm(bevel_all(bm_box(c, s), bev), mat)


def bm_prism(pts, axis, a0, a1):
    """2B cokgeni (pts) bir eksen boyunca a0..a1 araliginda uzatir.
    axis 'x': pts=(y,z); 'y': pts=(x,z); 'z': pts=(x,y)."""
    bm = bmesh.new()

    def P(p, a):
        if axis == 'x':
            return Vector((a, p[0], p[1]))
        if axis == 'y':
            return Vector((p[0], a, p[1]))
        return Vector((p[0], p[1], a))
    r0 = [bm.verts.new(P(p, a0)) for p in pts]
    r1 = [bm.verts.new(P(p, a1)) for p in pts]
    bm.faces.new(r0)
    bm.faces.new(list(reversed(r1)))
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def prism(mb, pts, axis, a0, a1, mat, bev=0.0):
    mb.add_bm(bevel_all(bm_prism(pts, axis, a0, a1), bev), mat)


def cyl(mb, c, axis, r, length, n, mat, bev=0.0):
    pts = [(r * math.cos(2 * math.pi * k / n), r * math.sin(2 * math.pi * k / n)) for k in range(n)]
    if axis == 'x':
        pts2 = [(c[1] + a, c[2] + b) for a, b in pts]
        a0, a1 = c[0] - length / 2, c[0] + length / 2
    elif axis == 'y':
        pts2 = [(c[0] + a, c[2] + b) for a, b in pts]
        a0, a1 = c[1] - length / 2, c[1] + length / 2
    else:
        pts2 = [(c[0] + a, c[1] + b) for a, b in pts]
        a0, a1 = c[2] - length / 2, c[2] + length / 2
    prism(mb, pts2, axis, a0, a1, mat, bev)


def rod(mb, a, b, r, n, mat):
    """a'dan b'ye uzanan n kenarli cubuk."""
    a, b = Vector(a), Vector(b)
    d = b - a
    q = d.to_track_quat('Z', 'Y')
    bm = bmesh.new()
    r0, r1 = [], []
    for k in range(n):
        t = 2 * math.pi * k / n
        off = q @ Vector((r * math.cos(t), r * math.sin(t), 0))
        r0.append(bm.verts.new(a + off))
        r1.append(bm.verts.new(b + off))
    bm.faces.new(r0)
    bm.faces.new(list(reversed(r1)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mb.add_bm(bm, mat)


def lin(a, b, n):
    return [a + (b - a) * i / n for i in range(n + 1)]


# ---------------------------------------------------------------------------
# Kesitli govde (loft). Kesit: alt pah, yan (karakter cizgisi), omuz (bel
# cizgisi), cam/direk seridi, tavan pahi, tepe (bombe). 13 nokta.
# ---------------------------------------------------------------------------
SLOTS = ['low', 'side1', 'side2', 'green', 'pillar', 'top',
         'top', 'pillar', 'green', 'side2', 'side1', 'low', 'bot']
SLOT_SIDE = ['R'] * 6 + ['L'] * 6 + ['C']


def section(st, P):
    tp = st.get('tp', 0.0)
    hw = P['hw'] - tp
    wm = hw
    wb = hw - st.get('tuck', P['tuck'])
    ws = hw - st.get('sh', P['shoulder'])
    zb, zs, zt = st['zb'], st['zs'], st['zt']
    wt = st.get('wt')
    if wt is None:
        wt = ws - P.get('deck_in', 0.03)
    wt = min(wt, ws - 0.008)
    g = max(zt - zs, 0.0)
    ct = st.get('ct', P['ct'])
    c4 = min(ct, 0.5 * g)
    c5 = min(ct * 1.1, wt * 0.3)
    zm = st.get('zm', zb + (zs - zb) * P['zmf'])
    cb = min(st.get('cb', P['cb']), (zm - zb) * 0.45)
    cr = st.get('cr', P['cr'])
    R = [(wb - cb, zb), (wb, zb + cb), (wm, zm), (ws, zs), (wt, zt - c4), (wt - c5, zt)]
    return R + [(0.0, zt + cr)] + [(-x, z) for (x, z) in reversed(R)]


def S(d, zb, zs, zt, **kw):
    st = dict(d=d, zb=zb, zs=zs, zt=zt)
    st.update(kw)
    return st


def role_value(role, slot, P):
    if slot in role:
        v = role[slot]
    elif 'all' in role:
        v = role['all']
    elif slot in P.get('slots', {}):
        v = P['slots'][slot]
    elif slot == 'bot':
        v = P.get('bot', 'AR_Sasi')
    else:
        v = 'P'
    if v == 'P':
        v = P['paint']
    return v


def loft_body(name, stations, roles, P, cap_f=None, cap_r=None, win=(0.028, 0.012)):
    """Kesitlerden kapali govde kabugu; cam bolgeleri iceri gomulu, kenarlari siyah fitil."""
    assert len(roles) == len(stations) - 1, (name, len(roles), len(stations))
    L = P['L']
    bm = bmesh.new()
    secs = [section(st, P) for st in stations]
    n = len(secs[0])
    rows = []
    for st, sec in zip(stations, secs):
        y = L / 2 - st['d']
        rows.append([bm.verts.new((x, y, z)) for (x, z) in sec])
    regions = {}
    for i in range(len(stations) - 1):
        role = roles[i]
        for k in range(n):
            k2 = (k + 1) % n
            f = bm.faces.new((rows[i][k], rows[i][k2], rows[i + 1][k2], rows[i + 1][k]))
            v = role_value(role, SLOTS[k], P)
            if isinstance(v, tuple):
                reg = v[1] if SLOTS[k] == 'top' else v[1] + '_' + SLOT_SIDE[k]
                regions.setdefault(reg, []).append(f)
                v = 'AR_Cam'
            f.material_index = MAT_IDX[v]
    fc = bm.faces.new(rows[0])
    fc.material_index = MAT_IDX[cap_f or P['paint']]
    fr = bm.faces.new(list(reversed(rows[-1])))
    fr.material_index = MAT_IDX[cap_r or P['paint']]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    th, dep = win
    for reg, faces in regions.items():
        res = bmesh.ops.inset_region(bm, faces=faces, thickness=th, depth=-dep,
                                     use_even_offset=True, use_boundary=True)
        for f in res['faces']:
            f.material_index = MAT_IDX['AR_Trim']
    # Duzlemsel olmayan dortgenleri geometriye bagli (simetrik) kosegenden bol; boolean ve FBX
    # ucgenlemesi sag/sol tarafta farkli kosegen secip govdeyi asimetrik yapmasin.
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='SHORT_EDGE', ngon_method='BEAUTY')
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for mn in MAT_ORDER:
        me.materials.append(MAT[mn])
    o = bpy.data.objects.new(name, me)
    COLL.objects.link(o)
    return o


def normalize_materials(me):
    """Malzeme yuvalarini MAT_ORDER sirasina getirir (boolean sonrasi)."""
    names = [m.name if m else 'AR_Trim' for m in me.materials]
    idx = [MAT_IDX.get(n.split('.')[0], MAT_IDX['AR_Trim']) for n in names]
    mi = [idx[p.material_index] if p.material_index < len(idx) else 0 for p in me.polygons]
    me.materials.clear()
    for mn in MAT_ORDER:
        me.materials.append(MAT[mn])
    for p, i in zip(me.polygons, mi):
        p.material_index = i


def compact_materials(me):
    used = sorted({p.material_index for p in me.polygons})
    remap = {old: new for new, old in enumerate(used)}
    mats = [me.materials[i] for i in used]
    mi = [remap[p.material_index] for p in me.polygons]
    me.materials.clear()
    for m in mats:
        me.materials.append(m)
    for p, i in zip(me.polygons, mi):
        p.material_index = i


def apply_boolean(obj, cutter, op='DIFFERENCE'):
    mod = obj.modifiers.new('kesim', 'BOOLEAN')
    mod.operation = op
    mod.solver = 'EXACT'
    mod.material_mode = 'TRANSFER'
    mod.object = cutter
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev)
    old = obj.data
    obj.modifiers.clear()
    obj.data = me
    bpy.data.meshes.remove(old)
    normalize_materials(me)
    bpy.data.objects.remove(cutter)


def make_bvh(obj):
    me = obj.data
    return BVHTree.FromPolygons([v.co.copy() for v in me.vertices],
                                [tuple(p.vertices) for p in me.polygons])


# ---------------------------------------------------------------------------
# Yuzeye oturan paneller (far, stop, izgara, kapi cizgisi...). Isin govdeye
# atilir, panel yuzeyin "proud" kadar onune konur, arkasi govdeye gomulur.
# ---------------------------------------------------------------------------
PLANES = {
    'F': (Vector((0, -1, 0)), 'x', 'z'),
    'B': (Vector((0, 1, 0)), 'x', 'z'),
    'R': (Vector((-1, 0, 0)), 'y', 'z'),
    'L': (Vector((1, 0, 0)), 'y', 'z'),
    'T': (Vector((0, 0, -1)), 'x', 'y'),
}


def _plane_point(plane, u, v):
    d, au, av = PLANES[plane]
    p = {'x': 0.0, 'y': 0.0, 'z': 0.0}
    p[au] = u
    p[av] = v
    return Vector((p['x'], p['y'], p['z'])) - d * 30.0


def patch_grid(mb, bvh, plane, grid, mat, proud=0.006, depth=0.02):
    d = PLANES[plane][0]
    nv = len(grid)
    nu = len(grid[0])
    front, back = [], []
    for row in grid:
        for (u, v) in row:
            hit = bvh.ray_cast(_plane_point(plane, u, v), d, 60.0)
            if hit[0] is None:
                raise RuntimeError(f'panel isini govdeye degmedi: {plane} {u:.3f} {v:.3f}')
            front.append(hit[0] - d * proud)
            back.append(hit[0] + d * depth)
    verts = front + back
    N = nu * nv
    faces = []
    for j in range(nv - 1):
        for i in range(nu - 1):
            a = j * nu + i
            faces.append((a, a + 1, a + nu + 1, a + nu))
            faces.append((N + a + nu, N + a + nu + 1, N + a + 1, N + a))
    per = [j * nu for j in range(nv)]          # sol kenar (asagidan yukari)
    per = ([i for i in range(nu)] +                         # alt
           [j * nu + nu - 1 for j in range(1, nv)] +        # sag
           [(nv - 1) * nu + i for i in range(nu - 2, -1, -1)] +  # ust
           [j * nu for j in range(nv - 2, 0, -1)])          # sol
    for k in range(len(per)):
        a, b = per[k], per[(k + 1) % len(per)]
        faces.append((a, b, N + b, N + a))
    mb.add(verts, faces, mat)


def _depth(bvh, plane, uv):
    d = PLANES[plane][0]
    hit = bvh.ray_cast(_plane_point(plane, uv[0], uv[1]), d, 60.0)[0]
    return None if hit is None else hit.dot(d)


def adaptive_params(bvh, plane, mapf, ss, ts, tol, max_iter=4):
    """Panel izgarasini yuzey kirimlarinda boler: kiris yuzeyin altina gomulmesin."""
    def need(a0, a1, others, along_t):
        am = (a0 + a1) / 2
        for o in others:
            if along_t:
                p0, p1, pm = mapf(o, a0), mapf(o, a1), mapf(o, am)
            else:
                p0, p1, pm = mapf(a0, o), mapf(a1, o), mapf(am, o)
            d0, d1, dm = _depth(bvh, plane, p0), _depth(bvh, plane, p1), _depth(bvh, plane, pm)
            if None in (d0, d1, dm):
                continue
            ch = (d0 + d1) / 2
            if ch - dm > tol or dm - ch > 3 * tol:
                return True
        return False
    ss, ts = list(ss), list(ts)
    for _ in range(max_iter):
        changed = False
        samp = sorted(set(ss + [(a + b) / 2 for a, b in zip(ss, ss[1:])]))
        nt = [ts[0]]
        for a, b in zip(ts, ts[1:]):
            if need(a, b, samp, True):
                nt.append((a + b) / 2)
                changed = True
            nt.append(b)
        ts = nt
        samp = sorted(set(ts + [(a + b) / 2 for a, b in zip(ts, ts[1:])]))
        ns = [ss[0]]
        for a, b in zip(ss, ss[1:]):
            if need(a, b, samp, False):
                ns.append((a + b) / 2)
                changed = True
            ns.append(b)
        ss = ns
        if not changed:
            break
    return ss, ts


def patch(mb, bvh, plane, u0, u1, v0, v1, mat, nu=1, nv=1, proud=0.006, depth=0.02, us=None, vs=None):
    us = us or lin(u0, u1, nu)
    vs = vs or lin(v0, v1, nv)
    us, vs = adaptive_params(bvh, plane, lambda a, b: (a, b), us, vs, max(proud * 0.6, 0.003), 2)
    grid = [[(u, v) for u in us] for v in vs]
    patch_grid(mb, bvh, plane, grid, mat, proud, depth)


def patch_sym(mb, bvh, plane, u0, u1, v0, v1, mat, **kw):
    """X'e gore simetrik cift panel (F/B/T duzlemleri icin u = x)."""
    patch(mb, bvh, plane, u0, u1, v0, v1, mat, **kw)
    patch(mb, bvh, plane, -u1, -u0, v0, v1, mat, **kw)


def patch_sides(mb, bvh, u0, u1, v0, v1, mat, **kw):
    """Sag ve sol yan yuzeye ayni panel (u = y)."""
    patch(mb, bvh, 'R', u0, u1, v0, v1, mat, **kw)
    patch(mb, bvh, 'L', u0, u1, v0, v1, mat, **kw)


def quad_grid(c, nu, nv, bvh=None, plane=None, tol=0.004, it=2):
    bl, br, tr, tl = [Vector((p[0], p[1])) for p in c]

    def mapf(s_, t_):
        A, B = bl.lerp(tl, t_), br.lerp(tr, t_)
        return tuple(A.lerp(B, s_))
    ss, ts = lin(0.0, 1.0, nu), lin(0.0, 1.0, nv)
    if bvh is not None:
        ss, ts = adaptive_params(bvh, plane, mapf, ss, ts, max(tol, 0.003), it)
    return [[mapf(s_, t_) for s_ in ss] for t_ in ts]


def expand_quad(c, e):
    cu = sum(p[0] for p in c) / 4
    cv = sum(p[1] for p in c) / 4
    return [(u + math.copysign(e, u - cu), v + math.copysign(e, v - cv)) for u, v in c]


def sub_quad(c, t0, t1):
    """Dortgenin alt->ust yonunde t0..t1 dilimi."""
    bl, br, tr, tl = [Vector(p) for p in c]
    return [tuple(bl.lerp(tl, t0)), tuple(br.lerp(tr, t0)), tuple(br.lerp(tr, t1)), tuple(bl.lerp(tl, t1))]


def lamp(mb, bvh, plane, corners, parts, nu=2, nv=1, bezel=0.016, proud=0.010, mirror=True):
    """Siyah cerceveli lamba grubu. parts: [(malzeme, t0, t1)] alt->ust dilimler."""
    sets = [corners]
    if mirror:
        sets.append([(-u, v) for (u, v) in corners])
    for c in sets:
        if bezel:
            patch_grid(mb, bvh, plane, quad_grid(expand_quad(c, bezel), nu, nv, bvh, plane, proud * 0.3),
                       'AR_Trim', proud=proud * 0.45, depth=0.02)
        for mat, t0, t1 in parts:
            patch_grid(mb, bvh, plane, quad_grid(sub_quad(c, t0, t1), nu, nv, bvh, plane, proud * 0.6),
                       mat, proud=proud, depth=0.02)


def quad(mb, bvh, plane, corners, mat, nu=2, nv=1, proud=0.006, depth=0.02):
    patch_grid(mb, bvh, plane, quad_grid(corners, nu, nv, bvh, plane, proud * 0.6), mat, proud=proud, depth=depth)


def arch_ring(mb, bvh, yc, zc, r_in, r_out, zmin, mat, n=12, proud=0.012, depth=0.02):
    """Teker kemeri cevresinde iki yana kaplama seridi."""
    # cemberin zmin ile kesistigi acilar
    s = max(-1.0, min(1.0, (zmin - zc) / r_out))
    a0 = math.asin(s)
    angs = lin(math.pi - a0, a0, n)
    for plane in ('R', 'L'):
        grid = [[(yc + r * math.cos(t), zc + r * math.sin(t)) for t in angs] for r in (r_in, r_out)]
        patch_grid(mb, bvh, plane, grid, mat, proud, depth)


def seam(mb, bvh, y, z0, z1, zs_extra=(), w=0.009, mat='AR_Trim'):
    vs = sorted(set([z0, z1] + [z for z in zs_extra if z0 < z < z1]))
    patch_sides(mb, bvh, y - w / 2, y + w / 2, 0, 0, mat, vs=vs, proud=0.0025, depth=0.01)


# ---------------------------------------------------------------------------
# Tekerler (torna profili, X ekseni etrafinda donel simetrik)
# ---------------------------------------------------------------------------
def lathe_bm(prof, n, side):
    """prof: [(a, r, mat_sonraki_segment)], a dis yuze dogru pozitif."""
    bm = bmesh.new()
    rings = []
    for a, r, _ in prof:
        x = side * a
        if r < 1e-6:
            rings.append([bm.verts.new((x, 0.0, 0.0))])
        else:
            rings.append([bm.verts.new((x, r * math.cos(-math.pi / 2 + 2 * math.pi * k / n),
                                        r * math.sin(-math.pi / 2 + 2 * math.pi * k / n)))
                          for k in range(n)])
    for j in range(len(rings) - 1):
        A, B = rings[j], rings[j + 1]
        mi = MAT_IDX[prof[j][2]]
        for k in range(n):
            k2 = (k + 1) % n
            if len(A) == 1:
                f = bm.faces.new((A[0], B[k], B[k2]))
            elif len(B) == 1:
                f = bm.faces.new((A[k], A[k2], B[0]))
            else:
                f = bm.faces.new((A[k], A[k2], B[k2], B[k]))
            f.material_index = mi
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def car_wheel_profile(r, w, rim_r, dish=0.05):
    """Binek/ticari teker: celik jant + gobek kapagi, yuvarlak omuzlu lastik."""
    h = w / 2
    J, T, L = 'AR_Jant', 'AR_Trim', 'AR_Lastik'
    sh = min(0.05, (r - rim_r) * 0.45)
    return [
        (h - dish - 0.012, 0.0, J),
        (h - dish - 0.012, rim_r * 0.30, J),
        (h - dish, rim_r * 0.36, J),
        (h - dish, rim_r * 0.74, T),
        (h - dish - 0.006, rim_r * 0.80, J),
        (h - 0.018, rim_r * 0.93, J),
        (h - 0.010, rim_r, L),
        (h, rim_r + (r - rim_r) * 0.45, L),
        (h - 0.004, r - sh, L),
        (h - sh * 0.6, r, L),
        (-h + sh * 0.6, r, L),
        (-h + 0.004, r - sh, L),
        (-h, rim_r + (r - rim_r) * 0.45, L),
        (-h + 0.010, rim_r, T),
        (-h + 0.03, rim_r * 0.75, T),
        (-h + 0.03, 0.0, T),
    ]


def dual_wheel_profile(r, w, rim_r):
    """Kamyon arka cift lastik: iki lastik, arada oluk, dis jant derin."""
    h = w / 2
    tw = (w - 0.035) / 2        # tek lastik genisligi
    J, T, L = 'AR_Jant', 'AR_Trim', 'AR_Lastik'
    sh = 0.05
    g0 = h - tw                 # dis lastigin ic yanagi
    g1 = -h + tw                # ic lastigin dis yanagi
    return [
        (h - 0.10, 0.0, J),
        (h - 0.10, rim_r * 0.32, J),
        (h - 0.085, rim_r * 0.38, J),
        (h - 0.085, rim_r * 0.72, T),
        (h - 0.07, rim_r * 0.80, J),
        (h - 0.02, rim_r * 0.93, J),
        (h - 0.010, rim_r, L),
        (h, rim_r + (r - rim_r) * 0.45, L),
        (h - 0.004, r - sh, L),
        (h - sh * 0.6, r, L),
        (g0 + sh * 0.5, r, L),
        (g0 + 0.003, r - sh, L),
        (g0, r - 0.11, T),
        (g1, r - 0.11, L),
        (g1 - 0.003, r - sh, L),
        (g1 - sh * 0.5, r, L),
        (-h + sh * 0.6, r, L),
        (-h + 0.004, r - sh, L),
        (-h, rim_r + (r - rim_r) * 0.45, L),
        (-h + 0.010, rim_r, T),
        (-h + 0.03, rim_r * 0.75, T),
        (-h + 0.03, 0.0, T),
    ]


def make_wheel(name, center, side, prof, n, extra=None):
    mb = MB()
    mb.add_bm(lathe_bm(prof, n, side), None)
    if extra:
        extra(mb)
    o = mb.to_object(name)
    o.location = center
    return o

# ---------------------------------------------------------------------------
# Ozel teker profilleri
# ---------------------------------------------------------------------------
def tractor_front_profile(r, w, rim_r):
    """Traktor on lastigi: uc dis (rib), arada iki oluk."""
    h = w / 2
    J, T, L = 'AR_Jant', 'AR_Trim', 'AR_Lastik'
    g = 0.024
    A, B, C = h - 0.03, h * 0.38, h * 0.20
    return [
        (h - 0.055, 0.0, J),
        (h - 0.055, rim_r * 0.42, J),
        (h - 0.035, rim_r * 0.55, J),
        (h - 0.020, rim_r * 0.94, J),
        (h - 0.010, rim_r, L),
        (h, rim_r + (r - rim_r) * 0.5, L),
        (h - 0.006, r - 0.04, L),
        (A, r, L), (B, r, L), (B, r - g, T), (C, r - g, L), (C, r, L),
        (-C, r, L), (-C, r - g, T), (-B, r - g, L), (-B, r, L), (-A, r, L),
        (-h + 0.006, r - 0.04, L),
        (-h, rim_r + (r - rim_r) * 0.5, L),
        (-h + 0.010, rim_r, T),
        (-h + 0.04, rim_r * 0.6, T),
        (-h + 0.04, 0.0, T),
    ]


def tractor_rear_profile(r, w, rim_r, lug):
    """Traktor arka lastik govdesi (tirnaklar ayri kutular)."""
    h = w / 2
    rc = r - lug
    J, T, L = 'AR_Jant', 'AR_Trim', 'AR_Lastik'
    return [
        (h - 0.13, 0.0, J),
        (h - 0.13, rim_r * 0.30, J),
        (h - 0.10, rim_r * 0.40, J),
        (h - 0.10, rim_r * 0.80, J),
        (h - 0.04, rim_r * 0.90, J),
        (h - 0.02, rim_r, L),
        (h, rim_r + (rc - rim_r) * 0.55, L),
        (h - 0.02, rc - 0.025, L),
        (h - 0.05, rc, L),
        (-h + 0.05, rc, L),
        (-h + 0.02, rc - 0.025, L),
        (-h, rim_r + (rc - rim_r) * 0.55, L),
        (-h + 0.02, rim_r, T),
        (-h + 0.06, rim_r * 0.6, T),
        (-h + 0.06, 0.0, T),
    ]


def tractor_lugs(mb, r, w, lug, side, n=16):
    """Iki sira capraz (V) tirnak; sol/sag sira yarim adim kaydirilmis."""
    h = w / 2
    rc = r - lug
    for row, sgn in ((0, 1), (1, -1)):
        a_mid = sgn * (h * 0.48)
        for k in range(n):
            t = -math.pi / 2 + 2 * math.pi * (k + 0.5 * row) / n
            bm = bm_box((0, 0, 0), (h * 0.80, 0.07, lug + 0.02))
            # tirnak yerel: X eksen boyunca, Y teget, Z radyal
            rot_v = Matrix.Rotation(sgn * math.radians(28), 4, 'Z')
            for v in bm.verts:
                v.co = rot_v @ v.co
            for v in bm.verts:
                v.co.z += rc + (lug + 0.02) / 2 - 0.02
            # radyal yonu (0,cos t, sin t) olacak sekilde X ekseni etrafinda dondur
            M = Matrix.Rotation(t - math.pi / 2, 4, 'X')
            for v in bm.verts:
                p = Vector((v.co.x + a_mid, v.co.y, v.co.z))
                p = M @ p
                v.co = Vector((side * p.x, p.y, p.z))
            mb.add_bm(bm, 'AR_Lastik')


# ---------------------------------------------------------------------------
# Arac kaydi
# ---------------------------------------------------------------------------
class Vehicle:
    def __init__(self, vid, short, L, W, H, wb, tip, mass, vmax, drive, com):
        self.id = vid
        self.short = short
        self.L, self.W, self.H, self.wb = L, W, H, wb
        self.tip, self.mass, self.vmax, self.drive = tip, mass, vmax, drive
        self.com = com            # Blender (x, y, z)
        self.wheels = []
        self.cols = []
        self.paint = 'AR_Boya'
        self.objects = []

    def wheel(self, ad, x, y, r, w, steer, drive, **kw):
        d = dict(ad=ad, x=x, y=y, r=r, w=w, steer=steer, drive=drive)
        d.update(kw)
        self.wheels.append(d)

    def axle(self, front, y, track, r, w, steer, drive, **kw):
        pre = 'On' if front else 'Arka'
        self.wheel(f'Teker_{pre}Sol', -track / 2, y, r, w, steer, drive, **kw)
        self.wheel(f'Teker_{pre}Sag', track / 2, y, r, w, steer, drive, **kw)

    def col(self, ad, mn, mx):
        self.cols.append((ad, tuple(mn), tuple(mx)))

    def y(self, d):
        return self.L / 2 - d


def arch_cutters(V, gap=0.045, zoff=0.012, inner_margin=0.04, mat='AR_Trim', only=None,
                 extra_x=0.35, mb=None):
    """Teker yuvalari: X ekseninde silindir kesiciler (ic duvar/tavan = mat)."""
    mb = mb or MB()
    for wh in V.wheels:
        if only and wh['ad'] not in only:
            continue
        r, w = wh['r'], wh['w']
        Ra = r + wh.get('gap', gap)
        xc = abs(wh['x'])
        if wh['steer']:
            reach = (w / 2) * math.cos(math.radians(35)) + r * math.sin(math.radians(35))
        else:
            reach = w / 2
        x_in = xc - reach - inner_margin
        x_out = V.W / 2 + extra_x
        s = 1 if wh['x'] > 0 else -1
        cyl(mb, (s * (x_in + x_out) / 2, wh['y'], r + zoff), 'x', Ra, x_out - x_in, 28, mat)
        wh['arch'] = (Ra, r + zoff, x_in)
    return mb


def arch_z0(V, y, zlo, pad=0.02):
    """y konumunda kemer ustundeki en alcak guvenli z (kapi cizgisi icin)."""
    z0 = zlo
    for wh in V.wheels:
        if 'arch' not in wh:
            continue
        Ra, zc, _ = wh['arch']
        if abs(y - wh['y']) < Ra + pad:
            z0 = max(z0, zc + math.sqrt(max(0.0, (Ra + pad) ** 2 - (y - wh['y']) ** 2)) + 0.01)
    return z0


def door_seams(V, pm, bvh, ds, zlo, ztops, zcrease):
    for d, zt in zip(ds, ztops):
        y = V.y(d)
        seam(pm, bvh, y, arch_z0(V, y, zlo), zt, zs_extra=zcrease)


def win_patch(mb, bvh, quad, frame=0.024, planes=('R', 'L')):
    """Yan cam paneli: siyah fitil cercevesi + fume cam (quad: alt-on, alt-arka, ust-arka, ust-on; u=y)."""
    cu = sum(p[0] for p in quad) / 4
    cv = sum(p[1] for p in quad) / 4
    ex = [(u + math.copysign(frame, u - cu), v + math.copysign(frame, v - cv)) for u, v in quad]
    for pl in planes:
        patch_grid(mb, bvh, pl, quad_grid(ex, 1, 1, bvh, pl, 0.002, 3), 'AR_Trim', proud=0.003, depth=0.015)
        patch_grid(mb, bvh, pl, quad_grid(quad, 1, 1, bvh, pl, 0.004, 3), 'AR_Cam', proud=0.007, depth=0.015)


def mirror_pair(mb, bvh, y, z, size, mat_shell, stalk_len=0.05, glass='AR_Cam', arm_z=0.0):
    """Kapiya oturan dikiz aynasi: kol + govde + arkaya bakan cam."""
    sx, sy, sz = size
    for plane, s in (('R', 1), ('L', -1)):
        hit = bvh.ray_cast(_plane_point(plane, y, z), PLANES[plane][0], 60.0)[0]
        if hit is None or abs(hit.x) < 0.55:
            raise RuntimeError(f'ayna yan yuzeye oturmadi: y={y:.3f} z={z:.3f} -> {hit}')
        x0 = abs(hit.x) - 0.012
        x1 = x0 + stalk_len
        box(mb, (s * (x0 + x1) / 2, y, z + arm_z / 2), (x1 - x0, sy * 0.45, sz * 0.30 + abs(arm_z)), 'AR_Trim', 0.006)
        cx = x1 + sx / 2 - 0.006
        zc = z + arm_z + sz * 0.15
        box(mb, (s * cx, y, zc), (sx, sy, sz), mat_shell, min(0.018, sy * 0.25))
        box(mb, (s * cx, y - sy / 2 - 0.002, zc), (sx * 0.80, 0.01, sz * 0.76), glass, 0.003)


def finish_vehicle(V, body, parts, mirrors, wheel_builder):
    """Govde + parcalar + aynalar -> Govde; tekerler; COL kutulari."""
    objs = [body]
    if not parts.empty():
        objs.append(parts.to_object(V.short + '_parcalar'))
    if not mirrors.empty():
        mo = mirrors.to_object(V.short + '_aynalar')
        vg = mo.vertex_groups.new(name='Ayna')
        vg.add(list(range(len(mo.data.vertices))), 1.0, 'REPLACE')
        objs.append(mo)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    gov = bpy.context.view_layer.objects.active
    gov.name = V.short + '_Govde'
    gov.data.name = V.short + '_Govde'
    gov['ad'] = 'Govde'
    compact_materials(gov.data)
    V.objects.append(gov)
    for wh in V.wheels:
        side = 1 if wh['x'] > 0 else -1
        o = wheel_builder(V, wh, side)
        o.name = V.short + '_' + wh['ad']
        o.data.name = o.name
        o['ad'] = wh['ad']
        compact_materials(o.data)
        V.objects.append(o)
    for ad, mn, mx in V.cols:
        mb = MB()
        c = [(a + b) / 2 for a, b in zip(mn, mx)]
        s = [b - a for a, b in zip(mn, mx)]
        mb.add_bm(bm_box(c, s), 'AR_Trim')
        o = mb.to_object(V.short + '_' + ad)
        o.data.materials.clear()
        o['ad'] = ad
        o.display_type = 'WIRE'
        o.hide_render = True
        V.objects.append(o)


def std_wheel_builder(V, wh, side):
    prof = car_wheel_profile(wh['r'], wh['w'], wh['r'] * wh.get('rim', 0.62), dish=wh.get('dish', 0.045))
    return make_wheel('w', (wh['x'], wh['y'], wh['r']), side, prof, wh.get('n', 18))


def car_cols(V, belt, roof_hw, d_cowl, d_rear, roof, zlow=0.26, alt_top=None):
    L, hw = V.L, V.W / 2
    inner = min(abs(w['x']) - w['w'] / 2 for w in V.wheels) - 0.03
    rtop = max(2 * w['r'] for w in V.wheels) + 0.03
    V.col('COL_Govde_Alt', (-inner, -L / 2 + 0.02, zlow), (inner, L / 2 - 0.02, alt_top or belt))
    V.col('COL_Govde_Ust', (-hw + 0.01, -L / 2 + 0.06, rtop), (hw - 0.01, L / 2 - 0.06, belt))
    V.col('COL_Kabin', (-roof_hw, V.y(d_rear), belt), (roof_hw, V.y(d_cowl), roof))


G = lambda r: ('G', r)
TRIM = {'all': 'AR_Trim'}


def car_front(V, pm, bvh, head, sig, grille, intake, chrome=None, head_n=(3, 2)):
    patch_sym(pm, bvh, 'F', *head, 'AR_Far', nu=head_n[0], nv=head_n[1], proud=0.009)
    if sig:
        patch_sym(pm, bvh, 'F', *sig, 'AR_Sinyal', nu=1, nv=head_n[1], proud=0.009)
    if grille:
        patch(pm, bvh, 'F', -grille[0], grille[0], grille[1], grille[2], 'AR_Trim', nu=4, nv=2, proud=0.006)
    if chrome:
        patch(pm, bvh, 'F', -chrome[0], chrome[0], chrome[1], chrome[2], 'AR_Krom', nu=4, nv=1, proud=0.009)
    if intake:
        patch(pm, bvh, 'F', -intake[0], intake[0], intake[1], intake[2], 'AR_Trim', nu=4, nv=1, proud=0.006)


def car_rear(V, pm, bvh, stop, sig, diffuser, n=(3, 2)):
    patch_sym(pm, bvh, 'B', *stop, 'AR_Stop', nu=n[0], nv=n[1], proud=0.009)
    if sig:
        patch_sym(pm, bvh, 'B', *sig, 'AR_Sinyal', nu=n[0], nv=1, proud=0.009)
    if diffuser:
        patch(pm, bvh, 'B', -diffuser[0], diffuser[0], diffuser[1], diffuser[2], 'AR_Trim', nu=4, nv=1, proud=0.006)


def handles(V, pm, bvh, ds, z0, z1, w=0.13):
    for d in ds:
        y = V.y(d)
        patch_sides(pm, bvh, y - w / 2, y + w / 2, z0, z1, 'AR_Trim', proud=0.011, depth=0.015)


# ---------------------------------------------------------------------------
# 1) HATCHBACK - kucuk sehir arabasi, 5 kapi
# ---------------------------------------------------------------------------
def build_hatchback():
    V = Vehicle('Arac_Hatchback', 'Hatchback', 3.95, 1.74, 1.48, 2.50, 'binek', 1100, 70, 'on', (0, 0.15, 0.42))
    L, hw = V.L, V.W / 2
    P = dict(L=L, hw=hw, tuck=0.04, shoulder=0.028, ct=0.07, cb=0.05, zmf=0.55, cr=0.018, paint=V.paint)
    yf = V.y(0.80)
    V.axle(True, yf, 1.50, 0.30, 0.185, True, True)
    V.axle(False, yf - V.wb, 1.48, 0.30, 0.185, False, False)
    st = [
        S(0.00, 0.30, 0.50, 0.535, tp=0.17, cr=0.0),
        S(0.04, 0.27, 0.575, 0.605, tp=0.09, cr=0.005),
        S(0.13, 0.25, 0.72, 0.75, tp=0.045, cr=0.010),
        S(0.30, 0.24, 0.765, 0.80, tp=0.015),
        S(0.65, 0.24, 0.83, 0.86),
        S(1.08, 0.24, 0.905, 0.925, wt=0.70),
        S(1.90, 0.24, 0.975, 1.445, wt=0.655, cr=0.022),
        S(2.02, 0.24, 0.98, 1.46, wt=0.66, cr=0.022),
        S(2.56, 0.24, 0.985, 1.46, wt=0.665, cr=0.022),
        S(2.64, 0.24, 0.985, 1.458, wt=0.665, cr=0.022),
        S(3.20, 0.24, 0.99, 1.44, wt=0.655, cr=0.02),
        S(3.50, 0.25, 0.995, 1.41, wt=0.63, cr=0.018),
        S(3.57, 0.26, 0.995, 1.385, wt=0.62, tp=0.012, cr=0.016),
        S(3.77, 0.27, 0.985, 1.035, wt=0.66, tp=0.035, cr=0.012),
        S(3.885, 0.28, 0.62, 0.67, tp=0.075, cr=0.005),
        S(3.95, 0.31, 0.52, 0.56, tp=0.14, cr=0.0),
    ]
    roles = [{}, {}, {}, {}, {},
             {'top': G('ws'), 'green': G('sF')}, {'green': G('sF')}, {'green': G('sF')},
             {'green': 'AR_Trim'}, {'green': G('sR')},
             {}, {}, {'top': G('rg')}, {}, {}]
    body = loft_body(V.short + '_kabuk', st, roles, P)
    apply_boolean(body, arch_cutters(V).to_object('kesici'))
    bvh = make_bvh(body)
    pm, mm = MB(), MB()
    lamp(pm, bvh, 'F', [(0.44, 0.622), (0.72, 0.630), (0.755, 0.735), (0.47, 0.722)],
         [('AR_Far', 0, 1)], nu=3, nv=1)
    lamp(pm, bvh, 'F', [(0.62, 0.395), (0.71, 0.395), (0.71, 0.445), (0.62, 0.445)], [('AR_Sinyal', 0, 1)], nu=1, bezel=0.012)
    quad(pm, bvh, 'F', [(-0.37, 0.635), (0.37, 0.635), (0.40, 0.705), (-0.40, 0.705)], 'AR_Trim', nu=4)
    quad(pm, bvh, 'F', [(-0.50, 0.33), (0.50, 0.33), (0.44, 0.47), (-0.44, 0.47)], 'AR_Trim', nu=4)
    lamp(pm, bvh, 'B', [(0.53, 0.79), (0.77, 0.79), (0.75, 1.015), (0.56, 1.015)],
         [('AR_Sinyal', 0, 0.26), ('AR_Stop', 0.26, 1)], nu=2, nv=1)
    patch(pm, bvh, 'B', -0.50, 0.50, 0.33, 0.42, 'AR_Trim', nu=4, nv=1, proud=0.006)
    door_seams(V, pm, bvh, (1.17, 2.60, 3.22), 0.305, (0.95, 0.97, 0.975), (0.24 + 0.745 * 0.55,))
    handles(V, pm, bvh, (2.42, 3.06), 0.90, 0.93)
    mirror_pair(mm, bvh, V.y(1.24), 0.99, (0.12, 0.075, 0.105), V.paint)
    car_cols(V, belt=0.98, roof_hw=0.62, d_cowl=1.20, d_rear=3.72, roof=1.46)
    finish_vehicle(V, body, pm, mm, std_wheel_builder)
    return V


# ---------------------------------------------------------------------------
# 2) SEDAN (+ 3 TAKSI) - uc hacimli
# ---------------------------------------------------------------------------
def build_sedan(taxi=False):
    if taxi:
        V = Vehicle('Arac_Taksi', 'Taksi', 4.50, 1.78, 1.61, 2.64, 'binek', 1350, 70, 'on', (0, 0.12, 0.44))
        V.paint = 'AR_Taksi_Sari'
    else:
        V = Vehicle('Arac_Sedan', 'Sedan', 4.50, 1.78, 1.45, 2.64, 'binek', 1300, 70, 'on', (0, 0.12, 0.43))
    L, hw = V.L, V.W / 2
    P = dict(L=L, hw=hw, tuck=0.04, shoulder=0.028, ct=0.07, cb=0.05, zmf=0.55, cr=0.018, paint=V.paint)
    yf = V.y(0.90)
    V.axle(True, yf, 1.53, 0.31, 0.195, True, True)
    V.axle(False, yf - V.wb, 1.52, 0.31, 0.195, False, False)
    st = [
        S(0.00, 0.31, 0.505, 0.54, tp=0.17, cr=0.0),
        S(0.045, 0.28, 0.575, 0.605, tp=0.09, cr=0.005),
        S(0.14, 0.26, 0.73, 0.76, tp=0.045, cr=0.010),
        S(0.32, 0.25, 0.78, 0.815, tp=0.015),
        S(0.80, 0.25, 0.85, 0.885),
        S(1.42, 0.25, 0.925, 0.945, wt=0.72),
        S(2.22, 0.25, 0.975, 1.415, wt=0.665, cr=0.022),
        S(2.34, 0.25, 0.98, 1.433, wt=0.67, cr=0.022),
        S(2.86, 0.25, 0.985, 1.435, wt=0.67, cr=0.022),
        S(2.94, 0.25, 0.985, 1.433, wt=0.67, cr=0.022),
        S(3.28, 0.25, 0.99, 1.42, wt=0.665, cr=0.02),
        S(3.42, 0.25, 0.995, 1.395, wt=0.65, cr=0.018),
        S(3.50, 0.25, 0.995, 1.365, wt=0.64, cr=0.016),
        S(3.93, 0.26, 1.005, 1.055, wt=0.73, cr=0.012),
        S(4.32, 0.27, 0.995, 1.04, tp=0.03, cr=0.010),
        S(4.42, 0.28, 0.76, 0.97, tp=0.06, cr=0.008),
        S(4.47, 0.29, 0.60, 0.645, tp=0.10, cr=0.004),
        S(4.50, 0.31, 0.52, 0.56, tp=0.16, cr=0.0),
    ]
    roles = [{}, {}, {}, {}, {},
             {'top': G('ws'), 'green': G('sF')}, {'green': G('sF')}, {'green': G('sF')},
             {'green': 'AR_Trim'}, {'green': G('sR')},
             {}, {}, {'top': G('rg')}, {}, {}, {}, {}]
    body = loft_body(V.short + '_kabuk', st, roles, P)
    apply_boolean(body, arch_cutters(V).to_object('kesici'))
    bvh = make_bvh(body)
    pm, mm = MB(), MB()
    lamp(pm, bvh, 'F', [(0.43, 0.618), (0.795, 0.624), (0.795, 0.735), (0.43, 0.74)],
         [('AR_Far', 0, 1)], nu=3, nv=1)
    lamp(pm, bvh, 'F', [(0.64, 0.40), (0.74, 0.40), (0.74, 0.44), (0.64, 0.44)], [('AR_Sinyal', 0, 1)], nu=1, bezel=0.012)
    quad(pm, bvh, 'F', [(-0.385, 0.618), (0.385, 0.618), (0.385, 0.74), (-0.385, 0.74)], 'AR_Trim', nu=4)
    quad(pm, bvh, 'F', [(-0.385, 0.722), (0.385, 0.722), (0.385, 0.74), (-0.385, 0.74)], 'AR_Krom', nu=4, proud=0.010)
    quad(pm, bvh, 'F', [(-0.46, 0.34), (0.46, 0.34), (0.42, 0.44), (-0.42, 0.44)], 'AR_Trim', nu=4)
    lamp(pm, bvh, 'B', [(0.50, 0.80), (0.78, 0.80), (0.775, 0.962), (0.52, 0.962)],
         [('AR_Sinyal', 0, 0.30), ('AR_Stop', 0.30, 1)], nu=2, nv=1)
    patch(pm, bvh, 'B', -0.52, 0.52, 0.335, 0.42, 'AR_Trim', nu=4, nv=1, proud=0.006)
    door_seams(V, pm, bvh, (1.47, 2.90, 3.42), 0.31, (0.955, 0.97, 0.975), (0.25 + 0.74 * 0.55,))
    handles(V, pm, bvh, (2.72, 3.28), 0.895, 0.925)
    # bagaj kapagi cizgileri
    for s in (-1, 1):
        patch(pm, bvh, 'T', s * 0.655 - 0.005, s * 0.655 + 0.005, V.y(4.40), V.y(3.97), 'AR_Trim',
              nv=3, proud=0.0025, depth=0.01)
    patch(pm, bvh, 'T', -0.655, 0.655, V.y(3.975), V.y(3.965), 'AR_Trim', nu=4, proud=0.0025, depth=0.01)
    patch(pm, bvh, 'B', -0.47, 0.47, 0.815, 0.825, 'AR_Trim', nu=4, proud=0.0025, depth=0.01)
    mirror_pair(mm, bvh, V.y(1.66), 0.965, (0.125, 0.075, 0.105), V.paint)
    roof = 1.455
    if taxi:
        # tavan lambasi: koyu taban + bos (yazisiz) lamba kutusu
        yb = V.y(2.80)
        hit = bvh.ray_cast(Vector((0, yb, 5)), Vector((0, 0, -1)), 10)[0]
        z0 = hit.z - 0.01
        box(pm, (0, yb, z0 + 0.016), (0.64, 0.27, 0.032), 'AR_Trim', 0.008)
        box(pm, (0, yb, z0 + 0.032 + 0.062), (0.58, 0.225, 0.124), 'AR_Taksi_Lamba', 0.022)
        roof = z0 + 0.156
    car_cols(V, belt=0.985, roof_hw=0.64, d_cowl=1.55, d_rear=3.90, roof=roof)
    finish_vehicle(V, body, pm, mm, std_wheel_builder)
    return V


# ---------------------------------------------------------------------------
# 4) SUV - kompakt crossover, tavan raylari
# ---------------------------------------------------------------------------
def build_suv():
    V = Vehicle('Arac_SUV', 'SUV', 4.40, 1.82, 1.66, 2.62, 'binek', 1450, 70, 'on', (0, 0.12, 0.52))
    L, hw = V.L, V.W / 2
    P = dict(L=L, hw=hw, tuck=0.045, shoulder=0.03, ct=0.075, cb=0.06, zmf=0.50, cr=0.02, paint=V.paint,
             slots={'low': 'AR_Trim'})
    yf = V.y(0.88)
    V.axle(True, yf, 1.57, 0.34, 0.215, True, True, rim=0.60, n=16)
    V.axle(False, yf - V.wb, 1.57, 0.34, 0.215, False, False, rim=0.60, n=16)
    st = [
        S(0.00, 0.40, 0.62, 0.655, tp=0.17, cr=0.0),
        S(0.05, 0.37, 0.70, 0.735, tp=0.09, cr=0.005),
        S(0.16, 0.35, 0.88, 0.915, tp=0.045, cr=0.010),
        S(0.34, 0.34, 0.935, 0.97, tp=0.015),
        S(0.85, 0.34, 1.00, 1.035),
        S(1.30, 0.34, 1.07, 1.09, wt=0.75),
        S(2.02, 0.34, 1.115, 1.575, wt=0.705, cr=0.022),
        S(2.14, 0.34, 1.12, 1.59, wt=0.71, cr=0.022),
        S(2.66, 0.34, 1.125, 1.595, wt=0.71, cr=0.022),
        S(2.74, 0.34, 1.125, 1.595, wt=0.71, cr=0.022),
        S(3.30, 0.34, 1.13, 1.59, wt=0.71, cr=0.022),
        S(3.37, 0.34, 1.13, 1.59, wt=0.71, cr=0.022),
        S(3.86, 0.35, 1.135, 1.575, wt=0.695, cr=0.02),
        S(4.10, 0.36, 1.14, 1.55, wt=0.67, cr=0.018),
        S(4.17, 0.37, 1.135, 1.515, wt=0.66, tp=0.01, cr=0.016),
        S(4.30, 0.38, 1.11, 1.15, wt=0.71, tp=0.035, cr=0.012),
        S(4.365, 0.39, 0.76, 0.80, tp=0.08, cr=0.005),
        S(4.40, 0.41, 0.64, 0.675, tp=0.15, cr=0.0),
    ]
    roles = [{}, {}, {}, {}, {},
             {'top': G('ws'), 'green': G('sF')}, {'green': G('sF')}, {'green': G('sF')},
             {'green': 'AR_Trim'}, {'green': G('sR')}, {'green': 'AR_Trim'}, {'green': G('sQ')},
             {}, {}, {'top': G('rg')}, {}, {}]
    body = loft_body(V.short + '_kabuk', st, roles, P)
    apply_boolean(body, arch_cutters(V, gap=0.05).to_object('kesici'))
    bvh = make_bvh(body)
    pm, mm = MB(), MB()
    lamp(pm, bvh, 'F', [(0.47, 0.765), (0.79, 0.772), (0.815, 0.895), (0.49, 0.885)],
         [('AR_Far', 0, 1)], nu=3, nv=1)
    lamp(pm, bvh, 'F', [(0.66, 0.53), (0.76, 0.53), (0.76, 0.575), (0.66, 0.575)], [('AR_Sinyal', 0, 1)], nu=1, bezel=0.012)
    quad(pm, bvh, 'F', [(-0.40, 0.625), (0.40, 0.625), (0.44, 0.89), (-0.44, 0.89)], 'AR_Trim', nu=4, nv=2)
    for z in (0.71, 0.80):
        quad(pm, bvh, 'F', [(-0.41, z), (0.41, z), (0.42, z + 0.018), (-0.42, z + 0.018)], V.paint, nu=4, proud=0.011)
    patch(pm, bvh, 'F', -0.72, 0.72, 0.41, 0.52, 'AR_Trim', nu=2, nv=1, proud=0.008)
    quad(pm, bvh, 'F', [(-0.40, 0.415), (0.40, 0.415), (0.36, 0.475), (-0.36, 0.475)], 'AR_Jant', nu=4, proud=0.013)
    lamp(pm, bvh, 'B', [(0.55, 0.93), (0.81, 0.93), (0.80, 1.135), (0.58, 1.135)],
         [('AR_Sinyal', 0, 0.25), ('AR_Stop', 0.25, 1)], nu=2, nv=1)
    patch(pm, bvh, 'B', -0.74, 0.74, 0.42, 0.53, 'AR_Trim', nu=2, nv=1, proud=0.008)
    quad(pm, bvh, 'B', [(-0.40, 0.425), (0.40, 0.425), (0.36, 0.48), (-0.36, 0.48)], 'AR_Jant', nu=4, proud=0.013)
    door_seams(V, pm, bvh, (1.36, 2.70, 3.34), 0.41, (1.105, 1.115, 1.12), (0.34 + 0.785 * 0.50,))
    handles(V, pm, bvh, (2.52, 3.18), 1.03, 1.06)
    # kaplamalar: kemer seritleri ve marspiyel
    for wh in V.wheels[::2]:
        Ra, zc, _ = wh['arch']
        arch_ring(pm, bvh, wh['y'], zc, Ra + 0.004, Ra + 0.065, 0.405, 'AR_Trim', n=9, proud=0.014)
    yfa = V.wheels[0]['y'] - V.wheels[0]['arch'][0] - 0.02
    yra = V.wheels[2]['y'] + V.wheels[2]['arch'][0] + 0.02
    patch_sides(pm, bvh, yra, yfa, 0.405, 0.475, 'AR_Trim', nu=4, proud=0.012)
    # tavan raylari
    for s in (-1, 1):
        x = s * 0.565
        for d0, d1 in ((2.20, 2.30), (3.92, 4.02)):
            hit = bvh.ray_cast(Vector((x, V.y((d0 + d1) / 2), 5)), Vector((0, 0, -1)), 10)[0]
            box(pm, (x, V.y((d0 + d1) / 2), hit.z + 0.022), (0.05, d1 - d0, 0.05), 'AR_Trim', 0.01)
        hit = bvh.ray_cast(Vector((x, V.y(3.1), 5)), Vector((0, 0, -1)), 10)[0]
        box(pm, (x, V.y(3.11), hit.z + 0.05), (0.042, 1.86, 0.03), 'AR_Trim', 0.009)
    mirror_pair(mm, bvh, V.y(1.50), 1.13, (0.13, 0.08, 0.115), V.paint)
    car_cols(V, belt=1.12, roof_hw=0.66, d_cowl=1.42, d_rear=4.25, roof=1.62, zlow=0.36)
    finish_vehicle(V, body, pm, mm, std_wheel_builder)
    return V


# ---------------------------------------------------------------------------
# 5) PICKUP - cift kabin, acik kasa
# ---------------------------------------------------------------------------
def build_pickup():
    V = Vehicle('Arac_Pickup', 'Pickup', 5.30, 1.86, 1.80, 3.10, 'ticari', 2000, 70, 'arka', (0, 0.25, 0.60))
    L, hw = V.L, V.W / 2 - 0.012
    P = dict(L=L, hw=hw, tuck=0.035, shoulder=0.03, ct=0.075, cb=0.05, zmf=0.50, cr=0.025, paint=V.paint,
             slots={'low': 'AR_Trim'})
    yf = V.y(0.90)
    V.axle(True, yf, 1.58, 0.37, 0.245, True, False, rim=0.58)
    V.axle(False, yf - V.wb, 1.58, 0.37, 0.245, False, True, rim=0.58)
    st = [
        S(0.07, 0.58, 0.98, 1.02, tp=0.10, cr=0.0),
        S(0.13, 0.55, 1.04, 1.075, tp=0.045, cr=0.010),
        S(0.30, 0.50, 1.075, 1.105, tp=0.015, cr=0.015),
        S(0.95, 0.48, 1.12, 1.15, cr=0.02),
        S(1.58, 0.48, 1.19, 1.215, wt=0.76, cr=0.02),
        S(2.22, 0.48, 1.235, 1.745, wt=0.71, cr=0.025),
        S(2.34, 0.48, 1.24, 1.77, wt=0.715, cr=0.025),
        S(2.50, 0.48, 1.245, 1.775, wt=0.715, cr=0.025),
        S(2.58, 0.48, 1.245, 1.775, wt=0.715, cr=0.025),
        S(3.22, 0.48, 1.245, 1.77, wt=0.715, cr=0.025),
        S(3.30, 0.48, 1.245, 1.755, wt=0.70, cr=0.02),
        S(3.32, 0.48, 1.245, 1.29, cr=0.004),
        S(5.21, 0.50, 1.245, 1.29, tp=0.015, cr=0.004),
        S(5.25, 0.52, 1.20, 1.25, tp=0.04, cr=0.0),
    ]
    roles = [{}, {}, {}, {},
             {'top': G('ws'), 'green': G('sF')}, {'green': G('sF')}, {'green': G('sF')},
             {'green': 'AR_Trim'}, {'green': G('sR')}, {}, {}, {}, {}]
    body = loft_body(V.short + '_kabuk', st, roles, P)
    cut = arch_cutters(V, gap=0.05)
    bed_hw = hw - 0.03 - 0.055
    box(cut, (0, (V.y(3.38) + V.y(5.18)) / 2, 1.30), (2 * bed_hw, 1.80, 0.64), 'AR_Kasa_Ici')
    apply_boolean(body, cut.to_object('kesici'))
    bvh = make_bvh(body)
    pm, mm = MB(), MB()
    # on tampon (koyu gri) ve izgara
    prism(pm, [(-0.90, V.y(0.15)), (0.90, V.y(0.15)), (0.93, V.y(0.30)), (0.89, V.y(0.04)), (0.80, V.y(0.0)),
               (-0.80, V.y(0.0)), (-0.89, V.y(0.04)), (-0.93, V.y(0.30))][::-1], 'z', 0.46, 0.70, 'AR_Trim', 0.02)
    patch(pm, bvh, 'F', -0.50, 0.50, 0.75, 0.98, 'AR_Trim', nu=4, nv=2, proud=0.010)
    patch(pm, bvh, 'F', -0.50, 0.50, 0.85, 0.875, 'AR_Krom', nu=4, nv=1, proud=0.016)
    patch_sym(pm, bvh, 'F', 0.54, 0.83, 0.86, 0.985, 'AR_Far', nu=2, nv=2, proud=0.010)
    patch_sym(pm, bvh, 'F', 0.54, 0.83, 0.815, 0.86, 'AR_Sinyal', nu=2, nv=1, proud=0.010)
    # arka: stoplar, bagaj kapagi kolu, kabin arka cami, tampon
    patch_sym(pm, bvh, 'B', 0.76, 0.875, 0.90, 1.21, 'AR_Stop', nu=1, nv=2, proud=0.010)
    patch_sym(pm, bvh, 'B', 0.76, 0.875, 0.84, 0.90, 'AR_Sinyal', nu=1, nv=1, proud=0.010)
    patch(pm, bvh, 'B', -0.12, 0.12, 1.13, 1.17, 'AR_Trim', proud=0.010)
    patch(pm, bvh, 'B', -0.56, 0.56, 1.36, 1.64, 'AR_Trim', nu=3, nv=1, proud=0.004)
    patch(pm, bvh, 'B', -0.53, 0.53, 1.385, 1.615, 'AR_Cam', nu=3, nv=1, proud=0.008)
    box(pm, (0, V.y(5.235), 0.60), (1.78, 0.13, 0.20), 'AR_Trim', 0.02)
    box(pm, (0, V.y(5.18), 0.705), (0.60, 0.20, 0.03), 'AR_Trim', 0.008)
    # kapi, kasa ayrim, bagaj kapagi cizgileri
    door_seams(V, pm, bvh, (1.64, 2.54, 3.25), 0.55, (1.20, 1.215, 1.215), (0.48 + 0.765 * 0.50,))
    seam(pm, bvh, V.y(3.335), 0.55, 1.28, zs_extra=(0.48 + 0.765 * 0.50,), w=0.016)
    seam(pm, bvh, V.y(5.20), arch_z0(V, V.y(5.20), 0.58), 1.28, zs_extra=(0.48 + 0.765 * 0.50,))
    handles(V, pm, bvh, (2.38, 3.10), 1.125, 1.16, w=0.15)
    # kemer kaplamalari ve basamak
    for wh in V.wheels[::2]:
        Ra, zc, _ = wh['arch']
        arch_ring(pm, bvh, wh['y'], zc, Ra + 0.004, Ra + 0.075, 0.545, 'AR_Trim', n=9, proud=0.016)
    for s in (-1, 1):
        box(pm, (s * 0.80, V.y(2.40), 0.43), (0.17, 1.60, 0.05), 'AR_Trim', 0.012)
        for d in (1.85, 2.95):
            box(pm, (s * 0.78, V.y(d), 0.47), (0.06, 0.06, 0.07), 'AR_Sasi', 0.0)
    # alt: arka diferansiyel ve aks
    cyl(pm, (0, V.wheels[2]['y'], 0.37), 'x', 0.065, 2 * (0.79 - 0.245 / 2 - 0.012), 10, 'AR_Sasi')
    box(pm, (0, V.wheels[2]['y'], 0.37), (0.30, 0.26, 0.24), 'AR_Sasi', 0.03)
    mirror_pair(mm, bvh, V.y(1.74), 1.265, (0.15, 0.09, 0.15), 'AR_Trim')
    inner = 0.79 - 0.245 / 2 - 0.03
    V.col('COL_Govde_Alt', (-inner, -L / 2 + 0.02, 0.42), (inner, L / 2 - 0.02, 1.25))
    V.col('COL_Govde_Ust', (-hw + 0.01, -L / 2 + 0.05, 0.77), (hw - 0.01, L / 2 - 0.05, 1.29))
    V.col('COL_Kabin', (-0.72, V.y(3.32), 1.24), (0.72, V.y(1.70), 1.80))
    finish_vehicle(V, body, pm, mm, std_wheel_builder)
    return V


# ---------------------------------------------------------------------------
# 6) HAFIF TICARI - yuksek tavanli kucuk panelvan
# ---------------------------------------------------------------------------
def build_hafif_ticari():
    V = Vehicle('Arac_HafifTicari', 'HafifTicari', 4.40, 1.82, 1.86, 2.75, 'ticari', 1500, 70, 'on', (0, 0.10, 0.58))
    L, hw = V.L, V.W / 2
    P = dict(L=L, hw=hw, tuck=0.03, shoulder=0.02, ct=0.07, cb=0.05, zmf=0.50, cr=0.03, paint=V.paint)
    yf = V.y(0.82)
    V.axle(True, yf, 1.54, 0.31, 0.195, True, True)
    V.axle(False, yf - V.wb, 1.56, 0.31, 0.195, False, False)
    st = [
        S(0.00, 0.32, 0.52, 0.555, tp=0.16, cr=0.0),
        S(0.06, 0.29, 0.60, 0.635, tp=0.08, cr=0.005),
        S(0.08, 0.29, 0.62, 0.655, tp=0.07, cr=0.005),
        S(0.17, 0.27, 0.84, 0.875, tp=0.035, cr=0.012),
        S(0.36, 0.27, 0.925, 0.96, tp=0.012, cr=0.02),
        S(0.86, 0.27, 1.04, 1.07, wt=0.80, cr=0.025),
        S(1.52, 0.27, 1.10, 1.74, wt=0.80, cr=0.03),
        S(1.64, 0.27, 1.105, 1.80, wt=0.815, cr=0.03),
        S(2.02, 0.27, 1.11, 1.83, wt=0.82, cr=0.03),
        S(2.10, 0.27, 1.11, 1.83, wt=0.82, cr=0.03),
        S(4.24, 0.28, 1.11, 1.83, wt=0.82, cr=0.03),
        S(4.33, 0.29, 1.10, 1.80, wt=0.81, tp=0.015, cr=0.028),
        S(4.37, 0.30, 1.08, 1.765, wt=0.80, tp=0.04, cr=0.025),
    ]
    roles = [TRIM, TRIM, {}, {}, {},
             {'top': G('ws'), 'green': G('sF')}, {'green': G('sF')}, {'green': G('sF')},
             {}, {}, {}, {}]
    body = loft_body(V.short + '_kabuk', st, roles, P, cap_f='AR_Trim')
    apply_boolean(body, arch_cutters(V).to_object('kesici'))
    bvh = make_bvh(body)
    pm, mm = MB(), MB()
    patch_sym(pm, bvh, 'F', 0.50, 0.79, 0.665, 0.855, 'AR_Far', nu=2, nv=2, proud=0.010)
    patch_sym(pm, bvh, 'F', 0.79, 0.855, 0.67, 0.80, 'AR_Sinyal', nu=1, nv=2, proud=0.010)
    patch(pm, bvh, 'F', -0.42, 0.42, 0.67, 0.84, 'AR_Trim', nu=4, nv=2, proud=0.007)
    # arka cift kapi: orta cizgi, camlar, kol; dik stoplar; tampon
    patch(pm, bvh, 'B', -0.005, 0.005, 0.56, 1.70, 'AR_Trim', nv=3, proud=0.0025, depth=0.01)
    for s in (-1, 1):
        q0, q1 = sorted((s * 0.07, s * 0.66))
        patch(pm, bvh, 'B', q0 - 0.022, q1 + 0.022, 1.16, 1.64, 'AR_Trim', proud=0.003, depth=0.015)
        patch(pm, bvh, 'B', q0, q1, 1.182, 1.618, 'AR_Cam', proud=0.007, depth=0.015)
    patch(pm, bvh, 'B', 0.03, 0.15, 1.03, 1.065, 'AR_Trim', proud=0.011)
    patch_sym(pm, bvh, 'B', 0.72, 0.855, 0.80, 1.06, 'AR_Stop', nu=1, nv=2, proud=0.010)
    patch_sym(pm, bvh, 'B', 0.72, 0.855, 0.62, 0.80, 'AR_Sinyal', nu=1, nv=1, proud=0.010)
    box(pm, (0, V.y(4.385), 0.42), (1.78, 0.06, 0.22), 'AR_Trim', 0.02)
    # kapilar: on kapi, surgulu kapi (iki yan), surgu rayi cizgisi
    zc = (0.27 + 1.11 * 0.5,)
    door_seams(V, pm, bvh, (1.20, 2.06), 0.33, (1.08, 1.10), zc)
    for d in (2.14, 3.08):
        seam(pm, bvh, V.y(d), arch_z0(V, V.y(d), 0.33), 1.70, zs_extra=zc + (1.11,))
    patch_sides(pm, bvh, V.y(3.08), V.y(2.14), 1.695, 1.705, 'AR_Trim', nu=3, proud=0.0025, depth=0.01)
    patch_sides(pm, bvh, V.y(4.22), V.y(3.08), 1.125, 1.14, 'AR_Trim', nu=3, proud=0.004, depth=0.01)
    handles(V, pm, bvh, (1.92, 2.26), 1.00, 1.035)
    # yan koruma citasi
    yfa = V.wheels[0]['y'] - V.wheels[0]['arch'][0] - 0.03
    yra = V.wheels[2]['y'] + V.wheels[2]['arch'][0] + 0.03
    patch_sides(pm, bvh, yra, yfa, 0.60, 0.665, 'AR_Trim', nu=4, proud=0.010)
    mirror_pair(mm, bvh, V.y(1.00), 1.12, (0.11, 0.085, 0.19), 'AR_Trim', stalk_len=0.07)
    car_cols(V, belt=1.10, roof_hw=0.84, d_cowl=0.95, d_rear=4.36, roof=1.86, zlow=0.28)
    finish_vehicle(V, body, pm, mm, std_wheel_builder)
    return V


# ---------------------------------------------------------------------------
# 7) MINIBUS - dolmus, yan cam sirasi
# ---------------------------------------------------------------------------
def build_minibus():
    V = Vehicle('Arac_Minibus', 'Minibus', 6.00, 2.02, 2.60, 3.66, 'ticari', 3200, 70, 'arka', (0, 0.15, 0.78))
    L, hw = V.L, V.W / 2
    P = dict(L=L, hw=hw, tuck=0.03, shoulder=0.02, ct=0.08, cb=0.05, zmf=0.50, cr=0.045, paint=V.paint)
    yf = V.y(0.95)
    V.axle(True, yf, 1.74, 0.36, 0.215, True, False, n=20)
    V.axle(False, yf - V.wb, 1.72, 0.36, 0.215, False, True, n=20)
    zw = 2.12
    base = dict(zb=0.34, zs=1.33, zt=2.555)
    st = [
        S(0.00, 0.40, 0.60, 0.64, tp=0.17, cr=0.0),
        S(0.07, 0.37, 0.69, 0.73, tp=0.09, cr=0.005),
        S(0.09, 0.37, 0.71, 0.75, tp=0.08, cr=0.005),
        S(0.22, 0.35, 0.98, 1.02, tp=0.035, cr=0.015),
        S(0.44, 0.34, 1.08, 1.12, tp=0.012, cr=0.025),
        S(0.90, 0.34, 1.21, 1.245, wt=0.91, cr=0.03),
        S(1.62, 0.34, 1.32, 2.28, wt=0.90, cr=0.04),
        S(1.82, 0.34, 1.325, 2.50, wt=0.92, cr=0.045),
        S(2.00, 0.34, 1.33, 2.555, wt=0.93),
        S(5.88, 0.35, 1.33, 2.555, wt=0.93),
        S(5.95, 0.37, 1.32, 2.52, wt=0.92, tp=0.015, cr=0.04),
        S(5.97, 0.38, 1.30, 2.48, wt=0.91, tp=0.035, cr=0.035),
    ]
    roles = [TRIM, TRIM, {}, {}, {}, {'top': G('ws')}, {}, {}, {}, {}, {}]
    body = loft_body(V.short + '_kabuk', st, roles, P, cap_f='AR_Trim')
    apply_boolean(body, arch_cutters(V).to_object('kesici'))
    bvh = make_bvh(body)
    pm, mm = MB(), MB()
    # on
    patch_sym(pm, bvh, 'F', 0.58, 0.90, 0.80, 0.965, 'AR_Far', nu=2, nv=2, proud=0.010)
    patch(pm, bvh, 'F', -0.54, 0.54, 0.77, 0.97, 'AR_Trim', nu=4, nv=2, proud=0.007)
    patch_sym(pm, bvh, 'F', 0.72, 0.88, 0.585, 0.635, 'AR_Sinyal', nu=1, nv=1, proud=0.010)
    # yan camlar: on kapi (A diregi egimli), yolcu camlari
    yA0, yA1 = V.y(1.16), V.y(1.68)
    win_patch(pm, bvh, [(yA0, 1.40), (V.y(1.98), 1.40), (V.y(1.98), zw), (yA1, zw)])
    for d0, d1 in ((2.12, 2.94), (3.04, 3.86), (3.96, 4.78), (4.88, 5.66)):
        win_patch(pm, bvh, [(V.y(d0), 1.40), (V.y(d1), 1.40), (V.y(d1), zw), (V.y(d0), zw)])
    # kapilar: on kapilar iki yanda, surgulu kapi sagda
    zc = (0.34 + 1.33 * 0.5, 1.33)
    for d, zt in ((1.40, 1.37), (2.03, 2.20)):
        seam(pm, bvh, V.y(d), arch_z0(V, V.y(d), 0.40), zt, zs_extra=zc)
    for d in (2.06, 3.00):
        y = V.y(d)
        patch(pm, bvh, 'R', y - 0.0045, y + 0.0045, 0, 0, 'AR_Trim', vs=[arch_z0(V, y, 0.40), zc[0], 1.33, 2.20],
              proud=0.0025, depth=0.01)
    patch(pm, bvh, 'R', V.y(3.00), V.y(2.06), 2.195, 2.205, 'AR_Trim', nu=3, proud=0.0025, depth=0.01)
    patch(pm, bvh, 'R', V.y(4.90), V.y(3.00), 1.36, 1.375, 'AR_Trim', nu=3, proud=0.004, depth=0.01)
    patch_sides(pm, bvh, V.y(1.92), V.y(1.80), 1.20, 1.235, 'AR_Trim', proud=0.011)
    patch(pm, bvh, 'R', V.y(2.30), V.y(2.16), 1.20, 1.235, 'AR_Trim', proud=0.011)
    # basamak (surgulu kapi alti)
    box(pm, (hw - 0.17, V.y(2.53), 0.32), (0.30, 0.86, 0.05), 'AR_Trim', 0.012)
    # yan koruma citasi
    yfa = V.wheels[0]['y'] - V.wheels[0]['arch'][0] - 0.03
    yra = V.wheels[2]['y'] + V.wheels[2]['arch'][0] + 0.03
    patch_sides(pm, bvh, yra, yfa, 0.62, 0.70, 'AR_Trim', nu=4, proud=0.010)
    # arka: cift kapi, camlar, dik stoplar, tampon
    patch(pm, bvh, 'B', -0.005, 0.005, 0.62, 2.30, 'AR_Trim', nv=3, proud=0.0025, depth=0.01)
    for s in (-1, 1):
        q0, q1 = sorted((s * 0.08, s * 0.72))
        patch(pm, bvh, 'B', q0 - 0.024, q1 + 0.024, 1.48, 2.12, 'AR_Trim', proud=0.003, depth=0.015)
        patch(pm, bvh, 'B', q0, q1, 1.504, 2.096, 'AR_Cam', proud=0.007, depth=0.015)
    patch(pm, bvh, 'B', 0.04, 0.17, 1.24, 1.28, 'AR_Trim', proud=0.011)
    patch_sym(pm, bvh, 'B', 0.80, 0.955, 1.00, 1.36, 'AR_Stop', nu=1, nv=2, proud=0.010)
    patch_sym(pm, bvh, 'B', 0.80, 0.955, 0.80, 1.00, 'AR_Sinyal', nu=1, nv=1, proud=0.010)
    box(pm, (0, V.y(5.985), 0.50), (1.96, 0.07, 0.24), 'AR_Trim', 0.02)
    box(pm, (0, V.y(5.92), 0.40), (1.20, 0.16, 0.05), 'AR_Trim', 0.01)
    mirror_pair(mm, bvh, V.y(1.08), 1.42, (0.12, 0.10, 0.30), 'AR_Trim', stalk_len=0.10)
    car_cols(V, belt=1.33, roof_hw=0.95, d_cowl=1.00, d_rear=5.96, roof=2.60, zlow=0.34)
    finish_vehicle(V, body, pm, mm, std_wheel_builder)
    return V


# ---------------------------------------------------------------------------
# 8) KAMYON - kabin onde (COE) + kapali kutu kasa, arkada cift lastik
# ---------------------------------------------------------------------------
def truck_wheel_builder(V, wh, side):
    r, w = wh['r'], wh['w']
    if wh.get('dual'):
        prof = dual_wheel_profile(r, w, r * 0.56)
    else:
        prof = car_wheel_profile(r, w, r * 0.56, dish=0.075)
    return make_wheel('w', (wh['x'], wh['y'], r), side, prof, 20)


def build_kamyon():
    V = Vehicle('Arac_Kamyon', 'Kamyon', 7.20, 2.30, 3.30, 4.00, 'agir', 7500, 60, 'arka', (0, -0.25, 0.95))
    L = V.L
    hwc = 1.11
    P = dict(L=L, hw=hwc, tuck=0.02, shoulder=0.015, ct=0.08, cb=0.04, zmf=0.45, cr=0.03, paint=V.paint)
    yf = V.y(1.30)
    V.axle(True, yf, 2.00, 0.48, 0.295, True, False)
    V.axle(False, yf - V.wb, 1.68, 0.48, 0.60, False, True, dual=True)
    st = [
        S(0.07, 0.86, 1.55, 1.60, tp=0.07, cr=0.0, wt=1.02),
        S(0.11, 0.84, 1.60, 1.66, tp=0.04, cr=0.010, wt=1.02),
        S(0.20, 0.82, 1.64, 2.62, wt=1.02, cr=0.03),
        S(0.30, 0.82, 1.65, 2.74, wt=1.03, cr=0.035),
        S(1.80, 0.82, 1.66, 2.74, wt=1.03, tp=0.01, cr=0.035),
        S(1.88, 0.82, 1.64, 2.66, wt=1.00, tp=0.03, cr=0.03),
    ]
    roles = [{}, {'top': G('ws')}, {}, {}, {}]
    body = loft_body(V.short + '_kabuk', st, roles, P)
    apply_boolean(body, arch_cutters(V, gap=0.065, only=('Teker_OnSol', 'Teker_OnSag')).to_object('kesici'))
    bvh = make_bvh(body)
    pm, mm = MB(), MB()
    y = V.y
    # kabin onu: izgara, gunesluk, tampon + farlar, basamaklar, kapi camlari
    patch(pm, bvh, 'F', -0.82, 0.82, 1.02, 1.48, 'AR_Trim', nu=4, nv=2, proud=0.008)
    for z in (1.17, 1.33):
        patch(pm, bvh, 'F', -0.82, 0.82, z, z + 0.035, V.paint, nu=4, nv=1, proud=0.014)
    box(pm, (0, y(0.17), 2.70), (2.04, 0.22, 0.05), 'AR_Trim', 0.012)
    prism(pm, [(-1.10, y(0.20)), (1.10, y(0.20)), (1.12, y(0.07)), (1.06, y(0.0)), (-1.06, y(0.0)),
               (-1.12, y(0.07))][::-1], 'z', 0.50, 0.86, 'AR_Trim', 0.025)
    for s in (-1, 1):
        box(pm, (s * 0.82, y(0.0) + 0.002, 0.74), (0.32, 0.012, 0.10), 'AR_Far', 0.004)
        box(pm, (s * 1.02, y(0.0) + 0.002, 0.74), (0.08, 0.012, 0.10), 'AR_Sinyal', 0.004)
        for z in (0.48, 0.74):
            box(pm, (s * 0.98, y(0.52), z), (0.22, 0.34, 0.035), 'AR_Trim', 0.008)
        box(pm, (s * 1.085, y(0.52), 0.64), (0.035, 0.36, 0.34), 'AR_Sasi', 0.008)
    win_patch(pm, bvh, [(y(0.36), 1.76), (y(1.10), 1.76), (y(1.10), 2.50), (y(0.36), 2.50)])
    for d in (0.30, 1.22):
        seam(pm, bvh, y(d), arch_z0(V, y(d), 0.88), 2.60, zs_extra=(0.82 + 0.84 * 0.45, 1.65))
    handles(V, pm, bvh, (1.05,), 1.60, 1.64, w=0.16)
    mirror_pair(mm, bvh, y(0.26), 1.95, (0.10, 0.08, 0.42), 'AR_Trim', stalk_len=0.16, arm_z=0.10)
    # sasi
    for s in (-1, 1):
        box(pm, (s * 0.435, y(3.85), 0.76), (0.08, 6.50, 0.26), 'AR_Sasi', 0.0)
    for d in (1.5, 2.6, 4.2, 6.2, 7.0):
        box(pm, (0, y(d), 0.76), (0.80, 0.10, 0.18), 'AR_Sasi', 0.0)
    # on aks kirisi ve arka aks govdesi
    box(pm, (0, V.wheels[0]['y'], 0.48), (1.10, 0.14, 0.14), 'AR_Sasi', 0.0)
    cyl(pm, (0, V.wheels[2]['y'], 0.48), 'x', 0.10, 2 * (0.84 - 0.30 - 0.015), 10, 'AR_Sasi')
    box(pm, (0, V.wheels[2]['y'], 0.48), (0.44, 0.40, 0.38), 'AR_Sasi', 0.04)
    # yakit deposu (sol), aku kutusu (sag), yan koruma
    prism(pm, [(y(2.30), 0.56), (y(3.30), 0.56), (y(3.30), 0.98), (y(2.30), 0.98)], 'x', -1.02, -0.50, 'AR_Jant', 0.04)
    box(pm, (0.76, y(2.65), 0.80), (0.48, 0.60, 0.34), 'AR_Sasi', 0.02)
    for s in (-1, 1):
        for z in (0.52, 0.80):
            box(pm, (s * 1.115, y(3.325), z), (0.03, 2.85, 0.09), 'AR_Sasi', 0.008)
        for d in (2.10, 3.30, 4.55):
            box(pm, (s * 0.80, y(d), 0.70), (0.60, 0.05, 0.05), 'AR_Sasi', 0.0)
    # kutu kasa
    kb0, kb1 = 2.00, 7.14
    box(pm, (0, y((kb0 + kb1) / 2), 2.22), (2.30, kb1 - kb0, 2.16), 'AR_Kasa', 0.035)
    for s in (-1, 1):
        box(pm, (s * 0.46, y((kb0 + kb1) / 2), 1.04), (0.12, kb1 - kb0, 0.12), 'AR_Sasi', 0.0)
    box(pm, (0, y((kb0 + kb1) / 2), 1.16), (2.30, kb1 - kb0 + 0.02, 0.12), 'AR_Trim', 0.015)
    for d in (kb0 + 0.04,):
        for s in (-1, 1):
            box(pm, (s * 1.135, y(d), 2.22), (0.03, 0.08, 2.16), 'AR_Sasi', 0.008)
    # arka cerceve ve kapilar
    yb = y(7.20)
    box(pm, (0, yb + 0.04, 3.255), (2.30, 0.08, 0.09), 'AR_Sasi', 0.012)
    box(pm, (0, yb + 0.04, 1.185), (2.30, 0.08, 0.13), 'AR_Sasi', 0.012)
    for s in (-1, 1):
        box(pm, (s * 1.105, yb + 0.04, 2.22), (0.09, 0.08, 2.00), 'AR_Sasi', 0.012)
    box(pm, (0, yb + 0.05, 2.22), (2.12, 0.04, 1.98), 'AR_Kasa', 0.006)
    box(pm, (0, yb + 0.028, 2.22), (0.012, 0.012, 1.96), 'AR_Trim', 0.0)
    for x in (-0.72, -0.30, 0.30, 0.72):
        box(pm, (x, yb + 0.022, 2.22), (0.035, 0.03, 1.94), 'AR_Jant', 0.006)
    # arka: alt koruma, stoplar, camurluklar ve paspaslar
    box(pm, (0, y(7.10), 0.56), (2.00, 0.10, 0.12), 'AR_Sasi', 0.01)
    for s in (-1, 1):
        box(pm, (s * 0.40, y(7.06), 0.76), (0.08, 0.08, 0.30), 'AR_Sasi', 0.0)
        box(pm, (s * 0.80, y(7.10), 0.95), (0.56, 0.08, 0.14), 'AR_Sasi', 0.01)
        box(pm, (s * 0.86, y(7.15), 0.95), (0.26, 0.03, 0.09), 'AR_Stop', 0.006)
        box(pm, (s * 1.02, y(7.15), 0.95), (0.07, 0.03, 0.09), 'AR_Sinyal', 0.005)
        box(pm, (s * 0.85, V.wheels[2]['y'], 1.035), (0.58, 1.30, 0.05), 'AR_Trim', 0.012)
        box(pm, (s * 0.86, V.wheels[2]['y'] - 0.62, 0.70), (0.58, 0.02, 0.62), 'AR_Trim', 0.006)
    V.col('COL_Govde_Alt', (-0.50, -L / 2 + 0.05, 0.40), (0.50, L / 2 - 0.02, 1.12))
    V.col('COL_Kabin', (-hwc, y(1.92), 0.99), (hwc, y(0.02), 2.80))
    V.col('COL_Kasa', (-1.15, -L / 2, 1.10), (1.15, y(kb0), 3.30))
    finish_vehicle(V, body, pm, mm, truck_wheel_builder)
    return V


# ---------------------------------------------------------------------------
# 9) TRAKTOR - kabinli tarla/bahce traktoru
# ---------------------------------------------------------------------------
def tractor_wheel_builder(V, wh, side):
    r, w = wh['r'], wh['w']
    if wh.get('rear'):
        lug = 0.055
        prof = tractor_rear_profile(r, w, r * 0.53, lug)
        return make_wheel('w', (wh['x'], wh['y'], r), side, prof, 20,
                          extra=lambda mb: tractor_lugs(mb, r, w, lug, side, 16))
    prof = tractor_front_profile(r, w, r * 0.52)
    return make_wheel('w', (wh['x'], wh['y'], r), side, prof, 16)


def build_traktor():
    V = Vehicle('Arac_Traktor', 'Traktor', 3.90, 2.00, 2.70, 2.25, 'traktor', 3200, 25, 'arka', (0, -0.35, 0.78))
    V.paint = 'AR_Traktor_Boya'
    L = V.L
    y = V.y
    yf = y(0.80)
    V.axle(True, yf, 1.50, 0.40, 0.235, True, False)
    V.axle(False, yf - V.wb, 1.58, 0.75, 0.42, False, True, rear=True)
    yr = V.wheels[2]['y']
    # kaput (dar loft)
    P = dict(L=L, hw=0.32, tuck=0.02, shoulder=0.012, ct=0.05, cb=0.03, zmf=0.50, cr=0.015, paint=V.paint)
    st = [
        S(0.16, 0.74, 1.30, 1.34, tp=0.03, cr=0.008),
        S(0.22, 0.73, 1.34, 1.38, tp=0.008, cr=0.012),
        S(1.10, 0.73, 1.40, 1.43, cr=0.015),
        S(1.86, 0.73, 1.43, 1.46, cr=0.015),
    ]
    body = loft_body(V.short + '_kaput', st, [{}, {}, {}], P, cap_f='AR_Trim')
    bvh = make_bvh(body)
    pm, mm = MB(), MB()
    pnt = V.paint
    # izgara cercevesi + farlar, kaput yan menfezleri
    patch(pm, bvh, 'F', -0.26, 0.26, 0.80, 1.31, 'AR_Trim', nu=2, nv=3, proud=0.006)
    patch_sym(pm, bvh, 'F', 0.12, 0.25, 1.15, 1.26, 'AR_Far', proud=0.016)
    for d0, d1 in ((0.30, 0.46), (0.52, 0.68)):
        patch_sides(pm, bvh, y(d1), y(d0), 0.96, 1.26, 'AR_Trim', proud=0.006)
    # on agirlik, motor blogu, on aks
    box(pm, (0, y(0.10), 0.57), (0.70, 0.22, 0.34), 'AR_Sasi', 0.03)
    box(pm, (0, y(0.20), 0.80), (0.40, 0.10, 0.18), 'AR_Sasi', 0.01)
    box(pm, (0, y(1.08), 0.60), (0.52, 1.80, 0.36), 'AR_Sasi', 0.03)
    box(pm, (0, yf, 0.43), (1.06, 0.13, 0.13), 'AR_Sasi', 0.0)
    box(pm, (0, yf, 0.52), (0.22, 0.24, 0.12), 'AR_Sasi', 0.01)
    # sanziman / arka aks govdesi
    box(pm, (0, y(2.70), 0.86), (0.66, 1.50, 0.62), 'AR_Sasi', 0.04)
    cyl(pm, (0, yr, 0.75), 'x', 0.11, 2 * (0.79 - 0.21 - 0.015), 10, 'AR_Sasi')
    # camurluklar (arka tekerleri saran yay seritleri)
    th0, th1 = math.radians(18), math.radians(165)
    for s in (-1, 1):
        ang = lin(th0, th1, 10)
        outer = [(yr + 0.86 * math.cos(t), 0.75 + 0.86 * math.sin(t)) for t in ang]
        inner = [(yr + 0.80 * math.cos(t), 0.75 + 0.80 * math.sin(t)) for t in reversed(ang)]
        x0, x1 = sorted((s * 0.50, s * 1.005))
        prism(pm, outer + inner, 'x', x0, x1, pnt, 0.012)
        # camurluk ic yanagi (kabin alt yan paneli)
        side_pts = [(yr + 0.80 * math.cos(t), 0.75 + 0.80 * math.sin(t)) for t in ang]
        side_pts += [(y(3.30), 1.20), (y(1.92), 1.20)]
        x0, x1 = sorted((s * 0.50, s * 0.53))
        prism(pm, side_pts[::-1], 'x', x0, x1, pnt, 0.0)
        # stop + sinyal (camurluk arka ucu)
        t = math.radians(150)
        box(pm, (s * 0.92, yr + 0.88 * math.cos(t), 0.75 + 0.88 * math.sin(t)), (0.10, 0.04, 0.07), 'AR_Stop', 0.006)
        box(pm, (s * 0.80, yr + 0.88 * math.cos(t) + 0.01, 0.75 + 0.88 * math.sin(t) + 0.02), (0.08, 0.04, 0.06), 'AR_Sinyal', 0.006)
    # kabin: taban, alt govde, cam ev, direkler, tavan
    box(pm, (0, y(2.62), 1.15), (1.10, 1.56, 0.10), 'AR_Sasi', 0.01)
    box(pm, (0, y(2.58), 1.40), (1.00, 1.40, 0.42), pnt, 0.02)
    cz0, cz1 = 1.60, 2.60
    cd0, cd1 = 1.92, 3.30
    box(pm, (0, y((cd0 + cd1) / 2), (cz0 + cz1) / 2), (1.27, cd1 - cd0 - 0.03, cz1 - cz0), 'AR_Cam', 0.0)
    for sx in (-1, 1):
        for d in (cd0, cd1):
            box(pm, (sx * 0.635, y(d), (cz0 + cz1) / 2), (0.075, 0.075, cz1 - cz0 + 0.02), pnt, 0.012)
        box(pm, (sx * 0.635, y(2.30), (cz0 + cz1) / 2), (0.05, 0.05, cz1 - cz0), pnt, 0.008)
        box(pm, (sx * 0.635, y((cd0 + cd1) / 2), cz0 + 0.03), (0.07, cd1 - cd0, 0.07), pnt, 0.01)
    box(pm, (0, y(cd0), cz0 + 0.03), (1.30, 0.07, 0.07), pnt, 0.01)
    box(pm, (0, y(cd1), cz0 + 0.03), (1.30, 0.07, 0.07), pnt, 0.01)
    box(pm, (0, y((cd0 + cd1) / 2), 2.655), (1.42, cd1 - cd0 + 0.18, 0.09), pnt, 0.03)
    box(pm, (0, y((cd0 + cd1) / 2), 2.605), (1.34, cd1 - cd0 + 0.08, 0.03), 'AR_Trim', 0.008)
    # kapi cizgisi (sol), kapi kolu
    # tavan is lambalari
    for sx in (-1, 1):
        for d in (cd0 - 0.02, cd1 + 0.02):
            box(pm, (sx * 0.50, y(d) + (0.10 if d < 2.5 else -0.10), 2.655), (0.10, 0.05, 0.06), 'AR_Far' if d < 2.5 else 'AR_Trim', 0.008)
    # egzoz (sag on)
    rod(pm, (0.25, y(1.78), 1.40), (0.25, y(1.78), 2.58), 0.042, 8, 'AR_Trim')
    rod(pm, (0.25, y(1.78), 1.62), (0.25, y(1.78), 2.00), 0.062, 8, 'AR_Sasi')
    # basamaklar (sol)
    for z in (0.62, 0.90):
        box(pm, (-0.62, y(2.10), z), (0.20, 0.24, 0.035), 'AR_Trim', 0.006)
    box(pm, (-0.73, y(2.10), 0.80), (0.025, 0.26, 0.48), 'AR_Sasi', 0.005)
    # arka uc donanimi (3 nokta askisi + cekme demiri)
    for s in (-1, 1):
        rod(pm, (s * 0.30, y(3.30), 0.60), (s * 0.36, y(3.86), 0.46), 0.035, 6, 'AR_Sasi')
    rod(pm, (0, y(3.40), 1.10), (0, y(3.80), 0.92), 0.035, 6, 'AR_Sasi')
    box(pm, (0, y(3.68), 0.48), (0.22, 0.44, 0.05), 'AR_Sasi', 0.008)
    box(pm, (0, y(3.47), 0.80), (0.20, 0.06, 0.10), 'AR_Sasi', 0.0)
    for sx in (-1, 1):
        rod(mm, (sx * 0.66, y(1.93), 2.30), (sx * 0.90, y(1.98), 2.30), 0.014, 6, 'AR_Trim')
        box(mm, (sx * 0.93, y(1.98), 2.26), (0.07, 0.05, 0.20), 'AR_Trim', 0.01)
        box(mm, (sx * 0.93, y(1.98) - 0.027, 2.26), (0.055, 0.008, 0.16), 'AR_Cam', 0.002)
    V.col('COL_Govde_Alt', (-0.34, -L / 2 + 0.03, 0.40), (0.34, L / 2, 1.46))
    V.col('COL_Kabin', (-0.70, y(3.38), 1.62), (0.70, y(1.80), 2.70))
    finish_vehicle(V, body, pm, mm, tractor_wheel_builder)
    return V


BUILDERS = [
    ('Hatchback', build_hatchback),
    ('Sedan', lambda: build_sedan(False)),
    ('Taksi', lambda: build_sedan(True)),
    ('SUV', build_suv),
    ('Pickup', build_pickup),
    ('HafifTicari', build_hafif_ticari),
    ('Minibus', build_minibus),
    ('Kamyon', build_kamyon),
    ('Traktor', build_traktor),
]


# ---------------------------------------------------------------------------
# Olcum, JSON, FBX
# ---------------------------------------------------------------------------
def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def u(v):
    """Blender (x,y,z) -> Unity [x, y(yukari), z(ileri)]."""
    return [round(v[0], 4), round(v[2], 4), round(v[1], 4)]


def world_bbox(objs, skip_group=None):
    pts = []
    for o in objs:
        me = o.data
        gi = o.vertex_groups[skip_group].index if skip_group and skip_group in o.vertex_groups else None
        for v in me.vertices:
            if gi is not None and any(g.group == gi for g in v.groups):
                continue
            pts.append(o.matrix_world @ v.co)
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


VEHICLES = []
for short, fn in BUILDERS:
    if ONLY and short not in ONLY:
        continue
    COLL = bpy.data.collections.new('Arac_' + short)
    SCENE.collection.children.link(COLL)
    V = fn()
    VEHICLES.append(V)
    vis = [o for o in V.objects if not o['ad'].startswith('COL_')]
    print('KURULDU', V.id, 'ucgen', sum(tri_count(o) for o in vis))

bpy.context.view_layer.update()
manifest = {'set': 'Araclar', 'eksen': 'Unity yerel [x, y(yukari), z(ileri)]; on = +Z, sag = +X, lastik alti y=0',
            'araclar': []}
for V in VEHICLES:
    vis = [o for o in V.objects if not o['ad'].startswith('COL_')]
    mn, mx = world_bbox(vis, 'Ayna')
    cmn, cmx = world_bbox(vis)
    size = mx - mn
    ent = {
        'id': V.id,
        'fbx': V.id + '.fbx',
        'boyut': [round(size.x, 3), round(size.z, 3), round(size.y, 3)],
        'boyut_not': 'aynalar haric (genislik/yukseklik/uzunluk)',
        'ucgen': sum(tri_count(o) for o in vis),
        'tip': V.tip,
        'kutle_kg': V.mass,
        'azami_hiz_kmh': V.vmax,
        'aks_mesafesi': V.wb,
        'agirlik_merkezi': u(V.com),
        'ceker': V.drive,
        'tekerler': [{
            'ad': wh['ad'],
            'konum': u((wh['x'], wh['y'], wh['r'])),
            'yaricap': wh['r'],
            'genislik': wh['w'],
            'direksiyon': wh['steer'],
            'ceker': wh['drive'],
        } for wh in V.wheels],
        'boya_malzemesi': V.paint,
        'far_malzemesi': 'AR_Far',
        'stop_malzemesi': 'AR_Stop',
        'sinyal_malzemesi': 'AR_Sinyal',
        'cerceve': {'min': u(cmn), 'max': u(cmx)},
        'carpisma_kutulari': [{'ad': ad, 'min': u(a), 'max': u(b)} for ad, a, b in V.cols],
        'nesneler': [o['ad'] for o in V.objects],
    }
    manifest['araclar'].append(ent)
    print('BOYUT', V.id, ent['boyut'], 'ucgen', ent['ucgen'])

if not NO_EXPORT:
    OUT.mkdir(parents=True, exist_ok=True)
    mats = []
    for name, col, met, smo, em, rnd in MAT_DEFS:
        d = {'name': name, 'color': list(col), 'metallic': met, 'smoothness': smo,
             'emission': list(em), 'alpha': 1.0}
        if rnd:
            d['randomize'] = rnd
        mats.append(d)
    (OUT / 'Araclar_malzemeler.json').write_text(
        json.dumps({'materials': mats, 'palettes': PALETTES}, ensure_ascii=False, indent=2), encoding='utf-8')
    if not ONLY:
        (OUT / 'Araclar_manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    for V in VEHICLES:
        for o in V.objects:
            o.name = o['ad']
        bpy.ops.object.select_all(action='DESELECT')
        for o in V.objects:
            o.select_set(True)
        bpy.context.view_layer.objects.active = V.objects[0]
        path = OUT / (V.id + '.fbx')
        bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'MESH', 'EMPTY'},
                                 axis_forward='Z', axis_up='Y', apply_unit_scale=True,
                                 apply_scale_options='FBX_SCALE_ALL', use_space_transform=True,
                                 bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='FACE',
                                 use_triangles=True, add_leaf_bones=False, bake_anim=False, path_mode='AUTO')
        for o in V.objects:
            o.name = V.short + '_' + o['ad']
        print('FBX', path)

BLEND.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
print('BLEND', BLEND)
