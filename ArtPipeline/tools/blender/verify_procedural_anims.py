"""Export/Animations/Procedural kliplerini FBX'ten yeniden yükleyip doğrular. Dosya YAZMAZ.
Kullanım: blender -b --factory-startup --python verify_procedural_anims.py --python-exit-code 1 [-- --only Ad]
"""
import hashlib
import json
import math
from pathlib import Path
import sys
import traceback
sys.dont_write_bytecode = True
import re
import bpy
from mathutils import Matrix, Vector, Euler
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Export/Animations/Procedural'
CREW = ROOT / 'Export/RescueCrew/SK_RescueCrew_Blue.fbx'
PATIENT = ROOT / 'Export/Patient/SK_Patient_Tourist.fbx'
PREFIX = 'mixamorig:'
FPS = 30
EXPECTED = {
    'Carry_Stretcher_Walk': (0.5, True), 'Carry_OneHand_Walk': (0.4, True), 'PickUp': (1.0, False),
    'PutDown': (0.9, False), 'GetUp_FromBack': (1.6, False), 'Revive_Helper': (1.2, True), 'Jump': (0.8, False),
    'Startled': (0.9, False), 'Lying_HelpWave': (1.4, True), 'Lying_Groan': (2.0, True), 'Celebrate': (1.5, False),
    # r3
    'Patient_SitDazed': (2.0, True), 'Patient_SitDazed_ArmSling': (2.0, True), 'Patient_DraggedOut': (1.5, False),
    'Piggyback_Rider': (1.0, True), 'Extract_Pull': (1.5, False), 'Rope_Descend': (1.0, True), 'Rope_Climb': (1.0, True),
    'Rope_Hang_Idle': (1.5, True), 'Piggyback_PickUp': (1.5, False), 'Piggyback_Rope_Climb': (1.0, True),
    'Piggyback_Walk': (0.8, True), 'Piggyback_PutDown': (1.2, False), 'Rope_Pull': (1.0, True),
    # r4
    'Carry_Stretcher_Front_Walk': (0.5, True),
    'Cabinet_TakeCoil': (1.4, False), 'Carry_Coil_Idle': (1.5, True), 'Carry_Coil_Walk': (0.4, True),
    'Rope_TieStake': (2.0, False), 'Rope_Throw': (1.4, False)}
COIL_CLIPS = {'Cabinet_TakeCoil', 'Carry_Coil_Idle', 'Carry_Coil_Walk', 'Rope_TieStake', 'Rope_Throw'}
COMBAT_EXPECTED = {
    'Push_Shove':(.6,False), 'Tackle_Dive':(.9,False), 'Tackle_GetUp':(1.,False),
    'Throw_Aim':(1.,True), 'Throw_Release':(.5,False), 'Whistle_Blow':(1.2,False),
    'HitReact':(.5,False), 'Thief_Sneak_Walk':(.6,True), 'Thief_Run_Carry':(.4,True),
    'Thief_Run':(.4,True), 'Thief_Grab':(.8,False), 'Thief_Punch':(.8,False),
    'Thief_Stunned':(1.2,True), 'Thief_ThrowInVan':(.8,False)}
EXPECTED.update(COMBAT_EXPECTED)
TRANSITIONS = [('Tackle_Dive','Tackle_GetUp',2), ('Throw_Aim','Throw_Release',2),
               ('Thief_Grab','Thief_Run_Carry',5)]
# Sedye tutuşu: her karede avuç noktası (el kemiği başı + kemik yönünde GRIP_POINT) meta'daki sap noktasına
# GRIP_LIMIT'ten yakın olmalı; meta'daki iki sap noktası sedyenin sap aralığıyla uyumlu olmalı.
STRETCHER_META = ROOT / 'Export/Stretcher/stretcher_meta.json'
GRIP_CLIPS = {'Carry_Stretcher_Walk': 'Handle_Rear', 'Carry_Stretcher_Front_Walk': 'Handle_Front'}
GRIP_POINT = 0.05
GRIP_LIMIT = 0.04
GRIP_SPACING_TOL = 0.01
GRIP_STRICT = {'Carry_Stretcher_Front_Walk'}
CIVILIAN = ROOT / 'Export/Patient/SK_Patient_Civilian.fbx'
PATIENT_CLIPS = ('Lying_', 'Patient_', 'Piggyback_Rider')
# Sırt taşıma: binen kökü taşıyanın Spine1 deformasyonuna rijit bağlı (build ile aynı ofset); bağlı kareler.
PIGGY_BONE = 'Spine1'
PIGGY_OFFSET = (0.0, 0.312, 0.292)
PIGGY_ATTACH = {'Piggyback_PickUp': (17, None), 'Piggyback_PutDown': (1, 16)}
PAIR_LIMIT = 0.04
TORSO = {'Hips', 'Spine', 'Spine1', 'Spine2', 'Neck'}
ARM = ('Arm', 'ForeArm', 'Hand', 'HandIndex1', 'HandIndex2', 'HandThumb1', 'HandThumb2')
LEG = ('UpLeg', 'Leg', 'Foot', 'ToeBase')
# Omuz eklemi çevresi (bağ pozunda eklem merkezine bu mesafeden yakın kol köşeleri) deri
# deformasyonunun doğal örtüşme bölgesidir; içe girme ölçümüne katılmaz.
SHOULDER_ZONE = 0.09
HIP_ZONE = 0.07
LIMIT = 0.03
# Taşıma döngülerinin oyundaki hedef hızı (m/sn): oynatma 1.0'da ayak, yerdeyken adım başına bundan az kaymalı.
TARGET_SPEED = {'Carry_Stretcher_Walk': 1.4, 'Carry_OneHand_Walk': 2.4, 'Piggyback_Walk': 1.2,
                'Carry_Stretcher_Front_Walk': 1.4, 'Carry_Coil_Walk': 2.4,
                'Thief_Sneak_Walk':1.6, 'Thief_Run_Carry':3.6, 'Thief_Run':3.6}
