# Phase 37: Memory Recall Quality Improvements

**Status**: 🔄 In Progress
**Priority**: 🔴 Critical
**Based on**: TwentyQuestionsGame Evaluation Report (2026-01-07)

---

## 📋 Phase Overview

### Problem Statement
TwentyQuestionsGame 평가에서 다음 3가지 critical 문제가 발견됨:

1. **중복 질문 반복**: Beta가 "Is it man-made?" 8회 반복 → Recall limit 부족
2. **추론 품질 저하**: 최근 deduction이 recall되지 않음 → Query 불충분
3. **전략 부재**: 용도 확인 질문 누락 → Phase 2/3 전략 부재

### Solution Approach
**4가지 개선사항**을 순차적으로 적용:

1. **Recall Limit 증가**: 15 → 30 (더 많은 이전 질문/추론 포함)
2. **Query 개선**: "previous questions answers latest deductions" 명시
3. **Importance Score 조정**: 질문 0.95+, Deduction 0.95+
4. **전략 고도화**: STRATEGY_PHASE2 (물리적 속성), PHASE3 (용도/목적)

### Expected Outcomes
- **중복 질문 80% 감소**: 8회 → 1-2회
- **추론 recall 품질 50% 향상**: R1만 recall → R1-R19 모두 recall
- **게임 승률 30% → 60% 향상**: 더 논리적이고 효율적인 질문

---

## 🎯 Tasks Breakdown

### Task 1: Recall Limit 증가
**File**: `samples/TwentyQuestionsGame/Program.cs`

**Current**:
```csharp
var betaMemories = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
{
    UserId = BETA_USER_ID,
    SessionId = BETA_SESSION_ID,
    Query = $"game rules strategy previous questions answers deductions round {round}",
    Limit = 15,  // ⚠️ Too low
    MinScore = 0.3f
});
```

**Target**:
```csharp
var betaMemories = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
{
    UserId = BETA_USER_ID,
    SessionId = BETA_SESSION_ID,
    Query = $"game rules strategy previous questions answers latest deductions round {round}",
    Limit = 30,  // ✅ Increased
    MinScore = 0.3f
});
```

**Rationale**:
- Beta 총 메모리: 7개 (게임 종료 시)
- 하지만 라운드마다 저장되므로 중간에 15개 초과 가능
- 30으로 증가하면 대부분 메모리 포함 가능

**Impact**: 중복 질문 50% 감소 예상

---

### Task 2: Query 개선
**File**: `samples/TwentyQuestionsGame/Program.cs`

**Current Query Issues**:
- "deductions" → 너무 일반적, 최근 추론 누락
- "previous questions" → 포함되지만 우선순위 낮음

**Improved Query**:
```csharp
// Beta Recall Query
var betaQuery = $@"
game rules strategy
previous questions asked by me
Alpha's answers yes no maybe
latest deductions confirmed ruled-out properties
round {round}
".Trim().Replace("\n", " ");
```

**Alpha Recall Query**:
```csharp
var alphaQuery = $@"
secret answer
game rules
previous questions from Beta
my answers
duplicate detection history
round {round}
".Trim().Replace("\n", " ");
```

**Rationale**:
- 명시적 키워드로 관련 메모리 우선순위 향상
- "latest deductions" → 최근 추론 강조
- "confirmed ruled-out" → 속성 상태 명확화

**Impact**: 추론 recall 품질 40% 향상 예상

---

### Task 3: Importance Score 조정
**File**: `samples/TwentyQuestionsGame/Program.cs`

**Current**:
```csharp
// Question storage (Beta → Alpha)
await memoryPrimitives.EncodeAsync(new EncodeRequest
{
    UserId = ALPHA_USER_ID,
    SessionId = ALPHA_SESSION_ID,
    Content = $"[QUESTION_R{round}] Beta asked: {betaQuestion}",
    Type = MemoryType.Episodic,
    Scope = Scope.Session,
    Tier = Tier.Long,
    ImportanceScore = 0.8f  // ⚠️ Too low
});
```

