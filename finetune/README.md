# 涂装缓存放行顾问微调

主路径是 **LoRA**。QLoRA 只在有 NVIDIA GPU 时可选。训完后合并适配器、导出 GGUF，再用 `ollama create` 接到产线。产线工控机（i5-14500 / 16 GB / 无独显）不训练，也不需要安装 LLamaSharp。

## 已经装好 Ollama 和 Qwen

跳过权重下载。`--model` 必须和 `ollama list` 里的名字一致。演示默认是 `qwen2.5:0.5b`；已经拉取 1.5b 时再传 `--model qwen2.5:1.5b`。

```bash
ollama list
dotnet run --project src/PaintQueueLocalAi.Demo -- --provider ollama
dotnet run --project src/PaintQueueLocalAi.Demo -- --provider ollama --model qwen2.5:1.5b
```

Ollama 没开时，四段规则仿真照常通过。下面的训练只在要改顾问口吻或现场策略时才做，并且放到另一台机器上。

## 数据

主文件 `data/paint_queue_advisor.jsonl` 由规则引擎生成，种子 `20261023`，48 条。重新导出：

```bash
dotnet run --project src/PaintQueueLocalAi.Demo -- --provider null \
  --export-dataset finetune/data/paint_queue_advisor.jsonl \
  --export-overrides finetune/data/plant_overrides.sample.jsonl \
  --export-modelfile finetune/Modelfile \
  --modelfile-from ./paint-advisor-q4_k_m.gguf
```

每行是一段对话。`order` 里是候选编号，形如 `buffer:BUF-02:B-0012` 或 `fast-lane-a:B-0020`，不是随意的工单号。

约束：

- `order` 只能重排该条 user 里出现的候选编号，不能新增车辆，也不能建议解除联锁。
- `explanation` 1 到 40 个字。
- 主训练集的 `order` 就是规则分从高到低。约三分之一的 user 把候选打乱，避免模型只会照抄列表顺序。
- `plant_overrides.sample.jsonl` 有两条现场样本：非紧急时把满库车道放到规则窗口最前；最小序号样本在分差已经明确时保持原顺序。默认训练不要合并它，除非现场确认这条策略。

校验（不下载模型、不需要 GPU）：

```bash
python3 finetune/train_lora.py --dry-run --data finetune/data/paint_queue_advisor.jsonl
python3 finetune/train_lora.py --dry-run --data finetune/data/plant_overrides.sample.jsonl
```

## 训练（LoRA）

在可以联网的训练机上准备环境，不要装到产线节拍进程里：

```bash
python3 -m venv .venv
. .venv/bin/activate
pip install -r finetune/requirements.txt
```

默认就是 LoRA，不要加 `--qlora`。CPU 能跑通，1.5B 会很慢，只适合试几步：

```bash
python3 finetune/train_lora.py \
  --device cpu \
  --max-steps 20 \
  --batch-size 1 \
  --grad-accum 8 \
  --data finetune/data/paint_queue_advisor.jsonl \
  --output finetune/output/adapter
```

有 GPU 但不用 4bit 时，同样不加 `--qlora`：

```bash
python3 finetune/train_lora.py \
  --device cuda \
  --max-steps 200 \
  --data finetune/data/paint_queue_advisor.jsonl \
  --output finetune/output/adapter
```

脚本对 Qwen2.5 的 `q_proj`、`k_proj`、`v_proj`、`o_proj` 加 LoRA（r=8，alpha=16）。损失只算 assistant 段。基座默认 `Qwen/Qwen2.5-1.5B-Instruct`，可用 `--model` 换成 3B 或 Phi-3-mini 的 Hugging Face 目录。3B 不要在 16 GB 的产线工控机上训练。

若采用现场覆盖样本：

```bash
cat finetune/data/paint_queue_advisor.jsonl finetune/data/plant_overrides.sample.jsonl > finetune/output/train.jsonl
python3 finetune/train_lora.py --data finetune/output/train.jsonl --output finetune/output/adapter
```

### 可选：QLoRA

只在有 CUDA 和 `bitsandbytes` 时使用。没有 CUDA 时脚本会直接退出，请去掉 `--qlora` 走上面的 LoRA。

```bash
pip install bitsandbytes
python3 finetune/train_lora.py \
  --qlora \
  --max-steps 200 \
  --data finetune/data/paint_queue_advisor.jsonl \
  --output finetune/output/adapter
```

## 导出并用 Ollama 消费

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

得到 `finetune/output/paint-advisor-q4_k_m.gguf`。把该文件拷到工控机，不要提交进 git。把 `finetune/Modelfile` 的 `FROM` 改成这台机器上的路径，然后：

```bash
ollama create paint-queue-advisor -f finetune/Modelfile
dotnet run --project src/PaintQueueLocalAi.Demo -- \
  --provider ollama \
  --model paint-queue-advisor
```

放行节拍里的 `AdvisorTimeout` 仍是 200 ms。演示程序另一次顾问调用默认 8 秒，只为了在这台 CPU 上看到回复，不要把 8 秒挡在 `CommitReleaseAsync` 前面。Ollama 不可用时保持规则顺序。

直接用 GGUF 和 LLamaSharp 是备选宿主，见 `src/PaintQueueLocalAi.LlamaSharp`。演示程序不引用它，默认工作流也不需要它。

## English summary

LoRA is the training path. QLoRA (`--qlora`) is optional and needs CUDA. Merge the adapter, quantize to Q4_K_M with llama.cpp built without CUDA, then `ollama create` and point Demo at that model name. The line PC does not train, does not download weights during dispatch, and does not need LLamaSharp. If Ollama and Qwen are already installed, skip the weight download and pass the name from `ollama list`.
