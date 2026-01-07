# Phase 39: Memory Recall Quality Recovery

**Status**: 🟢 In Progress
**Priority**: 🔴 Critical
**Timeline**: 2026-01-07
**Goal**: Beta의 memory recall quality를 회복하여 승률 0% → 20%+ 달성

---

## 📊 Phase 38 문제점 분석

### 심각한 Recall Quality 저하

**Phase 37 vs Phase 38 비교**:

| 지표 | Phase 37 | Phase 38 | 변화 |
|------|----------|----------|------|
| **평균 컨텍스트 (Beta)** | 1,072 chars | 841 chars | **-22%** ❌ |
| **Recall된 메모리** | ~7-8개 | 6개 | -15% ❌ |
| **중복 질문** | 3회 | 4회 | +33% ❌ |
| **최종 추측 성공** | 0% | 0% | 변화 없음 ❌ |

### Beta's Round 19 Recall 상세 분석

```yaml
Recalled (6 memories):
  - [0.94] GAME_RULES
  - [0.84] STRATEGY_PHASE1
  - [0.84] DEDUCTION_R1  ← R1만!
  - [0.83] ROUND
  - [0.81] DEDUCTION_R7
  - [0.81] QA_R10

Missing (대부분):
  - DEDUCTION_R2: "NOT man-made" ← 가장 중요!
  - DEDUCTION_R3, R4, R5, R6, R8, R9, ... R18
  - MY_QUESTION_R2-R18 대부분

Result:
  - Candidates: key, coin, smartphone, pen, spoon (모두 man-made)
  - 정답: a red apple (natural, NOT man-made) ← 완전히 틀린 category!
```

### 근본 원인

#### 1. Recall Query가 너무 Vague
```csharp
// 현재 Beta Recall (Line 345-353)
Query = $"previous questions and deductions from rounds {round}",
Limit = 30  // ← 설정은 30인데 6개만 return!
```

**문제**: "previous questions and deductions"가 너무 일반적
- Embedding이 구체적인 deduction과 잘 매칭되지 않음
- Round number 언급이 효과가 없음

#### 2. ImportanceScore 부족
```csharp
// Questions & Deductions (Phase 37-38)
ImportanceScore = 0.95f  // ← GAME_RULES (1.0)보다 낮음
```

**문제**: GAME_RULES, STRATEGY 등이 우선순위에서 밀림
- 실제 게임 진행 내용보다 static rules가 더 높은 score

#### 3. Deduction 저장 형식
```csharp
// 현재 Deduction 저장 (Line 635-641)
Content = $"[DEDUCTION_R{round}] " +
          (alphaAnswer == "Yes"
              ? "CONFIRMED: The secret HAS the property..."
              : "RULED OUT: The secret does NOT have the property...")
```

**문제**: 속성 자체(man-made, living 등)가 명시되지 않음
- "the property asked in 'Is it man-made?'" 형태로 간접적
- Embedding이 "man-made"를 직접 찾기 어려움

---

## 🎯 Phase 39 목표

### Primary Goal (Critical)
**Memory Recall Quality를 Phase 37 수준 이상으로 회복**
- Beta가 R2-R18 deduction을 정상적으로 recall
- 평균 컨텍스트 841 chars → 1,200+ chars
- Recalled memories 6개 → 15+ 개

### Secondary Goal (Important)
**Candidate Generation이 올바른 Category 선택**
- "NOT man-made" deduction을 recall
- Natural objects를 candidates에 포함
- 최종 추측 승률 0% → 20%+

---

## 🔧 구현 계획

### 1. 🔴 Recall Query 명시화

#### Beta Recall Query 개선
```csharp
// Before (Phase 38)
Query = $"previous questions and deductions from rounds {round}",

// After (Phase 39)
Query = $"my questions, Alpha's answers, and deductions from all previous rounds up to round {round - 1}",
```

**개선 효과**:
- "my questions" → MY_QUESTION_R* 직접 매칭
- "Alpha's answers" → ANSWER_R* 매칭
- "deductions" → DEDUCTION_R* 매칭
- "all previous rounds up to round X" → 범위 명확화

#### Alpha Recall Query 개선
```csharp
// Before (Phase 38)
Query = $"previous questions and duplicate detection from rounds {round}",

// After (Phase 39)
Query = $"Beta's questions and my answers from all previous rounds up to round {round - 1}",
```

### 2. 🔴 ImportanceScore 상향

