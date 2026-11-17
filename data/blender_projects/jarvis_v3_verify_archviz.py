import bpy, math
bpy.ops.mesh.primitive_cube_add(size=2.0, location=(0, 0, 1))
bpy.ops.wm.save_as_mainfile(filepath=r"C:\자비스\data\blender_projects\jarvis_v3_verify_archviz.blend")
print("JARVIS_BLENDER_SAVED:", r"C:\자비스\data\blender_projects\jarvis_v3_verify_archviz.blend")