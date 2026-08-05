from pathlib import Path
import math

import bpy
from mathutils import Vector


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parents[2]
BLEND_PATH = SCRIPT_DIR / "RaiderBeacon_Master.blend"
MODEL_PATH = PROJECT_ROOT / "Assets/DuneVector/Resources/RaiderBeacon/RaiderBeacon.glb"
PREVIEW_PATH = PROJECT_ROOT / "Assets/DuneVector/Models/RaiderBeacon/RaiderBeacon_Preview.png"
TEXTURE_DIR = PROJECT_ROOT / "Assets/DuneVector/Resources/RaiderBeacon/Textures"


PALETTE = {
    "RB Black Armor": (0.018, 0.010, 0.020, 1.0),
    "RB Dark Metal": (0.080, 0.045, 0.060, 1.0),
    "RB Raider Red": (0.360, 0.018, 0.045, 1.0),
    "RB Signal Magenta": (1.000, 0.020, 0.300, 1.0),
    "RB Hot Pink": (1.000, 0.130, 0.470, 1.0),
    "RB Amber": (1.000, 0.190, 0.018, 1.0),
    "RB Bone": (0.720, 0.480, 0.230, 1.0),
}

TEXTURE_NAMES = {
    "RB Black Armor": "RB_BlackArmor.png",
    "RB Dark Metal": "RB_DarkMetal.png",
    "RB Raider Red": "RB_RaiderRed.png",
    "RB Signal Magenta": "RB_SignalMagenta.png",
    "RB Hot Pink": "RB_HotPink.png",
    "RB Amber": "RB_Amber.png",
    "RB Bone": "RB_Bone.png",
}


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            datablocks.remove(datablock)


def make_material(name, color, emission_strength=0.0, metallic=0.0, roughness=0.5):
    material = bpy.data.materials.new(name)
    material.diffuse_color = color
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission_strength > 0.0:
        bsdf.inputs["Emission Color"].default_value = color
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    return material


def make_palette():
    return {
        name: make_material(
            name,
            color,
            emission_strength=2.6 if "Signal" in name else (1.8 if "Hot Pink" in name or "Amber" in name else 0.0),
            metallic=0.72 if name in ("RB Black Armor", "RB Dark Metal") else 0.18,
            roughness=0.32 if name in ("RB Black Armor", "RB Dark Metal") else 0.48,
        )
        for name, color in PALETTE.items()
    }


def assign_material(obj, material):
    if obj.data and hasattr(obj.data, "materials"):
        obj.data.materials.append(material)


def bevel_object(obj, width=0.12, segments=2):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    modifier = obj.modifiers.new("Forged edge", "BEVEL")
    modifier.width = width
    modifier.segments = segments
    modifier.limit_method = "ANGLE"
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)


def box(name, location, dimensions, material, parent, rotation_z=0.0, bevel=0.1):
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=(0.0, 0.0, rotation_z))
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    assign_material(obj, material)
    if bevel > 0.0:
        bevel_object(obj, min(bevel, min(dimensions) * 0.22), 2)
    obj.parent = parent
    return obj


def cylinder(name, location, radius, depth, vertices, material, parent, rotation=(0.0, 0.0, 0.0), bevel=0.08):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    assign_material(obj, material)
    if bevel > 0.0:
        bevel_object(obj, bevel, 2)
    obj.parent = parent
    return obj


def sphere(name, location, radius, material, parent):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=radius, location=location)
    obj = bpy.context.object
    obj.name = name
    assign_material(obj, material)
    obj.parent = parent
    return obj


