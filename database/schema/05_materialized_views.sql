-- ============================================================================
-- PostgreSQL Materialized Views for Lancer MCP
-- ============================================================================
-- This file creates materialized views for optimized queries and statistics.
-- Materialized views cache complex query results for faster access.
-- ============================================================================

-- ============================================================================
-- Repository Statistics View
-- ============================================================================
CREATE MATERIALIZED VIEW repo_stats AS
SELECT 
    r.id AS repo_id,
    r.name AS repo_name,
    COUNT(DISTINCT b.id) AS branch_count,
    COUNT(DISTINCT f.id) AS file_count,
    COUNT(DISTINCT s.id) AS symbol_count,
    COUNT(DISTINCT c.id) AS chunk_count,
    COUNT(DISTINCT e.id) AS embedding_count,
    SUM(f.size_bytes) AS total_size_bytes,
    SUM(f.line_count) AS total_line_count,
    MAX(b.last_indexed_at) AS last_indexed_at
FROM repos r
LEFT JOIN branches b ON r.id = b.repo_id
LEFT JOIN files f ON r.id = f.repo_id
LEFT JOIN symbols s ON r.id = s.repo_id
LEFT JOIN code_chunks c ON r.id = c.repo_id
LEFT JOIN embeddings e ON r.id = e.repo_id
GROUP BY r.id, r.name;

CREATE UNIQUE INDEX idx_repo_stats_repo_id ON repo_stats(repo_id);

COMMENT ON MATERIALIZED VIEW repo_stats IS 'Repository statistics (refresh periodically)';

-- ============================================================================
-- Branch Statistics View
-- ============================================================================
CREATE MATERIALIZED VIEW branch_stats AS
SELECT 
    b.id AS branch_id,
    b.repo_id,
    r.name AS repo_name,
    b.name AS branch_name,
    b.index_state,
    b.head_commit_sha,
    b.indexed_commit_sha,
    b.last_indexed_at,
    COUNT(DISTINCT f.id) AS file_count,
    COUNT(DISTINCT s.id) AS symbol_count,
    COUNT(DISTINCT c.id) AS chunk_count,
    COUNT(DISTINCT e.id) AS embedding_count,
    SUM(f.size_bytes) AS total_size_bytes,
    SUM(f.line_count) AS total_line_count
FROM branches b
JOIN repos r ON b.repo_id = r.id
LEFT JOIN files f ON b.repo_id = f.repo_id AND b.name = f.branch_name
LEFT JOIN symbols s ON b.repo_id = s.repo_id AND b.name = s.branch_name
LEFT JOIN code_chunks c ON b.repo_id = c.repo_id AND b.name = c.branch_name
LEFT JOIN embeddings e ON b.repo_id = e.repo_id AND b.name = e.branch_name
GROUP BY b.id, b.repo_id, r.name, b.name, b.index_state, b.head_commit_sha, b.indexed_commit_sha, b.last_indexed_at;

CREATE UNIQUE INDEX idx_branch_stats_branch_id ON branch_stats(branch_id);
CREATE INDEX idx_branch_stats_repo_id ON branch_stats(repo_id);
CREATE INDEX idx_branch_stats_index_state ON branch_stats(index_state);

COMMENT ON MATERIALIZED VIEW branch_stats IS 'Branch statistics (refresh periodically)';

-- ============================================================================
-- Language Statistics View
-- ============================================================================
CREATE MATERIALIZED VIEW language_stats AS
SELECT 
    r.id AS repo_id,
    r.name AS repo_name,
    f.language,
    COUNT(DISTINCT f.id) AS file_count,
    COUNT(DISTINCT s.id) AS symbol_count,
    COUNT(DISTINCT c.id) AS chunk_count,
    SUM(f.size_bytes) AS total_size_bytes,
    SUM(f.line_count) AS total_line_count
FROM repos r
JOIN files f ON r.id = f.repo_id
LEFT JOIN symbols s ON f.repo_id = s.repo_id AND f.file_path = s.file_path
LEFT JOIN code_chunks c ON f.repo_id = c.repo_id AND f.file_path = c.file_path
GROUP BY r.id, r.name, f.language;

