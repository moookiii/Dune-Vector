"""Generate the "Sunspire Exchange" world hub for Dune Vector.

This is a brand-new replacement for the original PremiumHub. It is authored to
the same integration contract that `DuneVectorCourierGame.BuildHub` expects:

  * Blender is Z-up; the FBX export maps Blender +Y -> Unity +Z and
    Blender +Z -> Unity +Y.
  * The walkable deck top sits at Blender z = 1.42 so the Unity circle collider
    (placed at hub-local y = PlatformThickness * 0.5 = 1.2) and the authored
    terminals line up with the visible surface.
  * The deck is an annulus with inner radius 18.70 because Unity draws the
    "Energy Inlay" disc of radius PlatformRadius * 0.72 = 18.72 in the middle.
  * The outer walkable radius is 25.35 to match PremiumVisualSurfaceRadius, and
    the containment wall lands at 25.35 - 0.8 = 24.55, which is where the guard
    rail is modelled.
  * Terminal axes, in Blender degrees around +X:
        0   -> Unity +X : message archive terminal (r 11) + upgrade pad (r 13)
        90  -> Unity +Z : contract terminal (r 11)
        180 -> Unity -X : free roam terminal (r 11)
    Those wedges are kept clear; the upgrade side is framed by an overhead
    gantry instead of a dock so the pad and its calibration arms stay usable.
  * Objects named `Aerie_Pylon_*`, `Pylon_Cap_*` and `Gantry_Leg_*` receive
    automatic box colliders from PremiumVisualStructuralColliderNamePrefixes.

No `bpy.ops` calls are used for geometry, so the script runs safely from the
Blender MCP add-on's timer context. Meshes are built from raw vertex/face data
with deterministic box-projected UVs at a 2 m texture repeat.
"""

import math
import os

import bpy
import numpy as np
from mathutils import Euler, Vector

# ---------------------------------------------------------------------------
# Paths.

ROOT = r"C:\Dune Vector URP"
SOURCE_DIR = os.path.join(ROOT, "ArtSource", "Blender", "SunspireExchange")
ASSET_DIR = os.path.join(ROOT, "Assets", "DuneVector", "Resources", "SunspireExchange")
# Placeholder maps stay outside Assets/: they are regenerable reference only,
# and the hub is textured by hand in Unity.
TEXTURE_DIR = os.path.join(SOURCE_DIR, "Textures")
MODEL_PATH = os.path.join(ASSET_DIR, "SunspireExchange.fbx")
BLEND_PATH = os.path.join(SOURCE_DIR, "SunspireExchange.blend")
PREVIEW_PATH = os.path.join(SOURCE_DIR, "SunspireExchangePreview.png")

for _path in (SOURCE_DIR, ASSET_DIR, TEXTURE_DIR):
    os.makedirs(_path, exist_ok=True)

# ---------------------------------------------------------------------------
# Integration constants. These mirror Dune Vector Runtime Settings.asset.

DECK_OUTER = 25.35          # PremiumVisualSurfaceRadius
DECK_INNER = 18.70          # PlatformRadius * 0.72, filled by Unity's energy disc
DECK_TOP = 1.42             # Visible walking surface
DECK_BOTTOM = 0.58
SKIRT_RADIUS = 25.80
RAIL_RADIUS = 24.55         # PremiumVisualSurfaceRadius - ContainmentWallThickness
RAIL_HEIGHT = 1.30

TERMINAL_AXES = (0.0, 90.0, 180.0)      # Blender degrees; see module docstring
DOCK_AXES = (90.0, 180.0)               # 0 deg is the upgrade gantry instead
UPGRADE_AXIS = 0.0

# A 2 m texture repeat keeps the Unity materials at a predictable scale.
UV_SCALE = 0.5

EXPORT_COLLECTION = "SunspireExchange"
PREVIEW_COLLECTION = "Preview Rig"

BUILD_REPORT = {}


# ---------------------------------------------------------------------------
# Scene helpers.

def purge_scene():
    """Remove every object and orphaned datablock so reruns are deterministic."""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    for group in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras,
                  bpy.data.lights, bpy.data.images, bpy.data.curves):
        for datablock in list(group):
            if datablock.users == 0:
                group.remove(datablock)


def make_collection(name):
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    return collection


# ---------------------------------------------------------------------------
# UV projection.

def _polygon_normal(verts, face):
    """Newell's method, which stays stable for the n-gons used by the rings."""
    nx = ny = nz = 0.0
    count = len(face)
    for i in range(count):
        current = verts[face[i]]
        following = verts[face[(i + 1) % count]]
        nx += (current[1] - following[1]) * (current[2] + following[2])
        ny += (current[2] - following[2]) * (current[0] + following[0])
        nz += (current[0] - following[0]) * (current[1] + following[1])
    return nx, ny, nz


def box_project_uvs(verts, faces, scale=UV_SCALE):
    """Return a flat per-loop UV list using dominant-axis box projection."""
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


# ---------------------------------------------------------------------------
# Mesh creation.

