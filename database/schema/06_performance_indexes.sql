-- ============================================================================
-- Performance Optimization Indexes
-- ============================================================================
-- This file contains additional indexes for performance optimization
-- that were identified after initial schema creation.

-- Functional index for case-insensitive qualified name lookups
-- This allows EdgeResolutionService to use an index when querying with LOWER(qualified_name)
-- Without this index, the LOWER() function prevents the use of idx_symbols_qualified_name
CREATE INDEX IF NOT EXISTS idx_symbols_qualified_name_lower 
    ON symbols(repo_id, branch_name, LOWER(qualified_name));

COMMENT ON INDEX idx_symbols_qualified_name_lower IS 
    'Functional index for case-insensitive qualified name lookups in EdgeResolutionService';

