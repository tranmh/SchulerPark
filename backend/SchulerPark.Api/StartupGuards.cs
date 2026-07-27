namespace SchulerPark.Api;

/// <summary>
/// Fail-fast startup validations for secrets/config that must never take an insecure
/// default outside local development.
/// </summary>
public static class StartupGuards
{
    /// <summary>
    /// Bug #16: the DB connection string must not be missing or carry the committed
    /// default 'changeme' password when running in a real (prod-like) environment.
    /// </summary>
    public static bool IsUnsafeDbConnectionString(string? connectionString) =>
        string.IsNullOrWhiteSpace(connectionString)
        || connectionString.Contains("changeme", StringComparison.OrdinalIgnoreCase);
}
