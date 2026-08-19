"""Build the Fallen Orbital Array landmark master asset.

Replaces the primitive-composited FallenOrbitalArray that DuneVectorLandmarks
used to assemble at runtime. Scale, palette and silhouette are carried over from
that build so the landmark still reads the same from the air:
    dish radius 28m, burial depth 5.5m, mast 24m, 3 solar wings 22m long.
"""
from pathlib import Path
import math
import random

import bpy
from mathutils import Vector, Matrix, Euler

try:
    SCRIPT_DIR = Path(__file__).resolve().parent
except NameError:
    SCRIPT_DIR = Path(r"C:/Dune Vector URP/ArtSource/Blender/FallenOrbitalArray")

PROJECT_ROOT = SCRIPT_DIR.parents[2]
BLEND_PATH = SCRIPT_DIR / "FallenOrbitalArray_Master.blend"
MODEL_PATH = PROJECT_ROOT / "Assets/DuneVector/Resources/FallenOrbitalArray/FallenOrbitalArray.glb"
PREVIEW_PATH = PROJECT_ROOT / "Assets/DuneVector/Models/FallenOrbitalArray/FallenOrbitalArray_Preview.png"

# Carried over from Dune Vector Runtime Settings so the mesh matches the
# landmark footprint the rest of the game already reserves for it.
DISH_RADIUS = 28.0
BURIAL_DEPTH = 5.5
MAST_HEIGHT = 24.0
WING_LENGTH = 22.0
DISH_TILT = 42.0
FOCAL_RATIO = 0.38
FOCAL_LENGTH = FOCAL_RATIO * DISH_RADIUS * 2.0

# Base colour, metallic, roughness, emission strength.
PALETTE = {
    "FOA Hull Bone": ((0.720, 0.610, 0.430, 1.0), 0.10, 0.62, 0.0),
    "FOA Hull Weathered": ((0.520, 0.430, 0.310, 1.0), 0.12, 0.78, 0.0),
    "FOA Steel": ((0.380, 0.460, 0.480, 1.0), 0.85, 0.38, 0.0),
    "FOA Dark Steel": ((0.150, 0.180, 0.200, 1.0), 0.80, 0.46, 0.0),
    "FOA Interior Black": ((0.055, 0.070, 0.080, 1.0), 0.35, 0.60, 0.0),
    "FOA Accent Teal": ((0.020, 0.350, 0.420, 1.0), 0.20, 0.30, 3.2),
    "FOA Concrete Gray": ((0.395, 0.370, 0.335, 1.0), 0.05, 0.86, 0.0),
    "FOA Solar Cell": ((0.020, 0.035, 0.100, 1.0), 0.55, 0.22, 0.0),
    "FOA Gold Foil": ((0.620, 0.460, 0.140, 1.0), 0.95, 0.28, 0.0),
    "FOA Rust": ((0.280, 0.130, 0.060, 1.0), 0.30, 0.88, 0.0),
    "FOA Warning Amber": ((0.720, 0.380, 0.040, 1.0), 0.15, 0.50, 0.0),
    "FOA Sand": ((0.600, 0.450, 0.280, 1.0), 0.02, 0.95, 0.0),
}


# ---------------------------------------------------------------- scene setup

def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for collection in (bpy.data.meshes, bpy.data.curves, bpy.data.materials,
                       bpy.data.cameras, bpy.data.lights):
        for datablock in list(collection):
            collection.remove(datablock)


def make_palette():
    materials = {}
    for name, (color, metallic, roughness, emission) in PALETTE.items():
        material = bpy.data.materials.new(name)
        material.diffuse_color = color
        material.use_nodes = True
        bsdf = material.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        if emission > 0.0:
            bsdf.inputs["Emission Color"].default_value = color
            bsdf.inputs["Emission Strength"].default_value = emission
        materials[name] = material
    return materials


# ------------------------------------------------------------ geometry buffer

class Geo:
    """Accumulates loose primitives into a single mesh so one part == one object."""

    def __init__(self):
        self.verts = []
        self.faces = []

    def add(self, verts, faces):
        base = len(self.verts)
        self.verts.extend(tuple(v) for v in verts)
        self.faces.extend([tuple(i + base for i in f) for f in faces])
        return self

    def __len__(self):
        return len(self.verts)


def mesh_object(name, geo, material, parent=None, smooth_angle=None):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(geo.verts, [], geo.faces)
    mesh.validate(verbose=False)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    if material is not None:
        mesh.materials.append(material)
    if parent is not None:
        obj.parent = parent
    if smooth_angle is not None:
        shade_by_angle(obj, smooth_angle)
    return obj


def shade_by_angle(obj, angle_deg=34.0):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle_deg))
    obj.select_set(False)


def empty(name, parent=None, location=(0.0, 0.0, 0.0), rotation=(0.0, 0.0, 0.0)):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_size = 2.0
    obj.location = location
    obj.rotation_euler = rotation
    bpy.context.scene.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    return obj


# ------------------------------------------------------------ primitive makers

