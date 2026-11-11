# -*- coding: utf-8 -*-
"""Ticari_Binalar dogrulamasi.

  blender.exe -b -P verify_ticari_binalar.py

Kontroller: parsel siniri (+-0,05 m), olcu hedefleri, ucgen butcesi, COL_ kutularinin eksen hizasi,
FONT nesnesi yok, malzemeler JSON'da, park yerleri dosemede ve duvarlara/binalara girmiyor, giris kesimleri acik,
FBX yeniden ice aktarilinca adlar / olculer / malzemeler korunuyor.
Sonuc: Kaynak/Raporlar/Ticari_Binalar_verify_sonuc.txt
"""
import bpy
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path(r"C:\Users\uur\RescueTeam_OfisHaritasi")
SET = "Ticari_Binalar"
OUT = ROOT / "Oyun" / "Assets" / "OfisHaritasi" / "Modeller" / SET
BLEND = ROOT / "Kaynak" / "Blender" / (SET + ".blend")
LOG = ROOT / "Kaynak" / "Raporlar" / (SET + "_verify_sonuc.txt")
TOL_PARSEL = 0.05

lines = []
fails = []


def ok(cond, msg):
    lines.append(('  OK   ' if cond else '  HATA ') + msg)
    if not cond:
        fails.append(msg)
    return cond


def info(msg):
    lines.append('       ' + msg)


def world_verts(o):
    M = o.matrix_world
    return [M @ v.co for v in o.data.vertices]


def bbox(vs):
    return (Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))),
            Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs))))


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def snapshot(objs):
    d = {}
    for o in objs:
        vs = world_verts(o) if o.type == 'MESH' else [o.matrix_world.translation]
        mn, mx = bbox(vs)
        d[o.name] = {'type': o.type, 'min': mn, 'max': mx, 'tris': tris(o) if o.type == 'MESH' else 0,
                     'mats': sorted({s.material.name for s in o.material_slots if s.material}) if o.type == 'MESH' else [],
                     'rot': tuple(o.rotation_euler), 'scale': tuple(o.scale), 'nverts': len(o.data.vertices) if o.type == 'MESH' else 0}
    return d


def is_aabb_box(o):
    vs = world_verts(o)
    if len(vs) != 8 or len(o.data.polygons) not in (6, 12):
        return False, f'{len(vs)} kose / {len(o.data.polygons)} yuz'
    mn, mx = bbox(vs)
    for v in vs:
        for i in range(3):
            if min(abs(v[i] - mn[i]), abs(v[i] - mx[i])) > 1e-4:
                return False, 'kose kutu kosesinde degil'
    for p in o.data.polygons:
        n = (o.matrix_world.to_3x3() @ p.normal).normalized()
        if max(abs(n.x), abs(n.y), abs(n.z)) < 0.9999:
            return False, 'yuz eksene dik degil'
    return True, ''


def rect_corners(x, y, yaw, w=2.5, l=5.0):
    t = math.radians(yaw)
    h = Vector((math.sin(t), math.cos(t)))
    s = Vector((math.cos(t), -math.sin(t)))
    c = Vector((x, y))
    return [c + h * (l / 2) + s * (w / 2), c + h * (l / 2) - s * (w / 2), c - h * (l / 2) - s * (w / 2), c - h * (l / 2) + s * (w / 2)]


def sat_overlap(poly_a, poly_b, eps=0.01):
    """Iki dis bukey cokgen (2B) eps'ten fazla ortusuyor mu."""
    for poly in (poly_a, poly_b):
        n = len(poly)
        for i in range(n):
            e = poly[(i + 1) % n] - poly[i]
            ax = Vector((-e.y, e.x)).normalized()
            pa = [ax.dot(p) for p in poly_a]
            pb = [ax.dot(p) for p in poly_b]
            if max(pa) <= min(pb) + eps or max(pb) <= min(pa) + eps:
                return False
    return True


def inside_union(pt, rects, tol=0.011):
    return any(r[0] - tol <= pt.x <= r[1] + tol and r[2] - tol <= pt.y <= r[3] + tol for r in rects)


