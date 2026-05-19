using System.Reflection;
using VibeTerm.Terminal;
using VibeTerm.ViewModels;
using Xunit;

namespace VibeTerm.Tests;

public sealed class TerminalViewModelModeTests
{
    public static TheoryData<TerminalStatus, string, string> StatusModes => new()
    {
        { TerminalStatus.Starting, "starting", "#858585" },
        { TerminalStatus.Active, "active", "#6A9955" },
        { TerminalStatus.Running, "running", "#DCDCAA" },
        { TerminalStatus.Exited, "exited", "#858585" },
        { TerminalStatus.Error, "error", "#F44747" },
    };

    [Theory]
    [MemberData(nameof(StatusModes))]
    public void StatusTextAndColorCoverEveryTerminalMode(
        TerminalStatus status,
        string expectedText,
        string expectedColor)
    {
        using var session = CreateSession(status);
        var vm = new TerminalViewModel(session);

        Assert.Equal(expectedText, vm.StatusText);
        Assert.Equal(expectedColor, vm.StatusColor);
    }

    [Fact]
    public void TestDataContainsAllDeclaredTerminalModes()
    {
        var coveredStatuses = StatusModes
            .Select(row => (TerminalStatus)row[0])
            .OrderBy(status => status)
            .ToArray();

        var declaredStatuses = Enum.GetValues<TerminalStatus>()
            .OrderBy(status => status)
            .ToArray();

        Assert.Equal(declaredStatuses, coveredStatuses);
    }

    [Fact]
    public void RenameCommandsSupportEditCommitAndCancelModes()
    {
        using var session = CreateSession(TerminalStatus.Active);
        var vm = new TerminalViewModel(session);

        vm.BeginRenameCommand.Execute(null);

        Assert.True(vm.IsEditing);
        Assert.Equal("PowerShell 1", vm.EditName);

        vm.EditName = "  build shell  ";
        vm.CommitRenameCommand.Execute(null);

        Assert.False(vm.IsEditing);
        Assert.Equal("build shell", vm.DisplayName);

        vm.BeginRenameCommand.Execute(null);
        vm.EditName = "ignored";
        vm.CancelRenameCommand.Execute(null);

        Assert.False(vm.IsEditing);
        Assert.Equal("build shell", vm.DisplayName);
    }

    private static TerminalSession CreateSession(TerminalStatus status)
    {
        var session = new TerminalSession(1, "PowerShell 1", "powershell.exe", null);
        typeof(TerminalSession)
            .GetProperty(nameof(TerminalSession.Status), BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(session, new object[] { status });
        return session;
    }
}
