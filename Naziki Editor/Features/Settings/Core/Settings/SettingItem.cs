using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Naziki_Editor.Core.Settings
{
    /// <summary>
    /// 单个设置项的数据模型，包含键、显示名称、描述、值类型、默认值等信息。
    /// 实现 INotifyPropertyChanged 以支持 MVVM 双向绑定。
    /// </summary>
    public class SettingItem : INotifyPropertyChanged
    {
        private object? _currentValue;
        private bool _isEnabled = true;
        private bool _isVisible = true;

        /// <summary>设置项唯一键名（建议使用 "Category.SettingName" 格式）</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>显示名称（UI 标签）</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>描述文本（Tooltip 或副标题）</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>值类型</summary>
        public SettingValueType ValueType { get; set; } = SettingValueType.String;

        /// <summary>默认值</summary>
        public object? DefaultValue { get; set; }

        /// <summary>
        /// 当前值。修改时触发 PropertyChanged 和内部值变更回调。
        /// </summary>
        public object? CurrentValue
        {
            get => _currentValue;
            set
            {
                if (!Equals(_currentValue, value))
                {
                    _currentValue = value;
                    OnPropertyChanged();
                    OnValueChanged?.Invoke(this);
                }
            }
        }

        /// <summary>是否启用（false 时 UI 灰显不可编辑）</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChanged(); } }
        }

        /// <summary>是否可见（false 时 UI 隐藏）</summary>
        public bool IsVisible
        {
            get => _isVisible;
            set { if (_isVisible != value) { _isVisible = value; OnPropertyChanged(); } }
        }

        /// <summary>下拉选项列表（仅当 ValueType == Combo 时有效）</summary>
        public List<string> ComboOptions { get; set; } = new();

        /// <summary>最小值（仅当 ValueType == Integer 或 Float 时有效，可为 null）</summary>
        public double? MinValue { get; set; }

        /// <summary>最大值（仅当 ValueType == Integer 或 Float 时有效，可为 null）</summary>
        public double? MaxValue { get; set; }

        /// <summary>所属分类键名</summary>
        public string CategoryKey { get; set; } = string.Empty;

        /// <summary>排序序号（数值越小越靠前）</summary>
        public int Order { get; set; }

        // ==========================================
        // ⌨️ 快捷键绑定专用属性
        // ==========================================

        /// <summary>默认快捷键手势文本（仅当 ValueType == KeyBinding 时有效）</summary>
        public string DefaultKeyGesture { get; set; } = string.Empty;

        private bool _isRecording;
        /// <summary>是否正在录制快捷键（仅当 ValueType == KeyBinding 时有效）</summary>
        public bool IsRecording
        {
            get => _isRecording;
            set { if (_isRecording != value) { _isRecording = value; OnPropertyChanged(); } }
        }

        /// <summary>快捷键冲突检测结果列表（仅当 ValueType == KeyBinding 时有效）</summary>
        public List<string> ConflictBindings { get; set; } = new();

        /// <summary>值变更时的内部回调</summary>
        internal event Action<SettingItem>? OnValueChanged;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}