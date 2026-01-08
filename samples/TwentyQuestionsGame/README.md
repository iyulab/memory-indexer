# Twenty Questions Game

AI vs AI demo proving **memory-indexer**'s core value: context without chat history.

## Concept

```
Traditional: Pass full chat history to LLM each turn
┌─────────────────────────────────────────────┐
│ messages: [Q1, A1, Q2, A2, Q3, A3, ...]    │ ← Growing context
└─────────────────────────────────────────────┘

This Demo: Memory-only context
┌─────────────────────────────────────────────┐
│ system: [recalled memories...]              │
│ user: "Yes"        ← Only last response    │
└─────────────────────────────────────────────┘
```

## Architecture

```
samples/TwentyQuestionsGame/
├── Program.cs              # DI + game setup (~130 lines)
├── Game/
│   ├── Game.cs             # GameConfiguration + GameState
│   └── GameRunner.cs       # Game loop orchestrator
├── Agents/
│   ├── AgentBase.cs        # Tool call processing loop
│   ├── AlphaAgent.cs       # QuizMaster (answers Yes/No/Maybe)
│   └── BetaAgent.cs        # Guesser (asks questions)
├── ToolCall/
│   ├── ToolCallParser.cs   # Parse <tool_call> XML
│   └── ToolCallExecutor.cs # Execute memory_store/recall
├── LLM/
│   └── LlmClient.cs        # OpenAI HTTP client + DTOs
└── Prompts/
    ├── AlphaSystemPrompt.md
    └── BetaSystemPrompt.md
```

## How It Works

1. **Beta asks** → LLM calls `memory_recall()` → gets previous Q&A → asks new question
2. **Alpha answers** → LLM calls `memory_recall()` → checks secret → responds Yes/No/Maybe
3. **Q&A stored** → Both agents can recall in future rounds
4. **Repeat** until Beta guesses correctly or 20 rounds pass

```xml
<!-- LLM tool call format -->
<tool_call>
memory_recall(query="GAME_QA asked answered", limit=20)
</tool_call>
```

## Memory Isolation

| Agent | User ID | Session ID | Purpose |
|-------|---------|------------|---------|
| Alpha | `alpha` | `alpha-session` | Stores secret, Q&A history |
| Beta | `beta` | `beta-session` | Stores Q&A history, strategy |

## Run

```bash
# Set OPENAI_API_KEY in .env (project root)
cd samples/TwentyQuestionsGame
dotnet run
```

## Sample Output

```
═══════════════════ Round 1/20 ═══════════════════
[BETA] >>> Is it a living thing?
       ⏱️ LLM: 1200ms | 🔧 Tool iterations: 1
[ALPHA] >>> No
        ⏱️ LLM: 800ms | Guess: False

═══════════════════ Round 19/20 ═══════════════════
[BETA] >>> My final guess is: the Eiffel Tower
       ⏱️ LLM: 1500ms | 🔧 Tool iterations: 1
[ALPHA] >>> Correct! You got it!

══════════════════════════════════════════════════
🎉 BETA WINS! Correctly guessed "the Eiffel Tower" in round 19!

📊 Game Statistics:
   Rounds played: 19
   Total tokens: 45,000
```

## Benchmark Mode

Run multiple games and collect metrics aligned with cognitive science principles (MemoryBench dimensions).

```bash
# Single game with benchmark output
dotnet run -- --benchmark

# Multiple iterations
dotnet run -- -b -n 10

# Save results to JSON
dotnet run -- -b -n 5 -o benchmark_results.json
```

### Benchmark Metrics

| Category | Metric | Description |
|----------|--------|-------------|
| **Effectiveness** | Win Rate | Beta success rate across games |
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
  BENCHMARK RESULTS
══════════════════════════════════════════════════════════════════════

📊 EFFECTIVENESS:
   Win Rate:           70.0% (7/10)
   Avg Rounds to Win:  15.3
   Avg Recall Precision: 85.0%

⚡ EFFICIENCY:
   Avg Tokens/Game:    42,500
   Avg LLM Time:       25,000ms
   Avg Recall Time:    1,200ms
   Recall Overhead:    4.8%

🧠 COGNITIVE SCIENCE COMPLIANCE:
   Working Memory (7±2): 80.0%
   Healthy Tier Flow:    100.0%
```

## Key Config

```csharp
// Program.cs
services.AddMemoryIndexer(options =>
{
    options.Deduplication.Enabled = false;  // Preserve all Q&A pairs as separate memories
});
```
