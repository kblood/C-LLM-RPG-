# Start RPG Web Server + Cloudflare Tunnel
# Usage: .\start-web.ps1
# This starts the Blazor web app on port 5100 and creates a public tunnel URL.

param(
    [int]$Port = 5100,
    [switch]$NoTunnel  # Use -NoTunnel for LAN-only access
)

Write-Host ""
Write-Host "=== C# RPG Web Server ===" -ForegroundColor Cyan
Write-Host ""

# Start the web server in background
Write-Host "Starting web server on port $Port..." -ForegroundColor Yellow
$webJob = Start-Job -ScriptBlock {
    param($port, $dir)
    Set-Location $dir
    dotnet run --project RPGWeb -- --urls "http://0.0.0.0:$port"
} -ArgumentList $Port, $PWD

Start-Sleep -Seconds 3

# Show local access info
$localIP = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notmatch "Loopback|vEthernet|WSL" -and $_.IPAddress -notmatch "^169\." } | Select-Object -First 1).IPAddress
Write-Host ""
Write-Host "Local access:   http://localhost:$Port" -ForegroundColor Green
if ($localIP) {
    Write-Host "LAN access:     http://${localIP}:$Port" -ForegroundColor Green
}

if (-not $NoTunnel) {
    # Check if cloudflared is available
    $cf = Get-Command cloudflared -ErrorAction SilentlyContinue
    if ($cf) {
        Write-Host ""
        Write-Host "Starting Cloudflare tunnel..." -ForegroundColor Yellow
        Write-Host "A public HTTPS URL will appear below. Share it with anyone to let them play!" -ForegroundColor Yellow
        Write-Host ""

        # Run cloudflared in foreground so user sees the URL
        # The "quick tunnel" requires no account or config
        cloudflared tunnel --url http://localhost:$Port
    } else {
        Write-Host ""
        Write-Host "cloudflared not found. Install it with: winget install Cloudflare.cloudflared" -ForegroundColor Red
        Write-Host "For now, only LAN access is available." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Press Ctrl+C to stop the server." -ForegroundColor Gray
        Wait-Job $webJob
    }
} else {
    Write-Host ""
    Write-Host "Tunnel disabled. LAN-only mode." -ForegroundColor Yellow
    Write-Host "Press Ctrl+C to stop the server." -ForegroundColor Gray
    Wait-Job $webJob
}

# Cleanup
Stop-Job $webJob -ErrorAction SilentlyContinue
Remove-Job $webJob -ErrorAction SilentlyContinue
