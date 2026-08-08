"""
Replace flat rounded-rectangle blocks in the Aerie hub with detailed equipment
modules.

Usage (Blender Scripting tab):
    1. Select every rounded-rect block you want upgraded.
    2. Paste this file into a new Text block and press Run Script.

Each selected object keeps its transform, its parent, and its material slots.
Only the mesh data is rebuilt. Objects that share a mesh datablock (mirrored
+1 / -1 pairs) are rebuilt once and stay linked.

Variation is deterministic: the module style is hashed from the mesh name, so
re-running produces the same result and neighbouring blocks differ from one
another.
"""

import bpy
import bmesh
import hashlib
from mathutils import Vector, Matrix

HALF_PI = 1.5707963267948966

# ---------------------------------------------------------------------------
# tuning
# ---------------------------------------------------------------------------

BEVEL_SEGMENTS = 2      # chamfer roundness on the chassis
CAP_SEGMENTS = 1        # chamfer roundness on the small greeble parts
RADIAL_SEGMENTS = 10    # sides on studs / conduits


# ---------------------------------------------------------------------------
# bmesh helpers
# ---------------------------------------------------------------------------

def _emit(dst, src):
    """Append the contents of bmesh `src` into bmesh `dst`, freeing `src`."""
    me = bpy.data.meshes.new("_dv_tmp")
    src.to_mesh(me)
    src.free()
    dst.from_mesh(me)
    bpy.data.meshes.remove(me)


def box(dst, center, size, bevel=0.0, segments=BEVEL_SEGMENTS):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel > 0.0:
        limit = min(abs(v) for v in size) * 0.45
        off = min(bevel, limit)
        if off > 1e-6:
            bmesh.ops.bevel(
                bm,
                geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                offset=off,
                segments=segments,
                profile=0.5,
                affect='EDGES',
                clamp_overlap=True,
            )
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    _emit(dst, bm)


