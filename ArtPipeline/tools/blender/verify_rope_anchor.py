"""SM_RopeAnchor FBX'ini boş sahnede yeniden yükleyip doğrular. Dosya YAZMAZ; hata → exit 1.
Kullanım: blender -b --factory-startup --python verify_rope_anchor.py --python-exit-code 1
"""
import json
from pathlib import Path
import sys
import traceback
sys.dont_write_bytecode = True
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Export/Props/RopeAnchor'
NAME = 'SM_RopeAnchor'
TOL = 0.002


def srgb(c):
    return c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def main():
    errors = []
    meta = json.loads((OUT / 'rope_anchor_meta.json').read_text(encoding='utf-8'))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT / (NAME + '.fbx')))
    objs = {o.name: o for o in bpy.context.scene.objects}
    meshes = [o for o in objs.values() if o.type == 'MESH']
    empties = [o for o in objs.values() if o.type == 'EMPTY']
    if [o.name for o in meshes] != [NAME]:
        errors.append('mesh listesi: %s' % [o.name for o in meshes])
    if [o.name for o in empties] != ['RopeAttach']:
        errors.append('bos obje listesi: %s' % [o.name for o in empties])
    obj = objs.get(NAME)
    attach = objs.get('RopeAttach')
    if obj is None or attach is None:
        raise RuntimeError('; '.join(errors))
    bpy.context.view_layer.update()
    me = obj.data
    me.calc_loop_triangles()
    tris = len(me.loop_triangles)
    if not 200 <= tris <= 600:
        errors.append('ucgen %d (200-600)' % tris)
    if tris != meta['triangles']:
        errors.append('meta ucgen %s != %d' % (meta['triangles'], tris))
    mw = obj.matrix_world
    pts = [mw @ v.co for v in me.vertices]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    for a, b, label in ((lo, meta['bounds_min_m'], 'min'), (hi, meta['bounds_max_m'], 'max')):
        if any(abs(x - y) > TOL for x, y in zip(a, b)):
            errors.append('sinir %s %s != meta %s' % (label, [round(x, 4) for x in a], b))
    # Pivot: obje kökü dünya orijininde; kazık ekseni (x=y=0 yakını) zeminin altından tepeye uzanır.
    if (mw.translation).length > TOL:
        errors.append('pivot orijinde degil: %s' % tuple(mw.translation))
    axis = [p for p in pts if abs(p.x) < 0.04 and abs(p.y) < 0.04]
    if not axis or min(p.z for p in axis) > -0.05 or max(p.z for p in axis) < 0.25:
        errors.append('kazik ekseni pivotta degil')
    if max(hi[i] - lo[i] for i in range(3)) > 0.6:
        errors.append('boyut buyuk: %s' % [round(hi[i] - lo[i], 3) for i in range(3)])
    # RopeAttach: mesh'in çocuğu, kazık tepesinin hemen altında, kazık ekseninde.
    ap = attach.matrix_world.translation
    want = Vector(meta['empties']['RopeAttach']['position_m'])
    if attach.parent != obj:
        errors.append('RopeAttach ebeveyni %s' % (attach.parent.name if attach.parent else None))
    if (ap - want).length > TOL:
        errors.append('RopeAttach %s != meta %s' % (tuple(round(x, 4) for x in ap), tuple(want)))
    top = meta['stake']['top_z_m']
    if not (top - 0.08 <= ap.z < top) or Vector((ap.x, ap.y)).length > 0.01:
        errors.append('RopeAttach kazik tepesinin hemen altinda degil (z=%.3f, tepe %.3f)' % (ap.z, top))
    # Malzemeler: iki düz renk (koyu gri metal, turuncu-sarı halat), doku yok, her ikisi de kullanılıyor.
    mats = [m.name for m in me.materials]
    if sorted(mats) != ['M_RopeAnchor_Metal', 'M_RopeAnchor_Rope']:
        errors.append('malzemeler %s' % mats)
    used = {me.materials[p.material_index].name for p in me.polygons}
    if used != set(mats):
        errors.append('kullanilmayan malzeme: %s' % (set(mats) - used))
    for m in me.materials:
        if m.node_tree and any(n.type == 'TEX_IMAGE' for n in m.node_tree.nodes):
            errors.append('%s doku iceriyor' % m.name)
        bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if m.node_tree else None
        col = tuple(bsdf.inputs['Base Color'].default_value)[:3] if bsdf else tuple(m.diffuse_color)[:3]
        s = [srgb(c) for c in col]
        if m.name.endswith('Metal') and not (max(s) < 0.45 and max(s) - min(s) < 0.08):
            errors.append('metal rengi koyu gri degil: %s' % [round(c, 3) for c in s])
        if m.name.endswith('Rope') and not (s[0] > 0.8 and 0.4 < s[1] < 0.8 and s[2] < 0.3):
            errors.append('halat rengi turuncu-sari degil: %s' % [round(c, 3) for c in s])
    # Halat yığını zeminde durur (en alt nokta ~0), kazığın yanında.
    rope_pts = [mw @ me.vertices[i].co for p in me.polygons if me.materials[p.material_index].name.endswith('Rope') for i in p.vertices]
    coil = [p for p in rope_pts if Vector((p.x, p.y)).length > 0.1]
    if not coil or abs(min(p.z for p in coil)) > 0.005:
        errors.append('halat yigini zemine oturmuyor')
    print('ROPE_ANCHOR tris=%d bounds=%s..%s attach=%s mats=%s' % (
        tris, [round(x, 3) for x in lo], [round(x, 3) for x in hi], tuple(round(x, 3) for x in ap), mats), flush=True)
    # Eski kazığın metal üçgenleri yeni varyantta birebir aynı konumlarda olmalı.
    def metal_triangles(o):
        o.data.calc_loop_triangles()
        return sorted(tuple(sorted(tuple(round(c,5) for c in o.matrix_world @ o.data.vertices[i].co) for i in t.vertices))
            for t in o.data.loop_triangles if 'Metal' in o.data.materials[t.material_index].name)
    legacy = metal_triangles(obj)
    old_attach = ap.copy()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT/(NAME+'_Bare.fbx')))
    bare = bpy.data.objects.get(NAME+'_Bare')
    ba = bpy.data.objects.get('RopeAttach')
    if bare is None or ba is None:
        errors.append('Bare mesh / RopeAttach eksik')
    else:
        if metal_triangles(bare) != legacy:
            errors.append('Bare metal geometrisi eski kaziktan farkli')
        if bare.matrix_world.translation.length>TOL or (ba.matrix_world.translation-old_attach).length>TOL or ba.parent!=bare:
            errors.append('Bare pivot / RopeAttach farkli')
        bare.data.calc_loop_triangles()
        bt = len(bare.data.loop_triangles)
        if bt != len(legacy) or bt != meta.get('bare_variant',{}).get('triangles'):
            errors.append('Bare ucgen sayisi / meta hatali')
        if any('Rope' in m.name and 'Metal' not in m.name for m in bare.data.materials):
            errors.append('Bare halat malzemesi iceriyor')
        if len([o for o in bpy.data.objects if o.type=='MESH'])!=1 or len([o for o in bpy.data.objects if o.type=='EMPTY'])!=1:
            errors.append('Bare fazladan obje iceriyor')
        print('ROPE_ANCHOR_BARE tris=%d identical_metal=True attach=%s' % (bt,tuple(ba.matrix_world.translation)),flush=True)
    if errors:
        for e in errors:
            print('ERROR', e, flush=True)
        print('VERIFY FAILED', flush=True)
        sys.exit(1)
    print('VERIFY OK', flush=True)


if __name__ == '__main__':
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        traceback.print_exc()
        print('VERIFY FAILED', flush=True)
        sys.exit(1)
