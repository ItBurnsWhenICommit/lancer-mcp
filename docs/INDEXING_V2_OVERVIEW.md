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
- Phase 3: Similarity without embeddings
- Phase 4: Optional embeddings for Hybrid/Semantic profiles

## Architecture (Target)

- Language frontends emit symbols and edges at symbol granularity
- Token index and BM25 provide primary retrieval
- Structural reranking boosts type/member and graph proximity
- Response compaction enforces strict payload budgets

## Tradeoffs

- Prioritizes deterministic, explainable ranking over heavy ML
- Fewer large chunks reduces recall for pure semantic queries
- Fast profile favors low footprint and latency over deep semantic matching
