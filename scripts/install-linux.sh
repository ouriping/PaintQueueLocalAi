#!/usr/bin/env bash
# 工程机或备用机的安装。现场目标机是 Windows，见 install-windows.ps1。
# 除非传入 --download-model 或 --install-ollama，否则不会下载任何内容。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOWNLOAD=0
INSTALL_OLLAMA=0
VARIANT="1.5b"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --download-model) DOWNLOAD=1; shift ;;
    --install-ollama) INSTALL_OLLAMA=1; shift ;;
    --variant) VARIANT="$2"; shift 2 ;;
    -h|--help)
      echo "Usage: scripts/install-linux.sh [--download-model] [--install-ollama] [--variant 1.5b|3b|phi3]"
      exit 0
      ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

mkdir -p "$ROOT/models"
echo "Repo: $ROOT"
echo "Model directory (gitignored): $ROOT/models"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK 8 was not found. Install it before building:"
  echo "  https://dotnet.microsoft.com/download/dotnet/8.0"
else
  dotnet --version
fi

if [[ "$INSTALL_OLLAMA" -eq 1 ]]; then
  echo "Installing Ollama with the upstream script."
  curl -fsSL https://ollama.com/install.sh | sh
fi

echo "Already have Ollama and a Qwen model? Skip --download-model and --install-ollama."
echo "Pass whatever name 'ollama list' shows. The demo default is qwen2.5:0.5b."
echo "0.5b is smaller and easier on the CPU timeout. If this machine has qwen2.5:1.5b, pass --model qwen2.5:1.5b."

if command -v ollama >/dev/null 2>&1; then
  echo "Ollama is installed. Keep it on loopback only:"
  echo "  export OLLAMA_HOST=127.0.0.1:11434"
  echo "  ollama list"
  echo "Pull only when this machine may use the network and the tag is missing:"
  echo "  ollama pull qwen2.5:0.5b"
  echo "  ollama pull qwen2.5:1.5b   # recommended plant size when that tag is wanted"
  echo "After a LoRA export, register the GGUF with Ollama (LLamaSharp is not required):"
  echo "  ollama create paint-queue-advisor -f $ROOT/scripts/ollama/Modelfile"
else
  echo "Ollama is not installed. Re-run with --install-ollama if this machine may use the network."
fi

if [[ "$DOWNLOAD" -eq 1 ]]; then
  "$ROOT/scripts/download-model.sh" "$VARIANT"
else
  echo "Skip weight download. An existing 'ollama list' entry is enough."
  echo "Download a GGUF only when you need to create an Ollama model offline:"
  echo "  $ROOT/models/qwen2.5-1.5b-instruct-q4_k_m.gguf"
fi

echo
echo "CPU only. Do not assume CUDA. The plant iGPU is Intel UHD 770."
echo "Rules only:"
echo "  dotnet run --project src/PaintQueueLocalAi.Demo -- --provider null"
echo "Ollama + Qwen (falls back to the rules if Ollama is down):"
echo "  dotnet run --project src/PaintQueueLocalAi.Demo -- --provider ollama --model qwen2.5:0.5b"
echo "  dotnet run --project src/PaintQueueLocalAi.Demo -- --provider ollama --model qwen2.5:1.5b"
