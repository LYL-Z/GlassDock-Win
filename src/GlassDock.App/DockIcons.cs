using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GlassDock.Core;

namespace GlassDock.App;

public enum DockIconRole
{
    Finder = 1,
    SystemSettings = 2,
    Downloads = 3,
    Trash = 4
}

public enum DockIconTheme
{
    Any = 1,
    Light = 2,
    Dark = 3
}

public enum DockIconVisualState
{
    Default = 1,
    Empty = 2,
    Full = 3,
    Unknown = 4
}

public enum DockIconFormat
{
    Png = 1,
    Ico = 2
}

public enum DockIconRedistributionStatus
{
    Unverified = 1,
    LocalUseOnly = 2,
    ClearedForRedistribution = 3
}

public enum DockIconSelectorKind
{
    Item = 1,
    Target = 2,
    Role = 3
}

public sealed record DockIconSelector
{
    private DockIconSelector(
        DockIconSelectorKind kind,
        DockItemId? itemId,
        DockTargetIdentity? targetIdentity,
        DockIconRole? role)
    {
        Kind = kind;
        ItemId = itemId;
        TargetIdentity = targetIdentity;
        Role = role;
    }

    public DockIconSelectorKind Kind { get; }
    public DockItemId? ItemId { get; }
    public DockTargetIdentity? TargetIdentity { get; }
    public DockIconRole? Role { get; }

    public static DockIconSelector ForItem(DockItemId itemId) => new(DockIconSelectorKind.Item, itemId, null, null);

    public static DockIconSelector ForTarget(DockTargetIdentity targetIdentity)
    {
        if (!targetIdentity.IsValid)
            throw new ArgumentException("A valid target identity is required.", nameof(targetIdentity));
        return new DockIconSelector(DockIconSelectorKind.Target, null, targetIdentity, null);
    }

    public static DockIconSelector ForRole(DockIconRole role)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        return new DockIconSelector(DockIconSelectorKind.Role, null, null, role);
    }

    public bool Matches(DockProjectItem item) => Kind switch
    {
        DockIconSelectorKind.Item => ItemId == item.Id,
        DockIconSelectorKind.Target => TargetIdentity == item.TargetIdentity,
        _ => false
    };
}

public sealed record DockIconRoleAssignment
{
    public DockIconRoleAssignment(DockIconSelector selector, DockIconRole role)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (selector.Kind is DockIconSelectorKind.Role)
            throw new ArgumentException("A role assignment must select an item or target.", nameof(selector));
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        Selector = selector;
        Role = role;
    }

    public DockIconSelector Selector { get; }
    public DockIconRole Role { get; }
}

public sealed record DockIconAssetVariant
{
    public DockIconAssetVariant(
        string relativePath,
        DockIconTheme theme = DockIconTheme.Any,
        DockIconVisualState state = DockIconVisualState.Default,
        string sourceNote = "User-provided",
        DockIconRedistributionStatus redistributionStatus = DockIconRedistributionStatus.LocalUseOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (relativePath.Length > 240 || relativePath.Contains('\0') || relativePath.Contains(':'))
            throw new ArgumentException("Icon asset path must be a short relative path.", nameof(relativePath));
        if (Path.IsPathRooted(relativePath) || relativePath[0] is '/' or '\\')
            throw new ArgumentException("Icon asset path cannot be rooted.", nameof(relativePath));

        var segments = relativePath.Replace('\\', '/').Split('/');
        if (segments.Any(segment => segment is ".." or "." or ""))
            throw new ArgumentException("Icon asset path cannot escape its local asset folder.", nameof(relativePath));

        var extension = Path.GetExtension(relativePath);
        if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".ico", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only PNG and ICO assets are supported.", nameof(relativePath));
        if (!Enum.IsDefined(theme))
            throw new ArgumentOutOfRangeException(nameof(theme));
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (!Enum.IsDefined(redistributionStatus))
            throw new ArgumentOutOfRangeException(nameof(redistributionStatus));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNote);
        if (sourceNote.Length > 256)
            throw new ArgumentOutOfRangeException(nameof(sourceNote));

        RelativePath = string.Join('/', segments);
        Theme = theme;
        State = state;
        SourceNote = sourceNote.Trim();
        RedistributionStatus = redistributionStatus;
    }

    public string RelativePath { get; }
    public DockIconTheme Theme { get; }
    public DockIconVisualState State { get; }
    public string SourceNote { get; }
    public DockIconRedistributionStatus RedistributionStatus { get; }
    public DockIconFormat Format => Path.GetExtension(RelativePath).Equals(".ico", StringComparison.OrdinalIgnoreCase)
        ? DockIconFormat.Ico
        : DockIconFormat.Png;
}

