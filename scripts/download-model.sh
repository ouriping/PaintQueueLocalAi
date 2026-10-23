#!/usr/bin/env bash
# Intentionally download a GGUF. The dispatch path never calls this.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VARIANT="${1:-1.5b}"

case "$VARIANT" in
  1.5b)
    FILE="qwen2.5-1.5b-instruct-q4_k_m.gguf"
    URL="https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/${FILE}"
    ;;
  3b)
    FILE="qwen2.5-3b-instruct-q4_k_m.gguf"
    URL="https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/${FILE}"
    ;;
  phi3)
    FILE="Phi-3-mini-4k-instruct-q4.gguf"
    URL="https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-gguf/resolve/main/${FILE}"
    ;;
  *)
    echo "Unknown variant: $VARIANT (use 1.5b, 3b, or phi3)" >&2
    exit 2
    ;;
esac

DEST="${2:-$ROOT/models/$FILE}"
mkdir -p "$(dirname "$DEST")"
echo "Downloading $VARIANT to $DEST"
echo "This file is about 1 GB or larger and is not committed to git."
curl -fL --retry 3 -C - -o "$DEST" "$URL"
echo "Saved $(wc -c < "$DEST") bytes to $DEST"
echo "Read the model card license before copying it onto a plant PC."
