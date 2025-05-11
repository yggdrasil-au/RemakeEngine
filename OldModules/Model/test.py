import os
import re
import struct # Included for context, not directly used for modification logic here

# Helper function to convert bytes to a space-separated hex string (for logging)
def bytes_to_hex_string(data: bytes) -> str:
    """Converts bytes to a space-separated hex string."""
    return ' '.join(f'{b:02X}' for b in data)

def mark_full_mesh_data_with_pattern(filepath: str) -> bool:
    """
    Finds mesh chunk signatures and subsequent headers, counts, vertex data,
    and face index data, replacing these identified blocks with ASCII '*' characters
    for easier identification in a hex editor.

    Args:
        filepath: The path to the .rws.preinstanced or .dff.preinstanced file.

    Returns:
        True if processing completed, False if an error occurred.
    """
    print(f"Processing file to mark full mesh structure and data: {filepath}")

    # --- Define Structure Lengths and Replacement Markers for the Initial Block ---
    # Based on the importer logic, the initial block after the 12-byte signature is:
    # 1. Mesh Signature: 12 bytes (regex defined below)
    # 2. Skip/Padding: 4 bytes
    # 3. Header (FaceDataOff, MeshDataSize): 8 bytes
    # 4. Skip/Padding: 20 bytes (0x14)
    # 5. Counts (mDataTableCount, mDataSubCount): 8 bytes
    # Total initial marked block size = 12 + 4 + 8 + 20 + 8 = 52 bytes

    mesh_signature_regex = re.compile(b"\x33\xEA\x00\x00....\x2D\x00\x02\x1C", re.DOTALL)
    SIG_LEN = 12

    SKIP1_LEN = 4
    HEADER_LEN = 8
    SKIP2_LEN = 20 # 0x14 in hex
    COUNTS_LEN = 8

    MARKED_INITIAL_BLOCK_LEN = SIG_LEN + SKIP1_LEN + HEADER_LEN + SKIP2_LEN + COUNTS_LEN

    # Define replacement texts for the initial block (MUST MATCH EXACT LENGTHS)
    rep_sig_text = "---MESH---__" # 12 characters
    rep_skip1_text = "PAD1"        # 4 characters
    rep_header_text = "FDO_MDS_"    # 8 characters (FaceDataOff, MeshDataSize)
    rep_skip2_text = "*" * SKIP2_LEN # 20 asterisks for padding
    rep_counts_text = "DT_ST_CT"    # 8 characters (mDataTableCount, mDataSubCount)


    # Encode replacement texts to bytes and verify lengths
    try:
        rep_sig_bytes = rep_sig_text.encode('ascii')
        rep_skip1_bytes = rep_skip1_text.encode('ascii')
        rep_header_bytes = rep_header_text.encode('ascii')
        rep_skip2_bytes = rep_skip2_text.encode('ascii')
        rep_counts_bytes = rep_counts_text.encode('ascii')

        # Verify that the encoded byte lengths match the required lengths
        if (len(rep_sig_bytes) != SIG_LEN or
            len(rep_skip1_bytes) != SKIP1_LEN or
            len(rep_header_bytes) != HEADER_LEN or
            len(rep_skip2_bytes) != SKIP2_LEN or
            len(rep_counts_bytes) != COUNTS_LEN):
            print("Error: One or more replacement text lengths for the initial block do not match the required binary lengths.")
            print(f"  Signature: {len(rep_sig_bytes)}/{SIG_LEN} bytes")
            print(f"  Skip 1:    {len(rep_skip1_bytes)}/{SKIP1_LEN} bytes")
            print(f"  Header:    {len(rep_header_bytes)}/{HEADER_LEN} bytes")
            print(f"  Skip 2:    {len(rep_skip2_bytes)}/{SKIP2_LEN} bytes")
            print(f"  Counts:    {len(rep_counts_bytes)}/{COUNTS_LEN} bytes")
            print("Cannot proceed.")
            return False

    except Exception as e:
        print(f"Error encoding replacement texts to ASCII: {e}")
        return False

    print(f"Using replacement markers for initial block:")
    print(f"  Signature ({SIG_LEN} bytes): '{rep_sig_text}'")
    print(f"  Skip 1    ({SKIP1_LEN} bytes):   '{rep_skip1_text}'")
    print(f"  Header    ({HEADER_LEN} bytes):  '{rep_header_text}'")
    print(f"  Skip 2    ({SKIP2_LEN} bytes):   '{rep_skip2_text}'")
    print(f"  Counts    ({COUNTS_LEN} bytes):  '{rep_counts_text}'")
    print(f"  Total initial block size per structure: {MARKED_INITIAL_BLOCK_LEN} bytes.")


    # Read the entire file content into a mutable bytearray
    # This allows us to modify the bytes in memory efficiently.
    try:
        with open(filepath, "rb") as f:
            byte_array = bytearray(f.read())
    except FileNotFoundError:
        print(f"Error: File not found at {filepath}")
        return False
    except Exception as e:
        print(f"Error reading file {filepath}: {e}")
        return False

    print(f"Read {len(byte_array)} bytes into memory.")
    file_size = len(byte_array)

    initial_blocks_marked = 0
    total_signatures_found = 0
    marked_vertex_data_bytes = 0
    marked_face_data_bytes = 0
    total_submeshes_processed = 0

    # Find all occurrences of the mesh signature in the bytearray content.
    # We search on a 'bytes' view because the 're' module works with 'bytes' or strings.
    print("Searching for mesh signatures and marking full structure and data...")
    # Iterate through the bytearray to find signature matches
    for match in mesh_signature_regex.finditer(bytes(byte_array)):
        total_signatures_found += 1
        signature_start_index = match.start() # Index of the start of the 12-byte signature

        print(f"\nFound mesh structure starting at byte index: {signature_start_index} (0x{signature_start_index:X})")

        # --- Mark the Initial Header Block (52 bytes) ---
        # Check if there's enough space after the signature for the full initial block
        if signature_start_index + MARKED_INITIAL_BLOCK_LEN > file_size:
             print(f"Warning: Not enough space in the file to mark the full {MARKED_INITIAL_BLOCK_LEN}-byte initial block ")
             print(f"         starting at offset {signature_start_index} (file size {file_size}). ")
             print("         Skipping marking for this structure.")
             continue # Skip marking this entire structure block, move to next signature match

        try:
            # Perform the replacements for the initial block sequentially
            current_replace_index_abs = signature_start_index # Absolute index in the bytearray

            # 1. Replace Signature (12 bytes)
            byte_array[current_replace_index_abs : current_replace_index_abs + SIG_LEN] = rep_sig_bytes
            current_replace_index_abs += SIG_LEN

            # 2. Replace Skip 1 (4 bytes) immediately after signature
            byte_array[current_replace_index_abs : current_replace_index_abs + SKIP1_LEN] = rep_skip1_bytes
            current_replace_index_abs += SKIP1_LEN

            # 3. Replace Header (8 bytes) after Skip 1
            byte_array[current_replace_index_abs : current_replace_index_abs + HEADER_LEN] = rep_header_bytes
            current_replace_index_abs += HEADER_LEN

            # 4. Replace Skip 2 (20 bytes) after Header
            byte_array[current_replace_index_abs : current_replace_index_abs + SKIP2_LEN] = rep_skip2_bytes
            current_replace_index_abs += SKIP2_LEN

            # 5. Replace Counts (8 bytes) after Skip 2
            byte_array[current_replace_index_abs : current_replace_index_abs + COUNTS_LEN] = rep_counts_bytes
            # current_replace_index_abs is now at signature_start_index + MARKED_INITIAL_BLOCK_LEN

            initial_blocks_marked += 1
            # print(f"  Successfully marked initial {MARKED_INITIAL_BLOCK_LEN} bytes.")

            # --- Parse Header Data for locating Sub-meshes ---
            # We need to re-read the relevant header values from the bytearray
            # (since we just overwrote them with text, we can't read the original binary anymore!)
            # This requires knowing the *structure* of the original binary data *before* it was marked.

            # Get the original header bytes from the bytearray *before* they were potentially overwritten.
            # (Alternatively, we could store these values during the first pass if we only marked on the second pass,
            # or re-read the original file temporarily. Let's assume we parse from the bytearray now.)

            # Need the index where the header bytes were originally located
            original_header_index_in_bytearray = signature_start_index + SIG_LEN + SKIP1_LEN # After signature + skip1

            # Need the index where the counts bytes were originally located
            original_counts_index_in_bytearray = original_header_index_in_bytearray + HEADER_LEN + SKIP2_LEN # After header + skip2


            # Read header values from the bytearray at their original locations
            # Note: If the file is already marked by a previous run, these reads
            # will get the ASCII text bytes, which will cause errors when
            # int.from_bytes is called. The user must run on an original file.
            try:
                 face_data_off_orig = int.from_bytes(byte_array[original_header_index_in_bytearray : original_header_index_in_bytearray + 4], byteorder='little')
                 # mesh_data_size = int.from_bytes(byte_array[original_header_index_in_bytearray + 4 : original_header_index_in_bytearray + 8], byteorder='little') # Not used

                 mdata_table_count = int.from_bytes(byte_array[original_counts_index_in_bytearray : original_counts_index_in_bytearray + 4], byteorder='big')
                 mdata_sub_count = int.from_bytes(byte_array[original_counts_index_in_bytearray + 4 : original_counts_index_in_bytearray + 8], byteorder='big')

                 # Calculate the reference point (MeshChunkStart) - index after FaceDataOff/MeshDataSize header
                 mesh_chunk_start_ref_index = signature_start_index + SIG_LEN + SKIP1_LEN + HEADER_LEN

                 # Calculate the start index of the sub-mesh parameter offset table
                 # It's after the counts + mDataTableCount * 8 bytes (based on importer seeking)
                 mdata_sub_table_start_index = original_counts_index_in_bytearray + COUNTS_LEN + mdata_table_count * 8

                 # print(f"  Parsed FaceDataOff: {face_data_off_orig}, mDataSubCount: {mdata_sub_count}")

            except Exception as e:
                 print(f"  Error parsing header/counts values from bytearray at 0x{signature_start_index:X}. File might be corrupted or already marked incorrectly: {e}")
                 continue # Skip processing sub-meshes for this chunk

            # --- Process each Sub-mesh within this chunk ---
            for i in range(mdata_sub_count):
                total_submeshes_processed += 1
                # print(f"  Processing sub-mesh {i}...")

                # Calculate the expected start index of this sub-mesh entry in the table (each entry is 0xC)
                sub_mesh_entry_offset_index = mdata_sub_table_start_index + i * 0xC + 8 # Index where the offset value is

                # Check if the index for reading the offset is within bounds
                #if sub_mesh_entry_offset_index < 0 or sub_mesh_entry_offset_index + 4 > file_size:
                #     print(f"    Warning: Sub-mesh entry offset index 0x{sub_mesh_entry_offset_index:X} out of bounds. Skipping sub-mesh {i}.")
                #     continue

                try:
                    # Read the offset value (big-endian) from the bytearray
                    # This offset points to the sub-mesh parameter block relative to MeshChunkStart + 0xC
                    offset_value = int.from_bytes(byte_array[sub_mesh_entry_offset_index : sub_mesh_entry_offset_index + 4], byteorder='big')

                    # Calculate the absolute bytearray index where the sub-mesh parameters block starts
                    sub_mesh_params_index = offset_value + mesh_chunk_start_ref_index + 0xC

                    # --- Read relevant Sub-mesh Parameters from the block ---
                    # We need VertCountDataOff and VertexStart offset from this block
                    # These are located at specific offsets relative to sub_mesh_params_index

                    # VertCountDataOff is at offset 0 relative to sub_mesh_params_index
                    vert_count_data_off_index_in_params = sub_mesh_params_index
                    if vert_count_data_off_index_in_params < 0 or vert_count_data_off_index_in_params + 4 > file_size:
                         print(f"    Warning: VertCountDataOff index 0x{vert_count_data_off_index_in_params:X} out of bounds. Skipping sub-mesh {i}.")
                         continue
                    vert_count_data_off_orig = int.from_bytes(byte_array[vert_count_data_off_index_in_params : vert_count_data_off_index_in_params + 4], byteorder='big')

                    # VertexStart offset is at offset 8 + 8 = 16 bytes relative to sub_mesh_params_index
                    # (Skip 8 bytes for unknown + 8 bytes for normals offset/size)
                    vertex_start_off_index_in_params = sub_mesh_params_index + 16
                    if vertex_start_off_index_in_params < 0 or vertex_start_off_index_in_params + 4 > file_size:
                         print(f"    Warning: VertexStart offset index 0x{vertex_start_off_index_in_params:X} out of bounds. Skipping sub-mesh {i}.")
                         continue
                    vertex_start_orig_offset = int.from_bytes(byte_array[vertex_start_off_index_in_params : vertex_start_off_index_in_params + 4], byteorder='big')

                    # FaceCount (NumIndices/2?) is at offset 16 + 4 + 0x14 = 36 bytes relative to sub_mesh_params_index
                    # (After VertexStart offset + 0x14 skip)
                    face_count_index_in_params = sub_mesh_params_index + 36
                    if face_count_index_in_params < 0 or face_count_index_in_params + 4 > file_size:
                        print(f"    Warning: FaceCount index 0x{face_count_index_in_params:X} out of bounds. Skipping sub-mesh {i}.")
                        continue
                    face_count_value = int.from_bytes(byte_array[face_count_index_in_params : face_count_index_in_params + 4], byteorder='big')


                     # FaceStart offset is at offset 36 + 4 + 4 = 44 bytes relative to sub_mesh_params_index
                    # (After FaceCount + 4 skip)
                    face_start_off_index_in_params = sub_mesh_params_index + 44
                    if face_start_off_index_in_params < 0 or face_start_off_index_in_params + 4 > file_size:
                        print(f"    Warning: FaceStart offset index 0x{face_start_off_index_in_params:X} out of bounds. Skipping sub-mesh {i}.")
                        continue
                    face_start_orig_offset = int.from_bytes(byte_array[face_start_off_index_in_params : face_start_off_index_in_params + 4], byteorder='big')


                    # Need VertChunkSize and VertChunkTotalSize from the location pointed to by VertCountDataOff
                    vert_count_data_info_index = vert_count_data_off_orig + mesh_chunk_start_ref_index
                    #if vert_count_data_info_index < 0 or vert_count_data_info_index + 8 > file_size:
                    #     print(f"    Warning: Vert count info index 0x{vert_count_data_info_index:X} out of bounds. Skipping sub-mesh {i}.")
                    #     continue
                    vert_chunk_total_size = int.from_bytes(byte_array[vert_count_data_info_index : vert_count_data_info_index + 4], byteorder='big')
                    vert_chunk_size = int.from_bytes(byte_array[vert_count_data_info_index + 4 : vert_count_data_info_index + 8], byteorder='big')

                    # Calculate the absolute bytearray index where the actual vertex data starts for this sub-mesh
                    vertex_data_start_index = vertex_start_orig_offset + face_data_off_orig + mesh_chunk_start_ref_index

                     # Calculate the absolute bytearray index where the face index data starts for this sub-mesh
                    face_data_start_index = face_start_orig_offset + face_data_off_orig + mesh_chunk_start_ref_index


                    # print(f"    Sub-mesh {i} params: VertCountDataOff=0x{vert_count_data_off_orig:X}, VertexStartOff=0x{vertex_start_orig_offset:X}, FaceCount={face_count_value}, FaceStartOff=0x{face_start_orig_offset:X}")
                    # print(f"    Vert Info: Total Size={vert_chunk_total_size}, Chunk Size={vert_chunk_size}")
                    # print(f"    Calculated Data Start Indices: Vertex=0x{vertex_data_start_index:X}, Face=0x{face_data_start_index:X}")


                except Exception as e:
                     print(f"  Error reading sub-mesh {i} parameters/info from bytearray: {e}")
                     continue # Skip to the next sub-mesh

                # --- Mark Vertex Data ---
                if vert_count > 0 and vert_chunk_size > 0 and vertex_data_start_index >= 0:
                    vertex_data_size_bytes = vert_count * vert_chunk_size
                    vertex_data_end_index = vertex_data_start_index + vertex_data_size_bytes

                    if vertex_data_end_index <= file_size:
                        print(f"    Marking {vertex_data_size_bytes} bytes of vertex data starting at 0x{vertex_data_start_index:X}")
                        try:
                            # Create a pattern of '*' bytes matching the vertex data size
                            rep_vertex_bytes = b'*' * vertex_data_size_bytes
                            byte_array[vertex_data_start_index : vertex_data_end_index] = rep_vertex_bytes
                            marked_vertex_data_bytes += vertex_data_size_bytes # Track bytes marked
                        except Exception as e:
                             print(f"    Error marking vertex data starting at 0x{vertex_data_start_index:X}: {e}")
                    else:
                        print(f"    Warning: Vertex data block calculated to end out of bounds (0x{vertex_data_end_index:X}) for file size {file_size}.")
                        print(f"    Block starts at 0x{vertex_data_start_index:X}, size {vertex_data_size_bytes}. Skipping marking.")
                elif vertex_data_start_index < 0:
                     print(f"    Skipping vertex data marking for sub-mesh {i}: Vertex data start index is negative.")
                else:
                    print(f"    Skipping vertex data marking for sub-mesh {i}: VertCount or VertChunkSize is zero or negative.")


                # --- Mark Face Index Data (Strips) ---
                # FaceCountValue is num_indices / 2, so the total index data size is face_count_value * 2 bytes
                if face_count_value > 0 and face_data_start_index >= 0:
                    face_data_size_bytes = face_count_value * 2
                    face_data_end_index = face_data_start_index + face_data_size_bytes

                    if face_data_end_index <= file_size:
                        print(f"    Marking {face_data_size_bytes} bytes of face index data starting at 0x{face_data_start_index:X}")
                        try:
                            # Create a pattern of '*' bytes matching the face data size
                            rep_face_bytes = b'*' * face_data_size_bytes
                            byte_array[face_data_start_index : face_data_end_index] = rep_face_bytes
                            marked_face_data_bytes += face_data_size_bytes # Track bytes marked
                        except Exception as e:
                             print(f"    Error marking face data starting at 0x{face_data_start_index:X}: {e}")
                    else:
                        print(f"    Warning: Face data block calculated to end out of bounds (0x{face_data_end_index:X}) for file size {file_size}.")
                        print(f"    Block starts at 0x{face_data_start_index:X}, size {face_data_size_bytes}. Skipping marking.")
                elif face_data_start_index < 0:
                    print(f"    Skipping face data marking for sub-mesh {i}: Face data start index is negative.")
                else:
                    print(f"    Skipping face data marking for sub-mesh {i}: FaceCount is zero or negative.")

        except Exception as e:
             # Catch potential errors during the sub-mesh entry processing itself
             print(f"Error processing sub-mesh entry {i}: {e}")
             continue # Continue to the next sub-mesh entry


    print(f"\nFinished processing {total_signatures_found} potential mesh structures.")
    print(f"Marked {initial_blocks_marked} initial structure blocks ({MARKED_INITIAL_BLOCK_LEN} bytes each).")
    print(f"Processed data for {total_submeshes_processed} sub-meshes.")
    print(f"Marked {marked_vertex_data_bytes} bytes of vertex data with '*'.")
    print(f"Marked {marked_face_data_bytes} bytes of face index data with '*'.")


    # If no structures were successfully marked, it might indicate an issue
    # or that the file doesn't contain the expected pattern. Avoid writing.
    if initial_blocks_marked == 0 and total_signatures_found > 0:
        print("Warning: Signatures were found, but no initial structure blocks could be fully marked.")
        print("This might indicate an unexpected file structure or truncated blocks.")
        print("No changes were written to the file.")
        return False # Indicate failure to mark expected structure

    if total_signatures_found == 0:
         print("No mesh signatures found based on the expected pattern.")
         print("The file may not be a standard .preinstanced mesh file or the signature is different.")
         print("No changes were written to the file.")
         return True # No error occurred, but nothing was found or modified.


    # Write the modified bytearray content back to the original file path.
    # 'wb' mode clears the existing file content before writing,
    # effectively replacing the original file with our modified bytearray.
    try:
        with open(filepath, "wb") as f:
            f.write(byte_array)
        print(f"\nSuccessfully wrote modified data back to {filepath}")
        print("\nYou can now open this file in a hex editor to view the marked structures and data.")
        print("Look for the ASCII markers for headers and large blocks of '*' for mesh data.")
        return True
    except Exception as e:
        print(f"Error writing file {filepath}: {e}")
        print("The file may be corrupted or incomplete.")
        return False

