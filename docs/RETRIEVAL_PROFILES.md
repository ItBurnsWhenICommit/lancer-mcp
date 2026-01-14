# Retrieval Profiles

## Fast (default)

- Symbol search (symbol_search) with weighted token BM25
- Symbol metadata + minimal snippet + compact "why" signals
- Structural signals and lightweight graph expansion (bounded)
- No embeddings required

## Hybrid

- Fast profile plus optional embeddings over code chunks
- Embeddings used only when available

## Semantic

- Embeddings-first ranking
- Falls back to Fast when embeddings are unavailable
