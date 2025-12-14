# LocalAI Integration Strategy

## Overview

Memory Indexer의 새로운 비전에 따라, LocalAI 패키지들의 역할을 재정의합니다.

---

## Package Requirements Matrix

| Package | Role in Memory Indexer | Layer | Priority |
|---------|----------------------|-------|----------|
| **LocalAI.Embedder** | 텍스트 임베딩 생성 | Core | ✅ **P0** (구현됨) |
| **LocalAI.Reranker** | 검색 결과 시맨틱 재순위 | Core | ✅ **P1** (예정) |
| **LocalAI.Generator** | 분류, 요약, 추출 | Intelligence | ✅ **P1** (예정) |

### Not Required

| Package | Reason |
|---------|--------|
| LocalAI.Captioner | 이미지 처리 - 텍스트 전용 시스템 |
| LocalAI.Ocr | 문서 OCR - 범위 외 |
| LocalAI.Detector | 객체 감지 - 비전 기능 |
| LocalAI.Segmenter | 이미지 분할 - 비전 기능 |
| LocalAI.Translator | 다국어 임베딩 모델이 대체 |
| LocalAI.Transcriber | 음성 처리 - 범위 외 |
| LocalAI.Synthesizer | 음성 합성 - 범위 외 |

---

## Integration Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      Memory Indexer                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │              Intelligence Layer (New)                      │ │
│  │  ┌────────────────────────────────────────────────────┐   │ │
│  │  │              LocalAI.Generator                      │   │ │
│  │  │  ┌──────────┐ ┌──────────┐ ┌──────────┐           │   │ │
│  │  │  │Classifier│ │Summarizer│ │Extractor │           │   │ │
│  │  │  └──────────┘ └──────────┘ └──────────┘           │   │ │
│  │  └────────────────────────────────────────────────────┘   │ │
│  └────────────────────────────────────────────────────────────┘ │
│                              │                                   │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │                    Core Layer (Current)                    │ │
│  │  ┌────────────────────┐    ┌────────────────────┐         │ │
│  │  │  LocalAI.Embedder  │    │  LocalAI.Reranker  │         │ │
│  │  │  (Text → Vector)   │    │  (Semantic Rerank) │         │ │
│  │  └────────────────────┘    └────────────────────┘         │ │
│  └────────────────────────────────────────────────────────────┘ │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 1. LocalAI.Embedder (구현 완료)

### Current Status: ✅ Integrated

### Usage

```csharp
public class LocalEmbeddingService : IEmbeddingService, IAsyncDisposable
{
    private IEmbeddingModel? _model;

    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, ...)
    {
        await EnsureModelLoadedAsync(cancellationToken);
        return await _model!.EmbedAsync(text);
    }
}
```

### Recommended Models

| Model | Dimensions | Use Case |
|-------|------------|----------|
| `all-MiniLM-L6-v2` | 384 | 빠른 처리, 경량 |
| `bge-small-en-v1.5` | 384 | 높은 품질, 영어 |
| `bge-base-en-v1.5` | 768 | 균형 잡힌 성능 |
| `bge-m3` | 1024 | 최고 품질, 다국어 |

---

## 2. LocalAI.Reranker (예정)

### Purpose

Vector search의 recall → Reranker의 precision 조합으로 검색 품질 향상

```
Query → Embedder → Vector Search (Top 20) → Reranker → Final (Top 5)
         (Fast)      (High Recall)           (High Precision)
```

### Integration Point

```csharp
// src/MemoryIndexer.Intelligence/Services/IRerankerService.cs
public interface IRerankerService
{
    /// <summary>
    /// Rerank memories based on semantic relevance to query.
    /// </summary>
    Task<IReadOnlyList<MemorySearchResult>> RerankAsync(
        string query,
        IReadOnlyList<MemorySearchResult> candidates,
        int topK = 5,
        CancellationToken cancellationToken = default);
}
```

### Implementation

```csharp
// src/MemoryIndexer.Intelligence/Services/LocalRerankerService.cs
public class LocalRerankerService : IRerankerService, IAsyncDisposable
{
    private IRerankerModel? _model;
    private readonly string _modelId = "bge-reranker-base";

    public async Task<IReadOnlyList<MemorySearchResult>> RerankAsync(
        string query,
        IReadOnlyList<MemorySearchResult> candidates,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        await EnsureModelLoadedAsync(cancellationToken);

        var documents = candidates.Select(c => c.Memory.Content).ToArray();
        var scores = await _model!.ScoreAsync(query, documents);

        return candidates
            .Select((c, i) => new MemorySearchResult
            {
                Memory = c.Memory,
                Score = scores[i]
            })
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }
}
```

### Recommended Models

| Model | Speed | Quality |
|-------|-------|---------|
| `bge-reranker-base` | Fast | Good |
| `bge-reranker-large` | Medium | Better |
| `bge-reranker-v2-m3` | Slow | Best (Multilingual) |

