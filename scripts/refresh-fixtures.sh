#!/usr/bin/env bash
set -euo pipefail

# refresh-fixtures.sh
# Refreshes test fixtures by:
# 1. Cloning/updating Git mirrors
# 2. Starting PostgreSQL and MCP server
# 3. Indexing repositories
# 4. Dumping database snapshot
# 5. Cleaning up

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
FIXTURES_DIR="$PROJECT_ROOT/tests/fixtures"
REPOS_DIR="$FIXTURES_DIR/repos"
DUMPS_DIR="$FIXTURES_DIR/dumps"

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
REPOS=(
    "https://github.com/ItBurnsWhenICommit/project-indexer-mcp.git"
    "https://github.com/Pulsar4xDevs/Pulsar4x.git"
)

DB_NAME="project_indexer"
DB_USER="postgres"
DB_PASSWORD="postgres"
DB_HOST="localhost"
DB_PORT="5432"

# Step 1: Clone or update Git mirrors
log_info "Step 1: Updating Git mirrors..."
mkdir -p "$REPOS_DIR"

for repo_url in "${REPOS[@]}"; do
    repo_name=$(basename "$repo_url" .git)
    mirror_path="$REPOS_DIR/${repo_name}.git"

    if [ -d "$mirror_path" ]; then
        log_info "Updating existing mirror: $repo_name"
        cd "$mirror_path"
        git fetch --all --prune
    else
        log_info "Cloning new mirror: $repo_name"
        git clone --mirror "$repo_url" "$mirror_path"
    fi
done

log_info "✓ Git mirrors updated"

# Step 2: Start PostgreSQL
log_info "Step 2: Starting PostgreSQL..."
cd "$PROJECT_ROOT/database"

# Check if already running
if docker compose ps | grep -q "postgres.*running"; then
    log_info "PostgreSQL already running"
else
    docker compose up -d
    log_info "Waiting for PostgreSQL to be ready..."
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
fi

# Step 3: Reset database schema
log_info "Step 3: Resetting database schema..."
cd "$PROJECT_ROOT/database"

# Drop and recreate database
docker compose exec -T postgres psql -U "$DB_USER" -c "DROP DATABASE IF EXISTS $DB_NAME;" || true
docker compose exec -T postgres psql -U "$DB_USER" -c "CREATE DATABASE $DB_NAME;"

# Run migrations
log_info "Running migrations..."
docker compose exec -T postgres psql -U "$DB_USER" -d "$DB_NAME" < schema/00_extensions.sql
docker compose exec -T postgres psql -U "$DB_USER" -d "$DB_NAME" < schema/01_enums.sql
docker compose exec -T postgres psql -U "$DB_USER" -d "$DB_NAME" < schema/02_tables.sql
docker compose exec -T postgres psql -U "$DB_USER" -d "$DB_NAME" < schema/03_chunks_embeddings.sql
docker compose exec -T postgres psql -U "$DB_USER" -d "$DB_NAME" < schema/04_functions.sql
docker compose exec -T postgres psql -U "$DB_USER" -d "$DB_NAME" < schema/05_materialized_views.sql

log_info "✓ Database schema initialized"

# Step 4: Build and run MCP server to index repositories
log_info "Step 4: Building MCP server..."
cd "$PROJECT_ROOT"
dotnet build ProjectIndexerMcp/ProjectIndexerMcp.csproj -c Release

