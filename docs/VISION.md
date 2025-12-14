# Memory Indexer Vision

## Executive Summary

Memory Indexer는 LLM 기반 AI 채팅 서비스의 **컨텍스트 윈도우 한계를 근본적으로 해결**하는 지능형 메모리 관리 시스템입니다.

기존 접근 방식(요약, 슬라이딩 윈도우, 대화 히스토리 전달)을 대체하여, AI 서비스 개발자가 **새 프롬프트만 전달**하면 Memory Indexer가 모든 맥락 관리를 자동으로 처리합니다.

---

## Problem Statement

### 현재 LLM 서비스의 한계

```
┌─────────────────────────────────────────────────────────────┐
│                    Context Window Limit                      │
│                    (128K, 200K tokens)                       │
├─────────────────────────────────────────────────────────────┤
│  Session 1    │  Session 2    │  Session 3    │  Session N  │
│  ■■■■■■■■■■   │  ■■■■■■■■■■   │  ■■■■■■■■■■   │  ■■■■■■■■■  │
│  (lost)       │  (lost)       │  (lost)       │  (current)  │
└─────────────────────────────────────────────────────────────┘

문제점:
- 이전 세션 정보 완전 소실
- 긴 세션에서 초반 대화 맥락 유실
- 매 세션마다 사용자 정보 재수집 필요
```

### 기존 해결책의 문제점

| 기법 | 설명 | 한계 |
|------|------|------|
| **Summarization** | 대화 요약 후 전달 | 정보 손실, 추가 LLM 호출 비용 |
| **Sliding Window** | 최근 N개 메시지만 유지 | 중요 초반 맥락 유실 |
| **RAG** | 관련 문서 검색 후 주입 | 대화 맥락 특화 아님 |
| **History Passing** | 전체 대화 히스토리 전달 | 토큰 한계 직면 |

---

## Vision: Intelligent Memory Management

### 목표

> **"AI 서비스 개발자는 새 프롬프트만 전달하고, 모든 맥락 관리는 Memory Indexer에 위임한다"**

```
┌─────────────────────────────────────────────────────────────┐
│                     Memory Indexer                           │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │   Working   │  │   Session   │  │        User         │  │
│  │   Memory    │  │   Memory    │  │       Memory        │  │
│  │  (Immediate)│  │  (Current)  │  │    (Persistent)     │  │
│  │             │  │             │  │                     │  │
│  │  최근 대화   │  │ 현재 세션    │  │  선호도, 사실,      │  │
│  │  맥락       │  │  전체 맥락   │  │  중요 정보          │  │
│  └──────┬──────┘  └──────┬──────┘  └──────────┬──────────┘  │
│         │                │                     │             │
│         └────────────────┴─────────────────────┘             │
│                          │                                   │
│              ┌───────────▼───────────┐                       │
│              │   Intelligent Recall  │                       │
│              │   (Context Assembly)  │                       │
│              └───────────┬───────────┘                       │
│                          │                                   │
└──────────────────────────┼──────────────────────────────────┘
                           │
                           ▼
              ┌─────────────────────────┐
              │   Optimized Context     │
              │   for LLM Prompt        │
              └─────────────────────────┘
```

### 핵심 원칙

1. **Zero Context Engineering**: 소비자가 컨텍스트 관리 코드를 작성하지 않음
2. **Intelligent Placement**: 메모리의 중요도/유형에 따른 자동 배치
3. **Hierarchical Memory**: 인간 기억 구조를 모방한 계층적 저장
4. **Proactive Consolidation**: 자동 기억 정리 및 통합

---

## Memory Hierarchy

