"""Service module art for the office/garage module system.

Five portable service devices (towing winch, water pump, fire hose reel, technical
service unit, transit case) plus the shared skid and the service port. Mid-poly,
plain colour materials, no text or decals.

Blender axes: X = Unity X, Y = Unity Z (module front: label rail and service port),
Z = up. The FBX settings are the ones the existing Blender module pipeline uses, so
BlenderModuleBuilder.ConfigureDevice maps the meshes unchanged.

Every device is written three times (LOD0/1/2) by the same functions; the global LOD
only changes segment counts, chamfers and which small parts are kept.

Run:  blender -b --factory-startup -P gen_service_modules.py -- [--no-export]
Env:  SM_DELIVERY=<folder>  also writes GLBs and the .blend copy there.
"""
import bpy, bmesh, math, json, os, sys, time
from pathlib import Path
from mathutils import Vector, Matrix, Euler

T0 = time.time()
ARGS = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
NO_EXPORT = '--no-export' in ARGS
REPO = Path(__file__).resolve().parents[3]
BLEND = REPO/'Art/Blender/Models/ServiceModules/service_modules.blend'
ART = REPO/'Assets/_Project/Art/BlenderModules'
FBX_DIR = ART/'Models'
MANIFEST = ART/'module_manifest.json'
PALETTE_JSON = ART/'service_module_palette.json'
DELIVERY = Path(os.environ['SM_DELIVERY']) if os.environ.get('SM_DELIVERY') else None
IDS = ('towing', 'water', 'fire', 'tech', 'cargo')

# Device envelope (existing collision/ConfigureDevice bounds) and the service port
# keep-out around the skid's socket housing, both in Blender axes.
ENV_MIN, ENV_MAX = Vector((-.321, -.221, .0728)), Vector((.321, .230, .440))
LIFT = .0015   # devices sit 1.5 mm above the deck: Unity's float bounds test rejects exactly .072
KEEP_MIN, KEEP_MAX = Vector((-.335, .125, .07)), Vector((-.262, .235, .32))
PORT_AT = Vector((-.29, .20, .27))          # Unity (-.29, .27, .20)
CABLE_END = (-.263, .158, .272)             # inner side face of the socket housing

# ------------------------------------------------------------------ scene reset
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'

# ------------------------------------------------------------------ materials
# sRGB colour, metallic, roughness. Unity materials are created from the same table
# (smoothness = 1 - roughness), so Blender previews and the game match.
PALETTE = {
    'SM_Frame':     ((.205, .222, .232), .35, .50),   # powder-coated anthracite
    'SM_Dark':      ((.088, .093, .097), .20, .60),   # motor bells, housings, panels
    'SM_Rubber':    ((.058, .060, .062), .00, .88),
    'SM_Steel':     ((.600, .615, .615), .85, .36),
    'SM_Rope':      ((.520, .530, .525), .85, .42),
    'SM_Alu':       ((.700, .705, .690), .80, .42),
    'SM_TowYellow': ((.800, .600, .205), .10, .50),
    'SM_PumpBlue':  ((.180, .360, .470), .10, .46),
    'SM_FireRed':   ((.600, .125, .090), .10, .45),
    'SM_Ivory':     ((.800, .760, .660), .05, .55),
    'SM_Glass':     ((.040, .050, .056), .00, .12),
    'SM_LampGreen': ((.300, .620, .360), .00, .30),
    'SM_LampAmber': ((.860, .560, .180), .00, .30),
}

def s2l(c):
    return c/12.92 if c <= .04045 else ((c+.055)/1.055)**2.4

MAT = {}
for name, (rgb, metallic, rough) in PALETTE.items():
    m = bpy.data.materials.new(name)
    lin = tuple(s2l(c) for c in rgb)
    m.diffuse_color = (*lin, 1)
    m.use_nodes = True
    bs = m.node_tree.nodes['Principled BSDF']
    bs.inputs['Base Color'].default_value = (*lin, 1)
    bs.inputs['Metallic'].default_value = metallic
    bs.inputs['Roughness'].default_value = rough
    MAT[name] = m
FRAME, DARK, RUB, STEEL, ROPE, ALU = (MAT[k] for k in ('SM_Frame', 'SM_Dark', 'SM_Rubber', 'SM_Steel', 'SM_Rope', 'SM_Alu'))
YEL, BLUE, RED, IVORY, GLASS = (MAT[k] for k in ('SM_TowYellow', 'SM_PumpBlue', 'SM_FireRed', 'SM_Ivory', 'SM_Glass'))
LGREEN, LAMBER = MAT['SM_LampGreen'], MAT['SM_LampAmber']

# ------------------------------------------------------------------ LOD helpers
LOD = 0
def S(n):
    """Segments for the current LOD."""
    return n if LOD == 0 else max(8, n//2) if LOD == 1 else max(6, n//4)

def bev(b):
    return b if LOD < 2 else 0

# ------------------------------------------------------------------ mesh buffer
class Part:
    def __init__(self):
        self.v, self.f, self.fm, self.mats, self.exempt = [], [], [], [], []
P = Part()

def add(verts, faces, mats, face_mats=None):
    base = len(P.v)
    P.v.extend(Vector(p) for p in verts)
    if not isinstance(mats, (list, tuple)): mats = [mats]
    idx = []
    for m in mats:
        if m not in P.mats: P.mats.append(m)
        idx.append(P.mats.index(m))
    for k, f in enumerate(faces):
        P.f.append([base+i for i in f])
        P.fm.append(idx[face_mats[k]] if face_mats else idx[0])

def add_bm(bm, mats, m=Matrix.Identity(4), face_mats=None):
    bm.verts.index_update()
    verts = [m @ v.co for v in bm.verts]
    faces = [[v.index for v in f.verts] for f in bm.faces]
    fms = face_mats if face_mats is not None else None
    bm.free()
    add(verts, faces, mats, fms)

def add_closed(verts, faces, mats, face_mats=None):
    bm = bmesh.new()
    vs = [bm.verts.new(Vector(v)) for v in verts]
    made = []
    for k, f in enumerate(faces):
        try:
            bm.faces.new([vs[i] for i in f]); made.append(k)
        except ValueError:
            pass
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    fm = [face_mats[k] for k in made] if face_mats else None
    add_bm(bm, mats, face_mats=fm)

def exempt(fn):
    """Geometry added inside fn may touch the socket housing (service cables)."""
    a = len(P.v); fn(); P.exempt.append((a, len(P.v)))

def T(loc=(0, 0, 0), rot=(0, 0, 0)):
    r = rot if isinstance(rot, Matrix) else Euler(rot).to_matrix()
    return Matrix.Translation(Vector(loc)) @ r.to_4x4()

AXIS = {'Z': Matrix.Identity(3), 'X': Matrix.Rotation(math.pi/2, 3, 'Y'), 'Y': Matrix.Rotation(-math.pi/2, 3, 'X')}
def basis(axis):
    if isinstance(axis, str): return AXIS[axis]
    a = Vector(axis).normalized()
    return a.to_track_quat('Z', 'Y' if abs(a.y) < .95 else 'X').to_matrix()

# ------------------------------------------------------------------ primitives
def box(c, s, mat, b=0, rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x*s[0], v.co.y*s[1], v.co.z*s[2]))
    b = bev(b)
    if b:
        bmesh.ops.bevel(bm, geom=bm.edges[:], offset=min(b, .45*min(s)), segments=1,
                        affect='EDGES', profile=.5, clamp_overlap=True)
    add_bm(bm, mat, T(c, rot))

