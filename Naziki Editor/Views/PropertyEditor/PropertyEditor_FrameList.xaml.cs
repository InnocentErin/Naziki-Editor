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

        public void LoadData(StoryboardObject editingObj, State.ProjectDataContext context)
        {
            _editingObject = editingObj;
            //_context = context;
            RefreshList();
            // 核心修复：进入编辑器时，强制解除选中状态
            ListFrames.SelectedIndex = -1;
            // 手动触发一次空状态广播，确保右侧面板显示空页面提示
            OnFrameSelected?.Invoke(null, "", null, false);
        }

        // 🌟 核心刷新逻辑：先清空列表和状态缓存，再重新读取 Root 和 States 生成列表项，最后恢复事件监听和默认选中
        private void RefreshList()
        {
            ListFrames.SelectionChanged -= ListFrames_SelectionChanged;
            ListFrames.Items.Clear();

            PropertyInfo statesProp = _editingObject.GetType().GetProperty("States");
            if (statesProp != null)
            {
                _statesList = statesProp.GetValue(_editingObject) as System.Collections.IList;
                _stateType = statesProp.PropertyType.GetGenericArguments()[0];

                if (_statesList == null || _statesList.Count == 0)
                {
                    if (_statesList == null)
                    {
                        _statesList = Activator.CreateInstance(statesProp.PropertyType) as System.Collections.IList;
                        statesProp.SetValue(_editingObject, _statesList);
                    }
                    object rootState = Activator.CreateInstance(_stateType);
                    rootState.GetType().GetProperty("Time")?.SetValue(rootState, "0");
                    rootState.GetType().GetProperty("Easing")?.SetValue(rootState, "none");
                    _statesList.Add(rootState);
                }

                ListFrames.Items.Add("👑 [0] 初始状态设定 (Root)");

                for (int i = 1; i < _statesList.Count; i++)
                {
                    object state = _statesList[i];
                    object timeObj = state.GetType().GetProperty("Time")?.GetValue(state);
                    string subTime = "未定";

                    // ✨ 核心修复：完美识别 JArray，告别 System.Collections 乱码！
                    if (timeObj is Newtonsoft.Json.Linq.JArray jArray)
                        subTime = $"多点触发 ({jArray.Count})";
                    else if (timeObj is System.Collections.IList tList)
                        subTime = $"多点触发 ({tList.Count})";
                    else if (timeObj != null)
                        subTime = timeObj.ToString();

                    ListFrames.Items.Add($"🎬 [{i}] 补间关键帧 (Time: {subTime})");
                }
            }

            BtnRemoveFrame.IsEnabled = false;
            ListFrames.SelectionChanged += ListFrames_SelectionChanged;
        }



        // 🌟 选中事件核心逻辑：根据选中项的索引区分 Root 和普通帧，提取对应状态并通过专线大喇叭通知模块四更新详情面板
        private void ListFrames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListFrames.SelectedIndex >= 0 && _statesList != null && _statesList.Count > 0)
            {
                string title = ListFrames.SelectedItem.ToString();
                object trueRootState = _statesList[0]; // 真正的初始属性集合！
                bool isRoot = ListFrames.SelectedIndex == 0;
                BtnRemoveFrame.IsEnabled = !isRoot;

                // 因为下标现在完美 1:1 对应，SelectedIndex 就是 _statesList 里的 Index！
                OnFrameSelected?.Invoke(_statesList[ListFrames.SelectedIndex], title, trueRootState, isRoot);
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
            if (_stateType == null) return;

            object newState = Activator.CreateInstance(_stateType);
            newState.GetType().GetProperty("Time")?.SetValue(newState, "0");
            newState.GetType().GetProperty("Easing")?.SetValue(newState, "linear");

            // 防呆保险：确保列表实例一定存在
            if (_statesList == null)
            {
                PropertyInfo statesProp = _editingObject.GetType().GetProperty("States");
                _statesList = Activator.CreateInstance(statesProp.PropertyType) as System.Collections.IList;
                statesProp.SetValue(_editingObject, _statesList);
            }

            _statesList.Add(newState);
            RefreshList();

            // ✨ 核心修复：异步派发选择事件，彻底切断 WPF 同步更新引发的堆栈死锁！
            Dispatcher.BeginInvoke(new Action(() => {
                ListFrames.SelectedIndex = ListFrames.Items.Count - 1;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }



        // 🌟 删除帧按钮核心逻辑：仅允许删除非 Root 帧，移除选中帧并刷新界面，最后自动选中 Root 以防止选中空白
        private void BtnRemoveFrame_Click(object sender, RoutedEventArgs e)
        {
            if (ListFrames.SelectedIndex > 0 && _statesList != null)
            {
                _statesList.RemoveAt(ListFrames.SelectedIndex); // 下标完美对应，直接删！
                RefreshList();

                ListFrames.SelectedIndex = -1;
            }
        }
    }
}