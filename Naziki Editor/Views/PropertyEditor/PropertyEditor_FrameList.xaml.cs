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


        private class FrameNodeTag
        {
            public bool IsRoot { get; set; }
            public bool IsGroup { get; set; }
            public object TargetObject { get; set; } // 挂载的数据实体
            public System.Collections.IList ParentList { get; set; }
            public int Index { get; set; }
        }



        // ✨ 终极枢纽：当用户选中一个帧时，触发这个事件，把具体的帧数据传出去！
        public event Action<object, string, object, bool> OnFrameSelected;

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
            TreeFrames.Items.Clear();
            _statesList = null;

            // 👑 自动确保 Root 的必备初始值绝对存在 (Time & Easing 防空桩)
            var timeProp = _editingObject.GetType().GetProperty("Time");
            var easingProp = _editingObject.GetType().GetProperty("Easing");
            if (string.IsNullOrEmpty(timeProp?.GetValue(_editingObject)?.ToString())) timeProp?.SetValue(_editingObject, "0");
            if (string.IsNullOrEmpty(easingProp?.GetValue(_editingObject)?.ToString())) easingProp?.SetValue(_editingObject, "none");

            // 1. 挂载唯一的【[0] 🚩 初始状态设定 (Root)】节点
            TreeViewItem rootNode = new TreeViewItem
            {
                Header = "👑 [0] 初始状态设定 (Root)",
                Tag = new FrameNodeTag { IsRoot = true, TargetObject = _editingObject }
            };
            TreeFrames.Items.Add(rootNode);

            // 2. 遍历加载子状态组与关键帧
            PropertyInfo statesProp = _editingObject.GetType().GetProperty("States");
            if (statesProp != null)
            {
                _statesList = statesProp.GetValue(_editingObject) as System.Collections.IList;
                _stateType = statesProp.PropertyType.GetGenericArguments()[0];

                if (_statesList != null)
                {
                    for (int i = 0; i < _statesList.Count; i++)
                    {
                        object groupState = _statesList[i];

                        // 创建状态组节点
                        TreeViewItem groupNode = new TreeViewItem
                        {
                            Header = $"📦 状态组 {i + 1}",
                            Tag = new FrameNodeTag { IsGroup = true, TargetObject = groupState, ParentList = _statesList, Index = i }
                        };

                        // 每一个状态组下面，自动默认生成它的“动作关键帧”
                        string subTime = groupState.GetType().GetProperty("Time")?.GetValue(groupState)?.ToString() ?? "未定";
                        TreeViewItem subFrameNode = new TreeViewItem
                        {
                            Header = $"  ⏱️ 关键帧 [Time: {subTime}]",
                            Tag = new FrameNodeTag { IsRoot = false, IsGroup = false, TargetObject = groupState, ParentList = _statesList, Index = i }
                        };
                        groupNode.Items.Add(subFrameNode);

                        TreeFrames.Items.Add(groupNode);
                    }
                }
            }

            BtnRemove.IsEnabled = false; // 初始状态下不给删除
        }

        // ==========================================
        // 📣 树节点点选大喇叭：接通 4 参数完全体信道
        // ==========================================
        private void TreeFrames_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (TreeFrames.SelectedItem is TreeViewItem selectedNode && selectedNode.Tag is FrameNodeTag tag)
            {
                object rootState = _editingObject; // 真正的老祖宗就是 _editingObject

                // 按钮防呆：Root 节点绝不允许被删除！
                BtnRemove.IsEnabled = !tag.IsRoot;

                // 呼叫右侧参数编辑器
                OnFrameSelected?.Invoke(tag.TargetObject, selectedNode.Header.ToString().Trim(), rootState, tag.IsRoot);
            }
            else
            {
                OnFrameSelected?.Invoke(null, "", null, false);
                BtnRemove.IsEnabled = false;
            }
        }

        // ✨ 动态创造一个新的 State 实例，附带 Cytoid 的出厂设置，然后添加到列表里！至于 State 的具体类型，我们通过反射从 States 属性的泛型参数里拿到！
        // ==========================================
        // ➕ 创建状态组按钮响应
        // ==========================================
        private void BtnAddGroup_Click(object sender, RoutedEventArgs e)
        {
            PropertyInfo statesProp = _editingObject.GetType().GetProperty("States");
            if (statesProp == null) return;

            if (_statesList == null)
            {
                Type listType = statesProp.PropertyType;
                _statesList = Activator.CreateInstance(listType) as System.Collections.IList;
                statesProp.SetValue(_editingObject, _statesList);
            }

            // 创建新的状态组实体
            object newStateGroup = Activator.CreateInstance(_stateType);
            newStateGroup.GetType().GetProperty("Time")?.SetValue(newStateGroup, "0");
            newStateGroup.GetType().GetProperty("Easing")?.SetValue(newStateGroup, "linear");

            _statesList.Add(newStateGroup);
            RefreshList();

            // 自动展开并选中新状态组的第一个关键帧
            if (TreeFrames.Items.Count > 0)
            {
                var lastGroup = TreeFrames.Items[TreeFrames.Items.Count - 1] as TreeViewItem;
                if (lastGroup != null && lastGroup.Items.Count > 0)
                {
                    var subFrame = lastGroup.Items[0] as TreeViewItem;
                    if (subFrame != null) subFrame.IsSelected = true;
                }
            }
        }

        // ==========================================
        // 🗑️ 删除选中帧/状态组响应 (落实问题 2)
        // ==========================================
        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (TreeFrames.SelectedItem is TreeViewItem selectedNode && selectedNode.Tag is FrameNodeTag tag)
            {
                if (tag.IsRoot) return; // 铁律防御：初始状态不可删

                if (tag.ParentList != null && tag.Index >= 0 && tag.Index < tag.ParentList.Count)
                {
                    tag.ParentList.RemoveAt(tag.Index);
                    RefreshList();

                    // 默认滚回 Root 节点
                    if (TreeFrames.Items.Count > 0 && TreeFrames.Items[0] is TreeViewItem rootNode)
                        rootNode.IsSelected = true;
                }
            }
        }
    }
}