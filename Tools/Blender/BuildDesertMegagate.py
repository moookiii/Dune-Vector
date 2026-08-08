"""Builds the Desert Megagate landmark: a colossal half-buried masonry transit arch.

Everything is authored as real geometry - individual masonry blocks, true wedge
voussoirs, corbelled cornices, dentil rows, riveted bronze plating and a
suspended machine ring - rather than stretched cubes standing in for detail.

Texel density is controlled by projecting UVs from world space at a fixed
metres-per-tile ratio per material, so a 40 m pylon course and a 0.4 m rivet
receive the same texture scale and no part ever ends up with a crowded or
stretched UV layout.

Run inside Blender:
    exec(open(r"C:\\Dune Vector URP\\Tools\\Blender\\BuildDesertMegagate.py").read())
"""

import bmesh
import bpy
import json
import math
import os
import random
import shutil
import struct
from mathutils import Euler, Matrix, Vector, noise

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

PROJECT_ROOT = r"C:\Dune Vector URP"
RESOURCES_DIR = os.path.join(PROJECT_ROOT, "Assets", "DuneVector", "Resources")
MODEL_DIR = os.path.join(PROJECT_ROOT, "Assets", "DuneVector", "Models", "DesertMegagate")
SOURCE_DIR = os.path.join(PROJECT_ROOT, "ArtSource", "Blender", "DesertMegagate")
TEXTURE_CACHE = os.path.join(SOURCE_DIR, "TexturesBaked")

BLEND_PATH = os.path.join(SOURCE_DIR, "DesertMegagate.blend")
MASTER_GLB = os.path.join(MODEL_DIR, "DesertMegagate.glb")
RUNTIME_GLB = os.path.join(RESOURCES_DIR, "DesertMegagate.glb")
HERO_RENDER = os.path.join(MODEL_DIR, "DesertMegagate_Preview.png")
FRONT_RENDER = os.path.join(MODEL_DIR, "DesertMegagate_Preview_Elevation.png")
DETAIL_RENDER = os.path.join(MODEL_DIR, "DesertMegagate_Preview_Detail.png")

for directory in (MODEL_DIR, SOURCE_DIR, TEXTURE_CACHE):
    os.makedirs(directory, exist_ok=True)

# ---------------------------------------------------------------------------
# Dimensions (metres, Z up, -Y is the approach face)
# ---------------------------------------------------------------------------

PYLON_X = 52.0            # centreline of each pylon
PLINTH_TOP = 12.0
SHAFT_TOP = 104.0
SHAFT_HALF_X = (17.5, 13.0)   # bottom, top
SHAFT_HALF_Y = (15.5, 11.5)
COURSE_HEIGHT = 4.2       # megalithic courses read better at landmark distance
BLOCK_LENGTH = 6.0
RING_WIDTH = 4.0
CORNICE_TOP = 113.0
MERLON_TOP = 118.5

ARCH_SPRING_Z = 96.0
ARCH_RADIUS = 42.4        # centreline of the main voussoir ring
ARCH_WIDTH = 9.5
ARCH_DEPTH = 18.0
ARCHIVOLT_RADIUS = 48.4
ARCHIVOLT_WIDTH = 4.4
SOFFIT_RADIUS = 36.6

RING_CENTRE_Z = 86.0
RING_RADIUS = 22.0

BUTTRESS_X = 91.0

rng = random.Random(20260808)

# ---------------------------------------------------------------------------
# Scene helpers
# ---------------------------------------------------------------------------


def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    for group in (bpy.data.meshes, bpy.data.materials, bpy.data.images,
                  bpy.data.cameras, bpy.data.lights, bpy.data.curves):
        for datablock in list(group):
            if datablock.users == 0:
                group.remove(datablock)


def make_collection(name, parent=None):
    collection = bpy.data.collections.new(name)
    (parent.children if parent else bpy.context.scene.collection.children).link(collection)
    return collection


# ---------------------------------------------------------------------------
# Textures - cached down-rezzed copies keep the exported GLB small
# ---------------------------------------------------------------------------

_texture_cache = {}


def prepared_image(relative_path, max_size, non_color=False):
    """Load a project texture, resized once into the art-source cache folder."""
    key = (relative_path, max_size)
    if key in _texture_cache:
        return _texture_cache[key]

    source = os.path.join(RESOURCES_DIR, relative_path)
    baked_name = "{}_{}.jpg".format(
        os.path.splitext(os.path.basename(relative_path))[0], max_size)
    baked = os.path.join(TEXTURE_CACHE, baked_name)

    if not os.path.exists(baked):
        staging = bpy.data.images.load(source, check_existing=False)
        if max(staging.size) > max_size:
            aspect = staging.size[1] / staging.size[0]
            if staging.size[0] >= staging.size[1]:
                staging.scale(max_size, max(1, int(round(max_size * aspect))))
            else:
                staging.scale(max(1, int(round(max_size / aspect))), max_size)
        staging.file_format = "JPEG"
        staging.filepath_raw = baked
        staging.save()
        bpy.data.images.remove(staging)

    image = bpy.data.images.load(baked, check_existing=True)
    image.colorspace_settings.name = "Non-Color" if non_color else "sRGB"
    _texture_cache[key] = image
    return image


# ---------------------------------------------------------------------------
# Materials
# ---------------------------------------------------------------------------


def socket(bsdf, *names):
    for name in names:
        found = bsdf.inputs.get(name)
        if found is not None:
            return found
    raise KeyError(names)


def surface_material(name, tile, colour_map, tint, roughness_map=None, roughness=0.9,
                     normal_map=None, metallic=0.0, normal_strength=1.0,
                     colour_size=1024, map_size=1024):
    """Image driven PBR surface. The tint multiply is re-applied to the GLB as a
    baseColorFactor so Unity matches Blender exactly."""
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    tree = material.node_tree
    nodes, links = tree.nodes, tree.links
    nodes.clear()

    output = nodes.new("ShaderNodeOutputMaterial")
    output.location = (620, 0)
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (300, 0)
    links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])

    colour = nodes.new("ShaderNodeTexImage")
    colour.location = (-320, 180)
    colour.image = prepared_image(colour_map, colour_size)
    tinting = nodes.new("ShaderNodeMixRGB")
    tinting.location = (0, 180)
    tinting.blend_type = "MULTIPLY"
    tinting.inputs[0].default_value = 1.0
    tinting.inputs[2].default_value = (*tint, 1.0)
    links.new(colour.outputs["Color"], tinting.inputs[1])
    links.new(tinting.outputs["Color"], socket(bsdf, "Base Color"))

    if roughness_map:
        rough = nodes.new("ShaderNodeTexImage")
        rough.location = (-320, -80)
        rough.image = prepared_image(roughness_map, map_size, non_color=True)
        scale = nodes.new("ShaderNodeMath")
        scale.location = (0, -80)
        scale.operation = "MULTIPLY"
        scale.inputs[1].default_value = roughness
        links.new(rough.outputs["Color"], scale.inputs[0])
        links.new(scale.outputs["Value"], socket(bsdf, "Roughness"))
    else:
        socket(bsdf, "Roughness").default_value = roughness

    if normal_map:
        normal_texture = nodes.new("ShaderNodeTexImage")
        normal_texture.location = (-320, -360)
        normal_texture.image = prepared_image(normal_map, map_size, non_color=True)
        normal_node = nodes.new("ShaderNodeNormalMap")
        normal_node.location = (0, -360)
        normal_node.inputs["Strength"].default_value = normal_strength
        links.new(normal_texture.outputs["Color"], normal_node.inputs["Color"])
        links.new(normal_node.outputs["Normal"], socket(bsdf, "Normal"))

    socket(bsdf, "Metallic").default_value = metallic
    material["dv_tile"] = tile
    material["dv_base_color_factor"] = [*tint, 1.0]
    material["dv_roughness_factor"] = roughness if roughness_map else 1.0
    return material


def signal_material(name, tile, base, emission, strength, roughness=0.25, metallic=0.1):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes["Principled BSDF"]
    socket(bsdf, "Base Color").default_value = (*base, 1.0)
    socket(bsdf, "Roughness").default_value = roughness
    socket(bsdf, "Metallic").default_value = metallic
    socket(bsdf, "Emission Color", "Emission").default_value = (*emission, 1.0)
    socket(bsdf, "Emission Strength").default_value = strength
    material["dv_tile"] = tile
    return material