def obox(c, u, v, s, mat, b=0):
    """Box with its local X along u and local Y along v (w = u x v)."""
    u = Vector(u).normalized(); v = (Vector(v)-Vector(v).project(u)).normalized(); w = u.cross(v)
    R = Matrix((u, v, w)).transposed()
    box(c, s, mat, b, R)

def beam(a, b2, w, h, mat, up=(0, 0, 1), b=0):
    a, b2 = Vector(a), Vector(b2); d = b2-a
    obox((a+b2)/2, d, Vector(up).cross(d).cross(d)*-1 if False else Vector(up).cross(d), (d.length, w, h), mat, b)

def lathe(c, axis, prof, mats, n=24, segm=None, cap0=True, cap1=True, closed=False):
    """Solid of revolution. prof = [(t along axis, radius)]; segm = material index per
    profile segment (and caps: segm[-1] is used for the end cap, segm[0] for the start)."""
    n = S(n); R = basis(axis); c = Vector(c)
    rings = []
    for t, r in prof:
        rings.append([c + R @ Vector((r*math.cos(2*math.pi*j/n), r*math.sin(2*math.pi*j/n), t)) for j in range(n)])
    verts = [p for ring in rings for p in ring]; faces = []; fm = []
    nseg = len(prof) if closed else len(prof)-1
    for i in range(nseg):
        i2 = (i+1) % len(prof)
        for j in range(n):
            faces.append([i*n+j, i*n+(j+1) % n, i2*n+(j+1) % n, i2*n+j])
            fm.append(segm[i] if segm else 0)
    if not closed:
        L = len(prof)-1
        if cap0 and prof[0][1] > 1e-6: faces.append(list(range(n))[::-1]); fm.append(segm[0] if segm else 0)
        if cap1 and prof[-1][1] > 1e-6: faces.append([L*n+j for j in range(n)]); fm.append(segm[-1] if segm else 0)
    add_closed(verts, faces, mats, fm)

def cyl(c, r, h, mat, axis='Z', n=16):
    lathe(c, axis, [(-h/2, r), (h/2, r)], mat, n)

def rod(a, b2, r, mat, n=12):
    a, b2 = Vector(a), Vector(b2)
    lathe((a+b2)/2, b2-a, [(-(b2-a).length/2, r), ((b2-a).length/2, r)], mat, n)

def tube(pts, r, mat, n=10, caps=True):
    """Circle swept along a polyline with parallel-transport frames."""
    pts = [Vector(p) for p in pts]; n = S(n)
    tan = []
    for i in range(len(pts)):
        if i == 0: t = pts[1]-pts[0]
        elif i == len(pts)-1: t = pts[-1]-pts[-2]
        else: t = (pts[i+1]-pts[i]).normalized()+(pts[i]-pts[i-1]).normalized()
        tan.append(t.normalized())
    ref = Vector((0, 0, 1)) if abs(tan[0].z) < .9 else Vector((1, 0, 0))
    N = tan[0].cross(ref).normalized(); rings = []
    for i, p in enumerate(pts):
        if i:
            ax = tan[i-1].cross(tan[i])
            if ax.length > 1e-9: N = Matrix.Rotation(tan[i-1].angle(tan[i]), 3, ax.normalized()) @ N
        B = tan[i].cross(N)
        rings.append([p+(N*math.cos(2*math.pi*j/n)+B*math.sin(2*math.pi*j/n))*r for j in range(n)])
    verts = [v for ring in rings for v in ring]; faces = []
    for i in range(len(rings)-1):
        for j in range(n): faces.append([i*n+j, i*n+(j+1) % n, (i+1)*n+(j+1) % n, (i+1)*n+j])
    if caps:
        faces.append(list(range(n))[::-1]); L = len(rings)-1; faces.append([L*n+j for j in range(n)])
    add_closed(verts, faces, mat)

def arc(c, r, a0, a1, steps, u=(0, 1, 0), v=(0, 0, 1)):
    c, u, v = Vector(c), Vector(u), Vector(v)
    return [c+(u*math.cos(math.radians(a0+(a1-a0)*k/steps))+v*math.sin(math.radians(a0+(a1-a0)*k/steps)))*r for k in range(steps+1)]

def P3(plane, p, a):
    return {'YZ': (a, p[0], p[1]), 'XZ': (p[0], a, p[1]), 'XY': (p[0], p[1], a)}[plane]

