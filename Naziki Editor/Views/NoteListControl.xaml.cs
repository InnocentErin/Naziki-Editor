using Microsoft.Win32;
using Naziki_Editor.Core;
using Naziki_Editor.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media; // 🌟 解决 Brushes 报错
using System.Windows.Shapes; // 🌟 解决 Rectangle 报错

namespace Naziki_Editor.Views
{
    public partial class NoteListControl : UserControl
    {
        public event Action<TreeViewItem> OnNoteGroupExported;
        private C2Chart _chartData;
        private ChartTimeEngine _timeEngine;
        public C2Chart _currentChart { get; set; }
        public ChartTimeEngine _currentEngine { get; set; }
        public HashSet<C2Note> _selectedNotes = new HashSet<C2Note>();
        public double _maxChartTime = 0;

        public NoteListControl()
        {
            InitializeComponent();
        }

        // ==========================================
        // 🌟 核心缓存：一次性构建全量音符 UI 树 (只在导入谱面时执行一次)
        // ==========================================
        public void BuildFullNoteTree()
        {
            if (_currentChart == null) return;
            NoteTreeView.Items.Clear();

            // 呼叫数学引擎：找出谁是儿子
            bool[] isChild = ChartLogic.FindChildren(_currentChart);

            // 全量挂载所有的“族长”和对应的“子孙链条”
            for (int i = 0; i < _currentChart.note_list.Count; i++)
            {
                if (!isChild[i])
                {
                    // 创建族长节点
                    var rootItem = CreateNoteTreeItem(_currentChart.note_list, i, _currentEngine);
                    rootItem.Tag = i; // 🌟 极其重要：把音符在数据列表里的身份证（索引）挂在 Tag 上作缓存！

                    C2Note rootNote = _currentChart.note_list[i];
                    if (rootNote.type == 3 || rootNote.type == 6)
                    {
                        int nextIndex = rootNote.next_id;
                        HashSet<int> visited = new HashSet<int>();

                        while (nextIndex >= 0 && nextIndex < _currentChart.note_list.Count && !visited.Contains(nextIndex))
                        {
                            visited.Add(nextIndex);
                            var childItem = CreateNoteTreeItem(_currentChart.note_list, nextIndex, _currentEngine);
                            childItem.Tag = nextIndex; // 子节点也挂上身份证
                            rootItem.Items.Add(childItem);

                            nextIndex = _currentChart.note_list[nextIndex].next_id;
                        }
                    }
                    NoteTreeView.Items.Add(rootItem);
                }
            }
        }
        // 🎨 辅助法术：专门造具有漂亮颜色的树叶
        private TreeViewItem CreateNoteTreeItem(List<C2Note> allNotes, int currentIndex, ChartTimeEngine engine)
        {
            C2Note note = allNotes[currentIndex];
            double realTime = engine.TickToSeconds(note.tick);
            string typeName = "Unknown";
            Brush colorBrush = Brushes.Gray;

            switch (note.type)
            {
                case 0: typeName = "Click"; colorBrush = Brushes.LightSeaGreen; break;
                case 1: typeName = "Hold"; colorBrush = Brushes.Magenta; break;
                case 2: typeName = "L-Hold"; colorBrush = Brushes.Gold; break;
                case 3: typeName = "Drag"; colorBrush = Brushes.MediumPurple; break;
                case 4: typeName = "D-Child"; colorBrush = Brushes.MediumPurple; break;
                case 5: typeName = "Flick"; colorBrush = Brushes.IndianRed; break;
                case 6: typeName = "CDrag"; colorBrush = Brushes.SkyBlue; break;
                case 7: typeName = "CD-Child"; colorBrush = Brushes.SkyBlue; break;
            }

            Grid rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(25) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(55) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(65) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });

            CheckBox chk = new CheckBox() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            TreeViewItem item = new TreeViewItem();
            chk.Tag = new Tuple<C2Note, TreeViewItem>(note, item);

            chk.Checked += (s, e) => {
                _selectedNotes.Add(note);
                if (note.type == 3 || note.type == 6)
                {
                    foreach (TreeViewItem child in item.Items)
                    {
                        var childGrid = child.Header as Grid;
                        var childChk = childGrid.Children[0] as CheckBox;
                        if (childChk != null) childChk.IsChecked = true;
                    }
                }
            };

            chk.Unchecked += (s, e) => {
                _selectedNotes.Remove(note);
                if (note.type == 3 || note.type == 6)
                {
                    foreach (TreeViewItem child in item.Items)
                    {
                        var childGrid = child.Header as Grid;
                        var childChk = childGrid.Children[0] as CheckBox;
                        if (childChk != null) childChk.IsChecked = false;
                    }
                }
            };

            Grid.SetColumn(chk, 0); rowGrid.Children.Add(chk);

            StackPanel idPanel = new StackPanel() { Orientation = Orientation.Horizontal };
            idPanel.Children.Add(new Rectangle() { Width = 10, Height = 10, Fill = colorBrush, Margin = new Thickness(0, 0, 5, 0) });
            idPanel.Children.Add(new TextBlock() { Text = note.id.ToString(), FontSize = 10 });
            Grid.SetColumn(idPanel, 1); rowGrid.Children.Add(idPanel);

            TextBlock timeTxt = new TextBlock() { Text = realTime.ToString("0.000"), FontSize = 10 };
            Grid.SetColumn(timeTxt, 2); rowGrid.Children.Add(timeTxt);

