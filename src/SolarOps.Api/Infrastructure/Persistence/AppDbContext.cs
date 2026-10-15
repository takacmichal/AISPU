using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Infrastructure.Identity;

namespace SolarOps.Api.Infrastructure.Persistence;

/// <summary>
/// Single EF Core context for the whole API. Feature modules do NOT edit this class:
/// they add <see cref="IEntityTypeConfiguration{TEntity}"/> classes in their own folder
/// and access entities via <c>db.Set&lt;T&gt;()</c>.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
