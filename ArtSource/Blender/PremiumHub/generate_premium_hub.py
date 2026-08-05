import bpy
import math
import os
from mathutils import Vector


ROOT = r"C:\Dune Vector URP"
SOURCE_DIR = os.path.join(ROOT, "ArtSource", "Blender", "PremiumHub")
ASSET_DIR = os.path.join(ROOT, "Assets", "DuneVector", "Resources", "PremiumHub")
TEXTURE_DIR = os.path.join(ASSET_DIR, "Textures")
MODEL_PATH = os.path.join(ASSET_DIR, "PremiumHub.fbx")
BLEND_PATH = os.path.join(SOURCE_DIR, "PremiumHub.blend")
PREVIEW_PATH = os.path.join(SOURCE_DIR, "PremiumHubPreview.png")

for path in (SOURCE_DIR, ASSET_DIR, TEXTURE_DIR):
    os.makedirs(path, exist_ok=True)


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def placeholder_material(name, color, metallic=0.0, roughness=0.5, emission=None):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1.0)
    mat.use_nodes = True
    principled = mat.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = (*color, 1.0)
    principled.inputs["Metallic"].default_value = metallic
    principled.inputs["Roughness"].default_value = roughness
    if emission:
        principled.inputs["Emission Color"].default_value = (*emission, 1.0)
        principled.inputs["Emission Strength"].default_value = 5.0
    return mat


def bevel_and_smooth(obj, width=0.12, segments=3, smooth=False):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bevel = obj.modifiers.new("Premium edge bevel", 'BEVEL')
    bevel.width = width
    bevel.segments = segments
    bevel.limit_method = 'ANGLE'
    if smooth:
        for poly in obj.data.polygons:
            poly.use_smooth = True
    obj.select_set(False)
    return obj


def cube(name, location, scale, material, rotation=(0.0, 0.0, 0.0), bevel=0.12):
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.scale = (scale[0] * 0.5, scale[1] * 0.5, scale[2] * 0.5)
    obj.data.materials.append(material)
    bevel_and_smooth(obj, bevel, 3)
    return obj


def cylinder(name, radius, depth, z, material, vertices=96, bevel=0.12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=(0, 0, z))
    obj = bpy.context.object
    obj.name = name
    obj.data.materials.append(material)
    bevel_and_smooth(obj, bevel, 3, True)
    return obj