```csharp
// Phase 38 → Phase 39
ImportanceScore 변경:

// Beta Questions
0.95f → 0.98f  // +0.03

// Beta Deductions
0.95f → 0.99f  // +0.04 (가장 중요!)

// Alpha Questions
0.95f → 0.98f  // +0.03

// Alpha Answers
0.95f → 0.96f  // +0.01 (적당히)
```

**근거**:
- GAME_RULES: 1.0 (static, 게임 시작 시 한번만 필요)
- **DEDUCTION: 0.99** (dynamic, 매 라운드 필수!)
- **QUESTION: 0.98** (dynamic, 중복 방지 필수)
- ANSWER: 0.96 (참고용)
- STRATEGY: 0.85 (필요시)

### 3. 🟡 Recall Limit 증가

```csharp
// Phase 38 → Phase 39
Recall Limit 변경:

// Beta Recall
Limit = 30 → Limit = 50  // +67%

// Alpha Recall
Limit = 30 → Limit = 50  // +67%
```

**근거**:
- 현재 6개만 recall되는 상황
- Round 19 시점: 18개 questions + 18개 deductions + rules/strategy = 40+ memories
- Limit 50으로 충분히 확보

### 4. 🟡 Deduction Format 개선

#### Before (Phase 38)
```csharp
Content = $"[DEDUCTION_R{round}] " +
          (alphaAnswer == "Yes"
              ? "CONFIRMED: The secret HAS the property asked in '{betaQuestion}'"
              : "RULED OUT: The secret does NOT have the property asked in '{betaQuestion}'")
```

**문제**: "the property asked in 'Is it man-made?'" → 간접적

#### After (Phase 39)
```csharp
// Extract property from question
string property = ExtractPropertyFromQuestion(betaQuestion);

Content = $"[DEDUCTION_R{round}] " +
          (alphaAnswer == "Yes"
              ? $"CONFIRMED: {property} - Alpha said 'Yes' to '{betaQuestion}'"
              : $"RULED OUT: NOT {property} - Alpha said 'No' to '{betaQuestion}'")
```

**예시**:
```
Before: [DEDUCTION_R2] RULED OUT: The secret does NOT have the property asked in 'Is it man-made?'
After:  [DEDUCTION_R2] RULED OUT: NOT man-made - Alpha said 'No' to 'Is it man-made?'
```

**개선 효과**:
- "NOT man-made" 직접 embedding → recall 시 "man-made" query와 잘 매칭
- 더 명시적이고 scan하기 쉬움

### 5. 📝 ExtractPropertyFromQuestion() Helper

```csharp
string ExtractPropertyFromQuestion(string question)
{
    // Simple heuristic extraction
    // "Is it X?" → "X"
    // "Does it have X?" → "X"
    // "Can it X?" → "X"

    var patterns = new[]
    {
        @"Is it (a |an )?(.*)\?",           // "Is it man-made?" → "man-made"
        @"Does it (have |contain )?(.*)\?", // "Does it have wheels?" → "wheels"
        @"Can it (.*)\?",                   // "Can it fly?" → "fly"
        @"Is it used (.*)\?",               // "Is it used indoors?" → "used indoors"
    };

    foreach (var pattern in patterns)
    {
        var match = Regex.Match(question, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[match.Groups.Count - 1].Value.Trim();
        }
    }

    // Fallback: use the full question
    return question.Replace("?", "").Trim();
}
```

---

## 📋 구현 체크리스트

### Critical (Must-Have)
- [ ] Beta Recall query 개선 (명시적 표현)
- [ ] Alpha Recall query 개선 (명시적 표현)
- [ ] ImportanceScore 상향 (Questions 0.98, Deductions 0.99)
- [ ] Recall limit 증가 (30 → 50)
- [ ] Deduction format 개선 (property 명시)
- [ ] ExtractPropertyFromQuestion() 구현

### Important (Should-Have)
- [ ] Round 19/20 deduction recall 검증 로깅
- [ ] Recalled memories 출력 강화 (디버깅용)
- [ ] Phase 39 게임 결과 기록

### Nice-to-Have
- [ ] Recall quality 메트릭 추가
- [ ] Deduction 누락 패턴 분석

---

## 🧪 테스트 계획

### Test Case 1: Natural Object (Phase 38 실패 케이스)
```yaml
Secret: "a red apple" (natural, NOT man-made, edible)

Expected_Behavior:
  Round_2: "Is it man-made?" → No
  Round_3: DEDUCTION_R2 recall 확인
  Round_19:
    - DEDUCTION_R2 ("NOT man-made") 포함
    - Candidates: apple, orange, flower, stone, wood (natural objects)
  Round_20: "My final guess is: a red apple" → CORRECT ✅

Success_Criteria:
  - ✅ Beta recalls 15+ memories (vs 6 in Phase 38)
  - ✅ DEDUCTION_R2-R18 대부분 포함
  - ✅ Candidates에 natural objects 포함
  - ✅ 정답 맞춤 (승률 100% for this case)
```