FOOT_SLIDE_LIMIT = 0.03
CONTACT_EPS = 0.015     # ayak bileği en alçak yüksekliğine bu kadar yakınsa yerde sayılır (kareler arası
                        # doğrusal dönüş karışımı basışta bileği ~7 mm oynatır; salınım bu bantta çok kısa kalır)
SUBSTEPS = 4
FBX_TIME_MODE_30 = 6    # FBX GlobalSettings TimeMode: eFrames30


def fbx_frame_rate(path):
    """FBX GlobalSettings'ten (TimeMode, CustomFrameRate) okur."""
    from io_scene_fbx import parse_fbx
    root, _ = parse_fbx.parse(str(path))
    found = {}

    def walk(e):
        for c in e.elems:
            if c.id == b'GlobalSettings':
                for pp in c.elems:
                    if pp.id == b'Properties70':
                        for q in pp.elems:
                            if q.props[0] in (b'TimeMode', b'CustomFrameRate'):
                                found[q.props[0].decode()] = q.props[-1]
            walk(c)
    walk(root)
    return found.get('TimeMode'), found.get('CustomFrameRate')


def foot_slide(rig, f0, f1, speed):
    """Yerinde döngüyü hedef hızla ileri (-Y) taşınmış say; her yere basışta ayağın yatay kaymasının en büyüğü (m).
    Döngü periyodik kabul edilir: son kare ilk kareyle aynı olduğundan [f0, f1) örneklenir ve basış dilimleri sarılır."""
    scene = bpy.context.scene
    mw = rig.matrix_world
    samples = []
    for f in range(f0, f1):
        for k in range(SUBSTEPS):
            sub = k / SUBSTEPS
            scene.frame_set(f, subframe=sub)
            t = (f - f0 + sub) / FPS
            samples.append((t, {side: mw @ rig.pose.bones[PREFIX + side + 'Foot'].head for side in ('Left', 'Right')}))
    period = (f1 - f0) / FPS
    worst = {}
    for side in ('Left', 'Right'):
        zs = [s[1][side].z for s in samples]
        lo = min(zs)
        contact = [z <= lo + CONTACT_EPS for z in zs]
        n = len(samples)
        if all(contact):
            worst[side] = float('inf')
            continue
        start = contact.index(False)
        segs, cur = [], []
        for i in range(n + 1):
            j = (start + i) % n
            wrap = start + i >= n
            if contact[j] and i < n:
                t, pts = samples[j]
                p = pts[side]
                tt = t + (period if wrap else 0.0)
                cur.append((p.x, p.y - speed * tt))
            elif cur:
                segs.append(cur)
                cur = []
        w = 0.0
        for seg in segs:
            xs = [p[0] for p in seg]
            ys = [p[1] for p in seg]
            w = max(w, math.hypot(max(xs) - min(xs), max(ys) - min(ys)))
        worst[side] = (w, len(segs))
    return worst


def check_grip(rig, f0, f1, name, hg):
    """Avuçların her karede meta'daki sap noktasına uzaklığı (en kötü, m) ve meta–sedye uyumu."""
    if not hg:
        return ['%s: handle_grip meta yok' % name], (0.0, '')
    errs = []
    if hg.get('stretcher_marker') != GRIP_CLIPS[name]:
        errs.append('%s: handle_grip isaretci %s (beklenen %s)' % (name, hg.get('stretcher_marker'), GRIP_CLIPS[name]))
    sm = json.loads(STRETCHER_META.read_text(encoding='utf-8'))
    spacing = sm['dimensions']['handle_spacing_m']
    target = {s: Vector(hg['grip_root_local_m'][s]) for s in ('Left', 'Right')}
    L, R = target['Left'], target['Right']
    if name in GRIP_STRICT and (abs((L.x - R.x) - spacing) > GRIP_SPACING_TOL or abs(L.x + R.x) > GRIP_SPACING_TOL
            or abs(L.y - R.y) > GRIP_SPACING_TOL or abs(L.z - R.z) > GRIP_SPACING_TOL):
        errs.append('%s: sap noktalari sedyeyle uyumsuz L=%s R=%s aralik %.3f' % (name, tuple(L), tuple(R), spacing))
    scene = bpy.context.scene
    mw = rig.matrix_world
    worst, wf = 0.0, f0
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        for side in ('Left', 'Right'):
            pb = rig.pose.bones[PREFIX + side + 'Hand']
            p = mw @ pb.head + (mw.to_3x3() @ pb.matrix.col[1].xyz).normalized() * GRIP_POINT
            d = (p - target[side]).length
            if d > worst:
                worst, wf = d, f
    # Carry_Stretcher_Walk (r2) değiştirilmedi: onun avuç sapması yalnız raporlanır, Unity IK'sı kapatır.
    if worst > GRIP_LIMIT and name in GRIP_STRICT:
        errs.append('%s: el-sap uzakligi %.3f m (f%d)' % (name, worst, wf))
    return errs, (worst, ' grip=%.4fm(f%d) span=%.3f/%.3f' % (worst, wf, L.x - R.x, spacing))


def short(name):
    return name[len(PREFIX):] if name.startswith(PREFIX) else name


def action_fcurves(action):
    out = []
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                out.extend(bag.fcurves)
    return out


def import_fbx(path, anim):
    before = set(bpy.data.objects)
    acts = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=anim)
    objs = [o for o in bpy.data.objects if o not in before]
    rig = next(o for o in objs if o.type == 'ARMATURE')
    new_actions = [a for a in bpy.data.actions if a not in acts]
    return rig, objs, new_actions


def remove(objs, actions):
    for o in objs:
        data = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if data is not None and data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Armature):
                bpy.data.armatures.remove(data)
    for a in actions:
        bpy.data.actions.remove(a)


