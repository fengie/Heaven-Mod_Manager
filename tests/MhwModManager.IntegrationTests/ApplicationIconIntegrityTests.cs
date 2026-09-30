using System.Buffers.Binary;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ApplicationIconIntegrityTests
{
    [Fact]
    public void ApplicationIconContainsStructurallyValidPngFrames()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "src", "MhwModManager.App", "Assets", "MHWModManager.ico");
        var bytes = File.ReadAllBytes(path);
        var data = bytes.AsSpan();

        Assert.True(data.Length >= 6);
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(data));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(data[2..]));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        Assert.True(count > 0);
        Assert.True(data.Length >= 6 + (count * 16));

        var sizes = new HashSet<(int Width, int Height)>();
        for (var index = 0; index < count; index++)
        {
            var entry = data.Slice(6 + (index * 16), 16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]));
            var offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]));
            Assert.True(offset >= 6 + (count * 16));
            Assert.True(length > 0);
            Assert.True(offset <= data.Length - length);
            Assert.True(sizes.Add((width, height)), $"Duplicate ICO frame {width}x{height}.");
            ValidatePng(data.Slice(offset, length), width, height);
        }

        foreach (var required in new[] { (16, 16), (24, 24), (32, 32) })
            Assert.Contains(required, sizes);
    }

    private static void ValidatePng(ReadOnlySpan<byte> png, int expectedWidth, int expectedHeight)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        Assert.True(png.Length >= 33);
        Assert.True(png[..8].SequenceEqual(signature));
        var position = 8;
        var sawHeader = false;
        var sawEnd = false;
        while (position + 12 <= png.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png[position..]));
            var dataStart = checked(position + 8);
            var dataEnd = checked(dataStart + length);
            var crcOffset = dataEnd;
            Assert.True(crcOffset <= png.Length - 4);
            var type = png.Slice(position + 4, 4);
            Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(png[crcOffset..]), ComputeCrc32(png.Slice(position + 4, checked(4 + length))));
            if (type.SequenceEqual("IHDR"u8))
            {
                Assert.Equal(13, length);
                Assert.Equal(expectedWidth, checked((int)BinaryPrimitives.ReadUInt32BigEndian(png[dataStart..])));
                Assert.Equal(expectedHeight, checked((int)BinaryPrimitives.ReadUInt32BigEndian(png[(dataStart + 4)..])));
                sawHeader = true;
            }
            position = checked(crcOffset + 4);
            if (type.SequenceEqual("IEND"u8))
            {
                Assert.Equal(0, length);
                sawEnd = true;
                break;
            }
        }
        Assert.True(sawHeader);
        Assert.True(sawEnd);
        Assert.Equal(png.Length, position);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xffffffffu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        return ~crc;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MhwModManager.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
