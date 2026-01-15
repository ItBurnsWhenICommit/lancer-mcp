-- ============================================================================
-- PostgreSQL Tables for Lancer MCP
-- ============================================================================
-- This file creates the main tables for storing repository metadata, symbols,
-- and relationships. Designed for efficient querying and graph traversal.
-- ============================================================================

-- ============================================================================
-- Repositories Table
-- ============================================================================
CREATE TABLE repos (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    remote_url TEXT NOT NULL,
    default_branch TEXT NOT NULL,
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_repos_name ON repos(name);

COMMENT ON TABLE repos IS 'Stores repository metadata';
COMMENT ON COLUMN repos.id IS 'Unique identifier (GUID from application)';
COMMENT ON COLUMN repos.name IS 'Repository name (unique)';
COMMENT ON COLUMN repos.remote_url IS 'Git remote URL';
COMMENT ON COLUMN repos.default_branch IS 'Default branch name (e.g., main, master)';

-- ============================================================================
-- Branches Table
-- ============================================================================
CREATE TABLE branches (
    id TEXT PRIMARY KEY,
    repo_id TEXT NOT NULL REFERENCES repos(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    head_commit_sha TEXT NOT NULL,
    index_state index_state NOT NULL DEFAULT 'Pending',
    indexed_commit_sha TEXT,
    last_indexed_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(repo_id, name)
);

CREATE INDEX idx_branches_repo_id ON branches(repo_id);
CREATE INDEX idx_branches_index_state ON branches(index_state);
CREATE INDEX idx_branches_repo_name ON branches(repo_id, name);

COMMENT ON TABLE branches IS 'Tracks branches and their indexing state';
COMMENT ON COLUMN branches.head_commit_sha IS 'Current HEAD commit SHA';
COMMENT ON COLUMN branches.index_state IS 'Indexing state (Pending, InProgress, Completed, Failed, Stale)';
COMMENT ON COLUMN branches.indexed_commit_sha IS 'Last successfully indexed commit SHA';

-- ============================================================================
-- Commits Table
-- ============================================================================
CREATE TABLE commits (
    id TEXT PRIMARY KEY,
    repo_id TEXT NOT NULL REFERENCES repos(id) ON DELETE CASCADE,
    sha TEXT NOT NULL,
    branch_name TEXT NOT NULL,
    author_name TEXT,
    author_email TEXT,
    commit_message TEXT,
    committed_at TIMESTAMPTZ,
    indexed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(repo_id, sha, branch_name)
);

CREATE INDEX idx_commits_repo_id ON commits(repo_id);
CREATE INDEX idx_commits_sha ON commits(sha);
CREATE INDEX idx_commits_repo_branch ON commits(repo_id, branch_name);
CREATE INDEX idx_commits_committed_at ON commits(committed_at DESC);

COMMENT ON TABLE commits IS 'Stores commit metadata for indexed commits';
COMMENT ON COLUMN commits.sha IS 'Git commit SHA';
COMMENT ON COLUMN commits.branch_name IS 'Branch where this commit was indexed';

-- ============================================================================
-- Files Table
-- ============================================================================
CREATE TABLE files (
    id TEXT PRIMARY KEY,
    repo_id TEXT NOT NULL REFERENCES repos(id) ON DELETE CASCADE,
    branch_name TEXT NOT NULL,
    commit_sha TEXT NOT NULL,
    file_path TEXT NOT NULL,
    language language NOT NULL DEFAULT 'Unknown',
    size_bytes BIGINT NOT NULL,
    line_count INTEGER NOT NULL,
    indexed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(repo_id, branch_name, commit_sha, file_path)
);

CREATE INDEX idx_files_repo_id ON files(repo_id);
CREATE INDEX idx_files_repo_branch ON files(repo_id, branch_name);
CREATE INDEX idx_files_file_path ON files(file_path);
CREATE INDEX idx_files_language ON files(language);

-- GIN index for fuzzy file path search
CREATE INDEX idx_files_file_path_trgm ON files USING GIN (file_path gin_trgm_ops);

COMMENT ON TABLE files IS 'Stores file metadata for indexed files';
COMMENT ON COLUMN files.file_path IS 'File path relative to repository root';
COMMENT ON COLUMN files.language IS 'Detected programming language';

-- ============================================================================
-- Symbols Table
-- ============================================================================
CREATE TABLE symbols (
    id TEXT PRIMARY KEY,
    repo_id TEXT NOT NULL REFERENCES repos(id) ON DELETE CASCADE,
    branch_name TEXT NOT NULL,
    commit_sha TEXT NOT NULL,
    file_path TEXT NOT NULL,
    name TEXT NOT NULL,
    qualified_name TEXT,
    kind symbol_kind NOT NULL DEFAULT 'Unknown',
    start_line INTEGER NOT NULL,
    end_line INTEGER NOT NULL,
    start_column INTEGER,
    end_column INTEGER,
    parent_symbol_id TEXT REFERENCES symbols(id) ON DELETE SET NULL,
    signature TEXT,
    documentation TEXT,
    modifiers TEXT[],
    language language NOT NULL DEFAULT 'Unknown',
    indexed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_symbols_repo_id ON symbols(repo_id);
CREATE INDEX idx_symbols_repo_branch ON symbols(repo_id, branch_name);
CREATE INDEX idx_symbols_file_path ON symbols(file_path);
CREATE INDEX idx_symbols_name ON symbols(name);
CREATE INDEX idx_symbols_qualified_name ON symbols(qualified_name);
CREATE INDEX idx_symbols_kind ON symbols(kind);
CREATE INDEX idx_symbols_parent_id ON symbols(parent_symbol_id);

-- GIN index for fuzzy symbol name search
CREATE INDEX idx_symbols_name_trgm ON symbols USING GIN (name gin_trgm_ops);

-- Composite index for common queries
CREATE INDEX idx_symbols_repo_branch_kind ON symbols(repo_id, branch_name, kind);

-- Unique constraint to prevent duplicate symbols
-- Each symbol is uniquely identified by repo, branch, file, name, and location
CREATE UNIQUE INDEX idx_symbols_unique ON symbols(repo_id, branch_name, file_path, name, start_line, end_line);

COMMENT ON TABLE symbols IS 'Stores extracted code symbols (classes, functions, etc.)';
COMMENT ON COLUMN symbols.qualified_name IS 'Fully qualified name (e.g., Namespace.Class.Method)';
COMMENT ON COLUMN symbols.parent_symbol_id IS 'Parent symbol (e.g., class for a method)';
COMMENT ON COLUMN symbols.signature IS 'Method/function signature';
COMMENT ON COLUMN symbols.modifiers IS 'Access modifiers (public, private, static, etc.)';

-- ============================================================================
-- Symbol Search Table
-- ============================================================================
CREATE TABLE symbol_search (
    symbol_id TEXT PRIMARY KEY REFERENCES symbols(id) ON DELETE CASCADE,
    repo_id TEXT NOT NULL REFERENCES repos(id) ON DELETE CASCADE,
    branch_name TEXT NOT NULL,
    commit_sha TEXT NOT NULL,
    file_path TEXT NOT NULL,
    language language NOT NULL DEFAULT 'Unknown',
    kind symbol_kind NOT NULL DEFAULT 'Unknown',
    name_tokens TEXT,
    qualified_tokens TEXT,
    signature_tokens TEXT,
    documentation_tokens TEXT,
    literal_tokens TEXT,
    snippet TEXT,
    search_vector tsvector
);

COMMENT ON TABLE symbol_search IS 'Sparse retrieval index for symbols (weighted tokens + snippet)';
COMMENT ON COLUMN symbol_search.search_vector IS 'Weighted tsvector for token search';

-- ============================================================================
-- Symbol Fingerprints Table
-- ============================================================================
CREATE TABLE symbol_fingerprints (
    symbol_id TEXT PRIMARY KEY REFERENCES symbols(id) ON DELETE CASCADE,
    repo_id TEXT NOT NULL REFERENCES repos(id) ON DELETE CASCADE,
    branch_name TEXT NOT NULL,
    commit_sha TEXT NOT NULL,
    file_path TEXT NOT NULL,
    language language NOT NULL DEFAULT 'Unknown',
    kind symbol_kind NOT NULL DEFAULT 'Unknown',
    fingerprint_kind TEXT NOT NULL,
    fingerprint BIGINT NOT NULL,
    band0 INTEGER NOT NULL,
    band1 INTEGER NOT NULL,
    band2 INTEGER NOT NULL,
    band3 INTEGER NOT NULL,
    indexed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE symbol_fingerprints IS 'Symbol-level fingerprints for similarity search (SimHash)';
COMMENT ON COLUMN symbol_fingerprints.fingerprint IS '64-bit fingerprint value stored as signed bigint';

-- ============================================================================
-- Edges Table (Symbol Relationships)
-- ============================================================================
CREATE TABLE edges (
    id TEXT PRIMARY KEY,
    source_symbol_id TEXT NOT NULL REFERENCES symbols(id) ON DELETE CASCADE,
    target_symbol_id TEXT NOT NULL,
    kind edge_kind NOT NULL DEFAULT 'Unknown',
    repo_id TEXT NOT NULL REFERENCES repos(id) ON DELETE CASCADE,
    branch_name TEXT NOT NULL,
    commit_sha TEXT NOT NULL,
    source_file_path TEXT,
    source_line INTEGER,
    indexed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_edges_source_symbol_id ON edges(source_symbol_id);
CREATE INDEX idx_edges_target_symbol_id ON edges(target_symbol_id);
CREATE INDEX idx_edges_kind ON edges(kind);
CREATE INDEX idx_edges_repo_branch ON edges(repo_id, branch_name);

-- Composite indexes for graph traversal
CREATE INDEX idx_edges_source_kind ON edges(source_symbol_id, kind);
CREATE INDEX idx_edges_target_kind ON edges(target_symbol_id, kind);

-- GIN index for multi-column queries
CREATE INDEX idx_edges_composite ON edges USING GIN (
    source_symbol_id,
    target_symbol_id,
    kind
) WITH (fastupdate = off);

COMMENT ON TABLE edges IS 'Stores relationships between symbols (calls, references, inheritance, etc.)';
COMMENT ON COLUMN edges.target_symbol_id IS 'Target symbol ID or external reference';
COMMENT ON COLUMN edges.source_file_path IS 'File where the relationship is defined';
COMMENT ON COLUMN edges.source_line IS 'Line number where the relationship occurs';

-- ============================================================================
-- Verify tables are created
-- ============================================================================
DO $$
BEGIN
    RAISE NOTICE 'Created tables:';
    RAISE NOTICE '  - repos';
    RAISE NOTICE '  - branches';
    RAISE NOTICE '  - commits';
    RAISE NOTICE '  - files';
    RAISE NOTICE '  - symbols';
    RAISE NOTICE '  - edges';
END $$;
