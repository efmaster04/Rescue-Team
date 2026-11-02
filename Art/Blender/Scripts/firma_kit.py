"""Firma Blender kit — shared helpers for every generated model.

Style: Stylized Mid-Poly / Semi-Realistic Indie. Real-world proportions, light bevels,
clean silhouettes, materials taken from the existing Garaj_Tam_Sahne palette.

Conventions (must match the Unity side):
  * 1 Blender unit = 1 metre, Z up.
  * A model's FRONT faces Blender +Y. After export it faces Unity +Z.
    Export mapping (axis_forward='Z', axis_up='Y'):  Unity(x, y, z) = Blender(x, z, y)
  * Origin: ground contact centre for furniture/vehicles/props, unless a brief says otherwise.
  * Naming of helper objects (exported as empties / meshes and read by Unity importers):
      SOCKET_<Name>  empty: attach / snap point (its local +Y = forward, +Z = up)
      ANIM_<Name>    moving part; its origin is the pivot (hinge, wheel centre, slide start)
      COL_<Name>     box mesh used as a BoxCollider in Unity (hidden in game)
      LIGHT_<Name>   empty where Unity adds a light
      SCREEN_<Name>  flat quad that receives a runtime screen texture
"""
import bpy
import bmesh
import math
import os
import json
from mathutils import Vector, Matrix

SOURCE_SCENE = r"C:\Users\uur\Desktop\modeller\01_GARAJ_OFIS_TESLIMI\01_Blender\Sahneler\Garaj_Tam_Sahne.blend"
ART_ROOT = r"C:\Users\uur\oyunnn\Art"
UNITY_MODELS = r"C:\Users\uur\oyunnn_staging\Assets\_Project\Art\Models"

