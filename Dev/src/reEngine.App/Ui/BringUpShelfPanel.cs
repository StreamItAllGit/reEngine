// @module: reEngine.App.Ui.BringUpShelfPanel
// @purpose: Decompilation toolchain readiness and local AOT cache status shelf.
// @depends-on: System, System.Drawing, System.Windows.Forms, reEngine.App
// @exports: BringUpShelfPanel

using System.Drawing;
using System.Windows.Forms;

namespace reEngine.App.Ui;

internal sealed class BringUpShelfPanel : Panel
{
    public Label HeaderLabel { get; }
    public Label ToolchainStatusLabel { get; }
    public Label AotCacheLabel { get; }
    public Label D4GuardLabel { get; }

    public BringUpShelfPanel()
    {
        BackColor = ReEngineTheme.Surface1;
        Name = "Shelf_BringUp";

        HeaderLabel = new Label
        {
            Name = "BringUp_Header",
            Text = "Decompilation Toolchain & AOT Cache Readiness",
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            ForeColor = ReEngineTheme.TextPrimary,
            AutoSize = true,
            UseMnemonic = false
        };

        ToolchainStatusLabel = new Label
        {
            Name = "BringUp_ToolchainLabel",
            Text = "Decompilation Toolchain: Router mode (Lightweight profile)",
            ForeColor = ReEngineTheme.TextPrimary,
            AutoEllipsis = true,
            UseMnemonic = false
        };

        AotCacheLabel = new Label
        {
            Name = "BringUp_AotCacheLabel",
            Text = "AOT Cache: 0 precompiled module(s) cached.",
            ForeColor = ReEngineTheme.AccentCyan,
            AutoEllipsis = true,
            UseMnemonic = false
        };

        D4GuardLabel = new Label
        {
            Name = "BringUp_GuidanceLabel",
            Text = "To decompile a .decomp package, install the Full offline toolchain or run 'reEngine decomp --module <file.decomp>'. Precompiled AOT modules are cached automatically under data\\cache\\.",
            ForeColor = ReEngineTheme.TextMuted,
            UseMnemonic = false
        };

        Controls.AddRange(new Control[] { HeaderLabel, ToolchainStatusLabel, AotCacheLabel, D4GuardLabel });
        Resize += (_, _) => LayoutChildren();
        LayoutChildren();
    }

    public void UpdateSummary(DecompDispatchStatusSummary decomp, AotCacheStatusSummary aot)
    {
        ArgumentNullException.ThrowIfNull(decomp);
        ArgumentNullException.ThrowIfNull(aot);

        ToolchainStatusLabel.Text = decomp.ToolchainAvailable
            ? $"Decompilation Toolchain: Offline CLI Toolchain Ready (Full profile, {decomp.InstalledEngineCount} engine(s)) — {decomp.ToolchainDirectory}"
            : $"Decompilation Toolchain: Router mode ({decomp.InstallProfile} profile, {decomp.InstalledEngineCount} engine(s))";
        ToolchainStatusLabel.ForeColor = decomp.ToolchainAvailable ? ReEngineTheme.AccentSuccess : ReEngineTheme.TextPrimary;

        double mb = aot.TotalBytes / (1024.0 * 1024.0);
        AotCacheLabel.Text = $"Precompiled AOT Cache: {aot.AssemblyCount} module(s) cached ({mb:0.00} MB) in {aot.CacheDirectory}";
    }

    private void LayoutChildren()
    {
        int w = Math.Max(320, Width);
        HeaderLabel.Location = new Point(16, 16);
        ToolchainStatusLabel.Bounds = new Rectangle(16, 52, w - 32, 24);
        AotCacheLabel.Bounds = new Rectangle(16, 84, w - 32, 24);
        D4GuardLabel.Bounds = new Rectangle(16, 120, w - 32, 54);
    }
}
