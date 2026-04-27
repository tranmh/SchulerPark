namespace SchulerPark.Infrastructure.Data.Seed;

using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;

public static class BootstrapAdmin
{
    public static async Task BootstrapAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        if (await db.Users.AnyAsync())
            return;

        var email = Environment.GetEnvironmentVariable("BOOTSTRAP_SUPERADMIN_EMAIL")
                    ?? "superadmin@schulerpark.local";

        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12))
            .Replace('/', '_')
            .Replace('+', '-')
            .TrimEnd('=');

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Super Administrator",
            Role = UserRole.SuperAdmin,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, password);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var path = Path.Combine(AppContext.BaseDirectory, "admin.yml");
        var yaml =
            "# Auto-generated SuperAdmin credentials — delete this file after first login\n" +
            $"email: {email}\n" +
            $"password: {password}\n";

        try
        {
            await File.WriteAllTextAsync(path, yaml);
            Console.WriteLine($"[Bootstrap] SuperAdmin created. Credentials written to {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bootstrap] SuperAdmin created. Could not write {path}: {ex.Message}");
            Console.WriteLine($"[Bootstrap] Email: {email}");
            Console.WriteLine($"[Bootstrap] Password: {password}");
        }
    }
}
