using FluentAssertions;
using HistoryVault.Configuration;
using HistoryVault.Models;
using HistoryVault.Storage;
using Xunit;

namespace HistoryVault.Tests.Storage;

public class StoragePathResolverTests
{
    private readonly string _testBasePath;
    private readonly StoragePathResolver _resolver;

    public StoragePathResolverTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), "HVTest", Guid.NewGuid().ToString());
        _resolver = new StoragePathResolver(new HistoryVaultOptions
        {
            BasePathOverride = _testBasePath
        });
    }

    [Fact]
    public void GetStoragePath_Local_ReturnsLocalPath()
    {
        var path = _resolver.GetStoragePath(StorageScope.Local);
        path.Should().Be(_testBasePath);
    }

    [Fact]
    public void GetStoragePath_Global_ReturnsOverridePath()
    {
        // With BasePathOverride, both scopes use the same base path
        var path = _resolver.GetStoragePath(StorageScope.Global);
        path.Should().Be(_testBasePath);
    }

    [Fact]
    public void GetSymbolPath_ReturnsCorrectPath()
    {
        var path = _resolver.GetSymbolPath(StorageScope.Local, "BTCUSDT");
        path.Should().Be(Path.Combine(_testBasePath, "BTCUSDT"));
    }

    [Fact]
    public void GetTimeframePath_ReturnsCorrectPath()
    {
        var path = _resolver.GetTimeframePath(StorageScope.Local, "BTCUSDT", CandlestickInterval.H1);
        path.Should().Be(Path.Combine(_testBasePath, "BTCUSDT", "1h"));
    }

    [Fact]
    public void GetYearPath_ReturnsCorrectPath()
    {
        var path = _resolver.GetYearPath(StorageScope.Local, "BTCUSDT", CandlestickInterval.H1, 2025);
        path.Should().Be(Path.Combine(_testBasePath, "BTCUSDT", "1h", "2025"));
    }

    [Theory]
    [InlineData(true, ".bin.gz")]
    [InlineData(false, ".bin")]
    public void GetMonthFilePath_ReturnsCorrectPath(bool compressed, string extension)
    {
        var path = _resolver.GetMonthFilePath(StorageScope.Local, "BTCUSDT", CandlestickInterval.H1, 2025, 3, compressed);
        path.Should().Be(Path.Combine(_testBasePath, "BTCUSDT", "1h", "2025", $"03{extension}"));
    }

    [Theory]
    [InlineData("BTCUSDT")]
    [InlineData("CON.EP.G25")]
    [InlineData("ABC-DEF")]
    public void GetSymbolPath_SanitizesSymbolName(string symbol)
    {
        var path = _resolver.GetSymbolPath(StorageScope.Local, symbol);
        path.Should().NotContain("/:");
        Path.GetFileName(path).Should().NotBeEmpty();
    }

    [Fact]
    public void EnsureDirectoryExists_CreatesDirectory()
    {
        var filePath = Path.Combine(_testBasePath, "new", "nested", "path", "file.bin");

        _resolver.EnsureDirectoryExists(filePath);

        Directory.Exists(Path.GetDirectoryName(filePath)).Should().BeTrue();

        // Cleanup
        Directory.Delete(Path.Combine(_testBasePath, "new"), true);
    }

    [Theory]
    [InlineData("/path/to/2025/03.bin", 2025, 3)]
    [InlineData("/path/to/2024/12.bin.gz", 2024, 12)]
    [InlineData("/path/to/2023/01.bin", 2023, 1)]
    public void ParseFilePath_ExtractsYearAndMonth(string path, int expectedYear, int expectedMonth)
    {
        var (year, month) = StoragePathResolver.ParseFilePath(path);
        year.Should().Be(expectedYear);
        month.Should().Be(expectedMonth);
    }

    [Fact]
    public void GetAllSymbols_ReturnsExistingSymbols()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_testBasePath, "BTCUSDT"));
        Directory.CreateDirectory(Path.Combine(_testBasePath, "ETHUSDT"));
        Directory.CreateDirectory(Path.Combine(_testBasePath, "XRPUSDT"));

        // Act
        var symbols = _resolver.GetAllSymbols(StorageScope.Local).ToList();

        // Assert
        symbols.Should().HaveCount(3);
        symbols.Should().Contain("BTCUSDT");
        symbols.Should().Contain("ETHUSDT");
        symbols.Should().Contain("XRPUSDT");

        // Cleanup
        Directory.Delete(_testBasePath, true);
    }

    [Fact]
    public void GetAvailableTimeframes_ReturnsExistingTimeframes()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_testBasePath, "TEST", "1m"));
        Directory.CreateDirectory(Path.Combine(_testBasePath, "TEST", "1h"));
        Directory.CreateDirectory(Path.Combine(_testBasePath, "TEST", "1d"));

        // Act
        var timeframes = _resolver.GetAvailableTimeframes(StorageScope.Local, "TEST").ToList();

        // Assert
        timeframes.Should().HaveCount(3);
        timeframes.Should().Contain(CandlestickInterval.M1);
        timeframes.Should().Contain(CandlestickInterval.H1);
        timeframes.Should().Contain(CandlestickInterval.D1);

        // Cleanup
        Directory.Delete(_testBasePath, true);
    }
}
