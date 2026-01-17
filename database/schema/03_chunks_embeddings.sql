-- ============================================================================
-- PostgreSQL Chunks and Embeddings Tables for Lancer MCP
-- ============================================================================
-- This file creates tables for code chunks and their embeddings with pgvector
-- support for semantic search using HNSW indexes.
-- ============================================================================

-- ============================================================================
-- Code Chunks Table
-- ============================================================================
CREATE TABLE code_chunks (
    id TEXT PRIMARY KEY,
    repo_id TEXT NOT NULL REFERENCES repos(id) ON DELETE CASCADE,
    branch_name TEXT NOT NULL,
    commit_sha TEXT NOT NULL,
    file_path TEXT NOT NULL,
    symbol_id TEXT REFERENCES symbols(id) ON DELETE SET NULL,
    symbol_name TEXT,
    symbol_kind symbol_kind,
    language language NOT NULL DEFAULT 'Unknown',
    content TEXT NOT NULL,
    start_line INTEGER NOT NULL,
    end_line INTEGER NOT NULL,
    chunk_start_line INTEGER NOT NULL,
    chunk_end_line INTEGER NOT NULL,
    token_count INTEGER NOT NULL DEFAULT 0,
    parent_symbol_name TEXT,
    signature TEXT,
    documentation TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_chunks_repo_id ON code_chunks(repo_id);
CREATE INDEX idx_chunks_repo_branch ON code_chunks(repo_id, branch_name);
CREATE INDEX idx_chunks_file_path ON code_chunks(file_path);
CREATE INDEX idx_chunks_symbol_id ON code_chunks(symbol_id);
CREATE INDEX idx_chunks_symbol_name ON code_chunks(symbol_name);
CREATE INDEX idx_chunks_symbol_kind ON code_chunks(symbol_kind);
CREATE INDEX idx_chunks_language ON code_chunks(language);

-- Full-text search index for BM25-style ranking
CREATE INDEX idx_chunks_content_fts ON code_chunks USING GIN (
    to_tsvector('english', content)
);

-- Composite index for common queries
CREATE INDEX idx_chunks_repo_branch_lang ON code_chunks(repo_id, branch_name, language);

-- Unique constraint to prevent duplicate chunks
-- Each chunk is uniquely identified by repo, branch, file, and chunk location
CREATE UNIQUE INDEX idx_chunks_unique ON code_chunks(repo_id, branch_name, file_path, chunk_start_line, chunk_end_line);

COMMENT ON TABLE code_chunks IS 'Stores code chunks for embedding and search';
COMMENT ON COLUMN code_chunks.content IS 'Code content with context lines';
COMMENT ON COLUMN code_chunks.start_line IS 'Symbol start line (excluding context)';
COMMENT ON COLUMN code_chunks.end_line IS 'Symbol end line (excluding context)';
COMMENT ON COLUMN code_chunks.chunk_start_line IS 'Chunk start line (including context)';
COMMENT ON COLUMN code_chunks.chunk_end_line IS 'Chunk end line (including context)';
COMMENT ON COLUMN code_chunks.token_count IS 'Approximate token count for embedding model';

-- ============================================================================
-- Embeddings Table
-- ============================================================================
CREATE TABLE embeddings (
    id TEXT PRIMARY KEY,
    chunk_id TEXT NOT NULL UNIQUE REFERENCES code_chunks(id) ON DELETE CASCADE,
    repo_id TEXT NOT NULL REFERENCES repos(id) ON DELETE CASCADE,
    branch_name TEXT NOT NULL,
    commit_sha TEXT NOT NULL,
    vector vector(768) NOT NULL,
    model TEXT NOT NULL,
    model_version TEXT,
    generated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE embeddings ADD COLUMN IF NOT EXISTS dims INTEGER;

CREATE INDEX idx_embeddings_repo_id ON embeddings(repo_id);
CREATE INDEX idx_embeddings_repo_branch ON embeddings(repo_id, branch_name);
CREATE INDEX idx_embeddings_model ON embeddings(model);

-- HNSW index for fast approximate nearest neighbor search
-- m=16: number of connections per layer (higher = better recall, more memory)
-- ef_construction=64: size of dynamic candidate list (higher = better index quality, slower build)
CREATE INDEX idx_embeddings_vector_hnsw ON embeddings USING hnsw (vector vector_cosine_ops)
WITH (m = 16, ef_construction = 64);

-- Alternative: IVFFlat index (faster build, slower search)
-- CREATE INDEX idx_embeddings_vector_ivfflat ON embeddings USING ivfflat (vector vector_cosine_ops)
-- WITH (lists = 100);

COMMENT ON TABLE embeddings IS 'Stores vector embeddings for code chunks';
COMMENT ON COLUMN embeddings.vector IS 'Embedding vector (768 dimensions for jina-embeddings-v2-base-code)';
COMMENT ON COLUMN embeddings.model IS 'Model used to generate embedding';
COMMENT ON COLUMN embeddings.model_version IS 'Model version for cache invalidation';

-- ============================================================================
-- Helper Functions for Vector Search
-- ============================================================================

-- Function to search embeddings by cosine similarity
CREATE OR REPLACE FUNCTION search_embeddings_cosine(
    query_vector vector(768),
    repo_filter TEXT DEFAULT NULL,
    branch_filter TEXT DEFAULT NULL,
    limit_count INTEGER DEFAULT 10
)
RETURNS TABLE (
    chunk_id TEXT,
    repo_id TEXT,
    branch_name TEXT,
    file_path TEXT,
    symbol_name TEXT,
    content TEXT,
    similarity FLOAT
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        c.id,
        c.repo_id,
        c.branch_name,
        c.file_path,
        c.symbol_name,
        c.content,
        1 - (e.vector <=> query_vector) AS similarity
    FROM embeddings e
    JOIN code_chunks c ON e.chunk_id = c.id
    WHERE
        (repo_filter IS NULL OR c.repo_id = repo_filter)
        AND (branch_filter IS NULL OR c.branch_name = branch_filter)
    ORDER BY e.vector <=> query_vector
    LIMIT limit_count;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION search_embeddings_cosine IS 'Search code chunks by vector similarity (cosine distance)';

-- Function to search embeddings by L2 distance
CREATE OR REPLACE FUNCTION search_embeddings_l2(
    query_vector vector(768),
    repo_filter TEXT DEFAULT NULL,
    branch_filter TEXT DEFAULT NULL,
    limit_count INTEGER DEFAULT 10
)
RETURNS TABLE (
    chunk_id TEXT,
    repo_id TEXT,
    branch_name TEXT,
    file_path TEXT,
    symbol_name TEXT,
    content TEXT,
    distance FLOAT
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        c.id,
        c.repo_id,
        c.branch_name,
        c.file_path,
        c.symbol_name,
        c.content,
        (e.vector <-> query_vector)::FLOAT AS distance
    FROM embeddings e
    JOIN code_chunks c ON e.chunk_id = c.id
    WHERE
        (repo_filter IS NULL OR c.repo_id = repo_filter)
        AND (branch_filter IS NULL OR c.branch_name = branch_filter)
    ORDER BY e.vector <-> query_vector
    LIMIT limit_count;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION search_embeddings_l2 IS 'Search code chunks by vector similarity (L2 distance)';

-- ============================================================================
-- Verify tables and indexes are created
-- ============================================================================
DO $$
BEGIN
    RAISE NOTICE 'Created tables:';
    RAISE NOTICE '  - code_chunks';
    RAISE NOTICE '  - embeddings';
    RAISE NOTICE '';
    RAISE NOTICE 'Created indexes:';
    RAISE NOTICE '  - Full-text search (GIN) on code_chunks.content';
    RAISE NOTICE '  - HNSW index on embeddings.vector';
    RAISE NOTICE '';
    RAISE NOTICE 'Created functions:';
    RAISE NOTICE '  - search_embeddings_cosine()';
    RAISE NOTICE '  - search_embeddings_l2()';
END $$;
