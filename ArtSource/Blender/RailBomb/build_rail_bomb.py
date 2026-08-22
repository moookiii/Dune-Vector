import bpy, bmesh, math, os

# ---- wipe scene -------------------------------------------------------
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
for me in list(bpy.data.meshes):
    bpy.data.meshes.remove(me)
for ma in list(bpy.data.materials):
    bpy.data.materials.remove(ma)

def mat(name, rgb, metallic, rough, emit=None, emit_strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    if emit is not None:
        b.inputs["Emission Color"].default_value = (emit[0], emit[1], emit[2], 1.0)
        b.inputs["Emission Strength"].default_value = emit_strength
    return m

M_CASE = mat("RailBombCasing", (0.075, 0.085, 0.105), 0.85, 0.32)
M_WARN = mat("RailBombWarning", (0.95, 0.30, 0.05), 0.0, 0.45, (1.0, 0.34, 0.06), 5.0)
M_CORE = mat("RailBombCore", (0.30, 0.85, 1.0), 0.0, 0.25, (0.35, 0.90, 1.0), 7.0)

SEG = 12
parts = []

def add_cyl(r1, r2, depth, z, name, seg=SEG):
    bpy.ops.mesh.primitive_cone_add(vertices=seg, radius1=r1, radius2=r2,
                                    depth=depth, location=(0.0, 0.0, z))
    o = bpy.context.active_object
    o.name = name
    parts.append(o)
    return o

# hull, nose at +Z
body  = add_cyl(0.50, 0.50, 1.15, -0.025, "Body")
noseA = add_cyl(0.50, 0.28, 0.30,  0.700, "NoseA")
noseB = add_cyl(0.28, 0.10, 0.15,  0.925, "NoseB")
tail  = add_cyl(0.50, 0.34, 0.30, -0.750, "TailTaper")

# hazard bands + emissive collar / thruster
bandA = add_cyl(0.532, 0.532, 0.10,  0.340, "BandA")
bandB = add_cyl(0.532, 0.532, 0.10,  0.060, "BandB")
collar = add_cyl(0.516, 0.516, 0.07,  0.520, "Collar")
thrust = add_cyl(0.345, 0.245, 0.07, -0.930, "Thruster")

# ---- fins -------------------------------------------------------------
profile = [(0.34, -0.40), (0.34, -0.92), (0.86, -0.92), (0.70, -0.56)]
fins = []
for i in range(4):
    me = bpy.data.meshes.new("Fin%d" % i)
    bm = bmesh.new()
    verts = [bm.verts.new((x, -0.026, z)) for (x, z) in profile]
    f = bm.faces.new(verts)
    bmesh.ops.solidify(bm, geom=[f], thickness=-0.052)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("Fin%d" % i, me)
    bpy.context.collection.objects.link(o)
    o.rotation_euler = (0.0, 0.0, math.radians(45.0 + 90.0 * i))
    fins.append(o)
parts.extend(fins)

# ---- material assignment ---------------------------------------------
for o in parts:
    o.data.materials.append(M_CASE)
for o in (bandA, bandB):
    o.data.materials.clear()
    o.data.materials.append(M_WARN)
for o in (collar, thrust):
    o.data.materials.clear()
    o.data.materials.append(M_CORE)

# ---- join -------------------------------------------------------------
bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
bomb = bpy.context.active_object
bomb.name = "RailBomb"

# nose along Blender +Y so the glTF round trip lands on Unity +Z
bomb.rotation_euler = (math.radians(-90.0), 0.0, 0.0)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

# flat shading for the vector look, plus crisp normals
bpy.ops.object.shade_flat()
bomb.data.polygons.foreach_set("use_smooth", [False] * len(bomb.data.polygons))
bomb.data.update()

dim = tuple(round(v, 4) for v in bomb.dimensions)
result = {"name": bomb.name, "verts": len(bomb.data.vertices),
          "tris": len(bomb.data.loop_triangles), "polys": len(bomb.data.polygons),
          "dimensions": dim,
          "materials": [m.name for m in bomb.data.materials]}
