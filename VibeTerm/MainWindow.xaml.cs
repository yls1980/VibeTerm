using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VibeTerm.Settings;
using VibeTerm.ViewModels;
using VibeTerm.Views;

namespace VibeTerm;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Dictionary<int, TerminalView> _terminalViews = new();
    private readonly Queue<byte[]?> _pendingSnapshots = new();
    private const string TileSplitterTag = "TerminalTileSplitter";
    private double _currentFontSize;
    private string? _lastHotkeyCommand;
    private DateTime _lastHotkeyAt = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        RestoreWindowBounds();

        _currentFontSize = App.Settings.CurrentFontSize > 0
            ? App.Settings.CurrentFontSize
            : App.Settings.FontSize;
        FontSizeLabel.Text = ((int)_currentFontSize).ToString();
        EnvironmentExpander.IsExpanded = App.Settings.EnvironmentPanelIsExpanded;

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        _vm.TerminalAdded   += OnTerminalAdded;
        _vm.TerminalRemoved += OnTerminalRemoved;
        _vm.OpenSettingsRequested += OnOpenSettings;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTerminal))
                ActivateView(_vm.SelectedTerminal);
        };

        var snapshots = App.Settings.TerminalSnapshots;
        if (snapshots.Count > 0)
        {
            int count = Math.Clamp(snapshots.Count, 1, 16);
            for (int i = 0; i < count; i++)
            {
                _pendingSnapshots.Enqueue(LoadSnapshotData(i));
                _vm.AddTerminal(snapshots[i].DisplayName);
            }
            int selIdx = Math.Clamp(App.Settings.SelectedTerminalIndex, 0, _vm.Terminals.Count - 1);
            if (_vm.Terminals.Count > 0)
                _vm.SelectedTerminal = _vm.Terminals[selIdx];
        }
        else
        {
            int terminalCount = Math.Clamp(App.Settings.OpenTerminalCount, 1, 16);
            for (int i = 0; i < terminalCount; i++)
                _vm.AddTerminal();
        }
    }

    // ── terminal view lifecycle ──────────────────────────────────────────────

    private void OnTerminalAdded(object? sender, TerminalViewModel vm)
    {
        var view = new TerminalView();
        view.SetInitialFontSize(_currentFontSize);
        if (_pendingSnapshots.TryDequeue(out var snapshot) && snapshot != null)
            view.SetSnapshot(snapshot);
        view.Attach(vm);
        view.HotkeyRequested += OnTerminalHotkeyRequested;
        view.Visibility = Visibility.Visible;
        view.Margin = new Thickness(1);

        _terminalViews[vm.Id] = view;
        TerminalArea.Children.Add(view);

        ArrangeTerminalTiles();
        UpdateNoTerminalOverlay();
    }

    private void OnTerminalRemoved(object? sender, TerminalViewModel vm)
    {
        if (!_terminalViews.TryGetValue(vm.Id, out var view)) return;
        view.HotkeyRequested -= OnTerminalHotkeyRequested;
        TerminalArea.Children.Remove(view);
        _terminalViews.Remove(vm.Id);
        ArrangeTerminalTiles();
        UpdateNoTerminalOverlay();
    }

    private void OnTerminalHotkeyRequested(object? sender, string command)
    {
        var now = DateTime.UtcNow;
        if (_lastHotkeyCommand == command && now - _lastHotkeyAt < TimeSpan.FromMilliseconds(500))
            return;

        _lastHotkeyCommand = command;
        _lastHotkeyAt = now;

        Dispatcher.InvokeAsync(() => _ = HandleTerminalHotkeyAsync(command));
    }

    private async System.Threading.Tasks.Task HandleTerminalHotkeyAsync(string command)
    {
        switch (command)
        {
            case "newTerminal":
                _vm.AddTerminal();
                break;
            case "closeTerminal":
                if (_vm.SelectedTerminal != null)
                    await _vm.CloseTerminalAsync(_vm.SelectedTerminal);
                break;
            case "nextTerminal":
                _vm.SelectNext();
                break;
            case "prevTerminal":
                _vm.SelectPrev();
                break;
            case "increaseFont":
                SetFontSize(_currentFontSize + 1);
                break;
            case "decreaseFont":
                SetFontSize(_currentFontSize - 1);
                break;
            case "resetFont":
                SetFontSize(App.Settings.FontSize);
                break;
        }
    }

    private void ActivateView(TerminalViewModel? vm)
    {
        if (vm != null && _terminalViews.TryGetValue(vm.Id, out var target))
            target.FocusTerminal();

        // Keep ListBox selection in sync (avoid feedback loop with SelectionChanged)
        if (TerminalList.SelectedItem != vm)
            TerminalList.SelectedItem = vm;

        UpdateNoTerminalOverlay();
    }

    private void ArrangeTerminalTiles()
    {
        RemoveTileSplitters();
        TerminalArea.RowDefinitions.Clear();
        TerminalArea.ColumnDefinitions.Clear();

        int count = _vm.Terminals.Count;
        if (count == 0)
            return;

        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (int)Math.Ceiling(count / (double)columns);

        for (int i = 0; i < rows; i++)
        {
            TerminalArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            if (i < rows - 1)
                TerminalArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        }

        for (int i = 0; i < columns; i++)
        {
            TerminalArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (i < columns - 1)
                TerminalArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        }

        RestoreTileLayout(count, rows, columns);

        var occupants = new TerminalView?[rows, columns];

        for (int i = 0; i < _vm.Terminals.Count; i++)
        {
            var vm = _vm.Terminals[i];
            if (!_terminalViews.TryGetValue(vm.Id, out var view))
                continue;

            int row = i / columns;
            int column = i % columns;
            int remaining = count - i;
            bool singleTileInLastRow = row == rows - 1 && remaining == 1;
            int columnSpan = singleTileInLastRow ? columns - column : 1;

            Grid.SetRow(view, row * 2);
            Grid.SetColumn(view, column * 2);
            Grid.SetRowSpan(view, 1);
            Grid.SetColumnSpan(view, columnSpan * 2 - 1);
            view.Visibility = Visibility.Visible;

            for (int c = column; c < column + columnSpan && c < columns; c++)
                occupants[row, c] = view;
        }

        AddTileSplitters(occupants, rows, columns);
    }

    private void RemoveTileSplitters()
    {
        for (int i = TerminalArea.Children.Count - 1; i >= 0; i--)
        {
            if (TerminalArea.Children[i] is FrameworkElement fe &&
                Equals(fe.Tag, TileSplitterTag))
            {
                TerminalArea.Children.RemoveAt(i);
            }
        }
    }

    private void AddTileSplitters(TerminalView?[,] occupants, int rows, int columns)
    {
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns - 1; column++)
            {
                var left = occupants[row, column];
                var right = occupants[row, column + 1];
                if (left == null || right == null || ReferenceEquals(left, right))
                    continue;

                var splitter = CreateVerticalSplitter();
                Grid.SetRow(splitter, row * 2);
                Grid.SetColumn(splitter, column * 2 + 1);
                TerminalArea.Children.Add(splitter);
            }
        }

        for (int row = 0; row < rows - 1; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                var top = occupants[row, column];
                var bottom = occupants[row + 1, column];
                if (top == null || bottom == null || ReferenceEquals(top, bottom))
                    continue;

                var splitter = CreateHorizontalSplitter();
                Grid.SetRow(splitter, row * 2 + 1);
                Grid.SetColumn(splitter, column * 2);
                TerminalArea.Children.Add(splitter);
            }
        }
    }

    private GridSplitter CreateVerticalSplitter()
    {
        var splitter = CreateBaseVerticalSplitter();
        splitter.DragCompleted += (_, _) => SaveCurrentTileLayout();
        return splitter;
    }

    private GridSplitter CreateHorizontalSplitter()
    {
        var splitter = CreateBaseHorizontalSplitter();
        splitter.DragCompleted += (_, _) => SaveCurrentTileLayout();
        return splitter;
    }

    private static GridSplitter CreateBaseVerticalSplitter() => new()
    {
        Tag = TileSplitterTag,
        Width = 6,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        ResizeDirection = GridResizeDirection.Columns,
        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        Background = new SolidColorBrush(Color.FromArgb(96, 86, 156, 214)),
        ShowsPreview = true
    };

    private static GridSplitter CreateBaseHorizontalSplitter() => new()
    {
        Tag = TileSplitterTag,
        Height = 6,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        ResizeDirection = GridResizeDirection.Rows,
        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        Background = new SolidColorBrush(Color.FromArgb(96, 86, 156, 214)),
        ShowsPreview = true
    };

    private void RestoreTileLayout(int count, int rows, int columns)
    {
        string key = count.ToString();
        if (App.Settings.TerminalTileLayouts == null ||
            !App.Settings.TerminalTileLayouts.TryGetValue(key, out var layout))
            return;

        if (layout.ColumnWeights.Count == columns)
        {
            for (int i = 0; i < columns; i++)
                TerminalArea.ColumnDefinitions[i * 2].Width =
                    new GridLength(Math.Max(1, layout.ColumnWeights[i]), GridUnitType.Star);
        }

        if (layout.RowWeights.Count == rows)
        {
            for (int i = 0; i < rows; i++)
                TerminalArea.RowDefinitions[i * 2].Height =
                    new GridLength(Math.Max(1, layout.RowWeights[i]), GridUnitType.Star);
        }
    }

    private void SaveCurrentTileLayout()
    {
        int count = _vm.Terminals.Count;
        if (count == 0)
            return;

        var columnWeights = TerminalArea.ColumnDefinitions
            .Where((_, index) => index % 2 == 0)
            .Select(c => c.ActualWidth > 0 ? c.ActualWidth : c.Width.Value)
            .ToList();

        var rowWeights = TerminalArea.RowDefinitions
            .Where((_, index) => index % 2 == 0)
            .Select(r => r.ActualHeight > 0 ? r.ActualHeight : r.Height.Value)
            .ToList();

        if (columnWeights.Count == 0 || rowWeights.Count == 0)
            return;

        App.Settings.TerminalTileLayouts ??= new Dictionary<string, TilingLayoutSettings>();
        App.Settings.TerminalTileLayouts[count.ToString()] = new TilingLayoutSettings
        {
            ColumnWeights = columnWeights,
            RowWeights = rowWeights
        };
        App.SettingsService.Save(App.Settings);
    }

    private void SaveApplicationState()
    {
        EnvironmentGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        EnvironmentGrid.CommitEdit(DataGridEditingUnit.Row, true);

        SaveCurrentTileLayout();
        App.Settings.OpenTerminalCount          = Math.Max(1, _vm.Terminals.Count);
        App.Settings.EnvironmentPanelIsExpanded = EnvironmentExpander.IsExpanded;
        App.Settings.CurrentFontSize            = _currentFontSize;
        App.Settings.SelectedTerminalIndex      = _vm.SelectedTerminal != null
            ? Math.Max(0, _vm.Terminals.IndexOf(_vm.SelectedTerminal)) : 0;
        SaveWindowBounds();
        SaveTerminalSnapshots();
        _vm.SaveEnvironmentVariables();
        App.SettingsService.Save(App.Settings);
    }

    private void SaveWindowBounds()
    {
        bool isMax  = WindowState == System.Windows.WindowState.Maximized;
        Rect bounds = isMax ? RestoreBounds : new Rect(Left, Top, Width, Height);
        App.Settings.Window = new Settings.WindowStateSettings
        {
            Left        = bounds.Left,
            Top         = bounds.Top,
            Width       = bounds.Width,
            Height      = bounds.Height,
            IsMaximized = isMax,
        };
    }

    private void SaveTerminalSnapshots()
    {
        try
        {
            Directory.CreateDirectory(SnapshotsDir);
            var list = new List<Settings.TerminalSessionSnapshot>();
            int i = 0;
            foreach (var vm in _vm.Terminals)
            {
                byte[] data = vm.Session.GetOutputSnapshot();
                try { File.WriteAllBytes(SnapshotPath(i), data); } catch { }
                list.Add(new Settings.TerminalSessionSnapshot { DisplayName = vm.DisplayName });
                i++;
            }
            App.Settings.TerminalSnapshots = list;
        }
        catch { }
    }

    private static string SnapshotsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VibeTerm", "snapshots");

    private static string SnapshotPath(int index) =>
        Path.Combine(SnapshotsDir, $"session_{index}.vt");

    private static byte[]? LoadSnapshotData(int index)
    {
        string path = SnapshotPath(index);
        if (!File.Exists(path)) return null;
        try { return File.ReadAllBytes(path); }
        catch { return null; }
    }

    private void RestoreWindowBounds()
    {
        var w = App.Settings.Window;
        if (w == null) return;

        Width  = Math.Clamp(w.Width,  MinWidth,  3840);
        Height = Math.Clamp(w.Height, MinHeight, 2160);

        if (w.Left.HasValue && w.Top.HasValue &&
            IsPositionVisible(w.Left.Value, w.Top.Value, w.Width, w.Height))
        {
            WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
            Left = w.Left.Value;
            Top  = w.Top.Value;
        }

        if (w.IsMaximized)
            WindowState = System.Windows.WindowState.Maximized;
    }

    private static bool IsPositionVisible(double left, double top, double width, double height)
    {
        double vl = SystemParameters.VirtualScreenLeft;
        double vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth;
        double vb = vt + SystemParameters.VirtualScreenHeight;
        // At least a 200×50 strip of the title bar must be within the virtual screen
        return left < vr - 100 && left + width  > vl + 100 &&
               top  < vb - 50  && top  + height > vt;
    }

    private void UpdateNoTerminalOverlay() =>
        NoTerminalOverlay.Visibility = _vm.Terminals.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

    // ── ListBox selection → ViewModel ────────────────────────────────────────

    private void TerminalList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TerminalList.SelectedItem is TerminalViewModel vm)
            _vm.SelectedTerminal = vm;
    }

    // ── close button in sidebar ──────────────────────────────────────────────

    private async void CloseTerminalButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't propagate to ListBoxItem (would change selection)
        if (sender is FrameworkElement fe && fe.Tag is TerminalViewModel vm)
            await ConfirmAndCloseAsync(vm);
    }

    private async System.Threading.Tasks.Task ConfirmAndCloseAsync(TerminalViewModel vm)
    {
        bool isRunning = vm.Session.Status == Terminal.TerminalStatus.Active
                      || vm.Session.Status == Terminal.TerminalStatus.Running;

        if (isRunning)
        {
            var result = MessageBox.Show(
                $"Terminal \"{vm.DisplayName}\" is still running. Close it?",
                "VibeTerm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }

        await _vm.CloseTerminalAsync(vm);
    }

    // ── rename TextBox key handler ───────────────────────────────────────────

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is TerminalViewModel vm)
        {
            if (e.Key == Key.Return) { vm.CommitRenameCommand.Execute(null); e.Handled = true; }
            if (e.Key == Key.Escape) { vm.CancelRenameCommand.Execute(null); e.Handled = true; }
        }
    }

    // ── font size ────────────────────────────────────────────────────────────

    private void FontIncrease_Click(object sender, RoutedEventArgs e) => SetFontSize(_currentFontSize + 1);
    private void FontDecrease_Click(object sender, RoutedEventArgs e) => SetFontSize(_currentFontSize - 1);
    private void FontReset_Click(object sender, RoutedEventArgs e)    => SetFontSize(App.Settings.FontSize);

    private void SetFontSize(double size)
    {
        size = Math.Clamp(size, 8, 32);
        _currentFontSize = size;
        FontSizeLabel.Text = ((int)size).ToString();
        foreach (var v in _terminalViews.Values)
            v.SetFontSize(size);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control)) return;
        switch (e.Key)
        {
            case Key.OemPlus: case Key.Add:
                SetFontSize(_currentFontSize + 1); e.Handled = true; break;
            case Key.OemMinus: case Key.Subtract:
                SetFontSize(_currentFontSize - 1); e.Handled = true; break;
            case Key.D0: case Key.NumPad0:
                SetFontSize(App.Settings.FontSize); e.Handled = true; break;
        }
    }

    // ── settings ─────────────────────────────────────────────────────────────

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        var dlg = new SettingsWindow(App.Settings) { Owner = this };
        if (dlg.ShowDialog() == true)
            SetFontSize(App.Settings.FontSize);
    }

    // ── window close ─────────────────────────────────────────────────────────

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_vm.HasActiveSessions())
        {
            e.Cancel = true;
            var result = MessageBox.Show(
                "Active terminal sessions are running. Close all and exit?",
                "VibeTerm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                SaveApplicationState();
                _vm.Dispose();
                Application.Current.Shutdown();
            }
            return;
        }
        SaveApplicationState();
        _vm.Dispose();
        base.OnClosing(e);
    }
}
