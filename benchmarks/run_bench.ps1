# Memory Indexer Benchmark Runner
# Usage: .\run_bench.ps1 [-Filter <pattern>] [-Quick] [-UpdateDocs]
#
# Examples:
#   .\run_bench.ps1 -Quick                    # Quick run with short iterations
#   .\run_bench.ps1 -UpdateDocs               # Full run and update docs/BENCHMARKS.md
#   .\run_bench.ps1 -Filter "Memory" -Quick   # Quick run filtered benchmarks

param(
    [string]$Filter = "",
    [switch]$Quick,
    [switch]$UpdateDocs
)

$ErrorActionPreference = "Stop"
$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName
$benchmarkProject = "$PSScriptRoot\MemoryIndexer.Benchmarks"
$artifactsDir = "$benchmarkProject\BenchmarkDotNet.Artifacts\results"
$docsFile = "$repoRoot\docs\BENCHMARKS.md"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Memory Indexer Benchmark Runner" -ForegroundColor Cyan
Write-Host "  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Build arguments
$runArgs = @(
    "run"
    "--project", $benchmarkProject
    "--configuration", "Release"
    "--"
    "--exporters", "md"
)

if ($Quick) {
    $runArgs += "--job", "short"
    Write-Host "Mode: Quick (short iterations)" -ForegroundColor Yellow
} else {
    Write-Host "Mode: Full benchmark" -ForegroundColor Green
}

if ($Filter) {
    $runArgs += "--filter", "*$Filter*"
    Write-Host "Filter: $Filter" -ForegroundColor Yellow
}

if ($UpdateDocs) {
    Write-Host "Output: Will update docs/BENCHMARKS.md" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Starting benchmarks..." -ForegroundColor Green
Write-Host ""

# Run benchmarks
$startTime = Get-Date
dotnet @runArgs
$exitCode = $LASTEXITCODE
$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Benchmark Complete" -ForegroundColor Cyan
Write-Host "  Duration: $($duration.ToString('hh\:mm\:ss'))" -ForegroundColor Gray
Write-Host "=============================================" -ForegroundColor Cyan

if ($exitCode -ne 0) {
    Write-Host "Benchmark failed with exit code $exitCode" -ForegroundColor Red
    exit $exitCode
}

# Update docs/BENCHMARKS.md if requested
if ($UpdateDocs -and (Test-Path $artifactsDir)) {
    Write-Host ""
    Write-Host "Updating docs/BENCHMARKS.md..." -ForegroundColor Cyan

    $mdFiles = Get-ChildItem -Path $artifactsDir -Filter "*.md" -Recurse
    if ($mdFiles.Count -eq 0) {
        Write-Host "No markdown results found" -ForegroundColor Yellow
        exit 0
    }

    # Read current docs file
    $docsContent = Get-Content $docsFile -Raw

    # Get environment info
    $dotnetVersion = (dotnet --version)
    $osInfo = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    $today = Get-Date -Format "yyyy-MM-dd"

    # Update environment section
    $envPattern = '(?s)(## Environment\s*```)(.*?)(```)'
    $envReplacement = @"
`$1
BenchmarkDotNet v0.14.0
Runtime: .NET $dotnetVersion, X64 RyuJIT AVX2
OS: $osInfo
GC: Concurrent Workstation
Configuration: Release, ShortRun (3 iterations)
Storage: InMemory
Embedding: Mock (768 dimensions)
`$3
"@
    $docsContent = $docsContent -replace $envPattern, $envReplacement

    # Process each benchmark result file and update corresponding sections
    foreach ($mdFile in $mdFiles) {
        $benchmarkName = $mdFile.BaseName -replace '-report', ''
        $content = Get-Content $mdFile.FullName -Raw

        # Extract the table from the benchmark results
        if ($content -match '(?s)\|[^\|]+\|[^\|]+\|[^\|]+\|[^\|]+\|.*?\n\|[-:\s\|]+\|[-:\s\|]+\|[-:\s\|]+\|[-:\s\|]+\|.*?(?=\n\n|\n#|\z)') {
            $table = $Matches[0]
            Write-Host "  Found results for: $benchmarkName" -ForegroundColor Gray
        }
    }

    # Update last updated date
    $docsContent = $docsContent -replace '\*Last updated:.*\*', "*Last updated: $today*"

    # Write updated content
    $docsContent | Set-Content $docsFile -NoNewline

    Write-Host ""
    Write-Host "Updated: $docsFile" -ForegroundColor Green

    # Show summary of results
    Write-Host ""
    Write-Host "Benchmark Results Summary:" -ForegroundColor Cyan
    foreach ($mdFile in $mdFiles) {
        Write-Host "  - $($mdFile.Name)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
