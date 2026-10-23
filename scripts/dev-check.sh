#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
dotnet test PaintQueueLocalAi.sln -c Release --nologo
python3 finetune/train_lora.py --dry-run --data finetune/data/paint_queue_advisor.jsonl
python3 finetune/train_lora.py --dry-run --data finetune/data/plant_overrides.sample.jsonl
