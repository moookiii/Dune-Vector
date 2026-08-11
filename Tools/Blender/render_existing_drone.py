import bpy
import math
import os
from mathutils import Vector


OUTPUT_DIR = r"C:\Dune Vector URP\BlenderRenders"
OUTPUT_BLEND = os.path.join(OUTPUT_DIR, "existing_drone_studio.blend")
OUTPUT_RENDER = os.path.join(OUTPUT_DIR, "existing_drone_reference_style.png")
SETUP_COLLECTION = "Reference_Render_Setup"


def look_at(obj, point):
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat("-Z", "Y").to_euler()


def material(name, color, metallic=0.0, roughness=0.45, emission=None, emission_strength=0.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    return mat


def link_to_setup(obj, setup):
    for coll in list(obj.users_collection):
        coll.objects.unlink(obj)
    setup.objects.link(obj)


def area_light(setup, name, location, color, energy, size, target):
    data = bpy.data.lights.new(name, "AREA")
    data.color = color
    data.energy = energy
    data.shape = "DISK"
    data.size = size
    obj = bpy.data.objects.new(name, data)
    setup.objects.link(obj)
    obj.location = location
    look_at(obj, target)
    return obj


def point_light(setup, name, location, color, energy, radius):
    data = bpy.data.lights.new(name, "POINT")
    data.color = color
    data.energy = energy
    data.shadow_soft_size = radius
    obj = bpy.data.objects.new(name, data)
    setup.objects.link(obj)
    obj.location = location
    return obj


# Remove only a previous generated studio setup, preserving the imported drone.
old_setup = bpy.data.collections.get(SETUP_COLLECTION)
if old_setup:
    for obj in list(old_setup.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(old_setup)

setup = bpy.data.collections.new(SETUP_COLLECTION)
bpy.context.scene.collection.children.link(setup)

# Determine the imported model's exact world bounds.
drone_meshes = [
    obj for obj in bpy.context.scene.objects
    if obj.type == "MESH" and SETUP_COLLECTION not in {c.name for c in obj.users_collection}
]
points = [obj.matrix_world @ Vector(corner) for obj in drone_meshes for corner in obj.bound_box]
mins = Vector(tuple(min(p[i] for p in points) for i in range(3)))
maxs = Vector(tuple(max(p[i] for p in points) for i in range(3)))
center = (mins + maxs) * 0.5
floor_z = mins.z - 0.045
model_width = maxs.x - mins.x
model_depth = maxs.y - mins.y

# Infinite-looking dark navy studio floor.
ground_mat = material("Reference Ground", (0.006, 0.012, 0.018), metallic=0.0, roughness=0.42)
bpy.ops.mesh.primitive_plane_add(size=40.0, location=(center.x, center.y, floor_z))
ground = bpy.context.object
ground.name = "Reference dark navy floor"
ground.data.materials.append(ground_mat)
link_to_setup(ground, setup)

# Match the reference's warm upper-left key and cool blue right-hand rim.
target = Vector((center.x, center.y, mins.z + (maxs.z - mins.z) * 0.48))
area_light(setup, "Warm ivory key", (-4.6, -5.8, 8.3), (1.0, 0.76, 0.56), 1750, 4.0, target)
area_light(setup, "Cool cyan fill", (5.5, -2.0, 4.2), (0.20, 0.62, 1.0), 1120, 4.5, target)
area_light(setup, "Blue rear rim", (2.5, 6.0, 4.0), (0.0, 0.44, 1.0), 820, 3.5, target)
area_light(setup, "Soft overhead", (-1.8, -1.0, 9.0), (0.72, 0.82, 1.0), 1350, 3.8, target)

# Three-quarter elevated orthographic framing, with the nose toward camera.
cam_data = bpy.data.cameras.new("Reference Camera")
camera = bpy.data.objects.new("Reference Camera", cam_data)
setup.objects.link(camera)
camera.location = (center.x + 6.9, center.y - 8.5, center.z + 5.5)
look_at(camera, target)
cam_data.type = "ORTHO"
cam_data.ortho_scale = max(7.00, model_width * 1.28)
cam_data.lens = 55
bpy.context.scene.camera = camera

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 768
scene.render.resolution_y = 576
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGB"
scene.render.image_settings.color_depth = "8"
scene.render.filepath = OUTPUT_RENDER
scene.render.film_transparent = False
scene.render.image_settings.color_depth = "8"
scene.render.engine = "BLENDER_EEVEE"
scene.render.image_settings.file_format = "PNG"

# High-quality contact shadows and antialiasing.
scene.render.engine = "BLENDER_EEVEE"
scene.render.use_file_extension = True
scene.render.image_settings.color_mode = "RGB"
scene.render.resolution_percentage = 100

scene.world.use_nodes = True
background = scene.world.node_tree.nodes.get("Background")
background.inputs["Color"].default_value = (0.0015, 0.004, 0.009, 1.0)
background.inputs["Strength"].default_value = 0.13
scene.view_settings.look = "AgX - Medium High Contrast"
scene.view_settings.exposure = 0.65

os.makedirs(OUTPUT_DIR, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUTPUT_BLEND)
bpy.ops.render.render(write_still=True)

result = {
    "blend": OUTPUT_BLEND,
    "render": OUTPUT_RENDER,
    "bounds": {"min": list(mins), "max": list(maxs)},
    "drone_meshes": len(drone_meshes),
    "camera_ortho_scale": cam_data.ortho_scale,
}
