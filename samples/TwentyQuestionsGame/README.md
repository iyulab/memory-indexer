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

## Key Config

```csharp
// Program.cs
services.AddMemoryIndexer(options =>
{
    options.Deduplication.Enabled = false;  // Preserve all Q&A pairs as separate memories
});
```
