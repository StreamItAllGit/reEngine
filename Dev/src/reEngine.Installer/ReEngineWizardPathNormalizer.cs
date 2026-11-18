// @module: reEngine.Installer.ReEngineWizardPathNormalizer
// @purpose: Live Setup Wizard folder auto-sorting into <parent>\reEngine and companion engines\ root resolution.
// @depends-on: System, System.IO, System.Linq, System.Windows.Forms, Foundation.Installer
// @exports: ReEngineWizardPathNormalizer

using System.Windows.Forms;
using Foundation.Installer;

namespace reEngine.Installer;

/// <summary>
/// Normalizes interactive Setup Wizard directory selections so choosing a generic parent folder
/// (for example <c>&lt;custom-dir&gt;\Misc</c>) sorts <c>reEngine</c> into <c>&lt;parent&gt;\reEngine</c>
/// and keeps companion <c>engines\</c> nested cleanly under <c>&lt;reEngineDir&gt;\engines\</c> unless
/// an umbrella <c>reEngine\</c> suite root already exists.
/// </summary>
internal static class ReEngineWizardPathNormalizer
{
    public static string NormalizeWizardReEngineDir(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        string normalized = NormalizeTrimmedFullPath(rawPath);
        string leaf = Path.GetFileName(normalized);
        if (string.Equals(leaf, "reEngine", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (File.Exists(Path.Combine(normalized, "reEngine.exe")) ||
            File.Exists(Path.Combine(normalized, "app", "reEngine.exe")) ||
            File.Exists(Path.Combine(normalized, "reEngine.Core.dll")) ||
            Directory.Exists(Path.Combine(normalized, "engines")))
        {
            return normalized;
        }

        return Path.Combine(normalized, "reEngine");
    }

    public static string ResolvePreferredSuiteInstallDir(string reEngineInstallDir, string? explicitSuiteDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitSuiteDir))
        {
            return NormalizeTrimmedFullPath(explicitSuiteDir);
        }

        string normalized = NormalizeTrimmedFullPath(reEngineInstallDir);
        DirectoryInfo? parent = Directory.GetParent(normalized);
        if (parent is not null &&
            !string.Equals(parent.FullName, Path.GetPathRoot(parent.FullName), StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(parent.Name, "reEngine", StringComparison.OrdinalIgnoreCase) ||
                Directory.Exists(Path.Combine(parent.FullName, "engines")) ||
                Directory.Exists(Path.Combine(parent.FullName, "toolchain")))
            {
                return parent.FullName;
            }
        }

        return normalized;
    }

    public static string ResolvePreferredEnginesRootDir(string reEngineInstallDir, string? explicitSuiteDir = null)
    {
        string suiteRoot = ResolvePreferredSuiteInstallDir(reEngineInstallDir, explicitSuiteDir);
        return Path.Combine(suiteRoot, "engines");
    }

    public static void WireWizardAutoSortHandlers(InstallerWizardForm wizard)
    {
        DisableLabelMnemonics(wizard);

        bool updating = false;
        bool userCustomizedSuiteDir = false;
        bool userCustomizedProfile = false;
        string lastSyncedSuite = NormalizeTrimmedFullPath(wizard.SecondaryDirTextBox.Text);

        wizard.LightweightProfileRadioButton.Click += (_, _) => userCustomizedProfile = true;
        wizard.FullProfileRadioButton.Click += (_, _) => userCustomizedProfile = true;

        void ApplyInstallAutoSort()
        {
            if (updating)
            {
                return;
            }

            string current = wizard.InstallDirTextBox.Text;
            string sorted = NormalizeWizardReEngineDir(current);
            if (string.IsNullOrWhiteSpace(sorted))
            {
                return;
            }

            updating = true;
            try
            {
                if (!string.Equals(current.Trim(), sorted, StringComparison.Ordinal))
                {
                    wizard.InstallDirTextBox.Text = sorted;
                    wizard.InstallDirTextBox.SelectionStart = sorted.Length;
                }

                LegacyMigrationHelper.TryReadExistingInstallSettings(
                    sorted, out string? savedProfile, out string? savedSuiteDir, out _);
                if (!userCustomizedProfile && !string.IsNullOrWhiteSpace(savedProfile))
                {
                    wizard.FullProfileRadioButton.Checked = string.Equals(savedProfile, "full", StringComparison.OrdinalIgnoreCase);
                    wizard.LightweightProfileRadioButton.Checked = !wizard.FullProfileRadioButton.Checked;
                }

                string currentSuite = NormalizeTrimmedFullPath(wizard.SecondaryDirTextBox.Text);
                if (!userCustomizedSuiteDir ||
                    string.IsNullOrWhiteSpace(currentSuite) ||
                    string.Equals(currentSuite, lastSyncedSuite, StringComparison.OrdinalIgnoreCase))
                {
                    string preferredSuite = ResolvePreferredSuiteInstallDir(sorted, savedSuiteDir);
                    if (!string.IsNullOrWhiteSpace(savedSuiteDir))
                    {
                        preferredSuite = NormalizeTrimmedFullPath(savedSuiteDir);
                    }
                    if (!string.Equals(currentSuite, preferredSuite, StringComparison.Ordinal))
                    {
                        wizard.SecondaryDirTextBox.Text = preferredSuite;
                        wizard.SecondaryDirTextBox.SelectionStart = preferredSuite.Length;
                    }

                    lastSyncedSuite = preferredSuite;
                }
            }
            finally
            {
                updating = false;
            }
        }

        void MarkSecondaryCustomizedIfChanged()
        {
            if (updating)
            {
                return;
            }

            string currentSuite = NormalizeTrimmedFullPath(wizard.SecondaryDirTextBox.Text);
            if (!string.Equals(currentSuite, lastSyncedSuite, StringComparison.OrdinalIgnoreCase))
            {
                userCustomizedSuiteDir = true;
            }
        }

        wizard.BrowseButton.Click += (_, _) => ApplyInstallAutoSort();
        wizard.InstallDirTextBox.Leave += (_, _) => ApplyInstallAutoSort();
        wizard.InstallDirTextBox.Validating += (_, _) => ApplyInstallAutoSort();
        wizard.InstallDirTextBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyInstallAutoSort();
            }
        };

        Button? secondaryBrowse = wizard.Controls
            .OfType<Button>()
            .FirstOrDefault(b => b != wizard.BrowseButton && b.Text.StartsWith("Browse", StringComparison.OrdinalIgnoreCase));
        if (secondaryBrowse is not null)
        {
            secondaryBrowse.Click += (_, _) => MarkSecondaryCustomizedIfChanged();
        }

        wizard.SecondaryDirTextBox.Leave += (_, _) => MarkSecondaryCustomizedIfChanged();
        wizard.SecondaryDirTextBox.Validating += (_, _) => MarkSecondaryCustomizedIfChanged();
        wizard.SecondaryDirTextBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                MarkSecondaryCustomizedIfChanged();
            }
        };

        wizard.PrimaryButton.MouseDown += (_, _) => ApplyInstallAutoSort();
        wizard.PrimaryButton.Enter += (_, _) => ApplyInstallAutoSort();
    }

    private static void DisableLabelMnemonics(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Label lbl) lbl.UseMnemonic = false;
            else if (child is ButtonBase btn) btn.UseMnemonic = false;
            if (child.HasChildren) DisableLabelMnemonics(child);
        }
    }

    private static string NormalizeTrimmedFullPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        string trimmed = rawPath.Trim();
        try
        {
            string full = Path.GetFullPath(trimmed);
            string? root = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(root) && string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                return full;
            }

            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
