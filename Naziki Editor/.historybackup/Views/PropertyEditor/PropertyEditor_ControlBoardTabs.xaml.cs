using Naziki_Editor.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditor_ControlBoardTabs : UserControl
    {
        // ⚡ 专属神圣交叉纽带：当标签页切换时，告诉父窗口该换数据源了！
        public event Action<IStoryboardEntity> OnActiveObjectSwitched;

        private IStoryboardEntity _mainObject;
        private string _originalId;
        private bool _isInternalTabChange = false;
        private IStoryboardEntity _currentActiveObject;

        // 对外暴露的影子集合，方便父窗口最后拿去落盘
        public List<IStoryboardEntity> ControlBoards { get; private set; } = new List<IStoryboardEntity>();

        public PropertyEditor_ControlBoardTabs()
        {
            InitializeComponent();
        }

        // 📥 唯一入口：父窗口把纯净的主对象和克隆好的影子兵团交接给它
        public void Init(IStoryboardEntity mainObject, List<IStoryboardEntity> controlBoards)
        {
            _mainObject = mainObject;
            _originalId = mainObject.Id ?? "";
            ControlBoards = controlBoards ?? new List<IStoryboardEntity>();

            // 独立模式防呆：如果主对象连ID都没有，或者它本身就是个控制板，直接把标签面板干掉
            if (string.IsNullOrEmpty(_originalId) || !string.IsNullOrEmpty(_mainObject.TargetId))
            {
                RootContainer.Visibility = Visibility.Collapsed;
                SwitchActiveObject(_mainObject);
                return;
            }

            RootContainer.Visibility = Visibility.Visible;
            RefreshTabs();
        }

        private void RefreshTabs()
        {
            _isInternalTabChange = true;
            int currentIndex = TabControlBoards.SelectedIndex;
            TabControlBoards.Items.Clear();

            var mainTab = new TabItem { Header = "🎭 主体对象", Tag = _mainObject };
            TabControlBoards.Items.Add(mainTab);

            // ✨ 极简自增优化：用 1, 2, 3 的代号隐藏掉长长的真实 ID 全称！
            int targetIndex = 1;
            foreach (var cb in ControlBoards)
            {
                var cbTab = new TabItem { Header = $"🎛️ Target_{targetIndex}", Tag = cb };
                TabControlBoards.Items.Add(cbTab);
                targetIndex++; // 编号自增，为未来的轨道组排版埋下引线
            }

            TabControlBoards.SelectedIndex = (currentIndex >= 0 && currentIndex < TabControlBoards.Items.Count) ? currentIndex : 0;
            _isInternalTabChange = false;

            if (TabControlBoards.SelectedItem is TabItem ti && ti.Tag is IStoryboardEntity obj)
                SwitchActiveObject(obj);
        }

        private void TabControlBoards_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInternalTabChange) return;
            if (TabControlBoards.SelectedItem is TabItem ti && ti.Tag is IStoryboardEntity obj)
                SwitchActiveObject(obj);
        }

        private void SwitchActiveObject(IStoryboardEntity obj)
        {
            _currentActiveObject = obj;
            bool isMain = (_currentActiveObject == _mainObject);
            BtnDeleteControlBoard.Visibility = isMain ? Visibility.Collapsed : Visibility.Visible;

            // 发射信号，让外面干活！
            OnActiveObjectSwitched?.Invoke(obj);
        }

        private void BtnAddControlBoard_Click(object sender, RoutedEventArgs e)
        {
            Type t = _mainObject.GetType();
            IStoryboardEntity newCb = (IStoryboardEntity)Activator.CreateInstance(t);

            // 确保生成独一无二的ID
            newCb.Id = $"{_originalId}_target_{ControlBoards.Count + 1}_{DateTime.Now.ToString("HHmmss")}";
            newCb.TargetId = _originalId;

            ControlBoards.Add(newCb);
            RefreshTabs();
            TabControlBoards.SelectedIndex = TabControlBoards.Items.Count - 1;
        }

        private void BtnDeleteControlBoard_Click(object sender, RoutedEventArgs e)
        {
            if (_currentActiveObject != _mainObject)
            {
                var res = MessageBox.Show($"确定要将控制板 [{_currentActiveObject.Id}] 彻底抹杀吗？\n此操作不可撤销！", "物理销毁确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes)
                {
                    ControlBoards.Remove(_currentActiveObject);
                    RefreshTabs();
                }
            }
        }
    }
}