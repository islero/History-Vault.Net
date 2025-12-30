using System.Runtime.InteropServices;
using HistoryVault.Configuration;
using HistoryVault.Extensions;
using HistoryVault.Models;

namespace HistoryVault.Storage;

/// <summary>
/// Resolves storage paths for market data files with cross-platform support.
/// </summary>
public sealed class StoragePathResolver
{
    private readonly HistoryVaultOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoragePathResolver"/> class.
    /// </summary>
    /// <param name="options">The configuration options.</param>
    public StoragePathResolver(HistoryVaultOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Gets the base storage path for the specified scope.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <returns>The base storage path.</returns>
    public string GetStoragePath(StorageScope scope)
    {
        if (scope == StorageScope.Local)
        {
            return _options.LocalBasePath
                ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "history-vault");
        }

        if (!string.IsNullOrEmpty(_options.GlobalBasePath))
        {
            return _options.GlobalBasePath;
        }

        return GetGlobalStoragePath();
    }

    /// <summary>
    /// Gets the directory path for a symbol.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <param name="symbol">The symbol name.</param>
    /// <returns>The symbol directory path.</returns>
    public string GetSymbolPath(StorageScope scope, string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        string sanitizedSymbol = SanitizeFileName(symbol);
        return Path.Combine(GetStoragePath(scope), sanitizedSymbol);
    }

    /// <summary>
    /// Gets the directory path for a symbol and timeframe.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <param name="symbol">The symbol name.</param>
    /// <param name="timeframe">The timeframe.</param>
    /// <returns>The timeframe directory path.</returns>
    public string GetTimeframePath(StorageScope scope, string symbol, CandlestickInterval timeframe)
    {
        return Path.Combine(GetSymbolPath(scope, symbol), timeframe.ToShortCode());
    }

    /// <summary>
    /// Gets the directory path for a year within a symbol/timeframe.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <param name="symbol">The symbol name.</param>
    /// <param name="timeframe">The timeframe.</param>
    /// <param name="year">The year.</param>
    /// <returns>The year directory path.</returns>
    public string GetYearPath(StorageScope scope, string symbol, CandlestickInterval timeframe, int year)
    {
        return Path.Combine(GetTimeframePath(scope, symbol, timeframe), year.ToString("D4"));
    }

    /// <summary>
    /// Gets the file path for a specific month of data.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <param name="symbol">The symbol name.</param>
    /// <param name="timeframe">The timeframe.</param>
    /// <param name="year">The year.</param>
    /// <param name="month">The month (1-12).</param>
    /// <param name="compressed">Whether the file is compressed.</param>
    /// <returns>The file path.</returns>
    public string GetMonthFilePath(
        StorageScope scope,
        string symbol,
        CandlestickInterval timeframe,
        int year,
        int month,
        bool compressed)
    {
        string extension = CompressionHandler.GetFileExtension(compressed);
        string fileName = $"{month:D2}{extension}";
        return Path.Combine(GetYearPath(scope, symbol, timeframe, year), fileName);
    }

    /// <summary>
    /// Gets all existing data files for a symbol and timeframe.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <param name="symbol">The symbol name.</param>
    /// <param name="timeframe">The timeframe.</param>
    /// <returns>An enumerable of file paths sorted chronologically.</returns>
    public IEnumerable<string> GetExistingDataFiles(StorageScope scope, string symbol, CandlestickInterval timeframe)
    {
        string timeframePath = GetTimeframePath(scope, symbol, timeframe);

        if (!Directory.Exists(timeframePath))
        {
            yield break;
        }

        var yearDirs = Directory.GetDirectories(timeframePath)
            .Where(d => int.TryParse(Path.GetFileName(d), out _))
            .OrderBy(d => d);

        foreach (string yearDir in yearDirs)
        {
            var files = Directory.GetFiles(yearDir, "*.bin*")
                .OrderBy(f => Path.GetFileName(f));

            foreach (string file in files)
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// Gets all existing data files for a symbol and timeframe within a date range.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <param name="symbol">The symbol name.</param>
    /// <param name="timeframe">The timeframe.</param>
    /// <param name="startDate">The start of the date range.</param>
    /// <param name="endDate">The end of the date range.</param>
    /// <returns>An enumerable of file paths within the range.</returns>
    public IEnumerable<string> GetDataFilesInRange(
        StorageScope scope,
        string symbol,
        CandlestickInterval timeframe,
        DateTime startDate,
        DateTime endDate)
    {
        string timeframePath = GetTimeframePath(scope, symbol, timeframe);

        if (!Directory.Exists(timeframePath))
        {
            yield break;
        }

        for (int year = startDate.Year; year <= endDate.Year; year++)
        {
            string yearPath = Path.Combine(timeframePath, year.ToString("D4"));
            if (!Directory.Exists(yearPath))
            {
                continue;
            }

            int startMonth = year == startDate.Year ? startDate.Month : 1;
            int endMonth = year == endDate.Year ? endDate.Month : 12;

            for (int month = startMonth; month <= endMonth; month++)
            {
                string compressedPath = Path.Combine(yearPath, $"{month:D2}.bin.gz");
                string uncompressedPath = Path.Combine(yearPath, $"{month:D2}.bin");

                if (File.Exists(compressedPath))
                {
                    yield return compressedPath;
                }
                else if (File.Exists(uncompressedPath))
                {
                    yield return uncompressedPath;
                }
            }
        }
    }

    /// <summary>
    /// Gets all symbol directories in the storage.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <returns>An enumerable of symbol names.</returns>
    public IEnumerable<string> GetAllSymbols(StorageScope scope)
    {
        string basePath = GetStoragePath(scope);

        if (!Directory.Exists(basePath))
        {
            yield break;
        }

        foreach (string dir in Directory.GetDirectories(basePath))
        {
            yield return Path.GetFileName(dir);
        }
    }

    /// <summary>
    /// Gets all available timeframes for a symbol.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <param name="symbol">The symbol name.</param>
    /// <returns>An enumerable of available timeframes.</returns>
    public IEnumerable<CandlestickInterval> GetAvailableTimeframes(StorageScope scope, string symbol)
    {
        string symbolPath = GetSymbolPath(scope, symbol);

        if (!Directory.Exists(symbolPath))
        {
            yield break;
        }

        foreach (string dir in Directory.GetDirectories(symbolPath))
        {
            string code = Path.GetFileName(dir);
            if (TryParseTimeframeCode(code, out var timeframe))
            {
                yield return timeframe;
            }
        }
    }

    /// <summary>
    /// Ensures the directory for a file path exists.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    public void EnsureDirectoryExists(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Extracts the year and month from a data file path.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>A tuple of (year, month).</returns>
    public static (int Year, int Month) ParseFilePath(string filePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);
        }

        string? yearDir = Path.GetFileName(Path.GetDirectoryName(filePath));

        if (!int.TryParse(yearDir, out int year) || !int.TryParse(fileName, out int month))
        {
            throw new ArgumentException($"Cannot parse year/month from path: {filePath}", nameof(filePath));
        }

        return (year, month);
    }

    private static string GetGlobalStoragePath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HistoryVault");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "HistoryVault");
        }

        // Linux and others
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "HistoryVault");
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    private static bool TryParseTimeframeCode(string code, out CandlestickInterval timeframe)
    {
        try
        {
            timeframe = CandlestickIntervalExtensions.FromShortCode(code);
            return true;
        }
        catch
        {
            timeframe = default;
            return false;
        }
    }
}
