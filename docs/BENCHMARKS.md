# Benchmarks

## Command

```bash
dotnet run --project LancerMcp.Benchmark/LancerMcp.Benchmark.csproj
```

Note: PostgreSQL must be running before executing the benchmark.

## Profile Comparisons (Fast vs Hybrid vs Semantic)

The benchmark uses the server's default retrieval profile. To compare profiles:

1) Update `LancerMcp.Benchmark/Program.cs` to set `DefaultRetrievalProfile` inside the `ServerOptions` initializer.
2) Run the benchmark command for each profile.

Example (temporary edit):

```
DefaultRetrievalProfile = LancerMcp.Models.RetrievalProfile.Hybrid,
```

Hybrid/Semantic require client-supplied query embeddings to meaningfully rerank. When embeddings are disabled or query embeddings are missing, they fall back to Fast and metrics will match Fast.

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
- Top-k hit rate (expected symbols in top-K)
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

## Phase 4E Comparison (2026-01-16)

All runs used the same dataset and DB. Hybrid/Semantic ran without query embeddings, so they fell back to Fast.

| Profile  | Indexing (ms) | Peak Memory (bytes) | DB Size (bytes) | Query p50/p95 (ms) | Top-5 Hit Rate |
| --- | --- | --- | --- | --- | --- |
| Fast | 3562 | 329834496 | 28004835 | 4/170 | 100.0% |
| Hybrid | 3462 | 329175040 | 28119523 | 4/166 | 100.0% |
| Semantic | 3432 | 329297920 | 28193251 | 4/163 | 100.0% |
