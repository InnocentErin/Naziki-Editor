using Microsoft.Extensions.DependencyInjection;
using Naziki_Editor;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Project;
using Naziki_Editor.Tests.Mocks;
using System.IO;
using Xunit;

namespace Naziki_Editor.Tests.Services
{
    public class ProjectServiceTests
    {
        private readonly ServiceProvider _serviceProvider;

        public ProjectServiceTests()
        {
            // 1. 搭建纯净的测试 DI 容器（不加载 UI 层依赖）
            var services = new ServiceCollection();

            // 注册真正要测试的核心服务
            services.AddSingleton<IProjectService, ProjectService>();
            services.AddSingleton<IMessageBroker, Naziki_Editor.Core.Messaging.MessageBroker>();
            // 关键：用 MockDialogService 代替真正的 WPF 弹窗服务
            services.AddSingleton<IDialogService, MockDialogService>();

            _serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void LoadProjectData_ShouldNotThrowException_WhenNepFileExists()
        {
            // Arrange: 准备一个测试用的 .nep 文件（实际测试时，您需要先手动生成一个基础 .nep 放在 /bin/Debug/TestData 下）
            string testNepPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData", "sample.nep");
            if (!File.Exists(testNepPath))
            {
                // 如果文件不存在，此测试会跳过（防止 CI/CD 管道报错）
                Assert.True(false, $"测试文件未找到: {testNepPath}，请先手动放置一个正常解析的 .nep 文件");
                return;
            }

            var projectService = _serviceProvider.GetRequiredService<IProjectService>();

            // Act
            var exception = Record.Exception(() =>
            {
                var context = projectService.LoadProjectData(testNepPath);
                Assert.NotNull(context);
                Assert.NotNull(context.ProjectData);
            });

            // Assert
            Assert.Null(exception); // 测试是否抛出异常，如果为 null 说明加载成功
        }

        [Fact]
        public void SilentImportChart_ShouldParseJson_WhenFilePathValid()
        {
            // Arrange
            string chartPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData", "sample_chart.json");
            var projectService = _serviceProvider.GetRequiredService<IProjectService>();

            // Act
            var chart = projectService.SilentImportChart(chartPath);

            // Assert
            Assert.NotNull(chart);
            Assert.True(chart.note_list.Count > 0, "谱面应该包含至少一个音符");
        }
    }
}