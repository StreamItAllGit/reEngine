// @module: reEngine.Tests.InstallerWizardAutoSortTests
// @purpose: xUnit regression tests for live Setup Wizard parent-folder auto-sorting into <parent>\reEngine and companion engines\ suite resolution.
// @depends-on: System, System.IO, Foundation.Installer, Xunit, reEngine.Installer
// @exports: InstallerWizardAutoSortTests

using System.Reflection;
using System.Windows.Forms;
using Foundation.Installer;
using reEngine.Installer;
using Xunit;

namespace reEngine.Tests;

public sealed class InstallerWizardAutoSortTests : IDisposable
{
    private readonly string _tempRoot;

    public InstallerWizardAutoSortTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_wizard_autosort_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch { /* ignore transient cleanup */ }
    }

    [Fact]
    public void NormalizeWizardReEngineDir_SortsParentIntoReEngineWithoutDoubleNesting()
    {
        string miscParent = Path.Combine(_tempRoot, "Misc");
        string sorted = ReEngineWizardPathNormalizer.NormalizeWizardReEngineDir(miscParent);
        Assert.Equal(Path.Combine(miscParent, "reEngine"), sorted);
        Assert.Equal(sorted, ReEngineWizardPathNormalizer.NormalizeWizardReEngineDir(sorted));

        string portableDir = Path.Combine(_tempRoot, "PortableRouter");
        Directory.CreateDirectory(portableDir);
        File.WriteAllBytes(Path.Combine(portableDir, "reEngine.exe"), Array.Empty<byte>());
        Assert.Equal(portableDir, ReEngineWizardPathNormalizer.NormalizeWizardReEngineDir(portableDir));
    }

    [Fact]
    public void ResolvePreferredSuiteAndEnginesDir_UsesInternalEnginesForMiscAndSiblingForUmbrella()
    {
        string miscReEngine = Path.Combine(_tempRoot, "Misc", "reEngine");
        Assert.Equal(miscReEngine, ReEngineWizardPathNormalizer.ResolvePreferredSuiteInstallDir(miscReEngine));
        Assert.Equal(
            Path.Combine(miscReEngine, "engines"),
            ReEngineWizardPathNormalizer.ResolvePreferredEnginesRootDir(miscReEngine));

        string umbrellaReEngine = Path.Combine(_tempRoot, "StreamItAll", "reEngine", "reEngine");
        string umbrellaRoot = Path.Combine(_tempRoot, "StreamItAll", "reEngine");
        Assert.Equal(umbrellaRoot, ReEngineWizardPathNormalizer.ResolvePreferredSuiteInstallDir(umbrellaReEngine));
        Assert.Equal(
            Path.Combine(umbrellaRoot, "engines"),
            ReEngineWizardPathNormalizer.ResolvePreferredEnginesRootDir(umbrellaReEngine));

        string customParent = Path.Combine(_tempRoot, "ExistingParent");
        Directory.CreateDirectory(Path.Combine(customParent, "engines"));
        string siblingReEngine = Path.Combine(customParent, "reEngine");
        Assert.Equal(customParent, ReEngineWizardPathNormalizer.ResolvePreferredSuiteInstallDir(siblingReEngine));
        Assert.Equal(
            Path.Combine(customParent, "engines"),
            ReEngineWizardPathNormalizer.ResolvePreferredEnginesRootDir(siblingReEngine));
    }

    [Fact]
    public void WireWizardAutoSortHandlers_UpdatesInstallDirAndSyncsSuitePicker()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                InstallerThemeOptions theme = reEngine.Installer.Program.CreateThemeOptions();
                InstallerAppDescriptor descriptor = reEngine.Installer.Program.CreateDescriptor();
                var ctx = new InstallerRuntimeContext(
                    cliDataDir: Path.Combine(_tempRoot, "data"),
                    cliConfigPath: null,
                    cliLogDir: Path.Combine(_tempRoot, "logs"),
                    dryRun: true,
                    envPrefix: descriptor.EnvPrefix);
                var engine = new InstallerEngine(descriptor, ctx, typeof(reEngine.Installer.Program).Assembly);

                using var wizard = new InstallerWizardForm(
                    engine: engine,
                    ctx: ctx,
                    initialInstallDir: theme.DefaultInstallDir,
                    externalPayloadPath: null,
                    isUninstallMode: false,
                    initialCreateShortcuts: false,
                    initialAutoStart: false,
                    initialLaunchAfter: false,
                    initialPurgeData: false,
                    initialProfileId: theme.DefaultProfileId,
                    initialSecondaryDir: theme.SecondaryPathPicker?.DefaultPath,
                    secondaryFlagName: theme.SecondaryPathPicker?.FlagName,
                    initialMigrateLegacy: false);

                ReEngineWizardPathNormalizer.WireWizardAutoSortHandlers(wizard);
                string miscParent = Path.Combine(_tempRoot, "Misc");
                wizard.InstallDirTextBox.Text = miscParent;
                typeof(Control).GetMethod("OnLeave", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(wizard.InstallDirTextBox, [EventArgs.Empty]);

                string expectedReEngine = Path.Combine(miscParent, "reEngine");
                Assert.Equal(expectedReEngine, wizard.InstallDirTextBox.Text);
                Assert.Equal(expectedReEngine, wizard.SecondaryDirTextBox.Text);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
        {
            throw threadEx;
        }
    }
}
