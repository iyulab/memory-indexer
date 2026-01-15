# MemoryChatApp Development Script
# Launches backend and frontend in separate Windows Terminal tabs

$ErrorActionPreference = "Stop"
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$frontendPath = Join-Path $scriptPath "frontend"

Write-Host "Starting MemoryChatApp Development Environment..." -ForegroundColor Cyan
Write-Host ""

# Check for .env file
$envPaths = @(
    (Join-Path $scriptPath ".env"),
    (Join-Path $scriptPath ".." ".." ".env")
)
$envFound = $false
foreach ($envPath in $envPaths) {
    if (Test-Path $envPath) {
        Write-Host "[ENV] Found: $envPath" -ForegroundColor Green
        $envFound = $true
        break
    }
}
if (-not $envFound) {
    Write-Host "[ENV] No .env file found (using LMSupply Local for embedding)" -ForegroundColor Yellow
}

# Check if npm is installed
$npmPath = Get-Command npm -ErrorAction SilentlyContinue
if (-not $npmPath) {
    Write-Host "[ERROR] npm is not installed. Please install Node.js first." -ForegroundColor Red
    exit 1
}

# Install frontend dependencies if needed
$nodeModulesPath = Join-Path $frontendPath "node_modules"
if (-not (Test-Path $nodeModulesPath)) {
    Write-Host "[FRONTEND] Installing dependencies..." -ForegroundColor Yellow
    Push-Location $frontendPath
    npm install
    Pop-Location
}

Write-Host ""
Write-Host "Starting services:" -ForegroundColor Cyan
Write-Host "  Backend:  http://localhost:5000 (ASP.NET)" -ForegroundColor White
Write-Host "  Frontend: http://localhost:3000 (Vite)" -ForegroundColor White
Write-Host ""
Write-Host "Access the app at: http://localhost:5000" -ForegroundColor Green
Write-Host ""

# Check if Windows Terminal is available
$wtPath = Get-Command wt -ErrorAction SilentlyContinue
if (-not $wtPath) {
    Write-Host "[ERROR] Windows Terminal (wt) not found. Please install it from Microsoft Store." -ForegroundColor Red
    exit 1
}

# Launch Windows Terminal with two tabs
wt --window new `
    --title "Backend (ASP.NET :5000)" -d "$scriptPath" cmd /k "dotnet run" `; `
    new-tab --title "Frontend (Vite :3000)" -d "$frontendPath" cmd /k "npm run dev"

Write-Host "[STARTED] Windows Terminal launched with Backend and Frontend tabs" -ForegroundColor Green
Write-Host "[TIP] Open http://localhost:5000 in your browser" -ForegroundColor Cyan
