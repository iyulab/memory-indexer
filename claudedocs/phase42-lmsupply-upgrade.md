# Phase 42: LMSupply Upgrade for ONNX Crash Fix

**Date**: 2026-01-07
**Branch**: refactor/cognitive-terminology-v0.4
**Related Issue**: lm-supply ONNX Runtime segfault (fixed in v0.8.5)

---

## 🎯 Objective

Upgrade LMSupply packages from v0.8.3 to v0.8.5 to resolve ONNX Runtime segfault that may be causing 82% memory loss in TwentyQuestionsGame.

---

## 📊 Current Status

### Memory Loss Problem (Phase 41 Discovery)
- **Expected**: 84 conversation memories
- **Actual**: 15 memories stored (17.9% retention)
- **Loss**: 69 memories (82% loss)

### LMSupply Version Status
| Package | Current | Latest | Status |
|---------|---------|--------|--------|
| LMSupply.Embedder | 0.8.3 | 0.8.5 | ⚠️ Outdated |
| LMSupply.Reranker | 0.8.3 | 0.8.5 | ⚠️ Outdated |
| LMSupply.Generator | 0.8.3 | 0.8.5 | ⚠️ Outdated |

---

## 🐛 Known Issues in v0.8.3

### Issue 1: ONNX Runtime Segfault
**Cause**: Synchronous `Create()` methods bypass RuntimeManager initialization
```
Create() [sync] → OnnxSessionFactory.Create() → No RuntimeManager init → SEGFAULT
CreateAsync() [async] → RuntimeManager.InitializeAsync() → Downloads native binaries → ✅ Works
```

**Impact on memory-indexer**:
- Embedding generation may crash silently
- Reranker operations may fail
- Memory storage fails when embeddings can't be created
- **Potential root cause of 82% memory loss**

### Issue 2: Model Name Recognition
**Problem**: `"bge-reranker-base"` not recognized (only alias `"quality"` worked)
- Fixed in v0.8.5 with `ModelRegistry._modelsByName` dictionary

---

## 🔧 Changes Required

### 1. Update Directory.Packages.props
```xml
<!-- Before (v0.8.3) -->
<PackageVersion Include="LMSupply.Embedder" Version="0.8.3" />
<PackageVersion Include="LMSupply.Reranker" Version="0.8.3" />
<PackageVersion Include="LMSupply.Generator" Version="0.8.3" />

<!-- After (v0.8.5) -->
<PackageVersion Include="LMSupply.Embedder" Version="0.8.5" />
<PackageVersion Include="LMSupply.Reranker" Version="0.8.5" />
<PackageVersion Include="LMSupply.Generator" Version="0.8.5" />
```

### 2. Verification Steps
1. Clean build: `dotnet clean && dotnet build`
2. Run tests: `dotnet test` (verify 504 tests still pass)
3. Delete TwentyQuestionsGame DB: `rm samples/TwentyQuestionsGame/twenty_questions.db`
4. Rerun game with fresh DB
5. Measure memory retention improvement

---

## 📈 Expected Results

### Hypothesis
If ONNX crashes were causing memory loss:
- **Before**: 15/84 memories (17.9% retention)
- **After**: 60-70/84 memories (71-83% retention) ✅

### Alternative Outcome
If memory loss persists after upgrade:
- ONNX crashes were NOT the root cause
- Proceed to Phase 42c: Deduplication/VCM investigation

---

## ✅ Success Criteria

1. ✅ LMSupply packages upgraded to v0.8.5
2. ✅ All 504 tests pass
3. ✅ TwentyQuestionsGame runs without crashes
4. ✅ Memory retention measured and compared
5. ✅ Root cause identified (ONNX vs other factors)

---

## 📝 Implementation Plan

### Phase 42a: Upgrade
- [ ] Update Directory.Packages.props
- [ ] Run `dotnet restore`
- [ ] Run `dotnet build`
- [ ] Run `dotnet test` (verify 504 tests)

### Phase 42b: Verification
- [ ] Delete `twenty_questions.db`
- [ ] Run TwentyQuestionsGame
- [ ] Count stored memories by type
- [ ] Compare retention rate (before: 17.9%)

### Phase 42c: Analysis
- [ ] If improved: Document ONNX crash as root cause
- [ ] If not improved: Investigate deduplication logic
- [ ] Update evaluation report
- [ ] Update ROADMAP

---

## 🔍 Related Files

