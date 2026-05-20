using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using Newtonsoft.Json;
using Naziki_Editor.Models;
using Naziki_Editor.Core;

namespace Naziki_Editor.Views
{
    public partial class CanvasControl : UserControl
    {
        // 📡 跨房连线 1：向主窗口索要当前最新数据
        public Func<StoryboardRoot> RequestCurrentStoryboardRoot;
        // 📡 跨房连线 2：修改成功后，通知主窗口全局刷新！
        public Action<StoryboardRoot> OnApplyJsonSuccess;
        // 📡 跨房连线 3：核心安检闸门！操作前询问大本营是否有双向冲突，若返回 false 则立刻中止
        public Func<bool> OnBeforeActionCheckConflict;

        // 🌟 状态锁与标记：用来区分是“系统刷新”还是“用户手动打字”
        private bool _isRefreshing = false;
        public bool HasUnappliedChanges { get; set; } = false;

        // 雷达缓存：记录主窗口当前选中的对象
        private object _lastSelectedObject;

        public CanvasControl()
        {
            InitializeComponent();

            // 为代码编辑器穿上接近 JSON 的语法高亮外衣
            JsonEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("JavaScript");
        }

        // 判断当前是不是正停留在 JSON 源码抽屉页
        public bool IsJsonTabActive
        {
            get
            {
                if (JsonEditor == null) return false;
                // 找到父级 TabControl 并看它的选中索引
                var parent = VisualTreeHelper.GetParent(this);
                while (parent != null && !(parent is TabControl)) parent = VisualTreeHelper.GetParent(parent);
                if (parent is TabControl tc) return tc.SelectedIndex == 1;
                return false;
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tabControl && tabControl.SelectedItem is TabItem selectedTab)
            {
                if (selectedTab.Header.ToString() == "检查编辑后的 JSON 源代码")
                {
                    // 只有在“两边都没发生冲突”或者“JSON这边没藏着脏改动”时，切过来才强制刷新
                    if (!HasUnappliedChanges)
                    {
                        RefreshJsonView();
                    }
                }
            }
        }

        // ==========================================
        // 📥 刷新视图并执行雷达定位 (公开强制刷新)
        // ==========================================
        public void RefreshJsonView()
        {
            _isRefreshing = true;

            if (JsonEditor == null) return;

            var currentModel = RequestCurrentStoryboardRoot?.Invoke();
            if (currentModel == null)
            {
                JsonEditor.Text = "{\n  \"提示\": \"当前无数据\"\n}";
                return;
            }

            try
            {
                _isRefreshing = true; // 开启系统保护锁，防止触发变脏监控

                // ✅ 全新官方蛇形大一统写法：隐藏 null，自动转换为小写蛇形命名！
                JsonEditor.Text = StoryboardSerializer.ToJson(_lastSelectedObject);
                HasUnappliedChanges = false; // 重置本端脏标记

                TxtJsonStatus.Text = "✅ 代码已刷新为最新状态。";
                TxtJsonStatus.Foreground = new SolidColorBrush(Colors.LightGreen);

                if (_lastSelectedObject != null)
                {
                    ExecuteRadarJump(_lastSelectedObject);
                }
            }
            catch (Exception ex)
            {
                JsonEditor.Text = "// 序列化异常: " + ex.Message;
            }
            finally
            {
                _isRefreshing = false; // 解除保护锁
            }
        }

        // ==========================================
        // 🚀 新一代安检应用入口
        // ==========================================
        private void BtnApplyJson_Click(object sender, RoutedEventArgs e)
        {
            // 🛑 询问最高统帅部：两边同时改了吗？发生冲突需要处理吗？
            if (OnBeforeActionCheckConflict != null && !OnBeforeActionCheckConflict())
            {
                return; // 统帅部拒绝了该行为，立刻刹车！
            }

            // 无冲突，或是仲裁直接放行，执行强制写入！
            ForceApplyJson();
        }

        // ==========================================
        // 🔮 提取出的独立强制解析法术
        // ==========================================
        public bool ForceApplyJson()
        {
            try
            {
                var newRoot = JsonConvert.DeserializeObject<StoryboardRoot>(JsonEditor.Text);
                if (newRoot == null) throw new Exception("解析结果为空！");

                OnApplyJsonSuccess?.Invoke(newRoot);

                HasUnappliedChanges = false;
                TxtJsonStatus.Text = "🎉 应用成功！事件列表与属性面板已同步。";
                TxtJsonStatus.Foreground = new SolidColorBrush(Colors.LightGreen);
                return true;
            }
            catch (Exception ex)
            {
                TxtJsonStatus.Text = "❌ 语法错误，应用失败: " + ex.Message;
                TxtJsonStatus.Foreground = new SolidColorBrush(Colors.OrangeRed);
                return false;
            }
        }

