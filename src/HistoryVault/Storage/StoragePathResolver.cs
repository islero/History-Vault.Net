using System.Runtime.InteropServices;
using System.Text;
using HistoryVault.Configuration;
using HistoryVault.Extensions;
using HistoryVault.Models;

namespace HistoryVault.Storage;

/// <summary>
/// Resolves storage paths for market data files with cross-platform support.
/// </summary>
public sealed class StoragePathResolver
{
    private const string EncodedSymbolPrefix = "s_";
    private const string SymbolMetadataFileName = ".symbol";
    private const string LegacyMonthlyTimeframeCode = "1M";

    private readonly string? _basePathOverride;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoragePathResolver"/> class.
    /// </summary>
    /// <param name="options">The configuration options.</param>
    public StoragePathResolver(HistoryVaultOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _basePathOverride = options.BasePathOverride;
    }

    /// <summary>
    /// Gets the base storage path for the specified scope.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <returns>The base storage path.</returns>
    public string GetStoragePath(StorageScope scope)
    {
        // If override is set, use it for all scopes (primarily for testing)
        if (!string.IsNullOrEmpty(_basePathOverride))
        {
            return _basePathOverride;
        }

        return scope == StorageScope.Local
            ? GetLocalStoragePath()
            : GetGlobalStoragePath();
    }

    /// <summary>
    /// Gets the local storage path (inside the project directory).
    /// </summary>
    private static string GetLocalStoragePath() => Path.Combine(Directory.GetCurrentDirectory(), "data", "history-vault");

    /// <summary>
    /// Gets the directory path for a symbol.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <param name="symbol">The symbol name.</param>
    /// <returns>The symbol directory path.</returns>
    public string GetSymbolPath(StorageScope scope, string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        string basePath = GetStoragePath(scope);
        string encodedSymbol = EncodeSymbolPathSegment(symbol);
        string preferredPath = Path.Combine(basePath, encodedSymbol);

        if (Directory.Exists(preferredPath))
        {
            return preferredPath;
        }

        string legacySymbol = SanitizeFileNameLegacy(symbol);
        string legacyPath = Path.Combine(basePath, legacySymbol);
        return Directory.Exists(legacyPath) ? legacyPath : preferredPath;
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
        string symbolPath = GetSymbolPath(scope, symbol);
        string preferredPath = Path.Combine(symbolPath, timeframe.ToShortCode());

        if (Directory.Exists(preferredPath))
        {
            return preferredPath;
        }

        if (timeframe == CandlestickInterval.MN1)
        {
            string? legacyMonthlyPath = FindExactTimeframeDirectory(symbolPath, LegacyMonthlyTimeframeCode);
            if (legacyMonthlyPath != null)
            {
                return legacyMonthlyPath;
            }
        }

        return preferredPath;
    }

    /// <summary>
    /// Gets the directory path for a year within a symbol/timeframe.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <param name="symbol">The symbol name.</param>
    /// <param name="timeframe">The timeframe.</param>
    /// <param name="year">The year.</param>
    /// <returns>The year directory path.</returns>
    public string GetYearPath(StorageScope scope, string symbol, CandlestickInterval timeframe, int year) => Path.Combine(GetTimeframePath(scope, symbol, timeframe), year.ToString("D4"));

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

        IOrderedEnumerable<string> yearDirs = Directory.GetDirectories(timeframePath)
            .Where(d => int.TryParse(Path.GetFileName(d), out _))
            .OrderBy(d => d);

        foreach (string yearDir in yearDirs)
        {
            IOrderedEnumerable<string> files = Directory.GetFiles(yearDir, "*.bin*")
                .OrderBy(Path.GetFileName);

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
            yield return ReadSymbolName(dir);
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
            if (TryParseTimeframeCode(code, out CandlestickInterval timeframe))
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
    /// Persists the original symbol name alongside the symbol directory.
    /// </summary>
    /// <param name="scope">The storage scope.</param>
    /// <param name="symbol">The original symbol name.</param>
    public void WriteSymbolMetadata(StorageScope scope, string symbol)
    {
        string symbolPath = GetSymbolPath(scope, symbol);
        Directory.CreateDirectory(symbolPath);

        string metadataPath = Path.Combine(symbolPath, SymbolMetadataFileName);
        File.WriteAllText(metadataPath, symbol, Encoding.UTF8);
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

    /// <summary>
    /// Gets the global storage path (OS-specific application data directory).
    /// </summary>
    private static string GetGlobalStoragePath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "HistoryVault");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "HistoryVault");
        }

        string? xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return Path.Combine(xdgDataHome, "HistoryVault");
        }

        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userHome, ".local", "share", "HistoryVault");
    }

    private static string EncodeSymbolPathSegment(string symbol)
    {
        if (!NeedsEncoding(symbol))
        {
            return symbol;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(symbol);
        var builder = new StringBuilder(EncodedSymbolPrefix, EncodedSymbolPrefix.Length + (bytes.Length * 2));

        foreach (byte value in bytes)
        {
            _ = builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }

    private static bool NeedsEncoding(string symbol)
    {
        foreach (char c in symbol)
        {
            bool isSafe =
                (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') ||
                (c >= '0' && c <= '9') ||
                c == '.' ||
                c == '-' ||
                c == '_';

            if (!isSafe)
            {
                return true;
            }
        }

        return symbol is "." or ".." || symbol.StartsWith(EncodedSymbolPrefix, StringComparison.Ordinal);
    }

    private static string SanitizeFileNameLegacy(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    private static string ReadSymbolName(string symbolDirectory)
    {
        string metadataPath = Path.Combine(symbolDirectory, SymbolMetadataFileName);
        if (File.Exists(metadataPath))
        {
            try
            {
                string symbol = File.ReadAllText(metadataPath, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(symbol))
                {
                    return symbol;
                }
            }
            catch
            {
                // Fall back to decoding or directory name below.
            }
        }

        string directoryName = Path.GetFileName(symbolDirectory);
        return TryDecodeSymbolPathSegment(directoryName, out string decodedSymbol)
            ? decodedSymbol
            : directoryName;
    }

    private static bool TryDecodeSymbolPathSegment(string segment, out string symbol)
    {
        symbol = string.Empty;

        if (!segment.StartsWith(EncodedSymbolPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string hex = segment[EncodedSymbolPrefix.Length..];
        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            return false;
        }

        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
            {
                return false;
            }
        }

        symbol = Encoding.UTF8.GetString(bytes);
        return true;
    }

    private static string? FindExactTimeframeDirectory(string symbolPath, string code)
    {
        if (!Directory.Exists(symbolPath))
        {
            return null;
        }

        foreach (string dir in Directory.GetDirectories(symbolPath))
        {
            if (string.Equals(Path.GetFileName(dir), code, StringComparison.Ordinal))
            {
                return dir;
            }
        }

        return null;
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
