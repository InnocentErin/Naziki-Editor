using Naziki_Editor.Core.Serialization;
using Newtonsoft.Json;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class JsonTextDiffServiceTests
{
    private readonly JsonTextDiffService _service = new();

    [Fact]
    public void IgnoresFormattingAndObjectPropertyOrder()
    {
        const string before = """
                              {
                                "id": "sprite",
                                "value": 1
                              }
                              """;
        const string after = """{"value":1,"id":"sprite"}""";

        var result = _service.Analyze(before, after);

        Assert.Equal(0, result.ChangeCount);
        Assert.Empty(result.BeforeChangedLines);
        Assert.Empty(result.AfterChangedLines);
    }

    [Fact]
    public void LocatesChangedPropertyOnBothSides()
    {
        const string before = """
                              {
                                "id": "sprite",
                                "opacity": 0.2
                              }
                              """;
        const string after = """
                             {
                               "id": "sprite",
                               "opacity": 0.8
                             }
                             """;

        var result = _service.Analyze(before, after);

        Assert.Equal(1, result.ChangeCount);
        Assert.Contains(3, result.BeforeChangedLines);
        Assert.Contains(3, result.AfterChangedLines);
        Assert.Equal(3, result.FirstBeforeLine);
        Assert.Equal(3, result.FirstAfterLine);
    }

    [Fact]
    public void DeletingArrayItemDoesNotMarkTrailingItemsAsChanged()
    {
        const string before = """
                              [
                                { "id": "a" },
                                { "id": "b" },
                                { "id": "c" }
                              ]
                              """;
        const string after = """
                             [
                               { "id": "a" },
                               { "id": "c" }
                             ]
                             """;

        var result = _service.Analyze(before, after);

        Assert.Equal(1, result.ChangeCount);
        Assert.Contains(3, result.BeforeChangedLines);
        Assert.DoesNotContain(4, result.BeforeChangedLines);
        Assert.Empty(result.AfterChangedLines);
    }

    [Fact]
    public void RejectsDuplicatePropertiesInsteadOfProducingAmbiguousLocations()
    {
        Assert.Throws<JsonReaderException>(() =>
            _service.Analyze("""{"id":1}""", """{"id":1,"id":2}"""));
    }
}
