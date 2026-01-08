# Memory Indexer Benchmark Runner
# Usage: .\run_bench.ps1 [-Filter <pattern>] [-ExportJson] [-Quick]

param(
    [string]$Filter = "",
    [switch]$ExportJson,
    [switch]$Quick
)

$ErrorActionPreference = "Stop"
$benchmarkProject = "$PSScriptRoot\MemoryIndexer.Benchmarks"
$outputDir = "$PSScriptRoot\results"
$timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Memory Indexer Benchmark Runner" -ForegroundColor Cyan
Write-Host "  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Ensure output directory exists
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
    Write-Host "Created results directory: $outputDir" -ForegroundColor Yellow
}

# Build arguments
$runArgs = @(
    "run"
    "--project", $benchmarkProject
    "--configuration", "Release"
    "--"
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

if ($ExportJson) {
    $runArgs += "--exporters", "json"
    Write-Host "Export: JSON enabled" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Starting benchmarks..." -ForegroundColor Green
Write-Host ""

# Run benchmarks
$startTime = Get-Date
dotnet @runArgs
$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Benchmark Complete" -ForegroundColor Cyan
Write-Host "  Duration: $($duration.ToString('hh\:mm\:ss'))" -ForegroundColor Gray
Write-Host "=============================================" -ForegroundColor Cyan

# Copy results to timestamped folder
$resultsSource = "$benchmarkProject\BenchmarkDotNet.Artifacts\results"
if (Test-Path $resultsSource) {
    $destFolder = "$outputDir\$timestamp"
    Copy-Item -Path $resultsSource -Destination $destFolder -Recurse
    Write-Host ""
    Write-Host "Results saved to: $destFolder" -ForegroundColor Green

    # Display summary
    $mdFiles = Get-ChildItem -Path $destFolder -Filter "*.md" -Recurse
    if ($mdFiles) {
        Write-Host ""
        Write-Host "Summary Reports:" -ForegroundColor Cyan
        foreach ($file in $mdFiles) {
            Write-Host "  - $($file.Name)" -ForegroundColor Gray
        }
    }
}
