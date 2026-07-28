using System.Security.Cryptography;
using System.Text;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json;

namespace Naziki_Editor.Core.Storyboard.Canonical;

public sealed class StoryboardCanonicalBridge : IStoryboardCanonicalBridge
{
    private readonly IStoryboardImportService _importer;
    private readonly IStoryboardRuntimeExporter _exporter;
    private readonly IStoryboardDocumentReader _wireReader;
    private readonly IStoryboardDocumentWriter _wireWriter;

    public StoryboardCanonicalBridge(IStoryboardImportService importer,
        IStoryboardRuntimeExporter exporter,
        IStoryboardDocumentReader wireReader,
        IStoryboardDocumentWriter wireWriter)
    {
        _importer = importer;
        _exporter = exporter;
        _wireReader = wireReader;
        _wireWriter = wireWriter;
    }

    public EditorStoryboardDocument Synchronize(ProjectDataContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
#pragma warning disable CS0618
        var legacyHash = ComputeLegacyProjectionHash(context.Storyboard);
        var projectionChanged =
            string.IsNullOrWhiteSpace(context.LegacyStoryboardProjectionHash) ||
            !string.Equals(legacyHash, context.LegacyStoryboardProjectionHash,
                StringComparison.Ordinal);
        if (!context.EditorStoryboard.IsEmpty && !projectionChanged)
            return context.EditorStoryboard;

        var imported = _importer.Import(_wireWriter.Write(context.Storyboard),
            context.Chart, context.StoryboardMeta,
            context.ProjectData?.ControlBoardIdMaps);
#pragma warning restore CS0618
        if (!imported.CanReplace)
            throw new JsonSerializationException(
                "The legacy storyboard projection could not be normalized:" +
                Environment.NewLine +
                string.Join(Environment.NewLine,
                    imported.Issues.Where(issue =>
                            issue.Severity ==
                            StoryboardDiagnosticSeverity.Error)
                        .Take(20).Select(issue =>
                        $"{issue.Path}: {issue.Message}")));
        context.EditorStoryboard = imported.Document;
        context.LegacyStoryboardProjectionHash = legacyHash;
        return context.EditorStoryboard;
    }

    public StoryboardRuntimeExportResult Export(ProjectDataContext context)
    {
        var document = Synchronize(context);
        return _exporter.Export(document, context.Chart,
            context.TimeEngine);
    }

    public StoryboardRoot CreateLegacyProjection(ProjectDataContext context)
    {
        var result = _exporter.Export(context.EditorStoryboard, context.Chart,
            context.TimeEngine);
        if (!result.Success)
            throw new JsonSerializationException(string.Join(
                Environment.NewLine, result.Issues.Where(issue =>
                        issue.Severity == StoryboardDiagnosticSeverity.Error)
                    .Select(issue => $"{issue.Path}: {issue.Message}")));
        var projection = _wireReader.Read(result.Json.ToString(Formatting.None));
        context.LegacyStoryboardProjectionHash =
            ComputeLegacyProjectionHash(projection);
        return projection;
    }

    public string ComputeLegacyProjectionHash(StoryboardRoot storyboard) =>
        Hash(_wireWriter.Write(storyboard));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
