// @module: reEngine.App.Ui.ReEngineTheme
// @purpose: Standard 09 Dark Slate WinForms colors and pill/button factory helpers for reEngine.App shelves and drawer.
// @depends-on: System, System.Drawing, System.Windows.Forms
// @exports: ReEngineTheme

using System.Drawing;
using System.Windows.Forms;

namespace reEngine.App.Ui;

internal static class ReEngineTheme
{
    public static readonly Color Surface0 = Color.FromArgb(14, 14, 16);
    public static readonly Color Surface1 = Color.FromArgb(18, 18, 20);
    public static readonly Color Surface2 = Color.FromArgb(24, 24, 27);
    public static readonly Color Surface3 = Color.FromArgb(39, 39, 42);
    public static readonly Color BorderDefault = Color.FromArgb(63, 63, 70);
    public static readonly Color TextPrimary = Color.FromArgb(244, 244, 245);
    public static readonly Color TextMuted = Color.FromArgb(161, 161, 170);
    public static readonly Color AccentCyan = Color.FromArgb(56, 189, 248);
    public static readonly Color AccentSuccess = Color.FromArgb(16, 185, 129);
    public static readonly Color AccentWarning = Color.FromArgb(245, 158, 11);
    public static readonly Color AccentDanger = Color.FromArgb(239, 68, 68);

    public static Button CreatePillButton(string name, string text, bool active)
    {
        var btn = new Button
        {
            Name = name,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseMnemonic = false
        };
        StylePill(btn, active);
        return btn;
    }

    public static void StylePill(Button btn, bool active)
    {
        btn.BackColor = active ? AccentCyan : Surface3;
        btn.ForeColor = active ? Surface0 : TextPrimary;
        btn.FlatAppearance.BorderColor = active ? AccentCyan : BorderDefault;
    }

    public static Button CreateActionButton(string name, string text, Color back, Color fore)
    {
        var btn = new Button
        {
            Name = name,
            Text = text,
            BackColor = back,
            ForeColor = fore,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseMnemonic = false
        };
        btn.FlatAppearance.BorderColor = BorderDefault;
        return btn;
    }
}
