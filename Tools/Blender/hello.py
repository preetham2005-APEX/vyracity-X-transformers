"""Card S4 connectivity check for headless Blender.

Usage: blender.exe -b --factory-startup -P Tools/Blender/hello.py
Prints the Blender version, the scene unit settings and whether the FBX and glTF exporters are available.
"""
import sys

import bpy

scene = bpy.context.scene
units = scene.unit_settings
print("VC_HELLO version", bpy.app.version_string)
print("VC_HELLO binary", bpy.app.binary_path)
print("VC_HELLO python", sys.version.split()[0])
print("VC_HELLO units", units.system, "scale_length", units.scale_length, "length_unit", units.length_unit)
print("VC_HELLO exporters fbx", hasattr(bpy.ops.export_scene, "fbx"), "gltf", hasattr(bpy.ops.export_scene, "gltf"))
print("VC_HELLO background", bpy.app.background)
