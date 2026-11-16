// @module: reEngine.Core.Associations.FileAssociationStores
// @purpose: File association abstraction with live HKCU Windows Registry store and fail-closed isolated virtual_registry.json store.
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
/// Abstracts <c>.recomp</c> and <c>.decomp</c> file association registration across live HKCU and isolated virtual storage.
/// </summary>
public interface IFileAssociationStore
{
    /// <summary>Registers the <c>.recomp</c> open handler pointing to <paramref name="executablePath"/>.</summary>
    FileAssociationRecord RegisterRecompAssociation(string executablePath, string? iconPath = null);

    /// <summary>Reads the currently registered <c>.recomp</c> association, or returns <see langword="null"/>.</summary>
    FileAssociationRecord? ReadRecompAssociation();

    /// <summary>Removes the <c>.recomp</c> association if present.</summary>
    bool UnregisterRecompAssociation();

    /// <summary>Registers the <c>.decomp</c> open handler pointing to <paramref name="executablePath"/>.</summary>
    FileAssociationRecord RegisterDecompAssociation(string executablePath, string? iconPath = null);

    /// <summary>Reads the currently registered <c>.decomp</c> association, or returns <see langword="null"/>.</summary>
    FileAssociationRecord? ReadDecompAssociation();

    /// <summary>Removes the <c>.decomp</c> association if present.</summary>
    bool UnregisterDecompAssociation();
}

