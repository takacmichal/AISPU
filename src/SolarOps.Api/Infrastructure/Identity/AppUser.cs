using Microsoft.AspNetCore.Identity;

namespace SolarOps.Api.Infrastructure.Identity;

/// <summary>
/// Application user (ASP.NET Core Identity, Guid keys). Auth itself is implemented in #132;
/// the type lives in the foundation so AppDbContext never has to change its base class.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
    }
}
