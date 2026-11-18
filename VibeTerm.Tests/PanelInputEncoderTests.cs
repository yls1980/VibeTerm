using System.Text;
using VibeTerm.Api;
using Xunit;

namespace VibeTerm.Tests;

public sealed class PanelInputEncoderTests
{
    [Theory]
    [InlineData("enter", 13)]
    [InlineData("Ctrl+C", 3)]
    [InlineData("ctrl + d", 4)]
    [InlineData("tab", 9)]
    public void EncodesNamedKeys(string name, byte expected)
    {
        byte[] bytes = PanelInputEncoder.EncodeKeys(name);
        Assert.Equal(expected, bytes[0]);
    }

    [Fact]
    public void SubmitTurnsNewlinesIntoEnter()
    {
        byte[] bytes = PanelInputEncoder.EncodeText("a\r\nb", submit: true);
        Assert.Equal(Encoding.UTF8.GetBytes("a\rb\r"), bytes);
    }

    [Fact]
    public void RawTextDoesNotAddEnter()
    {
        byte[] bytes = PanelInputEncoder.EncodeText("abc", submit: false);
        Assert.Equal(Encoding.UTF8.GetBytes("abc"), bytes);
    }

    [Fact]
    public void UnknownKeyIsRejected()
    {
        PanelApiException ex = Assert.Throws<PanelApiException>(() => PanelInputEncoder.EncodeKeys("f1"));
        Assert.Equal(400, ex.StatusCode);
    }
}