def box_geo(center, size, rot=(0.0, 0.0, 0.0)):
    hx, hy, hz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
    local = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
             (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)]
    basis = Euler(rot, "XYZ").to_matrix()
    origin = Vector(center)
    verts = [tuple(origin + basis @ Vector(v)) for v in local]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
             (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return verts, faces


def cyl_geo(center, radius, height, sides=16, rot=(0.0, 0.0, 0.0),
            radius_top=None, caps=True):
    """Z-aligned cylinder / frustum before *rot* is applied."""
    top_radius = radius if radius_top is None else radius_top
    basis = Euler(rot, "XYZ").to_matrix()
    origin = Vector(center)
    verts = []
    for level, level_radius in ((-height * 0.5, radius), (height * 0.5, top_radius)):
        for i in range(sides):
            angle = math.tau * i / sides
            local = Vector((math.cos(angle) * level_radius,
                            math.sin(angle) * level_radius, level))
            verts.append(tuple(origin + basis @ local))
    faces = []
    for i in range(sides):
        nxt = (i + 1) % sides
        faces.append((i, nxt, nxt + sides, i + sides))
    if caps:
        faces.append(tuple(range(sides - 1, -1, -1)))
        faces.append(tuple(range(sides, sides * 2)))
    return verts, faces


def _transport_frames(points, closed):
    count = len(points)
    tangents = []
    for i in range(count):
        if closed:
            tangent = points[(i + 1) % count] - points[i - 1]
        elif i == 0:
            tangent = points[1] - points[0]
        elif i == count - 1:
            tangent = points[-1] - points[-2]
        else:
            tangent = points[i + 1] - points[i - 1]
        if tangent.length < 1e-9:
            tangent = Vector((0.0, 0.0, 1.0))
        tangents.append(tangent.normalized())

    reference = Vector((0.0, 0.0, 1.0))
    if abs(tangents[0].dot(reference)) > 0.94:
        reference = Vector((1.0, 0.0, 0.0))
    normal = (reference - tangents[0] * reference.dot(tangents[0])).normalized()
    normals = [normal]
    for i in range(1, count):
        previous, current = tangents[i - 1], tangents[i]
        carried = normals[-1].copy()
        axis = previous.cross(current)
        if axis.length > 1e-9:
            carried.rotate(Matrix.Rotation(previous.angle(current), 4, axis.normalized()))
        carried = carried - current * carried.dot(current)
        if carried.length < 1e-9:
            carried = Vector((0.0, 0.0, 1.0)) - current * current.z
        normals.append(carried.normalized())
    return tangents, normals


def tube_geo(points, radius, sides=8, closed=False, caps=True, radii=None):
    pts = [Vector(p) for p in points]
    if len(pts) < 2:
        return [], []
    tangents, normals = _transport_frames(pts, closed)
    verts = []
    for i, point in enumerate(pts):
        ring_radius = radius if radii is None else radii[i]
        normal = normals[i]
        binormal = tangents[i].cross(normal).normalized()
        for s in range(sides):
            angle = math.tau * s / sides
            offset = normal * (math.cos(angle) * ring_radius) + \
                binormal * (math.sin(angle) * ring_radius)
            verts.append(tuple(point + offset))
    faces = []
    span = len(pts) if closed else len(pts) - 1
    for i in range(span):
        a, b = i * sides, ((i + 1) % len(pts)) * sides
        for s in range(sides):
            nxt = (s + 1) % sides
            faces.append((a + s, a + nxt, b + nxt, b + s))
    if caps and not closed:
        faces.append(tuple(range(sides - 1, -1, -1)))
        last = (len(pts) - 1) * sides
        faces.append(tuple(range(last, last + sides)))
    return verts, faces


def quad_slab_geo(corners, thickness, normal=None):
    """Extrude a planar quad (4 points) into a slab of *thickness*."""
    pts = [Vector(c) for c in corners]
    if normal is None:
        normal = (pts[1] - pts[0]).cross(pts[3] - pts[0])
        if normal.length < 1e-9:
            normal = Vector((0.0, 0.0, 1.0))
        normal = normal.normalized()
    else:
        normal = Vector(normal).normalized()
    offset = normal * (thickness * 0.5)
    verts = [tuple(p + offset) for p in pts] + [tuple(p - offset) for p in pts]
    faces = [(0, 1, 2, 3), (7, 6, 5, 4),
             (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    return verts, faces


def rock_geo(center, size, seed, roughness=0.34):
    """Angular debris chunk: a jittered spheroid shell."""
    rng = random.Random(seed)
    rings = 3
    segments = 7
    origin = Vector(center)
    basis = Euler((rng.uniform(0, math.tau), rng.uniform(0, math.tau),
                   rng.uniform(0, math.tau)), "XYZ").to_matrix()
    verts = []
    for ring in range(1, rings + 1):
        polar = math.pi * ring / (rings + 1)
        for seg in range(segments):
            azimuth = math.tau * seg / segments
            jitter = 1.0 + rng.uniform(-roughness, roughness)
            local = Vector((math.sin(polar) * math.cos(azimuth) * size[0] * jitter,
                            math.sin(polar) * math.sin(azimuth) * size[1] * jitter,
                            math.cos(polar) * size[2] * jitter))
            verts.append(tuple(origin + basis @ local))
    top = len(verts)
    verts.append(tuple(origin + basis @ Vector((0.0, 0.0, size[2] * 1.05))))
    bottom = len(verts)
    verts.append(tuple(origin + basis @ Vector((0.0, 0.0, -size[2] * 1.05))))
    faces = []
    for ring in range(rings - 1):
        a, b = ring * segments, (ring + 1) * segments
        for seg in range(segments):
            nxt = (seg + 1) % segments
            faces.append((a + seg, a + nxt, b + nxt, b + seg))
    last = (rings - 1) * segments
    for seg in range(segments):
        nxt = (seg + 1) % segments
        faces.append((top, seg, nxt))
        faces.append((bottom, last + nxt, last + seg))
    return verts, faces


def lathe_geo(profile, segments=32, center=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0)):
    """Revolve a closed (r, z) cross-section around the Z axis."""
    basis = Euler(rot, "XYZ").to_matrix()
    origin = Vector(center)
    count = len(profile)
    verts = []
    for s in range(segments):
        angle = math.tau * s / segments
        cos_a, sin_a = math.cos(angle), math.sin(angle)
        for r, z in profile:
            local = Vector((r * cos_a, r * sin_a, z))
            verts.append(tuple(origin + basis @ local))
    faces = []
    for s in range(segments):
        a, b = s * count, ((s + 1) % segments) * count
        for i in range(count):
            nxt = (i + 1) % count
            faces.append((a + i, a + nxt, b + nxt, b + i))
    return verts, faces


# ------------------------------------------------------------------- the dish

RING_RADII = [3.4, 7.6, 12.4, 17.2, 21.6, 25.0, 28.0]
RING_SEGMENTS = [16, 16, 32, 32, 32, 32]
# Wedge of the reflector torn away on impact, in degrees.
TORN_START, TORN_END = 198.0, 268.0


def parabola_z(radius):
    return (radius * radius) / (4.0 * FOCAL_LENGTH)


def surface_point(radius, theta, offset=0.0):
    """Point on the reflector, pushed *offset* along its surface normal (+ = concave side)."""
    slope = radius / (2.0 * FOCAL_LENGTH)
    length = math.hypot(slope, 1.0)
    shifted_r = radius + (-slope / length) * offset
    shifted_z = parabola_z(radius) + (1.0 / length) * offset
    return Vector((shifted_r * math.cos(theta), shifted_r * math.sin(theta), shifted_z))


def truss_depth(radius):
    span = (radius - RING_RADII[0]) / (RING_RADII[-1] - RING_RADII[0])
    span = min(1.0, max(0.0, span))
    return 0.75 + 3.1 * math.sin(math.pi * span) ** 0.75


def in_torn_wedge(degrees, pad=0.0):
    value = degrees % 360.0
    return (TORN_START - pad) <= value <= (TORN_END + pad)


def panel_alive(ring_index, segment_index, segment_count, rng):
    mid_deg = math.degrees(math.tau * (segment_index + 0.5) / segment_count)
    if ring_index >= 3 and in_torn_wedge(mid_deg):
        return False
    if ring_index >= 2 and in_torn_wedge(mid_deg, pad=14.0) and rng.random() < 0.55:
        return False
    return rng.random() > 0.055


def panel_geo(r0, r1, t0, t1, thickness, sub_r=2, sub_t=2):
    """One curved reflector panel with thickness and closed edges."""
    half = thickness * 0.5
    front, back = [], []
    for i in range(sub_r + 1):
        radius = r0 + (r1 - r0) * i / sub_r
        for j in range(sub_t + 1):
            theta = t0 + (t1 - t0) * j / sub_t
            front.append(surface_point(radius, theta, half))
            back.append(surface_point(radius, theta, -half))
    stride = sub_t + 1
    offset = len(front)
    verts = [tuple(v) for v in front] + [tuple(v) for v in back]
    faces = []
    for i in range(sub_r):
        for j in range(sub_t):
            a = i * stride + j
            faces.append((a, a + 1, a + stride + 1, a + stride))
            b = offset + a
            faces.append((b + stride, b + stride + 1, b + 1, b))
    for j in range(sub_t):  # inner and outer edges
        a = j
        faces.append((a + 1, a, offset + a, offset + a + 1))
        c = sub_r * stride + j
        faces.append((c, c + 1, offset + c + 1, offset + c))
    for i in range(sub_r):  # the two radial edges
        a = i * stride
        faces.append((a, a + stride, offset + a + stride, offset + a))
        c = i * stride + sub_t
        faces.append((c + stride, c, offset + c, offset + c + stride))
    return verts, faces


def build_reflector(parent, materials):
    rng = random.Random(70517)
    groups = {
        "FOA Hull Bone": Geo(),
        "FOA Hull Weathered": Geo(),
        "FOA Gold Foil": Geo(),
    }
    names = list(groups.keys())
    radial_gap = 0.17
    for ring in range(len(RING_SEGMENTS)):
        r0, r1 = RING_RADII[ring], RING_RADII[ring + 1]
        segments = RING_SEGMENTS[ring]
        arc_gap = (math.tau / segments) * 0.028
        for segment in range(segments):
            if not panel_alive(ring, segment, segments, rng):
                continue
            t0 = math.tau * segment / segments + arc_gap
            t1 = math.tau * (segment + 1) / segments - arc_gap
            verts, faces = panel_geo(r0 + radial_gap, r1 - radial_gap, t0, t1, 0.2)
            # Exposed thermal blanket clusters along the tear, where facing
            # plates stripped off. Scattering it at random just reads as noise.
            mid_deg = math.degrees((t0 + t1) * 0.5) % 360.0
            near_tear = min(abs(mid_deg - TORN_START), abs(mid_deg - TORN_END)) < 34.0
            if near_tear and rng.random() < 0.45:
                key = names[2]
            else:
                key = names[0] if rng.random() < 0.74 else names[1]
            groups[key].add(verts, faces)

    for name, geo in groups.items():
        if len(geo):
            mesh_object(f"Reflector Panels {name.split()[-1]}", geo, materials[name], parent)


def build_backing_structure(parent, materials):
    """Radial trusses, concentric hoops and the hub the panels bolt to."""
    truss = Geo()
    rib_count = 16
    samples = 9
    for rib in range(rib_count):
        theta = math.tau * rib / rib_count
        top_chord, bottom_chord = [], []
        for i in range(samples):
            radius = RING_RADII[0] + (RING_RADII[-1] - 0.5 - RING_RADII[0]) * i / (samples - 1)
            top = surface_point(radius, theta, -0.32)
            depth = truss_depth(radius)
            top_chord.append(top)
            bottom_chord.append(top - Vector((0.0, 0.0, depth)))
        truss.add(*tube_geo(top_chord, 0.2, sides=6))
        truss.add(*tube_geo(bottom_chord, 0.24, sides=6))
        for i in range(samples):
            truss.add(*tube_geo([top_chord[i], bottom_chord[i]], 0.12, sides=5))
            if i < samples - 1:
                lower = bottom_chord[i] if i % 2 == 0 else bottom_chord[i + 1]
                upper = top_chord[i + 1] if i % 2 == 0 else top_chord[i]
                truss.add(*tube_geo([lower, upper], 0.1, sides=5))

    for hoop_radius in (8.5, 14.5, 20.0, 25.2):
        ring_points = []
        for s in range(48):
            theta = math.tau * s / 48
            point = surface_point(hoop_radius, theta, -0.32)
            ring_points.append(point - Vector((0.0, 0.0, truss_depth(hoop_radius) * 0.62)))
        truss.add(*tube_geo(ring_points, 0.19, sides=6, closed=True))
    mesh_object("Dish Backing Truss", truss, materials["FOA Dark Steel"], parent, 34.0)

    hub = Geo()
    hub.add(*lathe_geo([(0.0, -3.1), (3.6, -3.1), (3.6, -0.4), (3.0, 0.35),
                        (0.0, 0.35)], 28))
    hub.add(*cyl_geo((0.0, 0.0, -3.35), 3.9, 0.5, sides=28))
    for bolt in range(20):  # hub flange bolt circle
        angle = math.tau * bolt / 20
        hub.add(*cyl_geo((math.cos(angle) * 3.55, math.sin(angle) * 3.55, -3.62),
                         0.16, 0.34, sides=6))
    mesh_object("Dish Hub", hub, materials["FOA Steel"], parent, 34.0)


def build_rim(parent, materials):
    rim = Geo()
    splices = Geo()
    segments = 32
    for segment in range(segments):
        mid_deg = math.degrees(math.tau * (segment + 0.5) / segments)
        if in_torn_wedge(mid_deg):
            continue
        arc = []
        steps = 5
        for i in range(steps):
            fraction = (segment + 0.045 + 0.91 * i / (steps - 1)) / segments
            arc.append(surface_point(RING_RADII[-1] - 0.28, math.tau * fraction, -0.32))
        rim.add(*tube_geo(arc, 0.62, sides=8))
        joint = surface_point(RING_RADII[-1] - 0.28, math.tau * segment / segments, -0.32)
        splices.add(*box_geo(joint, (1.1, 0.5, 1.5),
                             rot=(0.0, 0.0, math.tau * segment / segments)))
    mesh_object("Dish Rim", rim, materials["FOA Concrete Gray"], parent, 34.0)
    mesh_object("Dish Rim Splice Plates", splices, materials["FOA Steel"], parent)

    # Torn edge where the wedge ripped out: jagged spars left hanging.
    torn = Geo()
    rng = random.Random(4402)
    for edge_deg in (TORN_START, TORN_END):
        theta = math.radians(edge_deg)
        for i in range(6):
            radius = RING_RADII[2] + (RING_RADII[-1] - RING_RADII[2]) * i / 5.0
            root = surface_point(radius, theta, -0.3)
            tip = root + Vector((rng.uniform(-1.4, 1.4), rng.uniform(-1.4, 1.4),
                                 rng.uniform(-2.2, 0.9)))
            torn.add(*tube_geo([root, tip], 0.13, sides=4))
    mesh_object("Reflector Torn Spars", torn, materials["FOA Rust"], parent)


def build_feed_assembly(parent, materials):
    """Cassegrain quadpod. One leg buckled on impact, so the apex hangs off-axis."""
    apex = Vector((2.4, -1.3, FOCAL_LENGTH - 0.9))
    apex_tilt = math.radians(17.0)

    legs = Geo()
    for leg in range(4):
        theta = math.radians(45.0 + 90.0 * leg)
        root = surface_point(23.4, theta, -0.2)
        collar = apex + Vector((math.cos(theta) * 1.7, math.sin(theta) * 1.7, -1.4))
        if leg == 1:
            # Buckled leg: kinks outward at mid-span before reaching the apex.
            mid = root.lerp(collar, 0.52) + Vector((3.4, -2.1, -1.9))
            points = [root, root.lerp(collar, 0.26), mid,
                      mid.lerp(collar, 0.55) + Vector((1.1, -0.6, 0.4)), collar]
            radii = [0.46, 0.4, 0.3, 0.33, 0.3]
        else:
            mid = root.lerp(collar, 0.5) + Vector((0.0, 0.0, 0.55))
            points = [root, mid, collar]
            radii = [0.5, 0.36, 0.3]
        legs.add(*tube_geo(points, 0.4, sides=8, radii=radii))
        legs.add(*cyl_geo(root + Vector((0.0, 0.0, 0.3)), 0.95, 1.3, sides=10))
    mesh_object("Feed Quadpod Legs", legs, materials["FOA Steel"], parent, 34.0)

    head = Geo()
    head.add(*cyl_geo(apex, 1.75, 1.9, sides=16, rot=(apex_tilt, 0.0, 0.0)))
    head.add(*cyl_geo(apex + Vector((0.0, 0.0, 1.25)), 1.2, 0.9, sides=14,
                      rot=(apex_tilt, 0.0, 0.0)))
    mesh_object("Feed Apex Hub", head, materials["FOA Dark Steel"], parent, 34.0)

    # Sub-reflector, hanging askew under the apex and facing the main dish.
    sub_profile = []
    sub_radius, sub_rings = 3.5, 7
    for i in range(sub_rings + 1):
        radius = sub_radius * i / sub_rings
        sub_profile.append((radius, -(radius * radius) / (4.0 * 5.2)))
    sub_profile.append((sub_radius + 0.22, -(sub_radius ** 2) / (4.0 * 5.2) + 0.3))
    for i in range(sub_rings, -1, -1):
        radius = sub_radius * i / sub_rings
        sub_profile.append((radius, -(radius * radius) / (4.0 * 5.2) + 0.34))
    sub = Geo()
    sub.add(*lathe_geo(sub_profile, 32, center=tuple(apex + Vector((0.4, 0.35, -1.15))),
                       rot=(math.radians(26.0), math.radians(8.0), 0.0)))
    mesh_object("Sub Reflector", sub, materials["FOA Hull Bone"], parent, 34.0)

    # Feed horn cluster at the vertex, aimed back up at the sub-reflector.
    horns = Geo()
    for i in range(3):
        angle = math.tau * i / 3
        base = Vector((math.cos(angle) * 1.15, math.sin(angle) * 1.15, 0.6))
        horns.add(*cyl_geo(base + Vector((0.0, 0.0, 1.5)), 0.42, 3.0, sides=12,
                           radius_top=1.05))
        horns.add(*cyl_geo(base + Vector((0.0, 0.0, 0.35)), 0.5, 1.4, sides=12))
    horns.add(*cyl_geo((0.0, 0.0, 0.5), 1.9, 1.0, sides=18))
    mesh_object("Feed Horn Cluster", horns, materials["FOA Steel"], parent, 34.0)

    waveguide = Geo()
    for i in range(3):
        angle = math.tau * i / 3 + 0.4
        start = Vector((math.cos(angle) * 1.5, math.sin(angle) * 1.5, 0.9))
        theta = math.radians(45.0 + 90.0 * ((i + 2) % 4))
        end = surface_point(23.0, theta, -0.6)
        waveguide.add(*tube_geo([start,
                                 start.lerp(end, 0.4) + Vector((0.0, 0.0, -0.9)),
                                 start.lerp(end, 0.78) + Vector((0.0, 0.0, -0.4)),
                                 end], 0.16, sides=6))
    mesh_object("Feed Waveguides", waveguide, materials["FOA Gold Foil"], parent, 34.0)


# ------------------------------------------------------- gimbal and spacecraft

BUS_CENTER = Vector((0.0, -3.4, -13.6))
BUS_SIZE = (15.0, 9.4, 6.2)


def build_gimbal(parent, materials):
    bearings = [Vector((side * 10.0, 0.0,
                        parabola_z(10.0) - truss_depth(10.0) - 0.3)) for side in (-1, 1)]
    yoke = Geo()
    for side, bearing in zip((-1, 1), bearings):
        shoulder = BUS_CENTER + Vector((side * 6.2, -0.4, BUS_SIZE[2] * 0.5 - 0.4))
        elbow = shoulder.lerp(bearing, 0.55) + Vector((side * 1.6, 0.0, 0.0))
        yoke.add(*tube_geo([shoulder, elbow, bearing], 0.85, sides=10,
                           radii=[1.05, 0.8, 0.7]))
        yoke.add(*tube_geo([shoulder + Vector((0.0, 1.9, 0.0)),
                            elbow + Vector((0.0, 1.5, 0.0)),
                            bearing + Vector((0.0, 1.2, 0.0))], 0.42, sides=7))
    mesh_object("Gimbal Yoke", yoke, materials["FOA Steel"], parent, 34.0)

    hardware = Geo()
    for side, bearing in zip((-1, 1), bearings):
        hardware.add(*cyl_geo(bearing, 1.9, 2.6, sides=20, rot=(0.0, math.pi * 0.5, 0.0)))
        hardware.add(*cyl_geo(bearing + Vector((side * 1.45, 0.0, 0.0)), 1.15, 0.7,
                              sides=16, rot=(0.0, math.pi * 0.5, 0.0)))
        for bolt in range(10):  # bearing cap bolt circle
            angle = math.tau * bolt / 10
            hardware.add(*cyl_geo(
                bearing + Vector((side * 1.85, math.cos(angle) * 1.5, math.sin(angle) * 1.5)),
                0.13, 0.3, sides=5, rot=(0.0, math.pi * 0.5, 0.0)))
    mesh_object("Gimbal Bearings", hardware, materials["FOA Dark Steel"], parent, 34.0)

    # Elevation drive: toothed sector arc plus two hydraulic rams, one snapped.
    drive = Geo()
    sector_center = bearings[1] + Vector((1.1, 0.0, 0.0))
    tooth_count = 26
    for tooth in range(tooth_count):
        angle = math.radians(-58.0 + 116.0 * tooth / (tooth_count - 1))
        outer = sector_center + Vector((0.0, math.cos(angle) * 5.3, math.sin(angle) * 5.3))
        drive.add(*box_geo(outer, (0.7, 0.42, 0.62), rot=(angle, 0.0, 0.0)))
    arc = [sector_center + Vector((0.0, math.cos(math.radians(-58.0 + 116.0 * i / 15)) * 4.6,
                                   math.sin(math.radians(-58.0 + 116.0 * i / 15)) * 4.6))
           for i in range(16)]
    drive.add(*tube_geo(arc, 0.5, sides=6))
    drive.add(*cyl_geo(sector_center, 1.0, 1.0, sides=14, rot=(0.0, math.pi * 0.5, 0.0)))

    for side, snapped in ((-1, False), (1, True)):
        base = BUS_CENTER + Vector((side * 4.6, -3.8, 2.2))
        head = Vector((side * 7.4, -1.2, parabola_z(7.4) - truss_depth(7.4) - 0.6))
        if snapped:
            head = base.lerp(head, 0.55) + Vector((1.4, -1.0, -1.2))
        direction = (head - base)
        drive.add(*tube_geo([base, base + direction * 0.55], 0.62, sides=10))
        drive.add(*tube_geo([base + direction * 0.5, head], 0.34, sides=8))
        drive.add(*cyl_geo(base, 0.85, 1.2, sides=12))
    mesh_object("Elevation Drive", drive, materials["FOA Steel"], parent, 34.0)


def build_equipment_bus(parent, materials):
    half_x, half_y, half_z = (dim * 0.5 for dim in BUS_SIZE)
    shell = Geo()
    shell.add(*box_geo(BUS_CENTER, BUS_SIZE))
    # Recessed panel lines: a raised plate grid on the two long faces.
    for sign in (-1, 1):
        for column in range(4):
            for row in range(2):
                offset = Vector((-half_x + 2.1 + column * 3.5,
                                 sign * (half_y + 0.09),
                                 -half_z + 1.7 + row * 2.8))
                shell.add(*box_geo(BUS_CENTER + offset, (3.0, 0.18, 2.3)))
    for column in range(4):  # top deck plates
        shell.add(*box_geo(BUS_CENTER + Vector((-half_x + 2.1 + column * 3.5, 0.0,
                                                half_z + 0.09)), (3.0, 7.6, 0.18)))
    mesh_object("Equipment Bus Shell", shell, materials["FOA Hull Bone"], parent)

    foil = Geo()  # torn multi-layer insulation blanket over the underside
    foil.add(*box_geo(BUS_CENTER + Vector((0.0, 0.0, -half_z - 0.1)), (13.6, 8.2, 0.16)))
    for seam in range(7):
        foil.add(*box_geo(BUS_CENTER + Vector((-6.0 + seam * 2.0, 0.0, -half_z - 0.22)),
                          (0.22, 8.2, 0.16)))
    for seam in range(4):
        foil.add(*box_geo(BUS_CENTER + Vector((0.0, -3.0 + seam * 2.0, -half_z - 0.22)),
                          (13.6, 0.22, 0.16)))
    mesh_object("Bus Thermal Blanket", foil, materials["FOA Gold Foil"], parent)

    greeble = Geo()
    for fin in range(7):  # radiator stack
        greeble.add(*box_geo(BUS_CENTER + Vector((-half_x + 1.6 + fin * 1.9,
                                                  -half_y - 2.3, 0.6)),
                             (0.28, 4.4, 4.2), rot=(math.radians(-14.0), 0.0, 0.0)))
    greeble.add(*box_geo(BUS_CENTER + Vector((0.0, -half_y - 0.5, -1.9)), (13.4, 0.9, 0.6)))
    for louvre in range(5):  # vent louvres on the end cap
        greeble.add(*box_geo(BUS_CENTER + Vector((half_x + 0.25, 0.0, -2.1 + louvre * 1.05)),
                             (0.42, 5.6, 0.34), rot=(math.radians(24.0), 0.0, 0.0)))
    for port in range(12):  # connector bank
        greeble.add(*cyl_geo(BUS_CENTER + Vector((-half_x - 0.3, -2.75 + (port % 6) * 1.1,
                                                  0.9 - (port // 6) * 1.5)),
                             0.32, 0.7, sides=8, rot=(0.0, math.pi * 0.5, 0.0)))
    for thruster in range(4):  # attitude thruster quad
        offset = Vector((-5.4 + (thruster % 2) * 10.8, half_y + 0.6,
                         -2.0 + (thruster // 2) * 4.0))
        greeble.add(*cyl_geo(BUS_CENTER + offset, 0.3, 1.5, sides=10,
                             radius_top=0.72, rot=(math.radians(-90.0), 0.0, 0.0)))
    for tank in (-1, 1):  # propellant tanks slung under the bus
        greeble.add(*lathe_geo([(0.0, -2.3), (1.5, -1.6), (1.8, 0.0), (1.5, 1.6),
                                (0.0, 2.3), (0.0, 2.3)], 20,
                               center=tuple(BUS_CENTER + Vector((tank * 4.3, 5.4, -1.2))),
                               rot=(math.radians(90.0), 0.0, 0.0)))
        greeble.add(*tube_geo([BUS_CENTER + Vector((tank * 4.3, half_y, -1.2)),
                               BUS_CENTER + Vector((tank * 4.3, 5.4, -1.2))], 0.4, sides=8))
    mesh_object("Bus Equipment", greeble, materials["FOA Steel"], parent, 34.0)

    dark = Geo()
    dark.add(*cyl_geo(BUS_CENTER + Vector((3.6, half_y + 0.12, 1.4)), 1.5, 0.4, sides=18,
                      rot=(math.radians(90.0), 0.0, 0.0)))  # access hatch
    dark.add(*box_geo(BUS_CENTER + Vector((3.6, half_y + 0.4, 1.4)), (0.24, 0.4, 1.9)))
    for tracker in (-1, 1):  # star trackers
        dark.add(*cyl_geo(BUS_CENTER + Vector((tracker * 5.5, 2.4, half_z + 1.1)),
                          0.75, 2.0, sides=12, radius_top=0.5,
                          rot=(math.radians(28.0), 0.0, tracker * 0.4)))
    mesh_object("Bus Fittings", dark, materials["FOA Interior Black"], parent, 34.0)

    stripe = Geo()
    stripe.add(*box_geo(BUS_CENTER + Vector((0.0, 0.0, half_z - 0.3)), (15.2, 9.6, 0.5)))
    mesh_object("Bus Warning Band", stripe, materials["FOA Warning Amber"], parent)

    beacon = Geo()
    beacon.add(*lathe_geo([(0.0, 0.0), (0.85, 0.35), (0.85, 1.1), (0.0, 1.5)], 14,
                          center=tuple(BUS_CENTER + Vector((-5.2, -2.0, half_z + 0.4)))))
    mesh_object("Bus Status Beacon", beacon, materials["FOA Accent Teal"], parent, 34.0)


def build_comms_mast(parent, materials):
    """Snapped secondary mast off the bus, carrying the receiver head."""
    root = BUS_CENTER + Vector((-6.4, 3.2, BUS_SIZE[2] * 0.5))
    kink = root + Vector((-2.6, 4.6, MAST_HEIGHT * 0.34))
    tip = kink + Vector((1.8, 3.4, MAST_HEIGHT * 0.22))

    mast = Geo()
    mast.add(*tube_geo([root, kink, tip], 0.5, sides=8, radii=[0.62, 0.44, 0.3]))
    for stay in range(3):  # guy stays, two still attached
        angle = math.tau * stay / 3
        anchor = root + Vector((math.cos(angle) * 3.4, math.sin(angle) * 3.4, -0.6))
        end = kink if stay < 2 else kink.lerp(root, 0.35) + Vector((2.2, -1.6, -1.1))
        mast.add(*tube_geo([anchor, end], 0.1, sides=4))
    mesh_object("Comms Mast", mast, materials["FOA Steel"], parent, 34.0)

    head = Geo()
    head.add(*lathe_geo([(0.0, -1.2), (1.25, -0.7), (1.45, 0.4), (0.9, 1.25), (0.0, 1.5)],
                        18, center=tuple(tip + Vector((0.0, 0.0, 1.1)))))
    mesh_object("Receiver Head", head, materials["FOA Accent Teal"], parent, 34.0)

    rigging = Geo()
    for whip in range(4):  # snapped whip antennas
        angle = math.tau * whip / 4 + 0.5
        base = tip + Vector((math.cos(angle) * 1.1, math.sin(angle) * 1.1, 0.8))
        rigging.add(*tube_geo([base, base + Vector((math.cos(angle) * 1.9,
                                                    math.sin(angle) * 1.9,
                                                    3.2 - whip * 0.55))], 0.08, sides=4))
    mesh_object("Mast Antennas", rigging, materials["FOA Dark Steel"], parent)


# ------------------------------------------------- solar wings, speared into the dunes

def transformed(geo, basis, origin):
    moved = Geo()
    root = Vector(origin)
    moved.verts = [tuple(root + basis @ Vector(v)) for v in geo.verts]
    moved.faces = list(geo.faces)
    return moved


# name, ground entry point, yaw, pitch, roll, exposed length, buried length, width, torn
# Rolls alternate through 180 deg so the cell faces do not all point the same
# way: otherwise every wing shows its bare backing lattice from half the map.
WINGS = [
    ("Solar Wing A", (-30.0, -21.0), -24.0, 66.0, 186.0, WING_LENGTH, 9.5, 13.0, False),
    ("Solar Wing B", (25.0, -30.0), 41.0, 54.0, -9.0, WING_LENGTH * 0.92, 8.0, 12.0, True),
    ("Solar Wing C", (-7.0, -49.0), -68.0, 73.0, 184.0, WING_LENGTH * 0.86, 10.5, 12.0, False),
    ("Solar Fragment D", (35.0, -11.0), 14.0, 47.0, 12.0, WING_LENGTH * 0.42, 5.5, 8.6, True),
    ("Solar Fragment E", (-23.0, -44.0), 101.0, 61.0, -7.0, WING_LENGTH * 0.34, 4.5, 7.4, True),
]


def build_solar_wing(parent, materials, name, entry, yaw, pitch, roll,
                     exposed, buried, width, torn, seed):
    rng = random.Random(seed)
    basis = Euler((math.radians(roll), math.radians(-pitch), math.radians(yaw)),
                  "XYZ").to_matrix()
    origin = Vector((entry[0], entry[1], 0.0))
    half_w = width * 0.5

    substrate = Geo()
    substrate.add(*box_geo(((exposed - buried) * 0.5, 0.0, 0.0),
                           (exposed + buried, width, 0.26)))
    cells = Geo()
    frame = Geo()

    modules_x = max(3, int(round((exposed + buried) / 5.5)))
    modules_y = 2
    cells_x, cells_y = 5, 4
    torn_module = rng.randrange(modules_x - 1) if torn else -1
    # Cells stop just below the sand line: nothing under it is ever seen.
    cell_floor = -buried * 0.5

    for mx in range(modules_x):
        x0 = -buried + (exposed + buried) * mx / modules_x
        x1 = -buried + (exposed + buried) * (mx + 1) / modules_x
        for my in range(modules_y):
            y0 = -half_w + width * my / modules_y
            y1 = -half_w + width * (my + 1) / modules_y
            if mx == torn_module and my == modules_y - 1:
                continue
            for cx in range(cells_x):
                for cy in range(cells_y):
                    ax = x0 + (x1 - x0) * (cx + 0.06) / cells_x
                    bx = x0 + (x1 - x0) * (cx + 0.94) / cells_x
                    if bx < cell_floor:
                        continue
                    ay = y0 + (y1 - y0) * (cy + 0.06) / cells_y
                    by = y0 + (y1 - y0) * (cy + 0.94) / cells_y
                    if torn and rng.random() < 0.045:
                        continue
                    cells.add(*box_geo(((ax + bx) * 0.5, (ay + by) * 0.5, 0.2),
                                       (bx - ax, by - ay, 0.14)))
        frame.add(*tube_geo([(x0, -half_w, 0.0), (x0, half_w, 0.0)], 0.24, sides=6))
        # Stiffener lattice on the shaded back face: without it the underside of
        # a wing reads as a blank slab from every angle the cells face away from.
        if x1 > cell_floor:
            frame.add(*box_geo(((x0 + x1) * 0.5, 0.0, -0.3), (0.42, width, 0.4)))
            for bay in (-1, 1):
                frame.add(*tube_geo([(x0, bay * half_w * 0.86, -0.34),
                                     (x1, -bay * half_w * 0.28, -0.34)], 0.13, sides=5))

    for edge in (-1, 1):  # longeron rails down both long edges
        frame.add(*box_geo(((exposed - buried) * 0.5, edge * half_w * 0.62, -0.3),
                           (exposed + buried, 0.42, 0.4)))
        frame.add(*tube_geo([(-buried, edge * half_w, 0.0),
                             (exposed, edge * half_w, 0.0)], 0.34, sides=7))
    frame.add(*tube_geo([(exposed, -half_w, 0.0), (exposed, half_w, 0.0)], 0.34, sides=7))

    # Hinge yoke at the outboard end, torn off the bus it used to fold against.
    for edge in (-1, 1):
        hinge = Vector((exposed + 0.9, edge * half_w * 0.62, 0.0))
        frame.add(*cyl_geo(hinge, 0.55, 1.3, sides=12, rot=(math.pi * 0.5, 0.0, 0.0)))
        frame.add(*tube_geo([(exposed - 0.2, edge * half_w * 0.62, 0.0), hinge],
                            0.3, sides=6))
    frame.add(*tube_geo([(exposed + 0.9, -half_w * 0.62, 0.0),
                         (exposed + 0.9, half_w * 0.62, 0.0)], 0.26, sides=6))
    boom = Vector((exposed + 0.9, 0.0, 0.0))
    frame.add(*tube_geo([boom, boom + Vector((3.4, rng.uniform(-1.6, 1.6), 1.1))],
                        0.36, sides=7, radii=[0.4, 0.18]))

    shards = Geo()
    if torn:
        for _ in range(9):
            base_x = rng.uniform(max(cell_floor, -buried * 0.3), exposed)
            base = Vector((base_x, rng.choice((-1.0, 1.0)) * half_w, 0.0))
            shards.add(*tube_geo([base, base + Vector((rng.uniform(-1.5, 1.5),
                                                       rng.uniform(-2.4, 2.4),
                                                       rng.uniform(-0.9, 0.9)))],
                                 0.11, sides=4))

    mesh_object(f"{name} Substrate", transformed(substrate, basis, origin),
                materials["FOA Hull Weathered"], parent)
    mesh_object(f"{name} Cells", transformed(cells, basis, origin),
                materials["FOA Solar Cell"], parent)
    mesh_object(f"{name} Frame", transformed(frame, basis, origin),
                materials["FOA Steel"], parent, 34.0)
    if len(shards):
        mesh_object(f"{name} Torn Edge", transformed(shards, basis, origin),
                    materials["FOA Rust"], parent)

    # Sand ploughed up where the panel knifed in.
    mound = Geo()
    mound.add(*mound_geo((entry[0], entry[1], -0.15),
                         width * 0.95, 1.5 + width * 0.06, seed + 17))
    mesh_object(f"{name} Impact Mound", mound, materials["FOA Sand"], parent, 40.0)


# ------------------------------------------------------------ impact terrain

def mound_geo(center, radius, height, seed, segments=24, rings=5, falloff=1.7):
    rng = random.Random(seed)
    origin = Vector(center)
    verts = [tuple(origin + Vector((0.0, 0.0, height)))]
    for ring in range(1, rings + 1):
        span = ring / rings
        for s in range(segments):
            angle = math.tau * s / segments
            ring_radius = radius * span * rng.uniform(0.8, 1.2)
            level = 0.0 if ring == rings else height * (1.0 - span) ** falloff * rng.uniform(0.7, 1.15)
            verts.append(tuple(origin + Vector((math.cos(angle) * ring_radius,
                                                math.sin(angle) * ring_radius, level))))
    faces = []
    for s in range(segments):
        faces.append((0, 1 + s, 1 + (s + 1) % segments))
    for ring in range(rings - 1):
        a, b = 1 + ring * segments, 1 + (ring + 1) * segments
        for s in range(segments):
            nxt = (s + 1) % segments
            faces.append((a + s, b + s, b + nxt, a + nxt))
    return verts, faces


def berm_geo(center, inner_radius, outer_radius, height, seed,
             segments=44, scale=(1.0, 1.0)):
    rng = random.Random(seed)
    origin = Vector(center)
    profile = [(inner_radius, 0.0), (inner_radius * 0.35 + outer_radius * 0.65, height),
               (outer_radius, 0.0)]
    verts = []
    for s in range(segments):
        angle = math.tau * s / segments
        wobble = rng.uniform(0.86, 1.14)
        for ring_radius, level in profile:
            verts.append(tuple(origin + Vector((
                math.cos(angle) * ring_radius * wobble * scale[0],
                math.sin(angle) * ring_radius * wobble * scale[1],
                level * rng.uniform(0.72, 1.2)))))
    faces = []
    count = len(profile)
    for s in range(segments):
        a, b = s * count, ((s + 1) % segments) * count
        for i in range(count - 1):
            faces.append((a + i, a + i + 1, b + i + 1, b + i))
    return verts, faces


def build_impact_terrain(parent, materials):
    terrain = Geo()
    terrain.add(*berm_geo((0.0, 6.0, -0.2), 26.0, 42.0, 2.6, 8801, scale=(1.15, 0.92)))
    for side, offset in ((-1, 0), (1, 1)):  # plough furrow ridges trailing the impact
        for i in range(5):
            terrain.add(*mound_geo((side * (12.0 + i * 1.1), -18.0 - i * 11.0, -0.2),
                                   7.5 - i * 0.6, 2.0 - i * 0.16, 8900 + i * 7 + offset))
    mesh_object("Impact Berm", terrain, materials["FOA Sand"], parent, 44.0)


def build_debris(parent, materials):
    rng = random.Random(31337)
    rubble = Geo()
    shards = Geo()
    metal = Geo()
    for i in range(26):
        angle = rng.uniform(-2.85, -0.3)
        distance = rng.uniform(30.0, 88.0)
        spot = Vector((math.cos(angle) * distance * 1.1,
                       math.sin(angle) * distance, rng.uniform(-0.5, 0.4)))
        size = rng.uniform(0.8, 3.4)
        rubble.add(*rock_geo(spot, (size, size * rng.uniform(0.7, 1.3), size * 0.62),
                             4000 + i))
    for i in range(20):
        angle = rng.uniform(-2.9, -0.25)
        distance = rng.uniform(26.0, 80.0)
        spot = Vector((math.cos(angle) * distance * 1.1,
                       math.sin(angle) * distance, rng.uniform(0.1, 0.8)))
        shards.add(*box_geo(spot, (rng.uniform(2.0, 6.5), rng.uniform(1.6, 4.4), 0.22),
                            rot=(rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5),
                                 rng.uniform(0.0, math.tau))))
    for i in range(9):  # torn hoop and truss offcuts
        angle = rng.uniform(-2.8, -0.4)
        distance = rng.uniform(32.0, 74.0)
        base = Vector((math.cos(angle) * distance * 1.1,
                       math.sin(angle) * distance, rng.uniform(0.2, 1.0)))
        arc = [base + Vector((math.cos(t) * 6.0, math.sin(t) * 6.0,
                              math.sin(t * 2.0) * 0.8))
               for t in [rng.uniform(0.0, 1.0) + k * 0.34 for k in range(5)]]
        metal.add(*tube_geo(arc, rng.uniform(0.16, 0.34), sides=6))
    mesh_object("Impact Rubble", rubble, materials["FOA Concrete Gray"], parent, 40.0)
    mesh_object("Panel Shards", shards, materials["FOA Solar Cell"], parent)
    mesh_object("Structural Offcuts", metal, materials["FOA Rust"], parent, 34.0)


def build_cabling(parent, materials, dish_frame):
    """Conduit runs spilling out of the bus and dragging across the sand."""
    rng = random.Random(5150)
    cables = Geo()
    matrix = dish_frame.matrix_local
    for i in range(3):
        # Leave the bus low on its side wall and run mostly horizontally. Thin
        # cables dropping from height under the hull just read as stilts holding
        # the bus up off the sand.
        start = matrix @ (BUS_CENTER + Vector((-4.4 + i * 4.4, -BUS_SIZE[1] * 0.5, -1.2)))
        landing = Vector((start.x + rng.uniform(-3.0, 3.0),
                          start.y - rng.uniform(6.0, 9.0), 0.38))
        trail = landing + Vector((rng.uniform(-10.0, 10.0), -rng.uniform(8.0, 16.0), -0.04))
        tail = trail + Vector((rng.uniform(-9.0, 9.0), -rng.uniform(4.0, 12.0), 0.0))
        cables.add(*tube_geo([start, start.lerp(landing, 0.55) + Vector((0.0, 0.0, 0.5)),
                              landing, trail, tail],
                             rng.uniform(0.22, 0.32), sides=6))
    mesh_object("Spilled Conduit", cables, materials["FOA Interior Black"], parent, 34.0)


# ----------------------------------------------------------------- assembly

def world_min_z(*objects):
    levels = []
    for obj in objects:
        if obj is None or obj.type != "MESH":
            continue
        levels += [(obj.matrix_world @ Vector(corner)).z for corner in obj.bound_box]
    return 0.0 if not levels else min(levels)


def build_array(materials):
    root = empty("Fallen Orbital Array")
    dish_frame = empty("Orbital Array Impact Frame", root,
                       rotation=(math.radians(-DISH_TILT - 6.0), 0.0, math.radians(-7.0)))

    build_reflector(dish_frame, materials)
    build_backing_structure(dish_frame, materials)
    build_rim(dish_frame, materials)
    build_feed_assembly(dish_frame, materials)
    build_gimbal(dish_frame, materials)
    build_equipment_bus(dish_frame, materials)
    build_comms_mast(dish_frame, materials)

    # Bed the leading *rim* into the dunes by the authored burial depth. Anchoring
    # on the whole frame instead lands the assembly on its backing truss, which
    # leaves the rim and the bus hovering clear of the sand.
    bpy.context.view_layer.update()
    dish_frame.location.z += -BURIAL_DEPTH - world_min_z(bpy.data.objects["Dish Rim"])
    bpy.context.view_layer.update()

    for index, (name, entry, yaw, pitch, roll, exposed, buried, width, torn) in enumerate(WINGS):
        build_solar_wing(root, materials, name, entry, yaw, pitch, roll,
                         exposed, buried, width, torn, 6100 + index * 31)

    build_impact_terrain(root, materials)
    build_debris(root, materials)
    build_cabling(root, materials, dish_frame)
    bpy.context.view_layer.update()
    return root


def unity_lowest_bound(root):
    """Mirror Unity's grounding probe: mesh-local AABB corners in world space.

    GroundPrefabToDunes measures renderer.localBounds, not exact vertices, so a
    vertex-accurate figure would leave the landmark hovering by the difference.
    """
    lowest = 0.0
    for obj in root.children_recursive:
        if obj.type != "MESH" or not obj.data.vertices:
            continue
        coords = [v.co for v in obj.data.vertices]
        low = Vector((min(c.x for c in coords), min(c.y for c in coords),
                      min(c.z for c in coords)))
        high = Vector((max(c.x for c in coords), max(c.y for c in coords),
                       max(c.z for c in coords)))
        for corner in range(8):
            point = Vector((low.x if corner & 1 == 0 else high.x,
                            low.y if corner & 2 == 0 else high.y,
                            low.z if corner & 4 == 0 else high.z))
            lowest = min(lowest, (obj.matrix_world @ point).z)
    return lowest


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
        export_animations=False,
    )


def setup_preview(materials):
    bpy.ops.mesh.primitive_plane_add(size=420.0, location=(0.0, -10.0, -0.25))
    ground = bpy.context.object
    ground.name = "Preview Desert Ground"
    ground.data.materials.append(materials["FOA Sand"])

    # Broadside from +X: the dish reads as a dish, the bus and gimbal stay
    # unoccluded, and the wing field recedes into frame behind it. Head-on to the
    # reflector face hides the bus; from -Y you only get the backing truss.
    bpy.ops.object.camera_add(location=(152.0, -12.0, 48.0))
    camera = bpy.context.object
    camera.name = "Orbital Array Hero Camera"
    direction = Vector((-4.0, -20.0, 11.0)) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera.data.lens = 42.0
    bpy.context.scene.camera = camera

    bpy.ops.object.light_add(type="SUN", location=(70.0, 30.0, 96.0))
    sun = bpy.context.object
    sun.name = "Desert Key Sun"
    sun.data.energy = 4.4
    sun.data.color = (1.0, 0.90, 0.74)
    sun.data.angle = math.radians(2.4)
    sun.rotation_euler = (Vector((-10.0, -20.0, 8.0)) - sun.location).to_track_quat("-Z", "Y").to_euler()

    def area_light(name, location, color, energy, size):
        bpy.ops.object.light_add(type="AREA", location=location)
        light = bpy.context.object
        light.name = name
        light.data.energy = energy
        light.data.color = color
        light.data.shape = "DISK"
        light.data.size = size
        light.rotation_euler = (Vector((0.0, -8.0, 12.0)) - Vector(location)) \
            .to_track_quat("-Z", "Y").to_euler()

    area_light("Sky Fill", (20.0, 90.0, 80.0), (0.55, 0.66, 0.86), 40000.0, 70.0)
    area_light("Sand Bounce", (-80.0, -50.0, 18.0), (1.0, 0.70, 0.40), 30000.0, 60.0)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1500
    scene.render.resolution_y = 950
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(PREVIEW_PATH)
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("Orbital Array World")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (0.42, 0.50, 0.62, 1.0)
        background.inputs["Strength"].default_value = 0.85
    try:
        scene.view_settings.view_transform = "AgX"
        scene.view_settings.exposure = 0.9
    except TypeError:
        pass


def main():
    clear_scene()
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    materials = make_palette()
    root = build_array(materials)
    export_model(root)
    setup_preview(materials)

    BLEND_PATH.parent.mkdir(parents=True, exist_ok=True)
    PREVIEW_PATH.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    bpy.ops.render.render(write_still=True)

    meshes = [o for o in root.children_recursive if o.type == "MESH"]
    return {
        "objects": len(meshes),
        # Depth of the lowest rendered bound below the origin. This is what
        # OrbitalBurialDepth would need to be to land the origin exactly on the
        # sand -- but only over FLAT ground. Grounding actually uses the lowest
        # terrain sampled across the whole footprint, and that footprint spans
        # several dunes, so the shipped setting is well under this figure.
        # Treat it as the ceiling, not the target.
        "flat_ground_OrbitalBurialDepth": round(-unity_lowest_bound(root), 2),
        "tris": sum(len(o.data.loop_triangles) if o.data.loop_triangles
                    else sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes),
        "faces": sum(len(o.data.polygons) for o in meshes),
        "glb": str(MODEL_PATH),
        "preview": str(PREVIEW_PATH),
        "blend": str(BLEND_PATH),
    }


if __name__ == "__main__":
    main()
