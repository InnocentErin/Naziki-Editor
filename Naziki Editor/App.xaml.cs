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