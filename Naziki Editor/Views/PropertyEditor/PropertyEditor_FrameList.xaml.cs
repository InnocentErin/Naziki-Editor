using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Naziki_Editor.Models;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditor_FrameList : UserControl
    {
        private StoryboardObject _editingObject;
        private System.Collections.IList _statesList;
        private Type _stateType;

        // 专线大喇叭保持不变
        public event Action<object, string, object, bool> OnFrameSelected;

        public PropertyEditor_FrameList()
        {
            InitializeComponent();
        }

        public void LoadData(StoryboardObject editingObj, Naziki_Editor.State.ProjectDataContext context)
        {
            _editingObject = editingObj;
            //_context = context;
            RefreshList();
        }

        // 🌟 核心刷新逻辑：先清空列表和状态缓存，再重新读取 Root 和 States 生成列表项，最后恢复事件监听和默认选中
        private void RefreshList()
        {
            ListFrames.SelectionChanged -= ListFrames_SelectionChanged;
            ListFrames.Items.Clear();
            _statesList = null;

            // 👑 自动防呆确保 Root 的必备初始值
            var timeProp = _editingObject.GetType().GetProperty("Time");
            var easingProp = _editingObject.GetType().GetProperty("Easing");
            if (string.IsNullOrEmpty(timeProp?.GetValue(_editingObject)?.ToString())) timeProp?.SetValue(_editingObject, "0");
            if (string.IsNullOrEmpty(easingProp?.GetValue(_editingObject)?.ToString())) easingProp?.SetValue(_editingObject, "none");

            // 1. 挂载本体
            ListFrames.Items.Add("👑 [0] 初始状态设定 (Root)");

            // 2. 遍历纯净的 States 关键帧数组
            PropertyInfo statesProp = _editingObject.GetType().GetProperty("States");
            if (statesProp != null)
            {
                _statesList = statesProp.GetValue(_editingObject) as System.Collections.IList;
                _stateType = statesProp.PropertyType.GetGenericArguments()[0];

                if (_statesList != null)
                {
                    for (int i = 0; i < _statesList.Count; i++)
                    {
                        object state = _statesList[i];
                        string subTime = state.GetType().GetProperty("Time")?.GetValue(state)?.ToString() ?? "未定";
                        ListFrames.Items.Add($"🎬 [{i + 1}] 补间关键帧 (Time: {subTime})");
                    }
                }
            }

            BtnRemoveFrame.IsEnabled = false;
            ListFrames.SelectionChanged += ListFrames_SelectionChanged;

            if (ListFrames.Items.Count > 0 && ListFrames.SelectedIndex == -1)
                ListFrames.SelectedIndex = 0;
        }
        // 🌟 选中事件核心逻辑：根据选中项的索引区分 Root 和普通帧，提取对应状态并通过专线大喇叭通知模块四更新详情面板
        private void ListFrames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListFrames.SelectedIndex >= 0)
            {
                string title = ListFrames.SelectedItem.ToString();
                object rootState = _editingObject;
                bool isRoot = ListFrames.SelectedIndex == 0;
                BtnRemoveFrame.IsEnabled = !isRoot;

                if (isRoot)
                    OnFrameSelected?.Invoke(_editingObject, title, rootState, true);
                else if (_statesList != null)
                    OnFrameSelected?.Invoke(_statesList[ListFrames.SelectedIndex - 1], title, rootState, false);
            }
            else
            {
                OnFrameSelected?.Invoke(null, "", null, false);
                BtnRemoveFrame.IsEnabled = false;
            }
        }
        // 🌟 添加帧按钮核心逻辑：确保 States 列表存在，创建新帧并设置默认值，添加到列表并刷新界面，最后自动选中新帧以便编辑
        private void BtnAddFrame_Click(object sender, RoutedEventArgs e)
        {
            PropertyInfo statesProp = _editingObject.GetType().GetProperty("States");
            if (statesProp == null) return;

            if (_statesList == null)
            {
                _statesList = Activator.CreateInstance(statesProp.PropertyType) as System.Collections.IList;
                statesProp.SetValue(_editingObject, _statesList);
            }

            object newState = Activator.CreateInstance(_stateType);
            newState.GetType().GetProperty("Time")?.SetValue(newState, "0");
            newState.GetType().GetProperty("Easing")?.SetValue(newState, "linear");

            _statesList.Add(newState);
            RefreshList();
            ListFrames.SelectedIndex = _statesList.Count; // 自动选中新帧
        }
        // 🌟 删除帧按钮核心逻辑：仅允许删除非 Root 帧，移除选中帧并刷新界面，最后自动选中 Root 以防止选中空白
        private void BtnRemoveFrame_Click(object sender, RoutedEventArgs e)
        {
            if (ListFrames.SelectedIndex > 0 && _statesList != null)
            {
                _statesList.RemoveAt(ListFrames.SelectedIndex - 1);
                RefreshList();
                ListFrames.SelectedIndex = 0; // 滚回Root
            }
        }
    }
}