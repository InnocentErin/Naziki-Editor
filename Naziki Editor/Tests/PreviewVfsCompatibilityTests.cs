using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Naziki_Editor.Features.Preview;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class PreviewVfsCompatibilityTests
{
    [Fact]
    public async Task Materialize_NormalizesPngAndJpegWithUnicodeNamesAndReusesManifest()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var assets = Directory.CreateDirectory(Path.Combine(root, "素材")).FullName;
            WriteImage(Path.Combine(assets, "红色 图片.jpg"), Colors.Red, jpeg: true);
            WriteImage(Path.Combine(assets, "绿色图片.png"), Colors.Green, jpeg: false);
            var background = Path.Combine(root, "背景.jpg");
            WriteImage(background, Colors.Blue, jpeg: true);
            var music = Path.Combine(root, "music.ogg");
            await File.WriteAllBytesAsync(music, [0x4f, 0x67, 0x67, 0x53]);
            var snapshot = Snapshot(
                "unicode-" + Guid.NewGuid().ToString("N"),
                1,
                assets,
                music,
                background,
                """
                {
                  "sprites": [
                    {"id":"jpeg","time":0,"path":"红色 图片.jpg"},
                    {"id":"png","time":0,"path":"绿色图片.png"}
                  ]
                }
                """);
            var materializer = new PreviewVfsMaterializer(Path.Combine(root, "cache"));

            var first = await materializer.MaterializeAsync(snapshot);
            var second = await materializer.MaterializeAsync(snapshot);

            Assert.True(first.StoryboardEnabled);
            Assert.Empty(first.Diagnostics);
            Assert.Equal(first.Directory, second.Directory);
            Assert.Equal(first.AssetHashes.Count, second.AssetHashes.Count);
            Assert.All(first.AssetHashes, pair =>
                Assert.Equal(pair.Value, second.AssetHashes[pair.Key]));

            var storyboard = JObject.Parse(await File.ReadAllTextAsync(first.StoryboardPath));
            var runtimePaths = storyboard["sprites"]!.Values<string>("path").ToArray();
            Assert.Equal(2, runtimePaths.Length);
            Assert.All(runtimePaths, path =>
                Assert.Matches("^__naziki_images/[0-9a-f]{64}\\.png$", path));
            Assert.Equal(2, runtimePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var runtimePath in runtimePaths)
            {
                Assert.NotNull(runtimePath);
                var normalized = Path.Combine(first.Directory,
                    runtimePath!.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(normalized));
                var frame = ReadFirstFrame(normalized);
                Assert.Equal(3, frame.PixelWidth);
                Assert.Equal(2, frame.PixelHeight);
                var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
                frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
                Assert.Contains(pixels, value => value < 240);
            }

            var level = JObject.Parse(await File.ReadAllTextAsync(first.LevelPath));
            Assert.Equal("background.png", level["background"]!.Value<string>("path"));
            Assert.Equal(3, ReadFirstFrame(Path.Combine(first.Directory, "background.png")).PixelWidth);

            var manifestPath = Path.Combine(first.Directory, "snapshot.manifest.json");
            var manifest = JObject.Parse(await File.ReadAllTextAsync(manifestPath));
            var declaredFiles = ((JObject)manifest["Files"]!).Properties()
                .Select(property => property.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualFiles = Directory.EnumerateFiles(first.Directory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(first.Directory, path).Replace('\\', '/'))
                .Where(path => !string.Equals(path, "snapshot.manifest.json",
                    StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(actualFiles, declaredFiles);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Materialize_CorruptStoryboardImageFallsBackToChartOnly()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var assets = Directory.CreateDirectory(Path.Combine(root, "assets")).FullName;
            await File.WriteAllBytesAsync(Path.Combine(assets, "broken.png"),
                [0x89, 0x50, 0x4e, 0x47, 0x00]);
            var background = Path.Combine(root, "background.png");
            WriteImage(background, Colors.Black, jpeg: false);
            var music = Path.Combine(root, "music.ogg");
            await File.WriteAllBytesAsync(music, [1, 2, 3, 4]);
            var snapshot = Snapshot(
                "broken-" + Guid.NewGuid().ToString("N"),
                1,
                assets,
                music,
                background,
                """{"sprites":[{"id":"broken","time":0,"path":"broken.png"}]}""");
            var materializer = new PreviewVfsMaterializer(Path.Combine(root, "cache"));

            var result = await materializer.MaterializeAsync(snapshot);

            Assert.False(result.StoryboardEnabled);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("PREVIEW_ASSET_DECODE_FAILED", diagnostic.Code);
            Assert.Equal(PreviewDiagnosticImpact.StoryboardOnly, diagnostic.Impact);
            Assert.Equal("{}", await File.ReadAllTextAsync(result.StoryboardPath));
            Assert.True(File.Exists(result.ChartPath));
            Assert.True(File.Exists(Path.Combine(result.Directory, "music.ogg")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Materialize_PathEscapeFallsBackButReservedCollisionIsExplicit()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var assets = Directory.CreateDirectory(Path.Combine(root, "assets")).FullName;
            var background = Path.Combine(root, "background.png");
            WriteImage(background, Colors.Black, jpeg: false);
            var music = Path.Combine(root, "music.ogg");
            await File.WriteAllBytesAsync(music, [1, 2, 3, 4]);
            var materializer = new PreviewVfsMaterializer(Path.Combine(root, "cache"));
            var escaped = Snapshot(
                "escape-" + Guid.NewGuid().ToString("N"),
                1,
                assets,
                music,
                background,
                """{"sprites":[{"id":"escape","time":0,"path":"../outside.png"}]}""");

            var escapedResult = await materializer.MaterializeAsync(escaped);

            Assert.False(escapedResult.StoryboardEnabled);
            Assert.Contains(escapedResult.Diagnostics,
                item => item.Code == "PREVIEW_ASSET_PATH_ESCAPE" &&
                        item.Path == "$.sprites[0].path");

            await File.WriteAllTextAsync(Path.Combine(assets, "chart.json"), "reserved");
            var collision = Snapshot(
                "collision-" + Guid.NewGuid().ToString("N"),
                1,
                assets,
                music,
                background,
                "{}");
            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                materializer.MaterializeAsync(collision));
            Assert.Contains("PREVIEW_VFS_RESERVED_NAME_COLLISION", exception.Message);
            Assert.Contains(Path.Combine(assets, "chart.json"), exception.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Materialize_RejectsDifferentContentForPublishedVersion()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var assets = Directory.CreateDirectory(Path.Combine(root, "assets")).FullName;
            var background = Path.Combine(root, "background.png");
            WriteImage(background, Colors.Black, jpeg: false);
            var music = Path.Combine(root, "music.ogg");
            await File.WriteAllBytesAsync(music, [1, 2, 3, 4]);
            var snapshot = Snapshot(
                "immutable-" + Guid.NewGuid().ToString("N"),
                4,
                assets,
                music,
                background,
                "{}");
            var materializer = new PreviewVfsMaterializer(Path.Combine(root, "cache"));
            await materializer.MaterializeAsync(snapshot);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                materializer.MaterializeAsync(snapshot with
                {
                    StoryboardJson = "{\"sprites\":[]}" 
                }));

            Assert.Contains("PREVIEW_VFS_VERSION_CONFLICT", exception.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static StoryboardPreviewSnapshot Snapshot(
        string sessionId,
        long version,
        string assetRoot,
        string music,
        string background,
        string storyboard) =>
        new(sessionId, version, null, storyboard, """
            {
              "time_base":480,
              "page_list":[{"start_tick":0,"end_tick":960,"scan_line_direction":1}],
              "tempo_list":[{"tick":0,"value":500000}],
              "event_order_list":[],
              "note_list":[{"id":0,"page_index":0,"type":0,"tick":240,"x":0.5,
                "hold_tick":0,"next_id":-1,"has_sibling":false,"is_forward":false}]
            }
            """, assetRoot, 0)
        {
            LevelJson = """
                {
                  "title":"Preview fixture",
                  "music":{"path":"old.ogg"},
                  "music_preview":{"path":"old.ogg"},
                  "background":{"path":"old.png"},
                  "charts":[{
                    "type":"easy",
                    "path":"old.json",
                    "storyboard":{"path":"old-storyboard.json"}
                  }]
                }
                """,
            MusicPath = music,
            BackgroundPath = background,
            ChartDifficulty = "easy"
        };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(),
            "naziki-preview-vfs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteImage(string path, Color color, bool jpeg)
    {
        var pixels = new byte[3 * 2 * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = color.A;
        }
        var bitmap = BitmapSource.Create(
            3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 3 * 4);
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder() : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static BitmapFrame ReadFirstFrame(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(input,
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }
}
