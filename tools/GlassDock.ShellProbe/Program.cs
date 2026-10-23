using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using GlassDock.Core;
using GlassDock.Windows.Shell;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("This probe requires Windows. No Shell actions were performed.");
    return 2;
}

try
{
    var repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
    var evidenceDirectory = Path.Combine(repositoryRoot, ".local", "evidence", "windows-shell-discovery");
    var iconDirectory = Path.Combine(evidenceDirectory, "icons");
    Directory.CreateDirectory(iconDirectory);

    var provider = new WindowsShellProvider();
    var catalog = await provider.DiscoverApplicationsAsync();
    if (catalog.Status != WindowsShellCatalogStatus.Succeeded)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            status = catalog.Status.ToString(),
            catalog.DiagnosticCode,
            action = "Discovery only; no application was launched or activated."
        }));
        return 1;
    }

    var iconReports = new List<object>();
    var iconFoundCount = 0;
    var iconCandidates = catalog.Applications.Take(11).ToList();
    var appsFolderOnly = catalog.Applications.FirstOrDefault(application =>
        application.IconSource.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase));
    if (appsFolderOnly is not null && iconCandidates.All(application => application.TargetIdentity != appsFolderOnly.TargetIdentity))
        iconCandidates.Add(appsFolderOnly);
    var iconsToProbe = iconCandidates.Count;
    for (var i = 0; i < iconCandidates.Count; i++)
    {
        var application = iconCandidates[i];
        var iconResult = await provider.ResolveIconAsync(application.TargetIdentity);
        if (iconResult.Status != WindowsShellIconStatus.Found || iconResult.Asset is null)
        {
            iconReports.Add(new { status = iconResult.Status.ToString(), iconResult.DiagnosticCode });
            continue;
        }

        iconFoundCount++;
        var png = iconResult.Asset.EncodedPng.ToArray();
        var alpha = CountAlphaPixels(png);
        var sourceKind = application.IconSource.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase)
            ? "AppsFolder"
            : "StartMenuShortcut";
        var iconName = $"icon-{i + 1:D2}-{Convert.ToHexString(SHA256.HashData(png))[..12]}.png";
        var iconPath = Path.Combine(iconDirectory, iconName);
        await File.WriteAllBytesAsync(iconPath, png);
        iconReports.Add(new
        {
            status = iconResult.Status.ToString(),
            sourceKind,
            file = $"icons/{iconName}",
            sha256 = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant(),
            width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)),
            height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)),
            bytes = png.Length,
            transparentPixels = alpha.Transparent,
            partiallyTransparentPixels = alpha.Partial,
            opaquePixels = alpha.Opaque
        });
    }

    var report = new
    {
        observedAtUtc = DateTimeOffset.UtcNow,
        operatingSystem = Environment.OSVersion.Version.ToString(),
        runtime = RuntimeInformation.FrameworkDescription,
        status = catalog.Status.ToString(),
        catalog.ScannedShortcutCount,
        catalog.ScannedAumidEntryCount,
        discoveredApplications = catalog.Applications.Count,
        appUserModelIdApplications = catalog.Applications.Count(item =>
            item.TargetIdentity.Scheme == DockIdentityScheme.WindowsAppUserModelId),
        classicShellLinkApplications = catalog.Applications.Count(item =>
            item.TargetIdentity.Scheme == DockIdentityScheme.WindowsShellLinkPath),
        catalog.DuplicateCount,
        issueCounts = catalog.Issues.GroupBy(issue => issue.DiagnosticCode)
            .ToDictionary(group => group.Key, group => group.Count()),
        iconProbeCount = iconsToProbe,
        icons = iconReports,
        scope = "Current-user and common Start Menu Programs known folders only.",
        actions = "No application launch, activation, window enumeration, input injection, or system setting changes."
    };

    var reportPath = Path.Combine(evidenceDirectory, "summary.json");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        status = catalog.Status.ToString(),
        catalog.ScannedShortcutCount,
        catalog.ScannedAumidEntryCount,
        discoveredApplications = catalog.Applications.Count,
        appUserModelIdApplications = catalog.Applications.Count(item =>
            item.TargetIdentity.Scheme == DockIdentityScheme.WindowsAppUserModelId),
        classicShellLinkApplications = catalog.Applications.Count(item =>
            item.TargetIdentity.Scheme == DockIdentityScheme.WindowsShellLinkPath),
        catalog.DuplicateCount,
        iconFound = iconFoundCount,
        evidence = Path.GetRelativePath(repositoryRoot, reportPath),
        actions = "Read-only discovery/icon extraction. No application was launched or activated."
    }));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Shell probe failed: {exception.GetType().Name}");
    return 1;
}

static string FindRepositoryRoot(string startPath)
{
    var directory = new DirectoryInfo(Path.GetFullPath(startPath));
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
            || File.Exists(Path.Combine(directory.FullName, ".git")))
        {
            return directory.FullName;
        }
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Run this probe from within the repository checkout.");
}

static (long Transparent, long Partial, long Opaque) CountAlphaPixels(byte[] png)
{
    var width = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)));
    var height = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)));
    using var compressed = new MemoryStream();
    var offset = 8;
    while (offset + 12 <= png.Length)
    {
        var chunkLength = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
        var chunkType = png.AsSpan(offset + 4, 4);
        var dataOffset = offset + 8;
        if (dataOffset + chunkLength + 4 > png.Length)
            throw new InvalidDataException("PNG chunk exceeds the file bounds.");
        if (chunkType.SequenceEqual("IDAT"u8))
            compressed.Write(png, dataOffset, chunkLength);
        offset = dataOffset + chunkLength + 4;
        if (chunkType.SequenceEqual("IEND"u8))
            break;
    }

    compressed.Position = 0;
    using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
    using var decodedStream = new MemoryStream();
    zlib.CopyTo(decodedStream);
    var decoded = decodedStream.ToArray();
    var stride = checked(width * 4);
    if (decoded.Length != checked((stride + 1) * height))
        throw new InvalidDataException("PNG decoded byte count is invalid.");

    long transparent = 0;
    long partial = 0;
    long opaque = 0;
    for (var y = 0; y < height; y++)
    {
        var rowOffset = y * (stride + 1);
        if (decoded[rowOffset] != 0)
            throw new InvalidDataException("Probe PNG uses an unsupported row filter.");
        for (var pixel = 0; pixel < width; pixel++)
        {
            var alpha = decoded[rowOffset + 1 + pixel * 4 + 3];
            if (alpha == 0) transparent++;
            else if (alpha == 255) opaque++;
            else partial++;
        }
    }
    return (transparent, partial, opaque);
}
