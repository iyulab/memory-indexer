#!/bin/bash
# Memory Indexer Test Runner
# Usage:
#   ./run-tests.sh          # CI-safe tests only
#   ./run-tests.sh --all    # All tests including Heavy

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

ALL_TESTS=false
PARALLEL=4

while [[ $# -gt 0 ]]; do
    case $1 in
        --all|-a)
            ALL_TESTS=true
            shift
            ;;
        --parallel|-p)
            PARALLEL="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

echo "Memory Indexer Test Runner"
echo "========================="
echo ""

# Build first
echo "Building solution..."
dotnet build "$PROJECT_ROOT" --verbosity quiet
echo "Build succeeded!"
echo ""

if [ "$ALL_TESTS" = true ]; then
    echo "Running ALL tests (including Heavy - this may take a while)..."
    echo "Parallel threads: $PARALLEL"
    echo ""

    dotnet test "$PROJECT_ROOT" --no-build --verbosity normal -- xunit.maxParallelThreads=$PARALLEL
else
    echo "Running CI-safe tests (excluding Heavy category)..."
    echo ""

    echo "=== Core Tests ==="
    dotnet test "$PROJECT_ROOT/tests/MemoryIndexer.Core.Tests" --no-build --verbosity normal

    echo ""
    echo "=== Storage Tests ==="
    dotnet test "$PROJECT_ROOT/tests/MemoryIndexer.Storage.Tests" --no-build --verbosity normal

    echo ""
    echo "=== Intelligence Tests ==="
    dotnet test "$PROJECT_ROOT/tests/MemoryIndexer.Intelligence.Tests" --no-build --verbosity normal

    echo ""
    echo "=== Integration Tests (CI-safe only) ==="
    dotnet test "$PROJECT_ROOT/tests/MemoryIndexer.Integration.Tests" --no-build --verbosity normal --filter "Category!=Heavy"
fi

echo ""
echo "All tests passed!"