/// <summary>
/// Fail-closed isolated association store persisting records atomically to <c>&lt;dataDir&gt;\virtual_registry.json</c>.
/// </summary>
public sealed class VirtualRegistryAssociationStore : IFileAssociationStore
{
    private const string RecompExtension = ".recomp";
    private const string RecompProgId = "reEngine.RecompModule";
    private const string DecompExtension = ".decomp";
    private const string DecompProgId = "reEngine.DecompModule";

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
        string containedDataDir = AppEnvironment.AssertPathContained(dataDir, root);
        Directory.CreateDirectory(containedDataDir);
        string candidateFile = Path.Combine(containedDataDir, ReEnginePackagingConstants.VirtualRegistryFileName);
        _registryFilePath = AppEnvironment.AssertPathContained(candidateFile, root);
    }

    /// <inheritdoc />
    public FileAssociationRecord RegisterRecompAssociation(string executablePath, string? iconPath = null) =>
        RegisterExtension(RecompExtension, RecompProgId, $"\"{Path.GetFullPath(executablePath)}\" --open \"%1\"", executablePath, iconPath);

    /// <inheritdoc />
    public FileAssociationRecord? ReadRecompAssociation() => ReadExtension(RecompExtension);

    /// <inheritdoc />
    public bool UnregisterRecompAssociation() => UnregisterExtension(RecompExtension);

    /// <inheritdoc />
    public FileAssociationRecord RegisterDecompAssociation(string executablePath, string? iconPath = null) =>
        RegisterExtension(DecompExtension, DecompProgId, $"\"{Path.GetFullPath(executablePath)}\" decomp --module \"%1\"", executablePath, iconPath);

    /// <inheritdoc />
    public FileAssociationRecord? ReadDecompAssociation() => ReadExtension(DecompExtension);

    /// <inheritdoc />
    public bool UnregisterDecompAssociation() => UnregisterExtension(DecompExtension);

    private FileAssociationRecord RegisterExtension(
        string extension,
        string progId,
        string commandTemplate,
        string executablePath,
        string? iconPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        string fullExe = Path.GetFullPath(executablePath);
        string resolvedIcon = string.IsNullOrWhiteSpace(iconPath) ? $"{fullExe},0" : Path.GetFullPath(iconPath);
        var record = new FileAssociationRecord(
            SchemaVersion: 1,
            Extension: extension,
            ProgId: progId,
            CommandTemplate: commandTemplate,
            IconPath: resolvedIcon,
            UpdatedAtUtc: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

        Dictionary<string, FileAssociationRecord> records = ReadAllRecords();
        records[extension] = record;
        WriteAtomically(records);
        return record;
    }

    private FileAssociationRecord? ReadExtension(string extension) =>
        ReadAllRecords().TryGetValue(extension, out FileAssociationRecord? record) ? record : null;

    private bool UnregisterExtension(string extension)
    {
        if (!File.Exists(_registryFilePath))
        {
            return false;
        }

        Dictionary<string, FileAssociationRecord> records = ReadAllRecords();
        if (!records.Remove(extension))
        {
            return false;
        }

        if (records.Count == 0)
        {
            File.Delete(_registryFilePath);
        }
        else
        {
            WriteAtomically(records);
        }
        return true;
    }

    private Dictionary<string, FileAssociationRecord> ReadAllRecords()
    {
        var map = new Dictionary<string, FileAssociationRecord>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_registryFilePath))
        {
            return map;
        }

        string raw = File.ReadAllText(_registryFilePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return map;
        }

        using JsonDocument doc = JsonDocument.Parse(raw);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return map;
        }

        if (doc.RootElement.TryGetProperty("extension", out _))
        {
            FileAssociationRecord? single = JsonSerializer.Deserialize<FileAssociationRecord>(raw, JsonOptions);
            if (single is not null && !string.IsNullOrWhiteSpace(single.Extension))
            {
                map[single.Extension] = single;
            }
            return map;
        }

        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            FileAssociationRecord? rec = prop.Value.Deserialize<FileAssociationRecord>(JsonOptions);
            if (rec is not null)
            {
                map[prop.Name] = rec;
            }
        }
        return map;
    }

    private void WriteAtomically(Dictionary<string, FileAssociationRecord> records)
    {
        string tempPath = $"{_registryFilePath}.{Guid.NewGuid():N}.tmp";
        string bakPath = _registryFilePath + ".bak";
        string json = JsonSerializer.Serialize(records, JsonOptions);

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
    private const string DecompExtension = ".decomp";
    private const string DecompProgId = "reEngine.DecompModule";
    private const string ClassesRoot = @"Software\Classes";

    /// <summary>
    /// Initializes the live HKCU association store; throws fail-closed if host mutations are suppressed (<paramref name="suppressHostMutations"/> or <c>APP_ISOLATED_TEST=1</c>).
    /// </summary>
    public WindowsRegistryAssociationStore(bool suppressHostMutations = false)
    {
        string? envFlag = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
        bool isIsolated = suppressHostMutations ||
                          string.Equals(envFlag, "1", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(envFlag, "true", StringComparison.OrdinalIgnoreCase);
        if (isIsolated)
        {
            throw new InvalidOperationException(
                "Fail-Closed Isolation Violation: WindowsRegistryAssociationStore cannot be instantiated when host mutations are suppressed (APP_ISOLATED_TEST=1).");
        }
    }

    /// <inheritdoc />
    public FileAssociationRecord RegisterRecompAssociation(string executablePath, string? iconPath = null) =>
        RegisterExtension(
            RecompExtension,
            RecompProgId,
            "application/vnd.reengine.recomp+json",
            "reEngine Recompiled Module",
            $"\"{Path.GetFullPath(executablePath)}\" --open \"%1\"",
            executablePath,
            iconPath);

    /// <inheritdoc />
    public FileAssociationRecord? ReadRecompAssociation() => ReadExtension(RecompExtension);

    /// <inheritdoc />
    public bool UnregisterRecompAssociation() => UnregisterExtension(RecompExtension, RecompProgId);

    /// <inheritdoc />
    public FileAssociationRecord RegisterDecompAssociation(string executablePath, string? iconPath = null) =>
        RegisterExtension(
            DecompExtension,
            DecompProgId,
            "application/vnd.reengine.decomp+json",
            "reEngine Decompilation Module",
            $"\"{Path.GetFullPath(executablePath)}\" decomp --module \"%1\"",
            executablePath,
            iconPath);

    /// <inheritdoc />
    public FileAssociationRecord? ReadDecompAssociation() => ReadExtension(DecompExtension);

    /// <inheritdoc />
    public bool UnregisterDecompAssociation() => UnregisterExtension(DecompExtension, DecompProgId);

    private static FileAssociationRecord RegisterExtension(
        string extension,
        string progId,
        string contentType,
        string description,
        string command,
        string executablePath,
        string? iconPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("HKCU file association registration requires Windows.");
        }

        string fullExe = Path.GetFullPath(executablePath);
        string resolvedIcon = string.IsNullOrWhiteSpace(iconPath) ? $"{fullExe},0" : Path.GetFullPath(iconPath);

        using (RegistryKey extKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{extension}", writable: true))
        {
            extKey.SetValue(null, progId);
            extKey.SetValue("Content Type", contentType);
        }

        using (RegistryKey progKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{progId}", writable: true))
        {
            progKey.SetValue(null, description);
            using RegistryKey iconKey = progKey.CreateSubKey("DefaultIcon", writable: true);
            iconKey.SetValue(null, resolvedIcon);
            using RegistryKey cmdKey = progKey.CreateSubKey(@"shell\open\command", writable: true);
            cmdKey.SetValue(null, command);
        }

        return new FileAssociationRecord(
            SchemaVersion: 1,
            Extension: extension,
            ProgId: progId,
            CommandTemplate: command,
            IconPath: resolvedIcon,
            UpdatedAtUtc: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }

    private static FileAssociationRecord? ReadExtension(string extension)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using RegistryKey? extKey = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{extension}", writable: false);
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
            Extension: extension,
            ProgId: progId,
            CommandTemplate: command,
            IconPath: iconPath,
            UpdatedAtUtc: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }

    private static bool UnregisterExtension(string extension, string progId)
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
            if (classes.OpenSubKey(extension) is not null)
            {
                classes.DeleteSubKeyTree(extension, throwOnMissingSubKey: false);
                removed = true;
            }
            if (classes.OpenSubKey(progId) is not null)
            {
                classes.DeleteSubKeyTree(progId, throwOnMissingSubKey: false);
                removed = true;
            }
        }
        return removed;
    }
}
