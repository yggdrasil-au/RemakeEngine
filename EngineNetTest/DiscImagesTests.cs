using EngineNet.Core.DiscImages;

namespace EngineNetTest;

[TestClass]
public sealed class DiscImagesTests {
    /* :: :: Methods :: START :: */

    [TestMethod]
    public void SubStream_ReadsWithinBoundsAndLeavesBaseStreamOpen() {
        byte[] backing = new byte[256];
        for (int i = 0; i < backing.Length; i++) {
            backing[i] = (byte)i;
        }

        using System.IO.MemoryStream ms = new(buffer: backing, writable: false);
        using (SubStream sub = new(baseStream: ms, baseOffset: 100, length: 50)) {
            Assert.AreEqual(expected: 50L, actual: sub.Length);
            Assert.AreEqual(expected: 0L, actual: sub.Position);

            byte[] firstTen = new byte[10];
            int read = sub.Read(buffer: firstTen, offset: 0, count: 10);
            Assert.AreEqual(expected: 10, actual: read);
            Assert.AreEqual(expected: (byte)100, actual: firstTen[0]);
            Assert.AreEqual(expected: (byte)109, actual: firstTen[9]);

            sub.Seek(offset: -5, origin: System.IO.SeekOrigin.End);
            Assert.AreEqual(expected: 45L, actual: sub.Position);

            byte[] tail = new byte[10];
            int tailRead = sub.Read(buffer: tail, offset: 0, count: 10);
            Assert.AreEqual(expected: 5, actual: tailRead);
            Assert.AreEqual(expected: (byte)145, actual: tail[0]);
            Assert.AreEqual(expected: (byte)149, actual: tail[4]);
        }

        // Base stream must remain open and usable after SubStream disposal
        ms.Seek(offset: 0, loc: System.IO.SeekOrigin.Begin);
        Assert.AreEqual(expected: 0, actual: ms.ReadByte());
    }

