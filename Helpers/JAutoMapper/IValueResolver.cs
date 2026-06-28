namespace JAM;

/// <summary>
/// Full-featured value resolver matching AutoMapper's classic pattern.
/// Implement this and register via <c>ForMember(d => d.Prop, opt => opt.MapFrom&lt;MyResolver&gt;())</c>.
/// Resolver instances are created through <see cref="JAutoMapper.ServiceProvider"/> (supports DI).
/// </summary>
public interface IValueResolver<in TSource, in TDest, TMember>
{
    /// <param name="source">Source object being mapped from.</param>
    /// <param name="destination">Destination object being mapped into (partially built).</param>
    /// <param name="destMember">Current value of the destination member (before mapping).</param>
    /// <param name="context">Resolution context.</param>
    TMember Resolve(TSource source, TDest destination, TMember destMember, ResolutionContext context);
}
