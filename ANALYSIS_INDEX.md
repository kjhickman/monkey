# Kong Backend Architecture Analysis - Complete Deliverables

## Overview

This analysis examines the Kong compiler backend (IR lowering, CLR emission, runtime builtins) to identify architectural cleanup opportunities in preparation for:
1. BCL (Base Class Library) interop
2. Future NuGet package interop
3. Modular compilation support

**Status**: ✅ Complete Analysis with actionable recommendations

---

## Deliverables

### 1. **Main Analysis Document** (`BACKEND_ANALYSIS.md`)
- **Length**: 577 lines
- **Format**: Markdown with code examples
- **Contains**:
  - Executive summary of findings
  - 8 detailed findings ranked by impact (⭐ 1-5)
  - Root cause analysis with code evidence
  - Recommended structural changes (4 major refactors)
  - Exact file references and symbols
  - 4-phase execution plan with sequencing
  - Risk assessment and mitigation strategies
  - Success criteria and go/no-go gates
  - Measurement metrics and validation strategy

### 2. **Quick Reference Summary** (`BACKEND_ANALYSIS_SUMMARY.txt`)
- **Length**: 284 lines
- **Format**: Plain text tables and bullet points
- **Use Case**: For quick lookup during implementation
- **Contains**:
  - Top 8 findings with impact ratings
  - 4-phase plan overview (15h + 10h + 11h + 11h = 47 hours)
  - Exact file references table
  - New files to create (14 files total)
  - Key metrics (current vs. target)
  - Risk assessment matrix
  - Go/no-go gates for each phase
  - Success criteria checklist

---

## Top Findings Summary

### Priority 1 - Critical (MUST FIX FOR INTEROP)

1. **Hardcoded Builtin Mappings** (⭐⭐⭐⭐⭐)
   - 3 places, no single source of truth
   - Blocks NuGet interop, test brittleness
   - **Fix**: Create BuiltinRegistry.cs

2. **Type Mapping Hardcoding** (⭐⭐⭐⭐⭐)
   - No extensibility for custom/BCL types
   - Blocks BCL method binding, generics
   - **Fix**: Extract ITypeMapper interface

### Priority 2 - High (ARCHITECTURAL DEBT)

3. **Builtin Type Specialization** (⭐⭐⭐⭐)
   - Monomorphic dispatch, explosion with each type
   - **Fix**: Registry with type-signature resolution

4. **Monolithic IR → CLR Emission** (⭐⭐⭐⭐)
   - Single method orchestrates 7 steps
   - Can't test phases independently
   - **Fix**: Visitor pattern + phase extraction

### Priority 3 - Medium (CODE QUALITY)

5. **Runtime Builtin Coupling** (⭐⭐⭐)
6. **IR Type Assumptions** (⭐⭐⭐)
7. **Large IR → IL Switch Statement** (⭐⭐⭐)
8. **Mono.Cecil Tight Coupling** (⭐⭐)

---

## Implementation Roadmap

### Phase 1: Foundation (Weeks 1-2, ~15 hours)
**Goal**: Centralize builtins and type mapping, unblock NuGet interop

**New Files**:
- `src/Kong/BuiltinRegistry.cs`
- `src/Kong/TypeMapping/ITypeMapper.cs`
- `src/Kong/TypeMapping/DefaultTypeMapper.cs`

**Modified Files**:
- `IrLowerer.cs` (lines 927-984)
- `ClrPhase1Executor.cs` (lines 641-685, 927-942)

**Effort**: 4 tasks × 3-5 hours each
**Risk**: MEDIUM (behavior-preserving refactor)

### Phase 2: Modularity (Weeks 3-4, ~10 hours)
**Goal**: IR decoupling, context threading

**New Files**:
- `src/Kong/Compilation/CompilationContext.cs`
- `src/Kong/Emission/IrEmissionVisitor.cs`
- `src/Kong/Emission/ClrIlEmissionVisitor.cs`

