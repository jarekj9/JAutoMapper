using System.Linq.Expressions;
using System.Reflection;

namespace JAM;

public class MapConfig<TSource, TDest>
{
    /// <summary>
    /// Custom resolver for a destination property.
    /// The <paramref name="resolver"/> receives the source object and returns the value for the destination property.
    /// </summary>
    public MapConfig<TSource, TDest> ForMember<TProp>(
        Expression<Func<TDest, TProp>> destMember,
        Func<TSource, TProp?> resolver)
    {
        var memberName = GetMemberName(destMember);
        JAutoMapper.AddCustom<TSource, TDest>(memberName, resolver);
        return this;
    }

    /// <summary>
    /// Custom resolver with access to the source object (returns object).
    /// </summary>
    public MapConfig<TSource, TDest> ForMember(
        Expression<Func<TDest, object?>> destMember,
        Func<TSource, object?> resolver)
    {
        var memberName = GetMemberName(destMember);
        JAutoMapper.AddCustom<TSource, TDest>(memberName, resolver);
        return this;
    }

    /// <summary>Skip this destination property during mapping.</summary>
    public MapConfig<TSource, TDest> Ignore(Expression<Func<TDest, object?>> destMember)
    {
        var memberName = GetMemberName(destMember);
        JAutoMapper.AddIgnore<TSource, TDest>(memberName);
        return this;
    }

    /// <summary>Hook called before property mapping starts.</summary>
    public MapConfig<TSource, TDest> BeforeMap(Action<TSource, TDest> beforeAction)
    {
        JAutoMapper.SetBeforeMap<TSource, TDest>(beforeAction);
        return this;
    }

    /// <summary>Hook called after mapping completes.</summary>
    public MapConfig<TSource, TDest> AfterMap(Action<TSource, TDest> afterAction)
    {
        JAutoMapper.SetAfterMap<TSource, TDest>(afterAction);
        return this;
    }

    /// <summary>
    /// Register the inverse map (TDest → TSource).
    /// Returns a config for the reverse map so it can be further customized.
    /// </summary>
    public MapConfig<TDest, TSource> ReverseMap()
    {
        JAutoMapper.RegisterReverseMap<TSource, TDest>();
        return JAutoMapper.GetReverseConfig<TSource, TDest>();
    }

    /// <summary>Alias for ForMember. Custom resolver for a destination property.</summary>
    public MapConfig<TSource, TDest> MapFrom<TProp>(
        Expression<Func<TDest, TProp>> destMember,
        Func<TSource, TProp?> resolver)
    {
        return ForMember(destMember, resolver);
    }

    /// <summary>Alias for ForMember. Custom resolver with access to the source object (returns object).</summary>
    public MapConfig<TSource, TDest> MapFrom(
        Expression<Func<TDest, object?>> destMember,
        Func<TSource, object?> resolver)
    {
        return ForMember(destMember, resolver);
    }

    /// <summary>
    /// Configure a destination member using member options (e.g., <c>act => act.Ignore()</c>).
    /// </summary>
    public MapConfig<TSource, TDest> ForMember(
        Expression<Func<TDest, object?>> destMember,
        Action<IMemberConfiguration<TSource, TDest>> memberOptions)
    {
        var memberName = GetMemberName(destMember);
        var cfg = new MemberConfiguration<TSource, TDest>(memberName);
        memberOptions(cfg);
        return this;
    }

    /// <summary>Custom constructor instead of <c>new TDest()</c>.</summary>
    public MapConfig<TSource, TDest> ConstructUsing(Func<TSource, TDest> constructor)
    {
        JAutoMapper.SetConstructor<TSource, TDest>(constructor);
        return this;
    }

    /// <summary>
    /// Replace the entire auto-mapping for this pair with a custom converter function.
    /// AfterMap is still invoked if configured.
    /// </summary>
    public MapConfig<TSource, TDest> ConvertUsing(Func<TSource, TDest> converter)
    {
        JAutoMapper.SetConvertUsing<TSource, TDest>(converter);
        return this;
    }

    /// <summary>
    /// Preserve references for circular references — reuses already-mapped destination instances
    /// instead of throwing a <see cref="MappingException"/>.
    /// </summary>
    public MapConfig<TSource, TDest> PreserveReferences()
    {
        JAutoMapper.SetPreserveReferences<TSource, TDest>();
        return this;
    }

    /// <summary>
    /// Set the maximum recursion depth for this map (default is 32).
    /// Only applies when <c>PreserveReferences</c> is not set.
    /// </summary>
    public MapConfig<TSource, TDest> MaxDepth(int maxDepth)
    {
        JAutoMapper.SetMaxDepth<TSource, TDest>(maxDepth);
        return this;
    }

    /// <summary>
    /// Inherit mapping configurations from a base type map.
    /// When mapping <c>TSource → TDest</c>, also applies ignores, custom resolvers,
    /// conditions, null substitutes, and class resolvers from <c>TBaseSource → TBaseDest</c>.
    /// </summary>
    public MapConfig<TSource, TDest> IncludeBase<TBaseSource, TBaseDest>()
    {
        JAutoMapper.AddIncludeBase<TSource, TDest, TBaseSource, TBaseDest>();
        return this;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static string GetMemberName<T>(Expression<Func<T, object?>> expr)
    {
        var body = expr.Body;
        if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            body = unary.Operand;
        if (body is MemberExpression me)
            return me.Member.Name;
        throw new ArgumentException("Expression must be a member access (e.g., d => d.PropertyName).");
    }

    private static string GetMemberName<T, TProp>(Expression<Func<T, TProp>> expr)
    {
        var body = expr.Body;
        if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            body = unary.Operand;
        if (body is MemberExpression me)
            return me.Member.Name;
        throw new ArgumentException("Expression must be a member access (e.g., d => d.PropertyName).");
    }
}
