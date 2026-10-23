# Plant PC setup (Windows). ASCII only so Windows PowerShell 5.1 can parse it.
# Nothing is downloaded unless -DownloadModel or -InstallOllama is passed.
param(
    [ValidateSet("Check", "Ollama", "LlamaSharp")]
    [string]$Provider = "Check",
    [switch]$DownloadModel,
    [switch]$InstallOllama,
    [ValidateSet("1.5b", "3b", "phi3")]
    [string]$Variant = "1.5b",
    [string]$ModelDirectory = ""
)

$ErrorActionPreference = "Stop"
if ($env:OS -ne "Windows_NT") {
    throw "This script targets the plant PC (Windows). On Linux use scripts/install-linux.sh."
}

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $ModelDirectory) {
    $ModelDirectory = Join-Path $Root "models"
}
New-Item -ItemType Directory -Force -Path $ModelDirectory | Out-Null

Write-Host "Repo: $Root"
Write-Host "Model directory (do not commit): $ModelDirectory"
Write-Host "Target: Intel i5-14500, 16 GB RAM, Intel UHD 770, CPU llama.cpp, no CUDA."

$Dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $Dotnet) {
    Write-Warning "dotnet was not found. Install the .NET 8 SDK or the .NET 8 Desktop/ASP.NET runtime if you only run a published build."
    Write-Host "  winget install Microsoft.DotNet.SDK.8"
} else {
    & dotnet --version
}

if ($InstallOllama) {
    $Ollama = Get-Command ollama -ErrorAction SilentlyContinue
    if (-not $Ollama) {
        $Winget = Get-Command winget -ErrorAction SilentlyContinue
        if (-not $Winget) {
            throw "winget was not found. Install Ollama from https://ollama.com/download and re-run."
        }
        & winget install --id Ollama.Ollama -e --accept-package-agreements --accept-source-agreements
    }
    [Environment]::SetEnvironmentVariable("OLLAMA_HOST", "127.0.0.1:11434", "User")
    $env:OLLAMA_HOST = "127.0.0.1:11434"
    Write-Host "Set OLLAMA_HOST=127.0.0.1:11434 for the current user."
}

if ($InstallOllama -or $Provider -eq "Ollama") {
    Write-Host "Keep Ollama on loopback: OLLAMA_HOST=127.0.0.1:11434"
    Write-Host "After Ollama is on PATH, load the base model only if this PC may use the network:"
    Write-Host "  ollama pull qwen2.5:1.5b"
    Write-Host "Offline GGUF model:"
    Write-Host "  ollama create paint-queue-advisor -f scripts\ollama\Modelfile"
}

if ($DownloadModel) {
    $FileName = "qwen2.5-1.5b-instruct-q4_k_m.gguf"
    if ($Variant -eq "3b") { $FileName = "qwen2.5-3b-instruct-q4_k_m.gguf" }
    if ($Variant -eq "phi3") { $FileName = "Phi-3-mini-4k-instruct-q4.gguf" }
    & (Join-Path $PSScriptRoot "download-model.ps1") -Variant $Variant -Destination (Join-Path $ModelDirectory $FileName)
} else {
    Write-Host "Skip weight download. Copy this file from a machine that is allowed online:"
    Write-Host "  $ModelDirectory\qwen2.5-1.5b-instruct-q4_k_m.gguf"
    Write-Host "Optional heavier models: Qwen2.5-3B Q4_K_M, Phi-3-mini-4k-instruct Q4."
}

Write-Host ""
Write-Host "Self-check without a model:"
Write-Host "  dotnet run --project src\PaintQueueLocalAi.Demo -- --self-check"
Write-Host "Ollama (longer timeout for a manual explanation; the line default stays 200 ms):"
Write-Host "  dotnet run --project src\PaintQueueLocalAi.Demo -- --provider ollama --model qwen2.5:1.5b --timeout-ms 5000"
Write-Host "GGUF via LLamaSharp, CPU only:"
Write-Host "  dotnet run --project src\PaintQueueLocalAi.Demo -- --provider llamasharp --model-path models\qwen2.5-1.5b-instruct-q4_k_m.gguf --timeout-ms 5000"
Write-Host "Publish for this PC from a connected build machine:"
Write-Host "  dotnet publish src\PaintQueueLocalAi.Demo\PaintQueueLocalAi.Demo.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64"
