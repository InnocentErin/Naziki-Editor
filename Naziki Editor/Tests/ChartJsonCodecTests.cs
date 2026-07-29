using Naziki_Editor.Core.Charting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class ChartJsonCodecTests
{
    private readonly ChartJsonCodec _codec = new();

    [Fact]
    public void Decode_AllowsNegativeOverlappingPagesWhenEndsIncrease()
    {
        var result = _codec.Decode("""
        {
          "format_version": 1,
          "time_base": 480,
          "page_list": [
            {"start_tick":0,"end_tick":960,"scan_line_direction":1},
            {"start_tick":-960,"end_tick":1920,"scan_line_direction":-1}
          ],
          "tempo_list": [{"tick":0,"value":500000}],
          "event_order_list": [],
          "note_list": [{
            "id":10,"page_index":1,"type":0,"tick":1440,"x":0.5,
            "hold_tick":0,"next_id":-1,
            "approach_rate":0.75
          }]
        }
        """);

        Assert.True(result.Success);
        Assert.Contains(result.Diagnostics,
            item => item.Code == "CHART_PAGE_NEGATIVE_START" &&
                    item.Severity == ChartDiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics,
            item => item.Code == "CHART_PAGE_OVERLAP" &&
                    item.Severity == ChartDiagnosticSeverity.Warning);
        Assert.Equal(0.75,
            result.Document!.Projection.note_list[0].approach_rate);
    }

    [Fact]
    public void Decode_BlocksNonIncreasingPageEnds()
    {
        var result = _codec.Decode("""
        {
          "time_base":480,
          "page_list":[
            {"start_tick":0,"end_tick":960,"scan_line_direction":1},
            {"start_tick":0,"end_tick":900,"scan_line_direction":-1}
          ],
          "tempo_list":[{"tick":0,"value":500000}],
          "event_order_list":[],
          "note_list":[]
        }
        """);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics,
            item => item.Code == "CHART_PAGE_END_ORDER_INVALID");
    }

    [Fact]
    public void Decode_ValidatesNextIdAsListIndexNotNoteId()
    {
        var result = _codec.Decode("""
        {
          "time_base":480,
          "page_list":[
            {"start_tick":0,"end_tick":960,"scan_line_direction":1}
          ],
          "tempo_list":[{"tick":0,"value":500000}],
          "event_order_list":[],
          "note_list":[
            {"id":100,"page_index":0,"type":3,"tick":100,"x":0.4,
             "hold_tick":0,"next_id":1},
            {"id":200,"page_index":0,"type":4,"tick":200,"x":0.6,
             "hold_tick":0,"next_id":-1}
          ]
        }
        """);

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Diagnostics,
            item => item.Code == "CHART_NOTE_NEXT_INDEX_INVALID");
    }

    [Fact]
    public void SourceRoundTripPreservesUnknownAndFractionalValues()
    {
        var result = _codec.Decode("""
        {
          "time_base":480,
          "custom_root":{"enabled":true},
          "page_list":[{
            "start_tick":0.5,"end_tick":960.5,
            "scan_line_direction":1,
            "PositionFunction":{"Type":0,"Arguments":[0.5,1]},
            "custom_page":"kept"
          }],
          "tempo_list":[{"tick":0.25,"value":500000}],
          "event_order_list":[],
          "note_list":[{
            "id":0,"page_index":0,"type":0,"tick":240.5,"x":0.5,
            "hold_tick":0.25,"next_id":-1,
            "custom_note":"kept"
          }]
        }
        """);

        Assert.True(result.Success);
        var source = JObject.Parse(
            _codec.EncodeSource(result.Document!));
        Assert.True(source["custom_root"]!.Value<bool>("enabled"));
        Assert.Equal("kept",
            source["page_list"]![0]!.Value<string>("custom_page"));
        Assert.Equal("kept",
            source["note_list"]![0]!.Value<string>("custom_note"));
        Assert.Equal(240.5,
            source["note_list"]![0]!.Value<double>("tick"));
        Assert.NotNull(source["page_list"]![0]!["PositionFunction"]);
    }

    [Fact]
    public void BundledUnityWirePreservesOriginalCytus2Document()
    {
        var result = _codec.Decode("""
        {
          "time_base":480,
          "start_offset_time":0,
          "opacity":0.8,
          "page_list":[{
            "start_tick":0,"end_tick":960,"scan_line_direction":1,
            "PositionFunction":{"Type":0,"Arguments":[1,0]}
          }],
          "tempo_list":[{"tick":0,"value":500000}],
          "event_order_list":[],
          "note_list":[{
            "id":0,"page_index":0,"type":0,"tick":240,"x":0.5,
            "hold_tick":0,"next_id":-1,
            "approach_rate":1.5,"NoteDirection":1
          }]
        }
        """);

        var wire = JObject.Parse(_codec.EncodeWire(
            result.Document!, ChartRuntimeProfile.BundledUnity));

        Assert.Null(wire["music_offset"]);
        Assert.Equal(0, wire.Value<double>("start_offset_time"));
        Assert.Equal(0.8, wire.Value<double>("opacity"));
        Assert.NotNull(wire["page_list"]![0]!["PositionFunction"]);
        Assert.Equal(1.5,
            wire["note_list"]![0]!.Value<double>("approach_rate"));
        Assert.Equal(1,
            wire["note_list"]![0]!.Value<int>("NoteDirection"));
        Assert.Null(wire["note_list"]![0]!["is_forward"]);

        var diagnostics = _codec.Validate(
            result.Document!.Source,
            ChartRuntimeProfile.BundledUnity);
        Assert.Contains(diagnostics, item =>
            item.Code == "CHART_PROFILE_FIELD_IGNORED" &&
            item.Path == "$.start_offset_time");
        Assert.Contains(diagnostics, item =>
            item.Code == "CHART_PROFILE_FIELD_IGNORED" &&
            item.Path == "$.page_list[0].PositionFunction");
    }

    [Fact]
    public void BundledUnityValidation_BlocksEmptyNoteListBeforeRuntime()
    {
        var result = _codec.Decode("""
        {
          "time_base":480,
          "page_list":[
            {"start_tick":0,"end_tick":960,"scan_line_direction":1}
          ],
          "tempo_list":[{"tick":0,"value":500000}],
          "event_order_list":[],
          "note_list":[]
        }
        """, ChartRuntimeProfile.BundledUnity);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "CHART_NOTE_LIST_EMPTY" &&
            item.Path == "$.note_list");
    }
}
