import bpy


output_path = r"C:\Dune Vector URP\Assets\DuneVector\Resources\AncientSpire.glb"
bpy.ops.object.select_all(action="DESELECT")
for obj in bpy.data.objects:
    obj.select_set(obj.type == "MESH")
bpy.context.view_layer.objects.active = next(obj for obj in bpy.data.objects if obj.type == "MESH")

bpy.ops.export_scene.gltf(
    filepath=output_path,
    export_format="GLB",
    use_selection=True,
    export_yup=True,
    export_apply=False,
)
print("DV_EXPORT_COMPLETE", output_path)