**Target**:
```csharp
// Question storage (importance 0.95)
await memoryPrimitives.EncodeAsync(new EncodeRequest
{
    UserId = ALPHA_USER_ID,
    SessionId = ALPHA_SESSION_ID,
    Content = $"[QUESTION_R{round}] Beta asked: {betaQuestion}",
    Type = MemoryType.Episodic,
    Scope = Scope.Session,
    Tier = Tier.Long,
    ImportanceScore = 0.95f  // ✅ High importance
});

// Deduction storage (importance 0.95)
await memoryPrimitives.EncodeAsync(new EncodeRequest
{
    UserId = BETA_USER_ID,
    SessionId = BETA_SESSION_ID,
    Content = deduction,
    Type = MemoryType.Semantic,
    Scope = Scope.Session,
    Tier = Tier.Long,
    ImportanceScore = 0.95f  // ✅ High importance
});
```

**Importance Score Guidelines**:
```
1.0  - Game secret (Alpha only)
0.95 - Questions, Deductions, Answers (critical for game logic)
0.9  - Game rules, Strategy phases
0.8  - Round tracking
0.7  - General observations
```

**Rationale**:
- 질문과 추론은 게임 진행의 핵심 → 0.95로 상향
- Recall 시 상위에 위치하여 누락 방지

**Impact**: 중복 질문 30% 추가 감소, 추론 recall 10% 추가 향상

---

### Task 4: 전략 고도화 (PHASE2/PHASE3)
**File**: `samples/TwentyQuestionsGame/Program.cs`

**Current**:
```csharp
const string BETA_STRATEGY_PHASE1 = @"[STRATEGY_PHASE1] Rounds 1-3: Establish category
- Alive vs non-living
- Natural vs man-made
- Physical object vs place/concept
These questions split the entire possibility space.";
```

**Target**:
```csharp
const string BETA_STRATEGY_PHASE1 = @"[STRATEGY_PHASE1] Rounds 1-5: Establish category
- Alive vs non-living
- Natural vs man-made
- Physical object vs place/concept
Split the entire possibility space into broad categories.";

const string BETA_STRATEGY_PHASE2 = @"[STRATEGY_PHASE2] Rounds 6-12: Physical properties
- Size: hand-held, room-sized, larger?
- Material: metal, plastic, wood, fabric, organic?
- Location: indoor, outdoor, specific room?
- Electronic: requires power, battery, manual?
Narrow down based on physical characteristics.";

const string BETA_STRATEGY_PHASE3 = @"[STRATEGY_PHASE3] Rounds 13-18: Usage and purpose
- Function: what does it do? (tool, furniture, decoration, food, etc.)
- User: who uses it? (everyone, specific profession, children, etc.)
- Frequency: daily use, occasional, rare?
- Necessity: essential, luxury, optional?
Focus on how and why the object is used.";

const string BETA_STRATEGY_PHASE4 = @"[STRATEGY_PHASE4] Rounds 19-20: Final deduction
- Review ALL confirmed and ruled-out properties
- Generate 3-5 candidate objects matching criteria
- Rank by probability based on common objects
- Round 20: MUST make final guess (best candidate)";
```

**Strategy Application**:
```csharp
// During initialization, store all strategy phases
await memoryPrimitives.EncodeAsync(new EncodeRequest
{
    UserId = BETA_USER_ID,
    SessionId = BETA_SESSION_ID,
    Content = BETA_STRATEGY_PHASE1,
    Type = MemoryType.Procedural,
    Scope = Scope.Session,
    Tier = Tier.Long,
    ImportanceScore = 0.9f
});

// Repeat for PHASE2, PHASE3, PHASE4
```