**Package Management**:
- `Directory.Packages.props:54-56`

**TwentyQuestionsGame**:
- `samples/TwentyQuestionsGame/Program.cs`
- `samples/TwentyQuestionsGame/twenty_questions.db` (delete before rerun)

**Documentation**:
- `claudedocs/twentyquestions-evaluation-report.md`
- `docs/ROADMAP.md`

---

## 📊 Test Results

### Before Upgrade (v0.8.3)
```
Stored Memories: 15/84 (17.9%)
├─ Episodic: 10 (66.7%)
├─ Procedural: 4 (26.7%)
└─ Semantic: 1 (6.7%)

Loss: 69 memories (82%)
```

### After Upgrade (v0.8.5)
```
Stored Memories: 14/84 (16.7%)
├─ Episodic: 9 (64.3%)
├─ Procedural: 4 (28.6%)
└─ Semantic: 1 (7.1%)

Loss: 70 memories (83.3%)
Change from Phase 41: -1 memory (-6.7%)
```

**⚠️ UNEXPECTED RESULT**: LMSupply upgrade did NOT improve memory retention!

---

## 🔗 References

- **lm-supply Issue**: `D:\data\lm-supply\claudedocs\issue-response-reranker-onnx-crash.md`
- **LMSupply v0.8.5 Release**: Fixes synchronous API ONNX crash
- **Phase 41 Analysis**: `claudedocs/phase41-memorytype-metadata-fix.md`

---

## Status

**Phase 42a**: ✅ LMSupply upgraded to v0.8.5 (all 1015 tests pass)
**Phase 42b**: ✅ TwentyQuestionsGame rerun complete
**Phase 42c**: ⚠️ **Analysis required - memory loss NOT caused by ONNX crashes**

---

## 🔍 Root Cause Analysis

### Hypothesis Status: ❌ REJECTED

**Initial Hypothesis**: ONNX Runtime segfault in LMSupply v0.8.3 causing memory storage failures

**Test Results**:
- Upgraded to LMSupply v0.8.5 (ONNX crash fixed)
- Memory retention: **14/84 (16.7%)** vs Phase 41: 15/84 (17.9%)
- **Conclusion**: ONNX crashes were NOT the root cause

### Statistical Analysis

| Metric | Phase 41 (v0.8.3) | Phase 42 (v0.8.5) | Change |
|--------|-------------------|-------------------|--------|
| Total Memories | 15 | 14 | -1 (-6.7%) |
| Episodic | 10 (66.7%) | 9 (64.3%) | -1 |
| Procedural | 4 (26.7%) | 4 (28.6%) | 0 |
| Semantic | 1 (6.7%) | 1 (7.1%) | 0 |
| Loss Rate | 82.1% | 83.3% | +1.2% |

**Interpretation**:
- Variance of -1 memory is within statistical noise (84 conversations)
- No significant improvement from ONNX crash fix
- **Memory loss is NOT related to embedding generation failures**

### Eliminated Causes

✅ **MemoryType Serialization** (Phase 41): SDK correctly stores type as INTEGER column
✅ **ONNX Runtime Crashes** (Phase 42): LMSupply upgrade shows no impact
✅ **Embedding Generation Failures**: Would have shown dramatic improvement if this was the cause

### Remaining Suspects

🔍 **Deduplication Logic**:
- `MemoryPrimitivesService.EncodeAsync` may be too aggressive
- `IsSimilarContent` threshold needs investigation
- Possibly conflating similar but distinct conversations

🔍 **VCM Tier Transitions**:
- **Recently Buffer → Working Memory**: May expire before promotion
- **Working Memory Capacity**: 4-7 limit may be too restrictive
- **Working → Session**: Promotion conditions may be too strict

🔍 **Storage Backend**:
- SqliteVecMemoryStore.StoreAsync success/failure rate unknown
- No logging of actual storage operations
- Silent failures possible

### Next Steps (Phase 43 Proposal)

**Diagnostic Logging Enhancement**:
1. Add comprehensive logging to trace memory flow
2. Track counts at each VCM tier transition
3. Log deduplication decisions with similarity scores
4. Measure EncodeAsync success/failure rates

**Targeted Investigation**:
- Analyze Recently Buffer expiration vs promotion ratio
- Measure Working Memory turnover rate
- Trace 69 lost memories through the system

**Expected Outcome**:
Identify exact point where 82% of memories are lost
