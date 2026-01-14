# Retrieval Profiles

## Fast (default)

- Sparse/BM25 retrieval
- Structural signals and lightweight graph expansion
- No embeddings required

## Hybrid

- Fast profile plus optional embeddings
- Embeddings used only when available

## Semantic

- Embeddings-first ranking
- Falls back to Fast when embeddings are unavailable
