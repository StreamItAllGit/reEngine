// @module: reEngine.Core.Associations.FileAssociationStores
// @purpose: File association abstraction with live HKCU Windows Registry store and fail-closed Sandbox virtual_registry.json store.
// @depends-on: System, System.IO, System.Text, System.Text.Json, Microsoft.Win32, Foundation.Core, reEngine.Core.Bootstrap
// @exports: FileAssociationRecord, IFileAssociationStore, VirtualRegistryAssociationStore, WindowsRegistryAssociationStore

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundation.Core;
using Microsoft.Win32;
using reEngine.Core.Bootstrap;

namespace reEngine.Core.Associations;

/// <summary>
/// Persisted shell association metadata for <c>.recomp</c> packages.
/// </summary>
public sealed record FileAssociationRecord(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("extension")] string Extension,
    [property: JsonPropertyName("progId")] string ProgId,
    [property: JsonPropertyName("commandTemplate")] string CommandTemplate,
    [property: JsonPropertyName("iconPath")] string IconPath,
    [property: JsonPropertyName("updatedAtUtc")] string UpdatedAtUtc);

/// <summary>
/// Abstracts <c>.recomp</c> file association registration across live HKCU and sandbox virtual storage.
/// </summary>
public interface IFileAssociationStore
{
    /// <summary>Registers the <c>.recomp</c> open handler pointing to <paramref name="executablePath"/>.</summary>
    FileAssociationRecord RegisterRecompAssociation(string executablePath, string? iconPath = null);

    /// <summary>Reads the currently registered <c>.recomp</c> association, or returns <see langword="null"/>.</summary>
    FileAssociationRecord? ReadRecompAssociation();

    /// <summary>Removes the <c>.recomp</c> association if present.</summary>
    bool UnregisterRecompAssociation();
}

/// <summary>
/// Fail-closed sandboxed association store persisting records atomically to <c>&lt;dataDir&gt;\virtual_registry.json</c>.
/// </summary>
public sealed class VirtualRegistryAssociationStore : IFileAssociationStore
{
    private const string RecompExtension = ".recomp";
    private const string RecompProgId = "reEngine.RecompModule";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _registryFilePath;

    /// <summary>Gets the resolved path to <c>virtual_registry.json</c>.</summary>
    public string RegistryFilePath => _registryFilePath;

    /// <summary>
    /// Initializes a virtual association store strictly contained inside <paramref name="allowedRootDir"/>.
    /// </summary>
    public VirtualRegistryAssociationStore(string dataDir, string? allowedRootDir = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDir);
        string root = string.IsNullOrWhiteSpace(allowedRootDir) ? dataDir : allowedRootDir;
        string containedDataDir = SandboxEnvironment.AssertPathContained(dataDir, root);
        Directory.CreateDirectory(containedDataDir);
        string candidateFile = Path.Combine(containedDataDir, ReEnginePackagingConstants.VirtualRegistryFileName);
        _registryFilePath = SandboxEnvironment.AssertPathContained(candidateFile, root);
    }

    /// <inheritdoc />
    public FileAssociationRecord RegisterRecompAssociation(string executablePath, string? iconPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        string fullExe = Path.GetFullPath(executablePath);
        string resolvedIcon = string.IsNullOrWhiteSpace(iconPath) ? $"{fullExe},0" : Path.GetFullPath(iconPath);
        var record = new FileAssociationRecord(
            SchemaVersion: 1,
            Extension: RecompExtension,
            ProgId: RecompProgId,
            CommandTemplate: $"\"{fullExe}\" --open \"%1\"",
            IconPath: resolvedIcon,
            UpdatedAtUtc: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

        WriteAtomically(record);
        return record;
    }

    /// <inheritdoc />
    public FileAssociationRecord? ReadRecompAssociation()
    {
        if (!File.Exists(_registryFilePath))
        {
            return null;
        }
        string raw = File.ReadAllText(_registryFilePath, Encoding.UTF8);
        return string.IsNullOrWhiteSpace(raw) ? null : JsonSerializer.Deserialize<FileAssociationRecord>(raw, JsonOptions);
    }

    /// <inheritdoc />
    public bool UnregisterRecompAssociation()
    {
        if (!File.Exists(_registryFilePath))
        {
            return false;
        }
        File.Delete(_registryFilePath);
        return true;
    }

    private void WriteAtomically(FileAssociationRecord record)
    {
        string tempPath = $"{_registryFilePath}.{Guid.NewGuid():N}.tmp";
        string bakPath = _registryFilePath + ".bak";
        string json = JsonSerializer.Serialize(record, JsonOptions);

        try
        {
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(_registryFilePath))
            {
                File.Copy(_registryFilePath, bakPath, overwrite: true);
            }
            File.Move(tempPath, _registryFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore cleanup failure */ }
            }
        }
    }
}

