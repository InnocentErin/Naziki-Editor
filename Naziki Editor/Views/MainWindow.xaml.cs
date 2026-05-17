using Microsoft.Win32;
using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Naziki_Editor.Views;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Naziki_Editor.Views
{
    public partial class MainWindow : Window
    {
        private C2Chart _currentChart;
        private ChartTimeEngine _currentEngine;
        private HashSet<C2Note> _selectedNotes = new HashSet<C2Note>();
        private double _maxChartTime = 0;
        private StoryboardRoot _currentStoryboardRoot;

        public MainWindow()
        {
            InitializeComponent();

            // 牵线搭桥：当事件列表扫描到素材时，通知素材列表去刷新 UI
            EventList.OnAssetScanned += (bundle) => AssetList.RefreshAssetListUI(bundle);

            // 牵线搭桥：当音符列表打包好音符组时，通知事件列表塞进树状图里
            NoteList.OnNoteGroupExported += (groupItem) => EventList.AddNoteGroupToTree(groupItem);

            EventList.OnStoryboardLoaded += (root) =>
            {
                _currentStoryboardRoot = root;
                TriggerAutoLinkIfReady(); // 尝试联姻
            };

        }

        // ==========================================
        // 🌟 顶部菜单栏：事件绑定
        // ==========================================
        // 在 MainWindow.xaml.cs 中
        private void MenuOpenProject_Click(object sender, RoutedEventArgs e) => EventList.ExecuteOpenProject();


        private void MenuExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void MenuImportChart_Click(object sender, RoutedEventArgs e) => ExecuteImportChart();


        // ==========================================
        // 🌟 预留的谱面导入与渲染
        // ==========================================
        private void ExecuteImportChart()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "Cytus II 谱面 (*.json)|*.json|所有文件 (*.*)|*.*", Title = "请选择你的谱面文件" };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string jsonText = File.ReadAllText(openFileDialog.FileName);
                    C2Chart chart = Newtonsoft.Json.JsonConvert.DeserializeObject<C2Chart>(jsonText);

                    if (chart == null || chart.time_base == 0 || chart.page_list == null || chart.tempo_list == null || chart.note_list == null)
                    {
                        MessageBox.Show("这根本不是合法的 Cytus II 谱面呀，大骗子！(｀Д´)", "格式校验失败");
                        return;
                    }

                    _currentChart = chart;
                    _currentEngine = new ChartTimeEngine(chart.tempo_list, chart.time_base);

                    NoteList._currentChart = _currentChart;
                    NoteList._currentEngine = _currentEngine;

                    NoteList.BuildFullNoteTree();
                    // 一口气把几千个音符的 UI 控件全部建好放进内存缓存中
                    // 刷新音符列表的显示
                    if (_currentChart.note_list.Count > 0)
                    {
                        // 计算最大时间，并赋值给 NoteList 里的公开变量！
                        NoteList._maxChartTime = _currentEngine.TickToSeconds(_currentChart.note_list.Max(n => n.tick));
                    }
                    NoteList.RefreshNoteList();

                    string bpmText = ChartLogic.GetBpmText(chart.tempo_list);

                    // 弹出报告！
                    MessageBox.Show(
                        $"成功解密古代卷轴！(≧∇≦)ﾉ\n\n" +
                        $"🎵 音符总数：{chart.note_list.Count} 个\n" +
                        $"📄 谱面页数：{chart.page_list.Count} 页\n" +
                        $"⏱️ 歌曲 BPM：{bpmText}",
                        "情报解析成功");

                    
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"解析发生爆炸啦 QAQ：\n{ex.Message}", "系统报错");
                }
            }

            TriggerAutoLinkIfReady();
        }
        // 🌟 调度中心：只要谱面和故事板都到位了，就尝试触发引擎！
        private void TriggerAutoLinkIfReady()
        {
            if (_currentChart != null && _currentStoryboardRoot != null)
            {
                // 跨服呼叫联姻引擎！把需要的参数全部分发给它
                ChartStoryboardLink.TryTriggerAutoLink(
                    _currentChart,
                    _currentStoryboardRoot,
                    _currentEngine,
                    EventList.NoteCtrlTreeView,
                    EventList.UpdateEmptyHintVisibility); // 让子控件公开它的刷新方法
            }
        }

    }

}