#if DEBUG
using Petty.App.Pet;
using Petty.Core.Behavior;
using Petty.Core.Diagnostics;
using Petty.Core.Runtime;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Petty.App.Views;

/// <summary>Live diagnostics and bounded tuning, available only in Debug builds.</summary>
internal sealed partial class DebugWindow: Window
{
    private readonly PetRuntime _runtime;
    private readonly Func<PetSession?> _session;
    private readonly Action<DebugTuning> _apply;
    private readonly Func<bool> _savePet;
    private readonly DebugTuningStore _store;
    private readonly Slider[] _values = new Slider[16];
    private readonly TextBox[] _numbers = new TextBox[16];
    private readonly Slider[] _needs = new Slider[2];
    private readonly TextBox[] _needNumbers = new TextBox[2];
    private TextBox? _editingInput;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private DebugTuning _pendingTuning;
    private bool _tuningDirty;
    private bool _petDirty;
    private bool _updating;

    public DebugWindow(PetRuntime runtime, Func<PetSession?> session, DebugTuning settings,
        DebugTuningStore store, Action<DebugTuning> apply, Func<bool> savePet)
    {
        _runtime = runtime; _session = session; _store = store; _apply = apply; _savePet = savePet;
        _pendingTuning = settings.Sanitized();
        InitializeComponent();
        DebugParameterControl[] controls =
        [
            SatietyDecayControl, HappinessDecayControl, HungryControl, TiredControl, RecoveryControl,
            PlayDurationControl, PetDurationControl, EatIntervalControl, SleepAfterControl,
            SleepMinControl, SleepMaxControl, WanderMinControl, WanderMaxControl,
            RunChanceControl, SleepChanceControl, TimeScaleControl,
        ];
        for (var i = 0; i < controls.Length; i++)
        {
            var index = i;
            _values[i] = controls[i].ValueSlider;
            _numbers[i] = controls[i].ValueInput;
            _values[i].ValueChanged += (_, _) => OnTuningChanged();
            _numbers[i].TextChanged += (_, _) => OnInputChanged(_numbers[index], _values[index], index < 2);
            _numbers[i].LostKeyboardFocus += (_, _) => NormalizeInput(_numbers[index], _values[index]);
        }
        DebugParameterControl[] needControls = [SatietyControl, HappinessControl];
        for (var i = 0; i < needControls.Length; i++)
        {
            var index = i;
            _needs[i] = needControls[i].ValueSlider;
            _needNumbers[i] = needControls[i].ValueInput;
            _needs[i].ValueChanged += (_, _) => OnNeedChanged(index);
            _needNumbers[i].TextChanged += (_, _) => OnInputChanged(_needNumbers[index], _needs[index], false);
            _needNumbers[i].LostKeyboardFocus += (_, _) => NormalizeInput(_needNumbers[index], _needs[index]);
        }
        ShowTuning(_pendingTuning);
        ShowNeeds();
        _timer.Tick += Refresh;
        _saveTimer.Tick += OnSaveTick;
        Closed += (_, _) => { _timer.Stop(); _timer.Tick -= Refresh; FlushChanges(); _saveTimer.Tick -= OnSaveTick; };
        _timer.Start(); Refresh(this, EventArgs.Empty);
    }

    private void OnReset(object sender, RoutedEventArgs e) => ApplyTuning(new DebugTuning());
    private void OnFillBowl(object sender, RoutedEventArgs e) => SetFood(_runtime.Bowl.Capacity);
    private void OnEmptyBowl(object sender, RoutedEventArgs e) => SetFood(0);
    private void OnSleep(object sender, RoutedEventArgs e) => Request(_runtime.Behavior.RequestDebugActivity(PetActivity.Sleeping));
    private void OnWake(object sender, RoutedEventArgs e) => Request(_runtime.Behavior.RequestDebugActivity(PetActivity.Idle));
    private void OnWalk(object sender, RoutedEventArgs e) => Request(_runtime.Behavior.RequestDebugActivity(PetActivity.Walking));
    private void OnRun(object sender, RoutedEventArgs e) => Request(_runtime.Behavior.RequestDebugActivity(PetActivity.Running));
    private void OnPlay(object sender, RoutedEventArgs e) => Request(_runtime.RequestPlay());
    private void OnPet(object sender, RoutedEventArgs e) => Request(_runtime.RequestPet());

