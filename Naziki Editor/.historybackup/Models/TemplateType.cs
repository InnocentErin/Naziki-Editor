namespace Naziki_Editor.Models
{
    // ==========================================\
    // 🏷️ 故事板模板的 8 大门派分类
    // ==========================================\
    public enum TemplateType
    {
        Generic,         // 通用基础 (无法推断时的默认值)
        StageObject,     // 场景对象 (通用的 X/Y/Opacity 等)
        Text,            // 文本专用
        Sprite,          // 精灵专用
        Video,           // 视频专用
        Line,            // 线条专用
        Controller,      // 场景控制器 (相机、UI、滤镜)
        NoteController   // 音符控制器
    }
}