def prism(pts, plane, a0, a1, mat, b=0):
    bm = bmesh.new(); n = len(pts)
    v0 = [bm.verts.new(P3(plane, p, a0)) for p in pts]; v1 = [bm.verts.new(P3(plane, p, a1)) for p in pts]
    bm.faces.new(v0[::-1]); bm.faces.new(v1)
    for i in range(n): bm.faces.new([v0[i], v0[(i+1) % n], v1[(i+1) % n], v1[i]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    b = bev(b)
    if b:
        bmesh.ops.bevel(bm, geom=bm.edges[:], offset=b, segments=1, affect='EDGES', profile=.5, clamp_overlap=True)
    add_bm(bm, mat)

def frame_plate(outer, inner, plane, a0, a1, mat):
    """Plate with a through-opening; outer and inner loops have the same point count."""
    n = len(outer); verts = []
    for loop, a in ((outer, a0), (outer, a1), (inner, a1), (inner, a0)):
        verts += [P3(plane, p, a) for p in loop]
    faces = []
    for k in range(4):
        for i in range(n):
            j = (i+1) % n; k2 = (k+1) % 4
            faces.append([k*n+i, k*n+j, k2*n+j, k2*n+i])
    add_closed(verts, faces, mat)

def loft(rings, mat, caps=True):
    n = len(rings[0]); verts = [v for r in rings for v in r]; faces = []
    for i in range(len(rings)-1):
        for j in range(n): faces.append([i*n+j, i*n+(j+1) % n, (i+1)*n+(j+1) % n, (i+1)*n+j])
    if caps:
        L = len(rings)-1; faces.append(list(range(n))[::-1]); faces.append([L*n+j for j in range(n)])
    add_closed(verts, faces, mat)

# 2D outline helpers -------------------------------------------------------------
def rrect(w, h, r, k, c=(0, 0)):
    """Rounded rectangle, counter-clockwise, k segments per corner (k=0: sharp)."""
    pts = []
    for (sx, sy), a0 in (((1, -1), -90), ((1, 1), 0), ((-1, 1), 90), ((-1, -1), 180)):
        cx, cy = c[0]+sx*(w/2-r), c[1]+sy*(h/2-r)
        if k == 0:
            pts.append((c[0]+sx*w/2, c[1]+sy*h/2)); continue
        for i in range(k+1):
            a = math.radians(a0+90*i/k)
            pts.append((cx+r*math.cos(a), cy+r*math.sin(a)))
    return pts

def round_poly(pts, r, k):
    """Round every corner of a convex CCW polygon."""
    if k == 0 or r <= 0: return list(pts)
    out = []; n = len(pts)
    for i in range(n):
        p, a, b2 = Vector(pts[i]), Vector(pts[i-1]), Vector(pts[(i+1) % n])
        u, v = (a-p).normalized(), (b2-p).normalized()
        half = u.angle(v)/2; t = r/math.tan(half)
        p1, p2 = p+u*t, p+v*t
        bis = (u+v).normalized(); c = p+bis*(r/math.sin(half))
        a1 = math.atan2(p1.y-c.y, p1.x-c.x); a2 = math.atan2(p2.y-c.y, p2.x-c.x)
        da = (a2-a1+math.pi) % (2*math.pi)-math.pi
        for s in range(k+1):
            aa = a1+da*s/k; out.append((c.x+r*math.cos(aa), c.y+r*math.sin(aa)))
    return out

def offset_poly(pts, d):
    """Offset a convex CCW polygon inward by d (negative = outward)."""
    n = len(pts); out = []
    for i in range(n):
        p, a, b2 = Vector(pts[i]), Vector(pts[i-1]), Vector(pts[(i+1) % n])
        e0, e1 = (p-a).normalized(), (b2-p).normalized()
        n0, n1 = Vector((-e0.y, e0.x)), Vector((-e1.y, e1.x))
        m = n0+n1; m *= d/max(.2, 1+n0.dot(n1))
        out.append((p.x+m.x, p.y+m.y))
    return out

def slab(outline, plane, a0, a1, mat, r0=0, r1=0):
    """Extrude a convex outline with rounded (LOD0), chamfered (LOD1) or square ends."""
    def ends(a, r, sign):
        if r <= 0 or LOD == 2: return [(a, 0)]
        if LOD == 1: return [(a, r), (a+sign*r, 0)] if sign > 0 else [(a+sign*r, 0), (a, r)]
        q = [(a, r), (a+sign*.29*r, .29*r), (a+sign*r, 0)]
        return q if sign > 0 else q[::-1]
    steps = ends(a0, r0, 1)+ends(a1, r1, -1)
    rings = [[Vector(P3(plane, p, a)) for p in (offset_poly(outline, d) if d else outline)] for a, d in steps]
    loft(rings, mat)

# ------------------------------------------------------------------ finishing
def finish(name, col, smooth=35):
    global P
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in P.v], [], P.f)
    for m in P.mats: me.materials.append(m)
    me.polygons.foreach_set('material_index', P.fm)
    me.validate(clean_customdata=False)
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
    bad = [f for f in bm.faces if f.calc_area() < 1e-11]
    if bad: bmesh.ops.delete(bm, geom=bad, context='FACES_ONLY')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
    uv = bm.loops.layers.uv.new('UVMap')
    for f in bm.faces:
        n = f.normal; ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            co = l.vert.co
            l[uv].uv = (co.y, co.z) if ax == 0 else (co.x, co.z) if ax == 1 else (co.x, co.y)
    bm.to_mesh(me); bm.free()
    used = sorted({p.material_index for p in me.polygons})
    if used != list(range(len(me.materials))):
        raise RuntimeError(f'{name}: unused material slot')
    me.shade_smooth(); me.set_sharp_from_angle(angle=math.radians(smooth))
    o = bpy.data.objects.new(name, me); col.objects.link(o)
    P = Part()
    return o

def check_device(id, part):
    lo = Vector([min(v[i] for v in part.v) for i in range(3)])
    hi = Vector([max(v[i] for v in part.v) for i in range(3)])
    if any(lo[i] < ENV_MIN[i]-1e-6 or hi[i] > ENV_MAX[i]+1e-6 for i in range(3)):
        raise RuntimeError(f'{id}: outside device envelope {tuple(round(x, 4) for x in lo)}..{tuple(round(x, 4) for x in hi)}')
    skip = set()
    for a, b in part.exempt: skip.update(range(a, b))
    hits = [i for i, v in enumerate(part.v) if i not in skip and all(KEEP_MIN[k] < v[k] < KEEP_MAX[k] for k in range(3))]
    if hits:
        raise RuntimeError(f'{id}: {len(hits)} vertices inside the service port keep-out, e.g. {tuple(round(x, 4) for x in part.v[hits[0]])}')
    return lo, hi

