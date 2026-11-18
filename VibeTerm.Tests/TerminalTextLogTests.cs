using System.Reflection;
using System.Text;
using VibeTerm.Api;
using VibeTerm.Terminal;
using Xunit;

namespace VibeTerm.Tests;

public sealed class TerminalTextLogTests
{
    [Fact]
    public void KeepsPlainLinesAndStripsColorCodes()
    {
        var log = new TerminalTextLog();
        log.Append(Encoding.UTF8.GetBytes("hello\r\n"));
        log.Append(Encoding.UTF8.GetBytes("\u001b[31mred\u001b[0m\r\n"));

        TextChunk chunk = log.Read(0, 100);
        Assert.Equal("hello\nred\n", chunk.Text);
        Assert.False(chunk.Truncated);
    }

    [Fact]
    public void CarriageReturnAndEraseRewriteTheCurrentLine()
    {
        var log = new TerminalTextLog();
        log.Append(Encoding.UTF8.GetBytes("hello\rbye\u001b[K"));

        Assert.Equal("bye", log.Read(0, 100).Text);
    }

    [Fact]
    public void BackspaceRemovesThePreviousCharacterOnTheCurrentLine()
    {
        var log = new TerminalTextLog();
        log.Append(Encoding.UTF8.GetBytes("ab\b \b"));

        Assert.Equal("a", log.Read(0, 100).Text.TrimEnd());
    }

    [Fact]
    public void DropsTerminalTitleSequences()
    {
        var log = new TerminalTextLog();
        log.Append(Encoding.UTF8.GetBytes("\u001b]0;title\u0007next"));

        Assert.Equal("next", log.Read(0, 100).Text);
    }

    [Fact]
    public void ReadsFromAnOffsetAndKeepsASplitUtf8Character()
    {
        var log = new TerminalTextLog();
        log.Append(Encoding.UTF8.GetBytes("one\n"));
        byte[] letter = Encoding.UTF8.GetBytes("Я");
        log.Append(new[] { letter[0] });
        log.Append(new[] { letter[1] });

        TextChunk first = log.Read(0, 4);
        Assert.Equal("one\n", first.Text);
        TextChunk second = log.Read(first.NextOffset, 10);
        Assert.Equal("Я", second.Text);
    }

    [Fact]
    public void DropsOldTextWhenTheLogGrowsPastItsLimit()
    {
        var log = new TerminalTextLog();
        byte[] line = Encoding.UTF8.GetBytes(new string('a', 100) + "\n");
        for (int i = 0; i < 3000; i++)
            log.Append(line);

        TextChunk chunk = log.Read(0, 20);
        Assert.True(chunk.Truncated);
        Assert.True(chunk.Since > 0);
        Assert.Equal(20, chunk.Text.Length);
    }

    [Fact]
    public void SessionStoresPumpedOutputAsReadableText()
    {
        var session = new TerminalSession(7, "PowerShell 7", "pwsh.exe", null);
        try
        {
            MethodInfo? append = typeof(TerminalSession).GetMethod(
                "AppendToBuffer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(append);
            append!.Invoke(session, new object[] { Encoding.UTF8.GetBytes("ready\r\n") });

            Assert.Equal("ready\n", session.ReadText(0, 100).Text);
            Assert.Equal(session.TextOffset, session.ReadText(0, 100).NextOffset);
        }
        finally
        {
            session.Dispose();
        }
    }
}
