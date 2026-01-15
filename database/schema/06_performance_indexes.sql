-- ============================================================================
-- Performance Optimization Indexes
-- ============================================================================
-- This file contains additional indexes for performance optimization
-- that were identified after initial schema creation.

-- Functional index for case-insensitive qualified name lookups
-- This allows EdgeResolutionService to use an index when querying with LOWER(qualified_name)
-- Without this index, the LOWER() function prevents the use of idx_symbols_qualified_name
--
-- IMPORTANT: This index uses LOWER() without COLLATE, which is locale-dependent.
-- The C# code uses ToLowerInvariant() for normalization, which may differ from PostgreSQL's
-- LOWER() behavior for non-ASCII characters (e.g., Turkish İ/i, German ß).
--
-- This is acceptable because:
-- 1. C# qualified names (namespaces, classes, methods) are typically ASCII-only
-- 2. The database should use a compatible locale (e.g., en_US.UTF-8, C)
-- 3. Adding COLLATE "C" would require rebuilding the index if the database uses a different locale
--
-- If international symbol names are expected, consider:
-- - Using COLLATE "C" for deterministic ASCII-based sorting
-- - Or ensuring database locale matches C# invariant culture behavior
CREATE INDEX IF NOT EXISTS idx_symbols_qualified_name_lower
    ON symbols(repo_id, branch_name, LOWER(qualified_name));

COMMENT ON INDEX idx_symbols_qualified_name_lower IS
    'Functional index for case-insensitive qualified name lookups in EdgeResolutionService. Uses LOWER() without COLLATE (locale-dependent). Assumes ASCII qualified names or compatible database locale.';

-- ============================================================================
-- Symbol Search Indexes
-- ============================================================================
CREATE INDEX IF NOT EXISTS idx_symbol_search_repo_branch
    ON symbol_search(repo_id, branch_name);

CREATE INDEX IF NOT EXISTS idx_symbol_search_kind
    ON symbol_search(kind);

CREATE INDEX IF NOT EXISTS idx_symbol_search_vector
    ON symbol_search USING GIN (search_vector);

-- ============================================================================
-- Symbol Fingerprint Indexes
-- ============================================================================
CREATE INDEX IF NOT EXISTS idx_symbol_fingerprints_repo_branch
    ON symbol_fingerprints(repo_id, branch_name);

CREATE INDEX IF NOT EXISTS idx_symbol_fingerprints_band0
    ON symbol_fingerprints(repo_id, branch_name, language, kind, fingerprint_kind, band0);

CREATE INDEX IF NOT EXISTS idx_symbol_fingerprints_band1
    ON symbol_fingerprints(repo_id, branch_name, language, kind, fingerprint_kind, band1);

CREATE INDEX IF NOT EXISTS idx_symbol_fingerprints_band2
    ON symbol_fingerprints(repo_id, branch_name, language, kind, fingerprint_kind, band2);

CREATE INDEX IF NOT EXISTS idx_symbol_fingerprints_band3
    ON symbol_fingerprints(repo_id, branch_name, language, kind, fingerprint_kind, band3);