# --- Main execution block ---
if __name__ == "__main__":
    print("--- The Simpsons Game .preinstanced Full Mesh Structure & Data Marker ---")
    print("This script finds specific mesh structure patterns (signature, headers, counts)")
    print("AND the subsequent vertex and face index data blocks.")
    print("It replaces these identified blocks *in the file* with fixed-length ASCII text markers")
    print("or '*' patterns for easier identification in a hex editor.")
    print("")
    print("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")
    print("!!                              WARNING:                              !!")
    print("!! This script will DIRECTLY MODIFY AND OVERWRITE the original file.  !!")
    print("!! It relies heavily on the assumed binary structure after the        !!")
    print("!! signature AND the structure/offsets within sub-mesh parameter      !!")
    print("!! blocks to locate vertex and face data. If the structure varies in  !!")
    print("!! your file, this WILL LIKELY CORRUPT your file.                   !!")
    print("!!                                                                    !!")
    print("!!             >>>      MAKE A BACKUP OF YOUR FILE FIRST!     <<<    !!")
    print("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")
    print("-" * 70)

    input_filepath = input("Enter the full path to the binary .preinstanced file: ").strip().strip('"') # Handle potential quotes

    # Basic check if the file exists
    if not os.path.exists(input_filepath):
        print("Error: Input file not found at the specified path.")
    else:
        # Optional: Warn if the file extension isn't typical, but allow proceeding
        if not input_filepath.lower().endswith(('.preinstanced', '.rws', '.dff')):
             print("Note: The file extension is not typical (.preinstanced, .rws, or .dff).")
             print("Ensure this is the correct type of binary file before proceeding.")

        # Crucial confirmation steps
        confirm_backup = input("\nHave you confirmed you have a BACKUP of the original file? Type 'YES' to proceed: ")
        if confirm_backup.upper() != 'YES':
             print("Operation cancelled. Please create a backup before attempting modification.")
        else:
            confirm_overwrite = input(f"Are you absolutely sure you want to OVERWRITE this file:\n-> {input_filepath}\nType 'YES' to confirm file modification: ")
            if confirm_overwrite.upper() == 'YES':
                mark_full_mesh_data_with_pattern(input_filepath)
            else:
                print("Operation cancelled by user confirmation.")