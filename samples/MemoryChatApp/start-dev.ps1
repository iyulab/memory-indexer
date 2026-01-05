# MemoryChatApp Development Script
# Launches both backend and frontend with hot reload in separate console windows

$ErrorActionPreference = "Stop"
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path

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
$frontendPath = Join-Path $scriptPath "frontend"
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
Write-Host "Press Ctrl+C in each window to stop." -ForegroundColor Gray
Write-Host ""

# Start Backend in new console window
$backendCmd = "cd '$scriptPath'; dotnet watch run --no-hot-reload"
Start-Process powershell -ArgumentList "-NoExit", "-Command", $backendCmd -WorkingDirectory $scriptPath

# Wait a moment for backend to start
Start-Sleep -Seconds 2

# Start Frontend in new console window
$frontendCmd = "cd '$frontendPath'; npm run dev"
Start-Process powershell -ArgumentList "-NoExit", "-Command", $frontendCmd -WorkingDirectory $frontendPath

Write-Host "[STARTED] Both services launched in separate windows" -ForegroundColor Green
Write-Host "[TIP] Open http://localhost:3000 in your browser" -ForegroundColor Cyan
