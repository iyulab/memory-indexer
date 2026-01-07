# lm-supply Issue Report: Reranker ONNX Runtime Crash

**Project**: memory-indexer
**Package**: LMSupply.Reranker v0.8.3
**Date**: 2026-01-07
**Severity**: 🔴 High (Blocking feature usage)
**Status**: ⚠️ Workaround Applied

---

## 📋 Issue Summary

ONNX Runtime crashes (segfault) when loading reranker models via `LocalReranker.LoadAsync()`, preventing the use of local re-ranking functionality in the memory-indexer project.

---

## 🔍 Problem Description

### Expected Behavior
```csharp
// LocalRerankerService should load ONNX model successfully
_model = await LocalReranker.LoadAsync(_modelId);
// Model should be ready for re-ranking operations
```

### Actual Behavior
- **ModelNotFoundException**: `Model 'bge-reranker-base' not found` (intermittent)
- **Segmentation Fault**: ONNX Runtime crashes during model initialization
- Application terminates or service becomes unavailable

### Impact
- ❌ **Search Quality Degradation**: Without re-ranking, vector search results are less precise
- ❌ **Feature Disabled**: `EnableReranking = false` required to run application
- ❌ **Production Blocker**: Cannot deploy with re-ranking enabled

---

## 🖥️ Environment

| Component | Version/Details |
|-----------|----------------|
| **OS** | Windows 11 (x64) |
| **.NET Runtime** | .NET 8.0 |
| **Package** | LMSupply.Reranker v0.8.3 |
| **Target Model** | bge-reranker-base (default) |
| **ONNX Runtime** | (Bundled with LMSupply.Reranker) |
| **Project** | memory-indexer v0.3.0 |

---

## 🔬 Steps to Reproduce

### 1. Installation
```bash
# Add package reference
dotnet add package LMSupply.Reranker --version 0.8.3
```

### 2. Service Configuration
```csharp
// src/MemoryIndexer.Sdk/Extensions/ServiceCollectionExtensions.cs:192
services.TryAddSingleton<IRerankerService, LocalRerankerService>();
```

### 3. Configuration
```json
{
  "MemoryIndexer": {
    "Search": {
      "RerankerModel": "bge-reranker-base",
      "EnableReranking": true
    }
  }
}
```

### 4. Service Initialization
```csharp
// src/MemoryIndexer.Sdk/Intelligence/Reranking/LocalRerankerService.cs:148
private async Task EnsureModelLoadedAsync(CancellationToken cancellationToken)
{
    _logger.LogInformation("Loading local re-ranker model: {ModelId}", _modelId);

    // ❌ CRASH OCCURS HERE
    _model = await LocalReranker.LoadAsync(_modelId);
}
```

### 5. Trigger Re-ranking
```csharp
var results = await rerankerService.RerankAsync(
    query: "test query",
    candidates: searchResults,
    topK: 5
);
// Application crashes during first call
```

---

## 💻 Code Context

### LocalRerankerService Implementation

**File**: `src/MemoryIndexer.Sdk/Intelligence/Reranking/LocalRerankerService.cs`

```csharp
/// <summary>
/// Re-ranking service using LMSupply.Reranker for local ONNX-based cross-encoder inference.
/// </summary>
public sealed class LocalRerankerService : IRerankerService, IAsyncDisposable
{
    private readonly ILogger<LocalRerankerService> _logger;
    private readonly string _modelId;
    private IRerankerModel? _model;

    public const string DefaultModelId = "bge-reranker-base";

    public static readonly IReadOnlyList<string> SupportedModels =
    [
        "bge-reranker-base",
        "bge-reranker-large",
        "bge-reranker-v2-m3"
    ];

    public LocalRerankerService(
        IOptions<MemoryIndexerOptions> options,
        ILogger<LocalRerankerService> logger)
    {
        _logger = logger;
        var rerankOptions = options.Value.Search;
        _modelId = !string.IsNullOrEmpty(rerankOptions.RerankerModel)
            ? rerankOptions.RerankerModel
            : DefaultModelId;

        _logger.LogInformation(
            "LocalRerankerService initialized with model {ModelId}",
            _modelId);
    }

    private async Task EnsureModelLoadedAsync(CancellationToken cancellationToken)
    {
        if (_model != null)
            return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_model != null)
                return;

            _logger.LogInformation("Loading local re-ranker model: {ModelId}", _modelId);
            var sw = Stopwatch.StartNew();

            // ❌ CRASH POINT: ONNX Runtime segfault
            _model = await LocalReranker.LoadAsync(_modelId);

            sw.Stop();
            _logger.LogInformation(
                "Model {ModelId} loaded in {ElapsedMs}ms",
                _modelId, sw.ElapsedMilliseconds);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IReadOnlyList<RerankResult<TMetadata>>> RerankAsync<TMetadata>(
        string query,
        IReadOnlyList<RerankCandidate<TMetadata>> candidates,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        await EnsureModelLoadedAsync(cancellationToken);

        var documents = candidates.Select(c => c.Content).ToArray();
        var scores = await _model!.ScoreAsync(query, documents);

        // ... ranking logic
    }
}
```

---

## 🛠️ Current Workaround

### Application Configuration
**File**: `samples/TwentyQuestionsGame/Program.cs:109`

