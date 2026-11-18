namespace VibeTerm.Api;

/// <summary>
/// A slice of plain terminal text. <see cref="Since"/> is the offset of the
/// first returned character. <see cref="NextOffset"/> is where the next read
/// should start.
/// </summary>
public sealed record TextChunk(long Since, long NextOffset, string Text, bool Truncated);
