# Migration Guide: v0.3.0 → v0.4.0

## Overview

Version 0.4.0 introduces **cognitive terminology alignment** based on established cognitive science models (Atkinson-Shiffrin, Tulving, Baddeley). This is a **breaking change** that renames core interfaces and classes to better reflect their cognitive foundations.

**Migration Effort**: ~30 minutes for typical projects
**Breaking Changes**: Interface/class renames, configuration key changes
**Backward Compatibility**: None (v0.x stage allows breaking changes)

## Summary of Changes

### Interface Renames

| v0.3.0 (Old) | v0.4.0 (New) | Cognitive Basis |
|--------------|--------------|-----------------|
| `IRecentlyBuffer` | `ISensoryBuffer` | Atkinson-Shiffrin sensory memory |
| `ISessionStore` | `IEpisodicStore` | Tulving's episodic memory |
| `IUserProfile` | `ISemanticStore` | Tulving's semantic memory |
| `IWorkingMemory` | `IWorkingMemory` | No change (already aligned with Baddeley) |
| `IBufferPromoter` | `ISensoryPromoter` | Promoter renamed for consistency |

### Class Renames

| v0.3.0 (Old) | v0.4.0 (New) |
|--------------|--------------|
| `RecentlyBufferService` | `SensoryBufferService` |
| `InMemorySessionStore` | `InMemoryEpisodicStore` |
| `UserProfileService` | `SemanticStoreService` |
| `BufferPromoterService` | `SensoryPromoterService` |
| `RecentlyMemory` | `SensoryMemory` |
| `UserProfileEntry` | `SemanticStoreEntry` |
| `UserProfileCategory` | `SemanticStoreCategory` |

### Options Classes

| v0.3.0 (Old) | v0.4.0 (New) |
|--------------|--------------|
| `RecentlyBufferOptions` | `SensoryBufferOptions` |
| `SessionStoreOptions` | `EpisodicStoreOptions` |
| `UserProfileOptions` | `SemanticStoreOptions` |

### Configuration Keys

| v0.3.0 (Old) | v0.4.0 (New) |
|--------------|--------------|
| `VCM:RecentlyBuffer` | `VCM:SensoryBuffer` |
| `VCM:UserProfile` | `VCM:SemanticStore` |

### Health Check Names

| v0.3.0 (Old) | v0.4.0 (New) |
|--------------|--------------|
| `RecentlyBufferHealthCheck` | `SensoryBufferHealthCheck` |
| `SessionStoreHealthCheck` | `EpisodicStoreHealthCheck` |
| `UserProfileHealthCheck` | `SemanticStoreHealthCheck` |

## Migration Steps

### Step 1: Update Interface References

**Before (v0.3.0)**:
```csharp
private readonly IRecentlyBuffer _recentlyBuffer;
private readonly ISessionStore _sessionStore;
private readonly IUserProfile _userProfile;
private readonly IBufferPromoter _bufferPromoter;

public MyService(
    IRecentlyBuffer recentlyBuffer,
    ISessionStore sessionStore,
    IUserProfile userProfile,
    IBufferPromoter bufferPromoter)
{
    _recentlyBuffer = recentlyBuffer;
    _sessionStore = sessionStore;
    _userProfile = userProfile;
    _bufferPromoter = bufferPromoter;
}
```

**After (v0.4.0)**:
```csharp
private readonly ISensoryBuffer _sensoryBuffer;
private readonly IEpisodicStore _episodicStore;
private readonly ISemanticStore _semanticStore;
private readonly ISensoryPromoter _sensoryPromoter;

public MyService(
    ISensoryBuffer sensoryBuffer,
    IEpisodicStore episodicStore,
    ISemanticStore semanticStore,
    ISensoryPromoter sensoryPromoter)
{
    _sensoryBuffer = sensoryBuffer;
    _episodicStore = episodicStore;
    _semanticStore = semanticStore;
    _sensoryPromoter = sensoryPromoter;
}
```

