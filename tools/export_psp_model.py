import bpy
from pathlib import Path


project_root = Path(__file__).resolve().parents[1]
source_path = project_root / "ModelSource" / "PSP_v6.blend"
output_path = project_root / "Assets" / "Models" / "PSP.glb"

bpy.ops.wm.open_mainfile(filepath=str(source_path))
bpy.ops.object.select_all(action="DESELECT")

mesh_objects = [
    obj for obj in bpy.context.scene.objects
    if obj.type == "MESH" and not obj.name.startswith("Preview")
]
for obj in mesh_objects:
    obj.select_set(True)

if not mesh_objects:
    raise RuntimeError("The Blender source contains no PSP mesh objects.")

bpy.context.view_layer.objects.active = mesh_objects[0]
bpy.ops.export_scene.gltf(
    filepath=str(output_path),
    export_format="GLB",
    use_selection=True,
    export_yup=False,
    export_apply=True,
    export_materials="EXPORT",
    export_image_format="AUTO",
    export_cameras=False,
    export_lights=False,
    export_animations=False,
)
print(f"Exported {len(mesh_objects)} PSP mesh objects to {output_path}")