---

## 3. LocalAI.Generator (예정)

### Purpose

Intelligence Layer의 핵심 - 메모리 분류, 요약, 추출 담당

### Integration Points

#### 3.1 Memory Classifier

```csharp
// src/MemoryIndexer.Intelligence/Services/IMemoryClassifier.cs
public interface IMemoryClassifier
{
    /// <summary>
    /// Classify a message to determine memory placement.
    /// </summary>
    Task<MemoryClassification> ClassifyAsync(
        string content,
        SessionContext? session = null,
        CancellationToken cancellationToken = default);
}

public record MemoryClassification
{
    public MemoryTier Tier { get; init; }        // Working, Session, User
    public MemoryType Type { get; init; }        // Episodic, Semantic, Procedural, Fact
    public float Importance { get; init; }       // 0.0 - 1.0
    public string[] Topics { get; init; }        // ["api", "authentication"]
    public bool ShouldPersist { get; init; }     // false for transient messages
}

public enum MemoryTier { Working, Session, User }
```

#### 3.2 Fact Extractor

```csharp
// src/MemoryIndexer.Intelligence/Services/IFactExtractor.cs
public interface IFactExtractor
{
    /// <summary>
    /// Extract factual information from conversation.
    /// </summary>
    Task<IReadOnlyList<ExtractedFact>> ExtractFactsAsync(
        string conversation,
        CancellationToken cancellationToken = default);
}

public record ExtractedFact
{
    public string Subject { get; init; }      // "User"
    public string Predicate { get; init; }    // "prefers"
    public string Object { get; init; }       // "TypeScript"
    public float Confidence { get; init; }    // 0.0 - 1.0
}
```

#### 3.3 Memory Summarizer

```csharp
// src/MemoryIndexer.Intelligence/Services/IMemorySummarizer.cs
public interface IMemorySummarizer
{
    /// <summary>
    /// Summarize multiple related memories into one.
    /// </summary>
    Task<MemoryUnit> SummarizeAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken = default);
}
```

### Implementation Strategy

```csharp
// src/MemoryIndexer.Intelligence/Services/LocalGeneratorService.cs
public class LocalGeneratorService : IAsyncDisposable
{
    private IGeneratorModel? _model;
    private readonly string _modelId;

    public LocalGeneratorService(IOptions<IntelligenceOptions> options)
    {
        _modelId = options.Value.GeneratorModel ?? "phi-3-mini";
    }

    public async Task<string> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        await EnsureModelLoadedAsync(cancellationToken);

        var response = await _model!.GenerateAsync(new GenerateRequest
        {
            Messages = [
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            ],
            MaxTokens = 512,
            Temperature = 0.1f  // Low temperature for deterministic extraction
        });

        return response.Content;
    }
}
```

### Recommended Models

| Model | Parameters | Use Case | VRAM |
|-------|------------|----------|------|
| `phi-3-mini` | 3.8B | 분류, 추출 | ~4GB |
| `Qwen2.5-1.5B` | 1.5B | 경량 작업 | ~2GB |
| `Llama-3.2-1B` | 1B | 최소 리소스 | ~1.5GB |
| `Qwen2.5-3B` | 3B | 균형 | ~3.5GB |

### Prompt Templates

#### Classification Prompt

```
System: You are a memory classification assistant. Analyze the message and output JSON.

User: Classify this message for a memory system:
"{message}"

Session context: {session_summary}

Output JSON with:
- tier: "working" | "session" | "user"
- type: "episodic" | "semantic" | "procedural" | "fact"
- importance: 0.0-1.0
- topics: string[]
- should_persist: boolean
```

#### Fact Extraction Prompt

```
System: Extract factual information about the user from the conversation.

User: Extract facts from this conversation:
"{conversation}"

Output JSON array of facts:
[{ "subject": "User", "predicate": "prefers", "object": "TypeScript", "confidence": 0.9 }]

Only include facts that are:
- About the user's preferences, knowledge, or situation
- Explicitly stated or strongly implied
- Useful for future conversations
```

#### Summarization Prompt

```
System: Summarize multiple related memories into one coherent memory.

User: Summarize these memories:
{memories_json}

Create a single summary that:
- Preserves all important information
- Removes redundancy
- Is concise but complete
- Can replace the original memories
```

---

## Configuration

### appsettings.json

```json
{
  "MemoryIndexer": {
    "Embedding": {
      "Provider": "Local",
      "Model": "bge-base-en-v1.5",
      "Dimensions": 768
    },
    "Intelligence": {
      "Enabled": true,
      "GeneratorModel": "phi-3-mini",
      "RerankerModel": "bge-reranker-base",
      "ClassificationEnabled": true,
      "FactExtractionEnabled": true,
      "SummarizationEnabled": true,
      "ConsolidationIntervalMinutes": 30
    }
  }
}
```

