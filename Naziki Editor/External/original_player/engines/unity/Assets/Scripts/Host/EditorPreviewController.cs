#if CYTOID_EDITOR_HOST && UNITY_STANDALONE_WIN
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
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
    static int loadInProgress;
    public static long CurrentPreviewVersion => previewVersion;

    public static void Handle(JObject message)
    {
        var type = StringValue(message, "Type");
        var requestId = StringValue(message, "RequestId") ?? Guid.NewGuid().ToString("N");
        switch (type)
        {
            case "host.ping":
                Ack(message, requestId, "host.pong");
                break;
            case "preview.health.check":
                EditorPreviewBridge.SendProtocol(
                    "preview.health.ok",
                    requestId,
                    new JObject
                    {
                        ["state"] = CurrentGame()?.IsLoaded == true ? "ready" : "loading",
                        ["previewVersion"] = previewVersion
                    });
                break;
            case "host.shutdown":
                Shutdown(message, requestId).Forget();
                break;
            case "preview.open":
                if (!TryBeginLoad(message, requestId)) break;
                OpenSnapshot(message, requestId).Forget();
                break;
            case "preview.replaceSnapshot":
                if (!TryBeginLoad(message, requestId)) break;
                ReplaceSnapshot(message, requestId).Forget();
                break;
            case "preview.applyChanges":
                if (LongValue(message, "BasePreviewVersion") != previewVersion)
                {
                    Reject(message, requestId, "version_mismatch",
                        $"Expected base preview version {previewVersion}.");
                    break;
                }
                if (!TryBeginLoad(message, requestId)) break;
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

    static async UniTask Shutdown(JObject message, string requestId)
    {
        Ack(message, requestId);
        // Give the dedicated writer thread time to flush the acknowledgement.
        // Application.Quit destroys the bridge and cancels that thread.
        await UniTask.DelayFrame(2);
        Application.Quit();
    }

    static async UniTask OpenSnapshot(JObject message, string requestId)
    {
        LoadEvent(message, requestId, "preview.load.started", "accepted");
        try
        {
            LoadEvent(message, requestId, "preview.load.progress", "readingVfs");
            var root = RequireVfsRoot(message);
            var levelText = File.ReadAllText(Path.Combine(root, "level.json"));
            LoadEvent(message, requestId, "preview.load.progress", "parsingLevel");
            var level = JsonConvert.DeserializeObject<LevelMeta>(levelText);
            if (level == null || !level.Validate()) throw new InvalidDataException("Invalid level.json.");
            var chart = level.GetChartSection("hard") ??
                        level.GetChartSection("extreme") ??
                        level.GetChartSection("easy") ??
                        throw new InvalidDataException("No supported chart is present in level.json.");
            var chartIdentity = ValidateOfficialChart(root, chart);
            LoadEvent(message, requestId, "preview.load.progress", "startingGame");
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
            LoadEvent(message, requestId, "preview.load.progress", "loadingSceneAndAssets");
            await UniTask.WaitUntil(() => CurrentGame()?.IsLoaded == true);
            var game = CurrentGame();
            LoadEvent(message, requestId, "preview.load.progress", "evaluatingFirstFrame");
            var time = (float)(Payload(message).Value<double?>("time") ?? 0);
            game.PreviewEvaluateAt(time);
            if (lastSettingsPayload != null)
                ApplySettings((JObject)lastSettingsPayload.DeepClone());
            currentSignature = VfsSignature.Create(root);
            AcceptLoadedVersion(message, requestId, chartIdentity, game);
            SendState("Paused", requestId);
        }
        catch (Exception exception)
        {
            LoadFailed(message, requestId, "preview_open_failed", exception);
        }
        finally
        {
            Volatile.Write(ref loadInProgress, 0);
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
            LoadEvent(message, requestId, "preview.load.started", "hotSwapAccepted");
            LoadEvent(message, requestId, "preview.load.progress", "readingStoryboard");
            var root = RequireVfsRoot(message);
            var nextSignature = VfsSignature.Create(root);
            if (currentSignature == null || !currentSignature.CanHotSwap(nextSignature))
            {
                await OpenSnapshot(message, requestId);
                return;
            }
            await game.PreviewReplaceStoryboard(File.ReadAllText(Path.Combine(root, "storyboard.json")));
            LoadEvent(message, requestId, "preview.load.progress", "evaluatingFirstFrame");
            game.PreviewEvaluateAt(game.Time);
            currentSignature = nextSignature;
            AcceptLoadedVersion(message, requestId, null, game);
        }
        catch (Exception exception)
        {
            LoadFailed(message, requestId, "snapshot_replace_failed", exception);
        }
        finally
        {
            Volatile.Write(ref loadInProgress, 0);
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
            LoadEvent(message, requestId, "preview.load.started", "changesAccepted");
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
            AcceptLoadedVersion(message, requestId, null, game);
        }
        catch (Exception exception)
        {
            LoadFailed(message, requestId, "hot_reload_failed", exception);
        }
        finally
        {
            Volatile.Write(ref loadInProgress, 0);
        }
    }

    static bool TryBeginLoad(JObject message, string requestId)
    {
        if (Interlocked.CompareExchange(ref loadInProgress, 1, 0) == 0)
            return true;
        Reject(message, requestId, "load_in_progress",
            "Unity Preview is already materializing another snapshot.");
        return false;
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

    static void AcceptVersion(JObject source, string requestId, JObject chartIdentity = null)
    {
        previewVersion = LongValue(source, "TargetPreviewVersion");
        EditorPreviewBridge.SendProtocol(
            "preview.ack",
            requestId,
            chartIdentity == null
                ? new JObject()
                : new JObject { ["chartIdentity"] = chartIdentity },
            LongValue(source, "EditorVersion"),
            LongValue(source, "BasePreviewVersion"),
            LongValue(source, "TargetPreviewVersion"));
    }

    static void AcceptLoadedVersion(
        JObject source,
        string requestId,
        JObject chartIdentity,
        Game game)
    {
        previewVersion = LongValue(source, "TargetPreviewVersion");
        var payload = chartIdentity == null
            ? new JObject()
            : new JObject { ["chartIdentity"] = chartIdentity };
        payload["time"] = game?.Time ?? 0;
        payload["duration"] = game?.PreviewDuration ?? 0;
        EditorPreviewBridge.SendProtocol(
            "preview.load.ready",
            requestId,
            payload,
            LongValue(source, "EditorVersion"),
            LongValue(source, "BasePreviewVersion"),
            LongValue(source, "TargetPreviewVersion"));
    }

    static void LoadEvent(
        JObject source,
        string requestId,
        string type,
        string stage) =>
        EditorPreviewBridge.SendProtocol(
            type,
            requestId,
            new JObject { ["stage"] = stage },
            LongValue(source, "EditorVersion"),
            LongValue(source, "BasePreviewVersion"),
            LongValue(source, "TargetPreviewVersion"));

    static void LoadFailed(
        JObject source,
        string requestId,
        string code,
        Exception exception) =>
        EditorPreviewBridge.SendProtocol(
            "preview.load.failed",
            requestId,
            new JObject
            {
                ["code"] = code,
                ["message"] = SafeDiagnosticText(exception.Message, 2048),
                ["stage"] = "contentLoad",
                ["resourcePath"] = string.Empty,
                ["stackTrace"] = SafeDiagnosticText(exception.StackTrace, 8192)
            },
            LongValue(source, "EditorVersion"),
            LongValue(source, "BasePreviewVersion"),
            LongValue(source, "TargetPreviewVersion"));

    static string SafeDiagnosticText(string value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sanitized = value
            .Replace(Application.dataPath, "<unity-project>/Assets")
            .Replace(Environment.CurrentDirectory, "<working-directory>");
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized.Substring(0, maximumLength);
    }

    static JObject ValidateOfficialChart(string root, LevelMeta.ChartSection chart)
    {
        if (chart == null || string.IsNullOrWhiteSpace(chart.path))
            throw new InvalidDataException("PREVIEW_CHART_PATH_MISSING: level.json 中选中难度缺少 chart.path。");
        var chartPath = Path.GetFullPath(Path.Combine(root,
            chart.path.Replace('/', Path.DirectorySeparatorChar)));
        var rootPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!chartPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(chartPath))
            throw new InvalidDataException(
                $"PREVIEW_CHART_NOT_FOUND: 找不到正式谱面文件“{chart.path}”。");

        var json = JObject.Parse(File.ReadAllText(chartPath));
        var notes = json["note_list"] as JArray;
        var pages = json["page_list"] as JArray;
        var tempos = json["tempo_list"] as JArray;
        if (notes == null || notes.Count == 0)
            throw new InvalidDataException("PREVIEW_CHART_EMPTY: $.note_list 必须至少包含一个音符。");
        if (pages == null || pages.Count == 0)
            throw new InvalidDataException("PREVIEW_CHART_PAGES_EMPTY: $.page_list 必须至少包含一个扫描页。");
        if (tempos == null || tempos.Count == 0)
            throw new InvalidDataException("PREVIEW_CHART_TEMPO_EMPTY: $.tempo_list 必须至少包含一个 BPM 段。");

        return new JObject
        {
            ["difficulty"] = chart.type,
            ["path"] = chart.path.Replace('\\', '/'),
            ["sha256"] = HashFile(chartPath),
            ["noteCount"] = notes.Count,
            ["pageCount"] = pages.Count,
            ["tempoCount"] = tempos.Count
        };
    }

    static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
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
