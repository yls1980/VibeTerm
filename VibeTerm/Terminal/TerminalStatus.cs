namespace VibeTerm.Terminal;

public enum TerminalStatus
{
    Starting,
    Active,
    Running,   // process is busy (long-running command)
    Exited,
    Error
}
