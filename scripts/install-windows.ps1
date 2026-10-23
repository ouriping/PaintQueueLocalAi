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
Write-Host "Target: Intel i5-14500, 16 GB RAM, Intel UHD 770, CPU only, no CUDA."
Write-Host "Already have Ollama and Qwen? Skip -DownloadModel and -InstallOllama."
Write-Host "Use the name from 'ollama list'. The demo default is qwen2.5:1.5b."

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

if ($Provider -eq "LlamaSharp") {
    Write-Host "LLamaSharp is secondary. The demo does not load GGUF."
    Write-Host "Register the file with Ollama instead:"
    Write-Host "  ollama create paint-queue-advisor -f scripts\ollama\Modelfile"
}

if ($InstallOllama -or $Provider -eq "Ollama" -or $Provider -eq "Check") {
    Write-Host "Keep Ollama on loopback: OLLAMA_HOST=127.0.0.1:11434"
    Write-Host "If Qwen is already listed, do not pull or download weights:"
    Write-Host "  ollama list"
    Write-Host "Pull only when this PC may use the network and the tag is missing:"
    Write-Host "  ollama pull qwen2.5:1.5b"
    Write-Host "After LoRA export, register the GGUF with Ollama. LLamaSharp is not required:"
    Write-Host "  ollama create paint-queue-advisor -f scripts\ollama\Modelfile"
}

if ($DownloadModel) {
    $FileName = "qwen2.5-1.5b-instruct-q4_k_m.gguf"
    if ($Variant -eq "3b") { $FileName = "qwen2.5-3b-instruct-q4_k_m.gguf" }
    if ($Variant -eq "phi3") { $FileName = "Phi-3-mini-4k-instruct-q4.gguf" }
    & (Join-Path $PSScriptRoot "download-model.ps1") -Variant $Variant -Destination (Join-Path $ModelDirectory $FileName)
} else {
    Write-Host "Skip weight download. An existing 'ollama list' entry is enough."
    Write-Host "Copy a GGUF only when creating an Ollama model offline:"
    Write-Host "  $ModelDirectory\qwen2.5-1.5b-instruct-q4_k_m.gguf"
}

Write-Host ""
Write-Host "Rules only:"
Write-Host "  dotnet run --project src\PaintQueueLocalAi.Demo -- --provider null"
Write-Host "Ollama + Qwen (falls back to the rules if Ollama is down; release timeout stays 200 ms):"
Write-Host "  dotnet run --project src\PaintQueueLocalAi.Demo -- --provider ollama --model qwen2.5:1.5b"
Write-Host "Publish for this PC from a connected build machine:"
Write-Host "  dotnet publish src\PaintQueueLocalAi.Demo\PaintQueueLocalAi.Demo.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64"
