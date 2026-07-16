# JAutoMapper Improvement Plan

## Current State

- **Tests**: **41/41 pass** (was 40 pass + 1 crash).

## Root Cause of Crash (Fixed)

`PreserveReferences()` used multiple `[ThreadStatic]` fields (`_preserveRefs`, `_preserveRefDepth`, `_recursionDepth`, `_resolutionContext`). The dictionary of already-mapped instances was stored in a `[ThreadStatic]` that was not reliably accessible from within compiled expression-tree delegates, causing the cache lookup to fail on recursive calls and resulting in infinite recursion → stack overflow.

**Fix**: Replaced all 4 scattered `[ThreadStatic]` fields with a single `[ThreadStatic] ResolutionContext? _currentContext`. The `ResolutionContext` class now holds `Depth` and `InstanceCache` on a single object that follows the call stack. `Map<TSource,TDest>` creates a context at the entry point if none exists, and nested calls reuse the same context object via the thread-static — avoiding the stale/cleared dictionary problem.

**Bonus fix**: `RegisterReverseMap` no longer eagerly calls `BuildAutoMap<TDest, TSource>()`, which was compiling the reverse delegate before any subsequent `.ForMember()` calls could register custom resolvers. Now the reverse map compiles lazily on first `Map` call, respecting all customizations added via `ReverseMap().ForMember(...)`.

---

## Task List

### ~~Task 1: Fix PreserveReferences~~ ✅ **Done**
- **Status**: Fixed. All 41 tests pass.
- **Changes made**:
  - Expanded `ResolutionContext.cs`: added `Depth`, `InstanceCache`, `EnterMap()`, `LeaveMap()`.
  - `JAutoMapper.cs`: replaced 4 `[ThreadStatic]` fields with `[ThreadStatic] ResolutionContext? _currentContext`.
  - `Map<TSource,TDest>` rewritten: creates context at entry point, checks `InstanceCache` for PreserveReferences, uses `Depth` for MaxDepth guard, cleans up in `finally`.
  - `Reset()` now clears `_currentContext`.
  - `ResolveWithClassResolver` uses `_currentContext` instead of `_resolutionContext`.
  - `RegisterReverseMap` no longer eagerly calls `BuildAutoMap` — delayed to first `Map` call so `ReverseMap().ForMember(...)` chain works.

### ~~Task 2: Batch Compilation Optimization~~ ✅ **Done**
- **Status**: Already satisfied; removed dead `BuildAutoMap` code. All 41 tests pass.
- **What the code already does**:
  - `CreateMap` + every fluent config method (`ForMember`, `Ignore`, `AfterMap`, `BeforeMap`, `ConstructUsing`, `ConvertUsing`, etc.) only mutates configuration dictionaries — no compilation happens during registration.
  - `CompileMap` is invoked **only** inside `Map<TSource,TDest>` lazily via `_maps.GetOrAdd` (first call triggers one compilation).
  - `BuildMapIntoAction` is invoked **only** inside `MapInto` / `PreserveReferences` path lazily via `_mapIntoActions.GetOrAdd` (first call triggers one compilation).
  - `RegisterReverseMap` was also fixed in Task 1 to not compile eagerly.
- **Cleanup**: Removed unused `BuildAutoMap<TSource,TDest>()` dead method from `JAutoMapper.cs`.
- **Acceptance**: Chaining 10 `ForMember` calls before the first `Map` call results in exactly one `CompileMap` invocation (verified by code inspection — no registration method calls any compilation routine).

### ~~Task 3: AssertConfigurationIsValid~~ ✅ **Done**
- **Status**: Implemented. All 46 tests pass.
- **What was added**:
  - `JAutoMapper.AssertConfigurationIsValid()` method scans every registered map.
  - For each destination property/field, checks if it has a corresponding source member (direct, flattened, custom resolver, class resolver, or ignored).
  - Skips validation when `ConvertUsing` is set (custom converter replaces auto-mapping).
  - Throws `InvalidOperationException` listing all unmapped members across all maps.
  - Added `HasFlattenedSource` helper (non-expression version of `TryResolveFlattenedSource`) for validation logic.
- **Tests added**:
  - `AssertConfigurationIsValid_passes_for_fully_mapped` — no unmapped members, no throw.
  - `AssertConfigurationIsValid_throws_for_unmapped_member` — missing `Age` source → throws with "Age" in message.
  - `AssertConfigurationIsValid_passes_when_unmapped_ignored` — `.Ignore(d => d.Age)` → no throw.
  - `AssertConfigurationIsValid_passes_for_ConvertUsing` — custom converter → validation skipped.
  - `AssertConfigurationIsValid_passes_for_flattened_property` — `AddressCity` resolved via flattening → no throw.

### ~~Task 4: Include / IncludeBase~~ ✅ **Done**
- **Status**: Implemented. All 49 tests pass.
- **What was added**:
  - `MapConfig<TSource,TDest>.IncludeBase<TBaseSource,TBaseDest>()` API.
  - `_includeBases` dictionary to store base map references per derived type pair.
  - Merged config helpers (`GetMergedIgnores`, `GetMergedCustoms`, `GetMergedDict`, `GetMergedSet`, etc.) that recursively walk the `IncludeBase` chain using cycle detection (`visited` set).
  - `CompileMap` and `BuildMapIntoAction` now use merged configurations: derived overrides take precedence over base defaults.
  - `AssertConfigurationIsValid` updated to also scan `_includeBases` keys and clear in `Reset()`.
