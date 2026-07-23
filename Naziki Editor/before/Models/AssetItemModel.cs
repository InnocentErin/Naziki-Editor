using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Naziki_Editor.Models
{
    // ==========================================
    // 🌟 素材通用包装盒：送进前台列表前，统统装进这里！
    // ==========================================
    public class AssetItemModel : INotifyPropertyChanged
    {
        // 1. 物理信息
        public string FilePath { get; set; } // 完整绝对路径
        public string FileName { get; set; } // 原始文件名 (例如: dafeiwu.png 或 Text_123.nem)
        public string AssetType { get; set; } // 分类标签 (Image, Video, Text, Line, Template)

        // 2. 🌟 映射显示名 (支持双向绑定的核心！)
        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(); // 大喊一声：我的名字变啦，前台快刷新！
                }
            }
        }

        // 3. 🌟 状态开关：当前是否正在被修改名字？
        private bool _isEditing = false;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged(); // 大喊一声：我要变成输入框啦！
                }
            }
        }

        // 4. 备用载荷：如果是 .nem，我们可以把反序列化后的原对象暂存在这
        public object Tag { get; set; }

        // ==========================================
        // 📡 WPF 数据双向绑定信使
        // ==========================================
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}