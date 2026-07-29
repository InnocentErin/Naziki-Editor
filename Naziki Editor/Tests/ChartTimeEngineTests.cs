using System.Collections.Generic;
using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class ChartTimeEngineTests
{
    [Fact]
    public void TickToSeconds_PreservesFractionalTicksAcrossTempoChanges()
    {
        var engine = new ChartTimeEngine(
            new List<TempoEvent>
            {
                new() { tick = 0, value = 500_000 },
                new() { tick = 480, value = 250_000 }
            },
            480);

        var seconds = engine.TickToSeconds(720.25);

        Assert.Equal(
            0.5 + 240.25 / 480d * 0.25,
            seconds,
            precision: 10);
    }
}