# ====================================================================== SKID
def skid():
    k = 2 if LOD == 0 else 1
    prism(rrect(.57, .335, .025, 1, (0, .0025)), 'XY', .062, .070, FRAME, .0015)           # deck
    for y in (.172, -.167):
        box((0, y, .047), (.60, .02, .046), FRAME, .003)                                    # side rails
        if LOD < 2:
            box((0, y-.011*math.copysign(1, y), .027), (.59, .022, .006), FRAME, .0015)     # channel lip
    for x in (-.235, .235):
        box((x, .0025, .043), (.04, .33, .038), FRAME, .003)                                # cross channels
        for y in (-.1325, .1375):
            box((x, y, .012), (.07, .05, .024), RUB, .004)                                  # feet
    outer = rrect(.359, .111, .02, k, (.0025, .0795))
    inner = rrect(.13, .03, .0149, k, (.0025, .102))
    for x0 in (-.300, .292):
        frame_plate(outer, inner, 'YZ', x0, x0+.008, FRAME)                                 # end plates / handles
    # Socket tower on the front of the left end plate (Unity -X, viewer's right).
    box((-.29, .158, .185), (.036, .036, .10), FRAME, .004)
    box((-.29, .158, .27), (.06, .05, .075), DARK, .008)
    if LOD < 2:
        box((-.29, .1835, .27), (.052, .004, .066), FRAME, .0015)

def port():
    lathe((0, 0, 0), 'Y', [(-.024, .019), (.014, .019), (.0155, .0235), (.024, .0235), (.024, .018), (.008, .018)],
          [STEEL, DARK], 24, segm=[0, 0, 0, 0, 1, 1])

# ====================================================================== TOWING
def towing():
    Y0, Z0 = -.015, .200
    box((.005, .01, .079), (.43, .24, .014), FRAME, .003)                                   # base plate
    housing = [(-.11, .086), (.068, .086), (.074, .10), (.058, .25), (.028, .292), (-.072, .292), (-.102, .25), (-.116, .10)]
    for x0, x1 in ((-.146, -.116), (.121, .151)):
        prism(housing, 'YZ', x0, x1, YEL, .005)                                             # cast end frames
    # Drum flanges and wire rope.
    for x, s in ((-.110, -1), (.115, 1)):
        lathe((x, Y0, Z0), 'X', [(-.0035*s, .05), (-.0035*s, .079), (0, .082), (.0035*s, .079), (.0035*s, .05)], DARK, 32)
    if LOD == 0:
        pitch = .217/13; pr = [(-.106, .05), (-.106, .0645)]
        for i in range(13):
            t = -.106+i*pitch
            pr += [(t+pitch*.5, .0690), (t+pitch, .0645)]
        pr += [(.111, .05)]
    else:
        pr = [(-.106, .05), (-.106, .067), (.111, .067), (.111, .05)]
    lathe((0, Y0, Z0), 'X', pr, ROPE, 32)
    # Motor (Unity +X... viewer's left) and planetary gearbox.
    if LOD == 0:
        mp = [(-.146, .058), (-.150, .068), (-.160, .068), (-.162, .0715), (-.170, .0715), (-.172, .068),
              (-.240, .068), (-.242, .0715), (-.250, .0715), (-.252, .068), (-.258, .0665), (-.266, .058), (-.272, .044)]
        ms = [0]*10+[1, 1, 1]
    else:
        mp = [(-.146, .058), (-.150, .068), (-.256, .068), (-.266, .057), (-.272, .044)]; ms = [0, 0, 1, 1]
    lathe((0, Y0, Z0), 'X', mp, [YEL, DARK], 32, segm=ms)
    if LOD == 0:
        gp = [(.151, .064), (.156, .078), (.198, .078), (.200, .0755), (.205, .0755), (.207, .078), (.248, .078), (.256, .073), (.262, .064), (.266, .05)]
        gs = [0]*7+[1, 1]
    else:
        gp = [(.151, .064), (.156, .078), (.250, .078), (.262, .064), (.266, .05)]; gs = [0, 0, 1, 1]
    lathe((0, Y0, Z0), 'X', gp, [YEL, DARK], 32, segm=gs)
    if LOD < 2:
        cyl((.205, Y0, .284), .011, .014, DARK, 'Z', 16)                                      # clutch pivot
        beam((.205, Y0, .292), (.247, Y0+.034, .294), .012, .008, DARK)                    # clutch lever
        beam((.240, Y0+.028, .294), (.262, Y0+.046, .295), .016, .013, RUB, b=.003)
        for y, z in ((-.075, .27), (-.097, .13)):
            rod((-.150, y, z), (.155, y, z), .0065, STEEL, 12)                              # tie bars
    # Contactor box across the frame tops.
    box((.0025, -.022, .318), (.272, .085, .05), DARK, .008)
    if LOD < 2:
        box((.0025, -.022, .3455), (.258, .072, .006), FRAME, .002)
        tube([(-.205, -.035, .262), (-.205, -.035, .29), (-.19, -.035, .31), (-.137, -.035, .318)], .0055, RUB, 8)
        exempt(lambda: tube([(-.137, .005, .312), (-.175, .03, .322), (-.215, .10, .31), (-.243, .148, .286), CABLE_END], .006, RUB, 8))
    # Roller fairlead.
    k = 2 if LOD == 0 else 1
    frame_plate(rrect(.232, .110, .012, k, (.005, .141)), rrect(.19, .086, .008, k, (.005, .141)), 'XZ', .118, .134, DARK)
    if LOD < 2:
        for z in (.1115, .1705):
            lathe((.005, .130, z), 'X', [(-.095, .010), (-.092, .013), (.092, .013), (.095, .010)], STEEL, 16)
        for x in (-.076, .086):
            lathe((x, .122, .141), 'Z', [(-.042, .010), (-.039, .013), (.039, .013), (.042, .010)], STEEL, 16)
    # Rope out through the fairlead, thimble and hook.
    rod((.005, Y0, .136), (.005, .156, .137), .0045, ROPE, 8)
    if LOD < 2:
        lathe((.005, .163, .127), 'X', [(-.004, .006), (-.004, .014), (0, .0165), (.004, .014), (.004, .006), (0, .004)], STEEL, 16, closed=True)
        hook = [Vector((.005, .163, .114)), Vector((.005, .163, .102))]
        hook += [Vector((.005, y, z)) for y, z in [(p.y, p.z) for p in arc((0, .184, .102), .021, 180, 395, 8 if LOD == 0 else 5)][1:]]
        tube(hook, .0065, STEEL, 10)
        if LOD == 0:
            beam((.005, .2035, .1115), (.005, .1655, .114), .005, .0035, DARK)             # latch
    else:
        box((.005, .17, .11), (.012, .03, .04), STEEL)

