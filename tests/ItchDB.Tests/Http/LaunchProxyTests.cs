using ItchDB.Infrastructure.Http;

namespace ItchDB.Tests.Http;

public class LaunchProxyTests
{
    [Fact]
    public void TryReadArgument_WithoutFlagLeavesTheConnectionDirect()
    {
        var ok = LaunchProxy.TryReadArgument(["--urls", "http://localhost:5000"], out var address, out var error);

        Assert.True(ok);
        Assert.Equal("", address);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("--proxy", "http://127.0.0.1:10809", "http://127.0.0.1:10809")]
    [InlineData("--proxy=socks5://127.0.0.1:10808", null, "socks5://127.0.0.1:10808")]
    [InlineData("--proxy", "127.0.0.1:10809", "http://127.0.0.1:10809")]
    public void TryReadArgument_ReadsTheLaunchAddress(string flag, string? next, string expected)
    {
        var args = next is null ? new[] { flag } : new[] { flag, next };

        var ok = LaunchProxy.TryReadArgument(args, out var address, out var error);

        Assert.True(ok);
        Assert.Equal(expected, address);
        Assert.Null(error);
    }

    [Fact]
    public void TryReadArgument_RejectsAFlagWithoutAnAddress()
    {
        var ok = LaunchProxy.TryReadArgument(["--proxy"], out var address, out var error);

        Assert.False(ok);
        Assert.Equal("", address);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
