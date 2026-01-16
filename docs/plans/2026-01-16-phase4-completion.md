# Phase 4 Optional Embeddings Completion Addendum

Purpose: Define Phase 4A–4E execution order and gates. Detailed steps remain in the existing Phase 4 plan and design docs.

References:
- docs/plans/2026-01-16-phase4-optional-embeddings.md
- docs/plans/2026-01-16-phase4-optional-embeddings-design.md

Phase Breakdown (PR Titles):
- 4A: schema safety + schema verification test + SCHEMA/CHANGELOG
- 4B: IEmbeddingProvider + embeddings unavailable fallback tests + RETRIEVAL_PROFILES update
- 4C: worker + retry/claim semantics tests
- 4D: hybrid/semantic rerank tests + implementation
- 4E: benchmarks + docs sweep + cleanup

Gates Per Phase:
- Tests: `dotnet test LancerMcp.Tests`
- Build: `dotnet build --no-restore`
- Benchmarks (Phase 4E only): `dotnet run --project LancerMcp.Benchmark/LancerMcp.Benchmark.csproj`

Non-Negotiable Guardrails:
- Fast remains default and works end-to-end without embeddings.
- Embeddings are async only; baseline indexing/query never block on embeddings.
- Payload stays compact; token budget guardrails enforced.
- Schema, docs, and code stay consistent.