def torus(name, major_radius, minor_radius, z, material, major_segments=128, minor_segments=12):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius,
        minor_radius=minor_radius,
        major_segments=major_segments,
        minor_segments=minor_segments,
        location=(0, 0, z))
    obj = bpy.context.object
    obj.name = name
    obj.data.materials.append(material)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def annular_prism(name, inner_radius, outer_radius, bottom, top, material, segments=128):
    verts = []
    faces = []
    for z in (bottom, top):
        for radius in (inner_radius, outer_radius):
            for i in range(segments):
                angle = (2.0 * math.pi * i) / segments
                verts.append((math.cos(angle) * radius, math.sin(angle) * radius, z))
    def idx(layer, ring, i):
        return layer * segments * 2 + ring * segments + (i % segments)
    for i in range(segments):
        n = (i + 1) % segments
        faces.append((idx(1, 0, i), idx(1, 1, i), idx(1, 1, n), idx(1, 0, n)))
        faces.append((idx(0, 0, n), idx(0, 1, n), idx(0, 1, i), idx(0, 0, i)))
        faces.append((idx(0, 1, i), idx(0, 1, n), idx(1, 1, n), idx(1, 1, i)))
        faces.append((idx(0, 0, n), idx(0, 0, i), idx(1, 0, i), idx(1, 0, n)))
    mesh = bpy.data.meshes.new(name + " Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material)
    bevel_and_smooth(obj, 0.10, 3, True)
    return obj


def beam_between(name, start, end, width, depth, material, bevel=0.10):
    start_v = Vector(start)
    end_v = Vector(end)
    delta = end_v - start_v
    mid = (start_v + end_v) * 0.5
    obj = cube(name, mid, (width, depth, delta.length), material, bevel=bevel)
    obj.rotation_mode = 'QUATERNION'
    obj.rotation_quaternion = delta.to_track_quat('Z', 'Y')
    return obj


def radial_box(name, radius, angle_deg, tangential, radial, height, z, material, bevel=0.10):
    angle = math.radians(angle_deg)
    loc = (math.cos(angle) * radius, math.sin(angle) * radius, z)
    return cube(name, loc, (radial, tangential, height), material,
                rotation=(0, 0, angle), bevel=bevel)


def create_tiling_textures():
    import numpy as np
    size = 2048
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    u = xx / size
    v = yy / size

    def save_rgba(name, rgba, colorspace='sRGB'):
        image = bpy.data.images.new(name, width=size, height=size, alpha=True, float_buffer=False)
        image.colorspace_settings.name = colorspace
        image.pixels.foreach_set(np.ascontiguousarray(rgba.astype(np.float32)).ravel())
        image.filepath_raw = os.path.join(TEXTURE_DIR, name + ".png")
        image.file_format = 'PNG'
        image.save()
        bpy.data.images.remove(image)

    noise = (
        0.46 * np.sin(2 * math.pi * (u * 3 + v * 2 + 0.13)) +
        0.28 * np.sin(2 * math.pi * (u * 11 - v * 7 + 0.41)) +
        0.16 * np.sin(2 * math.pi * (u * 37 + v * 19 + 0.72)) +
        0.10 * np.sin(2 * math.pi * (u * 103 - v * 5 + 0.31)))
    brush = 0.5 + 0.5 * np.sin(2 * math.pi * (v * 181 + 0.12 * np.sin(u * math.pi * 8)))
    metal_h = noise * 0.55 + (brush - 0.5) * 0.20
    wear = np.clip((noise + 1.25) / 2.5, 0, 1)
    albedo = np.zeros((size, size, 4), dtype=np.float32)
    albedo[..., 0] = 0.025 + wear * 0.040
    albedo[..., 1] = 0.050 + wear * 0.055
    albedo[..., 2] = 0.065 + wear * 0.065
    albedo[..., 3] = 1
    save_rgba("PremiumHub_DarkMetal_Albedo", albedo)

    gy, gx = np.gradient(metal_h)
    normal = np.dstack((-gx * 3.1, -gy * 3.1, np.ones_like(gx)))
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    normal_rgba = np.dstack((normal * 0.5 + 0.5, np.ones_like(gx)))
    save_rgba("PremiumHub_DarkMetal_Normal", normal_rgba, 'Non-Color')

    mask = np.zeros((size, size, 4), dtype=np.float32)
    mask[..., 0] = 0.92
    mask[..., 1] = 0.82 + wear * 0.14
    mask[..., 2] = 0.0
    mask[..., 3] = 0.58 + brush * 0.18
    save_rgba("PremiumHub_DarkMetal_Mask", mask, 'Non-Color')

    panel_x = np.minimum(np.mod(u * 8, 1.0), 1.0 - np.mod(u * 8, 1.0))
    panel_y = np.minimum(np.mod(v * 8, 1.0), 1.0 - np.mod(v * 8, 1.0))
    seam = np.clip((0.035 - np.minimum(panel_x, panel_y)) / 0.035, 0, 1)
    fleck = 0.5 + 0.5 * np.sin(2 * math.pi * (u * 53 + v * 67 + noise * 0.25))
    deck_h = noise * 0.35 - seam * 0.9 + fleck * 0.08
    deck = np.zeros((size, size, 4), dtype=np.float32)
    deck[..., 0] = 0.055 + wear * 0.040 - seam * 0.025
    deck[..., 1] = 0.065 + wear * 0.045 - seam * 0.030
    deck[..., 2] = 0.072 + wear * 0.050 - seam * 0.033
    deck[..., 3] = 1
    save_rgba("PremiumHub_Deck_Albedo", deck)

    dgy, dgx = np.gradient(deck_h)
    dnormal = np.dstack((-dgx * 4.0, -dgy * 4.0, np.ones_like(dgx)))
    dnormal /= np.linalg.norm(dnormal, axis=2, keepdims=True)
    dnormal_rgba = np.dstack((dnormal * 0.5 + 0.5, np.ones_like(dgx)))
    save_rgba("PremiumHub_Deck_Normal", dnormal_rgba, 'Non-Color')

    dmask = np.zeros((size, size, 4), dtype=np.float32)
    dmask[..., 0] = 0.34 + fleck * 0.12
    dmask[..., 1] = 0.78 + wear * 0.18
    dmask[..., 2] = 0.0
    dmask[..., 3] = 0.42 + wear * 0.12
    save_rgba("PremiumHub_Deck_Mask", dmask, 'Non-Color')


def apply_uvs():
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH':
            continue
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        for other in bpy.context.selected_objects:
            if other != obj:
                other.select_set(False)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.015, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode='OBJECT')
        # A consistent 2 m texture repeat gives the Unity materials predictable scale.
        uv_layer = obj.data.uv_layers.active
        if uv_layer:
            for loop in uv_layer.data:
                loop.uv *= 0.5
        obj.select_set(False)