**Search & Replace**:
```bash
# PowerShell (Windows)
Get-ChildItem -Recurse -Include *.cs | ForEach-Object {
    (Get-Content $_) `
        -replace 'IRecentlyBuffer', 'ISensoryBuffer' `
        -replace 'ISessionStore', 'IEpisodicStore' `
        -replace 'IUserProfile', 'ISemanticStore' `
        -replace 'IBufferPromoter', 'ISensoryPromoter' |
    Set-Content $_
}

# Bash (Linux/macOS)
find . -name "*.cs" -type f -exec sed -i \
    -e 's/IRecentlyBuffer/ISensoryBuffer/g' \
    -e 's/ISessionStore/IEpisodicStore/g' \
    -e 's/IUserProfile/ISemanticStore/g' \
    -e 's/IBufferPromoter/ISensoryPromoter/g' {} +
```

---

### Step 2: Update Class References

**Before (v0.3.0)**:
```csharp
var bufferService = new RecentlyBufferService(options, logger);
var sessionStore = new InMemorySessionStore(logger);
var userProfile = new UserProfileService(embeddingService, options, logger);
var promoter = new BufferPromoterService(buffer, workingMemory, embedder, segmenter, logger);
```

**After (v0.4.0)**:
```csharp
var bufferService = new SensoryBufferService(options, logger);
var episodicStore = new InMemoryEpisodicStore(logger);
var semanticStore = new SemanticStoreService(embeddingService, options, logger);
var promoter = new SensoryPromoterService(buffer, workingMemory, embedder, segmenter, logger);
```

**Search & Replace**:
```bash
# PowerShell
Get-ChildItem -Recurse -Include *.cs | ForEach-Object {
    (Get-Content $_) `
        -replace 'RecentlyBufferService', 'SensoryBufferService' `
        -replace 'InMemorySessionStore', 'InMemoryEpisodicStore' `
        -replace 'UserProfileService', 'SemanticStoreService' `
        -replace 'BufferPromoterService', 'SensoryPromoterService' `
        -replace 'RecentlyMemory', 'SensoryMemory' `
        -replace 'UserProfileEntry', 'SemanticStoreEntry' `
        -replace 'UserProfileCategory', 'SemanticStoreCategory' |
    Set-Content $_
}
```

---

### Step 3: Update Configuration Files

**Before (v0.3.0)** - `appsettings.json`:
```json
{
  "MemoryIndexer": {
    "VCM": {
      "RecentlyBuffer": {
        "MaxIdleSeconds": 60,
        "TokenThreshold": 500,
        "TurnThreshold": 3
      },
      "UserProfile": {
        "MinConfirmationCount": 3,
        "MinConfidenceThreshold": 0.8
      }
    }
  }
}
```

**After (v0.4.0)** - `appsettings.json`:
```json
{
  "MemoryIndexer": {
    "VCM": {
      "SensoryBuffer": {
        "MaxIdleSeconds": 60,
        "TokenThreshold": 500,
        "TurnThreshold": 3
      },
      "SemanticStore": {
        "MinConfirmationCount": 3,
        "MinConfidenceThreshold": 0.8
      }
    }
  }
}
```

**Manual Update Required**: Configuration files must be updated manually. No automated migration tool.

---

### Step 4: Update DI Registration (if using manual registration)

**Before (v0.3.0)**:
```csharp
services.AddSingleton<IRecentlyBuffer, RecentlyBufferService>();
services.AddSingleton<ISessionStore, InMemorySessionStore>();
services.AddSingleton<IUserProfile, UserProfileService>();
services.AddSingleton<IBufferPromoter, BufferPromoterService>();
```

**After (v0.4.0)**:
```csharp
services.AddSingleton<ISensoryBuffer, SensoryBufferService>();
services.AddSingleton<IEpisodicStore, InMemoryEpisodicStore>();
services.AddSingleton<ISemanticStore, SemanticStoreService>();
services.AddSingleton<ISensoryPromoter, SensoryPromoterService>();
```

**Note**: If using `AddMemoryIndexer()` extension, DI is handled automatically — no changes needed.

---

### Step 5: Update Test Files

**Before (v0.3.0)**:
```csharp
public class RecentlyBufferServiceTests
{
    private readonly IRecentlyBuffer _buffer;

    public RecentlyBufferServiceTests()
    {
        var options = new MemoryIndexerOptions
        {
            RecentlyBuffer = new RecentlyBufferOptions
            {
                IdleTimeout = TimeSpan.FromSeconds(60)
            }
        };
        _buffer = new RecentlyBufferService(
            Options.Create(options),
            NullLogger<RecentlyBufferService>.Instance);
    }
}
```

**After (v0.4.0)**:
```csharp
public class SensoryBufferServiceTests
{
    private readonly ISensoryBuffer _buffer;