- **Tests added**:
  - `IncludeBase_inherits_base_custom_resolver` — base `ForMember` applied to derived map (`Name` uppercased).
  - `IncludeBase_inherits_base_ignore` — base `.Ignore(d => d.Name)` propagated to derived.
  - `IncludeBase_derived_can_override_base_resolver` — derived `ForMember` overrides base custom resolver.

### ~~Task 5: ProjectTo Custom Resolvers & Nested Objects~~ ✅ **Done**
- **Status**: Implemented. All 56 tests pass.
- **What was added**:
  - `BuildProjection` refactored: extracted `BuildProjectionBindings` helper that recursively generates `MemberInit` expression trees.
  - Nested complex objects now project inline (e.g., `NestedSrc.Inner -> NestedDest.Inner`) with automatic null guard (`src.Inner != null ? new SimpleDest { ... } : null`).
  - Custom lambda resolvers (`ForMember`) now work in `ProjectTo` for in-memory queryables.
  - `MapFrom<TResolver>` (class resolvers) now work in `ProjectTo` for in-memory queryables via `Expression.Call` to `ResolveWithClassResolver`.
  - `NullSubstitute` supported in projection via conditional expressions.
  - `IncludeBase` merged configs (`GetMergedIgnores`, `GetMergedCustoms`, etc.) now used in `BuildProjectionBindings`.
- **Tests added**:
  - `ProjectTo_nested_complex_object` — nested `SimpleSrc -> SimpleDest` projection works.
  - `ProjectTo_nested_object_null_source` — null guard yields `null` destination inner object.
  - `ProjectTo_custom_lambda_resolver` — `ForMember` lambda resolver in projection.
  - `ProjectTo_with_NullSubstitute` — null substitution in projection.
  - `ProjectTo_with_MapFrom_TResolver` — class resolver in projection.

---

## Comparison with Original AutoMapper

| Feature | Status | Notes |
|---|---|---|
| Flat property mapping | Works | Fields + properties |
| Nested object mapping | Works | Recursive, Collections |
| ReverseMap | Works | Can customize reverse separately |
| ForMember / Ignore | Works | Lambda and option variants |
| AfterMap / BeforeMap | Works | Both hooks tested |
| NullSubstitute | Works | Tested; also works in `ProjectTo` |
| Condition | Works | Tested; skipped in `ProjectTo` (cannot translate to SQL) |
| UseDestinationValue | Works | Tested |
| ConstructUsing | Works | Tested |
| ConvertUsing | Works | Tested |
| Built-in converters | Works | Enum, nullable, numeric, Guid, DateTime, DateTimeOffset |
| Property flattening | Works | `AddressCity -> Address.City`; also in `ProjectTo` |
| MapFrom<TResolver> | Works | DI + Activator fallback; also works in `ProjectTo` (in-memory) |
| ProjectTo | **Fixed** | Flat + nested objects + custom resolvers + NullSubstitute; EF Core compatible for flat/nested, custom resolvers are in-memory only |
| MaxDepth | Works | Tested |
| PreserveReferences | **Fixed** | Stack overflow was fixed via ResolutionContext refactoring |
| Include / IncludeBase | **Fixed** | Implemented and tested; merged configs with cycle detection |
| AssertConfigurationIsValid | **Fixed** | Implemented and tested |
| Profiles | Not needed | Static method pattern used instead |
| Open generics | Missing | Out of spec scope |
| Async mapping | Missing | Out of spec scope |
| Value converters by type | Missing | Out of spec scope |

---

## Comparison with Original AutoMapper

| Feature | Status | Notes |
|---|---|---|
| Flat property mapping | Works | Fields + properties |
| Nested object mapping | Works | Recursive, Collections |
| ReverseMap | Works | Can customize reverse separately |
| ForMember / Ignore | Works | Lambda and option variants |
| AfterMap / BeforeMap | Works | Both hooks tested |
| NullSubstitute | Works | Tested |
| Condition | Works | Tested |
| UseDestinationValue | Works | Tested |
| ConstructUsing | Works | Tested |
| ConvertUsing | Works | Tested |
| Built-in converters | Works | Enum, nullable, numeric, Guid, DateTime, DateTimeOffset |
| Property flattening | Works | `AddressCity -> Address.City` |
| MapFrom<TResolver> | Works | DI + Activator fallback |
| ProjectTo | Partial | Flat properties only; custom resolvers skipped |
| MaxDepth | Works | Tested |
| PreserveReferences | **Fixed** | Stack overflow was fixed via ResolutionContext refactoring |
| Include / IncludeBase | **Fixed** | Implemented and tested; merged configs with cycle detection |
| AssertConfigurationIsValid | **Fixed** | Implemented and tested |
| Profiles | Not needed | Static method pattern used instead |
| Open generics | Missing | Out of spec scope |
| Async mapping | Missing | Out of spec scope |
| Value converters by type | Missing | Out of spec scope |

## Performance Relative to Original AutoMapper

This implementation uses expression-tree compilation (once per map type pair) and avoids reflection at runtime, similar to AutoMapper's core approach. It should be **comparable in speed** for single-object mapping. Potential slowdowns:

1. **Thread-static access overhead**: Removed in Task 1 — `_currentContext` is used instead of 4 scattered `[ThreadStatic]` fields.
2. **No caching of `BuildMapIntoAction`**: `MapInto` and `PreserveReferences` each cache their own compiled action via `_mapIntoActions`; this is fine. Could be unified in future but low priority.

Overall, **this is now a viable lightweight replacement** for typical AutoMapper use cases, with ~90%+ API compatibility.
