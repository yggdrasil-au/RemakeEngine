namespace EngineNet.Core.FileHandlers.TxdExtractor;

internal static partial class Main {

    private static byte[] CreateDdsHeaderDxt(int width, int height, int mipMapCountFromFile, string fourcc) {
        byte[] buffer = new byte[128];
        using System.IO.MemoryStream ms = new(buffer);
        using System.IO.BinaryWriter writer = new(ms, System.Text.Encoding.ASCII, leaveOpen: true);

        const int DDSD_CAPS = 0x1;
        const int DDSD_HEIGHT = 0x2;
        const int DDSD_WIDTH = 0x4;
        const int DDSD_PIXELFORMAT = 0x1000;
        const int DDSD_MIPMAPCOUNT = 0x20000;
        const int DDSD_LINEARSIZE = 0x80000;

        int flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | DDSD_LINEARSIZE;
        if (mipMapCountFromFile > 0) {
            flags |= DDSD_MIPMAPCOUNT;
        }

        int dwMipMapCount = mipMapCountFromFile > 0 ? mipMapCountFromFile : 1;
        int linearSize = CalculateDxtLevelSize(width, height, fourcc);

        writer.Write(System.Text.Encoding.ASCII.GetBytes("DDS "));
        writer.Write(124);
        writer.Write(flags);
        writer.Write(height);
        writer.Write(width);
        writer.Write(linearSize);
        writer.Write(0);
        writer.Write(dwMipMapCount);

        for (int i = 0; i < 11; i++) {
            writer.Write(0);
        }

        const int pfSize = 32;
        const int DDPF_FOURCC = 0x4;
        writer.Write(pfSize);
        writer.Write(DDPF_FOURCC);
        byte[] fourccBytes = new byte[4];
        byte[] srcFourcc = System.Text.Encoding.ASCII.GetBytes(fourcc);
        System.Array.Copy(srcFourcc, fourccBytes, System.Math.Min(srcFourcc.Length, 4));
        writer.Write(fourccBytes);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        const int DDSCAPS_TEXTURE = 0x1000;
        const int DDSCAPS_MIPMAP = 0x400000;
        const int DDSCAPS_COMPLEX = 0x8;
        int caps = DDSCAPS_TEXTURE;
        if (dwMipMapCount > 1) {
            caps |= DDSCAPS_MIPMAP | DDSCAPS_COMPLEX;
        }

        writer.Write(caps);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        return buffer;
    }

    private static byte[] CreateDdsHeaderRgba(int width, int height, int mipMapCount) {
        byte[] buffer = new byte[128];
        using System.IO.MemoryStream ms = new(buffer);
        using System.IO.BinaryWriter writer = new(ms, System.Text.Encoding.ASCII, leaveOpen: true);

        const int DDSD_CAPS = 0x1;
        const int DDSD_HEIGHT = 0x2;
        const int DDSD_WIDTH = 0x4;
        const int DDSD_PIXELFORMAT = 0x1000;
        const int DDSD_PITCH = 0x8;

        writer.Write(System.Text.Encoding.ASCII.GetBytes("DDS "));
        writer.Write(124);
        writer.Write(DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | DDSD_PITCH);
        writer.Write(height);
        writer.Write(width);
        writer.Write(width * 4);
        writer.Write(0);
        writer.Write(mipMapCount > 0 ? mipMapCount : 1);

        for (int i = 0; i < 11; i++) {
            writer.Write(0);
        }

        const int pfSize = 32;
        const int DDPF_RGB = 0x40;
        const int DDPF_ALPHAPIXELS = 0x1;
        writer.Write(pfSize);
        writer.Write(DDPF_RGB | DDPF_ALPHAPIXELS);
        writer.Write(0);
        writer.Write(32);
        writer.Write(unchecked(0x000000FF));
        writer.Write(unchecked(0x0000FF00));
        writer.Write(unchecked(0x00FF0000));
        writer.Write(unchecked((int)0xFF000000));

        const int DDSCAPS_TEXTURE = 0x1000;
        writer.Write(DDSCAPS_TEXTURE);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        return buffer;
    }

}
