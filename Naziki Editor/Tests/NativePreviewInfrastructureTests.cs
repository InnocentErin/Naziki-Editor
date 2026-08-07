using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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

    [Fact]
    public void PreviewValidation_ReportsUnityChartWirePath()
    {
        var validator = new PreviewValidationService(
            new FakeStoryboardValidator());
        var context = new ProjectDataContext(MessageBroker.Default)
        {
            Storyboard = new StoryboardRoot()
        };
        var snapshot = new StoryboardPreviewSnapshot(
            "session",
            5,
            null,
            "{}",
            """
            {
              "music_offset": null,
              "time_base": 480,
              "page_list": [{"start_tick":0,"end_tick":480}],
              "tempo_list": [{"tick":0,"value":500000}],
              "note_list": [{
                "id":0,"page_index":0,"type":0,"tick":240,"x":0.5,
                "hold_tick":0,"next_id":-1,
                "has_sibling":false,"is_forward":false
              }],
              "event_order_list": []
            }
            """,
            null,
            0)
        {
            LevelJson = "{}"
        };

        var result = validator.Validate(context, snapshot);

        Assert.Contains(result.Diagnostics, item =>
            item.Code == "PREVIEW_CHART_WIRE_INVALID" &&
            item.Path == "$.music_offset");
    }

    [Fact]
    public void PreviewValidation_AllowsNegativeOverlappingPageEffects()
    {
        var validator = new PreviewValidationService(
            new FakeStoryboardValidator());
        var context = new ProjectDataContext(MessageBroker.Default)
        {
            Storyboard = new StoryboardRoot()
        };
        var snapshot = new StoryboardPreviewSnapshot(
            "session",
            6,
            null,
            "{}",
            """
            {
              "music_offset": 0,
              "time_base": 480,
              "page_list": [
                {"start_tick":0,"end_tick":960,"scan_line_direction":1},
                {"start_tick":-960,"end_tick":1920,"scan_line_direction":-1}
              ],
              "tempo_list": [{"tick":0,"value":500000}],
              "note_list": [{
                "id":0,"page_index":0,"type":0,"tick":240,"x":0.5,
                "hold_tick":0,"next_id":-1,
                "has_sibling":false,"is_forward":false
              }],
              "event_order_list": []
            }
            """,
            null,
            0)
        {
            LevelJson = "{}"
        };

        var result = validator.Validate(context, snapshot);

        Assert.Contains(result.Diagnostics, item =>
            item.Code == "CHART_PAGE_NEGATIVE_START" &&
            item.Severity == PreviewDiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "CHART_PAGE_OVERLAP" &&
            item.Severity == PreviewDiagnosticSeverity.Warning);
        Assert.DoesNotContain(result.Diagnostics, item =>
            item.Code == "PREVIEW_CHART_PAGE_RANGE");
    }

    [Fact]
    public async Task UnityHost_SurfacesTelemetryRuntimeExceptionImmediately()
    {
        var transport = new ConnectedPreviewTransport();
        var process = new HandshakePreviewProcess(transport);
        using var settings = new PreviewSettingsProvider(
            new FakeSettingsStore());
        using var host = new UnityStoryboardPreviewHost(
            transport,
            process,
            new FakePreviewVfs(),
            new FakePreviewValidation(),
            settings);
        var source = new StaticPreviewSource(new StoryboardPreviewSnapshot(
            "telemetry-session",
            1,
            null,
            "{}",
            null,
            null,
            0));
        host.Attach(source, source);
        await host.AttachWindowAsync(new IntPtr(123), 1280, 720);
        await host.OpenProjectAsync(new ProjectDataContext(MessageBroker.Default), 0);
        var launch = Assert.IsType<UnityPreviewLaunchOptions>(process.LaunchOptions);
        var inner = new JObject
        {
            ["schema"] = "cytoid.game-core.v2",
            ["type"] = "session.result",
            ["payload"] = new JObject
            {
                ["error"] = new JObject
                {
                    ["code"] = "runtime_exception",
                    ["message"] =
                        "Error converting null to System.Double. Path 'music_offset'."
                }
            }
        };

        transport.Raise(new PreviewProtocolMessage(
            "preview.telemetry",
            launch.SessionId,
            "telemetry",
            0,
            0,
            0,
            new JObject
            {
                ["cytoidGameCoreV2"] =
                    inner.ToString(Newtonsoft.Json.Formatting.None)
            })
        {
            ConnectionId = launch.ConnectionId,
            Generation = launch.Generation
        });

        await WaitUntilAsync(
            () => host.Availability == PreviewAvailabilityState.InvalidData,
            "The coordinator did not surface the Unity runtime exception.");
        Assert.Equal(PreviewAvailabilityState.InvalidData,
            host.Availability);
        var diagnostic = Assert.Single(host.Diagnostics);
        Assert.Equal("PREVIEW_UNITY_RUNTIME_EXCEPTION",
            diagnostic.Code);
        Assert.Equal("$.music_offset", diagnostic.Path);
        Assert.Contains("System.Double", diagnostic.Message);

        transport.Raise(new PreviewProtocolMessage(
            "preview.rejected",
            launch.SessionId,
            "late-timeout",
            0,
            0,
            0,
            new JObject
            {
                ["code"] = "preview_open_failed",
                ["message"] = "Exceed Timeout:00:00:30"
            })
        {
            ConnectionId = launch.ConnectionId,
            Generation = launch.Generation
        });
        transport.Raise(new PreviewProtocolMessage(
            "preview.performance",
            launch.SessionId,
            "queue-marker",
            0,
            0,
            0,
            new JObject { ["fps"] = 123d })
        {
            ConnectionId = launch.ConnectionId,
            Generation = launch.Generation
        });
        await WaitUntilAsync(
            () => host.Performance?.FramesPerSecond == 123d,
            "The coordinator did not drain the delayed rejection.");
        Assert.Equal("PREVIEW_UNITY_RUNTIME_EXCEPTION",
            Assert.Single(host.Diagnostics).Code);
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
            Assert.Single((JArray)level["charts"]!);
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

    [Fact]
    public async Task VfsMaterializer_RejectsEmptyChartBeforeUnityLaunch()
    {
        var sessions = Path.Combine(Path.GetTempPath(),
            "naziki-preview-empty-chart-" +
            Guid.NewGuid().ToString("N"));
        var snapshot = new StoryboardPreviewSnapshot(
            "empty-chart",
            1,
            null,
            "{}",
            """
            {
              "time_base":480,
              "page_list":[
                {"start_tick":0,"end_tick":480,"scan_line_direction":1}
              ],
              "tempo_list":[{"tick":0,"value":500000}],
              "note_list":[],
              "event_order_list":[]
            }
            """,
            null,
            0);

        try
        {
            var materializer =
                new PreviewVfsMaterializer(sessions);
            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => materializer.MaterializeAsync(snapshot));
            Assert.Contains("no notes", error.Message);
        }
        finally
        {
            if (Directory.Exists(sessions))
                Directory.Delete(sessions, true);
        }
    }

    [Fact]
    public async Task UnityHost_KeepsTransportSessionStableAndLoadsMaterializedSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "naziki-preview-handshake-" + Guid.NewGuid().ToString("N"));
        var assets = Path.Combine(root, "assets");
        var sessions = Path.Combine(root, "sessions");
        Directory.CreateDirectory(assets);
        var music = Path.Combine(root, "music.ogg");
        var background = Path.Combine(root, "background.png");
        File.WriteAllBytes(music, [1, 2, 3]);
        File.WriteAllBytes(background, [4, 5, 6]);
        File.WriteAllText(Path.Combine(assets, "storyboard-asset.txt"), "asset");
        var snapshot = CreateSnapshot(7, assets, music, background) with
        {
            SessionId = "project-snapshot-session"
        };
        var transport = new ConnectedPreviewTransport();
        var process = new HandshakePreviewProcess(transport);
        var store = new FakeSettingsStore();
        using var settings = new PreviewSettingsProvider(store);
        using var host = new UnityStoryboardPreviewHost(
            transport,
            process,
            new PreviewVfsMaterializer(sessions),
            new FakePreviewValidation(),
            settings);
        host.Changed += (_, _) =>
            throw new InvalidOperationException("UI observer failure must be isolated.");
        var source = new StaticPreviewSource(snapshot);
        host.Attach(source, source);

        try
        {
            await host.AttachWindowAsync(new IntPtr(123), 1280, 720);
            await host.OpenProjectAsync(new ProjectDataContext(MessageBroker.Default), 0);

            var launch = Assert.IsType<UnityPreviewLaunchOptions>(process.LaunchOptions);
            Assert.NotEqual("unbound", launch.SessionId);
            Assert.NotEqual(snapshot.SessionId, launch.SessionId);
            Assert.All(transport.Sent, message =>
                Assert.Equal(launch.SessionId, message.SessionId));
            var open = Assert.Single(transport.Sent, message => message.Type == "preview.open");
            var vfsRoot = Assert.IsType<string>(open.Payload["vfsRoot"]?.Value<string>());
            Assert.True(File.Exists(Path.Combine(vfsRoot, "level.json")));
            Assert.True(File.Exists(Path.Combine(vfsRoot, "chart.json")));
            Assert.True(File.Exists(Path.Combine(vfsRoot, "storyboard.json")));
            Assert.True(File.Exists(Path.Combine(vfsRoot, "music.ogg")));
            Assert.True(File.Exists(Path.Combine(vfsRoot, "background.png")));
            Assert.Equal(PreviewAvailabilityState.Ready, host.Availability);
            Assert.Equal(PreviewSessionPhase.PreviewReady, host.SessionStatus.Phase);

            transport.Raise(open with
            {
                Type = "preview.performance",
                RequestId = "malformed-editor-telemetry",
                Payload = new JObject { ["fps"] = "not-a-number" }
            });
            transport.Raise(open with
            {
                Type = "host.ready",
                RequestId = "stale-ready",
                Generation = open.Generation - 1,
                Payload = new JObject()
            });
            transport.Raise(open with
            {
                Type = "preview.health.ok",
                RequestId = "stale-heartbeat",
                Generation = open.Generation - 1,
                Payload = new JObject()
            });
            transport.RaiseConnection(new PreviewTransportStateChanged(
                transport.Generation - 1,
                false,
                "stale disconnect"));
            transport.Raise(open with
            {
                Type = "preview.unityLog",
                RequestId = "unity-warning",
                Payload = new JObject
                {
                    ["logType"] = "Warning",
                    ["summary"] = "simulated non-fatal video warning"
                }
            });
            transport.Raise(open with
            {
                Type = "preview.performance",
                RequestId = "coordinator-marker",
                Payload = new JObject { ["fps"] = 77d }
            });
            await WaitUntilAsync(
                () => host.Performance?.FramesPerSecond == 77d,
                "The coordinator did not drain stale generation events.");
            Assert.Equal(PreviewAvailabilityState.Ready, host.Availability);
            Assert.Equal(PreviewConnectionState.Healthy, host.SessionStatus.ConnectionState);
            Assert.Contains(host.Diagnostics,
                item => item.Code == "PREVIEW_MESSAGE_HANDLER_FAILED");
            Assert.Contains(host.Diagnostics,
                item => item.Code == "PREVIEW_UNITY_WARNING");
            Assert.DoesNotContain(host.Diagnostics,
                item => item.Code == "PREVIEW_CONNECTION_LOST");

            process.RaiseUnexpectedExit();
            await WaitUntilAsync(
                () => process.Generation == 2 &&
                      host.Availability == PreviewAvailabilityState.Ready,
                "Unity Preview did not complete its single automatic recovery.");
            Assert.Equal(2, transport.Sent.Count(message => message.Type == "preview.open"));
            Assert.DoesNotContain(transport.Sent,
                message => message.Type == "preview.replaceSnapshot");

            await host.ShutdownAsync();
            Assert.Equal(PreviewAvailabilityState.Disconnected, host.Availability);
            Assert.DoesNotContain(host.Diagnostics,
                item => item.Code == "PREVIEW_CONNECTION_LOST");
        }
        finally
        {
            await host.ShutdownAsync();
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task UnityHost_PreservesHostAcceptSendFailureWithoutFalseDisconnect()
    {
        var transport = new ConnectedPreviewTransport
        {
            FailSendType = "host.accept"
        };
        var process = new HandshakePreviewProcess(transport);
        using var settings = new PreviewSettingsProvider(new FakeSettingsStore());
        using var host = new UnityStoryboardPreviewHost(
            transport,
            process,
            new FakePreviewVfs(),
            new FakePreviewValidation(),
            settings);
        var source = new StaticPreviewSource(new StoryboardPreviewSnapshot(
            "accept-failure-session",
            1,
            null,
            "{}",
            null,
            null,
            0));
        host.Attach(source, source);

        await host.AttachWindowAsync(new IntPtr(123), 1280, 720);
        await host.OpenProjectAsync(new ProjectDataContext(MessageBroker.Default), 0);
        await WaitUntilAsync(
            () => process.Generation == 2 &&
                  host.Diagnostics.Any(item =>
                      item.Code == "PREVIEW_HANDSHAKE_SEND_FAILED"),
            "The host.accept send failure was not retained across automatic recovery.");

        Assert.Equal(PreviewAvailabilityState.Faulted, host.Availability);
        Assert.Equal("PREVIEW_HANDSHAKE_SEND_FAILED", host.Diagnostics[0].Code);
        Assert.DoesNotContain(host.Diagnostics,
            item => item.Code == "PREVIEW_CONNECTION_LOST" ||
                    item.Code == "PREVIEW_HOST_READY_TIMEOUT");
        Assert.Equal(2, transport.Sent.Count(message => message.Type == "host.accept"));
        Assert.DoesNotContain(transport.Sent,
            message => message.Type == "preview.open");
        Assert.False(process.IsRunning);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task NamedPipeTransport_PreservesFramesAndReportsMalformedJson()
    {
        await using var transport = new NamedPipeUnityPreviewTransport();
        var received = new List<PreviewProtocolMessage>();
        var receivedTwo = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<PreviewTransportStateChanged>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var faulted = new TaskCompletionSource<PreviewTransportFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        transport.MessageReceived += (_, message) =>
        {
            lock (received)
            {
                received.Add(message);
                if (received.Count == 2)
                    receivedTwo.TrySetResult(true);
            }
        };
        transport.ConnectionChanged += (_, state) =>
        {
            if (!state.Connected)
                disconnected.TrySetResult(state);
        };
        transport.Faulted += (_, fault) => faulted.TrySetResult(fault);

        var start = transport.StartAsync();
        await using var client = new NamedPipeClientStream(
            ".",
            transport.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);
        await start;
        await WriteProtocolFrameAsync(client, new PreviewProtocolMessage(
            "host.ready", "transport-session", "one", 0, 0, 0, new JObject()));
        await WriteProtocolFrameAsync(client, new PreviewProtocolMessage(
            "preview.load.progress", "transport-session", "two", 1, 0, 1,
            new JObject { ["stage"] = "loadingSceneAndAssets" }));
        await receivedTwo.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var malformed = Encoding.UTF8.GetBytes("{not-json");
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, malformed.Length);
        await client.WriteAsync(header);
        await client.WriteAsync(malformed);
        await client.FlushAsync();
        var failure = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var fault = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["one", "two"], received.Select(item => item.RequestId));
        Assert.Equal("transport-session", received[1].SessionId);
        Assert.Equal(PreviewTransportFaultKind.MalformedPayload, fault.Kind);
        Assert.NotNull(failure.Exception);
        Assert.Contains("character", failure.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NamedPipeTransport_ContinuesAfterSubscriberFailureAndStopsSilently()
    {
        await using var transport = new NamedPipeUnityPreviewTransport();
        var received = new List<string>();
        var receivedTwo = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatchFault = new TaskCompletionSource<PreviewTransportFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectCount = 0;
        transport.MessageReceived += (_, _) =>
            throw new InvalidOperationException("simulated editor subscriber failure");
        transport.MessageReceived += (_, message) =>
        {
            lock (received)
            {
                received.Add(message.RequestId);
                if (received.Count == 2)
                    receivedTwo.TrySetResult(true);
            }
        };
        transport.Faulted += (_, fault) =>
        {
            if (fault.Kind == PreviewTransportFaultKind.MessageDispatch)
                dispatchFault.TrySetResult(fault);
        };
        transport.ConnectionChanged += (_, state) =>
        {
            if (!state.Connected)
                Interlocked.Increment(ref disconnectCount);
        };

        var start = transport.StartAsync();
        await using var client = new NamedPipeClientStream(
            ".",
            transport.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);
        await start;
        await WriteProtocolFrameAsync(client, new PreviewProtocolMessage(
            "host.ready", "transport-session", "one", 0, 0, 0, new JObject()));
        await WriteProtocolFrameAsync(client, new PreviewProtocolMessage(
            "preview.load.progress", "transport-session", "two", 1, 0, 1,
            new JObject { ["stage"] = "loadingSceneAndAssets" }));

        await receivedTwo.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var fault = await dispatchFault.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(["one", "two"], received);
        Assert.Equal("host.ready", fault.MessageType);
        Assert.True(transport.IsConnected);

        await transport.StopAsync();
        await Task.Delay(50);
        Assert.Equal(0, Volatile.Read(ref disconnectCount));
    }

    [Fact]
    public async Task NamedPipeTransport_ReportsEofAsPhysicalDisconnect()
    {
        await using var transport = new NamedPipeUnityPreviewTransport();
        var disconnected = new TaskCompletionSource<PreviewTransportStateChanged>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var faulted = new TaskCompletionSource<PreviewTransportFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        transport.ConnectionChanged += (_, state) =>
        {
            if (!state.Connected)
                disconnected.TrySetResult(state);
        };
        transport.Faulted += (_, fault) => faulted.TrySetResult(fault);

        var start = transport.StartAsync();
        await using (var client = new NamedPipeClientStream(
                         ".",
                         transport.PipeName,
                         PipeDirection.InOut,
                         PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(2000);
            await start;
        }

        var fault = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var state = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(PreviewTransportFaultKind.EndOfStream, fault.Kind);
        Assert.IsType<EndOfStreamException>(state.Exception);
    }

    [Fact]
    public async Task NamedPipeTransport_RejectsOversizedFramePrecisely()
    {
        await using var transport = new NamedPipeUnityPreviewTransport();
        var disconnected = new TaskCompletionSource<PreviewTransportStateChanged>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var faulted = new TaskCompletionSource<PreviewTransportFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        transport.ConnectionChanged += (_, state) =>
        {
            if (!state.Connected)
                disconnected.TrySetResult(state);
        };
        transport.Faulted += (_, fault) => faulted.TrySetResult(fault);

        var start = transport.StartAsync();
        await using var client = new NamedPipeClientStream(
            ".",
            transport.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);
        await start;
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(
            header,
            64 * 1024 * 1024 + 1);
        await client.WriteAsync(header);
        await client.FlushAsync();

        var fault = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var state = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(PreviewTransportFaultKind.InvalidFrame, fault.Kind);
        Assert.IsType<InvalidDataException>(state.Exception);
        Assert.Contains("67108865", state.Reason, StringComparison.Ordinal);
    }

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        string failureMessage,
        int timeoutMilliseconds = 2000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!predicate() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(predicate(), failureMessage);
    }

    private static async Task WriteProtocolFrameAsync(
        Stream stream,
        PreviewProtocolMessage message)
    {
        var json = JObject.FromObject(message);
        json["protocol"] = NamedPipeUnityPreviewTransport.ProtocolName;
        var payload = Encoding.UTF8.GetBytes(
            json.ToString(Newtonsoft.Json.Formatting.None));
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
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
              "note_list": [{
                "id":0,"page_index":0,"type":0,"tick":240,"x":0.5,
                "hold_tick":0,"next_id":-1,
                "has_sibling":false,"is_forward":false
              }],
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
            ChartDifficulty = "hard",
            ProjectId = "test",
            ProjectName = "Test"
        };

    private sealed class FakeStoryboardValidator : IStoryboardDocumentValidator
    {
        public IReadOnlyList<StoryboardDiagnostic> Validate(StoryboardRoot document) => [];
        public IReadOnlyList<StoryboardDiagnostic> Validate(StoryboardRoot document, ProjectDataContext? context) => [];
        public IReadOnlyList<StoryboardDiagnostic> ValidateEntity(IStoryboardEntity entity, string path = "$") => [];
    }

    private sealed class ConnectedPreviewTransport : IUnityPreviewTransport
    {
        private long _generation;
        public bool IsConnected { get; private set; }
        public long Generation => _generation;
        public string PipeName => "connected-fake";
        public List<PreviewProtocolMessage> Sent { get; } = [];
        public string? FailSendType { get; init; }
        public event EventHandler<PreviewProtocolMessage>? MessageReceived;
        public event EventHandler<PreviewTransportStateChanged>? ConnectionChanged;
        public event EventHandler<PreviewTransportFault>? Faulted;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            _generation++;
            ConnectionChanged?.Invoke(this,
                new PreviewTransportStateChanged(_generation, true));
            return Task.CompletedTask;
        }

        public Task SendAsync(
            PreviewProtocolMessage message,
            CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            if (string.Equals(message.Type, FailSendType, StringComparison.Ordinal))
                throw new IOException($"simulated send failure for {message.Type}");
            if (message.Type == "host.accept")
            {
                Raise(message with
                {
                    Type = "host.ready",
                    Payload = new JObject
                    {
                        ["authenticationNonce"] =
                            message.Payload.Value<string>("authenticationNonce"),
                        ["hostRevision"] = 5
                    }
                });
            }
            if (message.Type == "preview.open")
            {
                Raise(message with
                {
                    Type = "preview.load.started",
                    Payload = new JObject { ["stage"] = "accepted" }
                });
                Raise(message with
                {
                    Type = "preview.load.progress",
                    Payload = new JObject { ["stage"] = "loadingSceneAndAssets" }
                });
                var chartPath = Path.Combine(
                    message.Payload.Value<string>("vfsRoot")!,
                    "chart.json");
                var chart = JObject.Parse(File.ReadAllText(chartPath));
                Raise(message with
                {
                    Type = "preview.load.ready",
                    Payload = new JObject
                    {
                        ["time"] = 0,
                        ["duration"] = 12,
                        ["chartIdentity"] = new JObject
                        {
                            ["path"] = chartPath,
                            ["sha256"] = Convert.ToHexString(
                                SHA256.HashData(File.ReadAllBytes(chartPath))).ToLowerInvariant(),
                            ["noteCount"] = (chart["note_list"] as JArray)?.Count ?? 0,
                            ["pageCount"] = (chart["page_list"] as JArray)?.Count ?? 0,
                            ["tempoCount"] = (chart["tempo_list"] as JArray)?.Count ?? 0
                        }
                    }
                });
            }
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Raise(PreviewProtocolMessage message) =>
            MessageReceived?.Invoke(this, message);
        public void RaiseConnection(PreviewTransportStateChanged state) =>
            ConnectionChanged?.Invoke(this, state);
    }

    private sealed class HandshakePreviewProcess(ConnectedPreviewTransport transport)
        : IUnityPreviewProcessService
    {
        public bool IsRunning { get; private set; }
        public bool IsGraphicsReady => IsRunning;
        public int? ProcessId => IsRunning ? 42 : null;
        public long Generation { get; private set; }
        public string RuntimePath => "fake";
        public UnityPreviewLaunchOptions? LaunchOptions { get; private set; }
        public event EventHandler<UnityPreviewProcessExited>? Exited;

        public Task StartAsync(
            UnityPreviewLaunchOptions options,
            CancellationToken cancellationToken = default)
        {
            LaunchOptions = options;
            IsRunning = true;
            Generation++;
            transport.Raise(new PreviewProtocolMessage(
                "host.hello",
                options.SessionId,
                Guid.NewGuid().ToString("N"),
                0,
                0,
                0,
                new JObject
                {
                    ["authenticationNonce"] = options.AuthenticationNonce,
                    ["hostRevision"] = 5,
                    ["capabilities"] = new JObject
                    {
                        ["officialRuntimeDataOnly"] = true,
                        ["chartPreflightV2"] = true,
                        ["unityLogV1"] = true,
                        ["loadProgressV1"] = true,
                        ["healthCheckV1"] = true,
                        ["threeWayHandshakeV2"] = true
                    }
                })
            {
                ConnectionId = options.ConnectionId,
                Generation = options.Generation
            });
            return Task.CompletedTask;
        }

        public Task ReparentAsync(
            IntPtr parentWindow,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(
            TimeSpan gracefulTimeout,
            CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void RaiseUnexpectedExit(int exitCode = -1)
        {
            IsRunning = false;
            Exited?.Invoke(this, new UnityPreviewProcessExited(
                Generation,
                42,
                exitCode,
                false));
        }

        public void Dispose() { }
    }

    private sealed class StaticPreviewSource(StoryboardPreviewSnapshot snapshot)
        : IStoryboardPreviewDataSource, IStoryboardChangeFeed
    {
        public long CurrentVersion => snapshot.Version;
        public StoryboardPreviewSnapshot GetSnapshot(
            ProjectDataContext context,
            double playbackTime = 0) => snapshot with { PlaybackTime = playbackTime };
        public IDisposable Subscribe(Action<StoryboardPreviewChangeSet> handler) =>
            new EmptyDisposable();
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class FakePreviewVfs : IPreviewVfsMaterializer
    {
        public Task<PreviewVfsVersion> MaterializeAsync(
            StoryboardPreviewSnapshot snapshot,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task PruneAsync(string sessionId,
            IReadOnlySet<long> protectedVersions, long maximumBytes) =>
            Task.CompletedTask;
    }

    private sealed class FakePreviewValidation : IPreviewValidationService
    {
        public PreviewValidationResult Validate(ProjectDataContext context,
            StoryboardPreviewSnapshot snapshot) =>
            PreviewValidationResult.Valid(snapshot.Version);
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
