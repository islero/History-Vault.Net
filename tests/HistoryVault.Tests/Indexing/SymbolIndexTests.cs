using FluentAssertions;
using HistoryVault.Configuration;
using HistoryVault.Models;
using HistoryVault.Indexing;
using HistoryVault.Storage;
using Xunit;

namespace HistoryVault.Tests.Indexing;

public class SymbolIndexTests : IDisposable
{
    private readonly string _tempPath;
    private readonly SymbolIndex _index;

    public SymbolIndexTests()
    {
        _tempPath = TestHelpers.GetTempDirectory();
        var pathResolver = new StoragePathResolver(new HistoryVaultOptions { LocalBasePath = _tempPath });
        _index = new SymbolIndex(pathResolver);
    }

    public void Dispose()
    {
        TestHelpers.CleanupTempDirectory(_tempPath);
    }

    [Fact]
    public async Task GetMatchingSymbols_WithWildcard_ReturnsMatches()
    {
        // Arrange
        CreateSymbolDir("BTC.USD");
        CreateSymbolDir("BTC.EUR");
        CreateSymbolDir("ETH.USD");

        // Act
        var matches = await _index.GetMatchingSymbolsAsync("BTC.*", StorageScope.Local);

        // Assert
        matches.Should().HaveCount(2);
        matches.Should().Contain("BTC.USD");
        matches.Should().Contain("BTC.EUR");
    }

    [Fact]
    public async Task GetMatchingSymbols_WithStar_ReturnsAll()
    {
        // Arrange
        CreateSymbolDir("SYM1");
        CreateSymbolDir("SYM2");
        CreateSymbolDir("SYM3");

        // Act
        var matches = await _index.GetMatchingSymbolsAsync("*", StorageScope.Local);

        // Assert
        matches.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetMatchingSymbols_LiteralMatch_ReturnsExact()
    {
        // Arrange
        CreateSymbolDir("BTCUSDT");
        CreateSymbolDir("BTCEUR");

        // Act
        var matches = await _index.GetMatchingSymbolsAsync("BTCUSDT", StorageScope.Local);

        // Assert
        matches.Should().HaveCount(1);
        matches[0].Should().Be("BTCUSDT");
    }

    [Fact]
    public async Task GetMatchingSymbols_QuestionMarkWildcard_MatchesSingleChar()
    {
        // Arrange
        CreateSymbolDir("SYM1");
        CreateSymbolDir("SYM2");
        CreateSymbolDir("SYM12");
        CreateSymbolDir("SYMX");

        // Act
        var matches = await _index.GetMatchingSymbolsAsync("SYM?", StorageScope.Local);

        // Assert
        matches.Should().HaveCount(3); // SYM1, SYM2, SYMX
        matches.Should().NotContain("SYM12");
    }

    [Fact]
    public async Task GetMatchingSymbols_ComplexPattern()
    {
        // Arrange
        CreateSymbolDir("CON.EP.G25");
        CreateSymbolDir("CON.EP.H25");
        CreateSymbolDir("CON.EP.M25");
        CreateSymbolDir("ABC.EP.G25");
        CreateSymbolDir("CON.FX.G25");

        // Act
        var matches = await _index.GetMatchingSymbolsAsync("CON.EP.*", StorageScope.Local);

        // Assert
        matches.Should().HaveCount(3);
        matches.Should().Contain("CON.EP.G25");
        matches.Should().Contain("CON.EP.H25");
        matches.Should().Contain("CON.EP.M25");
    }

    [Fact]
    public async Task GetMatchingSymbols_NoMatch_ReturnsEmpty()
    {
        // Arrange
        CreateSymbolDir("BTCUSDT");

        // Act
        var matches = await _index.GetMatchingSymbolsAsync("ETH*", StorageScope.Local);

        // Assert
        matches.Should().BeEmpty();
    }

    [Fact]
    public void SymbolExists_ExistingSymbol_ReturnsTrue()
    {
        CreateSymbolDir("EXISTS");
        _index.SymbolExists("EXISTS", StorageScope.Local).Should().BeTrue();
    }

    [Fact]
    public void SymbolExists_NonExistingSymbol_ReturnsFalse()
    {
        _index.SymbolExists("NONEXISTENT", StorageScope.Local).Should().BeFalse();
    }

    [Fact]
    public async Task InvalidateCache_ClearsCache()
    {
        // Arrange
        CreateSymbolDir("CACHE_TEST");
        _ = await _index.GetMatchingSymbolsAsync("*", StorageScope.Local);

        // Act
        _index.InvalidateCache(StorageScope.Local);
        Directory.Delete(Path.Combine(_tempPath, "CACHE_TEST"));
        var afterInvalidate = await _index.GetMatchingSymbolsAsync("*", StorageScope.Local);

        // Assert
        afterInvalidate.Should().NotContain("CACHE_TEST");
    }

    private void CreateSymbolDir(string symbol)
    {
        Directory.CreateDirectory(Path.Combine(_tempPath, symbol));
    }
}
