using System;
using System.Collections.Generic;
using System.Text;

namespace VibeTerm.Api;

/// <summary>
/// Turns API text and key names into bytes that ConPTY understands.
/// </summary>
public static class PanelInputEncoder
{
    private static readonly Dictionary<string, byte[]> Keys = new(StringComparer.Ordinal)
    {
        ["enter"] = new byte[] { 13 },
        ["return"] = new byte[] { 13 },
        ["tab"] = new byte[] { 9 },
        ["escape"] = new byte[] { 0x1B },
        ["esc"] = new byte[] { 0x1B },
        ["backspace"] = new byte[] { 0x7F },
        ["delete"] = new byte[] { 0x1B, (byte)'[', (byte)'3', (byte)'~' },
        ["up"] = new byte[] { 0x1B, (byte)'[', (byte)'A' },
        ["down"] = new byte[] { 0x1B, (byte)'[', (byte)'B' },
        ["right"] = new byte[] { 0x1B, (byte)'[', (byte)'C' },
        ["left"] = new byte[] { 0x1B, (byte)'[', (byte)'D' },
        ["home"] = new byte[] { 0x1B, (byte)'[', (byte)'H' },
        ["end"] = new byte[] { 0x1B, (byte)'[', (byte)'F' },
        ["pageup"] = new byte[] { 0x1B, (byte)'[', (byte)'5', (byte)'~' },
        ["pagedown"] = new byte[] { 0x1B, (byte)'[', (byte)'6', (byte)'~' },
        ["ctrl+c"] = new byte[] { 3 },
        ["ctrl+d"] = new byte[] { 4 },
        ["ctrl+z"] = new byte[] { 26 },
        ["ctrl+l"] = new byte[] { 12 },
        ["space"] = new byte[] { 32 },
    };

    public static byte[] EncodeText(string text, bool submit)
    {
        if (!submit)
            return Encoding.UTF8.GetBytes(text);

        string normalized = text.Replace("\r\n", "\n").Replace('\n', '\r');
        if (normalized.Length == 0 || normalized[^1] != '\r')
            normalized += "\r";

        return Encoding.UTF8.GetBytes(normalized);
    }

    public static byte[] EncodeKeys(string keys)
    {
        if (string.IsNullOrWhiteSpace(keys))
            throw new PanelApiException(400, "keys is required");

        string name = keys.Trim().ToLowerInvariant().Replace(" ", "");
        if (!Keys.TryGetValue(name, out byte[]? bytes))
            throw new PanelApiException(400, "unknown key");

        return bytes;
    }
}
