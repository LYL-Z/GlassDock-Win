using System.Text.Json;
using System.Text.Json.Serialization;

namespace GlassDock.Core;

public enum DockConfigurationLoadStatus
{
    LoadedCurrentVersion = 1,
    MigratedFromVersion1 = 2,
    DefaultedMissing = 3,
    RecoveredInvalidData = 4,
    RecoveredUnsupportedVersion = 5,
    MigratedFromVersion2 = 6
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
    public const int CurrentSchemaVersion = 3;
    public const int MaximumConfigurationCharacters = 1_048_576;
    public const int MaximumItemsPerSection = 4_096;

    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public static string Serialize(DockState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureSectionSize(state.PinnedApplications.Count, DockSection.PinnedApplications);
        EnsureSectionSize(state.RecentApplications.Count, DockSection.RecentApplications);
        EnsureSectionSize(state.FilesAndFolders.Count, DockSection.FilesAndFolders);

        var dto = new DockConfigurationV3Dto
        {
            SchemaVersion = CurrentSchemaVersion,
            Preferences = new DockPreferencesV3Dto
            {
                Edge = state.Preferences.Edge,
                ShowRecentApplications = state.Preferences.ShowRecentApplications,
                Appearance = state.Preferences.Appearance
            },
            PinnedApplications = state.PinnedApplications.Select(ToDto).ToList(),
            RecentApplications = state.RecentApplications.Select(ToDto).ToList(),
            FilesAndFolders = state.FilesAndFolders.Select(ToDto).ToList()
        };

        var json = JsonSerializer.Serialize(dto, SerializerOptions);
        if (json.Length > MaximumConfigurationCharacters)
            throw new InvalidOperationException($"Serialized Dock configuration exceeds {MaximumConfigurationCharacters} characters.");

        return json;
    }

    public static DockConfigurationLoadResult Deserialize(string? json)
    {
        if (json is null || json.Length == 0)
            return new DockConfigurationLoadResult(DockState.CreateDefault(), DockConfigurationLoadStatus.DefaultedMissing);
        if (json.Length > MaximumConfigurationCharacters)
            return RecoveredInvalid("ConfigurationTooLarge");
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

            if (version == 2)
            {
                var v2 = JsonSerializer.Deserialize<DockConfigurationV2Dto>(json, SerializerOptions);
                var state = FromV2(v2 ?? throw new JsonException("Configuration root is null."));
                return new DockConfigurationLoadResult(state, DockConfigurationLoadStatus.MigratedFromVersion2);
            }

            if (version == CurrentSchemaVersion)
            {
                var v3 = JsonSerializer.Deserialize<DockConfigurationV3Dto>(json, SerializerOptions);
                var state = FromV3(v3 ?? throw new JsonException("Configuration root is null."));
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
            PropertyNameCaseInsensitive = false,
            MaxDepth = 32
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
        IdentityComparisonKey = item.TargetIdentity.ComparisonKey,
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
            MapItems(dto.PinnedApplications, DockSection.PinnedApplications, requireComparisonKey: false),
            MapItems(dto.RecentApplications, DockSection.RecentApplications, requireComparisonKey: false),
            MapItems(dto.FilesAndFolders, DockSection.FilesAndFolders, requireComparisonKey: false),
            new DockPreferences(preferences.Edge.Value, preferences.ShowRecentApplications.Value,
                DockAppearancePreference.FollowSystem));
    }

    private static DockState FromV2(DockConfigurationV2Dto dto)
    {
        EnsureVersion(dto.SchemaVersion, expected: 2);
        var preferences = dto.Preferences
            ?? throw new ArgumentException("Version 2 preferences are missing.");
        if (preferences.Edge is null || preferences.ShowRecentApplications is null || preferences.Appearance is null)
            throw new ArgumentException("Version 2 preferences are incomplete.");

        return DockState.Restore(
            MapItems(dto.PinnedApplications, DockSection.PinnedApplications, requireComparisonKey: false),
            MapItems(dto.RecentApplications, DockSection.RecentApplications, requireComparisonKey: false),
            MapItems(dto.FilesAndFolders, DockSection.FilesAndFolders, requireComparisonKey: false),
            new DockPreferences(preferences.Edge.Value, preferences.ShowRecentApplications.Value,
                preferences.Appearance.Value));
    }

