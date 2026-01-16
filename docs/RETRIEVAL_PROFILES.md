# Retrieval Profiles

## Fast (default)

- Symbol search (symbol_search) with weighted token BM25
- Symbol metadata + minimal snippet + compact "why" signals
- Structural signals and lightweight graph expansion (bounded)
- No embeddings required
- Supports `similar:<symbol-id>` via SimHash fingerprints (extra terms are post-filter only)

## Hybrid

- Fast profile plus optional embeddings over code chunks
- Query embeddings are client-supplied; the server does not generate query embeddings
- Embeddings used only when available and query embeddings are valid

## Semantic

- Embeddings-first ranking when query embeddings are valid
- Falls back to Hybrid/Fast when embeddings are unavailable or query embeddings are missing/invalid

### Embedding Metadata + Fallbacks

Metadata keys:
- `embeddingUsed`: boolean, true only when reranking uses embeddings
- `embeddingModel`: resolved model name when known
- `fallback`: stable reason code when embeddings are not used
- `errorCode` / `error`: optional detail for invalid/missing query embeddings

Fallback codes:
- `embeddings_disabled`: embeddings disabled in config
- `embedding_provider_unavailable`: provider unavailable or not configured
- `missing_query_embedding`: query embedding not supplied
- `query_embedding_invalid`: invalid base64, model missing/ambiguous, or dims mismatch

## Similarity Operator

- Activation: `similar:<symbol-id> [extra terms...]`
- Extra terms are post-filter only
- Same repo + branch + language + symbol kind; seed excluded
- Top K=10 by lowest Hamming distance (SimHash), no embeddings
