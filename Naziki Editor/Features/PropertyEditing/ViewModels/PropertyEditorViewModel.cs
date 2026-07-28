using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Naziki_Editor.UI.ViewModels
{
    public class PropertyEditorViewModel : INotifyPropertyChanged
    {
        private readonly IMessageBroker _messageBroker;
        private readonly IPropertyEditorService _propertyEditorService;
        private ProjectDataContext? _context;

        public object? CurrentState { get; private set; }
        public string? CurrentTitle { get; private set; }
        public object? RootState { get; private set; }
        public bool IsRoot { get; private set; }

        public ObservableCollection<PropertyRowViewModel> DynamicProperties { get; } = new();
        public Dictionary<string, C2Template>? GlobalTemplates { get; private set; }

        private IStoryboardEntity? _mainObject;
        public IStoryboardEntity? MainObject
        {
            get => _mainObject;
            set { _mainObject = value; OnPropertyChanged(); }
        }

        public IStoryboardEntity? CurrentActiveObject { get; set; }

        public PropertyEditorViewModel(IMessageBroker messageBroker, IPropertyEditorService propertyEditorService)
        {
            _messageBroker = messageBroker;
            _propertyEditorService = propertyEditorService;
        }

        public void LoadState(object stateReference, string frameTitle, object rootState, bool isRoot,
            ProjectDataContext context, IStoryboardEntity? mainObject = null)
        {
            _context = context;
            CurrentState = stateReference;
            CurrentTitle = frameTitle;
            RootState = rootState;
            IsRoot = isRoot;
            MainObject = mainObject;

            BuildDynamicProperties();
        }

        public void InitTemplates(Dictionary<string, C2Template> templates)
        {
            GlobalTemplates = templates;
        }

        public void MarkAsModified()
        {
            _context?.MarkAsModified();
        }

        private void BuildDynamicProperties()
        {
            DynamicProperties.Clear();
            if (CurrentState == null) return;

            bool isTemplateMode = CurrentState.GetType().Name == "TemplateState";
            bool isControlBoard = CurrentActiveObject != null
                && !string.IsNullOrEmpty(CurrentActiveObject.TargetId);

            var props = CurrentState.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (!_propertyEditorService.IsEditableProperty(prop) || IsExcludedProperty(prop.Name)) continue;
                var value = prop.GetValue(CurrentState);
                DynamicProperties.Add(new PropertyRowViewModel
                {
                    PropertyName = prop.Name,
                    PropertyInfo = prop,
                    Value = value,
                    Constraint = _propertyEditorService.GetConstraint(prop.Name),
                    IsTemplateMode = isTemplateMode,
                    IsControlBoard = isControlBoard,
                    IsRoot = IsRoot,
                    StateReference = CurrentState
                });
            }
        }

        private static bool IsExcludedProperty(string name)
        {
            return name is "Id" or "TargetId" or "ParentId" or "Template" or "NoteTarget" or "Time"
                or "Easing" or "Layer" or "Order" or "UnknownProperties" or "Diagnostics" or "IsIdSynthetic";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class PropertyRowViewModel
    {
        public string PropertyName { get; set; } = "";
        public PropertyInfo? PropertyInfo { get; set; }
        public object? Value { get; set; }
        public Core.PropertyConstraint Constraint { get; set; }
        public bool IsTemplateMode { get; set; }
        public bool IsControlBoard { get; set; }
        public bool IsRoot { get; set; }
        public object? StateReference { get; set; }
    }
}