# Palette (name -> base rgb, roughness, metallic). Existing scene materials are appended by
# name when available so new models share the exact look of the delivered scene.
PALETTE = {
    "Charcoal iron": ((0.10, 0.115, 0.12), 0.58, 0.38),
    "D19 satin galvanized steel": ((0.39, 0.42, 0.41), 0.55, 0.58),
    "Satin steel edge": ((0.58, 0.59, 0.54), 0.47, 0.48),
    "Satin machined steel": ((0.60, 0.65, 0.67), 0.32, 0.78),
    "Dark plated steel": ((0.19, 0.23, 0.26), 0.48, 0.65),
    "Charcoal painted steel": ((0.022, 0.027, 0.033), 0.68, 0.20),
    "09 Powder-coated anthracite frame": ((0.078, 0.105, 0.12), 0.49, 0.41),
    "Workshop red enamel": ((0.54, 0.12, 0.08), 0.55, 0.18),
    "Jack red enamel": ((0.48, 0.012, 0.009), 0.42, 0.28),
    "Safety yellow": ((0.95, 0.50, 0.025), 0.48, 0.18),
    "D07 worn safety yellow": ((0.89, 0.62, 0.16), 0.72, 0.0),
    "Arm highlight yellow": ((1.0, 0.68, 0.055), 0.46, 0.16),
    "Safety orange": ((0.95, 0.33, 0.05), 0.72, 0.0),
    "01 Warm ivory paint": ((0.66, 0.62, 0.53), 0.62, 0.18),
    "02 Sunlit ivory": ((0.78, 0.73, 0.62), 0.65, 0.13),
    "05 Desaturated blue stripe": ((0.085, 0.245, 0.345), 0.69, 0.08),
    "D02 muted painted blue": ((0.16, 0.31, 0.41), 0.72, 0.0),
    "Lift blue enamel": ((0.025, 0.12, 0.40), 0.40, 0.30),
    "Filing green enamel": ((0.23, 0.33, 0.25), 0.61, 0.22),
    "Olive plastic": ((0.28, 0.33, 0.18), 0.72, 0.0),
    "Dusty teal plastic": ((0.22, 0.39, 0.40), 0.72, 0.0),
    "Cream plastic": ((0.79, 0.72, 0.58), 0.72, 0.0),
    "Charcoal ABS": ((0.034, 0.039, 0.042), 0.70, 0.0),
    "Tire rubber": ((0.055, 0.058, 0.055), 0.94, 0.0),
    "Black rubber": ((0.009, 0.013, 0.015), 0.90, 0.0),
    "15 Charcoal rubber": ((0.033, 0.044, 0.046), 0.79, 0.03),
    "D18 warm workshop timber": ((0.43, 0.28, 0.17), 0.72, 0.0),
    "D18 timber edge": ((0.32, 0.19, 0.105), 0.72, 0.0),
    "01 Honeyed workshop oak": ((0.55, 0.30, 0.115), 0.72, 0.0),
    "D09 kraft cardboard": ((0.57, 0.39, 0.22), 0.72, 0.0),
    "D09 pale cardboard": ((0.68, 0.49, 0.28), 0.72, 0.0),
    "D09 packing tape": ((0.70, 0.57, 0.37), 0.72, 0.0),
    "D12 cream paper": ((0.85, 0.79, 0.65), 0.72, 0.0),
    "D12 ink gray": ((0.25, 0.32, 0.33), 0.72, 0.0),
    "Paper edge": ((0.86, 0.83, 0.76), 0.90, 0.0),
    "Cream glazed ceramic": ((0.86, 0.84, 0.78), 0.24, 0.0),
    "Control white": ((0.93, 0.91, 0.85), 0.55, 0.02),
    "Control button red": ((0.65, 0.002, 0.02), 0.38, 0.08),
    "Power indicator green": ((0.03, 0.73, 0.18), 0.35, 0.0),
    "Warning lamp amber": ((1.0, 0.38, 0.015), 0.35, 0.0),
    "Warm fixture lens": ((1.0, 0.77, 0.43), 0.24, 0.0),
    "Cool smoky glazing": ((0.19, 0.30, 0.31), 0.20, 0.10),
    "13 Dark smoky glass": ((0.075, 0.085, 0.085), 0.26, 0.10),
    "Black glass inset": ((0.029, 0.046, 0.053), 0.12, 0.18),
    "16 Aged brass fittings": ((0.60, 0.39, 0.12), 0.35, 0.67),
    "Chair shell textile": ((0.029, 0.033, 0.036), 0.91, 0.0),
    "18 Dark taupe cloth": ((0.27, 0.26, 0.22), 0.90, 0.0),
    "D01 warm aged plaster": ((0.67, 0.62, 0.53), 0.72, 0.0),
    "D01 plaster highlight": ((0.76, 0.71, 0.62), 0.72, 0.0),
    "D05 concrete variation": ((0.53, 0.49, 0.43), 0.72, 0.0),
    "D06 oily concrete": ((0.31, 0.30, 0.28), 0.72, 0.0),
    "Deep green foliage": ((0.17, 0.27, 0.15), 0.72, 0.0),
    "Olive foliage": ((0.26, 0.36, 0.15), 0.72, 0.0),
    "Warm soil": ((0.44, 0.32, 0.20), 0.72, 0.0),
    "Terracotta rim": ((0.57, 0.32, 0.18), 0.82, 0.0),
    "Coffee surface": ((0.13, 0.065, 0.028), 0.30, 0.0),
}

_MATERIAL_LOG = {}