class Character:
    """Mesh'li karakter: köşe sınıfları (dominant ağırlık) ve dışlama bölgeleri."""

    def __init__(self, path):
        self.rig, self.objs, acts = import_fbx(path, False)
        remove([], acts)
        self.rig.animation_data_clear()
        self.meshes = [o for o in self.objs if o.type == 'MESH']
        bones = self.rig.data.bones
        self.cls = {}
        for o in self.meshes:
            names = {g.index: short(g.name) for g in o.vertex_groups}
            labels = []
            for v in o.data.vertices:
                ws = {names[g.group]: g.weight for g in v.groups if g.weight > 0}
                if not ws:
                    labels.append(None)
                    continue
                dom = max(sorted(ws), key=lambda k: ws[k])
                lab = None
                co = o.matrix_world @ v.co
                for side in ('Left', 'Right'):
                    if dom in tuple(side + a for a in ARM):
                        blend = any(k in TORSO or k.endswith('Shoulder') for k in ws)
                        near = (co - bones[PREFIX + side + 'Arm'].head_local).length < SHOULDER_ZONE
                        lab = None if (blend or near) else 'arm' + side
                    elif dom in tuple(side + a for a in LEG):
                        other = 'Right' if side == 'Left' else 'Left'
                        blend = any(k in TORSO or k.startswith(other) for k in ws)
                        near = (co - bones[PREFIX + side + 'UpLeg'].head_local).length < HIP_ZONE
                        lab = ('legP' if (blend or near) else 'leg') + side
                if dom in TORSO:
                    lab = 'torso'
                if dom == 'Head':
                    lab = 'head'
                labels.append(lab)
            self.cls[o.name] = labels

    def assign(self, action):
        ad = self.rig.animation_data_create()
        ad.action = action
        if ad.action_slot is None and len(action.slots):
            ad.action_slot = action.slots[0]

    def penetration(self, coil_points=(), surface_check=False, head_clearance=False, coil_legs=False):
        dg = bpy.context.evaluated_depsgraph_get()
        self.ground_min = float('inf')
        verts = {}
        faces = {'torso': ([], []), 'head': ([], []), 'legLeft': ([], []), 'legRight': ([], [])}
        for o in self.meshes:
            ev = o.evaluated_get(dg)
            me = ev.to_mesh()
            co = [ev.matrix_world @ v.co for v in me.vertices]
            self.ground_min = min(self.ground_min, min((c.z for c in co), default=float('inf')))
            lab = self.cls[o.name]
            for i, c in enumerate(co):
                if lab[i]:
                    verts.setdefault(lab[i], []).append(c)
            for p in me.polygons:
                ls = {lab[i] for i in p.vertices}
                for key, want in (('torso', {'torso'}), ('head', {'head'}), ('legLeft', {'legLeft', 'legPLeft'}), ('legRight', {'legRight', 'legPRight'})):
                    if ls <= want:
                        vl, fl = faces[key]
                        base = len(vl)
                        vl.extend(co[i] for i in p.vertices)
                        fl.append(list(range(base, base + len(p.vertices))))
            ev.to_mesh_clear()
        trees = {k: BVHTree.FromPolygons(v, f) for k, (v, f) in faces.items() if f}

        pose = self.rig.pose.bones
        mw = self.rig.matrix_world

        def chain(names, tail=False):
            pts = [mw @ pose[PREFIX + n].head for n in names]
            if tail:
                pts.append(mw @ pose[PREFIX + names[-1]].tail)
            return pts
        axes = {'torso': chain(['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head']),
                'legLeft': chain(['LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase'], True),
                'legRight': chain(['RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase'], True)}

        def axis_point(p, pts):
            best, bd = None, 1e9
            for a, b in zip(pts, pts[1:]):
                ab = b - a
                u = 0.0 if ab.length_squared < 1e-12 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
                q = a + ab * u
                d = (p - q).length
                if d < bd:
                    best, bd = q, d
            return best

        def depth(points, key, surface=False):
            # Yüzey normali yerine kemik ekseninden dışa doğru yön kullanılır (mesh yönelimine bağımsız):
            # köşe, en yakın yüzey noktasına göre eksene daha yakınsa içeridedir.
            tree, pts = trees[key], axes[key]
            worst = 0.0
            for c in points:
                hit = tree.find_nearest(c, 0.25)
                if hit[0] is None:
                    continue
                loc, _, _, dist = hit
                out = loc - axis_point(loc, pts)
                if out.length < 1e-6:
                    continue
                # Yeni kangal pozlarında açık yaka/etek sınırına en yakın dış noktalar radyal ölçütte
                # yanlış içerde sayılabilir. Pair kontrolündeki yüz normali + çıkış ışını bunu ayırır.
                # Eski 25 klibin kol/bacak ölçüm yolu aynen korunur.
                if surface and ((c-loc).dot(hit[1]) >= 0 or tree.ray_cast(c,hit[1],dist*2+.005)[0] is None):
                    continue
                if (c - loc).dot(out.normalized()) < 0:
                    worst = max(worst, dist)
            return worst
        arm = max(depth(verts.get('arm' + s, []), 'torso', surface_check) for s in ('Left', 'Right'))
        leg = max(depth(verts.get('legLeft', []), 'legRight'), depth(verts.get('legRight', []), 'legLeft'))
        self.coil_depth = depth(coil_points, 'torso', True) if coil_points else 0.0
        self.coil_leg_depth = max(depth(coil_points,k,True) for k in ('legLeft','legRight')) if coil_points and coil_legs else 0.0
        # Üçgen yüzeyleri 1.5 cm adımla örneklenir; bu yarıçap çıkarılarak alt mesafe sınırı raporlanır.
        self.coil_head_clearance = float('inf')
        if coil_points and head_clearance:
            head_axis = chain(['Head'],True)
            for p in coil_points:
                loc, normal, _, distance = trees['head'].find_nearest(p)
                # Karakter kafasının bazı yüzleri ters winding taşır; gövde kontrolündeki gibi
                # kemiğe göre dış yön kullanılır, ters yüz normaline göre dışarıdaki prop içerde sayılmaz.
                outward = loc-axis_point(loc,head_axis)
                inside = (p-loc).dot(outward)<0
                clearance = -distance if inside else distance-.015
                if clearance<self.coil_head_clearance:
                    self.coil_head_clearance = clearance
                    self.coil_head_witness = (tuple(p),tuple(loc),inside)
        return arm, leg


