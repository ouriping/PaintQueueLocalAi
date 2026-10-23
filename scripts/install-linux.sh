#!/usr/bin/env bash
# Engineering / spare PC setup. The plant target is Windows; see install-windows.ps1.
# Nothing is downloaded unless you pass --download-model or --install-ollama.
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

if command -v ollama >/dev/null 2>&1; then
  echo "Ollama is installed. Keep it on loopback only:"
  echo "  export OLLAMA_HOST=127.0.0.1:11434"
  echo "Primary model, when this machine is allowed to reach the network:"
  echo "  ollama pull qwen2.5:1.5b"
  echo "Or create a local model from a GGUF after download:"
  echo "  ollama create paint-queue-advisor -f $ROOT/scripts/ollama/Modelfile"
else
  echo "Ollama is not installed. Re-run with --install-ollama, or copy a GGUF and use the LLamaSharp path."
fi

if [[ "$DOWNLOAD" -eq 1 ]]; then
  "$ROOT/scripts/download-model.sh" "$VARIANT"
else
  echo "Skip weight download. Place the primary file at:"
  echo "  $ROOT/models/qwen2.5-1.5b-instruct-q4_k_m.gguf"
  echo "Optional: Qwen2.5-3B Q4_K_M or Phi-3-mini-4k-instruct Q4. Do not commit them."
fi

echo
echo "CPU only. Do not assume CUDA. The plant iGPU is Intel UHD 770."
echo "Check without a model:"
echo "  dotnet run --project src/PaintQueueLocalAi.Demo -- --self-check"
echo "Ollama:"
echo "  dotnet run --project src/PaintQueueLocalAi.Demo -- --provider ollama --model qwen2.5:1.5b --timeout-ms 5000"
echo "GGUF:"
echo "  dotnet run --project src/PaintQueueLocalAi.Demo -- --provider llamasharp --model-path models/qwen2.5-1.5b-instruct-q4_k_m.gguf --timeout-ms 5000"
