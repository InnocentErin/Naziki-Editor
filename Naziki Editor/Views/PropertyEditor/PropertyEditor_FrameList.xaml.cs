using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditor_FrameList : UserControl
    {
        private StoryboardObject _editingObject;
        private System.Collections.IList _statesList;
        private Type _stateType;
        private State.ProjectDataContext _context;
        private StoryboardTemplate _templateData;

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

        // ==========================================
        // ✨ 新增：模板专用的数据铺设轨
        // ==========================================
        // ==========================================
        // ✨ 新增：模板专用的数据铺设轨
        // ==========================================
        public void LoadTemplateData(StoryboardTemplate template, State.ProjectDataContext context)
        {
            _context = context;
            _templateData = template; // ✨ 开启模板印记！
            _statesList = template.States; // 喂给管家，防止它报空指针

            ListFrames.Items.Clear();

            // 👑 仅仅放入纯净的 UI 元素，把点击判定统一交给 SelectionChanged 管家！
            ListFrames.Items.Add(new ListBoxItem
            {
                Content = "👑 模板基础打底属性 (Root)",
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.Gold
            });

            if (template.States != null)
            {
                int frameIndex = 1;
                foreach (var state in template.States)
                {
                    ListFrames.Items.Add(new ListBoxItem { Content = $"帧 #{frameIndex} (Relative Time: {state.RelativeTime})" });
                    frameIndex++;
                }
            }

            if (ListFrames.Items.Count > 0) ListFrames.SelectedIndex = 0;
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
            if (ListFrames.SelectedIndex == -1)
            {
                OnFrameSelected?.Invoke(null, "", null, false);
                return;
            }

            // ==========================================
            // 👑 模板专属解析通道
            // ==========================================
            if (_templateData != null)
            {
                if (ListFrames.SelectedIndex == 0) // 点了第0项（虚拟Root）
                {
                    OnFrameSelected?.Invoke(_templateData, "模板初始状态", _templateData, true);
                }
                else // 点了子帧
                {
                    int stateIndex = ListFrames.SelectedIndex - 1;
                    if (_templateData.States != null && stateIndex >= 0 && stateIndex < _templateData.States.Count)
                    {
                        var state = _templateData.States[stateIndex];
                        OnFrameSelected?.Invoke(state, $"模板子帧 #{ListFrames.SelectedIndex}", _templateData, false);
                    }
                }
                return;
            }

            // ==========================================
            // 🚗 原来的普通对象解析通道
            // ==========================================
            if (_statesList != null && ListFrames.SelectedIndex >= 0 && ListFrames.SelectedIndex < _statesList.Count)
            {
                var state = _statesList[ListFrames.SelectedIndex];
                OnFrameSelected?.Invoke(state, $"帧 #{ListFrames.SelectedIndex + 1}", _editingObject, false);
            }
        }



        // 🌟 添加帧按钮核心逻辑：确保 States 列表存在，创建新帧并设置默认值，添加到列表并刷新界面，最后自动选中新帧以便编辑
        private void BtnAddFrame_Click(object sender, RoutedEventArgs e)
        {
            // ✨ 模板模式专属造帧机
            if (_templateData != null)
            {
                if (_templateData.States == null) _templateData.States = new System.Collections.Generic.List<StoryboardTemplate>();
                _templateData.States.Add(new StoryboardTemplate { RelativeTime = 0, Easing = "linear" }); // 模板的子帧也是 Template 类型哦

                LoadTemplateData(_templateData, _context); // 重新铺设 UI
                Dispatcher.BeginInvoke(new Action(() => { ListFrames.SelectedIndex = ListFrames.Items.Count - 1; }), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }



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
            // ✨ 模板模式专属销毁机
            if (_templateData != null)
            {
                if (ListFrames.SelectedIndex > 0 && _templateData.States != null) // 第0帧Root绝对不许删！
                {
                    _templateData.States.RemoveAt(ListFrames.SelectedIndex - 1);
                    LoadTemplateData(_templateData, _context);
                }
                return;
            }



            if (ListFrames.SelectedIndex > 0 && _statesList != null)
            {
                _statesList.RemoveAt(ListFrames.SelectedIndex); // 下标完美对应，直接删！
                RefreshList();

                ListFrames.SelectedIndex = -1;
            }
        }
    }
}