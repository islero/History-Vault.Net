using System.Buffers;
using System.IO.Compression;

namespace HistoryVault.Storage;

/// <summary>
/// Handles compression and decompression of binary data using GZip.
/// </summary>
public sealed class CompressionHandler
{
    private readonly ArrayPool<byte> _pool;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompressionHandler"/> class.
    /// </summary>
    /// <param name="pool">Optional custom array pool. Uses shared pool if null.</param>
    public CompressionHandler(ArrayPool<byte>? pool = null)
    {
        _pool = pool ?? ArrayPool<byte>.Shared;
    }

    /// <summary>
    /// Compresses data to a stream using GZip.
    /// </summary>
    /// <param name="data">The data to compress.</param>
    /// <param name="outputStream">The stream to write compressed data to.</param>
    /// <param name="compressionLevel">The compression level to use.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task CompressToStreamAsync(
        ReadOnlyMemory<byte> data,
        Stream outputStream,
        CompressionLevel compressionLevel = CompressionLevel.Optimal,
        CancellationToken ct = default)
    {
        await using var gzipStream = new GZipStream(outputStream, compressionLevel, leaveOpen: true);
        await gzipStream.WriteAsync(data, ct).ConfigureAwait(false);
        await gzipStream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Compresses data and returns the compressed bytes.
    /// </summary>
    /// <param name="data">The data to compress.</param>
    /// <param name="compressionLevel">The compression level to use.</param>
    /// <returns>The compressed data.</returns>
    public byte[] Compress(ReadOnlySpan<byte> data, CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, compressionLevel, leaveOpen: true))
        {
            gzipStream.Write(data);
        }
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Decompresses data from a stream.
    /// </summary>
    /// <param name="inputStream">The stream containing compressed data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The decompressed data.</returns>
    public async Task<byte[]> DecompressFromStreamAsync(Stream inputStream, CancellationToken ct = default)
    {
        await using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress, leaveOpen: true);
        using var memoryStream = new MemoryStream();

        byte[] buffer = _pool.Rent(81920);
        try
        {
            int bytesRead;
            while ((bytesRead = await gzipStream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
            }
            return memoryStream.ToArray();
        }
        finally
        {
            _pool.Return(buffer);
        }
    }

    /// <summary>
    /// Decompresses data from a byte array.
    /// </summary>
    /// <param name="compressedData">The compressed data.</param>
    /// <returns>The decompressed data.</returns>
    public byte[] Decompress(ReadOnlySpan<byte> compressedData)
    {
        using var inputStream = new MemoryStream(compressedData.ToArray());
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();

        byte[] buffer = _pool.Rent(81920);
        try
        {
            int bytesRead;
            while ((bytesRead = gzipStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                outputStream.Write(buffer, 0, bytesRead);
            }
            return outputStream.ToArray();
        }
        finally
        {
            _pool.Return(buffer);
        }
    }

    /// <summary>
    /// Decompresses data from a file stream with an estimated uncompressed size.
    /// </summary>
    /// <param name="inputStream">The stream containing compressed data.</param>
    /// <param name="estimatedUncompressedSize">Estimated size of uncompressed data for buffer allocation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple of (buffer, actual length). Caller is responsible for returning buffer to pool.</returns>
    public async Task<(byte[] Buffer, int Length)> DecompressToPooledBufferAsync(
        Stream inputStream,
        int estimatedUncompressedSize,
        CancellationToken ct = default)
    {
        byte[] buffer = _pool.Rent(estimatedUncompressedSize);
        int totalRead = 0;

        try
        {
            await using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress, leaveOpen: true);

            while (true)
            {
                if (totalRead >= buffer.Length)
                {
                    // Need a larger buffer
                    byte[] newBuffer = _pool.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, totalRead).CopyTo(newBuffer);
                    _pool.Return(buffer);
                    buffer = newBuffer;
                }

                int bytesRead = await gzipStream.ReadAsync(
                    buffer.AsMemory(totalRead, buffer.Length - totalRead), ct).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    break;
                }

                totalRead += bytesRead;
            }

            return (buffer, totalRead);
        }
        catch
        {
            _pool.Return(buffer);
            throw;
        }
    }

    /// <summary>
    /// Returns a buffer to the pool.
    /// </summary>
    /// <param name="buffer">The buffer to return.</param>
    public void ReturnBuffer(byte[] buffer)
    {
        _pool.Return(buffer);
    }

    /// <summary>
    /// Checks if a file appears to be GZip compressed by examining magic bytes.
    /// </summary>
    /// <param name="stream">The stream to check.</param>
    /// <returns>True if the file appears to be GZip compressed.</returns>
    public static bool IsGzipCompressed(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 2)
        {
            return false;
        }

        long originalPosition = stream.Position;
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            Span<byte> magic = stackalloc byte[2];
            int bytesRead = stream.Read(magic);
            return bytesRead == 2 && magic[0] == 0x1F && magic[1] == 0x8B;
        }
        finally
        {
            stream.Seek(originalPosition, SeekOrigin.Begin);
        }
    }

    /// <summary>
    /// Gets the file extension for compressed or uncompressed files.
    /// </summary>
    /// <param name="useCompression">Whether compression is enabled.</param>
    /// <returns>The file extension including the dot.</returns>
    public static string GetFileExtension(bool useCompression) => useCompression ? ".bin.gz" : ".bin";
}
