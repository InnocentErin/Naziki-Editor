using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Naziki_Editor.Core.Settings;

namespace Naziki_Editor.ViewModels.Settings
{
    /// <summary>
    /// 设置分类的 ViewModel，用于左侧导航栏的单项绑定。
    /// 包含分类信息及选中状态。
    /// </summary>
    public class SettingsCategoryViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        /// <summary>分类唯一键名</summary>
        public string Key { get; }

        /// <summary>显示名称（含图标）</summary>
        public string DisplayName { get; }

        /// <summary>图标文本</summary>
        public string Icon { get; }

        /// <summary>分类描述</summary>
        public string Description { get; }

        /// <summary>排序序号</summary>
        public int Order { get; }

        /// <summary>该分类下的设置项数量</summary>
        public int ItemCount { get; }

        /// <summary>是否被选中</summary>
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

        public SettingsCategoryViewModel(SettingsCategory category)
        {
            Key = category.Key;
            DisplayName = $"{category.Icon}  {category.DisplayName}";
            Icon = category.Icon;
            Description = category.Description;
            Order = category.Order;
            ItemCount = category.Items.Count;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}