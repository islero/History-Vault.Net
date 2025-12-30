using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using HistoryVault.Configuration;
using HistoryVault.Models;
using HistoryVault.Storage;

namespace HistoryVault.Indexing;

/// <summary>
/// Provides symbol lookup and pattern matching functionality.
/// </summary>
public sealed class SymbolIndex
{
    private readonly StoragePathResolver _pathResolver;
    private readonly object _lock = new();
    private readonly Dictionary<StorageScope, HashSet<string>> _symbolCache = new();
    private readonly Dictionary<StorageScope, DateTime> _cacheTimestamps = new();
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="SymbolIndex"/> class.
    /// </summary>
    /// <param name="pathResolver">The path resolver.</param>
    public SymbolIndex(StoragePathResolver pathResolver)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    /// <summary>
    /// Gets all symbols matching a pattern.
    /// </summary>
    /// <param name="pattern">The glob pattern (e.g., "BTC*", "CON.EP.*").</param>
    /// <param name="scope">The storage scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of matching symbol names.</returns>
    public Task<IReadOnlyList<string>> GetMatchingSymbolsAsync(
        string pattern,
        StorageScope scope,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var allSymbols = GetAllSymbolsCached(scope);

            if (string.IsNullOrEmpty(pattern) || pattern == "*")
            {
                return (IReadOnlyList<string>)allSymbols.ToList();
            }

            // Check if it's a literal (no wildcards)
            if (!ContainsWildcard(pattern))
            {
                return allSymbols.Contains(pattern)
                    ? new List<string> { pattern }
                    : (IReadOnlyList<string>)Array.Empty<string>();
            }

            // Use glob pattern matching
            var matches = allSymbols.Where(s => MatchesPattern(s, pattern)).ToList();
            return (IReadOnlyList<string>)matches;
        }, ct);
    }

    /// <summary>
    /// Checks if a symbol exists in the storage.
    /// </summary>
    /// <param name="symbol">The symbol to check.</param>
    /// <param name="scope">The storage scope.</param>
    /// <returns>True if the symbol exists; otherwise, false.</returns>
    public bool SymbolExists(string symbol, StorageScope scope)
    {
        string symbolPath = _pathResolver.GetSymbolPath(scope, symbol);
        return Directory.Exists(symbolPath);
    }

    /// <summary>
    /// Gets all available timeframes for a symbol.
    /// </summary>
    /// <param name="symbol">The symbol to query.</param>
    /// <param name="scope">The storage scope.</param>
    /// <returns>A list of available timeframes.</returns>
    public IReadOnlyList<CandlestickInterval> GetAvailableTimeframes(string symbol, StorageScope scope)
    {
        return _pathResolver.GetAvailableTimeframes(scope, symbol).ToList();
    }

    /// <summary>
    /// Invalidates the symbol cache for a specific scope.
    /// </summary>
    /// <param name="scope">The scope to invalidate.</param>
    public void InvalidateCache(StorageScope scope)
    {
        lock (_lock)
        {
            _symbolCache.Remove(scope);
            _cacheTimestamps.Remove(scope);
        }
    }

    /// <summary>
    /// Invalidates all caches.
    /// </summary>
    public void InvalidateAllCaches()
    {
        lock (_lock)
        {
            _symbolCache.Clear();
            _cacheTimestamps.Clear();
        }
    }

    /// <summary>
    /// Adds a symbol to the cache (called after saving new symbol data).
    /// </summary>
    /// <param name="symbol">The symbol to add.</param>
    /// <param name="scope">The storage scope.</param>
    public void AddSymbolToCache(string symbol, StorageScope scope)
    {
        lock (_lock)
        {
            if (_symbolCache.TryGetValue(scope, out var symbols))
            {
                symbols.Add(symbol);
            }
        }
    }

    private HashSet<string> GetAllSymbolsCached(StorageScope scope)
    {
        lock (_lock)
        {
            if (_symbolCache.TryGetValue(scope, out var cached) &&
                _cacheTimestamps.TryGetValue(scope, out var timestamp) &&
                DateTime.UtcNow - timestamp < CacheExpiration)
            {
                return cached;
            }

            var symbols = new HashSet<string>(
                _pathResolver.GetAllSymbols(scope),
                StringComparer.OrdinalIgnoreCase);

            _symbolCache[scope] = symbols;
            _cacheTimestamps[scope] = DateTime.UtcNow;

            return symbols;
        }
    }

    private static bool ContainsWildcard(string pattern)
    {
        return pattern.Contains('*') || pattern.Contains('?');
    }

    private static bool MatchesPattern(string symbol, string pattern)
    {
        // Convert glob pattern to regex-like matching
        // This is a simple implementation supporting * and ?

        int symbolIndex = 0;
        int patternIndex = 0;
        int starIndex = -1;
        int matchIndex = 0;

        while (symbolIndex < symbol.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' ||
                 char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(symbol[symbolIndex])))
            {
                symbolIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex;
                matchIndex = symbolIndex;
                patternIndex++;
            }
            else if (starIndex != -1)
            {
                patternIndex = starIndex + 1;
                matchIndex++;
                symbolIndex = matchIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