def add_mesh(name, verts, faces, material, collection,
             location=(0.0, 0.0, 0.0), rotation=None, quaternion=None,
             bevel=0.08, bevel_segments=2, smooth_faces=None, uv_scale=UV_SCALE):
    """Create a single-material mesh object from raw vertex/face data."""
    mesh = bpy.data.meshes.new(name + " Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()

    uv_layer = mesh.uv_layers.new(name="UVMap")
    uv_layer.data.foreach_set("uv", box_project_uvs(verts, faces, uv_scale))

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
        modifier.angle_limit = math.radians(40.0)
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


def frustum_data(radius_bottom, radius_top, height, segments, cap_bottom=True, cap_top=True):
    """Cone/cylinder body centred on the local origin, extending along Z."""
    half = height * 0.5
    verts = []
    for i in range(segments):
        angle = (2.0 * math.pi * i) / segments
        verts.append((math.cos(angle) * radius_bottom, math.sin(angle) * radius_bottom, -half))
    for i in range(segments):
        angle = (2.0 * math.pi * i) / segments
        verts.append((math.cos(angle) * radius_top, math.sin(angle) * radius_top, half))

    faces = []
    side_faces = []
    for i in range(segments):
        n = (i + 1) % segments
        side_faces.append(len(faces))
        faces.append((i, n, segments + n, segments + i))
    if cap_bottom and radius_bottom > 1e-5:
        faces.append(tuple(range(segments - 1, -1, -1)))
    if cap_top and radius_top > 1e-5:
        faces.append(tuple(range(segments, segments * 2)))
    return verts, faces, side_faces


def annulus_data(inner_radius, outer_radius, height, segments):
    """Hollow ring solid centred on the local origin, extending along Z."""
    half = height * 0.5
    verts = []
    for z in (-half, half):
        for radius in (inner_radius, outer_radius):
            for i in range(segments):
                angle = (2.0 * math.pi * i) / segments
                verts.append((math.cos(angle) * radius, math.sin(angle) * radius, z))

    def idx(layer, ring, i):
        return layer * segments * 2 + ring * segments + (i % segments)

    faces = []
    side_faces = []
    for i in range(segments):
        n = (i + 1) % segments
        faces.append((idx(1, 0, i), idx(1, 1, i), idx(1, 1, n), idx(1, 0, n)))    # top
        faces.append((idx(0, 0, n), idx(0, 1, n), idx(0, 1, i), idx(0, 0, i)))    # bottom
        side_faces.append(len(faces))
        faces.append((idx(0, 1, i), idx(0, 1, n), idx(1, 1, n), idx(1, 1, i)))    # outer
        side_faces.append(len(faces))
        faces.append((idx(0, 0, n), idx(0, 0, i), idx(1, 0, i), idx(1, 0, n)))    # inner
    return verts, faces, side_faces


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


def ovoid_data(radius, rings, segments, squash=1.0):
    verts = [(0.0, 0.0, radius * squash)]
    for r in range(1, rings):
        phi = math.pi * r / rings
        z = math.cos(phi) * radius * squash
        ring_radius = math.sin(phi) * radius
        for s in range(segments):
            theta = (2.0 * math.pi * s) / segments
            verts.append((math.cos(theta) * ring_radius, math.sin(theta) * ring_radius, z))
    verts.append((0.0, 0.0, -radius * squash))

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


def draped_panel_data(width, height, columns, rows, sag, wave):
    """A hanging cloth panel with a deterministic sag and ripple."""
    verts = []
    for row in range(rows + 1):
        v = row / rows
        for column in range(columns + 1):
            u = column / columns
            x = (u - 0.5) * width
            z = -v * height
            ripple = math.sin(u * math.pi * 3.0) * wave * v
            slack = math.sin(u * math.pi) * sag * v
            verts.append((x, ripple - slack, z))
    faces = []
    for row in range(rows):
        for column in range(columns):
            a = row * (columns + 1) + column
            faces.append((a, a + 1, a + columns + 2, a + columns + 1))
    return verts, faces


# ---------------------------------------------------------------------------
# Placement helpers.

def polar(radius, degrees, z=0.0):
    angle = math.radians(degrees)
    return Vector((math.cos(angle) * radius, math.sin(angle) * radius, z))


def radial_part(name, material, collection, radius, degrees, radial, tangential, height,
                z, bevel=0.06, **kwargs):
    """Place a box at a polar position, aligned so X points outward."""
    verts, faces = box_data((radial, tangential, height))
    return add_mesh(name, verts, faces, material, collection,
                    location=polar(radius, degrees, z),
                    rotation=(0.0, 0.0, math.radians(degrees)),
                    bevel=bevel, **kwargs)


def beam_between(name, material, collection, start, end, width, depth, bevel=0.06, **kwargs):
    """Place a box spanning two points, its length running along local Z."""
    start_v, end_v = Vector(start), Vector(end)
    delta = end_v - start_v
    verts, faces = box_data((width, depth, delta.length))
    return add_mesh(name, verts, faces, material, collection,
                    location=(start_v + end_v) * 0.5,
                    quaternion=delta.to_track_quat('Z', 'Y'),
                    bevel=bevel, **kwargs)


def ring_solid(name, material, collection, inner_radius, outer_radius, bottom, top,
               segments=128, bevel=0.06, smooth=True):
    verts, faces, sides = annulus_data(inner_radius, outer_radius, top - bottom, segments)
    return add_mesh(name, verts, faces, material, collection,
                    location=(0.0, 0.0, (bottom + top) * 0.5),
                    bevel=bevel, smooth_faces=sides if smooth else None)


def disc_solid(name, material, collection, radius_bottom, radius_top, bottom, top,
               segments=128, bevel=0.06, smooth=True):
    verts, faces, sides = frustum_data(radius_bottom, radius_top, top - bottom, segments)
    return add_mesh(name, verts, faces, material, collection,
                    location=(0.0, 0.0, (bottom + top) * 0.5),
                    bevel=bevel, smooth_faces=sides if smooth else None)


def ring_torus(name, material, collection, major_radius, minor_radius, z,
               major_segments=128, minor_segments=12, bevel=0.0):
    verts, faces = torus_data(major_radius, minor_radius, major_segments, minor_segments)
    return add_mesh(name, verts, faces, material, collection,
                    location=(0.0, 0.0, z), bevel=bevel, smooth_faces="ALL")


def in_clear_wedge(degrees, axes=TERMINAL_AXES, half_width=17.0):
    """True when an angle falls inside a protected terminal approach lane."""
    for axis in axes:
        delta = abs((degrees - axis + 180.0) % 360.0 - 180.0)
        if delta < half_width:
            return True
    return False


# ---------------------------------------------------------------------------
# Materials.

def build_material(name, base_color, metallic, roughness, emission=None, emission_strength=6.0,
                   textures=None):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.diffuse_color = (*base_color, 1.0)
    principled = material.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = (*base_color, 1.0)
    principled.inputs["Metallic"].default_value = metallic
    principled.inputs["Roughness"].default_value = roughness
    if emission:
        principled.inputs["Emission Color"].default_value = (*emission, 1.0)
        principled.inputs["Emission Strength"].default_value = emission_strength

    if textures:
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        albedo_node = nodes.new("ShaderNodeTexImage")
        albedo_node.image = textures["albedo"]
        albedo_node.location = (-620, 260)
        links.new(albedo_node.outputs["Color"], principled.inputs["Base Color"])

        normal_node = nodes.new("ShaderNodeTexImage")
        normal_node.image = textures["normal"]
        normal_node.image.colorspace_settings.name = 'Non-Color'
        normal_node.location = (-620, -180)
        normal_map = nodes.new("ShaderNodeNormalMap")
        normal_map.location = (-330, -180)
        links.new(normal_node.outputs["Color"], normal_map.inputs["Color"])
        links.new(normal_map.outputs["Normal"], principled.inputs["Normal"])
    return material


def save_texture(name, rgba, colorspace='sRGB'):
    size = rgba.shape[0]
    image = bpy.data.images.new(name, width=size, height=size, alpha=True, float_buffer=False)
    image.colorspace_settings.name = colorspace
    image.pixels.foreach_set(np.ascontiguousarray(rgba.astype(np.float32)).ravel())
    image.filepath_raw = os.path.join(TEXTURE_DIR, name + ".png")
    image.file_format = 'PNG'
    image.save()
    return image


def height_to_normal(height, strength):
    gy, gx = np.gradient(height)
    normal = np.dstack((-gx * strength, -gy * strength, np.ones_like(gx)))
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    return np.dstack((normal * 0.5 + 0.5, np.ones_like(gx)))


def create_textures(size=2048):
    """Author tiling albedo/normal/mask sets for the four surface families.

    Mask channels follow the Unity HDRP/URP mask convention used elsewhere in
    the project: R = metallic, G = ambient occlusion, B = detail, A = smoothness.
    """
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    u = xx / size
    v = yy / size

    def fbm(*harmonics):
        total = np.zeros_like(u)
        for amplitude, fu, fv, phase in harmonics:
            total += amplitude * np.sin(2 * math.pi * (u * fu + v * fv + phase))
        return total

    noise = fbm((0.46, 3, 2, 0.13), (0.28, 11, -7, 0.41),
                (0.16, 37, 19, 0.72), (0.10, 103, -5, 0.31))
    wear = np.clip((noise + 1.25) / 2.5, 0.0, 1.0)
    grain = fbm((0.30, 61, 43, 0.05), (0.18, 149, -97, 0.63))

    images = {}

    # --- Dark structural metal -------------------------------------------
    brush = 0.5 + 0.5 * np.sin(2 * math.pi * (v * 181 + 0.12 * np.sin(u * math.pi * 8)))
    metal_height = noise * 0.55 + (brush - 0.5) * 0.20 + grain * 0.06
    albedo = np.zeros((size, size, 4), dtype=np.float32)
    albedo[..., 0] = 0.022 + wear * 0.038
    albedo[..., 1] = 0.046 + wear * 0.052
    albedo[..., 2] = 0.062 + wear * 0.062
    albedo[..., 3] = 1.0
    mask = np.zeros((size, size, 4), dtype=np.float32)
    mask[..., 0] = 0.92
    mask[..., 1] = 0.82 + wear * 0.14
    mask[..., 2] = 0.0
    mask[..., 3] = 0.58 + brush * 0.18
    images["DarkMetal"] = {
        "albedo": save_texture("SunspireExchange_DarkMetal_Albedo", albedo),
        "normal": save_texture("SunspireExchange_DarkMetal_Normal",
                               height_to_normal(metal_height, 3.1), 'Non-Color'),
        "mask": save_texture("SunspireExchange_DarkMetal_Mask", mask, 'Non-Color'),
    }

    # --- Sun-struck bronze ------------------------------------------------
    hammer = 0.5 + 0.5 * np.sin(2 * math.pi * (u * 44 + 0.35 * np.sin(v * math.pi * 22)))
    patina = np.clip(fbm((0.5, 7, 5, 0.22), (0.3, 23, -17, 0.51)) * 0.5 + 0.5, 0.0, 1.0)
    bronze_height = noise * 0.30 + (hammer - 0.5) * 0.45 + grain * 0.10
    albedo = np.zeros((size, size, 4), dtype=np.float32)
    albedo[..., 0] = 0.155 + wear * 0.105 - patina * 0.040
    albedo[..., 1] = 0.082 + wear * 0.062 + patina * 0.028
    albedo[..., 2] = 0.038 + wear * 0.026 + patina * 0.046
    albedo[..., 3] = 1.0
    mask = np.zeros((size, size, 4), dtype=np.float32)
    mask[..., 0] = 0.88 - patina * 0.22
    mask[..., 1] = 0.80 + wear * 0.16
    mask[..., 2] = 0.0
    mask[..., 3] = 0.68 - patina * 0.26
    images["Bronze"] = {
        "albedo": save_texture("SunspireExchange_Bronze_Albedo", albedo),
        "normal": save_texture("SunspireExchange_Bronze_Normal",
                               height_to_normal(bronze_height, 2.6), 'Non-Color'),
        "mask": save_texture("SunspireExchange_Bronze_Mask", mask, 'Non-Color'),
    }

    # --- Deck plating -----------------------------------------------------
    panel_x = np.minimum(np.mod(u * 8, 1.0), 1.0 - np.mod(u * 8, 1.0))
    panel_y = np.minimum(np.mod(v * 8, 1.0), 1.0 - np.mod(v * 8, 1.0))
    seam = np.clip((0.035 - np.minimum(panel_x, panel_y)) / 0.035, 0.0, 1.0)
    tread = 0.5 + 0.5 * np.sin(2 * math.pi * (u * 128 + v * 128))
    tread *= np.clip(1.0 - seam * 2.0, 0.0, 1.0)
    fleck = 0.5 + 0.5 * np.sin(2 * math.pi * (u * 53 + v * 67 + noise * 0.25))
    deck_height = noise * 0.32 - seam * 0.95 + tread * 0.16 + fleck * 0.06
    albedo = np.zeros((size, size, 4), dtype=np.float32)
    albedo[..., 0] = 0.052 + wear * 0.038 - seam * 0.024 + tread * 0.012
    albedo[..., 1] = 0.062 + wear * 0.043 - seam * 0.029 + tread * 0.014
    albedo[..., 2] = 0.070 + wear * 0.048 - seam * 0.032 + tread * 0.016
    albedo[..., 3] = 1.0
    mask = np.zeros((size, size, 4), dtype=np.float32)
    mask[..., 0] = 0.34 + fleck * 0.12
    mask[..., 1] = 0.76 + wear * 0.18 - seam * 0.30
    mask[..., 2] = 0.0
    mask[..., 3] = 0.40 + wear * 0.12 - tread * 0.16
    images["Deck"] = {
        "albedo": save_texture("SunspireExchange_Deck_Albedo", albedo),
        "normal": save_texture("SunspireExchange_Deck_Normal",
                               height_to_normal(deck_height, 4.0), 'Non-Color'),
        "mask": save_texture("SunspireExchange_Deck_Mask", mask, 'Non-Color'),
    }

    # --- Banner canvas ----------------------------------------------------
    warp = 0.5 + 0.5 * np.sin(2 * math.pi * u * 512)
    weft = 0.5 + 0.5 * np.sin(2 * math.pi * v * 512)
    weave = warp * 0.5 + weft * 0.5
    sun_fade = np.clip(fbm((0.6, 2, 3, 0.08), (0.3, 13, 9, 0.44)) * 0.5 + 0.5, 0.0, 1.0)
    canvas_height = (weave - 0.5) * 0.7 + noise * 0.25
    albedo = np.zeros((size, size, 4), dtype=np.float32)
    albedo[..., 0] = 0.074 + sun_fade * 0.052
    albedo[..., 1] = 0.046 + sun_fade * 0.033
    albedo[..., 2] = 0.030 + sun_fade * 0.020
    albedo[..., 3] = 1.0
    mask = np.zeros((size, size, 4), dtype=np.float32)
    mask[..., 0] = 0.0
    mask[..., 1] = 0.70 + sun_fade * 0.22
    mask[..., 2] = 0.0
    mask[..., 3] = 0.12 + weave * 0.10
    images["Canvas"] = {
        "albedo": save_texture("SunspireExchange_Canvas_Albedo", albedo),
        "normal": save_texture("SunspireExchange_Canvas_Normal",
                               height_to_normal(canvas_height, 1.8), 'Non-Color'),
        "mask": save_texture("SunspireExchange_Canvas_Mask", mask, 'Non-Color'),
    }
    return images


def build_materials():
    textures = create_textures()
    return {
        "dark": build_material("SunspireExchange_DarkMetal", (0.024, 0.052, 0.070),
                               0.92, 0.30, textures=textures["DarkMetal"]),
        "bronze": build_material("SunspireExchange_Bronze", (0.155, 0.082, 0.040),
                                 0.88, 0.27, textures=textures["Bronze"]),
        "deck": build_material("SunspireExchange_Deck", (0.062, 0.070, 0.078),
                               0.42, 0.55, textures=textures["Deck"]),
        "canvas": build_material("SunspireExchange_Canvas", (0.096, 0.060, 0.038),
                                 0.0, 0.86, textures=textures["Canvas"]),
        "cyan": build_material("SunspireExchange_EmissiveCyan", (0.010, 0.170, 0.240),
                               0.25, 0.20, emission=(0.02, 1.80, 3.60), emission_strength=3.0),
        "amber": build_material("SunspireExchange_EmissiveAmber", (0.240, 0.110, 0.020),
                                0.25, 0.22, emission=(3.40, 1.30, 0.18), emission_strength=2.6),
    }


# ---------------------------------------------------------------------------
# Construction stages.

def build_substructure(mats, collection):
    """Everything hanging below the deck. The hub floats 24 m over the dunes."""
    # Stepped inverted foundation.
    tiers = ((25.10, 21.40, -1.90, 0.10),
             (21.40, 15.60, -4.60, -1.90),
             (15.60, 9.20, -7.10, -4.60),
             (9.20, 3.10, -9.00, -7.10))
    for index, (radius_top, radius_bottom, bottom, top) in enumerate(tiers):
        disc_solid(f"Substructure_Tier_{index + 1:02d}", mats["dark"], collection,
                   radius_bottom, radius_top, bottom, top,
                   segments=96, bevel=0.16)
        ring_torus(f"Substructure_Tier_Seam_{index + 1:02d}", mats["bronze"], collection,
                   radius_top - 0.10, 0.13, top - 0.05, 96, 8)

    # Keel fins tying the foundation back to the rim.
    for i in range(12):
        angle = i * 30.0 + 15.0
        beam_between(f"Substructure_Keel_{i + 1:02d}", mats["dark"], collection,
                     polar(24.20, angle, 0.05), polar(8.60, angle, -8.30),
                     0.52, 1.30, bevel=0.09)

    # Ballast pods with steering fins and thruster nozzles.
    for i in range(6):
        angle = i * 60.0 + 30.0
        pod_centre = polar(17.60, angle, -3.60)
        verts, faces = ovoid_data(2.35, 14, 24, squash=1.55)
        add_mesh(f"Ballast_Pod_{i + 1:02d}", verts, faces, mats["dark"], collection,
                 location=pod_centre, rotation=(math.radians(90.0), 0.0, math.radians(angle)),
                 bevel=0.0, smooth_faces="ALL")
        band = ring_torus(f"Ballast_Pod_Band_{i + 1:02d}", mats["bronze"], collection,
                          1.62, 0.14, 0.0, 48, 8)
        band.location = pod_centre
        band.rotation_euler = Euler((math.radians(90.0), 0.0, math.radians(angle)), 'XYZ')

        for side in (-1, 1):
            radial_part(f"Ballast_Fin_{i + 1:02d}_{side:+d}", mats["dark"], collection,
                        17.60, angle + side * 6.5, 3.10, 0.22, 1.80, -3.60, bevel=0.05)
        radial_part(f"Ballast_Nozzle_Ring_{i + 1:02d}", mats["bronze"], collection,
                    17.60, angle, 1.90, 1.90, 0.42, -6.05, bevel=0.10)
        radial_part(f"Ballast_Thruster_Core_{i + 1:02d}", mats["cyan"], collection,
                    17.60, angle, 1.35, 1.35, 0.16, -6.28, bevel=0.05)

    # Heat exchangers and service crates tucked under the deck ring.
    for i in range(12):
        angle = i * 30.0
        radial_part(f"Heat_Exchanger_{i + 1:02d}", mats["dark"], collection,
                    22.40, angle, 1.60, 2.60, 1.05, -0.75, bevel=0.07)
        for fin in range(5):
            radial_part(f"Heat_Exchanger_Fin_{i + 1:02d}_{fin + 1}", mats["bronze"], collection,
                        22.40, angle, 1.72, 0.14, 0.86, -0.75 + (fin - 2) * 0.42, bevel=0.02)

    # Anchor cables trailing toward the dunes below.
    for i in range(6):
        angle = i * 60.0
        beam_between(f"Anchor_Cable_{i + 1:02d}", mats["dark"], collection,
                     polar(24.60, angle, -0.30), polar(29.80, angle, -16.50),
                     0.20, 0.20, bevel=0.0)
        radial_part(f"Anchor_Cleat_{i + 1:02d}", mats["bronze"], collection,
                    24.55, angle, 1.10, 1.60, 0.70, -0.20, bevel=0.08)

    ring_torus("Substructure_Truss_Ring", mats["dark"], collection, 20.00, 0.36, -2.10, 96, 10)
    ring_torus("Under_Glow_Ring", mats["cyan"], collection, 23.10, 0.15, -0.55, 128, 8)


def build_foundation_and_deck(mats, collection):
    """The armoured skirt and the walkable annular deck."""
    disc_solid("Foundation_Skirt", mats["dark"], collection,
               25.10, SKIRT_RADIUS, 0.05, 0.62, segments=128, bevel=0.20)
    ring_torus("Lower_Energy_Seam", mats["cyan"], collection, DECK_OUTER, 0.09, 0.28, 128, 8)

    # Overlapping armour plates around the skirt.
    for i in range(36):
        angle = i * 10.0
        radial_part(f"Skirt_Armor_Plate_{i + 1:02d}", mats["dark"], collection,
                    25.72, angle, 0.42, 4.15, 0.74, 0.36, bevel=0.09)
        radial_part(f"Skirt_Plate_Bolt_{i + 1:02d}", mats["bronze"], collection,
                    25.90, angle, 0.20, 0.34, 0.34, 0.36, bevel=0.05)

    # Main deck.
    ring_solid("Deck_Annulus", mats["deck"], collection,
               DECK_INNER, DECK_OUTER, 0.60, DECK_TOP, segments=128, bevel=0.10)
    ring_torus("Deck_Outer_Crown", mats["bronze"], collection, 24.95, 0.32, DECK_TOP + 0.01, 128, 12)
    ring_torus("Deck_Inner_Lip", mats["cyan"], collection, DECK_INNER + 0.05, 0.16, DECK_TOP + 0.10, 128, 10)
    ring_torus("Deck_Inner_Curb", mats["bronze"], collection, DECK_INNER + 0.26, 0.24, DECK_TOP - 0.02, 128, 10)

    # Recessed panel seams running across the deck.
    for i in range(48):
        angle = i * 7.5
        radial_part(f"Deck_Panel_Seam_{i + 1:02d}", mats["dark"], collection,
                    22.03, angle, 6.55, 0.16, 0.07, DECK_TOP + 0.005, bevel=0.02)

    # Bronze spines and their energy inlays, offset from the terminal lanes.
    for i in range(6):
        angle = i * 60.0 + 30.0
        radial_part(f"Radial_Deck_Spine_{i + 1:02d}", mats["bronze"], collection,
                    22.03, angle, 6.30, 2.20, 0.34, DECK_TOP + 0.06, bevel=0.07)
        radial_part(f"Radial_Energy_Inlay_{i + 1:02d}", mats["cyan"], collection,
                    22.03, angle, 5.80, 0.24, 0.11, DECK_TOP + 0.24, bevel=0.03)

    # Recessed landing lights.
    for i in range(24):
        angle = i * 15.0 + 7.5
        material = mats["amber"] if i % 4 == 0 else mats["cyan"]
        radial_part(f"Landing_Light_{i + 1:02d}", mats["dark"], collection,
                    24.05, angle, 0.62, 0.90, 0.16, DECK_TOP + 0.02, bevel=0.04)
        radial_part(f"Landing_Light_Lens_{i + 1:02d}", material, collection,
                    24.05, angle, 0.40, 0.66, 0.06, DECK_TOP + 0.11, bevel=0.02)

    # Approach chevrons painted into the three terminal lanes.
    for lane, axis in enumerate(TERMINAL_AXES):
        for step in range(4):
            radius = 20.30 + step * 1.35
            for side in (-1, 1):
                offset = polar(radius, axis)
                tangent = Vector((-math.sin(math.radians(axis)), math.cos(math.radians(axis)), 0.0))
                start = offset + tangent * (side * 1.85) + Vector((0.0, 0.0, DECK_TOP + 0.02))
                end = offset + tangent * (side * 0.05) + Vector((0.0, 0.0, DECK_TOP + 0.02))
                end += Vector((offset.x, offset.y, 0.0)).normalized() * -0.95
                beam_between(f"Approach_Chevron_{lane + 1}_{step + 1}_{side:+d}",
                             mats["bronze"], collection, start, end, 0.34, 0.09, bevel=0.02)


def build_perimeter(mats, collection):
    """Rim armour, guard rail, mooring hardware and deck services."""
    for i in range(24):
        angle = i * 15.0
        material = mats["bronze"] if i % 2 == 0 else mats["dark"]
        radial_part(f"Rim_Segment_{i + 1:02d}", material, collection,
                    25.15, angle, 0.72, 2.15, 0.62, 1.62, bevel=0.07)

    # Guard rail: stanchions plus two continuous rails at the containment radius.
    for i in range(48):
        angle = i * 7.5
        if in_clear_wedge(angle, DOCK_AXES, 6.0):
            continue
        radial_part(f"Guard_Stanchion_{i + 1:02d}", mats["dark"], collection,
                    RAIL_RADIUS, angle, 0.20, 0.20, RAIL_HEIGHT, DECK_TOP + RAIL_HEIGHT * 0.5,
                    bevel=0.03)
        radial_part(f"Guard_Stanchion_Foot_{i + 1:02d}", mats["bronze"], collection,
                    RAIL_RADIUS, angle, 0.44, 0.44, 0.10, DECK_TOP + 0.05, bevel=0.03)
    ring_torus("Guard_Rail_Upper", mats["bronze"], collection,
               RAIL_RADIUS, 0.09, DECK_TOP + RAIL_HEIGHT, 128, 8)
    ring_torus("Guard_Rail_Lower", mats["dark"], collection,
               RAIL_RADIUS, 0.07, DECK_TOP + RAIL_HEIGHT * 0.55, 128, 8)

    # Mooring clamps for docking couriers.
    for i in range(8):
        angle = i * 45.0 + 22.5
        radial_part(f"Mooring_Clamp_Base_{i + 1:02d}", mats["dark"], collection,
                    23.60, angle, 1.30, 1.90, 0.46, DECK_TOP + 0.22, bevel=0.06)
        for jaw in (-1, 1):
            radial_part(f"Mooring_Clamp_Jaw_{i + 1:02d}_{jaw:+d}", mats["bronze"], collection,
                        23.60, angle + jaw * 2.2, 1.05, 0.32, 1.15, DECK_TOP + 0.95, bevel=0.05)
        radial_part(f"Clamp_Lamp_{i + 1:02d}", mats["amber"], collection,
                    23.60, angle, 0.34, 0.34, 0.14, DECK_TOP + 1.58, bevel=0.03)

    # Vent stacks and bollards.
    for i in range(6):
        angle = i * 60.0
        if in_clear_wedge(angle, DOCK_AXES, 10.0):
            angle += 22.0
        radial_part(f"Vent_Stack_{i + 1:02d}", mats["dark"], collection,
                    20.10, angle, 1.05, 1.05, 1.75, DECK_TOP + 0.88, bevel=0.07)
        radial_part(f"Vent_Cowl_{i + 1:02d}", mats["bronze"], collection,
                    20.10, angle, 1.42, 1.42, 0.22, DECK_TOP + 1.84, bevel=0.06)
        radial_part(f"Vent_Glow_{i + 1:02d}", mats["cyan"], collection,
                    20.10, angle, 0.78, 0.78, 0.08, DECK_TOP + 1.97, bevel=0.02)

    # Pipe runs following the deck curvature between the spines.
    for i in range(6):
        base_angle = i * 60.0 + 30.0
        for pipe, radius in enumerate((19.55, 19.95)):
            material = mats["bronze"] if pipe == 0 else mats["dark"]
            for step in range(7):
                angle = base_angle - 18.0 + step * 6.0
                start = polar(radius, angle, DECK_TOP + 0.30)
                end = polar(radius, angle + 6.0, DECK_TOP + 0.30)
                beam_between(f"Pipe_Run_{i + 1:02d}_{pipe + 1}_{step + 1}",
                             material, collection, start, end, 0.17, 0.17, bevel=0.0)


def build_pylons(mats, collection):
    """Six leaning double-beam pylons. Names drive the Unity box colliders."""
    for i in range(6):
        angle_deg = i * 60.0
        angle = math.radians(angle_deg)
        tangent = Vector((-math.sin(angle), math.cos(angle), 0.0))
        outward = Vector((math.cos(angle), math.sin(angle), 0.0))
        base_centre = outward * 23.10

        for side in (-1, 1):
            start = base_centre + tangent * (side * 1.05) + Vector((0.0, 0.0, 1.55))
            end = outward * 25.00 + tangent * (side * 1.75) + Vector((0.0, 0.0, 8.90))
            beam_between(f"Aerie_Pylon_{i + 1:02d}_{side:+d}", mats["dark"], collection,
                         start, end, 0.66, 0.82, bevel=0.12)
            rib_offset = outward * -0.02 + tangent * (-side * 0.01)
            beam_between(f"Pylon_Bronze_Rib_{i + 1:02d}_{side:+d}", mats["bronze"], collection,
                         start + rib_offset, end + rib_offset, 0.18, 0.90, bevel=0.04)
            radial_part(f"Pylon_Foot_Plate_{i + 1:02d}_{side:+d}", mats["bronze"], collection,
                        23.10, angle_deg + side * 2.6, 1.70, 1.20, 0.28, 1.62, bevel=0.06)

            # Ladder rungs climbing the outboard leg.
            for rung in range(8):
                t = 0.14 + rung * 0.105
                point = start.lerp(end, t) + tangent * (side * 0.62)
                beam_between(f"Pylon_Rung_{i + 1:02d}_{side:+d}_{rung + 1}",
                             mats["bronze"], collection,
                             point - tangent * (side * 0.28), point + tangent * (side * 0.28),
                             0.09, 0.09, bevel=0.0)

        # Cross bracing between the two legs.
        for brace in range(3):
            low = 0.20 + brace * 0.26
            high = low + 0.26
            left_low = (base_centre + tangent * -1.05 + Vector((0.0, 0.0, 1.55))).lerp(
                outward * 25.00 + tangent * -1.75 + Vector((0.0, 0.0, 8.90)), low)
            right_high = (base_centre + tangent * 1.05 + Vector((0.0, 0.0, 1.55))).lerp(
                outward * 25.00 + tangent * 1.75 + Vector((0.0, 0.0, 8.90)), high)
            left_high = (base_centre + tangent * -1.05 + Vector((0.0, 0.0, 1.55))).lerp(
                outward * 25.00 + tangent * -1.75 + Vector((0.0, 0.0, 8.90)), high)
            right_low = (base_centre + tangent * 1.05 + Vector((0.0, 0.0, 1.55))).lerp(
                outward * 25.00 + tangent * 1.75 + Vector((0.0, 0.0, 8.90)), low)
            beam_between(f"Pylon_Cross_Brace_{i + 1:02d}_{brace + 1}a", mats["dark"], collection,
                         left_low, right_high, 0.22, 0.22, bevel=0.03)
            beam_between(f"Pylon_Cross_Brace_{i + 1:02d}_{brace + 1}b", mats["dark"], collection,
                         left_high, right_low, 0.22, 0.22, bevel=0.03)

        radial_part(f"Pylon_Cap_{i + 1:02d}", mats["dark"], collection,
                    25.00, angle_deg, 0.88, 4.00, 0.55, 9.00, bevel=0.09)
        radial_part(f"Pylon_Crown_Trim_{i + 1:02d}", mats["bronze"], collection,
                    25.00, angle_deg, 1.04, 4.30, 0.14, 9.32, bevel=0.04)
        radial_part(f"Pylon_Beacon_{i + 1:02d}", mats["cyan"], collection,
                    24.86, angle_deg, 0.16, 2.30, 0.20, 9.44, bevel=0.03)

        # Signal mast above every second pylon.
        if i % 2 == 0:
            mast_base = outward * 25.00 + Vector((0.0, 0.0, 9.30))
            beam_between(f"Pylon_Mast_{i + 1:02d}", mats["dark"], collection,
                         mast_base, mast_base + Vector((0.0, 0.0, 4.60)), 0.20, 0.20, bevel=0.03)
            for dish in range(3):
                height = 9.90 + dish * 1.35
                radial_part(f"Pylon_Mast_Vane_{i + 1:02d}_{dish + 1}", mats["bronze"], collection,
                            25.00, angle_deg, 0.10, 1.55 - dish * 0.35, 0.08, height, bevel=0.02)
            radial_part(f"Pylon_Mast_Lamp_{i + 1:02d}", mats["amber"], collection,
                        25.00, angle_deg, 0.30, 0.30, 0.30, 14.05, bevel=0.06)


def build_halo(mats, collection):
    """The suspended ring above the deck, plus its lanterns and banners."""
    ring_torus("Aerie_Halo_Dark", mats["dark"], collection, 19.90, 0.28, 8.65, 128, 10)
    ring_torus("Aerie_Halo_Bronze_Inner", mats["bronze"], collection, 19.62, 0.11, 8.65, 128, 8)
    ring_torus("Aerie_Halo_Energy", mats["cyan"], collection, 20.18, 0.08, 8.65, 128, 8)

    for i in range(12):
        angle = i * 30.0
        radial_part(f"Halo_Bronze_Clamp_{i + 1:02d}", mats["bronze"], collection,
                    19.90, angle, 1.15, 0.72, 0.68, 8.65, bevel=0.06)
        radial_part(f"Halo_Energy_Node_{i + 1:02d}", mats["cyan"], collection,
                    19.90, angle + 15.0, 0.92, 0.28, 0.18, 8.93, bevel=0.03)

    # Spokes tying the halo out to the pylon caps.
    for i in range(6):
        angle_deg = i * 60.0
        beam_between(f"Halo_Spoke_{i + 1:02d}", mats["dark"], collection,
                     polar(19.90, angle_deg, 8.65), polar(24.70, angle_deg, 8.92),
                     0.26, 0.34, bevel=0.04)
        beam_between(f"Halo_Spoke_Tie_{i + 1:02d}", mats["bronze"], collection,
                     polar(20.60, angle_deg, 8.52), polar(24.40, angle_deg, 8.80),
                     0.10, 0.10, bevel=0.0)

    # Hanging lanterns.
    for i in range(12):
        angle = i * 30.0 + 15.0
        beam_between(f"Halo_Lantern_Chain_{i + 1:02d}", mats["dark"], collection,
                     polar(19.90, angle, 8.55), polar(19.90, angle, 7.35),
                     0.06, 0.06, bevel=0.0)
        radial_part(f"Halo_Lantern_Shell_{i + 1:02d}", mats["bronze"], collection,
                    19.90, angle, 0.54, 0.54, 0.78, 6.95, bevel=0.09)
        radial_part(f"Halo_Lantern_Core_{i + 1:02d}",
                    mats["amber"] if i % 3 == 0 else mats["cyan"], collection,
                    19.90, angle, 0.34, 0.34, 0.56, 6.95, bevel=0.05)

    # Canvas pennants hung between the halo clamps. They stay narrow and are
    # skipped over the docking lanes so they never curtain off a terminal.
    for i in range(6):
        angle = i * 60.0 + 30.0
        if in_clear_wedge(angle, DOCK_AXES, 14.0):
            continue
        verts, faces = draped_panel_data(2.10, 2.60, 10, 8, sag=0.30, wave=0.13)
        add_mesh(f"Aerie_Banner_{i + 1:02d}", verts, faces, mats["canvas"], collection,
                 location=polar(19.90, angle, 8.28),
                 rotation=(0.0, 0.0, math.radians(angle + 90.0)),
                 bevel=0.0, uv_scale=0.5)
        radial_part(f"Aerie_Banner_Rod_{i + 1:02d}", mats["bronze"], collection,
                    19.90, angle, 0.09, 2.35, 0.09, 8.32, bevel=0.02)


def build_docks(mats, collection):
    """Raised pads and backdrop screens behind the contract and free-roam terminals.

    The terminals themselves are authored at hub-local radius 11, so the pads
    start outboard of that and read as the surface the terminal stands on.
    """
    for index, axis in enumerate(DOCK_AXES):
        tag = f"{index + 1:02d}"
        radial_part(f"Terminal_Dock_{tag}", mats["deck"], collection,
                    14.10, axis, 6.60, 9.20, 0.44, DECK_TOP + 0.22, bevel=0.12)
        radial_part(f"Terminal_Dock_Lip_{tag}", mats["bronze"], collection,
                    10.90, axis, 0.30, 9.40, 0.30, DECK_TOP + 0.15, bevel=0.05)
        radial_part(f"Terminal_Dock_Trim_{tag}", mats["cyan"], collection,
                    17.30, axis, 0.20, 9.00, 0.12, DECK_TOP + 0.50, bevel=0.03)

        # The pad cantilevers inboard over the open centre, so carry it on a
        # truss that springs from the deck ring underside.
        axis_rad = math.radians(axis)
        tangent = Vector((-math.sin(axis_rad), math.cos(axis_rad), 0.0))
        for side in (-1, 1):
            root = polar(19.30, axis, DECK_TOP - 0.55) + tangent * (side * 3.90)
            tip = polar(10.95, axis, DECK_TOP - 0.02) + tangent * (side * 3.10)
            beam_between(f"Dock_Truss_{tag}_{side:+d}", mats["dark"], collection,
                         root, tip, 0.46, 0.62, bevel=0.05)
            beam_between(f"Dock_Truss_Tie_{tag}_{side:+d}", mats["bronze"], collection,
                         root + Vector((0.0, 0.0, -0.10)),
                         polar(14.60, axis, DECK_TOP - 1.55) + tangent * (side * 3.50),
                         0.20, 0.20, bevel=0.03)
        for strut in range(4):
            radius = 12.40 + strut * 2.15
            beam_between(f"Dock_Truss_Rung_{tag}_{strut + 1}", mats["dark"], collection,
                         polar(radius, axis, DECK_TOP - 0.34) + tangent * -3.50,
                         polar(radius, axis, DECK_TOP - 0.34) + tangent * 3.50,
                         0.16, 0.16, bevel=0.0)
        radial_part(f"Dock_Underlight_{tag}", mats["cyan"], collection,
                    13.60, axis, 5.20, 0.14, 0.08, DECK_TOP - 0.06, bevel=0.02)

        # Stepped transition from the deck ring down onto the pad.
        for step in range(3):
            radial_part(f"Terminal_Dock_Step_{tag}_{step + 1}", mats["deck"], collection,
                        18.10 + step * 0.55, axis, 0.55, 8.20 - step * 0.9,
                        0.16, DECK_TOP + 0.36 - step * 0.11, bevel=0.04)

        # Backdrop screen behind the terminal.
        radial_part(f"Dock_Backdrop_{tag}", mats["dark"], collection,
                    18.35, axis, 0.42, 8.40, 4.60, DECK_TOP + 2.60, bevel=0.10)
        radial_part(f"Dock_Screen_{tag}", mats["cyan"], collection,
                    18.10, axis, 0.10, 6.40, 2.90, DECK_TOP + 2.95, bevel=0.04)
        radial_part(f"Dock_Cornice_{tag}", mats["bronze"], collection,
                    18.35, axis, 0.72, 8.90, 0.34, DECK_TOP + 5.05, bevel=0.06)

        for side in (-1, 1):
            radial_part(f"Dock_Buttress_{tag}_{side:+d}", mats["dark"], collection,
                        18.60, axis + side * 12.5, 1.20, 0.70, 4.20, DECK_TOP + 2.40, bevel=0.07)
            radial_part(f"Dock_Lamp_{tag}_{side:+d}", mats["amber"], collection,
                        17.95, axis + side * 12.0, 0.26, 0.26, 0.42, DECK_TOP + 4.55, bevel=0.05)

        # Cargo staged either side of the approach.
        for side in (-1, 1):
            for crate in range(3):
                radial_part(f"Dock_Crate_{tag}_{side:+d}_{crate + 1}", mats["dark"], collection,
                            19.60 + crate * 0.15, axis + side * (17.0 + crate * 4.2),
                            1.35, 1.35, 1.20 - crate * 0.22,
                            DECK_TOP + 0.60 - crate * 0.11, bevel=0.06)
                radial_part(f"Crate_Band_{tag}_{side:+d}_{crate + 1}", mats["bronze"],
                            collection,
                            19.60 + crate * 0.15, axis + side * (17.0 + crate * 4.2),
                            1.42, 0.18, 1.24 - crate * 0.22,
                            DECK_TOP + 0.60 - crate * 0.11, bevel=0.02)


def build_upgrade_gantry(mats, collection):
    """An overhead frame on the +X axis that arches over the Unity upgrade pad.

    The pad sits at radius 13 with a 2.5 m radius, and Unity spins calibration
    arms just above it, so every part here stays outside radius 16 or above
    z = 6.4 to leave that volume clear.
    """
    apex_height = 8.10
    leg_radius = 20.40
    for side in (-1, 1):
        tag = f"{'L' if side < 0 else 'R'}"
        foot = polar(leg_radius, UPGRADE_AXIS + side * 20.0, DECK_TOP)
        knee = polar(leg_radius - 0.70, UPGRADE_AXIS + side * 19.0, DECK_TOP + 4.30)
        head = polar(18.60, UPGRADE_AXIS + side * 15.5, apex_height - 0.60)

        beam_between(f"Gantry_Leg_{tag}", mats["dark"], collection, foot, knee, 1.05, 1.05,
                     bevel=0.10)
        beam_between(f"Gantry_Leg_Upper_{tag}", mats["dark"], collection, knee, head, 0.86, 0.86,
                     bevel=0.09)
        beam_between(f"Gantry_Rib_{tag}", mats["bronze"], collection,
                     foot + Vector((0.0, 0.0, 0.10)), knee, 0.24, 1.10, bevel=0.04)
        radial_part(f"Gantry_Foot_Plate_{tag}", mats["bronze"], collection,
                    leg_radius, UPGRADE_AXIS + side * 20.0, 2.20, 2.20, 0.26,
                    DECK_TOP + 0.13, bevel=0.06)
        for bolt in range(4):
            bolt_angle = UPGRADE_AXIS + side * 20.0 + (bolt - 1.5) * 2.6
            radial_part(f"Gantry_Foot_Bolt_{tag}_{bolt + 1}", mats["dark"], collection,
                        leg_radius, bolt_angle, 0.26, 0.26, 0.16, DECK_TOP + 0.30, bevel=0.03)

    # The arch itself, swept inward over the pad as a chain of short beams.
    arch_points = []
    steps = 16
    for step in range(steps + 1):
        t = step / steps
        angle = UPGRADE_AXIS + (1.0 - 2.0 * t) * 15.5
        radius = 18.60 - math.sin(t * math.pi) * 5.30
        height = (apex_height - 0.60) + math.sin(t * math.pi) * 1.35
        arch_points.append(polar(radius, angle, height))
    for step in range(steps):
        beam_between(f"Gantry_Arch_{step + 1:02d}", mats["dark"], collection,
                     arch_points[step], arch_points[step + 1], 0.80, 0.72, bevel=0.05)
        beam_between(f"Gantry_Arch_Rib_{step + 1:02d}", mats["bronze"], collection,
                     arch_points[step] + Vector((0.0, 0.0, 0.42)),
                     arch_points[step + 1] + Vector((0.0, 0.0, 0.42)),
                     0.22, 0.16, bevel=0.02)

    # Calibration emitters hanging from the arch, aimed down at the pad.
    for step in range(3, steps - 2, 3):
        anchor = arch_points[step]
        beam_between(f"Gantry_Emitter_Stem_{step:02d}", mats["dark"], collection,
                     anchor, anchor + Vector((0.0, 0.0, -0.85)), 0.14, 0.14, bevel=0.0)
        drop = anchor + Vector((0.0, 0.0, -1.20))
        verts, faces, sides = frustum_data(0.46, 0.16, 0.70, 20)
        add_mesh(f"Gantry_Emitter_{step:02d}", verts, faces, mats["bronze"], collection,
                 location=drop, bevel=0.04, smooth_faces=sides)
        verts, faces, sides = frustum_data(0.30, 0.10, 0.24, 20)
        add_mesh(f"Gantry_Emitter_Lens_{step:02d}", verts, faces, mats["cyan"], collection,
                 location=drop + Vector((0.0, 0.0, -0.42)), bevel=0.02, smooth_faces=sides)

    # Signage board spanning the arch shoulders.
    radial_part("Gantry_Sign_Board", mats["dark"], collection,
                19.05, UPGRADE_AXIS, 0.36, 7.60, 1.30, apex_height + 1.35, bevel=0.07)
    radial_part("Gantry_Sign_Glow", mats["amber"], collection,
                18.86, UPGRADE_AXIS, 0.10, 6.70, 0.86, apex_height + 1.35, bevel=0.03)
    radial_part("Gantry_Sign_Crest", mats["bronze"], collection,
                19.05, UPGRADE_AXIS, 0.70, 8.10, 0.24, apex_height + 2.12, bevel=0.05)


def build_deck_props(mats, collection):
    """Scatter dressing across the deck ring, avoiding the approach lanes."""
    crate_plan = (
        (21.30, 45.0, 1.60, 1.30), (22.60, 51.0, 1.20, 0.95), (21.05, 57.0, 1.45, 1.15),
        (21.60, 132.0, 1.55, 1.25), (22.75, 138.5, 1.15, 0.90), (20.95, 126.0, 1.35, 1.05),
        (21.45, 219.0, 1.50, 1.20), (22.55, 226.0, 1.25, 1.00), (21.15, 232.5, 1.40, 1.10),
        (21.70, 303.0, 1.55, 1.25), (22.70, 310.0, 1.20, 0.95), (21.00, 316.5, 1.45, 1.15),
    )
    for index, (radius, angle, footprint, height) in enumerate(crate_plan):
        if in_clear_wedge(angle, TERMINAL_AXES, 14.0):
            continue
        radial_part(f"Deck_Crate_{index + 1:02d}", mats["dark"], collection,
                    radius, angle, footprint, footprint, height,
                    DECK_TOP + height * 0.5, bevel=0.06)
        radial_part(f"Crate_Seal_{index + 1:02d}", mats["bronze"], collection,
                    radius, angle, footprint + 0.06, 0.16, height * 0.9,
                    DECK_TOP + height * 0.5, bevel=0.02)
        radial_part(f"Crate_Tag_{index + 1:02d}", mats["cyan"], collection,
                    radius, angle, 0.06, footprint * 0.42, 0.16,
                    DECK_TOP + height * 0.78, bevel=0.02)

    # Fuel drums grouped on pallets.
    for group, (radius, angle) in enumerate(((22.20, 72.0), (22.20, 252.0), (21.90, 342.0))):
        if in_clear_wedge(angle, TERMINAL_AXES, 14.0):
            continue
        radial_part(f"Pallet_{group + 1:02d}", mats["bronze"], collection,
                    radius, angle, 2.40, 2.40, 0.16, DECK_TOP + 0.08, bevel=0.03)
        for drum in range(4):
            offset_angle = angle + (drum % 2 - 0.5) * 3.1
            offset_radius = radius + (drum // 2 - 0.5) * 1.15
            verts, faces, sides = frustum_data(0.44, 0.44, 1.15, 20)
            add_mesh(f"Fuel_Drum_{group + 1:02d}_{drum + 1}", verts, faces, mats["dark"],
                     collection, location=polar(offset_radius, offset_angle, DECK_TOP + 0.74),
                     bevel=0.05, smooth_faces=sides)
            verts, faces = torus_data(0.45, 0.06, 24, 6)
            add_mesh(f"Drum_Band_{group + 1:02d}_{drum + 1}", verts, faces, mats["amber"],
                     collection, location=polar(offset_radius, offset_angle, DECK_TOP + 0.92),
                     bevel=0.0, smooth_faces="ALL")

    # Windsock on the leeward side.
    sock_angle = 300.0
    beam_between("Windsock_Pole", mats["dark"], collection,
                 polar(23.40, sock_angle, DECK_TOP), polar(23.40, sock_angle, DECK_TOP + 4.20),
                 0.16, 0.16, bevel=0.02)
    ring_torus("Windsock_Hoop", mats["bronze"], collection, 0.42, 0.06, 0.0, 24, 6).location = \
        polar(22.95, sock_angle, DECK_TOP + 3.95)
    verts, faces, sides = frustum_data(0.42, 0.16, 1.70, 20, cap_bottom=False, cap_top=False)
    add_mesh("Windsock_Cone", verts, faces, mats["canvas"], collection,
             location=polar(22.10, sock_angle, DECK_TOP + 3.95),
             rotation=(0.0, math.radians(-78.0), math.radians(sock_angle)),
             bevel=0.0, smooth_faces=sides, uv_scale=0.6)

    # Antenna masts.
    for index, angle in enumerate((36.0, 156.0, 276.0)):
        if in_clear_wedge(angle, TERMINAL_AXES, 12.0):
            continue
        beam_between(f"Antenna_Mast_{index + 1:02d}", mats["dark"], collection,
                     polar(20.60, angle, DECK_TOP), polar(20.60, angle, DECK_TOP + 5.40),
                     0.22, 0.22, bevel=0.03)
        for stay in range(3):
            stay_angle = angle + stay * 120.0
            beam_between(f"Antenna_Stay_{index + 1:02d}_{stay + 1}", mats["bronze"], collection,
                         polar(20.60, angle, DECK_TOP + 4.60),
                         polar(20.60, angle, DECK_TOP) + polar(1.90, stay_angle, 0.0),
                         0.05, 0.05, bevel=0.0)
        radial_part(f"Antenna_Array_{index + 1:02d}", mats["bronze"], collection,
                    20.60, angle, 0.10, 1.90, 0.10, DECK_TOP + 5.05, bevel=0.02)
        radial_part(f"Antenna_Beacon_{index + 1:02d}", mats["amber"], collection,
                    20.60, angle, 0.24, 0.24, 0.24, DECK_TOP + 5.55, bevel=0.05)


# ---------------------------------------------------------------------------
# Mesh consolidation.

# Objects whose names start with these prefixes stay as individual meshes:
# Unity derives box colliders from them via
# HubSettings.PremiumVisualStructuralColliderNamePrefixes. Everything solid
# enough to block a courier is listed here; purely decorative overlays use
# different prefixes so they merge away.
COLLIDER_PREFIXES = (
    "Aerie_Pylon_",     # 12 pylon legs
    "Pylon_Cap_",       # 6 pylon caps
    "Gantry_Leg_",      # 4 upgrade gantry legs
    "Dock_Backdrop_",   # 2 terminal backdrop walls
    "Dock_Buttress_",   # 4 backdrop buttresses
    "Vent_Stack_",      # 6 deck vent stacks
    "Antenna_Mast_",    # 3 antenna masts
    "Deck_Crate_",      # 12 deck cargo crates
    "Dock_Crate_",      # 12 staged dock crates
    "Mooring_Clamp_",   # 8 clamp bases + 16 jaws
    "Fuel_Drum_",       # 12 fuel drums
    "Windsock_Pole",    # 1 windsock pole
)


def merge_by_material(collection):
    """Bake modifiers and join everything that is not a collider source.

    Authoring the hub as ~1100 small objects keeps the generator readable, but
    importing that many renderers into Unity is wasteful. Each material family
    is flattened into a single mesh, which leaves the scene at a couple of dozen
    objects while preserving the collider naming contract.
    """
    depsgraph = bpy.context.evaluated_depsgraph_get()
    families = {}
    consumed = []

    for obj in list(collection.objects):
        if obj.type != 'MESH':
            continue
        if obj.name.startswith(COLLIDER_PREFIXES):
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
        merged = bpy.data.objects.new(material_name.replace("SunspireExchange_", "Hub_"), mesh)
        merged.data.materials.append(bucket["material"])
        collection.objects.link(merged)

    # The collider sources keep their bevel modifiers; bake them so the exported
    # mesh bounds Unity reads for BoxCollider sizing match what is rendered.
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in list(collection.objects):
        if not obj.name.startswith(COLLIDER_PREFIXES) or not obj.modifiers:
            continue
        evaluated = obj.evaluated_get(depsgraph)
        baked = bpy.data.meshes.new_from_object(evaluated)
        old_mesh = obj.data
        obj.modifiers.clear()
        obj.data = baked
        if old_mesh.users == 0:
            bpy.data.meshes.remove(old_mesh)


# ---------------------------------------------------------------------------
# Preview rig and export.

def _aim(obj, target=(0.0, 0.0, 3.0)):
    obj.rotation_euler = (Vector(target) - Vector(obj.location)) \
        .to_track_quat('-Z', 'Y').to_euler()


# Reference framings rendered after the build so the hub can be reviewed
# without opening Blender.
PREVIEW_VIEWS = (
    ("Hero", (54.0, -58.0, 30.0), 46.0, (0.0, 0.0, 3.0)),
    ("Deck", (17.0, -25.0, 12.5), 30.0, (2.0, 4.0, 3.5)),
    ("Underside", (40.0, -42.0, -14.0), 40.0, (0.0, 0.0, -3.0)),
    ("Top", (0.0, -1.0, 96.0), 50.0, (0.0, 0.0, 1.4)),
)


def build_preview_rig(collection):
    camera_data = bpy.data.cameras.new("Preview Camera")
    camera_data.lens = 46.0
    camera = bpy.data.objects.new("Preview Camera", camera_data)
    camera.location = (54.0, -58.0, 30.0)
    _aim(camera)
    collection.objects.link(camera)
    bpy.context.scene.camera = camera

    # Warm low sun reading as the desert key light.
    sun_data = bpy.data.lights.new("Preview Sun", 'SUN')
    sun_data.energy = 6.5
    sun_data.color = (1.0, 0.72, 0.44)
    sun_data.angle = math.radians(2.0)
    sun = bpy.data.objects.new("Preview Sun", sun_data)
    sun.location = (40.0, -46.0, 40.0)
    _aim(sun)
    collection.objects.link(sun)

    key_data = bpy.data.lights.new("Preview Key", 'AREA')
    key_data.energy = 260000.0
    key_data.shape = 'DISK'
    key_data.size = 34.0
    key_data.color = (1.0, 0.62, 0.34)
    key = bpy.data.objects.new("Preview Key", key_data)
    key.location = (36.0, -44.0, 36.0)
    _aim(key)
    collection.objects.link(key)

    fill_data = bpy.data.lights.new("Preview Fill", 'AREA')
    fill_data.energy = 120000.0
    fill_data.size = 40.0
    fill_data.color = (0.30, 0.58, 1.0)
    fill = bpy.data.objects.new("Preview Fill", fill_data)
    fill.location = (-44.0, 26.0, 26.0)
    _aim(fill)
    collection.objects.link(fill)

    # Bounce card so the substructure is not a silhouette.
    bounce_data = bpy.data.lights.new("Preview Bounce", 'AREA')
    bounce_data.energy = 90000.0
    bounce_data.size = 60.0
    bounce_data.color = (0.95, 0.60, 0.36)
    bounce = bpy.data.objects.new("Preview Bounce", bounce_data)
    bounce.location = (0.0, 0.0, -34.0)
    _aim(bounce, (0.0, 0.0, 0.0))
    collection.objects.link(bounce)

    scene = bpy.context.scene
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.020, 0.026, 0.042, 1.0)
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = PREVIEW_PATH
    scene.view_settings.look = 'AgX - Base Contrast'
    scene.view_settings.exposure = 1.1


def render_previews():
    """Render the reference framings listed in PREVIEW_VIEWS."""
    scene = bpy.context.scene
    camera = scene.camera
    written = []
    for name, location, lens, target in PREVIEW_VIEWS:
        camera.location = location
        camera.data.lens = lens
        _aim(camera, target)
        bpy.context.view_layer.update()
        path = PREVIEW_PATH if name == "Hero" else os.path.join(
            SOURCE_DIR, "SunspireExchangePreview_{:s}.png".format(name))
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)
    return written


