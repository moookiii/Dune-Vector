import bpy
import sys


source_path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=source_path)

print("DV_GLB_AUDIT_BEGIN")
for obj in (item for item in bpy.data.objects if item.type == "MESH"):
    determinant = obj.matrix_world.to_3x3().determinant()
    if determinant <= 0.0:
        print("NON_POSITIVE_WORLD_DETERMINANT", obj.name, determinant)

cloth = bpy.data.objects.get("Spire_Cloth")
normal_matrix = cloth.matrix_world.to_3x3().inverted().transposed()
winding_sign = 1.0 if cloth.matrix_world.to_3x3().determinant() >= 0.0 else -1.0
score = 0.0
for polygon in cloth.data.polygons:
    center = cloth.matrix_world @ polygon.center
    radial = center.copy()
    radial.z = 0.0
    world_winding_normal = (normal_matrix @ polygon.normal) * winding_sign
    score += world_winding_normal.normalized().dot(radial.normalized())
print("CLOTH_MEAN_OUTWARD_RADIAL", score / len(cloth.data.polygons))
print("CLOTH_TRIANGLES", len(cloth.data.polygons))
print("MESH_OBJECTS", len([item for item in bpy.data.objects if item.type == "MESH"]))
print("DV_GLB_AUDIT_END")
