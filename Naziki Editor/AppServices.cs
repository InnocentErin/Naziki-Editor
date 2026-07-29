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
using Naziki_Editor.Core.Timeline.Abstractions;
using Naziki_Editor.Core.Timeline.Services;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Core.Timeline.EventBlocks.Abstractions;
using Naziki_Editor.Core.Timeline.EventBlocks.Services;
using Naziki_Editor.Core.Timeline.Projection;
using Naziki_Editor.Core.Timeline.Settings;
using Naziki_Editor.Core.Timeline.Editing;
using Naziki_Editor.Core.Compilation;
using Naziki_Editor.Core.ErrorHandling;
using Naziki_Editor.Core.Notifications;
using Naziki_Editor.Core.Settings;
using Naziki_Editor.Core.Shortcuts;
using Naziki_Editor.Core.Theming;
using Naziki_Editor.Core.Serialization;
using Naziki_Editor.Core.Storyboard.Corrections;
using Naziki_Editor.Views.Services;
using Naziki_Editor.Views.PropertyEditor;
using Naziki_Editor.ViewModels.Settings;
using Naziki_Editor.Features.EditorShell;
using Naziki_Editor.Features.EditorShell.Workspace;
using Naziki_Editor.Features.Editing;
using Naziki_Editor.Features.Preview;
using Naziki_Editor.Features.Project.Resources;
using Naziki_Editor.Features.Project.Loading;
using Naziki_Editor.Features.Audio.Playback;
using Naziki_Editor.Shared.Input;
using Naziki_Editor.Views.Loading;
using Naziki_Editor.Core.Storyboard.Canonical;
using Naziki_Editor.Core.Charting;

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
            services.AddSingleton<IProjectResourceService, ProjectResourceService>();
            services.AddSingleton<IProjectReadinessService, ProjectReadinessService>();
            services.AddSingleton<IProjectOpenPreparationService, ProjectOpenPreparationService>();
            services.AddSingleton<IStoryboardRepository, StoryboardRepository>();
            services.AddSingleton<IWorkspaceService, WorkspaceService>();
            services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
            services.AddSingleton<IEditorCoordinator, EditorCoordinator>();
            services.AddSingleton<IMessageBroker>(MessageBroker.Default);
            services.AddSingleton<IInputSessionManager, InputSessionManager>();
            services.AddSingleton<ISelectionService, SelectionService>();
            services.AddSingleton<ICompilationService, CompilationService>();
            services.AddSingleton<ICompilationNotifier, DialogCompilationNotifier>();
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<ITemplateManager, TemplateManager>();
            services.AddSingleton<IStoryboardParser, StoryboardParser>();
            services.AddSingleton<IStoryboardPropertyCatalog, StoryboardPropertyCatalogService>();
            services.AddSingleton<IStoryboardJsonNormalizer, StoryboardJsonNormalizer>();
            services.AddSingleton<IStoryboardDocumentReader, StoryboardDocumentReader>();
            services.AddSingleton<IStoryboardDocumentWriter, StoryboardDocumentWriter>();
            services.AddSingleton<IEditorStoryboardSerializer, EditorStoryboardSerializer>();
            services.AddSingleton<IEditorStoryboardValidator, EditorStoryboardValidator>();
            services.AddSingleton<IStoryboardImportService, StoryboardImportService>();
            services.AddSingleton<IStoryboardTimePositionResolver, StoryboardTimePositionResolver>();
            services.AddSingleton<INoteQueryService, NoteQueryService>();
            services.AddSingleton<IStoryboardMaterializer, StoryboardMaterializer>();
            services.AddSingleton<IStoryboardRuntimeExporter, StoryboardRuntimeExporter>();
            services.AddSingleton<IStoryboardSourceStore, StoryboardSourceStore>();
            services.AddSingleton<IEditorStoryboardEditService, EditorStoryboardEditService>();
            services.AddSingleton<IStoryboardTemplateViewAdapter, StoryboardTemplateViewAdapter>();
            services.AddSingleton<IStoryboardCanonicalBridge, StoryboardCanonicalBridge>();
            services.AddSingleton<IStoryboardImportCoordinator, StoryboardImportCoordinator>();
            services.AddSingleton<IChartJsonCodec, ChartJsonCodec>();
            services.AddSingleton<IChartPreviewWireAdapter, ChartPreviewWireAdapter>();
            services.AddSingleton<StoryboardPreviewService>();
            services.AddSingleton<IStoryboardPreviewDataSource>(sp => sp.GetRequiredService<StoryboardPreviewService>());
            services.AddSingleton<IStoryboardChangeFeed>(sp => sp.GetRequiredService<StoryboardPreviewService>());
            services.AddSingleton<IStoryboardPreviewPublisher>(sp => sp.GetRequiredService<StoryboardPreviewService>());
            services.AddSingleton<IPreviewSettingsProvider, PreviewSettingsProvider>();
            services.AddSingleton<IUnityPreviewTransport, NamedPipeUnityPreviewTransport>();
            services.AddSingleton<IUnityPreviewProcessService, UnityPreviewProcessService>();
            services.AddSingleton<IPreviewVfsMaterializer, PreviewVfsMaterializer>();
            services.AddSingleton<IPreviewValidationService, PreviewValidationService>();
            services.AddSingleton<UnityStoryboardPreviewHost>();
            services.AddSingleton<IStoryboardPreviewHost>(sp => sp.GetRequiredService<UnityStoryboardPreviewHost>());
            services.AddSingleton<IPreviewPlaybackController>(sp => sp.GetRequiredService<UnityStoryboardPreviewHost>());
            services.AddSingleton<IPreviewClock>(sp => sp.GetRequiredService<UnityStoryboardPreviewHost>());
            services.AddSingleton<IPreviewDiagnosticsService>(sp => sp.GetRequiredService<UnityStoryboardPreviewHost>());
            services.AddSingleton<IPreviewReloadCoordinator>(sp => sp.GetRequiredService<UnityStoryboardPreviewHost>());
            services.AddSingleton<IUnityPreviewSessionService>(sp => sp.GetRequiredService<UnityStoryboardPreviewHost>());
            services.AddSingleton<IEditorMutationService, EditorMutationService>();
            services.AddSingleton<IStoryboardTemplatePropertyMapper, StoryboardTemplatePropertyMapper>();
            services.AddSingleton<IStoryboardTimeResolver, StoryboardTimeResolver>();
            services.AddSingleton<IStoryboardCorrectionAnalyzer, StoryboardCorrectionAnalyzer>();
            services.AddSingleton<IStoryboardCorrectionService, StoryboardCorrectionService>();
            services.AddSingleton<IStoryboardDocumentValidator, StoryboardDocumentValidator>();
            services.AddSingleton<IEditorSnapshotSerializer, EditorSnapshotSerializer>();
            services.AddSingleton<IJsonTextDiffService, JsonTextDiffService>();
            services.AddSingleton<ITrackBlueprintManager, TrackBlueprintManager>();
            services.AddSingleton<IAudioSyncEngine>(sp => new AudioSyncEngineAdapter(new AudioSyncEngine()));
            services.AddSingleton<IPlaybackCoordinator, PlaybackCoordinator>();
            
            // UI层服务 - 单例
            services.AddSingleton<IDialogService, WpfDialogService>();
            services.AddSingleton<ILoadingService, LoadingService>();
            services.AddSingleton<EditorResourceManager>();
            services.AddSingleton<UI.Rendering.NoteVisualEngine>();
            services.AddSingleton<UI.Rendering.GlobalRenderEngine>();
            services.AddSingleton<IPropertyEditorService, PropertyEditorService>();

            // 命令服务 - 单例
            services.AddSingleton<AppCommands>();

            // 快捷键系统 - 单例（依赖 ICommandDispatcher）
            services.AddSingleton<IShortcutManager, ShortcutManager>();

            // 时间轴模块 - 共享引擎
            services.AddTransient<TimelineCoordEngine>();
            services.AddTransient<IMainTimelineService, MainTimelineService>();
            services.AddTransient<IMacroTimelineService, MacroTimelineService>();
            services.AddTransient<IMicroTimelineService, MicroTimelineService>();
            services.AddTransient<IEventBlockService, EventBlockService>();
            services.AddSingleton<ITimelineProjectionService, TimelineProjectionService>();
            services.AddSingleton<IPropertyMetadataCatalog, PropertyMetadataCatalog>();
            services.AddSingleton<IMicroTimelineSessionFactory, MicroTimelineSessionFactory>();
            services.AddSingleton<ITimelineSettings, TimelineSettingsProvider>();
            services.AddSingleton<ITimelineEditService, TimelineEditService>();
            services.AddSingleton<ITemplateInstanceService, TemplateInstanceService>();

            // 设置系统 - 单例
            services.AddSingleton<ISettingsStore, SettingsStore>();
            services.AddSingleton<IRecentProjectService, RecentProjectService>();
            services.AddSingleton<IWorkspaceLayoutService, WorkspaceLayoutService>();
            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<IEditorShellCoordinator, EditorShellCoordinator>();
            services.AddTransient<SettingsWindowViewModel>();

            // 主题系统 - 单例
            services.AddSingleton<IThemeManager, ThemeManager>();

            // 窗口 - 瞬态
            services.AddTransient<Views.MainWindow>();
            services.AddTransient<ProjectManagement.ProjectHubWindow>();
            services.AddTransient<Views.Settings.SettingsWindow>();

            ServiceProvider = services.BuildServiceProvider();

            // 初始化静态服务定位器（AssetMetaManager 需要 IDialogService）
            var dialogService = ServiceProvider.GetRequiredService<IDialogService>();
            AssetMetaManager.Initialize(dialogService);

            // 初始化静态服务定位器（Core/View 层静态类需要 IDialogService）
            Core.Timeline.Shared.TimelineLayoutEngine.Initialize(dialogService);
            BoundedSliderControl.Initialize(dialogService);

            // 初始化设置系统：注册默认分类并加载已保存的设置
            var settingsStore = ServiceProvider.GetRequiredService<ISettingsStore>() as SettingsStore;
            settingsStore?.RegisterDefaultCategories();
            settingsStore?.Load();

            // 初始化主题系统（必须在设置加载之后）
            var themeManager = ServiceProvider.GetRequiredService<IThemeManager>();
            themeManager.Initialize();
        }

        public static T GetService<T>() where T : notnull
            => ServiceProvider.GetRequiredService<T>();
    }
}