    public SensoryBufferServiceTests()
    {
        var options = new MemoryIndexerOptions
        {
            SensoryBuffer = new SensoryBufferOptions
            {
                IdleTimeout = TimeSpan.FromSeconds(60)
            }
        };
        _buffer = new SensoryBufferService(
            Options.Create(options),
            NullLogger<SensoryBufferService>.Instance);
    }
}
```

**Automated Test Rename**:
```bash
# Rename test files
mv tests/**/RecentlyBufferServiceTests.cs tests/**/SensoryBufferServiceTests.cs
mv tests/**/SessionStoreHealthCheckTests.cs tests/**/EpisodicStoreHealthCheckTests.cs
mv tests/**/UserProfileServiceTests.cs tests/**/SemanticStoreServiceTests.cs
```

---

### Step 6: Update Health Checks

**Before (v0.3.0)**:
```csharp
services.AddHealthChecks()
    .AddCheck<RecentlyBufferHealthCheck>(
        name: "Recently Buffer",
        tags: new[] { "tier", "tier:recently" })
    .AddCheck<SessionStoreHealthCheck>(
        name: "Session Store",
        tags: new[] { "tier", "tier:session" })
    .AddCheck<UserProfileHealthCheck>(
        name: "User Profile",
        tags: new[] { "tier", "tier:user" });
```

**After (v0.4.0)**:
```csharp
services.AddHealthChecks()
    .AddCheck<SensoryBufferHealthCheck>(
        name: "Sensory Buffer",
        tags: new[] { "tier", "tier:sensory" })
    .AddCheck<EpisodicStoreHealthCheck>(
        name: "Episodic Store",
        tags: new[] { "tier", "tier:episodic" })
    .AddCheck<SemanticStoreHealthCheck>(
        name: "Semantic Store",
        tags: new[] { "tier", "tier:semantic" });
```

**Or use the extension** (recommended):
```csharp
services.AddMemoryIndexerHealthChecks();
```

---

### Step 7: Update Using Statements

**Before (v0.3.0)**:
```csharp
using MemoryIndexer.Interfaces;  // Contains IRecentlyBuffer, ISessionStore, IUserProfile
using MemoryIndexer.Services;    // Contains RecentlyBufferService, UserProfileService
using MemoryIndexer.Configuration;  // Contains RecentlyBufferOptions, UserProfileOptions
```

**After (v0.4.0)**:
```csharp
using MemoryIndexer.Interfaces;  // Now contains ISensoryBuffer, IEpisodicStore, ISemanticStore
using MemoryIndexer.Services;    // Now contains SensoryBufferService, SemanticStoreService
using MemoryIndexer.Configuration;  // Now contains SensoryBufferOptions, SemanticStoreOptions
```

**No changes needed**: Namespaces remain the same, only type names changed.

---

### Step 8: Build and Test

```bash
# Clean build
dotnet clean
dotnet build

# Run all tests
dotnet test

# Expected results:
# - 0 errors
# - 848 tests passing (49 Core + 799 SDK)
# - Warnings only (nullable, style)
```

**Common Build Errors**:
1. **CS0246**: Type or namespace not found
   → Check for missed renames (use search & replace from Step 1-2)

2. **CS0117**: Does not contain definition
   → Check Options property names in configuration binding

3. **CS1061**: Does not contain member
   → Check method calls on renamed interfaces

---

### Step 9: Update Documentation

**Files to Update**:
1. README.md - Architecture diagrams, config examples
2. Internal documentation - API references
3. Code comments - Interface/class references

**Example**:
```csharp
// Before (v0.3.0)
/// <summary>
/// Manages the Recently Buffer for raw conversation staging.
/// </summary>

// After (v0.4.0)
/// <summary>
/// Manages the Sensory Buffer for raw conversation staging.
/// Based on Atkinson-Shiffrin sensory memory model.
/// </summary>
```

## Quick Reference: Find & Replace Cheat Sheet

### PowerShell Script (Windows)

```powershell
# Save as: migrate-v0.4.ps1

