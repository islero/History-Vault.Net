# Contributing to HistoryVault.Net

Thank you for your interest in contributing to HistoryVault.Net! This document provides guidelines and instructions for contributing.

## Code of Conduct

Please read and follow our [Code of Conduct](CODE_OF_CONDUCT.md).

## How to Contribute

### Reporting Bugs

1. Check existing issues to avoid duplicates
2. Use the bug report template
3. Include:
   - .NET version and OS
   - Steps to reproduce
   - Expected vs. actual behavior
   - Minimal code example if possible

### Suggesting Features

1. Check existing issues and discussions
2. Use the feature request template
3. Describe the use case and expected behavior
4. Consider performance implications for HPC use cases

### Pull Requests

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes
4. Ensure all tests pass (`dotnet test`)
5. Run benchmarks if performance-related (`dotnet run -c Release --project benchmarks/HistoryVault.Benchmarks`)
6. Commit with clear messages (`git commit -m 'Add amazing feature'`)
7. Push to your branch (`git push origin feature/amazing-feature`)
8. Open a Pull Request

## Development Setup

### Prerequisites

- .NET 9.0 SDK or later
- Git

### Building

```bash
cd HistoryVault.Net
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Running Benchmarks

```bash
dotnet run -c Release --project benchmarks/HistoryVault.Benchmarks
```

## Coding Standards

### General Guidelines

- Follow C# naming conventions
- Use meaningful variable and method names
- Keep methods focused and small
- Write self-documenting code

### Performance Guidelines

This is an HPC library. Performance is critical:

- Avoid allocations in hot paths
- Use `Span<T>` and `ArrayPool<T>` where appropriate
- Prefer struct over class for small data types
- Use `readonly` and `in` parameters for structs
- Consider cache locality for data structures
- Benchmark changes that affect performance

### Example: Good HPC Pattern

```csharp
// Good: Uses ArrayPool to avoid allocations
public void Serialize(IReadOnlyList<CandlestickV2> candles)
{
    var buffer = ArrayPool<byte>.Shared.Rent(candles.Count * RecordSize);
    try
    {
        var span = buffer.AsSpan();
        // Write to span...
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

### Testing Guidelines

- Write unit tests for all new functionality
- Use descriptive test names: `MethodName_Scenario_ExpectedResult`
- Use FluentAssertions for readable assertions
- Include edge cases and error conditions
- Ensure tests are deterministic and isolated

### Example: Test Structure

```csharp
[Fact]
public void Aggregate_M1ToM5_ProducesCorrectOHLCV()
{
    // Arrange
    var candles = GenerateTestCandles(10, CandlestickInterval.M1);

    // Act
    var result = _aggregator.Aggregate(candles, CandlestickInterval.M1, CandlestickInterval.M5);

    // Assert
    result.Should().HaveCount(2);
    result[0].Open.Should().Be(candles[0].Open);
}
```

## Project Structure

```
HistoryVault.Net/
├── src/
│   └── HistoryVault/
│       ├── Abstractions/     # Interfaces
│       ├── Aggregation/      # Timeframe aggregation
│       ├── Configuration/    # Options classes
│       ├── Extensions/       # Extension methods
│       ├── Indexing/         # Symbol and time indexing
│       ├── Models/           # Data models
│       └── Storage/          # Storage implementation
├── tests/
│   └── HistoryVault.Tests/   # Unit tests
└── benchmarks/
    └── HistoryVault.Benchmarks/  # Performance benchmarks
```

## Commit Messages

Use clear, descriptive commit messages:

- `feat: Add warmup candles support`
- `fix: Correct aggregation for partial periods`
- `perf: Optimize binary serialization`
- `test: Add edge case tests for wildcard matching`
- `docs: Update API documentation`
- `refactor: Extract compression logic`

## Release Process

1. Update version in project files
2. Update CHANGELOG.md
3. Create a git tag
4. CI/CD will build and publish to NuGet

## Questions?

Open a discussion or issue on GitHub.

Thank you for contributing!