def build_hub():
    dark = placeholder_material("PremiumHub_DarkMetal", (0.025, 0.055, 0.072), 0.92, 0.30)
    bronze = placeholder_material("PremiumHub_Bronze", (0.28, 0.12, 0.035), 0.88, 0.27)
    deck = placeholder_material("PremiumHub_Deck", (0.075, 0.082, 0.09), 0.42, 0.55)
    cyan = placeholder_material("PremiumHub_EmissiveCyan", (0.01, 0.18, 0.25), 0.25, 0.20, (0.02, 1.8, 3.6))

    cylinder("Foundation_Skirt", 25.8, 1.55, 0.12, dark, 128, 0.24)
    annular_prism("Deck_Annulus", 18.65, 25.35, 0.60, 1.42, deck, 128)
    torus("Outer_Bronze_Crown", 24.95, 0.32, 1.43, bronze)
    torus("Inner_Energy_Lip", 18.75, 0.16, 1.52, cyan)
    torus("Lower_Energy_Seam", 25.35, 0.09, 0.30, cyan)

    for i in range(24):
        angle = i * 15.0
        mat = bronze if i % 2 == 0 else dark
        radial_box(f"Outer_Rim_Segment_{i+1:02d}", 25.15, angle, 2.15, 0.72, 0.62, 1.62, mat, 0.08)

    for i in range(6):
        angle = i * 60.0 + 30.0
        radial_box(f"Radial_Deck_Spine_{i+1:02d}", 21.65, angle, 2.2, 5.8, 0.36, 1.63, bronze, 0.08)
        radial_box(f"Radial_Energy_Inlay_{i+1:02d}", 21.65, angle, 0.22, 5.3, 0.11, 1.86, cyan, 0.035)

    for i in range(6):
        angle = math.radians(i * 60.0)
        tangent = Vector((-math.sin(angle), math.cos(angle), 0))
        outward = Vector((math.cos(angle), math.sin(angle), 0))
        base_center = outward * 23.1
        for side in (-1, 1):
            start = base_center + tangent * (side * 1.05) + Vector((0, 0, 1.55))
            end = outward * 25.0 + tangent * (side * 1.75) + Vector((0, 0, 8.9))
            beam_between(f"Aerie_Pylon_{i+1:02d}_{side:+d}", start, end, 0.66, 0.82, dark, 0.14)
            accent_start = start + outward * -0.02 + tangent * (-side * 0.01)
            accent_end = end + outward * -0.02 + tangent * (-side * 0.01)
            beam_between(f"Pylon_Bronze_Rib_{i+1:02d}_{side:+d}", accent_start, accent_end, 0.18, 0.90, bronze, 0.05)
        cap_center = outward * 25.0 + Vector((0, 0, 9.0))
        radial_box(f"Pylon_Cap_{i+1:02d}", 25.0, math.degrees(angle), 4.0, 0.88, 0.55, 9.0, dark, 0.10)
        radial_box(f"Pylon_Beacon_{i+1:02d}", 24.86, math.degrees(angle), 2.3, 0.16, 0.20, 9.38, cyan, 0.04)

    torus("Aerie_Halo_Dark", 19.9, 0.28, 8.65, dark, 128, 10)
    for i in range(12):
        angle = i * 30.0
        radial_box(f"Halo_Bronze_Clamp_{i+1:02d}", 19.9, angle, 0.72, 1.15, 0.68, 8.65, bronze, 0.08)
        radial_box(f"Halo_Energy_Node_{i+1:02d}", 19.9, angle + 15.0, 0.28, 0.92, 0.18, 8.93, cyan, 0.04)

    for i, angle in enumerate((0.0, 90.0, 270.0)):
        radial_box(f"Terminal_Dock_{i+1:02d}", 13.1, angle, 7.2, 3.2, 0.42, 1.64, deck, 0.14)
        radial_box(f"Terminal_Dock_Trim_{i+1:02d}", 14.55, angle, 7.5, 0.18, 0.12, 1.93, cyan, 0.04)

    # Orientation marker aimed toward the contract terminal (+Y in Blender / +Z in Unity).
    for x in (-0.42, 0.42):
        beam_between("Contract_Chevron", (x, 16.8, 1.92), (0, 17.7, 1.92), 0.16, 0.10, bronze, 0.04)

    apply_uvs()


def add_preview_scene():
    bpy.ops.object.camera_add(location=(47, -50, 38))
    camera = bpy.context.object
    camera.name = "Preview Camera"
    direction = Vector((0, 0, 3.0)) - camera.location
    camera.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = camera
    camera.data.lens = 52

    bpy.ops.object.light_add(type='AREA', location=(6, -12, 35))
    key = bpy.context.object
    key.name = "Preview Key"
    key.data.energy = 4200
    key.data.shape = 'DISK'
    key.data.size = 18
    key.data.color = (1.0, 0.50, 0.22)

    bpy.ops.object.light_add(type='AREA', location=(-24, 10, 18))
    fill = bpy.context.object
    fill.name = "Preview Fill"
    fill.data.energy = 2800
    fill.data.size = 16
    fill.data.color = (0.10, 0.55, 1.0)

    world = bpy.context.scene.world
    world.color = (0.004, 0.006, 0.012)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = PREVIEW_PATH
    scene.view_settings.look = 'AgX - Medium High Contrast'


def export_assets():
    render_hidden = []
    for obj in bpy.context.scene.objects:
        if obj.type in {'CAMERA', 'LIGHT'}:
            obj.hide_render = False
            render_hidden.append(obj)
    bpy.context.scene.render.filepath = PREVIEW_PATH
    bpy.ops.render.render(write_still=True)

    for obj in render_hidden:
        obj.hide_set(True)
        obj.hide_render = True
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.context.scene.objects:
        if obj.type == 'MESH':
            obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=MODEL_PATH,
        use_selection=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        add_leaf_bones=False,
        bake_anim=False,
        object_types={'MESH'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        path_mode='AUTO')
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)


clear_scene()
create_tiling_textures()
build_hub()
add_preview_scene()
export_assets()
print("PREMIUM_HUB_BUILD_COMPLETE")
