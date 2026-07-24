using System;

namespace Naziki_Editor.Models
{
    // ==========================================
    // 🌟 核心工程模型：将被序列化为 .nep 文件
    // ==========================================
    public class NazikiProjectModel
    {
        // 1. 创世元数据
        public string ProjectName { get; set; } = "未命名项目";
        public string EditorVersion { get; set; } = "1.0.0";
        public DateTime CreationTime { get; set; } = DateTime.Now;
        public DateTime LastModifiedTime { get; set; } = DateTime.Now;

        // 2. 核心血脉的“引路石”（相对路径或绝对路径）
        public string StoryboardExportPath { get; set; } = null;
        public string ChartFilePath { get; set; } = null;
        public string AudioFilePath { get; set; } = null;
        public string BackgroundPath { get; set; } = null;

        // 3. 素材库缓存通道
        public string MaterialFolderPath { get; set; } = ".naziki_materials";

        // 4. 时空记忆
        public double LastTimelinePosition { get; set; } = 0;
        public double CanvasZoomLevel { get; set; } = 1.0;


        // 在 NazikiProjectModel 类的内部，追加这个属性：
        

        // 🌟 6. 控制板顺位身份证留痕账本 (Key: cb_类型_宿主ID_顺位索引 -> Value: 内存中追踪的唯一ID)
        public System.Collections.Generic.Dictionary<string, string> ControlBoardIdMaps { get; set; } = new System.Collections.Generic.Dictionary<string, string>();

    }
}