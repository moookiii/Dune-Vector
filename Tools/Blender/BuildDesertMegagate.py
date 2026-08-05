import bpy
import json
import math
import os
import random
import struct
from mathutils import Vector


PROJECT_ROOT = r"C:\Dune Vector URP"
OUTPUT_DIR = os.path.join(PROJECT_ROOT, "Assets", "DuneVector", "Models", "DesertMegagate")
SOURCE_DIR = os.path.join(PROJECT_ROOT, "ArtSource", "Blender", "DesertMegagate")
BLEND_PATH = os.path.join(SOURCE_DIR, "DesertMegagate_ColorMatched.blend")
GLB_PATH = os.path.join(OUTPUT_DIR, "DesertMegagate_ColorMatched.glb")
PREVIEW_PATH = os.path.join(OUTPUT_DIR, "DesertMegagate_ColorMatched_Preview.png")
CONCRETE_TEXTURE = os.path.join(PROJECT_ROOT, "Assets", "DuneVector", "Resources", "Concrete.jpg")
SAND_TEXTURE = os.path.join(PROJECT_ROOT, "Assets", "DuneVector", "Resources", "dunes.png")

random.seed(718204)
os.makedirs(OUTPUT_DIR, exist_ok=True)
os.makedirs(SOURCE_DIR, exist_ok=True)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def make_collection(name, parent=None):
    collection = bpy.data.collections.new(name)
    (parent.children if parent else bpy.context.scene.collection.children).link(collection)
    return collection


def move_to_collection(obj, collection):
    for current in list(obj.users_collection):
        current.objects.unlink(obj)
    collection.objects.link(obj)


def principled_input(bsdf, *names):
    for name in names:
        socket = bsdf.inputs.get(name)
        if socket is not None:
            return socket
    return None


def image_material(name, image_path, tint, roughness, metallic=0.0, texture_scale=1.0):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(image_path, check_existing=True)
    texture.extension = "REPEAT"
    texture.interpolation = "Linear"
    uv = nodes.new("ShaderNodeTexCoord")
    mapping = nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (texture_scale, texture_scale, texture_scale)
    multiply = nodes.new("ShaderNodeMixRGB")
    multiply.blend_type = "MULTIPLY"
    multiply.inputs[0].default_value = 1.0
    multiply.inputs[2].default_value = (*tint[:3], 1.0)
    links.new(uv.outputs["UV"], mapping.inputs["Vector"])
    links.new(mapping.outputs["Vector"], texture.inputs["Vector"])
    links.new(texture.outputs["Color"], multiply.inputs[1])
    links.new(multiply.outputs["Color"], principled_input(bsdf, "Base Color"))
    principled_input(bsdf, "Roughness").default_value = roughness
    principled_input(bsdf, "Metallic").default_value = metallic
    links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])
    return material


def flat_material(name, color, roughness, metallic=0.0, emission=None, emission_strength=0.0):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    principled_input(bsdf, "Base Color").default_value = color
    principled_input(bsdf, "Roughness").default_value = roughness
    principled_input(bsdf, "Metallic").default_value = metallic
    if emission is not None:
        principled_input(bsdf, "Emission Color", "Emission").default_value = emission
        principled_input(bsdf, "Emission Strength").default_value = emission_strength
    return material