log_info "Step 5: Configuring and indexing repositories..."
log_warn "NOTE: This step requires manual intervention:"
log_warn "1. Configure repositories in ProjectIndexerMcp/appsettings.json:"
log_warn ""
log_warn "   \"Repositories\": ["
log_warn "     {"
log_warn "       \"Name\": \"project-indexer-mcp\","
log_warn "       \"RemoteUrl\": \"file://$REPOS_DIR/project-indexer-mcp.git\","
log_warn "       \"DefaultBranch\": \"main\""
log_warn "     },"
log_warn "     {"
log_warn "       \"Name\": \"Pulsar4x\","
log_warn "       \"RemoteUrl\": \"file://$REPOS_DIR/Pulsar4x.git\","
log_warn "       \"DefaultBranch\": \"master\""
log_warn "     }"
log_warn "   ]"
log_warn ""
log_warn "2. Start the MCP server:"
log_warn "   dotnet run --project ProjectIndexerMcp/ProjectIndexerMcp.csproj -c Release"
log_warn ""
log_warn "3. The server will automatically index the default branches on startup"
log_warn "4. Wait for indexing to complete (check logs for 'Completed automatic indexing')"
log_warn "5. Press ENTER to continue with database dump..."
log_warn ""
read -p "Press ENTER when indexing is complete: "

# Step 6: Verify data exists
log_info "Step 6: Verifying indexed data..."
cd "$PROJECT_ROOT/database"

symbol_count=$(docker compose exec -T postgres psql -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM symbols;")
chunk_count=$(docker compose exec -T postgres psql -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM code_chunks;")
embedding_count=$(docker compose exec -T postgres psql -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM embeddings;")

log_info "Database statistics:"
log_info "  Symbols: $(echo $symbol_count | xargs)"
log_info "  Code chunks: $(echo $chunk_count | xargs)"
log_info "  Embeddings: $(echo $embedding_count | xargs)"

if [ "$(echo $symbol_count | xargs)" -eq 0 ]; then
    log_error "No symbols found! Indexing may have failed."
    exit 1
fi

log_info "✓ Data verification passed"

# Step 7: Dump database
log_info "Step 7: Creating database dump..."
mkdir -p "$DUMPS_DIR"

timestamp=$(date +%Y%m%d_%H%M%S)
dump_file="$DUMPS_DIR/project_indexer_${timestamp}.dump"
latest_link="$DUMPS_DIR/project_indexer_latest.dump"

docker compose exec -T postgres pg_dump -U "$DB_USER" -d "$DB_NAME" --format=custom > "$dump_file"

# Create/update symlink to latest dump
rm -f "$latest_link"
ln -s "$(basename "$dump_file")" "$latest_link"

log_info "✓ Database dump created: $dump_file"
log_info "✓ Latest dump symlink: $latest_link"

# Step 8: Create metadata file
log_info "Step 8: Creating metadata file..."
metadata_file="$DUMPS_DIR/project_indexer_${timestamp}.metadata.json"

cat > "$metadata_file" <<EOF
{
  "created_at": "$(date -Iseconds)",
  "repositories": [
    {
      "name": "project-indexer-mcp",
      "url": "https://github.com/ItBurnsWhenICommit/project-indexer-mcp.git",
      "mirror_path": "$REPOS_DIR/project-indexer-mcp.git"
    },
    {
      "name": "Pulsar4x",
      "url": "https://github.com/Pulsar4xDevs/Pulsar4x.git",
      "mirror_path": "$REPOS_DIR/Pulsar4x.git"
    }
  ],
  "statistics": {
    "symbols": $(echo $symbol_count | xargs),
    "code_chunks": $(echo $chunk_count | xargs),
    "embeddings": $(echo $embedding_count | xargs)
  },
  "database": {
    "name": "$DB_NAME",
    "dump_file": "$(basename "$dump_file")"
  }
}
EOF

log_info "✓ Metadata file created: $metadata_file"

# Step 9: Summary
log_info ""
log_info "========================================="
log_info "Fixture refresh complete!"
log_info "========================================="
log_info "Git mirrors: $REPOS_DIR"
log_info "Database dump: $dump_file"
log_info "Metadata: $metadata_file"
log_info ""
log_info "To use in tests:"
log_info "  1. Restore dump: pg_restore -U postgres -d test_db $dump_file"
log_info "  2. Copy mirrors to test working directory"
log_info "  3. Run integration tests"
log_info ""
log_info "To refresh again: $0"
log_info "========================================="