def reset_scene():
    """Empty the current file (keeps nothing)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    s = bpy.context.scene
    s.unit_settings.system = 'METRIC'
    s.unit_settings.scale_length = 1.0


def _append_material(name):
    if not os.path.exists(SOURCE_SCENE):
        return None
    try:
        with bpy.data.libraries.load(SOURCE_SCENE, link=False) as (src, dst):
            if name in src.materials:
                dst.materials = [name]
            else:
                return None
        return bpy.data.materials.get(name)
    except Exception as exc:  # pragma: no cover - defensive
        print("append failed", name, exc)
        return None


def mat(name, color=None, rough=0.6, metal=0.0, emission=None, emission_strength=0.0, alpha=1.0):
    """Return a material. Existing palette names are appended from the delivered scene so the
    look is identical; new names (prefix them with 'F ') are created as Principled BSDF."""
    m = bpy.data.materials.get(name)
    if m is None and color is None:
        m = _append_material(name)
        if m is None and name in PALETTE:
            c, r, mt = PALETTE[name]
            return mat(name, c, r, mt)
    if m is None:
        if color is None:
            color = (0.5, 0.5, 0.5)
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        if emission is not None:
            bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
            bsdf.inputs["Emission Strength"].default_value = emission_strength
        if alpha < 1.0:
            bsdf.inputs["Alpha"].default_value = alpha
            m.blend_method = 'BLEND' if hasattr(m, "blend_method") else None
        m.diffuse_color = (*color, alpha)
    _log_material(m)
    return m


UNITY_TEXTURES = r"C:\Users\uur\oyunnn_staging\Assets\_Project\Art\Textures"


def _texture_for(m, bsdf):
    """Absolute path of the image feeding Base Color (packed images are written to the Unity
    texture folder). None when the colour is a plain value."""
    tex = next((n for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image), None)
    if tex is None:
        return None
    img = tex.image
    safe = "".join(ch if ch.isalnum() or ch in "-_" else "_" for ch in img.name.rsplit(".", 1)[0])
    out = os.path.join(UNITY_TEXTURES, safe + ".png")
    os.makedirs(UNITY_TEXTURES, exist_ok=True)
    if not os.path.exists(out):
        try:
            if img.packed_file is not None or not os.path.exists(bpy.path.abspath(img.filepath)):
                img.filepath_raw = out
                img.file_format = 'PNG'
                img.save()
            else:
                import shutil
                shutil.copyfile(bpy.path.abspath(img.filepath), out)
        except Exception as exc:
            print("texture export failed", img.name, exc)
            return None
    return out


def _log_material(m):
    info = {"base": [0.5, 0.5, 0.5], "rough": 0.6, "metal": 0.0, "emit": None, "emit_str": 0.0, "alpha": 1.0, "tex": None}
    if m.node_tree:
        b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if b:
            info["base"] = list(b.inputs["Base Color"].default_value)[:3]
            info["rough"] = float(b.inputs["Roughness"].default_value)
            info["metal"] = float(b.inputs["Metallic"].default_value)
            info["emit"] = list(b.inputs["Emission Color"].default_value)[:3]
            info["emit_str"] = float(b.inputs["Emission Strength"].default_value)
            info["alpha"] = float(b.inputs["Alpha"].default_value)
            if b.inputs["Base Color"].is_linked:
                info["tex"] = _texture_for(m, b)
    _MATERIAL_LOG[m.name] = info


# ---------------------------------------------------------------- geometry helpers

def _finish(obj, material=None, bevel=0.0, segments=2, smooth_angle=35.0, weighted=True):
    if material is not None:
        if isinstance(material, str):
            material = mat(material)
        obj.data.materials.clear()
        obj.data.materials.append(material)
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", 'BEVEL')
        mod.width = bevel
        mod.segments = segments
        mod.limit_method = 'ANGLE'
        mod.angle_limit = math.radians(40)
        mod.harden_normals = True
        mod.miter_outer = 'MITER_ARC'
    shade(obj, smooth_angle, weighted=weighted and bevel <= 0)
    if bevel > 0:
        apply_modifiers(obj)
    return obj


def apply_modifiers(obj):
    bpy.context.view_layer.objects.active = obj
    for o in bpy.context.selected_objects:
        o.select_set(False)
    obj.select_set(True)
    for mod in list(obj.modifiers):
        try:
            bpy.ops.object.modifier_apply(modifier=mod.name)
        except Exception as exc:
            print("modifier apply failed", obj.name, mod.name, exc)
            obj.modifiers.remove(mod)


def shade(obj, angle=35.0, weighted=True):
    if obj.type != 'MESH':
        return
    mesh = obj.data
    for p in mesh.polygons:
        p.use_smooth = True
    # Sharp edges above angle -> crisp mid-poly silhouettes with soft bevel highlights.
    bm = bmesh.new()
    bm.from_mesh(mesh)
    lim = math.radians(angle)
    for e in bm.edges:
        if len(e.link_faces) == 2:
            e.smooth = e.calc_face_angle(0.0) < lim
        else:
            e.smooth = False
    bm.to_mesh(mesh)
    bm.free()
    if weighted:
        mod = obj.modifiers.new("WN", 'WEIGHTED_NORMAL')
        mod.keep_sharp = True
        apply_modifiers(obj)


def _new_obj(name, mesh):
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def box(name, size, loc=(0, 0, 0), material=None, bevel=0.01, segments=2, rot=(0, 0, 0)):
    """Axis aligned box; loc is the box centre."""
    sx, sy, sz = size
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * sx, v.co.y * sy, v.co.z * sz))
    bm.to_mesh(mesh)
    bm.free()
    obj = _new_obj(name, mesh)
    obj.location = loc
    obj.rotation_euler = rot
    return _finish(obj, material, bevel=min(bevel, min(size) * 0.45) if bevel else 0, segments=segments)


def cylinder(name, radius, depth, loc=(0, 0, 0), material=None, verts=20, bevel=0.005, axis='Z', rot=None, cap=True):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=cap, cap_tris=False, segments=verts, radius1=radius, radius2=radius, depth=depth)
    bm.to_mesh(mesh)
    bm.free()
    obj = _new_obj(name, mesh)
    obj.location = loc
    if rot is not None:
        obj.rotation_euler = rot
    elif axis == 'X':
        obj.rotation_euler = (0, math.radians(90), 0)
    elif axis == 'Y':
        obj.rotation_euler = (math.radians(90), 0, 0)
    return _finish(obj, material, bevel=min(bevel, radius * 0.4, depth * 0.4) if bevel else 0)


def cone(name, r1, r2, depth, loc=(0, 0, 0), material=None, verts=20, bevel=0.004, rot=(0, 0, 0)):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts, radius1=r1, radius2=r2, depth=depth)
    bm.to_mesh(mesh)
    bm.free()
    obj = _new_obj(name, mesh)
    obj.location = loc
    obj.rotation_euler = rot
    return _finish(obj, material, bevel=bevel)


def torus(name, major, minor, loc=(0, 0, 0), material=None, major_seg=24, minor_seg=8, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=major_seg,
                                     minor_segments=minor_seg, location=loc, rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    return _finish(obj, material, bevel=0)


def uv_sphere(name, radius, loc=(0, 0, 0), material=None, seg=16, rings=10, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, segments=seg, ring_count=rings, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    apply_transform(obj)
    return _finish(obj, material, bevel=0, smooth_angle=60)


def profile_extrude(name, points_xz, depth, loc=(0, 0, 0), material=None, bevel=0.006, axis='Y'):
    """Extrude a closed 2D outline (list of (x, z)) along Y (default) by depth, centred."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    vs = [bm.verts.new((x, -depth / 2, z)) for x, z in points_xz]
    face = bm.faces.new(vs)
    ret = bmesh.ops.extrude_face_region(bm, geom=[face])
    nv = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=nv, vec=(0, depth, 0))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = _new_obj(name, mesh)
    obj.location = loc
    if axis == 'X':
        obj.rotation_euler = (0, 0, math.radians(90))
    return _finish(obj, material, bevel=bevel)


