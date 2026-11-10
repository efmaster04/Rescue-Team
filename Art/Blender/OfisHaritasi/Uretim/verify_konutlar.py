# -*- coding: utf-8 -*-
"""Konutlar seti kontrolu.

Calistirma:
  blender.exe -b --factory-startup -P verify_konutlar.py

Kontroller: parsel disina tasma (+-0,05 m), hedef olculer (bina izi, kat/sacak kotu, cephe
geri cekilmesi, bahce duvari yukseklikleri, kapi bosluklari, parmaklik araligi), ucgen
butcesi, COL_ kutularinin eksen hizasi, FONT/egri yok, duz golgeleme, malzemeler JSON'da,
park ve agac noktalarinin bos alanda olmasi, FBX yeniden ice aktarilinca ad ve olculerin
korunmasi. Sonuc Kaynak/Raporlar/Konutlar_verify_sonuc.txt dosyasina da yazilir.
"""
import bpy
import json
import math
import sys
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

KOK = Path(r'C:\Users\uur\RescueTeam_OfisHaritasi')
OYUN = KOK / 'Oyun' / 'Assets' / 'OfisHaritasi' / 'Modeller' / 'Konutlar'
BLEND = KOK / 'Kaynak' / 'Blender' / 'Konutlar.blend'
RAPOR = KOK / 'Kaynak' / 'Raporlar' / 'Konutlar_verify_sonuc.txt'

TOL = 0.05
# Brif hedefleri (build betiginden bagimsiz yazildi)
HEDEF = {
    'Apartman_A': dict(parsel=(22, 22), iz=(12, 10), kat=3, zemin=0.45, sacak=9.45, butce=8000,
                       kapilar=(1.2, 3.0), on_duvar=True, yan_duvar=False),
    'Apartman_B': dict(parsel=(22, 22), iz=(14, 11), kat=3, zemin=0.15, parapet=10.75,
                       butce=8000, kapilar=(), on_duvar=False, yan_duvar=False),
    'Apartman_C': dict(parsel=(22, 24), iz=(13, 10), kat=2, zemin=0.9, sacak=7.6, butce=8000,
                       kapilar=(1.2, 3.0), on_duvar=True, yan_duvar=True),
    'Ev_A': dict(parsel=(18, 22), iz=(9, 8), kat=1, zemin=0.6, sacak=3.5, butce=6000,
                 kapilar=(1.2, 3.0), on_duvar=True, yan_duvar=True, arka_duvar=True),
    'Ev_B': dict(parsel=(18, 22), iz=(8, 8), kat=2, zemin=0.3, sacak=6.1, butce=6000,
                 kapilar=(1.2, 3.0), on_duvar=True, yan_duvar=True, arka_duvar=True),
    'Ev_C': dict(parsel=(18, 22), iz=(11, 9), kat=1, zemin=0.3, sacak=3.2, butce=6000,
                 kapilar=(1.2, 3.0), on_duvar=True, yan_duvar=True, arka_duvar=True),
    'Ev_D': dict(parsel=(18, 22), iz=(9, 9), kat=2, zemin=0.25, sacak=5.9, butce=6000,
                 kapilar=(1.2, 3.0), on_duvar=True, yan_duvar=True, arka_duvar=True),
}

satirlar = []
hatalar = []


def ok(msg):
    satirlar.append('  OK    ' + msg)


def hata(msg):
    satirlar.append('  HATA  ' + msg)
    hatalar.append(msg)


def kontrol(kosul, msg):
    (ok if kosul else hata)(msg)
    return kosul


def world_verts(o):
    mw = o.matrix_world
    return [mw @ v.co for v in o.data.vertices]


def bbox(o):
    vs = world_verts(o)
    return (min(v.x for v in vs), max(v.x for v in vs), min(v.y for v in vs),
            max(v.y for v in vs), min(v.z for v in vs), max(v.z for v in vs))


def bvh_of(objs):
    verts, polys = [], []
    for o in objs:
        b = len(verts)
        verts += world_verts(o)
        polys += [tuple(b + i for i in p.vertices) for p in o.data.polygons]
    return BVHTree.FromPolygons(verts, polys, epsilon=0.0)


