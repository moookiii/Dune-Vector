"""Generate the "Ancient Spire" landmark for Dune Vector.

Replaces the procedural Unity version in `DuneVectorLandmarks.BuildSpire`,
which is nine rotated cubes stacked into a taper. This is authored to the same
integration contract so it drops straight onto the existing landmark socket:

  * Blender is Z-up; the export maps Blender +Y -> Unity +Z and
    Blender +Z -> Unity +Y.
  * One Blender unit is one Unity unit *before* `SpireScale` (1.3) is applied,
    so every dimension here is read straight from Dune Vector Runtime Settings:
        SpireHeight            96    -> masonry stops at z = 88, crown at 96
        SpireBaseRingRadius    18    -> ground circuit inlay radius
        SpireBaseRingSegments  12    -> arc count in that inlay
        SpireMonolithCount      4    -> hovering monoliths at z = 96 * 0.58
        SpireShardCount         5    -> relic shards at z = 96 + 12
    The tower footprint stays inside the 18 unit half-width the old layer stack
    occupied, so landmark exclusion radii and contract sockets do not move.
  * Objects named `Spire_Relic`, `Spire_Shard_*` and `Spire_Monolith_*` are the
    animated parts. They are kept out of the material merge so Unity can still
    spin, bob and pulse them individually.

Textures come from the PBR sets already in `Assets/DuneVector/Resources`, so
nothing new is added to the project and the GLB carries no embedded copies.

No `bpy.ops` calls are used for geometry, so the script runs safely from the
Blender MCP add-on's timer context. Meshes are built from raw vertex/face data
with deterministic box-projected UVs.
"""

import math
import os
import random

import bpy
from mathutils import Euler, Vector

# ---------------------------------------------------------------------------
# Paths.

ROOT = r"C:\Dune Vector URP"
SOURCE_DIR = os.path.join(ROOT, "ArtSource", "Blender", "AncientSpire")
ASSET_DIR = os.path.join(ROOT, "Assets", "DuneVector", "Resources", "AncientSpire")
TEXTURE_LIB = os.path.join(ROOT, "Assets", "DuneVector", "Resources")
MODEL_PATH = os.path.join(ASSET_DIR, "AncientSpire.glb")
BLEND_PATH = os.path.join(SOURCE_DIR, "AncientSpire.blend")
PREVIEW_PATH = os.path.join(SOURCE_DIR, "AncientSpirePreview.png")

for _path in (SOURCE_DIR, ASSET_DIR):
    os.makedirs(_path, exist_ok=True)

EXPORT_COLLECTION = "Ancient Spire"
PREVIEW_COLLECTION = "Preview Rig"

# Parts that Unity animates. Excluded from the merge so they stay addressable.
DYNAMIC_PREFIXES = ("Spire_Relic", "Spire_Shard", "Spire_Monolith")

# ---------------------------------------------------------------------------
# Integration constants. These mirror Dune Vector Runtime Settings.asset.

SPIRE_HEIGHT = 96.0         # SpireHeight
BASE_RING_RADIUS = 18.0     # SpireBaseRingRadius
BASE_RING_SEGMENTS = 12     # SpireBaseRingSegments
BASE_RING_THICKNESS = 0.38  # SpireBaseRingThickness
MONOLITH_COUNT = 4          # SpireMonolithCount
SHARD_COUNT = 5             # SpireShardCount
RELIC_Z = SPIRE_HEIGHT + 12.0
MONOLITH_Z = SPIRE_HEIGHT * 0.58
MONOLITH_RADIUS = 15.0

# ---------------------------------------------------------------------------
# Tower profile.

SHAFT_Z0 = 5.60             # Top of the stepped plinth.
SHAFT_Z1 = 88.00            # Where masonry ends and the crown begins.
CROWN_Z1 = SPIRE_HEIGHT
SHAFT_R0 = 11.50            # Half-width at the base, matching the old layer 1.
SHAFT_R1 = 2.55
SHAFT_ENTASIS = 0.82        # <1 gives the concave sweep of a carved spire.
SHAFT_SEGMENTS = 72         # 8 faces x 9 samples, enough to resolve the flutes.
COURSE_COUNT = 22
FLUTE_DEPTH = 0.085         # Fraction of the section radius scooped per face.
FACE_ANGLE = math.pi / 4.0  # Octagonal cross-section.

GALLERY_Z = 54.00           # Underside of the cantilevered gallery deck.
GALLERY_DECK_TOP = 55.60
GALLERY_OUTER = 9.60
CORNICE_Z = 22.00           # Lower cornice band that breaks up the shaft.

# World-space texture repeat, in metres, per material family.
UV_REPEAT_STONE = 6.0
UV_REPEAT_DETAIL = 3.0
UV_REPEAT_METAL = 2.0
UV_REPEAT_SAND = 10.0

SEED = 20260810
rng = random.Random(SEED)


# ---------------------------------------------------------------------------
# Scene setup.

def purge_scene():
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def make_collection(name):
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    return collection


# ---------------------------------------------------------------------------
# UV projection.

def _polygon_normal(verts, face):
    """Newell's method, stable for the n-gon caps the rings produce."""
    nx = ny = nz = 0.0
    count = len(face)
    for i in range(count):
        current = verts[face[i]]
        following = verts[face[(i + 1) % count]]
        nx += (current[1] - following[1]) * (current[2] + following[2])
        ny += (current[2] - following[2]) * (current[0] + following[0])
        nz += (current[0] - following[0]) * (current[1] + following[1])
    return nx, ny, nz


def box_project_uvs(verts, faces, scale):
    """Flat per-loop UV list using dominant-axis box projection."""
    uvs = []
    for face in faces:
        nx, ny, nz = _polygon_normal(verts, face)
        ax, ay, az = abs(nx), abs(ny), abs(nz)
        if ax >= ay and ax >= az:
            first, second, flip = 1, 2, nx < 0.0
        elif ay >= ax and ay >= az:
            first, second, flip = 0, 2, ny > 0.0
        else:
            first, second, flip = 0, 1, nz < 0.0
        for index in face:
            vertex = verts[index]
            u = vertex[first] * scale
            v = vertex[second] * scale
            uvs.append(-u if flip else u)
            uvs.append(v)
    return uvs


def cylindrical_uvs(verts, faces, scale):
    """Unwrap around Z. The shaft needs this: box projection would seam the
    masonry courses eight times around, once per dominant-axis switch."""
    uvs = []
    for face in faces:
        _nx, _ny, nz = _polygon_normal(verts, face)
        horizontal = abs(nz) > max(abs(_nx), abs(_ny))
        angles = []
        for index in face:
            x, y, _z = verts[index]
            angles.append(math.atan2(y, x))
        # Keep a face from wrapping the whole way round at the atan2 seam.
        pivot = angles[0]
        for index, angle in zip(face, angles):
            x, y, z = verts[index]
            if horizontal:
                uvs.append(x * scale)
                uvs.append(y * scale)
                continue
            shifted = angle
            while shifted - pivot > math.pi:
                shifted -= 2.0 * math.pi
            while shifted - pivot < -math.pi:
                shifted += 2.0 * math.pi
            radius = math.hypot(x, y)
            uvs.append(shifted * radius * scale)
            uvs.append(z * scale)
    return uvs


# ---------------------------------------------------------------------------
# Mesh creation.

