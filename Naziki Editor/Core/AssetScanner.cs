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
                        var doc = JsonConvert.DeserializeObject<NemDocument>(json);
                        if (doc != null)
                        {
                            var model = new AssetItemModel
                            {
                                FilePath = nemFile,
                                FileName = Path.GetFileName(nemFile),
                                AssetType = doc.MaterialType,
                                DisplayName = string.IsNullOrEmpty(doc.MaterialName) ? Path.GetFileName(nemFile) : doc.MaterialName,
                                Tag = doc // 把胶囊本身也带上，方便以后直接“召唤”到画布上
                            };

                            // 分发到对应的抽屉里
                            if (doc.MaterialType == "Text") bundle.TextAssets.Add(model);
                            else if (doc.MaterialType == "Line") bundle.LineAssets.Add(model);
                            else if (doc.MaterialType == "Template") bundle.TemplateAssets.Add(model);
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