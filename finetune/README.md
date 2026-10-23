# 涂装队列顾问微调

默认训练集让小模型学会三件事：只输出一行 JSON、工单号必须来自候选、顺序与规则分一致，并给出不超过 40 字的中文说明。联锁样本不会进入训练集。现场若要在分数接近时优先保色块，把 `data/plant_overrides.sample.jsonl` 拼进训练集再训。

产线工控机（i5-14500 / 16 GB / 无独显）只加载导出的 GGUF 或 Ollama 模型，不在节拍里训练。

## 数据

主文件 `data/paint_queue_advisor.jsonl` 由规则引擎生成，种子 `20261023`，48 条。重新导出：

```bash
dotnet run --project src/PaintQueueLocalAi.Demo -- --export-dataset finetune/data/paint_queue_advisor.jsonl
dotnet run --project src/PaintQueueLocalAi.Demo -- --export-overrides finetune/data/plant_overrides.sample.jsonl
```

每行是一段对话：

```json
{"messages":[
  {"role":"system","content":"你是涂装队列顾问。…"},
  {"role":"user","content":"工位:B1 …\n候选(只能重排这些工单号):\n- S00J0 …"},
  {"role":"assistant","content":"{\"order\":[\"S00J0\",\"S00J1\"],\"explanation\":\"同色紧急件优先\"}"}
]}
```

约束：

- `order` 只能重排该条 user 里出现的工单号，不能新增。
- `explanation` 1 到 40 个字。
- 主训练集的 `order` 就是规则分从高到低。约三分之一的 user 把候选打乱，避免模型只会照抄列表顺序。
- `plant_overrides.sample.jsonl` 有两条手写策略：一条把同色工单放到略高分的异色紧急件前面，一条在分差很大时保持原顺序。默认训练不要合并它，除非现场确认这条策略。

校验（不下载模型、不需要 GPU）：

```bash
python3 finetune/train_lora.py --dry-run --data finetune/data/paint_queue_advisor.jsonl
python3 finetune/train_lora.py --dry-run --data finetune/data/plant_overrides.sample.jsonl
```

## 训练

在可以联网的训练机上准备环境，不要装到产线节拍进程里：

```bash
python3 -m venv .venv
. .venv/bin/activate
pip install -r finetune/requirements.txt
```

CPU LoRA（能跑，1.5B 也会很慢，只适合试通几步）：

```bash
python3 finetune/train_lora.py \
  --device cpu \
  --max-steps 20 \
  --batch-size 1 \
  --grad-accum 8 \
  --data finetune/data/paint_queue_advisor.jsonl \
  --output finetune/output/adapter
```

有一块 modest GPU 时用 QLoRA（4bit NF4，需要 CUDA 和 `bitsandbytes`）：

```bash
pip install bitsandbytes
python3 finetune/train_lora.py \
  --qlora \
  --max-steps 200 \
  --data finetune/data/paint_queue_advisor.jsonl \
  --output finetune/output/adapter
```

脚本对 Qwen2.5 的 `q_proj`、`k_proj`、`v_proj`、`o_proj` 加 LoRA（r=8，alpha=16）。损失只算 assistant 段，system 和 user 被掩掉。基座默认 `Qwen/Qwen2.5-1.5B-Instruct`，可用 `--model` 换成 3B 或 Phi-3-mini 的 Hugging Face 目录。3B 在 16 GB 核显机器上不适合边产边训；训练请放到另一台机器。

若采用现场覆盖样本：

```bash
cat finetune/data/paint_queue_advisor.jsonl finetune/data/plant_overrides.sample.jsonl > finetune/output/train.jsonl
python3 finetune/train_lora.py --qlora --data finetune/output/train.jsonl --output finetune/output/adapter
```

## 导出 GGUF 并接到顾问

```bash
python3 finetune/merge_lora.py \
  --adapter finetune/output/adapter \
  --base Qwen/Qwen2.5-1.5B-Instruct \
  --out finetune/output/merged

export LLAMA_CPP_DIR=/path/to/llama.cpp
cmake -S "$LLAMA_CPP_DIR" -B "$LLAMA_CPP_DIR/build" -DGGML_CUDA=OFF
cmake --build "$LLAMA_CPP_DIR/build" --target llama-quantize -j
bash finetune/export_gguf.sh finetune/output/merged finetune/output
```

得到 `finetune/output/paint-advisor-q4_k_m.gguf`。把该文件拷到工控机，不要提交进 git。

LLamaSharp（CPU，`GpuLayerCount` 固定为 0）：

```bash
dotnet run --project src/PaintQueueLocalAi.Demo -- \
  --provider llamasharp \
  --model-path models/paint-advisor-q4_k_m.gguf \
  --timeout-ms 5000
```

Ollama：

```bash
# finetune/Modelfile 的 FROM 指向同目录下的 paint-advisor-q4_k_m.gguf
ollama create paint-queue-advisor -f finetune/Modelfile
dotnet run --project src/PaintQueueLocalAi.Demo -- \
  --provider ollama \
  --model paint-queue-advisor \
  --base-url http://127.0.0.1:11434 \
  --timeout-ms 5000
```

节拍内发布配置仍建议 `--timeout-ms 200`。1.5B Q4 在这台 CPU 上经常来不及生成，超时会保持规则顺序，这是预期。需要看模型说明时再把超时放到数秒，并先预热。

## English summary

The JSONL is rule-teacher chat data for a paint-queue advisor: reorder only the given job ids and add a short Chinese explanation. `--dry-run` validates it with the standard library. Train LoRA on CPU or QLoRA on a modest NVIDIA GPU, merge the adapter, quantize to Q4_K_M with llama.cpp built without CUDA, then point Demo at the GGUF (`llamasharp`) or an Ollama model (`paint-queue-advisor`). The line PC does not train and does not download weights during dispatch.
