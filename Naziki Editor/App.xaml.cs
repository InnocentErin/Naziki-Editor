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
    }
}