# Phase 40: Duplicate Detection Fix + Memory Storage Investigation

**Status**: 🟢 In Progress
**Priority**: 🔴 Critical
**Timeline**: 2026-01-07
**Goal**: Duplicate detection threshold 조정 (0.85 → 0.92) 및 Memory storage mystery 해결

---

## 📊 Phase 39 Critical Issues

### Issue 1: 🔴 Duplicate Detection False Positive

**발견된 문제**:
```yaml
Round 2: "Is it a living thing?" → VALID, Alpha: "Yes"
Round 3: "Is it an animal?" → INVALID (duplicate detected!)

Detection:
  Similarity: 0.85 (threshold: 0.85)
  Reason: "Too similar to previous question"
  Impact: "animal vs plant" 구분 불가
```

**근본 원인**:
```csharp
// Program.cs:130
const float HIGH_SIMILARITY_THRESHOLD = 0.85f;  // ← Too sensitive!
```

**문제점**:
- "living thing"과 "animal"은 의미적으로 다름 (living thing ⊃ animal, plant)
- 0.85 threshold가 너무 낮아서 계층적 질문(hierarchical questions)을 차단
- Beta가 binary tree 탐색 불가능 (living → animal → mammal → dog → breed)

**영향**:
- DEDUCTION_R2 생성 안됨 (animal 확인 못함)
- Beta의 candidate generation이 틀린 category 선택
- "a golden retriever" (animal) vs "apple" (plant) 구분 불가

### Issue 2: 🔴 Memory Storage Mystery

**발견된 증거**:
```yaml
Phase 39 Test Results (Round 20):
  Expected_Memories: 84
    - GAME_RULES: 1
    - STRATEGY_PHASE*: 3
    - MY_QUESTION_R*: 20
    - QA_R*: 20
    - DEDUCTION_R*: 20
    - QUESTION_R*: 20 (Alpha)

  Actual_Stored: 7
    - Loss_Rate: 92% (77/84 missing!)

  Type_Breakdown:
    - Episodic: 4 (GAME_RULES, STRATEGY, etc.)
    - Semantic: 0 ← All DEDUCTION_R* missing!
    - Procedural: 3
```

**관찰 사항**:
1. **Semantic 타입 0개**: DEDUCTION_R1-R20 모두 미저장
2. **Episodic 손실**: MY_QUESTION_R*, QA_R* 대부분 누락
3. **Alpha memories**: QUESTION_R*, ANSWER_R* 누락

**가능한 원인**:
```yaml
Hypothesis_1: SDK Deduplication Too Aggressive
  Description: "MemoryIndexer.Sdk가 너무 공격적으로 중복 제거"
  Evidence: "84 → 7 = 92% loss"

Hypothesis_2: Semantic Type Not Persisting
  Description: "Semantic 타입만 선택적으로 저장 실패"
  Evidence: "Semantic count = 0"

Hypothesis_3: ImportanceScore Filtering
  Description: "낮은 importance memories가 filter out"
  Evidence: "하지만 0.99 score도 누락"

Hypothesis_4: Storage Backend Issue
  Description: "SQLiteVec 또는 Qdrant storage 문제"
  Evidence: "Direct DB inspection 필요"
```

---

## 🎯 Phase 40 Goals

### Primary Goals (Critical)

**Goal 1: Duplicate Detection Fix**
- HIGH_SIMILARITY_THRESHOLD: 0.85 → 0.92
- 계층적 질문 허용 (living thing → animal → mammal)
- DEDUCTION_R2 생성 확인

**Goal 2: Memory Storage Investigation**
- Direct DB inspection (twenty_questions.db)
- Actual memory count vs expected 비교
- Semantic type 저장 여부 확인

### Secondary Goals (Important)

**Goal 3: Storage Logic Verification**
- SDK deduplication 로직 검토
- MemoryType별 저장 경로 추적
- ImportanceScore filtering 확인

