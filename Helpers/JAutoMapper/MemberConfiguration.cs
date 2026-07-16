namespace JAM;

internal class MemberConfiguration<TSource, TDest> : IMemberConfiguration<TSource, TDest>
{
    public string MemberName { get; }

    public MemberConfiguration(string memberName)
    {
        MemberName = memberName;
    }

    public void Ignore()
    {
        JAutoMapper.AddIgnore<TSource, TDest>(MemberName);
    }

    public void MapFrom<TResolver>()
    {
        JAutoMapper.AddClassResolver<TSource, TDest>(MemberName, typeof(TResolver));
    }

    public void NullSubstitute(object? value)
    {
        JAutoMapper.SetNullSubstitute<TSource, TDest>(MemberName, value);
    }

    public void Condition(Func<TSource, TDest, bool> condition)
    {
        JAutoMapper.SetCondition<TSource, TDest>(MemberName, condition);
    }

    public void UseDestinationValue()
    {
        JAutoMapper.AddUseDestinationValue<TSource, TDest>(MemberName);
    }
}
