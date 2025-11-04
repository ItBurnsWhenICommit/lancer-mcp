#!/bin/bash
# ============================================================================
# PostgreSQL Migration Script for Lancer MCP
# ============================================================================
# This script runs all migration files in order to set up the database schema
# ============================================================================

set -e  # Exit on error

# Configuration
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_NAME="${DB_NAME:-lancer}"
DB_USER="${DB_USER:-postgres}"
DB_PASSWORD="${DB_PASSWORD:-postgres}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print colored output
print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to check if PostgreSQL is running
check_postgres() {
    print_info "Checking PostgreSQL connection..."
    if PGPASSWORD=$DB_PASSWORD psql -h $DB_HOST -p $DB_PORT -U $DB_USER -d postgres -c '\q' 2>/dev/null; then
        print_info "PostgreSQL is running"
        return 0
    else
        print_error "Cannot connect to PostgreSQL at $DB_HOST:$DB_PORT"
        return 1
    fi
}

# Function to create database if it doesn't exist
create_database() {
    print_info "Checking if database '$DB_NAME' exists..."
    if PGPASSWORD=$DB_PASSWORD psql -h $DB_HOST -p $DB_PORT -U $DB_USER -d postgres -lqt | cut -d \| -f 1 | grep -qw $DB_NAME; then
        print_info "Database '$DB_NAME' already exists"
    else
        print_info "Creating database '$DB_NAME'..."
        PGPASSWORD=$DB_PASSWORD psql -h $DB_HOST -p $DB_PORT -U $DB_USER -d postgres -c "CREATE DATABASE $DB_NAME;"
        print_info "Database '$DB_NAME' created successfully"
    fi
}

# Function to run a migration file
run_migration() {
    local file=$1
    local filename=$(basename $file)

    print_info "Running migration: $filename"

    if PGPASSWORD=$DB_PASSWORD psql -h $DB_HOST -p $DB_PORT -U $DB_USER -d $DB_NAME -f $file; then
        print_info "✓ $filename completed successfully"
        return 0
    else
        print_error "✗ $filename failed"
        return 1
    fi
}

# Main migration process
main() {
    echo "============================================================================"
    echo "PostgreSQL Migration for Lancer MCP"
    echo "============================================================================"
    echo "Database: $DB_NAME"
    echo "Host: $DB_HOST:$DB_PORT"
    echo "User: $DB_USER"
    echo "============================================================================"
    echo ""

    # Check PostgreSQL connection
    if ! check_postgres; then
        print_error "Migration aborted: Cannot connect to PostgreSQL"
        exit 1
    fi

    # Create database
    create_database

    # Get script directory
    SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
    SCHEMA_DIR="$SCRIPT_DIR/schema"

    # Check if schema directory exists
    if [ ! -d "$SCHEMA_DIR" ]; then
        print_error "Schema directory not found: $SCHEMA_DIR"
        exit 1
    fi

    # Run migrations in order
    print_info "Running migrations from: $SCHEMA_DIR"
    echo ""

    local migration_files=(
        "00_extensions.sql"
        "01_enums.sql"
        "02_tables.sql"
        "03_chunks_embeddings.sql"
        "04_functions.sql"
        "05_materialized_views.sql"
    )

    local failed=0

    for file in "${migration_files[@]}"; do
        local filepath="$SCHEMA_DIR/$file"

        if [ ! -f "$filepath" ]; then
            print_warn "Migration file not found: $file (skipping)"
            continue
        fi

        if ! run_migration "$filepath"; then
            failed=1
            break
        fi
        echo ""
    done

    echo "============================================================================"

    if [ $failed -eq 0 ]; then
        print_info "All migrations completed successfully! ✓"
        echo ""
        print_info "Next steps:"
        echo "  1. Verify extensions: SELECT * FROM pg_extension WHERE extname IN ('vector', 'pg_trgm', 'btree_gin');"
        echo "  2. Check tables: \\dt"
        echo "  3. Check materialized views: \\dm"
        echo "  4. Test hybrid search functions"
        echo ""
        return 0
    else
        print_error "Migration failed! ✗"
        echo ""
        print_info "To rollback, drop the database and try again:"
        echo "  DROP DATABASE $DB_NAME;"
        echo ""
        return 1
    fi
}

# Run main function
main

exit $?
