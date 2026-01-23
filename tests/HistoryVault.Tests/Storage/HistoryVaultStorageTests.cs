using FluentAssertions;
using HistoryVault.Configuration;
using HistoryVault.Models;
using HistoryVault.Storage;
using Xunit;

namespace HistoryVault.Tests.Storage;

public class HistoryVaultStorageTests : IDisposable
{
    private readonly string _tempPath;
    private readonly HistoryVaultOptions _options;
    private readonly HistoryVaultStorage _vault;

    public HistoryVaultStorageTests()
    {
        _tempPath = TestHelpers.GetTempDirectory();
        _options = new HistoryVaultOptions
        {
            BasePathOverride = _tempPath,
            DefaultScope = StorageScope.Local
        };
        _vault = new HistoryVaultStorage(_options);
    }

    public void Dispose()
    {
        _vault.DisposeAsync().AsTask().GetAwaiter().GetResult();
        TestHelpers.CleanupTempDirectory(_tempPath);
    }

    [Fact]
    public async Task Save_WithCompression_CreatesGzFile()
    {
        // Arrange
        var symbolData = TestHelpers.GenerateSymbolData(
            "BTCUSDT",
            CandlestickInterval.M1,
            100,
            new DateTime(2025, 1, 15, 10, 0, 0));

        var options = new SaveOptions
        {
            UseCompression = true,
            Scope = StorageScope.Local
        };

        // Act
        await _vault.SaveAsync(symbolData, options);

        // Assert
        var expectedPath = Path.Combine(_tempPath, "BTCUSDT", "1m", "2025", "01.bin.gz");
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Fact]
    public async Task Save_WithoutCompression_CreatesBinFile()
    {
        // Arrange
        var symbolData = TestHelpers.GenerateSymbolData(
            "ETHUSDT",
            CandlestickInterval.H1,
            24,
            new DateTime(2025, 2, 1, 0, 0, 0));

        var options = new SaveOptions
        {
            UseCompression = false,
            Scope = StorageScope.Local
        };

        // Act
        await _vault.SaveAsync(symbolData, options);

        // Assert
        var expectedPath = Path.Combine(_tempPath, "ETHUSDT", "1h", "2025", "02.bin");
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Fact]
    public async Task Save_PartialOverwrite_PreservesNonOverlappingData()
    {
        // Arrange
        var symbol = "PARTIAL_TEST";

        // First save: Jan 1-15
        var firstData = TestHelpers.GenerateSymbolData(
            symbol, CandlestickInterval.M1, 1440 * 15,
            new DateTime(2025, 1, 1, 0, 0, 0));
        await _vault.SaveAsync(firstData, new SaveOptions { AllowPartialOverwrite = true, UseCompression = false });

        // Get count of original candles
        var originalCount = firstData.Timeframes[0].Candlesticks.Count;

        // Second save: Jan 10-20 (overlaps Jan 10-15)
        var secondData = TestHelpers.GenerateSymbolData(
            symbol, CandlestickInterval.M1, 1440 * 10,
            new DateTime(2025, 1, 10, 0, 0, 0));
        await _vault.SaveAsync(secondData, new SaveOptions { AllowPartialOverwrite = true, UseCompression = false });

        // Act
        var loadOptions = LoadOptions.ForSymbol(symbol, new DateTime(2025, 1, 1), new DateTime(2025, 1, 31), CandlestickInterval.M1);
        var result = await _vault.LoadAsync(loadOptions);

        // Assert
        result.Should().NotBeNull();
        var candles = result!.Timeframes[0].Candlesticks;

        // Should have candles from Jan 1 (preserved) + new Jan 10-20
        candles.Should().NotBeEmpty();
        candles[0].OpenTime.Should().BeOnOrAfter(new DateTime(2025, 1, 1));
    }

    [Fact]
    public async Task Save_PartialOverwrite_ReplacesOverlappingData()
    {
        // Arrange
        var symbol = "OVERLAP_TEST";
        var startTime = new DateTime(2025, 3, 1, 0, 0, 0);

        // First save
        var firstData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 24, startTime);
        var originalFirstCandle = firstData.Timeframes[0].Candlesticks[0].Clone();
        await _vault.SaveAsync(firstData, new SaveOptions { AllowPartialOverwrite = true, UseCompression = false });

