// @module: reEngine.Host.Ui.ControlsTabView
// @purpose: Expressive Controls tab renderer (tier profile selector, Modern WASD+Mouse, mouse sensitivity stepper, menu chord, and dynamic ControlOptions).
// @depends-on: System, System.Collections.Generic, System.Drawing, System.Globalization, Foundation.Core, reEngine.Host.Contracts, reEngine.Host.Input
// @exports: ControlsTabView

using System.Drawing;
using System.Globalization;
using Foundation.Core;
using reEngine.Host.Contracts;
using reEngine.Host.Input;

namespace reEngine.Host.Ui;

/// <summary>
/// Renders and handles clicks for input profiles, PC camera/mouse controls, menu chord, and per-game <c>ControlOptions</c>.
/// </summary>
public sealed class ControlsTabView
{
    private sealed record ClickAction(string ControlId, Rectangle Bounds, Func<HostInputMapper, IHostSessionEventCallbacks?, bool> OnActivate);

    private readonly List<ClickAction> _actions = new();
    private readonly List<HostWidgetBounds> _widgets = new();
    private readonly Dictionary<string, string> _selectedChoices = new(StringComparer.OrdinalIgnoreCase);
    private HostInputSchemaKind? _pendingSchema;
    private HostControlTierMode _displayTier = HostControlTierMode.PerGameModern;
    private HostControlMode _displayMode = HostControlMode.ModernWasdMouse;
    private double _displayMouseSens = 1.0;
    private HostMenuControllerChord _displayMenuChord = HostMenuControllerChord.L3PlusR3;
    private int _standaloneScroll;

    /// <summary>Gets the current selected choice per <c>optionId</c>.</summary>
    public IReadOnlyDictionary<string, string> SelectedChoices => _selectedChoices;

    /// <summary>Gets the bounds of the profile cycle button rendered inside the Controls tab.</summary>
    public Rectangle CycleSchemaButtonRect { get; private set; }

    /// <summary>Gets the bottom Y coordinate occupied by rendered Controls tab content.</summary>
    public int ContentBottomY { get; private set; }

    /// <summary>Gets or sets live session event callbacks notified when a control option changes.</summary>
    public IHostSessionEventCallbacks? EventCallbacks { get; set; }

    /// <summary>Returns the currently selected choice for <paramref name="optionId"/>, or <see langword="null"/> if unset.</summary>
    public string? GetSelectedChoice(string optionId) =>
        _selectedChoices.TryGetValue(optionId, out string? choice) ? choice : null;

    /// <summary>Sets the selected choice for <paramref name="optionId"/> programmatically.</summary>
    public void SetSelectedChoice(string optionId, string choice)
    {
        if (!string.IsNullOrWhiteSpace(optionId) && !string.IsNullOrWhiteSpace(choice))
            _selectedChoices[optionId] = choice;
    }

    /// <summary>Renders the Controls tab into <paramref name="bodyRect"/>.</summary>
    public void Render(
        PixelOverlayCanvas canvas,
        Rectangle bodyRect,
        HostInputSchemaKind selectedSchema,
        IReadOnlyList<HostControlOptionDescriptor>? controlOptions) =>
        RenderCore(canvas, bodyRect, selectedSchema, controlOptions, view: null, tierStore: null, ref _standaloneScroll, out _);

