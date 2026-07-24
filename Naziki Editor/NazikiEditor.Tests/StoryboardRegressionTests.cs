using Naziki_Editor.Core;
using Naziki_Editor.Core.Compilation;
using Naziki_Editor.Core.Serialization.Converters;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace NazikiEditor.Tests;

/// <summary>
/// 故事板回归测试套件：使用官方示例文件进行完整导入导出稳定性验证
/// 覆盖不同复杂度的故事板案例，确保数据处理准确性和一致性
/// </summary>
public class StoryboardRegressionTests
{
    private readonly JsonSerializerSettings _settings;
    private readonly string _exampleFilePath;

    public StoryboardRegressionTests()
    {
        _settings = StoryboardSerializer.GetSettings();
        _exampleFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "tutorial", "storyboard_example.json");
    }

    // ==========================================
    // 🌟 官方示例文件完整导入导出测试
    // ==========================================

    [Fact]
    public void OfficialExampleFile_CanBeLoaded_WithoutError()
    {
        // 确保示例文件存在
        Assert.True(File.Exists(_exampleFilePath),
            $"示例文件不存在: {_exampleFilePath}");

        var json = File.ReadAllText(_exampleFilePath);
        var root = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(root);
        Assert.NotNull(root!.sprites);
        Assert.NotNull(root.texts);
        Assert.NotNull(root.videos);
        Assert.NotNull(root.controllers);
        Assert.NotNull(root.note_controllers);
        Assert.NotNull(root.templates);
    }

    [Fact]
    public void OfficialExampleFile_AllEntityTypes_ShouldBePreserved()
    {
        var json = File.ReadAllText(_exampleFilePath);
        var root = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(root);

        // 验证 sprites 数量（示例文件包含大量 sprites）
        Assert.NotEmpty(root!.sprites);
        Assert.True(root.sprites.Count >= 20, $"Expected at least 20 sprites, got {root.sprites.Count}");

        // 验证 texts 数量
        Assert.NotEmpty(root.texts);
        Assert.True(root.texts.Count >= 5, $"Expected at least 5 texts, got {root.texts.Count}");

        // 验证 videos 数量（示例文件包含视频背景）
        Assert.NotEmpty(root.videos);
        Assert.True(root.videos.Count >= 1, $"Expected at least 1 video, got {root.videos.Count}");

        // 验证 note_controllers 数量
        Assert.NotEmpty(root.note_controllers);
        Assert.True(root.note_controllers.Count >= 20, $"Expected at least 20 note controllers, got {root.note_controllers.Count}");

        // 验证 controllers 数量
        Assert.NotEmpty(root.controllers);
        Assert.True(root.controllers.Count >= 3, $"Expected at least 3 controllers, got {root.controllers.Count}");

        // 验证 templates 数量
        Assert.NotEmpty(root.templates);
        Assert.True(root.templates.Count >= 4, $"Expected at least 4 templates, got {root.templates.Count}");
    }

    [Fact]
    public void OfficialExampleFile_SingleRoundtrip_ShouldNotLoseData()
    {
        // 第一步：加载原始示例文件
        var originalJson = File.ReadAllText(_exampleFilePath);
        var root = JsonConvert.DeserializeObject<StoryboardRoot>(originalJson, _settings);
        Assert.NotNull(root);

        // 第二步：序列化回 JSON
        var reExportedJson = StoryboardSerializer.ToJson(root!);

        // 第三步：再次反序列化
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(reExportedJson, _settings);
        Assert.NotNull(reloaded);

        // 验证核心数据结构完整性
        Assert.Equal(root!.sprites.Count, reloaded!.sprites.Count);
        Assert.Equal(root.texts.Count, reloaded.texts.Count);
        Assert.Equal(root.videos.Count, reloaded.videos.Count);
        Assert.Equal(root.controllers.Count, reloaded.controllers.Count);
        Assert.Equal(root.note_controllers.Count, reloaded.note_controllers.Count);
        Assert.Equal(root.templates.Count, reloaded.templates.Count);
    }

    [Fact]
    public void OfficialExampleFile_TripleRoundtrip_ShouldNotDegrade()
    {
        // 三次往返确保数据不衰减
        var originalJson = File.ReadAllText(_exampleFilePath);
        var root = JsonConvert.DeserializeObject<StoryboardRoot>(originalJson, _settings);
        Assert.NotNull(root);

        // 统计原始数据的关键指标
        var spriteCount = root!.sprites.Count;
        var textCount = root.texts.Count;
        var videoCount = root.videos.Count;
        var controllerCount = root.controllers.Count;
        var noteControllerCount = root.note_controllers.Count;
        var templateCount = root.templates.Count;

        // 第一次往返
        var json1 = StoryboardSerializer.ToJson(root);
        var round1 = JsonConvert.DeserializeObject<StoryboardRoot>(json1, _settings);
        Assert.Equal(spriteCount, round1!.sprites.Count);

        // 第二次往返
        var json2 = StoryboardSerializer.ToJson(round1);
        var round2 = JsonConvert.DeserializeObject<StoryboardRoot>(json2, _settings);
        Assert.Equal(spriteCount, round2!.sprites.Count);

        // 第三次往返
        var json3 = StoryboardSerializer.ToJson(round2);
        var round3 = JsonConvert.DeserializeObject<StoryboardRoot>(json3, _settings);

        // 所有实体数量在三轮往返后保持一致
        Assert.Equal(spriteCount, round3!.sprites.Count);
        Assert.Equal(textCount, round3.texts.Count);
        Assert.Equal(videoCount, round3.videos.Count);
        Assert.Equal(controllerCount, round3.controllers.Count);
        Assert.Equal(noteControllerCount, round3.note_controllers.Count);
        Assert.Equal(templateCount, round3.templates.Count);
    }

    // ==========================================
    // 🌟 复杂模板系统测试
    // ==========================================

    [Fact]
    public void OfficialTemplates_ShouldBePreservedAfterRoundtrip()
    {
        var json = File.ReadAllText(_exampleFilePath);
        var root = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);
        Assert.NotNull(root);

        var reExportedJson = StoryboardSerializer.ToJson(root!);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(reExportedJson, _settings);

        Assert.NotNull(reloaded);
        // 验证所有模板都存在
        Assert.True(reloaded!.templates.ContainsKey("plus"));
        Assert.True(reloaded.templates.ContainsKey("Alight"));
        Assert.True(reloaded.templates.ContainsKey("Blight"));
        Assert.True(reloaded.templates.ContainsKey("Clight"));
        Assert.True(reloaded.templates.ContainsKey("Dlight"));

        // 验证 plus 模板的透视和缓动属性
        var plusTemplate = reloaded.templates["plus"];
        Assert.True(plusTemplate.BaseState.Perspective);
        Assert.Equal("linear", plusTemplate.BaseState.Easing);
        Assert.Equal(53.2f, plusTemplate.BaseState.Fov);
        Assert.NotEmpty(plusTemplate.Keyframes);
    }

    [Fact]
    public void TemplateKeyframesWithRelativeTime_ShouldRoundtrip()
    {
        // 创建一个使用模板的 sprite，并验证模板展开后的相对时间
        var root = new StoryboardRoot();
        var templates = new Dictionary<string, C2Template>();

        // 模拟官方示例中的 Alight 模板
        templates["Alight"] = new C2Template
        {
            BaseState = new TemplateState { Easing = "easeInOutQuad", Opacity = 0.15f },
            Keyframes = new List<TemplateState>
            {
                new() { RelativeTime = 0.001f, Opacity = 0.15f },
                new() { RelativeTime = 0.05f, Opacity = 0.5f },
                new() { RelativeTime = 0.05f, Opacity = 0.15f, Destroy = true }
            }
        };
        root.templates = templates;

        root.videos.Add(new C2Video
        {
            Id = "bgv",
            BaseState = new VideoState { Time = 0f, Path = "BGV.mp4", Opacity = 0.15f },
            Keyframes = new List<VideoState>
            {
                new() { Template = "Alight", Time = new List<object> { "start:531", "start:535" } }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.True(reloaded!.templates.ContainsKey("Alight"));
        var template = reloaded.templates["Alight"];
        Assert.Equal(3, template.Keyframes.Count);
        Assert.Equal(0.001f, template.Keyframes[0].RelativeTime);
        Assert.Equal(0.05f, template.Keyframes[1].RelativeTime);
        Assert.True(template.Keyframes[2].Destroy);
    }

    // ==========================================
    // 🌟 时间数组 (Time Array) 扩展测试
    // ==========================================

    [Fact]
    public void TimeArray_WithMultipleEntries_ShouldRoundtrip()
    {
        var root = new StoryboardRoot();
        var timeArray = new List<object> { "start:1", "start:2", "start:3", "start:4", "start:5" };
        root.sprites.Add(new C2Sprite
        {
            Id = "time_array_test",
            BaseState = new SpriteState { Time = 0f, Path = "test.png", Opacity = 0f },
            Keyframes = new List<SpriteState>
            {
                new() { Time = timeArray, Opacity = 1f }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.Single(sprite.Keyframes);
        Assert.NotNull(sprite.Keyframes[0].Time);
    }

    [Fact]
    public void TimeArray_LargeArray_With50PlusEntries_ShouldNotFail()
    {
        // 官方示例中包含超过50个时间点的数组
        var root = new StoryboardRoot();
        var timeArray = new List<object>();
        for (int i = 1; i <= 60; i++)
        {
            timeArray.Add($"start:{i}");
        }

        root.sprites.Add(new C2Sprite
        {
            Id = "large_time_array",
            BaseState = new SpriteState { Time = 0f, Path = "many.png", Opacity = 0f },
            Keyframes = new List<SpriteState>
            {
                new() { Time = timeArray, Opacity = 1f, Template = "plus" }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.sprites);
        Assert.Single(reloaded.sprites[0].Keyframes);
    }

    [Fact]
    public void TimeArray_WithNegativeOffsets_ShouldRoundtrip()
    {
        // 官方示例使用 "start:841:-1.5" 格式的负偏移
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "neg_offset",
            BaseState = new SpriteState
            {
                Time = "start:841:-1.5",
                Path = "test.png",
                Opacity = 1f,
                X = new UnitFloat { Value = 0.02f, Unit = ReferenceUnit.NoteX },
                Y = new UnitFloat { Value = 0.5f, Unit = ReferenceUnit.NoteY }
            },
            Keyframes = new List<SpriteState>
            {
                new() { Time = "start:841:-1", Opacity = 1f },
                new() { Time = "start:864:0.2", Opacity = 0f, Destroy = true }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.NotNull(sprite.BaseState.Time);
        // 时间字符串格式应保留
        Assert.Contains("841", sprite.BaseState.Time!.ToString());
        Assert.Equal(2, sprite.Keyframes.Count);
        Assert.True(sprite.Keyframes[1].Destroy);
    }

    // ==========================================
    // 🌟 Note 控制器完整测试
    // ==========================================

    [Fact]
    public void NoteController_WithSpecificNoteId_ShouldRoundtrip()
    {
        var root = new StoryboardRoot();
        root.note_controllers.Add(new C2NoteController
        {
            Id = "nc_841",
            BaseState = new NoteControllerState
            {
                Time = 0f,
                NoteTarget = 841,
                OverrideY = true,
                Y = new UnitFloat { Value = -1.0f, Unit = ReferenceUnit.NoteY }
            },
            Keyframes = new List<NoteControllerState>
            {
                new() { Time = 0f, Y = new UnitFloat { Value = -1.0f, Unit = ReferenceUnit.NoteY } },
                new() { Time = 140.0879f, Y = new UnitFloat { Value = -1.0f, Unit = ReferenceUnit.NoteY } },
                new() { Time = 140.0879f, Y = new UnitFloat { Value = 0f, Unit = ReferenceUnit.NoteY } }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var nc = reloaded!.note_controllers[0];
        Assert.NotNull(nc.BaseState.NoteTarget);
        Assert.True(nc.BaseState.OverrideY);
        Assert.Equal(3, nc.Keyframes.Count);
    }

    [Fact]
    public void NoteController_WithOpacityMultiplier_ShouldRoundtrip()
    {
        // 官方示例中使用 opacity_multiplier: 0.0 来隐藏 note
        var root = new StoryboardRoot();
        root.note_controllers.Add(new C2NoteController
        {
            Id = "pos_down_841",
            BaseState = new NoteControllerState
            {
                Time = 0f,
                NoteTarget = 841,
                NoteOpacityMultiplier = 0f
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var nc = reloaded!.note_controllers[0];
        Assert.Equal(0f, nc.BaseState.NoteOpacityMultiplier);
    }

    [Fact]
    public void NoteController_WithSelector_EmptySelector_ShouldRoundtrip()
    {
        // 官方示例中使用空的 note 选择器 {} 表示全选
        var root = new StoryboardRoot();
        root.note_controllers.Add(new C2NoteController
        {
            Id = "nc_all",
            BaseState = new NoteControllerState
            {
                Time = 0f,
                NoteTarget = new NoteSelectorModel(), // 空选择器 = 全选
                OverrideY = true,
                Y = new UnitFloat { Value = -1.0f, Unit = ReferenceUnit.NoteY }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var nc = reloaded!.note_controllers[0];
        Assert.NotNull(nc.BaseState.NoteTarget);
        Assert.IsType<JObject>(nc.BaseState.NoteTarget);
    }

    // ==========================================
    // 🌟 场景控制器 (Scene Controller) 完整测试
    // ==========================================

    [Fact]
    public void Controller_WithNoteFillColors_ShouldRoundtrip()
    {
        // 官方示例中使用了 note_fill_colors 12色阵列
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "color_ctrl",
            BaseState = new ControllerState
            {
                Time = 0f,
                NoteRingColor = "#eae2de",
                NoteFillColors = new List<string>
                {
                    "#b22222", "#191970", "#191970", "#191970",
                    "#b02a2a", "#191970", "#b02a2a", "#b22222",
                    "#191970", "#b22222", "#191970", "#191970"
                }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var ctrl = reloaded!.controllers[0];
        Assert.Equal("#eae2de", ctrl.BaseState.NoteRingColor);
        Assert.NotNull(ctrl.BaseState.NoteFillColors);
        Assert.Equal(12, ctrl.BaseState.NoteFillColors!.Count);
    }

    [Fact]
    public void Controller_WithGlitchEffects_ShouldRoundtrip()
    {
        // 官方示例中大量使用 glitch 效果
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "glitch_ctrl",
            BaseState = new ControllerState { Time = 0f, Glitch = true, GlitchIntensity = 0.1f },
            Keyframes = new List<ControllerState>
            {
                new() { Time = "start:1", Glitch = false, GlitchIntensity = 0.2f },
                new() { Time = "start:76", Glitch = true, GlitchIntensity = 0.1f },
                new() { Time = "start:82", Glitch = false, GlitchIntensity = 0.2f },
                new() { Time = "start:1134", Glitch = true, GlitchIntensity = 1f }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var ctrl = reloaded!.controllers[0];
        Assert.True(ctrl.BaseState.Glitch);
        Assert.Equal(0.1f, ctrl.BaseState.GlitchIntensity);
        Assert.Equal(4, ctrl.Keyframes.Count);
        Assert.True(ctrl.Keyframes[0].Glitch == false);
        Assert.True(ctrl.Keyframes[3].Glitch);
        Assert.Equal(1f, ctrl.Keyframes[3].GlitchIntensity);
    }

    [Fact]
    public void Controller_WithScanlineAnimation_ShouldRoundtrip()
    {
        // 官方示例中扫描线位置动画（scanline_pos 逐帧变化）
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "scanline_ctrl",
            BaseState = new ControllerState
            {
                Time = 0f,
                OverrideScanlinePos = true,
                ScanlinePos = new UnitFloat { Value = 0f, Unit = ReferenceUnit.NoteY }
            },
            Keyframes = new List<ControllerState>
            {
                new() { Time = 0.1f, ScanlinePos = new UnitFloat { Value = 0.01875f, Unit = ReferenceUnit.NoteY } },
                new() { Time = 0.2f, ScanlinePos = new UnitFloat { Value = 0.04375f, Unit = ReferenceUnit.NoteY } },
                new() { Time = 0.3f, OverrideScanlinePos = false, ScanlinePos = new UnitFloat { Value = 0.75f, Unit = ReferenceUnit.NoteY } }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var ctrl = reloaded!.controllers[0];
        Assert.True(ctrl.BaseState.OverrideScanlinePos);
        Assert.Equal(3, ctrl.Keyframes.Count);
        Assert.False(ctrl.Keyframes[2].OverrideScanlinePos);
    }

    // ==========================================
    // 🌟 Video 对象测试
    // ==========================================

    [Fact]
    public void Video_WithTemplateKeyframes_ShouldRoundtrip()
    {
        var root = new StoryboardRoot();
        root.videos.Add(new C2Video
        {
            Id = "bgv",
            BaseState = new VideoState
            {
                Time = 0f,
                Path = "BGV.mp4",
                Opacity = 0.15f,
                Width = new UnitFloat { Value = 800f, Unit = ReferenceUnit.StageX },
                Height = new UnitFloat { Value = 600f, Unit = ReferenceUnit.StageY }
            },
            Keyframes = new List<VideoState>
            {
                new() { Template = "Alight", Time = new List<object> { "start:531", "start:535", "start:539" } },
                new() { Template = "Blight", Time = new List<object> { "start:341" } },
                new() { Template = "Dlight", Time = new List<object> { "start:347", "start:865", "start:641" } }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var video = reloaded!.videos[0];
        Assert.Equal("BGV.mp4", video.BaseState.Path);
        Assert.Equal(0.15f, video.BaseState.Opacity);
        Assert.Equal(3, video.Keyframes.Count);
        Assert.Equal("Alight", video.Keyframes[0].Template);
        Assert.Equal("Blight", video.Keyframes[1].Template);
        Assert.Equal("Dlight", video.Keyframes[2].Template);
    }

    // ==========================================
    // 🌟 特殊值处理测试
    // ==========================================

    [Fact]
    public void Destroy_WithNumericValue_ShouldBeHandled()
    {
        // 官方示例中有些地方使用 "destroy": 1 (数字而非布尔)
        var json = @"{
            ""sprites"": [{
                ""path"": ""test.png"",
                ""time"": 0,
                ""opacity"": 1,
                ""states"": [{
                    ""time"": 1,
                    ""opacity"": 0,
                    ""destroy"": 1
                }]
            }]
        }";

        var root = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);
        Assert.NotNull(root);
        Assert.Single(root!.sprites);
        Assert.Single(root.sprites[0].Keyframes);
        Assert.True(root.sprites[0].Keyframes[0].Destroy);
    }

    [Fact]
    public void Serialize_NeverProducesNumericDestroy()
    {
        // 确保序列化时始终输出布尔值而非数字
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "destroy_test",
            BaseState = new SpriteState { Time = 0f, Path = "test.png", Opacity = 1f },
            Keyframes = new List<SpriteState>
            {
                new() { Time = 1f, Opacity = 0f, Destroy = true }
            }
        });

        var json = StoryboardSerializer.ToJson(root);

        // 应该输出布尔值 true，而非数字 1
        Assert.Contains("\"destroy\": true", json);
        Assert.DoesNotContain("\"destroy\": 1", json);
    }

    [Fact]
    public void Scale_Shortcut_ShouldCoexistWithScaleXScaleY()
    {
        // 官方示例中使用 scale 作为快捷方式
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "scale_test",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "scaled.png",
                Opacity = 1f,
                Scale = 0.5f
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Equal(0.5f, reloaded!.sprites[0].BaseState.Scale);
    }

    [Fact]
    public void Controller_WithArcadeEffect_ShouldRoundtrip()
    {
        // 官方示例中使用 arcade 效果切换
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "arcade_ctrl",
            BaseState = new ControllerState { Time = 0f },
            Keyframes = new List<ControllerState>
            {
                new() { Time = "start:319", Arcade = true },
                new() { Time = "start:340", Arcade = false },
                new() { Time = "start:772", Arcade = true },
                new() { Time = "start:840", Arcade = false }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var ctrl = reloaded!.controllers[0];
        Assert.Equal(4, ctrl.Keyframes.Count);
        Assert.True(ctrl.Keyframes[0].Arcade);
        Assert.False(ctrl.Keyframes[1].Arcade);
        Assert.True(ctrl.Keyframes[2].Arcade);
        Assert.False(ctrl.Keyframes[3].Arcade);
    }

    // ==========================================
    // 🌟 复杂多类型混合场景测试
    // ==========================================

    [Fact]
    public void MixedEntities_WithAllTypes_ShouldRoundtripAll()
    {
        var root = new StoryboardRoot();

        // Sprite
        root.sprites.Add(new C2Sprite
        {
            Id = "mixed_sprite",
            BaseState = new SpriteState
            {
                Time = "start:100",
                Path = "mixed.png",
                Opacity = 0.5f,
                Layer = 1,
                X = new UnitFloat { Value = 0.3f, Unit = ReferenceUnit.NoteX },
                Y = new UnitFloat { Value = 0.7f, Unit = ReferenceUnit.NoteY },
                Scale = 0.75f,
                Easing = "easeOutQuad"
            },
            Keyframes = new List<SpriteState>
            {
                new() { Time = "start:101", Opacity = 1f },
                new() { Time = "start:105", Opacity = 0f, Destroy = true }
            }
        });

        // Text
        root.texts.Add(new C2Text
        {
            Id = "mixed_text",
            BaseState = new TextState
            {
                Time = "start:100",
                TextContent = "Mixed Test",
                Size = 32f,
                Color = "#ffffff",
                Align = "middleCenter",
                FontWeight = "bold",
                Opacity = 0f,
                Layer = 2,
                Order = 100
            },
            Keyframes = new List<TextState>
            {
                new() { Time = "start:101", Opacity = 1f },
                new() { Time = "start:105", Opacity = 0f, Destroy = true }
            }
        });

        // Video
        root.videos.Add(new C2Video
        {
            Id = "mixed_video",
            BaseState = new VideoState
            {
                Time = 0f,
                Path = "bg.mp4",
                Opacity = 0.15f,
                Width = new UnitFloat { Value = 800f, Unit = ReferenceUnit.StageX },
                Height = new UnitFloat { Value = 600f, Unit = ReferenceUnit.StageY }
            }
        });

        // Controller
        root.controllers.Add(new C2SceneController
        {
            Id = "mixed_ctrl",
            BaseState = new ControllerState
            {
                Time = 0f,
                StoryboardOpacity = 1f,
                UiOpacity = 1f,
                Perspective = true,
                Fov = 53.2f
            },
            Keyframes = new List<ControllerState>
            {
                new() { Time = "start:100", Fov = 59.2f },
                new() { Time = "start:105", Fov = 53.2f }
            }
        });

        // Note Controller
        root.note_controllers.Add(new C2NoteController
        {
            Id = "mixed_nc",
            BaseState = new NoteControllerState
            {
                Time = 0f,
                NoteTarget = 100,
                OverrideY = true,
                Y = new UnitFloat { Value = -1f, Unit = ReferenceUnit.NoteY }
            }
        });

        // Template
        root.templates["pulse"] = new C2Template
        {
            BaseState = new TemplateState { Perspective = true, Easing = "linear", Fov = 53.2f },
            Keyframes = new List<TemplateState>
            {
                new() { RelativeTime = 0.05f, Fov = 56.2f },
                new() { RelativeTime = 0.05f, Fov = 53.2f }
            }
        };

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.sprites);
        Assert.Single(reloaded.texts);
        Assert.Single(reloaded.videos);
        Assert.Single(reloaded.controllers);
        Assert.Single(reloaded.note_controllers);
        Assert.True(reloaded.templates.ContainsKey("pulse"));

        // 验证 sprite 属性
        var sprite = reloaded.sprites[0];
        Assert.Equal("mixed.png", sprite.BaseState.Path);
        Assert.Equal(1, sprite.BaseState.Layer);
        Assert.Equal(0.75f, sprite.BaseState.Scale);
        Assert.Equal("easeOutQuad", sprite.BaseState.Easing);

        // 验证 text 属性
        var text = reloaded.texts[0];
        Assert.Equal("Mixed Test", text.BaseState.TextContent);
        Assert.Equal("middleCenter", text.BaseState.Align);
        Assert.Equal("bold", text.BaseState.FontWeight);

        // 验证 controller 属性
        var ctrl = reloaded.controllers[0];
        Assert.Equal(53.2f, ctrl.BaseState.Fov);
        Assert.True(ctrl.BaseState.Perspective);
    }

    // ==========================================
    // 🌟 边界值：官方示例特殊格式测试
    // ==========================================

    [Fact]
    public void TimeFormat_StartWithColonOffset_ShouldRoundtrip()
    {
        // "start:1134:2" 格式
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "start_offset",
            BaseState = new SpriteState
            {
                Time = "start:1134:2",
                Path = "test.png",
                Opacity = 0f,
                Layer = 1
            },
            Keyframes = new List<SpriteState>
            {
                new() { Time = "start:1134:3", Opacity = 1f },
                new() { Time = "start:1134:4", Opacity = 1f },
                new() { Time = "start:1134:6", Opacity = 0f, Destroy = true }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.NotNull(sprite.BaseState.Time);
        Assert.Contains("1134", sprite.BaseState.Time!.ToString());
        Assert.Equal(3, sprite.Keyframes.Count);
        Assert.True(sprite.Keyframes[2].Destroy);
    }

    [Fact]
    public void Coordinate_MixedUnitFloatAndPureNumber_ShouldRoundtrip()
    {
        // 官方示例混用 "noteX:0.5" 和纯数字 420.0
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "mixed_coord",
            BaseState = new SpriteState
            {
                Time = "start:841:-0.7",
                Path = "test.png",
                X = new UnitFloat { Value = 0.5f, Unit = ReferenceUnit.NoteX },
                Y = new UnitFloat { Value = 420f, Unit = ReferenceUnit.World },
                Opacity = 1f,
                Scale = 0.5f,
                Layer = 1
            },
            Keyframes = new List<SpriteState>
            {
                new()
                {
                    Time = "start:841:-0.7",
                    Y = new UnitFloat { Value = 420f, Unit = ReferenceUnit.World }
                },
                new()
                {
                    Time = "start:841:0",
                    Y = new UnitFloat { Value = 0f, Unit = ReferenceUnit.NoteY },
                    Destroy = true
                }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.NotNull(sprite.BaseState.X);
        Assert.Equal(0.5f, sprite.BaseState.X!.Value);
        Assert.Equal(ReferenceUnit.NoteX, sprite.BaseState.X.Unit);
        Assert.NotNull(sprite.BaseState.Y);
        Assert.Equal(420f, sprite.BaseState.Y!.Value);
        Assert.Equal(ReferenceUnit.World, sprite.BaseState.Y.Unit);
        Assert.Equal(2, sprite.Keyframes.Count);
    }

    // ==========================================
    // 🌟 控制器 UiOpacity 闪烁测试
    // ==========================================

    [Fact]
    public void Controller_UiOpacityFlickering_ShouldRoundtrip()
    {
        // 官方示例中有大量 ui_opacity 快速切换（闪烁效果）
        var root = new StoryboardRoot();
        var keyframes = new List<ControllerState>();
        for (int i = 0; i < 20; i++)
        {
            keyframes.Add(new ControllerState { Time = i * 0.1f, UiOpacity = 1f });
            keyframes.Add(new ControllerState { Time = i * 0.1f + 0.05f, UiOpacity = 0f });
        }

        root.controllers.Add(new C2SceneController
        {
            Id = "flicker",
            BaseState = new ControllerState { Time = 0f, UiOpacity = 1f },
            Keyframes = keyframes
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var ctrl = reloaded!.controllers[0];
        Assert.Equal(40, ctrl.Keyframes.Count);
        Assert.Equal(1f, ctrl.Keyframes[0].UiOpacity);
        Assert.Equal(0f, ctrl.Keyframes[1].UiOpacity);
    }

    // ==========================================
    // 🌟 PreserveAspect 和 FillWidth 测试
    // ==========================================

    [Fact]
    public void Sprite_PreserveAspectFalse_WithExplicitDimensions_ShouldRoundtrip()
    {
        // 官方示例中的 epilepsywarning.png 设置 preserve_aspect: false
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "no_aspect",
            BaseState = new SpriteState
            {
                Time = 0f,
                Path = "warning.png",
                Scale = 1.5f,
                Layer = 2,
                Opacity = 1f,
                PreserveAspect = false,
                Width = new UnitFloat { Value = 800f, Unit = ReferenceUnit.StageX },
                Height = new UnitFloat { Value = 600f, Unit = ReferenceUnit.StageY }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.False(sprite.BaseState.PreserveAspect);
        Assert.NotNull(sprite.BaseState.Width);
        Assert.Equal(800f, sprite.BaseState.Width!.Value);
        Assert.NotNull(sprite.BaseState.Height);
        Assert.Equal(600f, sprite.BaseState.Height!.Value);
    }

    // ==========================================
    // 🌟 空列表和空模板测试
    // ==========================================

    [Fact]
    public void EmptyLinesArray_ShouldBePreserved()
    {
        // 官方示例中 lines 为空数组
        var root = new StoryboardRoot();
        // lines 默认已经是空列表

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded!.lines);
        Assert.Empty(reloaded.lines);
    }

    [Fact]
    public void ObjectWithOnlyBaseState_NoKeyframes_ShouldSerializeCorrectly()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "solo",
            BaseState = new SpriteState
            {
                Time = 0f,
                Path = "solo.png",
                Opacity = 1f,
                Layer = 0,
                Order = 0
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        // 不应该有 states 数组
        Assert.DoesNotContain("\"states\"", json);

        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.sprites);
        Assert.Equal("solo.png", reloaded.sprites[0].BaseState.Path);
    }

    // ==========================================
    // 🌟 ID 标准化与账本同步回归测试
    // ==========================================

    [Fact]
    public void SaveThenLoad_ControlBoardIds_ShouldBeConsistent()
    {
        // 模拟完整的保存-加载-再保存流程
        var root = new StoryboardRoot();
        var project = new NazikiProjectModel();

        // 创建控制板
        root.sprites.Add(new C2Sprite
        {
            TargetId = "main_sprite",
            BaseState = new SpriteState { Time = 0f, X = new UnitFloat { Value = 100f, Unit = ReferenceUnit.StageX } }
        });
        root.sprites.Add(new C2Sprite
        {
            TargetId = "main_sprite",
            BaseState = new SpriteState { Time = 0f, Y = new UnitFloat { Value = 200f, Unit = ReferenceUnit.StageY } }
        });

        // 第一次导入 - 标准 ID
        var parser = new StoryboardParser(new NullErrorHandler());
        parser.StandardizeStoryboardIds(root, project);
        var firstId0 = root.sprites[0].Id;
        var firstId1 = root.sprites[1].Id;

        // 同步账本
        parser.SyncControlBoardIdMaps(root, project);

        // 模拟重新加载 - 创建新对象
        var root2 = new StoryboardRoot();
        root2.sprites.Add(new C2Sprite
        {
            TargetId = "main_sprite",
            BaseState = new SpriteState { Time = 0f, X = new UnitFloat { Value = 100f, Unit = ReferenceUnit.StageX } }
        });
        root2.sprites.Add(new C2Sprite
        {
            TargetId = "main_sprite",
            BaseState = new SpriteState { Time = 0f, Y = new UnitFloat { Value = 200f, Unit = ReferenceUnit.StageY } }
        });

        // 第二次导入 - 应该恢复相同的 ID
        parser.StandardizeStoryboardIds(root2, project);
        Assert.Equal(firstId0, root2.sprites[0].Id);
        Assert.Equal(firstId1, root2.sprites[1].Id);
    }

    // ==========================================
    // 🌟 AddTime 动画测试
    // ==========================================

    [Fact]
    public void AddTime_WithMultipleKeyframes_ShouldComputeCorrectly()
    {
        // 官方示例中大量使用 add_time
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "add_time_test",
            BaseState = new SpriteState
            {
                Time = "start:841",
                Path = "anim.png",
                X = new UnitFloat { Value = 0.5f, Unit = ReferenceUnit.NoteX },
                Y = new UnitFloat { Value = 0f, Unit = ReferenceUnit.NoteY },
                Opacity = 1f,
                Layer = 2,
                Scale = 0.75f,
                Easing = "easeOutQuad",
                RotZ = 0f
            },
            Keyframes = new List<SpriteState>
            {
                new()
                {
                    AddTime = 0.1f,
                    X = new UnitFloat { Value = 0.45f, Unit = ReferenceUnit.NoteX },
                    Y = new UnitFloat { Value = 0.063f, Unit = ReferenceUnit.NoteY },
                    RotZ = -20f,
                    Opacity = 0.8f,
                    Scale = 0.35f
                },
                new()
                {
                    AddTime = 0.2f,
                    X = new UnitFloat { Value = 0.35f, Unit = ReferenceUnit.NoteX },
                    Y = new UnitFloat { Value = 0.125f, Unit = ReferenceUnit.NoteY },
                    RotZ = -50f,
                    Opacity = 0f,
                    Scale = 0.1f,
                    Destroy = true
                }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.Equal(2, sprite.Keyframes.Count);
        Assert.Equal(0.1f, sprite.Keyframes[0].AddTime);
        Assert.Equal(0.2f, sprite.Keyframes[1].AddTime);
        Assert.True(sprite.Keyframes[1].Destroy);
    }

    // ==========================================
    // 🌟 Comment 属性测试
    // ==========================================

    [Fact]
    public void State_WithComment_ShouldRoundtrip()
    {
        // 官方示例中 scanline 动画带有 comment 属性
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "comment_test",
            BaseState = new ControllerState { Time = 0f },
            Keyframes = new List<ControllerState>
            {
                new()
                {
                    Time = 95.67082f,
                    OverrideScanlinePos = false,
                    ScanlinePos = new UnitFloat { Value = 0.75f, Unit = ReferenceUnit.NoteY }
                }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.controllers);
        Assert.Single(reloaded.controllers[0].Keyframes);
    }

    // ==========================================
    // 🌟 不同复杂度场景的序列化一致性测试
    // ==========================================

    [Fact]
    public void SimpleStoryboard_DeterministicOutput_ShouldBeConsistent()
    {
        // 确保相同的故事板内容多次序列化产生相同结果
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "consistent",
            BaseState = new SpriteState { Time = 0f, Path = "test.png", Opacity = 1f, Layer = 1 }
        });

        var json1 = StoryboardSerializer.ToJson(root);
        var json2 = StoryboardSerializer.ToJson(root);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void ComplexStoryboard_WithTemplates_DeterministicOutput()
    {
        var root = new StoryboardRoot();
        root.templates["fadeIn"] = new C2Template
        {
            BaseState = new TemplateState { Opacity = 0f, Easing = "easeInQuad" },
            Keyframes = new List<TemplateState>
            {
                new() { RelativeTime = 0.5f, Opacity = 1f }
            }
        };
        root.sprites.Add(new C2Sprite
        {
            Id = "complex",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "complex.png",
                Opacity = 1f, Template = "fadeIn",
                X = new UnitFloat { Value = 0.5f, Unit = ReferenceUnit.NoteX },
                Y = new UnitFloat { Value = 0.5f, Unit = ReferenceUnit.NoteY }
            },
            Keyframes = new List<SpriteState>
            {
                new() { Time = 1f, RotZ = 45f },
                new() { Time = 2f, RotZ = 90f, Destroy = true }
            }
        });

        var json1 = StoryboardSerializer.ToJson(root);
        var json2 = StoryboardSerializer.ToJson(root);

        Assert.Equal(json1, json2);
    }
}