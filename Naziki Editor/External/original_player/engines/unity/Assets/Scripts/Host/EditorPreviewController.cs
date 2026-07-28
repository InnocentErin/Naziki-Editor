#if CYTOID_EDITOR_HOST && UNITY_STANDALONE_WIN
using System;
using System.IO;
using System.Security.Cryptography;
using Cysharp.Threading.Tasks;
using Cytoid.Storyboard;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EditorPreviewController
{
    static long previewVersion;
    static VfsSignature currentSignature;
    static bool externalClock;
    static JObject lastSettingsPayload;

    public static void Handle(JObject message)
    {
        var type = StringValue(message, "Type");
        var requestId = StringValue(message, "RequestId") ?? Guid.NewGuid().ToString("N");
        switch (type)
        {
            case "host.ping":
                Ack(message, requestId, "host.pong");
                break;
            case "host.shutdown":
                Ack(message, requestId);
                Application.Quit();
                break;
            case "preview.open":
                OpenSnapshot(message, requestId).Forget();
                break;
            case "preview.replaceSnapshot":
                ReplaceSnapshot(message, requestId).Forget();
                break;
            case "preview.applyChanges":
                if (LongValue(message, "BasePreviewVersion") != previewVersion)
                {
                    Reject(message, requestId, "version_mismatch",
                        $"Expected base preview version {previewVersion}.");
                    break;
                }
                ApplyChanges(message, requestId).Forget();
                break;
            case "preview.play":
                CurrentGame()?.PreviewPlayFromCurrentTime();
                SendState("Playing", requestId);
                break;
            case "preview.pause":
                CurrentGame()?.PreviewPause();
                SendState("Paused", requestId);
                break;
            case "preview.stop":
                CurrentGame()?.PreviewEvaluateAt(0);
                SendState("Stopped", requestId);
                break;
            case "preview.seek":
            case "preview.scrub.update":
                CurrentGame()?.PreviewEvaluateAt((float)(Payload(message).Value<double?>("time") ?? 0));
                break;
            case "preview.clock.set":
                externalClock = string.Equals(
                    Payload(message).Value<string>("mode"),
                    "external",
                    StringComparison.OrdinalIgnoreCase);
                if (externalClock)
                    CurrentGame()?.PreviewPause();
                Ack(message, requestId);
                break;
            case "preview.clock.tick":
                if (externalClock)
                    CurrentGame()?.PreviewAdvanceExternalClock(
                        (float)(Payload(message).Value<double?>("time") ?? 0));
                break;
            case "preview.scrub.begin":
                BeginScrub(message, requestId).Forget();
                break;
            case "preview.scrub.commit":
            {
                var game = CurrentGame();
                game?.PreviewEvaluateAt((float)(Payload(message).Value<double?>("time") ?? 0));
                if (string.Equals(Payload(message).Value<string>("resumeState"), "Playing", StringComparison.OrdinalIgnoreCase))
                    game?.PreviewPlayFromCurrentTime();
                Ack(message, requestId);
                break;
            }
            case "preview.settings.apply":
                ApplySettings(Payload(message));
                Ack(message, requestId);
                break;
            case "preview.viewport.apply":
                ApplyViewport(message, requestId).Forget();
                break;
        }
    }

    static async UniTask OpenSnapshot(JObject message, string requestId)
    {
        try
        {
            var root = RequireVfsRoot(message);
            var levelText = File.ReadAllText(Path.Combine(root, "level.json"));
            var level = JsonConvert.DeserializeObject<LevelMeta>(levelText);
            if (level == null || !level.Validate()) throw new InvalidDataException("Invalid level.json.");
            var chart = level.GetChartSection("hard") ??
                        level.GetChartSection("extreme") ??
                        level.GetChartSection("easy") ??
                        throw new InvalidDataException("No supported chart is present in level.json.");
            var launch = new JObject
            {
                ["mode"] = "ranked",
                ["level"] = new JObject
                {
                    ["meta"] = JObject.Parse(levelText),
                    ["selectedDifficulty"] = chart.type,
                    ["assets"] = new JObject
                    {
                        ["vfsUri"] = GameLaunchVfs.ToFileUri(root),
                        ["chartPath"] = chart.path,
                        ["musicPath"] = level.GetMusicPath(chart.type),
                        ["storyboardPath"] = chart.storyboard?.path ?? "storyboard.json"
                    }
                },
                ["mods"] = new JArray("auto"),
                ["settings"] = BuildPreviewWireSettings(Payload(message)["settings"] as JObject),
                ["options"] = new JObject { ["recordPlayEvents"] = false }
            };
            GameLaunchBridge.StartGame(launch.ToString(Formatting.None));
            externalClock = false;
            await UniTask.WaitUntil(() => CurrentGame()?.IsLoaded == true)
                .Timeout(TimeSpan.FromSeconds(30));
            var game = CurrentGame();
            var time = (float)(Payload(message).Value<double?>("time") ?? 0);
            game.PreviewEvaluateAt(time);
            if (lastSettingsPayload != null)
                ApplySettings((JObject)lastSettingsPayload.DeepClone());
            currentSignature = VfsSignature.Create(root);
            AcceptVersion(message, requestId);
            SendState("Paused", requestId);
        }
        catch (Exception exception)
        {
            Reject(message, requestId, "preview_open_failed", exception.Message);
        }
    }

    static async UniTask ReplaceSnapshot(JObject message, string requestId)
    {
        var game = CurrentGame();
        if (game == null || !game.IsLoaded)
        {
            await OpenSnapshot(message, requestId);
            return;
        }
        try
        {
            var root = RequireVfsRoot(message);
            var nextSignature = VfsSignature.Create(root);
            if (currentSignature == null || !currentSignature.CanHotSwap(nextSignature))
            {
                await OpenSnapshot(message, requestId);
                return;
            }
            await game.PreviewReplaceStoryboard(File.ReadAllText(Path.Combine(root, "storyboard.json")));
            game.PreviewEvaluateAt(game.Time);
            currentSignature = nextSignature;
            AcceptVersion(message, requestId);
        }
        catch (Exception exception)
        {
            Reject(message, requestId, "snapshot_replace_failed", exception.Message);
        }
    }

    static async UniTask ApplyChanges(JObject message, string requestId)
    {
        var game = CurrentGame();
        if (game == null || !game.IsLoaded)
        {
            await OpenSnapshot(message, requestId);
            return;
        }
        try
        {
            var root = RequireVfsRoot(message);
            var nextSignature = VfsSignature.Create(root);
            if (currentSignature == null || !currentSignature.CanHotSwap(nextSignature))
            {
                await OpenSnapshot(message, requestId);
                return;
            }
            var json = File.ReadAllText(Path.Combine(root, "storyboard.json"));
            await StoryboardHotReloadCoordinator.Apply(game, json, Payload(message)["changes"] as JArray);
            game.PreviewEvaluateAt(game.Time);
            currentSignature = nextSignature;
            AcceptVersion(message, requestId);
        }
        catch (Exception exception)
        {
            Reject(message, requestId, "hot_reload_failed", exception.Message);
        }
    }

    static async UniTask BeginScrub(JObject message, string requestId)
    {
        try
        {
            var game = CurrentGame();
            if (game == null || !game.IsLoaded) return;
            game.PreviewPause();
            if (game.Storyboard != null)
                await game.PreviewReplaceStoryboard(game.Storyboard.RootObject.ToString());
            game.PreviewEvaluateAt((float)(Payload(message).Value<double?>("time") ?? 0));
        }
        catch (Exception exception)
        {
            Reject(message, requestId, "scrub_begin_failed", exception.Message);
        }
    }

    static async UniTask ApplyViewport(JObject message, string requestId)
    {
        try
        {
            var payload = Payload(message);
            var width = Mathf.Max(1, payload.Value<int?>("pixelWidth") ?? UnityEngine.Screen.width);
            var height = Mathf.Max(1, payload.Value<int?>("pixelHeight") ?? UnityEngine.Screen.height);
            UnityEngine.Screen.SetResolution(width, height, FullScreenMode.Windowed);
            ApplySettings(payload);
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            var game = CurrentGame();
            if (game != null && game.IsLoaded)
                game.PreviewEvaluateAt((float)(payload.Value<double?>("time") ?? game.Time));
            Ack(message, requestId);
        }
        catch (Exception exception)
        {
            Reject(message, requestId, "viewport_apply_failed", exception.Message);
        }
    }

    static void ApplySettings(JObject payload)
    {
        lastSettingsPayload = (JObject)payload.DeepClone();
        var settings = payload["settings"] as JObject ?? payload;
        var active = payload.Value<bool?>("active") ?? true;
        var frameRate = active
            ? settings.Value<string>("FrameRate") ?? settings.Value<string>("frameRate") ?? "60"
            : (settings.Value<int?>("InactiveFrameRate") ?? 15).ToString();
        if (string.Equals(frameRate, "Display", StringComparison.OrdinalIgnoreCase))
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
        }
        else
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = int.TryParse(frameRate, out var target) ? Mathf.Clamp(target, 30, 120) : 60;
        }

        var quality = settings.Value<string>("Quality") ?? "Medium";
        for (var i = 0; i < QualitySettings.names.Length; i++)
            if (string.Equals(QualitySettings.names[i], quality, StringComparison.OrdinalIgnoreCase))
                QualitySettings.SetQualityLevel(i, true);

        var scale = (settings.Value<int?>("RenderScalePercent") ?? 100) / 100f;
        var configuredThreshold = (float)(settings.Value<double?>("FrameSkipThresholdMilliseconds") ?? 16.67);
        var targetFrameRate = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60;
        var targetFrameBudget = 1000f / Mathf.Max(1, targetFrameRate);
        EditorPreviewBridge.ConfigureAdaptiveQuality(
            scale,
            (settings.Value<int?>("AdaptiveMinimumScalePercent") ?? 50) / 100f,
            settings.Value<bool?>("AdaptiveQuality") ?? true,
            Mathf.Max(configuredThreshold, targetFrameBudget));
        var width = payload.Value<int?>("pixelWidth");
        var height = payload.Value<int?>("pixelHeight");
        if (width.HasValue && height.HasValue)
            UnityEngine.Screen.SetResolution(Mathf.Max(1, width.Value), Mathf.Max(1, height.Value), FullScreenMode.Windowed);
    }

    static JObject BuildPreviewWireSettings(JObject settings)
    {
        var hitboxSizes = new JObject();
        var ringColors = new JObject();
        var fillColors = new JObject();
        var fillColorsAlt = new JObject();
        foreach (var key in new[]
                 {
                     "click", "hold", "longHold", "dragHead",
                     "dragChild", "flick", "cDragHead", "cDragChild"
                 })
        {
            hitboxSizes[key] = key == "flick" ? "medium" : "large";
            ringColors[key] = "#FFFFFF";
            fillColors[key] = key == "longHold" ? "#F2C85A" :
                key.Contains("drag") || key.Contains("Drag") ? "#39E59E" : "#35A7FF";
            fillColorsAlt[key] = key == "longHold" ? "#F2C85A" :
                key.Contains("drag") || key.Contains("Drag") ? "#39E59E" : "#FF5964";
        }

        return new JObject
        {
            ["profile"] = new JObject
            {
                ["language"] = "zh-CN",
                ["baseNoteOffset"] = 0,
                ["levelNoteOffset"] = 0,
                ["headsetNoteOffset"] = 0,
                ["judgmentOffset"] = 0,
                ["hitTapticFeedback"] = false,
                ["menuTapticFeedback"] = false
            },
            ["runtime"] = new JObject
            {
                ["musicVolume"] = 1,
                ["soundEffectsVolume"] = 1
            },
            ["visual"] = new JObject
            {
                ["noteSize"] = 0,
                ["horizontalMargin"] = 1,
                ["verticalMargin"] = 1,
                ["restrictPlayAreaAspectRatio"] = false,
                ["coverOpacity"] = 0,
                ["displayStoryboardEffects"] = true,
                ["displayBoundaries"] = false,
                ["skipMusicOnCompletion"] = false,
                ["displayEarlyLateIndicators"] = false,
                ["displayNoteIds"] = false,
                ["useExperimentalNoteAr"] = false,
                ["useExperimentalNoteAnimations"] = false,
                ["clearEffectsSize"] = 1,
                ["displayProfiler"] = false,
                ["adaptOverlayToSafeArea"] = true,
                ["graphicsQuality"] = settings?.Value<string>("Quality")?.ToLowerInvariant() ?? "medium"
            },
            ["audio"] = new JObject
            {
                ["hitSound"] = "click1",
                ["holdHitSoundTiming"] = "both",
                ["useNativeAudio"] = false,
                ["androidDspBufferSize"] = -1
            },
            ["noteStyle"] = new JObject
            {
                ["hitboxSizes"] = hitboxSizes,
                ["ringColors"] = ringColors,
                ["fillColors"] = fillColors,
                ["fillColorsAlt"] = fillColorsAlt,
                ["useFillColorForDragChildNodes"] = true
            }
        };
    }

    static string RequireVfsRoot(JObject message)
    {
        var root = Payload(message).Value<string>("vfsRoot");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new DirectoryNotFoundException("Preview VFS root is missing.");
        return Path.GetFullPath(root);
    }

    static Game CurrentGame() => UnityEngine.Object.FindObjectOfType<Game>();
    static JObject Payload(JObject message) =>
        message["Payload"] as JObject ?? message["payload"] as JObject ?? new JObject();
    static string StringValue(JObject message, string name) =>
        message.Value<string>(name) ?? message.Value<string>(char.ToLowerInvariant(name[0]) + name.Substring(1));

    static void Ack(JObject source, string requestId, string type = "preview.ack") =>
        EditorPreviewBridge.SendProtocol(
            type,
            requestId,
            new JObject(),
            LongValue(source, "EditorVersion"),
            LongValue(source, "BasePreviewVersion"),
            LongValue(source, "TargetPreviewVersion"));

    static void AcceptVersion(JObject source, string requestId)
    {
        previewVersion = LongValue(source, "TargetPreviewVersion");
        Ack(source, requestId);
    }

    static void Reject(JObject source, string requestId, string code, string text) =>
        EditorPreviewBridge.SendProtocol(
            "preview.rejected",
            requestId,
            new JObject { ["code"] = code, ["message"] = text },
            LongValue(source, "EditorVersion"),
            LongValue(source, "BasePreviewVersion"),
            LongValue(source, "TargetPreviewVersion"));

    static void SendState(string state, string requestId)
    {
        var game = CurrentGame();
        EditorPreviewBridge.SendProtocol("preview.state", requestId, new JObject
        {
            ["state"] = state,
            ["time"] = game?.Time ?? 0,
            ["duration"] = game?.PreviewDuration ?? 0
        });
    }

    static long LongValue(JObject message, string name) =>
        message.Value<long?>(name) ??
        message.Value<long?>(char.ToLowerInvariant(name[0]) + name.Substring(1)) ?? 0;

    sealed class VfsSignature
    {
        public string ChartHash;
        public string MusicHash;
        public string BackgroundHash;

        public static VfsSignature Create(string root)
        {
            var level = JObject.Parse(File.ReadAllText(Path.Combine(root, "level.json")));
            var music = level["music"]?["path"]?.Value<string>();
            var background = level["background"]?["path"]?.Value<string>();
            return new VfsSignature
            {
                ChartHash = Hash(Path.Combine(root, "chart.json")),
                MusicHash = HashOptional(root, music),
                BackgroundHash = HashOptional(root, background)
            };
        }

        public bool CanHotSwap(VfsSignature next) =>
            next != null &&
            string.Equals(ChartHash, next.ChartHash, StringComparison.Ordinal) &&
            string.Equals(MusicHash, next.MusicHash, StringComparison.Ordinal) &&
            string.Equals(BackgroundHash, next.BackgroundHash, StringComparison.Ordinal);

        static string HashOptional(string root, string relative) =>
            string.IsNullOrWhiteSpace(relative) ? string.Empty : Hash(Path.Combine(root, relative));

        static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
#endif