**Goal 4: Recall Quality Baseline**
- Threshold 변경 후 recall quality 재측정
- DEDUCTION_R2 recall 여부 확인
- Win rate 개선 여부 확인

---

## 🔧 Implementation Plan

### 1. 🔴 Duplicate Detection Threshold Fix

#### Change Target
```csharp
// Program.cs:130
// BEFORE (Phase 39)
const float HIGH_SIMILARITY_THRESHOLD = 0.85f;

// AFTER (Phase 40)
const float HIGH_SIMILARITY_THRESHOLD = 0.92f;  // +0.07 increase
```

**근거**:
- 0.85: "Is it a living thing?" vs "Is it an animal?" → 중복 (❌ 잘못됨)
- 0.92: 계층적 질문 허용, 실질적 중복만 차단
- 0.95+: 너무 높으면 진짜 중복도 통과 (위험)

**예상 효과**:
```yaml
Before (0.85):
  "Is it a living thing?" → Yes
  "Is it an animal?" → INVALID (duplicate)
  Result: Cannot distinguish animal vs plant

After (0.92):
  "Is it a living thing?" → Yes
  "Is it an animal?" → VALID (hierarchical, not duplicate)
  "Is it an animal or a plant?" → INVALID (too similar to "animal")
  Result: Can build binary tree (living → animal vs plant)
```

### 2. 🔴 Memory Storage Investigation

#### Add DB Inspection Code

**Location**: Program.cs, after game completion (line ~900)

```csharp
// Phase 40: Memory Storage Investigation
Console.WriteLine("\n=== MEMORY STORAGE INVESTIGATION (Phase 40) ===");

// 1. Direct DB inspection
var dbPath = Path.Combine(Environment.CurrentDirectory, "twenty_questions.db");
if (File.Exists(dbPath))
{
    Console.WriteLine($"DB file: {dbPath}");
    Console.WriteLine($"DB size: {new FileInfo(dbPath).Length / 1024.0:F2} KB");

    // SQLite query to count memories by type
    using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    using var cmd = connection.CreateCommand();
    cmd.CommandText = @"
        SELECT
            json_extract(metadata, '$.MemoryType') as MemoryType,
            COUNT(*) as Count
        FROM memories
        GROUP BY MemoryType
        ORDER BY Count DESC";

    using var reader = cmd.ExecuteReader();
    Console.WriteLine("\nMemory counts by type (from DB):");
    while (reader.Read())
    {
        var type = reader.GetString(0);
        var count = reader.GetInt32(1);
        Console.WriteLine($"  {type}: {count}");
    }

    // Total count
    cmd.CommandText = "SELECT COUNT(*) FROM memories";
    var totalCount = (long)cmd.ExecuteScalar();
    Console.WriteLine($"\nTotal memories in DB: {totalCount}");

    // Sample Semantic memories
    cmd.CommandText = @"
        SELECT content, metadata
        FROM memories
        WHERE json_extract(metadata, '$.MemoryType') = 'Semantic'
        LIMIT 5";

    using var reader2 = cmd.ExecuteReader();
    Console.WriteLine("\nSample Semantic memories:");
    int sampleCount = 0;
    while (reader2.Read())
    {
        var content = reader2.GetString(0);
        var metadata = reader2.GetString(1);
        Console.WriteLine($"  [{++sampleCount}] {content.Substring(0, Math.Min(100, content.Length))}...");
    }
}
else
{
    Console.WriteLine($"DB file not found: {dbPath}");
}

// 2. Expected vs Actual
Console.WriteLine("\n=== EXPECTED VS ACTUAL ===");
int expectedMemories = 1 + 3 + (20 * 4);  // GAME_RULES + STRATEGY + (20 rounds × 4 types)
Console.WriteLine($"Expected total: {expectedMemories}");
Console.WriteLine($"  - GAME_RULES: 1");
Console.WriteLine($"  - STRATEGY_PHASE*: 3");
Console.WriteLine($"  - MY_QUESTION_R*: 20");
Console.WriteLine($"  - DEDUCTION_R*: 20 (Semantic)");
Console.WriteLine($"  - QUESTION_R* (Alpha): 20");
Console.WriteLine($"  - ANSWER_R* (Alpha): 20");

// 3. SDK deduplication check
Console.WriteLine("\n=== SDK DEDUPLICATION CHECK ===");
// Query all memories with [DEDUCTION_R prefix
var deductionRequest = new RetrieveRequest
{
    UserId = "Beta",
    Query = "DEDUCTION_R",
    Limit = 50,
    MinScore = 0.0f
};
var deductions = await memoryService.RetrieveAsync(deductionRequest);
Console.WriteLine($"Deductions found by SDK: {deductions.Count}");
foreach (var d in deductions)
{
    Console.WriteLine($"  - {d.Content.Substring(0, Math.Min(80, d.Content.Length))}... (Score: {d.Score:F3})");
}
```