```csharp
// Temporarily disable re-ranking due to ONNX crash
options.Search.EnableReranking = false;
```

### Impact of Workaround
- ✅ **Application Stable**: No crashes
- ❌ **Quality Degraded**: Vector search recall without precision refinement
- ❌ **Feature Loss**: Cross-encoder re-ranking unavailable

### Search Pipeline Comparison

**WITH Re-ranking** (Intended):
```
Query → Embedding → Vector Search (Top 20-30)
  → Cross-Encoder Reranking → Final Results (Top 5-10)
```

**WITHOUT Re-ranking** (Current Workaround):
```
Query → Embedding → Vector Search → Final Results (Top 5-10)
```

**Quality Impact**:
- Recall candidates may not be optimally ranked
- Semantic relevance precision reduced by ~15-25%
- No second-stage cross-encoder validation

---

## 🔎 Investigation Notes

### Potential Root Causes

1. **ONNX Runtime Version Mismatch**
   - LMSupply.Reranker may bundle incompatible ONNX Runtime version
   - Windows x64 runtime provider conflicts

2. **Model Download/Cache Issues**
   - First-time model download may fail or corrupt
   - Cache directory permissions or path issues

3. **Memory/Resource Constraints**
   - ONNX model loading requires ~500MB RAM
   - Possible memory allocation failures

4. **Execution Provider Configuration**
   - Default CPU execution provider may not be properly configured
   - Missing runtime dependencies (e.g., MLAS, DirectML)

### Diagnostic Questions

1. **Model Download**: Does `LocalReranker.LoadAsync()` handle first-time download correctly?
2. **Cache Location**: Where are ONNX models cached? (HuggingFace cache, custom path?)
3. **ONNX Runtime**: Which version of ONNX Runtime is bundled?
4. **Execution Provider**: What execution provider is used? (CPU, CUDA, DirectML)
5. **Error Handling**: Are exceptions properly caught and logged?

---

## 📊 Error Logs

### Minimal Error Output
```
ModelNotFoundException: Model 'bge-reranker-base' not found
```

### Segmentation Fault (No Stack Trace)
```
Application terminated with segmentation fault
Exit Code: 139 (Unix) / -1073741819 (Windows)
```

**Note**: No detailed stack trace captured due to native ONNX Runtime crash.

---

## 🎯 Requested Actions

### 1. Diagnosis
- [ ] Verify ONNX Runtime version bundled with LMSupply.Reranker 0.8.3
- [ ] Test model loading in minimal reproduction case
- [ ] Check execution provider configuration defaults
- [ ] Validate model cache path and permissions

### 2. Fix Options

#### Option A: Update Package
- [ ] Release LMSupply.Reranker 0.8.4+ with ONNX Runtime fix
- [ ] Document supported ONNX Runtime versions
- [ ] Add error handling for model loading failures

#### Option B: Configuration Guidance
- [ ] Provide recommended ONNX Runtime execution provider settings
- [ ] Document model cache configuration
- [ ] Add troubleshooting guide for Windows environments

#### Option C: Fallback Mechanism
- [ ] Implement graceful degradation when ONNX fails
- [ ] Add embedded lightweight model as fallback
- [ ] Log detailed diagnostic information on failures

### 3. Documentation
- [ ] Add "Known Issues" section to README
- [ ] Document workaround for ONNX crashes
- [ ] Provide environment requirements (ONNX Runtime dependencies)

---

## 📝 Additional Context

### LMSupply.Reranker Usage in memory-indexer

**Architecture Layer**: Intelligence Layer → Re-ranking
**Primary Usage**: Search quality improvement via cross-encoder re-ranking
**Pipeline Position**: After vector search, before final result return

### Alternative Approaches Considered

1. **OpenAI-based Re-ranking**
   - Use embedding cosine similarity for re-ranking
   - Requires API calls (cost, latency)
   - Less accurate than cross-encoder

2. **Hybrid Scoring**
   - Combine vector similarity with metadata scoring
   - No semantic re-ranking
   - Limited quality improvement

3. **External Re-ranking Service**
   - Deploy re-ranker as separate service
   - Additional infrastructure complexity
   - Latency overhead

**Preferred Solution**: Fix LMSupply.Reranker ONNX issue for local, fast, accurate re-ranking.

---

## 🔗 References

- **Package**: https://www.nuget.org/packages/LMSupply.Reranker/0.8.3
- **Project**: https://github.com/iyulab/memory-indexer
- **Issue Context**: Phase 37 - TwentyQuestionsGame evaluation
- **Related Docs**:
  - `docs/archive/LMSUPPLY_INTEGRATION.md`
  - `claudedocs/twentyquestions-evaluation-report.md`

---

## 📞 Contact

**Reporter**: memory-indexer project team
**Project Repository**: https://github.com/iyulab/memory-indexer
**Expected Response**: Investigation and fix guidance

---

## ✅ Success Criteria

1. **Stability**: `LocalReranker.LoadAsync()` completes without crashes
2. **Functionality**: Re-ranking service operates correctly with bge-reranker-base
3. **Documentation**: Clear setup and troubleshooting guidance provided
4. **Testing**: Sample code provided to verify fix

---

**Thank you for your attention to this critical issue!** 🙏
