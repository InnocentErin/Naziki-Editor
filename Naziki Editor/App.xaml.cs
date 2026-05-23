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
            // 🛡️ 逻辑：如果是作为监控哨兵启动 (收到 --watch 参数)
            if (e.Args.Length >= 2 && e.Args[0] == "--watch")
            {
                int targetPid = int.Parse(e.Args[1]);
                try
                {
                    var p = System.Diagnostics.Process.GetProcessById(targetPid);
                    p.WaitForExit(); // 哨兵进程在这里死等
                    if (p.ExitCode != 0) // 如果是非正常退出 (如崩溃)
                    {
                        System.Windows.MessageBox.Show(
                            "🚨 监测到 Naziki Editor 发生致命崩溃！\n" +
                            "这是由 StackOverflow 或非法内存访问引发的。\n" +
                            "请检查最近添加的关键帧或属性设置。",
                            "Naziki 独立雷达哨兵", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch { } // 主程序已彻底消失，哨兵自毁
                Environment.Exit(0);
            }

            // 🛡️ 逻辑：作为主程序启动，顺手拉起一个哨兵
            try
            {
                var current = System.Diagnostics.Process.GetCurrentProcess();
                System.Diagnostics.Process.Start(current.MainModule.FileName, $"--watch {current.Id}");
            }
            catch { }

            base.OnStartup(e);
        }




        // ==========================================
        // 📡 全局唯一大总闸：双控流时空拦截网
        // ==========================================
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 唤醒雷达分身
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
                System.Diagnostics.Process.Start(exePath, $"--crash-watcher {currentPid}");
            }
            catch { }


            // 🌟 核心驱动 1：自适应动态换肤并修正 Themes/ 路径
            try
            {
                bool isDarkMode = true; // 默认暗黑
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key?.GetValue("AppsUseLightTheme") != null)
                    {
                        isDarkMode = (int)key.GetValue("AppsUseLightTheme") == 0;
                    }
                }

                string themePath = isDarkMode ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";
                var themeDict = new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) };

                // 强制掉包替换 App.xaml 里的默认皮肤字典
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
                            return; // 直接进城，结束流程！
                        }
                    }
                    catch { }
                }
            }

            // 🕒 核心驱动 3：【普通启动流程】
            ProjectHubWindow hubWindow = new ProjectHubWindow();
            hubWindow.Show();
        }

    }
}