def box_bvh(x0, x1, y0, y1, z0, z1):
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
         (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return BVHTree.FromPolygons([Vector(p) for p in v], f, epsilon=0.0)


def rect_dist(px, py, r):
    dx = max(r[0] - px, 0, px - r[1])
    dy = max(r[2] - py, 0, py - r[3])
    return math.hypot(dx, dy)


manifest = json.loads((OYUN / 'Konutlar_manifest.json').read_text(encoding='utf-8'))
mats = json.loads((OYUN / 'Konutlar_malzemeler.json').read_text(encoding='utf-8'))
mat_names = {m['name'] for m in mats['materials']}

satirlar.append('== Malzeme JSON ==')
alanlar = {'name', 'color', 'metallic', 'smoothness', 'emission', 'alpha'}
kontrol(all(alanlar <= set(m) for m in mats['materials']), 'her malzemede zorunlu alanlar var')
kontrol(all(m['name'].startswith('KN_') for m in mats['materials']), 'butun malzemeler KN_ onekli')
kontrol(all(0 <= c <= 1 for m in mats['materials'] for c in m['color']), 'renkler sRGB 0-1')
rnd = {m['name']: m.get('randomize') for m in mats['materials']}
kontrol(rnd.get('KN_Siva') == 'konut_siva' and rnd.get('KN_Panjur') == 'konut_panjur',
        'KN_Siva=konut_siva, KN_Panjur=konut_panjur randomize')
pal = mats.get('palettes', {})
kontrol(len(pal.get('konut_siva', [])) == 7, f"konut_siva paleti 7 ton ({len(pal.get('konut_siva', []))})")
kontrol(len(pal.get('konut_panjur', [])) == 4,
        f"konut_panjur paleti 4 ton ({len(pal.get('konut_panjur', []))})")

bpy.ops.wm.open_mainfile(filepath=str(BLEND))
satirlar.append('== Sahne geneli ==')
turler = {o.type for o in bpy.data.objects}
kontrol('FONT' not in turler and 'CURVE' not in turler and 'SURFACE' not in turler,
        f'FONT/egri nesnesi yok (turler: {sorted(turler)})')
kontrol(len(bpy.data.curves) == 0, 'bpy.data.curves bos (yazi verisi yok)')

olcu_blend = {}
for A in manifest['varliklar']:
    aid = A['id']
    H = HEDEF[aid]
    G, D = A['parsel']
    satirlar.append(f'== {aid} ==')
    coll = bpy.data.collections.get(aid)
    if not kontrol(coll is not None, 'koleksiyon var'):
        continue
    obs = {o.name: o for o in coll.objects}
    kontrol(set(A['nesneler']) | set(A['carpisma']) == set(obs),
            f"manifest nesne listesi sahneyle ayni ({len(obs)} nesne)")
    vis = [obs[n] for n in A['nesneler']]
    cols = [obs[n] for n in A['carpisma']]
    kontrol(tuple(A['parsel']) == H['parsel'], f"parsel {A['parsel']} = {list(H['parsel'])}")
    # parsel disina tasma
    tasma = []
    for o in vis + cols:
        b = bbox(o)
        if b[0] < -G / 2 - TOL or b[1] > G / 2 + TOL or b[2] < -TOL or b[3] > D + TOL or b[4] < -0.6 - 0.01:
            tasma.append(f'{o.name} {tuple(round(x, 3) for x in b)}')
    kontrol(not tasma, 'parsel disina tasma yok (+-0,05 m)' + ('' if not tasma else ': ' + '; '.join(tasma)))
    # ucgen
    tri = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in vis)
    tri_c = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in cols)
    kontrol(all(len(p.vertices) == 3 for o in vis for p in o.data.polygons), 'gorunur meshler ucgenlenmis')
    kontrol(tri <= H['butce'], f'ucgen {tri} <= {H["butce"]} (COL dahil {tri + tri_c})')
    kontrol(tri == A['ucgen'], f"manifest ucgen sayisi dogru ({A['ucgen']})")
    # duz golgeleme, malzemeler
    kontrol(not any(p.use_smooth for o in vis for p in o.data.polygons), 'duz golgeleme (smooth yok)')
    used = {s.material.name for o in vis for s in o.material_slots if s.material}
    bos = [o.name for o in vis for s in o.material_slots if not s.material]
    kontrol(used <= mat_names and not bos, f'malzemeler JSON\'da ({len(used)}: {sorted(used)})')
    kontrol(all(len(o.material_slots) > 0 for o in vis), 'her gorunur meshte malzeme var')
    # COL kutulari
    col_bad = []
    for o in cols:
        vs = world_verts(o)
        axis_ok = all(len({round(getattr(v, a), 5) for v in vs}) == 2 for a in 'xyz')
        tr_ok = (o.rotation_euler.to_quaternion().angle < 1e-6 and
                 all(abs(s - 1) < 1e-6 for s in o.scale) and o.location.length < 1e-6)
        if not (len(vs) == 8 and len(o.data.polygons) == 6 and axis_ok and tr_ok and o.parent is None):
            col_bad.append(o.name)
    kontrol(cols and not col_bad, f'COL_ kutulari ({len(cols)}) 8 koseli, eksen hizali, donussuz'
            + ('' if not col_bad else ': ' + ', '.join(col_bad)))
    kontrol(all(o.name.startswith('COL_') for o in cols), 'carpisma adlari COL_ onekli')
    # olculer
    cb = {o.name: bbox(o) for o in cols}
    bina = cb.get(f'COL_{aid}_Bina')
    izler = [i for i in A['bina_izleri']]
    if aid == 'Ev_C':
        kn = cb[f'COL_{aid}_Kanat']
        foot = (min(bina[0], kn[0]), max(bina[1], kn[1]), min(bina[2], kn[2]), max(bina[3], kn[3]))
    else:
        foot = bina[:4]
    w, d = foot[1] - foot[0], foot[3] - foot[2]
    kontrol(abs(w - H['iz'][0]) < 0.02 and abs(d - H['iz'][1]) < 0.02,
            f'bina izi {w:.2f} x {d:.2f} = {H["iz"][0]} x {H["iz"][1]}')
    bv = bbox(obs[f'{aid}_Bina'])
    kontrol(bv[0] <= foot[0] + 0.01 and bv[1] >= foot[1] - 0.01 and bv[2] <= foot[2] + 0.01,
            'gorunur bina mesh izi COL izini kapsiyor')
    # on cephe geri cekilmesi
    kontrol(4.0 - 0.01 <= foot[2] <= 6.0 + 0.01, f'bina on cephesi on kenardan {foot[2]:.2f} m geride (4-6)')
    oz = A['ozellikler']
    if 'sacak' in H:
        kontrol(abs(bina[5] - H['sacak']) < 0.02, f'sacak/duvar ust kotu {bina[5]:.2f} = {H["sacak"]}')
    if 'parapet' in H:
        kontrol(abs(bina[5] - H['parapet']) < 0.02, f'parapet ust kotu {bina[5]:.2f} = {H["parapet"]}')
    kontrol(abs(oz.get('zemin_kotu', H['zemin']) - H['zemin']) < 0.01, f"zemin kat kotu {H['zemin']}")
    if aid in ('Apartman_A',):
        nk = (bina[5] - H['zemin']) / 3.0
        kontrol(abs(nk - 3) < 0.01, f'kat sayisi (sacak-zemin)/3,0 = {nk:.2f} (3 kat, 3,0 m)')
    if aid == 'Apartman_B':
        ob = obs[f'{aid}_Bina']
        dz = [p for p in ob.data.polygons if p.normal.z > 0.99 and
              ob.material_slots[p.material_index].material.name == 'KN_Doseme']
        zcat = max(round(p.center.z, 3) for p in dz)
        alan = sum(p.area for p in dz if abs(p.center.z - zcat) < 0.01)
        nk = 1 + (zcat - 3.75) / 3.0
        kontrol(abs(zcat - 9.75) < 0.01 and abs(nk - 3) < 0.01 and alan > 100,
                f'duz cati kotu {zcat:.2f} ({alan:.0f} m2): 3,6 m dukkan kati + 2 x 3,0 m = 3 kat')
    if aid == 'Apartman_C':
        kontrol(abs((6.9 - H['zemin']) / 3.0 - 2) < 0.01, 'zemin + 1 kat (3,0 m) + cati kati 6,9')
    if aid in ('Ev_B', 'Ev_D'):
        nk = (bina[5] - H['zemin'] + 0.2) / 3.0
        kontrol(1.8 < nk < 2.1, f'iki kat ({nk:.2f} x 3,0 m)')
    # bahce duvari
    duv = {n.split('_Duvar_')[1]: obs[n] for n in A['nesneler'] if '_Duvar_' in n}
    kontrol(('On' in duv) == H['on_duvar'], f"on bahce duvari {'var' if H['on_duvar'] else 'yok'}")
    kontrol(('Sol' in duv and 'Sag' in duv) == H['yan_duvar'],
            f"yan duvarlar {'var' if H['yan_duvar'] else 'yok'}")
    if H.get('arka_duvar'):
        kontrol('Arka' in duv, 'arka duvar var (butun parsel siniri)')
    for side, o in duv.items():
        me = o.data
        demir = [p for p in me.polygons if o.material_slots[p.material_index].material.name == 'KN_Demir']
        zs = [me.vertices[i].co.z for p in demir for i in p.vertices]
        beton_top = [p for p in me.polygons
                     if o.material_slots[p.material_index].material.name == 'KN_Beton'
                     and p.normal.z > 0.9]
        cop = sorted({round(p.center.z, 3) for p in beton_top})
        ok_h = abs(min(zs) - 0.50) < 0.011 and abs(max(zs) - 1.20) < 0.011 and 0.5 in cop
        kontrol(ok_h, f'{side}: taban 0,50 m + parmaklik 0,70 m (demir {min(zs):.2f}-{max(zs):.2f}, harpusta {cop})')
        # parmaklik araligi: dusey cubuk yuzleri (yukseklik 0,62)
        cx = []
        for p in demir:
            vz = [me.vertices[i].co.z for i in p.vertices]
            if abs((max(vz) - min(vz)) - 0.62) < 0.005:
                c = p.center
                cx.append(round(c.x if side in ('On', 'Arka') else c.y, 2))
        xs = sorted(set(cx))
        merk = []
        for v in xs:
            if merk and v - merk[-1][-1] <= 0.025:
                merk[-1].append(v)
            else:
                merk.append([v])
        cen = [sum(g) / len(g) for g in merk]
        gaps = [b - a for a, b in zip(cen, cen[1:]) if b - a < 0.4]
        kontrol(gaps and 0.195 <= min(gaps) and max(gaps) <= 0.255,
                f'{side}: kare demir cubuk {len(cen)} adet, aralik {min(gaps):.3f}-{max(gaps):.3f} m')
    if 'On' in duv:
        tree = bvh_of([duv['On']])
        miss = []
        x = -G / 2 + 0.01
        while x < G / 2:
            hit = tree.ray_cast(Vector((x, 1.5, 0.3)), Vector((0, -1, 0)), 1.6)
            miss.append(hit[0] is None)
            x += 0.01
        gaps = []
        i = 0
        while i < len(miss):
            if miss[i]:
                j = i
                while j < len(miss) and miss[j]:
                    j += 1
                gaps.append((-G / 2 + 0.01 + i * 0.01, (j - i) * 0.01))
                i = j
            else:
                i += 1
        gaps = [g for g in gaps if g[1] > 0.3]
        widths = sorted(round(g[1], 2) for g in gaps)
        exp = sorted(H['kapilar'])
        kontrol(len(widths) == len(exp) and all(abs(a - b) < 0.03 for a, b in zip(widths, exp)),
                f'on duvar kapi bosluklari {widths} = {exp}')
        araclar = [g for g in gaps if g[1] > 2]
        if araclar and A['giris_kesimleri']:
            g0 = araclar[0][0]
            k0, k1 = A['giris_kesimleri'][0]
            kontrol(abs(g0 - k0) < 0.03 and abs(g0 + araclar[0][1] - k1) < 0.03,
                    f'giris_kesimleri {A["giris_kesimleri"][0]} arac kapisi hizasinda')
    if not A['giris_kesimleri']:
        kontrol(not A['park_yerleri'], 'arac kapisi yok -> park yeri listesi bos')
    # park ve agac noktalari
    engel = [o for o in vis if not o.name.endswith('_Bahce')]
    tr_e = bvh_of(engel)
    tr_all = bvh_of(vis)
    rects = [(i['min'][0], i['max'][0], i['min'][1], i['max'][1]) for i in izler]
    rects_kapali = [(i['min'][0], i['max'][0], i['min'][1], i['max'][1]) for i in izler
                    if i['tur'] != 'cardak']
    for k, p in enumerate(A['park_yerleri']):
        cx, cy = p['konum'][0], p['konum'][2]
        hw, hl = p['boyut'][0] / 2, p['boyut'][1] / 2
        bx = box_bvh(cx - hw, cx + hw, cy - hl, cy + hl, 0.10, 1.90)
        temas = tr_e.overlap(bx)
        icinde = any(r[0] < cx < r[1] and r[2] < cy < r[3] for r in rects_kapali)
        ic_parsel = -G / 2 < cx - hw and cx + hw < G / 2 and 0 < cy - hl and cy + hl < D
        k0, k1 = A['giris_kesimleri'][0]
        hiza = k0 - 0.3 <= cx - hw and cx + hw <= k1 + 0.3
        kontrol(not temas and not icinde and ic_parsel and hiza and p['yon_derece'] == 0,
                f'park {k + 1} ({cx}, {cy}) bos alanda, arac kapisinin arkasinda, yon 0'
                + ('' if not temas else f' TEMAS {len(temas)}') + ('' if not icinde else ' BINA ICINDE')
                + ('' if ic_parsel else ' PARSEL DISI') + ('' if hiza else ' KAPI HIZASI DISI'))
    n_ag = len(A['agac_noktalari'])
    if aid.startswith('Ev'):
        kontrol(2 <= n_ag <= 4, f'agac noktasi sayisi {n_ag} (2-4)')
    for k, (x, _, y) in enumerate(A['agac_noktalari']):
        bx = box_bvh(x - 0.6, x + 0.6, y - 0.6, y + 0.6, -0.3, 3.2)
        temas = tr_all.overlap(bx)
        dmin = min([rect_dist(x, y, r) for r in rects] or [99])
        ic = -G / 2 + 1 < x < G / 2 - 1 and 1 < y < D - 1
        pk = all(rect_dist(x, y, (p['konum'][0] - 1.25, p['konum'][0] + 1.25,
                                  p['konum'][2] - 2.5, p['konum'][2] + 2.5)) > 0.8
                 for p in A['park_yerleri'])
        kontrol(not temas and dmin >= 1.5 and ic and pk,
                f'agac {k + 1} ({x}, {y}) bina/yol/duvara degmiyor (binaya {dmin:.1f} m)'
                + ('' if not temas else f' TEMAS {len(temas)}'))
    olcu_blend[aid] = {o.name: bbox(o) for o in vis + cols}
    olcu_blend[aid]['_mats'] = {o.name: sorted(s.material.name for s in o.material_slots) for o in vis}
    # manifest boyutu
    xs = [olcu_blend[aid][o.name] for o in vis]
    bx_ = (min(b[0] for b in xs), max(b[1] for b in xs), min(b[2] for b in xs), max(b[3] for b in xs),
           min(b[4] for b in xs), max(b[5] for b in xs))
    boy = [bx_[1] - bx_[0], bx_[5] - bx_[4], bx_[3] - bx_[2]]
    kontrol(all(abs(a - b) < 0.01 for a, b in zip(boy, A['boyut'])),
            f"manifest boyut {A['boyut']} (Unity X,Y,Z)")

