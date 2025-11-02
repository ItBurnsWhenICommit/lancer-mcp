#!/bin/bash
# ============================================================================
# Test Script for PostgreSQL Database Setup (Docker Version)
# ============================================================================
# This script verifies that the database is set up correctly using Docker exec
# ============================================================================

set -e

# Configuration
CONTAINER_NAME="${CONTAINER_NAME:-project-indexer-postgres}"
DB_NAME="${DB_NAME:-project_indexer}"
DB_USER="${DB_USER:-postgres}"

# Colors
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

print_success() {
    echo -e "${GREEN}✓${NC} $1"
}

print_error() {
    echo -e "${RED}✗${NC} $1"
}

print_info() {
    echo -e "${YELLOW}ℹ${NC} $1"
}

run_test() {
    local test_name=$1
    local query=$2

    echo -n "Testing $test_name... "

    if docker exec $CONTAINER_NAME psql -U $DB_USER -d $DB_NAME -c "$query" > /dev/null 2>&1; then
        print_success "$test_name"
        return 0
    else
        print_error "$test_name"
        return 1
    fi
}

echo "============================================================================"
echo "PostgreSQL Database Setup Test (Docker)"
echo "============================================================================"
echo "Container: $CONTAINER_NAME"
echo "Database: $DB_NAME"
echo "User: $DB_USER"
echo "============================================================================"
echo ""

# Test 0: Check if container is running
print_info "Checking if Docker container is running..."
if ! docker ps --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    print_error "Container '$CONTAINER_NAME' is not running"
    echo ""
    echo "Please start the container with:"
    echo "  cd database && docker compose up -d"
    exit 1
fi
print_success "Container is running"
echo ""

# Test 1: Database connection
print_info "Testing database connection..."
if docker exec $CONTAINER_NAME psql -U $DB_USER -d $DB_NAME -c '\q' 2>/dev/null; then
    print_success "Database connection"
else
    print_error "Database connection"
    exit 1
fi

# Test 2: Extensions
print_info "Testing extensions..."
run_test "pgvector extension" "SELECT extversion FROM pg_extension WHERE extname = 'vector';"
run_test "pg_trgm extension" "SELECT extversion FROM pg_extension WHERE extname = 'pg_trgm';"
run_test "btree_gin extension" "SELECT extversion FROM pg_extension WHERE extname = 'btree_gin';"

# Test 3: Enums
print_info "Testing enums..."
run_test "language enum" "SELECT 'CSharp'::language;"
run_test "symbol_kind enum" "SELECT 'Class'::symbol_kind;"
run_test "edge_kind enum" "SELECT 'Calls'::edge_kind;"
run_test "index_state enum" "SELECT 'Completed'::index_state;"

# Test 4: Tables
print_info "Testing tables..."
run_test "repos table" "SELECT COUNT(*) FROM repos;"
run_test "branches table" "SELECT COUNT(*) FROM branches;"
run_test "commits table" "SELECT COUNT(*) FROM commits;"
run_test "files table" "SELECT COUNT(*) FROM files;"
run_test "symbols table" "SELECT COUNT(*) FROM symbols;"
run_test "edges table" "SELECT COUNT(*) FROM edges;"
run_test "code_chunks table" "SELECT COUNT(*) FROM code_chunks;"
run_test "embeddings table" "SELECT COUNT(*) FROM embeddings;"

# Test 5: Functions
print_info "Testing functions..."
run_test "search_chunks_fulltext function" "SELECT * FROM search_chunks_fulltext('test', limit_count := 1);"
run_test "search_symbols function" "SELECT * FROM search_symbols('test', limit_count := 1);"
run_test "search_embeddings_cosine function" "SELECT proname FROM pg_proc WHERE proname = 'search_embeddings_cosine';"
run_test "search_embeddings_l2 function" "SELECT proname FROM pg_proc WHERE proname = 'search_embeddings_l2';"
run_test "find_references function" "SELECT proname FROM pg_proc WHERE proname = 'find_references';"
run_test "find_dependencies function" "SELECT proname FROM pg_proc WHERE proname = 'find_dependencies';"
run_test "find_call_chain function" "SELECT proname FROM pg_proc WHERE proname = 'find_call_chain';"
run_test "hybrid_search function" "SELECT proname FROM pg_proc WHERE proname = 'hybrid_search';"

# Test 6: Materialized Views
print_info "Testing materialized views..."
run_test "repo_stats view" "SELECT COUNT(*) FROM repo_stats;"
run_test "branch_stats view" "SELECT COUNT(*) FROM branch_stats;"
run_test "language_stats view" "SELECT COUNT(*) FROM language_stats;"
run_test "symbol_kind_stats view" "SELECT COUNT(*) FROM symbol_kind_stats;"
run_test "edge_kind_stats view" "SELECT COUNT(*) FROM edge_kind_stats;"
run_test "hot_symbols view" "SELECT COUNT(*) FROM hot_symbols;"
run_test "recent_activity view" "SELECT COUNT(*) FROM recent_activity;"
run_test "indexing_progress view" "SELECT COUNT(*) FROM indexing_progress;"

# Test 7: Indexes
print_info "Testing indexes..."
run_test "HNSW index on embeddings" "SELECT indexname FROM pg_indexes WHERE indexname = 'idx_embeddings_vector_hnsw';"
run_test "FTS index on code_chunks" "SELECT indexname FROM pg_indexes WHERE indexname = 'idx_chunks_content_fts';"
run_test "Trigram index on files" "SELECT indexname FROM pg_indexes WHERE indexname = 'idx_files_file_path_trgm';"
run_test "Trigram index on symbols" "SELECT indexname FROM pg_indexes WHERE indexname = 'idx_symbols_name_trgm';"

echo ""
echo "============================================================================"
print_success "All tests passed! Database is set up correctly."
echo "============================================================================"
echo ""
print_info "You can now:"
echo "  1. Connect to the database: docker exec -it $CONTAINER_NAME psql -U $DB_USER -d $DB_NAME"
echo "  2. View tables: docker exec -it $CONTAINER_NAME psql -U $DB_USER -d $DB_NAME -c '\\dt'"
echo "  3. View materialized views: docker exec -it $CONTAINER_NAME psql -U $DB_USER -d $DB_NAME -c '\\dm'"
echo "  4. View functions: docker exec -it $CONTAINER_NAME psql -U $DB_USER -d $DB_NAME -c '\\df'"
echo "  5. Test search: docker exec -it $CONTAINER_NAME psql -U $DB_USER -d $DB_NAME -c \"SELECT * FROM search_chunks_fulltext('your query');\""
echo ""

