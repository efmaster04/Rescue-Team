# -*- coding: utf-8 -*-
"""
Donati seti kontrolu. Teslim edilen dosyalari (FBX + manifest + malzeme JSON + .blend) denetler.

  blender.exe -b --factory-startup -P verify_donati.py

1) JSON: malzeme listesi ve manifest alanlari.
2) .blend: FONT/CURVE yok, donus 0 / olcek 1, duz golgeleme, malzemeler mesh duzeyinde ve JSON'da.
3) Her FBX yeniden ice aktarilir (axis_forward=Z, axis_up=Y, bake_space_transform): nesne adlari
   korunuyor mu, olculer brif hedeflerinde mi, yon kurali (basliklar/tabelalar -Y, kol -X),
   mercek ve isik nesneleri, EMPTY'ler, COL_ kutularinin eksen hizasi, ucgen butcesi.
Sonuc: Kaynak/Raporlar/Donati_verify_sonuc.txt
"""
import bpy
import json
import math
import sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(r"C:\Users\uur\RescueTeam_OfisHaritasi")
OUT = ROOT / "Oyun" / "Assets" / "OfisHaritasi" / "Modeller" / "Donati"
BLEND = ROOT / "Kaynak" / "Blender" / "Donati.blend"
REPORT = ROOT / "Kaynak" / "Raporlar" / "Donati_verify_sonuc.txt"

lines, fails = [], []
nchecks = 0


def check(cond, msg):
    global nchecks
    nchecks += 1
    if not cond:
        fails.append(msg)
        lines.append("  HATA  " + msg)
    return cond


def info(msg):
    lines.append(msg)


def near(a, b, tol):
    return abs(a - b) <= tol


# ------------------------------------------------------------------ 1) JSON
man = json.loads((OUT / "Donati_manifest.json").read_text(encoding='utf-8'))
mats = json.loads((OUT / "Donati_malzemeler.json").read_text(encoding='utf-8'))["materials"]
MATN = {m["name"] for m in mats}
info(f"[JSON] {len(man['varliklar'])} varlik, {len(mats)} malzeme")
for m in mats:
    check(m["name"].startswith("DN_"), f"malzeme oneki: {m['name']}")
    check(len(m["color"]) == 3 and all(0 <= c <= 1 for c in m["color"]), f"renk sRGB 0-1: {m['name']}")
    check(len(m["emission"]) == 3, f"emission alani: {m['name']}")
    check(0 <= m["metallic"] <= 1 and 0 <= m["smoothness"] <= 1 and m["alpha"] == 1.0, f"PBR alanlari: {m['name']}")
for req in ['DN_Sinyal_Kirmizi', 'DN_Sinyal_Sari', 'DN_Sinyal_Yesil', 'DN_Lamba', 'DN_Yaprak_1', 'DN_Yaprak_2',
            'DN_Yaprak_3', 'DN_Govde']:
    check(req in MATN, f"brifteki malzeme eksik: {req}")
for n in ['DN_Sinyal_Kirmizi', 'DN_Sinyal_Sari', 'DN_Sinyal_Yesil']:
    m = next(x for x in mats if x['name'] == n)
    check(m['emission'] == [0, 0, 0], f"{n} emission 0 olmali (Unity yakacak)")
lam = next(x for x in mats if x['name'] == 'DN_Lamba')
check(sum(lam['emission']) > 0.5, "DN_Lamba emission acik olmali")

EXPECTED_IDS = ['Trafik_Isigi_Kollu', 'Trafik_Isigi_Direk', 'Tabela_YolVer', 'Tabela_Dur', 'Tabela_YayaGecidi',
                'Tabela_Park', 'Bariyer_Otoyol', 'Beton_Bariyer', 'Baba', 'Sokak_Lambasi', 'Park_Lambasi',
                'Elektrik_Diregi', 'Otobus_Duragi', 'Bank', 'Cop_Kutusu', 'Cop_Konteyneri', 'Cesme',
                'Oyun_Kaydirak', 'Oyun_Salincak', 'Agac_Cinar', 'Agac_Kavak', 'Agac_Cam', 'Agac_Meyve',
                'Cali_Yuvarlak', 'Cali_Cit']