# FBX yeniden ice aktarma
satirlar.append('== FBX yeniden ice aktarma ==')
for A in manifest['varliklar']:
    aid = A['id']
    path = OYUN / A['fbx']
    if not kontrol(path.exists(), f'{A["fbx"]} var ({path.stat().st_size // 1024} kB)' if path.exists()
                   else f'{A["fbx"]} yok'):
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    imp = {o.name: o for o in bpy.data.objects if o.type == 'MESH'}
    bek = set(A['nesneler']) | set(A['carpisma'])
    kontrol(set(imp) == bek, f'{aid}: ad listesi korunuyor ({len(imp)} mesh)'
            + ('' if set(imp) == bek else f' eksik {sorted(bek - set(imp))[:5]} fazla {sorted(set(imp) - bek)[:5]}'))
    fark = 0.0
    for n, o in imp.items():
        if n in olcu_blend[aid]:
            b0 = olcu_blend[aid][n]
            b1 = bbox(o)
            fark = max(fark, max(abs(a - b) for a, b in zip(b0, b1)))
    kontrol(fark < 0.005, f'{aid}: olculer korunuyor (en buyuk sapma {fark * 1000:.2f} mm)')
    mfark = [n for n in A['nesneler'] if n in imp and
             sorted(s.material.name.split('.')[0] for s in imp[n].material_slots) != olcu_blend[aid]['_mats'][n]]
    kontrol(not mfark, f'{aid}: malzeme adlari korunuyor' + ('' if not mfark else f' {mfark}'))
    cbad = [n for n in A['carpisma'] if n in imp and not all(
        len({round((imp[n].matrix_world @ v.co)[i], 4) for v in imp[n].data.vertices}) == 2 for i in range(3))]
    kontrol(not cbad, f'{aid}: COL kutulari ice aktarimdan sonra eksen hizali' + ('' if not cbad else f' {cbad}'))

sonuc = 'TEMIZ' if not hatalar else f'{len(hatalar)} HATA'
satirlar.insert(0, f'Konutlar verify sonucu: {sonuc}')
txt = '\n'.join(satirlar)
print(txt)
RAPOR.parent.mkdir(parents=True, exist_ok=True)
RAPOR.write_text(txt + '\n', encoding='utf-8')
print('VERIFY', sonuc)
