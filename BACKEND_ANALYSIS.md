# Kong Backend Architecture Analysis
## CLR Emission, Runtime, and Interop Readiness Assessment

**Analysis Date**: February 2026  
**Project**: Kong Language Compiler  
**Scope**: IR Lowering, CLR Emission, Runtime Builtins, Modular Compilation Prep

---

## EXECUTIVE SUMMARY

The Kong backend demonstrates a **well-structured, but tightly-coupled three-layer architecture**:
1. **IR Lowering** (AST → Intermediate Representation)
2. **CLR Emission** (IR → Mono.Cecil bytecode generation)
3. **Runtime Builtins** (execution-time function library)

**Key Finding**: The backend is **operationally sound but architecturally fragmented** for future BCL interop and modular compilation. Critical dependencies are **entangled across three files** with **hardcoded builtin mappings**, **runtime type assumptions**, and **monolithic code generation**.

---

## PART 1: TOP FINDINGS RANKED BY IMPACT

### 1. **CRITICAL: Hardcoded Builtin Name-to-Method Mapping (HIGH IMPACT)**

**Location**: `src/Kong/ClrPhase1Executor.cs:927-942` (`BuildBuiltinMap`)

**Problem**: Builtin functions mapped via hardcoded string keys duplicated across:
- `IrLowerer.cs:927-984` (TryLowerBuiltinCallName - decides which builtin to call)
- `ClrPhase1Executor.cs:927-942` (BuildBuiltinMap - resolves IL methods)
- `ClrRuntimeBuiltins.cs:3-64` (actual implementations)

**Evidence**:
```csharp
// IrLowerer.cs:962-966
if (identifier.Value == "puts" && callExpression.Arguments.Count == 1 &&
    TryGetExpressionType(callExpression.Arguments[0], out var putsArgTypeInt) && 
    putsArgTypeInt == TypeSymbols.Int)
{
    builtinName = "__builtin_puts_int";  // <-- String literal duplicate
    return true;
}

// ClrPhase1Executor.cs:934 (separate mapping)
["__builtin_puts_int"] = module.ImportReference(
    runtimeType.GetMethod(nameof(ClrRuntimeBuiltins.PutsInt))!),
```

**Why It Matters**:
- No single source of truth - adding builtins requires changes in 3 places
- Type-dispatch scattered - different overloads are hardcoded conditionals
- NuGet interop blocker - external libraries can't register new builtins
- Test brittleness - string constant drift causes silent failures

**Remediation Impact**: ⭐⭐⭐⭐⭐ (touches 3 files, affects extensibility)

---

### 2. **CRITICAL: Builtin Type Specialization Explosion (HIGH IMPACT)**

**Location**: `src/Kong/IrLowerer.cs:927-984` (TryLowerBuiltinCallName)

**Problem**: Each builtin is monomorphic with manual type-dispatch logic

**Evidence**:
```csharp
// IrLowerer.cs:955-960 - ONE of many overload checks
if (identifier.Value == "len" && callExpression.Arguments.Count == 1 &&
    TryGetExpressionType(callExpression.Arguments[0], out var argType) && 
    argType == TypeSymbols.String)
{
    builtinName = "__builtin_len_string";
    return true;
}
// Would need similar for: __builtin_len_array, __builtin_len_custom_type, etc.
```

**Why It Matters**:
- Scalability wall - each new type duplicates conditionals
- BCL interop impossible - can't bridge Kong types to System.String/System.Collections methods
- Arity explosion - Array methods (first/last/rest/push) hardcoded for int[] only

**Remediation Impact**: ⭐⭐⭐⭐ (architectural rethink needed)

---

### 3. **HIGH: Type Mapping Hardcoding in CLR Emission (HIGH IMPACT)**

**Location**: `src/Kong/ClrPhase1Executor.cs:641-685` (`MapType`)

**Problem**: Static method handles Kong types → CLR types with hardcoded if-chains

**Evidence**:
```csharp
private static TypeReference? MapType(
    TypeSymbol type,
    ModuleDefinition module,
    DiagnosticBag diagnostics,
    IReadOnlyDictionary<string, TypeDefinition> delegateTypeMap)
{
    if (type == TypeSymbols.Int)
        return module.TypeSystem.Int64;
    if (type == TypeSymbols.Bool)
        return module.TypeSystem.Boolean;
    if (type == TypeSymbols.String)
        return module.TypeSystem.String;
    // ... no extensibility point for custom types/generics
}
```

