using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Naziki_Editor.Models
{
    // ==========================================
    // 🌟 方案三核心：时间轴图层轨道模型 (Layer Track)
    // ==========================================
    public class TimelineTrackModel : INotifyPropertyChanged
    {
        public int TrackIndex { get; set; } // Z-Index 层级，0 是最底层，数字越大越在上面
        public string TrackName { get; set; } // 比如 "Layer 1"

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ==========================================
    // 🌟 方案三核心：时间轴实体方块模型 (Clip)
    // 贯彻“一生一世一长条”理念
    // ==========================================
    public class TimelineClipModel : INotifyPropertyChanged
    {
        // 绑定的原始大本营对象 (TextObject, SpriteObject 等)
        public IStoryboardEntity AssociatedObject { get; set; }

        public string DisplayName { get; set; } // 方块上显示的文字，比如 "text_1"
        public int TrackIndex { get; set; }     // 当前躺在哪个图层轨道上？

        // ⏱️ 生命的起点与终点 (单位：秒)
        public double StartTime { get; set; }
        public double EndTime { get; set; }     // 如果没有 destroy 帧，就等于无穷大或歌曲结尾

        // ✨ 补充需求：静默选中状态
        private bool _isSelected = false;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));






        
    }
}