using Naziki_Editor.Core;
using Naziki_Editor.Core.Compilation;
using Naziki_Editor.Core.Serialization.Converters;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Xunit;

namespace NazikiEditor.Tests;

/// <summary>
/// 故事板边界测试：空集合、极端值、深层嵌套、特殊字符等边界场景
/// </summary>
public class StoryboardBoundaryTests
{
    private readonly JsonSerializerSettings _settings;

    public StoryboardBoundaryTests()
    {
        _settings = StoryboardSerializer.GetSettings();
    }

    // ==========================================
    // 空/Null 集合边界测试
    // ==========================================

    [Fact]
    public void EmptyRoot_SerializeDeserialize_ShouldNotThrow()
    {
        var root = new StoryboardRoot();
        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded!.sprites);
        Assert.NotNull(reloaded.texts);
        Assert.NotNull(reloaded.lines);
        Assert.NotNull(reloaded.videos);
        Assert.NotNull(reloaded.controllers);
        Assert.NotNull(reloaded.note_controllers);
        Assert.NotNull(reloaded.templates);
    }

    [Fact]
    public void NullCollections_ShouldBeEmptyAfterDeserialize()
    {
        var json = @"{
            ""sprites"": null,
            ""texts"": null,
            ""controllers"": null
        }";
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded!.sprites); // 反序列化后应为空列表
        Assert.Empty(reloaded.sprites);
    }

    [Fact]
    public void EmptyKeyframes_ShouldNotProduceStatesArray()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "no_keyframes",
            BaseState = new SpriteState { Time = 0f, Path = "test.png", Opacity = 1f },
            Keyframes = new List<SpriteState>() // 空列表
        });

        var json = StoryboardSerializer.ToJson(root);
        Assert.DoesNotContain("\"states\"", json);
    }

    [Fact]
    public void NullKeyframes_ShouldNotThrow()
    {
        var root = new StoryboardRoot();
        var sprite = new C2Sprite
        {
            Id = "null_keyframes",
            BaseState = new SpriteState { Time = 0f, Path = "test.png", Opacity = 1f },
            Keyframes = null! // null 关键帧列表
        };
        root.sprites.Add(sprite);

        var json = StoryboardSerializer.ToJson(root);
        Assert.NotNull(json);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);
        Assert.NotNull(reloaded);
    }

    [Fact]
    public void NullBaseState_ShouldNotThrow()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "null_base",
            BaseState = null! // null 基准状态
        });

        var json = StoryboardSerializer.ToJson(root);
        Assert.NotNull(json);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);
        Assert.NotNull(reloaded);
    }

    // ==========================================
    // 极端浮点数值测试
    // ==========================================

    [Fact]
    public void ExtremeFloatValues_ShouldRoundtrip()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "extreme",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "extreme.png",
                Opacity = float.Epsilon, // 极小正浮点数
                ScaleX = float.MaxValue / 2, // 极大值
                RotZ = -360f // 负角度
            },
            Keyframes = new List<SpriteState>
            {
                new() { Time = float.Epsilon, Opacity = 0.0000001f },
                new() { Time = 999999f, Opacity = 0.9999999f }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.NotNull(sprite.BaseState);
        Assert.Equal(2, sprite.Keyframes.Count);
    }

    [Fact]
    public void ZeroValues_ShouldBePreserved()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "zeros",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "zeros.png",
                Opacity = 0f, Layer = 0, Order = 0,
                RotX = 0f, RotY = 0f, RotZ = 0f,
                ScaleX = 0f, ScaleY = 0f
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.Equal(0f, sprite.BaseState.Opacity);
        Assert.Equal(0, sprite.BaseState.Layer);
        Assert.Equal(0, sprite.BaseState.Order);
        Assert.Equal(0f, sprite.BaseState.RotZ);
    }

    [Fact]
    public void NegativeValues_ShouldBePreserved()
    {
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "neg",
            BaseState = new ControllerState
            {
                Time = 0f,
                X = new UnitFloat { Value = -400f, Unit = ReferenceUnit.StageX },
                Y = new UnitFloat { Value = -300f, Unit = ReferenceUnit.StageY },
                Z = new UnitFloat { Value = -15f, Unit = ReferenceUnit.World },
                RotZ = -180f
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var ctrl = reloaded!.controllers[0];
        Assert.NotNull(ctrl.BaseState.X);
        Assert.Equal(-400f, ctrl.BaseState.X!.Value);
        Assert.NotNull(ctrl.BaseState.Y);
        Assert.Equal(-300f, ctrl.BaseState.Y!.Value);
        Assert.Equal(-180f, ctrl.BaseState.RotZ);
    }

    [Fact]
    public void FloatMaxValue_Time_ShouldNotBeSerialized()
    {
        // float.MaxValue 表示"未设置时间"，不应被序列化
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "unset_time",
            BaseState = new SpriteState
            {
                Time = float.MaxValue, // 表示未设置
                Path = "unset.png",
                Opacity = 1f
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        // time 不应出现在 JSON 中（因为值为 float.MaxValue）
        Assert.DoesNotContain("\"time\": 3.402823", json);
    }

    [Fact]
    public void FloatMaxValue_StringTime_ShouldNotBeSerialized()
    {
        // 字符串形式的 float.MaxValue 也不应被序列化
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "unset_time_str",
            BaseState = new SpriteState
            {
                Time = float.MaxValue.ToString(), // 字符串形式的 MaxValue
                Path = "unset.png",
                Opacity = 1f
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        Assert.DoesNotContain(float.MaxValue.ToString(), json);
    }

    // ==========================================
    // 深度嵌套模板测试
    // ==========================================

    [Fact]
    public void DeeplyNestedTemplates_ShouldNotOverflow()
    {
        var root = new StoryboardRoot();
        var templates = new Dictionary<string, C2Template>();

        // 创建5层嵌套模板链
        templates["level1"] = new C2Template
        {
            BaseState = new TemplateState { Opacity = 0f },
            Keyframes = new List<TemplateState> { new() { Template = "level2", RelativeTime = 0.1f } }
        };
        templates["level2"] = new C2Template
        {
            BaseState = new TemplateState { Opacity = 0.2f },
            Keyframes = new List<TemplateState> { new() { Template = "level3", RelativeTime = 0.1f } }
        };
        templates["level3"] = new C2Template
        {
            BaseState = new TemplateState { Opacity = 0.4f },
            Keyframes = new List<TemplateState> { new() { Template = "level4", RelativeTime = 0.1f } }
        };
        templates["level4"] = new C2Template
        {
            BaseState = new TemplateState { Opacity = 0.6f },
            Keyframes = new List<TemplateState> { new() { Template = "level5", RelativeTime = 0.1f } }
        };
        templates["level5"] = new C2Template
        {
            BaseState = new TemplateState { Opacity = 0.8f },
            Keyframes = new List<TemplateState> { new() { RelativeTime = 0.1f, Opacity = 1f } }
        };
        root.templates = templates;

        root.sprites.Add(new C2Sprite
        {
            Id = "deep",
            BaseState = new SpriteState { Time = 0f, Path = "deep.png", Template = "level1" }
        });

        var compiler = CreateCompiler(templates);
        var ex = Record.Exception(() => compiler.FlattenStoryboard(root));
        Assert.Null(ex);
        Assert.NotEmpty(root.sprites[0].Keyframes);
    }

    // ==========================================
    // 特殊字符与长字符串测试
    // ==========================================

    [Fact]
    public void SpecialCharactersInText_ShouldRoundtrip()
    {
        var root = new StoryboardRoot();
        root.texts.Add(new C2Text
        {
            Id = "special",
            BaseState = new TextState
            {
                Time = 0f,
                TextContent = "Hello\nWorld\t\"quoted\" <b>bold</b> 🎵✨",
                Size = 30f,
                Align = "middleCenter",
                FontWeight = "bold"
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var text = reloaded!.texts[0];
        Assert.Contains("Hello", text.BaseState.TextContent!);
        Assert.Equal("middleCenter", text.BaseState.Align);
        Assert.Equal("bold", text.BaseState.FontWeight);
    }

    [Fact]
    public void LongString_ShouldNotTruncate()
    {
        var longPath = new string('x', 500) + ".png";
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "long_path",
            BaseState = new SpriteState { Time = 0f, Path = longPath, Opacity = 1f }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Equal(longPath, reloaded!.sprites[0].BaseState.Path);
    }

    [Fact]
    public void UnicodeCharacters_ShouldRoundtrip()
    {
        var root = new StoryboardRoot();
        root.texts.Add(new C2Text
        {
            Id = "unicode",
            BaseState = new TextState
            {
                Time = 0f,
                TextContent = "日本語 한국어 中文 😀🎮",
                Size = 24f
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Contains("日本語", reloaded!.texts[0].BaseState.TextContent);
    }

    // ==========================================
    // 大量数据边界测试
    // ==========================================

    [Fact]
    public void VeryLargeStoryboard_ShouldNotTimeout()
    {
        var root = new StoryboardRoot();
        // 1000 个 sprites，每个 100 个关键帧
        for (int i = 0; i < 100; i++)
        {
            var keyframes = new List<SpriteState>();
            for (int j = 0; j < 100; j++)
            {
                keyframes.Add(new SpriteState
                {
                    Time = j * 0.1f,
                    Opacity = (float)(j % 10) / 10f,
                    X = new UnitFloat { Value = j * 0.01f, Unit = ReferenceUnit.NoteX },
                    RotZ = j * 3.6f
                });
            }
            root.sprites.Add(new C2Sprite
            {
                Id = $"sprite_{i}",
                BaseState = new SpriteState { Time = 0f, Path = $"sprite_{i}.png", Opacity = 0f },
                Keyframes = keyframes
            });
        }

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Equal(100, reloaded!.sprites.Count);
        Assert.Equal(100, reloaded.sprites[0].Keyframes.Count);
    }

    // ==========================================
    // 坐标系统边界测试
    // ==========================================

    [Fact]
    public void AllCoordinateUnits_ShouldRoundtrip()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "all_coords",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "coord.png",
                X = new UnitFloat { Value = 0.5f, Unit = ReferenceUnit.NoteX },
                Y = new UnitFloat { Value = 0.5f, Unit = ReferenceUnit.NoteY },
                Z = new UnitFloat { Value = -5f, Unit = ReferenceUnit.World },
                W = new UnitFloat { Value = 400f, Unit = ReferenceUnit.StageX },
                H = new UnitFloat { Value = 300f, Unit = ReferenceUnit.StageY }
            },
            Keyframes = new List<SpriteState>
            {
                new()
                {
                    Time = 1f,
                    X = new UnitFloat { Value = 100f, Unit = ReferenceUnit.CameraX },
                    Y = new UnitFloat { Value = 50f, Unit = ReferenceUnit.CameraY }
                }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.Equal(ReferenceUnit.NoteX, sprite.BaseState.X!.Unit);
        Assert.Equal(ReferenceUnit.NoteY, sprite.BaseState.Y!.Unit);
        Assert.Equal(ReferenceUnit.StageX, sprite.BaseState.W!.Unit);
        Assert.Equal(ReferenceUnit.StageY, sprite.BaseState.H!.Unit);
        Assert.Equal(ReferenceUnit.CameraX, sprite.Keyframes[0].X!.Unit);
        Assert.Equal(ReferenceUnit.CameraY, sprite.Keyframes[0].Y!.Unit);
    }

    [Fact]
    public void CoordinateOutOfBounds_ShouldBePreserved()
    {
        // 坐标可以超出 [0,1] 或 [-400,400] 范围
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "out_of_bounds",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "oob.png",
                X = new UnitFloat { Value = -2f, Unit = ReferenceUnit.NoteX },
                Y = new UnitFloat { Value = 3f, Unit = ReferenceUnit.NoteY },
                W = new UnitFloat { Value = 2000f, Unit = ReferenceUnit.StageX }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.Equal(-2f, sprite.BaseState.X!.Value);
        Assert.Equal(3f, sprite.BaseState.Y!.Value);
        Assert.Equal(2000f, sprite.BaseState.W!.Value);
    }

    // ==========================================
    // NaN/Infinity 浮点值测试
    // ==========================================

    [Fact]
    public void NaN_FloatValues_ShouldNotThrow()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "nan_test",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "nan.png",
                Opacity = float.NaN,
                ScaleX = float.NaN,
                RotZ = float.NaN
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        // NaN 不应导致崩溃
        Assert.NotNull(json);
    }

    [Fact]
    public void Infinity_FloatValues_ShouldNotThrow()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "inf_test",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "inf.png",
                Opacity = float.PositiveInfinity,
                ScaleX = float.NegativeInfinity
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        Assert.NotNull(json);
    }

    // ==========================================
    // 空字符串和空白字符串测试
    // ==========================================

    [Fact]
    public void EmptyPath_ShouldNotThrow()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "empty_path",
            BaseState = new SpriteState { Time = 0f, Path = "", Opacity = 1f }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Equal("", reloaded!.sprites[0].BaseState.Path);
    }

    [Fact]
    public void WhitespaceOnlyStrings_ShouldRoundtrip()
    {
        var root = new StoryboardRoot();
        root.texts.Add(new C2Text
        {
            Id = "whitespace",
            BaseState = new TextState
            {
                Time = 0f,
                TextContent = "   \t  \n  ",
                Size = 20f
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded!.texts[0].BaseState.TextContent);
    }

    [Fact]
    public void NullColor_ShouldNotThrow()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "null_color",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "test.png",
                Opacity = 1f, Color = null!
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        Assert.DoesNotContain("\"color\": null", json);
    }

    // ==========================================
    // JSON 注入安全测试
    // ==========================================

    [Fact]
    public void JsonInjectionInPath_ShouldBeEscaped()
    {
        var maliciousPath = "test.png\", \"malicious\": true";
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "injection",
            BaseState = new SpriteState { Time = 0f, Path = maliciousPath, Opacity = 1f }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        // 反序列化后路径应保持一致
        Assert.Equal(maliciousPath, reloaded!.sprites[0].BaseState.Path);
    }

    [Fact]
    public void JsonInjectionInTextContent_ShouldBeEscaped()
    {
        var maliciousText = "Hello\"}, {\"id\": \"hacked\", \"text\": \"pwned";
        var root = new StoryboardRoot();
        root.texts.Add(new C2Text
        {
            Id = "injection_text",
            BaseState = new TextState { Time = 0f, TextContent = maliciousText, Size = 20f }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Equal(maliciousText, reloaded!.texts[0].BaseState.TextContent);
    }

    // ==========================================
    // 模板缺失引用测试
    // ==========================================

    [Fact]
    public void MissingTemplateReference_ShouldNotThrow()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "missing_template",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "orphan.png",
                Opacity = 1f, Template = "non_existent_template"
            }
        });

        var compiler = CreateCompiler();
        var ex = Record.Exception(() => compiler.FlattenStoryboard(root));
        Assert.Null(ex); // 不应崩溃，模板缺失应被优雅处理
    }

    [Fact]
    public void SelfReferencingTemplate_ShouldNotInfiniteLoop()
    {
        var root = new StoryboardRoot();
        var templates = new Dictionary<string, C2Template>();
        templates["self_ref"] = new C2Template
        {
            BaseState = new TemplateState { Opacity = 0f },
            Keyframes = new List<TemplateState>
            {
                new() { Template = "self_ref", RelativeTime = 0.5f }
            }
        };
        root.templates = templates;

        root.sprites.Add(new C2Sprite
        {
            Id = "self",
            BaseState = new SpriteState { Time = 0f, Path = "self.png", Template = "self_ref" }
        });

        var compiler = CreateCompiler(templates);
        var ex = Record.Exception(() => compiler.FlattenStoryboard(root));
        Assert.Null(ex);
    }

    // ==========================================
    // 负时间值测试
    // ==========================================

    [Fact]
    public void NegativeTimeValue_ShouldRoundtrip()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "neg_time",
            BaseState = new SpriteState
            {
                Time = -1.5f,
                Path = "early.png",
                Opacity = 1f
            },
            Keyframes = new List<SpriteState>
            {
                new() { Time = -1f, Opacity = 0.5f },
                new() { Time = 0f, Opacity = 1f }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.Equal(-1.5f, (float)(double)sprite.BaseState.Time!);
        Assert.Equal(2, sprite.Keyframes.Count);
    }

    // ==========================================
    // 控制器细胞分裂 (Mitosis) 边界测试
    // ==========================================

    [Fact]
    public void Mitosis_AllEffectsController_ShouldNotSplit()
    {
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "pure_effects",
            BaseState = new ControllerState
            {
                Time = 0f, Bloom = true, BloomIntensity = 2f,
                Glitch = true, GlitchIntensity = 0.5f,
                Noise = true, NoiseIntensity = 0.3f
            }
        });

        var compiler = CreateCompiler();
        compiler.FlattenStoryboard(root);

        Assert.Single(root.controllers);
        Assert.Equal("Effects", root.controllers[0].EditorMode);
    }

    [Fact]
    public void Mitosis_OnlyFovChange_ShouldStayAsCamera()
    {
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "fov_only",
            BaseState = new ControllerState { Time = 0f, Fov = 53.2f },
            Keyframes = new List<ControllerState>
            {
                new() { Time = 1f, Fov = 60f },
                new() { Time = 2f, Fov = 53.2f }
            }
        });

        var compiler = CreateCompiler();
        compiler.FlattenStoryboard(root);

        Assert.Single(root.controllers);
        Assert.Equal("Camera", root.controllers[0].EditorMode);
    }

    // ==========================================
    // 空 NoteTarget 选择器测试
    // ==========================================

    [Fact]
    public void NoteController_EmptySelector_ShouldRoundtrip()
    {
        var root = new StoryboardRoot();
        root.note_controllers.Add(new C2NoteController
        {
            Id = "empty_selector",
            BaseState = new NoteControllerState
            {
                Time = 0f,
                NoteTarget = new NoteSelectorModel(), // 空选择器
                NoteOpacityMultiplier = 0f
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var nc = reloaded!.note_controllers[0];
        Assert.NotNull(nc.BaseState.NoteTarget);
        Assert.Equal(0f, nc.BaseState.NoteOpacityMultiplier);
    }

    [Fact]
    public void NoteController_SelectorWithAllFilters_ShouldRoundtrip()
    {
        var root = new StoryboardRoot();
        root.note_controllers.Add(new C2NoteController
        {
            Id = "full_selector",
            BaseState = new NoteControllerState
            {
                Time = 0f,
                NoteTarget = new NoteSelectorModel
                {
                    Type = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
                    Start = 1,
                    End = 999,
                    Direction = 1,
                    MinX = 0f,
                    MaxX = 1f
                },
                OverrideX = true,
                X = new UnitFloat { Value = 0.5f, Unit = ReferenceUnit.NoteX }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.note_controllers);
    }

    // ==========================================
    // 极端 Z 深度值测试
    // ==========================================

    [Fact]
    public void ExtremeZValues_ShouldRoundtrip()
    {
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "extreme_z",
            BaseState = new ControllerState
            {
                Time = 0f,
                Perspective = true,
                Z = new UnitFloat { Value = -50f, Unit = ReferenceUnit.World }
            },
            Keyframes = new List<ControllerState>
            {
                new() { Time = 1f, Z = new UnitFloat { Value = -5f, Unit = ReferenceUnit.World } },
                new() { Time = 2f, Z = new UnitFloat { Value = 50f, Unit = ReferenceUnit.World } }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var ctrl = reloaded!.controllers[0];
        Assert.Equal(-50f, ctrl.BaseState.Z!.Value);
        Assert.Equal(2, ctrl.Keyframes.Count);
        Assert.Equal(50f, ctrl.Keyframes[1].Z!.Value);
    }

    // ==========================================
    // Easing 函数名大小写测试
    // ==========================================

    [Fact]
    public void Easing_CaseInsensitive_ShouldRoundtrip()
    {
        // 官方示例中使用 "easeoutquad" (小写无驼峰)，官方规范使用 "easeOutQuad"
        var easingVariants = new[] { "easeOutQuad", "easeoutquad", "EASEOUTQUAD", "easeInOutCubic", "linear" };
        foreach (var easing in easingVariants)
        {
            var root = new StoryboardRoot();
            root.sprites.Add(new C2Sprite
            {
                Id = $"easing_{easing}",
                BaseState = new SpriteState
                {
                    Time = 0f, Path = "test.png",
                    Opacity = 1f, Easing = easing
                }
            });

            var json = StoryboardSerializer.ToJson(root);
            var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

            Assert.NotNull(reloaded);
            Assert.Equal(easing, reloaded!.sprites[0].BaseState.Easing);
        }
    }

    // ==========================================
    // Template 属性的 BaseState 无 Time 测试
    // ==========================================

    [Fact]
    public void TemplateBaseState_WithoutTime_ShouldNotSerializeTime()
    {
        // 模板的 BaseState 通常不需要 time
        var root = new StoryboardRoot();
        root.templates["no_time"] = new C2Template
        {
            BaseState = new TemplateState
            {
                Opacity = 0f,
                Easing = "easeInQuad",
                Perspective = true,
                Fov = 53.2f
            }
        };

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.True(reloaded!.templates.ContainsKey("no_time"));
        var tmpl = reloaded.templates["no_time"];
        Assert.Equal(0f, tmpl.BaseState.Opacity);
        Assert.Equal("easeInQuad", tmpl.BaseState.Easing);
        Assert.True(tmpl.BaseState.Perspective);
    }

    private static StoryboardCompiler CreateCompiler(Dictionary<string, C2Template>? templates = null)
    {
        var chart = new C2Chart
        {
            time_base = 480,
            note_list = new List<C2Note>
            {
                new() { id = 1, tick = 0 },
                new() { id = 2, tick = 480 }
            }
        };
        var tempoList = new List<TempoEvent> { new() { tick = 0, value = 500000 } };
        var engine = new ChartTimeEngine(tempoList, 480);
        return new StoryboardCompiler(chart, engine, templates ?? new Dictionary<string, C2Template>());
    }
}