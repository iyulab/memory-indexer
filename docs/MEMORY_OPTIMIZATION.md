# Memory Usage Optimization Analysis

**Phase**: 18.2.3
**Date**: January 2026
**Status**: In Progress

## Objectives

1. Profile current memory usage patterns across 4-tier architecture
2. Identify memory hotspots and optimization opportunities
3. Implement optimizations without compromising functionality
4. Measure improvements through benchmarks

---

## Current Memory Architecture

### 4-Tier Memory Layout

```
Recently Buffer (Tier 0)
├─ Raw string storage in Dictionary<string, List<BufferItem>>
├─ Per-user buffers with no size limits
└─ Issue: Unbounded memory growth before promotion

Working Memory (L1)
├─ List<MemoryUnit> with capacity limits (4-7 items)
├─ Full MemoryUnit objects with embeddings (768-1024 dims)
└─ Issue: Large embedding vectors kept in memory

Session Store (L2)
├─ Vector database storage (SQLite-vec or Qdrant)
├─ Full metadata + embeddings persisted
└─ Issue: Frequent serialization/deserialization overhead

User Profile (L3)
├─ Dictionary-based UserProfileEntry storage
├─ Embeddings optional but recommended
└─ Issue: Growing profile size over time
```

---

## Memory Profiling Results

### Baseline Measurements (from Benchmarks)

**Expected Results** (to be filled after running benchmarks):

| Operation | Avg Time | Memory Allocated | Gen0 | Gen1 | Gen2 |
|-----------|----------|------------------|------|------|------|
| Store single | TBD | TBD | TBD | TBD | TBD |
| Store 10 sequential | TBD | TBD | TBD | TBD | TBD |
| Store 10 parallel | TBD | TBD | TBD | TBD | TBD |
| Recall (limit 5) | TBD | TBD | TBD | TBD | TBD |
| GetAll (limit 50) | TBD | TBD | TBD | TBD | TBD |

**Run benchmarks with:**
```bash
dotnet run -c Release --project benchmarks/MemoryIndexer.Benchmarks
```

---

## Identified Optimization Opportunities

### 1. Recently Buffer Memory Management

**Current Implementation:**
```csharp
Dictionary<string, List<BufferItem>>
- Each BufferItem contains: string Content, DateTime Timestamp, string SessionId
- No size limits until promotion triggers
```

**Issues:**
- ✗ Unbounded list growth per user
- ✗ Full string content stored (no chunking)
- ✗ No memory pressure handling

**Proposed Optimizations:**
1. **Implement MaxBufferSize per user** (already in options but not enforced)
2. **Add MaxBufferTokens enforcement** (currently configured but needs validation)
3. **Eager promotion under memory pressure**
4. **String pooling for common phrases**

**Implementation Priority**: HIGH

---

### 2. Embedding Vector Optimization

**Current Implementation:**
```csharp
ReadOnlyMemory<float> Embedding { get; set; }
- Dimensions: 768 (nomic) or 1024 (bge-m3)
- Stored in every MemoryUnit
- Full precision float[]
```

**Issues:**
- ✗ Large memory footprint (768 floats = 3KB per memory)
- ✗ Full precision not always needed
- ✗ Embeddings duplicated in Working Memory

**Proposed Optimizations:**
1. **Lazy embedding loading** - Don't load embeddings until needed for search
2. **Quantization** - Use float16 or int8 for storage (50-75% reduction)
3. **Embedding cache** - Shared cache for common queries
4. **Null embeddings in Working Memory** - Only need for Session/User search

**Implementation Priority**: MEDIUM

---

### 3. Working Memory Eviction Strategy

**Current Implementation:**
```csharp
List<MemoryUnit> with LRU eviction
- Capacity: 4-7 items (configurable)
- Full objects with embeddings
```

**Issues:**
- ✗ No predictive eviction
- ✗ Embeddings stored unnecessarily
- ✗ Eviction only on capacity, not memory pressure

**Proposed Optimizations:**
1. **Memory-pressure aware eviction** - Evict proactively under memory constraints
2. **Remove embeddings from Working Memory** - Regenerate if needed for search
3. **Streaming eviction** - Gradual eviction vs batch
4. **Importance-weighted eviction** - Consider importance score, not just LRU

**Implementation Priority**: MEDIUM

---

### 4. Metadata Dictionary Optimization

**Current Implementation:**
```csharp
Dictionary<string, string> Metadata { get; init; } = [];
- Created for every MemoryUnit
- Often empty or sparse
```