    private void OnInputChanged(TextBox input, Slider slider, bool isDecay)
    {
        if (_updating) return;
        var text = input.Text.Trim();
        if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
             !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) || !double.IsFinite(value))
        {
            SetStatus("DebugInvalidNumber");
            return;
        }
        if (isDecay && value > slider.Maximum) slider.Maximum = Math.Min(10000.0 / 60, value);
        _editingInput = input;
        try { slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum); }
        finally { _editingInput = null; }
    }

    private void NormalizeInput(TextBox input, Slider slider)
    {
        _updating = true;
        try { input.Text = Format(slider.Value); }
        finally { _updating = false; }
    }

    private void UpdateInput(TextBox input, double value)
    {
        if (!ReferenceEquals(input, _editingInput)) input.Text = Format(value);
    }

    private void OnTuningChanged()
    {
        if (_updating) return;
        ApplyTuning(new DebugTuning
        {
            SatietyDecayPerMinute = _values[0].Value,
            HappinessDecayPerMinute = _values[1].Value,
            Hungry = _values[2].Value,
            Tired = _values[3].Value,
            Recovery = _values[4].Value,
            PlaySeconds = _values[5].Value,
            PetSeconds = _values[6].Value,
            EatBiteSeconds = _values[7].Value,
            SleepAfterSeconds = _values[8].Value,
            SleepMinSeconds = _values[9].Value,
            SleepMaxSeconds = _values[10].Value,
            WanderMinSeconds = _values[11].Value,
            WanderMaxSeconds = _values[12].Value,
            RunChancePercent = _values[13].Value,
            SleepChancePercent = _values[14].Value,
            TimeScale = _values[15].Value,
        });
    }

    private void ApplyTuning(DebugTuning value)
    {
        var interactionActive = _runtime.Behavior.State is PetActivity.Playing or PetActivity.Petting or PetActivity.Sleeping;
        var durationChanged = value.PlaySeconds != _pendingTuning.PlaySeconds || value.PetSeconds != _pendingTuning.PetSeconds ||
            value.SleepMinSeconds != _pendingTuning.SleepMinSeconds || value.SleepMaxSeconds != _pendingTuning.SleepMaxSeconds;
        _pendingTuning = value.Sanitized();
        _apply(_pendingTuning);
        ShowTuning(_pendingTuning);
        _tuningDirty = true;
        ScheduleSave();
        SetStatus(interactionActive && durationChanged ? "DebugTuningNextAction" : "DebugTuningApplied");
    }

    private void ShowTuning(DebugTuning value)
    {
        double[] numbers = [value.SatietyDecayPerMinute, value.HappinessDecayPerMinute, value.Hungry, value.Tired, value.Recovery, value.PlaySeconds, value.PetSeconds, value.EatBiteSeconds, value.SleepAfterSeconds, value.SleepMinSeconds, value.SleepMaxSeconds, value.WanderMinSeconds, value.WanderMaxSeconds, value.RunChancePercent, value.SleepChancePercent, value.TimeScale];
        _updating = true;
        try
        {
            for (var i = 0; i < numbers.Length; i++)
            {
                if (i < 2) _values[i].Maximum = Math.Max(10, numbers[i]);
                _values[i].Value = numbers[i];
                UpdateInput(_numbers[i], numbers[i]);
            }
        }
        finally { _updating = false; }
    }

    private void OnNeedChanged(int index)
    {
        if (_updating) return;
        var satiety = index == 0 ? _needs[index].Value : _runtime.Needs.Satiety;
        var happiness = index == 1 ? _needs[index].Value : _runtime.Needs.Happiness;
        _runtime.SetNeeds(satiety, happiness);
        UpdateInput(_needNumbers[index], _needs[index].Value);
        _petDirty = true;
        ScheduleSave();
        SetStatus("DebugNeedsUpdated");
    }

    private void ShowNeeds()
    {
        double[] numbers = [_runtime.Needs.Satiety, _runtime.Needs.Happiness];
        _updating = true;
        try
        {
            for (var i = 0; i < numbers.Length; i++)
            {
                if (!_needs[i].IsMouseCaptureWithin && !_needs[i].IsKeyboardFocusWithin) _needs[i].Value = numbers[i];
                if (!_needNumbers[i].IsKeyboardFocusWithin) UpdateInput(_needNumbers[i], numbers[i]);
            }
        }
        finally { _updating = false; }
    }

    private void ScheduleSave() { _saveTimer.Stop(); _saveTimer.Start(); }
    private void OnSaveTick(object? sender, EventArgs e) => FlushChanges();
    private void FlushChanges()
    {
        _saveTimer.Stop();
        if (_tuningDirty)
        {
            try { _store.Save(_pendingTuning); _tuningDirty = false; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            { StatusText.Text = string.Format(Localization.Text("DebugSaveError"), ex.Message); }
        }
        if (_petDirty)
        {
            if (_savePet()) _petDirty = false;
            else SetStatus("DebugPetSaveError");
        }
    }

    private void SetFood(double amount)
    {
        _runtime.SetBowlFood(amount);
        _session()?.RefreshDebugBowl();
        _petDirty = !_savePet();
        SetStatus(_petDirty ? "DebugPetSaveError" : "DebugBowlUpdated");
    }

    public void ShowLoadError() => SetStatus("DebugLoadError");
    private void Request(bool accepted) => SetStatus(accepted ? "DebugActionStarted" : "DebugActionUnavailable");
    private void SetStatus(string key) => StatusText.SetResourceReference(TextBlock.TextProperty, key);
    private static string Format(double value) => value.ToString("0.########", CultureInfo.CurrentCulture);

    private void Refresh(object? sender, EventArgs e)
    {
        var session = _session();
        var area = session?.WalkArea;
        var activity = _runtime.Behavior.State.ToString();
        var clip = session?.DebugClip ?? "—";
        var localizedActivity = Application.Current.TryFindResource("DebugState" + activity) as string ?? activity;
        var clipKey = clip.Length > 0 ? "DebugClip" + char.ToUpperInvariant(clip[0]) + clip[1..] : "";
        var localizedClip = Application.Current.TryFindResource(clipKey) as string ?? clip;
        DiagnosticsText.Text = string.Format(CultureInfo.CurrentCulture, Localization.Text("DebugDiagnostics"),
            localizedActivity, localizedClip, session?.DebugFps ?? 0, _runtime.Needs.Satiety,
            _runtime.Needs.Happiness, _runtime.Bowl.Food, area?.Left ?? 0, area?.Right ?? 0, area?.FloorY ?? 0, Environment.NewLine);
        ShowNeeds();
    }
}
#endif
