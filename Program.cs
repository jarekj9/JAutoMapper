using JAM;
using JAM.Mappings;
using JAM.Models;

// Register all maps at startup
MappingProfile.Register();

Console.WriteLine("=== JAutoMapper Demo ===\n");

// ── Simple flat mapping ──────────────────────────────────────────
Console.WriteLine("--- Simple flat mapping ---");
var testUser = new TestUser
{
    Id = Guid.NewGuid(),
    FirstName = "John",
    LastName = "Doe",
    Age = 30
};

var testVm = JAutoMapper.Map<TestUser, TestUserViewModel>(testUser)!;
Console.WriteLine($"Id: {testVm.Id}");
Console.WriteLine($"FullName: {testVm.FullName}");
Console.WriteLine($"Age: {testVm.Age}");

// ── Map with ForMember, Ignore, AfterMap ──────────────────────────
Console.WriteLine("\n--- Full mapping with customizations ---");
var user = new User
{
    Id = Guid.NewGuid(),
    FirstName = "Jane",
    LastName = "Smith",
    Age = 28,
    Address = new Address { Street = "123 Main St", City = "Seattle", ZipCode = "98101" },
    Addresses =
    [
        new Address { Street = "456 Oak Ave", City = "Portland", ZipCode = "97201" },
        new Address { Street = "789 Pine Rd", City = "SF", ZipCode = "94101" }
    ]
};

var userDto = JAutoMapper.Map<User, UserDto>(user)!;
Console.WriteLine($"Id: {userDto.Id}");
Console.WriteLine($"FullName: {userDto.FullName}");
Console.WriteLine($"Age: {userDto.Age}");
Console.WriteLine($"InternalToken: {(userDto.InternalToken == null ? "null (ignored)" : userDto.InternalToken)}");
Console.WriteLine($"Initials: {userDto.Initials}");
Console.WriteLine($"Address: {userDto.Address?.Street}, {userDto.Address?.City}");
Console.WriteLine($"Addresses count: {userDto.Addresses?.Count}");
foreach (var a in userDto.Addresses ?? [])
    Console.WriteLine($"  - {a.Street}, {a.City}");

// ── ReverseMap ────────────────────────────────────────────────────
Console.WriteLine("\n--- ReverseMap ---");
var mappedBack = JAutoMapper.Map<UserDto, User>(userDto)!;
Console.WriteLine($"Id: {mappedBack.Id}");
Console.WriteLine($"FirstName: {mappedBack.FirstName}");
Console.WriteLine($"LastName: {mappedBack.LastName}");
Console.WriteLine($"Age: {mappedBack.Age}");

// ── Map into existing object ──────────────────────────────────────
Console.WriteLine("\n--- MapInto (merge) ---");
var existingUser = new User { Id = user.Id, FirstName = "Old", LastName = "Name", Age = 0 };
JAutoMapper.MapInto(userDto, existingUser);
Console.WriteLine($"Merged FirstName: {existingUser.FirstName}");
Console.WriteLine($"Merged LastName: {existingUser.LastName}");
Console.WriteLine($"Merged Age: {existingUser.Age}");

// ── Null source ──────────────────────────────────────────────────
Console.WriteLine("\n--- Null source returns default ---");
User? nullUser = null;
var nullResult = JAutoMapper.Map<User, UserDto>(nullUser);
Console.WriteLine($"Null map result: {(nullResult is null ? "null" : "not null")}");

// ── Missing map throws ──────────────────────────────────────────
Console.WriteLine("\n--- Missing map throws ---");
try
{
    JAutoMapper.Map<Address, TestUser>(new Address());
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Expected error: {ex.Message}");
}

// ── Convenience overload ──────────────────────────────────────────
Console.WriteLine("\n--- Convenience Map<TDest>(object) ---");
object boxed = testUser;
var viaObject = JAutoMapper.Map<TestUserViewModel>(boxed);
Console.WriteLine($"Via object overload: {viaObject?.FullName}");

// ── MapList ───────────────────────────────────────────────────────
Console.WriteLine("\n--- MapList ---");
var users = new List<User> { user, new User { Id = Guid.NewGuid(), FirstName = "Bob", LastName = "Brown", Age = 35 } };
var dtos = JAutoMapper.MapList<User, UserDto>(users);
Console.WriteLine($"Mapped {dtos.Count} users");
foreach (var d in dtos)
    Console.WriteLine($"  - {d.FullName}");

Console.WriteLine("\n✅ Demo completed successfully!");
