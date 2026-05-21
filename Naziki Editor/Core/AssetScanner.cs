using Naziki_Editor.Models;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Naziki_Editor.Core
{
    // ==========================================
    // 📦 扫描大礼包：分装好四大类素材，一次性丢给前台
    // ==========================================
    public class AssetBundle
    {
        public List<AssetItemModel> MediaAssets { get; set; } = new List<AssetItemModel>();
        public List<AssetItemModel> TextAssets { get; set; } = new List<AssetItemModel>();
        public List<AssetItemModel> LineAssets { get; set; } = new List<AssetItemModel>();
        public List<AssetItemModel> TemplateAssets { get; set; } = new List<AssetItemModel>();
    }

    // ==========================================
    // 🔍 核心雷达扫描仪：能读原文件，能查映射账本
    // ==========================================
    public static class AssetScanner
    {
        public static AssetBundle ScanProjectAssets(string projectDir, string materialFolderName)
        {
            var bundle = new AssetBundle();
            if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
                return bundle;

            // 1. 📖 向大管家借阅映射账本
            var metaMap = AssetMetaManager.LoadMetaMap(projectDir, materialFolderName);

            // 2. 🎬 扫描外部媒体素材 (图片、视频)
            var allowedMediaExts = new[] { ".png", ".jpg", ".jpeg", ".mp4", ".webm" };
            var files = Directory.GetFiles(projectDir);

            foreach (var file in files)
            {
                string ext = Path.GetExtension(file).ToLower();
                if (allowedMediaExts.Contains(ext))
                {
                    string fileName = Path.GetFileName(file);
                    string assetType = (ext == ".mp4" || ext == ".webm") ? "Video" : "Image";

                    // 🌟 核心映射：查账本！如果账本里写了别名，就用别名；没有就用原始文件名
                    string displayName = metaMap.ContainsKey(fileName) ? metaMap[fileName] : fileName;

                    bundle.MediaAssets.Add(new AssetItemModel
                    {
                        FilePath = file,
                        FileName = fileName,
                        AssetType = assetType,
                        DisplayName = displayName
                    });
                }
            }

            // 3. 💊 扫描 .naziki_materials 沙盒里的 .nem 胶囊
            string matDir = Path.Combine(projectDir, materialFolderName);
            if (Directory.Exists(matDir))
            {
                var nemFiles = Directory.GetFiles(matDir, "*.nem");
                foreach (var nemFile in nemFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(nemFile);

                        // ✨ 抛弃 NemDocument 外壳，直接按指挥官设定的“微型宇宙”进行解析！
                        var miniRoot = JsonConvert.DeserializeObject<StoryboardRoot>(json);

                        if (miniRoot != null)
                        {
                            // 🧠 智能推断：看看第一层节点里哪个数组有东西，它就是什么类型！
                            string assetType = "Template"; // 默认兜底
                            if (miniRoot.texts != null && miniRoot.texts.Count > 0) assetType = "Text";
                            else if (miniRoot.lines != null && miniRoot.lines.Count > 0) assetType = "Line";
                            else if (miniRoot.controllers != null && miniRoot.controllers.Count > 0) assetType = "Scene";
                            else if (miniRoot.note_controllers != null && miniRoot.note_controllers.Count > 0) assetType = "Scene";

                            string fileName = Path.GetFileName(nemFile);
                            var model = new AssetItemModel
                            {
                                FilePath = nemFile,
                                FileName = fileName,
                                AssetType = assetType,
                                DisplayName = metaMap.ContainsKey(fileName) ? metaMap[fileName] : fileName,
                                Tag = miniRoot // ✨ 提前把解析好的微型宇宙存起来，双击时不用再读一次硬盘啦！
                            };

                            // 分发到对应的抽屉里
                            if (assetType == "Text") bundle.TextAssets.Add(model);
                            else if (assetType == "Line") bundle.LineAssets.Add(model);
                            else bundle.TemplateAssets.Add(model);
                        }
                    }
                    catch
                    {
                        // 解析失败的残次品直接当没看见，防崩溃
                    }
                }
            }

            return bundle;
        }
    }
}