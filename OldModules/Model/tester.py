import struct
import re
import os
import math

def debug_nan_uvs_in_file(filepath):
    """
    Analyzes a Simpsons Game .preinstanced file to find and report
    NaN values in UV coordinates, providing context, and also reports
    the total count of valid and NaN UV pairs encountered.
    """
    if not os.path.exists(filepath):
        print(f"Error: File not found at {filepath}")
        return

    print(f"== Analyzing File: {filepath} for NaN UVs ==")
    nan_uv_count = 0
    valid_uv_count = 0
    total_uv_pairs_checked = 0

    try:
        with open(filepath, "rb") as cur_file:
            tmpRead = cur_file.read()
            # Use the same regex as the importer to find mesh chunks
            mshBytes = re.compile(b"\x33\xEA\x00\x00....\x2D\x00\x02\x1C", re.DOTALL)
            mesh_iter = 0

            for x in mshBytes.finditer(tmpRead):
                print(f"\n--- Found Mesh Chunk {mesh_iter} at offset {x.start():#x} ---")
                cur_file.seek(x.end() + 4) # Go past the matched pattern + 4 unknown bytes

                # Read offsets and sizes like in the importer
                FaceDataOff = int.from_bytes(cur_file.read(4), byteorder='little')
                MeshDataSize = int.from_bytes(cur_file.read(4), byteorder='little')
                MeshChunkStart = cur_file.tell() # This is the base offset for this chunk's relative offsets
                print(f"  MeshChunkStart Offset: {MeshChunkStart:#x}")
                print(f"  FaceData Relative Offset: {FaceDataOff:#x}")
                print(f"  MeshData Size: {MeshDataSize:#x}")

                # Navigate to metadata table counts
                cur_file.seek(MeshChunkStart + 0x14) # Offset relative to MeshChunkStart
                mDataTableCount = int.from_bytes(cur_file.read(4), byteorder='big')
                mDataSubCount = int.from_bytes(cur_file.read(4), byteorder='big')
                print(f"  Meta Data Table Count: {mDataTableCount}")
                print(f"  Meta Data Sub Count (Submeshes?): {mDataSubCount}")

                # Skip the first meta table data (importer does this too)
                cur_file.seek(mDataTableCount * 8, 1) # Each entry seems skipped by 8 bytes (4 read, 4 skipped) in importer

                mDataSubStart = cur_file.tell()
                print(f"  Sub-Chunk Definition Table Start: {mDataSubStart:#x}")


                # Iterate through the sub-chunks (potential individual mesh parts)
                for i in range(mDataSubCount):
                    print(f"\n  -- Processing Sub-Chunk {i} --")
                    # Get offset to this sub-chunk's detail structure
                    cur_file.seek(mDataSubStart + i * 0xC + 8) # Go to the offset field within the sub-chunk definition
                    offset_to_detail = int.from_bytes(cur_file.read(4), byteorder='big')
                    detail_struct_addr = offset_to_detail + MeshChunkStart
                    print(f"    Detail Structure Relative Offset: {offset_to_detail:#x}")
                    print(f"    Detail Structure Absolute Address: {detail_struct_addr:#x}")

                    # Go to the detail structure and read vertex info pointer
                    cur_file.seek(detail_struct_addr + 0xC)
                    VertCountDataOff_rel = int.from_bytes(cur_file.read(4), byteorder='big')
                    VertCountDataOff_abs = VertCountDataOff_rel + MeshChunkStart
                    print(f"    Vertex Count Info Relative Offset: {VertCountDataOff_rel:#x}")
                    print(f"    Vertex Count Info Absolute Address: {VertCountDataOff_abs:#x}")


                    # Go to the vertex count info structure
                    cur_file.seek(VertCountDataOff_abs)
                    VertChunkTotalSize = int.from_bytes(cur_file.read(4), byteorder='big')
                    VertChunkSize = int.from_bytes(cur_file.read(4), byteorder='big')

                    if VertChunkSize == 0:
                        print(f"    WARNING: Vertex Chunk Size is 0 for sub-chunk {i}. Skipping.")
                        continue # Avoid division by zero

                    VertCount = int(VertChunkTotalSize / VertChunkSize)
                    cur_file.seek(8, 1) # Skip unknown 8 bytes
                    VertexStart_rel = int.from_bytes(cur_file.read(4), byteorder='big')
                    # NOTE: FaceDataOff seems to be an *additional* base offset for vertex/face data regions
                    VertexStart_abs = VertexStart_rel + FaceDataOff + MeshChunkStart
                    print(f"    Vertex Data Relative Offset (to FaceDataOff+MeshChunkStart): {VertexStart_rel:#x}")
                    print(f"    Vertex Data Absolute Address: {VertexStart_abs:#x}")
                    print(f"    Vertex Count: {VertCount}")
                    print(f"    Vertex Chunk Size (Stride): {VertChunkSize}")
                    print(f"    Vertex Total Size: {VertChunkTotalSize}")

                    # --- Vertex Data Analysis ---
                    for v in range(VertCount):
                        vertex_base_addr = VertexStart_abs + v * VertChunkSize

                        # --- Read Position (for context - optional print) ---
                        pos_addr = vertex_base_addr
                        cur_file.seek(pos_addr)
                        try:
                            pos_bytes = cur_file.read(12) # Read 3 floats
                            pos_data = struct.unpack('>fff', pos_bytes)
                            # print(f"      Vert {v} Pos @ {pos_addr:#x}: {pos_data}") # Uncomment for extreme detail
                        except struct.error:
                             # print(f"      Vert {v} Pos @ {pos_addr:#x}: Error reading/unpacking position data.")
                             pos_data = (0,0,0)
                        except Exception as e:
                             # print(f"      Vert {v} Pos @ {pos_addr:#x}: Unexpected error reading position: {e}")
                             pos_data = (0,0,0)


                        # --- Read UVs (The target of our debugging) ---
                        uv_offset_in_chunk = VertChunkSize - 16 # Importer's assumption
                        uv_addr = vertex_base_addr + uv_offset_in_chunk

                        # Check if the calculated address makes sense
                        if uv_offset_in_chunk < 0:
                             # print(f"      ERROR: Vert {v} - Negative UV offset calculated ({uv_offset_in_chunk}). VertChunkSize ({VertChunkSize}) might be too small. Skipping UV check.")
                             continue

                        cur_file.seek(uv_addr)
                        try:
                            raw_uv_bytes = cur_file.read(8) # Read 2 floats (8 bytes)
                            if len(raw_uv_bytes) < 8:
                                 print(f"      ERROR: Vert {v} UV @ {uv_addr:#x}: Tried to read 8 bytes, but got only {len(raw_uv_bytes)}. Reached EOF prematurely? Skipping UV check.")
                                 continue

                            uv_data = struct.unpack('>ff', raw_uv_bytes)
                            total_uv_pairs_checked += 1

                            # --- THE NAN CHECK ---
                            is_nan_u = math.isnan(uv_data[0])
                            is_nan_v = math.isnan(uv_data[1])

                            if is_nan_u or is_nan_v:
                                nan_uv_count += 1
                                print(f"      !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")
                                print(f"      !!! NaN DETECTED at Vertex Index: {v} in Sub-Chunk {i} !!!")
                                print(f"      !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")
                                print(f"      Vertex Base Address: {vertex_base_addr:#x}")
                                print(f"      Calculated UV Address: {uv_addr:#x} (Base + VertChunkSize[{VertChunkSize}] - 16)")
                                print(f"      Raw Bytes Read @ {uv_addr:#x}: {raw_uv_bytes.hex(' ')}")
                                print(f"      Unpacked UV Data: {uv_data} {'<- NaN U' if is_nan_u else ''} {'<- NaN V' if is_nan_v else ''}")

                                # --- Provide Context Bytes ---
                                context_bytes_start = max(vertex_base_addr, uv_addr - 16) # Read a bit before UVs
                                context_bytes_end = min(VertexStart_abs + VertChunkTotalSize, uv_addr + 16) # Read a bit after UVs
                                context_size = context_bytes_end - context_bytes_start

                                if context_size > 0:
                                    cur_file.seek(context_bytes_start)
                                    context_bytes = cur_file.read(context_size)
                                    print(f"      Context Bytes [{context_bytes_start:#x} to {context_bytes_end:#x}]:")
                                    hex_lines = []
                                    bytes_per_line = 16
                                    for j in range(0, len(context_bytes), bytes_per_line):
                                        line_bytes = context_bytes[j:j+bytes_per_line]
                                        hex_string = line_bytes.hex(' ')
                                        ascii_string = "".join(chr(b) if 32 <= b <= 126 else '.' for b in line_bytes)
                                        addr_string = f"{(context_bytes_start + j):#010x}"
                                        # Highlight the actual UV bytes within the context
                                        uv_start_in_context = uv_addr - context_bytes_start
                                        uv_end_in_context = uv_start_in_context + 8
                                        if j <= uv_start_in_context < j + bytes_per_line:
                                            # This line contains the start of the UV data
                                            highlight_start_idx = (uv_start_in_context % bytes_per_line) * 3 # *3 for hex digits + space
                                            highlight_end_idx = min((uv_end_in_context - context_bytes_start - j) * 3, bytes_per_line*3)

                                            marked_hex = hex_string[:highlight_start_idx] + '[' + hex_string[highlight_start_idx:highlight_start_idx + min(8*3, len(hex_string) - highlight_start_idx)].rstrip() + ']' + hex_string[highlight_start_idx + min(8*3, len(hex_string) - highlight_start_idx):]

                                            # Clean up extra space if highlight is at the end
                                            if marked_hex.endswith('] '):
                                                 marked_hex = marked_hex[:-2] + ']'
                                            elif marked_hex.endswith(']'):
                                                 pass # Correctly ended

                                            hex_lines.append(f"        {addr_string}: {marked_hex:<{bytes_per_line*3}} {ascii_string}")

                                        elif uv_start_in_context < j and uv_end_in_context > j:
                                             # This line is fully within the highlighted UV data
                                             marked_hex = '[' + hex_string.rstrip() + ']'
                                             hex_lines.append(f"        {addr_string}: {marked_hex:<{bytes_per_line*3}} {ascii_string}")

                                        elif uv_start_in_context < j and j <= uv_end_in_context -1 < j+bytes_per_line:
                                             # This line contains the end of the UV data (started on previous line)
                                             highlight_end_idx = (uv_end_in_context - context_bytes_start - j) * 3
                                             marked_hex = '[' + hex_string[:highlight_end_idx].rstrip() + ']' + hex_string[highlight_end_idx:].rstrip()
                                             hex_lines.append(f"        {addr_string}: {marked_hex:<{bytes_per_line*3}} {ascii_string}")


                                        else:
                                            hex_lines.append(f"        {addr_string}: {hex_string:<{bytes_per_line*3}} {ascii_string}")


                                    print("\n".join(hex_lines))
                                else:
                                    print(f"        Could not read context bytes.")
                                print(f"      ----------------------------------------------------------")
                            else:
                                valid_uv_count += 1


                        except struct.error as e:
                            print(f"      ERROR: Vert {v} UV @ {uv_addr:#x}: Failed to unpack 8 bytes as '>ff'. Error: {e}. Raw bytes: {raw_uv_bytes.hex(' ') if 'raw_uv_bytes' in locals() else 'N/A'}")
                        except Exception as e:
                            print(f"      ERROR: Vert {v} UV @ {uv_addr:#x}: Unexpected error reading/processing UVs: {e}")

                mesh_iter += 1 # Increment mesh chunk counter

    except FileNotFoundError:
        # Already handled at the start, but good practice
        pass
    except Exception as e:
        print(f"\nAn unexpected error occurred during analysis: {e}")
        import traceback
        traceback.print_exc() # Print full traceback for unexpected errors

    print(f"\n== Analysis Complete ==")
    print(f"Total UV pairs checked: {total_uv_pairs_checked}")
    print(f"Valid UV pairs found: {valid_uv_count}")
    print(f"NaN UV pairs found: {nan_uv_count}")


# --- How to Use ---
if __name__ == "__main__":
    file_to_debug = r"A:\TMP_TSG_LNKS\_UNKNOWN_MAP\3278e7a204809611b30031b5e344aeae_preinstanced\opt_model1.rws.PS3.preinstanced"
    # Replace with the actual path to your .preinstanced file

    debug_nan_uvs_in_file(file_to_debug)