def main():
    man = json.loads((OUT / f'{SET}_manifest.json').read_text(encoding='utf-8'))
    mats = json.loads((OUT / f'{SET}_malzemeler.json').read_text(encoding='utf-8'))
    lines.append(f'{SET} dogrulama')
    lines.append('=' * 60)
    # --- malzeme JSON
    lines.append('[Malzeme JSON]')
    names = [m['name'] for m in mats['materials']]
    ok(len(names) == len(set(names)), f'{len(names)} malzeme, adlar benzersiz')
    ok(all(n.startswith('TB_') for n in names), 'butun malzemeler TB_ onekli')
    for m in mats['materials']:
        good = (len(m['color']) == 3 and all(0 <= c <= 1 for c in m['color']) and len(m['emission']) == 3
                and 0 <= m['metallic'] <= 1 and 0 <= m['smoothness'] <= 1 and 0 < m['alpha'] <= 1)
        if not good:
            ok(False, f'{m["name"]} alanlari gecersiz')
        if 'randomize' in m:
            ok(m['randomize'] in mats.get('palettes', {}), f'{m["name"]} randomize={m["randomize"]} paleti var')
    pal = mats.get('palettes', {}).get('atolye_kaplama', [])
    ok(len(pal) == 5, f'atolye_kaplama paleti {len(pal)} renk')
    kap = next((m for m in mats['materials'] if m['name'] == 'TB_Kaplama'), None)
    ok(kap is not None and kap.get('randomize') == 'atolye_kaplama', 'TB_Kaplama randomize=atolye_kaplama')
    ok(any(m['name'] == 'TB_Siva' for m in mats['materials']), 'TB_Siva mevcut')
    ok(any(m['name'] == 'TB_Cam' and m['alpha'] == 1.0 for m in mats['materials']), 'TB_Cam opak')

    # --- .blend
    bpy.ops.wm.open_mainfile(filepath=str(BLEND))
    ok(not any(o.type == 'FONT' for o in bpy.data.objects), '.blend icinde FONT nesnesi yok')
    ok(not any(c.bl_rna.identifier == 'TextCurve' for c in bpy.data.curves), '.blend icinde yazi egrisi yok')
    ref = {}
    for ent in man['varliklar']:
        aid = ent['id']
        G, D = ent['parsel']
        lines.append('')
        lines.append(f'[{aid}]  parsel {G} x {D}')
        coll = bpy.data.collections.get(aid)
        if not ok(coll is not None, 'koleksiyon var'):
            continue
        objs = list(coll.objects)
        ref[aid] = snapshot(objs)
        vis = [o for o in objs if not o.name.startswith('COL_')]
        cols = [o for o in objs if o.name.startswith('COL_')]
        ok(all(o.type == 'MESH' for o in objs), f'{len(vis)} gorsel + {len(cols)} COL nesnesi, hepsi MESH')
        ok(all(o.name.startswith(aid + '_') or o.name.startswith('COL_' + aid + '_') for o in objs), 'nesne adlari varlik onekli')
        ok(sorted(o.name for o in vis) == ent['nesneler'] and sorted(o.name for o in cols) == ent['col_kutulari'],
           'manifest nesne listesi .blend ile ayni')
        ok(all(max(abs(r) for r in o.rotation_euler) < 1e-6 and max(abs(s - 1) for s in o.scale) < 1e-6 for o in objs),
           'butun nesneler donus 0 / olcek 1')
        moving = {m['ad'] for m in ent['hareketli']}
        ok(all(o.location.length < 1e-6 for o in vis if o.name not in moving), 'sabit nesnelerin orijini parsel orijininde')
        # sinirlar
        allv = [v for o in objs for v in world_verts(o)]
        mn, mx = bbox(allv)
        ok(mn.x >= -G / 2 - TOL_PARSEL and mx.x <= G / 2 + TOL_PARSEL and mn.y >= -TOL_PARSEL and mx.y <= D + TOL_PARSEL,
           f'parsel icinde: X {mn.x:.3f}..{mx.x:.3f}, Y(Unity Z) {mn.y:.3f}..{mx.y:.3f}')
        ok(mn.z >= -0.6 - 1e-4, f'en alt kot {mn.z:.3f} (temel -0,6)')
        # ucgen
        vt = sum(tris(o) for o in vis)
        ok(vt <= ent['ucgen_butcesi'], f'gorsel ucgen {vt} <= butce {ent["ucgen_butcesi"]}')
        ok(vt == ent['ucgen'], 'manifest ucgen sayisi dogru')
        # malzemeler
        used = {s.material.name for o in vis for s in o.material_slots if s.material}
        missing = used - set(names)
        ok(not missing, f'kullanilan {len(used)} malzemenin hepsi JSON\'da' + (f' (eksik: {sorted(missing)})' if missing else ''))
        ok(all(len(o.material_slots) > 0 for o in vis), 'her gorsel mesh malzemeli')
        ok(all(all(p.material_index < len(o.material_slots) for p in o.data.polygons) for o in vis), 'yuz malzeme indeksleri gecerli')
        # COL
        bad = []
        for o in cols:
            g, why = is_aabb_box(o)
            if not g:
                bad.append(f'{o.name}: {why}')
        ok(not bad, f'{len(cols)} COL kutusu eksenlere hizali 8 koseli kutu' + (f' {bad}' if bad else ''))
        ok(all(len(o.material_slots) == 0 for o in cols), 'COL kutularinda malzeme yok')
        colb = {o.name: bbox(world_verts(o)) for o in cols}
        # olcu hedefleri
        chk = ent['hedef_olculer']

        def cdim(name):
            b = colb.get(f'COL_{aid}_{name}')
            return None if b is None else (b[1] - b[0])
        if 'govde' in chk:
            d = cdim('Bina')
            ok(d is not None and abs(d.x - chk['govde'][0]) < 0.02 and abs(d.y - chk['govde'][1]) < 0.02,
               f'govde {d.x:.2f} x {d.y:.2f} m (hedef {chk["govde"][0]} x {chk["govde"][1]})')
        if 'sacak' in chk:
            o = bpy.data.objects[f'{aid}_Bina']
            xw = colb[f'COL_{aid}_Bina'][1].x
            zs = [v.z for v in world_verts(o) if abs(v.x - xw) < 0.003]
            ok(zs and abs(max(zs) - chk['sacak']) < 0.02, f'yan duvar sacak kotu {max(zs):.2f} m (hedef {chk["sacak"]})')
        if 'yukseklik' in chk:
            d = colb[f'COL_{aid}_Bina']
            ok(abs(d[1].z - chk['yukseklik']) < 0.02, f'bina yuksekligi {d[1].z:.2f} m (hedef {chk["yukseklik"]})')
        for m in ent['hareketli']:
            o = bpy.data.objects.get(m['ad'])
            if not ok(o is not None, f'{m["ad"]} var'):
                continue
            b0, b1 = bbox(world_verts(o))
            wdt = max(b1.x - b0.x, b1.y - b0.y) + 0.06
            hgt = b1.z - b0.z
            ok(abs(wdt - m['genislik']) < 0.03 and abs(hgt - m['yukseklik']) < 0.01,
               f'{m["ad"]}: aciklik {wdt:.2f} x {hgt:.2f} m (hedef {m["genislik"]} x {m["yukseklik"]}), orijin ust kenarda z={o.location.z:.2f}')
        if 'kepenk' in chk:
            k = [m for m in ent['hareketli'] if m['tur'] == 'sarmal_kepenk']
            ok(len(k) >= 1 and abs(k[0]['genislik'] - chk['kepenk'][0]) < 1e-6 and abs(k[0]['yukseklik'] - chk['kepenk'][1]) < 1e-6,
               f'buyuk kepenk {chk["kepenk"][0]} x {chk["kepenk"][1]} m')
        if 'kepenk_sayisi' in chk:
            k = [m for m in ent['hareketli'] if m['tur'] == 'sarmal_kepenk']
            ok(len(k) == chk['kepenk_sayisi'], f'kepenk sayisi {len(k)} (hedef {chk["kepenk_sayisi"]})')
        if 'kapi_boslugu' in chk:
            bb = sorted([b for n, b in colb.items() if 'Kapi_Babasi' in n], key=lambda b: b[0].x)
            gap = bb[1][0].x - bb[0][1].x if len(bb) >= 2 else -1
            ok(abs(gap - chk['kapi_boslugu']) < 0.01, f'on arac kapisi boslugu {gap:.2f} m (hedef {chk["kapi_boslugu"]})')
        if 'kanopi' in chk:
            o = bpy.data.objects[f'{aid}_Kanopi']
            b0, b1 = bbox(world_verts(o))
            ok(abs(b1.x - b0.x - chk['kanopi'][0]) < 0.01 and abs(b1.y - b0.y - chk['kanopi'][1]) < 0.01,
               f'kanopi {b1.x - b0.x:.2f} x {b1.y - b0.y:.2f} m (hedef {chk["kanopi"][0]} x {chk["kanopi"][1]})')
            ok(abs(b0.z - chk['kanopi_alt']) < 0.005, f'kanopi alt yuzu {b0.z:.3f} m (hedef {chk["kanopi_alt"]})')
            ck = colb[f'COL_{aid}_Kanopi']
            ok(ck[0].z >= 4.5, f'kanopi COL alti {ck[0].z:.2f} m: arac gecebilir')
            lam = bpy.data.objects.get(f'{aid}_Kanopi_Lamba')
            if lam:
                ok(bbox(world_verts(lam))[0].z > 5.15, 'kanopi lambalari 5,15 m ustunde')
            d = cdim('Market')
            ok(abs(d.x - chk['market'][0]) < 0.02 and abs(d.y - chk['market'][1]) < 0.02, f'market binasi {d.x:.2f} x {d.y:.2f} m')
            nk = len([n for n in colb if '_Kolon_' in n])
            na = len([n for n in colb if '_Ada_Pompa_' in n])
            ok(4 <= nk <= 6 and nk == chk['kolon_sayisi'], f'kanopi kolonu {nk} (4-6)')
            ok(na == chk['ada_sayisi'], f'pompa adasi {na}')
        if 'park_aralik' in chk:
            ok(chk['park_aralik'][0] <= len(ent['park_yerleri']) <= chk['park_aralik'][1], f'otopark yeri {len(ent["park_yerleri"])} (8-10)')
        # park yerleri
        slabs = [(b[0].x, b[1].x, b[0].y, b[1].y) for n, b in colb.items() if '_Doseme' in n and abs(b[1].z - 0.04) < 1e-4]
        blockers = [(n, b) for n, b in colb.items() if '_Doseme' not in n and b[1].z > 0.1 and b[0].z < 2.0]
        polys = []
        perr = []
        for i, sp in enumerate(ent['park_yerleri']):
            x, y_up, z = sp['konum']
            corners = rect_corners(x, z, sp['yon_derece'], *sp['boyut'])
            if abs(y_up - 0.04) > 1e-6:
                perr.append(f'#{i + 1} kot {y_up}')
            if not all(inside_union(c, slabs) for c in corners + [Vector((x, z))]):
                perr.append(f'#{i + 1} doseme disinda')
            for n, b in blockers:
                bp = [Vector((b[0].x, b[0].y)), Vector((b[1].x, b[0].y)), Vector((b[1].x, b[1].y)), Vector((b[0].x, b[1].y))]
                if sat_overlap(corners, bp):
                    perr.append(f'#{i + 1} {n} ile cakisiyor')
            for j, q in enumerate(polys):
                if sat_overlap(corners, q):
                    perr.append(f'#{i + 1} park #{j + 1} ile cakisiyor')
            polys.append(corners)
        ok(not perr, f'{len(ent["park_yerleri"])} park yeri dosemede, duvar/bina/esyaya ve birbirine girmiyor' + (f' {perr}' if perr else ''))
        # giris kesimleri
        gerr = []
        for a, b in ent['giris_kesimleri']:
            if a < -G / 2 or b > G / 2 or b - a < 3.0:
                gerr.append(f'[{a},{b}] sinir/genislik')
            for n, bb in blockers:
                if bb[0].y < 0.6 and bb[1].x > a + 0.01 and bb[0].x < b - 0.01:
                    gerr.append(f'[{a},{b}] {n} tarafindan kapali')
            if not inside_union(Vector(((a + b) / 2, 0.3)), slabs):
                gerr.append(f'[{a},{b}] onunde doseme yok')
        ok(not gerr, f'giris kesimleri {ent["giris_kesimleri"]} acik ve dosemeli' + (f' {gerr}' if gerr else ''))
        for t in ent['agac_noktalari']:
            inb = -G / 2 + 0.5 <= t[0] <= G / 2 - 0.5 and 0.3 <= t[2] <= D - 0.3
            hit = any(b[0].x - 0.3 < t[0] < b[1].x + 0.3 and b[0].y - 0.3 < t[2] < b[1].y + 0.3 for n, b in colb.items()
                      if '_Doseme' not in n and '_Ada_' not in n and b[1].z > 0.3)
            if not inb or hit:
                ok(False, f'agac noktasi {t} parsel disi ya da engelde')
        # manifest boyut
        vb = bbox([v for o in vis for v in world_verts(o)])
        ok(abs(ent['boyut'][0] - (vb[1].x - vb[0].x)) < 0.02 and abs(ent['boyut'][2] - (vb[1].y - vb[0].y)) < 0.02
           and abs(ent['boyut'][1] - vb[1].z) < 0.02, f'manifest boyut {ent["boyut"]}')
        info(f'bina izleri: {[(f["ad"], f["boyut"]) for f in ent["bina_izleri"]]}')

    # --- FBX geri okuma
    lines.append('')
    lines.append('[FBX yeniden ice aktarma]')
    for ent in man['varliklar']:
        aid = ent['id']
        bpy.ops.wm.read_factory_settings(use_empty=True)
        fp = OUT / ent['fbx']
        if not ok(fp.exists(), f'{ent["fbx"]} var ({fp.stat().st_size // 1024} KB)' if fp.exists() else f'{ent["fbx"]} yok'):
            continue
        bpy.ops.import_scene.fbx(filepath=str(fp), use_manual_orientation=True, axis_forward='Z', axis_up='Y',
                                 bake_space_transform=True)
        objs = list(bpy.context.scene.objects)
        bpy.context.view_layer.update()
        got = snapshot(objs)
        exp = ref.get(aid, {})
        ok(set(got) == set(exp), f'{aid}: {len(got)} nesne, adlar ayni' + ('' if set(got) == set(exp) else f' fark {sorted(set(got) ^ set(exp))}'))
        ok(not any(o.type == 'FONT' for o in objs), f'{aid}: FBX icinde FONT yok')
        derr = []
        for n, e in exp.items():
            g = got.get(n)
            if not g:
                continue
            if (g['min'] - e['min']).length > 0.003 or (g['max'] - e['max']).length > 0.003:
                derr.append(f'{n} olcu')
            if g['tris'] != e['tris']:
                derr.append(f'{n} ucgen {g["tris"]}/{e["tris"]}')
            if not n.startswith('COL_') and g['mats'] != e['mats']:
                derr.append(f'{n} malzeme')
        ok(not derr, f'{aid}: olcu, ucgen ve malzemeler korunuyor' + (f' {derr[:6]}' if derr else ''))
        cbad = [o.name for o in objs if o.name.startswith('COL_') and not is_aabb_box(o)[0]]
        ok(not cbad, f'{aid}: FBX icindeki COL kutulari hala eksen hizali')
        rot = [o.name for o in objs if max(abs(r) for r in o.rotation_euler) > 1e-4 or max(abs(s - 1) for s in o.scale) > 1e-4]
        info(f'{aid}: ice aktarim sonrasi donus/olcek 0/1 olmayan nesne: {len(rot)}')
    lines.append('')
    lines.append('SONUC: ' + ('TEMIZ' if not fails else f'{len(fails)} HATA'))
    txt = '\n'.join(lines)
    LOG.parent.mkdir(parents=True, exist_ok=True)
    LOG.write_text(txt, encoding='utf-8')
    print(txt)


main()
