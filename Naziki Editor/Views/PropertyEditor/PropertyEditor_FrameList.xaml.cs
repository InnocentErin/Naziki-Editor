using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.Windows;
using System.Windows.Controls;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditor_FrameList : UserControl
    {
        private IStoryboardEntity _editingObject;      // 🌟 接口升级
        private System.Collections.IList _keyframesList; // 🌟 列表升级
        private Type _stateType;
        private ProjectDataContext _context;
        private C2Template _templateData;              // 🌟 模板升级

        public event Action<object, string, object, bool> OnFrameSelected;

        public PropertyEditor_FrameList()
        {
            InitializeComponent();
        }

        public void LoadData(IStoryboardEntity editingObj, ProjectDataContext context)
        {
            _editingObject = editingObj;
            _context = context;
            _templateData = null;

            // ✨ 接口显威：直接无视具体类型，一把抓出动画帧列表！
            _keyframesList = _editingObject?.GetKeyframes();
            if (_editingObject != null)
            {
                _stateType = _editingObject.GetBaseState().GetType(); // 自动识别状态帧的真实Type
            }

            RefreshList();
            ListFrames.SelectedIndex = -1;
            OnFrameSelected?.Invoke(null, "", null, false);
        }

        public void LoadTemplateData(C2Template template, ProjectDataContext context)
        {
            _templateData = template;
            _context = context;
            _editingObject = null;
            _keyframesList = template?.Keyframes;
            if (template != null)
            {
                _stateType = typeof(TemplateState);
            }

            RefreshList();
            ListFrames.SelectedIndex = -1;
            OnFrameSelected?.Invoke(null, "", null, false);
        }

        private void RefreshList()
        {
            ListFrames.Items.Clear();

            // 1. 铺设永恒的第 0 帧（Root 初始状态）
            ListFrames.Items.Add("🌟 初始属性 (Base State)");

            // 2. 依次平铺后续所有关键动画帧
            if (_keyframesList != null)
            {
                for (int i = 0; i < _keyframesList.Count; i++)
                {
                    var frame = _keyframesList[i] as ObjectState;
                    string timeText = frame?.Time?.ToString() ?? (frame?.RelativeTime?.ToString() ?? "未知时空");
                    ListFrames.Items.Add($"🎬 关键帧 {i + 1} ({timeText}s)");
                }
            }
        }

        private void ListFrames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListFrames.SelectedIndex < 0) return;

            if (ListFrames.SelectedIndex == 0)
            {
                // 选中了 Base 初始状态
                object baseState = _templateData != null ? (object)_templateData.BaseState : _editingObject.GetBaseState();
                OnFrameSelected?.Invoke(baseState, "🌟 初始属性编辑", baseState, true);
            }
            else if (_keyframesList != null && (ListFrames.SelectedIndex - 1) < _keyframesList.Count)
            {
                // 选中了常规动画关键帧
                object activeFrame = _keyframesList[ListFrames.SelectedIndex - 1];
                object baseState = _templateData != null ? (object)_templateData.BaseState : _editingObject.GetBaseState();
                OnFrameSelected?.Invoke(activeFrame, $"🎬 关键帧 {ListFrames.SelectedIndex} 详情", baseState, false);
            }
        }

        private void BtnAddFrame_Click(object sender, RoutedEventArgs e)
        {
            if (_stateType == null || _keyframesList == null) return;

            // 魔法般自动生产对应卡槽的全新空白帧！
            var newFrame = Activator.CreateInstance(_stateType);
            _keyframesList.Add(newFrame);

            RefreshList();

            Dispatcher.BeginInvoke(new Action(() => {
                ListFrames.SelectedIndex = ListFrames.Items.Count - 1;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void BtnRemoveFrame_Click(object sender, RoutedEventArgs e)
        {
            if (ListFrames.SelectedIndex > 0 && _keyframesList != null)
            {
                _keyframesList.RemoveAt(ListFrames.SelectedIndex - 1);
                RefreshList();
                ListFrames.SelectedIndex = 0; // 自动滚回第0帧，绝不卡死UI
            }
        }
    }
}