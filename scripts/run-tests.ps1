<#
.SYNOPSIS
    Run tests for Memory Indexer project
.DESCRIPTION
    Discovers every test project under tests/ and runs them, defaulting to the CI-safe categories.
    Every project must pass; a failure anywhere fails the run.

    The previous version named four projects explicitly — Core, Storage, Intelligence and
    Integration — none of which exist any more (the suite is MemoryIndexer.Tests and
    MemoryIndexer.Sdk.Tests). It also inspected $LASTEXITCODE only once, after the last of the four
    calls, so a failure in any earlier project was discarded. Both faults are invisible while the
    script prints a confident "All tests passed!".
.PARAMETER All
    Include Heavy / LocalModel / GpuStack tests (slow, needs local resources)
.PARAMETER Parallel
    Set max parallel threads (default: 4)
.EXAMPLE
    .\run-tests.ps1
    .\run-tests.ps1 -All
    .\run-tests.ps1 -All -Parallel 2
#>
param(
    [switch]$All,
    [int]$Parallel = 4
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Memory Indexer Test Runner" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build "$projectRoot" --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build succeeded!" -ForegroundColor Green
Write-Host ""

# Discovery rather than a fixed list: a renamed or added project silently drops out of a list, and
# the run still reports success because nothing failed — nothing ran.
$testProjects = @(
    Get-ChildItem -Path (Join-Path $projectRoot "tests") -Filter "*.Tests.csproj" -Recurse -File |
        ForEach-Object { $_.FullName } |
        Sort-Object
)

if ($testProjects.Count -eq 0) {
    Write-Host "ERROR: no test projects found under tests/." -ForegroundColor Red
    Write-Host "Discovery returning nothing means a broken path, never 'nothing to test'."
    exit 1
}

# Matches the CI workflow's filter, so a local green run means the same thing CI means.
$filter = if ($All) { "" } else { "Category!=LocalModel&Category!=Heavy&Category!=GpuStack" }

if ($All) {
    Write-Host "Running ALL tests (including Heavy - this may take a while)" -ForegroundColor Yellow
    Write-Host "Parallel threads: $Parallel" -ForegroundColor Gray
} else {
    Write-Host "Running CI-safe tests (excluding LocalModel, Heavy, GpuStack)" -ForegroundColor Yellow
}
Write-Host "Discovered $($testProjects.Count) test project(s)" -ForegroundColor Gray
Write-Host ""

$failedProjects = @()

foreach ($project in $testProjects) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($project)
    Write-Host "=== $name ===" -ForegroundColor Magenta

    $testArgs = @("test", $project, "--no-build", "--verbosity", "normal")
    if ($filter) { $testArgs += @("--filter", $filter) }
    if ($All)    { $testArgs += @("--", "xunit.maxParallelThreads=$Parallel") }

    & dotnet @testArgs
    # Checked per project, not once at the end: the exit code of the last call says nothing about
    # the ones before it.
    if ($LASTEXITCODE -ne 0) { $failedProjects += $name }
    Write-Host ""
}

if ($failedProjects.Count -gt 0) {
    Write-Host "Tests failed in: $($failedProjects -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "All tests passed!" -ForegroundColor Green
