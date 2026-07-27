using Naziki_Editor.Core.Abstractions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace Naziki_Editor.Core.Services
{
    // ==========================================
    // 🗂️ 素材映射大管家：专职负责记账和改名！
    // ==========================================
    public class AssetMetaManager
    {
        private static IDialogService? _dialogService;

        public static void Initialize(IDialogService dialogService) { _dialogService = dialogService; }

        // 获取当前工程沙盒下账本的物理路径
        private string GetMetaFilePath(string projectDir, string materialFolderName)
        {
            string dir = Path.Combine(projectDir, materialFolderName);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "asset_meta.json");
        }

        // ==========================================
        // 📥 读账本：获取所有图片/视频的映射关系
        // ==========================================
        public Dictionary<string, string> LoadMetaMap(string projectDir, string materialFolderName)
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
        public void SetExternalAssetDisplayName(string projectDir, string materialFolderName, string fileName, string newDisplayName)
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
                _dialogService?.ShowMessage($"更新素材账本失败 QAQ：{ex.Message}", "错误", DialogMessageType.Error);
            }
        }

        // ==========================================
        // 💊 胶囊手术：直接修改 .nem 文件的名字并保存
        // ==========================================
        public void RenameNemAsset(string nemFilePath, string newDisplayName)
        {
            if (!File.Exists(nemFilePath)) return;

            try
            {
                string json = File.ReadAllText(nemFilePath);
                var nemDoc = Newtonsoft.Json.Linq.JObject.Parse(json);
                if (nemDoc.Value<int?>("format_version") == 2)
                {
                    nemDoc["material_name"] = newDisplayName;
                    File.WriteAllText(nemFilePath, nemDoc.ToString(Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"修改 .nem 素材名称发生爆炸 QAQ：{ex.Message}", "错误", DialogMessageType.Error);
            }
        }
    }
}
