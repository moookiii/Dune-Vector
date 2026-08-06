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

        reference_materials = [slot.material for slot in reference_object.material_slots]
        target_object.data.materials.clear()
        for reference_material in reference_materials:
            if reference_material is None:
                target_object.data.materials.append(None)
                continue
            material_name = reference_material.name
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


base_color_image = load_texture("Drone_PlasticSpeckled_BaseColor.png", "sRGB")
roughness_image = load_texture("Drone_PlasticSpeckled_Roughness.png", "Non-Color")
normal_image = load_texture("Drone_PlasticSpeckled_Normal.png", "Non-Color")

restored_slots = restore_original_assignments()


def make_speckled_variant(original):
    variant_name = f"{original.name}_SpeckledPlastic"
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
    # Twelve times larger than the prior color-preserving speckle pass.
    mapping.inputs["Scale"].default_value = (0.1125, 0.1125, 0.1125)
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

    color_texture = image_node(base_color_image, (-700, 260), "Large speckle mask source")
    roughness_texture = image_node(roughness_image, (-700, -70), "Speckled roughness")
    normal_texture = image_node(normal_image, (-700, -370), "Speckled normal")

    grayscale = nodes.new("ShaderNodeRGBToBW")
    grayscale.location = (-430, 280)
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.location = (-230, 280)
    ramp.color_ramp.interpolation = "EASE"
    ramp.color_ramp.elements[0].position = 0.27
    ramp.color_ramp.elements[1].position = 0.44

    mix = nodes.new("ShaderNodeMixRGB")
    mix.location = (40, 240)
    mix.blend_type = "MIX"
    mix.inputs[1].default_value = original_color
    speckle_color = tuple(min(1.0, channel * 1.22 + 0.11) for channel in original_color[:3]) + (1.0,)
    mix.inputs[2].default_value = speckle_color

    if original_link is not None:
        links.new(original_link, mix.inputs[1])

    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.location = (100, -300)
    normal_map.inputs["Strength"].default_value = 0.34

    links.new(color_texture.outputs["Color"], grayscale.inputs["Color"])
    links.new(grayscale.outputs["Val"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], mix.inputs[0])
    links.new(mix.outputs["Color"], base_input)
    links.new(roughness_texture.outputs["Color"], shader.inputs["Roughness"])
    links.new(normal_texture.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])

    return material


variants = {
    name: make_speckled_variant(bpy.data.materials[name])
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
    f"Restored {restored_slots} original slots; added larger color-preserving speckles to "
    f"{textured_slots} plastic slots; preserved {preserved_cyan_slots} tech_cyan slots; "
    f"left {untouched_nonplastic_slots} glass/rubber/lens/ground slots unchanged."
)