def finish_mesh(obj, bevel=0.35, segments=2, uv_size=10.0):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0.0:
        modifier = obj.modifiers.new("Architectural Edge Bevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = segments
        modifier.limit_method = "ANGLE"
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=uv_size, correct_aspect=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.select_set(False)


def add_box(name, location, dimensions, material, collection, bevel=0.35, rotation=(0.0, 0.0, 0.0), uv_size=10.0):
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    move_to_collection(obj, collection)
    obj.data.materials.append(material)
    finish_mesh(obj, bevel=min(bevel, min(dimensions) * 0.18), segments=3, uv_size=uv_size)
    ASSET_OBJECTS.append(obj)
    return obj


def add_tapered_block(name, location, bottom, top, height, depth, material, collection, bevel=0.4, uv_size=12.0):
    bx = bottom * 0.5
    tx = top * 0.5
    by = depth * 0.5
    hz = height * 0.5
    vertices = [
        (-bx, -by, -hz), (bx, -by, -hz), (bx, by, -hz), (-bx, by, -hz),
        (-tx, -by * 0.84, hz), (tx, -by * 0.84, hz), (tx, by * 0.84, hz), (-tx, by * 0.84, hz),
    ]
    faces = [
        (0, 3, 2, 1), (4, 5, 6, 7),
        (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
    ]
    mesh = bpy.data.meshes.new(name + " Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.location = location
    obj.data.materials.append(material)
    finish_mesh(obj, bevel=bevel, segments=3, uv_size=uv_size)
    ASSET_OBJECTS.append(obj)
    return obj


def add_beam(name, start, end, width, depth, material, collection, bevel=0.25, uv_size=8.0):
    start_v = Vector(start)
    end_v = Vector(end)
    delta = end_v - start_v
    midpoint = (start_v + end_v) * 0.5
    bpy.ops.mesh.primitive_cube_add(location=midpoint)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = (width, depth, delta.length)
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = delta.to_track_quat("Z", "Y")
    move_to_collection(obj, collection)
    obj.data.materials.append(material)
    finish_mesh(obj, bevel=min(bevel, min(width, depth) * 0.2), segments=3, uv_size=uv_size)
    ASSET_OBJECTS.append(obj)
    return obj


def add_cylinder(name, location, radius, depth, material, collection, vertices=12, rotation=(0.0, 0.0, 0.0), bevel=0.25):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    move_to_collection(obj, collection)
    obj.data.materials.append(material)
    finish_mesh(obj, bevel=min(bevel, radius * 0.15), segments=2, uv_size=8.0)
    ASSET_OBJECTS.append(obj)
    return obj


def add_dune(name, location, scale, collection, material):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=1.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    move_to_collection(obj, collection)
    obj.data.materials.append(material)
    finish_mesh(obj, bevel=0.0, segments=1, uv_size=13.0)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    ASSET_OBJECTS.append(obj)
    return obj


def add_arch_layer(prefix, y, width, depth, material, collection, skips, z_offset=0.0):
    points = []
    segment_count = 18
    for index in range(segment_count + 1):
        t = index / segment_count
        x = -43.0 + 86.0 * t
        z = 98.0 + math.sin(math.pi * t) * 31.0 + z_offset
        points.append((x, y, z))
    for index in range(segment_count):
        if index in skips:
            continue
        add_beam(
            f"{prefix} Segment {index + 1:02d}",
            points[index], points[index + 1], width, depth,
            material, collection, bevel=0.38, uv_size=11.0)


def patch_glb_base_color_factors(filepath, factors_by_material):
    """Preserve Blender's texture tint using glTF's texture × baseColorFactor model."""
    with open(filepath, "rb") as source:
        data = source.read()
    magic, version, _ = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF" or version != 2:
        raise RuntimeError(f"Unexpected GLB header in {filepath}")

    chunks = []
    offset = 12
    patched_material_count = 0
    while offset < len(data):
        chunk_length, chunk_type = struct.unpack_from("<I4s", data, offset)
        chunk_data = data[offset + 8:offset + 8 + chunk_length]
        if chunk_type == b"JSON":
            document = json.loads(chunk_data.decode("utf-8").rstrip(" \t\r\n\0"))
            for material in document.get("materials", []):
                factor = factors_by_material.get(material.get("name"))
                if factor is None:
                    continue
                pbr = material.setdefault("pbrMetallicRoughness", {})
                pbr["baseColorFactor"] = list(factor)
                patched_material_count += 1
            chunk_data = json.dumps(document, separators=(",", ":")).encode("utf-8")
            chunk_data += b" " * ((4 - len(chunk_data) % 4) % 4)
        chunks.append((chunk_type, chunk_data))
        offset += 8 + chunk_length

    output = bytearray(struct.pack("<4sII", b"glTF", 2, 0))
    for chunk_type, chunk_data in chunks:
        output.extend(struct.pack("<I4s", len(chunk_data), chunk_type))
        output.extend(chunk_data)
    struct.pack_into("<I", output, 8, len(output))
    with open(filepath, "wb") as destination:
        destination.write(output)
    if patched_material_count != len(factors_by_material):
        raise RuntimeError(
            f"Patched {patched_material_count} GLB materials; expected {len(factors_by_material)}")


clear_scene()
ASSET_OBJECTS = []

asset_root = make_collection("DV_DESERT_MEGAGATE")
structure = make_collection("01_Monumental_Structure", asset_root)
armor = make_collection("02_Armor_And_Ribs", asset_root)
signals = make_collection("03_Signal_Channels", asset_root)
ruins = make_collection("04_Sand_Burial_And_Debris", asset_root)
presentation = make_collection("99_Presentation")

desert_stone = image_material(
    "DV Desert Stone - Sun Baked Concrete", CONCRETE_TEXTURE,
    (0.72, 0.31, 0.095, 1.0), 0.88, 0.0, 1.35)
desert_stone_light = image_material(
    "DV Desert Stone - Pale Eroded Faces", CONCRETE_TEXTURE,
    (0.92, 0.49, 0.18, 1.0), 0.93, 0.0, 1.75)
oxidized_bronze = image_material(
    "DV Oxidized Bronze - Sand Scoured", CONCRETE_TEXTURE,
    (0.19, 0.16, 0.115, 1.0), 0.48, 0.46, 2.1)
dark_recess = flat_material("DV Deep Recess", (0.012, 0.016, 0.019, 1.0), 0.38, 0.62)
cyan_signal = flat_material(
    "DV Cyan Wayfinding Glass", (0.005, 0.16, 0.19, 1.0), 0.22, 0.18,
    emission=(0.0, 0.82, 1.0, 1.0), emission_strength=8.0)
amber_signal = flat_material(
    "DV Amber Relic Glass", (0.22, 0.055, 0.008, 1.0), 0.28, 0.12,
    emission=(1.0, 0.14, 0.015, 1.0), emission_strength=4.0)
sand_material = image_material(
    "DV Wind Rippled Sand", SAND_TEXTURE,
    (0.52, 0.28, 0.10, 1.0), 1.0, 0.0, 0.55)

# Buried stepped foundations establish scale before the tall pylons.
for side_index, side in enumerate((-1.0, 1.0)):
    x = side * 44.0
    label = "West" if side < 0 else "East"
    add_box(f"{label} Buried Plinth", (x, 0.0, 2.5), (31.0, 23.0, 7.0), desert_stone, structure, 0.75, uv_size=13.0)
    add_box(f"{label} Foundation Step", (x, -0.8, 7.0), (25.0, 19.5, 5.0), desert_stone_light, structure, 0.6, uv_size=12.0)
    add_tapered_block(f"{label} Lower Monolith", (x, 0.0, 33.0), 21.0, 16.5, 48.0, 16.0, desert_stone, structure, 0.7, 12.0)
    add_box(f"{label} Mid Lock", (x, 0.0, 58.0), (24.0, 18.5, 8.0), oxidized_bronze, armor, 0.55, uv_size=9.0)
    add_tapered_block(f"{label} Upper Monolith", (x, 0.0, 78.5), 16.5, 13.0, 37.0, 13.5, desert_stone_light, structure, 0.58, 11.0)
    add_box(f"{label} Crown Block", (x, 0.0, 100.0), (21.0, 17.0, 10.0), desert_stone, structure, 0.72, uv_size=11.0)

    # Splayed outer buttresses and inner load ribs give the gate a believable load path.
    add_beam(f"{label} Outer Buttress", (x + side * 14.0, 1.5, 8.0), (x + side * 8.0, 1.2, 69.0), 5.0, 10.0, oxidized_bronze, armor, 0.45, 9.0)
    add_beam(f"{label} Inner Buttress", (x - side * 11.0, 2.0, 7.0), (x - side * 7.5, 1.0, 54.0), 3.6, 8.5, oxidized_bronze, armor, 0.38, 8.0)
    add_beam(f"{label} Rear Spine", (x, 6.4, 12.0), (x, 6.4, 96.0), 4.0, 3.2, dark_recess, armor, 0.28, 7.0)

    # Deep face recess, nested armor plates, and vertical illuminated route marks.
    add_box(f"{label} Main Face Recess", (x, -8.15, 43.0), (11.5, 1.1, 54.0), dark_recess, armor, 0.25, uv_size=8.0)
    add_box(f"{label} Upper Face Recess", (x, -7.0, 80.0), (8.2, 1.0, 24.0), dark_recess, armor, 0.22, uv_size=7.0)
    for panel in range(4):
        panel_z = 24.0 + panel * 12.3
        panel_width = 8.8 if panel % 2 == 0 else 7.2
        add_box(
            f"{label} Floating Armor Panel {panel + 1}",
            (x - side * (0.8 if panel % 2 else -0.6), -8.85, panel_z),
            (panel_width, 0.75, 8.8), oxidized_bronze, armor, 0.28, uv_size=6.5)
    add_box(f"{label} Cyan Route Channel", (x - side * 2.7, -9.3, 48.0), (0.75, 0.42, 48.0), cyan_signal, signals, 0.14, uv_size=5.0)
    add_box(f"{label} Crown Signal", (x + side * 2.4, -9.05, 98.5), (5.5, 0.5, 0.8), cyan_signal, signals, 0.12, uv_size=4.0)

    # Monumental fins split up the rectangular silhouette and catch desert rim light.
    for fin in range(3):
        fin_z = 24.0 + fin * 24.0
        add_box(
            f"{label} Outer Radiator Fin {fin + 1}",
            (x + side * 11.0, 0.0, fin_z),
            (2.2, 24.0 - fin * 2.0, 8.0), desert_stone_light, structure,
            0.45, rotation=(0.0, 0.0, math.radians(side * (5.0 + fin * 2.0))), uv_size=9.0)

# A fractured double arch creates a much stronger landmark silhouette than disconnected lintels.
add_arch_layer("Outer Broken Arch", 0.0, 7.0, 14.5, desert_stone, structure, {8, 9, 14}, 0.0)
add_arch_layer("Inner Structural Arch", -1.0, 3.5, 10.0, oxidized_bronze, armor, {3, 8, 9, 10, 14}, -7.0)

# Crown clamps, suspended wreckage, and a displaced keystone sell ancient structural failure.
for side in (-1.0, 1.0):
    x = side * 32.0
    add_beam("Crown Tension Clamp", (side * 40.0, -8.2, 103.0), (x, -8.2, 111.0), 2.5, 2.0, oxidized_bronze, armor, 0.25, 6.0)
    add_cylinder("Crown Clamp Pin", (side * 39.0, -9.0, 102.0), 1.45, 3.0, oxidized_bronze, armor, 12, (math.radians(90.0), 0.0, 0.0), 0.18)

add_box("Displaced Keystone", (7.0, -1.0, 126.0), (11.0, 13.0, 7.0), desert_stone_light, structure, 0.65, rotation=(math.radians(8.0), math.radians(-11.0), math.radians(13.0)), uv_size=9.0)
add_box("Hanging Arch Shard A", (-8.0, -0.5, 114.0), (9.0, 11.0, 4.0), oxidized_bronze, armor, 0.38, rotation=(math.radians(11.0), math.radians(5.0), math.radians(-18.0)), uv_size=8.0)
add_box("Hanging Arch Shard B", (17.0, 0.0, 116.0), (7.0, 10.0, 3.5), desert_stone, structure, 0.34, rotation=(math.radians(-9.0), math.radians(12.0), math.radians(26.0)), uv_size=8.0)
add_box("Central Amber Relic", (0.0, -7.2, 105.0), (2.2, 1.1, 2.2), amber_signal, signals, 0.3, rotation=(0.0, math.radians(45.0), math.radians(45.0)), uv_size=3.0)

# Wind-blown sand burial makes the structure belong to the desert rather than sit on it.
add_dune("West Foundation Sand Mantle", (-44.0, -1.5, -0.5), (25.0, 17.0, 6.2), ruins, sand_material)
add_dune("East Foundation Sand Mantle", (44.0, 1.0, -0.6), (24.0, 16.0, 5.8), ruins, sand_material)
add_dune("Central Wind Drift", (3.0, 6.0, -1.8), (38.0, 12.0, 4.0), ruins, sand_material)

# Deterministic, asymmetrical debris field with authored material variation.
for index in range(20):
    side = -1.0 if index % 2 == 0 else 1.0
    x = side * random.uniform(28.0, 74.0)
    y = random.uniform(-20.0, 19.0)
    z = random.uniform(0.2, 2.5)
    dims = (random.uniform(2.0, 7.5), random.uniform(2.0, 7.0), random.uniform(1.2, 5.5))
    rotation = tuple(math.radians(random.uniform(-32.0, 32.0)) for _ in range(3))
    material = desert_stone if index % 3 else oxidized_bronze
    add_box(f"Megagate Debris {index + 1:02d}", (x, y, z), dims, material, ruins, 0.3, rotation, 7.0)

# Presentation-only desert floor, camera, and dramatic neutral lighting.
add_box("Preview Desert Floor", (0.0, 18.0, -5.0), (290.0, 220.0, 5.0), sand_material, presentation, 0.0, uv_size=16.0)
ASSET_OBJECTS.remove(bpy.context.scene.objects.get("Preview Desert Floor"))

bpy.ops.object.light_add(type="AREA", location=(-70.0, -90.0, 145.0))
key = bpy.context.object
key.name = "Preview Sun Key"
key.data.energy = 3200.0
key.data.shape = "DISK"
key.data.size = 70.0
key.data.color = (1.0, 0.49, 0.22)
move_to_collection(key, presentation)

bpy.ops.object.light_add(type="AREA", location=(85.0, 25.0, 85.0))
fill = bpy.context.object
fill.name = "Preview Sky Fill"
fill.data.energy = 2800.0
fill.data.size = 60.0
fill.data.color = (0.18, 0.42, 1.0)
move_to_collection(fill, presentation)

bpy.ops.object.light_add(
    type="SUN",
    location=(0.0, 0.0, 150.0),
    rotation=(math.radians(28.0), math.radians(-24.0), math.radians(-32.0)),
)
sun = bpy.context.object
sun.name = "Preview Desert Sun"
sun.data.energy = 3.2
sun.data.angle = math.radians(7.0)
sun.data.color = (1.0, 0.58, 0.32)
move_to_collection(sun, presentation)

def track_to(obj, point):
    direction = Vector(point) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


bpy.ops.object.camera_add(location=(170.0, -295.0, 112.0))
camera = bpy.context.object
camera.name = "Desert Megagate Hero Camera"
camera.data.lens = 62.0
track_to(camera, (0.0, 0.0, 64.0))
move_to_collection(camera, presentation)
bpy.context.scene.camera = camera
track_to(key, (0.0, 0.0, 48.0))
track_to(fill, (0.0, 0.0, 58.0))

world = bpy.context.scene.world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.018, 0.009, 0.012, 1.0)
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.5

scene = bpy.context.scene
scene["DV_Asset"] = "Desert Megagate"
scene["DV_Design_Intent"] = "Monumental broken desert transit arch with authored asymmetry and sand burial"
scene["DV_Texture_Sources"] = "Concrete.jpg and dunes.png from the DuneVector project"
scene["DV_Approximate_Height_Meters"] = 132.0
scene["DV_Approximate_Opening_Meters"] = 70.0
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1200
scene.render.resolution_y = 900
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = PREVIEW_PATH
scene.render.film_transparent = False
scene.view_settings.look = "AgX - Medium High Contrast"
scene.view_settings.exposure = 1.0

# Export only asset geometry; presentation helpers remain in the editable blend.
bpy.ops.object.select_all(action="DESELECT")
for obj in ASSET_OBJECTS:
    if obj and obj.name in bpy.context.scene.objects:
        obj.select_set(True)
bpy.context.view_layer.objects.active = ASSET_OBJECTS[0]

try:
    bpy.ops.export_scene.gltf(
        filepath=GLB_PATH,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_materials="EXPORT",
    )
except TypeError:
    bpy.ops.export_scene.gltf(
        filepath=GLB_PATH,
        export_format="GLB",
        use_selection=True,
        export_yup=True,
    )

patch_glb_base_color_factors(
    GLB_PATH,
    {
        "DV Desert Stone - Sun Baked Concrete": (0.72, 0.31, 0.095, 1.0),
        "DV Desert Stone - Pale Eroded Faces": (0.92, 0.49, 0.18, 1.0),
        "DV Oxidized Bronze - Sand Scoured": (0.19, 0.16, 0.115, 1.0),
        "DV Wind Rippled Sand": (0.52, 0.28, 0.10, 1.0),
    },
)

bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
bpy.ops.render.render(write_still=True)

print(f"DESERT_MEGAGATE_BLEND={BLEND_PATH}")
print(f"DESERT_MEGAGATE_GLB={GLB_PATH}")
print(f"DESERT_MEGAGATE_PREVIEW={PREVIEW_PATH}")
print(f"DESERT_MEGAGATE_MESH_COUNT={len(ASSET_OBJECTS)}")
