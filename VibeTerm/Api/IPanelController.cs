using System.Collections.Generic;
using System.Threading.Tasks;

namespace VibeTerm.Api;

/// <summary>
/// Operations the local panel API can perform. The HTTP server calls this
/// interface, and the WPF window supplies the real implementation.
/// </summary>
public interface IPanelController
{
    IReadOnlyList<PanelSnapshot> List();

    PanelSnapshot Get(int id);

    PanelSnapshot Create(string? name, string? workingDirectory);

    Task CloseAsync(int id);

    PanelSnapshot Select(int id);

    PanelSnapshot Rename(int id, string name);

    Task SendTextAsync(int id, string text, bool submit);

    Task SendKeysAsync(int id, string keys);

    TextChunk ReadOutput(int id, long since, int maxChars);
}
