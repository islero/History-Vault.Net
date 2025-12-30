namespace HistoryVault.Models;

/// <summary>
/// Specifies the storage scope for market data.
/// </summary>
public enum StorageScope
{
    /// <summary>
    /// Local storage within the current working directory.
    /// Data is stored in ./data/history-vault/
    /// </summary>
    Local,

    /// <summary>
    /// Global storage in the user's application data directory.
    /// Platform-specific locations:
    /// - Windows: %APPDATA%\HistoryVault
    /// - macOS: ~/Library/Application Support/HistoryVault
    /// - Linux: ~/.local/share/HistoryVault
    /// </summary>
    Global
}
