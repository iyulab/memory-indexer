<#
.SYNOPSIS
    Run tests for Memory Indexer project
.DESCRIPTION
    Provides options to run all tests or only CI-safe tests (excluding Heavy category)
.PARAMETER All
    Run all tests including heavy/GPU tests
.PARAMETER CIOnly
    Run only CI-safe tests (excludes Heavy category)
.PARAMETER Parallel
    Set max parallel threads (default: 4)
.EXAMPLE
    .\run-tests.ps1 -CIOnly
    .\run-tests.ps1 -All
    .\run-tests.ps1 -All -Parallel 2
#>
param(
    [switch]$All,
    [switch]$CIOnly,
    [int]$Parallel = 4
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Memory Indexer Test Runner" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host ""

# Build first
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build "$projectRoot" --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build succeeded!" -ForegroundColor Green
Write-Host ""

if ($CIOnly -or (-not $All)) {
    Write-Host "Running CI-safe tests (excluding Heavy category)..." -ForegroundColor Yellow
    Write-Host ""

    # Unit tests
    Write-Host "=== Core Tests ===" -ForegroundColor Magenta
    dotnet test "$projectRoot\tests\MemoryIndexer.Core.Tests" --no-build --verbosity normal

    Write-Host ""
    Write-Host "=== Storage Tests ===" -ForegroundColor Magenta
    dotnet test "$projectRoot\tests\MemoryIndexer.Storage.Tests" --no-build --verbosity normal

    Write-Host ""
    Write-Host "=== Intelligence Tests ===" -ForegroundColor Magenta
    dotnet test "$projectRoot\tests\MemoryIndexer.Intelligence.Tests" --no-build --verbosity normal

    Write-Host ""
    Write-Host "=== Integration Tests (CI-safe only) ===" -ForegroundColor Magenta
    dotnet test "$projectRoot\tests\MemoryIndexer.Integration.Tests" --no-build --verbosity normal --filter "Category!=Heavy"
}
else {
    Write-Host "Running ALL tests (including Heavy - this may take a while)..." -ForegroundColor Yellow
    Write-Host "Parallel threads: $Parallel" -ForegroundColor Gray
    Write-Host ""

    dotnet test "$projectRoot" --no-build --verbosity normal -- xunit.maxParallelThreads=$Parallel
}

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "All tests passed!" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "Some tests failed." -ForegroundColor Red
    exit 1
}