CREATE INDEX idx_language_stats_repo_id ON language_stats(repo_id);
CREATE INDEX idx_language_stats_language ON language_stats(language);

COMMENT ON MATERIALIZED VIEW language_stats IS 'Language distribution statistics per repository';

-- ============================================================================
-- Symbol Kind Statistics View
-- ============================================================================
CREATE MATERIALIZED VIEW symbol_kind_stats AS
SELECT 
    r.id AS repo_id,
    r.name AS repo_name,
    s.kind AS symbol_kind,
    COUNT(DISTINCT s.id) AS symbol_count,
    COUNT(DISTINCT s.file_path) AS file_count
FROM repos r
JOIN symbols s ON r.id = s.repo_id
GROUP BY r.id, r.name, s.kind;

CREATE INDEX idx_symbol_kind_stats_repo_id ON symbol_kind_stats(repo_id);
CREATE INDEX idx_symbol_kind_stats_kind ON symbol_kind_stats(symbol_kind);

COMMENT ON MATERIALIZED VIEW symbol_kind_stats IS 'Symbol kind distribution per repository';

-- ============================================================================
-- Edge Kind Statistics View
-- ============================================================================
CREATE MATERIALIZED VIEW edge_kind_stats AS
SELECT 
    r.id AS repo_id,
    r.name AS repo_name,
    e.kind AS edge_kind,
    COUNT(*) AS edge_count
FROM repos r
JOIN edges e ON r.id = e.repo_id
GROUP BY r.id, r.name, e.kind;

CREATE INDEX idx_edge_kind_stats_repo_id ON edge_kind_stats(repo_id);
CREATE INDEX idx_edge_kind_stats_kind ON edge_kind_stats(edge_kind);

COMMENT ON MATERIALIZED VIEW edge_kind_stats IS 'Edge kind distribution per repository';

-- ============================================================================
-- Hot Symbols View (Most Referenced)
-- ============================================================================
CREATE MATERIALIZED VIEW hot_symbols AS
SELECT 
    s.id AS symbol_id,
    s.repo_id,
    r.name AS repo_name,
    s.branch_name,
    s.name AS symbol_name,
    s.qualified_name,
    s.kind AS symbol_kind,
    s.file_path,
    COUNT(DISTINCT e.id) AS reference_count,
    COUNT(DISTINCT CASE WHEN e.kind = 'Calls' THEN e.id END) AS call_count,
    COUNT(DISTINCT CASE WHEN e.kind = 'References' THEN e.id END) AS ref_count
FROM symbols s
JOIN repos r ON s.repo_id = r.id
LEFT JOIN edges e ON s.id = e.target_symbol_id
GROUP BY s.id, s.repo_id, r.name, s.branch_name, s.name, s.qualified_name, s.kind, s.file_path
HAVING COUNT(DISTINCT e.id) > 0
ORDER BY reference_count DESC;

CREATE INDEX idx_hot_symbols_repo_id ON hot_symbols(repo_id);
CREATE INDEX idx_hot_symbols_reference_count ON hot_symbols(reference_count DESC);

COMMENT ON MATERIALIZED VIEW hot_symbols IS 'Most referenced symbols (useful for identifying important code)';

-- ============================================================================
-- Recent Activity View
-- ============================================================================
CREATE MATERIALIZED VIEW recent_activity AS
SELECT 
    c.id AS commit_id,
    c.repo_id,
    r.name AS repo_name,
    c.branch_name,
    c.sha AS commit_sha,
    c.author_name,
    c.author_email,
    c.commit_message,
    c.committed_at,
    COUNT(DISTINCT f.id) AS files_changed,
    COUNT(DISTINCT s.id) AS symbols_changed
FROM commits c
JOIN repos r ON c.repo_id = r.id
LEFT JOIN files f ON c.repo_id = f.repo_id AND c.sha = f.commit_sha
LEFT JOIN symbols s ON c.repo_id = s.repo_id AND c.sha = s.commit_sha
GROUP BY c.id, c.repo_id, r.name, c.branch_name, c.sha, c.author_name, c.author_email, c.commit_message, c.committed_at
ORDER BY c.committed_at DESC;

