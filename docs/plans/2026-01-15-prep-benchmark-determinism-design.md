# Prep Tasks Design: Deterministic Benchmark Corpus + Solution Integration Tests

## Goal

Make the benchmark corpus copy deterministic by excluding build artifacts and add a runtime guard, then ensure integration tests are part of the solution test run and document required setup.

## Scope

- Task A: Filter benchmark corpus copy to skip `bin/`, `obj/`, `.git/`, and optional `*.user`/`*.suo` files; warn if such paths exist in the source; throw if they appear in the destination.
- Task B: Add the integration test project to `lancer-mcp.sln` and document the required fixture restore command plus environment variables.

## Architecture

The benchmark CLI will call a small internal copier helper that performs a filtered recursive copy and enforces post-copy guards. The helper exposes a single `CopyFiltered` entry point and centralizes exclusion rules. A unit test in `LancerMcp.Tests` exercises the helper directly to prevent regressions. Integration tests are added to the solution so `dotnet test lancer-mcp.sln` includes them; documentation is updated to describe how to restore fixtures and set required environment variables.

## Data Flow

1. Benchmark CLI computes the `testdata/csharp` source path.
2. The filtered copier traverses directories, skipping excluded path segments and file extensions.
3. If excluded folders are detected in the source, a warning is printed once.
4. After copy, the destination is scanned for excluded path segments; any match throws as a guardrail.
5. Benchmarks proceed as before using the copied corpus.

## Error Handling

- Source exclusions produce a warning and continue with the filtered copy.
- Destination exclusions throw to surface a bug in the filtering logic.
- The benchmark run otherwise behaves the same as before.

## Testing

- Unit test: copy a temp corpus containing `bin/` and `obj/` and assert the destination excludes them.
- Solution test inclusion: unit test reads `lancer-mcp.sln` and asserts the integration test project path is present.

## Tradeoffs

- Slightly more benchmark setup code in exchange for deterministic metrics.
- Solution tests may fail on machines without fixtures; documentation will instruct how to prepare them.
