"""Export the rescue-gear source models (02_KAYNAK_MODELLER) to FBX for the Rescue Team game.

usage: blender -b <file.blend> --python export_rescue_gear.py -- <job> <out_dir>
Jobs are defined below per source file. Each job exports one or more FBX files plus <name>.materials.json
(Principled BSDF values: linear base colour, metallic, roughness, emission, alpha) and <name>.points.json
(attachment empties in Blender coordinates relative to the exported root).
Static models use the repo's ambulance settings (axis_forward='-Z', axis_up='Y', no bake_space_transform) plus empties;
the dog uses the repo's armature settings (axis_forward='-Z', axis_up='Y', all actions as takes).
"""
import bpy, json, os, sys
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index('--') + 1:]
JOB, OUT = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)


def coll_objects(name):
    c = bpy.data.collections[name]
    out = []
    def walk(col):
        for o in col.objects:
            if o not in out:
                out.append(o)
        for ch in col.children:
            walk(ch)
    walk(c)
    return out


def principled(mat):
    info = {'name': mat.name}
    node = None
    if mat.use_nodes and mat.node_tree:
        for n in mat.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                node = n
                break
    if node is None:
        c = mat.diffuse_color
        info.update(base=[c[0], c[1], c[2], c[3]], metallic=mat.metallic, roughness=mat.roughness, emission=[0, 0, 0], emissionStrength=0.0, alpha=c[3], transmission=0.0)
        return info

    def val(key, default):
        s = node.inputs.get(key)
        if s is None:
            return default
        if s.is_linked:
            return default
        v = s.default_value
        try:
            return [float(x) for x in v]
        except TypeError:
            return float(v)

    info.update(base=val('Base Color', [0.8, 0.8, 0.8, 1.0]), metallic=val('Metallic', 0.0), roughness=val('Roughness', 0.5),
                emission=val('Emission Color', [0, 0, 0, 1])[:3], emissionStrength=val('Emission Strength', 0.0),
                alpha=val('Alpha', 1.0), transmission=val('Transmission Weight', 0.0))
    return info


def write_json(path, data):
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=1)


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons) if o.type == 'MESH' else 0


def select_only(objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.hide_set(False)
        o.hide_viewport = False
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def make_visible(objs):
    # objects in excluded / hidden collections cannot be selected: link the collection chain into the view layer
    for lc in bpy.context.view_layer.layer_collection.children:
        lc.exclude = False
        lc.hide_viewport = False
    for o in objs:
        o.hide_set(False)


def export_static(name, objs, root, extra_empties=True):
    """Export objs (mesh + empties) as one FBX whose origin is the root's world position."""
    make_visible(objs)
    # move the root to the origin (keep children relative) so the FBX pivot is the model root
    original = root.matrix_world.copy()
    if root.parent is not None and root.parent not in objs:
        root.parent = None
        root.matrix_world = original
    root.matrix_world = Matrix.Translation(-original.translation) @ root.matrix_world
    bpy.context.view_layer.update()
    select_only(objs)
    path = os.path.join(OUT, name + '.fbx')
    types = {'MESH', 'EMPTY'} if extra_empties else {'MESH'}
    # Hierarchies with pivots/empties: the repo's ambulance settings (no bake_space_transform; baking the axis change
    # into nested children rotated motors, wheels and propellers wrongly). Unity = (-x, z, -y) of Blender.
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types=types, axis_forward='-Z', axis_up='Y',
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', use_space_transform=True,
                             bake_space_transform=False, use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True,
                             add_leaf_bones=False, bake_anim=False, path_mode='AUTO', use_custom_props=False)
    mats = {}
    for o in objs:
        if o.type == 'MESH':
            for s in o.material_slots:
                if s.material and s.material.name not in mats:
                    mats[s.material.name] = principled(s.material)
    lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        if o.type == 'MESH':
            for v in o.data.vertices:
                w = o.matrix_world @ v.co
                lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
    points = {o.name: list(o.matrix_world.translation) for o in objs if o.type == 'EMPTY'}
    write_json(os.path.join(OUT, name + '.materials.json'), {'materials': list(mats.values())})
    write_json(os.path.join(OUT, name + '.points.json'), {'root': root.name, 'boundsBlender': {'min': list(lo), 'max': list(hi)},
                                                           'triangles': sum(tris(o) for o in objs), 'points': points})
    print('EXPORTED', name, 'objects', len(objs), 'tris', sum(tris(o) for o in objs), 'bounds', [round(x, 3) for x in lo], [round(x, 3) for x in hi], flush=True)
    root.matrix_world = original
    bpy.context.view_layer.update()