$replacements = @{
    'IRecentlyBuffer' = 'ISensoryBuffer'
    'ISessionStore' = 'IEpisodicStore'
    'IUserProfile' = 'ISemanticStore'
    'IBufferPromoter' = 'ISensoryPromoter'
    'RecentlyBufferService' = 'SensoryBufferService'
    'InMemorySessionStore' = 'InMemoryEpisodicStore'
    'UserProfileService' = 'SemanticStoreService'
    'BufferPromoterService' = 'SensoryPromoterService'
    'RecentlyMemory' = 'SensoryMemory'
    'UserProfileEntry' = 'SemanticStoreEntry'
    'UserProfileCategory' = 'SemanticStoreCategory'
    'RecentlyBufferOptions' = 'SensoryBufferOptions'
    'UserProfileOptions' = 'SemanticStoreOptions'
    'RecentlyBufferHealthCheck' = 'SensoryBufferHealthCheck'
    'SessionStoreHealthCheck' = 'EpisodicStoreHealthCheck'
    'UserProfileHealthCheck' = 'SemanticStoreHealthCheck'
}

Get-ChildItem -Recurse -Include *.cs | ForEach-Object {
    $content = Get-Content $_ -Raw
    $replacements.GetEnumerator() | ForEach-Object {
        $content = $content -replace $_.Key, $_.Value
    }
    Set-Content $_ -Value $content -NoNewline
}

Write-Host "Migration complete! Run 'dotnet build' to verify."
```

### Bash Script (Linux/macOS)

```bash
#!/bin/bash
# Save as: migrate-v0.4.sh

declare -A replacements=(
    ["IRecentlyBuffer"]="ISensoryBuffer"
    ["ISessionStore"]="IEpisodicStore"
    ["IUserProfile"]="ISemanticStore"
    ["IBufferPromoter"]="ISensoryPromoter"
    ["RecentlyBufferService"]="SensoryBufferService"
    ["InMemorySessionStore"]="InMemoryEpisodicStore"
    ["UserProfileService"]="SemanticStoreService"
    ["BufferPromoterService"]="SensoryPromoterService"
    ["RecentlyMemory"]="SensoryMemory"
    ["UserProfileEntry"]="SemanticStoreEntry"
    ["UserProfileCategory"]="SemanticStoreCategory"
    ["RecentlyBufferOptions"]="SensoryBufferOptions"
    ["UserProfileOptions"]="SemanticStoreOptions"
    ["RecentlyBufferHealthCheck"]="SensoryBufferHealthCheck"
    ["SessionStoreHealthCheck"]="EpisodicStoreHealthCheck"
    ["UserProfileHealthCheck"]="SemanticStoreHealthCheck"
)

for file in $(find . -name "*.cs" -type f); do
    for old in "${!replacements[@]}"; do
        new="${replacements[$old]}"
        sed -i "s/${old}/${new}/g" "$file"
    done
done

