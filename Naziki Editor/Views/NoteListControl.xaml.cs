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
using Naziki_Editor.State;

namespace Naziki_Editor.Views
{
    public partial class NoteListControl : UserControl
    {
        // 🌟 换成小艾的卡哇伊纯数据流专线：
        public event Action<List<C2Note>> OnNotesImportRequested;
        // 🌟 重要缓存：当前谱面里被选中的音符集合（通过勾选框维护），用来批量导出事件时打包数据！
        public HashSet<C2Note> _selectedNotes = new HashSet<C2Note>();
        // 🌟 重要缓存：谱面里最后一个音符的时间（秒），用来限制搜索区间的最大值，避免用户输入过大导致引擎闪退！
        public double _maxChartTime = 0;
        // 🔌 全局万能数据包接口
        public ProjectDataContext Context { get; private set; }
        // 
        public void LoadContext(ProjectDataContext context)
        {
            Context = context;
        }

        // 🌟 构造函数：初始化组件（加载 XAML）并做好准备工作
        public NoteListControl()
        {
            InitializeComponent();
        }

        // ==========================================
        // 🌟 核心缓存：一次性构建全量音符 UI 树 (只在导入谱面时执行一次)
        // ==========================================
        public void BuildFullNoteTree()
        {
            if (Context.Chart == null) return;
            NoteTreeView.Items.Clear();

            // 呼叫数学引擎：找出谁是儿子
            bool[] isChild = ChartLogic.FindChildren(Context.Chart);

            // 全量挂载所有的“族长”和对应的“子孙链条”
            for (int i = 0; i < Context.Chart.note_list.Count; i++)
            {
                if (!isChild[i])
                {
                    // 创建族长节点
                    var rootItem = CreateNoteTreeItem(Context.Chart.note_list, i, Context.TimeEngine);
                    rootItem.Tag = i; // 🌟 极其重要：把音符在数据列表里的身份证（索引）挂在 Tag 上作缓存！

                    C2Note rootNote = Context.Chart.note_list[i];
                    if (rootNote.type == 3 || rootNote.type == 6)
                    {
                        int nextIndex = rootNote.next_id;
                        HashSet<int> visited = new HashSet<int>();

                        while (nextIndex >= 0 && nextIndex < Context.Chart.note_list.Count && !visited.Contains(nextIndex))
                        {
                            visited.Add(nextIndex);
                            var childItem = CreateNoteTreeItem(Context.Chart.note_list, nextIndex, Context.TimeEngine);
                            childItem.Tag = nextIndex; // 子节点也挂上身份证
                            rootItem.Items.Add(childItem);

                            nextIndex = Context.Chart.note_list[nextIndex].next_id;
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
            if (Context.Chart == null) return;

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
                    if (ChartLogic.IsChainVisible(Context.Chart, Context.TimeEngine, noteIndex, searchMin, searchMax, searchType, filters))
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
        // 🚀 触发搜索法术：呼叫核心引擎重新过滤！
        // ==========================================
        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
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

        // ==========================================================
        // 🚀 一键导入事件：只传神圣的原始数据，拒绝制造垃圾 UI 壳子！
        // ==========================================================
        private void ImportEventBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNotes.Count == 0)
            {
                MessageBox.Show("还没有选择音符哦！指挥官快去勾选几个吧~", "提示");
                return;
            }

            // 🎯 数据提纯：按 Tick 时间线排好序，直接打包发射出去！
            var sortedNotes = _selectedNotes.OrderBy(n => n.tick).ToList();

            // 📡 呼叫主窗口基站接收数据包
            OnNotesImportRequested?.Invoke(sortedNotes);

            MessageBox.Show($"成功将 {sortedNotes.Count} 个音符打包并传送至故事板核心账本！(๑•̀ㅂ•́)و✧", "传送成功");
        }



    }
}