using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace Naziki_Editor.Core
{
    // ==========================================
    // 👑 统一资源大本营：接管所有皮肤、音效、UI的加载！
    // ==========================================
    public static class EditorResourceManager
    {
        // 🌟 全局皮肤设定 (未来可以写个 EditorConfig.json 保存在 AppData 里)
        public static string CurrentNoteSkin { get; set; } = "Cytus2_Default";
        public static string CurrentNoteSound { get; set; } = "Cytus2_Default";

        // 🧊 冰封缓存池：内存 OOM 终极防御盾
        private static Dictionary<string, BitmapImage> _noteIconCache = new Dictionary<string, BitmapImage>();

        /// <summary>
        /// 🧙‍♂️ 召唤音符图标：双层嗅探法术！
        /// </summary>
        public static BitmapImage GetNoteIcon(int noteType)
        {
            string fileName = "Click.png";

            // 1. 匹配大大精细划分的类型
            // 🎯 严格对照《Cytus2谱面格式详解》官方 0 基准类型进行精准分配！
            switch (noteType)
            {
                case 0: fileName = "Click.png"; break;         // 0 是 Click
                case 1: fileName = "Hold.png"; break;          // 1 是 Hold
                case 2: fileName = "LongHold.png"; break;      // 2 是 Long hold
                case 3: fileName = "Drag.png"; break;          // 3 是 Drag (头)
                case 4: fileName = "DragChild.png"; break;     // 4 是 Drag child (子)
                case 5: fileName = "Flick.png"; break;         // 5 是 Flick
                case 6: fileName = "CDrag.png"; break;         // 6 是 Click drag (点刷头)
                case 7: fileName = "CDragChild.png"; break;    // 7 是 Click drag child (点刷子)
                default: fileName = "Click.png"; break;
            }

            // 缓存键值绑定皮肤名，这样未来一键切皮肤时，图标能瞬间更新！
            string cacheKey = $"{CurrentNoteSkin}_{fileName}";

            if (_noteIconCache.ContainsKey(cacheKey))
                return _noteIconCache[cacheKey];

            BitmapImage bmp = null;

            try
            {
                // 🔍 第一层：外部实体雷达（去 .exe 运行目录下的自定义文件夹找）
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string externalPath = Path.Combine(appDir, "Resources", "NoteSkins", CurrentNoteSkin, "Notes", fileName);

                if (File.Exists(externalPath))
                {
                    bmp = new BitmapImage();
                    bmp.BeginInit();
                    // 🛡️ 核心防占用：强行全部读入内存，读完就释放文件锁，允许用户边开着软件边替换图片！
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(externalPath, UriKind.Absolute);
                    bmp.EndInit();
                }
                else
                {
                    // 🛡️ 第二层：内部绝对防御（如果外部被删了，或者用户没装皮肤，从 exe 肚子里抽保底！）
                    // 注意这里的路径：必须和您在 VS 项目里建的文件夹结构一模一样！
                    var packUri = new Uri($"pack://application:,,,/Resources/NoteSkins/Cytus2_Default/Notes/{fileName}");
                    bmp = new BitmapImage(packUri);
                }

                bmp.Freeze(); // ❄️ 必须冻结！否则 WPF 跨线程渲染直接崩溃！
                _noteIconCache[cacheKey] = bmp;
                return bmp;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[资源雷达] 读取 {fileName} 失败: {ex.Message}");
                _noteIconCache[cacheKey] = null; // 找不到就记空，防止下一次再重复抛出异常卡死
                return null;
            }
        }

        // 🧹 预留法术：未来在设置面板里一键切换皮肤时，调这个方法清空缓存！
        public static void ClearCache()
        {
            _noteIconCache.Clear();
        }
    }
}