# ====================================================================== WATER
def water():
    Y0, Z0 = -.02, .19
    for y in (-.085, .045):
        box((.02, y, .080), (.40, .032, .016), FRAME, .003)                                 # base rails
    foot = [(-.105, .088), (.065, .088), (.04, .128), (-.08, .128)]
    for x in (.015, .165):
        prism(foot, 'YZ', x-.015, x+.015, BLUE, .004)                                       # motor feet
    lathe((0, Y0, Z0), 'X', [(-.035, .058), (-.035, .075), (-.028, .075), (-.026, .07), (.195, .07), (.20, .066)], BLUE, 32)
    nf = 16 if LOD == 0 else 8 if LOD == 1 else 0
    for i in range(nf):
        a = 2*math.pi*i/nf + math.pi/nf
        if abs(math.sin(a)+1) < .35: continue                                               # leave room for the feet
        box((.093, Y0+.0745*math.cos(a), Z0+.0745*math.sin(a)), (.19, .005, .012), BLUE, 0, (a-math.pi/2, 0, 0))
    cp = [(.198, .070), (.203, .078), (.255, .078), (.262, .072)]
    cp += [(.265, .064), (.262, .058), (.266, .052), (.262, .046), (.266, .040), (.262, .034), (.266, .028), (.264, .018)] if LOD == 0 else [(.266, .04)]
    lathe((0, Y0, Z0), 'X', cp, DARK, 32)                                                   # fan cowl
    box((.09, Y0, .274), (.085, .075, .04), BLUE, .007)                                     # terminal box
    lathe((0, Y0, Z0), 'X', [(-.082, .040), (-.078, .044), (-.040, .044), (-.036, .040)], DARK, 24)  # lantern
    prism([(-.095, .088), (.055, .088), (.035, .11), (-.075, .11)], 'YZ', -.13, -.105, BLUE, .004)  # pump foot
    # Volute: spiral casing, radius growing to the tangential discharge at the front.
    nv = S(32); ch = bev(.006); out = []
    for i in range(nv+1):
        a = 2*math.pi*i/nv; r = .068+.022*i/nv
        out.append(Vector((0, Y0+r*math.cos(a), Z0+r*math.sin(a))))
    def at(x, inset):
        return [Vector((x, Y0+(p.y-Y0)*(1-inset/((p.y-Y0)**2+(p.z-Z0)**2)**.5), Z0+(p.z-Z0)*(1-inset/((p.y-Y0)**2+(p.z-Z0)**2)**.5))) for p in out]
    loft([at(-.152, ch), at(-.152+ch, 0), at(-.084-ch, 0), at(-.084, ch)] if ch else [at(-.152, 0), at(-.084, 0)], BLUE)
    lathe((-.118, .04, 0), 'Z', [(.19, .030), (.335, .030), (.34, .034), (.345, .034)], BLUE, 24)   # discharge neck
    if LOD < 2:
        lathe((-.118, .04, 0), 'Z', [(.345, .033), (.35, .037), (.37, .037), (.372, .034), (.378, .034), (.38, .037),
                                    (.39, .037), (.393, .032), (.393, .024), (.384, .024)], [STEEL, DARK], 24, segm=[0]*8+[1, 1])
    else:
        lathe((-.118, .04, 0), 'Z', [(.345, .036), (.39, .036)], STEEL, 8)
    if LOD == 0:
        for s in (-1, 1):
            beam((-.118+s*.040, .04, .372), (-.118+s*.046, .04, .336), .009, .006, STEEL)    # cam arms
        cyl((-.118, Y0-.012, .268), .008, .014, STEEL, 'Z', 6)                               # priming plug
    sp = [(-.152, .036), (-.158, .036), (-.158, .05), (-.165, .05), (-.165, .033), (-.205, .033)]
    sp += [(-.208, .038), (-.222, .038), (-.224, .035), (-.230, .035), (-.232, .038), (-.248, .038), (-.252, .033), (-.252, .024), (-.240, .024)] if LOD < 2 else [(-.205, .037), (-.250, .037)]
    lathe((0, Y0, Z0), 'X', sp, [BLUE, STEEL, DARK], 24, segm=([0]*5+[1]*7+[2, 2]) if LOD < 2 else [0]*5+[1, 1])
    if LOD < 2:
        box((.09, Y0, .2955), (.077, .067, .006), DARK, .002)                               # terminal lid
        cyl((.046, Y0+.015, .274), .008, .012, DARK, 'X', 12)                                # gland
        exempt(lambda: tube([(.042, Y0+.015, .274), (.01, .02, .29), (-.06, .085, .305), (-.17, .135, .30), (-.228, .158, .285), CABLE_END], .006, RUB, 8))
    # Tubular roll frame.
    st = 6 if LOD == 0 else 3 if LOD == 1 else 2; tn = 10
    for x in (-.268, .268):
        pts = [Vector((x, -.150, .078)), Vector((x, -.150, .375))]
        pts += [Vector((x, p.y, p.z)) for p in arc((0, -.110, .375), .04, 180, 90, st)]
        pts += [Vector((x, p.y, p.z)) for p in arc((0, .045, .375), .04, 90, 0, st)]
        pts += [Vector((x, .085, .078))]
        tube(pts, .011, FRAME, tn)
        if LOD < 2:
            for y in (-.150, .085): box((x, y, .075), (.032, .032, .006), RUB, .002)
    for y, z in ((-.09, .415), (.025, .415), (-.150, .09), (.085, .09)):
        if z < .3 and LOD == 2: continue
        rod((-.268, y, z), (.268, y, z), .011 if z > .3 else .009, FRAME, tn)
    if LOD < 2:
        for y in (-.09, .025):
            lathe((0, y, .415), 'X', [(-.08, .012), (-.075, .0145), (.075, .0145), (.08, .012)], RUB, 16)