public sealed record DockIconMapping
{
    public DockIconMapping(DockIconSelector selector, IEnumerable<DockIconAssetVariant> variants)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(variants);
        var snapshot = variants.ToArray();
        if (snapshot.Length is < 1 or > 12 || snapshot.Any(variant => variant is null))
            throw new ArgumentException("Each icon mapping must contain 1 to 12 variants.", nameof(variants));
        if (snapshot.Select(variant => (variant.Theme, variant.State)).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("A mapping cannot repeat a theme and state variant.", nameof(variants));

        Selector = selector;
        Variants = Array.AsReadOnly(snapshot);
    }

    public DockIconSelector Selector { get; }
    public IReadOnlyList<DockIconAssetVariant> Variants { get; }
}

public sealed class DockIconManifest
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumMappings = 512;

    public DockIconManifest(
        IEnumerable<DockIconRoleAssignment>? roleAssignments = null,
        IEnumerable<DockIconMapping>? mappings = null)
    {
        var roleSnapshot = (roleAssignments ?? []).ToArray();
        var mappingSnapshot = (mappings ?? []).ToArray();
        if (roleSnapshot.Length > MaximumMappings || mappingSnapshot.Length > MaximumMappings)
            throw new ArgumentOutOfRangeException(nameof(mappings), $"A manifest supports at most {MaximumMappings} entries per collection.");
        if (roleSnapshot.Any(item => item is null) || mappingSnapshot.Any(item => item is null))
            throw new ArgumentException("Icon manifest collections cannot contain null entries.");
        if (roleSnapshot.Select(item => item.Selector).Distinct().Count() != roleSnapshot.Length)
            throw new ArgumentException("Icon manifest contains duplicate role assignments.", nameof(roleAssignments));
        if (mappingSnapshot.Select(item => item.Selector).Distinct().Count() != mappingSnapshot.Length)
            throw new ArgumentException("Icon manifest contains duplicate icon selectors.", nameof(mappings));

        RoleAssignments = Array.AsReadOnly(roleSnapshot);
        Mappings = Array.AsReadOnly(mappingSnapshot);
    }

    public IReadOnlyList<DockIconRoleAssignment> RoleAssignments { get; }
    public IReadOnlyList<DockIconMapping> Mappings { get; }

    public static DockIconManifest Empty { get; } = new();
}

public enum DockIconManifestDecodeStatus
{
    Loaded = 1,
    Invalid = 2,
    UnsupportedVersion = 3
}

public sealed record DockIconManifestDecodeResult(
    DockIconManifestDecodeStatus Status,
    DockIconManifest Manifest,
    string? DiagnosticCode = null);

/// <summary>Pure, bounded JSON codec. It never reads files or resolves local asset paths.</summary>
public static class DockIconManifestJson
{
    public const int MaximumCharacters = 262_144;
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(DockIconManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var dto = new ManifestDto
        {
            SchemaVersion = DockIconManifest.CurrentSchemaVersion,
            RoleAssignments = manifest.RoleAssignments.Select(ToDto).ToList(),
            Mappings = manifest.Mappings.Select(ToDto).ToList()
        };
        var json = JsonSerializer.Serialize(dto, Options);
        if (json.Length > MaximumCharacters)
            throw new InvalidOperationException($"Icon manifest exceeds {MaximumCharacters} characters.");
        return json;
    }

    public static DockIconManifestDecodeResult Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new DockIconManifestDecodeResult(DockIconManifestDecodeStatus.Invalid, DockIconManifest.Empty, "IconManifestEmpty");
        if (json.Length > MaximumCharacters)
            return new DockIconManifestDecodeResult(DockIconManifestDecodeStatus.Invalid, DockIconManifest.Empty, "IconManifestTooLarge");

