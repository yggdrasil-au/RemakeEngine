namespace EngineNetTest;

using System.Buffers.Binary;
using System.Reflection;
using System.Text;

[TestClass]
public sealed class TextureSegmentProcessorTests {
    [TestMethod]
    public void ProcessSegment_PrefersCanonicalMetadataOverEarlierFalseMarker() {
        byte[] segmentData = CreateSegmentData(metadataOffset: 0x50, includeDecoyMarker: true);
        string outputDirectory = CreateOutputDirectory();

        try {
            int exported = ProcessSegment(segmentData: segmentData, outputDirectory: outputDirectory);

            Assert.AreEqual(expected: 1, actual: exported);
            Assert.IsTrue(condition: File.Exists(path: Path.Combine(path1: outputDirectory, path2: "test_texture.dds")));
        } finally {
            Directory.Delete(path: outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ProcessSegment_FallsBackToMetadataScanForNoncanonicalHeader() {
        byte[] segmentData = CreateSegmentData(metadataOffset: 0x60, includeDecoyMarker: false);
        string outputDirectory = CreateOutputDirectory();

        try {
            int exported = ProcessSegment(segmentData: segmentData, outputDirectory: outputDirectory);

            Assert.AreEqual(expected: 1, actual: exported);
            Assert.IsTrue(condition: File.Exists(path: Path.Combine(path1: outputDirectory, path2: "test_texture.dds")));
        } finally {
            Directory.Delete(path: outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ProcessSegment_SuffixesDuplicateNamesAcrossSegments() {
        string outputDirectory = CreateOutputDirectory();
        HashSet<string> exportedFileNames = new(comparer: StringComparer.OrdinalIgnoreCase);

        try {
            int firstExport = ProcessSegment(
                segmentData: CreateSegmentData(metadataOffset: 0x50, includeDecoyMarker: false),
                outputDirectory: outputDirectory,
                exportedFileNames: exportedFileNames
            );
            int secondExport = ProcessSegment(
                segmentData: CreateSegmentData(metadataOffset: 0x50, includeDecoyMarker: false),
                outputDirectory: outputDirectory,
                exportedFileNames: exportedFileNames,
                segmentStartOffset: 0x100
            );

            Assert.AreEqual(expected: 1, actual: firstExport);
            Assert.AreEqual(expected: 1, actual: secondExport);
            Assert.IsTrue(condition: File.Exists(path: Path.Combine(path1: outputDirectory, path2: "test_texture.dds")));
            Assert.IsTrue(condition: File.Exists(path: Path.Combine(path1: outputDirectory, path2: "test_texture_0x100.dds")));
        } finally {
            Directory.Delete(path: outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void UnswizzleData_RoundTripsSquareTexture() {
        AssertRgbaTextureExport(width: 8, height: 8);
    }

    [TestMethod]
    public void UnswizzleData_RoundTripsRectangularTexture() {
        AssertRgbaTextureExport(width: 256, height: 64);
    }

    private static void AssertRgbaTextureExport(int width, int height) {
        Assembly formatsAssembly = Assembly.Load(assemblyString: "EngineNet.Formats");
        Type utilType = formatsAssembly.GetType(name: "EngineNet.GameFormats.txd.utils.Util", throwOnError: true)!;
        MethodInfo mortonMethod = utilType.GetMethod(name: "MortonEncode2D", bindingAttr: BindingFlags.NonPublic | BindingFlags.Static)!;
        byte[] expectedPixels = new byte[width * height * 4];
        byte[] swizzledPixels = new byte[expectedPixels.Length];

        for (int pixelY = 0; pixelY < height; pixelY++) {
            for (int pixelX = 0; pixelX < width; pixelX++) {
                int pixelIndex = (pixelY * width) + pixelX;
                int linearOffset = pixelIndex * 4;
                byte red = (byte)((pixelX % 251) + 1);
                byte green = (byte)((pixelY % 251) + 1);
                byte blue = (byte)(((pixelX + pixelY) % 251) + 1);
                expectedPixels[linearOffset] = red;
                expectedPixels[linearOffset + 1] = green;
                expectedPixels[linearOffset + 2] = blue;
                expectedPixels[linearOffset + 3] = 255;
                int mortonIndex = (int)mortonMethod.Invoke(obj: null, parameters: [pixelX, pixelY, width, height])!;
                int swizzledOffset = mortonIndex * 4;
                swizzledPixels[swizzledOffset] = blue;
                swizzledPixels[swizzledOffset + 1] = green;
                swizzledPixels[swizzledOffset + 2] = red;
                swizzledPixels[swizzledOffset + 3] = 255;
            }
        }

        byte[] segmentData = CreateRgbaSegmentData(width: width, height: height, swizzledPixels: swizzledPixels);
        string outputDirectory = CreateOutputDirectory();
        try {
            int exported = ProcessSegment(segmentData: segmentData, outputDirectory: outputDirectory);
            byte[] ddsData = File.ReadAllBytes(path: Path.Combine(path1: outputDirectory, path2: "test_texture.dds"));

            Assert.AreEqual(expected: 1, actual: exported);
            CollectionAssert.AreEqual(expected: expectedPixels, actual: ddsData.AsSpan(start: 128).ToArray());
        } finally {
            Directory.Delete(path: outputDirectory, recursive: true);
        }
    }

    private static byte[] CreateSegmentData(int metadataOffset, bool includeDecoyMarker) {
        byte[] nameSignature = [0x2D, 0x00, 0x02, 0x1C, 0x00, 0x00, 0x00, 0x0A];
        byte[] textureName = Encoding.UTF8.GetBytes(s: "test_texture");
        byte[] metadata = new byte[16];
        byte[] segmentData = new byte[metadataOffset + metadata.Length + 8];

        nameSignature.CopyTo(array: segmentData, index: 0);
        textureName.CopyTo(array: segmentData, index: 12);
        if (includeDecoyMarker) {
            segmentData[0x20] = 0x01;
            segmentData[0x21] = 0x02;
        }

        metadata[2] = 0x01;
        metadata[3] = 0x52;
        BinaryPrimitives.WriteUInt16BigEndian(destination: metadata.AsSpan(start: 4, length: 2), value: 4);
        BinaryPrimitives.WriteUInt16BigEndian(destination: metadata.AsSpan(start: 6, length: 2), value: 4);
        metadata[9] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(destination: metadata.AsSpan(start: 12, length: 4), value: 8);
        metadata.CopyTo(array: segmentData, index: metadataOffset);

        return segmentData;
    }

    private static byte[] CreateRgbaSegmentData(int width, int height, byte[] swizzledPixels) {
        byte[] nameSignature = [0x2D, 0x00, 0x02, 0x1C, 0x00, 0x00, 0x00, 0x0A];
        byte[] textureName = Encoding.UTF8.GetBytes(s: "test_texture");
        byte[] metadata = new byte[16];
        const int metadataOffset = 0x50;
        const int pixelDataStart = metadataOffset + 16;
        byte[] segmentData = new byte[pixelDataStart + swizzledPixels.Length];

        nameSignature.CopyTo(array: segmentData, index: 0);
        textureName.CopyTo(array: segmentData, index: 12);
        metadata[2] = 0x01;
        metadata[3] = 0x86;
        BinaryPrimitives.WriteUInt16BigEndian(destination: metadata.AsSpan(start: 4, length: 2), value: (ushort)width);
        BinaryPrimitives.WriteUInt16BigEndian(destination: metadata.AsSpan(start: 6, length: 2), value: (ushort)height);
        metadata[9] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(destination: metadata.AsSpan(start: 12, length: 4), value: (uint)swizzledPixels.Length);
        metadata.CopyTo(array: segmentData, index: metadataOffset);
        swizzledPixels.CopyTo(array: segmentData, index: pixelDataStart);

        return segmentData;
    }

    private static string CreateOutputDirectory() {
        string outputDirectory = Path.Combine(path1: Path.GetTempPath(), path2: $"txd-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path: outputDirectory);
        return outputDirectory;
    }

    private static int ProcessSegment(
        byte[] segmentData,
        string outputDirectory,
        HashSet<string>? exportedFileNames = null,
        int segmentStartOffset = 0
    ) {
        Assembly formatsAssembly = Assembly.Load(assemblyString: "EngineNet.Formats");
        Type segmentType = formatsAssembly.GetType(name: "EngineNet.GameFormats.txd.Segment", throwOnError: true)!;
        Type processorType = formatsAssembly.GetType(name: "EngineNet.GameFormats.txd.TextureSegmentProcessor", throwOnError: true)!;
        ConstructorInfo segmentConstructor = segmentType.GetConstructor(
            bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(int), typeof(byte[])],
            modifiers: null
        )!;
        object segment = segmentConstructor.Invoke(parameters: [segmentStartOffset, segmentData]);
        object processor = Activator.CreateInstance(type: processorType, nonPublic: true)!;
        MethodInfo processMethod = processorType.GetMethod(name: "ProcessSegment", bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (int)processMethod.Invoke(
            obj: processor,
            parameters: [segment, outputDirectory, exportedFileNames ?? new HashSet<string>(comparer: StringComparer.OrdinalIgnoreCase), "dds"]
        )!;
    }
}