# ====================================================================== FIRE
def fire():
    Y0, Z0 = -.02, .25
    for x, s in ((-.115, -1), (.115, 1)):
        if LOD == 0:
            fp = [(-.004, .045), (-.004, .094), (-.007, .099), (-.007, .109), (-.004, .114), (-.004, .152),
                  (-.007, .157), (-.007, .162), (0, .1655), (.004, .162), (.004, .045)]
        elif LOD == 1:
            fp = [(-.004, .045), (-.004, .155), (0, .1655), (.004, .158), (.004, .045)]
        else:
            fp = [(-.004, .045), (-.004, .165), (.004, .165), (.004, .045)]
        lathe((x, Y0, Z0), 'X', [(t*-s, r) for t, r in fp], RED, 40)                          # pressed reel flanges
        if LOD < 2:
            lathe((x+s*.0045, Y0, Z0), 'X', [(-.003, .052), (.003, .052), (.005, .046), (.005, .03)], DARK, 24)  # hub plate
    if LOD == 0:
        pitch = .222/8; hp = [(-.111, .06), (-.111, .108)]
        for i in range(8):
            t = -.111+i*pitch
            hp += [(t+pitch*.25, .118), (t+pitch*.5, .1215), (t+pitch*.75, .118), (t+pitch, .108)]
        hp += [(.111, .06)]
    else:
        hp = [(-.111, .06), (-.111, .116), (.111, .116), (.111, .06)]
    lathe((0, Y0, Z0), 'X', hp, RUB, 28)                                                   # wound hose
    rod((-.170, Y0, Z0), (.170, Y0, Z0), .011, STEEL, 12)                                   # axle
    for x in (-.147, .147):
        box((x, Y0, Z0), (.026, .044, .05), FRAME, .005)                                    # bearing blocks
        for y in (-.150, .105):
            rod((x, Y0+.35*(y-Y0)/abs(y-Y0)*.04, Z0-.022), (x, y, .086), .011, FRAME, 8)    # A-frame legs
        box((x, -.022, .079), (.03, .30, .014), FRAME, .003)                                # foot rails
        if LOD < 2:
            lathe((x*.86, Y0, Z0), 'X', [(-.006, .024), (.006, .024)], STEEL, 16)            # hub bosses
    rod((-.147, -.14, .11), (.147, -.14, .11), .009, FRAME, 8)                              # rear brace
    # Crank on the right-hand (Unity +X) side.
    a = math.radians(-40); tip = (.176, Y0+.085*math.cos(a), Z0+.085*math.sin(a))
    beam((.176, Y0, Z0), tip, .02, .010, DARK, up=(1, 0, 0), b=.003)
    if LOD < 2:
        cyl((.172, Y0, Z0), .016, .012, DARK, 'X', 12)
        rod((.178, tip[1], tip[2]), (.238, tip[1], tip[2]), .0055, STEEL, 8)
        lathe((0, tip[1], tip[2]), 'X', [(.19, .009), (.193, .012), (.232, .012), (.236, .009)], RUB, 12)
    # Supply swivel, ball valve and inlet coupling at the back.
    lathe((0, Y0, Z0), 'X', [(-.168, .016), (-.172, .02), (-.19, .02), (-.193, .016)], STEEL, 16)
    st = 4 if LOD == 0 else 2
    pipe = [Vector((-.19, Y0, Z0)), Vector((-.20, Y0, Z0))]
    pipe += [Vector((p.x, Y0, p.z)) for p in arc((-.20, 0, Z0-.02), .02, 90, 0, st, u=(1, 0, 0), v=(0, 0, 1))][1:]
    pipe += [Vector((-.22, p.y, p.z)) for p in arc((0, Y0-.02, .16), .02, 0, -90, st, u=(0, 1, 0), v=(0, 0, 1))]
    pipe += [Vector((-.22, -.168, .14))]
    tube(pipe, .011, STEEL, 12)
    if LOD < 2:
        box((-.22, Y0, .196), (.034, .034, .036), STEEL, .004)                              # ball valve
        beam((-.22, Y0, .217), (-.22, Y0+.06, .220), .010, .006, RED, b=.002)               # valve lever
        lathe((-.22, 0, .14), 'Y', [(-.165, .014), (-.168, .018), (-.19, .018), (-.193, .015), (-.196, .015), (-.198, .018), (-.205, .018), (-.207, .012)], STEEL, 16)
    # Hose end in the front holder, nozzle pointing to Unity -X.
    hose = [(.02, Y0+.118*math.cos(math.radians(-55)), Z0+.118*math.sin(math.radians(-55))), (.02, .085, .128), (.005, .12, .108),
            (-.025, .15, .100), (-.055, .172, .100), (-.07, .175, .100)]
    tube(hose, .0115, RUB, 10)
    np_ = [(-.068, .012), (-.070, .016), (-.088, .016), (-.090, .0135), (-.094, .0135), (-.096, .017), (-.150, .021),
           (-.152, .024), (-.175, .024), (-.178, .020), (-.186, .016), (-.190, .010)]
    lathe((0, .175, .100), 'X', np_ if LOD < 2 else [(-.068, .016), (-.150, .021), (-.178, .024), (-.19, .012)],
          [STEEL, DARK, RUB], 20, segm=[0, 0, 0, 0, 0, 1, 1, 2, 2, 0, 0] if LOD < 2 else [0, 1, 2])
    if LOD < 2:
        box((-.12, .175, .079), (.03, .04, .014), DARK, .002)                                # holder
        for y in (.148, .202): box((-.12, y, .095), (.02, .006, .032), DARK, .0015)

