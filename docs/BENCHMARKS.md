# Benchmark Results

Performance measurements for Memory Indexer operations using BenchmarkDotNet.

## Environment

```
BenchmarkDotNet v0.14.0
Runtime: .NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2
OS: Windows 11
GC: Concurrent Workstation
HardwareIntrinsics: AVX2, AES, BMI1, BMI2, FMA, LZCNT, PCLMUL, POPCNT
Configuration: Release, ShortRun (3 iterations)
Storage: InMemory
Embedding: Mock (768 dimensions)
```

## Core Operations (MemoryOperationsBenchmark)

| Operation | Mean | StdDev | Allocated |
|-----------|------|--------|-----------|
| Get all memories (limit 50) | 269 ns | 5 ns | 752 B |
| Get all memories (limit 100) | 275 ns | 7 ns | 752 B |
| Update memory content | 1.01 μs | 17 ns | 2.44 KB |
| Store single memory | 1.17 μs | 53 ns | 2.77 KB |
| Recall memories (limit 5) | 4.28 μs | 216 ns | 5.46 KB |
| Recall memories (limit 20) | 4.18 μs | 49 ns | 5.46 KB |
| Store 10 (parallel) | 16.8 μs | 340 ns | 33.6 KB |
| Store 10 (sequential) | 18.1 μs | 209 ns | 32.6 KB |
| Delete memory | 7.41 μs | 2.02 μs | 7.91 KB |

## Tiered Workflow Benchmarks (TieredMemoryBenchmark)

| Workflow | Mean | StdDev | Allocated |
|----------|------|--------|-----------|
| Storage: Vector search | 482 ns | 13 ns | 4.39 KB |
| Storage: Store with embedding | 2.45 μs | 88 ns | 3.46 KB |
| Workflow: Store → Recall | 5.66 μs | 237 ns | 7.82 KB |
| Workflow: Batch store → GetAll | 7.07 μs | 116 ns | 15.25 KB |
| Workflow: Store → Update → Recall | 8.24 μs | 424 ns | 11.06 KB |
| Workflow: Mixed memory types | 10.34 μs | 268 ns | 16.47 KB |

## Tier Promotion Benchmarks (TierPromotionBenchmark)

Benchmarks for the 4-tier memory promotion pipeline (Buffer → Short → Long → Archive).

| Operation | Mean | StdDev | Allocated |
|-----------|------|--------|-----------|
| T0 Buffer: Get stats | 9.7 ns | 0.3 ns | 88 B |
| T3 Archive: Get stats | 15.9 ns | 0.4 ns | 160 B |
| T3 Archive: Get all | 18.6 ns | 0.3 ns | 144 B |
| T1 ShortTerm: Capacity check | 175 ns | 0.4 ns | 0 B |
| T1 ShortTerm: Get all | 237 ns | 3.2 ns | 408 B |
| T0 Buffer: Enqueue | 244 ns | 2.0 ns | 400 B |
| T0 Buffer: Enqueue + Drain (5 items) | 1.20 μs | 27 ns | 1.90 KB |
| VCM: Initialize session | 1.69 μs | 35 ns | 1.76 KB |
| T1 ShortTerm: Promote memory | 1.80 μs | 16 ns | 5.69 KB |
| VCM: Get context usage | 2.52 μs | 79 ns | 2.63 KB |
| T3 Archive: Set entry | 6.04 μs | 23 ns | 392 B |
| VCM: Consolidate | 6.48 μs | 234 ns | 11.87 KB |
| Pipeline: Store → Short → Recall | 7.15 μs | 139 ns | 11.19 KB |
| Pipeline: High-importance path | 9.76 μs | 916 ns | 13.70 KB |
| Pipeline: Mixed types promotion | 12.97 μs | 307 ns | 22.42 KB |
| Pipeline: Multi-store → Batch recall | 69.62 μs | 6.72 μs | 66.06 KB |

**Target**: Complete pipeline < 50ms async. ✅ All operations well within target.

## Concurrency Benchmarks (ConcurrencyBenchmark)

Load testing with parameterized concurrent operations (10, 50, 100 concurrent ops).

### N=10 Concurrent Operations

| Benchmark | Mean | StdDev | Allocated |
|-----------|------|--------|-----------|
| Contention: N updates to same memory | 1.98 μs | 86 ns | 3.66 KB |
| Parallel: Vector search N queries | 4.14 μs | 45 ns | 15.38 KB |
| Parallel: Store N memories (N users) | 13.2 μs | 109 ns | 26.7 KB |
| Throughput: Sequential baseline | 14.6 μs | 312 ns | 29.53 KB |
| Parallel: Store N memories (same user) | 17.4 μs | 361 ns | 31.41 KB |
| Throughput: Batched parallel | 20.0 μs | 3.92 μs | 30.1 KB |
| Parallel: Recall N queries (N users) | 78.9 μs | 1.11 μs | 102.8 KB |
| Mixed: N parallel (70% read, 30% write) | 167 μs | 4.27 μs | 214.7 KB |
| Parallel: Recall N queries (same user) | 286 μs | 9.35 μs | 393.1 KB |
| Contention: N deletes + stores | 135 μs | 2.35 μs | 101.2 KB |

