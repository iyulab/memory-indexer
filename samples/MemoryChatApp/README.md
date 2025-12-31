# MemoryChatApp

A simple chat application demonstrating Memory Indexer's short/mid/long-term memory capabilities.

## Features

- **Persistent Memory**: Uses SQLite with vector search (bge-m3 embeddings, 1024 dimensions)
- **Memory Types**:
  - Episodic (conversation history)
  - Semantic (user facts extracted automatically)
- **Session Management**: Short-term memory within session, long-term memory across sessions
- **Memory Recall**: Automatically retrieves relevant memories for context-aware responses

## Prerequisites

1. **GpuStack API**: Configure `.env` file with:
   ```
   GPUSTACK_URL=http://your-gpustack-server/v1-openai
   GPUSTACK_APIKEY=your-api-key
   ```

2. **.NET 10.0**: Required runtime

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
            → Generate LLM Response
            → Store Response as Memory
            → Extract Semantic Facts (if detected)
            → Update Rolling Summary (every 10 turns)
```

## Configuration

- **Database**: `chat_memories.db` (SQLite with vector extension)
- **Embedding Model**: bge-m3 (1024 dimensions)
- **Chat Model**: Qwen3-8B (via GpuStack)