def cyl(dst, center, radius, depth, axis='Z', segments=RADIAL_SEGMENTS,
        taper=1.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(
        bm,
        cap_ends=True,
        cap_tris=False,
        segments=segments,
        radius1=radius,
        radius2=radius * taper,
        depth=depth,
    )
    if axis == 'X':
        bmesh.ops.rotate(bm, verts=bm.verts,
                         matrix=Matrix.Rotation(HALF_PI, 3, 'Y'))
    elif axis == 'Y':
        bmesh.ops.rotate(bm, verts=bm.verts,
                         matrix=Matrix.Rotation(HALF_PI, 3, 'X'))
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    _emit(dst, bm)


# ---------------------------------------------------------------------------
# module construction, in a nominal [-0.5, 0.5] cube
# ---------------------------------------------------------------------------

def build_module(style, seed):
    """Return a bmesh holding one detailed module, roughly unit sized."""
    bm = bmesh.new()
    rnd = (seed >> 8) % 1000 / 1000.0

    # --- chassis: chamfered core, inset waist so the silhouette isn't a slab
    box(bm, (0, 0, 0), (0.86, 0.78, 0.80), bevel=0.075)
    box(bm, (0, 0, 0), (0.94, 0.66, 0.62), bevel=0.045)

    # --- end caps: heavier collars that read as machined flanges
    for sx in (-1, 1):
        box(bm, (sx * 0.46, 0, 0), (0.10, 0.90, 0.92), bevel=0.035,
            segments=CAP_SEGMENTS)
        box(bm, (sx * 0.53, 0, 0), (0.06, 0.62, 0.64), bevel=0.02,
            segments=CAP_SEGMENTS)
        # corner bolt studs on each flange
        for sy in (-1, 1):
            for sz in (-1, 1):
                cyl(bm, (sx * 0.51, sy * 0.35, sz * 0.36), 0.045, 0.05,
                    axis='X', segments=6)

    # --- ribs across the long axis
    ribs = 3 + (seed % 3)
    for i in range(ribs):
        t = (i + 0.5) / ribs - 0.5
        box(bm, (t * 0.72, 0, 0), (0.055, 0.92, 0.88), bevel=0.02,
            segments=CAP_SEGMENTS)

    # --- recessed vent slats on the +Y face
    slats = 4 + (seed >> 3) % 3
    for i in range(slats):
        t = (i + 0.5) / slats - 0.5
        box(bm, (t * 0.62, 0.40, 0.0), (0.34 / slats, 0.05, 0.50),
            bevel=0.012, segments=1)

    # --- style-specific topside fitting
    if style == 0:
        # pressure drum + tie-down lugs
        cyl(bm, (0, 0, 0.46), 0.22, 0.16, axis='Z', segments=RADIAL_SEGMENTS)
        cyl(bm, (0, 0, 0.55), 0.13, 0.10, axis='Z', segments=RADIAL_SEGMENTS,
            taper=0.7)
        for sx in (-1, 1):
            box(bm, (sx * 0.30, 0, 0.46), (0.10, 0.30, 0.06), bevel=0.02,
                segments=1)
    elif style == 1:
        # inspection hatch with a raised lip and a handle bar
        box(bm, (0, 0, 0.44), (0.44, 0.44, 0.09), bevel=0.03, segments=1)
        box(bm, (0, 0, 0.50), (0.30, 0.30, 0.05), bevel=0.02, segments=1)
        box(bm, (0, 0, 0.56), (0.34, 0.05, 0.04), bevel=0.015, segments=1)
        for sy in (-1, 1):
            cyl(bm, (0.0, sy * 0.12, 0.56), 0.03, 0.10, axis='Z', segments=6)
    elif style == 2:
        # conduit spine running the length, clamped at intervals
        cyl(bm, (0, 0, 0.44), 0.09, 1.02, axis='X', segments=RADIAL_SEGMENTS)
        for sx in (-1, 0, 1):
            box(bm, (sx * 0.30, 0, 0.44), (0.05, 0.24, 0.24), bevel=0.02,
                segments=1)
        box(bm, (0, 0.26, 0.42), (0.26, 0.06, 0.14), bevel=0.02, segments=1)
    else:
        # gauge cluster: stepped plinth with three dials
        box(bm, (0, 0, 0.44), (0.52, 0.34, 0.08), bevel=0.025, segments=1)
        for i in (-1, 0, 1):
            cyl(bm, (i * 0.18, 0.0, 0.52), 0.09, 0.09, axis='Z',
                segments=RADIAL_SEGMENTS)
            cyl(bm, (i * 0.18, 0.0, 0.575), 0.055, 0.03, axis='Z',
                segments=RADIAL_SEGMENTS)

    # --- underside skids so it doesn't sit as a flat plate
    for sy in (-1, 1):
        box(bm, (0, sy * 0.30, -0.45), (0.80, 0.10, 0.10), bevel=0.025,
            segments=1)
    # a diagonal strap on half the variants
    if rnd > 0.5:
        box(bm, (0, -0.42, 0), (0.96, 0.05, 0.10), bevel=0.02, segments=1)

    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    return bm


def fit_to_bounds(bm, lo, hi):
    """Remap the module's own bounds onto the target local-space box."""
    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for v in bm.verts:
        for i in range(3):
            mn[i] = min(mn[i], v.co[i])
            mx[i] = max(mx[i], v.co[i])
    src_size = mx - mn
    dst_size = hi - lo
    for v in bm.verts:
        for i in range(3):
            if src_size[i] > 1e-9:
                t = (v.co[i] - mn[i]) / src_size[i]
            else:
                t = 0.5
            v.co[i] = lo[i] + t * dst_size[i]


# ---------------------------------------------------------------------------
# driver
# ---------------------------------------------------------------------------

def main():
    targets = [o for o in bpy.context.selected_objects if o.type == 'MESH']
    if not targets:
        print("[dune] nothing selected — select the rounded-rect blocks first")
        return

    # group by mesh datablock so mirrored pairs stay linked and consistent
    by_mesh = {}
    for ob in targets:
        by_mesh.setdefault(ob.data.name, []).append(ob)

    rebuilt = 0
    for mesh_name, objs in by_mesh.items():
        ref = objs[0]
        old = ref.data

        bb = [Vector(c) for c in ref.bound_box]
        lo = Vector((min(c.x for c in bb), min(c.y for c in bb),
                     min(c.z for c in bb)))
        hi = Vector((max(c.x for c in bb), max(c.y for c in bb),
                     max(c.z for c in bb)))
        if (hi - lo).length < 1e-6:
            print("[dune] skipped degenerate mesh:", mesh_name)
            continue

        # the long axis of the source block should stay the module's long axis
        digest = hashlib.sha1(mesh_name.encode("utf-8")).digest()
        seed = int.from_bytes(digest[:4], "little")
        style = seed % 4

        bm = build_module(style, seed)

        extents = hi - lo
        order = sorted(range(3), key=lambda i: -extents[i])
        if order[0] != 0:
            # rotate the module so its X (long) axis lands on the block's
            # longest local axis
            rot = Matrix.Rotation(HALF_PI, 3, 'Z' if order[0] == 1 else 'Y')
            bmesh.ops.rotate(bm, verts=list(bm.verts), matrix=rot)

        fit_to_bounds(bm, lo, hi)

        new_mesh = bpy.data.meshes.new(mesh_name + "_detail")
        bm.to_mesh(new_mesh)
        bm.free()

        for slot in old.materials:
            new_mesh.materials.append(slot)
        if new_mesh.materials:
            for poly in new_mesh.polygons:
                poly.material_index = 0

        for ob in objs:
            ob.data = new_mesh
        rebuilt += 1

    print("[dune] rebuilt {} mesh datablock(s) across {} object(s)".format(
        rebuilt, len(targets)))


main()
