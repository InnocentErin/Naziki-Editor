using Microsoft.Extensions.DependencyInjection;
using Naziki_Editor.Core.Abstractions;
using Xunit;

namespace Naziki_Editor.Tests
{
    public class AppServicesConfigTests
    {
        [Fact]
        public void ServiceProvider_ShouldResolveAllCoreServices_WithoutThrowing()
        {
            // Act & Assert
            var exception = Record.Exception(() =>
            {
                var services = new ServiceCollection();
                // 将 AppServices 中的配置核心逻辑提取到公共静态方法，或者在此处手动注册相关服务
                // 这里只需验证我们是否可以通过 DI 获取到 IProjectService
                var serviceProvider = AppServices.ServiceProvider;
                var projectService = serviceProvider.GetRequiredService<IProjectService>();
                var historyService = serviceProvider.GetRequiredService<IHistoryService>();
                var messageBroker = serviceProvider.GetRequiredService<IMessageBroker>();
            });

            Assert.Null(exception);
        }
    }
}