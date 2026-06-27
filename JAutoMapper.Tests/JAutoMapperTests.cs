using JAM;

namespace JAM.Tests;

public class JAutoMapperTests
{
    // ── Named class resolver for MapFrom<TResolver> test ──────────────
public class FullNameResolver : IValueResolver<SimpleSrc, DestWithExtra, object?>
{
    public object? Resolve(SimpleSrc source, DestWithExtra destination, object? destMember, ResolutionContext context)
        => $"{source.Name} - resolved";
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

    // ── Setup / Teardown ────────────────────────────────────────────

    public JAutoMapperTests()
    {
        JAutoMapper.Reset();
    }

    // ── Tests ───────────────────────────────────────────────────────

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
    public void ForMember_with_MapFrom_TResolver_uses_named_class_resolver()
    {
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .ForMember(d => d.FullName, opt => opt.MapFrom<FullNameResolver>());

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "Alice", Value = 30 };
        var dest = JAutoMapper.Map<SimpleSrc, DestWithExtra>(src);

        Assert.NotNull(dest);
        Assert.Equal("Alice - resolved", dest.FullName);
    }

    [Fact]
    public void ResolverFactory_is_used_when_set()
    {
        JAutoMapper.CreateMap<SimpleSrc, DestWithExtra>()
            .ForMember(d => d.FullName, opt => opt.MapFrom<FullNameResolver>());

        var invoked = false;
        JAutoMapper.ResolverFactory = type =>
        {
            invoked = true;
            return new FullNameResolver();
        }!;

        var src = new SimpleSrc { Id = Guid.NewGuid(), Name = "DI", Value = 1 };
        var dest = JAutoMapper.Map<SimpleSrc, DestWithExtra>(src)!;

        Assert.True(invoked, "ResolverFactory should have been called");
        Assert.Equal("DI - resolved", dest.FullName);

        JAutoMapper.ResolverFactory = null; // cleanup
    }
}
