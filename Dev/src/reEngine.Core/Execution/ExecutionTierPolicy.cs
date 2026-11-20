// @module: reEngine.Core.Execution.ExecutionTierPolicy
// @purpose: Enforces Clean-Room AOT vs Tier 3 JIT rules, fail-closed raw-media boot checks, and strict CLI-only interpreterOracle quarantine.
// @depends-on: System, reEngine.Core.Updates
// @exports: ExecutionTierScope, ExecutionTierEvaluation, ExecutionTierPolicy

using reEngine.Core.Updates;

namespace reEngine.Core.Execution;

/// <summary>
/// Invocation context deciding whether quarantined CLI-only execution tiers may be activated.
/// </summary>
public enum ExecutionTierScope
{
    /// <summary>Persisted settings or interactive UI configuration (forbids <c>interpreterOracle</c>).</summary>
    PersistedSetting = 0,

    /// <summary>Explicit command-line diagnostic invocation (<c>--execution-tier interpreterOracle</c>).</summary>
    AllowCliOnly = 1
}

/// <summary>
/// Policy evaluation result describing effective execution tier mode and any clean-room warning or error.
/// </summary>
public sealed record ExecutionTierEvaluation
{
    /// <summary>Gets whether the requested configuration is permitted to execute or persist.</summary>
    public bool IsAllowed { get; init; }

    /// <summary>Gets whether Tier 3 JIT execution is effectively enabled.</summary>
    public bool IsJitEnabled { get; init; }

    /// <summary>Gets the canonical resolved Standard 18 execution tier token.</summary>
    public string EffectiveMode { get; init; } = ExecutionTierPolicy.StaticAotOnly;

    /// <summary>Gets whether quarantined Tier 4 <c>interpreterOracle</c> was selected via CLI quarantine.</summary>
    public bool IsInterpreterOracleQuarantined { get; init; }

    /// <summary>Gets any policy diagnostic or fail-closed denial explanation.</summary>
    public string? DiagnosticMessage { get; init; }
}

/// <summary>
/// Centralizes Clean-Room AOT and Tier 3 JIT execution tier validation rules per Standard R04.
/// </summary>
public static class ExecutionTierPolicy
{
    /// <summary>Pure static AOT mode with Tier 3 JIT disabled (<c>staticAotOnly</c>).</summary>
    public const string StaticAotOnly = "staticAotOnly";

    /// <summary>Primary AOT with Tier 3 RyuJIT fallback (<c>aotWithJitFallback</c>).</summary>
    public const string AotWithJitFallback = "aotWithJitFallback";

    /// <summary>Primary AOT with Zero-RWX Threaded-IR fallback (<c>aotWithThreadedIr</c>).</summary>
    public const string AotWithThreadedIr = "aotWithThreadedIr";

    /// <summary>Standalone Tier 3 JIT bring-up mode (<c>jitBringup</c>).</summary>
    public const string JitBringup = "jitBringup";

    /// <summary>Quarantined Tier 4 differential parity oracle (<c>interpreterOracle</c>; CLI-only).</summary>
    public const string InterpreterOracle = "interpreterOracle";