ids = [e['id'] for e in man['varliklar']]
check(sorted(ids) == sorted(EXPECTED_IDS), f"varlik listesi brifle ayni degil: {set(EXPECTED_IDS) ^ set(ids)}")
for e in man['varliklar']:
    for k in ('id', 'fbx', 'boyut', 'ucgen', 'tur'):
        check(k in e, f"{e['id']}: manifest alani eksik '{k}'")
    check(e['tur'] in ('trafik', 'aydinlatma', 'esya', 'bitki'), f"{e['id']}: tur gecersiz {e['tur']}")
    check((OUT / e['fbx']).exists(), f"{e['id']}: FBX yok")
    if e['id'].startswith('Trafik_Isigi'):
        check(set(e.get('lensler', {})) == {'kirmizi', 'sari', 'yesil'}, f"{e['id']}: lensler alani")
    if e['id'] == 'Elektrik_Diregi':
        check(len(e.get('tel_noktalari', [])) == 3, "Elektrik_Diregi: tel_noktalari 3 olmali")
    if e['id'] in ('Sokak_Lambasi', 'Park_Lambasi'):
        check(e.get('isik_nesnesi') == 'Lamba_Isik' and len(e.get('isik_noktasi', [])) == 3,
              f"{e['id']}: isik_nesnesi / isik_noktasi")
    if e['id'] in ('Bariyer_Otoyol', 'Cali_Cit', 'Beton_Bariyer'):
        check('segment_uzunlugu' in e, f"{e['id']}: segment_uzunlugu")
    if e['tur'] == 'bitki':
        check('tac_yaricapi' in e and 'yukseklik' in e, f"{e['id']}: tac_yaricapi/yukseklik")

# ------------------------------------------------------------------ 2) .blend
bpy.ops.wm.open_mainfile(filepath=str(BLEND))
bad_types = [o.name for o in bpy.data.objects if o.type in ('FONT', 'CURVE', 'SURFACE', 'META', 'GPENCIL', 'GREASEPENCIL')]
check(not bad_types, f".blend: yazi/egri nesnesi var: {bad_types}")
info(f"[BLEND] {len(bpy.data.objects)} nesne, FONT/CURVE yok: {not bad_types}")
for o in bpy.data.objects:
    check(all(abs(r) < 1e-6 for r in o.rotation_euler) and all(abs(s - 1) < 1e-6 for s in o.scale),
          f".blend {o.name}: donus/olcek uygulanmamis")
    if o.type == 'MESH':
        check(not any(p.use_smooth for p in o.data.polygons), f".blend {o.name}: yumusak golgeleme var")
        check(all(s.link == 'DATA' for s in o.material_slots), f".blend {o.name}: nesne duzeyi malzeme")
        for m in o.data.materials:
            check(m is not None and m.name in MATN, f".blend {o.name}: JSON'da olmayan malzeme {m and m.name}")
        check(not o.modifiers, f".blend {o.name}: uygulanmamis modifier")

# ------------------------------------------------------------------ 3) FBX
TARGET = {   # id: (genislik X, yukseklik, derinlik Y) hedef ve tolerans; None = denetlenmez
    'Trafik_Isigi_Kollu': ((4.0, 4.4), (6.5, 6.6), None),
    'Trafik_Isigi_Direk': (None, (3.2, 3.7), None),
    'Tabela_YolVer': (None, (2.8, 2.85), None), 'Tabela_Dur': (None, (2.8, 2.85), None),
    'Tabela_YayaGecidi': (None, (2.8, 2.85), None), 'Tabela_Park': (None, (2.8, 2.85), None),
    'Bariyer_Otoyol': ((4.0, 4.0), (0.70, 0.80), None),
    'Beton_Bariyer': ((2.0, 2.0), (0.78, 0.84), (0.58, 0.64)),
    'Baba': (None, (0.88, 0.92), None),
    'Sokak_Lambasi': ((1.8 + 0.1, 2.8), (7.95, 8.1), None),
    'Park_Lambasi': (None, (3.45, 3.55), None),
    'Elektrik_Diregi': (None, (9.0, 9.35), None),
    'Otobus_Duragi': ((4.15, 4.25), (2.55, 2.65), (1.55, 1.65)),
    'Bank': ((1.78, 1.82), (0.80, 0.95), (0.45, 0.65)),
    'Cop_Kutusu': (None, (0.9, 1.2), None),
    'Cop_Konteyneri': ((1.30, 1.45), (1.25, 1.40), (1.00, 1.20)),
    'Cesme': ((1.5, 1.7), (1.9, 2.1), (0.7, 0.9)),
    'Oyun_Kaydirak': (None, (1.6, 2.2), (2.8, 4.0)),
    'Oyun_Salincak': ((2.4, 3.2), (2.0, 2.5), None),
    'Agac_Cinar': (None, (9.0, 10.0), None), 'Agac_Kavak': (None, (12.5, 13.5), None),
    'Agac_Cam': (None, (8.5, 9.5), None), 'Agac_Meyve': (None, (3.5, 4.5), None),
    'Cali_Yuvarlak': (None, (1.0, 1.4), None), 'Cali_Cit': ((2.0, 2.0), (0.9, 1.3), None),
}
SIGN_MATS = {'DN_Tabela_Kirmizi', 'DN_Tabela_Mavi', 'DN_Beyaz', 'DN_Siyah_Mat'}


