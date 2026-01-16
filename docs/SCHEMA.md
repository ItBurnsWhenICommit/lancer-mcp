# Schema

## Tables

### repos
- `id`: repository identifier (uses repository name)
- `name`: unique repository name
- `remote_url`: clone URL
- `default_branch`: default branch name

### branches
- `repo_id`: repository id
- `name`: branch name
- `head_commit_sha`: current HEAD
- `index_state`: Pending/InProgress/Completed/Failed/Stale
- `indexed_commit_sha`: last indexed SHA
- `last_indexed_at`: timestamp of last index

### commits
- `repo_id`: repository id
- `sha`: commit SHA
- `branch_name`: branch for this commit
- `author_name`, `author_email`, `commit_message`, `committed_at`

### files
- `repo_id`, `branch_name`, `commit_sha`
- `file_path`: path relative to repo root
- `language`: detected language
- `size_bytes`, `line_count`, `indexed_at`

### symbols
- `repo_id`, `branch_name`, `commit_sha`, `file_path`
- `name`, `qualified_name`, `kind`
- `start_line`, `end_line`, `start_column`, `end_column`
- `parent_symbol_id`, `signature`, `documentation`, `modifiers`

### symbol_search
- `symbol_id` (PK, references symbols)
- `repo_id`, `branch_name`, `commit_sha`, `file_path`, `language`, `kind`
- `name_tokens`, `qualified_tokens`, `signature_tokens`
- `documentation_tokens`, `literal_tokens`
- `snippet`
- `search_vector` (weighted tsvector)

### symbol_fingerprints
- `symbol_id` (PK, references symbols)
- `repo_id`, `branch_name`, `commit_sha`, `file_path`, `language`, `kind`
- `fingerprint_kind` (e.g., simhash_v1)
- `fingerprint` (uint64 stored as bigint)
- `band0`..`band3` (16-bit bands for candidate lookup)

### edges
- `source_symbol_id`, `target_symbol_id`, `kind`
- `repo_id`, `branch_name`, `commit_sha`
- `source_file_path`, `source_line`

### code_chunks
- `repo_id`, `branch_name`, `commit_sha`, `file_path`
- `symbol_id`, `symbol_name`, `symbol_kind`
- `content`, `start_line`, `end_line`
- `chunk_start_line`, `chunk_end_line`, `token_count`
- `signature`, `documentation`

### embeddings
- `chunk_id`, `repo_id`, `branch_name`, `commit_sha`
- `vector`, `dims`, `model`, `model_version`, `generated_at`

### embedding_jobs
- `repo_id`, `branch_name`, `commit_sha`
- `target_kind`, `target_id`
- `model`, `dims`, `status`, `attempts`
- `next_attempt_at`, `last_error`, `locked_at`, `locked_by`

## Rationale

- Repos/branches/commits track Git state for incremental indexing
- Files/symbols/edges provide symbol-level retrieval and graph signals
- Symbol_search enables the Fast profile via weighted token search on symbols
- Symbol_fingerprints supports similarity search without embeddings (banded SimHash)
- Code chunks support sparse retrieval and optional embeddings
- Embeddings are optional and isolated from baseline retrieval
- Embedding_jobs tracks async embedding generation without blocking indexing/query