# ---------------------------------------------------------------------------
# Geometry part builder
# ---------------------------------------------------------------------------

ASSET_OBJECTS = []


def erosion(position, frequency, amount):
    if amount <= 0.0:
        return Vector((0.0, 0.0, 0.0))
    return noise.noise_vector(Vector(position) * frequency) * amount


class Part:
    """Accumulates many primitives into one mesh, then bevels and UV projects it."""

    def __init__(self, name, material, collection, bevel=0.07, segments=1, smooth=False,
                 export=True):
        self.name = name
        self.material = material
        self.collection = collection
        self.bevel = bevel
        self.segments = segments
        self.smooth = smooth
        self.export = export
        self.tile = float(material.get("dv_tile", 6.0))
        self.bm = bmesh.new()

    # -- primitives ---------------------------------------------------------

    def hexahedron(self, corners):
        verts = [self.bm.verts.new(Vector(c)) for c in corners]
        for indices in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
                        (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
            self.bm.faces.new([verts[i] for i in indices])

    def box(self, centre, size, rotation=(0.0, 0.0, 0.0), erode=0.0, frequency=0.09,
            taper=1.0, taper_y=None):
        hx, hy, hz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
        tx = hx * taper
        ty = hy * (taper if taper_y is None else taper_y)
        local = ((-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
                 (-tx, -ty, hz), (tx, -ty, hz), (tx, ty, hz), (-tx, ty, hz))
        basis = Euler(rotation, "XYZ").to_matrix()
        origin = Vector(centre)
        corners = []
        for point in local:
            world = origin + basis @ Vector(point)
            corners.append(world + erosion(world, frequency, erode))
        self.hexahedron(corners)

    def wedge(self, inner_radius, outer_radius, angle_a, angle_b, y_min, y_max,
              centre=(0.0, 0.0), erode=0.0, frequency=0.09):
        """True radial voussoir - the faces converge on the arch centre."""
        cx, cz = centre
        corners = []
        for y in (y_min, y_max):
            for radius, angle in ((inner_radius, angle_a), (outer_radius, angle_a),
                                  (outer_radius, angle_b), (inner_radius, angle_b)):
                world = Vector((cx + radius * math.cos(angle), y,
                                cz + radius * math.sin(angle)))
                corners.append(world + erosion(world, frequency, erode))
        self.hexahedron(corners)

    def cylinder(self, centre, radius, height, segments=10, matrix=None, taper=1.0):
        transform = Matrix.Translation(Vector(centre))
        if matrix is not None:
            transform = transform @ matrix
        try:
            bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False,
                                  segments=segments, radius1=radius,
                                  radius2=radius * taper, depth=height, matrix=transform)
        except TypeError:
            bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False,
                                  segments=segments, diameter1=radius,
                                  diameter2=radius * taper, depth=height, matrix=transform)

    def tube(self, start, end, radius, segments=8):
        start_v, end_v = Vector(start), Vector(end)
        delta = end_v - start_v
        if delta.length < 1e-5:
            return
        rotation = delta.to_track_quat("Z", "Y").to_matrix().to_4x4()
        self.cylinder((start_v + end_v) * 0.5, radius, delta.length, segments, rotation)

    def stud(self, centre, radius, height, axis="Y", segments=8):
        spin = {"X": Matrix.Rotation(math.radians(90.0), 4, "Y"),
                "Y": Matrix.Rotation(math.radians(-90.0), 4, "X"),
                "Z": Matrix.Identity(4)}[axis]
        self.cylinder(centre, radius, height, segments, spin, taper=0.62)

    def rock(self, centre, scale, seed, subdivisions=1, roughness=0.32):
        try:
            created = bmesh.ops.create_icosphere(
                self.bm, subdivisions=subdivisions, radius=1.0, matrix=Matrix.Identity(4))
        except TypeError:
            created = bmesh.ops.create_icosphere(
                self.bm, subdivisions=subdivisions, diameter=1.0, matrix=Matrix.Identity(4))
        basis = Euler((rng.uniform(0, 6.28), rng.uniform(0, 6.28), rng.uniform(0, 6.28)),
                      "XYZ").to_matrix()
        origin = Vector(centre)
        for vert in created["verts"]:
            local = Vector((vert.co.x * scale[0], vert.co.y * scale[1], vert.co.z * scale[2]))
            local = basis @ local
            world = origin + local
            vert.co = world + erosion(world + Vector((seed, seed, seed)), 0.22,
                                      roughness * min(scale))

    def surface_grid(self, samples, height_fn, closed_skirt=None):
        """Builds a displaced sheet from a callable - used for the sand drifts."""
        rows, cols = samples
        grid = []
        for row in range(rows):
            line = []
            for col in range(cols):
                u = row / (rows - 1.0)
                v = col / (cols - 1.0)
                point = height_fn(u, v)
                line.append(self.bm.verts.new(point) if point is not None else None)
            grid.append(line)
        for row in range(rows - 1):
            for col in range(cols - 1):
                quad = (grid[row][col], grid[row][col + 1],
                        grid[row + 1][col + 1], grid[row + 1][col])
                if all(quad):
                    self.bm.faces.new(quad)
        if closed_skirt is not None:
            for row in range(rows - 1):
                for col in range(cols - 1):
                    pass
        return grid

    # -- output -------------------------------------------------------------

    def build(self):
        bm = self.bm
        if not bm.faces:
            bm.free()
            return None
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        if self.bevel > 0.0:
            sharp = []
            for edge in bm.edges:
                if len(edge.link_faces) != 2:
                    continue
                if edge.calc_face_angle(0.0) > math.radians(24.0):
                    sharp.append(edge)
            if sharp:
                bmesh.ops.bevel(bm, geom=sharp, offset=self.bevel, offset_type="OFFSET",
                                segments=self.segments, profile=0.5, affect="EDGES",
                                clamp_overlap=True, material=-1, loop_slide=True)
        bm.normal_update()
        project_world_uvs(bm, self.tile)

        mesh = bpy.data.meshes.new(self.name + " Mesh")
        bm.to_mesh(mesh)
        bm.free()
        if self.smooth:
            for polygon in mesh.polygons:
                polygon.use_smooth = True
        obj = bpy.data.objects.new(self.name, mesh)
        self.collection.objects.link(obj)
        obj.data.materials.append(self.material)
        if self.export:
            ASSET_OBJECTS.append(obj)
        return obj


def project_world_uvs(bm, tile):
    """Box projection in world space at a fixed metres-per-tile ratio.

    Every part - a 40 m course of masonry or a 0.4 m rivet - ends up with the
    same texel density, so no island is ever crowded, stretched, or overlapping
    at a different scale from its neighbours."""
    uv_layer = bm.loops.layers.uv.verify()
    inverse = 1.0 / tile
    for face in bm.faces:
        normal = face.normal
        axis = max(range(3), key=lambda i: abs(normal[i]))
        flip = -1.0 if normal[axis] < 0.0 else 1.0
        for loop in face.loops:
            co = loop.vert.co
            if axis == 0:
                u, v = co.y * flip, co.z
            elif axis == 1:
                u, v = co.x * -flip, co.z
            else:
                u, v = co.x, co.y * flip
            loop[uv_layer].uv = (u * inverse, v * inverse)


# ---------------------------------------------------------------------------
# Composite builders
# ---------------------------------------------------------------------------


def lerp(a, b, t):
    return a + (b - a) * t


def shaft_half_extents(z):
    t = min(max((z - PLINTH_TOP) / (SHAFT_TOP - PLINTH_TOP), 0.0), 1.0)
    return lerp(SHAFT_HALF_X[0], SHAFT_HALF_X[1], t), lerp(SHAFT_HALF_Y[0], SHAFT_HALF_Y[1], t)


