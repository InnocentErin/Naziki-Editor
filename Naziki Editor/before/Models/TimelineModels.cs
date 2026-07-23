using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

namespace Naziki_Editor.Models
{
    // ==========================================
    // 🌟 方案三核心：时间轴图层轨道模型 (Layer Track)
    // ==========================================
    public class TimelineTrackModel : INotifyPropertyChanged
    {
        public int TrackIndex { get; set; }
        public string TrackName { get; set; }

        // ✨ 新增口袋：这条轨道上躺着的所有方块/关键帧节点
        public ObservableCollection<TimelineClipModel> Clips { get; set; } = new ObservableCollection<TimelineClipModel>();

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


    // ==========================================
    // 🌟 方案三核心：时间轴轨道组模型 (Track Group) - 宏观/微观的万能容器
    // ==========================================
    public class TimelineTrackGroupModel : INotifyPropertyChanged
    {
        public string GroupName { get; set; } // 组名，例如 "Layer 0", "控制板幽灵", "X 轴属性"
        public int GroupIndex { get; set; }   // 🌟 辐射排序核心：用来决定上下顺序（越大越靠上或靠下）
        
        public bool SortTracksAscending { get; set; } = false; // ✨ 轨道正序开关（如果是 True，则数字越大的轨道越靠下！）

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        // 肚子里装着该组下的所有轨道
        public ObservableCollection<TimelineTrackModel> Tracks { get; set; } = new ObservableCollection<TimelineTrackModel>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}