### IntelligenceOptions

```csharp
public sealed class IntelligenceOptions
{
    public bool Enabled { get; set; } = true;
    public string? GeneratorModel { get; set; } = "phi-3-mini";
    public string? RerankerModel { get; set; } = "bge-reranker-base";
    public bool ClassificationEnabled { get; set; } = true;
    public bool FactExtractionEnabled { get; set; } = true;
    public bool SummarizationEnabled { get; set; } = true;
    public int ConsolidationIntervalMinutes { get; set; } = 30;
}
```

---

## Dependency Registration

```csharp
// src/MemoryIndexer.Sdk/ServiceCollectionExtensions.cs
public static IServiceCollection AddMemoryIndexerIntelligence(
    this IServiceCollection services,
    Action<IntelligenceOptions>? configure = null)
{
    services.Configure(configure ?? (_ => { }));

    // Generator-based services
    services.AddSingleton<LocalGeneratorService>();
    services.AddSingleton<IMemoryClassifier, LocalMemoryClassifier>();
    services.AddSingleton<IFactExtractor, LocalFactExtractor>();
    services.AddSingleton<IMemorySummarizer, LocalMemorySummarizer>();

    // Reranker service
    services.AddSingleton<IRerankerService, LocalRerankerService>();

    // Consolidation background service
    services.AddHostedService<MemoryConsolidationService>();

    return services;
}
```

---

## Package References Update

### Directory.Packages.props

```xml
<!-- Local AI (iyulab open source) -->
<PackageVersion Include="LocalAI.Embedder" Version="0.7.0" />
<PackageVersion Include="LocalAI.Reranker" Version="0.7.0" />
<PackageVersion Include="LocalAI.Generator" Version="0.7.0" />
```

### MemoryIndexer.Intelligence.csproj

```xml
<ItemGroup>
  <PackageReference Include="LocalAI.Generator" />
  <PackageReference Include="LocalAI.Reranker" />
</ItemGroup>
```

---

## Implementation Phases

### Phase 1: Reranker Integration
- [ ] Add `LocalAI.Reranker` package reference
- [ ] Implement `IRerankerService` interface
- [ ] Integrate into `RecallAsync` pipeline
- [ ] Add configuration options
- [ ] Write tests

### Phase 2: Generator Integration (Classifier)
- [ ] Add `LocalAI.Generator` package reference
- [ ] Implement `LocalGeneratorService` base
- [ ] Implement `IMemoryClassifier`
- [ ] Integrate into `IngestAsync` pipeline
- [ ] Write tests

### Phase 3: Fact Extraction
- [ ] Implement `IFactExtractor`
- [ ] Create fact extraction prompts
- [ ] Integrate into session end workflow
- [ ] Write tests

### Phase 4: Summarization & Consolidation
- [ ] Implement `IMemorySummarizer`
- [ ] Implement `MemoryConsolidationService`
- [ ] Background consolidation job
- [ ] Write tests

---

## Performance Considerations

### Model Loading Strategy

```csharp
// Lazy loading with warmup
public class ModelManager : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IGeneratorModel? _generator;
    private IRerankerModel? _reranker;

    // Warmup on startup (optional)
    public async Task WarmupAsync()
    {
        // Load models in parallel
        var tasks = new[]
        {
            LoadGeneratorAsync(),
            LoadRerankerAsync()
        };
        await Task.WhenAll(tasks);
    }

    // Lazy load on first use
    public async Task<IGeneratorModel> GetGeneratorAsync()
    {
        if (_generator != null) return _generator;

        await _lock.WaitAsync();
        try
        {
            _generator ??= await LocalGenerator.LoadAsync(_modelId);
            return _generator;
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

### Resource Management

| Component | Memory | Startup | Per-Request |
|-----------|--------|---------|-------------|
| Embedder | ~500MB | 2-5s | 10-50ms |
| Reranker | ~500MB | 2-5s | 20-100ms |
| Generator | 2-4GB | 5-15s | 100-500ms |

### Optimization Strategies

1. **Model Sharing**: 동일 모델 인스턴스 재사용
2. **Batch Processing**: 여러 요청 배치 처리
3. **Async Loading**: 백그라운드 모델 로딩
4. **Caching**: 분류/추출 결과 캐싱
5. **Fallback**: GPU 없을 시 CPU 폴백

---

## Conclusion

Memory Indexer의 새로운 비전을 실현하기 위해:

1. **LocalAI.Embedder** ✅ - 이미 통합됨
2. **LocalAI.Reranker** - 검색 품질 향상을 위해 필요
3. **LocalAI.Generator** - Intelligence Layer의 핵심

이 세 패키지의 조합으로 **지능형 메모리 관리 시스템**을 구축할 수 있습니다.
