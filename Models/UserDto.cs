namespace JAM.Models;

public class UserDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int Age { get; set; }
    public string? InternalToken { get; set; }
    public AddressDto? Address { get; set; }
    public List<AddressDto>? Addresses { get; set; }
    public string Initials { get; set; } = string.Empty;
}
