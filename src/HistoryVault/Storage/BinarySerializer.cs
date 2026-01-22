using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HistoryVault.Models;

namespace HistoryVault.Storage;

/// <summary>
/// High-performance binary serializer for candlestick data.
/// Uses Span-based operations and ArrayPool for zero-allocation serialization.
/// </summary>
public sealed class BinarySerializer
{
    /// <summary>
    /// Magic bytes identifying HistoryVault binary files.
    /// </summary>
    public static ReadOnlySpan<byte> MagicBytes => "HVLT"u8;

    /// <summary>
    /// Current format version.
    /// </summary>
    public const ushort CurrentVersion = 1;

    /// <summary>
    /// Header size in bytes.
    /// </summary>
    public const int HeaderSize = 64;

    /// <summary>
    /// Record size for a single candlestick in bytes.
    /// OpenTime(8) + CloseTime(8) + Open(16) + High(16) + Low(16) + Close(16) + Volume(16) = 96 bytes
    /// </summary>
    public const int RecordSize = 96;

    /// <summary>
    /// Flag indicating compressed data.
    /// </summary>
    public const ushort FlagCompressed = 0x0001;

    private readonly ArrayPool<byte> _pool;

    /// <summary>
    /// Initializes a new instance of the <see cref="BinarySerializer"/> class.
    /// </summary>
    /// <param name="pool">Optional custom array pool. Uses shared pool if null.</param>
    public BinarySerializer(ArrayPool<byte>? pool = null)
    {
        _pool = pool ?? ArrayPool<byte>.Shared;
    }

    /// <summary>
    /// Serializes candlesticks to a byte array with header.
    /// </summary>
    /// <param name="candles">The candlesticks to serialize.</param>
    /// <param name="timeframe">The timeframe of the candlesticks.</param>
    /// <param name="isCompressed">Whether the data will be compressed after serialization.</param>
    /// <returns>A tuple of (byte array, actual length used).</returns>
    public (byte[] Buffer, int Length) Serialize(
        IReadOnlyList<CandlestickV2> candles,
        CandlestickInterval timeframe,
        bool isCompressed = false)
    {
        if (candles.Count == 0)
        {
            return SerializeEmptyData(timeframe, isCompressed);
        }

        int totalSize = HeaderSize + (candles.Count * RecordSize);
        byte[] buffer = _pool.Rent(totalSize);

        try
        {
            var span = buffer.AsSpan(0, totalSize);

            WriteHeader(span, candles, timeframe, isCompressed);
            WriteRecords(span[HeaderSize..], candles);

            return (buffer, totalSize);
        }
        catch
        {
            _pool.Return(buffer);
            throw;
        }
    }