echo "Migration complete! Run 'dotnet build' to verify."
```

## Configuration Migration Example

### Before (v0.3.0) - Full Config

```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "SqliteVec",
      "ConnectionString": "memory.db"
    },
    "Embedding": {
      "Provider": "Ollama",
      "Model": "bge-m3",
      "Dimensions": 1024
    },
    "VCM": {
      "WorkingMemory": {
        "Capacity": 7,
        "DefaultTtl": "00:10:00"
      },
      "RecentlyBuffer": {
        "MaxIdleSeconds": 60,
        "TokenThreshold": 500,
        "TurnThreshold": 3
      },
      "WorkingOrchestrator": {
        "IdleTimeout": "00:10:00",
        "TokenThreshold": 2000,
        "TurnThreshold": 10
      },
      "UserProfile": {
        "MinConfirmationCount": 3,
        "MinConfidenceThreshold": 0.8,
        "ConfidenceBoostPerConfirmation": 0.1,
        "MaxEntriesPerUser": 500
      }
    }
  }
}
```

### After (v0.4.0) - Full Config

```json
{
  "MemoryIndexer": {
    "Storage": {
      "Type": "SqliteVec",
      "ConnectionString": "memory.db"
    },
    "Embedding": {
      "Provider": "Ollama",
      "Model": "bge-m3",
      "Dimensions": 1024
    },
    "VCM": {
      "WorkingMemory": {
        "Capacity": 7,
        "DefaultTtl": "00:10:00"
      },
      "SensoryBuffer": {
        "MaxIdleSeconds": 60,
        "TokenThreshold": 500,
        "TurnThreshold": 3
      },
      "WorkingOrchestrator": {
        "IdleTimeout": "00:10:00",
        "TokenThreshold": 2000,
        "TurnThreshold": 10
      },
      "SemanticStore": {
        "MinConfirmationCount": 3,
        "MinConfidenceThreshold": 0.8,
        "ConfidenceBoostPerConfirmation": 0.1,
        "MaxEntriesPerUser": 500
      }
    }
  }
}
```

**Changes**:
- `RecentlyBuffer` → `SensoryBuffer`
- `UserProfile` → `SemanticStore`

## Validation Checklist

After migration, verify:

- [ ] **Build**: `dotnet build` completes with 0 errors
- [ ] **Tests**: All 848 tests pass (`dotnet test`)
- [ ] **Configuration**: appsettings.json updated with new keys
- [ ] **Health Checks**: Health endpoint returns correct tier names
- [ ] **Runtime**: Application starts without errors
- [ ] **Logging**: Log messages reference new terminology
- [ ] **Documentation**: Internal docs updated with new names

## Troubleshooting

### Issue: "Type or namespace 'IRecentlyBuffer' could not be found"

**Solution**: Run find & replace from Step 1 to update all interface references.

---

### Issue: "Configuration section 'VCM:RecentlyBuffer' not found"

**Solution**: Update appsettings.json with new configuration keys (Step 3).

---

### Issue: "Cannot resolve service for type 'IUserProfile'"

**Solution**:
1. If using manual DI: Update service registration (Step 4)
2. If using `AddMemoryIndexer()`: Update package to v0.4.0

---

### Issue: Tests failing with "Object reference not set to an instance"

**Solution**: Check test setup code for missed option property renames:
- `RecentlyBuffer` → `SensoryBuffer`
- `UserProfile` → `SemanticStore`

---

### Issue: Health checks returning old tier names

**Solution**: Update health check registration to use new health check classes (Step 6).

## FAQ

### Q: Can I use deprecated aliases during migration?

**A**: No. v0.4.0 does NOT include deprecated aliases. This is a clean break for v0.x stage.

**Rationale**: v0.x allows breaking changes. Clean migration without deprecation debt.

---

### Q: Will my existing database/storage be affected?

**A**: No. Storage schema unchanged. Only code-level interface/class names changed.

**Migration Required**: None for data. Only code changes needed.

---

### Q: Do I need to migrate all at once?

**A**: Yes. v0.4.0 is a breaking change with no backward compatibility.

**Recommended Approach**:
1. Create migration branch
2. Run automated find & replace scripts
3. Update configuration manually
4. Build & test
5. Merge when green

---

### Q: How long does migration typically take?

**A**: ~30 minutes for typical projects.

**Breakdown**:
- 5 min: Run automated scripts (Step 1-2)
- 5 min: Update configuration (Step 3)
- 10 min: Update tests (Step 5)
- 10 min: Build, test, fix issues (Step 8)

---

### Q: What if I have custom implementations of old interfaces?

**A**: Rename your implementations to match new interface names.

**Example**:
```csharp
// Before
public class CustomRecentlyBuffer : IRecentlyBuffer { }

// After
public class CustomSensoryBuffer : ISensoryBuffer { }
```

---

### Q: Will future versions maintain backward compatibility?

**A**: v1.0+ will use semantic versioning with proper deprecation cycles.

**v0.x Stage**: Breaking changes allowed
**v1.0+ Stage**: Semantic versioning, deprecation warnings, migration guides

## Support

**Issues**: https://github.com/iyulab/memory-indexer/issues
**Discussions**: https://github.com/iyulab/memory-indexer/discussions

**Migration Help**:
- Tag issue with `migration` label
- Include error messages and stack traces
- Provide minimal reproduction if possible

## See Also

- [Architecture Documentation](ARCHITECTURE.md)
- [Tier × Type Matrix](TIER_TYPE_MATRIX.md)
- [Cognitive Science Foundation](VISION.md)
- [Changelog](../CHANGELOG.md)