        try
        {
            var dto = JsonSerializer.Deserialize<ManifestDto>(json, Options);
            if (dto?.SchemaVersion != DockIconManifest.CurrentSchemaVersion)
            {
                var status = dto?.SchemaVersion > DockIconManifest.CurrentSchemaVersion
                    ? DockIconManifestDecodeStatus.UnsupportedVersion
                    : DockIconManifestDecodeStatus.Invalid;
                return new DockIconManifestDecodeResult(status, DockIconManifest.Empty,
                    status == DockIconManifestDecodeStatus.UnsupportedVersion ? "IconManifestVersionUnsupported" : "IconManifestVersionMissing");
            }
            if (dto.RoleAssignments is null || dto.Mappings is null)
                return new DockIconManifestDecodeResult(DockIconManifestDecodeStatus.Invalid, DockIconManifest.Empty, "IconManifestCollectionsMissing");

            var roles = dto.RoleAssignments.Select(FromDto).ToArray();
            var mappings = dto.Mappings.Select(FromDto).ToArray();
            return new DockIconManifestDecodeResult(
                DockIconManifestDecodeStatus.Loaded,
                new DockIconManifest(roles, mappings));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            return new DockIconManifestDecodeResult(DockIconManifestDecodeStatus.Invalid, DockIconManifest.Empty, "IconManifestInvalidData");
        }
    }

    private static RoleAssignmentDto ToDto(DockIconRoleAssignment value) => new()
    {
        Selector = ToDto(value.Selector),
        Role = value.Role
    };

    private static MappingDto ToDto(DockIconMapping value) => new()
    {
        Selector = ToDto(value.Selector),
        Variants = value.Variants.Select(variant => new VariantDto
        {
            RelativePath = variant.RelativePath,
            Theme = variant.Theme,
            State = variant.State,
            SourceNote = variant.SourceNote,
            RedistributionStatus = variant.RedistributionStatus
        }).ToList()
    };

    private static SelectorDto ToDto(DockIconSelector selector) => new()
    {
        ItemId = selector.ItemId?.ToString(),
        TargetIdentity = selector.TargetIdentity is { } identity
            ? new TargetIdentityDto
            {
                Scheme = identity.Scheme,
                Value = identity.Value,
                ComparisonKey = identity.ComparisonKey
            }
            : null,
        Role = selector.Role
    };

    private static DockIconRoleAssignment FromDto(RoleAssignmentDto dto)
    {
        if (dto.Selector is null || dto.Role is null)
            throw new JsonException("Icon role assignment is incomplete.");
        return new DockIconRoleAssignment(FromDto(dto.Selector), dto.Role.Value);
    }

    private static DockIconMapping FromDto(MappingDto dto)
    {
        if (dto.Selector is null || dto.Variants is null)
            throw new JsonException("Icon mapping is incomplete.");
        var variants = dto.Variants.Select(variant => new DockIconAssetVariant(
            variant.RelativePath ?? throw new JsonException("Icon asset path is missing."),
            variant.Theme,
            variant.State,
            variant.SourceNote ?? "User-provided",
            variant.RedistributionStatus));
        return new DockIconMapping(FromDto(dto.Selector), variants);
    }

    private static DockIconSelector FromDto(SelectorDto dto)
    {
        var selectorCount = (dto.ItemId is null ? 0 : 1)
            + (dto.TargetIdentity is null ? 0 : 1)
            + (dto.Role is null ? 0 : 1);
        if (selectorCount != 1)
            throw new JsonException("An icon selector must contain exactly one key.");
        if (dto.ItemId is not null)
            return DockIconSelector.ForItem(DockItemId.Parse(dto.ItemId));
        if (dto.Role is not null)
            return DockIconSelector.ForRole(dto.Role.Value);

        var target = dto.TargetIdentity!;
        if (target.Scheme is null || string.IsNullOrWhiteSpace(target.Value) || string.IsNullOrWhiteSpace(target.ComparisonKey))
            throw new JsonException("Target icon selector is incomplete.");
        return DockIconSelector.ForTarget(DockTargetIdentity.Create(target.Scheme.Value, target.Value, target.ComparisonKey));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 32
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    private sealed class ManifestDto
    {
        public int? SchemaVersion { get; set; }
        public List<RoleAssignmentDto>? RoleAssignments { get; set; }
        public List<MappingDto>? Mappings { get; set; }
    }

    private sealed class RoleAssignmentDto
    {
        public SelectorDto? Selector { get; set; }
        public DockIconRole? Role { get; set; }
    }

    private sealed class MappingDto
    {
        public SelectorDto? Selector { get; set; }
        public List<VariantDto>? Variants { get; set; }
    }

    private sealed class SelectorDto
    {
        public string? ItemId { get; set; }
        public TargetIdentityDto? TargetIdentity { get; set; }
        public DockIconRole? Role { get; set; }
    }

    private sealed class TargetIdentityDto
    {
        public DockIdentityScheme? Scheme { get; set; }
        public string? Value { get; set; }
        public string? ComparisonKey { get; set; }
    }

    private sealed class VariantDto
    {
        public string? RelativePath { get; set; }
        public DockIconTheme Theme { get; set; } = DockIconTheme.Any;
        public DockIconVisualState State { get; set; } = DockIconVisualState.Default;
        public string? SourceNote { get; set; }
        public DockIconRedistributionStatus RedistributionStatus { get; set; } = DockIconRedistributionStatus.LocalUseOnly;
    }
}

