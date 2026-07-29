using System.Text;
using System.IO;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Storyboard.Canonical;

public sealed class StoryboardSourceStore : IStoryboardSourceStore
{
    private readonly IEditorStoryboardSerializer _serializer;
    private readonly IEditorStoryboardValidator _validator;

    public StoryboardSourceStore(IEditorStoryboardSerializer serializer) =>
        (_serializer, _validator) =
        (serializer, new EditorStoryboardValidator());

    public StoryboardSourceStore(IEditorStoryboardSerializer serializer,
        IEditorStoryboardValidator validator) =>
        (_serializer, _validator) = (serializer, validator);

    public string GetDefaultSourcePath(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            throw new ArgumentException("Project path is required.",
                nameof(projectFilePath));
        var projectDirectory = Path.GetDirectoryName(
                                   Path.GetFullPath(projectFilePath))
                               ?? throw new InvalidOperationException(
                                   "Cannot resolve project directory.");
        return Path.Combine(projectDirectory, ".naziki",
            "storyboard.editor.json");
    }

    public EditorStoryboardDocument Load(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(
                "Editor storyboard source does not exist.", sourcePath);
        var document = _serializer.Deserialize(File.ReadAllText(sourcePath));
        EnsureStructurallyValid(document);
        return document;
    }

    public void Save(string sourcePath, EditorStoryboardDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        // Serialize first so every write path passes through canonical unit
        // normalization (including the empty-unit compatibility migration)
        // before structural validation.
        var serialized = _serializer.Serialize(document);
        var normalized = _serializer.Deserialize(serialized);
        EnsureStructurallyValid(normalized);
        var fullPath = Path.GetFullPath(sourcePath);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new IOException(
                            "Cannot resolve editor storyboard directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, serialized,
                new UTF8Encoding(false));
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void EnsureStructurallyValid(EditorStoryboardDocument document)
    {
        var errors = _validator.Validate(document)
            .Where(issue => issue.BlocksSourceSave)
            .ToArray();
        if (errors.Length == 0) return;
        throw new InvalidDataException(
            "Editor storyboard source is structurally invalid:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(issue =>
                $"{issue.Path}: {issue.Message}")));
    }
}
