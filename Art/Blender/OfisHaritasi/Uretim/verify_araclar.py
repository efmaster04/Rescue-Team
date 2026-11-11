# -*- coding: utf-8 -*-
"""Araclar seti kontrolu.

  blender -b Kaynak/Blender/Araclar.blend -P verify_araclar.py

Kontroller: olculer (%3), teker orijini/yaricap/lastik alti Z=0/donel simetri,
+-35 derece direksiyon temizligi (BVH), COL_ kutulari (eksen hizasi, taban >= 0.25,
tekerlek kapsamama), kapali kabuk + disa donuk normal, ucgen butcesi, FONT yok,
malzemeler JSON'da, manifest tutarliligi, FBX yeniden ice aktarma (ad + olcu).
"""
import bpy
import json
import math
import sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ROOT = Path(r'C:\Users\uur\RescueTeam_OfisHaritasi')
OUT = ROOT / 'Oyun' / 'Assets' / 'OfisHaritasi' / 'Modeller' / 'Araclar'

# Brif hedefleri: uzunluk, genislik, yukseklik, aks, on r, arka r, ucgen alt/ust
TARGETS = {
    'Hatchback':   ('Arac_Hatchback',   3.95, 1.74, 1.48, 2.50, 0.30, 0.30, 2500, 5000),
    'Sedan':       ('Arac_Sedan',       4.50, 1.78, 1.45, 2.64, 0.31, 0.31, 2500, 5000),
    'Taksi':       ('Arac_Taksi',       4.50, 1.78, 1.61, 2.64, 0.31, 0.31, 2500, 5000),
    'SUV':         ('Arac_SUV',         4.40, 1.82, 1.66, 2.62, 0.34, 0.34, 2500, 5000),
    'Pickup':      ('Arac_Pickup',      5.30, 1.86, 1.80, 3.10, 0.37, 0.37, 2500, 5000),
    'HafifTicari': ('Arac_HafifTicari', 4.40, 1.82, 1.86, 2.75, 0.31, 0.31, 2500, 5000),
    'Minibus':     ('Arac_Minibus',     6.00, 2.02, 2.60, 3.66, 0.36, 0.36, 0, 7000),
    'Kamyon':      ('Arac_Kamyon',      7.20, 2.30, 3.30, 4.00, 0.48, 0.48, 0, 7000),
    'Traktor':     ('Arac_Traktor',     3.90, 2.00, 2.70, 2.25, 0.40, 0.75, 0, 6000),
}
WHEELS = ['Teker_OnSol', 'Teker_OnSag', 'Teker_ArkaSol', 'Teker_ArkaSag']
REQUIRED_MATS = ['AR_Boya', 'AR_Cam', 'AR_Trim', 'AR_Krom', 'AR_Far', 'AR_Stop', 'AR_Sinyal', 'AR_Lastik',
                 'AR_Jant', 'AR_Sasi', 'AR_Taksi_Sari', 'AR_Taksi_Lamba', 'AR_Kasa', 'AR_Kasa_Ici',
                 'AR_Traktor_Boya']
STEER = list(range(-35, 36, 5))

fails = []
rows = []


def check(ok, msg):
    if not ok:
        fails.append(msg)
    return ok


def tris(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def world_verts(o, M=None):
    M = M or o.matrix_world
    return [M @ v.co for v in o.data.vertices]


def bvh_of(o, M=None):
    return BVHTree.FromPolygons(world_verts(o, M), [tuple(p.vertices) for p in o.data.polygons])


def bbox(pts):
    return (Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))),
            Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))))