### Test Case 2: Man-made Object
```yaml
Secret: "a smartphone"

Expected_Behavior:
  Round_2: "Is it man-made?" → Yes
  Round_19:
    - DEDUCTION_R2 ("man-made") 포함
    - Candidates: smartphone, laptop, watch, camera, calculator
  Round_20: "My final guess is: a smartphone" → CORRECT ✅
```

### Test Case 3: Living Thing (Plant)
```yaml
Secret: "a sunflower"

Expected_Behavior:
  Round_1: "Is it a living thing?" → Yes
  Round_2: "Is it an animal?" → No (PLANT 확정)
  Round_19:
    - DEDUCTION_R1 ("living thing"), R2 ("NOT animal") 포함
    - Candidates: sunflower, rose, tulip, tree, grass
  Round_20: "My final guess is: a sunflower" → CORRECT ✅
```

---

## 📊 예상 효과

### Metrics Prediction

| 지표 | Phase 38 | Target (Phase 39) | 개선 목표 |
|------|----------|-------------------|----------|
| **평균 컨텍스트 (Beta)** | 841 chars | 1,200+ chars | **+43%** 🎯 |
| **Recalled memories** | 6개 | 15+ 개 | **+150%** 🎯 |
| **DEDUCTION_R2-R18 포함** | 2/18 (11%) | 15/18 (83%+) | **+655%** 🎯 |
| **중복 질문** | 4회 | 2회 이하 | **-50%** 🎯 |
| **최종 추측 성공률** | 0% | 33%+ (1/3 tests) | **+33%** 🎯 |
| **게임 시간** | 152.3s | 160-170s | +5-10% (더 많은 recall) |

### Success Criteria

✅ **최소 성공**: Beta가 DEDUCTION_R2 recall
✅ **목표 성공**: 3회 테스트 중 1회 이상 정답 (33%+)
✅ **우수 성공**: 3회 테스트 중 2회 정답 (66%+)
✅ **완벽 성공**: 3회 테스트 모두 정답 (100%)

---

## 🚨 예상 리스크

### Risk 1: Recall Limit 50이 너무 많음
**문제**: 너무 많은 memories로 LLM context overflow
**완화**: 실제 recall 결과 모니터링, 필요시 40으로 조정

### Risk 2: ExtractPropertyFromQuestion() 오작동
**문제**: Property 추출이 잘못되어 deduction이 이상하게 저장
**완화**: Regex pattern 충분히 테스트, fallback logic 제공

### Risk 3: ImportanceScore 변경의 부작용
**문제**: GAME_RULES보다 DEDUCTION이 높아져서 static rules가 누락
**완화**: GAME_RULES를 1.0으로 유지 (최고 우선순위)

---

## 🔄 롤백 계획

Phase 39 적용 후 성능 저하 시:
1. **부분 롤백**: ImportanceScore만 원복 (0.98/0.99 → 0.95)
2. **Limit 조정**: 50 → 40 → 30 단계적 조정
3. **전체 롤백**: Phase 38 상태로 복귀
4. **대안 접근**: Query 개선만 적용, format 변경 제외

---

## 📝 문서 업데이트 계획

구현 완료 후:
1. ✅ `claudedocs/twentyquestions-evaluation-report.md`: Phase 39 Before/After 비교 추가
2. ✅ `docs/ROADMAP.md`: Phase 39 섹션 추가
3. ✅ `claudedocs/phase39-memory-recall-recovery.md`: 이 파일 업데이트

---

## 🎯 Phase 40 후보

Phase 39 성공 시 다음 개선 방향:

### Option A: Reranking 복구
- lm-supply 이슈 해결 후 search quality 향상
- Cross-encoder re-ranking으로 recall precision 개선

### Option B: Multi-round Deduction
- Round 18-20을 3단계 deduction으로 확장
- Intermediate deduction in R18, Candidates in R19, Final in R20

### Option C: Confidence Scoring
- Beta가 각 candidate에 대해 confidence 점수 부여
- "80% confident it's an apple" 형태로 추측

---

**Phase 39 구현 시작!** 🚀

Memory recall quality를 회복하여 Beta가 드디어 정답을 맞출 수 있도록 합니다.
