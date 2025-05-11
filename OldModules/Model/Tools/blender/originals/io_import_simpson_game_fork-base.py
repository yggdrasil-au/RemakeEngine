bl_info = {
    "name": "The Simpsons Game Mesh Importer",
    "author": "Turk & Mister_Nebula",
    "version": (1, 0, 2),
    "blender": (4, 0, 0),
    "location": "File > Import-Export",
    "description": "Import .rws.preinstanced, .dff.preinstanced mesh files from The Simpsons Game (PS3)",
    "warning": "",
    "category": "Import-Export",
}

import bpy
import bmesh
import os
import struct
import re
import io
import math
import mathutils
import numpy as np

from bpy.props import (
    StringProperty,
    CollectionProperty
)
from bpy_extras.io_utils import ImportHelper

# --- Logging Function ---
def log_to_blender(text: str, block_name: str = "SimpGame_Importer_Log", to_blender_editor: bool = False) -> None:
    """Appends a message to a text block in Blender's text editor if requested, and always prints to console."""
    # Print to the console for immediate feedback
    print(text)

    # Only try to write to Blender's text editor if requested and bpy.data has 'texts'
    if to_blender_editor and hasattr(bpy.data, "texts"):
        if block_name not in bpy.data.texts:
            text_block = bpy.data.texts.new(block_name)
        else:
            text_block = bpy.data.texts[block_name]
        text_block.write(text + "\n")
# --- End Logging Function ---




def utils_set_mode(mode: str) -> None:
    """Safely sets the object mode."""
    log_to_blender(f"[SetMode] Setting mode to {mode}") # Console only
    if bpy.ops.object.mode_set.poll():
        bpy.ops.object.mode_set(mode=mode, toggle=False)

