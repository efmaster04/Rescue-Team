"""Reference render from the supplied, read-only Blender scene."""
import math
import bpy
from mathutils import Vector

scene = bpy.context.scene
camera_data = bpy.data.cameras.new("ReferenceView")
camera = bpy.data.objects.new("ReferenceView", camera_data)
scene.collection.objects.link(camera)
camera.location = (7.9, -4.15, 2.6)
target = Vector((4.65, -0.20, 1.25))
camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
camera_data.type = "PERSP"
camera_data.angle = math.radians(88)
scene.camera = camera
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1280
scene.render.resolution_y = 720
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = r"C:\Users\uur\oyunnn\Documentation\OriginalBlenderView.png"
scene.render.film_transparent = False
bpy.ops.render.render(write_still=True)