**Why It Matters**:
- No way to bind external types (can't say "Kong's MyType maps to System.Collections.Generic.List<T>")
- Generic types unsupported
- NuGet interop requires this feature

**Remediation Impact**: ⭐⭐⭐⭐⭐ (central to all type flow)

---

### 4. **HIGH: Monolithic IR → CLR Code Generation (HIGH IMPACT)**

**Location**: `src/Kong/ClrPhase1Executor.cs:150-256` (EmitAssembly) & `258-639` (EmitFunction)

**Problem**: Single method orchestrates all module creation; no clear abstraction layers

**Evidence**:
```csharp
// EmitAssembly:
// 1. Create module/program type
// 2. Build delegate map
// 3. Create method definitions
// 4. Create main entry point
// 5. Build display classes
// 6. Emit function bodies
// 7. Write assembly
// Each step tightly coupled to previous

foreach (var function in allFunctions) {
    // ... create method definition ...
    methodMap[function.Name] = method;
}

var displayClassMap = new Dictionary<string, DisplayClassInfo>();
foreach (var function in allFunctions.Where(f => f.CaptureParameterCount > 0)) {
    var displayClass = BuildDisplayClass(function, methodMap, module, programType, diagnostics, delegateTypeMap);
    displayClassMap[function.Name] = displayClass;
}

foreach (var function in allFunctions) {
    if (!EmitFunction(function, methodMap[function.Name], ...)) return null;
}
```

**Why It Matters**:
- Can't separate backend phases (lowering/generation/optimization)
- Testing difficulty - hard to unit test individual emission steps
- Multi-pass complexity - functions known before IR emission
- Modular compilation blocker - can't compile one module independently

**Remediation Impact**: ⭐⭐⭐⭐ (pervasive but refactorable)

---

### 5. **MEDIUM: Runtime Builtin Coupling (MEDIUM IMPACT)**

**Location**: `src/Kong/ClrRuntimeBuiltins.cs:3-64`

**Problem**: Implementations tightly bound to Kong's type assumptions

**Evidence**:
```csharp
public static long FirstIntArray(long[] values)
{
    if (values.Length == 0)
        throw new InvalidOperationException("first() called on empty array");
    return values[0];
}
// Assumptions:
// 1. Long (int64) is the only array element type
// 2. InvalidOperationException is appropriate error
// 3. No way to pass context/logger/allocator
// 4. Method name hardcoded to match IL emission
```

**Why It Matters**:
- No extensibility - can't plug in custom allocators or logging
- Behavioral coupling - compiler + runtime tightly bound on semantics
- AOT incompatibility - exception-based control flow problematic for AOT
- Version skew - if builtins change, old assemblies break

**Remediation Impact**: ⭐⭐⭐ (mostly encapsulated)

---

### 6. **MEDIUM: IR Lowering Type Assumptions (MEDIUM IMPACT)**

**Location**: `src/Kong/IrLowerer.cs:1118-1132` (IsSupportedRuntimeType)

**Problem**: Runtime type support hardcoded in lowerer; no registry/plugin mechanism

**Evidence**:
```csharp
private static bool IsSupportedRuntimeType(TypeSymbol type)
{
    if (type == TypeSymbols.Void)
        return true;
    
    return type == TypeSymbols.Int ||
           type == TypeSymbols.Bool ||
           type == TypeSymbols.String ||
           type is ArrayTypeSymbol { ElementType: IntTypeSymbol } ||
           type is FunctionTypeSymbol functionType &&
           functionType.ParameterTypes.All(IsSupportedRuntimeType) &&
           IsSupportedRuntimeType(functionType.ReturnType);
}
```

**Why It Matters**:
- Custom types blocked - language can't grow beyond hardcoded types
- Modular compilation unsupported - each module needs same type whitelist
- No staging - types can't be gradually supported

**Remediation Impact**: ⭐⭐⭐ (affects extensibility)

---

### 7. **MEDIUM: Large IR → IL Switch Statement (MEDIUM IMPACT)**

**Location**: `src/Kong/ClrPhase1Executor.cs:327-596` (EmitFunction switch)

**Problem**: Large switch statement dispatching IR instructions; each case tightly bound to IL emission

**Impact**:
- No separation of concerns - IR structure married to IL emission strategy
- Testing limitation - can't test IR generation without full IL emission
- Future optimization passes blocked - can't insert optimization IR visitors
- Interpretation impossible - can't easily interpret IR for debugging

**Remediation Impact**: ⭐⭐⭐ (code quality issue, not blocking)

---

### 8. **LOW: Mono.Cecil Dependency Tightly Bound (LOW PRIORITY)**

**Location**: Throughout `src/Kong/ClrPhase1Executor.cs`

**Problem**: Mono.Cecil types leak throughout emission logic; no abstraction over IL generation

**Why It Matters**:
- Backend lock-in - moving to different IL or code generation is costly
- AOT incompatibility - Mono.Cecil uses reflection
- NuGet interop complicated - can't use Roslyn for BCL method bridging

**Remediation Impact**: ⭐⭐ (long-term investment)

---

## PART 2: RECOMMENDED STRUCTURAL CHANGES

### 2.1 Extract Builtin Registry (PHASE 1)

**New File**: `src/Kong/BuiltinRegistry.cs`

```csharp
public record class BuiltinSignature(
    string PublicName,
    string IrFunctionName,
    IReadOnlyList<TypeSymbol> ParameterTypes,
    TypeSymbol ReturnType);

public record class BuiltinBinding(
    BuiltinSignature Signature,
    string RuntimeMethodName,
    Type RuntimeType);

public class BuiltinRegistry
{
    private readonly Dictionary<string, List<BuiltinBinding>> _bindings = [];
    
    public void Register(BuiltinBinding binding) { ... }
    
    public BuiltinBinding? ResolveByTypeSignature(
        string publicName,
        IReadOnlyList<TypeSymbol> paramTypes) { ... }
    
    public static BuiltinRegistry CreateDefault() { ... }
}
```

**Impact**:
- Single source of truth for all builtins
- Enables runtime registration (for plugins/NuGet)
- Makes testing easier (can inject test registry)

**Files Modified**:
- Create: `src/Kong/BuiltinRegistry.cs` (NEW)
- Update: `IrLowerer.cs` (use registry instead of hardcoded checks)
- Update: `ClrPhase1Executor.cs` (use registry for IL method mapping)
- Create: `tests/Kong.Tests/BuiltinRegistryTests.cs`

---

### 2.2 Type Mapping Architecture (PHASE 1)

**New File**: `src/Kong/TypeMapping/ITypeMapper.cs`

```csharp
public interface ITypeMapper
{
    TypeReference? TryMapKongType(
        TypeSymbol kongType,
        ModuleDefinition module,
        DiagnosticBag diagnostics);
    
    bool IsTypeSupported(TypeSymbol type);
}

public class DefaultTypeMapper : ITypeMapper { ... }
public class CompositeTypeMapper : ITypeMapper { ... }
```

**Impact**:
- Pluggable type system for NuGet/BCL interop
- Easy to test type mapping independently
- Supports composition (user mappers + defaults)

**Files Modified**:
- Create: `src/Kong/TypeMapping/ITypeMapper.cs` (NEW)
- Create: `src/Kong/TypeMapping/DefaultTypeMapper.cs` (NEW)
- Update: `IrLowerer.cs` (inject ITypeMapper)
- Update: `ClrPhase1Executor.cs` (inject ITypeMapper)

---

### 2.3 IR Emission Visitor Pattern (PHASE 2)

**New File**: `src/Kong/Emission/IrEmissionVisitor.cs`

```csharp
public abstract class IrEmissionVisitor
{
    public abstract void Visit(IrConstInt instruction, EmissionContext ctx);
    public abstract void Visit(IrConstBool instruction, EmissionContext ctx);
    public abstract void Visit(IrBinary instruction, EmissionContext ctx);
    // ... one method per IR instruction type ...
}

public class ClrIlEmissionVisitor : IrEmissionVisitor { ... }
```

**Impact**:
- IR can be validated without emission
- Multiple backends possible (interpreted, debug, native)
- Testable in isolation

---

### 2.4 Compilation Context Extraction (PHASE 2)

**New File**: `src/Kong/Compilation/CompilationContext.cs`

```csharp
public class CompilationContext
{
    public required BuiltinRegistry Builtins { get; init; }
    public required ITypeMapper TypeMapper { get; init; }
    public required DiagnosticBag Diagnostics { get; init; }
    
    public Dictionary<string, string> ExternalTypeBindings { get; } = [];
    public Dictionary<string, string> ExternalMethodBindings { get; } = [];
}
```

**Impact**:
- Enables dependency injection throughout pipeline
- Supports modular compilation (each module has own context)
- Enables future: multi-module symbol resolution

---

## PART 3: EXACT FILE REFERENCES & SYMBOLS

### Critical Functions to Refactor

| Symbol | File | Lines | Status |
|--------|------|-------|--------|
| `IrLowerer.TryLowerBuiltinCallName()` | IrLowerer.cs | 927-984 | Depends on BuiltinRegistry |
| `ClrPhase1Executor.BuildBuiltinMap()` | ClrPhase1Executor.cs | 927-942 | Eliminate (migrate to IBuiltinProvider) |
| `ClrPhase1Executor.MapType()` | ClrPhase1Executor.cs | 641-685 | Extract to ITypeMapper |
| `ClrPhase1Executor.EmitAssembly()` | ClrPhase1Executor.cs | 150-256 | Refactor into phases |
| `ClrPhase1Executor.EmitFunction()` | ClrPhase1Executor.cs | 258-639 | Extract IR visitor |
| `IrLowerer.IsSupportedRuntimeType()` | IrLowerer.cs | 1118-1132 | Move to ITypeMapper |
| `ClrRuntimeBuiltins` class | ClrRuntimeBuiltins.cs | 3-64 | Wrap with IBuiltinProvider |
| `BuiltinNames` class | BuiltinNames.cs | 3-14 | Move definitions to BuiltinRegistry |

### Type Symbols to Audit

| Symbol | File | Lines | Note |
|--------|------|-------|------|
| `TypeSymbol` (abstract) | Types.cs | 3-11 | Base for all types; need extension mechanism |
| `IntTypeSymbol` | Types.cs | 13-22 | Singleton; OK |
| `FunctionTypeSymbol` | Types.cs | 84-87 | Needs Name property (for delegate mapping) |
| `ArrayTypeSymbol` | Types.cs | 79-82 | Only int[] supported; need generalization |
| `TypeSymbols` (static class) | Types.cs | 89-110 | Registry for all built-in types |

### IR Classes

| Symbol | File | Lines | Note |
|--------|------|-------|------|
| `IrInstruction` (abstract record) | Ir.cs | 39 | Base for all IR; needs visitor support |
| `IrProgram` | Ir.cs | 13-17 | Entry point holds one function; multi-module support needed |
| `IrFunction` | Ir.cs | 19-28 | No metadata (source location, doc strings, attributes) |
| `IrBlock` | Ir.cs | 32-37 | OK structure |

---

## PART 4: SEQUENCING & EXECUTION PLAN

### Phase 1: Foundation (Weeks 1-2)

**Goal**: Establish patterns that unblock BCL interop.

#### Task 1.1: Create BuiltinRegistry
**Files**:
- NEW: `src/Kong/BuiltinRegistry.cs`
- NEW: `tests/Kong.Tests/BuiltinRegistryTests.cs`

**Effort**: 3 hours | **Risk**: LOW (new, no refactoring)

#### Task 1.2: Refactor IrLowerer to use BuiltinRegistry
**Files**:
- MODIFY: `src/Kong/IrLowerer.cs` (lines 927-984)
- MODIFY: `tests/Kong.Tests/IrLowererTests.cs`

**Effort**: 4 hours | **Risk**: MEDIUM (behavior-preserving refactor) | **Blocker**: Task 1.1

#### Task 1.3: Extract ITypeMapper Interface
**Files**:
- NEW: `src/Kong/TypeMapping/ITypeMapper.cs`
- NEW: `src/Kong/TypeMapping/DefaultTypeMapper.cs`
- NEW: `tests/Kong.Tests/TypeMapperTests.cs`

**Effort**: 3 hours | **Risk**: LOW (new abstraction)

#### Task 1.4: Update ClrPhase1Executor to use BuiltinRegistry + ITypeMapper
**Files**:
- MODIFY: `src/Kong/ClrPhase1Executor.cs` (lines 150-256, 641-685, 927-942)
- MODIFY: `tests/Kong.Tests/ClrPhase1ExecutorTests.cs`

**Effort**: 5 hours | **Risk**: MEDIUM (refactor core emission) | **Blockers**: Tasks 1.1, 1.2, 1.3

#### Phase 1 Outcome
- ✅ Builtin registry centralized
- ✅ Type mapping pluggable
- ✅ Tests pass (no behavior change)
- ✅ Foundation for NuGet interop

---

### Phase 2: Modularity (Weeks 3-4)

**Goal**: Prepare for multi-module compilation.

#### Task 2.1: Create CompilationContext
**Files**:
- NEW: `src/Kong/Compilation/CompilationContext.cs`
- MODIFY: `src/Kong/IrLowerer.cs` (add context param)
- MODIFY: `src/Kong/ClrPhase1Executor.cs` (add context param)
- MODIFY: `src/Kong.Cli/Commands/CommandCompilation.cs`

**Effort**: 4 hours | **Risk**: MEDIUM (parameter threading) | **Blocker**: Phase 1

#### Task 2.2: Visitor Pattern for IR Emission
**Files**:
- NEW: `src/Kong/Emission/IrEmissionVisitor.cs`
- NEW: `src/Kong/Emission/ClrIlEmissionVisitor.cs`
- MODIFY: `src/Kong/ClrPhase1Executor.cs`

**Effort**: 6 hours | **Risk**: HIGH (large refactor of EmitFunction) | **Blocker**: Phase 1

#### Phase 2 Outcome
- ✅ Context plumbing established
- ✅ IR emission decoupled from IL
- ✅ Ready for alternative backends

---

### Phase 3: BCL Interop (Weeks 5-6)

**Goal**: Enable binding Kong types to BCL types.

#### Task 3.1: External Type Binding Registry
**Files**:
- NEW: `src/Kong/TypeMapping/ExternalTypeMapper.cs`
- MODIFY: `src/Kong/Compilation/CompilationContext.cs`
- MODIFY: `src/Kong.Cli/Commands/Build.cs`

**Effort**: 5 hours | **Risk**: MEDIUM | **Blocker**: Phase 2

#### Task 3.2: External Method Binding
**Files**:
- NEW: `src/Kong/Emission/ExternalMethodResolver.cs`
- MODIFY: `IrLowerer.cs`
- MODIFY: `ClrPhase1Executor.cs`

**Effort**: 6 hours | **Risk**: HIGH | **Blocker**: Task 3.1

#### Phase 3 Outcome
- ✅ Kong types can map to BCL types
- ✅ Kong functions can call BCL methods

---

### Phase 4: Modular Compilation (Weeks 7-8)

**Goal**: Enable separate compilation of modules.

#### Task 4.1: Multi-Module IR Program
**Files**:
- MODIFY: `src/Kong/Ir.cs`
- MODIFY: `src/Kong/IrLowerer.cs`
- NEW: `src/Kong/Emission/ModuleLinker.cs`

**Effort**: 5 hours | **Risk**: MEDIUM | **Blocker**: Phase 2

#### Task 4.2: Per-Module Symbol Export
**Files**:
- NEW: `src/Kong/Compilation/ModuleMetadata.cs`
- MODIFY: `ClrPhase1Executor.cs`

**Effort**: 6 hours | **Risk**: HIGH | **Blocker**: Task 4.1

#### Phase 4 Outcome
- ✅ Modules can be compiled separately
- ✅ Each module has stable ABI

---

## PART 5: RISK ASSESSMENT & MITIGATION

### Risk Matrix

| Risk | Phase | Probability | Impact | Mitigation |
|------|-------|-------------|--------|-----------|
| Regressions in builtin calls | 1 | MEDIUM | HIGH | Comprehensive test suite for each builtin |
| Type mapping inconsistencies | 1-2 | MEDIUM | HIGH | Freeze type symbol API; extensive testing |
| IR visitor refactor introduces bugs | 2 | HIGH | HIGH | Gradual migration; dual paths initially |
| Context threading breaks CLI | 2 | MEDIUM | MEDIUM | Incremental threading; test CLI early |
| External type binding conflicts | 3 | LOW | MEDIUM | Namespace resolution; clear error messages |
| Module linking breaks existing code | 4 | MEDIUM | HIGH | Non-breaking: single-module mode still works |

### Go/No-Go Criteria

**Phase 1**: All existing tests pass + no behavioral changes + new tests >90% coverage

**Phase 2**: IR visitor emits identical IL + CompilationContext threading complete + CLI works

**Phase 3**: External bindings work + no runtime failures + documentation complete

**Phase 4**: Single-module programs work + multi-module programs compile + symbol export/import tested

---

## CONCLUSION

The Kong backend requires **structural changes** to support BCL interop and modular compilation. The four-phase plan (16-20 weeks, ~40-50 hours) addresses top architectural debts:

1. **Phase 1 (Foundation)**: Centralize builtins and type mapping
2. **Phase 2 (Modularity)**: IR visitor pattern and context threading
3. **Phase 3 (BCL Interop)**: External type/method binding
4. **Phase 4 (Multi-module)**: Module ABI and linking

**High-ROI investments** (do first):
1. BuiltinRegistry + ITypeMapper (Phase 1) - unblocks all future work
2. CompilationContext (Phase 2) - enables dependency injection
3. IR Visitor pattern (Phase 2) - decouples backends

**Success Criteria**: Preserve all existing behavior (test-driven refactoring), no public API breaks, comprehensive test coverage.