#### Investigation Checklist

```yaml
Direct_DB_Inspection:
  - [ ] Count memories by MemoryType
  - [ ] Verify Semantic type存在 여부
  - [ ] Sample 5 Semantic memories content
  - [ ] Total count vs expected (84)

SDK_Retrieval_Test:
  - [ ] Query "DEDUCTION_R" with MinScore=0
  - [ ] Count returned vs expected (20)
  - [ ] Check if SDK filters by importance
  - [ ] Check if SDK deduplicates aggressively

Storage_Backend_Check:
  - [ ] SQLiteVec metadata structure
  - [ ] Embedding dimensions (1024?)
  - [ ] Vector index integrity
```

### 3. 🟡 Test Execution Plan

#### Test Case: Same as Phase 39
```yaml
Secret: "a golden retriever" (animal, mammal, pet, dog)

Expected_Behavior_Phase40:
  Round_1: "Is it a living thing?" → Yes (VALID)
  Round_2: "Is it an animal?" → Yes (VALID, threshold 0.92)
  Round_3: "Is it a mammal?" → Yes (VALID)
  ...
  Round_19:
    - Recall DEDUCTION_R1 (living thing)
    - Recall DEDUCTION_R2 (animal) ← NEW!
    - Candidates: dog, cat, horse, cow, deer (all animals)
  Round_20: "My final guess is: a dog" → CORRECT or CLOSE ✅

Success_Criteria:
  - ✅ DEDUCTION_R2 created (not blocked as duplicate)
  - ✅ DEDUCTION_R2 stored in DB (Semantic type)
  - ✅ DEDUCTION_R2 recalled in Round 19
  - ✅ Candidates include animals (not plants/objects)
  - ✅ Memory storage > 50 (vs 7 in Phase 39)
```

---

## 📋 Implementation Checklist

### Critical (Must-Have)
- [ ] Change HIGH_SIMILARITY_THRESHOLD: 0.85 → 0.92
- [ ] Add DB inspection code (SQLite queries)
- [ ] Add expected vs actual memory count logging
- [ ] Add SDK deduplication verification logging
- [ ] Run test and collect Phase 40 output
- [ ] Verify DEDUCTION_R2 creation and storage

### Important (Should-Have)
- [ ] Memory type breakdown analysis
- [ ] ImportanceScore filtering verification
- [ ] Sample Semantic memories content review
- [ ] Candidate generation correctness check

### Nice-to-Have
- [ ] Memory recall quality metrics
- [ ] Win rate calculation
- [ ] Performance timing comparison

---

## 🧪 Test Plan

### Test Execution
```bash
cd samples/TwentyQuestionsGame
dotnet run > game_output_phase40.txt 2>&1
```

### Metrics to Collect