def wverts(o):
    return [o.matrix_world @ v.co for v in o.data.vertices]


def bounds(objs):
    pts = [p for o in objs for p in wverts(o)]
    mn = Vector([min(p[i] for p in pts) for i in range(3)])
    mx = Vector([max(p[i] for p in pts) for i in range(3)])
    return mn, mx


def from_unity(u):
    return Vector((u[0], u[2], u[1]))


def front_cap_normal(o):
    """Mercek diskinin gorunen (en ondeki, -Y) kapak yuzunun dunya normali."""
    caps = [p for p in o.data.polygons if abs(p.normal.y) > 0.99]
    best = min(caps, key=lambda p: (o.matrix_world @ p.center).y)
    return (o.matrix_world.to_3x3() @ best.normal).normalized()


total = 0
for e in man['varliklar']:
    aid = e['id']
    info(f"\n[{aid}] tur={e['tur']} ucgen={e['ucgen']}/{e.get('ucgen_butcesi')} boyut={e['boyut']}")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT / e['fbx']), axis_forward='Z', axis_up='Y', bake_space_transform=True)
    objs = {o.name: o for o in bpy.data.objects}
    want = {n['ad']: n for n in e['nesneler']}
    check(set(objs) == set(want), f"{aid}: FBX ad farki {sorted(set(objs) ^ set(want))}")
    check(not [n for n in objs if '.0' in n], f"{aid}: kopya son eki (.001) var")
    for n, o in objs.items():
        check(all(abs(r) < 1e-4 for r in o.rotation_euler) and all(abs(s - 1) < 1e-4 for s in o.scale),
              f"{aid}/{n}: FBX'te donus/olcek 0/1 degil")
        if n in want:
            check((o.location - from_unity(want[n]['konum'])).length < 0.005, f"{aid}/{n}: orijin konumu manifestten farkli")
    for n, w in want.items():
        o = objs.get(n)
        if not o:
            continue
        if w['tur'] == 'nokta':
            check(o.type == 'EMPTY', f"{aid}/{n}: EMPTY olmali")
        else:
            check(o.type == 'MESH', f"{aid}/{n}: MESH olmali")
    meshes = [o for o in objs.values() if o.type == 'MESH']
    vis = [o for o in meshes if not o.name.startswith('COL_')]
    cols = [o for o in meshes if o.name.startswith('COL_')]
    # malzemeler
    for o in vis:
        for m in o.data.materials:
            check(m.name in MATN, f"{aid}/{o.name}: malzeme JSON'da yok: {m.name}")
        check(len(o.data.materials) > 0, f"{aid}/{o.name}: malzemesiz gorsel mesh")
    # ucgen
    tris = sum(len(p.vertices) - 2 for o in vis for p in o.data.polygons)
    total += tris
    check(tris == e['ucgen'], f"{aid}: FBX ucgen {tris} != manifest {e['ucgen']}")
    check(tris <= e['ucgen_butcesi'], f"{aid}: ucgen butcesi asildi {tris}>{e['ucgen_butcesi']}")
    # boyut ve sinirlar
    mn, mx = bounds(vis)
    check((mn - from_unity(e['sinirlar_unity']['min'])).length < 0.01 and
          (mx - from_unity(e['sinirlar_unity']['max'])).length < 0.01, f"{aid}: sinirlar manifestle uyusmuyor")
    dims = (mx.x - mn.x, mx.z, mx.y - mn.y)
    for k, (rng, d) in enumerate(zip(TARGET[aid], dims)):
        if rng:
            check(rng[0] - 0.01 <= d <= rng[1] + 0.01, f"{aid}: olcu {'XYZ'[k] if k != 1 else 'yukseklik'}={d:.3f} hedef {rng}")
    check(mn.z >= -0.31, f"{aid}: zemin alti cok derin {mn.z:.3f}")
    # COL_ kutulari: 8 kose, her eksende 2 deger, donussuz
    if aid.startswith('Cali_'):
        check(len(cols) == 0, f"{aid}: calida COL_ beklenmiyor (brif: yalniz agac govdeleri)")
    else:
        check(len(cols) >= 1, f"{aid}: COL_ kutusu yok")
    for c in cols:
        pts = wverts(c)
        uniq = {tuple(round(x, 4) for x in p) for p in pts}
        ax_ok = all(len({round(p[i], 4) for p in pts}) == 2 for i in range(3))
        check(len(uniq) == 8 and ax_ok, f"{aid}/{c.name}: COL_ eksen hizali 8 koseli kutu degil")
        cmn, cmx = bounds([c])
        check(cmn.z >= -0.001, f"{aid}/{c.name}: COL_ zemin altinda basliyor")
    # --- yone ve sete ozel kontroller
    main = objs.get(aid)
    if aid.startswith('Trafik_Isigi'):
        heads = {}
        for renk, adlar in e['lensler'].items():
            for n in adlar:
                o = objs.get(n)
                if not check(o is not None, f"{aid}: mercek yok {n}"):
                    continue
                check(n.startswith('Isik_') and n.split('_')[1] in ('Kirmizi', 'Sari', 'Yesil'), f"{aid}: mercek adi {n}")
                mname = {'kirmizi': 'DN_Sinyal_Kirmizi', 'sari': 'DN_Sinyal_Sari', 'yesil': 'DN_Sinyal_Yesil'}[renk]
                check([m.name for m in o.data.materials] == [mname], f"{aid}/{n}: mercek malzemesi {mname} olmali")
                nrm = front_cap_normal(o)
                check(nrm.y < -0.95, f"{aid}/{n}: mercek -Y'ye bakmiyor (normal {tuple(round(x, 2) for x in nrm)})")
                heads.setdefault(n[-1], {})[renk] = o.location.copy()
        for suf, hd in heads.items():
            check(hd['kirmizi'].z > hd['sari'].z > hd['yesil'].z, f"{aid}: baslik _{suf} sira kirmizi>sari>yesil degil")
            check(len({round(v.x, 3) for v in hd.values()}) == 1, f"{aid}: baslik _{suf} mercekleri ayni dikeyde degil")
        for b in e['basliklar']:
            hx = b['merkez_x']
            region = [p for p in wverts(main) if abs(p.x - hx) < 0.26 and p.y < -0.06 and
                      (p.z > 4.0 if b['alt_kenar'] > 4 else 1.5 < p.z < 4.0)]
            zb = min(p.z for p in region)
            check(near(zb, b['alt_kenar'], 0.015), f"{aid}: {b['ad']} alt kenari {zb:.3f} != {b['alt_kenar']}")
            info(f"  {b['ad']}: alt kenar {zb:.3f} m, x={hx}")
        if aid == 'Trafik_Isigi_Kollu':
            mmn, mmx = bounds([main])
            check(mmn.x < -3.95 and mmx.x < 0.25, f"{aid}: kol -X'te degil (x {mmn.x:.2f}..{mmx.x:.2f})")
            check(objs['Isik_Kirmizi_1'].location.x < -3.0 and abs(objs['Isik_Kirmizi_2'].location.x) < 0.01,
                  f"{aid}: _1 kol ucunda, _2 direkte olmali")
            pole_top = max(p.z for p in wverts(main) if abs(p.x) < 0.12 and abs(p.y) < 0.12)
            check(near(pole_top, 6.5, 0.06), f"{aid}: direk boyu {pole_top:.2f} (6.5)")
            info(f"  kol ucu x={mmn.x:.3f}, direk ust {pole_top:.3f}")
        else:
            pole_top = max(p.z for p in wverts(main) if abs(p.x) < 0.1 and -0.075 < p.y < 0.1)
            check(near(pole_top, 3.2, 0.05), f"{aid}: direk boyu {pole_top:.2f} (3.2)")
            info(f"  direk ust {pole_top:.3f}")
    if aid.startswith('Tabela_'):
        sign_faces = [p for p in main.data.polygons if main.data.materials[p.material_index].name in SIGN_MATS]
        check(sign_faces and all(p.normal.y < -0.99 for p in sign_faces), f"{aid}: renkli tabela yuzleri -Y'ye bakmiyor")
        info(f"  renkli yuz {len(sign_faces)}, hepsi -Y: {all(p.normal.y < -0.99 for p in sign_faces)}")
    if aid in ('Sokak_Lambasi', 'Park_Lambasi'):
        li = objs.get('Lamba_Isik')
        if check(li is not None, f"{aid}: Lamba_Isik yok"):
            check([m.name for m in li.data.materials] == ['DN_Lamba'], f"{aid}: Lamba_Isik malzemesi DN_Lamba olmali")
            pt = from_unity(e['isik_noktasi'])
            info(f"  Lamba_Isik orijin {tuple(round(x, 3) for x in li.location)}, isik noktasi {tuple(round(x, 3) for x in pt)}")
            if aid == 'Sokak_Lambasi':
                check(li.location.x < -1.8, f"{aid}: isik kol ucunda (-X) degil")
                check(any((li.matrix_world.to_3x3() @ p.normal).z < -0.99 for p in li.data.polygons), f"{aid}: isik yuzu asagi bakmiyor")
                mmn, _ = bounds([main])
                check(mmn.x <= -1.8, f"{aid}: kol 1.8 m'ye ulasmiyor ({mmn.x:.2f})")
    if aid == 'Elektrik_Diregi':
        for t in e['tel_noktalari']:
            o = objs.get(t['ad'])
            if check(o is not None and o.type == 'EMPTY', f"{aid}: {t['ad']} EMPTY yok"):
                check((o.location - from_unity(t['konum'])).length < 0.005, f"{aid}: {t['ad']} konumu manifestten farkli")
                info(f"  {t['ad']} {tuple(round(x, 3) for x in o.location)} (Unity {t['konum']})")
        conc = [p for o in [main] for p in wverts(o)]
        check(near(max(p.z for p in conc if abs(p.x) < 0.09 and abs(p.y) < 0.07 and p.z < 9.01), 9.0, 0.03),
              f"{aid}: beton direk boyu 9 m degil")
    if aid in ('Bariyer_Otoyol', 'Cali_Cit'):
        L = e['segment_uzunlugu']
        check(near(mn.x, 0.0, 0.002) and near(mx.x, L, 0.002), f"{aid}: segment 0..{L} +X degil ({mn.x:.3f}..{mx.x:.3f})")
        ends0 = sorted((round(p.y, 4), round(p.z, 4)) for p in wverts(main) if abs(p.x) < 1e-4)
        ends1 = sorted((round(p.y, 4), round(p.z, 4)) for p in wverts(main) if abs(p.x - L) < 1e-4)
        if aid == 'Cali_Cit':
            check(ends0 == ends1 and len(ends0) > 0, f"{aid}: uc kesitler ozdes degil (dikis gorunur)")
        else:
            rail0 = [q for q in ends0 if q[1] > 0.43]
            rail1 = [q for q in ends1 if q[1] > 0.43]
            check(rail0 == rail1 and len(rail0) > 0, f"{aid}: ray uc kesitleri ozdes degil")
            check(mn.y < -0.07, f"{aid}: W profil one (-Y) bakmiyor")
    if aid == 'Beton_Bariyer':
        check(near((mn.x + mx.x) / 2, 0, 0.002) and near((mn.y + mx.y) / 2, 0, 0.002), f"{aid}: orijin ortada degil")
    if e['tur'] == 'bitki' and aid.startswith('Agac'):
        c = next(o for o in cols if o.name == 'COL_Govde')
        cmn, cmx = bounds([c])
        check(cmn.x < 0 < cmx.x and cmn.y < 0 < cmx.y and cmx.z >= 1.0, f"{aid}: COL_Govde govdeyi sarmiyor")
        check(len(cols) == 1, f"{aid}: agacta yalniz COL_Govde olmali")
        check(near(e['yukseklik'], mx.z, 0.02), f"{aid}: manifest yukseklik {e['yukseklik']} != {mx.z:.2f}")
        info(f"  yukseklik {mx.z:.2f} m, tac yaricapi {e['tac_yaricapi']} m, COL_Govde z 0..{cmx.z:.2f}")
    if 'hareketli_parcalar' in e:
        for hp in e['hareketli_parcalar']:
            o = objs.get(hp['ad'])
            if check(o is not None, f"{aid}: hareketli parca yok {hp['ad']}"):
                check((o.location - from_unity(hp['mafsal'])).length < 0.005, f"{aid}: {hp['ad']} orijini mafsalda degil")
    if aid == 'Otobus_Duragi':
        check(near((mn.x + mx.x) / 2, 0, 0.01) and near((mn.y + mx.y) / 2, 0, 0.02), f"{aid}: orijin taban merkezinde degil")

info(f"\nTOPLAM UCGEN (gorsel): {total}")
status = "TEMIZ" if not fails else f"{len(fails)} HATA"
head = [f"Donati verify sonucu: {status}  ({nchecks} kontrol)", ""]
REPORT.parent.mkdir(parents=True, exist_ok=True)
REPORT.write_text("\n".join(head + lines + (["", "HATALAR:"] + fails if fails else [])), encoding='utf-8')
print("\n".join(head + lines))
print("VERIFY", status, nchecks)
if fails:
    for f in fails:
        print("FAIL", f)
