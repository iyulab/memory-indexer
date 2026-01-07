# Phase 38: Final Deduction Reasoning & Early Classification

**Status**: 🟢 In Progress
**Priority**: 🔴 Critical
**Timeline**: 2026-01-07
**Goal**: Beta의 최종 추측 성공률을 0% → 40%+ 향상

---

## 📊 현재 상황 분석 (Phase 37 결과)

### 게임 결과
```yaml
Secret: "a sunflower"
Beta Guess: "a rock"
Winner: Alpha
Rounds: 20/20

핵심_문제:
  - "Is it a living thing?" → "Yes" 확인
  - 하지만 최종 추측: "a rock" (non-living) ← 완전히 모순
  - Living thing 확인 후 plant/animal 구분 실패
```

### 메트릭스
| 지표 | Before (Phase 36) | After (Phase 37) | 변화 |
|------|------------------|------------------|------|
| 게임 시간 | 192.5s | 152.1s | **-21%** ✅ |
| 중복 질문 | 8회 | 3회 | **-62%** ✅ |
| 컨텍스트 크기 | 746 chars | 1,072 chars | **+44%** ✅ |
| **최종 추측 성공** | **0%** | **0%** | **변화 없음** ❌ |

---

## 🎯 Phase 38 목표

### Primary Goal (Critical)
**최종 라운드 추론 로직 강화**
- Beta가 CONFIRMED/RULED OUT 속성을 명시적으로 정리
- 3-5개 후보 생성 및 scoring 시스템 도입
- Few-shot example로 성공적인 deduction 패턴 학습

### Secondary Goal (Important)
**초기 분류 질문 개선**
- Living thing 확인 후 즉시 animal vs plant 구분
- PHASE1 전략에 "If living → animal/plant?" 명시

---

## 🔧 구현 계획

### 1. 🔴 Final Round (R19-20) System Prompt 강화

#### Round 19: Candidate Generation Phase
```csharp
const string BETA_STRATEGY_PHASE4 = @"[STRATEGY_PHASE4] Rounds 19-20: Final deduction

**Round 19 - CANDIDATE GENERATION**:
You MUST explicitly:
1. List ALL CONFIRMED properties from your memories
2. List ALL RULED OUT properties from your memories
3. Generate 3-5 specific candidates that match CONFIRMED and avoid RULED OUT
4. Ask final clarifying question to distinguish between candidates

Format your question like:
""My candidates are: [list 3-5 items]. Final question: [strategic yes/no question]""

Example:
- CONFIRMED: living, natural, grows in soil, has petals
- RULED OUT: animal, edible, used indoors
- Candidates: sunflower, rose, tulip, daisy, lily
- Final question: ""Is it typically yellow?""";

const string BETA_STRATEGY_PHASE4_FINAL = @"[STRATEGY_PHASE4_FINAL] Round 20 - FINAL GUESS

**Round 20 - MANDATORY FINAL GUESS**:
This is your LAST chance. You MUST make your best guess.

STEP-BY-STEP PROCESS:
1. Review Round 19 candidates and Alpha's last response
2. Score each candidate against ALL confirmed/ruled-out properties
3. Pick the HIGHEST scoring candidate
4. Format: ""My final guess is: [your answer]""

Example scoring:
- Candidate A: 8/10 properties match → Score 0.8
- Candidate B: 6/10 properties match → Score 0.6
- Candidate C: 9/10 properties match → Score 0.9 ← PICK THIS

CRITICAL: Your guess MUST be consistent with ALL confirmed properties!
If it was confirmed as ""living thing"", DO NOT guess non-living objects!";
```