def tube_path(name, points, radius, material=None, verts=10, closed=False):
    """Bevelled curve along points (cables, hoses, rails) converted to a mesh."""
    curve = bpy.data.curves.new(name, 'CURVE')
    curve.dimensions = '3D'
    curve.bevel_depth = radius
    curve.bevel_resolution = max(1, verts // 4)
    curve.resolution_u = 6
    sp = curve.splines.new('POLY' if len(points) < 3 else 'BEZIER')
    if sp.type == 'BEZIER':
        sp.bezier_points.add(len(points) - 1)
        for bp, p in zip(sp.bezier_points, points):
            bp.co = p
            bp.handle_left_type = bp.handle_right_type = 'AUTO'
    else:
        sp.points.add(len(points) - 1)
        for pt, p in zip(sp.points, points):
            pt.co = (*p, 1.0)
    sp.use_cyclic_u = closed
    obj = _new_obj(name + "_curve", curve)
    bpy.context.view_layer.objects.active = obj
    for o in bpy.context.selected_objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.ops.object.convert(target='MESH')
    obj = bpy.context.active_object
    obj.name = name
    return _finish(obj, material, bevel=0, smooth_angle=70)


def empty(name, loc=(0, 0, 0), rot=(0, 0, 0), parent=None, size=0.1):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'ARROWS'
    e.empty_display_size = size
    bpy.context.scene.collection.objects.link(e)
    e.location = loc
    e.rotation_euler = rot
    if parent is not None:
        set_parent(e, parent)
    return e


def collider(name, size, loc, parent=None):
    """COL_ box (exported, turned into a BoxCollider by the Unity importer)."""
    o = box(name if name.startswith("COL_") else "COL_" + name, size, loc, material=None, bevel=0)
    o.display_type = 'WIRE'
    o.hide_render = True
    if parent is not None:
        set_parent(o, parent)
    return o


def apply_transform(obj, loc=False, rot=True, scale=True):
    bpy.context.view_layer.objects.active = obj
    for o in bpy.context.selected_objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=loc, rotation=rot, scale=scale)


