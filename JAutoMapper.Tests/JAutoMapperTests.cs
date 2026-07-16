using JAM;
using JAM.Models;

namespace JAM.Tests;

public class JAutoMapperTests
{
    // ── Named class resolver for MapFrom<TResolver> test ──────────────
public class FullNameResolver : IValueResolver<SimpleSrc, DestWithExtra, object?>
{
    public object? Resolve(SimpleSrc source, DestWithExtra destination, object? destMember, ResolutionContext context)
        => $"{source.Name} - resolved";
}

// ── Resolver with constructor injection (DI scenario) ─────────────
public interface IUserService
{
    string GetRole<T>(T user);
}

public class FakeUserService : IUserService
{
    public string GetRole<T>(T user) => "Admin";
}

public class UserRoleResolver : IValueResolver<SimpleSrc, DestWithExtra, object?>
{
    private readonly IUserService _userService;
    public UserRoleResolver(IUserService userService) => _userService = userService;
    public object? Resolve(SimpleSrc source, DestWithExtra destination, object? destMember, ResolutionContext context)
        => _userService.GetRole(source);
}

// ── Model classes for tests ─────────────────────────────────────

    public class SimpleSrc
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class SimpleDest
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class DestWithExtra
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public int Value { get; set; }
        public string? Extra { get; set; }
    }

    public class NestedSrc
    {
        public SimpleSrc? Inner { get; set; }
    }

    public class NestedDest
    {
        public SimpleDest? Inner { get; set; }
    }

    public class SrcWithField
    {
        public Guid Id;
        public string Name = string.Empty;
        public int Value;
    }

    public class DestWithField
    {
        public Guid Id;
        public string Name = string.Empty;
        public int Value;
    }

    public class SrcWithFieldAndProp
    {
        public Guid Id { get; set; }
        public string Name = string.Empty;
    }

    public class DestWithFieldAndProp
    {
        public Guid Id;
        public string Name { get; set; } = string.Empty;
    }

    public class EnumSrc
    {
        public StatusCode Status { get; set; }
        public StatusCode? OptionalStatus { get; set; }
        public int StatusInt { get; set; }
    }

    public class EnumDest
    {
        public int Status { get; set; }
        public string OptionalStatus { get; set; } = string.Empty;
        public StatusCode StatusInt { get; set; }
    }

    public enum StatusCode
    {
        OK = 1,
        Error = 2
    }

    public class PrimitiveSrc
    {
        public int Count { get; set; }
        public double Amount { get; set; }
        public Guid Id { get; set; }
        public DateTime Created { get; set; }
    }

    public class PrimitiveDest
    {
        public long Count { get; set; }
        public decimal Amount { get; set; }
        public string Id { get; set; } = string.Empty;
        public string Created { get; set; } = string.Empty;
    }

    public class NullableSrc
    {
        public int? Value { get; set; }
    }

    public class NullableDest
    {
        public int Value { get; set; }
    }

    public class ConvertSrc
    {
        public string Data { get; set; } = string.Empty;
    }

    public class ConvertDest
    {
        public string Data { get; set; } = string.Empty;
        public string Extra { get; set; } = string.Empty;
    }

    public class HookSrc
    {
        public string Name { get; set; } = string.Empty;
    }

    public class HookDest
    {
        public string Name { get; set; } = string.Empty;
        public string Before { get; set; } = string.Empty;
        public string After { get; set; } = string.Empty;
    }

    public class NullSubSrc
    {
        public string? Value { get; set; }
    }

    public class NullSubDest
    {
        public string Value { get; set; } = string.Empty;
    }

    public class CondSrc
    {
        public string Name { get; set; } = string.Empty;
        public bool ShouldMap { get; set; }
    }

    public class CondDest
    {
        public string Name { get; set; } = string.Empty;
    }

    public class UdvSrc
    {
        public string Name { get; set; } = string.Empty;
    }

    public class UdvDest
    {
        public string Name { get; set; } = "Existing";
    }

    public class FlattenSrc
    {
        public Address Address { get; set; } = new();
    }

    public class FlattenDest
    {
        public string AddressCity { get; set; } = string.Empty;
        public string AddressStreet { get; set; } = string.Empty;
    }

    public class SrcWithCollection
    {
        public List<SimpleSrc>? Items { get; set; }
    }

    public class DestWithCollection
    {
        public List<SimpleDest>? Items { get; set; }
    }

    public class CircularA
    {
        public CircularB? B { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class CircularADto
    {
        public CircularBDto? B { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class CircularB
    {
        public CircularA? A { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class CircularBDto
    {
        public CircularADto? A { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class CircularA2
    {
        public CircularB2? B { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class CircularADto2
    {
        public CircularBDto2? B { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class CircularB2
    {
        public CircularA2? A { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class CircularBDto2
    {
        public CircularADto2? A { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class SrcWithCtor
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;

        public SrcWithCtor() { }
        public SrcWithCtor(Guid id, string name) => (Id, Name) = (id, name);
    }

    public class DestCustomCtor
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class SrcWithName
    {
        public string Name { get; set; } = string.Empty;
    }

    public class DestWithNameAndAge
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    public class BaseSrc
    {
        public string Name { get; set; } = string.Empty;
    }

    public class DerivedSrc : BaseSrc
    {
        public int Age { get; set; }
    }

    public class BaseDest
    {
        public string Name { get; set; } = string.Empty;
    }

    public class DerivedDest : BaseDest
    {
        public int Age { get; set; }
    }

    public class DerivedSrc2 : BaseSrc
    {
        public int Age { get; set; }
    }

    public class DerivedDest2 : BaseDest
    {
        public int Age { get; set; }
    }

    // ── Setup / Teardown ────────────────────────────────────────────

    public JAutoMapperTests()
    {
        JAutoMapper.Reset();
    }

    // ── Tests ───────────────────────────────────────────────────────

    [Fact]
    public void Property_flattening_maps_nested_property()
    {
        JAutoMapper.CreateMap<FlattenSrc, FlattenDest>();

        var src = new FlattenSrc { Address = new Address { City = "Krakow", Street = "Main" } };
        var dest = JAutoMapper.Map<FlattenSrc, FlattenDest>(src);

        Assert.Equal("Krakow", dest.AddressCity);
        Assert.Equal("Main", dest.AddressStreet);
    }

    [Fact]
    public void Enum_to_int_and_string_conversion()
    {
        JAutoMapper.CreateMap<EnumSrc, EnumDest>();

        var src = new EnumSrc { Status = StatusCode.Error, OptionalStatus = StatusCode.OK, StatusInt = 1 };
        var dest = JAutoMapper.Map<EnumSrc, EnumDest>(src);

        Assert.Equal(2, dest.Status);
        Assert.Equal("OK", dest.OptionalStatus);
        Assert.Equal(StatusCode.OK, dest.StatusInt);
    }

    [Fact]
    public void Nullable_to_non_nullable_conversion()
    {
        JAutoMapper.CreateMap<NullableSrc, NullableDest>();

        var src = new NullableSrc { Value = 42 };
        var dest = JAutoMapper.Map<NullableSrc, NullableDest>(src);

        Assert.Equal(42, dest.Value);
    }

    [Fact]
    public void Numeric_widening_and_guid_datetime_to_string()
    {
        JAutoMapper.CreateMap<PrimitiveSrc, PrimitiveDest>();

        var id = Guid.NewGuid();
        var dt = new DateTime(2024, 1, 15);
        var src = new PrimitiveSrc { Count = 5, Amount = 3.14, Id = id, Created = dt };
        var dest = JAutoMapper.Map<PrimitiveSrc, PrimitiveDest>(src);

        Assert.Equal(5L, dest.Count);
        Assert.Equal(3.14m, dest.Amount);
        Assert.Equal(id.ToString(), dest.Id);
        Assert.Equal(dt.ToString(), dest.Created);
    }

    [Fact]
    public void ConvertUsing_replaces_auto_mapping()
    {
        JAutoMapper.CreateMap<ConvertSrc, ConvertDest>()
            .ConvertUsing(src => new ConvertDest { Data = src.Data.ToUpper(), Extra = "custom" });

        var src = new ConvertSrc { Data = "hello" };
        var dest = JAutoMapper.Map<ConvertSrc, ConvertDest>(src);

        Assert.Equal("HELLO", dest.Data);
        Assert.Equal("custom", dest.Extra);
    }

    [Fact]
    public void ConvertUsing_with_AfterMap_still_works()
    {
        var afterCalled = false;
        JAutoMapper.CreateMap<ConvertSrc, ConvertDest>()
            .ConvertUsing(src => new ConvertDest { Data = src.Data })
            .AfterMap((_, _) => afterCalled = true);

        var src = new ConvertSrc { Data = "x" };
        JAutoMapper.Map<ConvertSrc, ConvertDest>(src);

        Assert.True(afterCalled);
    }

    [Fact]
    public void BeforeMap_and_AfterMap_both_called()
    {
        var beforeCalled = false;
        var afterCalled = false;

        JAutoMapper.CreateMap<HookSrc, HookDest>()
            .BeforeMap((src, dest) => { beforeCalled = true; dest.Before = $"before-{src.Name}"; })
            .AfterMap((src, dest) => { afterCalled = true; dest.After = $"after-{src.Name}"; });

        var src = new HookSrc { Name = "Test" };
        var dest = JAutoMapper.Map<HookSrc, HookDest>(src);

        Assert.True(beforeCalled);
        Assert.True(afterCalled);
        Assert.Equal("before-Test", dest.Before);
        Assert.Equal("after-Test", dest.After);
        Assert.Equal("Test", dest.Name);
    }

    [Fact]
    public void NullSubstitute_used_when_source_null()
    {
        JAutoMapper.CreateMap<NullSubSrc, NullSubDest>()
            .ForMember(d => d.Value, opt => opt.NullSubstitute("fallback"));

        var src = new NullSubSrc { Value = null };
        var dest = JAutoMapper.Map<NullSubSrc, NullSubDest>(src);

        Assert.Equal("fallback", dest.Value);
    }

    [Fact]
    public void NullSubstitute_not_used_when_source_has_value()
    {
        JAutoMapper.CreateMap<NullSubSrc, NullSubDest>()
            .ForMember(d => d.Value, opt => opt.NullSubstitute("fallback"));

        var src = new NullSubSrc { Value = "real" };
        var dest = JAutoMapper.Map<NullSubSrc, NullSubDest>(src);

        Assert.Equal("real", dest.Value);
    }

    [Fact]
    public void Condition_skips_mapping_when_false()
    {
        JAutoMapper.CreateMap<CondSrc, CondDest>()
            .ForMember(d => d.Name, opt => opt.Condition((src, dest) => src.ShouldMap));

        var src = new CondSrc { Name = "John", ShouldMap = false };
        var dest = JAutoMapper.Map<CondSrc, CondDest>(src);

        Assert.Equal(string.Empty, dest.Name);
    }

    [Fact]
    public void Condition_maps_when_true()
    {
        JAutoMapper.CreateMap<CondSrc, CondDest>()
            .ForMember(d => d.Name, opt => opt.Condition((src, dest) => src.ShouldMap));

        var src = new CondSrc { Name = "John", ShouldMap = true };
        var dest = JAutoMapper.Map<CondSrc, CondDest>(src);

        Assert.Equal("John", dest.Name);
    }

    [Fact]
    public void UseDestinationValue_preserves_existing()
    {
        JAutoMapper.CreateMap<UdvSrc, UdvDest>()
            .ForMember(d => d.Name, opt => opt.UseDestinationValue());

        var src = new UdvSrc { Name = "New" };
        var existing = new UdvDest { Name = "Existing" };

        JAutoMapper.MapInto(src, existing);

        Assert.Equal("Existing", existing.Name);
    }

    [Fact]
    public void Simple_flat_mapping_matching_field_names()
    {
        JAutoMapper.CreateMap<SrcWithField, DestWithField>();

        var src = new SrcWithField { Id = Guid.NewGuid(), Name = "Fieldy", Value = 42 };
        var dest = JAutoMapper.Map<SrcWithField, DestWithField>(src);

        Assert.NotNull(dest);
        Assert.Equal(src.Id, dest.Id);
        Assert.Equal(src.Name, dest.Name);
        Assert.Equal(src.Value, dest.Value);
    }

    [Fact]
    public void Mixed_fields_and_properties_mapped()
    {
        JAutoMapper.CreateMap<SrcWithFieldAndProp, DestWithFieldAndProp>();

        var src = new SrcWithFieldAndProp { Id = Guid.NewGuid(), Name = "Mixed" };
        var dest = JAutoMapper.Map<SrcWithFieldAndProp, DestWithFieldAndProp>(src);

        Assert.NotNull(dest);
        Assert.Equal(src.Id, dest.Id);
        Assert.Equal(src.Name, dest.Name);
    }

    [Fact]
    public void Simple_flat_mapping_matching_property_names()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Test", Value = 42 };
        var dest = JAutoMapper.Map<SimpleSrc, SimpleDest>(src);

        Assert.NotNull(dest);
        Assert.Equal(src.Id, dest.Id);
        Assert.Equal(src.Name, dest.Name);
        Assert.Equal(src.Value, dest.Value);
    }

    [Fact]
    public void ForMember_custom_resolver()
    {
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .ForMember(d => d.FullName, s => $"{s.Name} - {s.Value}");

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Test", Value = 42 };
        var dest = JAutoMapper.Map<SimpleSrc, DestWithExtra>(src);

        Assert.NotNull(dest);
        Assert.Equal(src.Id, dest.Id);
        Assert.Equal($"{src.Name} - {src.Value}", dest.FullName);
        Assert.Equal(src.Value, dest.Value);
    }

    [Fact]
    public void Ignore_skips_property()
    {
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .Ignore(d => d.Extra);

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Test", Value = 42 };
        // Set Extra via a different property won't work since Extra doesn't match src.
        // Instead verify that the ignored property keeps default
        var dest = JAutoMapper.Map<SimpleSrc, DestWithExtra>(src);

        Assert.NotNull(dest);
        Assert.Null(dest.Extra);
    }

    [Fact]
    public void ForMember_with_act_Ignore_skips_property()
    {
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .ForMember(d => d.Extra, act => act.Ignore());

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Test", Value = 42 };
        var dest = JAutoMapper.Map<SimpleSrc, DestWithExtra>(src);

        Assert.NotNull(dest);
        Assert.Null(dest.Extra);
    }

    [Fact]
    public void Null_source_returns_default()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();

        SimpleSrc? nullSrc = null;
        var result = JAutoMapper.Map<SimpleSrc, SimpleDest>(nullSrc);

        Assert.Null(result);
    }

    [Fact]
    public void MapList_returns_correct_count()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();

        var list = new List<SimpleSrc>
        {
            new() { Id = Guid.NewGuid(), Name = "A", Value = 1 },
            new() { Id = Guid.NewGuid(), Name = "B", Value = 2 },
            new() { Id = Guid.NewGuid(), Name = "C", Value = 3 }
        };

        var result = JAutoMapper.MapList<SimpleSrc, SimpleDest>(list);

        Assert.Equal(3, result.Count);
        Assert.Equal("A", result[0].Name);
        Assert.Equal(2, result[1].Value);
    }

    [Fact]
    public void MapList_null_input_returns_empty_list()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();

        var result = JAutoMapper.MapList<SimpleSrc, SimpleDest>(null);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void MapInto_updates_existing_object()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Updated", Value = 99 };
        var existing = new SimpleDest { Id = Guid.NewGuid(), Name = "Original", Value = 0 };

        JAutoMapper.MapInto(src, existing);

        Assert.Equal(src.Id, existing.Id);
        Assert.Equal("Updated", existing.Name);
        Assert.Equal(99, existing.Value);
    }

    [Fact]
    public void Nested_object_auto_mapped_recursively()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();
        JAutoMapper.CreateMap<NestedSrc, NestedDest>();

        var src = new NestedSrc
        {
            Inner = new SimpleSrc { Id = Guid.NewGuid(), Name = "Nested", Value = 7 }
        };

        var dest = JAutoMapper.Map<NestedSrc, NestedDest>(src);

        Assert.NotNull(dest);
        Assert.NotNull(dest.Inner);
        Assert.Equal(src.Inner.Id, dest.Inner.Id);
        Assert.Equal(src.Inner.Name, dest.Inner.Name);
        Assert.Equal(src.Inner.Value, dest.Inner.Value);
    }

    [Fact]
    public void Nested_collection_auto_mapped_recursively()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();
        JAutoMapper.CreateMap<SrcWithCollection, DestWithCollection>();

        var src = new SrcWithCollection
        {
            Items =
            [
                new SimpleSrc { Id = Guid.NewGuid(), Name = "A", Value = 1 },
                new SimpleSrc { Id = Guid.NewGuid(), Name = "B", Value = 2 }
            ]
        };

        var dest = JAutoMapper.Map<SrcWithCollection, DestWithCollection>(src);

        Assert.NotNull(dest);
        Assert.NotNull(dest.Items);
        Assert.Equal(2, dest.Items.Count);
        Assert.Equal("A", dest.Items[0].Name);
        Assert.Equal(2, dest.Items[1].Value);
    }

    [Fact]
    public void ReverseMap_maps_back_correctly()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>().ReverseMap();

        var original = new SimpleSrc { Id = Guid.NewGuid(), Name = "Forward", Value = 10 };
        var forward = JAutoMapper.Map<SimpleSrc, SimpleDest>(original);
        var back = JAutoMapper.Map<SimpleDest, SimpleSrc>(forward!);

        Assert.NotNull(back);
        Assert.Equal(original.Id, back.Id);
        Assert.Equal(original.Name, back.Name);
        Assert.Equal(original.Value, back.Value);
    }

    [Fact]
    public void ReverseMap_with_ForMember_on_reverse()
    {
        // Forward: SimpleSrc -> DestWithExtra (FullName = computed)
        // Reverse: DestWithExtra -> SimpleSrc (custom FirstName from FullName)
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .ForMember(d => d.FullName, s => $"{s.Name} - {s.Value}")
            .ReverseMap()
            .ForMember(d => d.Name, s => s.FullName.Split(" - ")[0]);

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Alice", Value = 25 };
        var fwd = JAutoMapper.Map<SimpleSrc, DestWithExtra>(src);
        var rev = JAutoMapper.Map<DestWithExtra, SimpleSrc>(fwd!);

        Assert.NotNull(rev);
        Assert.Equal(src.Id, rev.Id);
        Assert.Equal("Alice", rev.Name);
    }

    [Fact]
    public void AfterMap_hook_called()
    {
        var hookCalled = false;

        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>()
            .AfterMap((_, _) => hookCalled = true);

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Test", Value = 1 };
        JAutoMapper.Map<SimpleSrc, SimpleDest>(src);

        Assert.True(hookCalled);
    }

    [Fact]
    public void ConstructUsing_uses_custom_constructor()
    {
        // Use a read-only property that auto-map can't overwrite
        JAutoMapper.CreateMap<SrcWithCtor, DestCustomCtor>()
            .ConstructUsing(s => new DestCustomCtor { Id = Guid.NewGuid(), Name = s.Name + " (constructed)" })
            .Ignore(d => d.Id)   // custom constructor sets Id
            .Ignore(d => d.Name); // custom constructor sets Name

        var src = new SrcWithCtor(Guid.NewGuid(), "Test");
        var dest = JAutoMapper.Map<SrcWithCtor, DestCustomCtor>(src);

        Assert.NotNull(dest);
        Assert.NotEqual(src.Id, dest.Id); // constructor set a different Id
        Assert.Equal("Test (constructed)", dest.Name);
    }

    [Fact]
    public void Missing_map_throws_InvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JAutoMapper.Map<SimpleSrc, SimpleDest>(new SimpleSrc()));

        Assert.Contains("No map registered", ex.Message);
        Assert.Contains(nameof(SimpleSrc), ex.Message);
        Assert.Contains(nameof(SimpleDest), ex.Message);
    }

    [Fact]
    public void Reset_clears_all_maps()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();

        // Should work before reset
        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Before", Value = 1 };
        var dest = JAutoMapper.Map<SimpleSrc, SimpleDest>(src);
        Assert.NotNull(dest);

        // Reset
        JAutoMapper.Reset();

        // Should throw after reset
        Assert.Throws<InvalidOperationException>(() =>
            JAutoMapper.Map<SimpleSrc, SimpleDest>(src));
    }

    [Fact]
    public void Circular_reference_throws_MappingException()
    {
        JAutoMapper.CreateMap<CircularA, CircularADto>();
        JAutoMapper.CreateMap<CircularB, CircularBDto>();

        var a = new CircularA { Name = "A" };
        var b = new CircularB { Name = "B" };
        a.B = b;
        b.A = a;

        var ex = Assert.Throws<MappingException>(() =>
            JAutoMapper.Map<CircularA, CircularADto>(a));

        Assert.Contains("Circular reference", ex.Message);
    }

    [Fact]
    public void PreserveReferences_reuses_mapped_instances()
    {
        JAutoMapper.CreateMap<CircularA2, CircularADto2>().PreserveReferences();
        JAutoMapper.CreateMap<CircularB2, CircularBDto2>().PreserveReferences();

        var a = new CircularA2 { Name = "A" };
        var b = new CircularB2 { Name = "B" };
        a.B = b;
        b.A = a;

        var dto = JAutoMapper.Map<CircularA2, CircularADto2>(a);

        Assert.NotNull(dto);
        Assert.NotNull(dto.B);
        Assert.NotNull(dto.B.A);
        // Because of preserve references, dto.B.A should be the same instance as dto
        Assert.Same(dto, dto.B.A);
    }

    [Fact]
    public void MaxDepth_custom_limit_throws_when_exceeded()
    {
        JAutoMapper.CreateMap<CircularA2, CircularADto2>().MaxDepth(2);
        JAutoMapper.CreateMap<CircularB2, CircularBDto2>().MaxDepth(2);

        var a = new CircularA2 { Name = "A" };
        var b = new CircularB2 { Name = "B" };
        a.B = b;
        b.A = a;

        var ex = Assert.Throws<MappingException>(() =>
            JAutoMapper.Map<CircularA2, CircularADto2>(a));

        Assert.Contains("depth > 2", ex.Message);
    }

    [Fact]
    public void Convenience_overload_Map_TDest_object()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Conv", Value = 77 };
        object boxed = src;

        var dest = JAutoMapper.Map<SimpleDest>(boxed);

        Assert.NotNull(dest);
        Assert.Equal(src.Id, dest.Id);
        Assert.Equal(src.Name, dest.Name);
        Assert.Equal(src.Value, dest.Value);
    }

    [Fact]
    public void ForMember_resolver_throws_wraps_in_MappingException()
    {
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .ForMember(d => d.FullName, s => throw new InvalidOperationException("Boom!"));

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Test", Value = 1 };

        var ex = Assert.Throws<MappingException>(() =>
            JAutoMapper.Map<SimpleSrc, DestWithExtra>(src));

        Assert.Contains("Boom!", ex.Message);
        Assert.Equal("FullName", ex.PropertyName);
    }

    [Fact]
    public void ProjectTo_produces_correct_IQueryable_expression()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();

        var data = new List<SimpleSrc>
        {
            new() { Id = Guid.NewGuid(), Name = "Alpha", Value = 10 },
            new() { Id = Guid.NewGuid(), Name = "Beta",  Value = 20 }
        }.AsQueryable();

        var projected = data.ProjectTo<SimpleSrc, SimpleDest>().ToList();

        Assert.Equal(2, projected.Count);
        Assert.Equal("Alpha", projected[0].Name);
        Assert.Equal(10, projected[0].Value);
        Assert.Equal("Beta", projected[1].Name);
        Assert.Equal(20, projected[1].Value);
    }

    [Fact]
    public void ProjectTo_skips_ignored_properties()
    {
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .Ignore(d => d.Extra);

        var data = new List<SimpleSrc>
        {
            new() { Id = Guid.NewGuid(), Name = "Test", Value = 42 }
        }.AsQueryable();

        var projected = data.ProjectTo<SimpleSrc, DestWithExtra>().ToList();

        Assert.Single(projected);
        Assert.Null(projected[0].Extra); // Ignored — remains default
    }

    [Fact]
    public void ProjectTo_flattened_property()
    {
        JAutoMapper.CreateMap<Address, AddressDto>();
        JAutoMapper.CreateMap<FlattenSrc, FlattenDest>();

        var data = new List<FlattenSrc>
        {
            new() { Address = new Address { City = "Krakow", Street = "Main" } }
        }.AsQueryable();

        var projected = data.ProjectTo<FlattenSrc, FlattenDest>().ToList();

        Assert.Single(projected);
        Assert.Equal("Krakow", projected[0].AddressCity);
        Assert.Equal("Main", projected[0].AddressStreet);
    }

    [Fact]
    public void ProjectTo_nested_complex_object()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();
        JAutoMapper.CreateMap<NestedSrc, NestedDest>();

        var data = new List<NestedSrc>
        {
            new() { Inner = new SimpleSrc { Id = Guid.NewGuid(), Name = "Alpha", Value = 10 } }
        }.AsQueryable();

        var projected = data.ProjectTo<NestedSrc, NestedDest>().ToList();

        Assert.Single(projected);
        Assert.NotNull(projected[0].Inner);
        Assert.Equal("Alpha", projected[0].Inner!.Name);
        Assert.Equal(10, projected[0].Inner.Value);
    }

    [Fact]
    public void ProjectTo_with_conversion()
    {
        JAutoMapper.CreateMap<EnumSrc, EnumDest>();

        var data = new List<EnumSrc>
        {
            new() { Status = StatusCode.Error, OptionalStatus = StatusCode.OK, StatusInt = 1 }
        }.AsQueryable();

        var projected = data.ProjectTo<EnumSrc, EnumDest>().ToList();

        Assert.Single(projected);
        Assert.Equal(2, projected[0].Status);
        Assert.Equal("OK", projected[0].OptionalStatus);
        Assert.Equal(StatusCode.OK, projected[0].StatusInt);
    }

    [Fact]
    public void ProjectTo_nested_object_null_source()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();
        JAutoMapper.CreateMap<NestedSrc, NestedDest>();

        var data = new List<NestedSrc>
        {
            new() { Inner = null }
        }.AsQueryable();

        var projected = data.ProjectTo<NestedSrc, NestedDest>().ToList();

        Assert.Single(projected);
        Assert.Null(projected[0].Inner);
    }

    [Fact]
    public void ProjectTo_custom_lambda_resolver()
    {
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .ForMember(d => d.FullName, s => $"{s.Name} - {s.Value}");

        var data = new List<SimpleSrc>
        {
            new() { Id = Guid.NewGuid(), Name = "Alpha", Value = 10 }
        }.AsQueryable();

        var projected = data.ProjectTo<SimpleSrc, DestWithExtra>().ToList();

        Assert.Single(projected);
        Assert.Equal("Alpha - 10", projected[0].FullName);
    }

    [Fact]
    public void ProjectTo_with_NullSubstitute()
    {
        JAutoMapper.CreateMap<NullSubSrc, NullSubDest>()
            .ForMember(d => d.Value, opt => opt.NullSubstitute("fallback"));

        var data = new List<NullSubSrc>
        {
            new() { Value = null },
            new() { Value = "real" }
        }.AsQueryable();

        var projected = data.ProjectTo<NullSubSrc, NullSubDest>().ToList();

        Assert.Equal(2, projected.Count);
        Assert.Equal("fallback", projected[0].Value);
        Assert.Equal("real", projected[1].Value);
    }

    [Fact]
    public void ProjectTo_with_MapFrom_TResolver()
    {
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .ForMember(d => d.FullName, opt => opt.MapFrom<FullNameResolver>());

        var data = new List<SimpleSrc>
        {
            new() { Id = Guid.NewGuid(), Name = "Alice", Value = 30 }
        }.AsQueryable();

        var projected = data.ProjectTo<SimpleSrc, DestWithExtra>().ToList();

        Assert.Single(projected);
        Assert.Equal("Alice - resolved", projected[0].FullName);
    }

    [Fact]
    public void ForMember_with_MapFrom_TResolver_uses_named_class_resolver()
    {
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .ForMember(d => d.FullName, opt => opt.MapFrom<FullNameResolver>());

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Alice", Value = 30 };
        var dest = JAutoMapper.Map<SimpleSrc, DestWithExtra>(src);

        Assert.NotNull(dest);
        Assert.Equal("Alice - resolved", dest.FullName);
    }

    // Simple IServiceProvider backed by a dictionary (for tests only — avoids building a real DI container)
    private class MapServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = new();
        public MapServiceProvider Add<T>(T instance) { _services[typeof(T)] = instance!; return this; }
        public object? GetService(Type serviceType) =>
            _services.TryGetValue(serviceType, out var v) ? v : null;
    }

    [Fact]
    public void ServiceProvider_is_used_when_set()
    {
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .ForMember(d => d.FullName, opt => opt.MapFrom<FullNameResolver>());

        var sp = new MapServiceProvider().Add(new FullNameResolver());
        JAutoMapper.ServiceProvider = sp;

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "DI", Value = 1 };
        var dest = JAutoMapper.Map<SimpleSrc, DestWithExtra>(src)!;

        Assert.Equal("DI - resolved", dest.FullName);

        JAutoMapper.ServiceProvider = null; // cleanup
    }

    [Fact]
    public void MapFrom_TResolver_with_parameterized_constructor_via_ServiceProvider()
    {
        var userService = new FakeUserService();
        var sp = new MapServiceProvider().Add<IUserService>(userService)
                                         .Add(new UserRoleResolver(userService));
        JAutoMapper.ServiceProvider = sp;

        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .ForMember(d => d.FullName, opt => opt.MapFrom<UserRoleResolver>());

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Alice", Value = 30 };
        var dest = JAutoMapper.Map<SimpleSrc, DestWithExtra>(src)!;

        Assert.Equal("Admin", dest.FullName);

        JAutoMapper.ServiceProvider = null; // cleanup
    }

    [Fact]
    public void Resolver_with_parameterized_ctor_and_no_ServiceProvider_throws_helpful_error()
    {
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .ForMember(d => d.FullName, opt => opt.MapFrom<UserRoleResolver>());

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Alice", Value = 30 };

        var ex = Assert.Throws<MappingException>(() =>
            JAutoMapper.Map<SimpleSrc, DestWithExtra>(src));

        Assert.Contains("no parameterless constructor", ex.InnerException?.Message);
        Assert.Contains("ServiceProvider", ex.InnerException?.Message);
    }

    [Fact]
    public void AssertConfigurationIsValid_passes_for_fully_mapped()
    {
        JAutoMapper.CreateMap<SimpleSrc, SimpleDest>();
        JAutoMapper.AssertConfigurationIsValid(); // should not throw
    }

    [Fact]
    public void AssertConfigurationIsValid_throws_for_unmapped_member()
    {
        JAutoMapper.CreateMap<SrcWithName, DestWithNameAndAge>();
        var ex = Assert.Throws<InvalidOperationException>(() => JAutoMapper.AssertConfigurationIsValid());
        Assert.Contains("Age", ex.Message);
        Assert.Contains(nameof(DestWithNameAndAge), ex.Message);
    }

    [Fact]
    public void AssertConfigurationIsValid_passes_when_unmapped_ignored()
    {
        JAutoMapper.CreateMap<SrcWithName, DestWithNameAndAge>()
            .Ignore(d => d.Age);
        JAutoMapper.AssertConfigurationIsValid(); // should not throw
    }

    [Fact]
    public void AssertConfigurationIsValid_passes_for_ConvertUsing()
    {
        JAutoMapper.CreateMap<SrcWithName, DestWithNameAndAge>()
            .ConvertUsing(s => new DestWithNameAndAge { Name = s.Name, Age = 0 });
        JAutoMapper.AssertConfigurationIsValid(); // should not throw — ConvertUsing handles everything
    }

    [Fact]
    public void AssertConfigurationIsValid_passes_for_flattened_property()
    {
        JAutoMapper.CreateMap<FlattenSrc, FlattenDest>();
        JAutoMapper.AssertConfigurationIsValid(); // AddressCity / AddressStreet resolved via flattening
    }

    [Fact]
    public void IncludeBase_inherits_base_custom_resolver()
    {
        JAutoMapper.CreateMap<BaseSrc, BaseDest>()
            .ForMember(d => d.Name, s => s.Name.ToUpper());
        JAutoMapper.CreateMap<DerivedSrc, DerivedDest>()
            .IncludeBase<BaseSrc, BaseDest>();

        var derived = new DerivedSrc { Name = "alice", Age = 30 };
        var dto = JAutoMapper.Map<DerivedSrc, DerivedDest>(derived);

        Assert.Equal("ALICE", dto.Name);
        Assert.Equal(30, dto.Age);
    }

    [Fact]
    public void IncludeBase_inherits_base_ignore()
    {
        JAutoMapper.CreateMap<BaseSrc, BaseDest>()
            .Ignore(d => d.Name);
        JAutoMapper.CreateMap<DerivedSrc2, DerivedDest2>()
            .IncludeBase<BaseSrc, BaseDest>();

        var derived = new DerivedSrc2 { Name = "alice", Age = 30 };
        var dto = JAutoMapper.Map<DerivedSrc2, DerivedDest2>(derived);

        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(30, dto.Age);
    }

    [Fact]
    public void IncludeBase_derived_can_override_base_resolver()
    {
        JAutoMapper.CreateMap<BaseSrc, BaseDest>()
            .ForMember(d => d.Name, s => s.Name.ToUpper());
        JAutoMapper.CreateMap<DerivedSrc, DerivedDest>()
            .IncludeBase<BaseSrc, BaseDest>()
            .ForMember(d => d.Name, s => s.Name.ToLower());

        var derived = new DerivedSrc { Name = "ALICE", Age = 25 };
        var dto = JAutoMapper.Map<DerivedSrc, DerivedDest>(derived);

        Assert.Equal("alice", dto.Name);
        Assert.Equal(25, dto.Age);
    }
}
