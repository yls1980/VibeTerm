using VibeTerm.Logging;
using Xunit;

namespace VibeTerm.Tests;

public sealed class SensitiveMaskerTests
{
    [Theory]
    [InlineData("API_KEY=abc123", "API_KEY=[REDACTED]", "abc123")]
    [InlineData("Authorization: Bearer token-value", "Authorization: Bearer [REDACTED]", "token-value")]
    [InlineData("openai sk-abcdefghijklmnopqrstuvwxyz", "openai [REDACTED]", "sk-abcdefghijklmnopqrstuvwxyz")]
    [InlineData("tool --password super-secret", "tool --password [REDACTED]", "super-secret")]
    public void EnabledModeMasksSensitiveValues(
        string input,
        string expected,
        string hiddenValue)
    {
        var masker = new SensitiveMasker(enabled: true);

        string masked = masker.Mask(input);

        Assert.Equal(expected, masked);
        Assert.DoesNotContain(hiddenValue, masked);
    }

    [Fact]
    public void EnabledModeMasksPrivateKeyBlocks()
    {
        var masker = new SensitiveMasker(enabled: true);
        const string input = """
            -----BEGIN PRIVATE KEY-----
            private-key-body
            -----END PRIVATE KEY-----
            """;

        string masked = masker.Mask(input);

        Assert.Contains("[REDACTED]", masked);
        Assert.Contains("-----BEGIN PRIVATE KEY-----", masked);
        Assert.DoesNotContain("private-key-body", masked);
    }

    [Fact]
    public void DisabledModeLeavesInputUnchanged()
    {
        var masker = new SensitiveMasker(enabled: false);
        const string input = "PASSWORD=plain-text";

        Assert.Equal(input, masker.Mask(input));
    }
}