### Three-Tier Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    TIER 1: Working Memory                    │
│                    (Immediate Context)                       │
├─────────────────────────────────────────────────────────────┤
│  Scope:     Current conversation turn + recent exchanges     │
│  Lifetime:  Seconds to minutes                               │
│  Capacity:  ~10-20 items (like human working memory)        │
│  Purpose:   Immediate coherence, anaphora resolution         │
│                                                              │
│  Example:                                                    │
│  - "그거 말이야" → "그거" = 직전 언급된 피자                  │
│  - 현재 진행 중인 작업의 상태                                │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                    TIER 2: Session Memory                    │
│                    (Episodic Context)                        │
├─────────────────────────────────────────────────────────────┤
│  Scope:     Current session's full conversation              │
│  Lifetime:  Duration of session (hours to days)              │
│  Capacity:  Hundreds of memories                             │
│  Purpose:   Session coherence, topic continuity              │
│                                                              │
│  Example:                                                    │
│  - 세션 초반에 논의한 프로젝트 요구사항                       │
│  - 이 세션에서 사용자가 선택한 옵션들                        │
│  - 현재 세션의 목표와 진행 상황                              │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                    TIER 3: User Memory                       │
│                    (Semantic Long-term)                      │
├─────────────────────────────────────────────────────────────┤
│  Scope:     Cross-session persistent information             │
│  Lifetime:  Permanent (until explicitly deleted)             │
│  Capacity:  Unlimited                                        │
│  Purpose:   Personalization, user understanding              │
│                                                              │
│  Example:                                                    │
│  - "사용자는 Python보다 TypeScript를 선호함"                 │
│  - "사용자의 프로젝트는 e-commerce 플랫폼"                   │
│  - "사용자는 간결한 설명을 좋아함"                           │
└─────────────────────────────────────────────────────────────┘
```

### Memory Types (Orthogonal to Tiers)

| Type | Description | Example | Typical Tier |
|------|-------------|---------|--------------|
| **Episodic** | 특정 사건/대화의 기억 | "어제 API 에러 해결함" | Session |
| **Semantic** | 일반화된 사실/지식 | "사용자는 React 개발자" | User |
| **Procedural** | 수행 방법에 대한 기억 | "이 프로젝트는 pnpm 사용" | User |
| **Fact** | 명시적으로 저장된 사실 | "생일: 3월 15일" | User |

---

## Intelligent Processing

### Memory Placement Decision

```
┌─────────────────────────────────────────────────────────────┐
│                   Memory Classification                      │
│                      (Intelligence)                          │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  New Message ──► [Analyzer] ──┬──► Working Memory (transient)│
│                               │                              │
│                               ├──► Session Memory (episodic) │
│                               │                              │
│                               └──► User Memory (persistent)  │
│                                                              │
│  Classification Factors:                                     │
│  ┌─────────────────────────────────────────────────────────┐│
│  │ • Temporal Scope: 이 정보가 얼마나 오래 유효한가?       ││
│  │ • Generalizability: 다른 맥락에서도 유용한가?           ││
│  │ • Importance: 사용자에게 얼마나 중요한가?               ││
│  │ • Uniqueness: 새로운 정보인가, 기존 정보의 반복인가?    ││
│  │ • Topic: 어떤 주제/도메인에 속하는가?                   ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

### Automatic Consolidation

```
Session End / Periodic Batch
            │
            ▼
┌─────────────────────────────────────────────────────────────┐
│                   Memory Consolidation                       │
│              (Like Sleep Memory Processing)                  │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  1. Duplicate Detection                                      │
│     "피자 좋아해" + "피자 먹고싶어" → "사용자는 피자 선호"    │
│                                                              │
│  2. Importance Promotion                                     │
│     Session Memory (자주 참조됨) → User Memory로 승격        │
│                                                              │
│  3. Decay & Cleanup                                          │
│     오래된 + 낮은 중요도 + 미참조 → 정리/삭제                │
│                                                              │
│  4. Summarization                                            │
│     다수의 에피소드 기억 → 하나의 요약 기억으로 통합         │
│                                                              │
│  5. Fact Extraction                                          │
│     대화에서 명시적 사실 추출 → Fact Memory 생성             │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

### Intelligent Recall

```
User Query: "지난번에 얘기했던 그 API 문제 어떻게 됐어?"
                    │
                    ▼
