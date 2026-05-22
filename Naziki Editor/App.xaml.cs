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
        // ==========================================
        // 📡 全局唯一大总闸：双控流时空拦截网
        // ==========================================
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // ==========================================
            // 🛡️ 核心驱动 0：【独立崩溃雷达监视器】(完全脱离主程序依赖)
            // ==========================================
            if (e.Args.Length >= 2 && e.Args[0] == "--crash-watcher")
            {
                int targetPid = int.Parse(e.Args[1]);
                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(targetPid);
                    process.WaitForExit(); // 挂起，静静注视着主程序...

                    // ExitCode != 0 意味着主程序遭遇了系统级强杀 (如 0xC00000FD 堆栈溢出)
                    if (process.ExitCode != 0)
                    {
                        MessageBox.Show(
                            $"🚨 警告！Naziki Editor 主程序遭遇了极其致命的崩溃！\n\n" +
                            $"【退出代码】 0x{process.ExitCode:X}\n" +
                            $"【诊断分析】 极有可能是发生了 StackOverflow (属性死循环) 或 OutOfMemory (内存溢出)！\n" +
                            $"主程序已阵亡，但独立雷达进程幸存并为您带回了此情报。",
                            "Naziki 独立防线", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch { }
                Environment.Exit(0); // 雷达使命完成，自毁
                return;
            }
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
