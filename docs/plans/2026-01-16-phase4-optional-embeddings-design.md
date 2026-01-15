# Phase 4 Optional Embeddings Design

## Goal
Add optional embeddings for Hybrid/Semantic retrieval without changing Fast defaults or requiring embeddings for indexing, tests, or benchmarks.

## Non-Negotiables
- Query never generates embeddings.
- Indexing never blocks or fails if the embedding provider is down.
- Embeddings are optional; Fast remains the default and works with zero embeddings.
- Hybrid uses sparse-first, then optional vector rerank.
- Semantic uses vector-first only when embeddings exist; otherwise falls back Hybrid -> Fast.
- Response metadata always includes `embeddingUsed`, `embeddingModel` (when used), and `fallback`.

## Architecture Overview
- **Durable job queue**: `embedding_jobs` table stores pending work for chunks.
- **Background worker**: hosted service claims jobs in batches, calls the provider, and writes embeddings.
- **Client-supplied query embeddings**: MCP Query accepts `queryEmbeddingBase64` (float32 LE), `queryEmbeddingDims`, and `queryEmbeddingModel`.
- **Model resolution**: explicit or deterministic fallback (default model or unique model in DB).

## Data Model Changes
### embedding_jobs (new)
- Fields: `id`, `repo_id`, `branch_name`, `commit_sha`, `target_kind`, `target_id`, `model`, `dims`,
  `status`, `attempts`, `next_attempt_at`, `last_error`, `created_at`, `updated_at`, `locked_at`, `locked_by`.
- Unique constraint: `(repo_id, branch_name, target_kind, target_id, model)`.
- `model` is always a concrete string, normalized to lower-case. If missing config: `__missing__`.
- `dims` is nullable until the provider returns a vector.

### embeddings (update)
- Add `dims` column to record vector dimensionality.
- `model` stored normalized to lower-case.

## Indexing Flow
- Persist chunks as usual.
- If `EmbeddingsEnabled=false`, skip enqueueing.
- If enabled:
  - If model missing: enqueue `Blocked` jobs with `model="__missing__"`.
  - Else: enqueue `Pending` jobs for each chunk.
- Indexing never calls the embedding provider.

## Worker Flow
- Claim jobs with a single CTE using `FOR UPDATE SKIP LOCKED`.
- Claim criteria: `status='Pending' AND (next_attempt_at IS NULL OR next_attempt_at <= now())`.
- Update in the same statement: `status='Processing'`, `locked_at`, `locked_by`, `attempts+1`.
- Embed **canonical chunk content** from `code_chunks`.
- Success: upsert `embeddings`, write `dims`, mark job `Completed`.
- Missing chunk: mark `Completed` with `last_error="chunk_missing"` (terminal).
- Provider failure: requeue with exponential backoff (no permanent failure on transient errors).
- Stale `Processing` rows requeued to `Pending` after a lock timeout.
- Purge `Completed` jobs older than N days (default 7).

## Query Flow
- Parse and validate `queryEmbeddingBase64`:
  - `bytes % 4 == 0`, `dims` matches (if provided), `0 < dims <= 4096`.
  - Invalid input => fallback with `errorCode="invalid_query_embedding"` and `fallback` chain.
- Model resolution:
  - If `queryEmbeddingModel` provided: require matching embeddings or fallback (`embedding_model_not_found`).
  - Else use `DefaultEmbeddingModel` or the single model in repo+branch; else `embedding_model_ambiguous`.
- **Hybrid**: sparse-first, vector rerank only if valid query embedding and matching embeddings exist.
- **Semantic**: vector-first only if valid query embedding and matching embeddings exist; else fallback Hybrid -> Fast.
- Dims mismatch with stored model => fallback `embedding_dims_mismatch`.

## Configuration
- `EmbeddingsEnabled` (default false).
- `EmbeddingJobsBatchSize` (default 64).
- `EmbeddingJobsMaxAttempts` (default 10).
- `EmbeddingJobsStaleMinutes` (default 10).
- `EmbeddingJobsPurgeDays` (default 7).
- Worker `locked_by` format: `<hostname>:<pid>`.

## Metadata & Observability
- `embeddingUsed`: bool
- `embeddingModel`: string when used
- `fallback`: e.g. `semantic->hybrid->fast`
- Optional: `embeddingCandidateCount`
- Metrics semantics:
  - `Completed` + `last_error NULL` => success
  - `Completed` + `chunk_missing` => terminal/canceled
  - `Blocked` => config issue

## Testing Strategy
- Unit tests only until fixtures are regenerated.
- Required unit tests:
  - Hybrid/Semantic fallbacks + metadata without embeddings.
  - Query does not call provider.
  - Indexing enqueues jobs when enabled + model set.
  - Worker claims, retries, completes, and writes embeddings.
  - Rerank changes ordering when embeddings exist + query embedding provided.
- Integration + benchmark validation after fixtures are restored.
