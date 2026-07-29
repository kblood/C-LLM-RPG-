<#
.SYNOPSIS
    Launches llama-server using the model configured in llm-settings.json.

.DESCRIPTION
    Reads llm-settings.json (from the build output or the script directory),
    resolves the Ollama model blob path, and starts llama-server with the
    correct context size.  If llama-server is already listening on the
    configured port it won't start a second instance.

.PARAMETER Model
    Override the model tag from settings (e.g. "granite4:3b").

.PARAMETER CtxSize
    Override the context-window size (default: value from settings or 8192).

.PARAMETER Port
    Override the port (default: extracted from LlamaCppUrl in settings or 8080).

.PARAMETER SettingsPath
    Explicit path to llm-settings.json.

.EXAMPLE
    .\launch-llama-server.ps1
    .\launch-llama-server.ps1 -Model mistral:latest -CtxSize 16384
#>
param(
    [string] $Model      = "",
    [int]    $CtxSize    = 0,
    [int]    $Port       = 0,
    [string] $SettingsPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Locate llm-settings.json ─────────────────────────────────────────────────
function Find-Settings {
    param([string] $ExplicitPath)

    if ($ExplicitPath -and (Test-Path $ExplicitPath)) { return $ExplicitPath }

    $candidates = @(
        (Join-Path $PSScriptRoot "llm-settings.json"),
        (Join-Path $PSScriptRoot "bin\Debug\net8.0\llm-settings.json"),
        (Join-Path $PSScriptRoot "bin\Release\net8.0\llm-settings.json")
    )

    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    return $null
}

$settingsFile = Find-Settings -ExplicitPath $SettingsPath

$cfg = @{
    backend     = "ollama"
    model       = "granite4:3b"
    llamaCppUrl = "http://localhost:8080"
    contextSize = 8192
}

if ($settingsFile) {
    Write-Host "  Reading settings from: $settingsFile"
    $json = Get-Content $settingsFile -Raw | ConvertFrom-Json
    if ($json.PSObject.Properties["model"])       { $cfg.model       = $json.model       }
    if ($json.PSObject.Properties["llamaCppUrl"]) { $cfg.llamaCppUrl = $json.llamaCppUrl }
    if ($json.PSObject.Properties["contextSize"]) { $cfg.contextSize = [int]$json.contextSize }
    if ($json.PSObject.Properties["backend"])     { $cfg.backend     = $json.backend     }
} else {
    Write-Warning "llm-settings.json not found – using defaults. Run 'dotnet build' first or specify -SettingsPath."
}

# ── Apply overrides from parameters ──────────────────────────────────────────
if ($Model)   { $cfg.model       = $Model   }
if ($CtxSize) { $cfg.contextSize = $CtxSize }

# Extract port from URL (e.g. "http://localhost:8080" → 8080)
if ($Port -eq 0) {
    if ($cfg.llamaCppUrl -match ":(\d+)") { $Port = [int]$Matches[1] }
    else                                   { $Port = 8080 }
}

Write-Host ""
Write-Host "  Model      : $($cfg.model)"
Write-Host "  Context    : $($cfg.contextSize) tokens"
Write-Host "  Port       : $Port"
Write-Host ""

# ── Check if llama-server is already running ──────────────────────────────────
function Test-PortListening {
    param([int] $TestPort)
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.Connect("127.0.0.1", $TestPort)
        $tcp.Close()
        return $true
    } catch { return $false }
}

if (Test-PortListening -TestPort $Port) {
    Write-Host "  llama-server is already running on port $Port." -ForegroundColor Green
    Write-Host "  Nothing to do."
    exit 0
}

# ── Resolve the GGUF blob path from Ollama's model store ─────────────────────
function Get-OllamaModelsRoot {
    # Windows: %LOCALAPPDATA%\Ollama\models
    $winPath = Join-Path $env:LOCALAPPDATA "Ollama\models"
    if (Test-Path $winPath) { return $winPath }

    # Unix-style fallback (WSL / cross-platform)
    $unixPath = Join-Path $HOME ".ollama\models"
    if (Test-Path $unixPath) { return $unixPath }

    return $null
}

