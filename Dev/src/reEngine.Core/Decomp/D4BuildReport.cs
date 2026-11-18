// @module: reEngine.Core.Decomp.D4BuildReport
// @purpose: Standard 18 D4 clean-room build verification report schema with v0.2.1 field and flag-format compatibility.
// @depends-on: System, System.Collections.Generic, System.Text.Json, System.Text.Json.Serialization
// @exports: D4BuildReport, FlexibleCompilerFlagsJsonConverter

using System.Text.Json;
using System.Text.Json.Serialization;

namespace reEngine.Core.Decomp;

/// <summary>
/// Deserializes <c>compilerFlags</c> cleanly whether serialized as <c>string[]</c>, space-separated <c>string</c>, or <c>null</c>.
/// </summary>
public sealed class FlexibleCompilerFlagsJsonConverter : JsonConverter<IReadOnlyList<string>>
{
    /// <inheritdoc />
    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return Array.Empty<string>();

            case JsonTokenType.String:
            {
                string raw = reader.GetString() ?? string.Empty;
                return raw.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            }

            case JsonTokenType.StartArray:
            {
                var list = new List<string>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        break;
                    }
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        string? item = reader.GetString();
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            list.Add(item.Trim());
                        }
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                return list;
            }

            default:
                reader.Skip();
                return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartArray();
        if (value is not null)
        {
            foreach (string flag in value)
            {
                if (!string.IsNullOrWhiteSpace(flag))
                {
                    writer.WriteStringValue(flag);
                }
            }
        }
        writer.WriteEndArray();
    }
}

/// <summary>
/// Standard 18 <c>d4_build_report.json</c> schema proving zero warnings and zero decompiler tells at <c>D4</c>–<c>D5</c>.
/// </summary>
public sealed class D4BuildReport
{
    private bool? _isZeroWarningsVerified;
    private bool? _legacyZeroWarningsVerified;
    private bool? _isZeroDecompilerTellsVerified;
    private bool? _legacyZeroDecompilerTellsVerified;

    /// <summary>Manifest schema version.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Document manifest type identifier.</summary>
    [JsonPropertyName("manifestType")]
    public string? ManifestType { get; set; }

    /// <summary>Module slug or binary target name.</summary>
    [JsonPropertyName("moduleName")]
    public string? ModuleName { get; set; }

    /// <summary>Target tier identifier (<c>D4</c> or <c>D5</c>).</summary>
    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "D4";

    /// <summary>Total compiler error count recorded during the clean build.</summary>
    [JsonPropertyName("compilerErrorCount")]
    public int CompilerErrorCount { get; set; }

    /// <summary>Total compiler warning count recorded during the clean build.</summary>
    [JsonPropertyName("compilerWarningCount")]
    public int CompilerWarningCount { get; set; }

    /// <summary>Compiler flags used to verify the D4 translation units.</summary>
    [JsonPropertyName("compilerFlags")]
    [JsonConverter(typeof(FlexibleCompilerFlagsJsonConverter))]
    public IReadOnlyList<string> CompilerFlags { get; set; } = Array.Empty<string>();

    /// <summary>Total residual decompiler tell count detected in rewritten translation units.</summary>
    [JsonPropertyName("craftsmanshipTellCount")]
    public int CraftsmanshipTellCount { get; set; }

    /// <summary>Canonical Standard 18 zero-warnings verification flag (with v0.2.1 fallback).</summary>
    [JsonPropertyName("isZeroWarningsVerified")]
    public bool IsZeroWarningsVerified
    {
        get => _isZeroWarningsVerified ?? _legacyZeroWarningsVerified ?? false;
        set => _isZeroWarningsVerified = value;
    }

    /// <summary>Legacy v0.2.1 <c>zeroWarningsVerified</c> alias kept for fixture compatibility.</summary>
    [JsonPropertyName("zeroWarningsVerified")]
    public bool? LegacyZeroWarningsVerified
    {
        get => _legacyZeroWarningsVerified;
        set => _legacyZeroWarningsVerified = value;
    }

    /// <summary>Canonical Standard 18 zero-decompiler-tells verification flag (with v0.2.1 fallback).</summary>
    [JsonPropertyName("isZeroDecompilerTellsVerified")]
    public bool IsZeroDecompilerTellsVerified
    {
        get => _isZeroDecompilerTellsVerified ?? _legacyZeroDecompilerTellsVerified ?? false;
        set => _isZeroDecompilerTellsVerified = value;
    }

    /// <summary>Legacy v0.2.1 <c>zeroDecompilerTellsVerified</c> alias kept for fixture compatibility.</summary>
    [JsonPropertyName("zeroDecompilerTellsVerified")]
    public bool? LegacyZeroDecompilerTellsVerified
    {
        get => _legacyZeroDecompilerTellsVerified;
        set => _legacyZeroDecompilerTellsVerified = value;
    }

    /// <summary>Forward-compatible unknown JSON properties preserved across round-trips.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}
