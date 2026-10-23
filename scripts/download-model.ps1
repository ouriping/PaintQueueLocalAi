# Intentionally download a GGUF. The dispatch path never calls this.
# Windows PowerShell 5.1 and PowerShell 7. ASCII only.
param(
    [ValidateSet("1.5b", "3b", "phi3")]
    [string]$Variant = "1.5b",
    [string]$Destination = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

switch ($Variant) {
    "1.5b" {
        $File = "qwen2.5-1.5b-instruct-q4_k_m.gguf"
        $Url = "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/$File"
    }
    "3b" {
        $File = "qwen2.5-3b-instruct-q4_k_m.gguf"
        $Url = "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/$File"
    }
    "phi3" {
        $File = "Phi-3-mini-4k-instruct-q4.gguf"
        $Url = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-gguf/resolve/main/$File"
    }
}

if (-not $Destination) {
    $Destination = Join-Path (Join-Path $Root "models") $File
}

$Directory = Split-Path -Parent $Destination
New-Item -ItemType Directory -Force -Path $Directory | Out-Null
Write-Host "Downloading $Variant to $Destination"
Write-Host "This file is about 1 GB or larger and is not committed to git."

$Curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if (-not $Curl) {
    throw "curl.exe was not found. Install it or download the GGUF from the model card by hand."
}

& curl.exe -fL --retry 3 -C - -o $Destination $Url
if ($LASTEXITCODE -ne 0) {
    throw "curl.exe failed with exit code $LASTEXITCODE"
}

$Length = (Get-Item $Destination).Length
Write-Host "Saved $Length bytes to $Destination"
Write-Host "Read the model card license before copying it onto a plant PC."
