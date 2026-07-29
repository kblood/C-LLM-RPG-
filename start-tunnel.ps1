# Start RPG Web Server + SSH Tunnel to rpg.dionysus.dk
# Just run this one script - it starts everything and keeps it running.

param(
    [int]$Port = 5100,
    [string]$SshKeyPath = $env:RPGWEB_SSH_KEY_PATH,
    [string]$SshServer = $env:RPGWEB_SSH_SERVER
)

$ProjectDir = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($SshKeyPath)) {
    throw "Set RPGWEB_SSH_KEY_PATH or pass -SshKeyPath with the private-key file used for the tunnel."
}
if ([string]::IsNullOrWhiteSpace($SshServer)) {
    throw "Set RPGWEB_SSH_SERVER or pass -SshServer in user@host form."
}

$resolvedKey = (Resolve-Path -LiteralPath $SshKeyPath -ErrorAction Stop).Path

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "   RPG Web Server + Tunnel Launcher   " -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# --- Step 1: Build ---
Write-Host "Building RPGWeb..." -ForegroundColor Yellow
dotnet build "$ProjectDir\RPGWeb\RPGWeb.csproj" --nologo -v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED. Fix errors and try again." -ForegroundColor Red
    exit 1
}
Write-Host "Build OK." -ForegroundColor Green
Write-Host ""

# --- Step 2: Start web server as background job ---
Write-Host "Starting RPGWeb on port $Port..." -ForegroundColor Yellow
$webJob = Start-Job -ScriptBlock {
    param($dir, $port)
    Set-Location $dir
    $env:RPGWEB_LISTEN_URL = "http://127.0.0.1:$port"
    dotnet run --project RPGWeb --no-build
} -ArgumentList $ProjectDir, $Port

# Wait for server to be ready
$ready = $false
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Seconds 1
    try {
        $r = Invoke-WebRequest "http://localhost:$Port" -UseBasicParsing -TimeoutSec 1 -ErrorAction Stop
        $ready = $true; break
    } catch {}
}

if ($ready) {
    Write-Host "RPGWeb is running at http://localhost:$Port" -ForegroundColor Green
} else {
    Write-Host "RPGWeb may still be starting up..." -ForegroundColor Yellow
}
Write-Host ""

# --- Step 3: Tunnel loop ---
Write-Host "Opening SSH tunnel to rpg.dionysus.dk..." -ForegroundColor Yellow
Write-Host "Players can connect at: https://rpg.dionysus.dk" -ForegroundColor Green
Write-Host "Press Ctrl+C to stop everything." -ForegroundColor Gray
Write-Host ""

try {
    while ($true) {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Tunnel connecting..." -ForegroundColor DarkCyan
        ssh -i $resolvedKey `
            -o StrictHostKeyChecking=accept-new `
            -o ServerAliveInterval=30 `
            -o ServerAliveCountMax=3 `
            -o ExitOnForwardFailure=yes `
            -N -R "${Port}:127.0.0.1:${Port}" `
            $SshServer
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Tunnel dropped. Reconnecting in 5s..." -ForegroundColor Red
        Start-Sleep -Seconds 5
    }
} finally {
    # Ctrl+C cleanup
    Write-Host ""
    Write-Host "Stopping RPGWeb..." -ForegroundColor Yellow
    Stop-Job $webJob -ErrorAction SilentlyContinue
    Remove-Job $webJob -ErrorAction SilentlyContinue
    Write-Host "Stopped." -ForegroundColor Gray
}
