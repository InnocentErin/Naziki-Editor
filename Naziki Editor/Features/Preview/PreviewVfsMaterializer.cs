using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    IReadOnlyDictionary<string, string> AssetHashes);

public interface IPreviewVfsMaterializer
{
    Task<PreviewVfsVersion> MaterializeAsync(
        StoryboardPreviewSnapshot snapshot,
        CancellationToken cancellationToken = default);
    Task PruneAsync(string sessionId, IReadOnlySet<long> protectedVersions, long maximumBytes);
}

public sealed class PreviewVfsMaterializer : IPreviewVfsMaterializer
{
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
        var versionDirectory = Path.Combine(
            _sessionsRoot,
            SafeSegment(snapshot.SessionId),
            "versions",
            snapshot.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(versionDirectory);
        var blobRoot = Path.Combine(
            _sessionsRoot,
            SafeSegment(snapshot.SessionId),
            "blobs");
        Directory.CreateDirectory(blobRoot);

        var storyboardPath = Path.Combine(versionDirectory, "storyboard.json");
        var chartPath = Path.Combine(versionDirectory, "chart.json");
        var levelPath = Path.Combine(versionDirectory, "level.json");
        await AtomicWriteAsync(storyboardPath, snapshot.StoryboardJson, cancellationToken).ConfigureAwait(false);
        await AtomicWriteAsync(chartPath, snapshot.ChartJson ?? "{}", cancellationToken).ConfigureAwait(false);

        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var musicName = await LinkOrCopyAssetAsync(snapshot.MusicPath, versionDirectory, blobRoot, "music", hashes, cancellationToken)
            .ConfigureAwait(false);
        var backgroundName = await LinkOrCopyAssetAsync(snapshot.BackgroundPath, versionDirectory, blobRoot, "background", hashes, cancellationToken)
            .ConfigureAwait(false);
        await MaterializeAssetRootAsync(snapshot.AssetRoot, versionDirectory, blobRoot, hashes, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(snapshot.LevelJson))
            throw new InvalidDataException("The project level file is missing.");
        var level = JObject.Parse(snapshot.LevelJson);
        var charts = level["charts"] as JArray
            ?? throw new InvalidDataException("The level file does not contain charts.");
        var selectedChart = charts.OfType<JObject>().FirstOrDefault(chart =>
                                string.Equals(chart.Value<string>("type"), "hard", StringComparison.OrdinalIgnoreCase))
                            ?? charts.OfType<JObject>().FirstOrDefault(chart =>
                                chart.Value<string>("type") is "easy" or "extreme")
                            ?? throw new InvalidDataException("The level file has no supported chart.");

        level["music"] = new JObject { ["path"] = musicName };
        level["music_preview"] = new JObject { ["path"] = musicName };
        level["background"] = new JObject { ["path"] = backgroundName };
        selectedChart["path"] = "chart.json";
        selectedChart["music_override"] = new JObject { ["path"] = musicName };
        selectedChart["storyboard"] = new JObject
        {
            ["path"] = "storyboard.json",
            ["localizations"] = new JObject()
        };
        await AtomicWriteAsync(levelPath, level.ToString(Formatting.Indented), cancellationToken).ConfigureAwait(false);
        await AtomicWriteAsync(
            Path.Combine(versionDirectory, "assets.manifest.json"),
            JsonConvert.SerializeObject(hashes, Formatting.Indented),
            cancellationToken).ConfigureAwait(false);

        return new PreviewVfsVersion(
            snapshot.SessionId,
            snapshot.Version,
            versionDirectory,
            levelPath,
            chartPath,
            storyboardPath,
            hashes);
    }

    public Task PruneAsync(string sessionId, IReadOnlySet<long> protectedVersions, long maximumBytes)
    {
        return Task.Run(() =>
        {
            var sessionRoot = Path.Combine(_sessionsRoot, SafeSegment(sessionId));
            var root = Path.Combine(sessionRoot, "versions");
            if (!Directory.Exists(root))
                return;
            var directories = new DirectoryInfo(root).EnumerateDirectories()
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
            foreach (var manifest in Directory.EnumerateFiles(root, "assets.manifest.json", SearchOption.AllDirectories))
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

    private static async Task MaterializeAssetRootAsync(
        string? assetRoot,
        string destinationRoot,
        string blobRoot,
        IDictionary<string, string> hashes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetRoot) || !Directory.Exists(assetRoot))
            return;
        var normalizedRoot = Path.GetFullPath(assetRoot);
        foreach (var source in Directory.EnumerateFiles(normalizedRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(normalizedRoot, source);
            if (relative.StartsWith("..", StringComparison.Ordinal))
                continue;
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
                continue; // Reserved VFS files win over conflicting material names.
            hashes[Path.GetRelativePath(destinationRoot, destination).Replace('\\', '/')] =
                await MaterializeImmutableAssetAsync(source, destination, blobRoot, cancellationToken)
                    .ConfigureAwait(false);
        }
    }

    private static async Task<string?> LinkOrCopyAssetAsync(
        string? source,
        string destinationRoot,
        string blobRoot,
        string baseName,
        IDictionary<string, string> hashes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            return null;
        var fileName = baseName + Path.GetExtension(source).ToLowerInvariant();
        var destination = Path.Combine(destinationRoot, fileName);
        hashes[fileName] = await MaterializeImmutableAssetAsync(source, destination, blobRoot, cancellationToken)
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
        var blobPath = Path.Combine(blobRoot, hash + Path.GetExtension(source).ToLowerInvariant());
        if (!File.Exists(blobPath))
        {
            var temporary = blobPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             true))
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            try { File.Move(temporary, blobPath); }
            catch (IOException) when (File.Exists(blobPath)) { File.Delete(temporary); }
        }
        await LinkOrCopyAsync(blobPath, destination, cancellationToken).ConfigureAwait(false);
        return hash;
    }

    private static async Task LinkOrCopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
            return;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                CreateHardLink(destination, source, IntPtr.Zero))
                return;
        }
        catch { }
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AtomicWriteAsync(string destination, string contents, CancellationToken cancellationToken)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, contents, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, destination, true);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        try { return directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length); }
        catch { return 0; }
    }

    private static string SafeSegment(string value) =>
        string.Concat(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_'));

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
