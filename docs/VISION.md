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
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐    │
│  │ Recently │  │ Working  │  │ Session  │  │   User   │    │
│  │ (Buffer) │  │ (Active) │  │(Archive) │  │(Profile) │    │
│  │          │  │          │  │          │  │          │    │
│  │ Raw 대화  │  │ 토픽그룹  │  │ 세션요약  │  │ 장기사실  │    │
│  │ 스테이징 │  │ 활성맥락  │  │ 압축저장  │  │ 프로필   │    │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘    │
│       │             │             │             │          │
│       └─────────────┴─────────────┴─────────────┘          │
│                          │                                  │
│              ┌───────────▼───────────┐                      │
│              │   Intelligent Recall  │                      │
│              │   (Context Assembly)  │                      │
│              └───────────┬───────────┘                      │
│                          │                                  │
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

### 4-Tier Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                 TIER 0: Recently Buffer                      │
│                 (Raw Conversation Staging)                   │
├─────────────────────────────────────────────────────────────┤
│  Scope:     Raw conversation text, full detail              │
│  Lifetime:  60 seconds idle OR 500 tokens OR 3 turns        │
│  Capacity:  Unlimited (staging area)                        │
│  Purpose:   Async processing, immediate response            │
│  Promotion: OR logic (any trigger fires)                    │
│                                                              │
│  Example:                                                    │
│  - 방금 입력된 사용자 메시지 전문                            │
│  - 대화 진행 중인 상세 컨텍스트                              │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼ BufferPromoter
┌─────────────────────────────────────────────────────────────┐
│                 TIER 1: Working Memory                       │
│                 (Active Context)                             │
├─────────────────────────────────────────────────────────────┤
│  Scope:     Topic-grouped, summarized chunks                 │
│  Lifetime:  10 min OR 2K tokens OR 10 turns OR topic_change │
│  Capacity:  ~4-7 items (Baddeley's Working Memory Model)    │
│  Purpose:   Current task context, topic coherence           │
│  Promotion: OR logic (aggressive buffer cleanup)            │
│                                                              │
│  Example:                                                    │
│  - "현재 토픽: API 설계 논의"                               │
│  - "핵심 포인트: REST vs GraphQL 비교 중"                   │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼ WorkingMemoryOrchestrator
┌─────────────────────────────────────────────────────────────┐
│                 TIER 2: Session Memory                       │
│                 (Archived Sessions)                          │
├─────────────────────────────────────────────────────────────┤
│  Scope:     Session summaries, extracted facts               │
│  Lifetime:  Duration of session (hours to days)              │
│  Capacity:  Hundreds of memories per session                 │
│  Purpose:   Session coherence, topic continuity              │
│  Storage:   Vector DB (Qdrant/SQLite-vec)                   │
│                                                              │
│  Example:                                                    │
│  - 세션 초반에 논의한 프로젝트 요구사항                       │
│  - 이 세션에서 사용자가 선택한 옵션들                        │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼ AND logic promotion
┌─────────────────────────────────────────────────────────────┐
│                 TIER 3: User Profile                         │
│                 (Long-term Dictionary)                       │
├─────────────────────────────────────────────────────────────┤
│  Scope:     Cross-session persistent facts                   │
│  Lifetime:  Permanent (until explicitly deleted)             │
│  Capacity:  ~500 entries per user                           │
│  Purpose:   Personalization, user understanding              │
│  Promotion: Confidence >= 0.8 AND Confirmations >= 3        │
│                                                              │
│  Example:                                                    │
│  - "사용자는 Python보다 TypeScript를 선호함" (확신도 0.9)    │
│  - "사용자의 프로젝트는 e-commerce 플랫폼" (3회 확인됨)      │
│  - "사용자는 간결한 설명을 좋아함" (확신도 0.85)             │
└─────────────────────────────────────────────────────────────┘
```

### Memory Types (Orthogonal to Tiers)

| Type | Description | Example | Typical Tier |
|------|-------------|---------|--------------|
| **Episodic** | 특정 사건/대화의 기억 | "어제 API 에러 해결함" | Session |
| **Semantic** | 일반화된 사실/지식 | "사용자는 React 개발자" | User |
| **Procedural** | 수행 방법에 대한 기억 | "이 프로젝트는 pnpm 사용" | User |
| **Fact** | 명시적으로 저장된 사실 | "생일: 3월 15일" | User |

### User Profile Categories

| Category | Description | Example |
|----------|-------------|---------|
| **Fact** | 일반적 사실 | "개발자, 서울 거주" |
| **Preference** | 설정/선호도 | "다크모드 선호" |
| **Skill** | 기술/전문성 | "Python, React 숙련" |
| **Interest** | 관심사/취미 | "오픈소스 기여 활동" |
| **Relationship** | 관계 정보 | "팀 리더, 멘토 역할" |
| **Work** | 업무 맥락 | "스타트업 CTO" |
| **Goal** | 목표/계획 | "올해 AI 프로젝트 완성" |
| **Behavior** | 행동 패턴 | "코드 리뷰 꼼꼼함" |
| **Communication** | 소통 스타일 | "간결한 설명 선호" |

---

## Intelligent Processing

### Async Processing Pipeline

```
[User Input]
     │
     ▼
[Recently Buffer] ←─── Immediate storage (sync, <10ms)
     │
     ├─── Trigger check (async)
     │         │
     │         ▼
     │    [BufferPromoter]
     │         │
     │         ├─ Topic segmentation
     │         ├─ Entity extraction
     │         └─ Importance scoring
     │
     ▼
[Working Memory] ←─── Topic-grouped summaries
     │
     ├─── Promotion trigger (async)
     │         │
     │         ▼
     │    [WorkingMemoryOrchestrator]
     │         │
     │         ├─ Extractive summarization
     │         └─ Fact extraction
     │
     ▼
[Session Archive] ←─── Session summaries + facts
     │
     ├─── AND logic filter (async)
     │         │
     │         ▼
     │    [Confirmation Check]
     │         │
     │         ├─ Confidence >= 0.8?
     │         └─ Confirmations >= 3?
     │
     ▼
[User Profile] ←─── Structured dictionary
```

### Multi-Signal Promotion Triggers

| Transition | Signal | Threshold | Logic |
|------------|--------|-----------|-------|
| Recently → Working | Idle | 60 seconds | OR |
| | Tokens | 500 accumulated | OR |
| | Turns | 3 conversation turns | OR |
| Working → Session | Idle | 10 minutes | OR |
| | Tokens | 2000 in working | OR |
| | Turns | 10 turns same topic | OR |
| | Topic | Change detected | OR |
| Session → User | Confidence | >= 0.8 score | **AND** |
| | Confirmations | >= 3 times | **AND** |

**Design Principle**:
- **Lower promotions**: OR logic — 빠른 버퍼 정리
- **Upper promotion**: AND logic — 신중한 장기 저장

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
│  │ Recently Buffer → (비어있음 - 새 세션)                   ││
│  │ Working Memory  → (현재 세션에 관련 내용 없음)           ││
│  │ Session Memory  → "어제 API 인증 에러 논의함"            ││
│  │ User Profile    → "개발자, TypeScript 선호"              ││
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
│  "사용자 프로필: TypeScript 개발자, e-commerce 프로젝트      │
│   관련 기억: 어제 API 인증 에러를 JWT 토큰 갱신으로 해결    │
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

## Conflict Resolution (User Profile)

```yaml
conflict_strategy:
  same_key_update:
    rule: "Latest wins with version history"
    example:
      T1: user.coffee_preference = "loves coffee" (v1)
      T2: user.coffee_preference = "quit coffee" (v2, current)
    retention: "Keep last 3 versions for context"

  contradicting_facts:
    rule: "Higher confidence wins, flag for review if close"
    threshold: 0.1 confidence difference
    example:
      fact1: "vegetarian" (confidence: 0.9)
      fact2: "ate steak yesterday" (confidence: 0.8)
      action: "Flag contradiction, keep both with notes"

  confirmation_boost:
    rule: "Each confirmation boosts confidence by 0.1"
    cap: 1.0 maximum confidence
    example:
      initial: confidence 0.5, confirmations 1
      after_2_confirmations: confidence 0.7, confirmations 3
      status: "IsConfirmed = true (>= 3 confirms AND >= 0.8 confidence)"
```

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

| Metric | Target | Status |
|--------|--------|--------|
| Memory classification accuracy | > 85% | ✅ Achieved |
| Consolidation quality | No information loss | ✅ Achieved |
| Storage efficiency | 10x compression vs raw | ✅ Achieved |
| Cross-session recall | > 80% relevant retrieval | ✅ Achieved |
| Test coverage | > 500 tests | ✅ 504 tests |

---

## Implementation Status

### Completed Phases

- ✅ **Phase 1-6**: Foundation, Intelligence, Advanced Features
- ✅ **Phase 8-13**: Temporal KG, Consolidation, Operations, Summarization
- ✅ **Phase 14**: 4-Tier Memory Architecture
  - Recently Buffer (Tier 0)
  - Working Memory with BufferPromoter
  - Session Memory with WorkingMemoryOrchestrator
  - User Profile with AND logic promotion

### Planned Phases

- 🔲 **Phase 15**: Smart Tiered Retrieval
- 🔲 **Phase 16**: Graph-based Memory Network
- 🔲 **Phase 17**: Self-Directed Memory Management
- 🔲 **Phase 18**: Production & Ecosystem

---

## Conclusion

Memory Indexer는 단순한 벡터 저장소가 아닌, **인간의 기억 시스템을 모방한 지능형 메모리 관리 플랫폼**입니다.

핵심 가치:
1. **Zero Context Engineering**: 개발자 부담 제거
2. **Intelligent Automation**: 분류, 배치, 통합 자동화
3. **4-Tier Hierarchy**: 적절한 계층에 적절한 기억
4. **Seamless Integration**: MCP 기반 표준화된 인터페이스

이를 통해 AI 서비스 개발자는 컨텍스트 윈도우 한계를 걱정하지 않고, 핵심 서비스 로직에 집중할 수 있습니다.
