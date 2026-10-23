#!/usr/bin/env bash
# Convert a merged Hugging Face model to a Q4_K_M GGUF the plant PC can load.
# Requires a llama.cpp checkout. This does not run inside the dispatch process.
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: finetune/export_gguf.sh <merged-hf-dir> [output-dir]" >&2
  exit 2
fi

MERGED="$(cd "$1" && pwd)"
OUT_DIR="${2:-$(cd "$(dirname "$0")" && pwd)/output}"
LLAMA_CPP="${LLAMA_CPP_DIR:-}"

if [[ -z "$LLAMA_CPP" ]]; then
  echo "Set LLAMA_CPP_DIR to a llama.cpp checkout." >&2
  echo "Build the quantize tool with CUDA off, for example:" >&2
  echo "  cmake -S llama.cpp -B llama.cpp/build -DGGML_CUDA=OFF && cmake --build llama.cpp/build --target llama-quantize -j" >&2
  exit 2
fi

mkdir -p "$OUT_DIR"
F16="$OUT_DIR/paint-advisor-f16.gguf"
Q4="$OUT_DIR/paint-advisor-q4_k_m.gguf"
CONVERT="$LLAMA_CPP/convert_hf_to_gguf.py"
if [[ ! -f "$CONVERT" ]]; then
  echo "Missing $CONVERT" >&2
  exit 2
fi

python3 "$CONVERT" "$MERGED" --outfile "$F16" --outtype f16

QUANT=""
for candidate in \
  "$LLAMA_CPP/build/bin/llama-quantize" \
  "$LLAMA_CPP/build/llama-quantize" \
  "$LLAMA_CPP/llama-quantize"
do
  if [[ -x "$candidate" ]]; then
    QUANT="$candidate"
    break
  fi
done

if [[ -z "$QUANT" ]]; then
  echo "llama-quantize was not found under $LLAMA_CPP/build." >&2
  exit 2
fi

"$QUANT" "$F16" "$Q4" Q4_K_M
echo "Wrote $Q4"
echo "Copy that file to the plant PC as models/paint-advisor-q4_k_m.gguf"
echo "Ollama: ollama create paint-queue-advisor -f finetune/Modelfile"
echo "Demo:  dotnet run --project src/PaintQueueLocalAi.Demo -- --provider llamasharp --model-path models/paint-advisor-q4_k_m.gguf --timeout-ms 5000"
