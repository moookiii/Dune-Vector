"""Hero render styled after the reference: high 3/4 view, floating subject,
soft cool light, teal radial-glow backdrop with a heavy vignette."""
import bpy, bmesh, math, os
from mathutils import Vector

sc = bpy.context.scene
log = []

# An empty sequence editor would replace the 3D render with nothing.
sc.render.use_sequencer = False
sc.render.use_compositing = True

MANAGED = ("HeroCam", "KeyLight", "RimCool", "RimWarm", "TopStrip", "Fill",
           "TopSoft", "BackRim", "SideFill", "GroundPlane", "FogVolume",
           "Backdrop", "TestSun", "ScoutSun", "ScoutCam")
for name in MANAGED:
    ob = bpy.data.objects.get(name)
    if ob:
        bpy.data.objects.remove(ob, do_unlink=True)
for o in bpy.data.objects:
    o.hide_render = False

TARGET = Vector((-0.03, -0.10, 1.25))

# ---------------------------------------------------------------- camera
# Long lens, high elevation, subject small in frame with generous margins.
AZ, EL, DIST, LENS = 38.0, 33.0, 26.0, 85.0
cd = bpy.data.cameras.new("HeroCam")
cd.lens = LENS
cd.clip_end = 500.0
cam = bpy.data.objects.new("HeroCam", cd)
sc.collection.objects.link(cam)
sc.camera = cam

a, e = math.radians(AZ), math.radians(EL)
cam.location = TARGET + Vector((
    DIST * math.cos(e) * math.sin(a),
    -DIST * math.cos(e) * math.cos(a),
    DIST * math.sin(e),
))
view_dir = (TARGET - cam.location).normalized()
cam.rotation_euler = view_dir.to_track_quat('-Z', 'Y').to_euler()
cd.dof.use_dof = True
cd.dof.focus_distance = DIST
cd.dof.aperture_fstop = 8.0
log.append("camera dist={:.1f} lens={:.0f}".format(DIST, LENS))

# ---------------------------------------------------------------- backdrop
# A flat emissive card behind the subject, perpendicular to the view axis.
# It supplies the teal core glow and the falloff to near-black corners.
BACK_DIST = 26.0
bmesh_mesh = bpy.data.meshes.new("Backdrop")
backdrop = bpy.data.objects.new("Backdrop", bmesh_mesh)
sc.collection.objects.link(backdrop)
bm = bmesh.new()
bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=30.0)
bm.to_mesh(bmesh_mesh)
bm.free()
backdrop.location = TARGET + view_dir * BACK_DIST
# create_grid builds the plane in XY (normal +Z); aim that normal at the camera.
backdrop.rotation_euler = (-view_dir).to_track_quat('Z', 'Y').to_euler()

bmat = bpy.data.materials.get("HeroBackdrop") or bpy.data.materials.new("HeroBackdrop")
bmat.use_nodes = True
bnt = bmat.node_tree
bnt.nodes.clear()
bout = bnt.nodes.new("ShaderNodeOutputMaterial"); bout.location = (600, 0)
bemit = bnt.nodes.new("ShaderNodeEmission"); bemit.location = (400, 0)
btc = bnt.nodes.new("ShaderNodeTexCoord"); btc.location = (-600, 0)
bmap = bnt.nodes.new("ShaderNodeMapping"); bmap.location = (-400, 0)
# Radius of the bright core, in the backdrop's local units.
bmap.inputs["Scale"].default_value = (0.105, 0.105, 0.105)
bgrad = bnt.nodes.new("ShaderNodeTexGradient"); bgrad.location = (-200, 0)
bgrad.gradient_type = 'SPHERICAL'
bramp = bnt.nodes.new("ShaderNodeValToRGB"); bramp.location = (0, 0)
bramp.color_ramp.interpolation = 'EASE'
bramp.color_ramp.elements[0].position = 0.0
bramp.color_ramp.elements[0].color = (0.0015, 0.0040, 0.0075, 1.0)  # corners
bramp.color_ramp.elements[1].position = 1.0
bramp.color_ramp.elements[1].color = (0.026, 0.068, 0.078, 1.0)    # teal core
bnt.links.new(btc.outputs["Object"], bmap.inputs["Vector"])
bnt.links.new(bmap.outputs["Vector"], bgrad.inputs["Vector"])
bnt.links.new(bgrad.outputs["Color"], bramp.inputs["Fac"])
bnt.links.new(bramp.outputs["Color"], bemit.inputs["Color"])
bemit.inputs["Strength"].default_value = 1.0
bnt.links.new(bemit.outputs["Emission"], bout.inputs["Surface"])
bmesh_mesh.materials.append(bmat)
backdrop.visible_shadow = False
log.append("backdrop ok")

# ---------------------------------------------------------------- world
# Very dark ambient; the backdrop carries the visible background.
world = sc.world
world.use_nodes = True
wnt = world.node_tree
wnt.nodes.clear()
wout = wnt.nodes.new("ShaderNodeOutputWorld"); wout.location = (300, 0)
wbg = wnt.nodes.new("ShaderNodeBackground"); wbg.location = (100, 0)
wbg.inputs["Color"].default_value = (0.020, 0.045, 0.060, 1.0)
wbg.inputs["Strength"].default_value = 0.40
wnt.links.new(wbg.outputs["Background"], wout.inputs["Surface"])
log.append("world ok")


