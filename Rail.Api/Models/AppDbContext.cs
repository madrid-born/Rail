using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Rail.Models;

public partial class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        builder.Entity<ApplicationUser>().ToTable("AspNetUsers", "Identity");
        builder.Entity<IdentityRole>().ToTable("AspNetRoles", "Identity");
        builder.Entity<IdentityUserClaim<string>>().ToTable("AspNetUserClaims", "Identity");
        builder.Entity<IdentityUserLogin<string>>().ToTable("AspNetUserLogins", "Identity");
        builder.Entity<IdentityUserToken<string>>().ToTable("AspNetUserTokens", "Identity");
        builder.Entity<IdentityUserRole<string>>().ToTable("AspNetUserRoles", "Identity");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("AspNetRoleClaims", "Identity");
    }
}