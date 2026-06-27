namespace JAM.Models;

public class User
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int Age { get; set; }
    public Address? Address { get; set; }
    public List<Address>? Addresses { get; set; }
}