CREATE INDEX idx_recent_activity_repo_id ON recent_activity(repo_id);
CREATE INDEX idx_recent_activity_committed_at ON recent_activity(committed_at DESC);

COMMENT ON MATERIALIZED VIEW recent_activity IS 'Recent commit activity with file and symbol counts';

-- ============================================================================
-- Indexing Progress View
-- ============================================================================
CREATE MATERIALIZED VIEW indexing_progress AS
SELECT 
    r.id AS repo_id,
    r.name AS repo_name,
    COUNT(DISTINCT b.id) AS total_branches,
    COUNT(DISTINCT CASE WHEN b.index_state = 'Completed' THEN b.id END) AS completed_branches,
    COUNT(DISTINCT CASE WHEN b.index_state = 'InProgress' THEN b.id END) AS in_progress_branches,
    COUNT(DISTINCT CASE WHEN b.index_state = 'Pending' THEN b.id END) AS pending_branches,
    COUNT(DISTINCT CASE WHEN b.index_state = 'Failed' THEN b.id END) AS failed_branches,
    COUNT(DISTINCT CASE WHEN b.index_state = 'Stale' THEN b.id END) AS stale_branches,
    ROUND(
        100.0 * COUNT(DISTINCT CASE WHEN b.index_state = 'Completed' THEN b.id END) / 
        NULLIF(COUNT(DISTINCT b.id), 0), 
        2
    ) AS completion_percentage
FROM repos r
LEFT JOIN branches b ON r.id = b.repo_id
GROUP BY r.id, r.name;

CREATE UNIQUE INDEX idx_indexing_progress_repo_id ON indexing_progress(repo_id);

COMMENT ON MATERIALIZED VIEW indexing_progress IS 'Indexing progress per repository';

-- ============================================================================
-- Helper Functions to Refresh Materialized Views
-- ============================================================================

-- Function to refresh all materialized views
CREATE OR REPLACE FUNCTION refresh_all_stats()
RETURNS void AS $$
BEGIN
    REFRESH MATERIALIZED VIEW CONCURRENTLY repo_stats;
    REFRESH MATERIALIZED VIEW CONCURRENTLY branch_stats;
    REFRESH MATERIALIZED VIEW language_stats;
    REFRESH MATERIALIZED VIEW symbol_kind_stats;
    REFRESH MATERIALIZED VIEW edge_kind_stats;
    REFRESH MATERIALIZED VIEW hot_symbols;
    REFRESH MATERIALIZED VIEW recent_activity;
    REFRESH MATERIALIZED VIEW indexing_progress;
    RAISE NOTICE 'All materialized views refreshed';
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION refresh_all_stats IS 'Refresh all materialized views (call periodically)';

-- Function to refresh a specific materialized view
CREATE OR REPLACE FUNCTION refresh_stats(view_name TEXT)
RETURNS void AS $$
BEGIN
    EXECUTE format('REFRESH MATERIALIZED VIEW CONCURRENTLY %I', view_name);
    RAISE NOTICE 'Materialized view % refreshed', view_name;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION refresh_stats IS 'Refresh a specific materialized view';

-- ============================================================================
-- Verify materialized views are created
-- ============================================================================
DO $$
BEGIN
    RAISE NOTICE 'Created materialized views:';
    RAISE NOTICE '  - repo_stats';
    RAISE NOTICE '  - branch_stats';
    RAISE NOTICE '  - language_stats';
    RAISE NOTICE '  - symbol_kind_stats';
    RAISE NOTICE '  - edge_kind_stats';
    RAISE NOTICE '  - hot_symbols';
    RAISE NOTICE '  - recent_activity';
    RAISE NOTICE '  - indexing_progress';
    RAISE NOTICE '';
    RAISE NOTICE 'Created refresh functions:';
    RAISE NOTICE '  - refresh_all_stats()';
    RAISE NOTICE '  - refresh_stats(view_name)';
    RAISE NOTICE '';
    RAISE NOTICE 'To refresh all views: SELECT refresh_all_stats();';
END $$;