        // ==========================================
        // ⌨️ 键盘敲击雷达：实时捕获 JSON 被改动
        // ==========================================
        private void JsonEditor_TextChanged(object sender, EventArgs e)
        {
            if (_isRefreshing) return; // 系统自己在刷，直接无视

            HasUnappliedChanges = true; // 抓到你啦！标记 JSON 已变脏！
            TxtJsonStatus.Text = "⚠️ 源代码已修改，尚未应用到内存！(冲突保护中)";
            TxtJsonStatus.Foreground = new SolidColorBrush(Colors.Orange);
        }

        // ==========================================
        // 🎯 接收主窗口发来的定位坐标
        // ==========================================
        public void TrackSelectedObject(object obj)
        {
            _lastSelectedObject = obj;
            if (JsonEditor.IsVisible)
            {
                ExecuteRadarJump(obj);
            }
        }

        // ==========================================
        // 🔍 雷达追踪法术底层实现 (全频段智能嗅探版！)
        // ==========================================
        private void ExecuteRadarJump(object obj)
        {
            string searchKey = null;
            int searchStartIndex = 0; // 区域限定器，防止同名属性找错地方！

            // 1. 🎯 优先通过 ID 定位（如果对象有 ID，这是最准的）
            if (obj is StoryboardObject sbObj && !string.IsNullOrEmpty(sbObj.Id))
            {
                searchKey = $"\"id\": \"{sbObj.Id}\"";
            }
            // 2. 🖼️ 图片对象：如果没 ID，靠 path 找，且限定在 sprites 区！
            else if (obj is Sprite sprite && !string.IsNullOrEmpty(sprite.States[0].Path))
            {
                searchKey = $"\"path\": \"{sprite.States[0].Path}\"";
                searchStartIndex = Math.Max(0, JsonEditor.Text.IndexOf("\"sprites\":"));
            }
            // 3. 🎬 视频对象：如果没 ID，靠 path 找，且限定在 videos 区！
            else if (obj is Video video && !string.IsNullOrEmpty(video.States[0].Path))
            {
                searchKey = $"\"path\": \"{video.States[0].Path}\"";
                searchStartIndex = Math.Max(0, JsonEditor.Text.IndexOf("\"videos\":"));
            }
            // 4. 🎵 音符控制器定位
            else if (obj is NoteController noteCtrl && noteCtrl.NoteTarget != null)
            {
                if (noteCtrl.NoteTarget is Newtonsoft.Json.Linq.JObject)
                    searchKey = "\"note\": {";
                else
                    searchKey = $"\"note\": {noteCtrl.NoteTarget}";

                searchStartIndex = Math.Max(0, JsonEditor.Text.IndexOf("\"note_controllers\":"));
            }
            // 5. 🎯 场景控制器定位（通过触发时间嗅探，且严格限制在 controllers 数组内寻找！）
            else if (obj is Controller ctrl && ctrl.States != null && ctrl.States.Count > 0)
            {
                // 🌟 终极修正：去 States 列表里拿第 0 个状态的 Time！
                searchKey = $"\"time\": {ctrl.States[0].Time}";
                searchStartIndex = Math.Max(0, JsonEditor.Text.IndexOf("\"controllers\":"));
            }

            // 🚀 执行瞬间跃迁！
            if (!string.IsNullOrEmpty(searchKey))
            {
                // 在限定区域内搜寻目标
                int index = JsonEditor.Text.IndexOf(searchKey, searchStartIndex);

                // 兼容性抢救：万一是没排版的特殊情况，放宽条件全局再找一次
                if (index < 0 && searchStartIndex > 0)
                {
                    index = JsonEditor.Text.IndexOf(searchKey);
                }

                if (index >= 0)
                {
                    // 🌟 核心修复：使用 Dispatcher 延迟执行焦点夺取！
                    // 这会让 TreeView 先安安稳稳地把“选中冒泡”处理完，然后再执行我们的代码跳转
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        JsonEditor.Focus(); // 此时夺取焦点，就不会干扰左侧的树了
                        var documentLine = JsonEditor.Document.GetLineByOffset(index);

                        // 将视口优雅地滚动并居中该行
                        JsonEditor.ScrollToLine(documentLine.LineNumber);
                        JsonEditor.TextArea.Caret.Line = documentLine.LineNumber;
                        JsonEditor.TextArea.Caret.BringCaretToView();

                        // 华丽地高亮选中文本！
                        JsonEditor.Select(index, searchKey.Length);

                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }
    }
}