using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Timeline.Shared
{
    /// <summary>
    /// 📦 解码数据时空快递盒：封装关键帧解码后的视觉时间、属性值及数组标记。
    /// </summary>
    public class DecodedKeyframeBox
    {
        /// <summary>关键帧状态对象引用</summary>
        public ObjectState State { get; set; }

        /// <summary>统一转换后相对于方块出生点（0.0s）的视觉相对秒数</summary>
        public double VisualRelTime { get; set; }

        /// <summary>属性的当前数值</summary>
        public object Value { get; set; }

        /// <summary>是否为 JArray 时间表达式裂变出的子元素</summary>
        public bool IsArrayElement { get; set; } = false;

        /// <summary>指向最初包含数组的共享状态，用于联动修改</summary>
        public object OriginalArrayStateRef { get; set; } = null;

        public bool IsTemplateExpanded { get; set; }
        public IReadOnlyList<string> TemplateSourcePath { get; set; } = Array.Empty<string>();
    }
}
