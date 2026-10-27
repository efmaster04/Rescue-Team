# RescueCrew renk varyantlarını ayrı FBX dosyaları olarak dışa aktarır.
# Kullanım:
#   blender -b ArtSource/Characters/RescueCrew/SK_RescueCrew.blend --python-exit-code 1 \
#       -P tools/blender/export_variants.py -- --out Export/RescueCrew
import sys
from pathlib import Path

import bpy

VARIANTS = ('Orange', 'Yellow', 'Blue', 'Green')
MESHES = ('SK_RescueCrew_Body', 'SK_RescueCrew_Helmet', 'SK_RescueCrew_Face')
RIG = 'RescueCrew_Rig'


def swap_variant(objects, variant):
    # Kask ve turuncu reflektör slotlarını varyant materyaline çevirir; eski atamaları döndürür.
    changed = []
    for obj in objects:
        for slot in obj.material_slots:
            name = slot.material.name if slot.material else ''
            target = None
            if name == 'M_Helmet':
                target = f'M_Helmet_{variant}'
            elif name == 'M_Reflective_Orange':
                target = f'M_Reflective_{variant}'
            if target:
                changed.append((slot, slot.material))
                slot.material = bpy.data.materials[target]
    return changed


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = Path(argv[argv.index('--out') + 1] if '--out' in argv else 'Export/RescueCrew').resolve()
    out.mkdir(parents=True, exist_ok=True)
    rig = bpy.data.objects[RIG]
    objects = [bpy.data.objects[n] for n in MESHES]
    for variant in VARIANTS:
        changed = swap_variant(objects, variant) if variant != 'Orange' else []
        try:
            bpy.ops.object.select_all(action='DESELECT')
            for obj in [rig] + objects:
                obj.select_set(True)
            bpy.context.view_layer.objects.active = rig
            path = out / f'SK_RescueCrew_{variant}.fbx'
            # Ayarlar build_rescue_crew.py export() ile aynı; lineer Base Color olduğu gibi yazılır
            bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
                object_types={'ARMATURE', 'MESH'}, apply_scale_options='FBX_SCALE_ALL',
                axis_forward='-Z', axis_up='Y', add_leaf_bones=False, use_armature_deform_only=True,
                bake_anim=False, mesh_smooth_type='FACE', use_mesh_modifiers=False,
                use_custom_props=True, check_existing=False)
            print('EXPORTED', path)
        finally:
            for slot, material in changed:
                slot.material = material


if __name__ == '__main__':
    main()
