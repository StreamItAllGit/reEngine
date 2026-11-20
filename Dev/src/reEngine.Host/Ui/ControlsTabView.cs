// @module: reEngine.Host.Ui.ControlsTabView
// @purpose: Renders Schema A/B/C selector and dynamic module ControlOptions pills onto PixelOverlayCanvas with click/cycle and live callback support.
// @depends-on: System, System.Collections.Generic, System.Drawing, Foundation.Core, reEngine.Host.Contracts, reEngine.Host.Input
// @exports: ControlsTabView

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;
using reEngine.Host.Input;

namespace reEngine.Host.Ui;

/// <summary>
/// Renders and handles clicks for the active input schema selector and dynamic per-game <c>ControlOptions</c>.
/// </summary>
public sealed class ControlsTabView
{
    private sealed record ClickTarget(string ControlId, Rectangle Bounds, Func<IHostSessionEventCallbacks?, bool> OnActivate);

    private readonly List<ClickTarget> _targets = new();
    private readonly List<HostWidgetBounds> _widgets = new();
    private readonly Dictionary<string, string> _selectedChoices = new(StringComparer.OrdinalIgnoreCase);
    private HostInputSchemaKind? _pendingSchema;

    /// <summary>Gets the current selected choice per <c>optionId</c>.</summary>
    public IReadOnlyDictionary<string, string> SelectedChoices => _selectedChoices;

    /// <summary>Gets the bounds of the schema cycle button rendered inside the Controls tab.</summary>
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
        {
            _selectedChoices[optionId] = choice;
        }
    }

    /// <summary>Renders the schema selector and dynamic per-game control option pills into <paramref name="bodyRect"/>.</summary>
    public void Render(
        PixelOverlayCanvas canvas,
        Rectangle bodyRect,
        HostInputSchemaKind selectedSchema,
        IReadOnlyList<HostControlOptionDescriptor>? controlOptions)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        _targets.Clear();
        _widgets.Clear();

        canvas.DrawText(bodyRect.X, bodyRect.Y, "CONTROLLER SCHEMA", HostOverlayPalette.AccentCyan, 1);
        canvas.DrawText(bodyRect.X, bodyRect.Y + 18, $"Active Profile: Schema {HostInputSchemas.ToShortCode(selectedSchema)}", HostOverlayPalette.TextPrimary, 1);

        int gap = 6;
        int pillW = Math.Max(48, (bodyRect.Width - (gap * 2)) / 3);
        HostInputSchemaKind[] schemas = [HostInputSchemaKind.SchemaA, HostInputSchemaKind.SchemaB, HostInputSchemaKind.SchemaC];
        for (int i = 0; i < schemas.Length; i++)
        {
            HostInputSchemaKind schema = schemas[i];
            string code = HostInputSchemas.ToShortCode(schema);
            var rect = new Rectangle(bodyRect.X + (i * (pillW + gap)), bodyRect.Y + 36, pillW, 22);
            HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, $"Schema {code}", selectedSchema == schema, HostOverlayPalette.AccentCyan);
            RegisterTarget($"Controls_Schema_{code}", $"Schema {code}", rect, _ => { _pendingSchema = schema; return true; });
        }

        CycleSchemaButtonRect = new Rectangle(bodyRect.X, bodyRect.Y + 64, Math.Min(220, bodyRect.Width), 24);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, CycleSchemaButtonRect, "Cycle Schema (A/B/C)", true, HostOverlayPalette.AccentCyan);

        ContentBottomY = RenderControlOptions(canvas, bodyRect, bodyRect.Y + 98, controlOptions);
    }

    /// <summary>Returns interactive widget bounds produced by the most recent render pass.</summary>
    public IReadOnlyList<HostWidgetBounds> GetWidgets() => _widgets;

    /// <summary>Processes a pointer click on the Controls tab using <see cref="EventCallbacks"/>.</summary>
    public bool TryHandleClick(
        int x,
        int y,
        string? targetId,
        HostInputMapper inputMapper,
        ref HostInputSchemaKind selectedSchema) =>
        TryHandleClick(x, y, targetId, inputMapper, ref selectedSchema, EventCallbacks);

    /// <summary>Processes a pointer click on the Controls tab and notifies <paramref name="eventCallbacks"/> on option changes.</summary>
    public bool TryHandleClick(
        int x,
        int y,
        string? targetId,
        HostInputMapper inputMapper,
        ref HostInputSchemaKind selectedSchema,
        IHostSessionEventCallbacks? eventCallbacks)
    {
        ArgumentNullException.ThrowIfNull(inputMapper);
        _pendingSchema = null;

        foreach (ClickTarget target in _targets)
        {
            bool match = !string.IsNullOrEmpty(targetId)
                ? string.Equals(targetId, target.ControlId, StringComparison.OrdinalIgnoreCase)
                : target.Bounds.Contains(x, y);
            if (!match) continue;

            _ = target.OnActivate(eventCallbacks ?? EventCallbacks);
            if (_pendingSchema is { } nextSchema)
            {
                selectedSchema = nextSchema;
                inputMapper.ActiveSchema = nextSchema;
            }
            return true;
        }

        return false;
    }

    private int RenderControlOptions(
        PixelOverlayCanvas canvas,
        Rectangle bodyRect,
        int startY,
        IReadOnlyList<HostControlOptionDescriptor>? options)
    {
        if (options is null || options.Count == 0) return startY;

        int y = startY;
        canvas.DrawText(bodyRect.X, y, "GAME CONTROL OPTIONS", HostOverlayPalette.AccentCyan, 1);
        y += 16;

        foreach (HostControlOptionDescriptor opt in options)
        {
            if (opt.Choices.Count == 0 || y + 40 > bodyRect.Bottom) continue;
            string current = ResolveChoice(opt);
            canvas.DrawText(bodyRect.X, y, HostOverlayLayoutHelpers.FitText(opt.Label, bodyRect.Width, 1), HostOverlayPalette.TextPrimary, 1);
            y += 13;

            if (opt.Choices.Count <= 3)
            {
                int gap = 5;
                int cw = Math.Max(44, (bodyRect.Width - (gap * (opt.Choices.Count - 1))) / opt.Choices.Count);
                for (int i = 0; i < opt.Choices.Count; i++)
                {
                    string choice = opt.Choices[i];
                    var rect = new Rectangle(bodyRect.X + (i * (cw + gap)), y, cw, 22);
                    bool active = string.Equals(current, choice, StringComparison.OrdinalIgnoreCase);
                    HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, choice, active, HostOverlayPalette.AccentCyan);
                    string optId = opt.OptionId;
                    RegisterTarget($"Controls_Opt_{optId}_{choice}", choice, rect, cb =>
                    {
                        _selectedChoices[optId] = choice;
                        cb?.OnControlOptionChanged(optId, choice);
                        return true;
                    });
                }
            }
            else
            {
                var rect = new Rectangle(bodyRect.X, y, bodyRect.Width, 22);
                HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, $"{current} (Cycle)", true, HostOverlayPalette.AccentCyan);
                HostControlOptionDescriptor captured = opt;
                RegisterTarget($"Controls_Opt_{opt.OptionId}", current, rect, cb =>
                {
                    string next = CycleOption(captured);
                    cb?.OnControlOptionChanged(captured.OptionId, next);
                    return true;
                });
            }
            y += 28;
        }
        return y;
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

    private void RegisterTarget(string id, string label, Rectangle rect, Func<IHostSessionEventCallbacks?, bool> onActivate)
    {
        _targets.Add(new ClickTarget(id, rect, onActivate));
        _widgets.Add(new HostWidgetBounds(id, "Button", label, rect, true));
    }
}