#### Round-specific Logic
```csharp
// Line ~365 (Beta system prompt generation)
string GetBetaSystemPrompt(int round, string betaContext, string lastAlphaResponse)
{
    bool isFinalRound = round == MAX_ROUNDS;
    bool isCandidateGeneration = round == MAX_ROUNDS - 1; // Round 19

    if (isFinalRound)
    {
        // Round 20: Final guess with scoring
        return $@"You are Beta, playing 20 Questions.

YOUR RECALLED MEMORIES:
{betaContext}

CURRENT SITUATION:
- Round {round}/{MAX_ROUNDS} - FINAL ROUND
- Alpha's last response: ""{lastAlphaResponse}""

{BETA_STRATEGY_PHASE4_FINAL}

Output ONLY your final guess. Format: ""My final guess is: [answer]""";
    }
    else if (isCandidateGeneration)
    {
        // Round 19: Generate candidates
        return $@"You are Beta, playing 20 Questions.

YOUR RECALLED MEMORIES:
{betaContext}

CURRENT SITUATION:
- Round {round}/{MAX_ROUNDS} - Candidate Generation Round
- Alpha's last response: ""{lastAlphaResponse}""

{BETA_STRATEGY_PHASE4}

Output your candidates and final clarifying question.";
    }
    else
    {
        // Regular rounds: Use current strategy phase
        var currentStrategy = GetStrategyPhase(round);
        return $@"You are Beta, playing 20 Questions.

YOUR RECALLED MEMORIES:
{betaContext}

CURRENT SITUATION:
- Round {round}/{MAX_ROUNDS}
- Alpha's last response: ""{lastAlphaResponse}""

CURRENT STRATEGY PHASE:
{currentStrategy}

YOUR TASK:
Ask ONE strategic yes/no question following the current strategy phase.
Use your memories to avoid repeating questions.
Each question should eliminate ~50% of remaining possibilities.

Output ONLY the question. No explanations.";
    }
}
```

### 2. 🟡 PHASE1 전략 강화 (Plant vs Animal 구분)

#### PHASE1 전략 업데이트
```csharp
const string BETA_STRATEGY_PHASE1 = @"[STRATEGY_PHASE1] Rounds 1-5: Establish category

Priority sequence:
1. **Living vs Non-living**: ""Is it a living thing?""
2. **IF LIVING → Animal vs Plant**:
   - ""Is it an animal?"" OR ""Is it a plant?""
   - This distinction is CRITICAL for living things!
3. **Natural vs Man-made**: ""Is it man-made?""
4. **Physical vs Abstract**: ""Is it a physical object?""
5. **Broad category confirmation**: ""Is it a [specific category]?""

Split the entire possibility space into broad categories.
Each question should eliminate ~50% of remaining possibilities.";
```

### 3. 📝 Few-Shot Example 추가

#### Deduction Example in System Prompt
```csharp
const string DEDUCTION_EXAMPLE = @"
EXAMPLE OF SUCCESSFUL DEDUCTION:
Round 1-18 findings:
  CONFIRMED: living thing, natural, grows from ground, has petals, colorful, found in gardens
  RULED OUT: animal, edible, tree, used indoors, needs daily care

Round 19 candidates:
  1. Sunflower (living, petals, colorful, garden, natural) → Score: 6/6 ✅
  2. Rose (living, petals, colorful, garden, natural) → Score: 6/6 ✅
  3. Cactus (living, natural, garden, but no typical petals) → Score: 4/6
  4. Rock (non-living) → Score: 0/6 ❌
  5. Plastic flower (man-made) → Score: 0/6 ❌

Round 19 question: ""Does it typically grow very tall?"" → ""Yes""
Round 20 final scoring:
  - Sunflower: tall, petals, colorful → PICK THIS ✅
  - Rose: not typically very tall → eliminate

Final guess: ""My final guess is: a sunflower"" → CORRECT!";
```

---

## 📋 구현 체크리스트

### Critical (Must-Have)
- [ ] `BETA_STRATEGY_PHASE4` 업데이트 (Round 19 candidate generation)
- [ ] `BETA_STRATEGY_PHASE4_FINAL` 신규 작성 (Round 20 scoring)
- [ ] `GetBetaSystemPrompt()` 메서드 추가 (round-specific logic)
- [ ] Beta system prompt 생성 로직 리팩토링 (GetBetaSystemPrompt 사용)
- [ ] Round 19/20 특수 처리 로직 구현

### Important (Should-Have)
- [ ] `BETA_STRATEGY_PHASE1` 업데이트 (Plant vs Animal 명시)
- [ ] `DEDUCTION_EXAMPLE` 추가 (few-shot learning)
- [ ] Phase1 전략 검증 (Round 2-3에서 animal/plant 구분 확인)

