using Naziki_Editor.Features.Preview;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class PreviewDiagnosticBoundaryTests
{
    [Fact]
    public void StoryboardOnlyError_AllowsPreviewButDisablesStoryboard()
    {
        var result = new PreviewValidationResult(1,
        [
            new PreviewDiagnostic(
                "PREVIEW_STORYBOARD_TEST_FAILURE",
                "Storyboard fixture failed.",
                PreviewDiagnosticSeverity.Error,
                PreviewDiagnosticSource.Storyboard)
            {
                Impact = PreviewDiagnosticImpact.StoryboardOnly,
                Stage = "initialize"
            }
        ]);

        Assert.True(result.CanStartPreview);
        Assert.True(result.IsValid);
        Assert.False(result.CanLoadStoryboard);
    }

    [Fact]
    public void PreviewBlockingError_StopsPreview()
    {
        var result = new PreviewValidationResult(1,
        [
            new PreviewDiagnostic(
                "PREVIEW_CHART_TEST_FAILURE",
                "Chart fixture failed.",
                PreviewDiagnosticSeverity.Error,
                PreviewDiagnosticSource.Chart)
            {
                Impact = PreviewDiagnosticImpact.PreviewBlocking,
                Stage = "validate"
            }
        ]);

        Assert.False(result.CanStartPreview);
        Assert.False(result.IsValid);
        Assert.False(result.CanLoadStoryboard);
    }
}