def bone_rot_diff(rig, f0, f1):
    scene = bpy.context.scene
    scene.frame_set(f0)
    a = {pb.name: pb.matrix.to_quaternion() for pb in rig.pose.bones}
    scene.frame_set(f1)
    worst = 0.0
    for pb in rig.pose.bones:
        q = pb.matrix.to_quaternion()
        worst = max(worst, math.degrees(a[pb.name].rotation_difference(q).angle))
    return worst


_FC_RE = re.compile(r'pose\.bones\["(.+)"\]\.(\w+)')
CHAINS = (['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head', '+Head'],
          ['LeftShoulder', 'LeftArm', 'LeftForeArm', 'LeftHand', '+LeftHand'],
          ['RightShoulder', 'RightArm', 'RightForeArm', 'RightHand', '+RightHand'],
          ['LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase', '+LeftToeBase'],
          ['RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase', '+RightToeBase'])


def pose_from_action(rig, action, frame):
    """Aksiyonu atamadan pozu uygular (iki rig farklı kliplerin farklı karelerinde)."""
    for fc in action_fcurves(action):
        m = _FC_RE.fullmatch(fc.data_path)
        if m:
            getattr(rig.pose.bones[m.group(1)], m.group(2))[fc.array_index] = fc.evaluate(frame)


def shell(ch):
    """Değerlendirilmiş mesh: tüm köşeler, yüz BVH'si ve iskelet eksen parçaları (dünya)."""
    dg = bpy.context.evaluated_depsgraph_get()
    verts, polys, owner = [], [], []
    for o in ch.meshes:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        base = len(verts)
        verts.extend(ev.matrix_world @ v.co for v in me.vertices)
        owner.extend([o.name.split('_')[-1]] * len(me.vertices))
        polys.extend([base + i for i in p.vertices] for p in me.polygons)
        ev.to_mesh_clear()
    mw = ch.rig.matrix_world
    pose = ch.rig.pose.bones
    segs = []
    for chain in CHAINS:
        pts = [mw @ (pose[PREFIX + n[1:]].tail if n.startswith('+') else pose[PREFIX + n].head) for n in chain]
        segs.extend((p, q, n.lstrip('+')) for p, q, n in zip(pts, pts[1:], chain))
    return (verts, owner), BVHTree.FromPolygons(verts, polys), segs


def axis_point(p, segs):
    best, bd = None, 1e9
    for a, b, n in segs:
        ab = b - a
        u = 0.0 if ab.length_squared < 1e-12 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
        q = a + ab * u
        d = (p - q).length_squared
        if d < bd:
            best, bd = (q, n), d
    return best


def cross_penetration(a, b):
    """İki karakterin birbirine en derin girişi (m): köşe, öbürünün en yakın yüzeyine göre hem yüz normali hem
    iskelet ekseninden dışa yön içeride diyorsa ve normal yönündeki ışın yüzeyden çıkıyorsa içeridedir."""
    sa, sb = shell(a), shell(b)
    worst = 0.0
    where = ''
    for ((verts, owner), _, _), (_, tree, segs), who in ((sa, sb, 'tasiyan'), (sb, sa, 'binen')):
        for c, part in zip(verts, owner):
            hit = tree.find_nearest(c, 0.2)
            if hit[0] is None:
                continue
            loc, nrm, _, dist = hit
            if dist <= worst:
                continue
            q, bone = axis_point(loc, segs)
            out = loc - q
            # İçeride sayılması için: yüz normali ve iskelet ekseninden dışa yön ikisi de içeride desin (iri chibi
            # kafa/gövdede eksen ölçütü tek başına komşu uzva kayar) ve normal yönünde atılan ışın yüzeyden çıksın
            # (kask kenarı gibi ince kabukların altında kalan dış noktalar elenir).
            if (out.length > 1e-6 and (c - loc).dot(out) < 0 and (c - loc).dot(nrm) < 0
                    and tree.ray_cast(c, nrm, dist * 2 + 0.005)[0] is not None):
                worst, where = dist, '%s.%s->%s' % (who, part, bone)
    return worst, where


def place_rider(rider, carrier, base):
    pb = carrier.pose.bones[PREFIX + PIGGY_BONE]
    S = pb.matrix @ pb.bone.matrix_local.inverted() @ Matrix.Translation(PIGGY_OFFSET)
    mc = carrier.matrix_world
    rider.matrix_world = mc @ S @ mc.inverted() @ base