def shell_report(o):
    """Kenar basina yuz sayisi (manifold) ve ada basina isaretli hacim (normal yonu)."""
    me = o.data
    ec = {}
    for p in me.polygons:
        for k in p.edge_keys:
            ec[k] = ec.get(k, 0) + 1
    bad_edges = sum(1 for c in ec.values() if c != 2)
    parent = list(range(len(me.vertices)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    for p in me.polygons:
        vs = list(p.vertices)
        for v in vs[1:]:
            ra, rb = find(vs[0]), find(v)
            if ra != rb:
                parent[ra] = rb
    vol = {}
    co = [v.co for v in me.vertices]
    for p in me.polygons:
        vs = list(p.vertices)
        r = find(vs[0])
        s = 0.0
        for i in range(1, len(vs) - 1):
            s += co[vs[0]].dot(co[vs[i]].cross(co[vs[i + 1]])) / 6.0
        vol[r] = vol.get(r, 0.0) + s
    neg = sum(1 for v in vol.values() if v < -1e-7)
    return bad_edges, len(vol), neg


# ---------------------------------------------------------------------------
# JSON dosyalari
# ---------------------------------------------------------------------------
matj = json.loads((OUT / 'Araclar_malzemeler.json').read_text(encoding='utf-8'))
manj = json.loads((OUT / 'Araclar_manifest.json').read_text(encoding='utf-8'))
json_mats = {m['name']: m for m in matj['materials']}
for n in REQUIRED_MATS:
    check(n in json_mats, f'malzeme JSON eksik: {n}')
for m in matj['materials']:
    for k in ('name', 'color', 'metallic', 'smoothness', 'emission', 'alpha'):
        check(k in m, f'malzeme alani eksik {m.get("name")}: {k}')
pal = matj.get('palettes', {})
check(len(pal.get('arac_boya', [])) == 12, 'palet arac_boya 12 renk degil')
check(len(pal.get('kamyon_kasa', [])) == 4, 'palet kamyon_kasa 4 renk degil')
check(len(pal.get('traktor_boya', [])) == 3, 'palet traktor_boya 3 renk degil')
check(json_mats['AR_Boya'].get('randomize') == 'arac_boya', 'AR_Boya randomize degil')
check(json_mats['AR_Kasa'].get('randomize') == 'kamyon_kasa', 'AR_Kasa randomize degil')
check(json_mats['AR_Traktor_Boya'].get('randomize') == 'traktor_boya', 'AR_Traktor_Boya randomize degil')
check('randomize' not in json_mats['AR_Taksi_Sari'], 'AR_Taksi_Sari randomize olmamali')
check(json_mats['AR_Stop']['emission'] == [0, 0, 0], 'AR_Stop emission 0 olmali')
man = {a['id']: a for a in manj['araclar']}

# ---------------------------------------------------------------------------
# .blend kontrolleri
# ---------------------------------------------------------------------------
check(not any(o.type == 'FONT' for o in bpy.data.objects), 'FONT nesnesi var')
blend_meas = {}
for short, (vid, L, W, H, wb, rf, rr, tmin, tmax) in TARGETS.items():
    col = bpy.data.collections.get('Arac_' + short)
    if not check(col is not None, f'{short}: koleksiyon yok'):
        continue
    objs = {o.get('ad'): o for o in col.objects}
    check(all(o.type == 'MESH' for o in col.objects), f'{short}: MESH olmayan nesne')
    need = ['Govde'] + WHEELS + ['COL_Govde_Alt', 'COL_Kabin']
    for n in need:
        check(n in objs, f'{short}: {n} yok')
    if any(n not in objs for n in need):
        continue
    gov = objs['Govde']
    wheels = [objs[n] for n in WHEELS]
    cols = [o for n, o in objs.items() if n.startswith('COL_')]
    ent = man.get(vid)
    check(ent is not None, f'{short}: manifestte yok')
    # donus/olcek
    for o in col.objects:
        check(all(abs(a) < 1e-6 for a in o.rotation_euler), f'{short}/{o["ad"]}: donus 0 degil')
        check(all(abs(s - 1) < 1e-6 for s in o.scale), f'{short}/{o["ad"]}: olcek 1 degil')
    # olculer (aynalar haric)
    gi = gov.vertex_groups['Ayna'].index if 'Ayna' in gov.vertex_groups else None
    pts = [gov.matrix_world @ v.co for v in gov.data.vertices
           if gi is None or not any(g.group == gi for g in v.groups)]
    for w in wheels:
        pts += world_verts(w)
    mn, mx = bbox(pts)
    size = mx - mn
    dims = (size.y, size.x, size.z)
    for nm, got, want in zip(('uzunluk', 'genislik', 'yukseklik'), dims, (L, W, H)):
        check(abs(got - want) / want <= 0.03, f'{short}: {nm} {got:.3f} hedef {want:.3f} (%3 disi)')
    check(abs((mn.x + mx.x) / 2) < 0.02 and abs((mn.y + mx.y) / 2) < 0.03, f'{short}: orijin taban izinin ortasinda degil')
    check(abs(mn.z) < 0.002, f'{short}: en alt nokta Z={mn.z:.4f}')
    # aynalar sag/sol simetrik (govde kabugu simetrik oturmus mu)
    if gi is not None:
        ap = [v.co for v in gov.data.vertices if any(g.group == gi for g in v.groups)]
        if ap:
            axmin, axmax = min(p.x for p in ap), max(p.x for p in ap)
            check(abs(axmin + axmax) < 0.005, f'{short}: aynalar simetrik degil ({axmin:.3f} / {axmax:.3f})')
    # tekerler
    ys = {}
    for w in wheels:
        ad = w['ad']
        front = 'On' in ad
        r = rf if front else rr
        lc = [v.co for v in w.data.vertices]
        rad = max(math.hypot(c.y, c.z) for c in lc)
        check(abs(rad - r) / r < 0.005, f'{short}/{ad}: yaricap {rad:.4f} hedef {r}')
        zmin_w = min((w.matrix_world @ c).z for c in lc)
        check(abs(zmin_w) < 0.0015, f'{short}/{ad}: lastik alti Z={zmin_w:.4f}')
        check(abs(w.location.z - r) < 0.0015, f'{short}/{ad}: merkez Z {w.location.z:.4f} != r')
        check(('Sol' in ad) == (w.location.x < 0), f'{short}/{ad}: sol/sag tarafi yanlis')
        cy = sum(c.y for c in lc) / len(lc)
        cz = sum(c.z for c in lc) / len(lc)
        check(abs(cy) < 0.002 and abs(cz) < 0.002, f'{short}/{ad}: kesit merkezi eksende degil ({cy:.4f},{cz:.4f})')
        sect = [0.0] * 8
        for c in lc:
            a = int(((math.atan2(c.z, c.y) + math.pi) / (2 * math.pi)) * 8) % 8
            sect[a] = max(sect[a], math.hypot(c.y, c.z))
        check(max(sect) - min(sect) < 0.01 * r, f'{short}/{ad}: donel simetri bozuk {sect}')
        ys[ad] = w.location.y
        if ent:
            mw = next((t for t in ent['tekerler'] if t['ad'] == ad), None)
            check(mw is not None, f'{short}/{ad}: manifestte yok')
            if mw:
                want = Vector((mw['konum'][0], mw['konum'][2], mw['konum'][1]))
                check((want - w.location).length < 0.002, f'{short}/{ad}: manifest konumu farkli')
                check(abs(mw['yaricap'] - r) < 1e-6, f'{short}/{ad}: manifest yaricap farkli')
                check(mw['direksiyon'] == front, f'{short}/{ad}: direksiyon bayragi yanlis')
    got_wb = (ys['Teker_OnSol'] + ys['Teker_OnSag']) / 2 - (ys['Teker_ArkaSol'] + ys['Teker_ArkaSag']) / 2
    check(abs(got_wb - wb) < 0.01, f'{short}: aks mesafesi {got_wb:.3f} hedef {wb}')
    check(ys['Teker_OnSol'] > 0 and ys['Teker_ArkaSol'] < 0, f'{short}: on/arka yonu yanlis')
    # direksiyon temizligi (on) ve durgun temizlik (hepsi)
    gb = bvh_of(gov)
    worst = []
    for w in wheels:
        angs = STEER if 'On' in w['ad'] else [0]
        hits = []
        for a in angs:
            M = Matrix.Translation(w.location) @ Matrix.Rotation(math.radians(a), 4, 'Z')
            ov = gb.overlap(bvh_of(w, M))
            if ov:
                hits.append(a)
        check(not hits, f'{short}/{w["ad"]}: govdeye giriyor, acilar {hits}')
        worst.append(len(hits))
    # COL kutulari
    wb_boxes = [bbox(world_verts(w)) for w in wheels]
    for c in cols:
        me = c.data
        ok = len(me.vertices) == 8 and len(me.polygons) == 6
        vs = world_verts(c)
        for k in range(3):
            vals = sorted({round(v[k], 6) for v in vs})
            ok = ok and len(vals) == 2
        check(ok, f'{short}/{c["ad"]}: eksen hizali 8 koseli kutu degil')
        cmn, cmx = bbox(vs)
        check(cmn.z >= 0.25 - 1e-6, f'{short}/{c["ad"]}: taban {cmn.z:.3f} < 0.25')
        for (wmn, wmx), w in zip(wb_boxes, wheels):
            inter = all(min(cmx[k], wmx[k]) - max(cmn[k], wmn[k]) > 1e-4 for k in range(3))
            check(not inter, f'{short}/{c["ad"]}: {w["ad"]} tekerlegini kapsiyor')
    # kabuklar, malzemeler, ucgen
    shells = {}
    for o in [gov] + wheels:
        be, isl, neg = shell_report(o)
        shells[o['ad']] = (be, isl, neg)
        check(be == 0, f'{short}/{o["ad"]}: {be} acik/manifold olmayan kenar')
        check(neg == 0, f'{short}/{o["ad"]}: {neg} parcanin normali ice donuk')
        for i, m in enumerate(o.data.materials):
            check(m is not None, f'{short}/{o["ad"]}: bos malzeme yuvasi')
            if m:
                check(m.name in json_mats, f'{short}/{o["ad"]}: {m.name} JSON da yok')
        used = {p.material_index for p in o.data.polygons}
        check(len(used) == len(o.data.materials), f'{short}/{o["ad"]}: kullanilmayan malzeme yuvasi')
    tri = sum(tris(o.data) for o in [gov] + wheels)
    check(tmin <= tri <= tmax, f'{short}: ucgen {tri} butce {tmin}-{tmax}')
    if ent:
        check(ent['ucgen'] == tri, f'{short}: manifest ucgen {ent["ucgen"]} != {tri}')
        check(ent['fbx'] == vid + '.fbx', f'{short}: manifest fbx adi')
        for k in ('id', 'fbx', 'boyut', 'ucgen', 'tip', 'kutle_kg', 'azami_hiz_kmh', 'aks_mesafesi',
                  'agirlik_merkezi', 'ceker', 'tekerler', 'boya_malzemesi', 'far_malzemesi',
                  'stop_malzemesi', 'cerceve'):
            check(k in ent, f'{short}: manifest alani eksik {k}')
        check(ent['agirlik_merkezi'][1] < H * 0.45, f'{short}: agirlik merkezi yuksek')
        bm = [round(size.x, 3), round(size.z, 3), round(size.y, 3)]
        check(all(abs(a - b) < 0.002 for a, b in zip(bm, ent['boyut'])), f'{short}: manifest boyut {ent["boyut"]} != {bm}')
    allpts = []
    for o in [gov] + wheels + cols:
        allpts += world_verts(o)
    amn, amx = bbox([gov.matrix_world @ v.co for v in gov.data.vertices] + sum((world_verts(w) for w in wheels), []))
    blend_meas[vid] = {o['ad']: bbox(world_verts(o)) for o in col.objects}
    blend_meas[vid]['__mats'] = sorted(m.name for m in gov.data.materials)
    rows.append((short, tri, dims, max(abs(a) for a in [0]), shells['Govde']))
    print(f'ARAC {short:12s} ucgen {tri:5d}  L {dims[0]:.3f} W {dims[1]:.3f} H {dims[2]:.3f}  '
          f'govde kabuk: {shells["Govde"][1]} parca')

# ---------------------------------------------------------------------------
# FBX yeniden ice aktarma
# ---------------------------------------------------------------------------
for vid, meas in blend_meas.items():
    path = OUT / (vid + '.fbx')
    if not check(path.exists(), f'{vid}: FBX yok'):
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), axis_forward='Z', axis_up='Y')
    bpy.context.view_layer.update()
    got = {o.name: o for o in bpy.data.objects if o.type == 'MESH'}
    want = {k for k in meas if not k.startswith('__')}
    check(set(got) == want, f'{vid}: FBX nesne adlari {sorted(got)} != {sorted(want)}')
    check(not any(o.type == 'FONT' for o in bpy.data.objects), f'{vid}: FBX icinde FONT')
    for n, o in got.items():
        if n not in meas:
            continue
        a_mn, a_mx = bbox(world_verts(o))
        b_mn, b_mx = meas[n]
        d = max((a_mn - b_mn).length, (a_mx - b_mx).length)
        check(d < 0.003, f'{vid}/{n}: FBX olcusu farkli ({d:.4f} m)')
        if n.startswith('Teker_'):
            c = (a_mn + a_mx) / 2
            check((o.matrix_world.translation - c).length < 0.003, f'{vid}/{n}: FBX teker orijini merkezde degil')
    if 'Govde' in got:
        gm = sorted(m.name.split('.')[0] for m in got['Govde'].data.materials)
        check(gm == meas['__mats'], f'{vid}: FBX Govde malzemeleri {gm} != {meas["__mats"]}')

print('=' * 70)
if fails:
    print(f'VERIFY HATA: {len(fails)} sorun')
    for f in fails:
        print('  -', f)
else:
    print(f'VERIFY TEMIZ: {len(blend_meas)} arac, butun kontroller gecti')
