using System.Threading;
using System.Windows;
using Noty.Core;
using Noty.Deck;
using Noty.Interop;
using Noty.Services;
using Noty.Windows;

namespace Noty;

public partial class App : Application
{
    private static Mutex? _single;

    private DeckManager? _decks;
    private HotKeys? _hotKeys;
    private TrayIcon? _tray;
    private string? _reportedHotKeyFailures;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One deck per display is the point; two copies of the app is not.
        _single = new Mutex(true, "Local\\NotyRunningInstance", out var fresh);
        if (!fresh)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Line($"unhandled — {args.Exception}");
            args.Handled = true;
        };

        _decks = new DeckManager();
        Actions.Decks = _decks;
        Actions.OnShortcutsChanged = RegisterHotKeys;

        _hotKeys = new HotKeys();
        RegisterHotKeys();

        _tray = new TrayIcon();
        UndoToast.Shared.Start();

        Log.Line("started");
    }

    /// Rebinding in Settings tears the whole set down and puts it back, which is the
    /// only way RegisterHotKey lets a binding change.
    private void RegisterHotKeys()
    {
        if (_hotKeys is null) return;
        _hotKeys.Clear();

        var failed = new List<(string Command, Shortcut Shortcut)>();
        TryRegister("新建便签", Settings.ScNewNote, Actions.NewNote);
        TryRegister("全部便签", Settings.ScAllNotes, Actions.OpenAllNotes);
        TryRegister("归档", Settings.ScArchive, Actions.OpenArchive);

        if (failed.Count == 0)
        {
            _reportedHotKeyFailures = null;
            return;
        }

        var signature = string.Join("\n", failed.Select(f => $"{f.Command}:{f.Shortcut}"));
        if (signature == _reportedHotKeyFailures) return;
        _reportedHotKeyFailures = signature;

        // Run after the current key event (and, at startup, after the tray icon has
        // been created) so a failed registration cannot disappear into the log.
        Dispatcher.BeginInvoke(() =>
        {
            if (_reportedHotKeyFailures != signature) return;
            ShowHotKeyWarning(failed);
        });

        void TryRegister(string command, Shortcut shortcut, Action action)
        {
            if (!_hotKeys.Register(shortcut, action)) failed.Add((command, shortcut));
        }
    }

    private static void ShowHotKeyWarning(IReadOnlyList<(string Command, Shortcut Shortcut)> failed)
    {
        var bindings = string.Join("\n", failed.Select(f => $"• {f.Command} — {f.Shortcut}"));
        var message = $"Noty 无法注册以下全局快捷键：\n\n{bindings}\n\n" +
                      "Windows 已保留该快捷键，或已有其他程序占用。" +
                      "请在“设置”中改用其他快捷键。";

        var owner = Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (owner is not null)
            MessageBox.Show(owner, message, "全局快捷键不可用",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(message, "全局快捷键不可用",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotKeys?.Dispose();
        _decks?.Dispose();
        Settings.Flush();
        base.OnExit(e);
    }
}
