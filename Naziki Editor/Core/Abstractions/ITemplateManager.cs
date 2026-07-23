using Naziki_Editor.Models;
using System.Collections.Generic;

namespace Naziki_Editor.Core.Abstractions
{
    public interface ITemplateManager
    {
        bool CheckForCircularDependency(StoryboardRoot root, string templateName, string targetTemplateToInject);
        void RenameTemplateGlobally(StoryboardRoot root, string oldName, string newName);
        List<ObjectState> GetAllStatesInStoryboard(StoryboardRoot root);
        TemplateType InferTemplateType(C2Template template);
        HashSet<string> GetAllowedPropertiesForType(TemplateType type);
        bool IsPropertyAllowed(string propertyName, TemplateType type);
    }
}