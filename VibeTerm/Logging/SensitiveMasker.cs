using System.Text.RegularExpressions;

namespace VibeTerm.Logging;

/// <summary>
/// Masks obvious sensitive patterns before writing to log files.
/// NOTE: Masking is best-effort and does not guarantee full data protection.
/// </summary>
public sealed partial class SensitiveMasker
{
    private readonly bool _enabled;

    // C# 11 / .NET 7 source-generated regexes
    [GeneratedRegex(
        @"(?i)(API_?KEY|TOKEN|SECRET|PASSWORD|PASSWD|PRIVATE_?KEY|ACCESS_?KEY|AUTH_?TOKEN|CLIENT_?SECRET)\s*[=:]\s*(?<val>\S+)",
        RegexOptions.Multiline)]
    private static partial Regex KeyValuePattern();

    [GeneratedRegex(
        @"(?i)(Authorization\s*:\s*(?:Bearer|Basic|Token)\s+)(?<val>\S+)",
        RegexOptions.Multiline)]
    private static partial Regex AuthHeaderPattern();

    [GeneratedRegex(@"(?<val>sk-[A-Za-z0-9\-_]{20,})", RegexOptions.Multiline)]
    private static partial Regex SkKeyPattern();

    [GeneratedRegex(
        @"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----(?<val>[\s\S]*?)-----END (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
        RegexOptions.Multiline)]
    private static partial Regex PemKeyPattern();

    [GeneratedRegex(
        @"(?i)(?:--password|--passwd|-p)\s+(?<val>\S+)",
        RegexOptions.Multiline)]
    private static partial Regex PasswordFlagPattern();

    // Populated once; static field initializer runs after static methods exist
    private static readonly Regex[] Patterns = new[]
    {
        KeyValuePattern(),
        AuthHeaderPattern(),
        SkKeyPattern(),
        PemKeyPattern(),
        PasswordFlagPattern(),
    };

    public SensitiveMasker(bool enabled) => _enabled = enabled;

    public string Mask(string text)
    {
        if (!_enabled || string.IsNullOrEmpty(text)) return text;

        foreach (var rx in Patterns)
            text = rx.Replace(text, ReplaceMatch);

        return text;
    }

    private static string ReplaceMatch(Match m)
    {
        if (m.Groups["val"].Success)
        {
            // Keep the prefix (key name + separator), replace only the value
            int valStart = m.Groups["val"].Index - m.Index;
            return m.Value[..valStart] + "[REDACTED]";
        }
        return "[REDACTED]";
    }
}
