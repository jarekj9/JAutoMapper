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
    private static readonly ConcurrentDictionary<(Type, Type), Delegate> _beforeMaps = new();
    private static readonly ConcurrentDictionary<(Type, Type), List<CustomEntry>> _customs = new();
    private static readonly ConcurrentDictionary<(Type, Type), HashSet<string>> _ignores = new();
    private static readonly ConcurrentDictionary<(Type, Type), HashSet<string>> _useDestinationValue = new();
    private static readonly ConcurrentDictionary<(Type, Type), Dictionary<string, Delegate>> _conditions = new();
    private static readonly ConcurrentDictionary<(Type, Type), Dictionary<string, object>> _nullSubstitutes = new();
    private static readonly ConcurrentDictionary<(Type, Type), Delegate> _constructors = new();
    private static readonly ConcurrentDictionary<(Type, Type), Dictionary<string, Type>> _classResolverTypes = new();
    private static readonly ConcurrentDictionary<(Type, Type), LambdaExpression> _projections = new();
    private static readonly ConcurrentDictionary<(Type, Type), Delegate> _convertUsing = new();
    private static readonly ConcurrentDictionary<(Type, Type), bool> _preserveReferences = new();
    private static readonly ConcurrentDictionary<(Type, Type), int> _maxDepths = new();
    private static readonly ConcurrentDictionary<(Type, Type), List<(Type BaseSrc, Type BaseDest)>> _includeBases = new();

    [ThreadStatic]
    private static ResolutionContext? _currentContext;

    private const int DefaultMaxRecursionDepth = 32;

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
        return new MapConfig<TSource, TDest>();
    }

    /// <summary>Map a single object.</summary>
    public static TDest? Map<TSource, TDest>(TSource? source)
    {
        if (source is null) return default;

        var key = (typeof(TSource), typeof(TDest));
        if (!_customs.ContainsKey(key))
            ThrowNoMap<TSource, TDest>();

        bool isRoot = _currentContext is null;
        _currentContext ??= new ResolutionContext();
        _currentContext.EnterMap();
        try
        {
            // ── PreserveReferences: create instance FIRST, then populate ──
            if (_preserveReferences.ContainsKey(key))
            {
                var refKey = (typeof(TSource), typeof(TDest), (object)source);
                if (_currentContext.InstanceCache.TryGetValue(refKey, out var existing))
                    return (TDest?)existing;

                TDest dest = CreateDestinationInstance<TSource, TDest>(source);
                _currentContext.InstanceCache[refKey] = dest!;

                // BeforeMap hook
                if (_beforeMaps.TryGetValue(key, out var beforeDel))
                    ((Action<TSource, TDest>)beforeDel)(source, dest);

                // Populate properties via MapInto action (no new instance creation)
                var action = _mapIntoActions.GetOrAdd(key, static k =>
                {
                    var compiled = BuildMapIntoAction<TSource, TDest>();
                    return (src, d) => compiled((TSource?)src, (TDest?)d);
                });
                action(source, dest);
                return dest;
            }

            // ── Normal path: lazy compile + depth guard ──
            if (!_maps.TryGetValue(key, out var del))
            {
                del = CompileMap<TSource, TDest>();
                _maps[key] = del;
            }

            var func = (Func<TSource?, TDest?>)del!;

            int maxDepth = _maxDepths.TryGetValue(key, out var md) ? md : DefaultMaxRecursionDepth;
            if (_currentContext.Depth > maxDepth)
                throw new MappingException(
                    $"Circular reference detected while mapping {typeof(TSource).Name} → {typeof(TDest).Name} (depth > {maxDepth}).",
                    typeof(TSource), typeof(TDest));

            return func(source);
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
            _currentContext!.LeaveMap();
            if (isRoot) _currentContext = null;
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
        if (!_customs.ContainsKey(key))
            ThrowNoMap<TSource, TDest>();

        var action = _mapIntoActions.GetOrAdd(key, static k =>
        {
            var compiled = BuildMapIntoAction<TSource, TDest>();
            return (src, d) => compiled((TSource?)src, (TDest?)d);
        });

        action(source, dest);
        return dest;
    }

    /// <summary>Create a destination instance using ConvertUsing, ConstructUsing or parameterless ctor.</summary>
    private static TDest CreateDestinationInstance<TSource, TDest>(TSource source)
    {
        var key = (typeof(TSource), typeof(TDest));
        if (_convertUsing.TryGetValue(key, out var convertDel))
            return ((Func<TSource, TDest>)convertDel)(source);

        if (_constructors.TryGetValue(key, out var ctorDel))
            return ((Func<TSource, TDest>)ctorDel)(source);

        var ctor = typeof(TDest).GetConstructor(Type.EmptyTypes);
        if (ctor is not null)
            return (TDest)ctor.Invoke(null);

        return Activator.CreateInstance<TDest>();
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
        _convertUsing.Clear();
        _beforeMaps.Clear();
        _nullSubstitutes.Clear();
        _conditions.Clear();
        _useDestinationValue.Clear();
        _preserveReferences.Clear();
        _maxDepths.Clear();
        _includeBases.Clear();
        ServiceProvider = null;
        _currentContext = null;
    }

    /// <summary>
    /// Validates that every registered map can fully map all destination members.
    /// Throws <see cref="InvalidOperationException"/> if unmapped members are found.
    /// </summary>
    public static void AssertConfigurationIsValid()
    {
        var errors = new List<string>();

        var allKeys = new HashSet<(Type, Type)>();
        foreach (var key in _customs.Keys) allKeys.Add(key);
        foreach (var key in _ignores.Keys) allKeys.Add(key);
        foreach (var key in _constructors.Keys) allKeys.Add(key);
        foreach (var key in _convertUsing.Keys) allKeys.Add(key);
        foreach (var key in _afterMaps.Keys) allKeys.Add(key);
        foreach (var key in _beforeMaps.Keys) allKeys.Add(key);
        foreach (var key in _nullSubstitutes.Keys) allKeys.Add(key);
        foreach (var key in _conditions.Keys) allKeys.Add(key);
        foreach (var key in _useDestinationValue.Keys) allKeys.Add(key);
        foreach (var key in _classResolverTypes.Keys) allKeys.Add(key);
        foreach (var key in _preserveReferences.Keys) allKeys.Add(key);
        foreach (var key in _maxDepths.Keys) allKeys.Add(key);
        foreach (var key in _includeBases.Keys) allKeys.Add(key);

        foreach (var (srcType, destType) in allKeys)
        {
            var key = (srcType, destType);

            // ConvertUsing replaces auto-map entirely — skip validation
            if (_convertUsing.ContainsKey(key))
                continue;

            var destMembers = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p is { CanWrite: true, SetMethod.IsPublic: true })
                .Cast<MemberInfo>()
                .Concat(destType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => !f.IsInitOnly && !f.IsLiteral)
                    .Cast<MemberInfo>())
                .ToList();

            var srcMembers = srcType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p is { CanRead: true, GetMethod.IsPublic: true })
                .Cast<MemberInfo>()
                .Concat(srcType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => !f.IsLiteral)
                    .Cast<MemberInfo>())
                .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            var ignores = _ignores.GetValueOrDefault(key) ?? new HashSet<string>();
            var customs = _customs.GetValueOrDefault(key) ?? [];
            var classResolvers = _classResolverTypes.GetValueOrDefault(key) ?? new Dictionary<string, Type>();

            var unmapped = new List<string>();

            foreach (var destMember in destMembers)
            {
                var memberName = destMember.Name;

                if (ignores.Contains(memberName)) continue;
                if (customs.Any(c => c.DestMemberName == memberName)) continue;
                if (classResolvers.ContainsKey(memberName)) continue;
                if (srcMembers.ContainsKey(memberName)) continue;
                if (HasFlattenedSource(srcMembers, memberName)) continue;

                unmapped.Add(memberName);
            }

            if (unmapped.Count > 0)
            {
                errors.Add($"Unmapped members in {destType.Name} (map: {srcType.Name} → {destType.Name}): {string.Join(", ", unmapped)}");
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Configuration validation failed:\n" + string.Join("\n", errors));
    }

    // ── Internal helpers ────────────────────────────────────────────

    internal static void AddCustom<TSource, TDest>(string memberName, Delegate resolver)
    {
        var key = (typeof(TSource), typeof(TDest));
        var list = _customs.GetOrAdd(key, static _ => new List<CustomEntry>());
        list.RemoveAll(c => c.DestMemberName == memberName);
        list.Add(new CustomEntry { DestMemberName = memberName, Resolver = resolver });
    }

    internal static void AddIgnore<TSource, TDest>(string memberName)
    {
        var key = (typeof(TSource), typeof(TDest));
        var set = _ignores.GetOrAdd(key, static _ => new HashSet<string>(StringComparer.Ordinal));
        set.Add(memberName);
    }

    internal static void SetAfterMap<TSource, TDest>(Delegate afterMap)
    {
        var key = (typeof(TSource), typeof(TDest));
        _afterMaps[key] = afterMap;
    }

    internal static void SetBeforeMap<TSource, TDest>(Delegate beforeMap)
    {
        var key = (typeof(TSource), typeof(TDest));
        _beforeMaps[key] = beforeMap;
    }

    internal static void SetConstructor<TSource, TDest>(Delegate ctor)
    {
        var key = (typeof(TSource), typeof(TDest));
        _constructors[key] = ctor;
    }

    internal static void SetConvertUsing<TSource, TDest>(Delegate converter)
    {
        var key = (typeof(TSource), typeof(TDest));
        _convertUsing[key] = converter;
    }

    internal static void SetPreserveReferences<TSource, TDest>()
    {
        var key = (typeof(TSource), typeof(TDest));
        _preserveReferences[key] = true;
    }

    internal static void SetMaxDepth<TSource, TDest>(int maxDepth)
    {
        var key = (typeof(TSource), typeof(TDest));
        _maxDepths[key] = maxDepth;
    }

    internal static void SetNullSubstitute<TSource, TDest>(string memberName, object? value)
    {
        var key = (typeof(TSource), typeof(TDest));
        var dict = _nullSubstitutes.GetOrAdd(key, static _ => new Dictionary<string, object>(StringComparer.Ordinal));
        dict[memberName] = value!;
    }

    internal static void SetCondition<TSource, TDest>(string memberName, Delegate condition)
    {
        var key = (typeof(TSource), typeof(TDest));
        var dict = _conditions.GetOrAdd(key, static _ => new Dictionary<string, Delegate>(StringComparer.Ordinal));
        dict[memberName] = condition;
    }

    internal static void AddUseDestinationValue<TSource, TDest>(string memberName)
    {
        var key = (typeof(TSource), typeof(TDest));
        var set = _useDestinationValue.GetOrAdd(key, static _ => new HashSet<string>(StringComparer.Ordinal));
        set.Add(memberName);
    }

    internal static void AddIncludeBase<TDerivedSource, TDerivedDest, TBaseSource, TBaseDest>()
    {
        var derivedKey = (typeof(TDerivedSource), typeof(TDerivedDest));
        var baseKey = (typeof(TBaseSource), typeof(TBaseDest));
        var list = _includeBases.GetOrAdd(derivedKey, static _ => new List<(Type, Type)>());
        list.Add(baseKey);
    }

    internal static void RegisterReverseMap<TSource, TDest>()
    {
        // Create a plain reverse map (auto-map only — no customs/ignores carried over)
        var reverseKey = (typeof(TDest), typeof(TSource));
        _customs.GetOrAdd(reverseKey, static _ => new List<CustomEntry>());
        _ignores.GetOrAdd(reverseKey, static _ => new HashSet<string>(StringComparer.Ordinal));
        // Do NOT eagerly compile here — let ReverseMap().ForMember(...) chain
        // before the first Map call so the compiled delegate picks up customisations.
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

            _currentContext ??= new ResolutionContext();
            return (TMember?)((dynamic)instance).Resolve(source, dest, current, _currentContext);
        }
        return current;
    }

    // ── Delegate builders ───────────────────────────────────────────

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
        if (_convertUsing.TryGetValue(key, out var convertUsingDel))
        {
            steps.Add(Expression.Assign(destVar,
                Expression.Convert(
                    Expression.Invoke(Expression.Constant(convertUsingDel), srcParam),
                    typeof(TDest))));
        }
        else if (_constructors.TryGetValue(key, out var ctorDel))
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

        // 3. BeforeMap hook
        if (_beforeMaps.TryGetValue(key, out var beforeMapDel))
        {
            steps.Add(Expression.Invoke(Expression.Constant(beforeMapDel), srcParam, destVar));
        }

        // 4. Property + field mappings (skip if ConvertUsing overrides)
        if (!_convertUsing.ContainsKey(key))
        {
        var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanWrite: true, SetMethod.IsPublic: true })
            .Cast<MemberInfo>()
            .Concat(destType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly && !f.IsLiteral)
                .Cast<MemberInfo>())
            .ToList();

        var srcProps = srcType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, GetMethod.IsPublic: true })
            .Cast<MemberInfo>()
            .Concat(srcType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsLiteral)
                .Cast<MemberInfo>())
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var ignores = GetMergedIgnores(key);
        var customList = GetMergedCustoms(key);
        var customDict = new Dictionary<string, Delegate>(StringComparer.Ordinal);
        foreach (var c in customList)
            customDict[c.DestMemberName] = c.Resolver;

        var classResolvers = GetMergedClassResolvers(key);
        var nullSubstitutes = GetMergedNullSubstitutes(key);
        var conditions = GetMergedConditions(key);
        var udv = GetMergedUdv(key);

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
                var converted = Expression.Convert(invokeExpr, GetMemberType(destProp));
                var assignExpr = Expression.Assign(Expression.MakeMemberAccess(destVar, destProp), converted);
                steps.Add(WrapResolverWithPropertyCatch(assignExpr, srcType, destType, destProp.Name));
                continue;
            }

            // Class resolver (MapFrom<TResolver>) — resolved at runtime via ServiceProvider
            if (classResolvers.TryGetValue(destProp.Name, out _))
            {
                var resolveMethod = typeof(JAutoMapper).GetMethod(nameof(ResolveWithClassResolver),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(srcType, destType, GetMemberType(destProp));
                steps.Add(
                    Expression.Assign(
                        Expression.MakeMemberAccess(destVar, destProp),
                        Expression.Call(resolveMethod, srcParam, destVar,
                            Expression.MakeMemberAccess(destVar, destProp),
                            Expression.Constant(destProp.Name))
                    )
                );
                continue;
            }

            // Find matching source property (or flattened path like AddressCity -> Address.City)
            Expression? srcAccess = null;
            Type? srcPropType = null;
            if (srcProps.TryGetValue(destProp.Name, out var srcProp))
            {
                srcAccess = Expression.MakeMemberAccess(srcParam, srcProp);
                srcPropType = GetMemberType(srcProp);
            }
            else if (TryResolveFlattenedSource(srcParam, destProp.Name, srcProps, out var flatExpr, out var flatType))
            {
                srcAccess = flatExpr;
                srcPropType = flatType;
            }

            if (srcAccess == null) continue;

            // UseDestinationValue — keep existing destination value
            if (udv.Contains(destProp.Name))
                continue;

            // Build the value expression to assign
            Expression valueExpr;
            var destPropType = GetMemberType(destProp);

            if (destPropType.IsAssignableFrom(srcPropType))
            {
                valueExpr = Expression.Convert(srcAccess, destPropType);
            }
            else if (TryBuildConversionExpression(srcAccess, srcPropType, destPropType, out var convertedExpr))
            {
                valueExpr = convertedExpr;
            }
            else if (TryGetCollectionElementTypes(srcPropType, destPropType, out var srcElem, out var destElem))
            {
                var closedMapList = mapListMethodGeneric.MakeGenericMethod(srcElem, destElem);
                valueExpr = Expression.Call(closedMapList, srcAccess);
            }
            else if (!srcPropType.IsValueType && !destPropType.IsValueType)
            {
                var closedMap = mapMethodGeneric.MakeGenericMethod(srcPropType, destPropType);
                valueExpr = Expression.Call(closedMap, srcAccess);
            }
            else
            {
                continue; // incompatible types
            }

            // NullSubstitute — if source value is null, use substitute
            if (nullSubstitutes.TryGetValue(destProp.Name, out var substitute))
            {
                Expression isNull;
                if (srcPropType.IsValueType && Nullable.GetUnderlyingType(srcPropType) != null)
                {
                    isNull = Expression.Not(Expression.Property(srcAccess, "HasValue"));
                }
                else if (!srcPropType.IsValueType)
                {
                    isNull = Expression.ReferenceEqual(srcAccess, Expression.Constant(null, srcPropType));
                }
                else
                {
                    isNull = Expression.Constant(false);
                }
                var substituteExpr = Expression.Convert(Expression.Constant(substitute), destPropType);
                valueExpr = Expression.Condition(isNull, substituteExpr, valueExpr);
            }

            Expression finalAssignExpr = Expression.Assign(Expression.MakeMemberAccess(destVar, destProp), valueExpr);

            // Condition — only assign if condition returns true
            if (conditions.TryGetValue(destProp.Name, out var conditionDel))
            {
                var conditionExpr = Expression.Invoke(Expression.Constant(conditionDel), srcParam, destVar);
                finalAssignExpr = Expression.IfThen(conditionExpr, finalAssignExpr);
            }

            steps.Add(finalAssignExpr);
        } // end foreach
        } // end if (!_convertUsing.ContainsKey(key))

        // 5. AfterMap
        if (_afterMaps.TryGetValue(key, out var afterMapDel))
        {
            steps.Add(Expression.Invoke(Expression.Constant(afterMapDel), srcParam, destVar));
        }

        // 6. Return
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

        if (_convertUsing.TryGetValue(key, out var convertUsingDel2))
        {
            steps.Add(Expression.Assign(destParam,
                Expression.Convert(
                    Expression.Invoke(Expression.Constant(convertUsingDel2), srcParam),
                    typeof(TDest))));

            if (_afterMaps.TryGetValue(key, out var afterMapDel2))
                steps.Add(Expression.Invoke(Expression.Constant(afterMapDel2), srcParam, destParam));

            steps.Add(Expression.Label(returnLabel));
            var body2 = Expression.Block(steps);
            return Expression.Lambda<Action<TSource?, TDest?>>(body2, srcParam, destParam).Compile();
        }

        var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanWrite: true, SetMethod.IsPublic: true })
            .Cast<MemberInfo>()
            .Concat(destType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly && !f.IsLiteral)
                .Cast<MemberInfo>())
            .ToList();

        var srcProps = srcType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, GetMethod.IsPublic: true })
            .Cast<MemberInfo>()
            .Concat(srcType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsLiteral)
                .Cast<MemberInfo>())
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var ignores = GetMergedIgnores(key);
        var customList = GetMergedCustoms(key);
        var customDict = new Dictionary<string, Delegate>(StringComparer.Ordinal);
        foreach (var c in customList)
            customDict[c.DestMemberName] = c.Resolver;

        var classResolvers = GetMergedClassResolvers(key);
        var nullSubstitutes = GetMergedNullSubstitutes(key);
        var conditions = GetMergedConditions(key);
        var udv = GetMergedUdv(key);

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
                var converted = Expression.Convert(invokeExpr, GetMemberType(destProp));
                var assignExpr = Expression.Assign(Expression.MakeMemberAccess(destParam, destProp), converted);
                steps.Add(WrapResolverWithPropertyCatch(assignExpr, srcType, destType, destProp.Name));
                continue;
            }

            // Class resolver (MapFrom<TResolver>) — resolved at runtime via ServiceProvider
            if (classResolvers.TryGetValue(destProp.Name, out _))
            {
                var resolveMethod = typeof(JAutoMapper).GetMethod(nameof(ResolveWithClassResolver),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(srcType, destType, GetMemberType(destProp));
                steps.Add(
                    Expression.Assign(
                        Expression.MakeMemberAccess(destParam, destProp),
                        Expression.Call(resolveMethod, srcParam, destParam,
                            Expression.MakeMemberAccess(destParam, destProp),
                            Expression.Constant(destProp.Name))
                    )
                );
                continue;
            }

            // Find matching source property (or flattened path like AddressCity -> Address.City)
            Expression? srcAccess = null;
            Type? srcPropType = null;
            if (srcProps.TryGetValue(destProp.Name, out var srcProp))
            {
                srcAccess = Expression.MakeMemberAccess(srcParam, srcProp);
                srcPropType = GetMemberType(srcProp);
            }
            else if (TryResolveFlattenedSource(srcParam, destProp.Name, srcProps, out var flatExpr, out var flatType))
            {
                srcAccess = flatExpr;
                srcPropType = flatType;
            }

            if (srcAccess == null) continue;

            // UseDestinationValue — keep existing destination value
            if (udv.Contains(destProp.Name))
                continue;

            // Build the value expression to assign
            Expression valueExpr2;
            var destPropType2 = GetMemberType(destProp);

            if (destPropType2.IsAssignableFrom(srcPropType))
            {
                valueExpr2 = Expression.Convert(srcAccess, destPropType2);
            }
            else if (TryBuildConversionExpression(srcAccess, srcPropType, destPropType2, out var convertedExpr2))
            {
                valueExpr2 = convertedExpr2;
            }
            else if (TryGetCollectionElementTypes(srcPropType, destPropType2, out var srcElem, out var destElem))
            {
                var closedMapList = mapListMethodGeneric.MakeGenericMethod(srcElem, destElem);
                valueExpr2 = Expression.Call(closedMapList, srcAccess);
            }
            else if (!srcPropType.IsValueType && !destPropType2.IsValueType)
            {
                var closedMap = mapMethodGeneric.MakeGenericMethod(srcPropType, destPropType2);
                valueExpr2 = Expression.Call(closedMap, srcAccess);
            }
            else
            {
                continue; // incompatible types
            }

            // NullSubstitute
            if (nullSubstitutes.TryGetValue(destProp.Name, out var substitute2))
            {
                Expression isNull2;
                if (srcPropType.IsValueType && Nullable.GetUnderlyingType(srcPropType) != null)
                    isNull2 = Expression.Not(Expression.Property(srcAccess, "HasValue"));
                else if (!srcPropType.IsValueType)
                    isNull2 = Expression.ReferenceEqual(srcAccess, Expression.Constant(null, srcPropType));
                else
                    isNull2 = Expression.Constant(false);
                var substituteExpr2 = Expression.Convert(Expression.Constant(substitute2), destPropType2);
                valueExpr2 = Expression.Condition(isNull2, substituteExpr2, valueExpr2);
            }

            Expression finalAssignExpr2 = Expression.Assign(Expression.MakeMemberAccess(destParam, destProp), valueExpr2);

            // Condition
            if (conditions.TryGetValue(destProp.Name, out var conditionDel2))
            {
                var conditionExpr2 = Expression.Invoke(Expression.Constant(conditionDel2), srcParam, destParam);
                finalAssignExpr2 = Expression.IfThen(conditionExpr2, finalAssignExpr2);
            }

            steps.Add(finalAssignExpr2);
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
        var srcParam = Expression.Parameter(typeof(TSource), "src");
        var bindings = BuildProjectionBindings(srcParam, typeof(TSource), typeof(TDest));
        var newExpr = Expression.New(typeof(TDest));
        var memberInit = Expression.MemberInit(newExpr, bindings);
        return Expression.Lambda<Func<TSource, TDest>>(memberInit, srcParam);
    }

    private static List<MemberAssignment> BuildProjectionBindings(
        Expression sourceExpr, Type srcType, Type destType, int depth = 0)
    {
        if (depth > DefaultMaxRecursionDepth)
            return [];

        var key = (srcType, destType);

        var destProps = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanWrite: true, SetMethod.IsPublic: true })
            .Cast<MemberInfo>()
            .Concat(destType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly && !f.IsLiteral)
                .Cast<MemberInfo>())
            .ToList();

        var srcProps = srcType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, GetMethod.IsPublic: true })
            .Cast<MemberInfo>()
            .Concat(srcType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsLiteral)
                .Cast<MemberInfo>())
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var ignores = GetMergedIgnores(key);
        var customList = GetMergedCustoms(key);
        var customDict = new Dictionary<string, Delegate>(StringComparer.Ordinal);
        foreach (var c in customList)
            customDict[c.DestMemberName] = c.Resolver;

        var classResolvers = GetMergedClassResolvers(key);
        var nullSubstitutes = GetMergedNullSubstitutes(key);

        var bindings = new List<MemberAssignment>();

        foreach (var destProp in destProps)
        {
            if (ignores.Contains(destProp.Name)) continue;

            // Custom lambda resolver
            if (customDict.TryGetValue(destProp.Name, out var resolver))
            {
                var invokeExpr = Expression.Invoke(Expression.Constant(resolver), sourceExpr);
                var converted = Expression.Convert(invokeExpr, GetMemberType(destProp));
                bindings.Add(Expression.Bind(destProp, converted));
                continue;
            }

            // Class resolver (MapFrom<TResolver>) — works for in-memory queryables
            if (classResolvers.TryGetValue(destProp.Name, out _))
            {
                var resolveMethod = typeof(JAutoMapper).GetMethod(nameof(ResolveWithClassResolver),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(srcType, destType, GetMemberType(destProp));
                bindings.Add(
                    Expression.Bind(destProp,
                        Expression.Call(resolveMethod,
                            sourceExpr,
                            Expression.Constant(null, destType),
                            Expression.Default(GetMemberType(destProp)),
                            Expression.Constant(destProp.Name)))
                );
                continue;
            }

            Expression? srcAccess = null;
            Type? srcPropType = null;
            if (srcProps.TryGetValue(destProp.Name, out var srcProp))
            {
                srcAccess = Expression.MakeMemberAccess(sourceExpr, srcProp);
                srcPropType = GetMemberType(srcProp);
            }
            else if (TryResolveFlattenedSource(sourceExpr, destProp.Name, srcProps, out var flatExpr, out var flatType))
            {
                srcAccess = flatExpr;
                srcPropType = flatType;
            }

            if (srcAccess == null) continue;

            var destPropType = GetMemberType(destProp);
            Expression valueExpr;

            if (destPropType.IsAssignableFrom(srcPropType))
            {
                valueExpr = Expression.Convert(srcAccess, destPropType);
            }
            else if (TryBuildConversionExpression(srcAccess, srcPropType, destPropType, out var convertedExpr))
            {
                valueExpr = convertedExpr;
            }
            else if (!srcPropType.IsValueType && !destPropType.IsValueType
                     && _customs.ContainsKey((srcPropType, destPropType)))
            {
                // Nested complex type — recursive projection
                var nestedBindings = BuildProjectionBindings(srcAccess, srcPropType, destPropType, depth + 1);
                if (nestedBindings.Count == 0) continue;

                var nestedNew = Expression.New(destPropType);
                var nestedInit = Expression.MemberInit(nestedNew, nestedBindings);

                // Null guard: if source nested object is null, yield null destination
                var nullCheck = Expression.ReferenceEqual(srcAccess, Expression.Constant(null, srcPropType));
                valueExpr = Expression.Condition(nullCheck,
                    Expression.Constant(null, destPropType),
                    nestedInit);
            }
            else
            {
                continue; // unsupported
            }

            // NullSubstitute
            if (nullSubstitutes.TryGetValue(destProp.Name, out var substitute))
            {
                Expression isNull;
                if (srcPropType.IsValueType && Nullable.GetUnderlyingType(srcPropType) != null)
                    isNull = Expression.Not(Expression.Property(srcAccess, "HasValue"));
                else if (!srcPropType.IsValueType)
                    isNull = Expression.ReferenceEqual(srcAccess, Expression.Constant(null, srcPropType));
                else
                    isNull = Expression.Constant(false);
                var substituteExpr = Expression.Convert(Expression.Constant(substitute), destPropType);
                valueExpr = Expression.Condition(isNull, substituteExpr, valueExpr);
            }

            bindings.Add(Expression.Bind(destProp, valueExpr));
        }

        return bindings;
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

    // ── IncludeBase merged config helpers ────────────────────────

    private static HashSet<string> GetMergedIgnores((Type src, Type dest) key)
    {
        return GetMergedSet(key, _ignores);
    }

    private static HashSet<string> GetMergedUdv((Type src, Type dest) key)
    {
        return GetMergedSet(key, _useDestinationValue);
    }

    private static HashSet<string> GetMergedSet((Type src, Type dest) key,
        ConcurrentDictionary<(Type, Type), HashSet<string>> source)
    {
        var visited = new HashSet<(Type, Type)>();
        return GetMergedSet(key, source, visited);
    }

    private static HashSet<string> GetMergedSet((Type src, Type dest) key,
        ConcurrentDictionary<(Type, Type), HashSet<string>> source,
        HashSet<(Type, Type)> visited)
    {
        if (!visited.Add(key)) return new HashSet<string>(StringComparer.Ordinal);
        var result = new HashSet<string>(source.GetValueOrDefault(key) ?? new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        if (_includeBases.TryGetValue(key, out var bases))
        {
            foreach (var baseKey in bases)
            {
                foreach (var ig in GetMergedSet(baseKey, source, visited)) result.Add(ig);
            }
        }
        return result;
    }

    private static List<CustomEntry> GetMergedCustoms((Type src, Type dest) key)
    {
        var visited = new HashSet<(Type, Type)>();
        return GetMergedCustoms(key, visited);
    }

    private static List<CustomEntry> GetMergedCustoms((Type src, Type dest) key,
        HashSet<(Type, Type)> visited)
    {
        if (!visited.Add(key)) return [];
        var result = new List<CustomEntry>();
        if (_includeBases.TryGetValue(key, out var bases))
        {
            foreach (var baseKey in bases)
            {
                foreach (var c in GetMergedCustoms(baseKey, visited))
                {
                    if (!result.Any(r => r.DestMemberName == c.DestMemberName))
                        result.Add(c);
                }
            }
        }
        var own = _customs.GetValueOrDefault(key) ?? [];
        foreach (var c in own)
        {
            result.RemoveAll(r => r.DestMemberName == c.DestMemberName);
            result.Add(c);
        }
        return result;
    }

    private static Dictionary<string, Type> GetMergedClassResolvers((Type src, Type dest) key)
    {
        return GetMergedDict(key, _classResolverTypes);
    }

    private static Dictionary<string, Delegate> GetMergedConditions((Type src, Type dest) key)
    {
        return GetMergedDict(key, _conditions);
    }

    private static Dictionary<string, object> GetMergedNullSubstitutes((Type src, Type dest) key)
    {
        return GetMergedDict(key, _nullSubstitutes);
    }

    private static Dictionary<string, TValue> GetMergedDict<TValue>((Type src, Type dest) key,
        ConcurrentDictionary<(Type, Type), Dictionary<string, TValue>> source)
    {
        var visited = new HashSet<(Type, Type)>();
        return GetMergedDict(key, source, visited);
    }

    private static Dictionary<string, TValue> GetMergedDict<TValue>((Type src, Type dest) key,
        ConcurrentDictionary<(Type, Type), Dictionary<string, TValue>> source,
        HashSet<(Type, Type)> visited)
    {
        if (!visited.Add(key)) return new Dictionary<string, TValue>(StringComparer.Ordinal);
        var result = new Dictionary<string, TValue>(source.GetValueOrDefault(key) ?? new Dictionary<string, TValue>(StringComparer.Ordinal), StringComparer.Ordinal);
        if (_includeBases.TryGetValue(key, out var bases))
        {
            foreach (var baseKey in bases)
            {
                foreach (var kv in GetMergedDict(baseKey, source, visited))
                {
                    if (!result.ContainsKey(kv.Key))
                        result[kv.Key] = kv.Value;
                }
            }
        }
        return result;
    }

    private static Type GetMemberType(MemberInfo member) => member switch
    {
        PropertyInfo p => p.PropertyType,
        FieldInfo f => f.FieldType,
        _ => throw new NotSupportedException($"Member type {member.GetType()} not supported.")
    };

    /// <summary>
    /// Attempts to build an expression for common primitive / enum / nullable conversions.
    /// </summary>
    private static bool TryBuildConversionExpression(Expression srcExpr, Type srcType, Type destType, out Expression converted)
    {
        converted = null!;

        // Nullable<T> -> D (unwrap and convert, or default(D) if null)
        if (Nullable.GetUnderlyingType(srcType) is Type srcUnderlying)
        {
            if (TryBuildConversionExpression(Expression.Property(srcExpr, "Value"), srcUnderlying, destType, out var innerConverted))
            {
                var hasValue = Expression.Property(srcExpr, "HasValue");
                converted = Expression.Condition(hasValue, innerConverted, Expression.Default(destType));
                return true;
            }
            if (srcUnderlying == destType)
            {
                converted = Expression.Property(srcExpr, "Value");
                return true;
            }
        }
        // T -> Nullable<T>
        if (Nullable.GetUnderlyingType(destType) is Type destUnderlying && destUnderlying == srcType)
        {
            converted = Expression.New(destType.GetConstructor([destUnderlying])!, srcExpr);
            return true;
        }

        // Enum -> underlying type (e.g. MyEnum -> int)
        if (srcType.IsEnum && Enum.GetUnderlyingType(srcType) == destType)
        {
            converted = Expression.Convert(srcExpr, destType);
            return true;
        }
        // Underlying type -> enum (e.g. int -> MyEnum)
        if (destType.IsEnum && Enum.GetUnderlyingType(destType) == srcType)
        {
            converted = Expression.Convert(srcExpr, destType);
            return true;
        }
        // Enum -> string
        if (srcType.IsEnum && destType == typeof(string))
        {
            converted = Expression.Call(srcExpr, typeof(object).GetMethod("ToString")!);
            return true;
        }
        // String -> enum
        if (destType.IsEnum && srcType == typeof(string))
        {
            var parse = typeof(Enum).GetMethod("Parse", [typeof(Type), typeof(string)])!;
            converted = Expression.Convert(Expression.Call(parse, Expression.Constant(destType), srcExpr), destType);
            return true;
        }

        // String -> Guid
        if (srcType == typeof(string) && destType == typeof(Guid))
        {
            var parse = typeof(Guid).GetMethod("Parse", [typeof(string)])!;
            converted = Expression.Call(parse, srcExpr);
            return true;
        }
        // Guid -> string
        if (srcType == typeof(Guid) && destType == typeof(string))
        {
            converted = Expression.Call(srcExpr, typeof(object).GetMethod("ToString")!);
            return true;
        }

        // String -> DateTime
        if (srcType == typeof(string) && destType == typeof(DateTime))
        {
            var parse = typeof(DateTime).GetMethod("Parse", [typeof(string)])!;
            converted = Expression.Call(parse, srcExpr);
            return true;
        }
        // DateTime -> string
        if (srcType == typeof(DateTime) && destType == typeof(string))
        {
            converted = Expression.Call(srcExpr, typeof(object).GetMethod("ToString")!);
            return true;
        }

        // String -> DateTimeOffset
        if (srcType == typeof(string) && destType == typeof(DateTimeOffset))
        {
            var parse = typeof(DateTimeOffset).GetMethod("Parse", [typeof(string)])!;
            converted = Expression.Call(parse, srcExpr);
            return true;
        }
        // DateTimeOffset -> string
        if (srcType == typeof(DateTimeOffset) && destType == typeof(string))
        {
            converted = Expression.Call(srcExpr, typeof(object).GetMethod("ToString")!);
            return true;
        }

        // Numeric widening / narrowing
        if (IsNumeric(srcType) && IsNumeric(destType))
        {
            converted = Expression.Convert(srcExpr, destType);
            return true;
        }

        return false;
    }

    private static bool IsNumeric(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(byte) || t == typeof(sbyte)
            || t == typeof(short) || t == typeof(ushort)
            || t == typeof(int) || t == typeof(uint)
            || t == typeof(long) || t == typeof(ulong)
            || t == typeof(float) || t == typeof(double) || t == typeof(decimal);
    }

    /// <summary>
    /// Attempts to resolve a flattened source path, e.g. <c>AddressCity</c> → <c>Address.City</c>.
    /// </summary>
    private static bool TryResolveFlattenedSource(Expression srcParam, string destName,
        Dictionary<string, MemberInfo> srcMembers, out Expression srcExpr, out Type srcType)
    {
        srcExpr = null!;
        srcType = null!;
        for (int i = destName.Length - 1; i > 0; i--)
        {
            var prefix = destName.Substring(0, i);
            var suffix = destName.Substring(i);
            if (srcMembers.TryGetValue(prefix, out var srcMember))
            {
                var memberType = GetMemberType(srcMember);
                var subMembers = memberType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p is { CanRead: true, GetMethod.IsPublic: true })
                    .Cast<MemberInfo>()
                    .Concat(memberType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .Where(f => !f.IsLiteral)
                        .Cast<MemberInfo>())
                    .ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
                if (subMembers.TryGetValue(suffix, out var subMember))
                {
                    srcExpr = Expression.MakeMemberAccess(Expression.MakeMemberAccess(srcParam, srcMember), subMember);
                    srcType = GetMemberType(subMember);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Non-expression version of <see cref="TryResolveFlattenedSource"/> used by <see cref="AssertConfigurationIsValid"/>.
    /// </summary>
    private static bool HasFlattenedSource(Dictionary<string, MemberInfo> srcMembers, string destName)
    {
        for (int i = destName.Length - 1; i > 0; i--)
        {
            var prefix = destName.Substring(0, i);
            var suffix = destName.Substring(i);
            if (srcMembers.TryGetValue(prefix, out var srcMember))
            {
                var memberType = GetMemberType(srcMember);
                var subMembers = memberType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p is { CanRead: true, GetMethod.IsPublic: true })
                    .Cast<MemberInfo>()
                    .Concat(memberType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .Where(f => !f.IsLiteral)
                        .Cast<MemberInfo>())
                    .ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
                if (subMembers.ContainsKey(suffix))
                    return true;
            }
        }
        return false;
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
