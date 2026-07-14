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
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = "Super Administrator",
            Role = UserRole.SuperAdmin,
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, password);

        // The container runs non-root and /app is root-owned; the Dockerfile
        // provides a writable /bootstrap via BOOTSTRAP_ADMIN_DIR.
        var dir = Environment.GetEnvironmentVariable("BOOTSTRAP_ADMIN_DIR")
                  ?? AppContext.BaseDirectory;
        var path = Path.Combine(dir, "admin.yml");
        var yaml =
            "# Auto-generated SuperAdmin credentials — delete this file after first login\n" +
            $"email: {user.Email}\n" +
            $"password: {password}\n";

        // Write the credential file BEFORE creating the user, owner-read-only (0600).
        // The password is never logged: if the file cannot be written, the account is
        // not created and bootstrap simply retries on the next startup.
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            await using (var stream = new FileStream(path, options))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(yaml);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bootstrap] Could not write {path}: {ex.Message}. " +
                              "SuperAdmin NOT created; bootstrap will retry on next startup.");
            return;
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();
        Console.WriteLine($"[Bootstrap] SuperAdmin created. Credentials written to {path}");
    }
}