    [TestMethod]
    public void XdvdfsReader_ParsesNestedDirectoriesAndExtractsFiles() {
        byte[] xisoBytes = BuildSyntheticXiso();
        using System.IO.MemoryStream ms = new(buffer: xisoBytes, writable: false);
        using IIsoReader reader = IsoFactory.OpenStream(stream: ms, leaveOpen: true);

        Assert.AreEqual(expected: "XISO", actual: reader.Format);
        Assert.AreEqual(expected: "MICROSOFT*XBOX*MEDIA", actual: reader.VolumeLabel);
        Assert.IsTrue(condition: reader.FileExists(internalPath: "default.xex"));
        Assert.IsTrue(condition: reader.FileExists(internalPath: "DEFAULT.XEX"));
        Assert.IsTrue(condition: reader.FileExists(internalPath: "movies/intro.sfd"));

        byte[] xexBytes = reader.ReadFileBytes(internalPath: "default.xex");
        Assert.AreEqual(expected: "XEX2_PAYLOAD", actual: System.Text.Encoding.ASCII.GetString(bytes: xexBytes));

        byte[] movieBytes = reader.ReadFileBytes(internalPath: "MOVIES/INTRO.SFD");
        Assert.AreEqual(expected: "SFD_VIDEO_STREAM", actual: System.Text.Encoding.ASCII.GetString(bytes: movieBytes));

        string tempDir = System.IO.Path.Combine(path1: System.IO.Path.GetTempPath(), path2: "RemakeEngine_XisoTest_" + System.Guid.NewGuid().ToString("N"));
        try {
            IsoExtractResult filteredResult = reader.ExtractAll(options: new IsoExtractOptions {
                Destination = tempDir,
                Filter = "*.sfd",
                Overwrite = true
            });

            Assert.IsTrue(condition: filteredResult.Success);
            Assert.AreEqual(expected: 1, actual: filteredResult.FilesExtracted);
            Assert.IsFalse(condition: System.IO.File.Exists(path: System.IO.Path.Combine(path1: tempDir, path2: "default.xex")));
            Assert.IsTrue(condition: System.IO.File.Exists(path: System.IO.Path.Combine(path1: tempDir, path2: "movies", path3: "intro.sfd")));
        } finally {
            if (System.IO.Directory.Exists(path: tempDir)) {
                System.IO.Directory.Delete(path: tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Iso9660Reader_ParsesPs3HybridJolietAndStripsVersionSuffix() {
        byte[] ps3IsoBytes = BuildSyntheticPs3HybridIso();
        using System.IO.MemoryStream ms = new(buffer: ps3IsoBytes, writable: false);
        using IIsoReader reader = IsoFactory.OpenStream(stream: ms, leaveOpen: true);

        Assert.AreEqual(expected: "PS3_HYBRID", actual: reader.Format);
        Assert.AreEqual(expected: "PS3VOLUME", actual: reader.VolumeLabel);
        Assert.IsTrue(condition: reader.FileExists(internalPath: "PS3_GAME/PARAM.SFO"));
        Assert.IsTrue(condition: reader.FileExists(internalPath: "ps3_game/param.sfo"));
        Assert.IsTrue(condition: reader.FileExists(internalPath: "PS3_GAME/USRDIR/simpsons_gameflow.lua"));

        byte[] sfoBytes = reader.ReadFileBytes(internalPath: "PS3_GAME/PARAM.SFO");
        Assert.AreEqual(expected: "\0PSF_BLES00142", actual: System.Text.Encoding.ASCII.GetString(bytes: sfoBytes));

        byte[] luaBytes = reader.ReadFileBytes(internalPath: "PS3_GAME/USRDIR/simpsons_gameflow.lua");
        Assert.AreEqual(expected: "-- gameflow lua", actual: System.Text.Encoding.ASCII.GetString(bytes: luaBytes));
    }

    private static byte[] BuildSyntheticXiso() {
        // Total 37 sectors (37 * 2048 = 75,776 bytes)
        // Sector 32: XDVDFS volume descriptor
        // Sector 33: Root directory table ("default.xex" @ sector 35, "movies" dir @ sector 34)
        // Sector 34: "movies" directory table ("intro.sfd" @ sector 36)
        // Sector 35: "default.xex" payload
        // Sector 36: "intro.sfd" payload
        byte[] iso = new byte[37 * 2048];

        // Sector 32 header
        int vdOffset = 32 * 2048;
        byte[] magic = System.Text.Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");
        System.Buffer.BlockCopy(src: magic, srcOffset: 0, dst: iso, dstOffset: vdOffset, count: magic.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination: System.MemoryExtensions.AsSpan(array: iso, start: vdOffset + 0x14, length: 4), value: 33u);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination: System.MemoryExtensions.AsSpan(array: iso, start: vdOffset + 0x18, length: 4), value: 2048u);
        System.Buffer.BlockCopy(src: magic, srcOffset: 0, dst: iso, dstOffset: vdOffset + 0x7EC, count: magic.Length);

        // Fill sectors 33 and 34 with 0xFF padding
        for (int i = 33 * 2048; i < 35 * 2048; i++) {
            iso[i] = 0xFF;
        }

        byte[] xexPayload = System.Text.Encoding.ASCII.GetBytes("XEX2_PAYLOAD");
        byte[] sfdPayload = System.Text.Encoding.ASCII.GetBytes("SFD_VIDEO_STREAM");

        // Root directory at sector 33
        int rootPos = 33 * 2048;
        rootPos += WriteXdvdfsEntry(buffer: iso, offset: rootPos, rightOffsetWords: 7, startSector: 35u, size: (uint)xexPayload.Length, attrs: 0x20, name: "default.xex");
        WriteXdvdfsEntry(buffer: iso, offset: rootPos, rightOffsetWords: 0, startSector: 34u, size: 2048u, attrs: 0x10, name: "movies");

        // Subdirectory "movies" at sector 34
        int moviesPos = 34 * 2048;
        WriteXdvdfsEntry(buffer: iso, offset: moviesPos, rightOffsetWords: 0, startSector: 36u, size: (uint)sfdPayload.Length, attrs: 0x20, name: "intro.sfd");

        // Payloads at sectors 35 and 36
        System.Buffer.BlockCopy(src: xexPayload, srcOffset: 0, dst: iso, dstOffset: 35 * 2048, count: xexPayload.Length);
        System.Buffer.BlockCopy(src: sfdPayload, srcOffset: 0, dst: iso, dstOffset: 36 * 2048, count: sfdPayload.Length);

        return iso;
    }

    private static int WriteXdvdfsEntry(byte[] buffer, int offset, ushort rightOffsetWords, uint startSector, uint size, byte attrs, string name) {
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(s: name);
        int entryLen = (14 + nameBytes.Length + 3) & ~3;
        for (int i = 0; i < entryLen; i++) {
            buffer[offset + i] = 0;
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination: System.MemoryExtensions.AsSpan(array: buffer, start: offset, length: 2), value: 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination: System.MemoryExtensions.AsSpan(array: buffer, start: offset + 2, length: 2), value: rightOffsetWords);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination: System.MemoryExtensions.AsSpan(array: buffer, start: offset + 4, length: 4), value: startSector);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination: System.MemoryExtensions.AsSpan(array: buffer, start: offset + 8, length: 4), value: size);
        buffer[offset + 12] = attrs;
        buffer[offset + 13] = (byte)nameBytes.Length;
        System.Buffer.BlockCopy(src: nameBytes, srcOffset: 0, dst: buffer, dstOffset: offset + 14, count: nameBytes.Length);
        return entryLen;
    }

    private static byte[] BuildSyntheticPs3HybridIso() {
        // Sectors:
        // 16: Primary Volume Descriptor (PVD)
        // 17: Joliet Supplementary Volume Descriptor (SVD)
        // 18: Volume Descriptor Terminator
        // 20: UDF Bridge NSR03
        // 21: Joliet Root Directory (contains PS3_GAME @ sector 22)
        // 22: PS3_GAME Directory (contains PARAM.SFO @ sector 24, USRDIR @ sector 23)
        // 23: USRDIR Directory (contains simpsons_gameflow.lua @ sector 25)
        // 24: PARAM.SFO data
        // 25: simpsons_gameflow.lua data
        byte[] iso = new byte[26 * 2048];

        // Sector 16: PVD
        int pvd = 16 * 2048;
        iso[pvd] = 1; // Primary
        System.Buffer.BlockCopy(src: System.Text.Encoding.ASCII.GetBytes("CD001"), srcOffset: 0, dst: iso, dstOffset: pvd + 1, count: 5);
        iso[pvd + 6] = 1;
        WritePaddedAscii(buffer: iso, offset: pvd + 8, length: 32, text: "PLAYSTATION");
        WritePaddedAscii(buffer: iso, offset: pvd + 40, length: 32, text: "PS3VOLUME");
        // Root dir record at pvd + 156 pointing to sector 21, length 2048
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination: System.MemoryExtensions.AsSpan(array: iso, start: pvd + 156 + 2, length: 4), value: 0u);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination: System.MemoryExtensions.AsSpan(array: iso, start: pvd + 156 + 10, length: 4), value: 0u);

