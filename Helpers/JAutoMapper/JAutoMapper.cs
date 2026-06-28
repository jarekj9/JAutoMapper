using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace JAM;

public static class JAutoMapper
{
    // ── Internal state ──────────────────────────────────────────────
    private static readonly ConcurrentDictionary<(Type, Type), Delegate> _maps = new();
    private static readonly ConcurrentDictionary<(Type, Type), Action<object?, object?>> _mapIntoActions = new();
    private static readonly ConcurrentDictionary<(Type, Type), Delegate> _afterMaps = new();
    private static readonly ConcurrentDictionary<(Type, Type), List<CustomEntry>> _customs = new();
    private static readonly ConcurrentDictionary<(Type, Type), HashSet<string>> _ignores = new();
    private static readonly ConcurrentDictionary<(Type, Type), Delegate> _constructors = new();
    private static readonly ConcurrentDictionary<(Type, Type), Dictionary<string, Type>> _classResolverTypes = new();
    private static readonly ConcurrentDictionary<(Type, Type), LambdaExpression> _projections = new();

    [ThreadStatic]
    private static int _recursionDepth;

    [ThreadStatic]
    private static ResolutionContext? _resolutionContext;

    private const int MaxRecursionDepth = 32;

    /// <summary>
    /// Service provider for creating resolver instances used by <c>MapFrom&lt;TResolver&gt;()</c>.
    /// If <c>null</c> (default), resolvers are created via <c>Activator.CreateInstance</c>
    /// (requires a parameterless constructor).
    /// Set this to the root <c>IServiceProvider</c> (<c>app.Services</c>) to integrate with DI.
    /// Scoped services are handled correctly — a new scope is created per resolution and
    /// disposed after <c>Resolve</c> completes.
    /// </summary>
    public static IServiceProvider? ServiceProvider { get; set; }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>Register a map. Returns a fluent config object.</summary>
    public static MapConfig<TSource, TDest> CreateMap<TSource, TDest>()
    {
        var key = (typeof(TSource), typeof(TDest));
        _customs.GetOrAdd(key, static _ => new List<CustomEntry>());
        _ignores.GetOrAdd(key, static _ => new HashSet<string>(StringComparer.Ordinal));
        BuildAutoMap<TSource, TDest>();
        return new MapConfig<TSource, TDest>();
    }

    /// <summary>Map a single object.</summary>
    public static TDest? Map<TSource, TDest>(TSource? source)
    {
        if (source is null) return default;

        var key = (typeof(TSource), typeof(TDest));
        if (!_maps.TryGetValue(key, out var del))
            ThrowNoMap<TSource, TDest>();

        var func = (Func<TSource?, TDest?>)del!;

        if (_recursionDepth >= MaxRecursionDepth)
            throw new MappingException(
                $"Circular reference detected while mapping {typeof(TSource).Name} → {typeof(TDest).Name} (depth > {MaxRecursionDepth}).",
                typeof(TSource), typeof(TDest));

        _recursionDepth++;
        try
        {
            return func!(source);
        }
        catch (MappingException) { throw; }
        catch (Exception ex)
        {
            throw new MappingException(
                $"Error mapping {typeof(TSource).Name} → {typeof(TDest).Name}: {ex.Message}",
                typeof(TSource), typeof(TDest), null, ex);
        }
        finally
        {
            _recursionDepth--;
        }
    }

    /// <summary>Map source into an existing destination (same as MapInto).</summary>
    public static TDest Map<TSource, TDest>(TSource? source, TDest dest)
        => MapInto(source, dest);