function Get-OllamaModelBlob {
    param([string] $Tag)

    $root = Get-OllamaModelsRoot
    if (-not $root) { return $null }

    $colonIdx = $Tag.IndexOf(':')
    if ($colonIdx -ge 0) {
        $name = $Tag.Substring(0, $colonIdx)
        $tag  = $Tag.Substring($colonIdx + 1)
    } else {
        $name = $Tag
        $tag  = "latest"
    }

    $manifestPath = Join-Path $root "manifests\registry.ollama.ai\library\$name\$tag"
    if (-not (Test-Path $manifestPath)) {
        # Try without library/
        $manifestPath = Join-Path $root "manifests\$name\$tag"
        if (-not (Test-Path $manifestPath)) { return $null }
    }

    try {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        foreach ($layer in $manifest.layers) {
            if ($layer.mediaType -notmatch "model") { continue }
            $digest      = $layer.digest -replace ':', '-'
            $blobPath    = Join-Path $root "blobs\$digest"
            if (Test-Path $blobPath) { return $blobPath }
        }
    } catch {
        Write-Warning "Could not parse manifest at $manifestPath : $_"
    }
    return $null
}

$blobPath = Get-OllamaModelBlob -Tag $cfg.model
if (-not $blobPath) {
    Write-Host "  ERROR: Could not find model blob for '$($cfg.model)' in Ollama store." -ForegroundColor Red
    Write-Host "  Run:  ollama pull $($cfg.model)"
    exit 1
}

Write-Host "  Model blob : $blobPath" -ForegroundColor Cyan

# ── Find llama-server executable ─────────────────────────────────────────────
function Find-LlamaServer {
    # 1. Check PATH first
    $inPath = Get-Command "llama-server" -ErrorAction SilentlyContinue
    if ($inPath) { return $inPath.Source }

    # 2. winget install location: %LOCALAPPDATA%\Microsoft\WinGet\Packages\ggml.llamacpp_*\
    $wingetBase = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages"
    if (Test-Path $wingetBase) {
        $found = Get-ChildItem $wingetBase -Filter "ggml.llamacpp_*" -Directory -ErrorAction SilentlyContinue |
                 ForEach-Object { Join-Path $_.FullName "llama-server.exe" } |
                 Where-Object { Test-Path $_ } |
                 Select-Object -First 1
        if ($found) { return $found }
    }

    # 3. Common manual install locations
    $searchRoots = @(
        (Join-Path $env:LOCALAPPDATA "Programs\llama.cpp"),
        (Join-Path $env:LOCALAPPDATA "llama.cpp"),
        "C:\llama.cpp",
        "C:\llama-server",
        (Join-Path $HOME "llama.cpp")
    )
    foreach ($root in $searchRoots) {
        foreach ($sub in @("", "build\bin", "bin")) {
            $candidate = Join-Path $root $sub "llama-server.exe"
            if (Test-Path $candidate) { return $candidate }
        }
    }

    return $null
}

$llamaExe = Find-LlamaServer
if (-not $llamaExe) {
    Write-Host ""
    Write-Host "  ERROR: llama-server not found in PATH or common locations." -ForegroundColor Red
    Write-Host ""
    Write-Host "  Options:"
    Write-Host "    A) Install via winget (recommended):"
    Write-Host "       winget install ggml.llamacpp"    Write-Host "       Then re-run this script."
    Write-Host ""
    Write-Host "    B) Download a pre-built release manually:"
    Write-Host "       https://github.com/ggerganov/llama.cpp/releases"
    Write-Host "       Extract llama-server.exe and add its folder to your PATH."
    Write-Host ""
    Write-Host "    C) Or run directly if you know the path:"
    Write-Host "       llama-server --model `"$blobPath`" --port $Port --ctx-size $($cfg.contextSize)"
    exit 1
}

Write-Host "  llama-server: $llamaExe" -ForegroundColor Cyan

# ── Launch llama-server ───────────────────────────────────────────────────────
$args = @(
    "--model", $blobPath,
    "--port",  $Port,
    "--ctx-size", $cfg.contextSize
)

Write-Host ""
Write-Host "  Launching llama-server..." -ForegroundColor Yellow
Write-Host "  $llamaExe $($args -join ' ')"
Write-Host ""
Write-Host "  Press Ctrl+C here to stop the server."
Write-Host ""

# Run in this terminal so the user sees the server output
& $llamaExe @args