**Issues:**
- ✗ Dictionary overhead even when empty (48 bytes)
- ✗ String duplication for common keys

**Proposed Optimizations:**
1. **Null for empty metadata** - Don't allocate if not used
2. **Frozen dictionary for read-only metadata** - .NET 8+ FrozenDictionary
3. **String interning for common keys** - "source", "category", etc.

**Implementation Priority**: LOW

---

### 5. Object Pooling

**Current Status**: Not implemented

**Proposed Implementation:**
1. **ArrayPool<float> for embeddings** - Reuse embedding arrays
2. **ObjectPool<MemoryUnit> for transient objects** - Reduce allocations
3. **StringBuilderPool for concatenation** - Reduce string allocations

**Implementation Priority**: LOW (after profiling shows benefit)

---

### 6. Batch Processing Optimization

**Current Implementation:**
- Serial processing in promotion pipelines
- Individual database operations

**Proposed Optimizations:**
1. **Batch database writes** - Accumulate and write in batches
2. **Parallel embedding generation** - Use Parallel.ForEachAsync
3. **Streaming APIs** - IAsyncEnumerable for large result sets

**Implementation Priority**: MEDIUM

---

## Optimization Roadmap

### Phase 1: Quick Wins (Immediate)
- [ ] Enforce MaxBufferSize and MaxBufferTokens in Recently Buffer
- [ ] Null metadata dictionaries when empty
- [ ] Add memory pressure monitoring

### Phase 2: Structural (Week 1)
- [ ] Lazy embedding loading
- [ ] Remove embeddings from Working Memory
- [ ] Memory-pressure aware eviction

### Phase 3: Advanced (Week 2)
- [ ] Embedding quantization (float16/int8)
- [ ] ArrayPool for embedding arrays
- [ ] Batch processing optimization

### Phase 4: Polish (Week 3)
- [ ] FrozenDictionary for read-only metadata
- [ ] String interning for common keys
- [ ] ObjectPool for MemoryUnit

---

## Success Metrics

### Target Improvements

| Metric | Baseline | Target | Method |
|--------|----------|--------|--------|
| Memory/operation | TBD | -30% | BenchmarkDotNet MemoryDiagnoser |
| Gen0 collections | TBD | -40% | BenchmarkDotNet |
| Gen1 collections | TBD | -50% | BenchmarkDotNet |
| Peak memory usage | TBD | -25% | dotnet-counters |
| Allocation rate | TBD | -35% | dotnet-counters |

### Measurement Tools

1. **BenchmarkDotNet** - Micro-benchmarks with MemoryDiagnoser
2. **dotnet-counters** - Real-time memory monitoring
3. **dotnet-trace** - Memory allocation profiling
4. **PerfView** - Advanced memory analysis (Windows)

**Monitoring Commands:**
```bash
# Real-time memory monitoring
dotnet-counters monitor --process-id <PID> \
  System.Runtime[gc-heap-size,gen-0-gc-count,gen-1-gc-count,gen-2-gc-count,alloc-rate]

# Memory profiling
dotnet-trace collect --process-id <PID> --providers Microsoft-DotNETCore-SampleProfiler

# Run benchmarks
dotnet run -c Release --project benchmarks/MemoryIndexer.Benchmarks
```

---

## Implementation Plan

### Step 1: Baseline Profiling
1. Run current benchmarks to establish baseline
2. Document memory allocations and GC statistics
3. Identify top 3 memory hotspots

### Step 2: Implement Quick Wins
1. Enforce buffer limits in Recently Buffer
2. Null empty metadata dictionaries
3. Add memory pressure detection

### Step 3: Structural Optimizations
1. Implement lazy embedding loading
2. Remove embeddings from Working Memory
3. Add memory-pressure aware eviction

### Step 4: Validation
1. Re-run benchmarks
2. Compare metrics against targets
3. Document improvements

### Step 5: Documentation
1. Update architecture docs with optimizations
2. Add memory best practices guide
3. Document monitoring approach

---

## Notes

- All optimizations must maintain functional correctness (654 tests must pass)
- Benchmark before and after each optimization
- Profile in Release mode for accurate measurements
- Consider real-world usage patterns, not just micro-benchmarks

---

## References

- BenchmarkDotNet: https://benchmarkdotnet.org/
- .NET Memory Performance Best Practices: https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/memory-management-and-gc
- Quantization techniques: https://huggingface.co/docs/transformers/main/en/quantization
