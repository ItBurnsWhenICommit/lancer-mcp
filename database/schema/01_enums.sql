-- ============================================================================
-- PostgreSQL Enums for Project Indexer MCP
-- ============================================================================
-- This file defines enums that match the C# models in ProjectIndexerMcp/Models
-- ============================================================================

-- Programming language identifier
CREATE TYPE language AS ENUM (
    'Unknown',
    'CSharp',
    'Python',
    'JavaScript',
    'TypeScript',
    'TypeScriptReact',
    'JavaScriptReact',
    'Java',
    'Go',
    'Rust',
    'C',
    'CPlusPlus',
    'Ruby',
    'PHP',
    'Swift',
    'Kotlin',
    'Scala',
    'Haskell',
    'Clojure',
    'Elixir',
    'Erlang',
    'FSharp',
    'OCaml',
    'Dart',
    'Lua',
    'Perl',
    'R',
    'Shell',
    'PowerShell',
    'SQL',
    'HTML',
    'CSS',
    'SCSS',
    'LESS',
    'JSON',
    'YAML',
    'TOML',
    'XML',
    'Markdown',
    'Protobuf',
    'GraphQL'
);

-- Type of code symbol
CREATE TYPE symbol_kind AS ENUM (
    'Unknown',
    'Namespace',
    'Class',
    'Interface',
    'Struct',
    'Enum',
    'Method',
    'Function',
    'Property',
    'Field',
    'Variable',
    'Parameter',
    'Constant',
    'Event',
    'Delegate',
    'Constructor',
    'Destructor',
    'Module',
    'Package',
    'TypeParameter'
);

-- Type of relationship between symbols
CREATE TYPE edge_kind AS ENUM (
    'Unknown',
    'Import',       -- A imports B
    'Inherits',     -- A inherits from B
    'Implements',   -- A implements B
    'Calls',        -- A calls B
    'References',   -- A references B
    'Defines',      -- A defines B
    'Contains',     -- A contains B (parent-child)
    'Overrides',    -- A overrides B
    'TypeOf',       -- A is of type B
    'Returns'       -- A returns type B
);

-- Index state for tracking indexing progress
CREATE TYPE index_state AS ENUM (
    'Pending',      -- Not yet indexed
    'InProgress',   -- Currently being indexed
    'Completed',    -- Successfully indexed
    'Failed',       -- Indexing failed
    'Stale'         -- Needs re-indexing (commit changed)
);

-- Verify enums are created
DO $$
BEGIN
    RAISE NOTICE 'Created enums:';
    RAISE NOTICE '  - language (% values)', (SELECT COUNT(*) FROM pg_enum WHERE enumtypid = 'language'::regtype);
    RAISE NOTICE '  - symbol_kind (% values)', (SELECT COUNT(*) FROM pg_enum WHERE enumtypid = 'symbol_kind'::regtype);
    RAISE NOTICE '  - edge_kind (% values)', (SELECT COUNT(*) FROM pg_enum WHERE enumtypid = 'edge_kind'::regtype);
    RAISE NOTICE '  - index_state (% values)', (SELECT COUNT(*) FROM pg_enum WHERE enumtypid = 'index_state'::regtype);
END $$;

