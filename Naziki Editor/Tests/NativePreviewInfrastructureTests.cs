using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Settings;
using Naziki_Editor.Features.Preview;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class NativePreviewInfrastructureTests
{
    [Fact]
    public void PreviewSettings_MapPersistedPerformanceValues()
    {
        var store = new FakeSettingsStore();
        store.Set("Performance.PreviewRenderScale", "75%");
        store.Set("Performance.PreviewFrameRate", "120");
        store.Set("Performance.PreviewAdaptiveQuality", false);
        store.Set("Performance.MaxCacheSize", 256);
        store.Set("Performance.PreviewExternalClockRate", "60");
        store.Set("Performance.PreviewAdaptiveMinimumScale", "75%");
        store.Set("Editor.PreviewAspectRatio", "21:9");
        using var provider = new PreviewSettingsProvider(store);

        Assert.Equal(75, provider.Current.RenderScalePercent);
        Assert.Equal("120", provider.Current.FrameRate);
        Assert.False(provider.Current.AdaptiveQuality);
        Assert.Equal(256L * 1024 * 1024, provider.Current.MaxCacheBytes);
        Assert.Equal(60, provider.Current.ExternalClockRate);
        Assert.Equal(75, provider.Current.AdaptiveMinimumScalePercent);
        Assert.Equal("21:9", provider.Current.AspectRatio);
    }

    [Fact]
    public void PreviewSettings_RaiseOnlyForPreviewRelevantChanges()
    {
        var store = new FakeSettingsStore();
        using var provider = new PreviewSettingsProvider(store);
        var notifications = 0;
        provider.Changed += (_, _) => notifications++;

        store.Set("Timeline.SnapEnabled", false);
        store.Set("Performance.PreviewFrameRate", "30");

        Assert.Equal(1, notifications);
    }

    [Fact]
    public void PreviewValidation_BlocksMissingChartAndMusic()
    {
        var validator = new PreviewValidationService(new FakeStoryboardValidator());
        var context = new ProjectDataContext(MessageBroker.Default)
        {
            Storyboard = new StoryboardRoot()
        };
        var snapshot = new StoryboardPreviewSnapshot(
            "session",
            4,
            null,
            "{}",
            null,
            null,
            0);

        var result = validator.Validate(context, snapshot);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, item => item.Code == "PREVIEW_LEVEL_MISSING");
        Assert.Contains(result.Diagnostics, item => item.Code == "PREVIEW_CHART_MISSING");
        Assert.Contains(result.Diagnostics, item => item.Code == "PREVIEW_MUSIC_MISSING");
    }

    [Theory]
    [InlineData("Add", StoryboardEntityChangeOperation.Add)]
    [InlineData("Update", StoryboardEntityChangeOperation.Update)]
    [InlineData("Delete", StoryboardEntityChangeOperation.Delete)]
    [InlineData("Upsert", StoryboardEntityChangeOperation.Update)]
    public void EntityChange_UsesStrongOperationFallback(
        string wireValue,
        StoryboardEntityChangeOperation expected)
    {
        var change = new StoryboardEntityChange("id", wireValue, null, []);
        Assert.Equal(expected, change.TypedOperation);
    }

    [Fact]
    public async Task VfsMaterializer_KeepsAcceptedVersionsImmutableWhenSourceChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "naziki-preview-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var sessions = Path.Combine(root, "sessions");
        Directory.CreateDirectory(source);
        var music = Path.Combine(source, "track.wav");
        var background = Path.Combine(source, "cover.png");
        var sprite = Path.Combine(source, "sprite.png");
        await File.WriteAllBytesAsync(music, [1, 2, 3]);
        await File.WriteAllBytesAsync(background, [4, 5, 6]);
        await File.WriteAllBytesAsync(sprite, [7, 8, 9]);

        try
        {
            var materializer = new PreviewVfsMaterializer(sessions);
            var first = await materializer.MaterializeAsync(CreateSnapshot(1, source, music, background));
            var firstSprite = Path.Combine(first.Directory, "sprite.png");
            Assert.Equal(new byte[] { 7, 8, 9 }, await File.ReadAllBytesAsync(firstSprite));
            var level = JObject.Parse(await File.ReadAllTextAsync(first.LevelPath));
            Assert.Equal("Imported Title", level.Value<string>("title"));
            Assert.Equal("Imported Artist", level.Value<string>("artist"));
            Assert.Equal("music.wav", level["music"]?["path"]?.Value<string>());
            Assert.Equal("background.png", level["background"]?["path"]?.Value<string>());
            Assert.Equal("chart.json", level["charts"]?[0]?["path"]?.Value<string>());
            Assert.Equal("storyboard.json", level["charts"]?[0]?["storyboard"]?["path"]?.Value<string>());

            await File.WriteAllBytesAsync(sprite, [9, 8, 7]);
            var second = await materializer.MaterializeAsync(CreateSnapshot(2, source, music, background));

            Assert.Equal(new byte[] { 7, 8, 9 }, await File.ReadAllBytesAsync(firstSprite));
            Assert.Equal(new byte[] { 9, 8, 7 }, await File.ReadAllBytesAsync(Path.Combine(second.Directory, "sprite.png")));
            Assert.NotEqual(first.AssetHashes["sprite.png"], second.AssetHashes["sprite.png"]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static StoryboardPreviewSnapshot CreateSnapshot(
        long version,
        string assetRoot,
        string music,
        string background) =>
        new("session", version, null, "{}", """
            {
              "format_version": 1,
              "time_base": 480,
              "page_list": [{"start_tick":0,"end_tick":480,"scan_line_direction":1}],
              "tempo_list": [{"tick":0,"value":500000}],
              "note_list": [],
              "event_order_list": []
            }
            """, assetRoot, 0)
        {
            LevelJson = """
                {
                  "schema_version": 2,
                  "version": 1,
                  "id": "test",
                  "title": "Imported Title",
                  "artist": "Imported Artist",
                  "music": { "path": "original.ogg" },
                  "background": { "path": "original.png" },
                  "charts": [
                    { "type": "hard", "difficulty": 10, "path": "original-chart.json" }
                  ]
                }
                """,
            MusicPath = music,
            BackgroundPath = background,
            ProjectId = "test",
            ProjectName = "Test"
        };

    private sealed class FakeStoryboardValidator : IStoryboardDocumentValidator
    {
        public IReadOnlyList<StoryboardDiagnostic> Validate(StoryboardRoot document) => [];
        public IReadOnlyList<StoryboardDiagnostic> Validate(StoryboardRoot document, ProjectDataContext? context) => [];
        public IReadOnlyList<StoryboardDiagnostic> ValidateEntity(IStoryboardEntity entity, string path = "$") => [];
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
        public event EventHandler<SettingsChangedEventArgs>? SettingChanged;

        public T Get<T>(string key, T defaultValue = default!) =>
            _values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;

        public void Set<T>(string key, T value)
        {
            _values.TryGetValue(key, out var oldValue);
            _values[key] = value;
            SettingChanged?.Invoke(this, new SettingsChangedEventArgs(
                key,
                oldValue,
                value,
                key.Split('.')[0]));
        }

        public bool ContainsKey(string key) => _values.ContainsKey(key);
        public IReadOnlyList<SettingsCategory> GetCategories() => [];
        public IReadOnlyList<SettingItem> GetCategoryItems(string categoryKey) => [];
        public void Load() { }
        public void Save() { }
        public void Reset(string key) => _values.Remove(key);
        public void ResetCategory(string categoryKey) { }
        public void RegisterCategory(SettingsCategory category) { }
    }
}
