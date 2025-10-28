# Quick Reference - Database Functions

## Setup

```bash
# Start PostgreSQL with Docker
docker-compose up -d

# Run migrations
./migrate.sh

# Test setup
./test_setup.sh
```

## Common Queries

### Full-Text Search
```sql
-- Search code chunks
SELECT * FROM search_chunks_fulltext(
    query_text := 'authentication',
    repo_filter := 'my-repo',
    branch_filter := 'main',
    limit_count := 10
);
```

### Vector Search
```sql
-- Search by embedding similarity (cosine)
SELECT * FROM search_embeddings_cosine(
    query_vector := '[0.1, 0.2, ...]'::vector(768),
    repo_filter := 'my-repo',
    limit_count := 10
);

-- Search by embedding similarity (L2)
SELECT * FROM search_embeddings_l2(
    query_vector := '[0.1, 0.2, ...]'::vector(768),
    repo_filter := 'my-repo',
    limit_count := 10
);
```

### Hybrid Search
```sql
-- Combine BM25 + vector search
SELECT * FROM hybrid_search(
    query_text := 'authentication',
    query_vector := '[0.1, 0.2, ...]'::vector(768),
    repo_filter := 'my-repo',
    bm25_weight := 0.3,
    vector_weight := 0.7,
    limit_count := 10
);
```

### Symbol Search
```sql
-- Exact search
SELECT * FROM search_symbols(
    query_text := 'UserService',
    repo_filter := 'my-repo',
    fuzzy := false,
    limit_count := 10
);

-- Fuzzy search (typo-tolerant)
SELECT * FROM search_symbols(
    query_text := 'UserSrvice',  -- typo
    repo_filter := 'my-repo',
    fuzzy := true,
    limit_count := 10
);

-- Filter by symbol kind
SELECT * FROM search_symbols(
    query_text := 'User',
    kind_filter := 'Class',
    limit_count := 10
);
```

### Graph Traversal
```sql
-- Find all references to a symbol (who calls this?)
SELECT * FROM find_references(
    target_symbol_id := 'symbol-id-here',
    edge_kind_filter := 'Calls'
);

-- Find all dependencies of a symbol (what does this call?)
SELECT * FROM find_dependencies(
    source_symbol_id := 'symbol-id-here',
    edge_kind_filter := 'Calls'
);

-- Find call chain (recursive)
SELECT * FROM find_call_chain(
    start_symbol_id := 'symbol-id-here',
    max_depth := 5
);
```

## Statistics & Analytics

### Repository Stats
```sql
-- View repository statistics
SELECT * FROM repo_stats;

-- Specific repository
SELECT * FROM repo_stats WHERE repo_name = 'my-repo';
```

### Branch Stats
```sql
-- View branch statistics
SELECT * FROM branch_stats;

-- Specific branch
SELECT * FROM branch_stats 
WHERE repo_name = 'my-repo' AND branch_name = 'main';

-- Branches by index state
SELECT * FROM branch_stats WHERE index_state = 'Completed';
```

### Language Distribution
```sql
-- Language stats per repository
SELECT * FROM language_stats WHERE repo_name = 'my-repo';

-- Top languages by file count
SELECT language, SUM(file_count) as total_files
FROM language_stats
GROUP BY language
ORDER BY total_files DESC;
```

### Symbol Kind Distribution
```sql
-- Symbol kinds per repository
SELECT * FROM symbol_kind_stats WHERE repo_name = 'my-repo';

-- Most common symbol kinds
SELECT symbol_kind, SUM(symbol_count) as total
FROM symbol_kind_stats
GROUP BY symbol_kind
ORDER BY total DESC;
```

### Hot Symbols (Most Referenced)
```sql
-- Most referenced symbols
SELECT * FROM hot_symbols
ORDER BY reference_count DESC
LIMIT 20;

-- Most called functions
SELECT * FROM hot_symbols
WHERE symbol_kind IN ('Method', 'Function')
ORDER BY call_count DESC
LIMIT 20;
```

### Recent Activity
```sql
-- Recent commits
SELECT * FROM recent_activity
ORDER BY committed_at DESC
LIMIT 20;

-- Recent commits in a repository
SELECT * FROM recent_activity
WHERE repo_name = 'my-repo'
ORDER BY committed_at DESC;
```

### Indexing Progress
```sql
-- Overall indexing progress
SELECT * FROM indexing_progress;

-- Specific repository
SELECT * FROM indexing_progress WHERE repo_name = 'my-repo';
```

## Maintenance

### Refresh Materialized Views
```sql
-- Refresh all views
SELECT refresh_all_stats();

-- Refresh specific view
SELECT refresh_stats('repo_stats');
SELECT refresh_stats('branch_stats');
SELECT refresh_stats('hot_symbols');
```

### Vacuum and Analyze
```sql
-- Vacuum all tables
VACUUM ANALYZE;

-- Vacuum specific tables
VACUUM ANALYZE embeddings;
VACUUM ANALYZE code_chunks;
VACUUM ANALYZE symbols;
```

