#!/bin/bash
# Memory Indexer Test Runner
# Usage:
#   ./run-tests.sh          # CI-safe tests only
#   ./run-tests.sh --all    # All tests including Heavy
#
# Test projects are discovered, not listed. The previous version named four projects — Core,
# Storage, Intelligence, Integration — that no longer exist; with `set -e`, the first `dotnet test`
# against a missing path aborted the whole run, so this script could not succeed at all. (Its
# PowerShell sibling had the same dead list and failed the opposite way: it checked the exit code
# only once, after the last call, and reported success.)

set -euo pipefail

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

echo "Building solution..."
dotnet build "$PROJECT_ROOT" --verbosity quiet
echo "Build succeeded!"
echo ""

mapfile -t TEST_PROJECTS < <(find "$PROJECT_ROOT/tests" -name '*.Tests.csproj' | sort)

if [[ ${#TEST_PROJECTS[@]} -eq 0 ]]; then
    echo "ERROR: no test projects found under tests/."
    echo "Discovery returning nothing means a broken path, never 'nothing to test'."
    exit 1
fi

# Same filter as the CI workflow, so a green run here means what a green run there means.
if [[ "$ALL_TESTS" == true ]]; then
    echo "Running ALL tests (including Heavy - this may take a while)..."
    echo "Parallel threads: $PARALLEL"
    FILTER=""
else
    echo "Running CI-safe tests (excluding LocalModel, Heavy, GpuStack)..."
    FILTER="Category!=LocalModel&Category!=Heavy&Category!=GpuStack"
fi
echo "Discovered ${#TEST_PROJECTS[@]} test project(s)"
echo ""

# Collect failures across every project instead of aborting on the first, so one run reports every
# broken project rather than only the earliest one.
set +e
FAILED=()
for project in "${TEST_PROJECTS[@]}"; do
    name="$(basename "$project" .csproj)"
    echo "=== $name ==="

    if [[ -n "$FILTER" ]]; then
        dotnet test "$project" --no-build --verbosity normal --filter "$FILTER"
    else
        dotnet test "$project" --no-build --verbosity normal -- "xunit.maxParallelThreads=$PARALLEL"
    fi

    [[ $? -ne 0 ]] && FAILED+=("$name")
    echo ""
done
set -e

if [[ ${#FAILED[@]} -gt 0 ]]; then
    echo "Tests failed in: ${FAILED[*]}"
    exit 1
fi

echo "All tests passed!"