    /// <summary>
    /// Serializes candlesticks directly to a stream.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="candles">The candlesticks to serialize.</param>
    /// <param name="timeframe">The timeframe of the candlesticks.</param>
    /// <param name="isCompressed">Whether the data will be compressed after serialization.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SerializeToStreamAsync(
        Stream stream,
        IReadOnlyList<CandlestickV2> candles,
        CandlestickInterval timeframe,
        bool isCompressed = false,
        CancellationToken ct = default)
    {
        var (buffer, length) = Serialize(candles, timeframe, isCompressed);
        try
        {
            await stream.WriteAsync(buffer.AsMemory(0, length), ct).ConfigureAwait(false);
        }
        finally
        {
            _pool.Return(buffer);
        }
    }

    /// <summary>
    /// Deserializes candlesticks from a byte array.
    /// </summary>
    /// <param name="buffer">The buffer containing serialized data.</param>
    /// <returns>A tuple of (candlesticks, header info).</returns>
    public (IReadOnlyList<CandlestickV2> Candles, HeaderInfo Header) Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize)
        {
            throw new InvalidDataException($"Buffer too small for header. Expected at least {HeaderSize} bytes.");
        }

        var header = ReadHeader(buffer);
        ValidateHeader(header);

        if (header.RecordCount == 0)
        {
            return (Array.Empty<CandlestickV2>(), header);
        }

        int expectedSize = HeaderSize + (header.RecordCount * RecordSize);
        if (buffer.Length < expectedSize)
        {
            throw new InvalidDataException(
                $"Buffer too small for records. Expected {expectedSize} bytes, got {buffer.Length}.");
        }

        var candles = ReadRecords(buffer[HeaderSize..], header.RecordCount);
        return (candles, header);
    }

    /// <summary>
    /// Deserializes candlesticks from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple of (candlesticks, header info).</returns>
    public async Task<(IReadOnlyList<CandlestickV2> Candles, HeaderInfo Header)> DeserializeFromStreamAsync(
        Stream stream,
        CancellationToken ct = default)
    {
        byte[] headerBuffer = _pool.Rent(HeaderSize);
        try
        {
            int bytesRead = await stream.ReadAtLeastAsync(headerBuffer.AsMemory(0, HeaderSize), HeaderSize, false, ct)
                .ConfigureAwait(false);

            if (bytesRead < HeaderSize)
            {
                throw new InvalidDataException($"Could not read header. Expected {HeaderSize} bytes, got {bytesRead}.");
            }

            var header = ReadHeader(headerBuffer.AsSpan(0, HeaderSize));
            ValidateHeader(header);

            if (header.RecordCount == 0)
            {
                return (Array.Empty<CandlestickV2>(), header);
            }

            int recordsSize = header.RecordCount * RecordSize;
            byte[] recordBuffer = _pool.Rent(recordsSize);
            try
            {
                bytesRead = await stream.ReadAtLeastAsync(recordBuffer.AsMemory(0, recordsSize), recordsSize, false, ct)
                    .ConfigureAwait(false);

                if (bytesRead < recordsSize)
                {
                    throw new InvalidDataException(
                        $"Could not read records. Expected {recordsSize} bytes, got {bytesRead}.");
                }

                var candles = ReadRecords(recordBuffer.AsSpan(0, recordsSize), header.RecordCount);
                return (candles, header);
            }
            finally
            {
                _pool.Return(recordBuffer);
            }
        }
        finally
        {
            _pool.Return(headerBuffer);
        }
    }

    /// <summary>
    /// Returns a rented buffer to the pool.
    /// </summary>
    /// <param name="buffer">The buffer to return.</param>
    public void ReturnBuffer(byte[] buffer)
    {
        _pool.Return(buffer);
    }

    /// <summary>
    /// Deserializes only the header from a byte array without reading record data.
    /// This is useful for quickly checking file metadata without loading all candlesticks.
    /// </summary>
    /// <param name="buffer">The buffer containing at least the header bytes.</param>
    /// <returns>The header info.</returns>
    public HeaderInfo DeserializeHeaderOnly(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize)
        {
            throw new InvalidDataException($"Buffer too small for header. Expected at least {HeaderSize} bytes.");
        }

        var header = ReadHeader(buffer);
        ValidateHeader(header);
        return header;
    }

    private (byte[] Buffer, int Length) SerializeEmptyData(CandlestickInterval timeframe, bool isCompressed)
    {
        byte[] buffer = _pool.Rent(HeaderSize);
        var span = buffer.AsSpan(0, HeaderSize);

        MagicBytes.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], CurrentVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], isCompressed ? FlagCompressed : (ushort)0);
        BinaryPrimitives.WriteInt64LittleEndian(span[8..], 0); // RecordCount
        BinaryPrimitives.WriteInt64LittleEndian(span[16..], 0); // FirstTimestamp
        BinaryPrimitives.WriteInt64LittleEndian(span[24..], 0); // LastTimestamp
        BinaryPrimitives.WriteInt32LittleEndian(span[32..], (int)timeframe);
        span[36..64].Clear(); // Reserved

        return (buffer, HeaderSize);
    }

    private static void WriteHeader(
        Span<byte> span,
        IReadOnlyList<CandlestickV2> candles,
        CandlestickInterval timeframe,
        bool isCompressed)
    {
        MagicBytes.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], CurrentVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], isCompressed ? FlagCompressed : (ushort)0);
        BinaryPrimitives.WriteInt64LittleEndian(span[8..], candles.Count);
        BinaryPrimitives.WriteInt64LittleEndian(span[16..], candles[0].OpenTime.Ticks);
        BinaryPrimitives.WriteInt64LittleEndian(span[24..], candles[^1].CloseTime.Ticks);
        BinaryPrimitives.WriteInt32LittleEndian(span[32..], (int)timeframe);
        span[36..64].Clear(); // Reserved
    }

    private static void WriteRecords(Span<byte> span, IReadOnlyList<CandlestickV2> candles)
    {
        for (int i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];
            var recordSpan = span.Slice(i * RecordSize, RecordSize);

            BinaryPrimitives.WriteInt64LittleEndian(recordSpan, candle.OpenTime.Ticks);
            BinaryPrimitives.WriteInt64LittleEndian(recordSpan[8..], candle.CloseTime.Ticks);
            WriteDecimal(recordSpan[16..], candle.Open);
            WriteDecimal(recordSpan[32..], candle.High);
            WriteDecimal(recordSpan[48..], candle.Low);
            WriteDecimal(recordSpan[64..], candle.Close);
            WriteDecimal(recordSpan[80..], candle.Volume);
        }
    }

    private static HeaderInfo ReadHeader(ReadOnlySpan<byte> span)
    {
        return new HeaderInfo
        {
            Magic = span[..4].ToArray(),
            Version = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]),
            Flags = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]),
            RecordCount = (int)BinaryPrimitives.ReadInt64LittleEndian(span[8..]),
            FirstTimestamp = new DateTime(BinaryPrimitives.ReadInt64LittleEndian(span[16..])),
            LastTimestamp = new DateTime(BinaryPrimitives.ReadInt64LittleEndian(span[24..])),
            Timeframe = (CandlestickInterval)BinaryPrimitives.ReadInt32LittleEndian(span[32..])
        };
    }

    private static void ValidateHeader(HeaderInfo header)
    {
        if (!header.Magic.AsSpan().SequenceEqual(MagicBytes))
        {
            throw new InvalidDataException("Invalid magic bytes. Not a HistoryVault file.");
        }

        if (header.Version > CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported format version {header.Version}. Maximum supported version is {CurrentVersion}.");
        }

        if (header.RecordCount < 0)
        {
            throw new InvalidDataException($"Invalid record count: {header.RecordCount}");
        }
    }

    private static List<CandlestickV2> ReadRecords(ReadOnlySpan<byte> span, int count)
    {
        var candles = new List<CandlestickV2>(count);

        for (int i = 0; i < count; i++)
        {
            var recordSpan = span.Slice(i * RecordSize, RecordSize);

            candles.Add(new CandlestickV2
            {
                OpenTime = new DateTime(BinaryPrimitives.ReadInt64LittleEndian(recordSpan)),
                CloseTime = new DateTime(BinaryPrimitives.ReadInt64LittleEndian(recordSpan[8..])),
                Open = ReadDecimal(recordSpan[16..]),
                High = ReadDecimal(recordSpan[32..]),
                Low = ReadDecimal(recordSpan[48..]),
                Close = ReadDecimal(recordSpan[64..]),
                Volume = ReadDecimal(recordSpan[80..])
            });
        }

        return candles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteDecimal(Span<byte> span, decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        BinaryPrimitives.WriteInt32LittleEndian(span, bits[0]);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], bits[1]);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], bits[2]);
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], bits[3]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static decimal ReadDecimal(ReadOnlySpan<byte> span)
    {
        Span<int> bits = stackalloc int[4];
        bits[0] = BinaryPrimitives.ReadInt32LittleEndian(span);
        bits[1] = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
        bits[2] = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
        bits[3] = BinaryPrimitives.ReadInt32LittleEndian(span[12..]);
        return new decimal(bits);
    }

    /// <summary>
    /// Contains header information from a serialized file.
    /// </summary>
    public readonly struct HeaderInfo
    {
        /// <summary>
        /// The magic bytes identifying the file format.
        /// </summary>
        public required byte[] Magic { get; init; }

        /// <summary>
        /// The format version number.
        /// </summary>
        public required ushort Version { get; init; }

        /// <summary>
        /// Bit flags for various options.
        /// </summary>
        public required ushort Flags { get; init; }

        /// <summary>
        /// The number of candlestick records.
        /// </summary>
        public required int RecordCount { get; init; }

        /// <summary>
        /// The timestamp of the first candlestick.
        /// </summary>
        public required DateTime FirstTimestamp { get; init; }

        /// <summary>
        /// The timestamp of the last candlestick.
        /// </summary>
        public required DateTime LastTimestamp { get; init; }

        /// <summary>
        /// The timeframe of the candlesticks.
        /// </summary>
        public required CandlestickInterval Timeframe { get; init; }

        /// <summary>
        /// Gets whether the data is compressed.
        /// </summary>
        public bool IsCompressed => (Flags & FlagCompressed) != 0;
    }
}

/// <summary>
/// Provides access to BinaryPrimitives for little-endian read/write operations.
/// </summary>
file static class BinaryPrimitives
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt16LittleEndian(Span<byte> destination, ushort value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt32LittleEndian(Span<byte> destination, int value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(destination, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt64LittleEndian(Span<byte> destination, long value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(destination, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> source)
    {
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(source);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadInt32LittleEndian(ReadOnlySpan<byte> source)
    {
        return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(source);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadInt64LittleEndian(ReadOnlySpan<byte> source)
    {
        return System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(source);
    }
}
