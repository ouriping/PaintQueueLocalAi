# PaintQueueLocalAi

涂装队列的本地调度。规则先给出候选和顺序；本机模型只允许重排这些候选，或补一句界面说明。没有模型、超时、输出不合法时，保持规则顺序。不调用云端，运行时也不下载权重。

目标机是 Intel i5-14500、16 GB 内存、只有 Intel UHD 770。推理走 CPU 上的 llama.cpp（Ollama 或 LLamaSharp），不假设 CUDA。

## 行为边界

- 急停、输送停止、喷房故障、烘房未就绪：本轮不调度，不询问模型。
- 质量保留、闪干未完成、涂料体系与喷房不一致、烘房温度低于工艺要求 5°C 以上：工单不进入候选，也不会出现在提示词里。
- 模型只能重排 `TopK` 候选（默认 5，最大 8），不能发明工单。未知工单号会被丢掉；没写全的顺序用规则顺序补齐。一个合法工单号都没有，则整段建议作废。
- 节拍内默认超时 200 ms。1.5B Q4 在这台 CPU 上通常生成不完，超时回退是正常现象。开班预热不占用这 200 ms。要在界面上看模型说明时，把超时调到约 5 秒。
- 核心库 `src/PaintQueueLocalAi` 不引用 LLamaSharp，也不发起 HTTP。Ollama 客户端在 `src/PaintQueueLocalAi.Local`，且只接受 `127.0.0.1`、`localhost`、`::1`，关闭代理和重定向。GGUF 加载在 `src/PaintQueueLocalAi.LlamaSharp`。

规则分（高者优先，平分再按交期、工单号）：紧急 +400，高优先 +200，逾期 +300 再加每分钟 2 分（最多算 120 分钟），30 分钟内交期 +180，两小时内 +80，与当前色相同 +220，与当前零件族相同 +40。这是当前喷房的即时排序，不是整班换色优化。

## 没有模型时

```bash
dotnet run --project src/PaintQueueLocalAi.Demo -- --self-check
dotnet run --project src/PaintQueueLocalAi.Demo -- --provider null --scenario sample
dotnet run --project src/PaintQueueLocalAi.Demo -- --provider null --scenario interlock
```

自检应选中 `W-URG`，急停场景不询问模型。

## 安装

权重不要提交进 git。放到仓库下的 `models/`（已忽略）或任意本机路径。

主模型：Qwen2.5-1.5B-Instruct，GGUF `Q4_K_M`，约 1.1 GB。

- 文件名：`qwen2.5-1.5b-instruct-q4_k_m.gguf`
- 模型卡：[Qwen/Qwen2.5-1.5B-Instruct-GGUF](https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF)

可选、更大、仍建议 Q4：Qwen2.5-3B-Instruct，或 Phi-3-mini-4k-instruct Q4。16 GB 内存装得下，但更难在短超时内返回。使用前阅读各自模型卡上的许可。

脚本默认什么都不下载。需要在可联网的机器上取文件时再加开关，然后用 U 盘拷到工控机。

### Windows（工控机）

```powershell
# 只检查环境并打印路径
powershell -ExecutionPolicy Bypass -File scripts\install-windows.ps1

# 安装 Ollama，并把 OLLAMA_HOST 设为 127.0.0.1:11434
powershell -ExecutionPolicy Bypass -File scripts\install-windows.ps1 -InstallOllama

# 有网时下载 1.5B（也可 -Variant 3b 或 phi3）
powershell -ExecutionPolicy Bypass -File scripts\install-windows.ps1 -DownloadModel -Variant 1.5b
```

Ollama 在线模型：

```powershell
$env:OLLAMA_HOST = "127.0.0.1:11434"
ollama pull qwen2.5:1.5b
```

离线 GGUF 注册成 Ollama 模型（`scripts/ollama/Modelfile` 指向 `models/qwen2.5-1.5b-instruct-q4_k_m.gguf`）：

```powershell
ollama create paint-queue-advisor -f scripts\ollama\Modelfile
```

确认监听地址是回环，而不是 `0.0.0.0`。

GGUF + LLamaSharp 不需要单独安装 llama.cpp。Demo 引用 CPU 后端，层数强制为 0。把文件放在：

```text
models\qwen2.5-1.5b-instruct-q4_k_m.gguf
```

在能联网的机器上发布到工控机（自包含，工控机可以不装 SDK）：

```powershell
dotnet publish src\PaintQueueLocalAi.Demo\PaintQueueLocalAi.Demo.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64
```

### Linux（编程机或备机）

```bash
bash scripts/install-linux.sh
bash scripts/install-linux.sh --download-model --variant 1.5b
bash scripts/install-linux.sh --install-ollama
```

同样设置 `OLLAMA_HOST=127.0.0.1:11434`。产线目标仍是 Windows。

## 运行顾问

Ollama，先预热（默认开启），人工说明可用更长超时：

```bash
dotnet run --project src/PaintQueueLocalAi.Demo -- \
  --provider ollama \
  --base-url http://127.0.0.1:11434 \
  --model qwen2.5:1.5b \
  --timeout-ms 5000
```

微调后的 Ollama 模型名换成 `paint-queue-advisor`。

GGUF：

```bash
dotnet run --project src/PaintQueueLocalAi.Demo -- \
  --provider llamasharp \
  --model-path models/qwen2.5-1.5b-instruct-q4_k_m.gguf \
  --threads 6 \
  --timeout-ms 5000
```

`--no-warmup` 时 GGUF 路径不会在节拍里读盘，直接按规则排。配置了局域网或云端地址时，程序拒绝启动，而不是静默回退。Ollama 进程没开、模型文件缺失、输出不是 JSON，都保持规则顺序。

`src/PaintQueueLocalAi.Demo/appsettings.json` 的默认 `provider` 是 `null`，超时 200 ms。命令行覆盖该文件。

## 微调

完整步骤在 [finetune/README.md](finetune/README.md)。摘要：

1. 数据集是规则引擎导出的对话 JSONL，标签只含候选重排和一句中文说明。
2. 训练用 LoRA（CPU 可跑通，很慢）或 QLoRA（一块 modest GPU）。不要在产线节拍上训练。
3. 合并适配器后，用关闭 CUDA 的 llama.cpp 量化成 `Q4_K_M` GGUF。
4. 把 GGUF 交给 `--provider llamasharp --model-path ...`，或用 `finetune/Modelfile` 做成 Ollama 模型 `paint-queue-advisor`。

```bash
dotnet run --project src/PaintQueueLocalAi.Demo -- --export-dataset finetune/data/paint_queue_advisor.jsonl
python3 finetune/train_lora.py --dry-run --data finetune/data/paint_queue_advisor.jsonl
```

## 测试

```bash
dotnet test
bash scripts/dev-check.sh
```

测试不下载模型，也不加载 GGUF 权重。

## English summary

Rule-first paint-queue dispatch with an optional on-box advisor. Safety interlocks never call the model. `ILlmAdvisor` may only reorder the existing top candidates or attach a short HMI explanation; a missing model, timeout (default 200 ms), or invalid output keeps the rule order. The core library does not reference LLamaSharp and does not download weights. Demo can talk to loopback Ollama or a local GGUF through the CPU build of LLamaSharp (`GpuLayerCount` forced to 0 for the UHD 770 machine). Install scripts download models only when asked. Fine-tune data, LoRA/QLoRA, and GGUF export notes are in `finetune/README.md`.
