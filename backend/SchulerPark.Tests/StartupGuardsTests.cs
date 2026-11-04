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
    [InlineData("\t\r\n")]
    [InlineData("Host=db;Port=5432;Database=louise;Username=louise;Password=changeme")]
    [InlineData("Host=db;Username=louise;Password=CHANGEME")]
    [InlineData("Host=db;Username=louise;Password=ChangeMe")]
    [InlineData("Host=db;Username=louise;Password=changeme123")]
    [InlineData("Host=db;Username=louise;Password=x-changeme-x")]
    [InlineData("changeme")]
    public void IsUnsafeDbConnectionString_MissingOrDefaultPassword_ReturnsTrue(string? connectionString)
    {
        Assert.True(StartupGuards.IsUnsafeDbConnectionString(connectionString));
    }

    // B43: 'changeme' anywhere in the string counts, not only in the password (an admin may
    // have moved the placeholder into another key by mistake).
    [Theory]
    [InlineData("Host=changeme;Username=louise;Password=S3cure-Pw_9x")]
    [InlineData("Host=db;Username=changeme;Password=S3cure-Pw_9x")]
    [InlineData("Host=db;Database=Changeme;Username=louise;Password=S3cure-Pw_9x")]
    public void IsUnsafeDbConnectionString_PlaceholderOutsidePassword_ReturnsTrue(string connectionString)
    {
        Assert.True(StartupGuards.IsUnsafeDbConnectionString(connectionString));
    }

    [Theory]
    [InlineData("Host=db;Port=5432;Database=louise;Username=louise;Password=S3cure-Pw_9x")]
    [InlineData("Host=db;Database=louise;Username=louise")]
    [InlineData("Host=db;Username=louise;Password=change_me")]
    [InlineData("Host=db;Username=louise;Password=change-me")]
    [InlineData("Host=db;Username=louise;Password=changem")]
    [InlineData("Host=db;Username=louise;Password=CHANGE ME")]
    [InlineData("Host=db;Username=louise;Password=hangeme")]
    [InlineData("x")]
    public void IsUnsafeDbConnectionString_NoPlaceholder_ReturnsFalse(string connectionString)
    {
        Assert.False(StartupGuards.IsUnsafeDbConnectionString(connectionString));
    }
}