def join(objs, name):
    objs = [o for o in objs if o is not None]
    bpy.context.view_layer.objects.active = objs[0]
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    return o


def set_parent(child, parent, keep_world=True):
    mw = child.matrix_world.copy()
    child.parent = parent
    if keep_world:
        child.matrix_world = mw


def set_origin(obj, world_point):
    """Move the object origin (pivot) to world_point without moving the geometry."""
    mw = obj.matrix_world.copy()
    local = mw.inverted() @ Vector(world_point)
    obj.data.transform(Matrix.Translation(-local))
    obj.matrix_world = mw @ Matrix.Translation(local)


def root(name, loc=(0, 0, 0)):
    """Root empty of an exported asset; every part is parented under it."""
    return empty(name, loc, size=0.3)


def parent_all(root_obj, objs):
    for o in objs:
        if o is not None and o is not root_obj and o.parent is None:
            set_parent(o, root_obj)


def tri_count(objs):
    n = 0
    for o in objs:
        if o.type == 'MESH':
            n += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return n


def descendants(obj):
    out = [obj]
    for c in obj.children:
        out.extend(descendants(c))
    return out


# ---------------------------------------------------------------- export / preview

def export_fbx(root_obj, rel_path, anim=False):
    """Export root_obj + descendants to <UNITY_MODELS>/<rel_path>.fbx and write a materials json
    next to it. Unity(x,y,z) = Blender(x,z,y)."""
    path = os.path.join(UNITY_MODELS, rel_path + ".fbx")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for o in bpy.context.selected_objects:
        o.select_set(False)
    objs = descendants(root_obj)
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = root_obj
    kwargs = dict(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                  axis_forward='Z', axis_up='Y', bake_space_transform=True, object_types={'EMPTY', 'MESH', 'ARMATURE'},
                  use_mesh_modifiers=True, mesh_smooth_type='FACE', use_tspace=False, add_leaf_bones=False,
                  path_mode='COPY', embed_textures=False, use_custom_props=True)
    if anim:
        kwargs.update(bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                      bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    else:
        kwargs.update(bake_anim=False)
    bpy.ops.export_scene.fbx(**kwargs)
    mats = {}
    for o in objs:
        if o.type == 'MESH':
            for s in o.material_slots:
                if s.material:
                    _log_material(s.material)
                    mats[s.material.name] = _MATERIAL_LOG[s.material.name]
    with open(path[:-4] + ".materials.json", "w", encoding="utf-8") as f:
        json.dump(mats, f, indent=1)
    print("EXPORTED", path, "tris", tri_count(objs))
    return path


def save_blend(rel_path):
    path = os.path.join(ART_ROOT, "Blender", "Models", rel_path + ".blend")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)
    return path


