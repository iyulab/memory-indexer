# MemoryChatApp

A simple chat application demonstrating Memory Indexer's short/mid/long-term memory capabilities.

## Features

- **Persistent Memory**: Uses SQLite with vector search (1024 dimensions)
- **Dual Embedding Support**:
  - **Default**: LMSupply Local (bge-large-en-v1.5) - No API required
  - **Optional**: GpuStack (bge-m3) - Requires API configuration
- **Memory Types**:
  - Episodic (conversation history)
  - Semantic (user facts extracted automatically)
- **Session Management**: Short-term memory within session, long-term memory across sessions
- **Memory Recall**: Automatically retrieves relevant memories for context-aware responses

## Prerequisites

### Default Mode (LMSupply Local)
- **.NET 10.0**: Required runtime
- No additional configuration required
- First run will download the embedding model (~400MB)

### GpuStack Mode (Optional)
Configure `.env` file in solution root:
```
GPUSTACK_URL=http://your-gpustack-server/v1-openai
GPUSTACK_APIKEY=your-api-key
GPUSTACK_MODEL=Qwen3-8B
```

## Usage

```bash
# Run from solution root
dotnet run --project samples/MemoryChatApp

# Or from this directory
dotnet run
```

## App Flow

### Main Menu
- **1. chat**: Interactive chat mode with memory-augmented responses
- **2. status**: View memory statistics and storage details
- **3. exit**: Exit application

### Chat Mode
- Type your message and press Enter
- Memory Indexer automatically:
  - Stores your messages as episodic memories
  - Extracts semantic facts (e.g., "My name is John")
  - Recalls relevant past memories for context
  - Generates rolling summaries periodically
- Type `exit` or press `Ctrl+C` to return to main menu

### Status Mode
Displays:
- Total memory count by type (Episodic/Semantic/Procedural)
- Memory distribution by session
- Recent memories with timestamps
- High-importance memories
- Database storage details

## Architecture

```
User Message → Store as Episodic Memory
            → Recall Relevant Memories (session + cross-session)
            → Build Context with Memories
            → Generate LLM Response (or Echo Mode if no LLM)
            → Store Response as Memory
            → Extract Semantic Facts (if detected)
            → Update Rolling Summary (every 10 turns)
```

## Configuration

| Mode | Embedding | Chat LLM | Configuration |
|------|-----------|----------|---------------|
| Default | LMSupply bge-large-en-v1.5 (local) | Echo Mode | None |
| GpuStack | bge-m3 (remote) | GPUSTACK_MODEL | .env file |

## Embedding Models

### LMSupply Local (Default)
- Model: `bge-large-en-v1.5`
- Dimensions: 1024
- No API key required
- Runs locally using ONNX Runtime

### GpuStack (Optional)
- Model: `bge-m3`
- Dimensions: 1024
- Requires GPUSTACK_URL and GPUSTACK_APIKEY
- OpenAI-compatible API