        // Create new data with different values at the same time
        var newData = new SymbolDataV2
        {
            Symbol = symbol,
            Timeframes = new List<TimeframeV2>
            {
                new TimeframeV2
                {
                    Timeframe = CandlestickInterval.H1,
                    Candlesticks = new List<CandlestickV2>
                    {
                        new()
                        {
                            OpenTime = startTime,
                            CloseTime = startTime.AddHours(1).AddTicks(-1),
                            Open = 999m,
                            High = 1000m,
                            Low = 998m,
                            Close = 999.5m,
                            Volume = 12345m
                        }
                    }
                }
            }
        };

        // Act
        await _vault.SaveAsync(newData, new SaveOptions { AllowPartialOverwrite = true, UseCompression = false });

        var loadOptions = LoadOptions.ForSymbol(symbol, startTime, startTime.AddDays(1), CandlestickInterval.H1);
        var result = await _vault.LoadAsync(loadOptions);

        // Assert
        result.Should().NotBeNull();
        var firstCandle = result!.Timeframes[0].Candlesticks.First(c => c.OpenTime == startTime);
        firstCandle.Open.Should().Be(999m);  // New data, not original
        firstCandle.Volume.Should().Be(12345m);
    }

    [Fact]
    public async Task Load_WithWarmup_ReturnsExtraCandles()
    {
        // Arrange
        var symbol = "WARMUP_TEST";
        var startTime = new DateTime(2025, 4, 1, 0, 0, 0);
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 48, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        // Request data starting at hour 24, with 10 warmup candles
        var loadOptions = new LoadOptions
        {
            Symbol = symbol,
            StartDate = startTime.AddHours(24),
            EndDate = startTime.AddHours(48),
            WarmupCandlesCount = 10,
            Timeframes = new[] { CandlestickInterval.H1 }
        };

        // Act
        var result = await _vault.LoadAsync(loadOptions);

        // Assert
        result.Should().NotBeNull();
        var candles = result!.Timeframes[0].Candlesticks;

        // Should include warmup candles before the start date
        candles.Any(c => c.OpenTime < loadOptions.StartDate).Should().BeTrue();
    }

    [Fact]
    public async Task Load_WildcardSymbol_ReturnsAllMatches()
    {
        // Arrange
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData("BTC.USD", CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData("BTC.EUR", CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData("ETH.USD", CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });

        var loadOptions = new LoadOptions
        {
            Symbol = "BTC.*",
            Timeframes = new[] { CandlestickInterval.M1 }
        };

        // Act
        var results = await _vault.LoadMultipleAsync(loadOptions);

        // Assert
        results.Should().HaveCount(2);
        results.Select(r => r.Symbol).Should().BeEquivalentTo(new[] { "BTC.USD", "BTC.EUR" });
    }

    [Fact]
    public async Task Load_WithAggregation_CreatesRequestedTimeframe()
    {
        // Arrange
        var symbol = "AGG_TEST";
        var startTime = new DateTime(2025, 5, 1, 0, 0, 0);

        // Save M1 data (60 candles = 1 hour)
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, 60, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        // Request H1 data with aggregation enabled
        var loadOptions = new LoadOptions
        {
            Symbol = symbol,
            Timeframes = new[] { CandlestickInterval.H1 },
            StartDate = startTime,
            EndDate = startTime.AddHours(1),
            AllowAggregation = true
        };

        // Act
        var result = await _vault.LoadAsync(loadOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Timeframes.Should().HaveCount(1);
        result.Timeframes[0].Timeframe.Should().Be(CandlestickInterval.H1);
        result.Timeframes[0].Candlesticks.Should().HaveCount(1);
    }

    [Fact]
    public async Task Load_NonExistentSymbol_ReturnsNull()
    {
        // Arrange
        var loadOptions = new LoadOptions
        {
            Symbol = "NON_EXISTENT_SYMBOL",
            Timeframes = new[] { CandlestickInterval.M1 }
        };

        // Act
        var result = await _vault.LoadAsync(loadOptions);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckAvailability_ReturnsReport()
    {
        // Arrange
        var symbol = "AVAIL_REPORT";
        var startTime = new DateTime(2025, 6, 1, 0, 0, 0);
        var endTime = startTime.AddHours(24);
        var symbolData = TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 24, startTime);
        await _vault.SaveAsync(symbolData, new SaveOptions { UseCompression = false });

        // Act
        var report = await _vault.CheckAvailabilityAsync(symbol, CandlestickInterval.H1, startTime, endTime);

        // Assert - verify report is returned and has correct basic properties
        report.Should().NotBeNull();
        report.Symbol.Should().Be(symbol);
        report.Timeframe.Should().Be(CandlestickInterval.H1);
        report.QueryStart.Should().Be(startTime);
        report.QueryEnd.Should().Be(endTime);
    }

    [Fact]
    public async Task GetMatchingSymbols_ReturnsCorrectSymbols()
    {
        // Arrange
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData("TEST.A", CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData("TEST.B", CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData("OTHER.A", CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });

        // Act
        var results = await _vault.GetMatchingSymbolsAsync("TEST.*");

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain("TEST.A");
        results.Should().Contain("TEST.B");
    }

    [Fact]
    public async Task DeleteSymbol_RemovesAllData()
    {
        // Arrange
        var symbol = "DELETE_TEST";
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 5, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });

        // Act
        var deleted = await _vault.DeleteSymbolAsync(symbol, StorageScope.Local);

        // Assert
        deleted.Should().BeTrue();
        var loadResult = await _vault.LoadAsync(new LoadOptions { Symbol = symbol });
        loadResult.Should().BeNull();
    }

    [Fact]
    public async Task GetAvailableTimeframes_ReturnsAllTimeframes()
    {
        // Arrange
        var symbol = "TF_TEST";
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.M5, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });
        await _vault.SaveAsync(TestHelpers.GenerateSymbolData(symbol, CandlestickInterval.H1, 10, DateTime.UtcNow),
            new SaveOptions { UseCompression = false });

        // Act
        var timeframes = await _vault.GetAvailableTimeframesAsync(symbol);

        // Assert
        timeframes.Should().HaveCount(3);
        timeframes.Should().Contain(CandlestickInterval.M1);
        timeframes.Should().Contain(CandlestickInterval.M5);
        timeframes.Should().Contain(CandlestickInterval.H1);
    }

    [Fact]
    public async Task SaveAndLoad_LargeMultiTimeframeWithWarmup_DataIntegrityPreserved()
    {
        // Arrange
        const string symbol = "MULTI_TF_WARMUP";
        const int warmupCandleCount = 50;
        const int mainCandleCount = 500;
        const int totalCandleCount = warmupCandleCount + mainCandleCount;

        var startTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var warmupEndTime = startTime.AddMinutes(warmupCandleCount); // After 50 M1 candles

        // Use timeframes that work well with 550 M1 candles (~9 hours of data)
        var timeframes = new[]
        {
            CandlestickInterval.M1,
            CandlestickInterval.M5,
            CandlestickInterval.M15,
            CandlestickInterval.M30,
            CandlestickInterval.H1
        };

        // Save each timeframe separately to avoid cross-timeframe interference
        foreach (var timeframe in timeframes)
        {
            var data = TestHelpers.GenerateSymbolData(symbol, timeframe, totalCandleCount, startTime);
            await _vault.SaveAsync(data, new SaveOptions { UseCompression = true, Scope = StorageScope.Local });
        }

        // Act - Load with warmup simulation
        var loadOptions = new LoadOptions
        {
            Symbol = symbol,
            StartDate = warmupEndTime,
            EndDate = startTime.AddMinutes(totalCandleCount * 60), // Enough range for H1
            WarmupCandlesCount = warmupCandleCount,
            Timeframes = timeframes,
            Scope = StorageScope.Local
        };

        var loadedData = await _vault.LoadAsync(loadOptions);

        // Assert
        loadedData.Should().NotBeNull();
        loadedData!.Symbol.Should().Be(symbol);
        loadedData.Timeframes.Should().HaveCount(timeframes.Length);

        // Verify each timeframe that was loaded
        foreach (var loadedTf in loadedData.Timeframes)
        {
            // Verify warmup candles are included (candles before warmupEndTime)
            var warmupCandles = loadedTf.Candlesticks.Where(c => c.OpenTime < warmupEndTime).ToList();
            warmupCandles.Should().NotBeEmpty($"Timeframe {loadedTf.Timeframe} should have warmup candles");

            // Regenerate original data for comparison (uses same seed)
            var originalCandles = TestHelpers.GenerateCandles(totalCandleCount, loadedTf.Timeframe, startTime);

            // Verify each candle's data integrity
            foreach (var loadedCandle in loadedTf.Candlesticks)
            {
                var originalCandle = originalCandles.FirstOrDefault(c => c.OpenTime == loadedCandle.OpenTime);
                if (originalCandle != null)
                {
                    AssertCandleEquality(originalCandle, loadedCandle, loadedTf.Timeframe);
                }
            }
        }
    }

    [Fact]
    public async Task SaveAndLoad_LargeMultiTimeframe_AllCandlesPreservedExactly()
    {
        // Arrange
        const string symbol = "LARGE_MULTI_TF";
        const int candleCount = 1000;

        var startTime = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        var timeframes = new[]
        {
            CandlestickInterval.M1,
            CandlestickInterval.M5,
            CandlestickInterval.M15,
            CandlestickInterval.M30,
            CandlestickInterval.H1
        };

        // Store original candles for comparison
        var originalCandlesByTimeframe = new Dictionary<CandlestickInterval, List<CandlestickV2>>();

        // Save each timeframe separately
        foreach (var timeframe in timeframes)
        {
            var candles = TestHelpers.GenerateCandles(candleCount, timeframe, startTime);
            originalCandlesByTimeframe[timeframe] = candles;

            var data = new SymbolDataV2
            {
                Symbol = symbol,
                Timeframes = new List<TimeframeV2>
                {
                    new() { Timeframe = timeframe, Candlesticks = candles }
                }
            };
            await _vault.SaveAsync(data, new SaveOptions { UseCompression = true, Scope = StorageScope.Local });
        }

        // Act - Load all data
        var loadOptions = new LoadOptions
        {
            Symbol = symbol,
            StartDate = startTime,
            EndDate = startTime.AddDays(365), // Large range to cover all data
            Timeframes = timeframes,
            Scope = StorageScope.Local
        };

        var loadedData = await _vault.LoadAsync(loadOptions);

        // Assert
        loadedData.Should().NotBeNull();
        loadedData!.Timeframes.Should().HaveCount(timeframes.Length);

        foreach (var timeframe in timeframes)
        {
            var loadedTf = loadedData.Timeframes.First(t => t.Timeframe == timeframe);
            var originalCandles = originalCandlesByTimeframe[timeframe];

            loadedTf.Candlesticks.Should().HaveCount(
                originalCandles.Count,
                $"Timeframe {timeframe} should have same candle count");

            for (int i = 0; i < originalCandles.Count; i++)
            {
                AssertCandleEquality(originalCandles[i], loadedTf.Candlesticks[i], timeframe);
            }
        }
    }

    [Fact]
    public async Task SaveAndLoad_LargeMultiTimeframe_CandleCountsMatch()
    {
        // Arrange
        const string symbol = "CANDLE_COUNT_TEST";
        const int candleCount = 2000; // Large dataset

        var startTime = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var timeframes = new[]
        {
            CandlestickInterval.M1,
            CandlestickInterval.M5,
            CandlestickInterval.M15,
            CandlestickInterval.M30,
            CandlestickInterval.H1,
            CandlestickInterval.H4,
            CandlestickInterval.D1
        };

        // Store expected candle counts before saving
        var expectedCandleCounts = new Dictionary<CandlestickInterval, int>();

        // Save each timeframe with its own candle count
        foreach (var timeframe in timeframes)
        {
            var candles = TestHelpers.GenerateCandles(candleCount, timeframe, startTime);
            expectedCandleCounts[timeframe] = candles.Count;

            var data = new SymbolDataV2
            {
                Symbol = symbol,
                Timeframes = new List<TimeframeV2>
                {
                    new() { Timeframe = timeframe, Candlesticks = candles }
                }
            };
            await _vault.SaveAsync(data, new SaveOptions { UseCompression = true, Scope = StorageScope.Local });
        }

        // Act - Load all data
        var loadOptions = new LoadOptions
        {
            Symbol = symbol,
            StartDate = startTime,
            EndDate = startTime.AddYears(10), // Very large range to ensure all data is loaded
            Timeframes = timeframes,
            Scope = StorageScope.Local
        };

        var loadedData = await _vault.LoadAsync(loadOptions);

        // Assert - Verify candle counts match for each timeframe
        loadedData.Should().NotBeNull();
        loadedData!.Timeframes.Should().HaveCount(timeframes.Length,
            "All timeframes should be present in loaded data");

        foreach (var timeframe in timeframes)
        {
            var loadedTf = loadedData.Timeframes.FirstOrDefault(t => t.Timeframe == timeframe);
            loadedTf.Should().NotBeNull($"Timeframe {timeframe} should exist in loaded data");

            int expectedCount = expectedCandleCounts[timeframe];
            loadedTf!.Candlesticks.Should().HaveCount(expectedCount,
                $"Timeframe {timeframe}: expected {expectedCount} candles, got {loadedTf.Candlesticks.Count}");
        }

        // Verify total candle count
        var totalOriginalCandles = expectedCandleCounts.Values.Sum();
        var totalLoadedCandles = loadedData.Timeframes.Sum(tf => tf.Candlesticks.Count);
        totalLoadedCandles.Should().Be(totalOriginalCandles,
            $"Total candle count should match: expected {totalOriginalCandles}, got {totalLoadedCandles}");
    }

    [Fact]
    public async Task SaveAndLoad_MultiTimeframeGenerator_DataMatchesBeforeAndAfter()
    {
        // Arrange
        const string symbol = "MULTI_TF_GENERATOR_TEST";
        const int candleCountPerTimeframe = 1000;
        var startTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Generate multi-timeframe data using the new generator
        var originalData = TestHelpers.GenerateMultiTimeframeSymbolData(
            symbol,
            candleCountPerTimeframe,
            startTime);

        // Verify we have all expected timeframes
        originalData.Timeframes.Should().HaveCount(TestHelpers.AllTimeframes.Length);

        await _vault.SaveAsync(originalData, new SaveOptions
        {
            UseCompression = true,
            Scope = StorageScope.Local,
            AllowPartialOverwrite = false
        });

        // Act - Load all timeframes
        var loadOptions = new LoadOptions
        {
            Symbol = symbol,
            StartDate = startTime,
            EndDate = startTime.AddYears(15), // Large range to cover all timeframes
            Timeframes = TestHelpers.AllTimeframes,
            Scope = StorageScope.Local
        };

        var loadedData = await _vault.LoadAsync(loadOptions);

        // Assert
        loadedData.Should().NotBeNull();
        loadedData!.Symbol.Should().Be(symbol);
        loadedData.Timeframes.Should().HaveCount(TestHelpers.AllTimeframes.Length,
            "All timeframes should be loaded");

        // Compare each timeframe
        foreach (var originalTf in originalData.Timeframes)
        {
            var loadedTf = loadedData.Timeframes.FirstOrDefault(t => t.Timeframe == originalTf.Timeframe);
            loadedTf.Should().NotBeNull($"Timeframe {originalTf.Timeframe} should be present");

            // Verify candle count
            loadedTf!.Candlesticks.Should().HaveCount(originalTf.Candlesticks.Count,
                $"Timeframe {originalTf.Timeframe} should have {originalTf.Candlesticks.Count} candles");

            // Verify each candle field-by-field
            for (int i = 0; i < originalTf.Candlesticks.Count; i++)
            {
                AssertCandleEquality(originalTf.Candlesticks[i], loadedTf.Candlesticks[i], originalTf.Timeframe);
            }
        }
    }

    [Fact]
    public async Task SaveAndLoad_MultiTimeframeGenerator_LargeDataset_IntegrityPreserved()
    {
        // Arrange
        const string symbol = "MULTI_TF_LARGE_TEST";
        const int candleCountPerTimeframe = 500;
        var startTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Generate multi-timeframe data
        var originalData = TestHelpers.GenerateMultiTimeframeSymbolData(
            symbol,
            candleCountPerTimeframe,
            startTime);

        // Store original data for comparison (deep copy the counts and first/last candles)
        var originalStats = originalData.Timeframes.ToDictionary(
            tf => tf.Timeframe,
            tf => new
            {
                Count = tf.Candlesticks.Count,
                FirstCandle = tf.Candlesticks.First().Clone(),
                LastCandle = tf.Candlesticks.Last().Clone(),
                TotalVolume = tf.Candlesticks.Sum(c => c.Volume)
            });

        // Save each timeframe
        foreach (var timeframe in originalData.Timeframes)
        {
            var singleTimeframeData = new SymbolDataV2
            {
                Symbol = symbol,
                Timeframes = new List<TimeframeV2> { timeframe }
            };
            await _vault.SaveAsync(singleTimeframeData, new SaveOptions
            {
                UseCompression = true,
                Scope = StorageScope.Local
            });
        }

        // Act - Load all timeframes
        var loadOptions = new LoadOptions
        {
            Symbol = symbol,
            StartDate = startTime,
            EndDate = startTime.AddYears(10),
            Timeframes = TestHelpers.AllTimeframes,
            Scope = StorageScope.Local
        };

        var loadedData = await _vault.LoadAsync(loadOptions);

        // Assert
        loadedData.Should().NotBeNull();
        loadedData!.Timeframes.Should().HaveCount(TestHelpers.AllTimeframes.Length);

        foreach (var loadedTf in loadedData.Timeframes)
        {
            var stats = originalStats[loadedTf.Timeframe];

            // Verify count
            loadedTf.Candlesticks.Should().HaveCount(stats.Count,
                $"Timeframe {loadedTf.Timeframe} count mismatch");

            // Verify first candle
            AssertCandleEquality(stats.FirstCandle, loadedTf.Candlesticks.First(), loadedTf.Timeframe);

            // Verify last candle
            AssertCandleEquality(stats.LastCandle, loadedTf.Candlesticks.Last(), loadedTf.Timeframe);

            // Verify total volume is preserved
            var loadedTotalVolume = loadedTf.Candlesticks.Sum(c => c.Volume);
            loadedTotalVolume.Should().Be(stats.TotalVolume,
                $"Timeframe {loadedTf.Timeframe} total volume mismatch");
        }

        // Verify total candle count across all timeframes
        var originalTotalCandles = originalStats.Values.Sum(s => s.Count);
        var loadedTotalCandles = loadedData.Timeframes.Sum(tf => tf.Candlesticks.Count);
        loadedTotalCandles.Should().Be(originalTotalCandles,
            $"Total candles: expected {originalTotalCandles}, got {loadedTotalCandles}");
    }

    [Fact]
    public async Task SaveAndLoad_MultiTimeframeGenerator_WithCompression_DataPreserved()
    {
        // Arrange
        const string symbol = "MULTI_TF_COMPRESSION_TEST";
        const int candleCountPerTimeframe = 200;
        var startTime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var originalData = TestHelpers.GenerateMultiTimeframeSymbolData(
            symbol,
            candleCountPerTimeframe,
            startTime);

        // Save with compression enabled
        foreach (var timeframe in originalData.Timeframes)
        {
            var singleTimeframeData = new SymbolDataV2
            {
                Symbol = symbol,
                Timeframes = new List<TimeframeV2> { timeframe }
            };
            await _vault.SaveAsync(singleTimeframeData, new SaveOptions
            {
                UseCompression = true,
                Scope = StorageScope.Local
            });
        }

        // Act - Load data back
        var loadOptions = new LoadOptions
        {
            Symbol = symbol,
            StartDate = startTime,
            EndDate = startTime.AddYears(10),
            Timeframes = TestHelpers.AllTimeframes,
            Scope = StorageScope.Local
        };

        var loadedData = await _vault.LoadAsync(loadOptions);

        // Assert - Verify all data is preserved exactly after compression/decompression
        loadedData.Should().NotBeNull();

        foreach (var originalTf in originalData.Timeframes)
        {
            var loadedTf = loadedData!.Timeframes.First(t => t.Timeframe == originalTf.Timeframe);

            loadedTf.Candlesticks.Should().HaveCount(originalTf.Candlesticks.Count);

            // Full comparison of all candles
            for (int i = 0; i < originalTf.Candlesticks.Count; i++)
            {
                var original = originalTf.Candlesticks[i];
                var loaded = loadedTf.Candlesticks[i];

                AssertCandleEquality(original, loaded, originalTf.Timeframe);
            }
        }
    }

    private static void AssertCandleEquality(CandlestickV2 expected, CandlestickV2 actual, CandlestickInterval timeframe)
    {
        actual.OpenTime.Should().Be(expected.OpenTime, $"OpenTime mismatch for {timeframe}");
        actual.CloseTime.Should().Be(expected.CloseTime, $"CloseTime mismatch for {timeframe}");
        actual.Open.Should().Be(expected.Open, $"Open price mismatch for {timeframe} at {expected.OpenTime}");
        actual.High.Should().Be(expected.High, $"High price mismatch for {timeframe} at {expected.OpenTime}");
        actual.Low.Should().Be(expected.Low, $"Low price mismatch for {timeframe} at {expected.OpenTime}");
        actual.Close.Should().Be(expected.Close, $"Close price mismatch for {timeframe} at {expected.OpenTime}");
        actual.Volume.Should().Be(expected.Volume, $"Volume mismatch for {timeframe} at {expected.OpenTime}");
    }
}
