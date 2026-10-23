using System.Text.Json;
using System.Text.Json.Serialization;

namespace GlassDock.Core;

public enum DockConfigurationLoadStatus
{
    LoadedCurrentVersion = 1,
    MigratedFromVersion1 = 2,
    DefaultedMissing = 3,
    RecoveredInvalidData = 4,
    RecoveredUnsupportedVersion = 5
}

public sealed record DockConfigurationLoadResult(
    DockState State,
    DockConfigurationLoadStatus Status,
    string? DiagnosticCode = null)
{
    public bool RequiresRecovery => Status is DockConfigurationLoadStatus.RecoveredInvalidData
        or DockConfigurationLoadStatus.RecoveredUnsupportedVersion;
}

/// <summary>
/// Versioned, UI-independent JSON codec for Dock state. It never reads or writes files itself.
/// </summary>
public static class DockConfigurationJson
{
    public const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public static string Serialize(DockState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var dto = new DockConfigurationV2Dto
        {
            SchemaVersion = CurrentSchemaVersion,
            Preferences = new DockPreferencesV2Dto
            {
                Edge = state.Preferences.Edge,
                ShowRecentApplications = state.Preferences.ShowRecentApplications,
                Appearance = state.Preferences.Appearance
            },
            PinnedApplications = state.PinnedApplications.Select(ToDto).ToList(),
            RecentApplications = state.RecentApplications.Select(ToDto).ToList(),
            FilesAndFolders = state.FilesAndFolders.Select(ToDto).ToList()
        };

        return JsonSerializer.Serialize(dto, SerializerOptions);
    }

