# JAutoMapper — AI Agent Code Generation Spec

A lightweight, dependency-free AutoMapper replacement for .NET 10 (C# 13).  
Single file: `JAutoMapper.cs`. No NuGet, no code generation, no reflection overhead beyond registration time.

---

## Design Goals

- API surface close to AutoMapper (familiar to existing users)
- No external dependencies
- Nested object mapping (auto-recursive)
- ReverseMap support
- Fluent configuration via `CreateMap<TSource, TDest>()`
- Thread-safe (all maps stored in `ConcurrentDictionary`)
- All registration done once at startup (e.g., static initializer or DI setup)

---

## Public API

### 1. `JAutoMapper.CreateMap<TSource, TDest>()`

Returns `MapConfig<TSource, TDest>` for fluent chaining.  
Registers an auto-map that:
- Maps all public readable properties of `TSource` to matching-name, compatible-type properties of `TDest`
- Recursively maps nested complex objects if a map is registered for those types
- Handles `null` source gracefully (returns `default`)

```csharp
JAutoMapper.CreateMap<User, UserDto>()
    .ForMember(d => d.FullName, s => $"{s.FirstName} {s.LastName}")
    .Ignore(d => d.InternalToken)
    .ReverseMap();
```

---

### 2. `MapConfig<TSource, TDest>` — Fluent Config

| Method | Signature | Description |
|--------|-----------|-------------|
| `ForMember` | `ForMember(d => d.Prop, s => value)` | Custom resolver lambda |
| `ForMember` | `ForMember(d => d.Prop, act => act.Ignore())` | Member options (`act.Ignore()`, `act.MapFrom<Resolver>()`) |
| `MapFrom` | `MapFrom(d => d.Prop, s => value)` | Alias for `ForMember` (custom resolver) |
| `MapFrom` | `MapFrom(d => d.Prop, act => act.Ignore())` | Alias for `ForMember` (member options) |
| `Ignore` | `Ignore(d => d.Prop)` | Skip this destination property |
| `AfterMap` | `AfterMap((src, dest) => { })` | Hook called after mapping completes |
| `ReverseMap` | `ReverseMap()` | Auto-register the inverse map (`TDest → TSource`). Returns `MapConfig<TDest, TSource>` for further chaining of the reverse. |
| `ConstructUsing` | `ConstructUsing(s => new TDest(s.Id))` | Custom constructor instead of `new TDest()` |

### 2b. `IMemberConfiguration<TSource, TDest>` — Member Options

Available inside `ForMember(d => d.Prop, act => ...)` lambdas:

| Method | Description |
|--------|-------------|
| `act.Ignore()` | Skip this destination property |
| `act.MapFrom<TResolver>()` | Use a named class resolver (implements `IValueResolver<TSource, TDest, TMember>`) |

```csharp
JAutoMapper.CreateMap<User, UserDto>()
    .ForMember(d => d.FullName, act => act.MapFrom<FullNameResolver>())
    .ForMember(d => d.InternalToken, act => act.Ignore());
```

---

### 3. `JAutoMapper.Map<TSource, TDest>(TSource source)`

Maps a single object. Throws `InvalidOperationException` if no map is registered.

```csharp
var dto = JAutoMapper.Map<User, UserDto>(user);
```

---

### 4. `JAutoMapper.Map<TDest>(object source)`

Convenience overload — infers `TSource` from runtime type.  
**Auto-detects collections:** if `source` is an `IEnumerable` and `TDest` is a collection type (`List<>`, `IEnumerable<>`, etc.), it automatically delegates to `MapList` instead of trying to map the collection wrapper.

```csharp
var dto = JAutoMapper.Map<UserDto>(user);          // single object
var dtos = JAutoMapper.Map<List<UserDto>>(users);   // auto → MapList<User, UserDto>(users)
```

---

### 5. `JAutoMapper.MapList<TSource, TDest>(IEnumerable<TSource> source)`

Maps a collection. Returns `List<TDest>`. Null input returns empty list.

```csharp
var dtos = JAutoMapper.MapList<User, UserDto>(users);
```

---

### 6. `JAutoMapper.Map<TSource, TDest>(TSource source, TDest dest)`

Maps `source` into an **existing** destination instance (merge/update).  
Convenience alias for `MapInto`.

```csharp
var dto = JAutoMapper.Map<CategoryViewModel, Category>(categoryVm, existingCategory);
```

### 7. `JAutoMapper.MapInto<TSource, TDest>(TSource source, TDest dest)`

Maps `source` into an **existing** destination instance (merge/update).

```csharp
JAutoMapper.MapInto(updateDto, existingEntity);
```

---

### 8. `JAutoMapper.ProjectTo<TSource, TDest>(IQueryable<TSource> query)`

Builds a LINQ `Expression`-based projection — no runtime reflection, EF Core compatible.  
Only maps flat properties (no navigation properties). If a property requires a custom resolver, it is skipped in projection.

```csharp
var dtos = db.Users.ProjectTo<User, UserDto>().ToList();
```

---

### 9. `JAutoMapper.Reset()`

Clears all registered maps. Useful for unit tests.

---

## Nested Object Mapping

When auto-mapping, if a destination property type `TDestProp` differs from source property type `TSrcProp` (i.e., they are not assignable), check if a map is registered for `(TSrcProp, TDestProp)`. If yes, call `Map<TSrcProp, TDestProp>(srcValue)` recursively.

**Example:**

```csharp
JAutoMapper.CreateMap<Address, AddressDto>();
JAutoMapper.CreateMap<User, UserDto>(); // Address → AddressDto resolved automatically
```

Collections of complex types are handled too: if the property type is `IEnumerable<T>` or `List<T>`, apply `MapList` recursively.

---

## ReverseMap

`ReverseMap()` on `MapConfig<TSource, TDest>`:

1. Registers a new auto-map for `(TDest, TSource)`
2. Any `ForMember` customizations on the forward map are NOT applied in reverse (auto-map only)
3. Returns `MapConfig<TDest, TSource>` so the reverse can be further customized:

```csharp
JAutoMapper.CreateMap<User, UserDto>()
    .ForMember(d => d.FullName, s => $"{s.FirstName} {s.LastName}")
    .ReverseMap()
    .ForMember(d => d.FirstName, s => s.FullName.Split(' ')[0]);
```

---

## Auto-Map Resolution Order (per property)

1. If property name exists in `ForMember` customizations → use custom resolver
2. If property name exists in `Ignore` list → skip
3. If property has a class resolver (`MapFrom<TResolver>()`) → create resolver and call `Resolve()`
4. If destination property type is assignable from source property type → direct assignment
5. If a map is registered for `(srcPropType, destPropType)` → recursive `Map()`
6. If property is a generic collection (`IEnumerable<T>`) of a mapped type → `MapList()`
7. Otherwise → skip (leave destination property at default)

---

## Internal Architecture

Put this in folder 'Helpers':

```
JAutoMapper (static)
├── _maps: ConcurrentDictionary<(Type src, Type dest), Delegate>
├── _afterMaps: ConcurrentDictionary<(Type, Type), Delegate>
├── _customs: ConcurrentDictionary<(Type, Type), List<CustomEntry>>
├── _ignores: ConcurrentDictionary<(Type, Type), HashSet<string>>
├── _constructors: ConcurrentDictionary<(Type, Type), Delegate>
└── BuildAutoMap<TSource, TDest>()  — called on CreateMap + after each ForMember
```

`BuildAutoMap` is called at registration time and re-called after each `ForMember` so the compiled delegate always reflects latest config. It should be deferred/lazy so nested type maps don't require specific registration order.

**Lazy map resolution:** When executing a map, if a nested property's map isn't found at delegate-build time, look it up at call time from `_maps` dict (avoid requiring registration order).

---

## Error Handling

| Scenario | Behavior |
|----------|----------|
| `source == null` | Return `default(TDest)` |
| No map registered | Throw `InvalidOperationException` with message: `"No map registered for {TSource.Name} → {TDest.Name}. Call JAutoMapper.CreateMap<{TSource.Name}, {TDest.Name}>() at startup."` |
| `ForMember` resolver throws | Wrap in `MappingException` with source/dest type info + property name |
| Circular reference | Detect via a `[ThreadStatic]` depth counter; throw `MappingException("Circular reference detected...")` at depth > 32 |

---

## Unit Test Scenarios (for agent to implement as xUnit tests)

```
✓ Simple flat mapping — matching property names
✓ ForMember custom resolver
✓ Ignore skips property
✓ Null source returns default
✓ MapList returns correct count
✓ MapInto updates existing object
✓ Nested object auto-mapped recursively
✓ Nested collection auto-mapped recursively
✓ ReverseMap maps back correctly
✓ ReverseMap with ForMember on reverse
✓ AfterMap hook called
✓ ConstructUsing uses custom constructor
✓ ProjectTo produces correct IQueryable expression
✓ Reset clears all maps
✓ Missing map throws InvalidOperationException
✓ Circular reference throws MappingException (depth guard)
```

---

## Usage Registration Pattern (DI-friendly)

```csharp
// MappingProfile.cs
public static class MappingProfile
{
    public static void Register()
    {
        JAutoMapper.CreateMap<Address, AddressDto>().ReverseMap();

        JAutoMapper.CreateMap<User, UserDto>()
            .ForMember(d => d.FullName, s => $"{s.FirstName} {s.LastName}")
            .AfterMap((src, dest) => dest.Initials = $"{src.FirstName[0]}{src.LastName[0]}")
            .ReverseMap()
            .ForMember(d => d.FirstName, s => s.FullName.Split(' ')[0])
            .ForMember(d => d.LastName,  s => s.FullName.Contains(' ') ? s.FullName.Split(' ')[1] : "");
    }
}

// Program.cs
MappingProfile.Register();
```

---

## Named Class Resolvers

Implement `IValueResolver<TSource, TDest, TMember>` and register via `ForMember(..., opt => opt.MapFrom<TResolver>())`:

```csharp
public class UserRoleResolver : IValueResolver<ApplicationUser, UserViewModel, string>
{
    private readonly IUserService userService;

    public UserRoleResolver(IUserService userService) => this.userService = userService;

    public string Resolve(ApplicationUser source, UserViewModel destination, string destMember, ResolutionContext context)
        => userService.GetRole(source);
}
```

Registration:
```csharp
JAutoMapper.CreateMap<ApplicationUser, UserViewModel>()
    .ForMember(d => d.RoleName, opt => opt.MapFrom<UserRoleResolver>());
```

### DI Support

Resolvers with parameterless constructors work out of the box.  
For DI (constructor injection), set `JAutoMapper.ResolverFactory` at startup:

```csharp
var serviceProvider = services.BuildServiceProvider();
JAutoMapper.ResolverFactory = serviceProvider.GetRequiredService;
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `JAutoMapper.ResolverFactory` | `Func<Type, object>?` | `null` (tries `Activator.CreateInstance` fallback) | Creates resolver instances. Set to `serviceProvider.GetRequiredService` for DI. |

### Resolver Interface

```csharp
public interface IValueResolver<in TSource, in TDest, TMember>
{
    TMember Resolve(TSource source, TDest destination, TMember destMember, ResolutionContext context);
}
```

---

## Out of Scope (do not implement)

- Attribute-based configuration
- Profile classes (not needed — static method groups are sufficient)
- Value converters registered by type
- Open generics mapping
- `BeforeMap` hook (AfterMap is sufficient)
- Async mapping