def add_mesh(name, verts, faces, material, collection,
             location=(0.0, 0.0, 0.0), rotation=None, quaternion=None,
             bevel=0.06, bevel_segments=2, smooth_faces=None,
             uv_scale=None, uvs=None, uv_mode='BOX'):
    """Create a single-material mesh object from raw vertex/face data."""
    mesh = bpy.data.meshes.new(name + " Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()

    if uv_scale is None:
        uv_scale = 1.0 / UV_REPEAT_STONE
    if uvs is None:
        if uv_mode == 'CYLINDRICAL':
            uvs = cylindrical_uvs(verts, faces, uv_scale)
        else:
            uvs = box_project_uvs(verts, faces, uv_scale)
    mesh.uv_layers.new(name="UVMap").data.foreach_set("uv", uvs)

    if smooth_faces == "ALL":
        for polygon in mesh.polygons:
            polygon.use_smooth = True
    elif smooth_faces:
        smooth_lookup = set(smooth_faces)
        for index, polygon in enumerate(mesh.polygons):
            if index in smooth_lookup:
                polygon.use_smooth = True

    obj = bpy.data.objects.new(name, mesh)
    obj.data.materials.append(material)
    collection.objects.link(obj)
    obj.location = location
    if quaternion is not None:
        obj.rotation_mode = 'QUATERNION'
        obj.rotation_quaternion = quaternion
    elif rotation is not None:
        obj.rotation_euler = Euler(rotation, 'XYZ')

    if bevel and bevel > 0.0:
        modifier = obj.modifiers.new("Edge Bevel", 'BEVEL')
        modifier.width = bevel
        modifier.segments = bevel_segments
        modifier.limit_method = 'ANGLE'
        modifier.angle_limit = math.radians(38.0)
        modifier.miter_outer = 'MITER_ARC'
    return obj


# ---------------------------------------------------------------------------
# Primitive generators (local space; the object transform places them).

def box_data(size):
    hx, hy, hz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
    verts = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
             (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
             (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return verts, faces


def wedge_data(length, width_root, width_tip, height_root, height_tip):
    """Radial buttress fin: tall and wide at the tower, thin at the far end."""
    hr, ht = width_root * 0.5, width_tip * 0.5
    verts = [(0.0, -hr, 0.0), (0.0, hr, 0.0), (length, ht, 0.0), (length, -ht, 0.0),
             (0.0, -hr, height_root), (0.0, hr, height_root),
             (length, ht, height_tip), (length, -ht, height_tip)]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
             (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return verts, faces


def prism_data(radius_bottom, radius_top, height, sides, twist=0.0, phase=0.0):
    """Regular n-gon prism centred on the local origin, extending along Z."""
    half = height * 0.5
    verts = []
    for i in range(sides):
        angle = phase + (2.0 * math.pi * i) / sides
        verts.append((math.cos(angle) * radius_bottom, math.sin(angle) * radius_bottom, -half))
    for i in range(sides):
        angle = phase + twist + (2.0 * math.pi * i) / sides
        verts.append((math.cos(angle) * radius_top, math.sin(angle) * radius_top, half))
    faces = []
    for i in range(sides):
        n = (i + 1) % sides
        faces.append((i, n, sides + n, sides + i))
    faces.append(tuple(range(sides - 1, -1, -1)))
    faces.append(tuple(range(sides, sides * 2)))
    return verts, faces


def annulus_data(inner_radius, outer_radius, height, segments, phase=0.0):
    """Hollow ring solid centred on the local origin, extending along Z."""
    half = height * 0.5
    verts = []
    for z in (-half, half):
        for radius in (inner_radius, outer_radius):
            for i in range(segments):
                angle = phase + (2.0 * math.pi * i) / segments
                verts.append((math.cos(angle) * radius, math.sin(angle) * radius, z))

    def idx(layer, ring, i):
        return layer * segments * 2 + ring * segments + (i % segments)

    faces = []
    for i in range(segments):
        n = (i + 1) % segments
        faces.append((idx(1, 0, i), idx(1, 1, i), idx(1, 1, n), idx(1, 0, n)))
        faces.append((idx(0, 0, n), idx(0, 1, n), idx(0, 1, i), idx(0, 0, i)))
        faces.append((idx(0, 1, i), idx(0, 1, n), idx(1, 1, n), idx(1, 1, i)))
        faces.append((idx(0, 0, n), idx(0, 0, i), idx(1, 0, i), idx(1, 0, n)))
    return verts, faces


def arc_data(inner_radius, outer_radius, height, start_deg, sweep_deg, segments):
    """Open arc of an annulus, used for the broken ground circuit and rails."""
    half = height * 0.5
    steps = max(2, segments)
    verts = []
    for z in (-half, half):
        for radius in (inner_radius, outer_radius):
            for i in range(steps + 1):
                angle = math.radians(start_deg + sweep_deg * i / steps)
                verts.append((math.cos(angle) * radius, math.sin(angle) * radius, z))

    ring = steps + 1

    def idx(layer, r, i):
        return layer * ring * 2 + r * ring + i

    faces = []
    for i in range(steps):
        faces.append((idx(1, 0, i), idx(1, 1, i), idx(1, 1, i + 1), idx(1, 0, i + 1)))
        faces.append((idx(0, 0, i + 1), idx(0, 1, i + 1), idx(0, 1, i), idx(0, 0, i)))
        faces.append((idx(0, 1, i), idx(0, 1, i + 1), idx(1, 1, i + 1), idx(1, 1, i)))
        faces.append((idx(0, 0, i + 1), idx(0, 0, i), idx(1, 0, i), idx(1, 0, i + 1)))
    faces.append((idx(0, 0, 0), idx(0, 1, 0), idx(1, 1, 0), idx(1, 0, 0)))
    faces.append((idx(0, 1, steps), idx(0, 0, steps), idx(1, 0, steps), idx(1, 1, steps)))
    return verts, faces


def torus_data(major_radius, minor_radius, major_segments, minor_segments):
    verts = []
    for i in range(major_segments):
        theta = (2.0 * math.pi * i) / major_segments
        cx, cy = math.cos(theta), math.sin(theta)
        for j in range(minor_segments):
            phi = (2.0 * math.pi * j) / minor_segments
            r = major_radius + minor_radius * math.cos(phi)
            verts.append((cx * r, cy * r, minor_radius * math.sin(phi)))
    faces = []
    for i in range(major_segments):
        ni = (i + 1) % major_segments
        for j in range(minor_segments):
            nj = (j + 1) % minor_segments
            faces.append((i * minor_segments + j, ni * minor_segments + j,
                          ni * minor_segments + nj, i * minor_segments + nj))
    return verts, faces


def rock_data(radius, seed, rings=7, segments=10, squash=0.68):
    """Lumpy boulder: a sphere pushed around by a few low-frequency lobes."""
    local = random.Random(seed)
    lobes = [(local.uniform(0.0, math.tau), local.uniform(0.0, math.pi),
              local.uniform(0.16, 0.34)) for _ in range(4)]

    def deform(theta, phi):
        scale = 1.0
        for lobe_theta, lobe_phi, amount in lobes:
            weight = (math.cos(theta - lobe_theta) * math.sin(phi)
                      + math.cos(phi - lobe_phi)) * 0.5
            scale += amount * weight
        return radius * max(0.45, scale)

    verts = [(0.0, 0.0, deform(0.0, 0.0) * squash)]
    for r in range(1, rings):
        phi = math.pi * r / rings
        for s in range(segments):
            theta = (2.0 * math.pi * s) / segments
            rad = deform(theta, phi)
            verts.append((math.cos(theta) * math.sin(phi) * rad,
                          math.sin(theta) * math.sin(phi) * rad,
                          math.cos(phi) * rad * squash))
    verts.append((0.0, 0.0, -deform(0.0, math.pi) * squash))

    faces = []
    bottom = len(verts) - 1
    for s in range(segments):
        n = (s + 1) % segments
        faces.append((0, 1 + n, 1 + s))
    for r in range(rings - 2):
        base = 1 + r * segments
        following = base + segments
        for s in range(segments):
            n = (s + 1) % segments
            faces.append((base + s, base + n, following + n, following + s))
    last = 1 + (rings - 2) * segments
    for s in range(segments):
        n = (s + 1) % segments
        faces.append((bottom, last + s, last + n))
    return verts, faces


def crystal_data(radius, height, sides=8, waist=0.34, twist=0.0):
    """Faceted bipyramid with a straight waist: the relic and its shards."""
    half = height * 0.5
    verts = [(0.0, 0.0, half)]
    for level, (z, r) in enumerate(((half * waist, radius),
                                    (-half * waist, radius * 0.92))):
        for i in range(sides):
            angle = twist * level + (2.0 * math.pi * i) / sides
            verts.append((math.cos(angle) * r, math.sin(angle) * r, z))
    verts.append((0.0, 0.0, -half))

    faces = []
    top, bottom = 0, len(verts) - 1
    upper = 1
    lower = 1 + sides
    for i in range(sides):
        n = (i + 1) % sides
        faces.append((top, upper + n, upper + i))
        faces.append((upper + i, upper + n, lower + n, lower + i))
        faces.append((bottom, lower + i, lower + n))
    return verts, faces


def loft(rings, cap_bottom=True, cap_top=True):
    """Quad-strip a list of equal-length vertex rings into a closed tube."""
    count = len(rings[0])
    verts = []
    for ring in rings:
        verts.extend(ring)
    faces = []
    for r in range(len(rings) - 1):
        base = r * count
        following = base + count
        for i in range(count):
            n = (i + 1) % count
            faces.append((base + i, base + n, following + n, following + i))
    if cap_bottom:
        faces.append(tuple(range(count - 1, -1, -1)))
    if cap_top:
        faces.append(tuple(range(len(verts) - count, len(verts))))
    return verts, faces


def sweep_rect(sections, cap_ends=True):
    """Sweep a quad cross-section along a list of 4-corner sections."""
    verts = []
    for section in sections:
        verts.extend(section)
    faces = []
    for s in range(len(sections) - 1):
        base = s * 4
        following = base + 4
        for i in range(4):
            n = (i + 1) % 4
            faces.append((base + i, base + n, following + n, following + i))
    if cap_ends:
        faces.append((3, 2, 1, 0))
        faces.append(tuple(range(len(verts) - 4, len(verts))))
    return verts, faces


def tube_data(points, radius, sides=8, close_ends=True):
    """Sweep a circle along a polyline with parallel-transported frames."""
    path = [Vector(point) for point in points]
    tangents = []
    for index in range(len(path)):
        if index == 0:
            tangent = path[1] - path[0]
        elif index == len(path) - 1:
            tangent = path[-1] - path[-2]
        else:
            tangent = path[index + 1] - path[index - 1]
        tangents.append(tangent.normalized() if tangent.length > 1e-9
                        else Vector((0.0, 0.0, 1.0)))

    reference = tangents[0].orthogonal().normalized()
    verts = []
    for centre, tangent in zip(path, tangents):
        reference = (reference - tangent * reference.dot(tangent))
        if reference.length < 1e-6:
            reference = tangent.orthogonal()
        reference.normalize()
        binormal = tangent.cross(reference)
        for side in range(sides):
            angle = (2.0 * math.pi * side) / sides
            verts.append(tuple(centre
                               + reference * (math.cos(angle) * radius)
                               + binormal * (math.sin(angle) * radius)))
    faces = []
    for ring in range(len(path) - 1):
        base = ring * sides
        following = base + sides
        for side in range(sides):
            n = (side + 1) % sides
            faces.append((base + side, base + n, following + n, following + side))
    if close_ends:
        faces.append(tuple(range(sides - 1, -1, -1)))
        faces.append(tuple(range(len(verts) - sides, len(verts))))
    return verts, faces


def cloth_panel_data(width, height, columns, rows, sag, wave, tatter_seed):
    """Hanging banner: sags between its corners, ripples, and frays along the
    bottom edge so it reads as centuries old rather than freshly hung."""
    local = random.Random(tatter_seed)
    tears = [local.uniform(0.0, 1.0) for _ in range(columns + 1)]
    verts = []
    for r in range(rows + 1):
        v = r / rows
        for c in range(columns + 1):
            u = c / columns
            # Ease the deformation in from the rod so the top edge stays straight.
            fall = v * v
            x = (u - 0.5) * width
            y = (math.sin(u * math.pi * 2.4 + v * 1.8) * wave
                 + math.sin(v * math.pi * 1.3) * sag) * fall
            frayed = 1.0 if r < rows else (0.72 + 0.28 * tears[c])
            z = -v * height * frayed
            verts.append((x, y, z))
    faces = []
    stride = columns + 1
    for r in range(rows):
        for c in range(columns):
            a = r * stride + c
            faces.append((a, a + 1, a + stride + 1, a + stride))
    return verts, faces


# ---------------------------------------------------------------------------
# Tower profile helpers. Everything that attaches to the shaft goes through
# these so the ribs, gallery and crown follow the same lean and taper.

def shaft_radius(z):
    t = (z - SHAFT_Z0) / (SHAFT_Z1 - SHAFT_Z0)
    t = max(0.0, t)
    return SHAFT_R0 + (SHAFT_R1 - SHAFT_R0) * (t ** SHAFT_ENTASIS)


def shaft_centre(z):
    """A settled tower is never plumb. A slight cumulative lean plus a bow."""
    t = max(0.0, min(1.15, (z - SHAFT_Z0) / (SHAFT_Z1 - SHAFT_Z0)))
    lean = 0.45 * t + 0.30 * math.sin(t * math.pi)
    return (lean * 0.80, -lean * 0.35)


def shaft_twist(z):
    t = max(0.0, (z - SHAFT_Z0) / (SHAFT_Z1 - SHAFT_Z0))
    return math.radians(7.5) * t


def section_radius(theta, radius, flute=1.0):
    """Octagonal section with a scooped flute across the middle of each face."""
    local = ((theta + FACE_ANGLE * 0.5) % FACE_ANGLE) - FACE_ANGLE * 0.5
    out = radius / math.cos(local)
    if flute > 0.0:
        u = local / (FACE_ANGLE * 0.5)
        out -= radius * FLUTE_DEPTH * flute * (0.5 + 0.5 * math.cos(math.pi * u))
    return out


def shaft_ring(z, scale=1.0, flute=1.0, segments=SHAFT_SEGMENTS, inflate=0.0):
    radius = shaft_radius(z) * scale + inflate
    cx, cy = shaft_centre(z)
    twist = shaft_twist(z)
    ring = []
    for i in range(segments):
        theta = (2.0 * math.pi * i) / segments
        r = section_radius(theta, radius, flute)
        ring.append((cx + r * math.cos(theta + twist),
                     cy + r * math.sin(theta + twist), z))
    return ring


def shaft_point(z, theta, radial_offset=0.0, flute=0.0):
    """A point on (or just off) the shaft surface at a given local angle."""
    radius = shaft_radius(z)
    cx, cy = shaft_centre(z)
    twist = shaft_twist(z)
    r = section_radius(theta, radius, flute) + radial_offset
    return (cx + r * math.cos(theta + twist),
            cy + r * math.sin(theta + twist), z)


CORNER_ANGLES = [FACE_ANGLE * 0.5 + FACE_ANGLE * i for i in range(8)]
FACE_ANGLES = [FACE_ANGLE * i for i in range(8)]


# ---------------------------------------------------------------------------
# Materials.

TEXTURE_SETS = {
    "stone": ("Rock062_2K-JPG", "Rock062_2K-JPG", True),
    "stone_dark": ("Rock029_2K-JPG", "Rock029_2K-JPG", True),
    "carved": ("Concrete025_2K-JPG", "Concrete025_2K-JPG", True),
    "bronze": ("Metal049B_4K-JPG", "Metal049B_4K-JPG", False),
    "plate": ("MetalPlates005_4K-JPG", "MetalPlates005_4K-JPG", False),
    "sand": ("Ground093C_2K-JPG", "Ground093C_2K-JPG", True),
}


def load_texture(folder, stem, suffix, non_color):
    path = os.path.join(TEXTURE_LIB, folder, "{:s}_{:s}.jpg".format(stem, suffix))
    if not os.path.exists(path):
        return None
    key = os.path.basename(path)
    image = bpy.data.images.get(key)
    if image is None:
        image = bpy.data.images.load(path, check_existing=True)
        image.name = key
    if non_color:
        image.colorspace_settings.name = 'Non-Color'
    return image


def build_material(name, base_color, metallic, roughness, texture_key=None,
                   repeat=UV_REPEAT_STONE, tint=None, emission=None,
                   emission_strength=6.0, alpha=1.0):
    """Principled material wired to one of the project's ambientCG sets.

    `tint` multiplies the albedo. The rock sets are neutral grey-brown; the
    landmark palette in Dune Vector Runtime Settings is a warm terracotta
    (LandmarkStoneColor 0.62, 0.39, 0.20), so the tint is what keeps the new
    spire reading as the same building as the rest of the desert.
    """
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.diffuse_color = (*base_color, 1.0)
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    principled = nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = (*base_color, 1.0)
    principled.inputs["Metallic"].default_value = metallic
    principled.inputs["Roughness"].default_value = roughness
    if alpha < 1.0:
        principled.inputs["Alpha"].default_value = alpha
        material.blend_method = 'BLEND'
    if emission:
        principled.inputs["Emission Color"].default_value = (*emission, 1.0)
        principled.inputs["Emission Strength"].default_value = emission_strength

    if texture_key is None:
        return material

    folder, stem, has_ao = TEXTURE_SETS[texture_key]
    # UVs are already authored in world-space metres divided by the repeat, so
    # the mapping node only has to correct for texture sets that want a
    # different density than the family default.
    coords = nodes.new("ShaderNodeTexCoord")
    coords.location = (-1400, 0)
    mapping = nodes.new("ShaderNodeMapping")
    mapping.location = (-1200, 0)
    mapping.inputs["Scale"].default_value = (UV_REPEAT_STONE / repeat,) * 3
    links.new(coords.outputs["UV"], mapping.inputs["Vector"])

    def image_node(suffix, y, non_color):
        image = load_texture(folder, stem, suffix, non_color)
        if image is None:
            return None
        node = nodes.new("ShaderNodeTexImage")
        node.image = image
        node.location = (-960, y)
        node.extension = 'REPEAT'
        links.new(mapping.outputs["Vector"], node.inputs["Vector"])
        return node

    albedo = image_node("Color", 320, False)
    if albedo is not None:
        source = albedo.outputs["Color"]
        if has_ao:
            occlusion = image_node("AmbientOcclusion", 620, True)
            if occlusion is not None:
                ao_mix = nodes.new("ShaderNodeMix")
                ao_mix.data_type = 'RGBA'
                ao_mix.blend_type = 'MULTIPLY'
                ao_mix.location = (-680, 470)
                ao_mix.inputs["Factor"].default_value = 0.65
                links.new(source, ao_mix.inputs[6])
                links.new(occlusion.outputs["Color"], ao_mix.inputs[7])
                source = ao_mix.outputs[2]
        if tint is not None:
            tint_mix = nodes.new("ShaderNodeMix")
            tint_mix.data_type = 'RGBA'
            tint_mix.blend_type = 'MULTIPLY'
            tint_mix.location = (-430, 320)
            tint_mix.inputs["Factor"].default_value = 1.0
            links.new(source, tint_mix.inputs[6])
            tint_mix.inputs[7].default_value = (*tint, 1.0)
            source = tint_mix.outputs[2]
        links.new(source, principled.inputs["Base Color"])

    roughness_node = image_node("Roughness", 40, True)
    if roughness_node is not None:
        curve = nodes.new("ShaderNodeMapRange")
        curve.location = (-680, 40)
        curve.inputs["To Min"].default_value = max(0.0, roughness - 0.22)
        curve.inputs["To Max"].default_value = min(1.0, roughness + 0.22)
        links.new(roughness_node.outputs["Color"], curve.inputs["Value"])
        links.new(curve.outputs["Result"], principled.inputs["Roughness"])

    metal_node = image_node("Metalness", -240, True)
    if metal_node is not None:
        links.new(metal_node.outputs["Color"], principled.inputs["Metallic"])

    normal_image = image_node("NormalGL", -520, True)
    if normal_image is not None:
        normal_map = nodes.new("ShaderNodeNormalMap")
        normal_map.location = (-430, -520)
        normal_map.inputs["Strength"].default_value = 1.15
        links.new(normal_image.outputs["Color"], normal_map.inputs["Color"])
        links.new(normal_map.outputs["Normal"], principled.inputs["Normal"])
    return material


def build_materials():
    # LandmarkAccentEmission is (0.08, 3.2, 5.0): a cold cyan against the sand.
    accent = (0.02, 0.35, 0.42)
    return {
        "stone": build_material(
            "AncientSpire_Stone", (0.62, 0.39, 0.20), 0.0, 0.80,
            texture_key="stone", repeat=UV_REPEAT_STONE, tint=(1.02, 0.78, 0.52)),
        "stone_dark": build_material(
            "AncientSpire_StoneDark", (0.34, 0.22, 0.13), 0.0, 0.86,
            texture_key="stone_dark", repeat=UV_REPEAT_STONE, tint=(0.72, 0.52, 0.36)),
        "carved": build_material(
            "AncientSpire_Carved", (0.72, 0.61, 0.43), 0.0, 0.68,
            texture_key="carved", repeat=UV_REPEAT_DETAIL, tint=(1.05, 0.90, 0.68)),
        "bronze": build_material(
            "AncientSpire_Bronze", (0.38, 0.46, 0.48), 0.85, 0.42,
            texture_key="bronze", repeat=UV_REPEAT_METAL, tint=(0.70, 0.86, 0.78)),
        "plate": build_material(
            "AncientSpire_Plate", (0.40, 0.42, 0.40), 0.80, 0.50,
            texture_key="plate", repeat=UV_REPEAT_METAL, tint=(0.86, 0.74, 0.58)),
        "sand": build_material(
            "AncientSpire_Sand", (0.78, 0.58, 0.34), 0.0, 0.92,
            texture_key="sand", repeat=UV_REPEAT_SAND, tint=(1.10, 0.92, 0.66)),
        "cloth": build_material(
            "AncientSpire_Cloth", (0.30, 0.20, 0.14), 0.0, 0.88,
            texture_key="carved", repeat=UV_REPEAT_DETAIL, tint=(0.52, 0.30, 0.20)),
        "accent": build_material(
            "AncientSpire_Accent", accent, 0.25, 0.28,
            emission=(0.08, 3.2 / 5.0, 1.0), emission_strength=5.0),
        "relic": build_material(
            "AncientSpire_Relic", (0.10, 0.55, 0.66), 0.15, 0.12,
            emission=(0.10, 0.78, 1.0), emission_strength=9.0),
        "interior": build_material(
            "AncientSpire_Interior", (0.055, 0.07, 0.08), 0.0, 0.95),
    }


# ---------------------------------------------------------------------------
# Base: sand drift, stepped plinth, buttress fins, ground circuit.

def build_base(mats, collection):
    # Wind-piled sand lapping at the plinth. Built as a lofted ring so the
    # silhouette is irregular instead of a clean cone.
    drift_rings = []
    for step, (radius, z) in enumerate(((28.5, -0.40), (25.0, 0.55),
                                        (22.4, 1.35), (20.6, 2.05))):
        ring = []
        for i in range(48):
            theta = (2.0 * math.pi * i) / 48
            wobble = (math.sin(theta * 3.0 + step) * 1.5
                      + math.sin(theta * 7.0 - step * 1.7) * 0.7)
            r = radius + wobble * (1.0 - step * 0.18)
            lift = math.sin(theta * 5.0 + 1.1) * 0.22 * (1.0 - step * 0.2)
            ring.append((math.cos(theta) * r, math.sin(theta) * r, z + lift))
        drift_rings.append(ring)
    verts, faces = loft(drift_rings, cap_bottom=True, cap_top=False)
    add_mesh("Spire_SandDrift", verts, faces, mats["sand"], collection,
             bevel=0.0, smooth_faces="ALL", uv_scale=1.0 / UV_REPEAT_SAND)

    # Three-step octagonal plinth. Each step is rotated slightly off the one
    # below so the corners cast their own shadow line.
    steps = ((20.5, 19.8, 0.00, 2.40, 0.0),
             (17.8, 17.3, 2.40, 4.20, math.radians(4.0)),
             (15.0, 14.5, 4.20, 5.60, math.radians(-3.0)))
    for index, (r_bottom, r_top, z0, z1, phase) in enumerate(steps):
        verts, faces = prism_data(r_bottom, r_top, z1 - z0, 8, phase=phase + math.pi / 8.0)
        add_mesh("Spire_Plinth_{:d}".format(index + 1), verts, faces,
                 mats["stone_dark"] if index == 0 else mats["stone"], collection,
                 location=(0.0, 0.0, (z0 + z1) * 0.5), bevel=0.14, bevel_segments=2)

    # Radial buttress fins, rooted inside the plinth and running out past its
    # bottom step into the sand. These are what is left of the outer casing.
    for i in range(8):
        angle = math.radians(22.5 + 45.0 * i)
        length = 9.8 + rng.uniform(-1.2, 2.1)
        verts, faces = wedge_data(length, 4.6, 1.9,
                                  7.2 + rng.uniform(-1.0, 1.6),
                                  1.2 + rng.uniform(-0.4, 0.8))
        add_mesh("Spire_BaseFin_{:d}".format(i + 1), verts, faces,
                 mats["stone_dark"], collection,
                 location=(math.cos(angle) * 15.4, math.sin(angle) * 15.4,
                           -0.5 + rng.uniform(-0.3, 0.3)),
                 rotation=(0.0, math.radians(rng.uniform(-6.0, 6.0)), angle),
                 bevel=0.12)

    # Ground circuit inlay: SpireBaseRingSegments arcs at SpireBaseRingRadius.
    # Two arcs are missing and one is buried, so the ring reads as ruined
    # rather than as a freshly poured trim strip.
    span = 360.0 / BASE_RING_SEGMENTS
    missing = {3, 8}
    for i in range(BASE_RING_SEGMENTS):
        if i in missing:
            continue
        buried = (i == 6)
        sweep = span * (0.62 if buried else 0.74)
        start = span * i + (span - sweep) * 0.5
        height = BASE_RING_THICKNESS * (0.5 if buried else 1.0)
        verts, faces = arc_data(BASE_RING_RADIUS - 0.55, BASE_RING_RADIUS + 0.55,
                                height, start, sweep, 6)
        add_mesh("Spire_GroundCircuit_{:d}".format(i + 1), verts, faces,
                 mats["stone_dark"] if buried else mats["accent"], collection,
                 location=(0.0, 0.0, 0.18), bevel=0.05,
                 uv_scale=1.0 / UV_REPEAT_METAL)

        # Bronze anchor cleat straddling each surviving arc.
        cleat_angle = math.radians(start + sweep * 0.5)
        verts, faces = box_data((1.9, 0.9, 0.5))
        add_mesh("Spire_CircuitCleat_{:d}".format(i + 1), verts, faces,
                 mats["bronze"], collection,
                 location=(math.cos(cleat_angle) * BASE_RING_RADIUS,
                           math.sin(cleat_angle) * BASE_RING_RADIUS, 0.30),
                 rotation=(0.0, 0.0, cleat_angle), bevel=0.05,
                 uv_scale=1.0 / UV_REPEAT_METAL)

    # Approach stair on the +X side, ground up to the plinth top.
    stair_count = 9
    for i in range(stair_count):
        z0 = 5.60 * i / stair_count
        depth = 1.15
        width = 6.4 - i * 0.22
        verts, faces = box_data((depth, width, 5.60 / stair_count + 0.16))
        add_mesh("Spire_Stair_{:d}".format(i + 1), verts, faces,
                 mats["stone"], collection,
                 location=(21.6 - i * depth, 0.0, z0 + 5.60 / (stair_count * 2.0)),
                 bevel=0.05)
    for side in (-1.0, 1.0):
        verts, faces = box_data((11.0, 1.1, 1.5))
        add_mesh("Spire_StairCheek_{:s}".format("L" if side < 0 else "R"),
                 verts, faces, mats["stone_dark"], collection,
                 location=(16.6, side * 3.6, 3.1),
                 rotation=(0.0, math.radians(-26.0), 0.0), bevel=0.08)

    # Standing stones: the ring of ancient markers the tower was raised inside.
    for i in range(6):
        angle = math.radians(30.0 + 60.0 * i)
        fallen = (i == 4)
        height = rng.uniform(8.4, 12.6)
        radius = 23.0 + rng.uniform(-1.4, 1.8)
        if fallen:
            verts, faces = box_data((2.4, 1.5, height))
            add_mesh("Spire_StandingStone_{:d}".format(i + 1), verts, faces,
                     mats["stone_dark"], collection,
                     location=(math.cos(angle) * radius, math.sin(angle) * radius, 1.2),
                     rotation=(math.radians(84.0), math.radians(6.0), angle),
                     bevel=0.14)
            continue
        verts, faces = prism_data(1.55, 1.15, height, 6, phase=rng.uniform(0.0, 1.0))
        add_mesh("Spire_StandingStone_{:d}".format(i + 1), verts, faces,
                 mats["stone_dark"], collection,
                 location=(math.cos(angle) * radius, math.sin(angle) * radius,
                           height * 0.5 - 1.1),
                 rotation=(math.radians(rng.uniform(-5.0, 5.0)),
                           math.radians(rng.uniform(-5.0, 5.0)), angle),
                 bevel=0.10)
        # Rune plate set into the face turned toward the tower.
        verts, faces = box_data((0.24, 0.9, height * 0.42))
        add_mesh("Spire_StoneRune_{:d}".format(i + 1), verts, faces,
                 mats["accent"], collection,
                 location=(math.cos(angle) * (radius - 1.35),
                           math.sin(angle) * (radius - 1.35), height * 0.28),
                 rotation=(0.0, 0.0, angle), bevel=0.03,
                 uv_scale=1.0 / UV_REPEAT_METAL)

    # Rubble field. Densest where the fins have shed their casing.
    for i in range(26):
        angle = rng.uniform(0.0, math.tau)
        radius = rng.uniform(13.0, 30.0)
        size = rng.uniform(0.34, 1.30)
        verts, faces = rock_data(size, SEED + 400 + i)
        add_mesh("Spire_Rubble_{:d}".format(i + 1), verts, faces,
                 mats["stone_dark"] if i % 3 else mats["stone"], collection,
                 location=(math.cos(angle) * radius, math.sin(angle) * radius,
                           size * 0.34),
                 rotation=(rng.uniform(0.0, 0.6), rng.uniform(0.0, 0.6),
                           rng.uniform(0.0, math.tau)),
                 bevel=0.03, smooth_faces="ALL")

    # Fallen course blocks: the masonry that used to be up on the shaft.
    for i in range(7):
        angle = rng.uniform(0.0, math.tau)
        radius = rng.uniform(14.0, 24.0)
        verts, faces = box_data((rng.uniform(2.0, 3.2), rng.uniform(1.4, 2.2),
                                 rng.uniform(1.2, 2.0)))
        add_mesh("Spire_FallenBlock_{:d}".format(i + 1), verts, faces,
                 mats["stone"], collection,
                 location=(math.cos(angle) * radius, math.sin(angle) * radius, 0.55),
                 rotation=(rng.uniform(-0.35, 0.35), rng.uniform(-0.35, 0.35),
                           rng.uniform(0.0, math.tau)),
                 bevel=0.09)


# ---------------------------------------------------------------------------
# Shaft: one lofted mesh carrying every masonry course.

def build_shaft(mats, collection):
    rings = []
    course_span = (SHAFT_Z1 - SHAFT_Z0) / COURSE_COUNT
    for i in range(COURSE_COUNT):
        z0 = SHAFT_Z0 + course_span * i
        z1 = z0 + course_span
        gap = course_span * 0.18
        belt = (i % 4 == 3)
        settle = 1.0 + rng.uniform(-0.012, 0.012)
        main = (1.055 if belt else 1.0) * settle
        flute = 0.25 if belt else 1.0
        reveal = main * 0.925
        rings.append((z0, main, flute))
        rings.append((z1 - gap, main, flute))
        rings.append((z1 - gap, reveal, flute * 0.4))
        rings.append((z1, reveal, flute * 0.4))

    verts, faces = loft([shaft_ring(z, scale, flute) for z, scale, flute in rings],
                        cap_bottom=True, cap_top=True)
    add_mesh("Spire_Shaft", verts, faces, mats["stone"], collection,
             bevel=0.05, bevel_segments=2, uv_mode='CYLINDRICAL',
             uv_scale=1.0 / UV_REPEAT_STONE)

    # Corner pilasters, one per octagon corner, following the taper and lean.
    for index, angle in enumerate(CORNER_ANGLES):
        top = SHAFT_Z1 - 6.0 if index % 3 else SHAFT_Z1 - 14.0
        sections = []
        steps = 40
        for s in range(steps + 1):
            z = SHAFT_Z0 - 0.6 + (top - SHAFT_Z0 + 0.6) * s / steps
            radius = shaft_radius(z)
            cx, cy = shaft_centre(z)
            twist = shaft_twist(z)
            theta = angle + twist
            radial = Vector((math.cos(theta), math.sin(theta), 0.0))
            tangent = Vector((-math.sin(theta), math.cos(theta), 0.0))
            inner = section_radius(angle, radius, 0.0) - 0.30
            outer = section_radius(angle, radius, 0.0) + 0.30 + radius * 0.055
            half_w = 0.16 * radius + 0.28
            centre = Vector((cx, cy, z))
            sections.append([
                tuple(centre + radial * inner - tangent * half_w),
                tuple(centre + radial * outer - tangent * half_w),
                tuple(centre + radial * outer + tangent * half_w),
                tuple(centre + radial * inner + tangent * half_w)])
        verts, faces = sweep_rect(sections)
        add_mesh("Spire_Pilaster_{:d}".format(index + 1), verts, faces,
                 mats["stone"], collection, bevel=0.07, bevel_segments=2,
                 uv_mode='CYLINDRICAL', uv_scale=1.0 / UV_REPEAT_STONE)

        # Bronze binding collars where the pilaster crosses a belt course.
        for course in (3, 7, 11, 15):
            z = SHAFT_Z0 + (SHAFT_Z1 - SHAFT_Z0) * (course + 0.82) / COURSE_COUNT
            if z > top - 1.0:
                continue
            if (index + course) % 2:
                continue
            point = shaft_point(z, angle, radial_offset=0.36 + shaft_radius(z) * 0.055)
            verts, faces = box_data((0.9, 0.30 + shaft_radius(z) * 0.34, 0.7))
            add_mesh("Spire_Collar_{:d}_{:d}".format(index + 1, course), verts, faces,
                     mats["bronze"], collection, location=point,
                     rotation=(0.0, 0.0, angle + shaft_twist(z)), bevel=0.05,
                     uv_scale=1.0 / UV_REPEAT_METAL)

    # Displaced blocks: individual stones shoved out of true by settling.
    for i in range(9):
        course = rng.randrange(2, COURSE_COUNT - 3)
        z = SHAFT_Z0 + (SHAFT_Z1 - SHAFT_Z0) * (course + 0.42) / COURSE_COUNT
        angle = rng.choice(FACE_ANGLES) + rng.uniform(-0.18, 0.18)
        radius = shaft_radius(z)
        point = shaft_point(z, angle, radial_offset=radius * 0.10 + 0.10, flute=0.6)
        verts, faces = box_data((radius * 0.30 + 0.5, radius * 0.42 + 0.9,
                                 (SHAFT_Z1 - SHAFT_Z0) / COURSE_COUNT * 0.60))
        add_mesh("Spire_DisplacedBlock_{:d}".format(i + 1), verts, faces,
                 mats["stone"], collection, location=point,
                 rotation=(0.0, math.radians(rng.uniform(-7.0, 7.0)),
                           angle + shaft_twist(z) + math.radians(rng.uniform(-9.0, 9.0))),
                 bevel=0.07)

    # Glyph plaques recessed into the faces, with a lit inlay strip.
    for i in range(14):
        z = SHAFT_Z0 + 6.0 + (SHAFT_Z1 - SHAFT_Z0 - 14.0) * (i / 13.0)
        angle = FACE_ANGLES[(i * 3) % 8]
        radius = shaft_radius(z)
        depth = radius * 0.06
        point = shaft_point(z, angle, radial_offset=-depth * 0.5, flute=1.0)
        width = min(2.6, radius * 0.42)
        verts, faces = box_data((depth * 2.0, width, 2.4))
        add_mesh("Spire_Glyph_{:d}".format(i + 1), verts, faces,
                 mats["carved"], collection, location=point,
                 rotation=(0.0, 0.0, angle + shaft_twist(z)), bevel=0.04,
                 uv_scale=1.0 / UV_REPEAT_DETAIL)
        lit = shaft_point(z, angle, radial_offset=depth * 0.35, flute=1.0)
        verts, faces = box_data((depth * 0.5, width * 0.52, 1.5))
        add_mesh("Spire_GlyphLight_{:d}".format(i + 1), verts, faces,
                 mats["accent"], collection, location=lit,
                 rotation=(0.0, 0.0, angle + shaft_twist(z)), bevel=0.02,
                 uv_scale=1.0 / UV_REPEAT_METAL)

    # Iron anchor rings, the kind ropes and hoists were slung from.
    for i in range(8):
        z = SHAFT_Z0 + 9.0 + (SHAFT_Z1 - SHAFT_Z0 - 22.0) * (i / 7.0)
        angle = CORNER_ANGLES[(i * 5) % 8]
        point = shaft_point(z, angle, radial_offset=0.62, flute=0.0)
        verts, faces = torus_data(0.44, 0.09, 14, 6)
        add_mesh("Spire_AnchorRing_{:d}".format(i + 1), verts, faces,
                 mats["bronze"], collection, location=point,
                 rotation=(math.radians(90.0), 0.0, angle + shaft_twist(z)),
                 bevel=0.0, smooth_faces="ALL", uv_scale=1.0 / UV_REPEAT_METAL)

    # Exposed armature: a stretch where the casing has spalled off the
    # north-east face and the bronze reinforcement ribs show through.
    scar_angle = FACE_ANGLES[1]
    for i in range(4):
        z0, z1 = 31.0, 41.5
        offset = (i - 1.5) * 0.62
        points = []
        for s in range(9):
            z = z0 + (z1 - z0) * s / 8
            points.append(shaft_point(z, scar_angle + offset / max(1.0, shaft_radius(z)),
                                      radial_offset=0.16, flute=1.0))
        verts, faces = tube_data(points, 0.13, sides=6)
        add_mesh("Spire_Armature_{:d}".format(i + 1), verts, faces,
                 mats["bronze"], collection, bevel=0.0, smooth_faces="ALL",
                 uv_scale=1.0 / UV_REPEAT_METAL)
    for i in range(3):
        z = 33.0 + i * 4.0
        point = shaft_point(z, scar_angle, radial_offset=0.05, flute=1.0)
        verts, faces = box_data((0.22, shaft_radius(z) * 0.52, 0.30))
        add_mesh("Spire_ArmatureTie_{:d}".format(i + 1), verts, faces,
                 mats["plate"], collection, location=point,
                 rotation=(0.0, 0.0, scar_angle + shaft_twist(z)), bevel=0.03,
                 uv_scale=1.0 / UV_REPEAT_METAL)


# ---------------------------------------------------------------------------
# Portal, lower cornice and the cantilevered gallery.

def build_portal(mats, collection):
    """Doorway on the +X face, lined up with the approach stair.

    Built as an applied porch rather than a boolean cut: two piers standing
    proud of the wall, a lintel, and a dark reveal panel sitting flush behind
    them. That reads as a doorway from every angle the player sees it from and
    keeps the shaft a single clean manifold.
    """
    angle = 0.0
    z_base, z_head = SHAFT_Z0 + 0.2, SHAFT_Z0 + 7.4
    half_open = 2.30

    def place(z, radial, lateral):
        """Point on the local wall frame: `radial` out, `lateral` across."""
        base = shaft_point(z, angle, radial_offset=radial, flute=1.0)
        yaw = angle + shaft_twist(z)
        return (base[0] - math.sin(yaw) * lateral,
                base[1] + math.cos(yaw) * lateral, base[2])

    def yaw_at(z):
        return angle + shaft_twist(z)

    mid = (z_base + z_head) * 0.5

    # Dark reveal, flush with the wall so it never reads as a floating box.
    verts, faces = box_data((0.7, half_open * 2.0, z_head - z_base))
    add_mesh("Spire_PortalReveal", verts, faces, mats["interior"], collection,
             location=place(mid, 0.06, 0.0), rotation=(0.0, 0.0, yaw_at(mid)),
             bevel=0.0)

    # Two orders of jamb pier, the outer one standing further proud.
    for order, (radial, depth, extra) in enumerate(((0.92, 1.55, 0.30),
                                                    (0.46, 1.10, 0.00))):
        height = (z_head - z_base) + extra * 2.0
        pier_mid = z_base + height * 0.5 - extra
        width = 1.05 - order * 0.22
        lateral = half_open + width * 0.5 + order * 0.02
        for side in (-1.0, 1.0):
            verts, faces = box_data((depth, width, height))
            add_mesh("Spire_PortalPier_{:d}_{:s}".format(order, "L" if side < 0 else "R"),
                     verts, faces, mats["carved"], collection,
                     location=place(pier_mid, radial, side * lateral),
                     rotation=(0.0, 0.0, yaw_at(pier_mid)), bevel=0.07,
                     uv_scale=1.0 / UV_REPEAT_DETAIL)
        lintel_z = z_base + height - extra + 0.55
        verts, faces = box_data((depth, (lateral + width * 0.5) * 2.0, 1.10))
        add_mesh("Spire_PortalLintel_{:d}".format(order), verts, faces,
                 mats["carved"], collection,
                 location=place(lintel_z, radial, 0.0),
                 rotation=(0.0, 0.0, yaw_at(lintel_z)), bevel=0.07,
                 uv_scale=1.0 / UV_REPEAT_DETAIL)

    # Bronze tympanum plate above the opening, carrying the accent inlay.
    plate_z = z_head + 1.55
    verts, faces = box_data((0.55, 4.9, 1.9))
    add_mesh("Spire_PortalPlate", verts, faces, mats["plate"], collection,
             location=place(plate_z, 1.05, 0.0), rotation=(0.0, 0.0, yaw_at(plate_z)),
             bevel=0.06, uv_scale=1.0 / UV_REPEAT_METAL)
    verts, faces = box_data((0.30, 3.2, 0.42))
    add_mesh("Spire_PortalPlateGlyph", verts, faces, mats["accent"], collection,
             location=place(plate_z, 1.40, 0.0), rotation=(0.0, 0.0, yaw_at(plate_z)),
             bevel=0.03, uv_scale=1.0 / UV_REPEAT_METAL)

    # Lit threshold strip, reading as the same energy as the ground circuit.
    sill_z = z_base + 0.12
    verts, faces = box_data((2.4, half_open * 2.0 + 1.6, 0.30))
    add_mesh("Spire_PortalSill", verts, faces, mats["accent"], collection,
             location=place(sill_z, 0.55, 0.0), rotation=(0.0, 0.0, yaw_at(sill_z)),
             bevel=0.04, uv_scale=1.0 / UV_REPEAT_METAL)

    # Weathered step block bridging the sill and the top of the stair flight.
    verts, faces = box_data((3.0, 6.0, 0.7))
    add_mesh("Spire_PortalStep", verts, faces, mats["stone_dark"], collection,
             location=place(z_base - 0.30, 2.0, 0.0),
             rotation=(0.0, 0.0, yaw_at(z_base)), bevel=0.08)


def build_cornice(mats, collection):
    """Lower cornice band at z = 22, carried on eight corbels."""
    z = CORNICE_Z
    rings = [shaft_ring(z - 1.10, 1.02, 0.3),
             shaft_ring(z - 0.30, 1.20, 0.15),
             shaft_ring(z + 0.85, 1.24, 0.10),
             shaft_ring(z + 1.35, 1.08, 0.20)]
    verts, faces = loft(rings, cap_bottom=False, cap_top=False)
    add_mesh("Spire_Cornice", verts, faces, mats["carved"], collection,
             bevel=0.06, uv_mode='CYLINDRICAL', uv_scale=1.0 / UV_REPEAT_DETAIL)

    for index, angle in enumerate(CORNER_ANGLES):
        point = shaft_point(z - 2.1, angle, radial_offset=0.6, flute=0.0)
        verts, faces = wedge_data(2.3, 1.5, 1.0, 2.9, 1.0)
        add_mesh("Spire_Corbel_{:d}".format(index + 1), verts, faces,
                 mats["stone_dark"], collection, location=point,
                 rotation=(0.0, math.radians(-18.0), angle + shaft_twist(z)),
                 bevel=0.06)


def build_gallery(mats, collection):
    """Cantilevered watch gallery, half of its parapet gone."""
    deck_bottom, deck_top = GALLERY_Z, GALLERY_DECK_TOP
    mid = (deck_bottom + deck_top) * 0.5
    inner = shaft_radius(mid) * 0.86

    # Corbel brackets carrying the overhang.
    for i in range(16):
        angle = math.tau * i / 16
        point = shaft_point(deck_bottom - 1.9, angle, radial_offset=0.4, flute=0.0)
        verts, faces = wedge_data(3.5, 1.35, 0.85, 3.4, 1.0)
        add_mesh("Spire_GalleryCorbel_{:d}".format(i + 1), verts, faces,
                 mats["stone_dark"], collection, location=point,
                 rotation=(0.0, math.radians(-24.0), angle + shaft_twist(mid)),
                 bevel=0.05)

    # Deck slab, slightly thicker at the rim where the drip course sits.
    rings = [shaft_ring(deck_bottom, 0.90, 0.0)]
    for radius, z in ((GALLERY_OUTER - 0.5, deck_bottom + 0.30),
                      (GALLERY_OUTER, deck_bottom + 0.95),
                      (GALLERY_OUTER, deck_top),
                      (inner, deck_top)):
        cx, cy = shaft_centre(z)
        rings.append([(cx + math.cos(math.tau * i / SHAFT_SEGMENTS) * radius,
                       cy + math.sin(math.tau * i / SHAFT_SEGMENTS) * radius, z)
                      for i in range(SHAFT_SEGMENTS)])
    verts, faces = loft(rings, cap_bottom=False, cap_top=False)
    add_mesh("Spire_GalleryDeck", verts, faces, mats["stone"], collection,
             bevel=0.06, uv_mode='CYLINDRICAL', uv_scale=1.0 / UV_REPEAT_STONE)

    # Merlons. Six of the twenty-four are gone and four are broken short.
    cx, cy = shaft_centre(deck_top)
    gone = {2, 3, 9, 15, 16, 21}
    short = {5, 10, 17, 22}
    for i in range(24):
        if i in gone:
            continue
        angle = math.tau * i / 24
        height = 1.1 if i in short else 2.5
        radius = GALLERY_OUTER - 0.55
        verts, faces = box_data((1.15, 1.55, height))
        add_mesh("Spire_Merlon_{:d}".format(i + 1), verts, faces,
                 mats["carved"], collection,
                 location=(cx + math.cos(angle) * radius,
                           cy + math.sin(angle) * radius,
                           deck_top + height * 0.5 - 0.1),
                 rotation=(0.0, math.radians(rng.uniform(-2.5, 2.5)), angle),
                 bevel=0.07, uv_scale=1.0 / UV_REPEAT_DETAIL)

    # Bronze handrail, surviving in three arcs.
    for start, sweep in ((22.0, 84.0), (152.0, 62.0), (250.0, 74.0)):
        points = []
        steps = 14
        for s in range(steps + 1):
            angle = math.radians(start + sweep * s / steps)
            points.append((cx + math.cos(angle) * (GALLERY_OUTER - 0.55),
                           cy + math.sin(angle) * (GALLERY_OUTER - 0.55),
                           deck_top + 2.62))
        verts, faces = tube_data(points, 0.13, sides=8)
        add_mesh("Spire_GalleryRail_{:d}".format(int(start)), verts, faces,
                 mats["bronze"], collection, bevel=0.0, smooth_faces="ALL",
                 uv_scale=1.0 / UV_REPEAT_METAL)

    # Sand that has blown up onto the deck and settled against the parapet.
    for i in range(7):
        angle = rng.uniform(0.0, math.tau)
        radius = rng.uniform(GALLERY_OUTER - 2.6, GALLERY_OUTER - 1.0)
        size = rng.uniform(0.9, 1.7)
        verts, faces = rock_data(size, SEED + 900 + i, rings=5, segments=9, squash=0.24)
        add_mesh("Spire_GallerySand_{:d}".format(i + 1), verts, faces,
                 mats["sand"], collection,
                 location=(cx + math.cos(angle) * radius,
                           cy + math.sin(angle) * radius, deck_top),
                 bevel=0.0, smooth_faces="ALL", uv_scale=1.0 / UV_REPEAT_SAND)

    # Tattered banners slung from the gallery underside.
    for i, angle_deg in enumerate((38.0, 168.0, 292.0)):
        angle = math.radians(angle_deg)
        rod_r = GALLERY_OUTER - 0.9
        verts, faces = cloth_panel_data(3.6, 12.0 + i * 2.0, 10, 16,
                                        sag=0.9, wave=0.55, tatter_seed=SEED + i)
        add_mesh("Spire_Banner_{:d}".format(i + 1), verts, faces,
                 mats["cloth"], collection,
                 location=(cx + math.cos(angle) * rod_r,
                           cy + math.sin(angle) * rod_r, deck_bottom + 0.1),
                 rotation=(0.0, 0.0, angle + math.pi * 0.5),
                 bevel=0.0, smooth_faces="ALL", uv_scale=1.0 / UV_REPEAT_DETAIL)
        verts, faces = tube_data([(0.0, -2.4, 0.0), (0.0, 2.4, 0.0)], 0.11, sides=6)
        add_mesh("Spire_BannerRod_{:d}".format(i + 1), verts, faces,
                 mats["bronze"], collection,
                 location=(cx + math.cos(angle) * rod_r,
                           cy + math.sin(angle) * rod_r, deck_bottom + 0.1),
                 rotation=(0.0, 0.0, angle + math.pi * 0.5),
                 bevel=0.0, smooth_faces="ALL", uv_scale=1.0 / UV_REPEAT_METAL)


# ---------------------------------------------------------------------------
# Crown, chains, and the relic assembly.

def build_crown(mats, collection):
    """Eight stone fingers, unevenly broken, bound by a bronze armature."""
    # Base drum the fingers rise out of.
    rings = [shaft_ring(SHAFT_Z1 - 0.4, 0.98, 0.4),
             shaft_ring(SHAFT_Z1 + 0.3, 1.30, 0.0),
             shaft_ring(SHAFT_Z1 + 2.1, 1.26, 0.0),
             shaft_ring(SHAFT_Z1 + 2.8, 1.06, 0.2)]
    verts, faces = loft(rings, cap_bottom=False, cap_top=False)
    add_mesh("Spire_CrownDrum", verts, faces, mats["carved"], collection,
             bevel=0.06, uv_mode='CYLINDRICAL', uv_scale=1.0 / UV_REPEAT_DETAIL)

    finger_tops = (CROWN_Z1, CROWN_Z1 - 2.6, CROWN_Z1 - 0.8, CROWN_Z1 - 4.4,
                   CROWN_Z1 - 0.3, CROWN_Z1 - 3.1, CROWN_Z1 - 1.6, CROWN_Z1 - 5.2)
    z_start = SHAFT_Z1 + 2.4
    for index, angle in enumerate(CORNER_ANGLES):
        top = finger_tops[index]
        sections = []
        steps = 14
        for s in range(steps + 1):
            f = s / steps
            z = z_start + (top - z_start) * f
            # Fingers flare out then hook back in over the relic.
            flare = math.sin(f * math.pi * 0.85) * 1.5 - f * 0.55
            radius = shaft_radius(z) * 1.05 + flare
            cx, cy = shaft_centre(z)
            twist = shaft_twist(z)
            theta = angle + twist
            radial = Vector((math.cos(theta), math.sin(theta), 0.0))
            tangent = Vector((-math.sin(theta), math.cos(theta), 0.0))
            half_w = 0.80 - 0.34 * f
            half_d = 0.62 - 0.24 * f
            centre = Vector((cx, cy, z)) + radial * radius
            sections.append([
                tuple(centre - radial * half_d - tangent * half_w),
                tuple(centre + radial * half_d - tangent * half_w),
                tuple(centre + radial * half_d + tangent * half_w),
                tuple(centre - radial * half_d + tangent * half_w)])
        verts, faces = sweep_rect(sections)
        add_mesh("Spire_CrownFinger_{:d}".format(index + 1), verts, faces,
                 mats["stone"], collection, bevel=0.06,
                 uv_mode='CYLINDRICAL', uv_scale=1.0 / UV_REPEAT_STONE)

    # Bronze binding rings holding the fingers together.
    for z, radius, minor in ((SHAFT_Z1 + 3.6, 3.35, 0.17),
                             (SHAFT_Z1 + 6.1, 3.55, 0.14)):
        cx, cy = shaft_centre(z)
        verts, faces = torus_data(radius, minor, 40, 8)
        add_mesh("Spire_CrownBand_{:d}".format(int(z)), verts, faces,
                 mats["bronze"], collection, location=(cx, cy, z),
                 bevel=0.0, smooth_faces="ALL", uv_scale=1.0 / UV_REPEAT_METAL)

    # Cradle arms reaching up from the crown toward the relic. These are what
    # make the floating relic read as held by the tower instead of parked
    # above it; they stay static while Unity bobs the relic between them.
    for i in range(3):
        angle = math.radians(90.0 + 120.0 * i)
        points = []
        steps = 12
        for s in range(steps + 1):
            f = s / steps
            z = SHAFT_Z1 + 5.0 + (RELIC_Z - 4.4 - (SHAFT_Z1 + 5.0)) * f
            radius = 3.5 + math.sin(f * math.pi) * 1.9 - f * 1.7
            cx, cy = shaft_centre(min(z, SHAFT_Z1))
            points.append((cx + math.cos(angle) * radius,
                           cy + math.sin(angle) * radius, z))
        verts, faces = tube_data(points, 0.22, sides=6)
        add_mesh("Spire_CrownCradle_{:d}".format(i + 1), verts, faces,
                 mats["bronze"], collection, bevel=0.0, smooth_faces="ALL",
                 uv_scale=1.0 / UV_REPEAT_METAL)
        verts, faces = crystal_data(0.42, 1.5, sides=6, waist=0.30)
        add_mesh("Spire_CradleTip_{:d}".format(i + 1), verts, faces,
                 mats["accent"], collection,
                 location=points[-1], bevel=0.02, uv_scale=1.0 / UV_REPEAT_METAL)

    # Chains from the crown down to the gallery parapet.
    gcx, gcy = shaft_centre(GALLERY_DECK_TOP)
    for i, angle_deg in enumerate((67.5, 157.5, 247.5, 337.5)):
        angle = math.radians(angle_deg)
        top_z = SHAFT_Z1 + 5.4
        tcx, tcy = shaft_centre(top_z)
        start = Vector((tcx + math.cos(angle) * 3.5,
                        tcy + math.sin(angle) * 3.5, top_z))
        end = Vector((gcx + math.cos(angle) * (GALLERY_OUTER - 0.7),
                      gcy + math.sin(angle) * (GALLERY_OUTER - 0.7),
                      GALLERY_DECK_TOP + 2.6))
        points = []
        steps = 22
        for s in range(steps + 1):
            f = s / steps
            point = start.lerp(end, f)
            point.z -= math.sin(f * math.pi) * 3.4
            points.append(tuple(point))
        verts, faces = tube_data(points, 0.11, sides=6)
        add_mesh("Spire_Chain_{:d}".format(i + 1), verts, faces,
                 mats["bronze"], collection, bevel=0.0, smooth_faces="ALL",
                 uv_scale=1.0 / UV_REPEAT_METAL)


def build_relic(mats, collection):
    """Animated parts. Kept as separate objects for the Unity animator."""
    cx, cy = shaft_centre(SHAFT_Z1)

    verts, faces = crystal_data(2.45, 6.2, sides=8, waist=0.30, twist=math.radians(22.5))
    add_mesh("Spire_Relic", verts, faces, mats["relic"], collection,
             location=(cx, cy, RELIC_Z), bevel=0.03, uv_scale=1.0 / UV_REPEAT_METAL)

    # Armillary rings around the relic. Named under the relic prefix so they
    # stay separate objects Unity can parent to the same animated transform.
    for i in range(3):
        verts, faces = torus_data(3.05, 0.10, 32, 6)
        add_mesh("Spire_Relic_Ring_{:d}".format(i + 1), verts, faces,
                 mats["bronze"], collection, location=(cx, cy, RELIC_Z),
                 rotation=(math.radians(90.0), math.radians(18.0 * i),
                           math.radians(60.0 * i)),
                 bevel=0.0, smooth_faces="ALL", uv_scale=1.0 / UV_REPEAT_METAL)

    for i in range(SHARD_COUNT):
        angle = math.tau * i / SHARD_COUNT
        radius = 8.0 + (i % 2) * 2.0
        z = RELIC_Z + ((i % 3) - 1.0) * 2.0
        verts, faces = crystal_data(0.62, 4.4, sides=6, waist=0.24)
        add_mesh("Spire_Shard_{:d}".format(i + 1), verts, faces,
                 mats["relic"], collection,
                 location=(cx + math.cos(angle) * radius,
                           cy + math.sin(angle) * radius, z),
                 rotation=(math.radians(i * 17.0), math.radians(i * 9.0), angle),
                 bevel=0.02, uv_scale=1.0 / UV_REPEAT_METAL)

    # Hovering monoliths at SpireHeight * 0.58, matching the old flight markers.
    for i in range(MONOLITH_COUNT):
        angle = math.tau * i / MONOLITH_COUNT
        verts, faces = prism_data(1.05, 0.72, 8.4, 6, phase=math.radians(15.0))
        add_mesh("Spire_Monolith_{:d}".format(i + 1), verts, faces,
                 mats["stone_dark"], collection,
                 location=(math.cos(angle) * MONOLITH_RADIUS,
                           math.sin(angle) * MONOLITH_RADIUS, MONOLITH_Z),
                 rotation=(math.radians(rng.uniform(-4.0, 4.0)),
                           math.radians(rng.uniform(-4.0, 4.0)), angle),
                 bevel=0.10)
        verts, faces = prism_data(1.25, 1.25, 0.34, 6, phase=math.radians(15.0))
        add_mesh("Spire_MonolithGlow_{:d}".format(i + 1), verts, faces,
                 mats["accent"], collection,
                 location=(math.cos(angle) * MONOLITH_RADIUS,
                           math.sin(angle) * MONOLITH_RADIUS, MONOLITH_Z - 4.4),
                 rotation=(0.0, 0.0, angle), bevel=0.04,
                 uv_scale=1.0 / UV_REPEAT_METAL)


# ---------------------------------------------------------------------------
# Merge, preview rig, export.

def merge_by_material(collection):
    """Flatten the authored parts into one mesh per material family.

    Authoring as ~230 small objects keeps the generator readable; importing
    that many renderers into Unity does not. The animated parts are skipped so
    `DuneVectorLandmarkAnimator` can still address them by name.
    """
    depsgraph = bpy.context.evaluated_depsgraph_get()
    families = {}
    consumed = []

    for obj in list(collection.objects):
        if obj.type != 'MESH' or obj.name.startswith(DYNAMIC_PREFIXES):
            continue
        material = obj.data.materials[0] if obj.data.materials else None
        if material is None:
            continue

        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        matrix = obj.matrix_world

        bucket = families.setdefault(material.name, {
            "material": material, "verts": [], "faces": [], "uvs": [], "smooth": []})
        offset = len(bucket["verts"])
        bucket["verts"].extend((matrix @ vertex.co)[:] for vertex in mesh.vertices)

        uv_data = mesh.uv_layers.active.data if mesh.uv_layers.active else None
        for polygon in mesh.polygons:
            bucket["faces"].append(tuple(index + offset for index in polygon.vertices))
            bucket["smooth"].append(polygon.use_smooth)
            for loop_index in polygon.loop_indices:
                if uv_data is None:
                    bucket["uvs"].extend((0.0, 0.0))
                else:
                    uv = uv_data[loop_index].uv
                    bucket["uvs"].extend((uv[0], uv[1]))

        evaluated.to_mesh_clear()
        consumed.append(obj)

    for obj in consumed:
        mesh_data = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if mesh_data.users == 0:
            bpy.data.meshes.remove(mesh_data)

    for material_name, bucket in sorted(families.items()):
        mesh = bpy.data.meshes.new(material_name + " Merged Mesh")
        mesh.from_pydata(bucket["verts"], [], bucket["faces"])
        mesh.update()
        mesh.uv_layers.new(name="UVMap").data.foreach_set("uv", bucket["uvs"])
        for polygon, smooth in zip(mesh.polygons, bucket["smooth"]):
            polygon.use_smooth = smooth
        merged = bpy.data.objects.new(
            material_name.replace("AncientSpire_", "Spire_"), mesh)
        merged.data.materials.append(bucket["material"])
        collection.objects.link(merged)

    # Bake the modifiers still sitting on the animated parts.
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in list(collection.objects):
        if not obj.name.startswith(DYNAMIC_PREFIXES) or not obj.modifiers:
            continue
        evaluated = obj.evaluated_get(depsgraph)
        baked = bpy.data.meshes.new_from_object(evaluated)
        old_mesh = obj.data
        obj.modifiers.clear()
        obj.data = baked
        if old_mesh.users == 0:
            bpy.data.meshes.remove(old_mesh)


def _aim(obj, target):
    obj.rotation_euler = (Vector(target) - Vector(obj.location)) \
        .to_track_quat('-Z', 'Y').to_euler()


# Framings chosen so nothing important sits behind the tower: the hero is a
# three-quarter view, and the detail shots look along the surface rather than
# through it.
PREVIEW_VIEWS = (
    ("Hero", (86.0, -104.0, 62.0), 52.0, (0.0, 0.0, 48.0)),
    ("Base", (34.0, -40.0, 12.0), 34.0, (0.0, 0.0, 9.0)),
    ("Gallery", (24.0, -27.0, 63.0), 46.0, (0.0, 0.0, 56.5)),
    ("Crown", (20.0, -24.0, 104.0), 44.0, (0.0, 0.0, 100.0)),
)


def build_preview_rig(collection):
    camera_data = bpy.data.cameras.new("Preview Camera")
    camera_data.lens = 52.0
    camera = bpy.data.objects.new("Preview Camera", camera_data)
    camera.location = (86.0, -104.0, 62.0)
    collection.objects.link(camera)
    _aim(camera, (0.0, 0.0, 48.0))
    bpy.context.scene.camera = camera

    sun_data = bpy.data.lights.new("Preview Sun", 'SUN')
    sun_data.energy = 5.2
    sun_data.color = (1.0, 0.78, 0.52)
    sun_data.angle = math.radians(1.6)
    sun = bpy.data.objects.new("Preview Sun", sun_data)
    sun.location = (70.0, -80.0, 90.0)
    collection.objects.link(sun)
    _aim(sun, (0.0, 0.0, 40.0))

    fill_data = bpy.data.lights.new("Preview Fill", 'AREA')
    fill_data.energy = 900000.0
    fill_data.size = 90.0
    fill_data.color = (0.34, 0.58, 1.0)
    fill = bpy.data.objects.new("Preview Fill", fill_data)
    fill.location = (-90.0, 70.0, 70.0)
    collection.objects.link(fill)
    _aim(fill, (0.0, 0.0, 40.0))

    bounce_data = bpy.data.lights.new("Preview Bounce", 'AREA')
    bounce_data.energy = 400000.0
    bounce_data.size = 120.0
    bounce_data.color = (1.0, 0.72, 0.42)
    bounce = bpy.data.objects.new("Preview Bounce", bounce_data)
    bounce.location = (30.0, -30.0, 2.0)
    collection.objects.link(bounce)
    _aim(bounce, (0.0, 0.0, 30.0))

    # Sand plane so the base is not floating in void in the previews. It is
    # kept out of the export collection.
    mesh = bpy.data.meshes.new("Preview Ground Mesh")
    mesh.from_pydata([(-400.0, -400.0, -0.6), (400.0, -400.0, -0.6),
                      (400.0, 400.0, -0.6), (-400.0, 400.0, -0.6)],
                     [], [(0, 1, 2, 3)])
    mesh.update()
    mesh.uv_layers.new(name="UVMap").data.foreach_set(
        "uv", [-40.0, -40.0, 40.0, -40.0, 40.0, 40.0, -40.0, 40.0])
    ground = bpy.data.objects.new("Preview Ground", mesh)
    ground.data.materials.append(bpy.data.materials["AncientSpire_Sand"])
    collection.objects.link(ground)

    scene = bpy.context.scene
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.20, 0.30, 0.48, 1.0)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 1750
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = PREVIEW_PATH
    try:
        scene.view_settings.look = 'AgX - Base Contrast'
    except TypeError:
        pass
    scene.view_settings.exposure = 0.4


def render_previews():
    scene = bpy.context.scene
    camera = scene.camera
    written = []
    for name, location, lens, target in PREVIEW_VIEWS:
        camera.location = location
        camera.data.lens = lens
        _aim(camera, target)
        if name == "Hero":
            scene.render.resolution_x, scene.render.resolution_y = 1200, 1600
            path = PREVIEW_PATH
        else:
            scene.render.resolution_x, scene.render.resolution_y = 1500, 1000
            path = os.path.join(SOURCE_DIR, "AncientSpirePreview_{:s}.png".format(name))
        bpy.context.view_layer.update()
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)
    return written


def export_assets(export_collection):
    for obj in bpy.data.objects:
        obj.select_set(False)
    for obj in export_collection.objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = next(iter(export_collection.objects), None)

    # `export_image_format='NONE'` on purpose: every map this model uses is
    # already in Assets/DuneVector/Resources. Embedding them would add ~180 MB
    # of duplicate 2K/4K JPEGs to the repo for no benefit.
    bpy.ops.export_scene.gltf(
        filepath=MODEL_PATH,
        export_format='GLB',
        use_selection=True,
        export_yup=True,
        export_apply=True,
        export_image_format='NONE',
        export_texcoords=True,
        export_normals=True,
        export_tangents=True,
        export_materials='EXPORT',
        export_cameras=False,
        export_lights=False,
        export_animations=False)

    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)


def evaluated_triangle_count(collection):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    total = 0
    for obj in collection.objects:
        if obj.type != 'MESH':
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        total += sum(max(0, len(polygon.vertices) - 2) for polygon in mesh.polygons)
        evaluated.to_mesh_clear()
    return total


# ---------------------------------------------------------------------------
# Entry point.

def build(export=True, render=True):
    global rng
    rng = random.Random(SEED)

    purge_scene()
    export_collection = make_collection(EXPORT_COLLECTION)
    preview_collection = make_collection(PREVIEW_COLLECTION)

    mats = build_materials()
    build_base(mats, export_collection)
    build_shaft(mats, export_collection)
    build_portal(mats, export_collection)
    build_cornice(mats, export_collection)
    build_gallery(mats, export_collection)
    build_crown(mats, export_collection)
    build_relic(mats, export_collection)

    authored = len(export_collection.objects)
    merge_by_material(export_collection)
    build_preview_rig(preview_collection)

    triangles = evaluated_triangle_count(export_collection)
    rendered = []
    error = None
    if render:
        try:
            rendered = render_previews()
        except Exception as exception:            # pylint: disable=broad-except
            error = "render: {:s}: {:s}".format(type(exception).__name__, str(exception))
    if export:
        try:
            export_assets(export_collection)
        except Exception as exception:            # pylint: disable=broad-except
            error = "{:s}{:s}: {:s}".format(
                (error + " | ") if error else "", type(exception).__name__, str(exception))

    return {
        "authored_objects": authored,
        "exported_objects": len(export_collection.objects),
        "object_names": sorted(obj.name for obj in export_collection.objects),
        "triangles": triangles,
        "materials": sorted(m.name for m in bpy.data.materials if m.name.startswith("AncientSpire")),
        "textures": sorted({os.path.basename(i.filepath) for i in bpy.data.images if i.filepath}),
        "renders": rendered,
        "model": MODEL_PATH,
        "blend": BLEND_PATH,
        "error": error,
    }