┌─────────────────────────────────────────────────────────────┐
│                    Recall Pipeline                           │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  Step 1: Query Understanding                                 │
│  ┌─────────────────────────────────────────────────────────┐│
│  │ • "지난번" → 과거 세션 참조                              ││
│  │ • "API 문제" → 토픽 식별                                 ││
│  │ • 질문 의도: 상태 확인                                   ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
│  Step 2: Multi-Tier Search                                   │
│  ┌─────────────────────────────────────────────────────────┐│
│  │ Working Memory  → (없음 - 새 세션)                       ││
│  │ Session Memory  → (현재 세션에 관련 내용 없음)           ││
│  │ User Memory     → "API 인증 에러 해결함 (2일 전)"        ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
│  Step 3: Context Assembly                                    │
│  ┌─────────────────────────────────────────────────────────┐│
│  │ Retrieved: [API 에러 해결 기억, 프로젝트 정보, 선호도]   ││
│  │ Ranked by: Relevance × Recency × Importance             ││
│  │ Formatted: LLM-optimized context block                  ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
└─────────────────────────────────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────────────────┐
│  Context for LLM:                                            │
│  "사용자 맥락: TypeScript 개발자, e-commerce 프로젝트        │
│   관련 기억: 2일 전 API 인증 에러를 JWT 토큰 갱신으로 해결  │
│   현재 질문: API 문제 해결 상태 확인"                        │
└─────────────────────────────────────────────────────────────┘
```

---

## Consumer Experience

### Before (Traditional Approach)

```python
# 개발자가 직접 모든 것을 관리해야 함
class ChatService:
    def __init__(self):
        self.conversation_history = []
        self.user_profile = {}
        self.summarizer = SummarizerModel()

    def chat(self, user_message):
        # 1. 히스토리 관리
        self.conversation_history.append(user_message)

        # 2. 컨텍스트 윈도우 체크
        if self.count_tokens(self.conversation_history) > MAX_TOKENS:
            # 3. 요약 수행
            summary = self.summarizer.summarize(self.conversation_history[:10])
            self.conversation_history = [summary] + self.conversation_history[10:]

        # 4. 사용자 프로필 로드
        user_context = self.load_user_profile()

        # 5. 프롬프트 조립
        prompt = self.build_prompt(
            system=SYSTEM_PROMPT,
            user_context=user_context,
            history=self.conversation_history,
            current=user_message
        )

        # 6. LLM 호출
        response = llm.generate(prompt)

        # 7. 히스토리 업데이트
        self.conversation_history.append(response)

        # 8. 중요 정보 추출 및 저장
        self.extract_and_save_important_info(user_message, response)

        return response
```

### After (With Memory Indexer)

```python
# Memory Indexer가 모든 복잡성을 처리
class ChatService:
    def __init__(self, user_id: str):
        self.memory = MemoryIndexerClient(user_id)
        self.session = self.memory.create_session()

    async def chat(self, user_message: str):
        # 1. Memory Indexer에 새 메시지 저장 (자동 분류/배치)
        await self.memory.ingest(self.session, user_message)

        # 2. 관련 컨텍스트 자동 조립
        context = await self.memory.recall(user_message)

        # 3. LLM 호출 (컨텍스트 자동 포함)
        response = await llm.generate(
            system=SYSTEM_PROMPT,
            context=context,  # Memory Indexer가 조립
            message=user_message
        )

        # 4. 응답도 저장 (자동 분류/배치)
        await self.memory.ingest(self.session, response, role="assistant")

        return response
```

### API Surface (Simplified)

```python
# 소비자가 알아야 할 것은 이것뿐
memory = MemoryIndexer(user_id="user_123")

# 세션 시작
session = memory.start_session()

# 메시지 저장 (모든 분류/배치는 자동)
await memory.ingest(session, message)

# 관련 기억 조회 (모든 계층에서 자동 검색)
context = await memory.recall(query)

