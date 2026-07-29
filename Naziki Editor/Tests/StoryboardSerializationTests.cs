using System;
using System.IO;
using System.Linq;
using Naziki_Editor.Core.Serialization;
using Naziki_Editor.Models;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class StoryboardSerializationTests
{
    private readonly StoryboardPropertyCatalogService _catalog = new();

    [Fact]
    public void OfficialExampleRoundTripsCollectionsAndUnknownFields()
    {
        var reader = new StoryboardDocumentReader(_catalog);
        var writer = new StoryboardDocumentWriter();
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "storyboard_example.json"));

        var first = reader.Read(source);
        var output = writer.Write(first);
        var second = reader.Read(output);

        Assert.Equal(first.templates.Keys, second.templates.Keys);
        Assert.Equal(first.sprites.Count, second.sprites.Count);
        Assert.Equal(first.texts.Count, second.texts.Count);
        Assert.Equal(first.lines.Count, second.lines.Count);
        Assert.Equal(first.videos.Count, second.videos.Count);
        Assert.Equal(first.controllers.Count, second.controllers.Count);
        Assert.Equal(first.note_controllers.Count, second.note_controllers.Count);
        Assert.Equal(first.sprites.Select(item => item.Keyframes.Count),
            second.sprites.Select(item => item.Keyframes.Count));
        Assert.Contains(first.controllers.SelectMany(item => item.Keyframes),
            state => state.UnknownProperties.ContainsKey("comment"));
        Assert.Contains(second.controllers.SelectMany(item => item.Keyframes),
            state => state.UnknownProperties.ContainsKey("comment"));
    }

    [Fact]
    public void UnknownPropertiesProduceWarningsAndCanBeRemoved()
    {
        var reader = new StoryboardDocumentReader(_catalog);
        var validator = new StoryboardDocumentValidator();
        var root = reader.Read("""
            {"sprites":[{"id":"s","time":0,"future_property":{"x":1},
            "states":[{"relative_time":1,"future_state":true}]}]}
            """);

        var diagnostics = validator.Validate(root);
        Assert.Equal(2, diagnostics.Count(item => item.Code == "UNKNOWN_PROPERTY"));
        Assert.True(root.sprites[0].BaseState.UnknownProperties.Remove("future_property"));
        Assert.Single(validator.Validate(root), item => item.Code == "UNKNOWN_PROPERTY");
    }

    [Fact]
    public void EntityReadReplacesStatesInsteadOfAppending()
    {
        var reader = new StoryboardDocumentReader(_catalog);
        var entity = reader.ReadEntity("""
            {"id":"s","time":0,"states":[{"relative_time":1},{"add_time":2}]}
            """, typeof(C2Sprite));
        var replacement = reader.ReadEntity("""
            {"id":"s","time":0,"states":[{"relative_time":3}]}
            """, typeof(C2Sprite));

        Assert.Equal(2, entity.GetKeyframes().Count);
        Assert.Single(replacement.GetKeyframes());
    }

    [Fact]
    public void UnitValuesUseOfficialPrefixAndInvariantNumbers()
    {
        var reader = new StoryboardDocumentReader(_catalog);
        var writer = new StoryboardDocumentWriter();
        var root = reader.Read("""{"sprites":[{"id":"s","time":0,"x":"noteX:0.25","y":1.5}]}""");

        var json = JObject.Parse(writer.Write(root));
        Assert.Equal("noteX:0.25", (string?)json["sprites"]?[0]?["x"]);
        Assert.Equal(1.5, (double?)json["sprites"]?[0]?["y"]);
    }

    [Fact]
    public void UnitValuesAcceptObjectFormProducedByExternalEditors()
    {
        var reader = new StoryboardDocumentReader(_catalog);
        var writer = new StoryboardDocumentWriter();
        var root = reader.Read("""
        {
          "Sprites": [{
            "Id": "s",
            "Time": 0,
            "Width": {
              "Value": 1000,
              "Unit": 1,
              "ScaleToCanvas": true,
              "Span": true
            },
            "Height": { "Value": 600, "Unit": 2 },
            "X": { "Value": 0.25, "Unit": 0 }
          }]
        }
        """);

        var sprite = Assert.Single(root.sprites);
        Assert.Equal(1000, sprite.BaseState.Width!.Value);
        Assert.Equal(ReferenceUnit.StageX, sprite.BaseState.Width.Unit);
        Assert.Equal(ReferenceUnit.StageY, sprite.BaseState.Height!.Unit);
        Assert.Equal(ReferenceUnit.World, sprite.BaseState.X!.Unit);

        var json = JObject.Parse(writer.Write(root));
        Assert.Equal("stageX:1000",
            json["sprites"]![0]!["width"]!.Value<string>());
        Assert.Equal("stageY:600",
            json["sprites"]![0]!["height"]!.Value<string>());
        Assert.Equal("world:0.25",
            json["sprites"]![0]!["x"]!.Value<string>());
    }

    [Fact]
    public void SnapshotPreservesSyntheticEditorIdentity()
    {
        var serializer = new EditorSnapshotSerializer();
        var root = new StoryboardRoot
        {
            controllers =
            [
                new C2SceneController
                {
                    Id = "internal",
                    IsIdSynthetic = true,
                    BaseState = new ControllerState { Time = 0 },
                    Keyframes = [new ControllerState { RelativeTime = 1 }]
                }
            ]
        };

        var restored = serializer.Deserialize<StoryboardRoot>(serializer.Serialize(root));
        var entity = Assert.Single(restored!.controllers);
        Assert.Equal("internal", entity.Id);
        Assert.True(entity.IsIdSynthetic);
        Assert.Single(entity.Keyframes);
    }

    [Fact]
    public void PropertyCatalogCoversEveryDeclaredJsonProperty()
    {
        var covered = _catalog.Catalog.Properties.Select(item => item.JsonName)
            .Concat(_catalog.Catalog.KnownProperties)
            .Concat(_catalog.Catalog.RootCollections)
            .ToHashSet(StringComparer.Ordinal);
        var modelTypes = new[]
        {
            typeof(StoryboardRoot), typeof(ObjectState), typeof(StageObjectState),
            typeof(SpriteState), typeof(TextState), typeof(LineState), typeof(VideoState),
            typeof(ControllerState), typeof(NoteControllerState), typeof(TemplateState),
            typeof(LinePosition), typeof(NoteSelectorModel), typeof(C2Trigger)
        };
        var declared = modelTypes.SelectMany(type => type.GetProperties())
            .Select(property => property.GetCustomAttributes(typeof(JsonPropertyAttribute), true)
                .Cast<JsonPropertyAttribute>().FirstOrDefault()?.PropertyName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal);

        Assert.DoesNotContain(declared, name => !covered.Contains(name));
    }

    [Fact]
    public void PropertyCatalogCoversEveryUnityControllerParserField()
    {
        var relative = Path.Combine("External", "original_player", "engines",
            "unity", "Assets", "Scripts", "Storyboard", "Controllers",
            "ControllerStateParser.cs");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, relative)))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);

        var parser = File.ReadAllText(Path.Combine(directory!.FullName,
            relative));
        var parserFields = Regex.Matches(parser,
                """SelectToken\("([^"]+)"\)""")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var covered = _catalog.Catalog.Properties
            .Select(item => item.JsonName)
            .Concat(_catalog.Catalog.KnownProperties)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(parserFields,
            field => !covered.Contains(field));
    }
}
