using System;
using System.Collections.Generic;
using System.IO;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Storyboard.Canonical;
using Naziki_Editor.Features.Preview;
using Naziki_Editor.Features.Project.Resources;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class OfficialPreviewCompatibilityTests
{
    [Fact]
    public void UnityFork_UsesOfficialLevelChartStoryboardAndStateParserContracts()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            "Game/Chart/ChartModel.cs",
            "Level/LevelMeta.cs",
            "Storyboard/GenericStateParser.cs",
            "Storyboard/StoryboardModel.cs",
            "Storyboard/Controllers/ControllerStateParser.cs",
            "Storyboard/Lines/LineStateParser.cs",
            "Storyboard/Notes/NoteControllerStateParser.cs",
            "Storyboard/Sprites/SpriteStateParser.cs",
            "Storyboard/Texts/TextStateParser.cs",
            "Storyboard/Videos/VideoStateParser.cs"
        };

        foreach (var relative in files)
        {
            var official = Path.Combine(root, ".original_player", "engines", "unity", "Assets", "Scripts", relative);
            var preview = Path.Combine(root, "External", "original_player", "engines", "unity", "Assets", "Scripts", relative);
            Assert.True(File.Exists(official), $"Missing official contract: {relative}");
            Assert.True(File.Exists(preview), $"Missing Preview contract: {relative}");
            Assert.Equal(NormalizeSource(File.ReadAllText(official)), NormalizeSource(File.ReadAllText(preview)));
        }
    }

    [Fact]
    public void StoryboardWire_MapsLegacyDimensionsAndFiltersExtensionsWithoutMutatingSource()
    {
        var source = JObject.Parse("""
            {
              "sprites": [{"id":"s","w":10,"h":20,"width":30,"pivot_x":0.5,"dx":1,"dy":2}],
              "texts": [{"id":"t","size":2.5,"line_spacing":1.2,"font_style":"italic"}],
              "videos": [{"id":"v","w":40,"h":50,"color":"#80ffffff","preserve_aspect":true,"speed":2,"loop":true}]
            }
            """);
        var wire = (JObject)source.DeepClone();
        var issues = new List<StoryboardImportIssue>();

        CytoidStoryboardWireCompatibility.Normalize(wire, issues);

        Assert.Equal(10, source["sprites"]?[0]?["w"]?.Value<int>());
        Assert.Equal(30, wire["sprites"]?[0]?["width"]?.Value<int>());
        Assert.Null(wire["sprites"]?[0]?["w"]);
        Assert.Equal(20, wire["sprites"]?[0]?["height"]?.Value<int>());
        Assert.Equal(1, wire["sprites"]?[0]?["dx"]?.Value<int>());
        Assert.Equal(2, wire["sprites"]?[0]?["dy"]?.Value<int>());
        Assert.Null(wire["sprites"]?[0]?["pivot_x"]);
        Assert.Null(wire["texts"]?[0]?["line_spacing"]);
        Assert.Null(wire["texts"]?[0]?["font_style"]);
        Assert.Equal(2.5, wire["texts"]?[0]?["size"]?.Value<double>());
        Assert.Equal("#80ffffff", wire["videos"]?[0]?["color"]?.Value<string>());
        Assert.Null(wire["videos"]?[0]?["preserve_aspect"]);
        Assert.Null(wire["videos"]?[0]?["speed"]);
        Assert.Null(wire["videos"]?[0]?["loop"]);
        Assert.Contains(issues, issue => issue.Code == "CYTOID_LEGACY_DIMENSION_CONFLICT");
        Assert.Contains(issues, issue => issue.Code == "CYTOID_INTEGER_RUNTIME_CONVERSION");
    }

    [Theory]
    [InlineData(2.5, 2)]
    [InlineData(3.5, 4)]
    public void NewtonsoftIntegerConversion_UsesMidpointToEven(double value, int expected)
    {
        Assert.Equal(expected, new JValue(value).ToObject<int>());
    }

    [Theory]
    [InlineData("easy")]
    [InlineData("hard")]
    [InlineData("extreme")]
    public void LevelBinding_ResolvesEachOfficialDifficulty(string expectedDifficulty)
    {
        var root = Path.Combine(Path.GetTempPath(), "naziki-level-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "charts"));
        var chart = Path.Combine(root, "charts", expectedDifficulty + ".json");
        var level = Path.Combine(root, "level.json");
        File.WriteAllText(chart, "{}");
        File.WriteAllText(level, """
            {"charts":[
              {"type":"easy","path":"charts/easy.json"},
              {"type":"hard","path":"charts/hard.json"},
              {"type":"extreme","path":"charts/extreme.json"}
            ]}
            """);
        try
        {
            Assert.Equal(expectedDifficulty, CytoidLevelChartBinding.Resolve(level, chart));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LevelBinding_RejectsZeroAndMultipleMatches()
    {
        var root = Path.Combine(Path.GetTempPath(), "naziki-level-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "charts"));
        var selected = Path.Combine(root, "charts", "selected.json");
        var level = Path.Combine(root, "level.json");
        File.WriteAllText(selected, "{}");
        try
        {
            File.WriteAllText(level, """
                {"charts":[{"type":"hard","path":"charts/another.json"}]}
                """);
            Assert.Throws<InvalidDataException>(() => CytoidLevelChartBinding.Resolve(level, selected));

            File.WriteAllText(level, """
                {"charts":[
                  {"type":"easy","path":"charts/selected.json"},
                  {"type":"hard","path":"charts/selected.json"}
                ]}
                """);
            Assert.Throws<InvalidDataException>(() => CytoidLevelChartBinding.Resolve(level, selected));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LevelBinding_RejectsEscapingChartEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), "naziki-level-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var level = Path.Combine(root, "level.json");
        var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(outside, "{}");
        File.WriteAllText(level, """
            {"charts":[{"type":"hard","path":"../outside.json"}]}
            """);
        try
        {
            Assert.Throws<InvalidDataException>(() => CytoidLevelChartBinding.Resolve(level, outside));
        }
        finally
        {
            File.Delete(outside);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void V2Envelope_SerializesOnlyLowerCamelContractNames()
    {
        var message = new PreviewProtocolMessage("preview.open", "session", "request", 1, 0, 1, new JObject())
        {
            ConnectionId = "connection",
            Generation = 2
        };

        var json = JObject.FromObject(message);

        Assert.Equal("connection", json.Value<string>("connectionId"));
        Assert.Equal(2, json.Value<long>("generation"));
        Assert.Equal("preview.open", json.Value<string>("type"));
        Assert.Equal("session", json.Value<string>("sessionId"));
        Assert.Null(json["Type"]);
        Assert.Equal("naziki.editor-preview.v2", NamedPipeUnityPreviewTransport.ProtocolName);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".original_player")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Cannot locate the repository's .original_player reference.");
    }

    private static string NormalizeSource(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