**Modified Files**:
- `ClrPhase1Executor.cs` (lines 258-639)

**Effort**: 2 tasks × 4-6 hours each
**Risk**: HIGH (large refactor of EmitFunction)

### Phase 3: BCL Interop (Weeks 5-6, ~11 hours)
**Goal**: Enable Kong ↔ BCL type/method binding

**New Files**:
- `src/Kong/TypeMapping/ExternalTypeMapper.cs`
- `src/Kong/Emission/ExternalMethodResolver.cs`

**Modified Files**:
- `IrLowerer.cs` (builtin dispatch)
- `ClrPhase1Executor.cs` (method emission)

**Effort**: 2 tasks × 5-6 hours each
**Risk**: MEDIUM-HIGH (signature validation needed)

### Phase 4: Modular Compilation (Weeks 7-8, ~11 hours)
**Goal**: Multi-module compilation with stable ABI

**New Files**:
- `src/Kong/Emission/ModuleLinker.cs`
- `src/Kong/Compilation/ModuleMetadata.cs`

**Modified Files**:
- `Ir.cs` (add IrModule)
- `IrLowerer.cs` (module-aware lowering)

**Effort**: 2 tasks × 5-6 hours each
**Risk**: HIGH (IR structure change)

---

## Exact File References

### Critical Functions (In Order of Importance)

| Priority | Function | File | Lines | Phase |
|----------|----------|------|-------|-------|
| 1 | `MapType()` | ClrPhase1Executor.cs | 641-685 | P1 |
| 1 | `TryLowerBuiltinCallName()` | IrLowerer.cs | 927-984 | P1 |
| 1 | `BuildBuiltinMap()` | ClrPhase1Executor.cs | 927-942 | P1 |
| 2 | `EmitFunction()` | ClrPhase1Executor.cs | 258-639 | P2 |
| 2 | `EmitAssembly()` | ClrPhase1Executor.cs | 150-256 | P2 |
| 2 | `IsSupportedRuntimeType()` | IrLowerer.cs | 1118-1132 | P1 |
| 3 | `ClrRuntimeBuiltins` | ClrRuntimeBuiltins.cs | 3-64 | P1-3 |

### Type Symbols to Audit

- `TypeSymbol` (abstract base) - src/Kong/Types.cs:3-11
- `FunctionTypeSymbol` - src/Kong/Types.cs:84-87 (needs Name property)
- `ArrayTypeSymbol` - src/Kong/Types.cs:79-82 (int[] only)
- `TypeSymbols` (static registry) - src/Kong/Types.cs:89-110

### IR Classes to Extend

- `IrInstruction` (abstract record) - src/Kong/Ir.cs:39
- `IrProgram` - src/Kong/Ir.cs:13-17 (needs multi-module support)
- `IrFunction` - src/Kong/Ir.cs:19-28 (needs metadata)

---

## Key Metrics

### Current State
- 5 hardcoded type mapping cases
- 2 builtin dispatch sites (scattered)
- ~30 cyclomatic complexity in EmitFunction
- No extensibility for custom types
- No BCL binding support
- Monolithic emission phase

### Target State (After Phase 4)
- 0 hardcoded type cases (all in ITypeMapper)
- 1 builtin dispatch site (registry)
- ~15 cyclomatic complexity (visitor-based)
- Pluggable type mapper for custom types
- External type/method binding support
- Separated, testable emission phases
- Multi-module compilation support

---

## Testing Strategy

### Unit Tests
- BuiltinRegistry resolution (100% coverage)
- ITypeMapper implementations (100% coverage)
- IR visitors (100% coverage)
- CompilationContext threading

### Integration Tests
- Existing semantic golden tests (no regressions)
- External type binding end-to-end
- Multi-module linking