# 세션 종료 (자동 통합 수행)
await memory.end_session(session)
```

---

## Architecture Components

### Required Services

```
┌─────────────────────────────────────────────────────────────┐
│                     Memory Indexer                           │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                  MCP Interface Layer                    ││
│  │  memory_ingest, memory_recall, memory_session_*        ││
│  └─────────────────────────────────────────────────────────┘│
│                           │                                  │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                  Intelligence Layer                     ││
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐       ││
│  │  │  Classifier │ │Consolidator │ │  Extractor  │       ││
│  │  │  (배치결정) │ │ (기억통합)  │ │ (사실추출) │       ││
│  │  └─────────────┘ └─────────────┘ └─────────────┘       ││
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐       ││
│  │  │ Summarizer  │ │  Deduper    │ │   Ranker    │       ││
│  │  │  (요약)     │ │ (중복제거)  │ │  (재순위)   │       ││
│  │  └─────────────┘ └─────────────┘ └─────────────┘       ││
│  └─────────────────────────────────────────────────────────┘│
│                           │                                  │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                    Core Layer                           ││
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐       ││
│  │  │  Embedder   │ │   Scorer    │ │   Storage   │       ││
│  │  │ (임베딩)    │ │ (스코어링)  │ │  (저장소)   │       ││
│  │  └─────────────┘ └─────────────┘ └─────────────┘       ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

### LocalAI Package Requirements (Revised)

| Package | Role | Necessity |
|---------|------|-----------|
| **LocalAI.Embedder** | 텍스트 → 벡터 변환 | ✅ **필수** |
| **LocalAI.Reranker** | 검색 결과 재순위 | ✅ **필수** (Ranker) |
| **LocalAI.Generator** | 요약, 분류, 추출 | ✅ **필수** (Intelligence) |

### Intelligence Layer Detail

```
┌─────────────────────────────────────────────────────────────┐
│                  Intelligence Layer                          │
│              (Powered by LocalAI.Generator)                  │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │  Memory Classifier                                      ││
│  │  ───────────────────                                    ││
│  │  Input:  New message                                    ││
│  │  Output: { tier: "session", type: "episodic",           ││
│  │            importance: 0.8, topics: ["api", "error"] }  ││
│  │  Model:  Lightweight LLM (phi-3-mini, Qwen2.5-1.5B)     ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │  Fact Extractor                                         ││
│  │  ───────────────                                        ││
│  │  Input:  Conversation segment                           ││
│  │  Output: ["User prefers TypeScript", "Project: e-comm"] ││
│  │  Model:  Lightweight LLM                                ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │  Memory Summarizer                                      ││
│  │  ─────────────────                                      ││
│  │  Input:  Multiple related memories                      ││
│  │  Output: Consolidated summary memory                    ││
│  │  Model:  Lightweight LLM                                ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │  Importance Estimator                                   ││
│  │  ────────────────────                                   ││
│  │  Input:  Message content                                ││
│  │  Output: Importance score (0.0 - 1.0)                   ││
│  │  Model:  Lightweight LLM or heuristic                   ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

---

## Data Model

### Entity Relationships

```
┌──────────────┐       ┌──────────────┐       ┌──────────────┐
│     User     │       │   Session    │       │    Memory    │
├──────────────┤       ├──────────────┤       ├──────────────┤
│ Id           │──┐    │ Id           │──┐    │ Id           │
│ CreatedAt    │  │    │ UserId (FK)  │  │    │ UserId (FK)  │
│ Metadata     │  │    │ StartedAt    │  │    │ SessionId?   │
└──────────────┘  │    │ EndedAt?     │  │    │ Tier         │
                  │    │ Status       │  │    │ Type         │
                  │    └──────────────┘  │    │ Content      │
                  │           │          │    │ Embedding    │
                  │           │          │    │ Importance   │
                  └───────────┼──────────┘    │ Topics[]     │
                              │               │ AccessCount  │
                              │               │ CreatedAt    │
                              └──────────────►│ LastAccessed │
                                              └──────────────┘

Memory.Tier:
  - Working   (transient, not persisted)
  - Session   (persisted, session-scoped)
  - User      (persisted, permanent)

Memory.Type:
  - Episodic   (specific event/conversation)
  - Semantic   (generalized fact/knowledge)
  - Procedural (how-to knowledge)
  - Fact       (explicit stored fact)
