# Retrieval Profiles

## Fast (default)

- Symbol search (symbol_search) with weighted token BM25
- Symbol metadata + minimal snippet + compact "why" signals
- Structural signals and lightweight graph expansion (bounded)
- No embeddings required
- Supports `similar:<symbol-id>` via SimHash fingerprints (extra terms are post-filter only)

## Hybrid

- Fast profile plus optional embeddings over code chunks
- Embeddings used only when available

## Semantic

- Embeddings-first ranking
- Falls back to Fast when embeddings are unavailable

## Similarity Operator

- Activation: `similar:<symbol-id> [extra terms...]`
- Extra terms are post-filter only
- Same repo + branch + language + symbol kind; seed excluded
- Top K=10 by lowest Hamming distance (SimHash), no embeddings
