import bpy


MATERIAL_NAME = "Plastic_Speckled"
PRESERVED_MATERIAL_PREFIX = "tech_cyan"


def input_socket(node, *names):
    for name in names:
        socket = node.inputs.get(name)
        if socket is not None:
            return socket
    return None


material = bpy.data.materials.get(MATERIAL_NAME)
if material is None:
    material = bpy.data.materials.new(MATERIAL_NAME)

material.use_nodes = True
material.diffuse_color = (0.055, 0.065, 0.075, 1.0)
nodes = material.node_tree.nodes
links = material.node_tree.links
nodes.clear()

output = nodes.new("ShaderNodeOutputMaterial")
output.location = (720, 0)

shader = nodes.new("ShaderNodeBsdfPrincipled")
shader.location = (430, 0)
shader.label = "Molded speckled plastic"
input_socket(shader, "Metallic").default_value = 0.0
input_socket(shader, "Roughness").default_value = 0.38
input_socket(shader, "IOR").default_value = 1.46
coat_weight = input_socket(shader, "Coat Weight", "Coat")
if coat_weight is not None:
    coat_weight.default_value = 0.18
coat_roughness = input_socket(shader, "Coat Roughness")
if coat_roughness is not None:
    coat_roughness.default_value = 0.24

texcoord = nodes.new("ShaderNodeTexCoord")
texcoord.location = (-900, 0)

noise = nodes.new("ShaderNodeTexNoise")
noise.location = (-650, 80)
noise.noise_dimensions = "3D"
noise.inputs["Scale"].default_value = 135.0
noise.inputs["Detail"].default_value = 3.0
noise.inputs["Roughness"].default_value = 0.72
noise.inputs["Distortion"].default_value = 0.08

ramp = nodes.new("ShaderNodeValToRGB")
ramp.location = (-350, 120)
ramp.color_ramp.interpolation = "CONSTANT"
ramp.color_ramp.elements.remove(ramp.color_ramp.elements[1])
base = ramp.color_ramp.elements[0]
base.position = 0.0
base.color = (0.035, 0.045, 0.055, 1.0)
mid = ramp.color_ramp.elements.new(0.56)
mid.color = (0.075, 0.09, 0.105, 1.0)
fleck = ramp.color_ramp.elements.new(0.77)
fleck.color = (0.32, 0.36, 0.39, 1.0)

bump = nodes.new("ShaderNodeBump")
bump.location = (160, -180)
bump.inputs["Strength"].default_value = 0.11
bump.inputs["Distance"].default_value = 0.025

links.new(texcoord.outputs["Generated"], noise.inputs["Vector"])
links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
links.new(ramp.outputs["Color"], shader.inputs["Base Color"])
links.new(noise.outputs["Fac"], bump.inputs["Height"])
links.new(bump.outputs["Normal"], shader.inputs["Normal"])
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

bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print(
    f"Applied {MATERIAL_NAME} to {changed_slots} material slots across "
    f"{len(changed_objects)} mesh objects; preserved {preserved_slots} tech_cyan slots."
)
