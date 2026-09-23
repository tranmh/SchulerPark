namespace SchulerPark.Tests.Helpers;

using FluentAssertions;
using SchulerPark.Core.Helpers;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Test1234!", true)]
    [InlineData("abcdefgh", false)]       // one class
    [InlineData("Abcdefgh", false)]       // two classes
    [InlineData("Abcdefg1", true)]        // three classes
    [InlineData("Short1!", false)]        // 7 chars
    [InlineData("Password1", false)]      // common
    [InlineData("Schuler2026!", false)]   // site term
    [InlineData("LouisE-rocks-1", false)] // site term
    public void IsAcceptable(string password, bool expected)
    {
        PasswordPolicy.IsAcceptable(password).Should().Be(expected);
    }

    [Fact]
    public void RejectsOverlongPasswords()
    {
        PasswordPolicy.IsAcceptable(new string('a', 100) + "B1!" + new string('c', 30)).Should().BeFalse();
        PasswordPolicy.IsAcceptable(null).Should().BeFalse();
    }
}