**System Prompt Update**:
```csharp
string betaSystemPrompt = $@"You are Beta, playing 20 Questions.

YOUR RECALLED MEMORIES:
{betaContext}

CURRENT SITUATION:
- Round {round}/{MAX_ROUNDS}
- Alpha's last response: ""{lastAlphaResponse}""

STRATEGIC APPROACH:
{GetStrategyPhase(round)}

INSTRUCTIONS:
1. Review your recalled memories for previous Q&A and deductions
2. Follow the strategic approach for this round range
3. Ask ONE yes/no question that maximizes information gain
4. Avoid duplicate or similar questions (check your memories)
5. Round 20: You MUST make a final guess in format ""My final guess is: [object]""

Generate ONLY your question (no explanation).";
```

**Helper Method**:
```csharp
string GetStrategyPhase(int round)
{
    return round switch
    {
        <= 5 => "PHASE 1: Establish broad category (alive, man-made, physical)",
        <= 12 => "PHASE 2: Identify physical properties (size, material, location, electronic)",
        <= 18 => "PHASE 3: Determine usage and purpose (function, user, frequency, necessity)",
        _ => "PHASE 4: Make final deduction based on all confirmed/ruled-out properties"
    };
}
```

**Rationale**:
- 체계적인 질문 전략으로 효율성 향상
- Phase 3에서 용도 확인 → "Is it edible?" 등의 질문 유도
- Phase 4에서 논리적 추론 → 정답 확률 향상

**Impact**: 게임 승률 30% 향상 예상 (30% → 60%)

---

## 🧪 Testing Plan

### Test Execution
```bash
cd samples/TwentyQuestionsGame
dotnet run --verbosity quiet > game_output_phase37.txt 2>&1
```

### Success Criteria
1. **중복 질문 감소**: "Is it man-made?" 반복 횟수 8회 → 2회 이하
2. **추론 recall 품질**: [DEDUCTION_R1] 외에 R2-R19도 recall됨
3. **전략 실행**: Phase 2/3 질문이 실제로 나옴 (size, material, usage)
4. **게임 승률**: 3회 실행 중 1-2회 승리 (33% → 60%)

### Validation Metrics
```yaml
Metrics to Track:
  duplicate_questions:
    metric: Count of repeated questions
    baseline: 8 (man-made question)
    target: <= 2

  deduction_recall:
    metric: Unique deduction rounds recalled
    baseline: 1 (only R1)
    target: >= 5 (R1-R5 or more)

  strategy_adherence:
    metric: Questions matching phase strategy
    baseline: N/A (no strategy)
    target: >= 70% questions match phase

  win_rate:
    metric: Games won / total games
    baseline: 0% (1 game, lost)
    target: >= 50% (3+ games)
```

---

## 📚 Project Philosophy Alignment

### Core Principles
이 Phase는 다음 프로젝트 철학과 일치:

1. **Evidence-Based Improvements**:
   - 실제 게임 실행 결과 기반 개선
   - 측정 가능한 메트릭으로 검증

2. **User-Centric Design**:
   - 게임 승률 향상 = 사용자 경험 개선
   - 중복 질문 감소 = 더 지능적인 AI 경험

3. **Memory-First Architecture**:
   - Recall 품질이 전체 시스템의 핵심
   - Query, Importance, Limit 튜닝으로 품질 향상

4. **Iterative Refinement**:
   - Phase 1 (Critical) → Phase 2 (Quality) → Phase 3 (Performance)
   - 순차적 개선으로 안정성 확보

### Critical Attitude
**비판적 검토 허용**:

1. **Recall Limit 30이 충분한가?**
   - 🤔 30은 여전히 작을 수 있음
   - ✅ 하지만 Beta 총 메모리 7개 + 여유분 고려하면 적절
   - 📊 테스트 후 필요시 50으로 증가 검토

2. **Query 개선이 실제로 효과적인가?**
   - 🤔 Query 키워드가 embedding에 얼마나 영향?
   - ✅ Semantic search는 키워드에 민감 → 효과 있음
   - 📊 A/B 테스트로 이전 query vs 개선 query 비교

3. **전략 Phase가 LLM에게 실제로 이해되는가?**
   - 🤔 System prompt만으로 충분한가?
   - ✅ GPT-4 계열은 instruction following 우수 → 효과적
   - 📊 실제 질문 패턴 분석으로 검증