    /// <summary>Convenience overload — infers TSource from runtime type.</summary>
    public static TDest? Map<TDest>(object? source)
    {
        if (source is null) return default;

        // Collection detection: if source is IEnumerable and TDest is a List<>/IEnumerable<>,
        // delegate to MapList instead of trying to map the collection type directly.
        if (source is not string && source is System.Collections.IEnumerable
            && IsCollectionType(typeof(TDest), out var destElemType))
        {
            var srcType = source.GetType();
            var srcEnum = GetEnumerableInterface(srcType);
            if (srcEnum is not null)
            {
                var srcElemType = srcEnum.GetGenericArguments()[0];
                var mapListMethod = typeof(JAutoMapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == nameof(MapList) && m.IsGenericMethodDefinition
                             && m.GetGenericArguments().Length == 2);
                var closedMethod = mapListMethod.MakeGenericMethod(srcElemType, destElemType!);
                try
                {
                    var listResult = closedMethod.Invoke(null, [source]);
                    if (listResult is TDest result)
                        return result;
                    // If TDest isn't exactly List<T>, try converting (e.g. IEnumerable<T>)
                }
                catch (TargetInvocationException tie)
                {
                    throw tie.InnerException ?? tie;
                }
            }
        }

        var srcType2 = source.GetType();
        var method = GetMapMethod().MakeGenericMethod(srcType2, typeof(TDest));
        try
        {
            return (TDest?)method.Invoke(null, [source]);
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException ?? tie;
        }
    }

