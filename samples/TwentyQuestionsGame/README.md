# Twenty Questions Game

AI vs AI demo validating **memory-indexer**'s core functionality: semantic memory recall without chat history injection.

## Purpose & Value

This sample serves as a **functional validation** for Memory Indexer, not as a game showcase.

| Aspect | Description |
|--------|-------------|
| **Primary Goal** | Validate `memory_store` and `memory_recall` work correctly |
| **Design Philosophy** | Keep sample thin; improvements go into the library |
| **Success Metric** | Memory Indexer features work as designed |

**What This Proves:**
- O(1) context maintenance via semantic recall (no growing chat history)
- Agents can build coherent strategy using only recalled memories
- Memory isolation between agents works correctly

## Architecture

```
samples/TwentyQuestionsGame/
├── Program.cs              # DI + game setup, CLI parsing
├── Game/
│   ├── Game.cs             # GameConfiguration + GameState
│   └── GameRunner.cs       # Game loop orchestrator
├── Agents/
│   ├── AgentBase.cs        # Tool call processing loop
│   ├── AlphaAgent.cs       # QuizMaster (answers Yes/No/Maybe)
│   └── BetaAgent.cs        # Guesser (asks questions via memory_recall)
├── ToolCall/
│   ├── ToolCallParser.cs   # Parse <tool_call> XML
│   └── ToolCallExecutor.cs # Execute memory_store/recall
├── LLM/
│   └── LlmClient.cs        # LLM client (OpenAI/GPUStack/Local)
├── Benchmark/
│   └── BenchmarkRunner.cs  # Multi-game benchmark runner
└── Prompts/
    ├── AlphaSystemPrompt.md
    └── BetaSystemPrompt.md
```

## How It Works

### Memory-Only Context (Key Differentiator)

```
Traditional Approach: Pass full chat history each turn
┌─────────────────────────────────────────────────────────┐
│ messages: [Q1, A1, Q2, A2, Q3, A3, ... Q19, A19]       │ ← O(n) growing
└─────────────────────────────────────────────────────────┘

This Demo: Memory-only context
┌─────────────────────────────────────────────────────────┐
│ system: "Use memory_recall to get previous Q&A"         │
│ user: "Alpha says: Yes"    ← Only last response        │ ← O(1) constant
└─────────────────────────────────────────────────────────┘
```

**Critical**: Beta's prompt does NOT inject Q&A history directly. Beta must use `memory_recall()` to retrieve previous questions and answers. This validates Memory Indexer's semantic search functionality.

### Game Flow

1. **Alpha stores secret** → `memory_store("MY_SECRET: Eiffel Tower")`
2. **Beta recalls history** → `memory_recall("GAME_QA")` → gets previous Q&A
3. **Beta asks question** → Based on recalled memories
4. **Q&A stored** → Both agents store the exchange
5. **Repeat** until correct guess or 20 rounds

```xml
<!-- LLM tool call format -->
<tool_call>
memory_recall(query="GAME_QA asked answered", limit=20)
</tool_call>
```

### Memory Isolation

| Agent | User ID | Session ID | Stores |
|-------|---------|------------|--------|
| Alpha | `alpha` | `alpha-session` | Secret, Q&A history |
| Beta | `beta` | `beta-session` | Q&A history, strategy notes |

## Running the Demo

### Prerequisites

Set one of these in `.env` file (project root):

```bash
# Option 1: OpenAI (recommended for quick start)
OPENAI_API_KEY=sk-...

# Option 2: GPUStack (self-hosted)
GPUSTACK_URL=http://localhost:8080/v1
GPUSTACK_APIKEY=your-key
GPUSTACK_MODEL=gpt-oss-20b

# Option 3: Use --local flag (no API key needed)
```

### Basic Usage

```bash
cd samples/TwentyQuestionsGame

# Auto-detect provider (GPUStack > OpenAI)
dotnet run

# Use local ONNX model (no API key needed)
dotnet run -- --local

# Show full LLM prompts for debugging
dotnet run -- --debug
```

### CLI Options

| Flag | Description |
|------|-------------|
| `-l, --local` | Use LMSupply local model (Phi-4) |
| `-m, --model MODEL` | Specify local model ID |
| `-d, --debug` | Show full LLM prompts |
| `-b, --benchmark` | Enable benchmark mode |
| `-n, --iterations N` | Number of games (benchmark) |
| `-o, --output FILE` | Save results to JSON |

### LLM Provider Priority

1. `--local` flag → LMSupply (local ONNX model)
2. `GPUSTACK_URL` + `GPUSTACK_APIKEY` → GPUStack
3. `OPENAI_API_KEY` → OpenAI (default model: `gpt-4o-mini`)