### N=50 Concurrent Operations

| Benchmark | Mean | StdDev | Allocated |
|-----------|------|--------|-----------|
| Contention: N updates to same memory | 3.81 μs | 135 ns | 7.30 KB |
| Parallel: Vector search N queries | 19.9 μs | 944 ns | 63.05 KB |
| Parallel: Store N memories (N users) | 110 μs | 4.45 μs | 147.8 KB |
| Throughput: Sequential baseline | 167 μs | 2.21 μs | 218 KB |
| Throughput: Batched parallel | 177 μs | 1.80 μs | 220.4 KB |
| Parallel: Store N memories (same user) | 179 μs | 3.32 μs | 226 KB |
| Parallel: Recall N queries (N users) | 443 μs | 14.9 μs | 510.7 KB |
| Mixed: N parallel (70% read, 30% write) | 1.05 ms | 27.5 μs | 1.30 MB |
| Parallel: Recall N queries (same user) | 1.06 ms | 31.0 μs | 1.70 MB |
| Contention: N deletes + stores | 1.31 ms | 56.2 μs | 704.5 KB |

### N=100 Concurrent Operations

| Benchmark | Mean | StdDev | Allocated |
|-----------|------|--------|-----------|
| Contention: N updates to same memory | 5.92 μs | 78 ns | 11.89 KB |
| Parallel: Vector search N queries | 41.1 μs | 812 ns | 122.7 KB |
| Parallel: Store N memories (N users) | 260 μs | 10.4 μs | 334.4 KB |
| Throughput: Batched parallel | 586 μs | 4.70 μs | 616.4 KB |
| Parallel: Store N memories (same user) | 597 μs | 20.9 μs | 627.4 KB |
| Throughput: Sequential baseline | 599 μs | 16.9 μs | 611.7 KB |
| Parallel: Recall N queries (N users) | 1.21 ms | 11.3 μs | 1.10 MB |
| Parallel: Recall N queries (same user) | 2.22 ms | 61.7 μs | 3.11 MB |
| Mixed: N parallel (70% read, 30% write) | 2.33 ms | 44.1 μs | 2.71 MB |
| Contention: N deletes + stores | 3.96 ms | 26.1 μs | 1.90 MB |

## Throughput Summary

| Metric | Value |
|--------|-------|
| Store ops/sec | ~855,000 |
| Recall ops/sec | ~234,000 |
| Vector search ops/sec | ~2,070,000 |
| Store→Recall workflow/sec | ~177,000 |

## Running Benchmarks

### PowerShell Script (Recommended)

```powershell
# Quick run (short iterations, ~2-3 minutes)
.\benchmarks\run_bench.ps1 -Quick

# Full benchmark (~10-15 minutes)
.\benchmarks\run_bench.ps1

# Run and update this documentation
.\benchmarks\run_bench.ps1 -Quick -UpdateDocs

# Specific filter
.\benchmarks\run_bench.ps1 -Filter "Store" -Quick
```

### Direct CLI

```bash
cd benchmarks/MemoryIndexer.Benchmarks

# Run all benchmarks (short mode)
dotnet run -c Release -- --filter "*" --job short

# Run specific benchmark class
dotnet run -c Release -- --filter "*MemoryOperationsBenchmark*"
dotnet run -c Release -- --filter "*TierPromotionBenchmark*"
dotnet run -c Release -- --filter "*ConcurrencyBenchmark*"

# Full benchmark with all exporters
dotnet run -c Release -- --filter "*" --exporters html,csv,json,markdown
```

Results are exported to `BenchmarkDotNet.Artifacts/` in the repository root.

## Notes

### Storage Considerations

- **InMemory**: These benchmarks use in-memory storage for isolation
- **SQLite-vec**: Add ~10-50μs per operation for disk I/O
- **Qdrant**: Add network latency depending on deployment

### Embedding Considerations

- **Mock (768-dim)**: Zero-cost embedding for benchmarking
- **Ollama (bge-m3)**: Add ~50-100ms per embedding operation
- **OpenAI**: Add ~100-500ms per embedding (network latency)

### Memory Allocation

- Gen0 GC collections are expected during high-throughput scenarios
- Gen1 collections occur occasionally with parallel operations
- No Gen2 collections observed in normal workloads

### Parallelism

- Store operations parallelize well with minimal overhead
- Sequential vs parallel performance is similar due to async nature
- Thread pool scaling maintains performance under load

## Version History

| Version | Store (μs) | Recall (μs) | Vector Search (ns) | Notes |
|---------|------------|-------------|-------------------|-------|
| v0.8.2 | 1.17 | 4.28 | 482 | Fix benchmark GC pressure (Delete: 2.28ms → 7.4μs) |
| v0.8.1 | 1.17 | 4.28 | 482 | Performance improvements |
| v0.8.0 | 2.18 | 1.5 | 812 | Tier promotion & concurrency benchmarks |
| v0.4.0 | 2.18 | 1.5 | 812 | 4-tier cognitive architecture |
| v0.3.0 | 2.45 | 1.8 | 950 | Pre-cognitive architecture |

---

*Last updated: 2026-01-10*
*Run with: BenchmarkDotNet ShortRun on Windows 11*