    private static bool IsCollectionType(Type type, out Type? elementType)
    {
        if (type.IsGenericType)
        {
            var genDef = type.GetGenericTypeDefinition();
            if (genDef == typeof(List<>) || genDef == typeof(IEnumerable<>)
                || genDef == typeof(ICollection<>) || genDef == typeof(IList<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
        }
        elementType = null;
        return false;
    }

    /// <summary>Map a collection.</summary>
    public static List<TDest> MapList<TSource, TDest>(IEnumerable<TSource>? source)
    {
        if (source is null) return [];
        return source.Select(s => Map<TSource, TDest>(s)!).ToList();
    }

    /// <summary>Map into an existing destination instance (merge/update).</summary>
    public static TDest MapInto<TSource, TDest>(TSource? source, TDest dest)
    {
        if (source is null) return dest;

        var key = (typeof(TSource), typeof(TDest));
        if (!_maps.TryGetValue(key, out _))
            ThrowNoMap<TSource, TDest>();

        var action = _mapIntoActions.GetOrAdd(key, static k =>
        {
            var compiled = BuildMapIntoAction<TSource, TDest>();
            return (src, d) => compiled((TSource?)src, (TDest?)d);
        });

        action(source, dest);
        return dest;
    }

    /// <summary>Build an IQueryable projection (EF Core compatible).</summary>
    public static IQueryable<TDest> ProjectTo<TSource, TDest>(this IQueryable<TSource> query)
    {
        var key = (typeof(TSource), typeof(TDest));
        if (!_projections.TryGetValue(key, out var expr))
        {
            expr = BuildProjection<TSource, TDest>();
            _projections[key] = expr;
        }

        return query.Select((Expression<Func<TSource, TDest>>)expr);
    }

    /// <summary>Clear all registered maps (for unit tests).</summary>
    public static void Reset()
    {
        _maps.Clear();
        _mapIntoActions.Clear();
        _afterMaps.Clear();
        _customs.Clear();
        _ignores.Clear();
        _constructors.Clear();
        _projections.Clear();
        _classResolverTypes.Clear();
        ServiceProvider = null;
    }

    // ── Internal helpers ────────────────────────────────────────────

    internal static void AddCustom<TSource, TDest>(string memberName, Delegate resolver)
    {
        var key = (typeof(TSource), typeof(TDest));
        var list = _customs.GetOrAdd(key, static _ => new List<CustomEntry>());
        list.RemoveAll(c => c.DestMemberName == memberName);
        list.Add(new CustomEntry { DestMemberName = memberName, Resolver = resolver });
        BuildAutoMap<TSource, TDest>();
    }

    internal static void AddIgnore<TSource, TDest>(string memberName)
    {
        var key = (typeof(TSource), typeof(TDest));
        var set = _ignores.GetOrAdd(key, static _ => new HashSet<string>(StringComparer.Ordinal));
        set.Add(memberName);
        BuildAutoMap<TSource, TDest>();
    }

    internal static void SetAfterMap<TSource, TDest>(Delegate afterMap)
    {
        var key = (typeof(TSource), typeof(TDest));
        _afterMaps[key] = afterMap;
        BuildAutoMap<TSource, TDest>();
    }

    internal static void SetConstructor<TSource, TDest>(Delegate ctor)
    {
        var key = (typeof(TSource), typeof(TDest));
        _constructors[key] = ctor;
        BuildAutoMap<TSource, TDest>();
    }

    internal static void RegisterReverseMap<TSource, TDest>()
    {
        // Create a plain reverse map (auto-map only — no customs/ignores carried over)
        var reverseKey = (typeof(TDest), typeof(TSource));
        _customs.GetOrAdd(reverseKey, static _ => new List<CustomEntry>());
        _ignores.GetOrAdd(reverseKey, static _ => new HashSet<string>(StringComparer.Ordinal));
        BuildAutoMap<TDest, TSource>();
    }

    internal static MapConfig<TDest, TSource> GetReverseConfig<TSource, TDest>()
    {
        return new MapConfig<TDest, TSource>();
    }

    internal static void AddClassResolver<TSource, TDest>(string memberName, Type resolverType)
    {
        var key = (typeof(TSource), typeof(TDest));
        var dict = _classResolverTypes.GetOrAdd(key, static _ => new Dictionary<string, Type>(StringComparer.Ordinal));
        dict[memberName] = resolverType;
        BuildAutoMap<TSource, TDest>();
    }

    /// <summary>
    /// Called from the compiled delegate at map time. Looks up the class resolver
    /// for the given property, creates an instance via <see cref="ServiceProvider"/>
    /// or <c>Activator.CreateInstance</c>, and invokes its <c>Resolve</c> method.
    /// </summary>
    internal static TMember? ResolveWithClassResolver<TSource, TDest, TMember>(
        TSource source, TDest dest, TMember? current, string propName)
    {
        var key = (typeof(TSource), typeof(TDest));
        if (_classResolverTypes.TryGetValue(key, out var dict) && dict.TryGetValue(propName, out var resolverType))
        {
            object? instance = null;

            if (ServiceProvider != null)
            {
                try
                {
                    instance = ServiceProvider.GetService(resolverType);
                }
                catch
                {
                    // Resolving a scoped service from root provider throws silently fall through
                }
            }

            if (instance == null)
            {
                try { instance = Activator.CreateInstance(resolverType)!; }
                catch (MissingMethodException)
                {
                    throw new InvalidOperationException(
                        $"Resolver '{resolverType.Name}' has no parameterless constructor. " +
                        $"Register it as Singleton/Transient in DI, or use a scoped provider " +
                        $"for JAutoMapper.ServiceProvider.");
                }
            }

            var ctx = _resolutionContext ??= new ResolutionContext();
            return (TMember?)((dynamic)instance).Resolve(source, dest, current, ctx);
        }
        return current;
    }

    // ── Delegate builders ───────────────────────────────────────────

    private static void BuildAutoMap<TSource, TDest>()
    {
        var key = (typeof(TSource), typeof(TDest));
        var del = CompileMap<TSource, TDest>();
        _maps[key] = del;
    }

    private static Func<TSource?, TDest?> CompileMap<TSource, TDest>()
    {
        var srcType = typeof(TSource);
        var destType = typeof(TDest);
        var key = (srcType, destType);

        var srcParam = Expression.Parameter(typeof(TSource), "src");
        var destVar = Expression.Variable(typeof(TDest), "dest");
        var returnTarget = Expression.Label(typeof(TDest));

        var steps = new List<Expression>();

        // 1. Null check
        steps.Add(
            Expression.IfThen(
                Expression.ReferenceEqual(srcParam, Expression.Constant(null, typeof(TSource))),
                Expression.Return(returnTarget, Expression.Default(typeof(TDest)))
            )
        );

        // 2. Create destination
        if (_constructors.TryGetValue(key, out var ctorDel))
        {
            steps.Add(
                Expression.Assign(destVar,
                    Expression.Convert(
                        Expression.Invoke(Expression.Constant(ctorDel), srcParam),
                        typeof(TDest)))
            );
        }
        else
        {
            var ctor = destType.GetConstructor(Type.EmptyTypes);
            if (ctor is not null)
                steps.Add(Expression.Assign(destVar, Expression.New(ctor)));
            else
                steps.Add(Expression.Assign(destVar, Expression.New(destType)));
        }

        // 3. Property mappings
        var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanWrite: true, SetMethod.IsPublic: true })
            .ToList();

        var srcProps = srcType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, GetMethod.IsPublic: true })
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var ignores = _ignores.GetValueOrDefault(key) ?? new HashSet<string>(StringComparer.Ordinal);
        var customList = _customs.GetValueOrDefault(key) ?? [];
        var customDict = new Dictionary<string, Delegate>(StringComparer.Ordinal);
        foreach (var c in customList)
            customDict[c.DestMemberName] = c.Resolver;

        var mapMethods = typeof(JAutoMapper).GetMethods(BindingFlags.Public | BindingFlags.Static);
        var mapMethodGeneric = mapMethods.First(m =>
            m.Name == nameof(Map) && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2
            && m.GetParameters().Length == 1);
        var mapListMethodGeneric = mapMethods.First(m =>
            m.Name == nameof(MapList) && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);

        foreach (var destProp in destProps)
        {
            if (ignores.Contains(destProp.Name)) continue;

            // Custom resolver — wrapped in try-catch to include property name
            if (customDict.TryGetValue(destProp.Name, out var resolver))
            {
                var invokeExpr = Expression.Invoke(Expression.Constant(resolver), srcParam);
                var converted = Expression.Convert(invokeExpr, destProp.PropertyType);
                var assignExpr = Expression.Assign(Expression.Property(destVar, destProp), converted);
                steps.Add(WrapResolverWithPropertyCatch(assignExpr, srcType, destType, destProp.Name));
                continue;
            }

            // Class resolver (MapFrom<TResolver>) — resolved at runtime via ServiceProvider
            if (_classResolverTypes.TryGetValue(key, out var classResolvers)
                && classResolvers.TryGetValue(destProp.Name, out _))
            {
                var resolveMethod = typeof(JAutoMapper).GetMethod(nameof(ResolveWithClassResolver),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(srcType, destType, destProp.PropertyType);
                steps.Add(
                    Expression.Assign(
                        Expression.Property(destVar, destProp),
                        Expression.Call(resolveMethod, srcParam, destVar,
                            Expression.Property(destVar, destProp),
                            Expression.Constant(destProp.Name))
                    )
                );
                continue;
            }

            // Find matching source property
            if (!srcProps.TryGetValue(destProp.Name, out var srcProp)) continue;

            var srcAccess = Expression.Property(srcParam, srcProp);

            // Direct assignment if assignable
            if (destProp.PropertyType.IsAssignableFrom(srcProp.PropertyType))
            {
                steps.Add(
                    Expression.Assign(
                        Expression.Property(destVar, destProp),
                        Expression.Convert(srcAccess, destProp.PropertyType)
                    )
                );
                continue;
            }

            // Collection map check
            if (TryGetCollectionElementTypes(srcProp.PropertyType, destProp.PropertyType,
                    out var srcElem, out var destElem))
            {
                var closedMapList = mapListMethodGeneric.MakeGenericMethod(srcElem, destElem);
                steps.Add(
                    Expression.Assign(
                        Expression.Property(destVar, destProp),
                        Expression.Call(closedMapList, srcAccess)
                    )
                );
                continue;
            }

            // Nested object map (uses runtime lookup via Map<>)
            if (!srcProp.PropertyType.IsValueType && !destProp.PropertyType.IsValueType)
            {
                var closedMap = mapMethodGeneric.MakeGenericMethod(srcProp.PropertyType, destProp.PropertyType);
                steps.Add(
                    Expression.Assign(
                        Expression.Property(destVar, destProp),
                        Expression.Call(closedMap, srcAccess)
                    )
                );
            }
            // else: skip incompatible types
        }

        // 4. AfterMap
        if (_afterMaps.TryGetValue(key, out var afterMapDel))
        {
            steps.Add(Expression.Invoke(Expression.Constant(afterMapDel), srcParam, destVar));
        }

        // 5. Return
        steps.Add(Expression.Label(returnTarget, destVar));

        var body = Expression.Block(new[] { destVar }, steps);
        return Expression.Lambda<Func<TSource?, TDest?>>(body, srcParam).Compile();
    }

    private static Action<TSource?, TDest?> BuildMapIntoAction<TSource, TDest>()
    {
        var srcType = typeof(TSource);
        var destType = typeof(TDest);
        var key = (srcType, destType);

        var srcParam = Expression.Parameter(typeof(TSource), "src");
        var destParam = Expression.Parameter(typeof(TDest), "dest");

        var returnLabel = Expression.Label();
        var steps = new List<Expression>();

        // Null check on source - early return (Action is void)
        steps.Add(
            Expression.IfThen(
                Expression.ReferenceEqual(srcParam, Expression.Constant(null, typeof(TSource))),
                Expression.Return(returnLabel)
            )
        );

        var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanWrite: true, SetMethod.IsPublic: true })
            .ToList();

        var srcProps = srcType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, GetMethod.IsPublic: true })
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var ignores = _ignores.GetValueOrDefault(key) ?? new HashSet<string>(StringComparer.Ordinal);
        var customList = _customs.GetValueOrDefault(key) ?? [];
        var customDict = new Dictionary<string, Delegate>(StringComparer.Ordinal);
        foreach (var c in customList)
            customDict[c.DestMemberName] = c.Resolver;

        var mapMethodGeneric = typeof(JAutoMapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Map) && m.IsGenericMethodDefinition
                     && m.GetGenericArguments().Length == 2 && m.GetParameters().Length == 1);
        var mapListMethodGeneric = typeof(JAutoMapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(MapList) && m.IsGenericMethodDefinition
                     && m.GetGenericArguments().Length == 2);

        foreach (var destProp in destProps)
        {
            if (ignores.Contains(destProp.Name)) continue;

            if (customDict.TryGetValue(destProp.Name, out var resolver))
            {
                var invokeExpr = Expression.Invoke(Expression.Constant(resolver), srcParam);
                var converted = Expression.Convert(invokeExpr, destProp.PropertyType);
                var assignExpr = Expression.Assign(Expression.Property(destParam, destProp), converted);
                steps.Add(WrapResolverWithPropertyCatch(assignExpr, srcType, destType, destProp.Name));
                continue;
            }

            // Class resolver (MapFrom<TResolver>) — resolved at runtime via ServiceProvider
            if (_classResolverTypes.TryGetValue(key, out var classResolvers)
                && classResolvers.TryGetValue(destProp.Name, out _))
            {
                var resolveMethod = typeof(JAutoMapper).GetMethod(nameof(ResolveWithClassResolver),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(srcType, destType, destProp.PropertyType);
                steps.Add(
                    Expression.Assign(
                        Expression.Property(destParam, destProp),
                        Expression.Call(resolveMethod, srcParam, destParam,
                            Expression.Property(destParam, destProp),
                            Expression.Constant(destProp.Name))
                    )
                );
                continue;
            }

            if (!srcProps.TryGetValue(destProp.Name, out var srcProp)) continue;

            var srcAccess = Expression.Property(srcParam, srcProp);

            if (destProp.PropertyType.IsAssignableFrom(srcProp.PropertyType))
            {
                steps.Add(
                    Expression.Assign(
                        Expression.Property(destParam, destProp),
                        Expression.Convert(srcAccess, destProp.PropertyType)
                    )
                );
                continue;
            }

            if (TryGetCollectionElementTypes(srcProp.PropertyType, destProp.PropertyType,
                    out var srcElem, out var destElem))
            {
                var closedMapList = mapListMethodGeneric.MakeGenericMethod(srcElem, destElem);
                steps.Add(
                    Expression.Assign(
                        Expression.Property(destParam, destProp),
                        Expression.Call(closedMapList, srcAccess)
                    )
                );
                continue;
            }

            if (!srcProp.PropertyType.IsValueType && !destProp.PropertyType.IsValueType)
            {
                var closedMap = mapMethodGeneric.MakeGenericMethod(srcProp.PropertyType, destProp.PropertyType);
                steps.Add(
                    Expression.Assign(
                        Expression.Property(destParam, destProp),
                        Expression.Call(closedMap, srcAccess)
                    )
                );
            }
        }

        // AfterMap
        if (_afterMaps.TryGetValue(key, out var afterMapDel))
        {
            steps.Add(Expression.Invoke(Expression.Constant(afterMapDel), srcParam, destParam));
        }

        // Label for early return
        steps.Add(Expression.Label(returnLabel));

        var body = Expression.Block(steps);
        return Expression.Lambda<Action<TSource?, TDest?>>(body, srcParam, destParam).Compile();
    }

    private static Expression<Func<TSource, TDest>> BuildProjection<TSource, TDest>()
    {
        var srcType = typeof(TSource);
        var destType = typeof(TDest);
        var key = (srcType, destType);

        var srcParam = Expression.Parameter(typeof(TSource), "src");

        var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanWrite: true, SetMethod.IsPublic: true })
            .ToList();

        var srcProps = srcType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, GetMethod.IsPublic: true })
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var ignores = _ignores.GetValueOrDefault(key) ?? new HashSet<string>(StringComparer.Ordinal);

        var bindings = new List<MemberAssignment>();

        foreach (var destProp in destProps)
        {
            if (ignores.Contains(destProp.Name)) continue;

            if (!srcProps.TryGetValue(destProp.Name, out var srcProp)) continue;

            // Only map simple, assignable properties for projection
            if (destProp.PropertyType.IsAssignableFrom(srcProp.PropertyType))
            {
                bindings.Add(
                    Expression.Bind(destProp, Expression.Convert(Expression.Property(srcParam, srcProp), destProp.PropertyType))
                );
            }
        }

        var newExpr = Expression.New(destType);
        var memberInit = Expression.MemberInit(newExpr, bindings);
        return Expression.Lambda<Func<TSource, TDest>>(memberInit, srcParam);
    }

    // ── Utility ─────────────────────────────────────────────────────

    /// <summary>
    /// Wraps a property assignment in a try-catch so that if the custom resolver throws,
    /// the exception is re-thrown as a MappingException that includes the property name.
    /// </summary>
    private static Expression WrapResolverWithPropertyCatch(Expression body, Type srcType, Type destType, string propName)
    {
        var exParam = Expression.Parameter(typeof(Exception), "ex");
        var concatMethod = typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!;
        var message = Expression.Add(
            Expression.Constant($"Error resolving property '{propName}' while mapping {srcType.Name} → {destType.Name}: "),
            Expression.Property(exParam, "Message"),
            concatMethod);
        var mappingCtor = typeof(MappingException).GetConstructors()
            .First(c => c.GetParameters().Length == 5);
        var newMappingEx = Expression.New(mappingCtor,
            message,
            Expression.Constant(srcType),
            Expression.Constant(destType),
            Expression.Constant(propName, typeof(string)),
            exParam);
        // try-body and catch-body must share the same expression type
        return Expression.TryCatch(body,
            Expression.Catch(exParam, Expression.Throw(newMappingEx, body.Type)));
    }

    private static bool TryGetCollectionElementTypes(Type srcType, Type destType,
        out Type srcElem, out Type destElem)
    {
        srcElem = null!;
        destElem = null!;

        var srcEnum = GetEnumerableInterface(srcType);
        var destEnum = GetEnumerableInterface(destType);
        if (srcEnum is null || destEnum is null) return false;

        srcElem = srcEnum.GetGenericArguments()[0];
        destElem = destEnum.GetGenericArguments()[0];

        // Only treat as collection map if element types differ AND both are reference types
        return srcElem != destElem && !srcElem.IsValueType && !destElem.IsValueType;
    }

    private static Type? GetEnumerableInterface(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type;
        return type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
    }

    private static void ThrowNoMap<TSource, TDest>()
    {
        throw new InvalidOperationException(
            $"No map registered for {typeof(TSource).Name} → {typeof(TDest).Name}. " +
            $"Call JAutoMapper.CreateMap<{typeof(TSource).Name}, {typeof(TDest).Name}>() at startup.");
    }

    private static MethodInfo GetMapMethod()
    {
        return typeof(JAutoMapper).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Map) && m.IsGenericMethodDefinition
                     && m.GetGenericArguments().Length == 2 && m.GetParameters().Length == 1);
    }
}
