"""Kangal FBX ve dolap seçimini doğrular. İzlenen hiçbir dosyayı değiştirmez."""
import hashlib
import json
import math
from pathlib import Path
import sys
import traceback
sys.dont_write_bytecode = True
import bpy
from mathutils import Vector, Euler

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'Export/Props/RopeCoil'


def srgb(c):
    return 12.92*c if c<=.0031308 else 1.055*c**(1/2.4)-.055


def main():
    errors = []
    meta = json.loads((OUT/'rope_coil_meta.json').read_text(encoding='utf-8'))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT/'SM_RopeCoil.fbx'),use_anim=False)
    objects = list(bpy.context.scene.objects)
    meshes = [o for o in objects if o.type=='MESH']
    assert len(meshes)==1 and meshes[0].name=='SM_RopeCoil', 'mesh listesi hatali'
    obj = meshes[0]
    obj.data.calc_loop_triangles()
    tris = len(obj.data.loop_triangles)
    if not 300<=tris<=700 or tris!=meta['triangles']:
        errors.append('ucgen butcesi / meta hatali: %d' % tris)
    pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    if obj.matrix_world.translation.length>.001 or ((lo+hi)/2).length>.005:
        errors.append('pivot kangal merkezinde degil')
    dims = hi-lo
    if abs(dims.x-.44)>.01 or abs(dims.z-.44)>.01 or (dims-Vector(meta['dimensions_m'])).length>.001:
        errors.append('boyutlar hatali')
    # Meta'ya güvenmek yerine FBX bağlı bileşenlerini ölç: geniş kapalı tur halkaları ve kısa uç.
    neighbors = {v.index:set() for v in obj.data.vertices}
    for edge in obj.data.edges:
        a,b = edge.vertices
        neighbors[a].add(b)
        neighbors[b].add(a)
    remaining, components = set(neighbors), []
    while remaining:
        todo, component = [remaining.pop()], set()
        while todo:
            i = todo.pop()
            component.add(i)
            found = neighbors[i] & remaining
            remaining -= found
            todo.extend(found)
        components.append(component)
    rings = [c for c in components if max(pts[i].x for i in c)-min(pts[i].x for i in c)>.30]
    diameters = [max(math.hypot(pts[i].x,pts[i].z) for i in c)-min(math.hypot(pts[i].x,pts[i].z) for i in c) for c in rings]
    if not 5<=len(rings)<=7 or len(rings)!=meta.get('turn_count'):
        errors.append('gorunur sarim sayisi hatali: %d' % len(rings))
    if any(not .026<=d<=.034 for d in diameters) or not .100<=dims.y<=.120:
        errors.append('3 cm halat / 10-12 cm demet kalinligi hatali')
    if abs(dims.y-meta.get('bundle_depth_m',0))>.001:
        errors.append('demet kalinligi meta uyusmazligi')
    tails = [c for c in components if c not in rings]
    if len(tails)!=1 or min((pts[i]-pts[j]).length for i in tails[0] for c in rings for j in c)>.03:
        errors.append('kisa uc yok veya govdeden kopuk')
    empties = {o.name:o for o in objects if o.type=='EMPTY'}
    if set(empties)!={'Grip_L','Grip_R'}:
        errors.append('Grip boslari eksik / fazla')
    for name, expected in (('Grip_L',(.204,0,0)),('Grip_R',(-.204,0,0))):
        marker = empties[name]
        pos = marker.matrix_world.translation
        if marker.parent != obj or (pos-Vector(expected)).length>.001 or (pos-Vector(meta['grips'][name]['position_m'])).length>.001:
            errors.append(name+': konum / ebeveyn hatali')
    anchor = json.loads((ROOT/'Export/Props/RopeAnchor/rope_anchor_meta.json').read_text(encoding='utf-8'))
    expected_rgb = anchor['materials']['M_RopeAnchor_Rope']['srgb']
    if len(obj.data.materials)!=1 or meta['color_hex']!='#F29E1F' or meta['rope_radius_m']!=.015:
        errors.append('renk / kalinlik kaydi hatali')
    for mat in obj.data.materials:
        bsdf = next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        col = bsdf.inputs['Base Color'].default_value[:3]
        if max(abs(srgb(c)-e) for c,e in zip(col,expected_rgb))>.003:
            errors.append('lineer Base Color eski halatla ayni degil')
        if any(n.type=='TEX_IMAGE' for n in mat.node_tree.nodes):
            errors.append('beklenmeyen doku')
    slot = meta['towtruck_slot']
    truck_meta = json.loads((ROOT/'Export/TowTruck/towtruck_meta.json').read_text(encoding='utf-8'))
    part = slot['cabinet_part']
    if part not in truck_meta['parts'] or slot['door_motion']!=truck_meta['parts'][part]['motion']:
        errors.append('dolap parcasi / mentese meta hatali')
    if slot['up']!='Z' or slot['units']!='meters' or slot['parent']!='SM_TowTruck' or slot['side']!='road_shoulder':
        errors.append('slot koordinat / taraf kaydi hatali')
    pos = Vector(slot['position_m'])
    rot = Euler([math.radians(x) for x in slot['rotation_euler_deg']],slot['rotation_order']).to_matrix()
    placed = [pos+rot@p for p in pts]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ROOT/'Export/TowTruck/SM_TowTruck.fbx'),use_anim=False)
    candidates = []
    for name in truck_meta['parts']:
        if '_LockerDoor_L' not in name:
            continue
        door = bpy.data.objects[name]
        dp = [door.matrix_world@v.co for v in door.data.vertices]
        bottom = min(p.z for p in dp)
        if bottom<=1.2:
            candidates.append((bottom,name,dp))
    bottom, chosen, dp = min(candidates)
    if part!=chosen or abs(slot['floor_m']-bottom)>.001:
        errors.append('en alcak erisilebilir yol dolabi secilmemis')
    # +X, Bölüm 1 +90° Z yerleşiminde yola (+Y) bakar. Kangal kapak iç yüzünden içeride.
    if min(p.x for p in placed)<.80 or max(p.x for p in placed)>1.20:
        errors.append('kangal dolabin derinlik siniri disinda')
    if min(p.y for p in placed)<min(p.y for p in dp) or max(p.y for p in placed)>max(p.y for p in dp):
        errors.append('kangal kapak genisligini asiyor')
    if min(p.z for p in placed)<bottom or max(p.z for p in placed)>max(p.z for p in dp) or not 1.0<=pos.z<=1.2:
        errors.append('kangal dolaba sigmiyor / el yuksekligi hatali')
    source = ROOT/'tools/blender/build_rope_coil.py'
    digest = hashlib.sha256(source.read_bytes().replace(b'\r\n',b'\n')).hexdigest()
    if meta.get('source_sha256')!=digest:
        errors.append('kangal kaynak hash guncel degil')
    image = bpy.data.images.load(str(OUT/'SM_RopeCoil_preview.png'))
    width,height = image.size
    corner = max(sum(image.pixels[i:i+3])/3*255 for i in (0,(width-1)*4,(height-1)*width*4,(height*width-1)*4))
    if corner<150:
        errors.append('karanlik onizleme')
    print('ROPE_COIL tris=%d dims=%s grip_span=%.3f slot=%s floor=%.3f corner=%.1f' % (tris,tuple(round(c,3) for c in dims),.408,part,bottom,corner),flush=True)
    print('BUNDLE turns=%d rope_diameters_m=%s depth_m=%.4f' % (len(rings),[round(d,4) for d in diameters],dims.y),flush=True)
    if errors:
        for error in errors:
            print('ERROR',error,flush=True)
        raise RuntimeError('kangal dogrulanamadi')
    print('VERIFY OK',flush=True)


if __name__=='__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        print('VERIFY FAILED',flush=True)
        sys.exit(1)