            TextBlock typeTxt = new TextBlock() { Text = typeName, FontSize = 10, Foreground = colorBrush };
            Grid.SetColumn(typeTxt, 3); rowGrid.Children.Add(typeTxt);

            TextBlock xTxt = new TextBlock() { Text = note.x.ToString("0.00"), FontSize = 10 };
            Grid.SetColumn(xTxt, 4); rowGrid.Children.Add(xTxt);

            item.Header = rowGrid;
            item.IsExpanded = false;

            return item;
        }

        // ==========================================
        // 🎨 核心 UI：高速显隐过滤器 (完全基于 UI 缓存，零销毁，零新建！)
        // ==========================================
        public void RefreshNoteList()
        {
            if (_currentChart == null) return;

            // 1. 刷新列表时清空购物车，并将下方全选框重置
            _selectedNotes.Clear();
            if (SelectAllChk != null) SelectAllChk.IsChecked = false;

            // 2. 读取并转换搜索区间条件
            double searchMin = 0;
            double searchMax = double.MaxValue;
            double.TryParse(SearchMinBox.Text, out searchMin);
            if (double.TryParse(SearchMaxBox.Text, out double parsedMax)) searchMax = parsedMax;

            int searchType = SearchMode.SelectedIndex;
            if (searchType == 0 && searchMax > _maxChartTime) searchMax = _maxChartTime;
            if (searchType == 1 && searchMax > 1.0) searchMax = 1.0;

            // 3. 提取过滤状态打包给数学引擎
            bool[] filters = { FilterClick.IsChecked == true, FilterHold.IsChecked == true, FilterLHold.IsChecked == true, FilterDrag.IsChecked == true, FilterFlick.IsChecked == true, FilterCDrag.IsChecked == true };

            // 4. 🚀 终极闪电过滤循环：不执行 Items.Clear()！只给已有控件下达显隐密令！
            foreach (TreeViewItem rootItem in NoteTreeView.Items)
            {
                if (rootItem.Tag is int noteIndex)
                {
                    // 🧼 顺手把之前勾选的复选框给清空（因为购物车空了）
                    if (rootItem.Header is Grid grid && grid.Children[0] is CheckBox chk)
                    {
                        chk.IsChecked = false;
                    }

                    // 🔮 呼叫纯数学引擎：判定这一大家子（长条链条）是否有任何一个音符落入选区
                    if (ChartLogic.IsChainVisible(_currentChart, _currentEngine, noteIndex, searchMin, searchMax, searchType, filters))
                    {
                        rootItem.Visibility = Visibility.Visible; // 显形！
                    }
                    else
                    {
                        rootItem.Visibility = Visibility.Collapsed; // 隐身！
                    }
                }
            }
        }
        // 🎨 辅助法术：过滤器的点击事件 (所有过滤器共用一个事件处理器，简化代码！)
        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            var filters = new[] { FilterClick, FilterCDrag, FilterDrag, FilterFlick, FilterHold, FilterLHold };
            if (filters.All(f => f.IsChecked == false))
            {
                ((ToggleButton)sender).IsChecked = true;
                return;
            }
            RefreshNoteList();
        }

        // ==========================================
        // 🌟 谱面列表里的全选按钮 (注意：这只是 UI 上的全选，真正的数据选中状态是通过 CheckBox 的事件来维护的！)
        // ==========================================
        private void SelectAllChk_Click(object sender, RoutedEventArgs e)
        {
            bool isAllChecked = SelectAllChk.IsChecked == true;
            foreach (TreeViewItem rootItem in NoteTreeView.Items)
            {
                var grid = rootItem.Header as Grid;
                var chk = grid.Children[0] as CheckBox;
                if (chk != null) chk.IsChecked = isAllChecked;
            }
        }

        // ==========================================
        // 🌟 谱面列表里的导入事件按钮：把购物车里的音符打包成一个新的“音符控制器”，然后丢到左侧的“音符抽屉”里！
        // ==========================================
        private void ImportEventBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNotes.Count == 0)
            {
                MessageBox.Show("还没有选择音符哦！", "提示");
                return;
            }

            TreeViewItem groupItem = new TreeViewItem();
            string groupName = $"🎵 音符控制组_{DateTime.Now:HHmmss} ({_selectedNotes.Count}个音符)";
            groupItem.Header = groupName;
            groupItem.Foreground = Brushes.DarkSlateBlue;
            groupItem.FontWeight = FontWeights.Bold;

            TreeViewItem effectNode = new TreeViewItem() { Header = "[+] 添加动画特效...", Foreground = Brushes.Gray };
            groupItem.Items.Add(effectNode);

            TreeViewItem targetFolder = new TreeViewItem() { Header = "▷ 绑定的音符列表", IsExpanded = false };

            foreach (var note in _selectedNotes.OrderBy(n => n.tick))
            {
                double time = _currentEngine.TickToSeconds(note.tick);
                string noteInfo = $"ID: {note.id} | 时间: {time:0.000}s | X: {note.x}";
                targetFolder.Items.Add(new TreeViewItem() { Header = noteInfo });
            }

            groupItem.Items.Add(targetFolder);
            // 触发事件，把做好的 groupItem 丢出去！
            OnNoteGroupExported?.Invoke(groupItem);

            MessageBox.Show($"成功将 {_selectedNotes.Count} 个音符打包并传送到“音符抽屉”！", "传送成功");
        }

        

    }
}