    private static DockState FromV3(DockConfigurationV3Dto dto)
    {
        EnsureVersion(dto.SchemaVersion, CurrentSchemaVersion);
        var preferences = dto.Preferences
            ?? throw new ArgumentException("Version 3 preferences are missing.");
        if (preferences.Edge is null || preferences.ShowRecentApplications is null || preferences.Appearance is null)
            throw new ArgumentException("Version 3 preferences are incomplete.");

        return DockState.Restore(
            MapItems(dto.PinnedApplications, DockSection.PinnedApplications, requireComparisonKey: true),
            MapItems(dto.RecentApplications, DockSection.RecentApplications, requireComparisonKey: true),
            MapItems(dto.FilesAndFolders, DockSection.FilesAndFolders, requireComparisonKey: true),
            new DockPreferences(preferences.Edge.Value, preferences.ShowRecentApplications.Value,
                preferences.Appearance.Value));
    }

    private static void EnsureVersion(int actual, int expected)
    {
        if (actual != expected)
            throw new ArgumentException("Configuration schema version changed during parsing.");
    }

    private static void EnsureSectionSize(int count, DockSection section)
    {
        if (count > MaximumItemsPerSection)
            throw new InvalidOperationException($"The {section} collection exceeds {MaximumItemsPerSection} items.");
    }

    private static DockProjectItem[] MapItems(
        List<DockProjectItemDto>? items,
        DockSection section,
        bool requireComparisonKey)
    {
        if (items is null)
            throw new ArgumentException($"The {section} collection is missing.");
        EnsureSectionSize(items.Count, section);

        return items.Select(dto =>
        {
            if (dto is null
                || string.IsNullOrWhiteSpace(dto.Id)
                || dto.Kind is null
                || string.IsNullOrWhiteSpace(dto.DisplayName)
                || dto.IdentityScheme is null
                || string.IsNullOrWhiteSpace(dto.IdentityValue)
                || (requireComparisonKey && string.IsNullOrWhiteSpace(dto.IdentityComparisonKey)))
                throw new ArgumentException($"The {section} collection contains incomplete item data.");

            var comparisonKey = string.IsNullOrWhiteSpace(dto.IdentityComparisonKey)
                ? dto.IdentityValue
                : dto.IdentityComparisonKey;
            var identity = DockTargetIdentity.Create(dto.IdentityScheme.Value, dto.IdentityValue, comparisonKey!);
            return new DockProjectItem(
                DockItemId.Parse(dto.Id),
                dto.Kind.Value,
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

    private sealed class DockConfigurationV3Dto
    {
        public DockConfigurationV3Dto() { }
        public int SchemaVersion { get; set; }
        public DockPreferencesV3Dto? Preferences { get; set; }
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

    private sealed class DockPreferencesV3Dto
    {
        public DockPreferencesV3Dto() { }
        public DockEdge? Edge { get; set; }
        public bool? ShowRecentApplications { get; set; }
        public DockAppearancePreference? Appearance { get; set; }
    }

    private sealed class DockProjectItemDto
    {
        public DockProjectItemDto() { }
        public string? Id { get; set; }
        public DockProjectItemKind? Kind { get; set; }
        public string? DisplayName { get; set; }
        public DockIdentityScheme? IdentityScheme { get; set; }
        public string? IdentityValue { get; set; }
        public string? IdentityComparisonKey { get; set; }
        public DateTimeOffset? LastUsedUtc { get; set; }
    }
}
