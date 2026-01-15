# Benchmarks

## Command

```bash
dotnet run --project LancerMcp.Benchmark/LancerMcp.Benchmark.csproj
```

Note: PostgreSQL must be running before executing the benchmark.

## Dataset

- Default dataset: `testdata/csharp`
- Source repository is created in a temp directory per run
- Benchmark copy excludes `bin/`, `obj/`, `.git/`, `*.user`, and `*.suo` for determinism
- Embeddings are disabled (Fast profile baseline)

## Metrics

- Indexing time (ms)
- Peak memory (bytes, best-effort)
- DB size (bytes)
- Query p50/p95 (ms)
- Top-k hit rate
- Similarity top-k hit rate for `similar:<symbol-id>` queries (Phase 3)

## Database Configuration

The benchmark uses the same environment variables as the main server:

- `DB_HOST` (default: `localhost`)
- `DB_PORT` (default: `5432`)
- `DB_NAME` (default: `lancer`)
- `DB_USER` (default: `postgres`)
- `DB_PASSWORD` (default: `postgres`)

## Baseline (Phase 0)

- Baseline collected: 2026-01-14
- Indexing time (ms): 2970
- Peak memory (bytes): 188698624
- DB size (bytes): 9875939
- Query p50/p95 (ms): 3/150
- Top-k hit rate: 100.0% (Top-5)