def load_coil():
    """FBX üçgenlerinin 1.5 cm yüzey örnekleri; köşeler arasındaki çakışmalar da yakalanır."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(ROOT/'Export/Props/RopeCoil/SM_RopeCoil.fbx'), use_anim=False)
    objs = [o for o in bpy.data.objects if o not in before]
    mesh = next(o for o in objs if o.type == 'MESH')
    mesh.data.calc_loop_triangles()
    points = []
    for tri in mesh.data.loop_triangles:
        a,b,c = [mesh.matrix_world @ mesh.data.vertices[i].co for i in tri.vertices]
        steps = max(1,math.ceil(max((a-b).length,(a-c).length,(b-c).length)/.015))
        points.extend(a+(b-a)*(i/steps)+(c-a)*(j/steps) for i in range(steps+1) for j in range(steps+1-i))
    grips = {o.name: o.matrix_world.translation.copy() for o in objs if o.type == 'EMPTY'}
    remove(objs, [])
    return points, grips


def load_stake_tree(position):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(ROOT/'Export/Props/RopeAnchor/SM_RopeAnchor_Bare.fbx'),use_anim=False)
    objects = [o for o in bpy.data.objects if o not in before]
    vertices, faces = [], []
    for obj in objects:
        if obj.type!='MESH':
            continue
        base = len(vertices)
        vertices.extend(Vector(position)+obj.matrix_world@v.co for v in obj.data.vertices)
        faces.extend([base+i for i in p.vertices] for p in obj.data.polygons)
    tree = BVHTree.FromPolygons(vertices,faces)
    remove(objects,[])
    return tree


def surface_depth(points,tree):
    worst = 0.0
    for p in points:
        loc,normal,_,distance = tree.find_nearest(p,.15)
        if loc is not None and (p-loc).dot(normal)<0 and tree.ray_cast(p,normal,distance*2+.005)[0] is not None:
            worst = max(worst,distance)
    return worst


def coil_pose(rig, name, build_frame, m):
    cg = m['coil_grip']
    hands = {s: rig.matrix_world @ (rig.pose.bones[cg['bones'][s]].matrix @ Vector(cg['palm_offset_hand_local_m'])) for s in ('Left','Right')}
    center = (hands['Left']+hands['Right'])/2
    x = (hands['Left']-hands['Right']).normalized()
    z = rig.matrix_world.to_3x3() @ Vector((0,0,1))
    z = (z-x*x.dot(z)).normalized()
    rot = Matrix((x,z.cross(x).normalized(),z)).transposed().to_4x4()
    if name == 'Cabinet_TakeCoil' and build_frame < m['grab_frame']:
        center, rot = rig.matrix_world @ Vector(m['cabinet_coil_root_local_m']), rig.matrix_world.to_3x3().to_4x4()
    if name == 'Rope_TieStake' and m['coil_ground_frames'][0] <= build_frame <= m['coil_ground_frames'][1]:
        center = rig.matrix_world @ Vector(m['coil_ground_root_local_m'])
        rot = rig.matrix_world.to_3x3().to_4x4() @ Euler([math.radians(v) for v in m['coil_ground_rotation_euler_deg']]).to_matrix().to_4x4()
    return hands, Matrix.Translation(center) @ rot


def check_coil_meta(rig, name, m, n, errors):
    cg = m.get('coil_grip', {})
    if cg.get('bones') != {s:PREFIX+s+'Hand' for s in ('Left','Right')} or cg.get('palm_offset_hand_local_m') != [0,.05,0]:
        errors.append(name+': coil_grip kemik/avuc bilgisi hatali')
    if len(cg.get('samples', [])) != n:
        errors.append(name+': coil_grip kare kaydi eksik')
    if cg.get('grip_root_local_m') != cg['samples'][0]['hands_root_local_m']:
        errors.append(name+': coil_grip ilk kare konumu hatali')
    if not m.get('in_place') or m.get('root_motion') or m.get('duration_s') != round((n-1)/FPS,4):
        errors.append(name+': sure/in_place/root_motion meta hatali')
    for i, rec in enumerate(cg.get('samples', []), 1):
        attached = (i>=11 if name=='Cabinet_TakeCoil' else i<=16 or i>=46 if name=='Rope_TieStake' else i<=22 if name=='Rope_Throw' else True)
        if rec['frame'] != i or rec['attached'] != attached:
            errors.append(name+': bagli kare araligi hatali')
            break
    if name == 'Rope_Throw':
        f = m.get('release_frame',0)
        if f != 22 or not 1 < f < n:
            errors.append(name+': release_frame hatali')
        d = Vector(m.get('throw_direction_root_local',[0,0,0]))
        if abs(d.length-1)>.001 or d.y>=0 or d.z>=0:
            errors.append(name+': atis yonu ileri-asagi degil')
        hands = cg['samples'][f-1]['hands_root_local_m']
        prev = cg['samples'][f-2]['hands_root_local_m']
        center = (Vector(hands['Left'])+Vector(hands['Right']))/2
        velocity = (center-(Vector(prev['Left'])+Vector(prev['Right']))/2).normalized()
        if (center-Vector(m['release_position_root_local_m'])).length>.001 or velocity.dot(d)<.999:
            errors.append(name+': birakis merkezi / yonu avuc hareketine uymuyor')
    if name == 'Rope_TieStake' and m.get('stake_hand_frames') != [21,40]:
        errors.append(name+': stake_hand_frames hatali')


def check_strip(name, errors):
    path = OUT/'renders'/(name+'_strip.png')
    if not path.exists():
        errors.append(name+': serit eksik')
        return
    img = bpy.data.images.load(str(path))
    image_width, image_height = img.size
    width, count = 360, 10 if name=='Rope_Throw' else 8
    if name in COMBAT_EXPECTED:
        width = 420 if name.startswith('Tackle_') else 360
        count = 8 if round(COMBAT_EXPECTED[name][0]*FPS)+1 >= 40 else 7
        if name=='Thief_Punch':
            count=8
    if img.size[0] != width*count+4*(count-1):
        errors.append(name+': serit boyutu hatali')
    else:
        for i in range(count):
            x0 = i*(width+4)
            # Sadece dört köşe okunur: tüm şeridi Python float demetine kopyalamak yüzlerce MB tüketir.
            corners = [(y*image_width+x)*4 for x in (x0,x0+width-1) for y in (0,image_height-1)]
            bright = max(sum(img.pixels[k:k+3])/3*255 for k in corners)
            if bright < 150:
                errors.append('%s: karanlik kare %d %.1f' % (name,i,bright))
    bpy.data.images.remove(img)


def check_combat_meta(name, m, n, errors):
    duration, loop = COMBAT_EXPECTED[name]
    character = 'thief' if name.startswith('Thief_') else 'crew'
    if (m.get('character') != character or m.get('preview_mesh') != 'SK_RescueCrew_Blue'
            or m.get('duration_s') != duration or not m.get('in_place') or m.get('root_motion')):
        errors.append(name+': karakter/sure/yerinde/mesh meta hatali')
    events = {'Push_Shove':{'hit_frame':9}, 'Tackle_Dive':{'hit_frame':12,'ground_frame':22},
        'Throw_Release':{'release_frame':8}, 'Whistle_Blow':{'blow_frames':[15,27]},
        'Thief_Grab':{'grab_frame':11}, 'Thief_Punch':{'hit_frame':14},
        'Thief_ThrowInVan':{'release_frame':14}}
    for key, value in events.get(name,{}).items():
        if m.get(key) != value or any(not 1 <= f <= n for f in (value if isinstance(value,list) else [value])):
            errors.append(name+': '+key+' hatali')
    if name == 'Throw_Aim':
        grip = m.get('hold_grip',{})
        if grip.get('bone') != PREFIX+'RightHand' or grip.get('palm_offset_hand_local_m') != [0,.05,0]:
            errors.append(name+': hold_grip hatali')
    if name == 'Thief_Run_Carry':
        grip = m.get('loot_grip',{})
        if (grip.get('bones') != {s:PREFIX+s+'Hand' for s in ('Left','Right')}
                or grip.get('palm_offset_hand_local_m') != [0,.05,0]):
            errors.append(name+': loot_grip hatali')


def check_transitions(errors, only):
    for left,right,limit in TRANSITIONS:
        if only and left not in only and right not in only:
            continue
        ends=[]
        for name,end in ((left,True),(right,False)):
            rig,objs,acts=import_fbx(OUT/(name+'.fbx'),True)
            bpy.context.scene.render.fps=FPS
            frame=acts[0].frame_range[1 if end else 0]
            bpy.context.scene.frame_set(round(frame))
            ends.append({b.name:b.matrix.copy() for b in rig.pose.bones})
            remove(objs,acts)
        angle=max(math.degrees(a.to_quaternion().rotation_difference(ends[1][n].to_quaternion()).angle)
                  for n,a in ends[0].items())
        distance=max((a.translation-ends[1][n].translation).length for n,a in ends[0].items())
        print('TRANSITION %s -> %s rotation=%.5fdeg position=%.6fm' % (left,right,angle,distance),flush=True)
        if angle>limit or distance>.005:
            errors.append('%s -> %s: gecis %.3f derece %.4fm' % (left,right,angle,distance))


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    only = argv[argv.index('--only') + 1].split(',') if '--only' in argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = FPS
    errors = []
    meta = json.loads((OUT / 'clips_meta.json').read_text(encoding='utf-8'))
    if meta.get('fps') != FPS:
        errors.append('meta kare hizi 30 degil')
    source = ROOT/'tools/blender/build_procedural_anims.py'
    if meta.get('source_sha256') != hashlib.sha256(source.read_bytes().replace(b'\r\n',b'\n')).hexdigest():
        errors.append('kaynak hash guncel degil; tam build gerekli')
    files = sorted(p.stem for p in OUT.glob('*.fbx'))
    if sorted(files) != sorted(EXPECTED):
        errors.append('dosya listesi: %s' % files)
    crew = Character(CREW)
    patient = Character(PATIENT)
    ref_bones = sorted(b.name for b in crew.rig.data.bones)
    if len(ref_bones)!=31 or meta.get('bone_count')!=31:
        errors.append('31 kemikli iskelet/meta bekleniyor')
    coil_points, coil_grips = load_coil()
    stake_tree = load_stake_tree(meta['clips']['Rope_TieStake']['stake_root_local_m'])
    coil_span = (coil_grips['Grip_L']-coil_grips['Grip_R']).length
    for c, rest in ((crew, None), (patient, None)):
        c.rig.animation_data_clear()
        scene.frame_set(1)
    base = {'crew': crew.penetration(), 'patient': patient.penetration()}
    print('BASELINE bind-pose penetration (arm, leg) m:', {k: tuple(round(x, 4) for x in v) for k, v in base.items()}, flush=True)
    worst_all = {'arm': (0, ''), 'leg': (0, ''), 'loop': (0, ''), 'slide': (0, ''), 'foot_slide': (0, ''), 'pair': (0, ''), 'grip': (0, ''), 'coil': (0,''), 'coil_span_error':(0,'')}
    civilian = rider = None
    rider_f0 = rider_len = 0
    hashes = []
    for name in sorted(EXPECTED):
        if only and name not in only:
            continue
        duration, loop = EXPECTED[name]
        mode, rate = fbx_frame_rate(OUT / (name + '.fbx'))
        if mode != FBX_TIME_MODE_30 or rate is None or abs(rate - FPS) > 1e-3:
            errors.append('%s: FBX kare hizi TimeMode=%s CustomFrameRate=%s (beklenen 30)' % (name, mode, rate))
        rig, objs, acts = import_fbx(OUT / (name + '.fbx'), True)
        scene.render.fps = FPS
        try:
            if len(acts) != 1:
                errors.append('%s: aksiyon sayisi %d' % (name, len(acts)))
            act = acts[0]
            bones = sorted(b.name for b in rig.data.bones)
            if bones != ref_bones:
                errors.append('%s: kemik adlari farkli' % name)
            for b in rig.data.bones:
                rb = crew.rig.data.bones[b.name]
                if (b.head_local - rb.head_local).length > 1e-4:
                    errors.append('%s: bag pozu farkli %s' % (name, b.name))
                    break
            f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
            n = f1 - f0 + 1
            want = int(round(duration * FPS)) + 1
            if abs(n - want) > 2:
                errors.append('%s: kare sayisi %d (beklenen %d)' % (name, n, want))
            if name in COIL_CLIPS | COMBAT_EXPECTED.keys() and n != want:
                errors.append(name+': yeni klip kare sayisi tam eslesmiyor')
            if n < 2:
                errors.append('%s: tek kare' % name)
            data = []
            for fc in sorted(action_fcurves(act), key=lambda c: (c.data_path, c.array_index)):
                data.append([fc.data_path, fc.array_index, [[round(k.co[0], 2), round(k.co[1], 4)] for k in fc.keyframe_points]])
            h = hashlib.sha256(json.dumps(data, separators=(',', ':')).encode()).hexdigest()
            hashes.append(h)
            ad = rig.animation_data
            if ad.action_slot is None and len(act.slots):
                ad.action_slot = act.slots[0]
            loopdiff = bone_rot_diff(rig, f0, f1) if loop else 0.0
            if loop and loopdiff >= 0.5:
                errors.append('%s: dongu farki %.3f derece' % (name, loopdiff))
            hips = []
            for f in range(f0, f1 + 1):
                scene.frame_set(f)
                hips.append(rig.matrix_world @ rig.pose.bones[PREFIX + 'Hips'].head)
            slide = max(math.hypot(p.x - hips[0].x, p.y - hips[0].y) for p in hips)
            m = meta['clips'].get(name, {})
            if name in COMBAT_EXPECTED:
                check_combat_meta(name,m,n,errors)
                check_strip(name,errors)
                if slide > .0001:
                    errors.append(name+': yeni klip yatay kalca hareketi sifir degil')
                if name == 'Tackle_Dive':
                    # Son poz gerçekten yatay ve yere yakın olmalı; yalnız olay meta'sı yeterli değil.
                    scene.frame_set(f1)
                    hip = rig.pose.bones[PREFIX+'Hips'].head
                    neck = rig.pose.bones[PREFIX+'Neck'].head
                    axis = (neck-hip).normalized()
                    if abs(axis.z)>.15 or not .15 <= hip.z <= .25 or axis.y>-.8:
                        errors.append(name+': bitis yuzustu yere inmis degil')
            if name in COIL_CLIPS:
                check_coil_meta(rig,name,m,n,errors)
                check_strip(name,errors)
            if name in COIL_CLIPS | COMBAT_EXPECTED.keys():
                if loop:
                    scene.frame_set(f0)
                    first = {b.name:b.matrix.copy() for b in rig.pose.bones}
                    scene.frame_set(f1)
                    if max(abs(b.matrix[i][j]-first[b.name][i][j]) for b in rig.pose.bones for i in range(4) for j in range(4)) > .00001:
                        errors.append(name+': dongu matrisleri esit degil')
            if m.get('in_place') and slide >= 0.02:
                errors.append('%s: kalca kaymasi %.3f m' % (name, slide))
            if m.get('frames') != n or m.get('loop') != loop:
                errors.append('%s: meta uyumsuz' % name)
            fslide = ''
            if name in TARGET_SPEED:
                v = TARGET_SPEED[name]
                if abs(m.get('target_speed_mps', -1) - v) > 1e-6:
                    errors.append('%s: meta hedef hiz %s (beklenen %s)' % (name, m.get('target_speed_mps'), v))
                fs = foot_slide(rig, f0, f1, v)
                parts = []
                for side, val in fs.items():
                    if val == float('inf'):
                        errors.append('%s: %s ayak hic kalkmiyor' % (name, side))
                        continue
                    w, nseg = val
                    parts.append('%s=%.4fm/%d' % (side[0], w, nseg))
                    if nseg != 1:
                        errors.append('%s: %s ayak dongude %d kez basiyor (beklenen 1)' % (name, side, nseg))
                    if w >= FOOT_SLIDE_LIMIT:
                        errors.append('%s: ayak kaymasi %s %.3f m/adim @ %.1f m/sn' % (name, side, w, v))
                    if w > worst_all['foot_slide'][0]:
                        worst_all['foot_slide'] = (w, name)
                fslide = ' footslide@%.1fm/s %s' % (v, ' '.join(parts))
            if name in GRIP_CLIPS:
                gerr, gtxt = check_grip(rig, f0, f1, name, m.get('handle_grip'))
                errors.extend(gerr)
                if gtxt[0] > worst_all['grip'][0]:
                    worst_all['grip'] = (gtxt[0], name)
                fslide += gtxt[1]
            if name == 'Patient_SitDazed_ArmSling':
                if civilian is None:
                    civilian = Character(CIVILIAN)
                ch = civilian
            else:
                ch = patient if name.startswith(PATIENT_CLIPS) else crew
            ch.assign(act)
            wa = wl = 0.0
            ground_min = float('inf')
            wc = ws = wm = wt = 0.0
            coil_leg = coil_stake = 0.0
            head_clearance = float('inf')
            wfa = wfl = f0
            for f in range(f0, f1 + 1):
                scene.frame_set(f)
                cp = []
                if name in COIL_CLIPS:
                    bf = f-f0+1
                    hands, transform = coil_pose(ch.rig,name,bf,m)
                    rec = m['coil_grip']['samples'][bf-1]
                    for side,p in hands.items():
                        wm = max(wm,(p-ch.rig.matrix_world @ Vector(rec['hands_root_local_m'][side])).length)
                    if rec['attached']:
                        ws = max(ws,abs((hands['Left']-hands['Right']).length-coil_span))
                        # İki gerçek Grip boşunun her iki avuca da oturduğunu ölç.
                        for side,marker in (('Left','Grip_L'),('Right','Grip_R')):
                            ws = max(ws, (hands[side]-transform @ coil_grips[marker]).length)
                    if name!='Rope_Throw' or bf<=m['release_frame']:
                        cp = [transform @ p for p in coil_points]
                    if name=='Rope_TieStake' and m['stake_hand_frames'][0]<=bf<=m['stake_hand_frames'][1]:
                        top = Vector(m['stake_root_local_m'])+Vector((0,0,.30))
                        wt = max(wt,*[(p-top).length for p in hands.values()])
                    if name=='Rope_Throw' and bf==m['release_frame']:
                        for side,p in hands.items():
                            wm=max(wm,(p-Vector(m['release_hand_root_local_m'][side])).length)
                a, l = ch.penetration(cp, name in COIL_CLIPS | COMBAT_EXPECTED.keys(),name=='Cabinet_TakeCoil',name=='Rope_TieStake')
                ground_min = min(ground_min,ch.ground_min)
                wc = max(wc,ch.coil_depth)
                if name=='Cabinet_TakeCoil':
                    if ch.coil_head_clearance<head_clearance:
                        head_clearance = ch.coil_head_clearance
                        head_witness = (bf,ch.coil_head_witness)
                if name=='Rope_TieStake':
                    coil_leg = max(coil_leg,ch.coil_leg_depth)
                    coil_stake = max(coil_stake,surface_depth(cp,stake_tree))
                if a > wa:
                    wa, wfa = a, f
                if l > wl:
                    wl, wfl = l, f
            ch.rig.animation_data.action = None
            if name in ('Tackle_Dive','Tackle_GetUp'):
                print('GROUND %s min_z=%.5fm' % (name,ground_min),flush=True)
                if ground_min < -.02:
                    errors.append(name+': tackle mesh zeminin 2 cm altina giriyor')
            if name in COIL_CLIPS:
                for key,val in (('coil',wc),('coil_span_error',ws)):
                    if val>worst_all[key][0]:
                        worst_all[key]=(val,name)
                if wc > .03 or ws > .03 or wm > .003 or wt > .09:
                    errors.append('%s: kangal %.4f m, el araligi hatasi %.4f m, meta %.4f m, kazik erisim %.4f m' % (name,wc,ws,wm,wt))
                print('COIL %s penetration=%.4fm grip_span_error=%.4fm metadata_error=%.5fm stake_reach=%.4fm' % (name,wc,ws,wm,wt),flush=True)
                if name=='Cabinet_TakeCoil':
                    print('COIL_HEAD conservative_clearance=%.5fm' % head_clearance,flush=True)
                    print('COIL_HEAD closest_frame_point_surface_inside=',head_witness,flush=True)
                    if head_clearance<.03:
                        errors.append('Cabinet_TakeCoil: kafa boslugu 3 cm altinda: %.5fm' % head_clearance)
                if name=='Rope_TieStake':
                    print('COIL_TIE leg_penetration=%.5fm stake_penetration=%.5fm' % (coil_leg,coil_stake),flush=True)
                    if coil_leg>.03 or coil_stake>.03:
                        errors.append('Rope_TieStake: kangal bacak/kazik ic ice 3 cm ustunde: %.5f / %.5fm' % (coil_leg,coil_stake))
            pair = ''
            if name.startswith('Piggyback_') and name != 'Piggyback_Rider':
                # Taşıyan + binen birlikte: binen Piggyback_Rider'ı kendi döngüsünde oynatır, Spine1 soketine bağlı.
                if rider is None:
                    rider = import_fbx(OUT / 'Piggyback_Rider.fbx', True)
                    rider_f0 = int(round(rider[2][0].frame_range[0]))
                    rider_len = int(round(rider[2][0].frame_range[1])) - rider_f0
                lo, hi = PIGGY_ATTACH.get(name, (1, None))
                lo = f0 + lo - 1  # build kareleri 1 tabanlı; içe aktarılan aksiyon f0dan başlar
                hi = f1 if hi is None else f0 + hi - 1
                ch.assign(act)
                patient.rig.animation_data_create().action = None
                base = patient.rig.matrix_world.copy()
                wp, wfp, wwhere = 0.0, lo, ''
                for f in range(lo, hi + 1):
                    scene.frame_set(f)
                    pose_from_action(patient.rig, rider[2][0], rider_f0 + (f - f0) % rider_len)
                    bpy.context.view_layer.update()
                    place_rider(patient.rig, crew.rig, base)
                    bpy.context.view_layer.update()
                    d, where = cross_penetration(crew, patient)
                    if d > wp:
                        wp, wfp, wwhere = d, f, where
                patient.rig.matrix_world = base
                for pb in patient.rig.pose.bones:
                    pb.location = (0, 0, 0)
                    pb.rotation_quaternion = (1, 0, 0, 0)
                    pb.scale = (1, 1, 1)
                bpy.context.view_layer.update()
                ch.rig.animation_data.action = None
                if wp > worst_all['pair'][0]:
                    worst_all['pair'] = (wp, name)
                if wp > PAIR_LIMIT:
                    errors.append('%s: tasiyan-binen ic ice %.3f m (f%d)' % (name, wp, wfp))
                pair = ' pair=%.4fm(f%d %s) frames=%d..%d' % (wp, wfp, wwhere, lo, hi)
            for key, val in (('arm', wa), ('leg', wl), ('loop', loopdiff), ('slide', slide)):
                if val > worst_all[key][0]:
                    worst_all[key] = (val, name)
            if wa > LIMIT or wl > LIMIT:
                errors.append('%s: ice girme kol %.3f (f%d) bacak %.3f (f%d)' % (name, wa, wfa, wl, wfl))
            print('CLIP %-26s fps=%s frames=%d loopdiff=%.3fdeg hipslide=%.4fm arm=%.4fm(f%d) leg=%.4fm(f%d) hips_z=%.3f..%.3f%s%s' % (
                name, rate, n, loopdiff, slide, wa, wfa, wl, wfl, min(p.z for p in hips), max(p.z for p in hips), fslide, pair), flush=True)
        finally:
            remove(objs, acts)
    if rider is not None:
        remove(rider[1], rider[2])
    check_transitions(errors,only)
    print('WORST', {k: (round(v[0], 4), v[1]) for k, v in worst_all.items()}, flush=True)
    print('FBX_CURVES_SHA256', hashlib.sha256(''.join(hashes).encode()).hexdigest(), flush=True)
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
