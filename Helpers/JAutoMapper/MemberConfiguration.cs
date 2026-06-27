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
}