4. **Importance Score 조정의 부작용은?**
   - 🤔 모든 메모리 importance가 높으면 의미 없지 않나?
   - ✅ 0.7-1.0 범위로 차등화되므로 여전히 유효
   - 📊 Recall 결과 분석으로 실제 영향 측정

---

## 🔬 Research & Web Search

### Research Questions
이 Phase 수행 전/후 research 필요:

1. **Optimal Recall Limit**:
   - 주제: "optimal number of memories for context window LLM"
   - 도구: Tavily web search
   - 검색어: "RAG retrieval limit optimization 2024"

2. **Query Optimization for Semantic Search**:
   - 주제: "effective query formulation for vector similarity search"
   - 도구: Context7 (academic papers)
   - 검색어: "semantic search query expansion techniques"

3. **Importance Weighting in Retrieval**:
   - 주제: "importance scoring in memory retrieval systems"
   - 도구: Tavily + Context7
   - 검색어: "importance weighting RAG memory systems"

### Research Execution
```bash
# Phase 1 실행 전 research (optional, 시간 여유시)
/sc:research "optimal recall limit for LLM context window with RAG"

# Phase 1 실행 후 분석 (필수)
# - 게임 로그 분석
# - Recall된 메모리 분석
# - 중복 질문 패턴 분석
```

---

## 📦 Deliverables

### Code Changes
1. `samples/TwentyQuestionsGame/Program.cs`:
   - Recall limit: 15 → 30
   - Query 개선 (Beta, Alpha 각각)
   - Importance score: 0.8 → 0.95
   - STRATEGY_PHASE2/PHASE3/PHASE4 추가
   - GetStrategyPhase() helper method

### Documentation Updates
1. `samples/TwentyQuestionsGame/README.md`:
   - Phase 전략 설명 추가
   - Recall limit 변경 사항 반영
   - Importance score guidelines 추가

2. `claudedocs/twentyquestions-evaluation-report.md`:
   - Phase 37 개선사항 적용 결과 추가
   - Before/After 비교 섹션 추가

3. `docs/ROADMAP.md`:
   - Phase 37 추가 (✅ Complete 또는 🔄 In Progress)

### Test Results
1. `samples/TwentyQuestionsGame/game_output_phase37.txt`:
   - 개선 후 게임 실행 로그
   - 중복 질문, 추론 recall, 전략 실행 검증

2. `claudedocs/phase37-test-results.md`:
   - Before/After 메트릭 비교
   - Success criteria 충족 여부
   - 추가 개선사항 도출

---

## 🚀 Next Steps (After Phase 37)

### Phase 38: Reranking Recovery (Priority: 🟡 Important)
**Goal**: ONNX Runtime 문제 해결 또는 대안 구현

**Options**:
1. LMSupply.Reranker 업데이트 → 최신 버전 테스트
2. OpenAI embedding-based reranking 구현
3. Fallback mechanism (reranking 실패 시 similarity 정렬)

**Expected Impact**: Recall 품질 20% 추가 향상

### Phase 39: Few-Shot Learning (Priority: 🟡 Important)
**Goal**: 성공적인 게임 패턴을 학습 데이터로 활용

**Tasks**:
1. 10-20개 성공적인 게임 전사 수집
2. System prompt에 few-shot examples 추가
3. 효과적인 질문 패턴 학습

**Expected Impact**: 승률 60% → 75% 향상

### Phase 40: Performance Optimization (Priority: 🟢 Nice-to-Have)
**Goal**: 게임 시간 단축

**Tasks**:
1. Recall + LLM 병렬화
2. Embedding cache 구현
3. Batch processing

**Expected Impact**: 게임 시간 192초 → 120초 (37% 단축)

---

**Created**: 2026-01-07
**Author**: Claude (Sonnet 4.5)
**Version**: v0.3.0
**Status**: 🔄 In Progress
