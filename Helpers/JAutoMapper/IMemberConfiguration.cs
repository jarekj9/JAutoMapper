namespace JAM;

public interface IMemberConfiguration<out TSource, TDest>
{
    /// <summary>Skip this destination property during mapping.</summary>
    void Ignore();

    /// <summary>
    /// Use a named class resolver to compute the value for this destination member.
    /// <paramref name="TResolver"/> must implement <see cref="IValueResolver{TSource,TDest,TMember}"/>
    /// (for any TMember compatible with the destination property).
    /// The resolver is created via <see cref="JAutoMapper.ServiceProvider"/> (supports DI).
    /// </summary>
    void MapFrom<TResolver>();
}
