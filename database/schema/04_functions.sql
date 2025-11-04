-- ============================================================================
-- PostgreSQL Functions for Lancer MCP
-- ============================================================================
-- This file creates helper functions for hybrid search, BM25 ranking,
-- graph traversal, and query orchestration.
-- ============================================================================

-- ============================================================================
-- Full-Text Search with BM25-style Ranking
-- ============================================================================

-- Function to search code chunks using full-text search with BM25-style ranking
CREATE OR REPLACE FUNCTION search_chunks_fulltext(
    query_text TEXT,
    repo_filter TEXT DEFAULT NULL,
    branch_filter TEXT DEFAULT NULL,
    language_filter language DEFAULT NULL,
    limit_count INTEGER DEFAULT 10
)
RETURNS TABLE (
    chunk_id TEXT,
    repo_id TEXT,
    branch_name TEXT,
    file_path TEXT,
    symbol_name TEXT,
    symbol_kind symbol_kind,
    content TEXT,
    rank REAL
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        c.id,
        c.repo_id,
        c.branch_name,
        c.file_path,
        c.symbol_name,
        c.symbol_kind,
        c.content,
        ts_rank_cd(to_tsvector('english', c.content), plainto_tsquery('english', query_text)) AS rank
    FROM code_chunks c
    WHERE 
        to_tsvector('english', c.content) @@ plainto_tsquery('english', query_text)
        AND (repo_filter IS NULL OR c.repo_id = repo_filter)
        AND (branch_filter IS NULL OR c.branch_name = branch_filter)
        AND (language_filter IS NULL OR c.language = language_filter)
    ORDER BY rank DESC
    LIMIT limit_count;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION search_chunks_fulltext IS 'Full-text search on code chunks with BM25-style ranking';

-- ============================================================================
-- Symbol Search Functions
-- ============================================================================

-- Function to search symbols by name (exact and fuzzy)
CREATE OR REPLACE FUNCTION search_symbols(
    query_text TEXT,
    repo_filter TEXT DEFAULT NULL,
    branch_filter TEXT DEFAULT NULL,
    kind_filter symbol_kind DEFAULT NULL,
    fuzzy BOOLEAN DEFAULT TRUE,
    limit_count INTEGER DEFAULT 10
)
RETURNS TABLE (
    symbol_id TEXT,
    repo_id TEXT,
    branch_name TEXT,
    file_path TEXT,
    name TEXT,
    qualified_name TEXT,
    kind symbol_kind,
    signature TEXT,
    documentation TEXT,
    similarity REAL
) AS $$
BEGIN
    IF fuzzy THEN
        -- Fuzzy search using trigram similarity
        RETURN QUERY
        SELECT 
            s.id,
            s.repo_id,
            s.branch_name,
            s.file_path,
            s.name,
            s.qualified_name,
            s.kind,
            s.signature,
            s.documentation,
            similarity(s.name, query_text) AS sim
        FROM symbols s
        WHERE 
            s.name % query_text
            AND (repo_filter IS NULL OR s.repo_id = repo_filter)
            AND (branch_filter IS NULL OR s.branch_name = branch_filter)
            AND (kind_filter IS NULL OR s.kind = kind_filter)
        ORDER BY sim DESC
        LIMIT limit_count;
    ELSE
        -- Exact search (case-insensitive)
        RETURN QUERY
        SELECT 
            s.id,
            s.repo_id,
            s.branch_name,
            s.file_path,
            s.name,
            s.qualified_name,
            s.kind,
            s.signature,
            s.documentation,
            1.0::REAL AS sim
        FROM symbols s
        WHERE 
            LOWER(s.name) = LOWER(query_text)
            AND (repo_filter IS NULL OR s.repo_id = repo_filter)
            AND (branch_filter IS NULL OR s.branch_name = branch_filter)
            AND (kind_filter IS NULL OR s.kind = kind_filter)
        ORDER BY s.name
        LIMIT limit_count;
    END IF;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION search_symbols IS 'Search symbols by name with exact or fuzzy matching';

-- ============================================================================
-- Graph Traversal Functions
-- ============================================================================

-- Function to find all symbols that reference a given symbol (incoming edges)
CREATE OR REPLACE FUNCTION find_references(
    target_symbol_id TEXT,
    edge_kind_filter edge_kind DEFAULT NULL,
    limit_count INTEGER DEFAULT 100
)
RETURNS TABLE (
    source_symbol_id TEXT,
    source_name TEXT,
    source_kind symbol_kind,
    source_file_path TEXT,
    edge_kind edge_kind,
    edge_line INTEGER
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        s.id,
        s.name,
        s.kind,
        s.file_path,
        e.kind,
        e.source_line
    FROM edges e
    JOIN symbols s ON e.source_symbol_id = s.id
    WHERE 
        e.target_symbol_id = target_symbol_id
        AND (edge_kind_filter IS NULL OR e.kind = edge_kind_filter)
    ORDER BY s.file_path, e.source_line
    LIMIT limit_count;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION find_references IS 'Find all symbols that reference a given symbol';

-- Function to find all symbols referenced by a given symbol (outgoing edges)
CREATE OR REPLACE FUNCTION find_dependencies(
    source_symbol_id TEXT,
    edge_kind_filter edge_kind DEFAULT NULL,
    limit_count INTEGER DEFAULT 100
)
RETURNS TABLE (
    target_symbol_id TEXT,
    edge_kind edge_kind,
    edge_line INTEGER
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        e.target_symbol_id,
        e.kind,
        e.source_line
    FROM edges e
    WHERE 
        e.source_symbol_id = source_symbol_id
        AND (edge_kind_filter IS NULL OR e.kind = edge_kind_filter)
    ORDER BY e.source_line
    LIMIT limit_count;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION find_dependencies IS 'Find all symbols referenced by a given symbol';

-- Function to find call chain (recursive)
CREATE OR REPLACE FUNCTION find_call_chain(
    start_symbol_id TEXT,
    max_depth INTEGER DEFAULT 5
)
RETURNS TABLE (
    depth INTEGER,
    symbol_id TEXT,
    symbol_name TEXT,
    symbol_kind symbol_kind,
    file_path TEXT
) AS $$
BEGIN
    RETURN QUERY
    WITH RECURSIVE call_chain AS (
        -- Base case: start symbol
        SELECT 
            0 AS depth,
            s.id,
            s.name,
            s.kind,
            s.file_path
        FROM symbols s
        WHERE s.id = start_symbol_id
        
        UNION ALL
        
        -- Recursive case: follow 'Calls' edges
        SELECT 
            cc.depth + 1,
            s.id,
            s.name,
            s.kind,
            s.file_path
        FROM call_chain cc
        JOIN edges e ON cc.symbol_id = e.source_symbol_id
        JOIN symbols s ON e.target_symbol_id = s.id
        WHERE 
            e.kind = 'Calls'
            AND cc.depth < max_depth
    )
    SELECT * FROM call_chain;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION find_call_chain IS 'Find call chain starting from a symbol (recursive)';

-- ============================================================================
-- Hybrid Search Function (BM25 + Vector + Graph Re-ranking)
-- ============================================================================

-- Function to perform hybrid search combining full-text and vector search
CREATE OR REPLACE FUNCTION hybrid_search(
    query_text TEXT,
    query_vector vector(768) DEFAULT NULL,
    repo_filter TEXT DEFAULT NULL,
    branch_filter TEXT DEFAULT NULL,
    language_filter language DEFAULT NULL,
    bm25_weight REAL DEFAULT 0.3,
    vector_weight REAL DEFAULT 0.7,
    limit_count INTEGER DEFAULT 10
)
RETURNS TABLE (
    chunk_id TEXT,
    repo_id TEXT,
    branch_name TEXT,
    file_path TEXT,
    symbol_name TEXT,
    symbol_kind symbol_kind,
    content TEXT,
    combined_score REAL,
    bm25_score REAL,
    vector_score REAL
) AS $$
BEGIN
    RETURN QUERY
    WITH bm25_results AS (
        SELECT 
            c.id AS chunk_id,
            ts_rank_cd(to_tsvector('english', c.content), plainto_tsquery('english', query_text)) AS score
        FROM code_chunks c
        WHERE 
            to_tsvector('english', c.content) @@ plainto_tsquery('english', query_text)
            AND (repo_filter IS NULL OR c.repo_id = repo_filter)
            AND (branch_filter IS NULL OR c.branch_name = branch_filter)
            AND (language_filter IS NULL OR c.language = language_filter)
    ),
    vector_results AS (
        SELECT 
            e.chunk_id,
            (1 - (e.vector <=> query_vector))::REAL AS score
        FROM embeddings e
        JOIN code_chunks c ON e.chunk_id = c.id
        WHERE 
            query_vector IS NOT NULL
            AND (repo_filter IS NULL OR c.repo_id = repo_filter)
            AND (branch_filter IS NULL OR c.branch_name = branch_filter)
            AND (language_filter IS NULL OR c.language = language_filter)
        ORDER BY e.vector <=> query_vector
        LIMIT limit_count * 2
    ),
    combined AS (
        SELECT 
            COALESCE(b.chunk_id, v.chunk_id) AS chunk_id,
            COALESCE(b.score, 0) * bm25_weight + COALESCE(v.score, 0) * vector_weight AS combined_score,
            COALESCE(b.score, 0) AS bm25_score,
            COALESCE(v.score, 0) AS vector_score
        FROM bm25_results b
        FULL OUTER JOIN vector_results v ON b.chunk_id = v.chunk_id
    )
    SELECT 
        c.id,
        c.repo_id,
        c.branch_name,
        c.file_path,
        c.symbol_name,
        c.symbol_kind,
        c.content,
        comb.combined_score,
        comb.bm25_score,
        comb.vector_score
    FROM combined comb
    JOIN code_chunks c ON comb.chunk_id = c.id
    ORDER BY comb.combined_score DESC
    LIMIT limit_count;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION hybrid_search IS 'Hybrid search combining BM25 full-text search and vector similarity';

-- ============================================================================
-- Verify functions are created
-- ============================================================================
DO $$
BEGIN
    RAISE NOTICE 'Created functions:';
    RAISE NOTICE '  - search_chunks_fulltext()';
    RAISE NOTICE '  - search_symbols()';
    RAISE NOTICE '  - find_references()';
    RAISE NOTICE '  - find_dependencies()';
    RAISE NOTICE '  - find_call_chain()';
    RAISE NOTICE '  - hybrid_search()';
END $$;

