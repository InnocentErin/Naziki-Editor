using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Abstractions;

public interface IStoryboardDocumentReader
{
    StoryboardRoot Read(string json);
    IStoryboardEntity ReadEntity(string json, Type entityType);
}

public interface IStoryboardDocumentWriter
{
    string Write(StoryboardRoot document);
    string WriteNode(object node);
}

public interface IStoryboardDocumentValidator
{
    IReadOnlyList<StoryboardDiagnostic> Validate(StoryboardRoot document);
    IReadOnlyList<StoryboardDiagnostic> Validate(StoryboardRoot document, ProjectDataContext? context);
    IReadOnlyList<StoryboardDiagnostic> ValidateEntity(IStoryboardEntity entity, string path = "$");
}

public interface IEditorSnapshotSerializer
{
    string Serialize(object value);
    T? Deserialize<T>(string json) where T : class;
    object? Deserialize(string json, Type type);
}

public interface IStoryboardPropertyCatalog
{
    StoryboardPropertyCatalog Catalog { get; }
}
