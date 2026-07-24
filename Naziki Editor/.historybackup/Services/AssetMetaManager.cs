using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace Naziki_Editor.Core
{
    // ==========================================
    // 🗂️ 素材映射大管家：专职负责记账和改名！
    // ==========================================
    public static class AssetMetaManager
    {
        // 获取当前工程沙盒下账本的物理路径
        private static string GetMetaFilePath(string projectDir, string materialFolderName)
        {
            string dir = Path.Combine(projectDir, materialFolderName);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "asset_meta.json");
        }

        // ==========================================
        // 📥 读账本：获取所有图片/视频的映射关系
        // ==========================================
        public static Dictionary<string, string> LoadMetaMap(string projectDir, string materialFolderName)
        {
            string path = GetMetaFilePath(projectDir, materialFolderName);
            if (!File.Exists(path)) return new Dictionary<string, string>();

            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        // ==========================================
        // ✍️ 写账本：当主人对图片/视频重命名时呼叫它！
        // ==========================================
        public static void SetExternalAssetDisplayName(string projectDir, string materialFolderName, string fileName, string newDisplayName)
        {
            var map = LoadMetaMap(projectDir, materialFolderName);
            map[fileName] = newDisplayName; // 添加或更新记账

            try
            {
                string path = GetMetaFilePath(projectDir, materialFolderName);
                File.WriteAllText(path, JsonConvert.SerializeObject(map, Formatting.Indented));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"更新素材账本失败 QAQ：{ex.Message}");
            }
        }

        // ==========================================
        // 💊 胶囊手术：直接修改 .nem 文件的名字并保存
        // ==========================================
        public static void RenameNemAsset(string nemFilePath, string newDisplayName)
        {
            if (!File.Exists(nemFilePath)) return;

            try
            {
                string json = File.ReadAllText(nemFilePath);
                var nemDoc = JsonConvert.DeserializeObject<Models.NemDocument>(json);
                if (nemDoc != null)
                {
                    nemDoc.MaterialName = newDisplayName; // 替换内置名字
                    File.WriteAllText(nemFilePath, JsonConvert.SerializeObject(nemDoc, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"修改 .nem 素材名称发生爆炸 QAQ：{ex.Message}");
            }
        }
    }
}