### Reindex
```sql
-- Reindex all indexes
REINDEX DATABASE project_indexer;

-- Reindex specific index
REINDEX INDEX idx_embeddings_vector_hnsw;
REINDEX INDEX idx_chunks_content_fts;
```

### Monitor Performance
```sql
-- Check index usage
SELECT 
    schemaname,
    tablename,
    indexname,
    idx_scan,
    idx_tup_read,
    idx_tup_fetch
FROM pg_stat_user_indexes
ORDER BY idx_scan DESC;

-- Check table sizes
SELECT 
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size
FROM pg_tables
WHERE schemaname = 'public'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;

-- Check slow queries (requires pg_stat_statements)
SELECT 
    query,
    calls,
    total_exec_time,
    mean_exec_time,
    max_exec_time
FROM pg_stat_statements
ORDER BY total_exec_time DESC
LIMIT 10;
```

## Direct Table Queries

### Repositories
```sql
-- List all repositories
SELECT * FROM repos;

-- Get repository by name
SELECT * FROM repos WHERE name = 'my-repo';
```

### Branches
```sql
-- List all branches
SELECT * FROM branches;

-- Branches for a repository
SELECT * FROM branches WHERE repo_id = 'repo-id';

-- Branches by state
SELECT * FROM branches WHERE index_state = 'Completed';
```

### Commits
```sql
-- Recent commits
SELECT * FROM commits ORDER BY committed_at DESC LIMIT 20;

-- Commits by author
SELECT * FROM commits WHERE author_email = 'user@example.com';
```

### Files
```sql
-- Files in a repository
SELECT * FROM files WHERE repo_id = 'repo-id';

-- Files by language
SELECT * FROM files WHERE language = 'CSharp';

-- Largest files
SELECT file_path, size_bytes 
FROM files 
ORDER BY size_bytes DESC 
LIMIT 20;
```

### Symbols
```sql
-- All symbols in a file
SELECT * FROM symbols WHERE file_path = 'path/to/file.cs';

-- Symbols by kind
SELECT * FROM symbols WHERE kind = 'Class';

-- Top-level symbols (no parent)
SELECT * FROM symbols WHERE parent_symbol_id IS NULL;

-- Child symbols of a parent
SELECT * FROM symbols WHERE parent_symbol_id = 'parent-id';
```

### Edges
```sql
-- All edges from a symbol
SELECT * FROM edges WHERE source_symbol_id = 'symbol-id';

-- All edges to a symbol
SELECT * FROM edges WHERE target_symbol_id = 'symbol-id';

-- Edges by kind
SELECT * FROM edges WHERE kind = 'Calls';
```

### Code Chunks
```sql
-- Chunks in a file
SELECT * FROM code_chunks WHERE file_path = 'path/to/file.cs';

-- Chunks by language
SELECT * FROM code_chunks WHERE language = 'CSharp';

-- Chunks with embeddings
SELECT c.* 
FROM code_chunks c
JOIN embeddings e ON c.id = e.chunk_id;
```

### Embeddings
```sql
-- Count embeddings
SELECT COUNT(*) FROM embeddings;

-- Embeddings by model
SELECT model, COUNT(*) 
FROM embeddings 
GROUP BY model;

-- Recent embeddings
SELECT * FROM embeddings ORDER BY generated_at DESC LIMIT 20;
```

## Performance Tips

### HNSW Query Performance
```sql
-- Increase ef_search for better recall (slower)
SET hnsw.ef_search = 200;

-- Decrease ef_search for faster queries (lower recall)
SET hnsw.ef_search = 40;
```

### Full-Text Search Performance
```sql
-- Use phrase search for exact matches
SELECT * FROM code_chunks 
WHERE to_tsvector('english', content) @@ phraseto_tsquery('english', 'user authentication');

-- Use prefix search for autocomplete
SELECT * FROM code_chunks 
WHERE to_tsvector('english', content) @@ to_tsquery('english', 'auth:*');
```

### Batch Operations
```sql
-- Insert multiple rows efficiently
INSERT INTO code_chunks (id, repo_id, branch_name, ...)
VALUES 
    ('id1', 'repo1', 'main', ...),
    ('id2', 'repo1', 'main', ...),
    ('id3', 'repo1', 'main', ...)
ON CONFLICT (id) DO UPDATE SET ...;

-- Use COPY for bulk inserts
COPY code_chunks FROM '/path/to/data.csv' WITH (FORMAT csv, HEADER true);
```

## Useful Psql Commands

```bash
# Connect to database
psql -h localhost -U postgres -d project_indexer

# List tables
\dt

# List materialized views
\dm

# List functions
\df

# Describe table
\d repos
\d+ repos  # with more details

# List indexes
\di

# Show table sizes
\dt+

# Execute SQL file
\i /path/to/file.sql

# Export query results to CSV
\copy (SELECT * FROM repos) TO '/tmp/repos.csv' WITH CSV HEADER;

# Timing queries
\timing on
SELECT * FROM search_chunks_fulltext('test');
```

