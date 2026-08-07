using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Features.Preview;

public sealed record PreviewVfsVersion(
    string SessionId,
    long Version,
    string Directory,
    string LevelPath,
    string ChartPath,
    string StoryboardPath,
    IReadOnlyDictionary<string, string> AssetHashes)
{
    public bool StoryboardEnabled { get; init; } = true;
    public IReadOnlyList<PreviewDiagnostic> Diagnostics { get; init; } = [];
}

public interface IPreviewVfsMaterializer
{
    Task<PreviewVfsVersion> MaterializeAsync(
        StoryboardPreviewSnapshot snapshot,
        CancellationToken cancellationToken = default);
    Task PruneAsync(string sessionId, IReadOnlySet<long> protectedVersions, long maximumBytes);
}

public sealed class PreviewVfsMaterializer : IPreviewVfsMaterializer
{
    private const string SnapshotManifestName = "snapshot.manifest.json";
    private const string AssetManifestName = "assets.manifest.json";
    private const string NormalizedImageDirectory = "__naziki_images";
    private static readonly HashSet<string> StaticImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
        };

    private readonly string _sessionsRoot;

    public PreviewVfsMaterializer(string? sessionsRoot = null)
    {
        _sessionsRoot = sessionsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NazikiEditor",
            "PreviewSessions");
    }

    public async Task<PreviewVfsVersion> MaterializeAsync(
        StoryboardPreviewSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var safeSession = SafeSegment(snapshot.SessionId);
        if (string.IsNullOrWhiteSpace(safeSession))
            throw new InvalidDataException("PREVIEW_VFS_SESSION_INVALID: sessionId contains no safe characters.");

        var sessionRoot = Path.Combine(_sessionsRoot, safeSession);
        var versionsRoot = Path.Combine(sessionRoot, "versions");
        var blobRoot = Path.Combine(sessionRoot, "blobs");
        Directory.CreateDirectory(versionsRoot);
        Directory.CreateDirectory(blobRoot);

        var versionDirectory = Path.Combine(versionsRoot,
            snapshot.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var identity = await ComputeSnapshotIdentityAsync(snapshot, cancellationToken)
            .ConfigureAwait(false);
        if (Directory.Exists(versionDirectory))
            return await LoadAndValidatePublishedVersionAsync(
                    snapshot, versionDirectory, identity, cancellationToken)
                .ConfigureAwait(false);

        var stagingDirectory = versionDirectory + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var built = await BuildVersionAsync(
                    snapshot, stagingDirectory, blobRoot, identity, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                Directory.Move(stagingDirectory, versionDirectory);
            }
            catch (IOException) when (Directory.Exists(versionDirectory))
            {
                TryDeleteStaging(stagingDirectory, versionsRoot, versionDirectory);
                return await LoadAndValidatePublishedVersionAsync(
                        snapshot, versionDirectory, identity, cancellationToken)
                    .ConfigureAwait(false);
            }

            return built with
            {
                Directory = versionDirectory,
                LevelPath = Path.Combine(versionDirectory, "level.json"),
                ChartPath = Path.Combine(versionDirectory, "chart.json"),
                StoryboardPath = Path.Combine(versionDirectory, "storyboard.json")
            };
        }
        catch
        {
            TryDeleteStaging(stagingDirectory, versionsRoot, versionDirectory);
            throw;
        }
    }

    private static async Task<PreviewVfsVersion> BuildVersionAsync(
        StoryboardPreviewSnapshot snapshot,
        string destinationRoot,
        string blobRoot,
        string identity,
        CancellationToken cancellationToken)
    {
        var chartJson = snapshot.ChartJson
            ?? throw new InvalidDataException("PREVIEW_CHART_MISSING: the Unity chart payload is missing.");
        var chartRoot = JObject.Parse(chartJson);
        var noteCount = (chartRoot["note_list"] as JArray)?.Count
            ?? throw new InvalidDataException("PREVIEW_CHART_NOTE_LIST_MISSING: chart.json does not contain note_list.");
        if (noteCount == 0)
            throw new InvalidDataException("PREVIEW_CHART_EMPTY: chart.json contains no notes.");

        var imageResult = NormalizeStoryboardImages(snapshot);
        var storyboardPath = Path.Combine(destinationRoot, "storyboard.json");
        var chartPath = Path.Combine(destinationRoot, "chart.json");
        var levelPath = Path.Combine(destinationRoot, "level.json");
        await AtomicWriteAsync(storyboardPath, imageResult.Json, cancellationToken).ConfigureAwait(false);
        await AtomicWriteAsync(chartPath, chartJson, cancellationToken).ConfigureAwait(false);

        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var musicName = await LinkOrCopyRequiredAssetAsync(
                snapshot.MusicPath, destinationRoot, blobRoot, "music", hashes, cancellationToken)
            .ConfigureAwait(false);
        var backgroundName = await NormalizeRequiredBackgroundAsync(
                snapshot.BackgroundPath, destinationRoot, blobRoot, hashes, cancellationToken)
            .ConfigureAwait(false);

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "storyboard.json",
            "chart.json",
            "level.json",
            AssetManifestName,
            SnapshotManifestName,
            musicName,
            backgroundName
        };
        await MaterializeAssetRootAsync(
                snapshot.AssetRoot, destinationRoot, blobRoot, hashes, reserved, cancellationToken)
            .ConfigureAwait(false);
        foreach (var image in imageResult.Images
                     .GroupBy(item => item.RuntimePath, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var destination = ResolveContainedPath(destinationRoot, image.RuntimePath,
                "PREVIEW_VFS_IMAGE_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            hashes[image.RuntimePath] = await MaterializeImmutableBytesAsync(
                    image.PngBytes, destination, blobRoot, ".png", cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(snapshot.LevelJson))
            throw new InvalidDataException("PREVIEW_LEVEL_MISSING: the project level file is missing.");
        var level = JObject.Parse(snapshot.LevelJson);
        var charts = level["charts"] as JArray
            ?? throw new InvalidDataException("PREVIEW_LEVEL_CHARTS_MISSING: level.json does not contain charts.");
        if (string.IsNullOrWhiteSpace(snapshot.ChartDifficulty))
            throw new InvalidDataException(
                "LEVEL_CHART_BINDING_MISSING: the project chart difficulty has not been resolved.");
        var selectedCharts = charts.OfType<JObject>().Where(chart =>
            string.Equals(chart.Value<string>("type"), snapshot.ChartDifficulty,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selectedCharts.Length != 1)
            throw new InvalidDataException(selectedCharts.Length == 0
                ? $"LEVEL_CHART_BINDING_NOT_FOUND: level.json does not contain difficulty '{snapshot.ChartDifficulty}'."
                : $"LEVEL_CHART_BINDING_AMBIGUOUS: level.json contains multiple '{snapshot.ChartDifficulty}' entries.");
        var selectedChart = (JObject)selectedCharts[0].DeepClone();

        SetSectionPath(level, "music", musicName);
        SetSectionPath(level, "music_preview", musicName);
        SetSectionPath(level, "background", backgroundName);
        selectedChart["path"] = "chart.json";
        SetSectionPath(selectedChart, "music_override", musicName);
        SetSectionPath(selectedChart, "storyboard", "storyboard.json");
        level["charts"] = new JArray(selectedChart);
        await AtomicWriteAsync(levelPath, level.ToString(Formatting.Indented), cancellationToken)
            .ConfigureAwait(false);
        await AtomicWriteAsync(
                Path.Combine(destinationRoot, AssetManifestName),
                JsonConvert.SerializeObject(hashes, Formatting.Indented),
                cancellationToken)
            .ConfigureAwait(false);

        var files = await BuildFileManifestAsync(destinationRoot, cancellationToken)
            .ConfigureAwait(false);
        var manifest = new SnapshotManifest
        {
            Identity = identity,
            Files = files,
            AssetHashes = hashes,
            StoryboardEnabled = imageResult.Enabled,
            Diagnostics = imageResult.Diagnostics.ToList()
        };
        await AtomicWriteAsync(
                Path.Combine(destinationRoot, SnapshotManifestName),
                JsonConvert.SerializeObject(manifest, Formatting.Indented),
                cancellationToken)
            .ConfigureAwait(false);

        return new PreviewVfsVersion(
            snapshot.SessionId,
            snapshot.Version,
            destinationRoot,
            levelPath,
            chartPath,
            storyboardPath,
            hashes)
        {
            StoryboardEnabled = imageResult.Enabled,
            Diagnostics = imageResult.Diagnostics
        };
    }

    private static StoryboardImageNormalizationResult NormalizeStoryboardImages(
        StoryboardPreviewSnapshot snapshot)
    {
        if (!snapshot.StoryboardEnabled || string.IsNullOrWhiteSpace(snapshot.StoryboardJson))
            return new StoryboardImageNormalizationResult("{}", false, [], []);

        JToken root;
        try
        {
            root = JToken.Parse(snapshot.StoryboardJson);
        }
        catch (Exception ex)
        {
            return StoryboardImageNormalizationResult.Failed(new PreviewDiagnostic(
                "PREVIEW_STORYBOARD_JSON_INVALID",
                $"故事板 JSON 无法解析，已降级为仅谱面预览：{ex.Message}",
                PreviewDiagnosticSeverity.Error,
                PreviewDiagnosticSource.Storyboard,
                "$")
            {
                Impact = PreviewDiagnosticImpact.StoryboardOnly,
                Stage = "normalize-assets",
                StackTrace = ex.ToString()
            });
        }

        var images = new List<NormalizedImage>();
        var diagnostics = new List<PreviewDiagnostic>();
        foreach (var property in Traverse(root).OfType<JProperty>()
                     .Where(item => string.Equals(item.Name, "path", StringComparison.OrdinalIgnoreCase)))
        {
            var reference = property.Value.Value<string>();
            if (string.IsNullOrWhiteSpace(reference) ||
                reference.Contains("://", StringComparison.Ordinal) ||
                !StaticImageExtensions.Contains(Path.GetExtension(reference)))
                continue;

            var jsonPath = string.IsNullOrWhiteSpace(property.Path) ? "$" : $"$.{property.Path}";
            var entityId = property.Ancestors().OfType<JObject>()
                .Select(owner => owner.Value<string>("id"))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            try
            {
                if (string.IsNullOrWhiteSpace(snapshot.AssetRoot) ||
                    !Directory.Exists(snapshot.AssetRoot))
                    throw new PreviewAssetException(
                        "PREVIEW_ASSET_ROOT_MISSING", reference,
                        "The project asset root is missing.");
                var source = ResolveContainedPath(snapshot.AssetRoot, reference,
                    "PREVIEW_ASSET_PATH_ESCAPE");
                if (!File.Exists(source))
                    throw new PreviewAssetException(
                        "PREVIEW_ASSET_NOT_FOUND", source,
                        $"Storyboard image '{reference}' does not exist.");

                var pngBytes = DecodeBgra32Png(source);
                var hash = HashBytes(pngBytes);
                var runtimePath = $"{NormalizedImageDirectory}/{hash}.png";
                property.Value = runtimePath;
                images.Add(new NormalizedImage(runtimePath, pngBytes));
            }
            catch (Exception ex)
            {
                var assetException = ex as PreviewAssetException;
                diagnostics.Add(new PreviewDiagnostic(
                    assetException?.Code ?? "PREVIEW_ASSET_DECODE_FAILED",
                    $"故事板图片“{reference}”无法用于预览，已降级为仅谱面预览：{ex.Message}",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Asset,
                    jsonPath,
                    entityId)
                {
                    Impact = PreviewDiagnosticImpact.StoryboardOnly,
                    Stage = "normalize-assets",
                    StackTrace = ex.ToString()
                });
            }
        }

        if (diagnostics.Count > 0)
            return new StoryboardImageNormalizationResult("{}", false, [], diagnostics);
        return new StoryboardImageNormalizationResult(
            root.ToString(Formatting.None), true, images, []);
    }

    private static byte[] DecodeBgra32Png(string source)
    {
        try
        {
            using var input = new FileStream(
                source, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(
                input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                throw new InvalidDataException("WIC returned no image frames.");
            BitmapSource bitmap = decoder.Frames[0];
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                throw new InvalidDataException("WIC returned an empty image.");
            if (bitmap.Format != PixelFormats.Bgra32)
                bitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            if (bitmap.CanFreeze)
                bitmap.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream();
            encoder.Save(output);
            if (output.Length == 0)
                throw new InvalidDataException("WIC produced an empty PNG.");
            return output.ToArray();
        }
        catch (Exception ex) when (ex is not PreviewAssetException)
        {
            throw new PreviewAssetException(
                "PREVIEW_ASSET_DECODE_FAILED", source,
                $"WIC could not decode '{source}' as a static image.", ex);
        }
    }

    private static async Task<string> NormalizeRequiredBackgroundAsync(
        string? source,
        string destinationRoot,
        string blobRoot,
        IDictionary<string, string> hashes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            throw new InvalidDataException(
                $"PREVIEW_BACKGROUND_MISSING: required background file '{source}' does not exist.");
        byte[] pngBytes;
        try
        {
            pngBytes = DecodeBgra32Png(source);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"PREVIEW_BACKGROUND_DECODE_FAILED: background '{source}' could not be decoded.", ex);
        }
        const string fileName = "background.png";
        var destination = Path.Combine(destinationRoot, fileName);
        hashes[fileName] = await MaterializeImmutableBytesAsync(
                pngBytes, destination, blobRoot, ".png", cancellationToken)
            .ConfigureAwait(false);
        return fileName;
    }

    private static async Task MaterializeAssetRootAsync(
        string? assetRoot,
        string destinationRoot,
        string blobRoot,
        IDictionary<string, string> hashes,
        IReadOnlySet<string> reserved,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetRoot) || !Directory.Exists(assetRoot))
            return;
        var normalizedRoot = Path.GetFullPath(assetRoot);
        var sources = Directory.EnumerateFiles(normalizedRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(normalizedRoot, path), StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeRelativePath(Path.GetRelativePath(normalizedRoot, source));
            if (relative.StartsWith("../", StringComparison.Ordinal) || relative == "..")
                throw new InvalidDataException(
                    $"PREVIEW_ASSET_PATH_ESCAPE: asset '{source}' escapes '{normalizedRoot}'.");
            if (reserved.Contains(relative) ||
                relative.StartsWith(NormalizedImageDirectory + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"PREVIEW_VFS_RESERVED_NAME_COLLISION: asset '{source}' conflicts with reserved VFS path '{relative}'.");

            var destination = ResolveContainedPath(destinationRoot, relative,
                "PREVIEW_VFS_ASSET_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            hashes[relative] = await MaterializeImmutableAssetAsync(
                    source, destination, blobRoot, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<string> LinkOrCopyRequiredAssetAsync(
        string? source,
        string destinationRoot,
        string blobRoot,
        string baseName,
        IDictionary<string, string> hashes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            throw new InvalidDataException(
                $"PREVIEW_REQUIRED_ASSET_MISSING: required asset '{source}' does not exist.");
        var fileName = baseName + Path.GetExtension(source).ToLowerInvariant();
        var destination = Path.Combine(destinationRoot, fileName);
        hashes[fileName] = await MaterializeImmutableAssetAsync(
                source, destination, blobRoot, cancellationToken)
            .ConfigureAwait(false);
        return fileName;
    }

    private static async Task<string> MaterializeImmutableAssetAsync(
        string source,
        string destination,
        string blobRoot,
        CancellationToken cancellationToken)
    {
        var hash = await HashFileAsync(source, cancellationToken).ConfigureAwait(false);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        var blobPath = Path.Combine(blobRoot, hash + extension);
        if (!File.Exists(blobPath))
        {
            var temporary = blobPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 81920, true))
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            PublishBlob(temporary, blobPath);
        }
        await LinkOrCopyAsync(blobPath, destination, cancellationToken).ConfigureAwait(false);
        return hash;
    }

    private static async Task<string> MaterializeImmutableBytesAsync(
        byte[] contents,
        string destination,
        string blobRoot,
        string extension,
        CancellationToken cancellationToken)
    {
        var hash = HashBytes(contents);
        var blobPath = Path.Combine(blobRoot, hash + extension);
        if (!File.Exists(blobPath))
        {
            var temporary = blobPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temporary, contents, cancellationToken).ConfigureAwait(false);
            PublishBlob(temporary, blobPath);
        }
        await LinkOrCopyAsync(blobPath, destination, cancellationToken).ConfigureAwait(false);
        return hash;
    }

    private static void PublishBlob(string temporary, string blobPath)
    {
        try
        {
            File.Move(temporary, blobPath);
        }
        catch (IOException) when (File.Exists(blobPath))
        {
            File.Delete(temporary);
        }
    }

    private static async Task LinkOrCopyAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
        {
            var sourceHash = await HashFileAsync(source, cancellationToken).ConfigureAwait(false);
            var destinationHash = await HashFileAsync(destination, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(sourceHash, destinationHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"PREVIEW_VFS_ASSET_COLLISION: '{destination}' already exists with different content.");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                CreateHardLink(destination, source, IntPtr.Zero))
                return;
        }
        catch
        {
            // Fall back to an ordinary copy when hard links are unavailable.
        }
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 81920, true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PreviewVfsVersion> LoadAndValidatePublishedVersionAsync(
        StoryboardPreviewSnapshot snapshot,
        string versionDirectory,
        string expectedIdentity,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(versionDirectory, SnapshotManifestName);
        if (!File.Exists(manifestPath))
            throw new InvalidDataException(
                $"PREVIEW_VFS_MANIFEST_MISSING: immutable version '{versionDirectory}' has no {SnapshotManifestName}.");
        SnapshotManifest manifest;
        try
        {
            manifest = JsonConvert.DeserializeObject<SnapshotManifest>(
                           await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false))
                       ?? throw new JsonException("Manifest deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new InvalidDataException(
                $"PREVIEW_VFS_MANIFEST_INVALID: '{manifestPath}' is invalid.", ex);
        }
        if (!string.Equals(manifest.Identity, expectedIdentity, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"PREVIEW_VFS_VERSION_CONFLICT: version '{snapshot.Version}' was already published with different content.");

        var actualFiles = Directory.EnumerateFiles(versionDirectory, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(versionDirectory, path)))
            .Where(path => !string.Equals(path, SnapshotManifestName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var declaredFiles = manifest.Files.Keys
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!actualFiles.SequenceEqual(declaredFiles, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"PREVIEW_VFS_MANIFEST_INCOMPLETE: file set for '{versionDirectory}' differs from its manifest.");
        foreach (var pair in manifest.Files)
        {
            var path = ResolveContainedPath(versionDirectory, pair.Key,
                "PREVIEW_VFS_MANIFEST_PATH_ESCAPE");
            var hash = await HashFileAsync(path, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, pair.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"PREVIEW_VFS_HASH_MISMATCH: '{path}' does not match its manifest hash.");
        }

        var assetManifestPath = Path.Combine(versionDirectory, AssetManifestName);
        var assetHashes = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                              await File.ReadAllTextAsync(assetManifestPath, cancellationToken).ConfigureAwait(false))
                          ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!DictionaryEqual(assetHashes, manifest.AssetHashes))
            throw new InvalidDataException(
                $"PREVIEW_VFS_ASSET_MANIFEST_INCOMPLETE: '{assetManifestPath}' differs from the snapshot manifest.");

        return new PreviewVfsVersion(
            snapshot.SessionId,
            snapshot.Version,
            versionDirectory,
            Path.Combine(versionDirectory, "level.json"),
            Path.Combine(versionDirectory, "chart.json"),
            Path.Combine(versionDirectory, "storyboard.json"),
            new Dictionary<string, string>(assetHashes, StringComparer.OrdinalIgnoreCase))
        {
            StoryboardEnabled = manifest.StoryboardEnabled,
            Diagnostics = manifest.Diagnostics
        };
    }

    private static bool DictionaryEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out var value) &&
            string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase));

    private static async Task<Dictionary<string, string>> BuildFileManifestAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase))
        {
            var relative = NormalizeRelativePath(Path.GetRelativePath(root, file));
            if (string.Equals(relative, SnapshotManifestName, StringComparison.OrdinalIgnoreCase))
                continue;
            result[relative] = await HashFileAsync(file, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    private static async Task<string> ComputeSnapshotIdentityAsync(
        StoryboardPreviewSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var files = new JArray();
        async Task AddFileAsync(string kind, string? root, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                files.Add(new JObject { ["kind"] = kind, ["missing"] = path ?? string.Empty });
                return;
            }
            files.Add(new JObject
            {
                ["kind"] = kind,
                ["path"] = root is null
                    ? Path.GetFileName(path)
                    : NormalizeRelativePath(Path.GetRelativePath(root, path)),
                ["hash"] = await HashFileAsync(path, cancellationToken).ConfigureAwait(false)
            });
        }

        await AddFileAsync("music", null, snapshot.MusicPath).ConfigureAwait(false);
        await AddFileAsync("background", null, snapshot.BackgroundPath).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(snapshot.AssetRoot) && Directory.Exists(snapshot.AssetRoot))
        {
            var root = Path.GetFullPath(snapshot.AssetRoot);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase))
                await AddFileAsync("asset", root, file).ConfigureAwait(false);
        }
        else
        {
            files.Add(new JObject { ["kind"] = "asset-root", ["missing"] = snapshot.AssetRoot ?? string.Empty });
        }

        var identity = new JObject
        {
            ["storyboard"] = HashText(snapshot.StoryboardJson),
            ["storyboardEnabled"] = snapshot.StoryboardEnabled,
            ["chart"] = HashText(snapshot.ChartJson ?? string.Empty),
            ["level"] = HashText(snapshot.LevelJson ?? string.Empty),
            ["difficulty"] = snapshot.ChartDifficulty ?? string.Empty,
            ["files"] = files
        };
        return HashText(identity.ToString(Formatting.None));
    }

    private static void SetSectionPath(JObject owner, string property, string? path)
    {
        var section = owner[property] as JObject ?? new JObject();
        section["path"] = path;
        owner[property] = section;
    }

    private static string ResolveContainedPath(string root, string relative, string errorCode)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(
            normalizedRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new PreviewAssetException(errorCode, relative,
                $"Path '{relative}' escapes root '{normalizedRoot}'.");
        return fullPath;
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static IEnumerable<JToken> Traverse(JToken token)
    {
        yield return token;
        if (token is not JContainer container)
            yield break;
        foreach (var child in container.Children())
        foreach (var nested in Traverse(child))
            yield return nested;
    }

    private static async Task AtomicWriteAsync(
        string destination,
        string contents,
        CancellationToken cancellationToken)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, contents, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, destination, true);
    }

    private static async Task<string> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string HashBytes(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string HashText(string value) =>
        HashBytes(Encoding.UTF8.GetBytes(value));

    private static void TryDeleteStaging(
        string stagingDirectory,
        string versionsRoot,
        string versionDirectory)
    {
        try
        {
            var fullStaging = Path.GetFullPath(stagingDirectory);
            var fullVersions = Path.GetFullPath(versionsRoot);
            var expectedPrefix = Path.GetFullPath(versionDirectory) + ".staging-";
            if (!string.Equals(Path.GetDirectoryName(fullStaging), fullVersions,
                    StringComparison.OrdinalIgnoreCase) ||
                !fullStaging.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                return;
            if (Directory.Exists(fullStaging))
                Directory.Delete(fullStaging, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public Task PruneAsync(
        string sessionId,
        IReadOnlySet<long> protectedVersions,
        long maximumBytes)
    {
        return Task.Run(() =>
        {
            var sessionRoot = Path.Combine(_sessionsRoot, SafeSegment(sessionId));
            var root = Path.Combine(sessionRoot, "versions");
            if (!Directory.Exists(root))
                return;
            var directories = new DirectoryInfo(root).EnumerateDirectories()
                .Where(directory => !directory.Name.Contains(".staging-", StringComparison.Ordinal))
                .Select(directory => new
                {
                    Directory = directory,
                    Version = long.TryParse(directory.Name, out var version) ? version : -1,
                    Bytes = GetDirectorySize(directory)
                })
                .OrderByDescending(item => item.Version)
                .ToArray();
            var total = GetDirectorySize(new DirectoryInfo(sessionRoot));
            foreach (var item in directories.OrderBy(entry => entry.Version))
            {
                if (total <= maximumBytes)
                    break;
                if (item.Version >= 0 && protectedVersions.Contains(item.Version))
                    continue;
                try
                {
                    item.Directory.Delete(true);
                    total -= item.Bytes;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            if (total <= maximumBytes)
                return;
            var referencedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var manifest in Directory.EnumerateFiles(root, AssetManifestName, SearchOption.AllDirectories))
            {
                try
                {
                    var values = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(manifest));
                    if (values is not null)
                        referencedHashes.UnionWith(values.Values);
                }
                catch (JsonException) { }
                catch (IOException) { }
            }
            var blobRoot = Path.Combine(sessionRoot, "blobs");
            if (!Directory.Exists(blobRoot))
                return;
            foreach (var blob in new DirectoryInfo(blobRoot).EnumerateFiles()
                         .OrderBy(file => file.LastWriteTimeUtc))
            {
                if (total <= maximumBytes)
                    break;
                if (referencedHashes.Contains(Path.GetFileNameWithoutExtension(blob.Name)))
                    continue;
                try
                {
                    var length = blob.Length;
                    blob.Delete();
                    total -= length;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        });
    }

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(file => file.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static string SafeSegment(string value) =>
        string.Concat(value.Where(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_'));

    private sealed record NormalizedImage(string RuntimePath, byte[] PngBytes);

    private sealed record StoryboardImageNormalizationResult(
        string Json,
        bool Enabled,
        IReadOnlyList<NormalizedImage> Images,
        IReadOnlyList<PreviewDiagnostic> Diagnostics)
    {
        public static StoryboardImageNormalizationResult Failed(PreviewDiagnostic diagnostic) =>
            new("{}", false, [], [diagnostic]);
    }

    private sealed class SnapshotManifest
    {
        public string Identity { get; set; } = string.Empty;
        public Dictionary<string, string> Files { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> AssetHashes { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public bool StoryboardEnabled { get; set; } = true;
        public List<PreviewDiagnostic> Diagnostics { get; set; } = [];
    }

    private sealed class PreviewAssetException : IOException
    {
        public PreviewAssetException(string code, string resourcePath, string message,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
            ResourcePath = resourcePath;
        }

        public string Code { get; }
        public string ResourcePath { get; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);
}
