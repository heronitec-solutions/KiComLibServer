using KiComLibBackend.Services;

namespace KiComLibBackend.Tests;

public class SecurityPolicyTests
{
    [Fact]
    public void SvgSanitizer_DropsActiveContentAndKeepsPaths()
    {
        string raw = """
            <svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)">
              <path d="M0 0"/>
              <foreignObject><body xmlns="http://www.w3.org/1999/xhtml"><img src="x" onerror="alert(1)"/></body></foreignObject>
              <script>alert(1)</script>
            </svg>
            """;

        string clean = SvgSanitizer.Sanitize(raw);
        Assert.Contains("<path", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("foreignObject", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlobMedia_RejectsHtmlAndAcceptsKiCadTypes()
    {
        Assert.False(BlobMedia.TryAccept("text/html", out _, out string? error));
        Assert.Equal("This media type cannot be stored", error);
        Assert.True(BlobMedia.TryAccept("text/x-kicad-sexpr", out string sexpr, out _));
        Assert.Equal("text/x-kicad-sexpr", sexpr);
        Assert.True(BlobMedia.TryAccept("image/svg+xml; charset=utf-8", out string svg, out _));
        Assert.Equal("image/svg+xml", svg);
    }

    [Fact]
    public void PasswordPolicy_RecognizesTheInstalledDefault()
    {
        Assert.True(PasswordPolicy.IsDefaultHash(PasswordPolicy.DefaultHash));
        Assert.True(PasswordPolicy.MustChange("local", PasswordPolicy.DefaultHash));
        Assert.False(PasswordPolicy.MustChange("directory", PasswordPolicy.DefaultHash));
        Assert.NotNull(PasswordPolicy.ValidateNew("short"));
        Assert.Equal(PasswordPolicy.DefaultPasswordError, PasswordPolicy.ValidateNew(PasswordPolicy.DefaultPassword));
        Assert.Equal(PasswordPolicy.DefaultPasswordError, PasswordPolicy.ValidateNew("  kicomlib  "));
        Assert.Null(PasswordPolicy.ValidateNew("a-longer-secret"));
    }

    [Fact]
    public void OutboundUrl_BlocksPrivateAddresses()
    {
        Assert.True(OutboundUrl.IsBlockedAddress(System.Net.IPAddress.Parse("10.1.2.3")));
        Assert.True(OutboundUrl.IsBlockedAddress(System.Net.IPAddress.Parse("192.168.1.1")));
        Assert.True(OutboundUrl.IsBlockedAddress(System.Net.IPAddress.Loopback));
        Assert.False(OutboundUrl.IsBlockedAddress(System.Net.IPAddress.Parse("93.184.216.34")));
        Assert.Equal(OutboundUrl.PrivateAddressError, OutboundUrl.RejectResolved("cdn.example", new[] { System.Net.IPAddress.Parse("10.1.2.3") }));
        Assert.Equal(OutboundUrl.PrivateAddressError, OutboundUrl.RejectResolved("127.0.0.1", new[] { System.Net.IPAddress.Loopback }));
        Assert.Null(OutboundUrl.RejectResolved("cdn.example", new[] { System.Net.IPAddress.Parse("93.184.216.34") }));
    }
}