## Sample Output

```
╔══════════════════════════════════════════════════════════════╗
║          Twenty Questions Game - Memory Demo                  ║
║          AI vs AI with LLM-generated secrets                 ║
╚══════════════════════════════════════════════════════════════╝

[CONFIG] LLM Provider: OpenAI
[CONFIG] LLM Model: gpt-4o-mini
[CONFIG] Embedding: text-embedding-3-small

═══════════════════ Round 1/20 ═══════════════════
[BETA] >>> Is it a living thing?
       ⏱️ LLM: 1200ms | 🔧 Tool iterations: 1
[ALPHA] >>> No
        ⏱️ LLM: 800ms | Guess: False

═══════════════════ Round 15/20 ═══════════════════
[BETA] >>> My final guess is: the Eiffel Tower
       ⏱️ LLM: 1100ms | 🔧 Tool iterations: 1
[ALPHA] >>> Yes
        ⏱️ LLM: 600ms | Guess: True, Correct: True

══════════════════════════════════════════════════
🎉 BETA WINS! Correctly guessed in round 15!

📊 Game Statistics:
   Total time: 65.2s
   Total tokens: 25,420
   Avg latency: 2.1s/round
```

## Benchmark Mode

Run multiple games and collect metrics aligned with cognitive science principles.

```bash
# Single game with benchmark output
dotnet run -- --benchmark

# Multiple iterations
dotnet run -- -b -n 10

# Save results to JSON
dotnet run -- -b -n 5 -o benchmark_results.json
```

### Metrics

| Category | Metric | Description |
|----------|--------|-------------|
| **Effectiveness** | Win Rate | Beta success rate |
| | Rounds to Win | Average rounds needed |
| | Recall Precision | Relevant memories / total recalled |
| **Efficiency** | Tokens/Game | Total token consumption |
| | LLM Time | Total LLM latency |
| | Recall Overhead | Memory recall time vs LLM time |
| **Cognitive Compliance** | Working Memory (7±2) | Baddeley's capacity adherence |
| | Healthy Tier Flow | Buffer < Short < Long distribution |

### Sample Benchmark Output

```
══════════════════════════════════════════════════════════════════════
  BENCHMARK RESULTS (5 games)
══════════════════════════════════════════════════════════════════════

📊 EFFECTIVENESS:
   Win Rate:           80.0% (4/5)
   Avg Rounds to Win:  14.5
   Avg Recall Precision: 88.0%

⚡ EFFICIENCY:
   Avg Tokens/Game:    28,500
   Avg LLM Time:       45,000ms
   Avg Recall Time:    850ms
   Recall Overhead:    1.9%

🧠 COGNITIVE COMPLIANCE:
   Working Memory (7±2): 85.0%
   Healthy Tier Flow:    100.0%
```

## Design Decisions

### Why No Direct Q&A Injection?

```csharp
// BetaAgent.cs - Q&A history is NOT injected
// ==========================================================================
// Memory Indexer Validation: Beta must rely on memory_recall for Q&A history
// No direct injection - this tests semantic search functionality
// ==========================================================================
```

If we injected Q&A history directly into the prompt, the sample would bypass Memory Indexer entirely, defeating its validation purpose.

### Why `gpt-4o-mini` Default?

- `gpt-4o`: Higher quality but ~10-20s/response
- `gpt-4o-mini`: Good enough for demo, ~1-3s/response

### Why MaxOutputTokenCount = 300?

Agents only need to output:
- Alpha: JSON response (~50 tokens)
- Beta: Question + tool call (~100 tokens)

Limiting output prevents runaway responses and reduces costs.

## Extending This Sample

This sample intentionally stays minimal. If you need:

| Need | Solution |
|------|----------|
| Better game strategy | Improve prompts in `Prompts/` directory |
| Different secret categories | Modify `AlphaSystemPrompt.md` |
| More sophisticated recall | Enhance `memory_recall` query patterns |
| Custom memory configurations | Adjust `AddMemoryIndexer()` options |

**Remember**: Generic improvements should go into the Memory Indexer library, not this sample.

## Troubleshooting

### Empty Alpha Response

If Alpha's response is empty, check:
1. LLM is responding with JSON format
2. Use `--debug` flag to see actual prompts

### Game Ends Prematurely

If the game ends before Round 20 with incorrect result:
1. Check if Alpha correctly distinguishes `isGuess: true` (exact guess) vs `isGuess: false` (category question)
2. Use `--debug` to trace the logic

### High Latency

If responses take >10s:
1. Check LLM provider (GPUStack may be slower)
2. Try `--local` for local model
3. Verify network connectivity
