#!/usr/bin/env python3
"""LoRA SFT for the paint-buffer advisor. QLoRA is optional (--qlora, CUDA only).

The dataset is chat JSONL produced by PaintQueueLocalAi:
  {"messages":[{"role":"system","content":"..."},{"role":"user","content":"..."},{"role":"assistant","content":"{...json...}"}]}

Dry-run (no torch, safe on the plant PC):

  python3 finetune/train_lora.py --dry-run --data finetune/data/paint_queue_advisor.jsonl

Primary path, LoRA (no --qlora):

  python3 finetune/train_lora.py --device cpu --max-steps 20 --batch-size 1

Optional QLoRA on a CUDA GPU:

  python3 finetune/train_lora.py --qlora --max-steps 200 --output finetune/output/adapter

After training, merge, export GGUF, then `ollama create`. Do not train on the line PC,
and do not require LLamaSharp to consume the adapter.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def load_records(path: Path) -> list[dict]:
    if not path.is_file():
        raise SystemExit(f"dataset not found: {path}")
    records: list[dict] = []
    for line_no, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not raw.strip():
            continue
        try:
            row = json.loads(raw)
        except json.JSONDecodeError as exc:
            raise SystemExit(f"{path}:{line_no}: {exc}") from exc
        messages = row.get("messages")
        if not isinstance(messages, list) or len(messages) != 3:
            raise SystemExit(f"{path}:{line_no}: expected 3 messages")
        roles = [item.get("role") for item in messages]
        if roles != ["system", "user", "assistant"]:
            raise SystemExit(f"{path}:{line_no}: roles must be system, user, assistant")
        user = messages[1].get("content")
        assistant = messages[2].get("content")
        if not isinstance(user, str) or not isinstance(assistant, str):
            raise SystemExit(f"{path}:{line_no}: content must be text")
        try:
            payload = json.loads(assistant)
        except json.JSONDecodeError as exc:
            raise SystemExit(f"{path}:{line_no}: assistant is not JSON: {exc}") from exc
        order = payload.get("order")
        explanation = payload.get("explanation")
        if not isinstance(order, list) or not order or not all(isinstance(item, str) and item for item in order):
            raise SystemExit(f"{path}:{line_no}: order must be a non-empty string list")
        if not isinstance(explanation, str) or not explanation or len(explanation) > 40:
            raise SystemExit(f"{path}:{line_no}: explanation must be 1..40 characters")
        for job_id in order:
            if f"- {job_id} " not in user:
                raise SystemExit(f"{path}:{line_no}: {job_id} is not a candidate in the user prompt")
        records.append(row)
    if not records:
        raise SystemExit(f"{path}: empty dataset")
    return records


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Fine-tune the paint-buffer advisor. LoRA is the default; --qlora is optional.")
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--model", default="Qwen/Qwen2.5-1.5B-Instruct")
    parser.add_argument("--output", type=Path, default=Path("finetune/output/adapter"))
    parser.add_argument("--device", choices=("auto", "cpu", "cuda"), default="auto")
    parser.add_argument("--qlora", action="store_true", help="4-bit QLoRA. Requires a CUDA GPU and bitsandbytes.")
    parser.add_argument("--max-steps", type=int, default=200)
    parser.add_argument("--batch-size", type=int, default=1)
    parser.add_argument("--grad-accum", type=int, default=8)
    parser.add_argument("--learning-rate", type=float, default=2e-4)
    parser.add_argument("--lora-r", type=int, default=8)
    parser.add_argument("--lora-alpha", type=int, default=16)
    parser.add_argument("--max-length", type=int, default=1024)
    parser.add_argument("--seed", type=int, default=20261023)
    return parser.parse_args()


def train(args: argparse.Namespace, records: list[dict]) -> None:
    import torch
    from peft import LoraConfig, get_peft_model
    from transformers import AutoModelForCausalLM, AutoTokenizer, Trainer, TrainingArguments

    if args.qlora and not torch.cuda.is_available():
        raise SystemExit("QLoRA needs a CUDA GPU. Re-run without --qlora for CPU LoRA, or use a training PC.")

    device = args.device
    if device == "auto":
        device = "cuda" if torch.cuda.is_available() else "cpu"
    if device == "cpu" and args.qlora:
        raise SystemExit("QLoRA cannot run on CPU.")

    tokenizer = AutoTokenizer.from_pretrained(args.model, use_fast=True)
    if tokenizer.pad_token is None:
        tokenizer.pad_token = tokenizer.eos_token

    model_kwargs: dict = {}
    if args.qlora:
        from transformers import BitsAndBytesConfig

        model_kwargs["quantization_config"] = BitsAndBytesConfig(
            load_in_4bit=True,
            bnb_4bit_quant_type="nf4",
            bnb_4bit_use_double_quant=True,
            bnb_4bit_compute_dtype=torch.bfloat16,
        )
        model_kwargs["device_map"] = "auto"
    elif device == "cuda":
        model_kwargs["torch_dtype"] = torch.bfloat16
        model_kwargs["device_map"] = "auto"
    else:
        model_kwargs["torch_dtype"] = torch.float32

    model = AutoModelForCausalLM.from_pretrained(args.model, **model_kwargs)
    model = get_peft_model(
        model,
        LoraConfig(
            r=args.lora_r,
            lora_alpha=args.lora_alpha,
            lora_dropout=0.05,
            bias="none",
            task_type="CAUSAL_LM",
            target_modules=["q_proj", "k_proj", "v_proj", "o_proj"],
        ),
    )

    features = [encode(row, tokenizer, args.max_length) for row in records]

    class JsonlDataset(torch.utils.data.Dataset):
        def __len__(self) -> int:
            return len(features)

        def __getitem__(self, index: int) -> dict:
            return features[index]

    use_bf16 = bool(args.qlora or (device == "cuda" and torch.cuda.is_bf16_supported()))
    trainer = Trainer(
        model=model,
        args=TrainingArguments(
            output_dir=str(args.output),
            per_device_train_batch_size=args.batch_size,
            gradient_accumulation_steps=args.grad_accum,
            max_steps=args.max_steps,
            learning_rate=args.learning_rate,
            logging_steps=5,
            save_steps=max(args.max_steps, 1),
            bf16=use_bf16,
            fp16=False,
            report_to=[],
            remove_unused_columns=False,
            seed=args.seed,
            dataloader_pin_memory=device == "cuda",
        ),
        train_dataset=JsonlDataset(),
        data_collator=collate,
    )
    trainer.train()
    args.output.mkdir(parents=True, exist_ok=True)
    trainer.model.save_pretrained(args.output)
    tokenizer.save_pretrained(args.output)
    print(f"adapter written to {args.output}")


def encode(row: dict, tokenizer, max_length: int) -> dict:
    messages = row["messages"]
    prompt_ids = tokenizer.apply_chat_template(
        messages[:-1], tokenize=True, add_generation_prompt=True
    )
    full_ids = tokenizer.apply_chat_template(
        messages, tokenize=True, add_generation_prompt=False
    )
    if len(full_ids) > max_length:
        full_ids = full_ids[:max_length]
    labels = list(full_ids)
    prompt_len = min(len(prompt_ids), len(labels))
    for index in range(prompt_len):
        labels[index] = -100
    return {
        "input_ids": full_ids,
        "attention_mask": [1] * len(full_ids),
        "labels": labels,
    }


def collate(features: list[dict]) -> dict:
    import torch

    width = max(len(item["input_ids"]) for item in features)
    pad_id = 0
    batch = {"input_ids": [], "attention_mask": [], "labels": []}
    for item in features:
        pad = width - len(item["input_ids"])
        batch["input_ids"].append(item["input_ids"] + [pad_id] * pad)
        batch["attention_mask"].append(item["attention_mask"] + [0] * pad)
        batch["labels"].append(item["labels"] + [-100] * pad)
    return {key: torch.tensor(value, dtype=torch.long) for key, value in batch.items()}


def main() -> None:
    args = parse_args()
    records = load_records(args.data)
    if args.dry_run:
        print(f"ok {len(records)} records in {args.data}")
        sample = json.loads(records[0]["messages"][2]["content"])
        print("sample order:", ",".join(sample["order"]))
        print("sample explanation:", sample["explanation"])
        return
    train(args, records)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(130)