def torus(name, location, major_radius, minor_radius, material, parent, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius,
        minor_radius=minor_radius,
        major_segments=24,
        minor_segments=6,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    assign_material(obj, material)
    obj.parent = parent
    return obj


def tapered_prism(name, location, height, bottom_size, top_size, material, parent, rotation_z=0.0, bevel=0.08):
    bx, by = bottom_size[0] * 0.5, bottom_size[1] * 0.5
    tx, ty = top_size[0] * 0.5, top_size[1] * 0.5
    z0, z1 = -height * 0.5, height * 0.5
    vertices = [
        (-bx, -by, z0), (bx, -by, z0), (bx, by, z0), (-bx, by, z0),
        (-tx, -ty, z1), (tx, -ty, z1), (tx, ty, z1), (-tx, ty, z1),
    ]
    faces = [
        (0, 1, 2, 3), (4, 7, 6, 5),
        (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (4, 0, 3, 7),
    ]
    mesh = bpy.data.meshes.new(name + " Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler.z = rotation_z
    assign_material(obj, material)
    if bevel > 0.0:
        bevel_object(obj, bevel, 2)
    obj.parent = parent
    return obj


def radial_point(angle, radius, z):
    return (math.cos(angle) * radius, math.sin(angle) * radius, z)


def radial_box(name, angle, radius, z, length, width, height, material, parent, bevel=0.1):
    return box(name, radial_point(angle, radius, z), (length, width, height), material, parent, angle, bevel)


def empty(name, parent=None, location=(0.0, 0.0, 0.0)):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = 1.0
    obj.location = location
    obj.parent = parent
    return obj


def build_beacon(materials):
    root = empty("RB_RAIDER_BEACON")
    static = empty("RB_STATIC", root)
    orbit = empty("RB_SIGNAL_ORBIT", root, (0.0, 0.0, 27.2))
    pulse = empty("RB_CORE_PULSE", root, (0.0, 0.0, 38.0))

    black = materials["RB Black Armor"]
    dark = materials["RB Dark Metal"]
    red = materials["RB Raider Red"]
    magenta = materials["RB Signal Magenta"]
    pink = materials["RB Hot Pink"]
    amber = materials["RB Amber"]
    bone = materials["RB Bone"]

    # Layered three-point foundation, keeping the recognizable original footprint.
    cylinder("Foundation Shadow", (0.0, 0.0, 0.28), 4.65, 0.56, 12, black, static, bevel=0.18)
    cylinder("Foundation Red Plinth", (0.0, 0.0, 0.68), 3.95, 0.48, 12, red, static, bevel=0.14)
    cylinder("Foundation Dark Deck", (0.0, 0.0, 1.04), 3.25, 0.34, 12, dark, static, bevel=0.11)
    cylinder("Foundation Amber Lock", (0.0, 0.0, 1.29), 2.55, 0.22, 12, amber, static, bevel=0.08)

    for arm_index in range(3):
        angle = math.radians(-90.0 + arm_index * 120.0)
        suffix = arm_index + 1
        radial_box(f"Foundation Arm {suffix}", angle, 6.15, 0.72, 8.1, 2.10, 1.10, black, static, 0.18)
        radial_box(f"Foundation Arm Red Armor {suffix}", angle, 6.05, 1.34, 6.55, 1.42, 0.34, red, static, 0.10)
        radial_box(f"Foundation Arm Signal Inlay {suffix}", angle, 6.15, 1.55, 4.4, 0.30, 0.13, pink, static, 0.05)

        # Heavier, stepped foot pads make the base feel planted rather than improvised cubes.
        foot = radial_point(angle, 10.15, 0.78)
        box(f"Anchor Foot {suffix}", foot, (3.5, 3.0, 1.55), black, static, angle, 0.28)
        foot_top = radial_point(angle, 9.98, 1.67)
        box(f"Anchor Foot Red Cap {suffix}", foot_top, (2.8, 2.28, 0.46), red, static, angle, 0.12)
        foot_lock = radial_point(angle, 9.52, 1.98)
        box(f"Anchor Foot Amber Lock {suffix}", foot_lock, (1.05, 1.5, 0.22), amber, static, angle, 0.06)
        spike_loc = radial_point(angle, 11.45, 0.32)
        tapered_prism(f"Anchor Ground Tooth {suffix}", spike_loc, 1.55, (1.35, 1.10), (0.45, 0.45), dark, static, angle, 0.08)

        # Raised braces echo the original red star while giving the hub believable structure.
        radial_box(f"Foundation Brace {suffix}", angle, 4.65, 2.05, 4.65, 0.52, 0.48, red, static, 0.09)
        bolt_loc = radial_point(angle, 3.05, 2.35)
        cylinder(f"Foundation Bolt {suffix}", bolt_loc, 0.34, 0.42, 8, amber, static, rotation=(math.pi / 2.0, angle, 0.0), bevel=0.04)

    # Central tower: black split armor and the hot vertical signal spine from the reference.
    cylinder("Tower Inner Column", (0.0, 0.0, 14.4), 1.24, 26.0, 12, dark, static, bevel=0.12)
    cylinder("Tower Lower Collar", (0.0, 0.0, 2.0), 2.65, 1.05, 12, black, static, bevel=0.16)
    cylinder("Tower Red Collar", (0.0, 0.0, 3.0), 2.4, 0.46, 12, red, static, bevel=0.10)

    for fin_index in range(4):
        angle = math.radians(45.0 + fin_index * 90.0)
        direction = Vector((math.cos(angle), math.sin(angle), 0.0))
        fin_loc = direction * 1.92
        tapered_prism(
            f"Split Tower Armor {fin_index + 1}",
            (fin_loc.x, fin_loc.y, 14.8),
            22.7,
            (2.05, 1.02),
            (1.76, 0.82),
            black,
            static,
            angle - math.pi / 2.0,
            0.12,
        )
        panel_loc = direction * 2.47
        box(
            f"Raider Red Fin Panel {fin_index + 1}",
            (panel_loc.x, panel_loc.y, 15.0),
            (1.18, 0.20, 18.8),
            red,
            static,
            angle - math.pi / 2.0,
            0.07,
        )
        seam_loc = direction * 2.59
        box(
            f"Vertical Signal Spine {fin_index + 1}",
            (seam_loc.x, seam_loc.y, 15.2),
            (0.28, 0.12, 19.6),
            pink,
            static,
            angle - math.pi / 2.0,
            0.04,
        )

    for band_index, z in enumerate((6.2, 11.4, 16.6, 21.8, 25.8), start=1):
        cylinder(f"Tower Clamp {band_index}", (0.0, 0.0, z), 2.78 if band_index < 5 else 3.2, 0.48, 12, black, static, bevel=0.08)
        for bolt_index in range(4):
            angle = math.radians(45.0 + bolt_index * 90.0)
            loc = radial_point(angle, 2.72 if band_index < 5 else 3.12, z)
            box(f"Clamp Amber Bolt {band_index}-{bolt_index + 1}", loc, (0.46, 0.26, 0.24), amber, static, angle, 0.05)

    # Upper deck and mast preserve the original straight red stem beneath the orb.
    cylinder("Signal Deck Shadow", (0.0, 0.0, 26.45), 4.05, 0.70, 12, black, static, bevel=0.15)
    cylinder("Signal Deck Red Rim", (0.0, 0.0, 27.0), 3.52, 0.42, 12, red, static, bevel=0.10)
    cylinder("Upper Signal Mast", (0.0, 0.0, 31.5), 0.92, 8.8, 12, red, static, bevel=0.12)
    box("Upper Front Signal Spine", (0.0, -0.94, 31.45), (0.38, 0.18, 8.1), pink, static, 0.0, 0.05)
    cylinder("Orb Lower Socket", (0.0, 0.0, 35.0), 1.55, 1.20, 12, black, static, bevel=0.14)
    cylinder("Orb Hot Collar", (0.0, 0.0, 35.72), 1.88, 0.36, 12, amber, static, bevel=0.08)

    # The iconic spherical beacon stays dominant and simple, with only a thin equatorial detail.
    sphere("Raider Beacon Orb", (0.0, 0.0, 0.0), 3.18, magenta, pulse)
    torus("Beacon Orb Equator", (0.0, 0.0, 0.0), 3.19, 0.13, pink, pulse)
    cylinder("Beacon Orb Top Cap", (0.0, 0.0, 3.08), 0.65, 0.35, 10, amber, pulse, bevel=0.06)

    # Segmented magenta orbit ring and three long signal bars match the reference animation.
    segment_count = 16
    orbit_radius = 7.15
    segment_length = (2.0 * math.pi * orbit_radius / segment_count) * 0.70
    for segment_index in range(segment_count):
        angle = math.radians((360.0 / segment_count) * segment_index)
        material = amber if segment_index % 5 == 0 else magenta
        radial_box(
            f"Signal Orbit Segment {segment_index + 1}",
            angle + math.pi / 2.0,
            orbit_radius,
            0.0,
            segment_length,
            0.38,
            0.28,
            material,
            orbit,
            0.08,
        )

    for blade_index in range(3):
        angle = math.radians(blade_index * 120.0)
        radial_box(f"Orbit Signal Blade {blade_index + 1}", angle, 4.9, 0.0, 6.4, 0.38, 0.34, magenta, orbit, 0.08)
        node_loc = radial_point(angle, 7.65, 0.0)
        box(f"Orbit Counterweight {blade_index + 1}", node_loc, (1.15, 0.82, 0.88), black, orbit, angle, 0.14)
        node_light = radial_point(angle, 8.05, 0.0)
        box(f"Orbit Counterweight Light {blade_index + 1}", node_light, (0.40, 0.52, 0.44), pink, orbit, angle, 0.07)

    # Small bone chevrons read as raider markings without changing the original massing.
    for stripe_index, z in enumerate((8.5, 13.7, 18.9), start=1):
        for side in (-1.0, 1.0):
            box(
                f"Front Bone Chevron {stripe_index}-{1 if side < 0 else 2}",
                (side * 0.42, -2.50, z),
                (0.72, 0.16, 0.24),
                bone,
                static,
                math.radians(side * 32.0),
                0.04,
            )

    return root


def write_flat_textures():
    TEXTURE_DIR.mkdir(parents=True, exist_ok=True)
    for material_name, file_name in TEXTURE_NAMES.items():
        image = bpy.data.images.new(file_name[:-4], width=16, height=16)
        image.generated_color = PALETTE[material_name]
        image.filepath_raw = str(TEXTURE_DIR / file_name)
        image.file_format = "PNG"
        image.save()
        bpy.data.images.remove(image)


def setup_preview(materials):
    ground_material = make_material("Preview Desert", (0.32, 0.075, 0.018, 1.0), roughness=0.78)
    bpy.ops.mesh.primitive_plane_add(size=120.0, location=(0.0, 0.0, -0.03))
    ground = bpy.context.object
    ground.name = "Preview Desert Ground"
    assign_material(ground, ground_material)

    bpy.ops.object.camera_add(location=(44.0, -64.0, 37.0))
    camera = bpy.context.object
    camera.name = "Raider Beacon Hero Camera"
    direction = Vector((0.0, 0.0, 18.8)) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera.data.lens = 52.0
    bpy.context.scene.camera = camera

    def area_light(name, location, color, energy, size):
        bpy.ops.object.light_add(type="AREA", location=location)
        light = bpy.context.object
        light.name = name
        light.data.energy = energy
        light.data.color = color
        light.data.shape = "DISK"
        light.data.size = size
        direction = Vector((0.0, 0.0, 17.0)) - light.location
        light.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()

    area_light("Warm Desert Key", (-18.0, -24.0, 45.0), (1.0, 0.28, 0.07), 2300.0, 14.0)
    area_light("Magenta Raider Rim", (22.0, 12.0, 34.0), (1.0, 0.01, 0.18), 1800.0, 10.0)
    area_light("Amber Base Fill", (-18.0, 10.0, 8.0), (1.0, 0.10, 0.015), 1200.0, 12.0)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1200
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(PREVIEW_PATH)
    scene.render.film_transparent = False
    scene.world.color = (0.002, 0.001, 0.003)
    scene.view_settings.look = "AgX - Medium High Contrast"


def export_model(root):
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for child in root.children_recursive:
        child.select_set(True)
    bpy.context.view_layer.objects.active = root
    MODEL_PATH.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(MODEL_PATH),
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_materials="EXPORT",
        export_cameras=False,
        export_lights=False,
    )


def main():
    clear_scene()
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    materials = make_palette()
    root = build_beacon(materials)
    write_flat_textures()
    export_model(root)
    setup_preview(materials)
    BLEND_PATH.parent.mkdir(parents=True, exist_ok=True)
    PREVIEW_PATH.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
