using SolarOps.Api.Infrastructure.Querying;

namespace SolarOps.Api.Tests.Unit.Querying;

public sealed class SqlLikeTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("50%", "50\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("c:\\x", "c:\\\\x")]
    [InlineData("%_\\", "\\%\\_\\\\")]
    public void Escape_EscapesWildcardsAndEscapeCharacter(string input, string expected) =>
        Assert.Equal(expected, SqlLike.Escape(input));

    [Fact]
    public void Contains_WrapsTheTrimmedEscapedTerm() => Assert.Equal("%a\\%b%", SqlLike.Contains("  a%b "));
}
