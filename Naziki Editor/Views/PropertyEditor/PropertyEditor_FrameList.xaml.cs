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

            // ✨ 女娲补天：万一传进来的模板连关键帧列表都没有（比如异常的旧项目），立刻初始化一个！
            if (template != null)
            {
                if (template.Keyframes == null) template.Keyframes = new System.Collections.Generic.List<TemplateState>();
                if (template.BaseState == null) template.BaseState = new TemplateState();
            }

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

            object referenceState = null;
            object newFrame = null;

            // 1. 🧬 寻找“基因供体” (Reference State)
            if (_keyframesList.Count > 0)
            {
                // 如果已经有关键帧了，就继承上一个关键帧的基因
                referenceState = _keyframesList[_keyframesList.Count - 1];
            }
            else
            {
                // 如果这是第一个关键帧，就继承初始属性 (BaseState) 的基因
                referenceState = _templateData != null ? (object)_templateData.BaseState : _editingObject.GetBaseState();
            }

            // 2. 🪄 施展克隆法术
            if (referenceState != null)
            {
                // 利用咱们配置好的大管家进行完美深拷贝！
                string jsonClone = Core.StoryboardSerializer.ToJson(referenceState);
                newFrame = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonClone, _stateType, Core.StoryboardSerializer.GetSettings());

                // 🚨 【小艾的终极防呆】：继承属性可以，但绝对不能继承“时间”！
                // 如果时间和上一帧完全一样，就会触发咱们之前写的 StoryboardValidator 时空悖论报错！
                if (newFrame is ObjectState os)
                {
                    os.Time = null;
                    os.RelativeTime = null;
                    os.AddTime = null;
                    // （可选）如果你希望新帧的缓动曲线默认重置为 linear，也可以加一句 os.Easing = null;
                }
            }
            else
            {
                // 万一遇到宇宙奇点（啥也没有），才使用女娲造人模式（白板）
                newFrame = Activator.CreateInstance(_stateType);
            }

            // 3. 将新生命加入时间轨道
            _keyframesList.Add(newFrame);
            RefreshList();

            // 4. 自动选中这个刚刚诞生的新关键帧
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