-- ============================================================================
-- PostgreSQL Extensions for Project Indexer MCP
-- ============================================================================
-- This file sets up the required PostgreSQL extensions for:
-- - Vector similarity search (pgvector)
-- - Fuzzy text matching (pg_trgm)
-- - Full-text search (built-in)
-- ============================================================================

-- Enable pgvector extension for vector similarity search
-- Requires: PostgreSQL 11+ and pgvector 0.5.0+ for HNSW support
CREATE EXTENSION IF NOT EXISTS vector;

-- Enable pg_trgm for fuzzy text matching and similarity search
-- Used for file path search with typo tolerance
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Enable btree_gin for composite indexes (GIN + B-tree)
-- Allows efficient multi-column indexes combining GIN and B-tree
CREATE EXTENSION IF NOT EXISTS btree_gin;

-- Enable uuid-ossp for UUID generation (optional, using app-generated UUIDs)
-- CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Verify extensions are installed
DO $$
BEGIN
    RAISE NOTICE 'Installed extensions:';
    RAISE NOTICE '  - vector: %', (SELECT extversion FROM pg_extension WHERE extname = 'vector');
    RAISE NOTICE '  - pg_trgm: %', (SELECT extversion FROM pg_extension WHERE extname = 'pg_trgm');
    RAISE NOTICE '  - btree_gin: %', (SELECT extversion FROM pg_extension WHERE extname = 'btree_gin');
END $$;