public sealed class DockIconAsset
{
    public const int MaximumEncodedBytes = 16 * 1024 * 1024;
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly byte[] _encodedBytes;

    private DockIconAsset(byte[] encodedBytes, DockIconFormat format)
    {
        _encodedBytes = encodedBytes;
        Format = format;
    }

    public DockIconFormat Format { get; }
    public ReadOnlyMemory<byte> EncodedBytes => _encodedBytes;

    public static DockIconAsset Create(ReadOnlyMemory<byte> encodedBytes, DockIconFormat format)
    {
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format));
        if (encodedBytes.Length is < 24 or > MaximumEncodedBytes)
            throw new ArgumentException("Icon asset has an unsupported encoded size.", nameof(encodedBytes));

        if (format == DockIconFormat.Png)
            ValidatePng(encodedBytes.Span);
        else
            ValidateIco(encodedBytes.Span);
        return new DockIconAsset(encodedBytes.ToArray(), format);
    }

    private static void ValidatePng(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith(PngSignature) || bytes.Length < 33
            || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new ArgumentException("Icon data is not a valid PNG header.", nameof(bytes));
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        if (width is 0 or > 1024 || height is 0 or > 1024 || (ulong)width * height > 1_048_576)
            throw new ArgumentException("PNG icon dimensions exceed the supported limit.", nameof(bytes));
    }

    private static void ValidateIco(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 22 || BinaryPrimitives.ReadUInt16LittleEndian(bytes[..2]) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(2, 2)) != 1)
            throw new ArgumentException("Icon data is not a valid ICO directory.", nameof(bytes));

        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(4, 2));
        if (count is 0 or > 32 || bytes.Length < 6 + count * 16)
            throw new ArgumentException("ICO frame table is invalid or too large.", nameof(bytes));

        var tableEnd = 6 + count * 16;
        for (var index = 0; index < count; index++)
        {
            var entry = bytes.Slice(6 + index * 16, 16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            var imageLength = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8, 4));
            var imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(12, 4));
            if (width is 0 or > 1024 || height is 0 or > 1024 || (long)width * height > 1_048_576
                || imageLength < 8 || imageOffset < tableEnd || imageOffset > bytes.Length
                || imageLength > bytes.Length - imageOffset)
                throw new ArgumentException("ICO frame exceeds supported bounds.", nameof(bytes));
        }
    }
}

public enum DockIconAssetReadStatus
{
    Found = 1,
    NotFound = 2,
    AccessDenied = 3,
    Invalid = 4,
    Failed = 5
}

public sealed record DockIconAssetReadResult(DockIconAssetReadStatus Status, DockIconAsset? Asset, string? DiagnosticCode = null)
{
    public static DockIconAssetReadResult Found(DockIconAsset asset) => new(DockIconAssetReadStatus.Found, asset);
    public static DockIconAssetReadResult Unavailable(DockIconAssetReadStatus status, string? diagnosticCode = null) =>
        new(status, null, diagnosticCode);
}

public interface IDockIconAssetReader
{
    ValueTask<DockIconAssetReadResult> ReadLocalAssetAsync(string relativePath, CancellationToken cancellationToken = default);
}

public interface IDockIconVisualStateProvider
{
    ValueTask<DockIconVisualState> GetVisualStateAsync(DockIconRole role, CancellationToken cancellationToken = default);
}

public enum DockIconResolutionSource
{
    UserItemOverride = 1,
    UserTargetOverride = 2,
    UserRoleOverride = 3,
    WindowsShell = 4
}

public sealed record DockIconResolutionResult(
    DockApplicationIconStatus Status,
    DockIconAsset? Asset,
    DockIconResolutionSource? Source,
    DockIconRole? Role,
    string? DiagnosticCode = null,
    DockIconVisualState? VisualState = null);

public sealed record DockIconResolutionDiagnostic(
    string Stage,
    string Event,
    double ElapsedMilliseconds,
    string? Outcome = null,
    string? DiagnosticCode = null);