    public static DockConfigurationLoadResult Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new DockConfigurationLoadResult(DockState.CreateDefault(), DockConfigurationLoadStatus.DefaultedMissing);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out var version))
                return RecoveredInvalid("MissingOrInvalidSchemaVersion");

            if (version == 1)
            {
                var v1 = JsonSerializer.Deserialize<DockConfigurationV1Dto>(json, SerializerOptions);
                var state = FromV1(v1 ?? throw new JsonException("Configuration root is null."));
                return new DockConfigurationLoadResult(state, DockConfigurationLoadStatus.MigratedFromVersion1);
            }

            if (version == CurrentSchemaVersion)
            {
                var v2 = JsonSerializer.Deserialize<DockConfigurationV2Dto>(json, SerializerOptions);
                var state = FromV2(v2 ?? throw new JsonException("Configuration root is null."));
                return new DockConfigurationLoadResult(state, DockConfigurationLoadStatus.LoadedCurrentVersion);
            }

            return new DockConfigurationLoadResult(
                DockState.CreateDefault(),
                DockConfigurationLoadStatus.RecoveredUnsupportedVersion,
                $"UnsupportedSchemaVersion:{version}");
        }
        catch (JsonException)
        {
            return RecoveredInvalid("InvalidJson");
        }
        catch (Exception exception) when (exception is ArgumentException
            or FormatException or InvalidOperationException or NotSupportedException or OverflowException)
        {
            return RecoveredInvalid("InvalidConfigurationData");
        }
    }

    private static DockConfigurationLoadResult RecoveredInvalid(string diagnosticCode) =>
        new(DockState.CreateDefault(), DockConfigurationLoadStatus.RecoveredInvalidData, diagnosticCode);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static DockProjectItemDto ToDto(DockProjectItem item) => new()
    {
        Id = item.Id.ToString(),
        Kind = item.Kind,
        DisplayName = item.DisplayName,
        IdentityScheme = item.TargetIdentity.Scheme,
        IdentityValue = item.TargetIdentity.Value,
        LastUsedUtc = item.LastUsedUtc
    };

    private static DockState FromV1(DockConfigurationV1Dto dto)
    {
        EnsureVersion(dto.SchemaVersion, expected: 1);
        var preferences = dto.Preferences
            ?? throw new ArgumentException("Version 1 preferences are missing.");
        if (preferences.Edge is null || preferences.ShowRecentApplications is null)
            throw new ArgumentException("Version 1 preferences are incomplete.");

        return DockState.Restore(
            MapItems(dto.PinnedApplications, DockSection.PinnedApplications),
            MapItems(dto.RecentApplications, DockSection.RecentApplications),
            MapItems(dto.FilesAndFolders, DockSection.FilesAndFolders),
            new DockPreferences(preferences.Edge.Value, preferences.ShowRecentApplications.Value,
                DockAppearancePreference.FollowSystem));
    }

    private static DockState FromV2(DockConfigurationV2Dto dto)
    {
        EnsureVersion(dto.SchemaVersion, CurrentSchemaVersion);
        var preferences = dto.Preferences
            ?? throw new ArgumentException("Version 2 preferences are missing.");
        if (preferences.Edge is null || preferences.ShowRecentApplications is null || preferences.Appearance is null)
            throw new ArgumentException("Version 2 preferences are incomplete.");

        return DockState.Restore(
            MapItems(dto.PinnedApplications, DockSection.PinnedApplications),
            MapItems(dto.RecentApplications, DockSection.RecentApplications),
            MapItems(dto.FilesAndFolders, DockSection.FilesAndFolders),
            new DockPreferences(preferences.Edge.Value, preferences.ShowRecentApplications.Value,
                preferences.Appearance.Value));
    }

    private static void EnsureVersion(int actual, int expected)
    {
        if (actual != expected)
            throw new ArgumentException("Configuration schema version changed during parsing.");
    }

    private static DockProjectItem[] MapItems(List<DockProjectItemDto>? items, DockSection section)
    {
        if (items is null)
            throw new ArgumentException($"The {section} collection is missing.");

        return items.Select(dto =>
        {
            if (dto is null
                || string.IsNullOrWhiteSpace(dto.Id)
                || string.IsNullOrWhiteSpace(dto.DisplayName)
                || string.IsNullOrWhiteSpace(dto.IdentityValue))
                throw new ArgumentException($"The {section} collection contains incomplete item data.");

            var identity = DockTargetIdentity.Create(dto.IdentityScheme, dto.IdentityValue);
            return new DockProjectItem(
                DockItemId.Parse(dto.Id),
                dto.Kind,
                section,
                dto.DisplayName,
                identity,
                dto.LastUsedUtc);
        }).ToArray();
    }

    private sealed class DockConfigurationV1Dto
    {
        public DockConfigurationV1Dto() { }
        public int SchemaVersion { get; set; }
        public DockPreferencesV1Dto? Preferences { get; set; }
        public List<DockProjectItemDto>? PinnedApplications { get; set; }
        public List<DockProjectItemDto>? RecentApplications { get; set; }
        public List<DockProjectItemDto>? FilesAndFolders { get; set; }
    }

    private sealed class DockConfigurationV2Dto
    {
        public DockConfigurationV2Dto() { }
        public int SchemaVersion { get; set; }
        public DockPreferencesV2Dto? Preferences { get; set; }
        public List<DockProjectItemDto>? PinnedApplications { get; set; }
        public List<DockProjectItemDto>? RecentApplications { get; set; }
        public List<DockProjectItemDto>? FilesAndFolders { get; set; }
    }

    private sealed class DockPreferencesV1Dto
    {
        public DockPreferencesV1Dto() { }
        public DockEdge? Edge { get; set; }
        public bool? ShowRecentApplications { get; set; }
    }

    private sealed class DockPreferencesV2Dto
    {
        public DockPreferencesV2Dto() { }
        public DockEdge? Edge { get; set; }
        public bool? ShowRecentApplications { get; set; }
        public DockAppearancePreference? Appearance { get; set; }
    }

    private sealed class DockProjectItemDto
    {
        public DockProjectItemDto() { }
        public string? Id { get; set; }
        public DockProjectItemKind Kind { get; set; }
        public string? DisplayName { get; set; }
        public DockIdentityScheme IdentityScheme { get; set; }
        public string? IdentityValue { get; set; }
        public DateTimeOffset? LastUsedUtc { get; set; }
    }
}
