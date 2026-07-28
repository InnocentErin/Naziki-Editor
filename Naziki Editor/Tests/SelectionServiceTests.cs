using Naziki_Editor.Shared.Input;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class SelectionServiceTests
{
    [Fact]
    public void SetToggleAndClearMaintainPrimarySelection()
    {
        var service = new SelectionService();
        var first = new object();
        var second = new object();

        service.Set(first, "test");
        service.Toggle(second, "test");

        Assert.Equal(second, service.Primary);
        Assert.Equal(2, service.Items.Count);

        service.Toggle(second, "test");
        Assert.Equal(first, service.Primary);

        service.Clear("test");
        Assert.Null(service.Primary);
        Assert.Empty(service.Items);
    }
}