```

### Storage Strategy

```
┌─────────────────────────────────────────────────────────────┐
│                    Storage Strategy                          │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  Working Memory:                                             │
│  └─► In-Memory only (Redis/Local cache)                      │
│      - Fast access                                           │
│      - Auto-expiry                                           │
│      - No persistence needed                                 │
│                                                              │
│  Session Memory:                                             │
│  └─► Hot Storage (SQLite/PostgreSQL)                         │
│      - Fast read/write                                       │
│      - Session-scoped queries                                │
│      - Periodic consolidation                                │
│                                                              │
│  User Memory:                                                │
│  └─► Vector Database (Qdrant/SQLite-vec)                     │
│      - Semantic search                                       │
│      - Long-term persistence                                 │
│      - Cross-session queries                                 │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

---

## MCP Tools (Revised)

### Session Management

| Tool | Description |
|------|-------------|
| `session_start` | 새 세션 시작, Working Memory 초기화 |
| `session_end` | 세션 종료, 자동 통합 트리거 |
| `session_status` | 현재 세션 상태 조회 |

### Memory Operations

| Tool | Description |
|------|-------------|
| `memory_ingest` | 새 메시지 저장 (자동 분류/배치) |
| `memory_recall` | 쿼리 기반 관련 기억 조회 (전 계층) |
| `memory_get` | ID로 특정 기억 조회 |
| `memory_update` | 기억 내용/메타데이터 수정 |
| `memory_delete` | 기억 삭제 |
| `memory_promote` | Session → User 메모리 승격 |

### Intelligence Operations

| Tool | Description |
|------|-------------|
| `memory_consolidate` | 수동 기억 통합 트리거 |
| `memory_extract_facts` | 대화에서 사실 추출 |
| `memory_summarize` | 기억 그룹 요약 |

### Query Operations

| Tool | Description |
|------|-------------|
| `memory_search` | 고급 검색 (필터, 정렬, 페이징) |
| `memory_list_topics` | 사용자의 토픽 목록 |
| `memory_by_topic` | 토픽별 기억 조회 |

---

## Success Metrics

### For Consumers

| Metric | Target |
|--------|--------|
| Integration complexity | < 10 lines of code |
| Context management code | 0 lines (fully delegated) |
| Recall relevance | > 90% precision@5 |
| Response latency overhead | < 100ms |

### For System

| Metric | Target |
|--------|--------|
| Memory classification accuracy | > 85% |
| Consolidation quality | No information loss |
| Storage efficiency | 10x compression vs raw history |
| Cross-session recall | > 80% relevant retrieval |

---

## Roadmap Implications

### Phase 1: Foundation (Current)
- ✅ Core storage and retrieval
- ✅ Basic embedding service
- ✅ Scoring service

### Phase 2: Hierarchy
- 🔲 Three-tier memory model
- 🔲 Session management
- 🔲 Working memory (in-memory tier)

### Phase 3: Intelligence
- 🔲 LocalAI.Generator integration
- 🔲 Memory classifier
- 🔲 Fact extractor
- 🔲 Memory summarizer

### Phase 4: Consolidation
- 🔲 Automatic consolidation service
- 🔲 Duplicate detection & merging
- 🔲 Importance-based promotion
- 🔲 Decay & cleanup

### Phase 5: Advanced
- 🔲 Topic-based organization
- 🔲 Cross-user knowledge (optional)
- 🔲 Memory graph (entity relationships)

---

## Conclusion

Memory Indexer는 단순한 벡터 저장소가 아닌, **인간의 기억 시스템을 모방한 지능형 메모리 관리 플랫폼**입니다.

핵심 가치:
1. **Zero Context Engineering**: 개발자 부담 제거
2. **Intelligent Automation**: 분류, 배치, 통합 자동화
3. **Hierarchical Memory**: 적절한 계층에 적절한 기억
4. **Seamless Integration**: MCP 기반 표준화된 인터페이스

이를 통해 AI 서비스 개발자는 컨텍스트 윈도우 한계를 걱정하지 않고, 핵심 서비스 로직에 집중할 수 있습니다.
