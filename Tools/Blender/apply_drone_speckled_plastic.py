from pathlib import Path

import bpy


PRESERVED_MATERIAL_PREFIX = "tech_cyan"
PLASTIC_MATERIALS = {
    "Armor_Highlight",
    "Armor_Sand",
    "Chassis_Blue_Shadow",
    "Chassis_Pale_Blue",
    "Rotor_Graphite",
    "Soft_White",
}
PROJECT_ROOT = Path(__file__).resolve().parents[2]
REFERENCE_BLEND = PROJECT_ROOT / "Assets" / "DuneVector" / "Art" / "Drone" / "DuneVectorScoutDrone.blend"
TEXTURE_DIRECTORY = PROJECT_ROOT / "Assets" / "DuneVector" / "Art" / "Drone" / "Textures"


def input_socket(node, *names):
    for name in names:
        socket = node.inputs.get(name)
        if socket is not None:
            return socket
    return None


def load_texture(filename: str, color_space: str):
    path = TEXTURE_DIRECTORY / filename
    if not path.is_file():
        raise FileNotFoundError(path)
    image = bpy.data.images.load(str(path), check_existing=True)
    image.colorspace_settings.name = color_space
    return image


def restore_original_assignments():
    local_materials = {material.name: material for material in bpy.data.materials if material.library is None}

    with bpy.data.libraries.load(str(REFERENCE_BLEND), link=True) as (source, target):
        source_names = list(source.objects)
        target.objects = source.objects

    linked_objects = list(target.objects)
    restored_slots = 0

    for source_name, reference_object in zip(source_names, linked_objects):
        if reference_object is None or reference_object.type != "MESH":
            continue
        target_object = bpy.context.scene.objects.get(source_name)
        if target_object is None or target_object.type != "MESH":
            continue

        previous_mesh = target_object.data
        target_object.data = reference_object.data.copy()
        target_object.data.name = reference_object.data.name
        if previous_mesh.users == 0:
            bpy.data.meshes.remove(previous_mesh)

        for modifier in list(target_object.modifiers):
            if modifier.name in {
                "Drone_Smooth_Subdivision",
                "Drone_Weighted_Smoothing",
                "Drone_Surface_Remesh",
                "Drone_Export_Polygon_Budget",
            }:
                target_object.modifiers.remove(modifier)
        if "dune_vector_export_smoothed" in target_object:
            del target_object["dune_vector_export_smoothed"]

        reference_materials = [slot.material for slot in reference_object.material_slots]
        target_object.data.materials.clear()
        for reference_material in reference_materials:
            if reference_material is None:
                target_object.data.materials.append(None)
                continue
            material_name = reference_material.name
            base_name, separator, numeric_suffix = material_name.rpartition(".")
            if separator and numeric_suffix.isdigit() and (
                base_name in local_materials
                or base_name in PLASTIC_MATERIALS
                or base_name == "Tech_Cyan"
            ):
                material_name = base_name
            original_material = local_materials.get(material_name)
            if original_material is None:
                original_material = reference_material.copy()
                original_material.name = material_name
                local_materials[material_name] = original_material
            target_object.data.materials.append(original_material)
            restored_slots += 1

    for reference_object in linked_objects:
        if reference_object is not None:
            bpy.data.objects.remove(reference_object, do_unlink=True)

    return restored_slots


plastic006_image = load_texture("Drone_Plastic006_Color.jpg", "sRGB")
plastic006_pattern_mask = load_texture("Drone_Plastic006_PatternMask.png", "Non-Color")

restored_slots = restore_original_assignments()
tech_cyan = bpy.data.materials.get("Tech_Cyan")
if tech_cyan is None:
    raise KeyError("The original Tech_Cyan material was not found")
for obj in bpy.context.scene.objects:
    if obj.type != "MESH":
        continue
    for slot in obj.material_slots:
        if slot.material and slot.material.name.casefold().startswith(PRESERVED_MATERIAL_PREFIX):
            slot.material = tech_cyan
for duplicate in list(bpy.data.materials):
    if duplicate is not tech_cyan and duplicate.name.casefold().startswith(PRESERVED_MATERIAL_PREFIX) and duplicate.users == 0:
        bpy.data.materials.remove(duplicate)
for legacy_material in list(bpy.data.materials):
    if legacy_material.name.endswith("_SpeckledPlastic") and legacy_material.users == 0:
        bpy.data.materials.remove(legacy_material)


