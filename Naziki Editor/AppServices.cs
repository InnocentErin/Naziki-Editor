using Microsoft.Extensions.DependencyInjection;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Common;
using Naziki_Editor.Core.Services;
using Naziki_Editor.Core.Commands;
using Naziki_Editor.Core.Editor;
using Naziki_Editor.Core.History;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Project;
using Naziki_Editor.Core.Storyboard;
using Naziki_Editor.Core.Storyboard.Compilation;
using Naziki_Editor.Core.Audio;
using Naziki_Editor.Core.Workspace;
using Naziki_Editor.Core.Timeline;
using Naziki_Editor.Core.Compilation;
using Naziki_Editor.Core.ErrorHandling;
using Naziki_Editor.Views.Services;
using Naziki_Editor.Views.PropertyEditor;

namespace Naziki_Editor
{
    public static class AppServices
    {
        public static ServiceProvider ServiceProvider { get; private set; }

        public static void ConfigureServices()
        {
            var services = new ServiceCollection();

            // Core层服务 - 单例
            services.AddSingleton<IErrorHandler, ErrorHandler>();
            services.AddSingleton<IHistoryService, HistoryService>();
            services.AddSingleton<IEntityFactory, EntityFactory>();
            services.AddSingleton<IProjectService, ProjectService>();
            services.AddSingleton<IStoryboardRepository, StoryboardRepository>();
            services.AddSingleton<IWorkspaceService, WorkspaceService>();
            services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
            services.AddSingleton<IEditorCoordinator, EditorCoordinator>();
            services.AddSingleton<IMessageBroker>(MessageBroker.Default);
            services.AddSingleton<ICompilationService, CompilationService>();
            services.AddSingleton<ICompilationNotifier, DialogCompilationNotifier>();
            services.AddSingleton<ITemplateManager, TemplateManager>();
            services.AddSingleton<IStoryboardParser, StoryboardParser>();
            services.AddSingleton<ITrackBlueprintManager, TrackBlueprintManager>();
            services.AddSingleton<IAudioSyncEngine>(sp => new AudioSyncEngineAdapter(new AudioSyncEngine()));
            
            // UI层服务 - 单例
            services.AddSingleton<IDialogService, WpfDialogService>();
            services.AddSingleton<EditorResourceManager>();
            services.AddSingleton<UI.Rendering.NoteVisualEngine>();
            services.AddSingleton<UI.Rendering.GlobalRenderEngine>();
            services.AddSingleton<IPropertyEditorService, PropertyEditorService>();

            // 命令服务 - 单例
            services.AddSingleton<AppCommands>();

            // 窗口 - 瞬态
            services.AddTransient<Views.MainWindow>();
            services.AddTransient<ProjectManagement.ProjectHubWindow>();

            ServiceProvider = services.BuildServiceProvider();

            // 初始化静态服务定位器（AssetMetaManager 需要 IDialogService）
            var dialogService = ServiceProvider.GetRequiredService<IDialogService>();
            AssetMetaManager.Initialize(dialogService);

            // 初始化静态服务定位器（Core/View 层静态类需要 IDialogService）
            Core.Timeline.TimelineLayoutEngine.Initialize(dialogService);
            Core.Compilation.StoryboardCompiler.Initialize(dialogService);
            BoundedSliderControl.Initialize(dialogService);
        }

        public static T GetService<T>() where T : notnull
            => ServiceProvider.GetRequiredService<T>();
    }
}