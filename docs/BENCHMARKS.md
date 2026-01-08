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
| Store single memory | 2.18 μs | 0.13 μs | 2.7 KB |
| Store 10 (sequential) | 32.2 μs | 0.34 μs | 31.8 KB |
| Store 10 (parallel) | 32.8 μs | 1.70 μs | 32.8 KB |
| Recall (limit 5) | ~1.5 μs | - | ~1.5 KB |
| Recall (limit 20) | ~1.8 μs | - | ~2.0 KB |
| GetAll (limit 50) | ~2.1 μs | - | ~2.5 KB |
| GetAll (limit 100) | ~2.4 μs | - | ~3.0 KB |
| Update memory | ~4.2 μs | - | ~3.5 KB |
| Delete memory | ~3.8 μs | - | ~3.0 KB |

## Tiered Workflow Benchmarks (TieredMemoryBenchmark)

| Workflow | Mean | StdDev | Allocated |
|----------|------|--------|-----------|
| Storage: Vector search | 812 ns | 84 ns | 4.39 KB |
| Storage: Store with embedding | 4.05 μs | 0.11 μs | 3.46 KB |
| Workflow: Store → Recall | 13.06 μs | 1.08 μs | 7.82 KB |
| Workflow: Batch store → GetAll | 13.51 μs | 0.27 μs | 15.25 KB |
| Workflow: Store → Update → Recall | 16.02 μs | 0.40 μs | 10.87 KB |
| Workflow: Mixed memory types | 20.42 μs | 1.50 μs | 16.48 KB |

## Throughput Summary

| Metric | Value |
|--------|-------|
| Store ops/sec | ~460,000 |
| Recall ops/sec | ~670,000 |
| Vector search ops/sec | ~1,230,000 |
| Store→Recall workflow/sec | ~77,000 |

## Running Benchmarks

### PowerShell Script

```powershell
# Quick run (short iterations)
.\benchmarks\run_bench.ps1 -Quick

# Full benchmark
.\benchmarks\run_bench.ps1

# Specific filter
.\benchmarks\run_bench.ps1 -Filter "Store"

# Export JSON results
.\benchmarks\run_bench.ps1 -ExportJson
```

### Direct CLI

```bash
cd benchmarks/MemoryIndexer.Benchmarks

# Run all benchmarks (short mode)
dotnet run -c Release -- --filter "*" --job short

# Run specific benchmark class
dotnet run -c Release -- --filter "*MemoryOperationsBenchmark*"

# Full benchmark with all exporters
dotnet run -c Release -- --filter "*" --exporters html,csv,json
```

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
| v0.4.0 | 2.18 | 1.5 | 812 | 4-tier cognitive architecture |
| v0.3.0 | 2.45 | 1.8 | 950 | Pre-cognitive architecture |

---

*Last updated: 2026-01-09*
*Run with: BenchmarkDotNet ShortRun on Windows 11*