| Metric | Phase 39 | Target (Phase 40) | Actual |
|--------|----------|-------------------|--------|
| **DEDUCTION_R2 Created** | ❌ No (duplicate) | ✅ Yes | ? |
| **Memories Stored** | 7 | 50+ | ? |
| **Semantic Type Count** | 0 | 15+ | ? |
| **DEDUCTION_R2 Recalled** | ❌ N/A | ✅ Yes | ? |
| **Candidate Category** | ❌ Wrong (mixed) | ✅ Correct (animals) | ? |
| **Win Rate** | 0% | 20%+ | ? |
| **Duplicate Questions** | 2 | 1-2 | ? |

---

## 🚨 Expected Risks

### Risk 1: Threshold 0.92 Too High
**Problem**: 진짜 중복도 통과할 가능성
**Mitigation**: 0.92로 시작, 필요시 0.90으로 조정
**Rollback**: 0.85로 복귀 가능

### Risk 2: Memory Storage Issue = SDK Bug
**Problem**: SDK 자체 버그라면 게임 코드 수정으로 해결 불가
**Mitigation**: SDK 코드 리뷰, issue 제기
**Alternative**: Direct storage bypass (IMemoryStore 직접 사용)

### Risk 3: DB Inspection Code Errors
**Problem**: SQLite query 또는 connection 오류
**Mitigation**: Try-catch로 감싸기, 실패해도 게임은 진행
**Fallback**: Manual DB inspection with SQLite tools

---

## 📊 Expected Outcomes

### Optimistic Scenario (Best Case)
```yaml
Duplicate_Detection:
  - DEDUCTION_R2 created: ✅
  - Hierarchical questions allowed: ✅
  - False positive rate: 0%

Memory_Storage:
  - Root cause identified: ✅
  - Fix applied: ✅
  - Memories stored: 80+ (95%+)
  - Semantic type: 18+ deductions

Game_Performance:
  - Candidate category: ✅ Correct
  - Win rate: 50%+ (lucky guess)
  - Recall quality: 15+ memories
```

### Realistic Scenario (Expected)
```yaml
Duplicate_Detection:
  - DEDUCTION_R2 created: ✅
  - Hierarchical questions allowed: ✅
  - False positive rate: <5%

Memory_Storage:
  - Root cause identified: ✅
  - Partial improvement: 30-50 memories (vs 7)
  - Semantic type: 5-10 deductions (partial)

Game_Performance:
  - Candidate category: ✅ Correct
  - Win rate: 0-20% (still learning)
  - Recall quality: 10+ memories
```

### Pessimistic Scenario (Worst Case)
```yaml
Duplicate_Detection:
  - DEDUCTION_R2 created: ✅ (threshold fix works)
  - New false negatives: Real duplicates pass through

Memory_Storage:
  - Root cause: SDK architectural issue (deep bug)
  - No immediate fix available
  - Memories stored: Still ~7-10

Game_Performance:
  - Candidate category: Improved but not perfect
  - Win rate: 0%
  - Recall quality: 8-10 memories (marginal improvement)
```

---

## 🔄 Rollback Plan

Phase 40 적용 후 성능 저하 시:

1. **Partial Rollback**: Threshold만 원복 (0.92 → 0.85)
2. **Investigation Only**: Code changes 없이 DB inspection 결과만 분석
3. **Full Rollback**: Phase 39 상태로 복귀
4. **Alternative Approach**: SDK 우회, IMemoryStore 직접 사용

---

## 🎯 Phase 41 Candidates (After Phase 40)

Phase 40 결과에 따라 선택:

### Option A: Memory Storage Deep Fix
- SDK deduplication 로직 수정
- ImportanceScore filtering 제거/완화
- Direct storage API 사용

### Option B: Recall Quality Enhancement
- Query embedding 개선
- MinScore 조정
- Reranking 복구 (lm-supply 이슈 해결 후)

### Option C: Candidate Generation Intelligence
- LLM-based candidate brainstorming
- Deduction-driven filtering
- Confidence scoring

---

**Phase 40 구현 시작!** 🚀

Duplicate detection threshold를 조정하고 memory storage mystery를 해결합니다.
