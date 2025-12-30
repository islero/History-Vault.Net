using System.IO.Compression;
using FluentAssertions;
using HistoryVault.Storage;
using Xunit;

namespace HistoryVault.Tests.Storage;

public class CompressionHandlerTests
{
    private readonly CompressionHandler _compression = new();

    [Fact]
    public void Compress_Decompress_Roundtrip()
    {
        // Arrange
        var originalData = new byte[1000];
        new Random(42).NextBytes(originalData);

        // Act
        var compressed = _compression.Compress(originalData);
        var decompressed = _compression.Decompress(compressed);

        // Assert
        decompressed.Should().BeEquivalentTo(originalData);
    }

    [Fact]
    public void Compress_ProducesSmallOutput_ForRepetitiveData()
    {
        // Arrange
        var repetitiveData = new byte[10000];
        Array.Fill(repetitiveData, (byte)0x42);

        // Act
        var compressed = _compression.Compress(repetitiveData, CompressionLevel.SmallestSize);

        // Assert
        compressed.Length.Should().BeLessThan(repetitiveData.Length / 10); // Should compress well
    }

    [Fact]
    public async Task CompressToStreamAsync_Works()
    {
        // Arrange
        var data = new byte[500];
        new Random(42).NextBytes(data);
        using var outputStream = new MemoryStream();

        // Act
        await _compression.CompressToStreamAsync(data, outputStream);
        outputStream.Seek(0, SeekOrigin.Begin);
        var decompressed = await _compression.DecompressFromStreamAsync(outputStream);

        // Assert
        decompressed.Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task DecompressToPooledBufferAsync_ReturnsCorrectData()
    {
        // Arrange
        var original = new byte[5000];
        new Random(42).NextBytes(original);
        var compressed = _compression.Compress(original);
        using var stream = new MemoryStream(compressed);

        // Act
        var (buffer, length) = await _compression.DecompressToPooledBufferAsync(stream, 4000);

        try
        {
            // Assert
            length.Should().Be(5000);
            buffer.AsSpan(0, length).ToArray().Should().BeEquivalentTo(original);
        }
        finally
        {
            _compression.ReturnBuffer(buffer);
        }
    }

    [Fact]
    public void IsGzipCompressed_DetectsGzip()
    {
        // Arrange
        var data = new byte[100];
        new Random(42).NextBytes(data);
        var compressed = _compression.Compress(data);
        using var stream = new MemoryStream(compressed);

        // Act
        var result = CompressionHandler.IsGzipCompressed(stream);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsGzipCompressed_RejectNonGzip()
    {
        // Arrange
        var plainData = new byte[100];
        new Random(42).NextBytes(plainData);
        using var stream = new MemoryStream(plainData);

        // Act
        var result = CompressionHandler.IsGzipCompressed(stream);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, ".bin.gz")]
    [InlineData(false, ".bin")]
    public void GetFileExtension_ReturnsCorrectExtension(bool useCompression, string expected)
    {
        CompressionHandler.GetFileExtension(useCompression).Should().Be(expected);
    }

    [Theory]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.SmallestSize)]
    public void Compress_WorksWithAllLevels(CompressionLevel level)
    {
        // Arrange
        var data = new byte[1000];
        new Random(42).NextBytes(data);

        // Act
        var compressed = _compression.Compress(data, level);
        var decompressed = _compression.Decompress(compressed);

        // Assert
        decompressed.Should().BeEquivalentTo(data);
    }
}