### Smoke Tests
```bash
# Phase 1: Verify basic functionality preserved
dotnet test -- --filter "IrLowerer or ClrPhase1Executor"

# Phase 2: Verify visitor emission identical
dotnet run --project src/Kong.Cli -- run examples/hello.kg

# Phase 3: Verify BCL bindings work
dotnet run --project src/Kong.Cli -- build examples/hello.kg \
  --bind-type MyType=System.String

# Phase 4: Verify multi-module compilation
dotnet run --project src/Kong.Cli -- build module1.kg module2.kg
```

---

## Risk Assessment

### Go/No-Go Criteria

**Phase 1 Complete** when:
- ✅ All existing tests pass
- ✅ No behavioral changes in compiler output
- ✅ New BuiltinRegistry tests >90% coverage
- ✅ `dotnet build` clean
- ✅ `dotnet test` all pass

**Phase 2 Complete** when:
- ✅ IR visitor emits identical IL to original code
- ✅ CompilationContext threading complete
- ✅ CLI commands still work
- ✅ All tests pass

**Phase 3 Complete** when:
- ✅ External type bindings work with System.* types
- ✅ No runtime failures with BCL method calls
- ✅ Documentation updated
- ✅ Integration tests pass

**Phase 4 Complete** when:
- ✅ Single-module programs work identically
- ✅ Multi-module programs compile and link
- ✅ Symbol export/import validated
- ✅ All tests pass

---

## Success Criteria

1. **No Breaking Changes**
   - Preserve all existing behavior
   - CLI interface unchanged
   - All existing tests pass

2. **Code Quality**
   - Test-driven refactoring
   - >90% coverage for new abstractions
   - Clear separation of concerns

3. **Documentation**
   - Updated BACKEND_ANALYSIS.md
   - API docs for BuiltinRegistry, ITypeMapper, CompilationContext
   - Examples for external type binding

4. **Extensibility**
   - NuGet packages can provide type mappers
   - New builtins can be registered at runtime
   - Alternative backends (Roslyn, LLVM) possible

---

## Timeline

| Phase | Duration | Effort | Risk | Blockers |
|-------|----------|--------|------|----------|
| 1: Foundation | Weeks 1-2 | 15h | MEDIUM | None |
| 2: Modularity | Weeks 3-4 | 10h | HIGH | Phase 1 |
| 3: BCL Interop | Weeks 5-6 | 11h | MEDIUM | Phase 2 |
| 4: Multi-Module | Weeks 7-8 | 11h | HIGH | Phase 2 |
| **Total** | **8 weeks** | **47h** | - | - |

---

## How to Use This Analysis

1. **For Decision Making**: Read BACKEND_ANALYSIS.md executive summary + Part 1 (top 8 findings)
2. **For Planning**: Use BACKEND_ANALYSIS_SUMMARY.txt timeline and risk assessment
3. **For Implementation**: Reference exact file locations in Part 3
4. **For Testing**: Follow Phase X go/no-go criteria

---

## Document Structure

```
ANALYSIS_INDEX.md (this file)
├── Quick overview
├── Phase breakdown with effort estimates
├── Risk assessment
└── Links to detailed docs

BACKEND_ANALYSIS.md (detailed)
├── Part 1: Top 8 findings (detailed analysis)
├── Part 2: Structural changes (4 major refactors)
├── Part 3: Exact file references
├── Part 4: 4-phase execution plan
└── Part 5: Risk assessment & validation

BACKEND_ANALYSIS_SUMMARY.txt (quick reference)
├── 1-page findings summary
├── Phase overview
├── File reference tables
├── Go/no-go gates
└── Timeline
```

---

## Questions & Contact

For questions about this analysis:
1. Check BACKEND_ANALYSIS.md (comprehensive) 
2. Check BACKEND_ANALYSIS_SUMMARY.txt (quick lookup)
3. Verify findings by examining source files at line references

---

**Analysis Date**: February 20, 2026  
**Status**: ✅ Ready for implementation  
**Recommendation**: Start with Phase 1 (Foundation) immediately - it unblocks all future work
