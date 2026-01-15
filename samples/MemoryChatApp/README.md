# MemoryChatApp

A simple chat application demonstrating Memory Indexer's short/mid/long-term memory capabilities.

## Why Memory-Indexer?

Traditional chat apps send **full conversation history** to the LLM on every request:

```
Traditional: messages = [msg1, msg2, msg3, ... msgN]  → Token cost: O(n)
```

Memory-Indexer replaces this with **intelligent recall**:

```
Memory-Indexer: recalled_context = recall(query, budget=2000)  → Token cost: O(1)
```

| Aspect | Traditional | Memory-Indexer |
|--------|-------------|----------------|
| Token Cost | Linear growth | Fixed budget |
| Cross-session Knowledge | ❌ Lost | ✅ Semantic/Fact memories |
| Relevance | Order-based (last N) | Similarity-based search |
| Context Window | Truncate when exceeded | Smart allocation by tier |

## Architecture

```
┌─────────────────────────────────────────────────┐
│  Frontend (Vite + TypeScript)                   │
│  http://localhost:3000                          │
│  - Chat UI with session management              │
│  - Memory status sidebar                        │
│  - Proxy to backend /api/*                      │
└─────────────────────┬───────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────┐
│  Backend (ASP.NET Minimal API)                  │
│  http://localhost:5000                          │
│  - /api/health - Health check                   │
│  - /api/session - Create session                │
│  - /api/chat - Send message                     │
│  - /api/status - Get memory stats               │
│  - /api/memories - Delete all memories          │
└─────────────────────┬───────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────┐
│  Memory Indexer                                 │
│  - SQLite with vector search (1024 dims)        │
│  - Episodic + Semantic memory types             │
│  - Session-scoped + cross-session retrieval     │
└─────────────────────────────────────────────────┘
```

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

## Quick Start

### Prerequisites
- **.NET 10.0 SDK**
- **Node.js 18+** (for frontend)

### Development Mode (Hot Reload)

```powershell
# Run from this directory
.\start-dev.ps1
```

This opens two console windows:
- Backend: `http://localhost:5000` (dotnet watch)
- Frontend: `http://localhost:3000` (Vite dev server)

### Manual Start

```bash
# Terminal 1: Backend
dotnet run

# Terminal 2: Frontend
cd frontend
npm install
npm run dev
```

### Production Build

```bash
# Build frontend
cd frontend
npm run build

# Build backend
dotnet build -c Release
```

## Configuration

### Default Mode (LMSupply Local)
- No configuration required
- First run downloads embedding model (~400MB)
- Chat runs in "Echo Mode" (no LLM)

### GpuStack Mode (Optional)

Create `.env` file in solution root:

```env
# Required for chat LLM
GPUSTACK_URL=http://your-gpustack-server/v1
GPUSTACK_APIKEY=your-api-key
GPUSTACK_MODEL=gpt-oss-20b

# Optional: If not set, uses LMSupply Local embedding
# GPUSTACK_EMBED_MODEL=bge-m3
```

| Mode | Embedding | Chat LLM | Configuration |
|------|-----------|----------|---------------|
| Default | LMSupply bge-large-en-v1.5 (local) | Echo Mode | None |
| GpuStack Chat | LMSupply bge-large-en-v1.5 (local) | GPUSTACK_MODEL | .env (no EMBED_MODEL) |
| GpuStack Full | bge-m3 (remote) | GPUSTACK_MODEL | .env (with EMBED_MODEL) |

## App Flow

```
User Message → Store as Episodic Memory
            → Recall Relevant Memories (session + cross-session)
            → Build Context with Memories
            → Generate LLM Response (or Echo Mode if no LLM)
            → Store Response as Memory
            → Extract Semantic Facts (if detected)
```

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/health` | Health check |
| POST | `/api/session` | Create new session |
| POST | `/api/chat` | Send message |
| GET | `/api/status` | Get memory statistics |
| DELETE | `/api/memories` | Clear all memories |

## Embedding Models

### LMSupply Local (Default)
- Model: `bge-large-en-v1.5`
- Dimensions: 1024
- No API key required
- Runs locally using ONNX Runtime

### GpuStack (Optional)
- Model: `bge-m3`
- Dimensions: 1024
- Requires GPUSTACK_URL, GPUSTACK_APIKEY, GPUSTACK_EMBED_MODEL
- OpenAI-compatible API