class SimpGameImport(bpy.types.Operator, ImportHelper):
    bl_idname = "custom_import_scene.simpgame"
    bl_label = "Import"
    bl_options = {'PRESET', 'UNDO'}
    filter_glob: StringProperty(
        default="*.preinstanced",
        options={'HIDDEN'},
    )
    filepath: StringProperty(subtype='FILE_PATH',)
    files: CollectionProperty(type=bpy.types.PropertyGroup)

    def draw(self, context: bpy.types.Context) -> None:
        pass

    def execute(self, context: bpy.types.Context) -> set:
        cur_file = open(self.filepath, "rb")
        log_to_blender("== The Simpsons Game Import Log ==", to_blender_editor=True) # Log header to editor
        log_to_blender(f"[File] Importing file: {self.filepath}", to_blender_editor=True) # Log file path to editor
        log_to_blender(f"[File] File size: {os.path.getsize(self.filepath)} bytes") # Console only
        log_to_blender(f"[File] File name: {os.path.basename(self.filepath)}") # Console only
        log_to_blender(f"[File] output file: {os.path.splitext(os.path.basename(self.filepath))[0]}.blend") # Console only

        cur_collection = bpy.data.collections.new("New Mesh")
        bpy.context.scene.collection.children.link(cur_collection)

        tmpRead = cur_file.read()
        mshBytes = re.compile(b"\x33\xEA\x00\x00....\x2D\x00\x02\x1C", re.DOTALL)
        mesh_iter = 0

        for x in mshBytes.finditer(tmpRead):
            cur_file.seek(x.end() + 4)
            try: # Added error handling for reading initial chunk data
                FaceDataOff = int.from_bytes(cur_file.read(4), byteorder='little')
                MeshDataSize = int.from_bytes(cur_file.read(4), byteorder='little')
                MeshChunkStart = cur_file.tell()
                cur_file.seek(0x14, 1)
                mDataTableCount = int.from_bytes(cur_file.read(4), byteorder='big')
                mDataSubCount = int.from_bytes(cur_file.read(4), byteorder='big')
            except Exception as e:
                log_to_blender(f"[Error] Failed to read mesh chunk header data: {e}", to_blender_editor=True) # Log error to editor
                cur_file.close()
                return {'CANCELLED'}

            for i in range(mDataTableCount):
                cur_file.seek(4, 1)
                cur_file.read(4) # Reading and discarding 4 bytes

            mDataSubStart = cur_file.tell()

            for i in range(mDataSubCount):
                try: # Added error handling for reading sub-mesh data
                    cur_file.seek(mDataSubStart + i * 0xC + 8)
                    offset = int.from_bytes(cur_file.read(4), byteorder='big')
                    cur_file.seek(offset + MeshChunkStart + 0xC)
                    VertCountDataOff = int.from_bytes(cur_file.read(4), byteorder='big') + MeshChunkStart
                    cur_file.seek(VertCountDataOff)
                    VertChunkTotalSize = int.from_bytes(cur_file.read(4), byteorder='big')
                    VertChunkSize = int.from_bytes(cur_file.read(4), byteorder='big')
                    VertCount = int(VertChunkTotalSize / VertChunkSize)
                    cur_file.seek(8, 1) # Skipping 8 bytes (possibly normals offset and size)
                    VertexStart = int.from_bytes(cur_file.read(4), byteorder='big') + FaceDataOff + MeshChunkStart
                    cur_file.seek(0x14, 1) # Skipping 0x14 bytes
                    FaceCount = int(int.from_bytes(cur_file.read(4), byteorder='big') / 2) # FaceCount seems to be num_indices / 2
                    cur_file.seek(4, 1) # Skipping 4 bytes (possibly material index offset)
                    FaceStart = int.from_bytes(cur_file.read(4), byteorder='big') + FaceDataOff + MeshChunkStart

                    log_to_blender(f"[MeshPart {mesh_iter}_{i}] Reading data. VertCount: {VertCount}, FaceCount: {FaceCount}") # Console only

                except Exception as e:
                    log_to_blender(f"[Error] Failed to read sub-mesh data for part {mesh_iter}_{i}: {e}", to_blender_editor=True) # Log error to editor
                    continue # Continue to the next sub-mesh if data reading fails

                cur_file.seek(FaceStart)
                StripList = []
                tmpList = []
                try: # Added error handling for reading face indices
                    for f in range(FaceCount):
                        Indice = int.from_bytes(cur_file.read(2), byteorder='big')
                        if Indice == 65535:
                            if tmpList: # Only append if tmpList is not empty
                                StripList.append(tmpList.copy())
                            tmpList.clear()
                        else:
                            tmpList.append(Indice)
                    if tmpList: # Append the last strip if it doesn't end with 65535
                        StripList.append(tmpList.copy())
                except Exception as e:
                    log_to_blender(f"[Error] Failed to read face indices for mesh part {mesh_iter}_{i}: {e}", to_blender_editor=True) # Log error to editor
                    # Decide whether to continue processing this mesh part without faces or skip
                    continue # Skipping this mesh part if face indices can't be read

                FaceTable = []
                for f in StripList:
                    FaceTable.extend(strip2face(f)) # Use extend to add faces from strip2face

                VertTable = []
                UVTable = []
                CMTable = []
                try: # Added error handling for reading vertex data
                    for v in range(VertCount):
                        cur_file.seek(VertexStart + v * VertChunkSize)
                        TempVert = struct.unpack('>fff', cur_file.read(4 * 3))
                        VertTable.append(TempVert)

                        cur_file.seek(VertexStart + v * VertChunkSize + VertChunkSize - 16)
                        TempUV = struct.unpack('>ff', cur_file.read(4 * 2))
                        UVTable.append((TempUV[0], 1 - TempUV[1])) # Keep original UVs, apply V inversion

                        cur_file.seek(VertexStart + v * VertChunkSize + VertChunkSize - 8)
                        TempCM = struct.unpack('>ff', cur_file.read(4 * 2))
                        CMTable.append((TempCM[0], 1 - TempCM[1])) # Keep original CMs, apply V inversion
                except Exception as e:
                    log_to_blender(f"[Error] Failed to read vertex data for mesh part {mesh_iter}_{i}: {e}", to_blender_editor=True) # Log error to editor
                    continue # Skipping this mesh part if vertex data can't be read

                mesh1 = bpy.data.meshes.new(f"Mesh_{mesh_iter}_{i}") # Name mesh data block
                mesh1.use_auto_smooth = True
                obj = bpy.data.objects.new(f"Mesh_{mesh_iter}_{i}", mesh1) # Name object
                cur_collection.objects.link(obj)
                bpy.context.view_layer.objects.active = obj
                obj.select_set(True)
                mesh = bpy.context.object.data
                bm = bmesh.new()

                # Add vertices to BMesh
                for v_co in VertTable:
                    bm.verts.new(v_co)
                bm.verts.ensure_lookup_table()

                # Create faces in BMesh
                faces_created_count = 0
                for f_indices in FaceTable:
                    try:
                        # Ensure indices are within the valid range
                        valid_face = True
                        for idx in f_indices:
                            if idx < 0 or idx >= len(bm.verts):
                                log_to_blender(f"[FaceError] Invalid vertex index {idx} in face {f_indices}. Skipping face.", to_blender_editor=True) # Log error to editor
                                valid_face = False
                                break
                        if valid_face:
                            bm.faces.new((bm.verts[f_indices[0]], bm.verts[f_indices[1]], bm.verts[f_indices[2]]))
                            faces_created_count += 1
                    except Exception as e:
                        log_to_blender(f"[FaceError] Failed to create face {f_indices}: {e}", to_blender_editor=True) # Log error to editor
                        continue

                log_to_blender(f"[MeshPart {mesh_iter}_{i}] Created {faces_created_count} faces.") # Console only

                # Validate bmesh before accessing layers and assigning UVs
                if not bm.faces:
                    log_to_blender(f"[BMeshWarning] No faces created for mesh {mesh_iter}_{i}. Skipping UV assignment and further processing for this mesh part.", to_blender_editor=True) # Log warning to editor
                    bm.free()
                    continue

                # Ensure UV layers exist before accessing them
                uv_layer = bm.loops.layers.uv.get("uvmap") # Get default UV layer or None
                if uv_layer is None:
                    uv_layer = bm.loops.layers.uv.new("uvmap") # Create if it doesn't exist
                    log_to_blender("[Info] Created new 'uvmap' layer.") # Console only

                cm_layer = bm.loops.layers.uv.get("CM_uv") # Get CM UV layer or None
                if cm_layer is None:
                    cm_layer = bm.loops.layers.uv.new("CM_uv") # Create if it doesn't exist
                    log_to_blender("[Info] Created new 'CM_uv' layer.") # Console only

                uv_layer_name = uv_layer.name
                cm_layer_name = cm_layer.name

                # Assign UVs to loops and perform basic sanitization during assignment
                # This is done per loop, so it handles shared vertices correctly
                uv_assigned_count = 0
                cm_assigned_count = 0
                for f in bm.faces:
                    f.smooth = True
                    for l in f.loops:
                        vert_index = l.vert.index
                        if vert_index >= len(UVTable) or vert_index >= len(CMTable):
                            log_to_blender(f"[UVError] Vertex index {vert_index} out of range for UV/CM tables ({len(UVTable)}/{len(CMTable)}) during assignment. Skipping UV assignment for this loop.", to_blender_editor=True) # Log error to editor
                            continue

                        try:
                            # Assign main UVs
                            uv_coords = UVTable[vert_index]
                            # Sanitize main UVs during assignment
                            if all(math.isfinite(c) for c in uv_coords):
                                l[uv_layer].uv = uv_coords
                                uv_assigned_count += 1
                            else:
                                log_to_blender(f"[Sanitize] Non-finite main UV for vertex {vert_index} in loop. Assigning (0.0, 0.0).") # Console only
                                l[uv_layer].uv = (0.0, 0.0)
                                uv_assigned_count += 1 # Count even if sanitized to default

                            # Assign CM UVs
                            cm_coords = CMTable[vert_index]
                            # Sanitize CM UVs during assignment
                            if all(math.isfinite(c) for c in cm_coords):
                                l[cm_layer].uv = cm_coords
                                cm_assigned_count += 1
                            else:
                                log_to_blender(f"[Sanitize] Non-finite CM UV for vertex {vert_index} in loop. Assigning (0.0, 0.0).") # Console only
                                l[cm_layer].uv = (0.0, 0.0)
                                cm_assigned_count += 1 # Count even if sanitized to default

                        except Exception as e:
                            log_to_blender(f"[UVError] Failed to assign UV/CM for vertex {vert_index} in loop: {e}", to_blender_editor=True) # Log error to editor
                            continue

                log_to_blender(f"[MeshPart {mesh_iter}_{i}] Assigned UVs to {uv_assigned_count} loops, CM UVs to {cm_assigned_count} loops.") # Console only

                bm.to_mesh(mesh)
                bm.free() # Free the bmesh as it's no longer needed

                if uv_layer_name in mesh.uv_layers:
                    sanitize_uvs(mesh.uv_layers[uv_layer_name])
                else:
                    log_to_blender(f"[Sanitize] Warning: Main UV layer '{uv_layer_name}' not found after to_mesh.", to_blender_editor=True) # Log warning to editor

                if cm_layer_name in mesh.uv_layers:
                    sanitize_uvs(mesh.uv_layers[cm_layer_name])
                else:
                    log_to_blender(f"[Sanitize] Warning: CM UV layer '{cm_layer_name}' not found after to_mesh.", to_blender_editor=True) # Log warning to editor

                obj.rotation_euler = (1.5707963705062866, 0, 0)
                log_to_blender(f"[MeshPart {mesh_iter}_{i}] Object created and rotated.") # Console only

            mesh_iter += 1

        cur_file.close()
        log_to_blender("== Import Complete ==", to_blender_editor=True) # Log completion to editor
        return {'FINISHED'}

