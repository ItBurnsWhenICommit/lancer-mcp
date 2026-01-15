# INDEXING_V2 Overview

## Goals

- Symbol-first indexing for C# to improve precision and reduce storage
- Sparse retrieval + structural signals for strong default performance
- Compact, high-signal payloads for low token usage
- Optional embeddings only in non-default profiles

## Phases

- Phase 0: Benchmarks and safety rails (current)
- Phase 1: C# symbol-first index
- Phase 2: Sparse retrieval + structural reranking (Fast profile default)
- Phase 3: Similarity without embeddings (SimHash + banded buckets)
- Phase 4: Optional embeddings for Hybrid/Semantic profiles

## Architecture (Target)

- Language frontends emit symbols and edges at symbol granularity
- Symbol search index (symbol_search) stores weighted tokens and minimal snippets
- Token index and BM25 provide primary retrieval (Fast profile via symbol_search)
- Structural reranking boosts type/member and graph proximity signals
- Symbol fingerprints (symbol_fingerprints) store SimHash + band buckets for similarity
- Response compaction enforces strict payload budgets

## Similarity (Phase 3)

- SimHash input = existing sparse fields plus a lightweight identifier stream from the symbol snippet/body
  (bounded 4000 chars / 256 tokens / min len 3 / drop numeric + C# keywords)
- Identifier stream is built from parser source text already in memory (no extra snippet I/O)
- Query activation: `similar:<symbol-id> [extra terms...]`
- Extra terms are post-filter only (no new recall step)
- Candidate scope: same repo + branch + language + symbol kind (method↔method, type↔type)
- Seed symbol is excluded; top K=10 by lowest Hamming distance
- Candidate lookup uses band0..band3 INT buckets with OR match and a hard cap (2000)
- Post-filter scans the candidate list until K results or cap is reached
- Failure modes return metadata: `seed_not_found` and `seed_fingerprint_missing` (reindex)
- Payload stays within Phase 0 caps (10 results / 8k snippet chars / 16KB JSON)
- Fingerprints are upserted per symbol (replace on reindex)
- Results include symbol id, qualified name, file path, and snippet line span

## Tradeoffs

- Prioritizes deterministic, explainable ranking over heavy ML
- Fewer large chunks reduces recall for pure semantic queries
- Fast profile favors low footprint and latency over deep semantic matching