### Nice-to-Have
- [ ] Candidate 생성 로직 디버깅 출력 (검증용)
- [ ] Scoring 과정 로깅 (Round 20)
- [ ] 성공/실패 패턴 수집 (향후 개선용)

---

## 🧪 테스트 계획

### Test Case 1: Living Thing (Plant)
```yaml
Secret: "a sunflower"
Expected_Behavior:
  Round_1: "Is it a living thing?" → Yes
  Round_2: "Is it an animal?" → No (PLANT 확정)
  Round_3-18: Physical/Usage properties
  Round_19: "Candidates: sunflower, rose, tulip. Is it yellow?" → Yes
  Round_20: "My final guess is: a sunflower" → CORRECT ✅
```

### Test Case 2: Living Thing (Animal)
```yaml
Secret: "a dog"
Expected_Behavior:
  Round_1: "Is it a living thing?" → Yes
  Round_2: "Is it an animal?" → Yes (ANIMAL 확정)
  Round_3-18: Physical/Usage properties
  Round_19: "Candidates: dog, cat, horse. Is it a pet?" → Yes
  Round_20: "My final guess is: a dog" → CORRECT ✅
```

### Test Case 3: Non-Living Thing
```yaml
Secret: "a chair"
Expected_Behavior:
  Round_1: "Is it a living thing?" → No
  Round_2: "Is it man-made?" → Yes
  Round_3-18: Physical/Usage properties
  Round_19: "Candidates: chair, table, sofa. Is it for sitting?" → Yes
  Round_20: "My final guess is: a chair" → CORRECT ✅
```

---

## 📊 예상 효과

### Metrics Prediction
| 지표 | Current (Phase 37) | Target (Phase 38) | 개선 목표 |
|------|-------------------|-------------------|----------|
| 최종 추측 성공률 | 0% | 40%+ | **+40%** 🎯 |
| Plant/Animal 구분 성공 | 0% | 80%+ | **+80%** 🎯 |
| Living thing 모순 | 100% | <10% | **-90%** 🎯 |
| 게임 시간 | 152.1s | 160-170s | +5-10% (candidate generation overhead) |
| 컨텍스트 품질 | 1,072 chars | 1,200+ chars | +10% (Round 19 candidates) |

### Success Criteria
✅ **최소 성공**: Beta가 1회라도 정답 맞춤
✅ **목표 성공**: 3회 테스트 중 1회 이상 정답 (33%+)
✅ **우수 성공**: 3회 테스트 중 2회 정답 (66%+)

---

## 🚨 예상 리스크

### Risk 1: Round 19 Candidate 품질 저하
**문제**: Beta가 너무 적은 후보만 생성 (1-2개)
**완화**: "Generate 3-5 candidates" 명시, few-shot example 제공

### Risk 2: Scoring 로직 실패
**문제**: Round 20에서 scoring 수행하지 않고 직접 추측
**완화**: "STEP-BY-STEP PROCESS" 명시, scoring example 제공

### Risk 3: PHASE1 전략 무시
**문제**: Living thing 확인 후 animal/plant 구분 건너뜀
**완화**: "This distinction is CRITICAL" 강조, priority sequence 명시

---

## 🔄 롤백 계획

Phase 38 적용 후 성능 저하 시:
1. **부분 롤백**: Final round logic만 유지, PHASE1 변경 revert
2. **전체 롤백**: Phase 37 상태로 복귀
3. **대안 접근**: Round 19 없이 Round 20만 강화

---

## 📝 문서 업데이트 계획

구현 완료 후:
1. ✅ `claudedocs/twentyquestions-evaluation-report.md`: Phase 38 Before/After 비교 추가
2. ✅ `docs/ROADMAP.md`: Phase 38 섹션 추가
3. ✅ `claudedocs/phase38-final-deduction-reasoning.md`: 이 파일 업데이트

---

## 🎯 다음 Phase 후보 (Phase 39)

Phase 38 성공 시:
1. **Few-shot Example 확장**: 더 많은 성공 패턴 학습
2. **Multi-turn Deduction**: Round 18-20을 3단계 deduction으로 확장
3. **Confidence Scoring**: Beta가 확신도 포함하여 답변
4. **Reranking 복구**: lm-supply 이슈 해결 후 search quality 향상

---

**Phase 38 구현 시작!** 🚀