        // Sector 17: Joliet SVD
        int svd = 17 * 2048;
        iso[svd] = 2; // Supplementary
        System.Buffer.BlockCopy(src: System.Text.Encoding.ASCII.GetBytes("CD001"), srcOffset: 0, dst: iso, dstOffset: svd + 1, count: 5);
        iso[svd + 6] = 1;
        // Joliet UCS-2 Level 3 escape sequence '%/E' (0x25, 0x2F, 0x45) at offset 88
        iso[svd + 88] = 0x25;
        iso[svd + 89] = 0x2F;
        iso[svd + 90] = 0x45;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination: System.MemoryExtensions.AsSpan(array: iso, start: svd + 156 + 2, length: 4), value: 21u);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination: System.MemoryExtensions.AsSpan(array: iso, start: svd + 156 + 10, length: 4), value: 2048u);

        // Sector 18: Terminator
        int term = 18 * 2048;
        iso[term] = 255;
        System.Buffer.BlockCopy(src: System.Text.Encoding.ASCII.GetBytes("CD001"), srcOffset: 0, dst: iso, dstOffset: term + 1, count: 5);

        // Sector 20: UDF NSR03
        int udf = 20 * 2048;
        System.Buffer.BlockCopy(src: System.Text.Encoding.ASCII.GetBytes("NSR03"), srcOffset: 0, dst: iso, dstOffset: udf + 1, count: 5);

        byte[] sfoBytes = System.Text.Encoding.ASCII.GetBytes("\0PSF_BLES00142");
        byte[] luaBytes = System.Text.Encoding.ASCII.GetBytes("-- gameflow lua");

        // Sector 21: Root directory (Joliet UCS-2 BE)
        int s21 = 21 * 2048;
        s21 += WriteIsoDirRecord(buffer: iso, offset: s21, extentSector: 22u, dataLen: 2048u, flags: 0x02, name: "PS3_GAME", isJoliet: true);

        // Sector 22: PS3_GAME directory
        int s22 = 22 * 2048;
        s22 += WriteIsoDirRecord(buffer: iso, offset: s22, extentSector: 24u, dataLen: (uint)sfoBytes.Length, flags: 0x00, name: "PARAM.SFO;1", isJoliet: true);
        WriteIsoDirRecord(buffer: iso, offset: s22, extentSector: 23u, dataLen: 2048u, flags: 0x02, name: "USRDIR", isJoliet: true);

        // Sector 23: USRDIR directory (with mixed-case long filename)
        int s23 = 23 * 2048;
        WriteIsoDirRecord(buffer: iso, offset: s23, extentSector: 25u, dataLen: (uint)luaBytes.Length, flags: 0x00, name: "simpsons_gameflow.lua;1", isJoliet: true);

        // Sector 24 & 25: Payloads
        System.Buffer.BlockCopy(src: sfoBytes, srcOffset: 0, dst: iso, dstOffset: 24 * 2048, count: sfoBytes.Length);
        System.Buffer.BlockCopy(src: luaBytes, srcOffset: 0, dst: iso, dstOffset: 25 * 2048, count: luaBytes.Length);

        return iso;
    }

    private static void WritePaddedAscii(byte[] buffer, int offset, int length, string text) {
        for (int i = 0; i < length; i++) {
            buffer[offset + i] = (byte)' ';
        }
        byte[] ascii = System.Text.Encoding.ASCII.GetBytes(s: text);
        System.Buffer.BlockCopy(src: ascii, srcOffset: 0, dst: buffer, dstOffset: offset, count: System.Math.Min(val1: length, val2: ascii.Length));
    }

    private static int WriteIsoDirRecord(byte[] buffer, int offset, uint extentSector, uint dataLen, byte flags, string name, bool isJoliet) {
        byte[] idBytes = isJoliet
            ? System.Text.Encoding.BigEndianUnicode.GetBytes(s: name)
            : System.Text.Encoding.ASCII.GetBytes(s: name);
        int recordLen = 33 + idBytes.Length;
        if ((recordLen & 1) != 0) {
            recordLen++;
        }

        buffer[offset] = (byte)recordLen;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination: System.MemoryExtensions.AsSpan(array: buffer, start: offset + 2, length: 4), value: extentSector);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination: System.MemoryExtensions.AsSpan(array: buffer, start: offset + 10, length: 4), value: dataLen);
        buffer[offset + 25] = flags;
        buffer[offset + 32] = (byte)idBytes.Length;
        System.Buffer.BlockCopy(src: idBytes, srcOffset: 0, dst: buffer, dstOffset: offset + 33, count: idBytes.Length);
        return recordLen;
    }

    /* :: :: Methods :: END :: */
}