# ====================================================================== TECH
def tech():
    k = 3 if LOD == 0 else 1 if LOD == 1 else 0
    shell = round_poly([(-.165, .118), (.135, .118), (.135, .245), (.078, .352), (-.165, .352)], .016, k)
    slab(shell, 'YZ', -.245, .245, IVORY, .014, .014)
    box((0, -.015, .1015), (.50, .31, .033), FRAME, .006)                                   # base tray
    if LOD < 2:
        box((0, -.015, .1205), (.493, .303, .006), BLUE, .0015)                             # van-blue seam
        for x in (-.205, .205):
            for y in (-.135, .105): box((x, y, .0785), (.05, .04, .013), RUB, .003)
    # Sloped control panel.
    A, B = Vector((0, .135, .245)), Vector((0, .078, .352))
    d = (B-A).normalized(); nrm = Vector((0, d.z, -d.y)); M = (A+B)/2
    obox(M+nrm*.003, (1, 0, 0), d, (.40, .085, .006), DARK, .002)
    if LOD < 2:
        lathe(M+nrm*.006+Vector((-.13, 0, 0)), nrm, [(0, .019), (.006, .019), (.008, .015), (.022, .013), (.024, .009)], RUB, 20)
        obox(M+nrm*.0065+Vector((.085, 0, 0)), (1, 0, 0), d, (.15, .05, .002), GLASS)
        for x, lamp in ((-.065, LGREEN), (-.035, LAMBER)):
            lathe(M+nrm*.006+Vector((x, 0, 0)), nrm, [(0, .009), (.004, .009), (.005, .007), (.009, .004)], [DARK, lamp], 12, segm=[0, 1, 1, 1])
        if LOD == 0:
            obox(M+nrm*.0305+Vector((-.13, 0, 0)), (1, 0, 0), d, (.004, .02, .004), STEEL)  # knob pointer
    # Lower front: two industrial sockets and the main switch.
    for x, mat in ((-.12, BLUE), (-.04, RED)):
        box((x, .137, .18), (.056, .006, .06), mat, .003)
        if LOD < 2:
            lathe((x, 0, .18), 'Y', [(.14, .021), (.155, .021), (.156, .024), (.163, .024), (.165, .020)], mat, 20)
            if LOD == 0: box((x, .157, .207), (.02, .012, .008), mat, .002)
    box((.11, .137, .18), (.065, .006, .065), DARK, .004)
    if LOD < 2:
        cyl((.11, .143, .18), .02, .008, DARK, 'Y', 16)
        box((.11, .152, .18), (.052, .014, .014), RED, .003, (0, math.pi/4, 0))
    # Side louvres, rear vent.
    for s in (-1, 1):
        if LOD < 2: box((s*.2475, -.02, .235), (.004, .17, .12), DARK, .001)
        if LOD == 0:
            for z in (.195, .22, .245, .27): box((s*.249, -.02, z), (.006, .17, .009), IVORY, .002)
    if LOD == 0: box((0, -.1675, .24), (.30, .004, .10), DARK, .001)
    # Carry handle.
    for x in (-.16, .16): box((x, -.045, .376), (.03, .035, .048), FRAME, .005)
    rod((-.172, -.045, .393), (.172, -.045, .393), .010, STEEL, 12)
    if LOD < 2:
        lathe((0, -.045, .393), 'X', [(-.09, .011), (-.086, .0145), (.086, .0145), (.09, .011)], RUB, 16)
        cyl((-.250, .07, .285), .008, .012, DARK, 'X', 12)
        exempt(lambda: tube([(-.252, .07, .285), (-.262, .10, .288), (-.264, .135, .28), CABLE_END], .006, RUB, 8))

# ====================================================================== CARGO
def cargo():
    W, D, cy = .488, .33, -.01
    kc = 2 if LOD == 0 else 1 if LOD == 1 else 0
    plan = rrect(W, D, .022, kc, (0, cy))
    slab(plan, 'XY', .086, .348, ALU, .012, 0)                                             # body
    if LOD < 2:
        for z0, z1 in ((.165, .178), (.272, .285)):
            slab(offset_poly(plan, -.004), 'XY', z0, z1, ALU, .003, .003)                  # pressed ribs
        slab(offset_poly(plan, .003), 'XY', .347, .355, RUB)                               # gasket
    slab(offset_poly(plan, -.0035), 'XY', .354, .415, ALU, 0, .012)                        # lid
    if LOD < 2:
        slab(rrect(W-.08, D-.08, .015, kc, (0, cy)), 'XY', .412, .419, ALU, 0, .003)       # lid panel
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx*(W/2-.018), cy+sy*(D/2-.018)
            box((x, y, .398), (.046, .046, .038), DARK, .006)                               # corner caps
            box((x, y, .104), (.046, .046, .036), DARK, .006)
    for yf, s in ((cy+D/2, 1), (cy-D/2, -1)):
        if s < 0 and LOD > 0: continue
        for x in (-.13, .13):
            if LOD < 2:
                box((x, yf+s*.0025, .333), (.05, .005, .04), STEEL, .002)                   # latch base
                box((x, yf+s*.003, .368), (.032, .006, .016), STEEL, .002)                  # catch
            box((x, yf+s*.0065, .338), (.036, .008, .046), DARK, .003, (math.radians(-8)*s, 0, 0))
    for sx in (-1, 1):
        x = sx*(W/2)
        if LOD < 2:
            for y in (cy-.07, cy+.07): box((x+sx*.006, y, .31), (.012, .018, .022), DARK, .003)
            st = 3 if LOD == 0 else 1
            h = [Vector((x+sx*.012, cy-.07, .31))]
            h += [Vector((x+sx*.012, p.y, p.z)) for p in arc((0, cy-.058, .284), .012, 180, 270, st)]
            h += [Vector((x+sx*.012, p.y, p.z)) for p in arc((0, cy+.058, .284), .012, 270, 360, st)]
            h += [Vector((x+sx*.012, cy+.07, .31))]
            tube(h, .0055, STEEL, 8)
            lathe((x+sx*.012, 0, .272), 'Y', [(cy-.05, .009), (cy-.047, .0115), (cy+.047, .0115), (cy+.05, .009)], RUB, 12)
    if LOD < 2:
        for y in (cy-.11, cy+.11): box((0, y, .0785), (.42, .03, .013), RUB, .003)

BUILDERS = {'towing': towing, 'water': water, 'fire': fire, 'tech': tech, 'cargo': cargo}