def masonry_course(part, centre_x, half_x, half_y, z_centre, height, ring_width,
                   block_length, course_index, erode=0.10, jitter=0.16, missing=0.0):
    """One ring of individual blocks around a rectangular tower footprint."""
    long_on_x = course_index % 2 == 0
    inset = ring_width if long_on_x else 0.0

    for sign in (-1.0, 1.0):
        run = 2.0 * (half_y - (0.0 if long_on_x else ring_width))
        count = max(2, int(round(run / block_length)) + (course_index % 2))
        step = run / count
        for index in range(count):
            if missing > 0.0 and rng.random() < missing:
                continue
            y = -run * 0.5 + step * (index + 0.5)
            push = rng.uniform(-0.05, jitter)
            part.box(
                (centre_x + sign * (half_x - ring_width * 0.5 + push * 0.5), y, z_centre),
                (ring_width + push, step - 0.22, height - 0.18),
                erode=erode)

    for sign in (-1.0, 1.0):
        run = 2.0 * (half_x - inset)
        count = max(2, int(round(run / block_length)) + ((course_index + 1) % 2))
        step = run / count
        for index in range(count):
            if missing > 0.0 and rng.random() < missing:
                continue
            x = -run * 0.5 + step * (index + 0.5)
            push = rng.uniform(-0.05, jitter)
            part.box(
                (centre_x + x, sign * (half_y - ring_width * 0.5 + push * 0.5), z_centre),
                (step - 0.22, ring_width + push, height - 0.18),
                erode=erode)


def quoin_corners(part, centre_x, half_x, half_y, z_centre, height, size=5.4, out=1.1):
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            part.box(
                (centre_x + sx * (half_x - size * 0.5 + out * 0.5),
                 sy * (half_y - size * 0.5 + out * 0.5), z_centre),
                (size + out, size + out, height - 0.14),
                erode=0.12)


def belt_course(part, centre_x, z_centre, height, overhang, block_length=4.2, erode=0.07):
    half_x, half_y = shaft_half_extents(z_centre)
    half_x += overhang
    half_y += overhang
    masonry_course(part, centre_x, half_x, half_y, z_centre, height, RING_WIDTH + overhang,
                   block_length, 0, erode=erode, jitter=0.05)


def dentil_row(part, centre_x, z_centre, height, spacing=2.35, depth=1.5, size=1.15):
    half_x, half_y = shaft_half_extents(z_centre)
    half_x += 0.35
    half_y += 0.35
    for sign in (-1.0, 1.0):
        count = int((2.0 * half_y) / spacing)
        for index in range(count):
            y = -half_y + spacing * (index + 0.5)
            part.box((centre_x + sign * (half_x + depth * 0.25), y, z_centre),
                     (depth, size, height), erode=0.03)
        count = int((2.0 * half_x) / spacing)
        for index in range(count):
            x = -half_x + spacing * (index + 0.5)
            part.box((centre_x + x, sign * (half_y + depth * 0.25), z_centre),
                     (size, depth, height), erode=0.03)


def ribbon_blocks(part, points, width, depth, y_centre=0.0, skips=(), erode=0.08,
                  gap=0.16, width_jitter=0.0):
    """Extrudes a XZ polyline into individual radial-faced blocks (arches, ribs)."""
    for index in range(len(points) - 1):
        if index in skips:
            continue
        a = Vector(points[index])
        b = Vector(points[index + 1])
        tangent = (b - a)
        if tangent.length < 1e-5:
            continue
        tangent.normalize()
        shrink = tangent * gap * 0.5
        a = a + shrink
        b = b - shrink
        normal = Vector((-tangent.y, tangent.x))
        half = width * 0.5 + rng.uniform(0.0, width_jitter)
        corners = []
        for y in (y_centre - depth * 0.5, y_centre + depth * 0.5):
            for point, direction in ((a, -1.0), (a, 1.0), (b, 1.0), (b, -1.0)):
                planar = point + normal * (half * direction)
                world = Vector((planar.x, y, planar.y))
                corners.append(world + erosion(world, 0.08, erode))
        part.hexahedron(corners)


def arc_points(centre, radius, start_angle, end_angle, count):
    cx, cz = centre
    points = []
    for index in range(count + 1):
        angle = lerp(start_angle, end_angle, index / count)
        points.append(Vector((cx + radius * math.cos(angle), cz + radius * math.sin(angle))))
    return points


def rivet_grid(part, corner_a, corner_b, plane, spacing, radius, height, axis):
    """Rivet rows around the border of a plate."""
    ax, ay = corner_a
    bx, by = corner_b
    steps_x = max(1, int(round(abs(bx - ax) / spacing)))
    steps_y = max(1, int(round(abs(by - ay) / spacing)))
    positions = []
    for index in range(steps_x + 1):
        u = lerp(ax, bx, index / steps_x)
        positions.append((u, ay))
        positions.append((u, by))
    for index in range(1, steps_y):
        v = lerp(ay, by, index / steps_y)
        positions.append((ax, v))
        positions.append((bx, v))
    for u, v in positions:
        if axis == "Y":
            centre = (u, plane, v)
        elif axis == "X":
            centre = (plane, u, v)
        else:
            centre = (u, v, plane)
        part.stud(centre, radius, height, axis=axis)


# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------

clear_scene()

root = make_collection("DV_DESERT_MEGAGATE")
collection_base = make_collection("01_Foundations", root)
collection_pylons = make_collection("02_Pylons", root)
collection_arch = make_collection("03_Arch", root)
collection_machine = make_collection("04_Gate_Machinery", root)
collection_dressing = make_collection("05_Dressing", root)
collection_ruin = make_collection("06_Sand_And_Ruin", root)
collection_preview = make_collection("99_Presentation")

sunstone = surface_material(
    "DV Megagate Sunstone", 9.0,
    "Concrete025_2K-JPG/Concrete025_2K-JPG_Color.jpg", (0.86, 0.63, 0.40),
    "Concrete025_2K-JPG/Concrete025_2K-JPG_Roughness.jpg", 0.96,
    "Concrete025_2K-JPG/Concrete025_2K-JPG_NormalGL.jpg", normal_strength=1.0,
    colour_size=1024, map_size=1024)
shadowstone = surface_material(
    "DV Megagate Shadowstone", 7.0,
    "Concrete025_2K-JPG/Concrete025_2K-JPG_Color.jpg", (0.30, 0.20, 0.145),
    "Concrete025_2K-JPG/Concrete025_2K-JPG_Roughness.jpg", 1.0,
    "Concrete025_2K-JPG/Concrete025_2K-JPG_NormalGL.jpg", normal_strength=1.2)
bronze = surface_material(
    "DV Megagate Oxidised Bronze", 4.5,
    "Metal049B_4K-JPG/Metal049B_4K-JPG_Color.jpg", (0.52, 0.34, 0.16),
    "Metal049B_4K-JPG/Metal049B_4K-JPG_Roughness.jpg", 0.72,
    "Metal049B_4K-JPG/Metal049B_4K-JPG_NormalGL.jpg", metallic=0.88,
    colour_size=768, map_size=768)
# Shared with the hub: hub_carbonfiber.mat (untinted, metallic 0, smoothness 0.5).
darkiron = surface_material(
    "DV Megagate Carbon Fiber", 2.2,
    "Hub_CarbonFiber.png", (1.0, 1.0, 1.0), None, 0.5,
    None, metallic=0.0, colour_size=1024)
sand = surface_material(
    "DV Megagate Drift Sand", 34.0,
    "dunes.png", (0.86, 0.60, 0.33), None, 1.0,
    "dunes_normal.png", normal_strength=0.7, colour_size=1024, map_size=1024)
# Shared with the hub: fabricblue/SunspireExchange_Canvas.mat, used untinted.
banner_cloth = surface_material(
    "DV Megagate Banner Cloth", 6.0,
    "fabricblue/SunspireExchange_Canvas_Base_color.png", (1.0, 1.0, 1.0),
    "fabricblue/SunspireExchange_Canvas_Specular_roughness.png", 1.0,
    "fabricblue/SunspireExchange_Canvas_Normal_OpenGL.png", normal_strength=0.9,
    colour_size=1024, map_size=1024)
cyan_signal = signal_material("DV Megagate Cyan Signal", 2.0,
                              (0.01, 0.13, 0.16), (0.05, 0.78, 1.0), 9.0)
amber_signal = signal_material("DV Megagate Amber Core", 2.0,
                               (0.22, 0.07, 0.01), (1.0, 0.42, 0.08), 6.0)

# --- Parts -----------------------------------------------------------------

