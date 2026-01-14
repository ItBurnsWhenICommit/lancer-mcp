# Benchmarks

## Command

```bash
dotnet run --project LancerMcp.Benchmark/LancerMcp.Benchmark.csproj
```

## Dataset

- Default dataset: `testdata/csharp`
- Source repository is created in a temp directory per run
- Embeddings are disabled (Fast profile baseline)

## Metrics

- Indexing time (ms)
- Peak memory (bytes, best-effort)
- DB size (bytes)
- Query p50/p95 (ms)
- Top-k hit rate

## Database Configuration

The benchmark uses the same environment variables as the main server:

- `DB_HOST` (default: `localhost`)
- `DB_PORT` (default: `5432`)
- `DB_NAME` (default: `lancer`)
- `DB_USER` (default: `postgres`)
- `DB_PASSWORD` (default: `postgres`)

## Baseline (Phase 0)

- Indexing time (ms): TBD
- Peak memory (bytes): TBD
- DB size (bytes): TBD
- Query p50/p95 (ms): TBD
- Top-k hit rate: TBD
