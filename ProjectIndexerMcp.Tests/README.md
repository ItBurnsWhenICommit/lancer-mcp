# ProjectIndexerMcp Tests

Integration tests for the Project Indexer MCP server.

## Test Coverage

### GitTrackerService Tests
- ✅ Repository initialization and cloning
- ✅ Branch tracking (on-demand lazy loading)
- ✅ Branch existence checking
- ✅ Remote branch enumeration
- ✅ File change detection
- ✅ Branch indexing state management
- ✅ Repository and branch metadata persisted to PostgreSQL
- ✅ Existing branches loaded from database on startup
- ✅ Stale branch cleanup
- ✅ Default branch protection

### BranchCleanupHostedService Tests
- ✅ Graceful cancellation

## Running Tests

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test
dotnet test --filter "FullyQualifiedName~GitTrackerServiceTests.InitializeAsync_ShouldCloneRepository"

# Run tests with coverage (requires coverlet)
dotnet test /p:CollectCoverage=true
```

## Test Repository

Tests use the public `octocat/Hello-World` repository from GitHub via SSH:
- Small size (quick to clone)
- Stable (won't change unexpectedly)
- Uses SSH authentication (git@github.com)
- Well-known test repository

## Test Isolation

Each test:
- Creates a temporary directory for repositories
- Uses its own `GitTrackerService` instance
- Cleans up after itself (via `IDisposable`)
- Is independent and can run in parallel

## Testing Framework

- **xUnit** - Test framework
- **xUnit Assertions** - Built-in assertion library (no third-party dependencies)
- **Microsoft.Extensions.Logging.Abstractions** - For logging in tests
- **Microsoft.Extensions.Options** - For configuration in tests

## Adding New Tests

1. Create a new test method with `[Fact]` attribute
2. Follow the Arrange-Act-Assert pattern
3. Use xUnit's built-in `Assert` class for assertions
4. Ensure proper cleanup in `Dispose()`

Example:
```csharp
[Fact]
public async Task MyNewTest_ShouldDoSomething()
{
    // Arrange
    await _gitTracker.InitializeAsync(CancellationToken.None);

    // Act
    var result = await _gitTracker.SomeMethod();

    // Assert
    Assert.NotNull(result);
    Assert.True(result.IsValid);
    Assert.Equal("expected", result.Value);
}
```

