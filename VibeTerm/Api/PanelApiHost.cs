using System;
using System.Diagnostics;
using System.Windows.Threading;
using VibeTerm.ViewModels;

namespace VibeTerm.Api;

/// <summary>
/// Starts the loopback API for the open window and removes the address file on exit.
/// </summary>
public static class PanelApiHost
{
    public static IDisposable Start(MainViewModel viewModel, Dispatcher dispatcher)
    {
        if (!viewModel.Settings.EnablePanelApi)
            return EmptyDisposable.Instance;

        try
        {
            var server = new PanelApiServer(new WpfPanelController(viewModel, dispatcher));
            server.Start();
            string path = PanelApiEndpointFile.ResolvePath();
            PanelApiEndpointFile.Write(path, server.BaseUrl ?? string.Empty, server.Token);
            return new RunningServer(server, path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Panel API did not start: " + ex.Message);
            return EmptyDisposable.Instance;
        }
    }

    private sealed class RunningServer : IDisposable
    {
        private readonly PanelApiServer _server;
        private readonly string _path;
        private bool _disposed;

        public RunningServer(PanelApiServer server, string path)
        {
            _server = server;
            _path = path;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _server.Dispose();
            PanelApiEndpointFile.DeleteIfOwned(_path, Environment.ProcessId);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
