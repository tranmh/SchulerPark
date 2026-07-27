namespace SchulerPark.Tests;

using SchulerPark.Api;
using Xunit;

// Bug #16: the startup guard must reject a missing connection string or one carrying
// the committed 'changeme' default password.
public class StartupGuardsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Host=db;Port=5432;Database=louise;Username=louise;Password=changeme")]
    [InlineData("Host=db;Username=louise;Password=CHANGEME")]
    public void Rejects_missing_or_default_password(string? connectionString)
    {
        Assert.True(StartupGuards.IsUnsafeDbConnectionString(connectionString));
    }

    [Fact]
    public void Accepts_a_real_connection_string()
    {
        Assert.False(StartupGuards.IsUnsafeDbConnectionString(
            "Host=db;Port=5432;Database=louise;Username=louise;Password=S3cure-Pw_9x"));
    }
}
