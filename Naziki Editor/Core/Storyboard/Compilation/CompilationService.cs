using System.Collections.Generic;
using System.Linq;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Compilation;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Storyboard.Compilation
{
    /// <summary>
    /// 故事板编译服务实现，包装 <see cref="StoryboardCompiler"/> 与 <see cref="ControllerOptimizer"/>。
    /// </summary>
    public class CompilationService : ICompilationService
    {
        private readonly ICompilationNotifier? _notifier;
        private readonly IStoryboardTemplatePropertyMapper _templatePropertyMapper;

        public CompilationService(
            ICompilationNotifier? notifier = null,
            IStoryboardTemplatePropertyMapper? templatePropertyMapper = null)
        {
            _notifier = notifier;
            _templatePropertyMapper =
                templatePropertyMapper ?? new StoryboardTemplatePropertyMapper();
        }

        /// <inheritdoc />
        public void CompileStoryboard(ProjectDataContext context)
        {
            if (context?.Storyboard == null) return;

            var shadowStoryboard = DeepCloneStoryboard(context.Storyboard);

            var compiler = new StoryboardCompiler(
                context.Chart,
                context.TimeEngine,
                shadowStoryboard.templates ?? new Dictionary<string, C2Template>(),
                _templatePropertyMapper);

            compiler.FlattenStoryboard(shadowStoryboard);

            if (compiler.CompileWarnings.Count > 0)
            {
                string warningMsg = "🌟 设计师！时空安检雷达在展平落盘时发现了一些瑕疵，不过呆胶布（没关系），文件已安全生成：\n\n" +
                                    string.Join("\n", compiler.CompileWarnings.Take(5));
                if (compiler.CompileWarnings.Count > 5)
                    warningMsg += $"\n... 以及其他 {compiler.CompileWarnings.Count - 5} 条时空安检警报。";

                _notifier?.NotifyWarning("小艾的时空安检报告", warningMsg);
            }

            // 将展平结果写回上下文（原逻辑：覆盖原故事板）
            context.Storyboard = shadowStoryboard;
        }

        /// <inheritdoc />
        public void OptimizeScatteredControllers(ProjectDataContext context, OptimizeTarget target)
        {
            if (context == null) return;

            ControllerOptimizer.OptimizeControllers(
                context,
                target,
                emptyShellCount => true);
        }

        /// <inheritdoc />
        public StoryboardRoot CompileForExport(ProjectDataContext context)
        {
            if (context?.Storyboard == null)
                throw new InvalidOperationException("上下文或故事板为空，无法编译导出");

            var shadowStoryboard = DeepCloneStoryboard(context.Storyboard);

            var compiler = new StoryboardCompiler(
                context.Chart,
                context.TimeEngine,
                shadowStoryboard.templates ?? new Dictionary<string, C2Template>(),
                _templatePropertyMapper);

            compiler.FlattenStoryboard(shadowStoryboard);

            if (compiler.CompileWarnings.Count > 0)
            {
                string warningMsg = "🌟 设计师！时空安检雷达在展平落盘时发现了一些瑕疵，不过呆胶布（没关系），文件已安全生成：\n\n" +
                                    string.Join("\n", compiler.CompileWarnings.Take(5));
                if (compiler.CompileWarnings.Count > 5)
                    warningMsg += $"\n... 以及其他 {compiler.CompileWarnings.Count - 5} 条时空安检警报。";

                _notifier?.NotifyWarning("小艾的时空安检报告", warningMsg);
            }

            return shadowStoryboard;
        }

        /// <inheritdoc />
        public void SyncTemplateMetadata(ProjectDataContext context)
        {
            if (context?.Storyboard == null) return;

            if (context.StoryboardMeta == null)
                context.StoryboardMeta = new StoryboardMeta();

            if (context.StoryboardMeta.TemplateMetas == null)
                context.StoryboardMeta.TemplateMetas = new Dictionary<string, EditorTemplateMeta>();

            if (context.Storyboard.templates != null)
            {
                foreach (var kvp in context.Storyboard.templates)
                {
                    if (kvp.Value?.BaseState == null) continue;

                    var deducedType = TemplateClassifier.AnalyzeTemplate(kvp.Value.BaseState);
                    if (!context.StoryboardMeta.TemplateMetas.ContainsKey(kvp.Key))
                        context.StoryboardMeta.TemplateMetas[kvp.Key] = new EditorTemplateMeta();

                    context.StoryboardMeta.TemplateMetas[kvp.Key].Type = deducedType;
                }

                var keysToRemove = context.StoryboardMeta.TemplateMetas.Keys
                    .Where(k => !context.Storyboard.templates.ContainsKey(k))
                    .ToList();

                foreach (var k in keysToRemove)
                    context.StoryboardMeta.TemplateMetas.Remove(k);
            }
        }

        private static StoryboardRoot DeepCloneStoryboard(StoryboardRoot source)
        {
            var snapshots = AppServices.GetService<IEditorSnapshotSerializer>();
            return snapshots.Deserialize<StoryboardRoot>(snapshots.Serialize(source)) ?? new StoryboardRoot();
        }
    }
}
