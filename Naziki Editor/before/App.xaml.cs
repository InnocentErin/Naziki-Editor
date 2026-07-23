using System;
using System.IO;
using System.Windows;
using Newtonsoft.Json;
using Naziki_Editor.Models;
using Naziki_Editor.Views;
using Naziki_Editor.ProjectManagement;

namespace Naziki_Editor
{
    public partial class App : Application
    {
        // =========================================
        // 🛡️ 监控哨兵机制：双进程守护，崩溃即报
        // =========================================
        protected override void OnStartup(StartupEventArgs e)
        {
            // 👇 🟢【源头级增量注入】：在所有业务、哨兵启动前，率先全量张开时空安全网！
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;



            // 🛑 【雷达修正核心】安全网升级：只要是带参数的哨兵（无论叫啥），通通拦截，绝对不许往下执行！
            if (e.Args.Length > 0 && (e.Args[0] == "--watch" || e.Args[0] == "--crash-watcher"))
            {
                if (e.Args.Length >= 2)
                {
                    int targetPid = int.Parse(e.Args[1]);
                    try
                    {
                        var p = System.Diagnostics.Process.GetProcessById(targetPid);
                        p.WaitForExit(); // 哨兵进程在这里死等主程序
                        if (p.ExitCode != 0) // 如果是非正常退出
                        {
                            System.Windows.MessageBox.Show(
                                "🚨 监测到 Naziki Editor 发生致命崩溃！\n" +
                                "这是由 StackOverflow 或非法内存访问引发的。\n" +
                                "请检查最近添加的关键帧或属性设置。",
                                "Naziki 独立雷达哨兵", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch { }
                }
                Environment.Exit(0); // 💥 致命修复：哨兵任务结束直接自毁，切断套娃！
                return;
            }

            // 🛡️ 主程序启动哨兵（全局只留这一个召唤法阵就足够啦！）
            try
            {
                var current = System.Diagnostics.Process.GetCurrentProcess();
                System.Diagnostics.Process.Start(current.MainModule.FileName, $"--watch {current.Id}");
            }
            catch { }

            base.OnStartup(e); // 往下会触发 Application_Startup
        }

        // ==========================================
        // 📡 全局唯一大总闸：双控流时空拦截网
        // ==========================================
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 🗑️ 【已经把这里导致套娃的 --crash-watcher 召唤法阵删掉了！】

            // 🌟 核心驱动 1：自适应动态换肤
            try
            {
                bool isDarkMode = true;
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key?.GetValue("AppsUseLightTheme") != null)
                    {
                        isDarkMode = (int)key.GetValue("AppsUseLightTheme") == 0;
                    }
                }

                string themePath = isDarkMode ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";
                var themeDict = new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) };
                Application.Current.Resources.MergedDictionaries[0] = themeDict;
            }
            catch { }

            // 🚀 核心驱动 2：【.nep 项目双击直通车机制】
            if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            {
                string nepPath = e.Args[0];
                if (Path.GetExtension(nepPath).ToLower() == ".nep")
                {
                    try
                    {
                        string jsonText = File.ReadAllText(nepPath);
                        NazikiProjectModel project = JsonConvert.DeserializeObject<NazikiProjectModel>(jsonText);

                        if (project != null)
                        {
                            MainWindow mainWindow = new MainWindow();
                            mainWindow.LoadProject(nepPath, project);
                            mainWindow.Show();
                            return;
                        }
                    }
                    catch { }
                }
            }

            // 🕒 核心驱动 3：【普通启动流程】（设计师你之前删掉的窗口，小艾帮你加回来啦！）
            ProjectHubWindow hubWindow = new ProjectHubWindow();
            hubWindow.Show();
        }

        // =========================================================================
        // 🛡️ 核心雷达总线：全量缉拿未捕获异常，拒绝沉默，现场抓捕！
        // =========================================================================

        /// <summary>
        /// 💥 1. UI主干交互线程致命异常雷达
        /// </summary>
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // 阻断异常继续向上抛引发程序直接黑屏闪退，为主程序留下调试自救的喘息时间！
            e.Handled = true;
            ShowDetailedErrorWindow("🔮 [UI 物理交互线程发生穿模]", e.Exception);
        }

        /// <summary>
        /// 💥 2. 后台多线程/文件流等非UI异次元时空崩溃雷达
        /// </summary>
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                ShowDetailedErrorWindow("📡 [后台或独立多线程发生引擎核爆]", ex);
            }
        }

        /// <summary>
        /// 💥 3. Async/Await 异步流未观察任务死锁/异常雷达
        /// </summary>
        private void TaskScheduler_UnobservedTaskException(object sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            // 标志异常已被观察，防止垃圾回收 (GC) 时引发次生连带闪退
            e.SetObserved();
            ShowDetailedErrorWindow("⏳ [Async 异步时空遭遇因果悖论]", e.Exception);
        }

        /// <summary>
        /// 📺 终极可视化弹窗：精准剥离真实病灶并定格现场
        /// </summary>
        private void ShowDetailedErrorWindow(string errorSource, Exception ex)
        {
            // 顺着藤蔓一路往下摸，揪出导致整体翻车的那个最底层、最真实的元凶异常
            Exception realException = ex;
            while (realException.InnerException != null)
            {
                realException = realException.InnerException;
            }

            // 格式化案发现场报告
            string errorMsg = $"{errorSource}\n\n" +
                              $"⚠️ 异常真名: {realException.GetType().FullName}\n" +
                              $"💬 报错原因: {realException.Message}\n\n" +
                              $"📍 崩溃精准定位 (StackTrace):\n{realException.StackTrace}";

            // 祭出最高优先级的报错警告框
            MessageBox.Show(errorMsg, "Naziki 核心物理引擎安检警报", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}