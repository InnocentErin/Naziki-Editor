using Naziki_Editor.Core.Serialization.Converters;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Xunit;

namespace NazikiEditor.Tests;

/// <summary>
/// UnitFloatConverter 坐标转换器的序列化与反序列化测试
/// </summary>
public class UnitFloatConverterTests
{
    private readonly JsonSerializerSettings _settings;

    public UnitFloatConverterTests()
    {
        _settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            Converters = new List<JsonConverter> { new UnitFloatConverter() }
        };
    }

    [Theory]
    [InlineData("noteX:0.5", 0.5f, ReferenceUnit.NoteX)]
    [InlineData("noteX:0", 0f, ReferenceUnit.NoteX)]
    [InlineData("noteX:1.0", 1.0f, ReferenceUnit.NoteX)]
    [InlineData("noteY:0.25", 0.25f, ReferenceUnit.NoteY)]
    [InlineData("stageX:400", 400f, ReferenceUnit.StageX)]
    [InlineData("stageY:300", 300f, ReferenceUnit.StageY)]
    public void Deserialize_OfficialFormat_ShouldParseCorrectly(string jsonValue, float expectedValue, ReferenceUnit expectedUnit)
    {
        var json = $"{{\"x\":\"{jsonValue}\"}}";
        var result = JsonConvert.DeserializeObject<TestPos>(json, _settings);
        Assert.NotNull(result);
        Assert.NotNull(result!.X);
        Assert.Equal(expectedValue, result.X.Value);
        Assert.Equal(expectedUnit, result.X.Unit);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0f)]
    [InlineData(400f)]
    [InlineData(-100f)]
    public void Deserialize_PureNumber_ShouldBeWorldUnit(float input)
    {
        var json = $"{{\"x\":{input}}}";
        var result = JsonConvert.DeserializeObject<TestPos>(json, _settings);
        Assert.NotNull(result);
        Assert.NotNull(result!.X);
        Assert.Equal(input, result.X.Value);
        Assert.Equal(ReferenceUnit.World, result.X.Unit);
    }

    [Fact]
    public void Deserialize_NullValue_ShouldReturnNull()
    {
        var json = "{\"x\":null}";
        var result = JsonConvert.DeserializeObject<TestPos>(json, _settings);
        Assert.NotNull(result);
        Assert.Null(result!.X);
    }

    [Fact]
    public void Serialize_WorldUnit_ShouldOutputPureNumber()
    {
        var obj = new TestPos { X = new UnitFloat { Value = 400f, Unit = ReferenceUnit.World } };
        var json = JsonConvert.SerializeObject(obj, _settings);
        Assert.Contains("\"X\":400.0", json);
    }

    [Fact]
    public void Serialize_ZeroValue_WorldUnit_ShouldStillOutput()
    {
        var obj = new TestPos { X = new UnitFloat { Value = 0f, Unit = ReferenceUnit.World } };
        var json = JsonConvert.SerializeObject(obj, _settings);
        Assert.Contains("\"X\":0.0", json);
    }

    [Theory]
    [InlineData(0.5f, ReferenceUnit.NoteX)]
    [InlineData(0.25f, ReferenceUnit.NoteY)]
    [InlineData(400f, ReferenceUnit.StageX)]
    [InlineData(300f, ReferenceUnit.StageY)]
    public void Roundtrip_SerializeThenDeserialize_ShouldPreserveValue(float value, ReferenceUnit unit)
    {
        var original = new TestPos { X = new UnitFloat { Value = value, Unit = unit } };
        var json = JsonConvert.SerializeObject(original, _settings);
        var result = JsonConvert.DeserializeObject<TestPos>(json, _settings);
        Assert.NotNull(result);
        Assert.NotNull(result!.X);
        Assert.Equal(value, result.X.Value);
        Assert.Equal(unit, result.X.Unit);
    }

    [Fact]
    public void Deserialize_EmptyString_ShouldReturnWorldUnitZero()
    {
        var json = "{\"x\":\"\"}";
        var result = JsonConvert.DeserializeObject<TestPos>(json, _settings);
        Assert.NotNull(result);
        Assert.NotNull(result!.X);
        Assert.Equal(ReferenceUnit.World, result.X.Unit);
    }

    [Fact]
    public void Deserialize_VeryLargeValue_ShouldNotOverflow()
    {
        var json = "{\"x\":999999.999}";
        var result = JsonConvert.DeserializeObject<TestPos>(json, _settings);
        Assert.NotNull(result);
        Assert.NotNull(result!.X);
        Assert.Equal(999999.999f, result.X.Value);
    }

    [Fact]
    public void Serialize_AllUnits_ShouldProduceDistinctOutputs()
    {
        var units = new[] { ReferenceUnit.NoteX, ReferenceUnit.NoteY, ReferenceUnit.StageX, ReferenceUnit.StageY, ReferenceUnit.CameraX, ReferenceUnit.CameraY };
        var outputs = new HashSet<string>();
        foreach (var unit in units)
        {
            var obj = new TestPos { X = new UnitFloat { Value = 1.0f, Unit = unit } };
            var json = JsonConvert.SerializeObject(obj, _settings);
            outputs.Add(json);
        }
        Assert.Equal(6, outputs.Count);
    }

    private class TestPos
    {
        [JsonConverter(typeof(UnitFloatConverter))]
        public UnitFloat? X { get; set; }
    }
}