    internal void Render(
        PixelOverlayCanvas canvas,
        Rectangle bodyRect,
        F1SettingsDrawerView view,
        HostDrawerDescriptor descriptor,
        HostControlTierStore? tierStore,
        ref int scrollOffset,
        out int maxScroll)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(descriptor);
        _displayTier = tierStore?.ActiveTier ?? _displayTier;
        _displayMode = view.ControlMode;
        _displayMouseSens = view.MouseSensitivity;
        _displayMenuChord = view.MenuControllerChord;
        RenderCore(canvas, bodyRect, view.SelectedSchema, descriptor.ControlOptions, view, tierStore, ref scrollOffset, out maxScroll, descriptor.ExtraRows);
    }

    internal void RenderCore(
        PixelOverlayCanvas canvas,
        Rectangle bodyRect,
        HostInputSchemaKind selectedSchema,
        IReadOnlyList<HostControlOptionDescriptor>? controlOptions,
        F1SettingsDrawerView? view,
        HostControlTierStore? tierStore,
        ref int scrollOffset,
        out int maxScroll,
        IReadOnlyList<HostDrawerRowDescriptor>? extraRows = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        _actions.Clear();
        _widgets.Clear();
        CycleSchemaButtonRect = Rectangle.Empty;

        double uiScale = view?.UiTextScale ?? 1.0;
        var layout = new HostDrawerScrollLayout(bodyRect, uiScale, view?.FocusedControlId);
        var controls = new List<DrawerControl>();
        HostControlTierMode activeTier = tierStore?.ActiveTier ?? _displayTier;
        HostControlMode mode = view?.ControlMode ?? _displayMode;
        double sens = Math.Clamp(double.IsFinite(view?.MouseSensitivity ?? _displayMouseSens) ? (view?.MouseSensitivity ?? _displayMouseSens) : 1.0, 0.25, 3.0);
        HostMenuControllerChord chord = view?.MenuControllerChord ?? _displayMenuChord;

        layout.AddText($"Preset: {HostInputSchemas.FormatPresetLabel(selectedSchema)}", HostOverlayPalette.TextPrimary);

        layout.AddPill("Controls_Profile_ModernPc", "Modern PC (Per-Action)", activeTier == HostControlTierMode.PerGameModern,
            HostOverlayPalette.AccentCyan, _ => { }, controls);
        _actions.Add(new("Controls_Profile_ModernPc", Rectangle.Empty, (m, _) => ApplyTier(m, tierStore, HostControlTierMode.PerGameModern)));

        layout.AddColumns(controls,
            ("Controls_Profile_Game1to1", "Game 1:1", activeTier == HostControlTierMode.PerGameController, false, HostOverlayPalette.AccentCyan, _ => { }, null),
            ("Controls_Profile_Global1to1", "Global 1:1", activeTier == HostControlTierMode.GlobalController, false, HostOverlayPalette.AccentCyan, _ => { }, null));
        _actions.Add(new("Controls_Profile_Game1to1", Rectangle.Empty, (m, _) => ApplyTier(m, tierStore, HostControlTierMode.PerGameController)));
        _actions.Add(new("Controls_Profile_Global1to1", Rectangle.Empty, (m, _) => ApplyTier(m, tierStore, HostControlTierMode.GlobalController)));

        layout.AddToggle("Controls_Toggle_ModernWasdMouse", "Modern WASD + Mouse", mode == HostControlMode.ModernWasdMouse,
            HostOverlayPalette.AccentCyan, _ => { }, controls);
        _actions.Add(new("Controls_Toggle_ModernWasdMouse", Rectangle.Empty, (m, cb) =>
        {
            HostControlMode next = m.ControlMode == HostControlMode.ModernWasdMouse ? HostControlMode.ClassicTank : HostControlMode.ModernWasdMouse;
            m.ControlMode = next;
            _displayMode = next;
            if (view is not null) view.ControlMode = next;
            cb?.OnControlModeChanged(next);
            return true;
        }));

        string sensText = string.Create(CultureInfo.InvariantCulture, $"{sens:0.00}x");
        layout.AddStepper("Controls_MouseSens", "Controls_MouseSens_Dec", "Controls_MouseSens_Inc",
            "Mouse Sensitivity", sensText, HostOverlayPalette.AccentCyan, _ => { }, _ => { }, _ => { }, controls);
        _actions.Add(new("Controls_MouseSens_Dec", Rectangle.Empty, (m, cb) => StepSensitivity(m, view, cb, -0.25)));
        _actions.Add(new("Controls_MouseSens_Inc", Rectangle.Empty, (m, cb) => StepSensitivity(m, view, cb, 0.25)));
        _actions.Add(new("Controls_MouseSens", Rectangle.Empty, (m, cb) => StepSensitivity(m, view, cb, 0.25)));

        string chordLabel = $"Chord: {HostMenuControllerChordCodec.ToDisplayLabel(chord)}";
        layout.AddColumns(controls,
            ("Controls_MenuChord", chordLabel, chord != HostMenuControllerChord.None, false, HostOverlayPalette.AccentCyan, _ => { }, null),
            ("Drawer_ActionButton", "Cycle Control Profile", true, false, HostOverlayPalette.AccentCyan, _ => { }, r => CycleSchemaButtonRect = r));
        _actions.Add(new("Controls_MenuChord", Rectangle.Empty, (m, cb) =>
        {
            HostMenuControllerChord next = HostMenuControllerChordCodec.CycleNext(m.MenuControllerChord);
            m.MenuControllerChord = next;
            _displayMenuChord = next;
            if (view is not null) view.MenuControllerChord = next;
            cb?.OnMenuControllerChordChanged(next);
            return true;
        }));
        _actions.Add(new("Drawer_ActionButton", Rectangle.Empty, (m, _) =>
        {
            HostInputSchemaKind next = HostInputSchemas.CycleNext(m.ActiveSchema);
            _pendingSchema = next;
            return true;
        }));

        AppendControlOptions(layout, controlOptions, controls);
        if (view is not null) HostDrawerScrollLayout.AppendExtraRows(layout, HostDrawerTabId.Controls, extraRows, view, controls);

        ContentBottomY = layout.Render(canvas, "CONTROLS & INPUT", "Controls", ref scrollOffset, out maxScroll, controls, d => view?.ScrollActiveTab(d));
        SyncRenderedBounds(controls);
    }

    /// <summary>Returns interactive widget bounds produced by the most recent render pass.</summary>
    public IReadOnlyList<HostWidgetBounds> GetWidgets() => _widgets;

    /// <summary>Processes a pointer click on the Controls tab using <see cref="EventCallbacks"/>.</summary>
    public bool TryHandleClick(int x, int y, string? targetId, HostInputMapper inputMapper, ref HostInputSchemaKind selectedSchema) =>
        TryHandleClick(x, y, targetId, inputMapper, ref selectedSchema, EventCallbacks);

    /// <summary>Processes a pointer click on the Controls tab and notifies <paramref name="eventCallbacks"/> on option changes.</summary>
    public bool TryHandleClick(
        int x, int y, string? targetId, HostInputMapper inputMapper,
        ref HostInputSchemaKind selectedSchema, IHostSessionEventCallbacks? eventCallbacks)
    {
        ArgumentNullException.ThrowIfNull(inputMapper);
        _pendingSchema = null;

        foreach (ClickAction action in _actions)
        {
            bool match = !string.IsNullOrEmpty(targetId)
                ? string.Equals(targetId, action.ControlId, StringComparison.OrdinalIgnoreCase)
                : (!action.Bounds.IsEmpty && action.Bounds.Contains(x, y));
            if (!match) continue;

            _ = action.OnActivate(inputMapper, eventCallbacks ?? EventCallbacks);
            if (_pendingSchema is { } nextSchema)
            {
                selectedSchema = nextSchema;
                inputMapper.ActiveSchema = nextSchema;
            }
            return true;
        }
        return false;
    }

    private void AppendControlOptions(HostDrawerScrollLayout layout, IReadOnlyList<HostControlOptionDescriptor>? options, List<DrawerControl> controls)
    {
        if (options is null || options.Count == 0) return;
        layout.AddText("GAME CONTROL OPTIONS", HostOverlayPalette.AccentCyan);
        foreach (HostControlOptionDescriptor opt in options)
        {
            if (opt.Choices.Count == 0) continue;
            string current = ResolveChoice(opt);
            layout.AddText(opt.Label, HostOverlayPalette.TextPrimary);

            if (opt.Choices.Count <= 3)
            {
                var cols = new (string, string, bool, bool, RgbaColor, Action<bool>, Action<Rectangle>?)[opt.Choices.Count];
                for (int i = 0; i < opt.Choices.Count; i++)
                {
                    string choice = opt.Choices[i];
                    string optId = opt.OptionId;
                    string cid = $"Controls_Opt_{optId}_{choice}";
                    cols[i] = (cid, choice, string.Equals(current, choice, StringComparison.OrdinalIgnoreCase), false, HostOverlayPalette.AccentCyan, _ => { }, null);
                    _actions.Add(new(cid, Rectangle.Empty, (_, cb) =>
                    {
                        _selectedChoices[optId] = choice;
                        cb?.OnControlOptionChanged(optId, choice);
                        return true;
                    }));
                }
                layout.AddColumns(controls, cols);
            }
            else
            {
                HostControlOptionDescriptor captured = opt;
                string cid = $"Controls_Opt_{opt.OptionId}";
                layout.AddPill(cid, $"{current} (Cycle)", true, HostOverlayPalette.AccentCyan, _ => { }, controls);
                _actions.Add(new(cid, Rectangle.Empty, (_, cb) =>
                {
                    string next = CycleOption(captured);
                    cb?.OnControlOptionChanged(captured.OptionId, next);
                    return true;
                }));
            }
        }
    }

    private void SyncRenderedBounds(List<DrawerControl> rendered)
    {
        var byId = new Dictionary<string, Rectangle>(StringComparer.OrdinalIgnoreCase);
        foreach (DrawerControl c in rendered)
        {
            byId[c.ControlId] = c.Bounds;
            _widgets.Add(new(c.ControlId, c.ControlType, c.Label, c.Bounds, true));
            if (c.ControlId.StartsWith("Controls_Scroll", StringComparison.OrdinalIgnoreCase) ||
                c.ControlId.StartsWith("ExtraRow_", StringComparison.OrdinalIgnoreCase))
            {
                DrawerControl captured = c;
                _actions.Add(new(c.ControlId, c.Bounds, (_, _) => { captured.OnClick(false); return true; }));
            }
        }
        for (int i = 0; i < _actions.Count; i++)
        {
            if (byId.TryGetValue(_actions[i].ControlId, out Rectangle r))
                _actions[i] = _actions[i] with { Bounds = r };
        }
    }

    private bool ApplyTier(HostInputMapper mapper, HostControlTierStore? tierStore, HostControlTierMode tier)
    {
        mapper.ActiveControlTier = tier;
        if (tierStore is not null) tierStore.ActiveTier = tier;
        _displayTier = tier;
        return true;
    }

    private bool StepSensitivity(HostInputMapper mapper, F1SettingsDrawerView? view, IHostSessionEventCallbacks? cb, double delta)
    {
        double current = view?.MouseSensitivity ?? mapper.MouseSensitivity;
        double next = Math.Clamp(Math.Round(current + delta, 2), 0.25, 3.0);
        mapper.MouseSensitivity = next;
        _displayMouseSens = next;
        if (view is not null) view.MouseSensitivity = next;
        cb?.OnMouseSensitivityChanged(next);
        return true;
    }

    private string ResolveChoice(HostControlOptionDescriptor opt)
    {
        if (_selectedChoices.TryGetValue(opt.OptionId, out string? existing) && !string.IsNullOrWhiteSpace(existing)) return existing;
        string initial = !string.IsNullOrWhiteSpace(opt.SelectedChoice) ? opt.SelectedChoice : opt.Choices[0];
        _selectedChoices[opt.OptionId] = initial;
        return initial;
    }

    private string CycleOption(HostControlOptionDescriptor opt)
    {
        if (opt.Choices.Count == 0) return string.Empty;
        string current = ResolveChoice(opt);
        int idx = 0;
        for (int i = 0; i < opt.Choices.Count; i++)
        {
            if (string.Equals(opt.Choices[i], current, StringComparison.OrdinalIgnoreCase)) { idx = (i + 1) % opt.Choices.Count; break; }
        }
        string next = opt.Choices[idx];
        _selectedChoices[opt.OptionId] = next;
        return next;
    }
}
