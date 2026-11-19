// @module: reEngine.Host.Ui.ControlsTabView
// @purpose: Renders Schema A/B/C selector and dynamic module ControlOptions pills onto PixelOverlayCanvas with click/cycle support.
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
    private sealed record ClickTarget(string ControlId, Rectangle Bounds, Action OnActivate);

    private readonly List<ClickTarget> _targets = new();
    private readonly List<HostWidgetBounds> _widgets = new();
    private readonly Dictionary<string, string> _selectedChoices = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> SelectedChoices => _selectedChoices;
    public Rectangle CycleSchemaButtonRect { get; private set; }

    public string? GetSelectedChoice(string optionId) =>
        _selectedChoices.TryGetValue(optionId, out string? choice) ? choice : null;

    public void SetSelectedChoice(string optionId, string choice)
    {
        if (!string.IsNullOrWhiteSpace(optionId) && !string.IsNullOrWhiteSpace(choice))
        {
            _selectedChoices[optionId] = choice;
        }
    }

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
            RegisterTarget($"Controls_Schema_{code}", $"Schema {code}", rect, () => _pendingSchema = schema);
        }

        CycleSchemaButtonRect = new Rectangle(bodyRect.X, bodyRect.Y + 64, Math.Min(220, bodyRect.Width), 24);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, CycleSchemaButtonRect, "Cycle Schema (A/B/C)", true, HostOverlayPalette.AccentCyan);

        RenderControlOptions(canvas, bodyRect, bodyRect.Y + 98, controlOptions);
    }

    private HostInputSchemaKind? _pendingSchema;

    private void RenderControlOptions(
        PixelOverlayCanvas canvas,
        Rectangle bodyRect,
        int startY,
        IReadOnlyList<HostControlOptionDescriptor>? options)
    {
        if (options is null || options.Count == 0) return;

        int y = startY;
        canvas.DrawText(bodyRect.X, y, "GAME CONTROL OPTIONS", HostOverlayPalette.AccentCyan, 1);
        y += 16;

        foreach (HostControlOptionDescriptor opt in options)
        {
            if (opt.Choices.Count == 0 || y + 40 > bodyRect.Bottom) continue;
            string current = ResolveChoice(opt);
            string label = HostOverlayLayoutHelpers.FitText(opt.Label, bodyRect.Width, 1);
            canvas.DrawText(bodyRect.X, y, label, HostOverlayPalette.TextPrimary, 1);
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
                    RegisterTarget($"Controls_Opt_{optId}_{choice}", choice, rect, () => _selectedChoices[optId] = choice);
                }
            }
            else
            {
                var rect = new Rectangle(bodyRect.X, y, bodyRect.Width, 22);
                HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, $"{current} (Cycle)", true, HostOverlayPalette.AccentCyan);
                HostControlOptionDescriptor captured = opt;
                RegisterTarget($"Controls_Opt_{opt.OptionId}", current, rect, () => CycleOption(captured));
            }

            y += 28;
        }
    }

    public IReadOnlyList<HostWidgetBounds> GetWidgets() => _widgets;

    public bool TryHandleClick(
        int x,
        int y,
        string? targetId,
        HostInputMapper inputMapper,
        ref HostInputSchemaKind selectedSchema)
    {
        ArgumentNullException.ThrowIfNull(inputMapper);
        _pendingSchema = null;

        foreach (ClickTarget target in _targets)
        {
            bool match = !string.IsNullOrEmpty(targetId)
                ? string.Equals(targetId, target.ControlId, StringComparison.OrdinalIgnoreCase)
                : target.Bounds.Contains(x, y);
            if (!match) continue;

            target.OnActivate();
            if (_pendingSchema is { } nextSchema)
            {
                selectedSchema = nextSchema;
                inputMapper.ActiveSchema = nextSchema;
            }
            return true;
        }

        return false;
    }

    private string ResolveChoice(HostControlOptionDescriptor opt)
    {
        if (_selectedChoices.TryGetValue(opt.OptionId, out string? existing) && !string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }
        string initial = !string.IsNullOrWhiteSpace(opt.SelectedChoice) ? opt.SelectedChoice : opt.Choices[0];
        _selectedChoices[opt.OptionId] = initial;
        return initial;
    }

    private void CycleOption(HostControlOptionDescriptor opt)
    {
        if (opt.Choices.Count == 0) return;
        string current = ResolveChoice(opt);
        int idx = 0;
        for (int i = 0; i < opt.Choices.Count; i++)
        {
            if (string.Equals(opt.Choices[i], current, StringComparison.OrdinalIgnoreCase))
            {
                idx = (i + 1) % opt.Choices.Count;
                break;
            }
        }
        _selectedChoices[opt.OptionId] = opt.Choices[idx];
    }

    private void RegisterTarget(string id, string label, Rectangle rect, Action onActivate)
    {
        _targets.Add(new ClickTarget(id, rect, onActivate));
        _widgets.Add(new HostWidgetBounds(id, "Button", label, rect, true));
    }
}