/// <summary>Resolves visual assets separately from Dock target identity and Shell command routing.</summary>
public sealed class DockIconResolver
{
    private readonly DockIconManifest _manifest;
    private readonly IDockIconAssetReader _assets;
    private readonly IDockApplicationProvider _applications;
    private readonly IDockIconVisualStateProvider? _visualStates;
    private readonly Action<DockIconResolutionDiagnostic>? _diagnostics;

    public DockIconResolver(
        DockIconManifest manifest,
        IDockIconAssetReader assets,
        IDockApplicationProvider applications,
        IDockIconVisualStateProvider? visualStates = null,
        Action<DockIconResolutionDiagnostic>? diagnostics = null)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));
        _visualStates = visualStates;
        _diagnostics = diagnostics;
    }

    public async ValueTask<DockIconResolutionResult> ResolveAsync(
        DockProjectItem item,
        DockIconTheme theme = DockIconTheme.Any,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Enum.IsDefined(theme))
            throw new ArgumentOutOfRangeException(nameof(theme));

        var target = DockIconSelector.ForTarget(item.TargetIdentity);
        var itemSelector = DockIconSelector.ForItem(item.Id);
        var role = FindRole(item, itemSelector, target);
        var state = DockIconVisualState.Default;
        if (role is not null && _visualStates is not null)
        {
            var stateTimer = Stopwatch.StartNew();
            Report(new DockIconResolutionDiagnostic("visual-state-query", "started", 0, role.ToString()));
            try
            {
                state = await _visualStates.GetVisualStateAsync(role.Value, cancellationToken).ConfigureAwait(false);
                if (!Enum.IsDefined(state))
                    state = DockIconVisualState.Unknown;
                Report(new DockIconResolutionDiagnostic(
                    "visual-state-query", "completed", stateTimer.Elapsed.TotalMilliseconds, state.ToString()));
            }
            catch (OperationCanceledException)
            {
                Report(new DockIconResolutionDiagnostic(
                    "visual-state-query", "cancelled", stateTimer.Elapsed.TotalMilliseconds, role.ToString()));
                throw;
            }
            catch
            {
                state = DockIconVisualState.Unknown;
                Report(new DockIconResolutionDiagnostic(
                    "visual-state-query", "failed", stateTimer.Elapsed.TotalMilliseconds, state.ToString()));
            }
        }
        var candidates = new List<(DockIconSelector Selector, DockIconResolutionSource Source)>
        {
            (itemSelector, DockIconResolutionSource.UserItemOverride),
            (target, DockIconResolutionSource.UserTargetOverride)
        };
        if (role is not null)
            candidates.Add((DockIconSelector.ForRole(role.Value), DockIconResolutionSource.UserRoleOverride));

        string? customFailure = null;
        foreach (var (selector, source) in candidates)
        {
            var mapping = _manifest.Mappings.FirstOrDefault(candidate => candidate.Selector == selector);
            var variant = mapping is null ? null : SelectVariant(mapping, theme, state);
            if (variant is null)
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            var assetTimer = Stopwatch.StartNew();
            Report(new DockIconResolutionDiagnostic("local-asset-read", "started", 0, selector.Kind.ToString()));
            DockIconAssetReadResult loaded;
            try
            {
                loaded = await _assets.ReadLocalAssetAsync(variant.RelativePath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Report(new DockIconResolutionDiagnostic("local-asset-read", "cancelled", assetTimer.Elapsed.TotalMilliseconds));
                throw;
            }
            catch (Exception exception)
            {
                Report(new DockIconResolutionDiagnostic(
                    "local-asset-read", "failed", assetTimer.Elapsed.TotalMilliseconds, DiagnosticCode: exception.GetType().Name));
                throw;
            }
            Report(new DockIconResolutionDiagnostic(
                "local-asset-read", "completed", assetTimer.Elapsed.TotalMilliseconds, loaded.Status.ToString(), loaded.DiagnosticCode));
            if (loaded.Status == DockIconAssetReadStatus.Found && loaded.Asset is not null)
                return new DockIconResolutionResult(DockApplicationIconStatus.Found, loaded.Asset, source, role,
                    VisualState: role is null ? null : state);
            customFailure ??= loaded.DiagnosticCode ?? "custom-icon-unavailable";
        }

        return await ResolveWindowsShellFallbackAsync(item, role, role is null ? null : state, customFailure, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests the platform icon directly, for example after a UI decoder rejects a local asset.</summary>
    public ValueTask<DockIconResolutionResult> ResolveWindowsShellFallbackAsync(
        DockProjectItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ResolveWindowsShellFallbackAsync(
            item,
            FindRole(item, DockIconSelector.ForItem(item.Id), DockIconSelector.ForTarget(item.TargetIdentity)),
            null,
            null,
            cancellationToken);
    }

    private async ValueTask<DockIconResolutionResult> ResolveWindowsShellFallbackAsync(
        DockProjectItem item,
        DockIconRole? role,
        DockIconVisualState? visualState,
        string? customFailure,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        Report(new DockIconResolutionDiagnostic("windows-shell-fallback", "started", 0, role?.ToString()));
        var shell = await _applications.ResolveApplicationIconAsync(item.TargetIdentity, cancellationToken).ConfigureAwait(false);
        Report(new DockIconResolutionDiagnostic(
            "windows-shell-fallback", "completed", timer.Elapsed.TotalMilliseconds, shell.Status.ToString(), shell.DiagnosticCode));
        if (shell.Status == DockApplicationIconStatus.Found && shell.Asset is not null)
        {
            try
            {
                var asset = DockIconAsset.Create(shell.Asset.Png, DockIconFormat.Png);
                return new DockIconResolutionResult(DockApplicationIconStatus.Found, asset,
                    DockIconResolutionSource.WindowsShell, role, customFailure, visualState);
            }
            catch (ArgumentException)
            {
                return new DockIconResolutionResult(DockApplicationIconStatus.Failed, null, null, role, "shell-icon-invalid", visualState);
            }
        }

        return new DockIconResolutionResult(shell.Status, null, null, role, shell.DiagnosticCode ?? customFailure, visualState);
    }

    private void Report(DockIconResolutionDiagnostic diagnostic)
    {
        try
        {
            _diagnostics?.Invoke(diagnostic);
        }
        catch
        {
            // Optional diagnostics must not change the resolved icon or selected state.
        }
    }

    private DockIconRole? FindRole(DockProjectItem item, DockIconSelector itemSelector, DockIconSelector targetSelector)
    {
        var explicitRole = _manifest.RoleAssignments.FirstOrDefault(assignment => assignment.Selector == itemSelector)
            ?? _manifest.RoleAssignments.FirstOrDefault(assignment => assignment.Selector == targetSelector);
        if (explicitRole is not null)
            return explicitRole.Role;

        if (item.TargetIdentity.Scheme == DockIdentityScheme.WindowsSystemRole)
        {
            if (EqualsKey(item.TargetIdentity, "windows.file-manager")) return DockIconRole.Finder;
            if (EqualsKey(item.TargetIdentity, "windows.settings")) return DockIconRole.SystemSettings;
            if (EqualsKey(item.TargetIdentity, "windows.recycle-bin")) return DockIconRole.Trash;
        }
        if (item.TargetIdentity.Scheme == DockIdentityScheme.WindowsKnownFolderId
            && IsDownloadsIdentity(item.TargetIdentity))
            return DockIconRole.Downloads;
        return null;
    }

    private static bool IsDownloadsIdentity(DockTargetIdentity identity)
    {
        if (EqualsKey(identity, "downloads") || EqualsKey(identity, "folderid_downloads"))
            return true;
        return Guid.TryParse(identity.ComparisonKey, out var folderId)
            && folderId == new Guid("374DE290-123F-4565-9164-39C4925E467B");
    }

    private static DockIconAssetVariant? SelectVariant(DockIconMapping mapping, DockIconTheme requestedTheme, DockIconVisualState requestedState)
    {
        return mapping.Variants
            .Select(variant => (Variant: variant, Score: VariantScore(variant, requestedTheme, requestedState)))
            .Where(candidate => candidate.Score < int.MaxValue)
            .OrderBy(candidate => candidate.Score)
            .Select(candidate => candidate.Variant)
            .FirstOrDefault();
    }

    private static int VariantScore(DockIconAssetVariant variant, DockIconTheme theme, DockIconVisualState state)
    {
        var themeScore = variant.Theme == theme ? 0 : variant.Theme == DockIconTheme.Any ? 1 : int.MaxValue;
        var stateScore = variant.State == state ? 0
            : variant.State == DockIconVisualState.Default ? 1
            : int.MaxValue;
        return themeScore == int.MaxValue || stateScore == int.MaxValue
            ? int.MaxValue
            : themeScore * 2 + stateScore;
    }

    private static bool EqualsKey(DockTargetIdentity identity, string value) =>
        string.Equals(identity.ComparisonKey, value, StringComparison.OrdinalIgnoreCase);
}