# ====================================================================== build
export_col = bpy.data.collections.new('SM_EXPORT (Unity FBX, at module origin)')
scene.collection.children.link(export_col)
report = {'models': [], 'fixtures': {}}
lods = {}
for id in IDS:
    lods[id] = []
    for level in range(3):
        LOD = level
        BUILDERS[id]()
        P.v = [v+Vector((0, 0, LIFT)) for v in P.v]
        lo, hi = check_device(id, P)
        if level == 0: report_bounds = (lo, hi)
        lods[id].append(finish(f'{id}_LOD{level}', export_col))
    tris = [len(o.data.polygons) for o in lods[id]]
    if not (tris[0] > tris[1] > tris[2] and tris[2] < tris[0]*.15):
        raise RuntimeError(f'{id}: LOD triangle counts {tris} do not fall off enough')
    report['models'].append({'id': id, 'tris': tris, 'bounds': [list(map(lambda x: round(x, 4), report_bounds[0])), list(map(lambda x: round(x, 4), report_bounds[1]))]})
    print('DEVICE', id, tris, flush=True)

LOD = 0
skid(); skid_obj = finish('ServiceModuleSkid', export_col)
port(); port_obj = finish('ServiceModulePort', export_col)
for o in (skid_obj, port_obj):
    report['fixtures'][o.name] = len(o.data.polygons)
print('FIXTURES', report['fixtures'], flush=True)
export_col.hide_render = True
for o in export_col.objects: o.hide_set(True)

# ---------------------------------------------------------------- preview layout
preview = bpy.data.collections.new('SM_ONIZLEME (yerlesim, disa aktarilmaz)')
scene.collection.children.link(preview)
roots = {}
for i, id in enumerate(IDS):
    root = bpy.data.objects.new(f'Modul | {id}', None); root.empty_display_size = .2
    root.location = ((i-2)*1.25, 0, 0); preview.objects.link(root); roots[id] = root
    for src, loc in ((lods[id][0], (0, 0, 0)), (skid_obj, (0, 0, 0)), (port_obj, tuple(PORT_AT))):
        o = bpy.data.objects.new(f'{id} | {src.name}', src.data); o.location = loc
        preview.objects.link(o); o.parent = root

# Studio (render only).
stage = bpy.data.collections.new('PREVIEW_ONLY'); scene.collection.children.link(stage)
me = bpy.data.meshes.new('Preview floor')
me.from_pydata([(-60, -60, 0), (60, -60, 0), (60, 60, 0), (-60, 60, 0)], [], [(0, 1, 2, 3)])
fl = bpy.data.materials.new('preview floor'); fl.use_nodes = True
fl.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.40, .385, .345, 1)
fl.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .92
me.materials.append(fl)
stage.objects.link(bpy.data.objects.new('Preview floor', me))
world = bpy.data.worlds.new('World'); scene.world = world; world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (.70, .69, .65, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = .45
for loc, power, size, color in [((-3, 4, 5), 420, 4, (1.0, .90, .78)), ((4, 3, 4), 260, 3.5, (.82, .90, 1.0)), ((1, -4, 3), 140, 3, (1.0, .96, .90))]:
    data = bpy.data.lights.new('Softbox', 'AREA'); data.energy = power; data.shape = 'DISK'; data.size = size; data.color = color
    o = bpy.data.objects.new('Preview softbox', data); stage.objects.link(o); o.location = loc
    o.rotation_euler = (Vector((0, 0, .2))-o.location).to_track_quat('-Z', 'Y').to_euler()
camdata = bpy.data.cameras.new('Preview camera'); camdata.type = 'ORTHO'; camdata.ortho_scale = 1.0
cam = bpy.data.objects.new('Preview camera', camdata); stage.objects.link(cam); scene.camera = cam
scene.render.engine = 'CYCLES'; scene.cycles.samples = 64; scene.cycles.use_denoising = True
scene.render.resolution_x = 1400; scene.render.resolution_y = 1000
scene.render.image_settings.file_format = 'PNG'; scene.view_settings.view_transform = 'AgX'

# ---------------------------------------------------------------- save / export
BLEND.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
if DELIVERY:
    (DELIVERY/'Blender').mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(DELIVERY/'Blender'/'Servis_Modulleri.blend'), copy=True)

def fbx(path, objs):
    for o in bpy.data.objects: o.select_set(False)
    for o in objs: o.hide_set(False); o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'MESH'}, axis_forward='Z', axis_up='Y',
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', use_space_transform=True, bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True, add_leaf_bones=False, bake_anim=False, path_mode='AUTO')
    for o in objs: o.hide_set(True)

if not NO_EXPORT:
    FBX_DIR.mkdir(parents=True, exist_ok=True)
    blend_rel = BLEND.relative_to(REPO).as_posix()
    manifest = {'version': 1, 'blender': '5.2', 'models': [], 'fixtures': []}
    for entry in report['models']:
        id = entry['id']
        fbx(FBX_DIR/f'{id}.fbx', lods[id])
        manifest['models'].append({'id': id, 'lods': [{'name': o.name, 'triangles': len(o.data.polygons),
            'materials': [m.name for m in o.data.materials]} for o in lods[id]], 'blend': blend_rel,
            'bounds_blender': {'min': entry['bounds'][0], 'max': entry['bounds'][1]}})
    fbx(FBX_DIR/'service_module_fixtures.fbx', [skid_obj, port_obj])
    manifest['fixtures'] = [{'name': o.name, 'triangles': len(o.data.polygons), 'materials': [m.name for m in o.data.materials]} for o in (skid_obj, port_obj)]
    MANIFEST.write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
    PALETTE_JSON.write_text(json.dumps({'materials': [{'name': k, 'color': list(v[0]), 'metallic': v[1], 'smoothness': round(1-v[2], 3)}
        for k, v in PALETTE.items()]}, indent=2)+'\n', encoding='utf-8')
    if DELIVERY:
        (DELIVERY/'Unity_GLB').mkdir(parents=True, exist_ok=True)
        for id, root in roots.items():
            keep = root.location.copy(); root.location = (0, 0, 0); bpy.context.view_layer.update()
            for o in bpy.data.objects: o.select_set(False)
            root.select_set(True)
            for c in root.children: c.select_set(True)
            bpy.ops.export_scene.gltf(filepath=str(DELIVERY/'Unity_GLB'/f'Servis_Modulu_{id}.glb'), export_format='GLB',
                                      use_selection=True, export_apply=True, export_yup=True)
            root.location = keep
        bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))

print('SM_REPORT '+json.dumps(report), flush=True)
print(f'SM_DONE {time.time()-T0:.1f}s', flush=True)
