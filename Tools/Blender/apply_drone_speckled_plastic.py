from pathlib import Path

import bpy


MATERIAL_NAME = "Plastic_Speckled_Textured"
PRESERVED_MATERIAL_PREFIX = "tech_cyan"
PROJECT_ROOT = Path(__file__).resolve().parents[2]
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


base_color_image = load_texture("Drone_PlasticSpeckled_BaseColor.png", "sRGB")
roughness_image = load_texture("Drone_PlasticSpeckled_Roughness.png", "Non-Color")
normal_image = load_texture("Drone_PlasticSpeckled_Normal.png", "Non-Color")

material = bpy.data.materials.get(MATERIAL_NAME)
if material is None:
    material = bpy.data.materials.new(MATERIAL_NAME)

material.use_nodes = True
material.diffuse_color = (0.055, 0.06, 0.065, 1.0)
nodes = material.node_tree.nodes
links = material.node_tree.links
nodes.clear()

output = nodes.new("ShaderNodeOutputMaterial")
output.location = (880, 0)

shader = nodes.new("ShaderNodeBsdfPrincipled")
shader.location = (580, 0)
shader.label = "Image-textured speckled plastic"
input_socket(shader, "Metallic").default_value = 0.0
input_socket(shader, "IOR").default_value = 1.46
coat_weight = input_socket(shader, "Coat Weight", "Coat")
if coat_weight is not None:
    coat_weight.default_value = 0.16
coat_roughness = input_socket(shader, "Coat Roughness")
if coat_roughness is not None:
    coat_roughness.default_value = 0.25

texcoord = nodes.new("ShaderNodeTexCoord")
texcoord.location = (-1050, 0)

mapping = nodes.new("ShaderNodeMapping")
mapping.location = (-830, 0)
mapping.inputs["Scale"].default_value = (4.5, 4.5, 4.5)


def image_node(image, location, label):
    node = nodes.new("ShaderNodeTexImage")
    node.image = image
    node.location = location
    node.label = label
    node.extension = "REPEAT"
    node.interpolation = "Linear"
    node.projection = "BOX"
    node.projection_blend = 0.22
    links.new(mapping.outputs["Vector"], node.inputs["Vector"])
    return node


base_color = image_node(base_color_image, (-520, 240), "Speckled plastic base color")
roughness = image_node(roughness_image, (-520, -40), "Speckled plastic roughness")
normal_texture = image_node(normal_image, (-520, -320), "Speckled plastic normal")

normal_map = nodes.new("ShaderNodeNormalMap")
normal_map.location = (260, -260)
normal_map.inputs["Strength"].default_value = 0.42

links.new(texcoord.outputs["Generated"], mapping.inputs["Vector"])
links.new(base_color.outputs["Color"], shader.inputs["Base Color"])
links.new(roughness.outputs["Color"], shader.inputs["Roughness"])
links.new(normal_texture.outputs["Color"], normal_map.inputs["Color"])
links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])
links.new(shader.outputs["BSDF"], output.inputs["Surface"])

changed_slots = 0
preserved_slots = 0
changed_objects = set()

for obj in bpy.context.scene.objects:
    if obj.type != "MESH":
        continue

    if len(obj.data.materials) == 0:
        obj.data.materials.append(material)
        changed_slots += 1
        changed_objects.add(obj.name)
        continue

    for slot in obj.material_slots:
        existing = slot.material
        if existing and existing.name.casefold().startswith(PRESERVED_MATERIAL_PREFIX):
            preserved_slots += 1
            continue
        if existing != material:
            slot.material = material
            changed_slots += 1
            changed_objects.add(obj.name)

for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type != "VIEW_3D":
            continue
        for space in area.spaces:
            if space.type == "VIEW_3D":
                space.shading.type = "MATERIAL"

bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print(
    f"Applied {MATERIAL_NAME} to {changed_slots} material slots across "
    f"{len(changed_objects)} mesh objects; preserved {preserved_slots} tech_cyan slots; "
    "packed base color, roughness, and normal textures."
)