def render_preview(root_obj, rel_path, res=(900, 700), angle=(-35.0, 28.0), extra_views=False):
    """Eevee preview with soft three point lighting. Writes Art/Previews/<rel_path>.png
    (and _back.png when extra_views)."""
    scene = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else ('BLENDER_EEVEE' if 'BLENDER_EEVEE' in engines else 'CYCLES')
    if scene.render.engine == 'CYCLES':
        scene.cycles.samples = 32
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.film_transparent = False
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.55, 0.6, 0.62, 1)
        bg.inputs[1].default_value = 0.55
    objs = [o for o in descendants(root_obj) if o.type == 'MESH' and not o.name.startswith("COL_")]
    mn = Vector((1e9, 1e9, 1e9)); mx = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            p = o.matrix_world @ Vector(c)
            mn = Vector(map(min, mn, p)); mx = Vector(map(max, mx, p))
    centre = (mn + mx) / 2
    radius = max((mx - mn).length / 2, 0.05)
    # ground plane
    gp = bpy.data.objects.get("_preview_ground")
    if gp is None:
        bpy.ops.mesh.primitive_plane_add(size=1)
        gp = bpy.context.active_object
        gp.name = "_preview_ground"
        gp.data.materials.append(mat("F Preview ground", (0.42, 0.41, 0.38), 0.9))
    gp.scale = (radius * 8, radius * 8, 1)
    gp.location = (centre.x, centre.y, mn.z - 0.001)
    cam_data = bpy.data.cameras.new("_preview_cam")
    cam = bpy.data.objects.new("_preview_cam", cam_data)
    scene.collection.objects.link(cam)
    cam_data.lens = 50
    scene.camera = cam
    lights = []
    try:
        scene.view_settings.view_transform = 'AgX'
        scene.view_settings.look = 'AgX - Base Contrast'
    except Exception:
        pass
    for nm, loc, energy in (("_key", (1.0, -1.2, 1.6), 380), ("_fill", (-1.4, -0.6, 0.8), 120), ("_rim", (0.2, 1.6, 1.4), 220)):
        ld = bpy.data.lights.new(nm, 'AREA')
        ld.energy = energy * radius * radius
        ld.size = radius * 1.5
        lo = bpy.data.objects.new(nm, ld)
        scene.collection.objects.link(lo)
        lo.location = centre + Vector(loc) * radius * 3
        lo.rotation_euler = (centre - lo.location).to_track_quat('-Z', 'Y').to_euler()
        lights.append(lo)
    outs = []
    views = [("", angle)] + ([("_back", (angle[0] + 180, angle[1]))] if extra_views else [])
    for suffix, (az, el) in views:
        azr, elr = math.radians(az), math.radians(el)
        dist = radius / math.tan(math.radians(18)) * 1.15
        direction = Vector((math.sin(azr) * math.cos(elr), -math.cos(azr) * math.cos(elr), math.sin(elr)))
        cam.location = centre + direction * dist
        cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
        out = os.path.join(ART_ROOT, "Previews", rel_path + suffix + ".png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        scene.render.filepath = out
        bpy.ops.render.render(write_still=True)
        outs.append(out)
    for lo in lights:
        bpy.data.objects.remove(lo, do_unlink=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    bpy.data.objects.remove(gp, do_unlink=True)
    return outs
