using JAM.Models;
using JAM;

namespace JAM.Mappings;

public static class MappingProfile
{
    public static void Register()
    {
        JAutoMapper.CreateMap<Address, AddressDto>().ReverseMap();

        JAutoMapper.CreateMap<User, UserDto>()
            .ForMember(d => d.FullName, s => $"{s.FirstName} {s.LastName}")
            .ForMember(d => d.InternalToken, _ => (string?)null)
            .AfterMap((src, dest) => dest.Initials = $"{src.FirstName[0]}{src.LastName[0]}")
            .ReverseMap()
            .ForMember(d => d.FirstName, s => s.FullName.Split(' ')[0])
            .ForMember(d => d.LastName, s => s.FullName.Contains(' ') ? s.FullName.Split(' ')[1] : "");

        // Simple flat mapping demo
        JAutoMapper.CreateMap<TestUser, TestUserViewModel>()
            .ForMember(d => d.FullName, s => $"{s.FirstName} {s.LastName}");
    }
}