def export_dog(name):
    objs = coll_objects('KOPEK_EXPORT')
    make_visible(objs)
    arm = [o for o in objs if o.type == 'ARMATURE'][0]
    select_only(objs)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(OUT, name + '.fbx')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             add_leaf_bones=False, use_armature_deform_only=False, bake_anim=True,
                             bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0,
                             bake_anim_force_startend_keying=True, mesh_smooth_type='FACE', use_mesh_modifiers=False,
                             use_custom_props=False, check_existing=False)
    mats = {}
    for o in objs:
        if o.type == 'MESH':
            for s in o.material_slots:
                if s.material and s.material.name not in mats:
                    mats[s.material.name] = principled(s.material)
    actions = {a.name: [a.frame_range[0], a.frame_range[1]] for a in bpy.data.actions}
    write_json(os.path.join(OUT, name + '.materials.json'), {'materials': list(mats.values())})
    write_json(os.path.join(OUT, name + '.points.json'), {'root': arm.name, 'actions': actions, 'fps': bpy.context.scene.render.fps,
                                                           'triangles': sum(tris(o) for o in objs)})
    print('EXPORTED', name, 'actions', len(actions), 'fps', bpy.context.scene.render.fps, flush=True)


def root_of(objs, prefix):
    return [o for o in objs if o.name.startswith(prefix)][0]


if JOB == 'boat':
    objs = coll_objects('KB_KURTARMA_BOTU')
    export_static('KurtarmaBotu', objs, root_of(objs, 'KB00'))
elif JOB == 'trailer':
    objs = coll_objects('RM_BOT_ROMORKU')
    export_static('BotRomorku', objs, root_of(objs, 'RM00'))
    objs = coll_objects('CD_CEKI_DEMIRI')
    export_static('CekiDemiri', objs, root_of(objs, 'CD00'))
elif JOB == 'drone':
    objs = coll_objects('DR_DRONE')
    export_static('Drone', objs, root_of(objs, 'DR00'))
    objs = coll_objects('DS_DRONE_ISTASYONU')
    export_static('DroneIstasyonu', objs, root_of(objs, 'DS00'))
    pay = coll_objects('DY_DRONE_YUKLERI')
    for prefix, name in (('DY10', 'DroneYuku_CanYelegi'), ('DY20', 'DroneYuku_Telsiz'), ('DY30', 'DroneYuku_IpMakarasi')):
        r = root_of(pay, prefix)
        sub = [r] + [o for o in r.children_recursive if o in pay]
        export_static(name, sub, r)
elif JOB == 'dog':
    export_dog('Kopek')
elif JOB == 'food':
    objs = coll_objects('FOOD_DRINK_GAME_ASSET')
    # one FBX with the whole table set; the set's own origin (0,0,0) is the table-top centre
    anchor = bpy.data.objects.new('YiyecekIcecek_Koku', None)
    bpy.context.scene.collection.objects.link(anchor)
    for o in objs:
        if o.parent is None:
            m = o.matrix_world.copy(); o.parent = anchor; o.matrix_world = m
    export_static('YiyecekIcecekSeti', objs + [anchor], anchor)
elif JOB in ('van', 'tools'):
    # sets without a single root (van: 38 top-level parts; tools: six item roots) get an anchor at the origin
    coll, name = ('VAN_22_MODULAR_PARTS', 'KurtarmaVani') if JOB == 'van' else ('POWER_TOOLS_PRINTER_GAME_ASSET', 'ElektrikliAletlerSeti')
    objs = coll_objects(coll)
    anchor = bpy.data.objects.new(name + '_Koku', None)
    bpy.context.scene.collection.objects.link(anchor)
    for o in objs:
        if o.parent is None:
            m = o.matrix_world.copy(); o.parent = anchor; o.matrix_world = m
    export_static(name, objs + [anchor], anchor)
elif JOB == 'rescuekit':
    for coll, prefix, name in (('SK1_CAN_SIMIDI', 'SK10', 'CanSimidi'), ('SK2_KURTARMA_ASKISI', 'SK20', 'KurtarmaAskisi'),
                               ('SK3_MEGAFON', 'SK30', 'Megafon'), ('SK4_AGAC_KAYISI', 'SK40', 'AgacKayisi')):
        objs = coll_objects(coll)
        export_static(name, objs, root_of(objs, prefix))
else:
    raise SystemExit('unknown job ' + JOB)
