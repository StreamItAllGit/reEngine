// @module: reEngine.Host.Ui.KeybindsTabView
// @purpose: Renders Schema A/B/C keybind tables directly onto Foundation.Core.PixelOverlayCanvas inside the F1 drawer.
// @depends-on: System, System.Collections.Generic, System.Drawing, Foundation.Core, reEngine.Host.Input
// @exports: KeybindsTabView

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Input;

namespace reEngine.Host.Ui;

internal sealed class KeybindsTabView
{
    public void Render(PixelOverlayCanvas canvas, Rectangle bodyRect, HostInputSchemaKind schema)
    {
        canvas.DrawText(bodyRect.X, bodyRect.Y, $"ACTIVE INPUT SCHEMA: {HostInputSchemas.ToShortCode(schema)}", HostOverlayPalette.AccentCyan, 1);
        IReadOnlyList<HostKeybindEntry> rows = HostInputSchemas.GetBindings(schema);
        int y = bodyRect.Y + 20;

        foreach (HostKeybindEntry row in rows)
        {
            if (y + 34 > bodyRect.Bottom) break;
            var card = new Rectangle(bodyRect.X, y, bodyRect.Width, 28);
            HostOverlayLayoutHelpers.DrawPanel(canvas, card, HostOverlayPalette.Surface3Glass, HostOverlayPalette.Border);
            canvas.DrawText(card.X + 8, card.Y + 5, row.ActionName, HostOverlayPalette.TextPrimary, 1);
            canvas.DrawText(card.X + 8, card.Y + 16, $"{row.PrimaryKey} | {row.AlternateInput}", HostOverlayPalette.TextMuted, 1);
            y += 34;
        }
    }
}
