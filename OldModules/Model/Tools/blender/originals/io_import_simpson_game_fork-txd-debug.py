bl_info = {
    "name": "The Simpsons Game Mesh Importer",
    "author": "Turk & Mister_Nebula",
    "version": (1, 0, 3), # Incremented version
    "blender": (4, 0, 0),
    "location": "File > Import-Export",
    "description": "Import .rws.preinstanced, .dff.preinstanced mesh files from The Simpsons Game (PS3) and detect embedded strings.", # Updated description
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
import string # Import string for ALLOWED_CHARS

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
            log_to_blender(f"[Log] Created new text block: '{block_name}'", to_blender_editor=False) # Log creation to console
        else:
            text_block = bpy.data.texts[block_name]
        text_block.write(text + "\n")

# --- End Logging Function ---

# Characters typically allowed in programmer-defined strings
ALLOWED_CHARS = string.ascii_letters + string.digits + '_-.'
ALLOWED_CHARS_BYTES = ALLOWED_CHARS.encode('ascii') # Convert allowed chars to bytes

# --- Configuration for String Detection ---
# Fixed Signatures to check (often indicate block headers)
# You need to determine the correct signature bytes (as bytes)
# and the relative offset (in bytes) from the SIGNATURE START to the STRING START.
# Use your previous script's output and a hex editor to find these.
FIXED_SIGNATURES_TO_CHECK = [
    {'signature': bytes.fromhex('0211010002000000'), 'relative_string_offset': 16, 'description': 'String Block Header (General, 8 bytes)'},
    {'signature': bytes.fromhex('0211010002000000140000002d00021c'), 'relative_string_offset': 16, 'description': 'String Block Header (Subtype A, 16 bytes)'},
    {'signature': bytes.fromhex('0211010002000000180000002d00021c'), 'relative_string_offset': 16, 'description': 'String Block Header (Subtype B, 16 bytes) - Hypothesized'},
    {'signature': bytes.fromhex('905920010000803f0000803f0000803f'), 'relative_string_offset': 16, 'description': 'Another Block Type Header (16 bytes, Common Float Pattern)'} # Corrected based on common 803f pattern, PLACEHOLDER: Verify exact bytes and offset
]

# Analysis Settings for String Detection
MAX_POTENTIAL_STRING_LENGTH = 64
MIN_EXTRACTED_STRING_LENGTH = 4
CONTEXT_SIZE = 16 # Bytes around the SIGNATURE / START marker to show
STRING_CONTEXT_SIZE = 5 # Bytes around the STRING to show

# --- Adapted String Finding Function ---
def find_strings_by_signature_in_data(data: bytes, signatures_info: list, max_string_length: int, min_string_length: int, context_bytes: int, string_context_bytes: int):
    """
    Searches binary data for specific byte signatures and attempts to extract
    associated strings at a fixed relative offset.
    Outputs results including context around the signature and string.

    Args:
        data (bytes): The binary data to search within.
        signatures_info (list): A list of dictionaries, each containing 'signature'
                                ('bytes'), 'relative_string_offset' (int), and
                                'description' (str).
        max_string_length (int): Maximum number of bytes to check for a string.
        min_string_length (int): Minimum length for an extracted string to be
                                considered valid.
        context_bytes (int): Bytes of context around the signature.
        string_context_bytes (int): Bytes of context around the extracted string.

    Returns:
        list: A list of dictionaries for found patterns, containing details
            similar to the previous script's output.
    """
    results = []
    data_len = len(data)

    log_to_blender("[String Search] Starting search for configured fixed signatures...") # Console only

    for sig_info in signatures_info:
        signature = sig_info['signature']
        relative_string_offset = sig_info['relative_string_offset']
        signature_len = len(signature)
        current_offset = 0

        log_to_blender(f"[String Search] Searching for signature: {signature.hex()} ('{sig_info['description']}')") # Console only

        while current_offset < data_len:
            # Search for the next occurrence of the signature
            signature_offset = data.find(signature, current_offset)

            if signature_offset == -1:
                # Signature not found further in the data
                break

            # Calculate the potential string start offset
            string_start_offset = signature_offset + relative_string_offset

            # Check if the potential string start is within data bounds
            if string_start_offset < 0 or string_start_offset >= data_len:
                # log_to_blender(f"Warning: Calculated string offset {string_start_offset:08X} for signature at {signature_offset:08X} is out of data bounds.", to_blender_editor=False) # Too chatty for console
                current_offset = signature_offset + signature_len
                continue


            # --- Attempt to extract string ---
            extracted_string_bytes = b""
            # Limit string search to not go past max_string_length OR data end
            string_search_end = min(data_len, string_start_offset + max_string_length)
            string_end_offset = string_start_offset # Initialize end offset to start

            # Ensure we don't read past the end of the data
            if string_start_offset < data_len:
                for i in range(string_start_offset, string_search_end):
                    if i >= data_len: # Extra safety check, though range should prevent this
                        break
                    byte = data[i]
                    if byte in ALLOWED_CHARS_BYTES:
                        extracted_string_bytes += bytes([byte])
                        string_end_offset = i + 1 # Update end offset (exclusive)
                    else:
                        # Non-allowed character ends the string
                        break


            extracted_string_text = None
            is_valid_string = False
            string_context_before_data = None
            string_context_after_data = None


            if extracted_string_bytes:
                try:
                    extracted_string_text = extracted_string_bytes.decode('ascii')
                    if len(extracted_string_text) >= min_string_length:
                        is_valid_string = True
                        # --- Extract context bytes around the STRING ---
                        string_context_before_start = max(0, string_start_offset - string_context_bytes)
                        string_context_after_end = min(data_len, string_end_offset + string_context_bytes)
                        string_context_before_data = data[string_context_before_start : string_start_offset]
                        string_context_after_data = data[string_end_offset : string_context_after_end]

                except UnicodeDecodeError:
                    # log_to_blender(f"Warning: UnicodeDecodeError at {string_start_offset:08X} trying to decode potential string.", to_blender_editor=False) # Too chatty for console
                    pass # String is not valid if decoding fails


            # --- Extract context bytes around the SIGNATURE ---
            context_before_start = max(0, signature_offset - context_bytes)
            context_after_end = min(data_len, signature_offset + signature_len + context_bytes)

            context_before_data = data[context_before_start : signature_offset]
            context_after_data = data[signature_offset + signature_len : context_after_end]

            results.append({
                'type': 'fixed_signature_string', # Indicate result type
                'signature_offset': signature_offset,
                'signature': signature.hex(),
                'signature_description': sig_info['description'],
                'context_before': context_before_data.hex(),
                'context_after': context_after_data.hex(),
                'string_found': is_valid_string,
                'string_offset': string_start_offset if is_valid_string else None,
                'string': extracted_string_text if is_valid_string else None,
                'string_context_before': string_context_before_data.hex() if string_context_before_data is not None else None,
                'string_context_after': string_context_after_data.hex() if string_context_after_data is not None else None
            })

            # Continue search *after* the current signature occurrence
            current_offset = signature_offset + signature_len

    log_to_blender("[String Search] Fixed signature search complete.")
    return results


def sanitize_uvs(uv_layer: bpy.types.MeshUVLoopLayer) -> None:
    """Checks for and sanitizes non-finite UV coordinates in a UV layer."""
    # log_to_blender(f"[Sanitize] Checking UV layer: {uv_layer.name}", to_blender_editor=False) # Console only - too chatty

    # Check if uv_layer.data is accessible and has elements
    if not uv_layer.data:
        log_to_blender(f"[Sanitize] Warning: UV layer '{uv_layer.name}' has no data.", to_blender_editor=True) # Log warning to editor
        return

    # Note: Sanitize is now mostly done during assignment in the main loop for performance,
    # but this could catch any remaining issues or be a fallback.
    sanitized_count = 0
    for uv_loop in uv_layer.data:
        # Check for NaN or infinity
        if not all(math.isfinite(c) for c in uv_loop.uv):
            # log_to_blender(f"[Sanitize] Non-finite UV replaced with (0.0, 0.0): {uv_loop.uv[:]}") # Too chatty for console
            uv_loop.uv.x = 0.0
            uv_loop.uv.y = 0.0
            sanitized_count += 1
    if sanitized_count > 0:
        log_to_blender(f"[Sanitize] Sanitized {sanitized_count} non-finite UV coordinates in layer '{uv_layer.name}'.", to_blender_editor=True) # Log count to editor



def utils_set_mode(mode: str) -> None:
    """Safely sets the object mode."""
    # log_to_blender(f"[SetMode] Setting mode to {mode}", to_blender_editor=False) # Console only - too chatty
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
        log_block_name = "SimpGame_Importer_Log" # Define the log block name
        log_to_blender("== The Simpsons Game Import Log ==", block_name=log_block_name, to_blender_editor=True) # Log header to editor
        log_to_blender(f"[File] Importing file: {self.filepath}", block_name=log_block_name, to_blender_editor=True) # Log file path to editor
        log_to_blender(f"[File] File size: {os.path.getsize(self.filepath)} bytes", block_name=log_block_name, to_blender_editor=False) # Console only
        log_to_blender(f"[File] File name: {os.path.basename(self.filepath)}", block_name=log_block_name, to_blender_editor=False) # Console only
        log_to_blender(f"[File] Output file: {os.path.splitext(os.path.basename(self.filepath))[0]}.blend", block_name=log_block_name, to_blender_editor=False) # Console only

        try:
            with open(self.filepath, "rb") as cur_file:
                tmpRead = cur_file.read()
        except FileNotFoundError:
            log_to_blender(f"[Error] File not found: {self.filepath}", block_name=log_block_name, to_blender_editor=True) # Log error to editor
            return {'CANCELLED'}
        except Exception as e:
            log_to_blender(f"[Error] Failed to read file {self.filepath}: {e}", block_name=log_block_name, to_blender_editor=True) # Log error to editor
            return {'CANCELLED'}


        # --- Perform String Detection ---
        log_to_blender("\n--- Embedded String Analysis ---", block_name=log_block_name, to_blender_editor=True) # Log header to editor
        string_results = find_strings_by_signature_in_data(
            tmpRead,
            FIXED_SIGNATURES_TO_CHECK,
            MAX_POTENTIAL_STRING_LENGTH,
            MIN_EXTRACTED_STRING_LENGTH,
            CONTEXT_SIZE,
            STRING_CONTEXT_SIZE
        )

        found_string_count = 0
        for item in string_results:
            if item['type'] == 'fixed_signature_string' and item['string_found']:
                found_string_count += 1
                log_to_blender(f"\n[String Found] Signature '{item['signature_description']}' ({item['signature']}) at {item['signature_offset']:08X}", block_name=log_block_name, to_blender_editor=True)
                log_to_blender(f"  Context Before Sig: {item['context_before']}", block_name=log_block_name, to_blender_editor=True)
                log_to_blender(f"  Context After Sig : {item['context_after']}", block_name=log_block_name, to_blender_editor=True)
                log_to_blender(f"  Found String Offset: {item['string_offset']:08X}", block_name=log_block_name, to_blender_editor=True)
                log_to_blender(f"  Extracted String  : {item['string_context_before']} | {item['string']} | {item['string_context_after']}", block_name=log_block_name, to_blender_editor=True)
        if found_string_count == 0:
            log_to_blender("[String Found] No valid strings found for configured signatures.", block_name=log_block_name, to_blender_editor=True)

        log_to_blender("\n--- Mesh Import Process ---", block_name=log_block_name, to_blender_editor=True) # Log header to editor


        # --- Start Mesh Import (Existing Logic) ---
        cur_collection = bpy.data.collections.new("New Mesh")
        bpy.context.scene.collection.children.link(cur_collection)

        # Using re.compile on the full data bytes is fine for finding chunk starts
        mshBytes = re.compile(b"\x33\xEA\x00\x00....\x2D\x00\x02\x1C", re.DOTALL)
        mesh_iter = 0

        # Use io.BytesIO to treat the byte data like a file for seeking/reading
        data_io = io.BytesIO(tmpRead)

        for x in mshBytes.finditer(tmpRead):
            data_io.seek(x.end() + 4) # Use data_io for seeking/reading within the matched chunk
            try: # Added error handling for reading initial chunk data
                FaceDataOff = int.from_bytes(data_io.read(4), byteorder='little')
                MeshDataSize = int.from_bytes(data_io.read(4), byteorder='little')
                MeshChunkStart = data_io.tell() # Use data_io.tell()
                data_io.seek(0x14, 1) # Use data_io.seek()
                mDataTableCount = int.from_bytes(data_io.read(4), byteorder='big')
                mDataSubCount = int.from_bytes(data_io.read(4), byteorder='big')
                log_to_blender(f"[Mesh {mesh_iter}] Found chunk at {x.start():08X}. FaceDataOff: {FaceDataOff}, MeshDataSize: {MeshDataSize}, mDataTableCount: {mDataTableCount}, mDataSubCount: {mDataSubCount}", block_name=log_block_name, to_blender_editor=False) # Log chunk info to console

            except Exception as e:
                log_to_blender(f"[Error] Failed to read mesh chunk header data at {x.start():08X}: {e}", block_name=log_block_name, to_blender_editor=True) # Log error to editor
                continue # Skip this chunk and try to find the next one

            for i in range(mDataTableCount):
                data_io.seek(4, 1) # Use data_io
                data_io.read(4) # Reading and discarding 4 bytes using data_io

            mDataSubStart = data_io.tell() # Use data_io.tell()

            for i in range(mDataSubCount):
                try: # Added error handling for reading sub-mesh data
                    data_io.seek(mDataSubStart + i * 0xC + 8) # Use data_io
                    offset = int.from_bytes(data_io.read(4), byteorder='big') # Use data_io
                    data_io.seek(offset + MeshChunkStart + 0xC) # Use data_io
                    VertCountDataOff = int.from_bytes(data_io.read(4), byteorder='big') + MeshChunkStart # Use data_io
                    data_io.seek(VertCountDataOff) # Use data_io
                    VertChunkTotalSize = int.from_bytes(data_io.read(4), byteorder='big') # Use data_io
                    VertChunkSize = int.from_bytes(data_io.read(4), byteorder='big') # Use data_io
                    if VertChunkSize <= 0:
                        log_to_blender(f"[Mesh {mesh_iter}_{i}] Warning: VertChunkSize is non-positive ({VertChunkSize}). Skipping mesh part.", block_name=log_block_name, to_blender_editor=True)
                        continue
                    VertCount = int(VertChunkTotalSize / VertChunkSize)
                    data_io.seek(8, 1) # Skipping 8 bytes (possibly normals offset and size) using data_io
                    VertexStart = int.from_bytes(data_io.read(4), byteorder='big') + FaceDataOff + MeshChunkStart # Use data_io
                    data_io.seek(0x14, 1) # Skipping 0x14 bytes using data_io
                    # Ensure enough bytes are available before reading FaceCount
                    face_count_bytes_offset = data_io.tell()
                    if face_count_bytes_offset + 4 > len(tmpRead):
                        log_to_blender(f"[Mesh {mesh_iter}_{i}] Error: Insufficient data to read FaceCount at offset {face_count_bytes_offset:08X}. Skipping mesh part.", block_name=log_block_name, to_blender_editor=True)
                        continue
                    FaceCount = int(int.from_bytes(data_io.read(4), byteorder='big') / 2) # FaceCount seems to be num_indices / 2, use data_io
                    data_io.seek(4, 1) # Skipping 4 bytes (possibly material index offset) using data_io
                    FaceStart = int.from_bytes(data_io.read(4), byteorder='big') + FaceDataOff + MeshChunkStart # Use data_io

                    log_to_blender(f"[MeshPart {mesh_iter}_{i}] Reading data. VertCount: {VertCount}, FaceCount: {FaceCount}, VertexStart: {VertexStart:08X}, FaceStart: {FaceStart:08X}", block_name=log_block_name, to_blender_editor=False) # Console only

                except Exception as e:
                    log_to_blender(f"[Error] Failed to read sub-mesh header data for part {mesh_iter}_{i}: {e}", block_name=log_block_name, to_blender_editor=True) # Log error to editor
                    continue # Continue to the next sub-mesh if data reading fails

                # Read Face Indices
                data_io.seek(FaceStart) # Use data_io
                StripList = []
                tmpList = []
                try: # Added error handling for reading face indices
                    # Check if FaceStart is within bounds to prevent excessive reading attempts
                    if FaceStart < 0 or FaceStart >= len(tmpRead):
                        log_to_blender(f"[MeshPart {mesh_iter}_{i}] Error: FaceStart offset {FaceStart:08X} is out of bounds. Skipping face data read.", block_name=log_block_name, to_blender_editor=True)
                        FaceCount = 0 # Effectively skip face processing
                    else:
                        data_io.seek(FaceStart) # Reset seek in case bounds check changed it
                        # Ensure enough data is available for FaceCount indices (each 2 bytes)
                        if FaceStart + FaceCount * 2 > len(tmpRead):
                            log_to_blender(f"[MeshPart {mesh_iter}_{i}] Warning: Predicted face data size ({FaceCount * 2} bytes) exceeds file bounds from FaceStart {FaceStart:08X}. Reading available data.", block_name=log_block_name, to_blender_editor=True)
                            # Adjust FaceCount based on available data
                            FaceCount = (len(tmpRead) - FaceStart) // 2
                            log_to_blender(f"[MeshPart {mesh_iter}_{i}] Adjusted FaceCount to {FaceCount} based on available data.", block_name=log_block_name, to_blender_editor=True)


                    for f in range(FaceCount):
                        # Ensure enough data is available for the next index
                        if data_io.tell() + 2 > len(tmpRead):
                            log_to_blender(f"[MeshPart {mesh_iter}_{i}] Warning: Hit end of data while reading face index {f}. Stopping face index read.", block_name=log_block_name, to_blender_editor=True)
                            break # Stop reading indices if not enough data
                        Indice = int.from_bytes(data_io.read(2), byteorder='big') # Use data_io
                        if Indice == 65535:
                            if tmpList: # Only append if tmpList is not empty
                                StripList.append(tmpList.copy())
                            tmpList.clear()
                        else:
                            tmpList.append(Indice)
                    if tmpList: # Append the last strip if it doesn't end with 65535
                        StripList.append(tmpList.copy())
                except Exception as e:
                    log_to_blender(f"[Error] Failed to read face indices for mesh part {mesh_iter}_{i}: {e}", block_name=log_block_name, to_blender_editor=True) # Log error to editor
                    # Decide whether to continue processing this mesh part without faces or skip
                    continue # Skipping this mesh part if face indices can't be read

                FaceTable = []
                for f in StripList:
                    FaceTable.extend(strip2face(f)) # Use extend to add faces from strip2face

                VertTable = []
                UVTable = []
                CMTable = []
                try: # Added error handling for reading vertex data
                    # Check if VertexStart is within bounds
                    if VertexStart < 0 or VertexStart >= len(tmpRead):
                        log_to_blender(f"[MeshPart {mesh_iter}_{i}] Error: VertexStart offset {VertexStart:08X} is out of bounds. Skipping vertex data read.", block_name=log_block_name, to_blender_editor=True)
                        VertCount = 0 # Effectively skip vertex processing

                    for v in range(VertCount):
                        vert_data_start = VertexStart + v * VertChunkSize
                        # Check if there's enough data for this vertex chunk
                        if vert_data_start + VertChunkSize > len(tmpRead):
                            log_to_blender(f"[MeshPart {mesh_iter}_{i}] Warning: Hit end of data while reading vertex {v}. Stopping vertex read.", block_name=log_block_name, to_blender_editor=True)
                            # Adjust VertCount for subsequent loops if necessary, although breaking works for current loop
                            break

                        data_io.seek(vert_data_start) # Use data_io

                        # Ensure enough data for vertex coords
                        if data_io.tell() + 12 > len(tmpRead): # 4 bytes/float * 3 floats = 12 bytes
                            log_to_blender(f"[MeshPart {mesh_iter}_{i}] Warning: Insufficient data for vertex coords at {data_io.tell():08X} for vertex {v}. Skipping.", block_name=log_block_name, to_blender_editor=True)
                            continue # Skip this vertex

                        TempVert = struct.unpack('>fff', data_io.read(4 * 3)) # Use data_io
                        VertTable.append(TempVert)

                        # Ensure enough data for UVs
                        uv_offset = vert_data_start + VertChunkSize - 16
                        if uv_offset < 0 or uv_offset + 8 > len(tmpRead): # 4 bytes/float * 2 floats = 8 bytes
                            log_to_blender(f"[MeshPart {mesh_iter}_{i}] Warning: Insufficient data for UV coords at {uv_offset:08X} for vertex {v}. Skipping UV.", block_name=log_block_name, to_blender_editor=True)
                            TempUV = (0.0, 0.0) # Assign default UVs
                        else:
                            data_io.seek(uv_offset) # Use data_io
                            TempUV = struct.unpack('>ff', data_io.read(4 * 2)) # Use data_io
                        UVTable.append((TempUV[0], 1 - TempUV[1])) # Keep original UVs, apply V inversion

                        # Ensure enough data for CMs
                        cm_offset = vert_data_start + VertChunkSize - 8
                        if cm_offset < 0 or cm_offset + 8 > len(tmpRead): # 4 bytes/float * 2 floats = 8 bytes
                            log_to_blender(f"[MeshPart {mesh_iter}_{i}] Warning: Insufficient data for CM coords at {cm_offset:08X} for vertex {v}. Skipping CM.", block_name=log_block_name, to_blender_editor=True)
                            TempCM = (0.0, 0.0) # Assign default CMs
                        else:
                            data_io.seek(cm_offset) # Use data_io
                            TempCM = struct.unpack('>ff', data_io.read(4 * 2)) # Use data_io
                        CMTable.append((TempCM[0], 1 - TempCM[1])) # Keep original CMs, apply V inversion


                    log_to_blender(f"[MeshPart {mesh_iter}_{i}] Read {len(VertTable)} vertices, {len(UVTable)} UVs, {len(CMTable)} CMs.", block_name=log_block_name, to_blender_editor=False) # Console only

                except Exception as e:
                    log_to_blender(f"[Error] Failed to read vertex data for mesh part {mesh_iter}_{i}: {e}", block_name=log_block_name, to_blender_editor=True) # Log error to editor
                    continue # Skipping this mesh part if vertex data can't be read

                # Check if we have data to create a mesh
                if not VertTable or not FaceTable:
                    log_to_blender(f"[MeshPart {mesh_iter}_{i}] Warning: No valid vertices or faces read for mesh part. Skipping mesh creation.", block_name=log_block_name, to_blender_editor=True)
                    continue # Skip creating mesh if no data

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
                log_to_blender(f"[MeshPart {mesh_iter}_{i}] Added {len(bm.verts)} vertices to BMesh.", block_name=log_block_name, to_blender_editor=False)

                # Create faces in BMesh
                faces_created_count = 0
                for f_indices in FaceTable:
                    try:
                        # Ensure indices are within the valid range
                        valid_face = True
                        face_verts = []
                        for idx in f_indices:
                            if idx < 0 or idx >= len(bm.verts):
                                log_to_blender(f"[FaceError] Invalid vertex index {idx} in face {f_indices}. Skipping face.", block_name=log_block_name, to_blender_editor=True) # Log error to editor
                                valid_face = False
                                break
                            face_verts.append(bm.verts[idx])

                        if valid_face:
                            # Check if the face already exists to prevent duplicates
                            # (This is a simplified check, might not catch all cases)
                            existing_face = None
                            try:
                                # Check if a face with these vertices already exists (Blender internal check)
                                # This can sometimes throw an error if the verts don't form a valid loop yet
                                # bm.faces.get([v.index for v in face_verts]) # This doesn't work directly for checking existence before adding
                                # A better check might involve iterating or using vertex hashing, but for simplicity, rely on bm.faces.new's behavior or basic index checks.
                                pass
                            except Exception:
                                # Ignore potential errors during check
                                pass

                            try:
                                # Only add if it doesn't cause errors (e.g., non-planar, duplicate edge)
                                bm.faces.new(face_verts)
                                faces_created_count += 1
                            except ValueError as e:
                                # Catch cases where face creation fails (e.g., non-manifold, duplicate)
                                log_to_blender(f"[FaceWarning] Failed to create face {f_indices} ({len(face_verts)} verts): {e}. Skipping.", block_name=log_block_name, to_blender_editor=True)
                            except Exception as e:
                                log_to_blender(f"[FaceError] Unexpected error creating face {f_indices}: {e}. Skipping.", block_name=log_block_name, to_blender_editor=True)


                    except Exception as e:
                        log_to_blender(f"[FaceError] Unhandled error processing face indices {f_indices}: {e}", block_name=log_block_name, to_blender_editor=True) # Log error to editor
                        continue

                log_to_blender(f"[MeshPart {mesh_iter}_{i}] Attempted to create {len(FaceTable)} faces, successfully created {faces_created_count}.", block_name=log_block_name, to_blender_editor=False)

                # Validate bmesh before accessing layers and assigning UVs
                if not bm.faces:
                    log_to_blender(f"[BMeshWarning] No faces created for mesh {mesh_iter}_{i}. Skipping UV assignment and further processing for this mesh part.", block_name=log_block_name, to_blender_editor=True) # Log warning to editor
                    bm.free()
                    # Ensure object and mesh data are cleaned up if no faces were created
                    if mesh1: # Check if mesh data was created
                        if mesh1.users == 1: # Check if only this object uses it
                            bpy.data.meshes.remove(mesh1)
                            log_to_blender(f"[BMeshWarning] Removed unused mesh data block '{mesh1.name}'.", block_name=log_block_name, to_blender_editor=True)
                    if obj: # Check if object was created
                        if obj.users == 1: # Check if only the collection links it
                            # Remove from collection and delete
                            for col in bpy.data.collections:
                                if obj.name in col.objects:
                                    col.objects.unlink(obj)
                            bpy.data.objects.remove(obj)
                            log_to_blender(f"[BMeshWarning] Removed unused object '{obj.name}'.", block_name=log_block_name, to_blender_editor=True)

                    continue # Skip to the next mesh part

                # Ensure UV layers exist before accessing them
                # Check if the layers already exist first to avoid errors if run multiple times
                uv_layer = bm.loops.layers.uv.get("uvmap") # Get default UV layer or None
                if uv_layer is None:
                    uv_layer = bm.loops.layers.uv.new("uvmap") # Create if it doesn't exist
                    log_to_blender("[Info] Created new 'uvmap' layer.", block_name=log_block_name, to_blender_editor=False) # Console only
                else:
                    # Clear existing data if layer already existed? Not strictly needed for new bmesh.
                    pass

                cm_layer = bm.loops.layers.uv.get("CM_uv") # Get CM UV layer or None
                if cm_layer is None:
                    cm_layer = bm.loops.layers.uv.new("CM_uv") # Create if it doesn't exist
                    log_to_blender("[Info] Created new 'CM_uv' layer.", block_name=log_block_name, to_blender_editor=False) # Console only
                else:
                    # Clear existing data if layer already existed?
                    pass

                uv_layer_name = uv_layer.name
                cm_layer_name = cm_layer.name

                # Assign UVs to loops and perform basic sanitization during assignment
                # This is done per loop, so it handles shared vertices correctly
                uv_assigned_count = 0
                cm_assigned_count = 0
                for f in bm.faces:
                    f.smooth = True # Set face to smooth shading
                    for l in f.loops:
                        vert_index = l.vert.index
                        if vert_index >= len(UVTable) or vert_index >= len(CMTable):
                            log_to_blender(f"[UVError] Vertex index {vert_index} out of range for UV/CM tables ({len(UVTable)}/{len(CMTable)}) during assignment for mesh part {mesh_iter}_{i}. Skipping UV assignment for this loop.", block_name=log_block_name, to_blender_editor=True) # Log error to editor
                            # Assign default (0,0) UVs to avoid errors with missing data
                            l[uv_layer].uv = (0.0, 0.0)
                            l[cm_layer].uv = (0.0, 0.0)
                            continue

                        try:
                            # Assign main UVs
                            uv_coords = UVTable[vert_index]
                            # Sanitize main UVs during assignment
                            if all(math.isfinite(c) for c in uv_coords):
                                l[uv_layer].uv = uv_coords
                                uv_assigned_count += 1
                            else:
                                # log_to_blender(f"[Sanitize] Non-finite main UV for vertex {vert_index} in loop of mesh part {mesh_iter}_{i}. Assigning (0.0, 0.0).", block_name=log_block_name, to_blender_editor=False) # Too chatty
                                l[uv_layer].uv = (0.0, 0.0)
                                uv_assigned_count += 1 # Count even if sanitized to default

                            # Assign CM UVs
                            cm_coords = CMTable[vert_index]
                            # Sanitize CM UVs during assignment
                            if all(math.isfinite(c) for c in cm_coords):
                                l[cm_layer].uv = cm_coords
                                cm_assigned_count += 1
                            else:
                                # log_to_blender(f"[Sanitize] Non-finite CM UV for vertex {vert_index} in loop of mesh part {mesh_iter}_{i}. Assigning (0.0, 0.0).", block_name=log_block_name, to_blender_editor=False) # Too chatty
                                l[cm_layer].uv = (0.0, 0.0)
                                cm_assigned_count += 1 # Count even if sanitized to default

                        except Exception as e:
                            log_to_blender(f"[UVError] Failed to assign UV/CM for vertex {vert_index} in loop of mesh part {mesh_iter}_{i}: {e}", block_name=log_block_name, to_blender_editor=True) # Log error to editor
                            # Assign default (0,0) UVs to prevent potential issues even on error
                            l[uv_layer].uv = (0.0, 0.0)
                            l[cm_layer].uv = (0.0, 0.0)
                            continue # Continue to the next loop


                log_to_blender(f"[MeshPart {mesh_iter}_{i}] Assigned UVs to {uv_assigned_count} loops, CM UVs to {cm_assigned_count} loops.", block_name=log_block_name, to_blender_editor=False) # Console only


                # Finish BMesh and assign to mesh data
                bm.to_mesh(mesh)
                bm.free() # Free the bmesh as it's no longer needed
                log_to_blender(f"[MeshPart {mesh_iter}_{i}] BMesh converted to mesh data.", block_name=log_block_name, to_blender_editor=False)


                # Perform a final sanitize check on the created UV layers in mesh data
                # Note: The assignment loop already sanitizes, so this is a fallback/verification
                if uv_layer_name in mesh.uv_layers:
                    # Don't pass to_blender_editor=True here, sanitize_uvs logs its own findings to editor
                    sanitize_uvs(mesh.uv_layers[uv_layer_name])
                else:
                    log_to_blender(f"[Sanitize] Warning: Main UV layer '{uv_layer_name}' not found on mesh data block after to_mesh for mesh {mesh_iter}_{i}.", block_name=log_block_name, to_blender_editor=True) # Log warning to editor

                if cm_layer_name in mesh.uv_layers:
                    # Don't pass to_blender_editor=True here
                    sanitize_uvs(mesh.uv_layers[cm_layer_name])
                else:
                    log_to_blender(f"[Sanitize] Warning: CM UV layer '{cm_layer_name}' not found on mesh data block after to_mesh for mesh {mesh_iter}_{i}.", block_name=log_block_name, to_blender_editor=True) # Log warning to editor

                # Apply rotation
                obj.rotation_euler = (1.5707963705062866, 0, 0) # Rotate 90 degrees around X (pi/2)
                log_to_blender(f"[MeshPart {mesh_iter}_{i}] Object created '{obj.name}' and rotated.", block_name=log_block_name, to_blender_editor=False) # Console only

            mesh_iter += 1

        # data_io is automatically closed when the function exits or garbage collected
        # cur_file is also implicitly closed by the 'with open' block

        log_to_blender("== Import Complete ==", block_name=log_block_name, to_blender_editor=True) # Log completion to editor
        return {'FINISHED'}

def strip2face(strip: list) -> list:
    """Converts a triangle strip into a list of triangle faces."""
    # log_to_blender(f"[Strip2Face] Converting strip of length {len(strip)} to faces", to_blender_editor=False) # Console only - too chatty
    flipped = False
    tmpTable = []
    # Need at least 3 indices to form a triangle strip
    if len(strip) < 3:
        # log_to_blender(f"[Strip2Face] Strip too short ({len(strip)}) to form faces. Skipping.", to_blender_editor=False) # Console only - too chatty
        return []

    for x in range(len(strip)-2):
        v1 = strip[x]
        v2 = strip[x+1]
        v3 = strip[x+2]
        # Check for degenerate triangles (indices are the same)
        if v1 == v2 or v1 == v3 or v2 == v3:
            # log_to_blender(f"[Strip2Face] Skipping degenerate face in strip at index {x} with indices ({v1}, {v2}, {v3})", to_blender_editor=False) # Console only - too chatty
            # Even if degenerate, the 'flipped' state still needs to toggle for the next potential face
            flipped = not flipped # Still flip for correct winding of subsequent faces
            continue # Skip this specific face

        if flipped:
            tmpTable.append((v3, v2, v1)) # Reversed winding for flipped faces
        else:
            tmpTable.append((v2, v3, v1)) # Standard winding
        flipped = not flipped # Toggle flipped state for the next iteration

    # log_to_blender(f"[Strip2Face] Generated {len(tmpTable)} faces from strip.", to_blender_editor=False) # Console only - too chatty
    return tmpTable


def menu_func_import(self, context: bpy.types.Context) -> None:
    """Adds the import option to the Blender file import menu."""
    # log_to_blender("[MenuFunc] Adding import option to menu", to_blender_editor=False) # Console only - too chatty
    self.layout.operator(SimpGameImport.bl_idname, text="The Simpsons Game (.rws,dff)")

def register() -> None:
    """Registers the addon classes and menu functions."""
    log_to_blender("[Register] Registering import operator and menu function", to_blender_editor=False) # Console only
    bpy.utils.register_class(SimpGameImport)
    bpy.types.TOPBAR_MT_file_import.append(menu_func_import)

def unregister() -> None:
    """Unregisters the addon classes and menu functions."""
    log_to_blender("[Unregister] Unregistering import operator and menu function", to_blender_editor=False) # Console only
    try:
        bpy.utils.unregister_class(SimpGameImport)
    except RuntimeError as e:
        log_to_blender(f"[Unregister] Warning: {e}", to_blender_editor=True) # Log warning to editor
    try:
        bpy.types.TOPBAR_MT_file_import.remove(menu_func_import)
    except Exception as e:
        log_to_blender(f"[Unregister] Warning: {e}", to_blender_editor=True) # Log warning to editor

# This allows the script to be run directly in Blender's text editor
if __name__ == "__main__":
    log_to_blender("[Main] Running as main script. Registering.", to_blender_editor=False) # Console only
    register()