plinth = Part("Megagate Stepped Plinths", sunstone, collection_base, bevel=0.10)
plinth_dark = Part("Megagate Plinth Shadow Courses", shadowstone, collection_base, bevel=0.10)
paving = Part("Megagate Threshold Paving", shadowstone, collection_base, bevel=0.09)

for side in (-1.0, 1.0):
    pylon_x = side * PYLON_X
    steps = ((-9.0, 2.0, 30.0, 26.0), (2.0, 5.6, 26.5, 23.0),
             (5.6, 9.0, 23.5, 20.6), (9.0, PLINTH_TOP, 21.0, 18.6))
    for index, (z0, z1, half_x, half_y) in enumerate(steps):
        target = plinth if index % 2 == 0 else plinth_dark
        masonry_course(target, pylon_x, half_x, half_y, (z0 + z1) * 0.5, z1 - z0,
                       4.4, 5.6, index, erode=0.14, jitter=0.22)
    plinth_dark.box((pylon_x, 0.0, 1.0), (2 * 21.0, 2 * 18.6, 20.0), erode=0.0)

# Threshold paving between the pylons - individual slabs, sunk at the edges.
for row in range(-6, 7):
    for column in range(-7, 8):
        x = column * 5.4
        y = row * 5.4
        if abs(x) > 34.0 and abs(y) < 20.0:
            continue
        drop = rng.uniform(-0.35, 0.2) - max(0.0, (abs(y) - 22.0)) * 0.08
        paving.box((x, y, -3.4 + drop), (5.4 - rng.uniform(0.25, 0.55),
                                         5.4 - rng.uniform(0.25, 0.55), 7.0),
                   rotation=(0.0, 0.0, rng.uniform(-0.02, 0.02)), erode=0.10)

# Stepped approach apron so the threshold reads as a built floor, not a plane.
for step_index in range(5):
    y_row = -46.0 + step_index * 3.4
    run = 88.0
    count = 17
    for index in range(count):
        x = -run * 0.5 + run * (index + 0.5) / count
        paving.box((x, y_row, -5.0 + step_index * 0.85),
                   (run / count - 0.3, 3.4 - 0.25, 6.0), erode=0.12)

plinth.build()
plinth_dark.build()
paving.build()

# --- Pylon shafts ----------------------------------------------------------

