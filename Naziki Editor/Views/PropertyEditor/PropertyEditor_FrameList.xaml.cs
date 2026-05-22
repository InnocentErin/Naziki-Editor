using System;
using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditor_FrameList : UserControl
    {
        private StoryboardObject _editingObject;
        private ProjectDataContext _context;
        private IList _statesList;
        private Type _stateType;

        // ✨ 终极枢纽：当用户选中一个帧时，触发这个事件，把具体的帧数据传出去！
        public event Action<object, string> OnFrameSelected;

        public PropertyEditor_FrameList()
        {
            InitializeComponent();
        }

        public void LoadData(StoryboardObject editingObj, ProjectDataContext context)
        {
            _editingObject = editingObj;
            _context = context;
            RefreshList();
        }

        private void RefreshList()
        {
            // 断开大喇叭，防止 Items.Clear() 引发死循环！
            ListFrames.SelectionChanged -= ListFrames_SelectionChanged;
            ListFrames.Items.Clear();
            _statesList = null;

            PropertyInfo statesProp = _editingObject.GetType().GetProperty("States");
            if (statesProp != null)
            {
                _statesList = statesProp.GetValue(_editingObject) as IList;
                _stateType = statesProp.PropertyType.GetGenericArguments()[0]; // 获取具体的 State 类型

                if (_statesList != null)
                {
                    for (int i = 0; i < _statesList.Count; i++)
                    {
                        // 智能识别：第0帧叫初始属性，后面的叫关键帧
                        string header = i == 0 ? $"[0] 🚩 初始状态 (Init)" : $"[{i}] 🎬 补间关键帧";
                        ListFrames.Items.Add(header);
                    }
                }
            }

            // 智能防呆按钮变色
            if (_statesList == null || _statesList.Count == 0)
            {
                BtnAddFrame.Content = "⚠️ 创建初始状态 (States[0])";
                BtnAddFrame.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5555")); // 红色警告
            }
            else
            {
                BtnAddFrame.Content = "➕ 添加后续关键帧";
                BtnAddFrame.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4DB8FF")); // 蓝色正常
            }

            BtnRemoveFrame.IsEnabled = (_statesList != null && _statesList.Count > 0);

            // 🟢 重新接通大喇叭！
            ListFrames.SelectionChanged += ListFrames_SelectionChanged;
        }

        private void ListFrames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListFrames.SelectedIndex >= 0 && _statesList != null)
            {
                object selectedState = _statesList[ListFrames.SelectedIndex];
                string frameTitle = ListFrames.SelectedItem.ToString(); // 📣 呼叫模块四！
                object rootState = _statesList[0];// 永远抓取第 0 帧作为标尺
                bool isRoot = (ListFrames.SelectedIndex == 0);
                OnFrameSelected?.Invoke(selectedState, frameTitle); // 传递选中的帧数据和标题，让模块四去显示和编辑它！
                
            }
            else
            {
                OnFrameSelected?.Invoke(null, ""); // 通知模块四清空画面
            }
        }

        private void BtnAddFrame_Click(object sender, RoutedEventArgs e)
        {
            PropertyInfo statesProp = _editingObject.GetType().GetProperty("States");
            if (statesProp == null) return;

            if (_statesList == null)
            {
                Type listType = statesProp.PropertyType;
                _statesList = Activator.CreateInstance(listType) as IList;
                statesProp.SetValue(_editingObject, _statesList);
            }

            // 动态创造一个新帧，并附带 Cytoid 的出厂设置
            object newState = Activator.CreateInstance(_stateType);
            _stateType.GetProperty("Time")?.SetValue(newState, 0.0f);
            _stateType.GetProperty("Opacity")?.SetValue(newState, 1.0f);
            _stateType.GetProperty("Easing")?.SetValue(newState, "linear"); // 默认线性补间

            _statesList.Add(newState);
            RefreshList();

            // 自动选中刚刚新建的这一帧，触发联动
            ListFrames.SelectedIndex = _statesList.Count - 1;
        }

        private void BtnRemoveFrame_Click(object sender, RoutedEventArgs e)
        {
            if (ListFrames.SelectedIndex >= 0 && _statesList != null)
            {
                _statesList.RemoveAt(ListFrames.SelectedIndex);
                RefreshList();
            }
        }
    }
}