/// <summary>
/// Live per-user Windows Registry association store writing strictly under <c>HKCU\Software\Classes</c>.
/// </summary>
public sealed class WindowsRegistryAssociationStore : IFileAssociationStore
{
    private const string RecompExtension = ".recomp";
    private const string RecompProgId = "reEngine.RecompModule";
    private const string ClassesRoot = @"Software\Classes";

    /// <summary>
    /// Initializes the live HKCU association store; throws fail-closed if <paramref name="testMode"/> is <see langword="true"/>.
    /// </summary>
    public WindowsRegistryAssociationStore(bool testMode = false)
    {
        if (testMode)
        {
            throw new InvalidOperationException(
                "Fail-Closed Sandbox Violation: WindowsRegistryAssociationStore cannot be instantiated when --test-mode is active.");
        }
    }

    /// <inheritdoc />
    public FileAssociationRecord RegisterRecompAssociation(string executablePath, string? iconPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("HKCU file association registration requires Windows.");
        }

        string fullExe = Path.GetFullPath(executablePath);
        string resolvedIcon = string.IsNullOrWhiteSpace(iconPath) ? $"{fullExe},0" : Path.GetFullPath(iconPath);
        string command = $"\"{fullExe}\" --open \"%1\"";

        using (RegistryKey extKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{RecompExtension}", writable: true))
        {
            extKey.SetValue(null, RecompProgId);
            extKey.SetValue("Content Type", "application/vnd.reengine.recomp+json");
        }

        using (RegistryKey progKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{RecompProgId}", writable: true))
        {
            progKey.SetValue(null, "reEngine Recompiled Module");
            using RegistryKey iconKey = progKey.CreateSubKey("DefaultIcon", writable: true);
            iconKey.SetValue(null, resolvedIcon);
            using RegistryKey cmdKey = progKey.CreateSubKey(@"shell\open\command", writable: true);
            cmdKey.SetValue(null, command);
        }

        return new FileAssociationRecord(
            SchemaVersion: 1,
            Extension: RecompExtension,
            ProgId: RecompProgId,
            CommandTemplate: command,
            IconPath: resolvedIcon,
            UpdatedAtUtc: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }

    /// <inheritdoc />
    public FileAssociationRecord? ReadRecompAssociation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using RegistryKey? extKey = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{RecompExtension}", writable: false);
        string? progId = extKey?.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(progId))
        {
            return null;
        }

        using RegistryKey? cmdKey = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{progId}\shell\open\command", writable: false);
        string? command = cmdKey?.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        using RegistryKey? iconKey = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{progId}\DefaultIcon", writable: false);
        string iconPath = (iconKey?.GetValue(null) as string) ?? string.Empty;

        return new FileAssociationRecord(
            SchemaVersion: 1,
            Extension: RecompExtension,
            ProgId: progId,
            CommandTemplate: command,
            IconPath: iconPath,
            UpdatedAtUtc: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }

    /// <inheritdoc />
    public bool UnregisterRecompAssociation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        bool removed = false;
        using (RegistryKey? classes = Registry.CurrentUser.OpenSubKey(ClassesRoot, writable: true))
        {
            if (classes is null)
            {
                return false;
            }
            if (classes.OpenSubKey(RecompExtension) is not null)
            {
                classes.DeleteSubKeyTree(RecompExtension, throwOnMissingSubKey: false);
                removed = true;
            }
            if (classes.OpenSubKey(RecompProgId) is not null)
            {
                classes.DeleteSubKeyTree(RecompProgId, throwOnMissingSubKey: false);
                removed = true;
            }
        }
        return removed;
    }
}
