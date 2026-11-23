// SPDX-License-Identifier: GPL-3.0-or-later
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThemeLens.Core.Themes;
using ThemeLens.Engine;
using ThemeLens.Platform.Windows;

namespace ThemeLens.App.ViewModels;

/// <summary>A theme as the drop-down list shows it: its name, under the heading of its base.</summary>
public sealed record ThemeChoice(Theme Theme, string Group)
{
    public string Name => Theme.Name;

    /// <summary>What a screen reader says for the entry.</summary>
    public override string ToString() => Theme.Name;
}

/// <summary>One way of keeping the background (section 6.5), as the drop-down list shows it.</summary>
public sealed record KeepChoice(KeepBackgroundMode Mode, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One line of the shortcut list: the action, and its keys or "not set".</summary>
public sealed record ShortcutRow(string Action, string Keys, bool IsSet)
{
    /// <summary>The whole line as one sentence, for screen readers.</summary>
    public string Spoken => Strings.Format(Strings.ShortcutRowName, Action, Keys);
}

/// <summary>
/// The control window: lens on and off, theme and the user's own values for it, fill-screen, edit mode, status,
/// frame rate and the shortcut list. It lives on the UI thread; the lens reports from its own threads, and
/// everything it reports is brought over here before it touches a property.
/// </summary>
/// <remarks>
/// <para>
/// Two things stand behind it. <see cref="LensRuntime"/> is the lens on screen: its windows, its place, on and
/// off. <see cref="LensSession"/> is what the app knows and remembers: the chosen theme with the user's values,
/// where the lens was, whether it was on. The runtime moves and switches the lens by itself when its handle or
/// a shortcut is used, so this class tells the session afterwards.
/// </para>
/// <para>
/// The first-run flow, the mapping table, the tray icon and the settings page of section 4 are not here yet.
/// </para>
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly LensRuntime _runtime;
    private readonly LensSession _session;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refresh;
    private LensStatus _latestStatus;
    private int _statusQueued;
    private bool _syncing;
    private bool _disposed;

    public MainViewModel(LensRuntime runtime, LensSession session, AppOptions options, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _runtime = runtime;
        _session = session;
        _dispatcher = dispatcher;

        Themes = [.. session.Catalogue.All.Select(static theme => new ThemeChoice(theme, GroupName(theme.Base)))];
        var view = new ListCollectionView(Themes.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ThemeChoice.Group)));
        ThemesView = view;
        KeepChoices =
        [
            new KeepChoice(KeepBackgroundMode.Strict, Strings.KeepStrict),
            new KeepChoice(KeepBackgroundMode.Balanced, Strings.KeepBalanced),
            new KeepChoice(KeepBackgroundMode.Off, Strings.KeepOff),
        ];

        Shortcuts = [.. runtime.HotkeyStatus.Select(ToRow)];
        Notes = [.. BuildNotes(runtime, options)];

        _latestStatus = runtime.Controller.Status;
        _syncing = true;
        string currentId = session.SelectedTheme.Id;
        SelectedTheme = Themes.FirstOrDefault(choice => choice.Theme.Id == currentId) ?? Themes.FirstOrDefault();
        ShowAdjustments();
        Apply(_latestStatus);
        IsEditMode = runtime.IsEditMode;
        FillsScreen = runtime.Placement.FillsScreen;
        FrameRateText = Strings.Format(Strings.FrameRate, 0);
        _syncing = false;
        runtime.SetHandleText(HandleText(session.SelectedTheme.Name));

        runtime.Controller.StatusChanged += OnStatusChanged;
        runtime.EditModeChanged += OnLensChanged;
        runtime.PlacementChanged += OnLensChanged;
        runtime.HotkeyPressed += OnHotkeyPressed;
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, OnRefresh, dispatcher);
    }

    /// <summary>The "open control window" shortcut was pressed.</summary>
    public event Action? ShowWindowRequested;

    public IReadOnlyList<ThemeChoice> Themes { get; }

    /// <summary>The themes grouped by base, for the drop-down list.</summary>
    public ICollectionView ThemesView { get; }

    public IReadOnlyList<ShortcutRow> Shortcuts { get; }

    /// <summary>The ways of keeping the background, for the drop-down list.</summary>
    public IReadOnlyList<KeepChoice> KeepChoices { get; }

    /// <summary>Things worth knowing about this run: development mode, the test pattern, rendering on the CPU.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>True when the app was started with --dev. The window says so at the top, where it cannot be missed.</summary>
    public bool IsDevMode => _runtime.IsDevMode;

    [ObservableProperty]
    public partial bool IsLensOn { get; set; }

    [ObservableProperty]
    public partial bool IsEditMode { get; set; }

    [ObservableProperty]
    public partial bool FillsScreen { get; set; }

    [ObservableProperty]
    public partial ThemeChoice? SelectedTheme { get; set; }

    /// <summary>The status pill: "Live, 9 ms", "Live, reduced speed", "Holding background", or why capture is not available.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>What costs the time while the status is "Live, reduced speed" (FR-25). Empty otherwise.</summary>
    [ObservableProperty]
    public partial string StatusDetail { get; set; } = string.Empty;

    /// <summary>
    /// Changes only when the state itself changes, not with every millisecond of delay. The window uses it to
    /// tell a screen reader "Lens on", "Holding background" and so on, once each.
    /// </summary>
    [ObservableProperty]
    public partial string StatusKind { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrameRateText { get; set; } = string.Empty;

    // The user's own values for the selected theme (FR-14). They change the lens while the slider moves and
    // are remembered per theme.

    /// <summary>Brightness limit, 0.05 to 1: no pixel of the lens is brighter than this share of white.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrightnessText))]
    public partial double BrightnessLimit { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WarmthText))]
    public partial double Warmth { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContrastText))]
    public partial double Contrast { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SharpenText))]
    public partial double Sharpen { get; set; }

    [ObservableProperty]
    public partial KeepChoice? SelectedKeep { get; set; }

    /// <summary>False for a theme that keeps the screen's own colours: it has no background of its own to keep.</summary>
    [ObservableProperty]
    public partial bool CanKeepBackground { get; set; }

    /// <summary>True when the user has changed something on this theme, so there is something to go back from.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetAdjustmentsCommand))]
    public partial bool HasAdjustments { get; set; }

    public string BrightnessText => Percent(BrightnessLimit);

    public string WarmthText => Percent(Warmth);

    public string ContrastText => Percent(Contrast);

    public string SharpenText => Percent(Sharpen);

    /// <summary>Returns the selected theme to the values in its file.</summary>
    [RelayCommand(CanExecute = nameof(HasAdjustments))]
    private void ResetAdjustments()
    {
        _session.ResetAdjustments();
        Sync(ShowAdjustments);
    }

    /// <summary>Moves to the theme after the current one, and round to the first again.</summary>
    public void SelectNextTheme()
    {
        if (Themes.Count == 0)
        {
            return;
        }

        int index = SelectedTheme is null ? -1 : Themes.ToList().IndexOf(SelectedTheme);
        SelectedTheme = Themes[(index + 1) % Themes.Count];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refresh.Stop();
        _session.Flush();
        _runtime.Controller.StatusChanged -= OnStatusChanged;
        _runtime.EditModeChanged -= OnLensChanged;
        _runtime.PlacementChanged -= OnLensChanged;
        _runtime.HotkeyPressed -= OnHotkeyPressed;
    }

    private static string GroupName(ThemeBase themeBase) => themeBase switch
    {
        ThemeBase.Dark => Strings.BaseDark,
        ThemeBase.Light => Strings.BaseLight,
        _ => Strings.BaseKeep,
    };

    private static ShortcutRow ToRow(HotkeyRegistration registration)
    {
        string action = registration.Action switch
        {
            HotkeyAction.ToggleLens => Strings.ShortcutToggleLens,
            HotkeyAction.ToggleEditMode => Strings.ShortcutEditMode,
            HotkeyAction.ToggleFillScreen => Strings.ShortcutFillScreen,
            HotkeyAction.NextTheme => Strings.ShortcutNextTheme,
            HotkeyAction.OpenControlWindow => Strings.ShortcutControlWindow,
            _ => Strings.ShortcutEverythingOff,
        };
        string keys = registration.IsRegistered
            ? Strings.Format(Strings.ShortcutGesture, registration.Key)
            : Strings.ShortcutNotSet;
        return new ShortcutRow(action, keys, registration.IsRegistered);
    }

    private static IEnumerable<string> BuildNotes(LensRuntime runtime, AppOptions options)
    {
        if (options.Unknown is not null)
        {
            yield return Strings.Format(Strings.UnknownOption, options.Unknown);
        }

        if (options.Source == FrameSourceKind.Pattern)
        {
            yield return Strings.PatternNote;
        }

        if (runtime.IsSoftwareRendered)
        {
            yield return Strings.SoftwareRendering;
        }

        yield return Strings.CaptureNote;
    }

    private static string Percent(double share) =>
        Strings.Format(Strings.PercentValue, Math.Round(share * 100).ToString("0", CultureInfo.CurrentCulture));

    private string HandleText(string themeName) =>
        Strings.Format(IsDevMode ? Strings.HandleTextDev : Strings.HandleText, themeName);

    /// <summary>Puts the values the lens is using now into the sliders. Call inside <see cref="Sync"/>.</summary>
    private void ShowAdjustments()
    {
        Theme shown = _session.EffectiveTheme;
        BrightnessLimit = shown.Limits.MaxLuminance;
        Warmth = shown.Warmth;
        Contrast = shown.Contrast.Amount;
        Sharpen = shown.Sharpen.Amount;
        CanKeepBackground = shown.HasRoles;
        SelectedKeep = KeepChoices.FirstOrDefault(choice => choice.Mode == shown.KeepBackground);
        HasAdjustments = !_session.Adjustments.IsEmpty;
    }

    private void Adjust(Func<ThemeAdjustments, ThemeAdjustments> change)
    {
        if (_syncing)
        {
            return;
        }

        _session.Adjust(change);
        HasAdjustments = !_session.Adjustments.IsEmpty;
    }

    partial void OnBrightnessLimitChanged(double value) => Adjust(a => a with { BrightnessLimit = (float)value });

    partial void OnWarmthChanged(double value) => Adjust(a => a with { Warmth = (float)value });

    partial void OnContrastChanged(double value) => Adjust(a => a with { ContrastAmount = (float)value });

    partial void OnSharpenChanged(double value) => Adjust(a => a with { SharpenAmount = (float)value });

    partial void OnSelectedKeepChanged(KeepChoice? value)
    {
        if (value is not null)
        {
            Adjust(a => a with { KeepBackground = value.Mode });
        }
    }

    partial void OnIsLensOnChanged(bool value)
    {
        if (_syncing)
        {
            return;
        }

        _session.NoteLensSwitched(value);

        // Starting capture can take a moment; the window stays responsive meanwhile. The status line follows.
        LensController controller = _runtime.Controller;
        _ = Task.Run(() =>
        {
            if (value)
            {
                controller.TurnOn();
            }
            else
            {
                controller.TurnOff();
            }
        });
    }

    partial void OnIsEditModeChanged(bool value)
    {
        if (!_syncing)
        {
            _runtime.SetEditMode(value);
        }
    }

    partial void OnFillsScreenChanged(bool value)
    {
        if (!_syncing && _runtime.Placement.FillsScreen != value)
        {
            _runtime.ToggleFillScreen();
        }
    }

    partial void OnSelectedThemeChanged(ThemeChoice? value)
    {
        if (value is null)
        {
            return;
        }

        if (_syncing)
        {
            return;
        }

        // The session puts the theme on the lens, with the values the user last chose for it.
        _session.SelectTheme(value.Theme.Id);
        _runtime.SetHandleText(HandleText(value.Name));
        Sync(ShowAdjustments);
    }

    /// <summary>Called on one of the lens's threads, many times a second while it is live: keep the newest, queue one update.</summary>
    private void OnStatusChanged(LensStatus status)
    {
        Volatile.Write(ref _latestStatus, status);
        if (Interlocked.Exchange(ref _statusQueued, 1) == 0)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                Volatile.Write(ref _statusQueued, 0);
                if (!_disposed)
                {
                    Sync(() => Apply(Volatile.Read(ref _latestStatus)));
                }
            });
        }
    }

    private void OnLensChanged() =>
        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                // The lens was moved, resized or filled by its handle, by a shortcut or from here: remember where it is.
                _session.NotePlacement(_runtime.Placement);
                Sync(() =>
                {
                    IsEditMode = _runtime.IsEditMode;
                    FillsScreen = _runtime.Placement.FillsScreen;
                });
            }
        });

    private void OnHotkeyPressed(HotkeyAction action) =>
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            // The lens has already carried out its own shortcuts; these two are the app's.
            if (action is HotkeyAction.ToggleLens or HotkeyAction.EverythingOff)
            {
                _session.NoteLensSwitched(_runtime.Controller.State != LensState.Off);
            }
            else if (action == HotkeyAction.NextTheme)
            {
                SelectNextTheme();
            }
            else if (action == HotkeyAction.OpenControlWindow)
            {
                ShowWindowRequested?.Invoke();
            }
        });

    private void OnRefresh(object? sender, EventArgs e)
    {
        // Twice a second is often enough to write down what a slider did, and seldom enough for the disk.
        _session.Flush();
        double rate = _runtime.Controller.State == LensState.Off ? 0 : _runtime.FramesPerSecond;
        FrameRateText = Strings.Format(Strings.FrameRate, Math.Round(rate).ToString("0", CultureInfo.CurrentCulture));
    }

    /// <summary>Sets properties from the lens's own state without sending them back to the lens as commands.</summary>
    private void Sync(Action update)
    {
        bool before = _syncing;
        _syncing = true;
        try
        {
            update();
        }
        finally
        {
            _syncing = before;
        }
    }

    private void Apply(LensStatus status)
    {
        IsLensOn = status.State != LensState.Off;
        string text = status.State switch
        {
            LensState.Off => status.CaptureProblem ?? Strings.StatusOff,
            LensState.HoldingBackground => Strings.StatusHolding,
            _ when status.ReducedSpeed => Strings.StatusLiveReduced,
            _ => Strings.Format(Strings.StatusLive, Math.Round(status.Delay.TotalMilliseconds).ToString("0", CultureInfo.CurrentCulture)),
        };
        StatusText = status.State == LensState.Edit ? Strings.Format(Strings.StatusEditing, text) : text;
        StatusKind = $"{status.State}|{status.HoldReason}|{status.ReducedSpeed}|{status.CaptureProblem}";
        StatusDetail = status.SlowOption switch
        {
            SlowOption.StrongSharpen => Strings.SlowStrongSharpen,
            SlowOption.LocalContrast => Strings.SlowLocalContrast,
            SlowOption.CpuRendering => Strings.SlowCpuRendering,
            _ => string.Empty,
        };
    }
}