def make_textured_variant(original):
    variant_name = f"{original.name}_Plastic006"
    existing = bpy.data.materials.get(variant_name)
    if existing is not None:
        bpy.data.materials.remove(existing)

    material = original.copy()
    material.name = variant_name
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links

    shader = next((node for node in nodes if node.type == "BSDF_PRINCIPLED"), None)
    if shader is None:
        raise RuntimeError(f"{original.name} has no Principled BSDF")

    base_input = input_socket(shader, "Base Color")
    original_link = base_input.links[0].from_socket if base_input.is_linked else None
    original_color = tuple(base_input.default_value)
    if base_input.is_linked:
        links.remove(base_input.links[0])

    texcoord = nodes.new("ShaderNodeTexCoord")
    texcoord.location = (-1150, 0)
    mapping = nodes.new("ShaderNodeMapping")
    mapping.location = (-950, 0)
    mapping.inputs["Scale"].default_value = (1.5, 1.5, 1.5)
    links.new(texcoord.outputs["Generated"], mapping.inputs["Vector"])

    def image_node(image, location, label):
        node = nodes.new("ShaderNodeTexImage")
        node.image = image
        node.location = location
        node.label = label
        node.extension = "REPEAT"
        node.interpolation = "Linear"
        node.projection = "BOX"
        node.projection_blend = 0.28
        links.new(mapping.outputs["Vector"], node.inputs["Vector"])
        return node

    color_texture = image_node(plastic006_pattern_mask, (-700, 220), "Plastic006 contrast pattern")

    grayscale = nodes.new("ShaderNodeRGBToBW")
    grayscale.location = (-430, 280)

    color_variation = nodes.new("ShaderNodeValToRGB")
    color_variation.location = (-200, 260)
    color_variation.color_ramp.interpolation = "EASE"
    color_variation.color_ramp.elements[0].position = 0.10
    color_variation.color_ramp.elements[0].color = (0.45, 0.45, 0.45, 1.0)
    color_variation.color_ramp.elements[1].position = 0.90
    color_variation.color_ramp.elements[1].color = (1.0, 1.0, 1.0, 1.0)

    roughness_variation = nodes.new("ShaderNodeValToRGB")
    roughness_variation.location = (-200, -80)
    roughness_variation.color_ramp.interpolation = "EASE"
    roughness_variation.color_ramp.elements[0].position = 0.10
    roughness_variation.color_ramp.elements[0].color = (0.38, 0.38, 0.38, 1.0)
    roughness_variation.color_ramp.elements[1].position = 0.90
    roughness_variation.color_ramp.elements[1].color = (0.62, 0.62, 0.62, 1.0)

    color_mix = nodes.new("ShaderNodeMixRGB")
    color_mix.location = (50, 220)
    color_mix.blend_type = "MULTIPLY"
    color_mix.inputs[0].default_value = 0.45
    color_mix.inputs[1].default_value = original_color
    if original_link is not None:
        links.new(original_link, color_mix.inputs[1])

    links.new(color_texture.outputs["Color"], grayscale.inputs["Color"])
    links.new(grayscale.outputs["Val"], color_variation.inputs["Fac"])
    links.new(grayscale.outputs["Val"], roughness_variation.inputs["Fac"])
    links.new(color_variation.outputs["Color"], color_mix.inputs[2])
    links.new(color_mix.outputs["Color"], base_input)
    links.new(roughness_variation.outputs["Color"], shader.inputs["Roughness"])

    return material


variants = {
    name: make_textured_variant(bpy.data.materials[name])
    for name in PLASTIC_MATERIALS
}

textured_slots = 0
preserved_cyan_slots = 0
untouched_nonplastic_slots = 0

for obj in bpy.context.scene.objects:
    if obj.type != "MESH":
        continue
    for slot in obj.material_slots:
        material = slot.material
        if material is None:
            continue
        if material.name.casefold().startswith(PRESERVED_MATERIAL_PREFIX):
            preserved_cyan_slots += 1
        elif material.name in variants:
            slot.material = variants[material.name]
            textured_slots += 1
        else:
            untouched_nonplastic_slots += 1

for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == "VIEW_3D":
            for space in area.spaces:
                if space.type == "VIEW_3D":
                    space.shading.type = "MATERIAL"

bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print(
    f"Restored {restored_slots} original slots; applied the color-preserving Plastic006 texture to "
    f"{textured_slots} plastic slots; preserved {preserved_cyan_slots} tech_cyan slots; "
    f"left {untouched_nonplastic_slots} glass/rubber/lens/ground slots unchanged; "
    "restored original mesh geometry without smoothing or vertex-count changes."
)
