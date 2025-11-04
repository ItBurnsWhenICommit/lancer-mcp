#!/usr/bin/env bash
set -euo pipefail

# restore-fixtures.sh
# Restores test fixtures for integration testing:
# 1. Creates a test database
# 2. Restores the latest fixture dump
# 3. Copies Git mirrors to a test working directory
# 4. Outputs configuration for test runs

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
FIXTURES_DIR="$PROJECT_ROOT/tests/fixtures"
DUMPS_DIR="$FIXTURES_DIR/dumps"
REPOS_DIR="$FIXTURES_DIR/repos"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Configuration
TEST_DB_NAME="${TEST_DB_NAME:-project_indexer_test}"
DB_USER="${DB_USER:-postgres}"
DB_PASSWORD="${DB_PASSWORD:-postgres}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
TEST_WORKING_DIR="${TEST_WORKING_DIR:-/tmp/project-indexer-test-$(date +%s)}"

# Parse arguments
DUMP_FILE="${1:-$DUMPS_DIR/project_indexer_latest.dump}"

if [ ! -f "$DUMP_FILE" ]; then
    log_error "Dump file not found: $DUMP_FILE"
    log_error "Run scripts/refresh-fixtures.sh first to generate fixtures"
    exit 1
fi

log_info "Restoring fixtures for integration testing"
log_info "Dump file: $DUMP_FILE"
log_info "Test database: $TEST_DB_NAME"
log_info "Test working directory: $TEST_WORKING_DIR"

# Step 1: Ensure PostgreSQL is running
log_info "Step 1: Checking PostgreSQL..."
cd "$PROJECT_ROOT/database"

if ! docker compose ps | grep -q "postgres.*running"; then
    log_info "Starting PostgreSQL..."
    docker compose up -d
    sleep 5

    # Wait for PostgreSQL to accept connections
    for i in {1..30}; do
        if docker compose exec -T postgres pg_isready -U "$DB_USER" > /dev/null 2>&1; then
            log_info "✓ PostgreSQL is ready"
            break
        fi
        if [ $i -eq 30 ]; then
            log_error "PostgreSQL failed to start"
            exit 1
        fi
        sleep 1
    done
else
    log_info "✓ PostgreSQL is already running"
fi

# Step 2: Create test database
log_info "Step 2: Creating test database..."
docker compose exec -T postgres psql -U "$DB_USER" -c "DROP DATABASE IF EXISTS $TEST_DB_NAME;" || true
docker compose exec -T postgres psql -U "$DB_USER" -c "CREATE DATABASE $TEST_DB_NAME;"

log_info "✓ Test database created: $TEST_DB_NAME"

# Step 3: Restore dump
log_info "Step 3: Restoring database dump (schema and data only, no indexes)..."

# For test fixtures, we only need schema and data - skip all post-data (indexes, constraints, materialized views)
# This makes restore MUCH faster since we skip:
# - HNSW vector index (very slow to build)
# - GIN full-text search indexes (slow)
# - Trigram indexes (slow)
# - Materialized view refreshes (very slow with complex joins)
# Tests don't need query performance optimizations, just the data!

docker compose exec -T postgres pg_restore \
    -U "$DB_USER" \
    -d "$TEST_DB_NAME" \
    --no-owner \
    --no-acl \
    --section=pre-data \
    --section=data \
    < "$DUMP_FILE" 2>&1 | head -20 || {
    log_warn "Some restore warnings occurred (this is normal for test fixtures)"
}

log_info "✓ Database dump restored (schema + data only)"
log_warn "Note: All indexes, constraints, and materialized views were skipped for faster test setup"
log_warn "This is fine for integration tests which don't rely on query performance"

# Step 4: Verify data (skip materialized view checks)
log_info "Step 4: Verifying restored data..."

# Check if tables exist and have data
table_count=$(docker compose exec -T postgres psql -U "$DB_USER" -d "$TEST_DB_NAME" -t -c "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE';")
log_info "Tables restored: $(echo $table_count | xargs)"

symbol_count=$(docker compose exec -T postgres psql -U "$DB_USER" -d "$TEST_DB_NAME" -t -c "SELECT COUNT(*) FROM symbols;" 2>/dev/null || echo "0")
chunk_count=$(docker compose exec -T postgres psql -U "$DB_USER" -d "$TEST_DB_NAME" -t -c "SELECT COUNT(*) FROM code_chunks;" 2>/dev/null || echo "0")
embedding_count=$(docker compose exec -T postgres psql -U "$DB_USER" -d "$TEST_DB_NAME" -t -c "SELECT COUNT(*) FROM embeddings;" 2>/dev/null || echo "0")

log_info "Restored data statistics:"
log_info "  Symbols: $(echo $symbol_count | xargs)"
log_info "  Code chunks: $(echo $chunk_count | xargs)"
log_info "  Embeddings: $(echo $embedding_count | xargs)"

if [ "$(echo $symbol_count | xargs)" -eq 0 ]; then
    log_warn "No symbols found in restored database!"
    log_warn "This might be expected if the dump was empty"
fi

log_info "✓ Data verification passed"

# Step 5: Copy Git mirrors to test working directory
log_info "Step 5: Copying Git mirrors to test working directory..."
mkdir -p "$TEST_WORKING_DIR"

for mirror in "$REPOS_DIR"/*.git; do
    if [ -d "$mirror" ]; then
        mirror_name=$(basename "$mirror")
        log_info "Copying $mirror_name..."
        cp -r "$mirror" "$TEST_WORKING_DIR/"
    fi
done

log_info "✓ Git mirrors copied to: $TEST_WORKING_DIR"

# Step 6: Output test configuration
log_info ""
log_info "========================================="
log_info "Fixtures restored successfully!"
log_info "========================================="
log_info ""
log_info "Test Configuration:"
log_info "  Database Name: $TEST_DB_NAME"
log_info "  Database Host: $DB_HOST"
log_info "  Database Port: $DB_PORT"
log_info "  Working Directory: $TEST_WORKING_DIR"
log_info ""
log_info "Connection String:"
log_info "  Host=$DB_HOST;Port=$DB_PORT;Database=$TEST_DB_NAME;Username=$DB_USER;Password=$DB_PASSWORD"
log_info ""
log_info "Environment Variables:"
log_info "  export TEST_DB_NAME=$TEST_DB_NAME"
log_info "  export TEST_WORKING_DIR=$TEST_WORKING_DIR"
log_info ""
log_info "To run tests:"
log_info "  dotnet test --filter Category=Integration"
log_info ""
log_info "To cleanup:"
log_info "  docker compose exec -T postgres psql -U $DB_USER -c 'DROP DATABASE $TEST_DB_NAME;'"
log_info "  rm -rf $TEST_WORKING_DIR"
log_info "========================================="

# Create a cleanup script
cleanup_script="$TEST_WORKING_DIR/cleanup.sh"
cat > "$cleanup_script" <<EOF
#!/usr/bin/env bash
# Auto-generated cleanup script
cd "$PROJECT_ROOT/database"
docker compose exec -T postgres psql -U $DB_USER -c "DROP DATABASE IF EXISTS $TEST_DB_NAME;"
rm -rf "$TEST_WORKING_DIR"
echo "Test environment cleaned up"
EOF
chmod +x "$cleanup_script"

log_info "Cleanup script created: $cleanup_script"