def export_assets(export_collection, preview_collection):
    # Render the reference previews first, while the rig is still visible.
    render_previews()

    for obj in bpy.data.objects:
        obj.select_set(False)
    for obj in export_collection.objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = next(iter(export_collection.objects), None)

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

    # Textures live in a Textures folder beside the .blend. Rewriting the
    # absolute paths as blend-relative ones keeps the file working if the repo
    # is cloned somewhere else, then re-save so the remap is persisted.
    for image in bpy.data.images:
        if image.filepath:
            relative = "//Textures/" + os.path.basename(image.filepath)
            image.filepath = relative
            image.filepath_raw = relative
    bpy.ops.wm.save_mainfile()


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

def build(export=True):
    purge_scene()
    export_collection = make_collection(EXPORT_COLLECTION)
    preview_collection = make_collection(PREVIEW_COLLECTION)

    mats = build_materials()
    build_substructure(mats, export_collection)
    build_foundation_and_deck(mats, export_collection)
    build_perimeter(mats, export_collection)
    build_pylons(mats, export_collection)
    build_halo(mats, export_collection)
    build_docks(mats, export_collection)
    build_upgrade_gantry(mats, export_collection)
    build_deck_props(mats, export_collection)

    authored_objects = len(export_collection.objects)
    bpy.context.view_layer.update()
    merge_by_material(export_collection)

    build_preview_rig(preview_collection)
    bpy.context.view_layer.update()

    triangles = evaluated_triangle_count(export_collection)
    export_error = None
    if export:
        try:
            export_assets(export_collection, preview_collection)
        except Exception as exception:  # Surfaced in the report rather than losing the build.
            export_error = "{:s}: {:s}".format(type(exception).__name__, str(exception))

    BUILD_REPORT.update({
        "authored_objects": authored_objects,
        "objects": len(export_collection.objects),
        "object_names": sorted(obj.name for obj in export_collection.objects),
        "triangles": triangles,
        "export_error": export_error,
        "materials": sorted(material.name for material in mats.values()),
        "fbx": MODEL_PATH,
        "fbx_exists": os.path.exists(MODEL_PATH),
        "blend": BLEND_PATH,
        "preview": PREVIEW_PATH,
        "preview_exists": os.path.exists(PREVIEW_PATH),
        "textures": sorted(os.listdir(TEXTURE_DIR)),
        "collider_pylons": len([o for o in export_collection.objects
                                if o.name.startswith("Aerie_Pylon_")]),
        "collider_caps": len([o for o in export_collection.objects
                              if o.name.startswith("Pylon_Cap_")]),
        "collider_gantry_legs": len([o for o in export_collection.objects
                                     if o.name.startswith("Gantry_Leg_")]),
    })
    return BUILD_REPORT
