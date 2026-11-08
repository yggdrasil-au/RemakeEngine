using System.Collections;
using System.Reflection;
using System.Text;
using EngineNet.GameFormats.p3d;

namespace EngineNetTest;

/// <summary>Protects binary field order and cursor consumption without exposing parser internals as public API.</summary>
[TestClass]
public sealed class P3dChunkDataTests {
    private static readonly Assembly Formats = typeof(P3dExtractor).Assembly;
    private const BindingFlags InternalInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>Modern mesh chunks read name, version, and payload in that order.</summary>
    [TestMethod]
    public void FromChunkType_MeshReadsNameBeforeVersion() {
        byte[] Bytes = Payload(Writer => { Name(Writer, "mesh"); Writer.Write(7u); Writer.Write(3u); });
        (object Data, int Remaining) = Parse("Mesh", Bytes);

        Assert.AreEqual("mesh", Property(Data, "Name"));
        Assert.AreEqual(7u, Property(Data, "Version"));
        Assert.AreEqual(3u, Property(Property(Data, "Payload")!, "NumPrimGroups"));
        Assert.AreEqual(0, Remaining);
    }

    /// <summary>Legacy particle chunks intentionally read version before name.</summary>
    [TestMethod]
    public void FromChunkType_OldParticleReadsVersionBeforeName() {
        byte[] Bytes = Payload(Writer => { Writer.Write(9u); Name(Writer, "particle"); Name(Writer, "unknown"); });
        (object Data, int Remaining) = Parse("OldParticleSystem", Bytes);

        Assert.AreEqual("particle", Property(Data, "Name"));
        Assert.AreEqual(9u, Property(Data, "Version"));
        Assert.AreEqual("unknown", Property(Property(Data, "Payload")!, "Unknown"));
        Assert.AreEqual(0, Remaining);
    }

    /// <summary>Animation channels consume frame indices before their typed values.</summary>
    [TestMethod]
    public void FromChunkType_FloatChannelReadsFramesAndValues() {
        byte[] Bytes = Payload(Writer => {
            Writer.Write(2u);
            Writer.Write(Encoding.ASCII.GetBytes("ABCD"));
            Writer.Write(2u);
            Writer.Write((ushort)4); Writer.Write((ushort)8);
            Writer.Write(1.5f); Writer.Write(2.5f);
        });
        (object Data, int Remaining) = Parse("Float1Channel", Bytes);
        object Channel = Property(Data, "Payload")!;

        CollectionAssert.AreEqual(new ushort[] { 4, 8 }, (ICollection)Property(Channel, "Frames")!);
        CollectionAssert.AreEqual(new[] { 1.5f, 2.5f }, (ICollection)Property(Channel, "Values")!);
        Assert.AreEqual(0, Remaining);
    }

    /// <summary>Container and unsupported chunks leave their payload bytes untouched.</summary>
    [TestMethod]
    public void FromChunkType_ContainerAndUnknownPreserveUnreadBytes() {
        byte[] Bytes = [1, 2, 3];
        (object Container, int ContainerRemaining) = Parse("DataFile", Bytes);
        (object Unknown, int UnknownRemaining) = Parse("Grid", Bytes);

        Assert.IsNull(Property(Container, "Payload"));
        Assert.AreEqual(false, Property(Container, "IsUnknown"));
        Assert.AreEqual(true, Property(Unknown, "IsUnknown"));
        Assert.AreEqual(3, ContainerRemaining);
        Assert.AreEqual(3, UnknownRemaining);
    }

    /// <summary>Short payloads retain the parser's error type and cursor detail.</summary>
    [TestMethod]
    public void FromChunkType_TruncatedPayloadRetainsOverrunError() {
        TargetInvocationException Error = Assert.ThrowsExactly<TargetInvocationException>(() => Parse("Mesh", [0]));
        Assert.IsNotNull(Error.InnerException);
        Assert.AreEqual("P3dParseException", Error.InnerException.GetType().Name);
        StringAssert.Contains(Error.InnerException.Message, "Overrun by 4 bytes (position=1, requested=4, remaining=0).");
    }

    private static (object Data, int Remaining) Parse(string ChunkName, byte[] Bytes) {
        Type ReaderType = Formats.GetType("EngineNet.GameFormats.p3d.ByteReader", throwOnError: true)!;
        Type ChunkType = Formats.GetType("EngineNet.GameFormats.p3d.ChunkType", throwOnError: true)!;
        Type Factory = Formats.GetType("EngineNet.GameFormats.p3d.ChunkDataFactory", throwOnError: true)!;
        object Reader = Activator.CreateInstance(ReaderType, InternalInstance, null, new object[] { new ReadOnlyMemory<byte>(Bytes) }, null)!;
        object Data = Factory.GetMethod("FromChunkType", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Enum.Parse(ChunkType, ChunkName), Reader])!;
        return (Data, (int)Property(Reader, "Remaining")!);
    }

    private static object? Property(object Instance, string Name) {
        return Instance.GetType().GetProperty(Name, InternalInstance | BindingFlags.Public)!.GetValue(Instance);
    }

    private static byte[] Payload(Action<BinaryWriter> Write) {
        using MemoryStream Stream = new();
        using BinaryWriter Writer = new(Stream, Encoding.UTF8, leaveOpen: true);
        Write(Writer);
        return Stream.ToArray();
    }

    private static void Name(BinaryWriter Writer, string Value) {
        Writer.Write((byte)Value.Length);
        Writer.Write(Encoding.ASCII.GetBytes(Value));
    }
}