# ---------------------------------------------------------------- lights
def add_area(name, loc, size, energy, color, shape='SQUARE', size_y=None,
             aim=None):
    ld = bpy.data.lights.new(name, type='AREA')
    ld.shape = shape
    ld.size = size
    if size_y is not None:
        ld.size_y = size_y
    ld.energy = energy
    ld.color = color
    ob = bpy.data.objects.new(name, ld)
    sc.collection.objects.link(ob)
    ob.location = loc
    d = (aim if aim is not None else TARGET) - Vector(loc)
    ob.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    return ob


# Broad soft top light — the high camera means the top surfaces carry the shot.
add_area("TopSoft", (-1.5, -1.0, 9.0), 14.0, 4200, (0.88, 0.94, 1.0))
# Cool key from upper front-left, slightly stronger for shaping.
add_area("KeyLight", (-7.5, -7.5, 6.5), 9.0, 3000, (0.92, 0.96, 1.0))
# Dim blue fill from the right to open the shadow side without flattening.
add_area("Fill", (8.0, -5.0, 2.5), 8.0, 600, (0.42, 0.62, 1.0))
# Subtle cool rim from behind to separate the silhouette from the backdrop.
add_area("BackRim", (3.0, 8.5, 5.0), 5.0, 1400, (0.55, 0.82, 1.0))
log.append("lights ok")

# ---------------------------------------------------------------- LED punch
BOOST = {"material": 4.0, "Material.009": 5.0, "Material.010": 4.0,
         "Material.011": 2.5, "Material.003": 2.0}
for mname, strength in BOOST.items():
    m = bpy.data.materials.get(mname)
    if not m or not m.use_nodes:
        continue
    for n in m.node_tree.nodes:
        if n.type == 'BSDF_PRINCIPLED':
            es = n.inputs.get("Emission Strength")
            if es is not None:
                es.default_value = strength
log.append("led ok")

# ---------------------------------------------------------------- render cfg
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = 1280
sc.render.resolution_y = 720
sc.render.resolution_percentage = 100
sc.render.film_transparent = False
sc.render.image_settings.file_format = 'PNG'

ee = sc.eevee
ee.taa_render_samples = 128
ee.use_raytracing = True
ee.use_shadows = True
ee.shadow_ray_count = 3
ee.shadow_step_count = 8
try:
    ee.ray_tracing_options.resolution_scale = '1'
    ee.ray_tracing_options.use_denoise = True
    ee.ray_tracing_options.screen_trace_quality = 0.5
except Exception as ex:
    log.append("rt opts fail: " + str(ex))

sc.view_settings.view_transform = 'AgX'
try:
    sc.view_settings.look = 'None'
except Exception:
    pass
sc.view_settings.exposure = 0.2
sc.view_settings.gamma = 1.0

# ---------------------------------------------------------------- compositor
# Blender 5.x: the compositor is a node group. Feeding it from Group Input
# yields an empty image, so read the render with a Render Layers node inside.
old = bpy.data.node_groups.get("HeroComp")
if old:
    bpy.data.node_groups.remove(old)
ng = bpy.data.node_groups.new("HeroComp", "CompositorNodeTree")
ng.interface.new_socket("Image", in_out='OUTPUT', socket_type='NodeSocketColor')
gout = ng.nodes.new("NodeGroupOutput"); gout.location = (900, 0)
rl = ng.nodes.new("CompositorNodeRLayers"); rl.location = (-300, 0)
rl.scene = sc


def set_sock(node, key, val):
    s = node.inputs.get(key)
    if s is None:
        log.append("no socket " + key)
        return
    try:
        s.default_value = val
    except Exception as ex:
        log.append("sock {:s} failed: {:s}".format(key, str(ex)))


bloom = ng.nodes.new("CompositorNodeGlare"); bloom.location = (100, 0)
set_sock(bloom, "Type", 'Bloom')
set_sock(bloom, "Quality", 'High')
set_sock(bloom, "Threshold", 0.9)
set_sock(bloom, "Strength", 0.22)
set_sock(bloom, "Size", 0.7)
set_sock(bloom, "Smoothness", 0.5)

ng.links.new(rl.outputs["Image"], bloom.inputs["Image"])
ng.links.new(bloom.outputs["Image"], gout.inputs["Image"])
sc.compositing_node_group = ng
sc.use_nodes = True
log.append("compositor ok")

OUT = r"C:\Users\jpall\AppData\Local\Temp\claude\C--Dune-Vector-URP\b64ce166-d4a9-447b-ac6a-df47b789eba2\scratchpad\shots"
os.makedirs(OUT, exist_ok=True)
sc.render.filepath = os.path.join(OUT, "ref_preview")
bpy.ops.render.render(write_still=True)
result = {"log": log}