def strip2face(strip: list) -> list:
    """Converts a triangle strip into a list of triangle faces."""
    log_to_blender(f"[Strip2Face] Converting strip of length {len(strip)} to faces") # Console only
    flipped = False
    tmpTable = []
    # Need at least 3 indices to form a triangle strip
    if len(strip) < 3:
        log_to_blender(f"[Strip2Face] Strip too short ({len(strip)}) to form faces. Skipping.") # Console only
        return []

    for x in range(len(strip)-2):
        # Check for degenerate triangles (indices are the same)
        if strip[x] == strip[x+1] or strip[x] == strip[x+2] or strip[x+1] == strip[x+2]:
            log_to_blender(f"[Strip2Face] Skipping degenerate face in strip at index {x} with indices ({strip[x]}, {strip[x+1]}, {strip[x+2]})") # Console only
            flipped = not flipped # Still flip for correct winding of subsequent faces
            continue

        if flipped:
            tmpTable.append((strip[x+2], strip[x+1], strip[x]))
        else:
            tmpTable.append((strip[x+1], strip[x+2], strip[x]))
        flipped = not flipped
    log_to_blender(f"[Strip2Face] Generated {len(tmpTable)} faces from strip.") # Console only
    return tmpTable


def menu_func_import(self, context: bpy.types.Context) -> None:
    """Adds the import option to the Blender file import menu."""
    log_to_blender("[MenuFunc] Adding import option to menu") # Console only
    self.layout.operator(SimpGameImport.bl_idname, text="The Simpsons Game (.rws,dff)")

def register() -> None:
    """Registers the addon classes and menu functions."""
    log_to_blender("[Register] Registering import operator and menu function") # Console only
    bpy.utils.register_class(SimpGameImport)
    bpy.types.TOPBAR_MT_file_import.append(menu_func_import)

def unregister() -> None:
    """Unregisters the addon classes and menu functions."""
    log_to_blender("[Unregister] Unregistering import operator and menu function") # Console only
    try:
        bpy.utils.unregister_class(SimpGameImport)
    except RuntimeError as e:
        log_to_blender(f"[Unregister] Warning: {e}", to_blender_editor=True) # Log warning to editor
    try:
        bpy.types.TOPBAR_MT_file_import.remove(menu_func_import)
    except Exception as e:
        log_to_blender(f"[Unregister] Warning: {e}", to_blender_editor=True) # Log warning to editor
if __name__ == "__main__":
    log_to_blender("[Main] Running as main script") # Console only
    register()