course_count = int((SHAFT_TOP - PLINTH_TOP) / COURSE_HEIGHT)
for side in (-1.0, 1.0):
    label = "West" if side < 0 else "East"
    pylon_x = side * PYLON_X

    # Split each shaft into three objects so no single mesh gets unwieldy.
    sections = ((0, course_count // 3), (course_count // 3, 2 * course_count // 3),
                (2 * course_count // 3, course_count))
    for section_index, (first, last) in enumerate(sections):
        shaft = Part("{} Pylon Shaft {}".format(label, section_index + 1),
                     sunstone, collection_pylons, bevel=0.075)
        quoins = Part("{} Pylon Quoins {}".format(label, section_index + 1),
                      shadowstone, collection_pylons, bevel=0.09)
        for course in range(first, last):
            z_centre = PLINTH_TOP + COURSE_HEIGHT * (course + 0.5)
            half_x, half_y = shaft_half_extents(z_centre)
            weathering = 0.09 + 0.06 * (course / course_count)
            # The highest courses have lost stones to weather and collapse.
            lost = max(0.0, (course - (course_count - 4)) / 4.0) * 0.30
            masonry_course(shaft, pylon_x, half_x, half_y, z_centre, COURSE_HEIGHT,
                           RING_WIDTH, BLOCK_LENGTH, course, erode=weathering, missing=lost)
            if course % 2 == 0:
                quoin_corners(quoins, pylon_x, half_x, half_y, z_centre, COURSE_HEIGHT)
        shaft.build()
        quoins.build()

    # Belt courses, dentils and the corbelled crown.
    trim = Part("{} Pylon Cornices".format(label), sunstone, collection_pylons, bevel=0.08)
    trim_dark = Part("{} Pylon Cornice Shadow".format(label), shadowstone,
                     collection_pylons, bevel=0.07)
    for z_band in (32.0, 55.0, 78.0):
        belt_course(trim, pylon_x, z_band, 2.6, 1.5)
        dentil_row(trim_dark, pylon_x, z_band - 2.6, 1.5)

    for index, (z0, overhang) in enumerate(((SHAFT_TOP + 1.4, 1.6),
                                            (SHAFT_TOP + 4.2, 2.9),
                                            (SHAFT_TOP + 7.0, 4.1))):
        belt_course(trim if index != 1 else trim_dark, pylon_x, z0, 2.8, overhang,
                    block_length=3.8)
    dentil_row(trim_dark, pylon_x, SHAFT_TOP - 0.6, 1.8, spacing=2.1, depth=1.9)

    # Battlement merlons with a gap-toothed, half-collapsed parapet.
    crown_half_x, crown_half_y = shaft_half_extents(SHAFT_TOP)
    crown_half_x += 3.2
    crown_half_y += 3.2
    merlon_index = 0
    for sign in (-1.0, 1.0):
        count = int(2.0 * crown_half_y / 4.4)
        for index in range(count):
            merlon_index += 1
            if merlon_index % 5 == 3:
                continue
            y = -crown_half_y + (2.0 * crown_half_y / count) * (index + 0.5)
            height = MERLON_TOP - CORNICE_TOP - rng.uniform(0.0, 2.4)
            trim.box((pylon_x + sign * (crown_half_x - 1.6), y, CORNICE_TOP + height * 0.5),
                     (3.0, 3.0, height), erode=0.18)
        count = int(2.0 * crown_half_x / 4.4)
        for index in range(count):
            merlon_index += 1
            if merlon_index % 4 == 1:
                continue
            x = -crown_half_x + (2.0 * crown_half_x / count) * (index + 0.5)
            height = MERLON_TOP - CORNICE_TOP - rng.uniform(0.0, 2.4)
            trim.box((pylon_x + x, sign * (crown_half_y - 1.6), CORNICE_TOP + height * 0.5),
                     (3.0, 3.0, height), erode=0.18)
    trim.build()
    trim_dark.build()

    # Carved relief panels on the approach and rear faces.
    relief = Part("{} Pylon Relief Panels".format(label), sunstone,
                  collection_dressing, bevel=0.05)
    glyphs = Part("{} Pylon Relief Glyphs".format(label), shadowstone,
                  collection_dressing, bevel=0.05)
    lit_glyphs = Part("{} Pylon Lit Glyphs".format(label), cyan_signal,
                      collection_dressing, bevel=0.04)
    for face_sign in (-1.0, 1.0):
        for panel_index, (z0, z1) in enumerate(((22.0, 52.0), (62.0, 98.0))):
            half_x, half_y = shaft_half_extents((z0 + z1) * 0.5)
            plane = face_sign * (half_y - RING_WIDTH * 0.30)
            width = half_x * 1.02
            # Sunken cartouche with a raised stone frame around it.
            relief.box((pylon_x, plane + face_sign * 0.4, (z0 + z1) * 0.5),
                       (width, 1.8, z1 - z0), erode=0.05)
            for edge_z in (z0 - 1.2, z1 + 1.2):
                relief.box((pylon_x, plane + face_sign * 1.5, edge_z),
                           (width + 2.6, 1.5, 2.4), erode=0.05)
            for edge_x in (-width * 0.5 - 1.3, width * 0.5 + 1.3):
                relief.box((pylon_x + edge_x, plane + face_sign * 1.5, (z0 + z1) * 0.5),
                           (2.4, 1.5, z1 - z0 + 4.8), erode=0.05)
            # Carved glyph register: two ordered columns of small tiles.
            rows = int((z1 - z0) / 3.6)
            for row in range(rows):
                for column in (-1, 1):
                    if rng.random() < 0.12:
                        continue
                    gx = pylon_x + column * width * 0.24
                    gz = z0 + (z1 - z0) * (row + 0.7) / rows
                    glyphs.box((gx, plane + face_sign * 1.1, gz),
                               (1.5 + rng.uniform(-0.25, 0.35), 0.7,
                                1.5 + rng.uniform(-0.25, 0.35)), erode=0.04)
                    glyphs.box((gx + column * 0.1, plane + face_sign * 1.0, gz - 1.6),
                               (0.7, 0.5, 0.7), erode=0.03)
            # A single inlaid signal spine keeps the emissive read disciplined.
            lit_glyphs.box((pylon_x, plane + face_sign * 0.95, (z0 + z1) * 0.5),
                           (0.65, 0.6, (z1 - z0) * 0.86))
            for marker in range(3):
                mz = lerp(z0 + 3.0, z1 - 3.0, marker / 2.0)
                lit_glyphs.box((pylon_x, plane + face_sign * 1.0, mz), (3.4, 0.6, 0.6))
    relief.build()
    glyphs.build()
    lit_glyphs.build()

    # Bronze armour up the inner face, with real rivets.
    armour = Part("{} Pylon Bronze Armour".format(label), bronze,
                  collection_dressing, bevel=0.06)
    inner = -side  # inner faces point toward the opening
    for plate_index in range(6):
        z_centre = 20.0 + plate_index * 14.0
        half_x, _ = shaft_half_extents(z_centre)
        plane = pylon_x + inner * (half_x + 0.55)
        armour.box((plane, 0.0, z_centre), (1.4, 17.0 - plate_index * 0.7, 11.4),
                   erode=0.03)
        rivet_grid(armour, (-7.4 + plate_index * 0.3, z_centre - 4.9),
                   (7.4 - plate_index * 0.3, z_centre + 4.9),
                   plane + inner * 0.85, 2.5, 0.34, 0.6, axis="X")
        armour.box((plane + inner * 0.5, 0.0, z_centre + 7.4),
                   (1.1, 19.0 - plate_index * 0.7, 1.5), erode=0.02)
    armour.build()

    # Recessed signal channels: a dark bronze groove with a thin lit core.
    grooves = Part("{} Pylon Signal Grooves".format(label), darkiron,
                   collection_dressing, bevel=0.05)
    signals = Part("{} Pylon Signal Channels".format(label), cyan_signal,
                   collection_dressing, bevel=0.04)
    for offset in (-6.4, 6.4):
        for segment in range(9):
            z0 = 18.0 + segment * 9.0
            half_x, _ = shaft_half_extents(z0)
            plane = pylon_x + inner * (half_x + 1.2)
            grooves.box((plane, offset, z0 + 3.4), (1.1, 2.1, 7.2), erode=0.02)
            signals.box((plane + inner * 0.45, offset, z0 + 3.4), (0.4, 0.55, 5.8))
    grooves.build()
    signals.build()

# --- Flying buttresses -----------------------------------------------------

for side in (-1.0, 1.0):
    label = "West" if side < 0 else "East"
    pier_x = side * BUTTRESS_X
    pier = Part("{} Buttress Pier".format(label), sunstone, collection_base, bevel=0.08)
    pier_dark = Part("{} Buttress Pier Trim".format(label), shadowstone,
                     collection_base, bevel=0.08)
    pier_courses = 14
    for course in range(pier_courses):
        z_centre = -6.0 + 4.4 * (course + 0.5)
        t = course / (pier_courses - 1.0)
        half_x = lerp(10.5, 7.0, t)
        half_y = lerp(13.5, 9.0, t)
        masonry_course(pier, pier_x, half_x, half_y, z_centre, 4.4, 3.4, 5.2, course,
                       erode=0.13)
        if course in (5, 11):
            masonry_course(pier_dark, pier_x, half_x + 1.2, half_y + 1.2, z_centre + 2.2,
                           1.8, 4.4, 4.0, 0, erode=0.05, jitter=0.05)
    pier.build()
    pier_dark.build()

    rib = Part("{} Flying Buttress Rib".format(label), sunstone, collection_arch, bevel=0.08)
    rib_iron = Part("{} Buttress Tie".format(label), bronze, collection_arch, bevel=0.05)
    for rib_index, (z_pier, z_wall, bulge, depth) in enumerate(
            ((52.0, 78.0, 9.5, 7.5), (28.0, 48.0, 6.0, 6.0))):
        points = []
        for step in range(17):
            t = step / 16.0
            x = lerp(pier_x, side * 66.0, t)
            z = lerp(z_pier, z_wall, t) + math.sin(math.pi * t) * bulge
            points.append(Vector((x, z)))
        ribbon_blocks(rib, points, 5.2, depth, y_centre=-4.0 + rib_index * 8.0,
                      erode=0.09, gap=0.2)
        rib_iron.tube((points[3].x, -4.0 + rib_index * 8.0, points[3].y - 2.4),
                      (points[13].x, -4.0 + rib_index * 8.0, points[13].y - 2.4), 0.6, 8)
    rib.build()
    rib_iron.build()

# --- The arch --------------------------------------------------------------

arch = Part("Megagate Arch Voussoirs", sunstone, collection_arch, bevel=0.09)
archivolt = Part("Megagate Archivolt", shadowstone, collection_arch, bevel=0.08)
soffit = Part("Megagate Bronze Soffit Band", bronze, collection_arch, bevel=0.05)
spandrel = Part("Megagate Spandrel Masonry", sunstone, collection_arch, bevel=0.08)
keystone = Part("Megagate Keystone", sunstone, collection_arch, bevel=0.12, segments=2)
keycore = Part("Megagate Keystone Core", amber_signal, collection_arch, bevel=0.06)

VOUSSOIR_COUNT = 34
collapsed = {23, 24, 25, 26}
for index in range(VOUSSOIR_COUNT):
    if index in collapsed:
        continue
    a = math.pi * (1.0 - index / VOUSSOIR_COUNT)
    b = math.pi * (1.0 - (index + 1) / VOUSSOIR_COUNT)
    gap = 0.0035
    keyed = index in (16, 17)
    thickness = 0.0 if not keyed else 1.6
    arch.wedge(ARCH_RADIUS - ARCH_WIDTH * 0.5, ARCH_RADIUS + ARCH_WIDTH * 0.5 + thickness,
               a - gap, b + gap, -ARCH_DEPTH * 0.5, ARCH_DEPTH * 0.5,
               centre=(0.0, ARCH_SPRING_Z), erode=0.12)

ARCHIVOLT_COUNT = 30
for index in range(ARCHIVOLT_COUNT):
    if index in (20, 21, 22, 23, 24, 28):
        continue
    a = math.pi * (1.0 - index / ARCHIVOLT_COUNT)
    b = math.pi * (1.0 - (index + 1) / ARCHIVOLT_COUNT)
    archivolt.wedge(ARCHIVOLT_RADIUS - ARCHIVOLT_WIDTH * 0.5,
                    ARCHIVOLT_RADIUS + ARCHIVOLT_WIDTH * 0.5,
                    a - 0.004, b + 0.004, -5.5, 5.5,
                    centre=(0.0, ARCH_SPRING_Z), erode=0.10)

SOFFIT_COUNT = 44
for index in range(SOFFIT_COUNT):
    if index in (30, 31, 32, 33):
        continue
    a = math.pi * (1.0 - index / SOFFIT_COUNT)
    b = math.pi * (1.0 - (index + 1) / SOFFIT_COUNT)
    soffit.wedge(SOFFIT_RADIUS - 0.9, SOFFIT_RADIUS + 0.9, a - 0.006, b + 0.006,
                 -ARCH_DEPTH * 0.55, ARCH_DEPTH * 0.55, centre=(0.0, ARCH_SPRING_Z),
                 erode=0.02)
    mid = (a + b) * 0.5
    for y in (-ARCH_DEPTH * 0.5, ARCH_DEPTH * 0.5):
        soffit.stud((math.cos(mid) * (SOFFIT_RADIUS - 1.0), y,
                     ARCH_SPRING_Z + math.sin(mid) * (SOFFIT_RADIUS - 1.0)),
                    0.42, 0.7, axis="Y")

# Spandrel masonry ties the arch back into the pylon crowns.
for side in (-1.0, 1.0):
    for course in range(6):
        z_centre = ARCH_SPRING_Z + 1.8 + course * 3.4
        radius = math.sqrt(max(0.0, (ARCHIVOLT_RADIUS + 3.0) ** 2 -
                               (z_centre - ARCH_SPRING_Z) ** 2))
        inner_x = radius
        outer_x = PYLON_X + shaft_half_extents(SHAFT_TOP)[0] + 1.0
        if inner_x >= outer_x - 3.0:
            continue
        run = outer_x - inner_x
        count = max(1, int(round(run / 5.0)))
        for index in range(count):
            x0 = inner_x + run * index / count
            x1 = inner_x + run * (index + 1) / count
            spandrel.box((side * (x0 + x1) * 0.5, 0.0, z_centre),
                         (x1 - x0 - 0.25, 15.0 - course * 0.5, 3.4 - 0.2), erode=0.11)

# Dropped keystone with an amber core.
keystone.wedge(ARCH_RADIUS - ARCH_WIDTH * 0.5 - 1.6, ARCH_RADIUS + ARCH_WIDTH * 0.5 + 3.2,
               math.pi * 0.5 + 0.075, math.pi * 0.5 - 0.075, -ARCH_DEPTH * 0.62,
               ARCH_DEPTH * 0.62, centre=(0.0, ARCH_SPRING_Z - 1.2), erode=0.10)
keycore.box((0.0, 0.0, ARCH_SPRING_Z + ARCH_RADIUS - 2.2), (2.6, ARCH_DEPTH * 1.3, 2.6),
            rotation=(0.0, math.radians(45.0), 0.0))

# Broken entablature bedded straight onto the crown of the archivolt.
entablature = Part("Megagate Broken Entablature", sunstone, collection_arch, bevel=0.09)
entablature_trim = Part("Megagate Entablature Dentils", shadowstone, collection_arch,
                        bevel=0.06)
crown_z = ARCH_SPRING_Z + ARCHIVOLT_RADIUS - 1.6
for index in range(19):
    x = -42.0 + index * 4.6
    seat = crown_z - max(0.0, (abs(x + 2.3) - 12.0)) ** 1.7 * 0.055
    if x > 4.0 and index % 2 == 1:
        continue  # the eastern half has shed most of its blocks
    if x > 16.0:
        continue
    entablature_trim.box((x + 2.3, 0.0, seat + 1.0), (4.2, 15.5, 2.0), erode=0.08)
    height = 5.8 - max(0.0, (x - 2.0)) * 0.14
    entablature.box((x + 2.3, 0.0, seat + 2.0 + height * 0.5),
                    (4.4, 13.4 - abs(x) * 0.045, height), erode=0.16)

arch.build()
archivolt.build()
soffit.build()
spandrel.build()
keystone.build()
keycore.build()
entablature.build()
entablature_trim.build()

# --- Gate machinery: suspended transit ring --------------------------------

ring = Part("Megagate Transit Ring", bronze, collection_machine, bevel=0.06)
ring_bronze = Part("Megagate Transit Ring Collars", darkiron, collection_machine, bevel=0.05)
ring_light = Part("Megagate Transit Ring Emitters", cyan_signal, collection_machine,
                  bevel=0.04)

RING_SEGMENTS = 30
ring_missing = {12, 13, 14, 24}
for index in range(RING_SEGMENTS):
    if index in ring_missing:
        continue
    a = 2.0 * math.pi * index / RING_SEGMENTS
    b = 2.0 * math.pi * (index + 1) / RING_SEGMENTS
    ring.wedge(RING_RADIUS - 2.4, RING_RADIUS + 2.4, a + 0.008, b - 0.008, -3.0, 3.0,
               centre=(0.0, RING_CENTRE_Z), erode=0.0)
    mid = (a + b) * 0.5
    tooth = Vector((math.cos(mid), math.sin(mid)))
    if index % 2 == 0:
        ring_bronze.box((tooth.x * (RING_RADIUS + 3.2), 0.0,
                         RING_CENTRE_Z + tooth.y * (RING_RADIUS + 3.2)),
                        (2.2, 4.6, 2.2), rotation=(0.0, -mid, 0.0))
    else:
        ring_light.box((tooth.x * (RING_RADIUS - 2.9), 0.0,
                        RING_CENTRE_Z + tooth.y * (RING_RADIUS - 2.9)),
                       (1.5, 4.0, 1.5), rotation=(0.0, -mid, 0.0))
    for y in (-3.0, 3.0):
        ring_bronze.stud((tooth.x * RING_RADIUS, y, RING_CENTRE_Z + tooth.y * RING_RADIUS),
                         0.5, 0.8, axis="Y")

for angle_degrees in (54.0, 90.0, 126.0):
    angle = math.radians(angle_degrees)
    inner = Vector((math.cos(angle) * (RING_RADIUS + 2.4), 0.0,
                    RING_CENTRE_Z + math.sin(angle) * (RING_RADIUS + 2.4)))
    outer = Vector((math.cos(angle) * (SOFFIT_RADIUS - 1.2), 0.0,
                    ARCH_SPRING_Z + math.sin(angle) * (SOFFIT_RADIUS - 1.2)))
    if outer.z < 6.0:
        outer.z = 4.0
    for y in (-3.4, 3.4):
        ring.tube((inner.x, y, inner.z), (outer.x, y, outer.z), 0.75, 8)
    ring_bronze.tube((inner.x, -3.9, inner.z), (inner.x, 3.9, inner.z), 1.1, 10)

ring.build()
ring_bronze.build()
ring_light.build()

# --- Crown masts, cables and banners ---------------------------------------

masts = Part("Megagate Crown Masts", bronze, collection_dressing, bevel=0.05)
beacons = Part("Megagate Crown Beacons", cyan_signal, collection_dressing, bevel=0.04)
cables = Part("Megagate Anchor Cables", darkiron, collection_dressing, bevel=0.0)

for side in (-1.0, 1.0):
    pylon_x = side * PYLON_X
    for index, (offset_x, offset_y, height) in enumerate(
            ((-6.0, -6.0, 15.0), (6.0, 6.0, 11.0), (-6.0, 6.0, 8.5))):
        base = Vector((pylon_x + offset_x, offset_y, MERLON_TOP - 1.0))
        masts.cylinder((base.x, base.y, base.z + height * 0.5), 0.85, height, 8, taper=0.45)
        masts.box((base.x, base.y, base.z + height + 0.6), (2.6, 2.6, 1.2))
        beacons.box((base.x, base.y, base.z + height + 1.7), (1.1, 1.1, 1.4))
        for guy in range(3):
            angle = math.radians(30.0 + guy * 120.0)
            anchor = Vector((base.x + math.cos(angle) * 7.0, base.y + math.sin(angle) * 7.0,
                             MERLON_TOP - 4.0))
            cables.tube((base.x, base.y, base.z + height * 0.9), anchor, 0.26, 5)

    # Catenary anchor cables sweeping from the crown down to ground blocks.
    for cable_index, (anchor_y, anchor_x) in enumerate(((-52.0, 92.0), (52.0, 92.0))):
        top = Vector((pylon_x + side * 10.0, anchor_y * 0.14, MERLON_TOP - 3.0))
        bottom = Vector((side * anchor_x, anchor_y, 4.0))
        previous = top
        for step in range(1, 13):
            t = step / 12.0
            point = top.lerp(bottom, t)
            point.z -= math.sin(math.pi * t) * 13.0
            cables.tube(previous, point, 0.62, 6)
            previous = point
        masts.box((bottom.x, bottom.y, 3.0), (5.5, 5.5, 8.0), erode=0.1)

masts.build()
beacons.build()
cables.build()

# Narrow processional pennants flanking each carved cartouche, so the relief
# stays readable behind them.
banners = Part("Megagate Hanging Banners", banner_cloth, collection_dressing, bevel=0.02)
banner_rods = Part("Megagate Banner Rods", bronze, collection_dressing, bevel=0.05)
for side in (-1.0, 1.0):
    pylon_x = side * PYLON_X
    _, half_y = shaft_half_extents(80.0)
    plane = -(half_y + 2.2)
    columns, rows = 4, 12
    width = 5.4
    top_z = 100.5
    row_height = 3.2
    for pennant, offset in enumerate((-10.4, 10.4)):
        for column in range(columns):
            u = (column + 0.5) / columns - 0.5
            tatter = rows - 1 - int((abs(math.sin(column * 2.3 + pennant + side)) ** 1.4) * 4.0)
            for row in range(rows):
                if row > tatter:
                    continue
                z = top_z - (row + 0.5) * row_height
                # Cloth billows off the wall and gathers toward the free edges.
                sway = math.sin(u * 7.0 + row * 0.45 + pennant) * 1.25
                billow = (0.3 + row * 0.1) * (1.0 - abs(u) * 1.5)
                banners.box((pylon_x + offset + u * width + sway * 0.3,
                             plane - 0.25 - max(0.0, billow) - abs(sway) * 0.28, z),
                            (width / columns - 0.05, 0.22, row_height - 0.05),
                            rotation=(math.radians(sway * 3.0), math.radians(sway * 2.5),
                                      math.radians(sway * 7.0)))
        banner_rods.cylinder((pylon_x + offset, plane, top_z + 1.2), 0.6, width + 2.6, 8,
                             Matrix.Rotation(math.radians(90.0), 4, "Y"))
        for end in (-1.0, 1.0):
            banner_rods.box((pylon_x + offset + end * (width * 0.5 + 1.6), plane,
                             top_z + 1.2), (1.4, 1.4, 1.4))
banners.build()
banner_rods.build()

# --- Guardian stelae flanking the approach ---------------------------------

STELA_COURSES = 11
STELA_COURSE_HEIGHT = 4.1

for side in (-1.0, 1.0):
    label = "West" if side < 0 else "East"
    stela = Part("{} Guardian Stela".format(label), sunstone,
                 collection_dressing, bevel=0.11, segments=2)
    stela_carving = Part("{} Guardian Stela Carving".format(label), shadowstone,
                         collection_dressing, bevel=0.06)
    stela_cap = Part("{} Guardian Stela Pyramidion".format(label), bronze,
                     collection_dressing, bevel=0.08)
    stela_beacon = Part("{} Guardian Stela Beacon".format(label), cyan_signal,
                        collection_dressing, bevel=0.05)

    base_x = side * 66.0
    base_y = -104.0
    lean = math.radians(3.4) if side > 0.0 else math.radians(-0.9)
    dais_top = 10.4

    def stela_place(local_x, local_y, z, size, extra_rotation=0.0):
        """Positions a block on the stela's slightly settled axis."""
        rise = z - dais_top
        return ((base_x + local_x + math.sin(lean) * rise, base_y + local_y, z),
                size, (0.0, lean + extra_rotation, 0.0))

    # Coursed dais.
    for course, half in enumerate((13.5, 12.2, 11.0)):
        z_centre = -3.4 + 4.6 * (course + 0.5)
        count = int(round(2.0 * half / 5.2))
        run = 2.0 * half
        for index in range(count):
            offset = -half + run * (index + 0.5) / count
            for edge in (-1.0, 1.0):
                stela.box((base_x + offset, base_y + edge * (half - 2.1), z_centre),
                          (run / count - 0.25, 4.2, 4.6), erode=0.2)
                stela.box((base_x + edge * (half - 2.1), base_y + offset, z_centre),
                          (4.2, run / count - 0.25, 4.6), erode=0.2)
        stela.box((base_x, base_y, z_centre - 0.4), (2.0 * half - 8.0, 2.0 * half - 8.0,
                                                     4.6), erode=0.06)

    # Tapered obelisk shaft, two blocks per course so the joints stagger.
    for course in range(STELA_COURSES):
        t = course / (STELA_COURSES - 1.0)
        z_centre = dais_top + STELA_COURSE_HEIGHT * (course + 0.5)
        half = lerp(5.2, 2.9, t)
        split_x = course % 2 == 0
        for quadrant in (-1.0, 1.0):
            local_x = quadrant * half * 0.5 if split_x else 0.0
            local_y = 0.0 if split_x else quadrant * half * 0.5
            size = ((half - 0.12, 2.0 * half - 0.2) if split_x
                    else (2.0 * half - 0.2, half - 0.12))
            centre, dims, rotation = stela_place(
                local_x, local_y, z_centre, (size[0], size[1],
                                             STELA_COURSE_HEIGHT - 0.16))
            stela.box(centre, dims, rotation=rotation, erode=0.16)

        # Carved glyph register down the faces that look toward the road.
        if course < STELA_COURSES - 1 and course % 1 == 0:
            for face in (-1.0, 1.0):
                centre, dims, rotation = stela_place(
                    0.0, face * (half + 0.25), z_centre,
                    (half * 1.05, 0.9, STELA_COURSE_HEIGHT - 1.3))
                stela_carving.box(centre, dims, rotation=rotation, erode=0.05)
                for glyph in (-1.0, 1.0):
                    centre, dims, rotation = stela_place(
                        glyph * half * 0.26, face * (half + 0.7), z_centre,
                        (1.25, 0.55, 1.25))
                    stela.box(centre, dims, rotation=rotation, erode=0.04)

    # Bronze pyramidion and its beacon.
    apex_z = dais_top + STELA_COURSE_HEIGHT * STELA_COURSES
    centre, dims, rotation = stela_place(0.0, 0.0, apex_z + 0.9, (6.6, 6.6, 1.8))
    stela_cap.box(centre, dims, rotation=rotation, erode=0.04)
    centre, dims, rotation = stela_place(0.0, 0.0, apex_z + 4.4, (5.8, 5.8, 5.2))
    stela_cap.box(centre, dims, rotation=rotation, taper=0.06, erode=0.03)
    centre, dims, rotation = stela_place(0.0, 0.0, apex_z + 7.6, (1.5, 1.5, 1.5))
    stela_beacon.box(centre, dims, rotation=rotation)

    # The eastern stela has shed its top course into the sand.
    if side > 0.0:
        stela.box((base_x + 19.0, base_y - 8.0, 2.6), (6.4, 6.4, 5.0),
                  rotation=(math.radians(21.0), math.radians(-14.0), math.radians(33.0)),
                  erode=0.3)

    stela.build()
    stela_carving.build()
    stela_cap.build()
    stela_beacon.build()

# --- Sand, collapse debris and fallen voussoirs ----------------------------

drift = Part("Megagate Sand Drifts", sand, collection_ruin, bevel=0.0, smooth=True)


def sand_mound(centre, radius_x, radius_y, height, resolution=30, seed=0.0):
    def height_fn(u, v):
        px = (u - 0.5) * 2.0
        py = (v - 0.5) * 2.0
        radial = math.sqrt(px * px + py * py)
        if radial > 1.06:
            return None
        falloff = math.cos(min(radial, 1.0) * math.pi * 0.5) ** 1.5
        world_x = centre[0] + px * radius_x
        world_y = centre[1] + py * radius_y
        ripple = noise.noise(Vector((world_x * 0.05, world_y * 0.05, seed))) * 2.4
        dune = math.sin(world_x * 0.045 + world_y * 0.02) * 1.4
        return (world_x, world_y, centre[2] + height * falloff + (ripple + dune) * falloff)
    drift.surface_grid((resolution, resolution), height_fn)


for side in (-1.0, 1.0):
    sand_mound((side * PYLON_X, 2.0, -1.0), 40.0, 34.0, 11.5, 32, seed=side * 3.0)
    sand_mound((side * BUTTRESS_X, -2.0, -1.0), 24.0, 22.0, 7.0, 22, seed=side * 7.0)
    sand_mound((side * 66.0, -104.0, -1.0), 26.0, 24.0, 6.0, 20, seed=side * 13.0)
sand_mound((4.0, 34.0, -1.5), 58.0, 26.0, 8.0, 34, seed=11.0)
sand_mound((-6.0, -40.0, -1.5), 52.0, 24.0, 6.5, 30, seed=17.0)
drift.build()

rubble = Part("Megagate Collapse Rubble", sunstone, collection_ruin, bevel=0.06)
rubble_dark = Part("Megagate Rubble Shadow Stone", shadowstone, collection_ruin, bevel=0.06)

# The voussoirs that fell out of the arch are lying where they landed.
for index in range(9):
    x = rng.uniform(24.0, 62.0)
    y = rng.uniform(-26.0, 22.0)
    angle = rng.uniform(-1.4, 1.4)
    target = rubble if index % 3 else rubble_dark
    target.wedge(ARCH_RADIUS - ARCH_WIDTH * 0.5, ARCH_RADIUS + ARCH_WIDTH * 0.5,
                 0.0, 0.09, -ARCH_DEPTH * 0.5, ARCH_DEPTH * 0.5,
                 centre=(x - ARCH_RADIUS, rng.uniform(1.5, 4.5)), erode=0.2)

for index in range(46):
    side = -1.0 if index % 2 == 0 else 1.0
    x = side * rng.uniform(20.0, 104.0)
    y = rng.uniform(-72.0, 62.0)
    z = rng.uniform(0.3, 3.4)
    scale = (rng.uniform(1.6, 5.4), rng.uniform(1.6, 5.0), rng.uniform(1.0, 3.6))
    target = rubble if index % 3 else rubble_dark
    if index % 4 == 0:
        target.box((x, y, z), (scale[0] * 1.6, scale[1] * 1.6, scale[2] * 1.4),
                   rotation=tuple(math.radians(rng.uniform(-28.0, 28.0)) for _ in range(3)),
                   erode=0.3)
    else:
        target.rock((x, y, z), scale, seed=index * 4.0, subdivisions=1)
rubble.build()
rubble_dark.build()

# ---------------------------------------------------------------------------
# Presentation: desert floor, sky, lights and cameras
# ---------------------------------------------------------------------------

floor_part = Part("Preview Desert Floor", sand, collection_preview, bevel=0.0,
                  smooth=True, export=False)


def floor_height(u, v):
    x = (u - 0.5) * 1400.0
    y = (v - 0.5) * 1400.0
    height = noise.noise(Vector((x * 0.006, y * 0.006, 4.0))) * 9.0
    height += math.sin(x * 0.012 + y * 0.004) * 3.5
    height -= 3.0
    return (x, y, min(height, 1.5))


floor_part.surface_grid((90, 90), floor_height)
floor_part.build()

world = bpy.context.scene.world
if world is None:
    world = bpy.data.worlds.new("Megagate Sky")
    bpy.context.scene.world = world
world.use_nodes = True
world_nodes = world.node_tree.nodes
world_links = world.node_tree.links
world_nodes.clear()
world_output = world_nodes.new("ShaderNodeOutputWorld")
background = world_nodes.new("ShaderNodeBackground")
background.inputs["Strength"].default_value = 1.0
world_links.new(background.outputs["Background"], world_output.inputs["Surface"])
try:
    sky = world_nodes.new("ShaderNodeTexSky")
    sky.sky_type = "NISHITA"
    sky.sun_elevation = math.radians(11.0)
    sky.sun_rotation = math.radians(-42.0)
    sky.altitude = 250.0
    sky.dust_density = 4.2
    sky.air_density = 1.6
    sky.sun_intensity = 0.55
    world_links.new(sky.outputs["Color"], background.inputs["Color"])
except Exception:
    background.inputs["Color"].default_value = (0.10, 0.13, 0.22, 1.0)

bpy.ops.object.light_add(type="SUN", location=(0.0, 0.0, 260.0))
sun_light = bpy.context.object
sun_light.name = "Preview Desert Sun"
sun_light.data.energy = 5.5
sun_light.data.angle = math.radians(1.6)
sun_light.data.color = (1.0, 0.78, 0.55)
sun_light.rotation_euler = Euler((math.radians(66.0), 0.0, math.radians(-52.0)), "XYZ")
for existing in list(sun_light.users_collection):
    existing.objects.unlink(sun_light)
collection_preview.objects.link(sun_light)

bpy.ops.object.light_add(type="AREA", location=(-260.0, 210.0, 150.0))
bounce = bpy.context.object
bounce.name = "Preview Sky Bounce"
bounce.data.energy = 260000.0
bounce.data.size = 180.0
bounce.data.color = (0.34, 0.52, 1.0)
bounce.rotation_euler = (Vector((0.0, 0.0, 70.0)) - bounce.location).to_track_quat("-Z", "Y").to_euler()
for existing in list(bounce.users_collection):
    existing.objects.unlink(bounce)
collection_preview.objects.link(bounce)


def add_camera(name, location, target, lens):
    camera_data = bpy.data.cameras.new(name)
    camera_data.lens = lens
    camera_data.clip_end = 4000.0
    camera = bpy.data.objects.new(name, camera_data)
    collection_preview.objects.link(camera)
    camera.location = Vector(location)
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()
    return camera


# Camera lines are chosen to keep the arch opening clear of the colossi.
hero_camera = add_camera("Megagate Hero Camera", (-215.0, -560.0, 100.0), (0.0, 0.0, 72.0), 70.0)
front_camera = add_camera("Megagate Elevation Camera", (0.0, -700.0, 112.0), (0.0, 0.0, 84.0), 85.0)
detail_camera = add_camera("Megagate Detail Camera", (-150.0, -190.0, 55.0), (-52.0, -30.0, 66.0), 58.0)

scene = bpy.context.scene
scene["DV_Asset"] = "Desert Megagate"
scene["DV_Design_Intent"] = ("Colossal half-buried masonry transit arch: coursed block "
                             "construction, true voussoir arch, suspended machine ring")
scene["DV_Approximate_Height_Meters"] = round(ARCH_SPRING_Z + ARCHIVOLT_RADIUS + 8.4, 1)
scene["DV_Approximate_Opening_Meters"] = round(2.0 * (SOFFIT_RADIUS - 0.9), 1)
scene["DV_UV_Policy"] = "World-space box projection, fixed metres-per-tile per material"

scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1600
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
try:
    scene.eevee.taa_render_samples = 96
    scene.eevee.use_raytracing = True
except Exception:
    pass
try:
    scene.view_settings.look = "AgX - Medium High Contrast"
except Exception:
    pass
scene.view_settings.exposure = 0.2

# ---------------------------------------------------------------------------
# Export
# ---------------------------------------------------------------------------


def patch_glb_material_factors(filepath, factors_by_material):
    """glTF multiplies its textures by the material factors, so the Blender tint
    and roughness multipliers are re-applied here for a 1:1 match in Unity."""
    with open(filepath, "rb") as source:
        data = source.read()
    magic, version, _ = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF" or version != 2:
        raise RuntimeError("Unexpected GLB header in {}".format(filepath))

    chunks = []
    offset = 12
    patched = 0
    while offset < len(data):
        chunk_length, chunk_type = struct.unpack_from("<I4s", data, offset)
        chunk_data = data[offset + 8:offset + 8 + chunk_length]
        if chunk_type == b"JSON":
            document = json.loads(chunk_data.decode("utf-8").rstrip(" \t\r\n\0"))
            for material in document.get("materials", []):
                override = factors_by_material.get(material.get("name"))
                if override is None:
                    continue
                pbr = material.setdefault("pbrMetallicRoughness", {})
                pbr.update(override)
                patched += 1
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
    return patched


bpy.ops.object.select_all(action="DESELECT")
for obj in ASSET_OBJECTS:
    obj.select_set(True)
bpy.context.view_layer.objects.active = ASSET_OBJECTS[0]

bpy.ops.export_scene.gltf(
    filepath=MASTER_GLB,
    export_format="GLB",
    use_selection=True,
    export_apply=True,
    export_yup=True,
    export_materials="EXPORT",
    export_image_format="JPEG",
    export_jpeg_quality=88,
    export_normals=True,
    # Tangents would add ~7 MB to a Resources-folder asset; Unity recalculates
    # them on import instead.
    export_tangents=False,
    export_texcoords=True,
    export_cameras=False,
    export_lights=False,
)

factors = {}
for material in (sunstone, shadowstone, bronze, darkiron, sand, banner_cloth):
    factors[material.name] = {
        "baseColorFactor": list(material["dv_base_color_factor"]),
        "roughnessFactor": float(material["dv_roughness_factor"]),
    }
patch_glb_material_factors(MASTER_GLB, factors)
shutil.copyfile(MASTER_GLB, RUNTIME_GLB)

bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

triangles = 0
vertices = 0
for obj in ASSET_OBJECTS:
    mesh = obj.data
    mesh.calc_loop_triangles()
    triangles += len(mesh.loop_triangles)
    vertices += len(mesh.vertices)

print("DESERT_MEGAGATE_OBJECTS={}".format(len(ASSET_OBJECTS)))
print("DESERT_MEGAGATE_TRIANGLES={}".format(triangles))
print("DESERT_MEGAGATE_VERTICES={}".format(vertices))
print("DESERT_MEGAGATE_GLB={} ({:.1f} MB)".format(
    MASTER_GLB, os.path.getsize(MASTER_GLB) / 1048576.0))
print("DESERT_MEGAGATE_BLEND={}".format(BLEND_PATH))
