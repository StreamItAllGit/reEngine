// @module: reEngine.App.Program.Bootstrap
// @purpose: Resolves canonical install root data\config\settings.json and logs\ defaults before AppEnvironment construction (Bug #2 fix).
// @depends-on: System, System.Collections.Generic, System.IO, Foundation.Core
// @exports: ProgramBootstrapHelper

using Foundation.Core;

namespace reEngine.App;

internal static class ProgramBootstrapHelper
{
    public static string[] NormalizeBootstrapArgs(string[]? rawArgs, string? installRootOverride = null)
    {
        var result = new List<string>(rawArgs ?? Array.Empty<string>());
        bool isIsolated = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APP_ISOLATED_TEST")) ||
                          !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOUNDATION_SANDBOX_ROOT"));
        bool hasDataEnv = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REENGINE_DATA_DIR")) ||
                          !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DATA_DIR")) ||
                          !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APP_HOME"));

        string installRoot = !string.IsNullOrWhiteSpace(installRootOverride)
            ? Path.GetFullPath(installRootOverride)
            : ReEngineAppStatusHelper.ResolveAppInstallRoot();

        bool hasDataArg = HasOption(result, "--data-dir");
        bool hasConfigArg = HasOption(result, "--config");
        bool hasLogArg = HasOption(result, "--log-dir");

        if (!isIsolated && !hasDataEnv)
        {
            string defaultDataDir = Path.Combine(installRoot, "data");
            if (!hasDataArg)
            {
                result.Add("--data-dir");
                result.Add(defaultDataDir);
            }
            if (!hasConfigArg)
            {
                string explicitData = RecompLaunchOptions.GetOptionValue(result.ToArray(), "--data-dir") ?? defaultDataDir;
                result.Add("--config");
                result.Add(Path.Combine(Path.GetFullPath(explicitData), "config", "settings.json"));
            }
            if (!hasLogArg)
            {
                result.Add("--log-dir");
                result.Add(Path.Combine(installRoot, "logs"));
            }
        }
        else if (!hasConfigArg && (hasDataArg || hasDataEnv))
        {
            string? explicitData = RecompLaunchOptions.GetOptionValue(result.ToArray(), "--data-dir")
                ?? Environment.GetEnvironmentVariable("REENGINE_DATA_DIR")
                ?? Environment.GetEnvironmentVariable("DATA_DIR")
                ?? Environment.GetEnvironmentVariable("APP_HOME");
            if (!string.IsNullOrWhiteSpace(explicitData))
            {
                string canonicalConfig = Path.Combine(Path.GetFullPath(explicitData), "config", "settings.json");
                string legacyConfig = Path.Combine(Path.GetFullPath(explicitData), "settings.json");
                if (File.Exists(canonicalConfig) || !File.Exists(legacyConfig))
                {
                    result.Add("--config");
                    result.Add(canonicalConfig);
                }
            }
        }

        return result.ToArray();
    }

    private static bool HasOption(IReadOnlyList<string> args, string optionName) =>
        args.Any(a => string.Equals(a, optionName, StringComparison.OrdinalIgnoreCase) ||
                      a.StartsWith(optionName + "=", StringComparison.OrdinalIgnoreCase));
}