    /// <summary>
    /// Normalizes a raw or legacy execution tier string to its canonical Standard 18 token.
    /// </summary>
    /// <param name="rawMode">Raw tier mode string.</param>
    /// <param name="canonicalMode">Canonical Standard 18 token when recognized.</param>
    /// <returns><c>true</c> if recognized; otherwise <c>false</c>.</returns>
    public static bool TryNormalize(string? rawMode, out string canonicalMode)
    {
        canonicalMode = string.Empty;
        if (string.IsNullOrWhiteSpace(rawMode))
        {
            return false;
        }

        switch (rawMode.Trim().ToLowerInvariant())
        {
            case "staticaotonly":
            case "staticaot":
            case "static":
            case "aotprimary":
            case "aot":
                canonicalMode = StaticAotOnly;
                return true;
            case "aotwithjitfallback":
            case "jitfallback":
            case "jit":
                canonicalMode = AotWithJitFallback;
                return true;
            case "aotwiththreadedir":
            case "threadedir":
                canonicalMode = AotWithThreadedIr;
                return true;
            case "jitbringup":
            case "bringup":
                canonicalMode = JitBringup;
                return true;
            case "interpreteroracle":
            case "interpreter":
            case "oracle":
                canonicalMode = InterpreterOracle;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="rawMode"/> is strictly quarantined to CLI execution (<c>interpreterOracle</c>).
    /// </summary>
    /// <param name="rawMode">Execution tier token to check.</param>
    public static bool IsCliQuarantinedMode(string? rawMode) =>
        TryNormalize(rawMode, out string normalized) &&
        string.Equals(normalized, InterpreterOracle, StringComparison.Ordinal);

    /// <summary>
    /// Returns <c>true</c> if <paramref name="rawMode"/> is eligible to be persisted in settings.
    /// </summary>
    /// <param name="rawMode">Execution tier token to check.</param>
    public static bool IsPersistableMode(string? rawMode) =>
        TryNormalize(rawMode, out string normalized) &&
        !string.Equals(normalized, InterpreterOracle, StringComparison.Ordinal);

    /// <summary>
    /// Resolves a guaranteed persistable Standard 18 execution tier mode, enforcing <c>isJitEnabled == false -&gt; staticAotOnly</c>
    /// and blocking <c>interpreterOracle</c> from ever becoming a saved default.
    /// </summary>
    /// <param name="isJitEnabled">Whether Tier 3 runtime JIT is enabled.</param>
    /// <param name="requestedMode">Optional requested tier mode.</param>
    /// <returns>Sanitized persistable canonical execution tier token.</returns>
    public static string NormalizeForPersistence(bool isJitEnabled, string? requestedMode)
    {
        if (!isJitEnabled)
        {
            return StaticAotOnly;
        }

        if (TryNormalize(requestedMode, out string normalized) &&
            (string.Equals(normalized, AotWithJitFallback, StringComparison.Ordinal) ||
             string.Equals(normalized, AotWithThreadedIr, StringComparison.Ordinal) ||
             string.Equals(normalized, JitBringup, StringComparison.Ordinal)))
        {
            return normalized;
        }

        return AotWithJitFallback;
    }

    /// <summary>
    /// Applies persistence invariants directly to <paramref name="settings"/> in place.
    /// </summary>
    /// <param name="settings">Core settings object to normalize.</param>
    public static void EnforcePersistenceInvariants(ReEngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ExecutionTierMode = NormalizeForPersistence(settings.IsJitEnabled, settings.ExecutionTierMode);
    }

    /// <summary>
    /// Evaluates execution tier legality against Clean-Room AOT laws, raw-media boot requirements, and CLI quarantine rules.
    /// </summary>
    /// <param name="isJitEnabled">Whether Tier 3 JIT is enabled.</param>
    /// <param name="requestedMode">Requested execution tier token.</param>
    /// <param name="scope">Evaluation scope (<see cref="ExecutionTierScope.PersistedSetting"/> or <see cref="ExecutionTierScope.AllowCliOnly"/>).</param>
    /// <param name="isRawMediaBoot"><c>true</c> when launching arbitrary raw console media without a pre-lifted <c>.recomp</c> recipe.</param>
    /// <returns>Evaluation outcome detailing effective mode and legality.</returns>
    public static ExecutionTierEvaluation Evaluate(
        bool isJitEnabled,
        string? requestedMode,
        ExecutionTierScope scope = ExecutionTierScope.PersistedSetting,
        bool isRawMediaBoot = false)
    {
        bool normalizedOk = TryNormalize(requestedMode, out string normalized);
        if (normalizedOk && string.Equals(normalized, InterpreterOracle, StringComparison.Ordinal))
        {
            if (scope == ExecutionTierScope.AllowCliOnly)
            {
                return new ExecutionTierEvaluation
                {
                    IsAllowed = true,
                    IsJitEnabled = isJitEnabled,
                    EffectiveMode = InterpreterOracle,
                    IsInterpreterOracleQuarantined = true,
                    DiagnosticMessage = "[WARN] Tier 4 'interpreterOracle' is active under AllowCliOnly quarantine and will not be persisted."
                };
            }

            string safeFallback = NormalizeForPersistence(isJitEnabled, null);
            return new ExecutionTierEvaluation
            {
                IsAllowed = false,
                IsJitEnabled = isJitEnabled,
                EffectiveMode = safeFallback,
                IsInterpreterOracleQuarantined = false,
                DiagnosticMessage = "Execution tier 'interpreterOracle' is strictly CLI-quarantined (AllowCliOnly) and cannot be persisted as default."
            };
        }

        if (!isJitEnabled)
        {
            if (isRawMediaBoot)
            {
                return new ExecutionTierEvaluation
                {
                    IsAllowed = false,
                    IsJitEnabled = false,
                    EffectiveMode = StaticAotOnly,
                    DiagnosticMessage = "Fail-closed Clean-Room AOT violation: raw-media boot requires Tier 3 JIT (isJitEnabled == true) and cannot launch under staticAotOnly."
                };
            }

            return new ExecutionTierEvaluation
            {
                IsAllowed = true,
                IsJitEnabled = false,
                EffectiveMode = StaticAotOnly
            };
        }

        string effective = NormalizeForPersistence(isJitEnabled: true, requestedMode);
        if (normalizedOk && string.Equals(normalized, StaticAotOnly, StringComparison.Ordinal) && isRawMediaBoot)
        {
            return new ExecutionTierEvaluation
            {
                IsAllowed = false,
                IsJitEnabled = false,
                EffectiveMode = StaticAotOnly,
                DiagnosticMessage = "Fail-closed Clean-Room AOT violation: staticAotOnly cannot boot unlifted raw media without JIT enabled."
            };
        }

        return new ExecutionTierEvaluation
        {
            IsAllowed = true,
            IsJitEnabled = true,
            EffectiveMode = effective
        };
    }

    /// <summary>
    /// Formats a concise UI label for an execution tier mode.
    /// </summary>
    /// <param name="mode">Execution tier token.</param>
    /// <param name="isJitEnabled">Whether JIT is enabled.</param>
    /// <returns>Display-friendly label.</returns>
    public static string ToDisplayLabel(string? mode, bool isJitEnabled)
    {
        if (IsCliQuarantinedMode(mode))
        {
            return "Interpreter Oracle (CLI)";
        }

        if (!isJitEnabled)
        {
            return "AOT Only (Pure Static)";
        }

        return NormalizeForPersistence(true, mode) switch
        {
            AotWithThreadedIr => "AOT + Threaded IR",
            JitBringup => "JIT Bring-Up",
            _ => "AOT + JIT Fallback"
        };
    }
}
