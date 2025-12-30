using FluentAssertions;
using HistoryVault.Models;
using HistoryVault.Storage;
using Xunit;

namespace HistoryVault.Tests.Storage;

public class BinarySerializerTests
{
    private readonly BinarySerializer _serializer = new();

    [Fact]
    public void BinarySerializer_Roundtrip_PreservesAllFields()
    {
        // Arrange
        var candles = new List<CandlestickV2>
        {
            new()
            {
                OpenTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                CloseTime = new DateTime(2025, 1, 1, 10, 0, 59, DateTimeKind.Utc),
                Open = 100.5m,
                High = 105.75m,
                Low = 99.25m,
                Close = 104.125m,
                Volume = 12345.6789m
            },
            new()
            {
                OpenTime = new DateTime(2025, 1, 1, 10, 1, 0, DateTimeKind.Utc),
                CloseTime = new DateTime(2025, 1, 1, 10, 1, 59, DateTimeKind.Utc),
                Open = 104.125m,
                High = 106.5m,
                Low = 103.0m,
                Close = 105.5m,
                Volume = 9876.5432m
            }
        };

        // Act
        var (buffer, length) = _serializer.Serialize(candles, CandlestickInterval.M1, false);
        var (deserialized, header) = _serializer.Deserialize(buffer.AsSpan(0, length));
        _serializer.ReturnBuffer(buffer);

        // Assert
        deserialized.Should().HaveCount(2);
        header.RecordCount.Should().Be(2);
        header.Timeframe.Should().Be(CandlestickInterval.M1);
        header.IsCompressed.Should().BeFalse();

        for (int i = 0; i < candles.Count; i++)
        {
            deserialized[i].OpenTime.Should().Be(candles[i].OpenTime);
            deserialized[i].CloseTime.Should().Be(candles[i].CloseTime);
            deserialized[i].Open.Should().Be(candles[i].Open);
            deserialized[i].High.Should().Be(candles[i].High);
            deserialized[i].Low.Should().Be(candles[i].Low);
            deserialized[i].Close.Should().Be(candles[i].Close);
            deserialized[i].Volume.Should().Be(candles[i].Volume);
        }
    }

    [Fact]
    public void BinarySerializer_DecimalPrecision_IsPreserved()
    {
        // Arrange
        var candle = new CandlestickV2
        {
            OpenTime = DateTime.UtcNow,
            CloseTime = DateTime.UtcNow.AddMinutes(1),
            Open = 0.123456789012345678901234567890m,
            High = 9999999999.999999999999999999m,
            Low = 0.000000000000000000000000001m,
            Close = 1234567890.123456789012345678m,
            Volume = 99999999999999999999999999.99m
        };

        // Act
        var (buffer, length) = _serializer.Serialize(new[] { candle }, CandlestickInterval.M1, false);
        var (deserialized, _) = _serializer.Deserialize(buffer.AsSpan(0, length));
        _serializer.ReturnBuffer(buffer);

        // Assert
        var result = deserialized[0];
        result.Open.Should().Be(candle.Open);
        result.High.Should().Be(candle.High);
        result.Low.Should().Be(candle.Low);
        result.Close.Should().Be(candle.Close);
        result.Volume.Should().Be(candle.Volume);
    }

    [Fact]
    public void BinarySerializer_EmptyCandles_ProducesValidEmptyFile()
    {
        // Arrange
        var candles = new List<CandlestickV2>();

        // Act
        var (buffer, length) = _serializer.Serialize(candles, CandlestickInterval.H1, false);
        var (deserialized, header) = _serializer.Deserialize(buffer.AsSpan(0, length));
        _serializer.ReturnBuffer(buffer);

        // Assert
        length.Should().Be(BinarySerializer.HeaderSize);
        deserialized.Should().BeEmpty();
        header.RecordCount.Should().Be(0);
        header.Timeframe.Should().Be(CandlestickInterval.H1);
    }

    [Fact]
    public void BinarySerializer_Header_ContainsMagicBytes()
    {
        // Arrange
        var candles = TestHelpers.GenerateCandles(5, CandlestickInterval.M1, DateTime.UtcNow);

        // Act
        var (buffer, length) = _serializer.Serialize(candles, CandlestickInterval.M1, false);
        var (_, header) = _serializer.Deserialize(buffer.AsSpan(0, length));
        _serializer.ReturnBuffer(buffer);

        // Assert
        header.Magic.Should().BeEquivalentTo(BinarySerializer.MagicBytes.ToArray());
        header.Version.Should().Be(BinarySerializer.CurrentVersion);
    }

    [Fact]
    public void BinarySerializer_InvalidMagic_ThrowsException()
    {
        // Arrange
        var invalidData = new byte[100];
        invalidData[0] = (byte)'X';
        invalidData[1] = (byte)'Y';
        invalidData[2] = (byte)'Z';
        invalidData[3] = (byte)'!';

        // Act
        var action = () => _serializer.Deserialize(invalidData);

        // Assert
        action.Should().Throw<InvalidDataException>()
            .WithMessage("*magic bytes*");
    }

    [Fact]
    public void BinarySerializer_BufferTooSmall_ThrowsException()
    {
        // Arrange
        var smallBuffer = new byte[10];

        // Act
        var action = () => _serializer.Deserialize(smallBuffer);

        // Assert
        action.Should().Throw<InvalidDataException>()
            .WithMessage("*too small*");
    }

    [Fact]
    public async Task BinarySerializer_StreamRoundtrip_Works()
    {
        // Arrange
        var candles = TestHelpers.GenerateCandles(100, CandlestickInterval.M5, DateTime.UtcNow);
        using var stream = new MemoryStream();

        // Act
        await _serializer.SerializeToStreamAsync(stream, candles, CandlestickInterval.M5);
        stream.Seek(0, SeekOrigin.Begin);
        var (deserialized, header) = await _serializer.DeserializeFromStreamAsync(stream);

        // Assert
        deserialized.Should().HaveCount(100);
        header.Timeframe.Should().Be(CandlestickInterval.M5);
    }

    [Fact]
    public void BinarySerializer_LargeDataset_HandlesCorrectly()
    {
        // Arrange
        var candles = TestHelpers.GenerateCandles(10000, CandlestickInterval.M1, DateTime.UtcNow);

        // Act
        var (buffer, length) = _serializer.Serialize(candles, CandlestickInterval.M1, false);
        var (deserialized, header) = _serializer.Deserialize(buffer.AsSpan(0, length));
        _serializer.ReturnBuffer(buffer);

        // Assert
        deserialized.Should().HaveCount(10000);
        header.RecordCount.Should().Be(10000);

        // Verify first and last
        deserialized[0].Open.Should().Be(candles[0].Open);
        deserialized[9999].Close.Should().Be(candles[9999].Close);
    }
}
