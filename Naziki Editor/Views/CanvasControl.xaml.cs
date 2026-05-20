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

        private bool _isGlobalPreviewMode = false;
        // 在类最上方定义全局开关，并新增按钮事件
        private void BtnPreviewGlobal_Click(object sender, RoutedEventArgs e)
        {
            _isGlobalPreviewMode = true;
            _lastSelectedObject = null;
            RefreshJsonView();
        }

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
            if (JsonEditor == null) return;
            var currentModel = RequestCurrentStoryboardRoot?.Invoke();
            if (currentModel == null) return;

            try
            {
                _isRefreshing = true;
                // 🌟 全局预览模式：不管选中什么，都直接展示整个故事板的 JSON，且隐藏“未选中提示”。
                if (_isGlobalPreviewMode)
                {
                    NoSelectionHint.Visibility = Visibility.Collapsed;
                    JsonEditor.Visibility = Visibility.Visible;
                    JsonEditor.Text = StoryboardSerializer.ToJson(currentModel);
                }
                else if (_lastSelectedObject == null)
                {
                    NoSelectionHint.Visibility = Visibility.Visible;
                    JsonEditor.Visibility = Visibility.Collapsed;
                    JsonEditor.Text = "";
                }
                else
                {
                    NoSelectionHint.Visibility = Visibility.Collapsed;
                    JsonEditor.Visibility = Visibility.Visible;
                    JsonEditor.Text = StoryboardSerializer.ToJson(_lastSelectedObject);
                }
                // 刷新完毕，重置状态
                HasUnappliedChanges = false;
                TxtJsonStatus.Text = "✅ 代码已刷新为最新状态。";
                TxtJsonStatus.Foreground = new SolidColorBrush(Colors.LightGreen);
                // 🌟 修复：刷新后如果不是全局预览模式，才执行雷达跳转！避免全局模式下的频繁跳跃引发崩溃。
                if (_lastSelectedObject != null && !_isGlobalPreviewMode)
                    ExecuteRadarJump(_lastSelectedObject);
            }
            catch (Exception ex)
            {
                JsonEditor.Visibility = Visibility.Visible;
                NoSelectionHint.Visibility = Visibility.Collapsed;
                JsonEditor.Text = "// 序列化异常: " + ex.Message;
            }
            finally { _isRefreshing = false; }
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
                var root = RequestCurrentStoryboardRoot?.Invoke();

                if (_isGlobalPreviewMode)
                {
                    // 🌍 全局模式：重塑整个宇宙
                    var newRoot = JsonConvert.DeserializeObject<StoryboardRoot>(JsonEditor.Text);
                    if (newRoot == null) throw new Exception("解析结果为空！");
                    OnApplyJsonSuccess?.Invoke(newRoot);
                }
                else if (_lastSelectedObject != null)
                {
                    // 🎯 局部模式：直接把修改像“打点滴”一样灌入内存中的单体对象！
                    JsonConvert.PopulateObject(JsonEditor.Text, _lastSelectedObject);
                    OnApplyJsonSuccess?.Invoke(root);
                }

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
            _isGlobalPreviewMode = false; // ✨ 只要点击了列表，立刻退出全局模式

            // 🌟 修复：如果当前停留在源代码页面，直接交给 RefreshJsonView 刷新，它刷新后会自动呼叫雷达。
            // 绝对不能先跳跃再刷新，否则会引发时空崩溃！
            if (CanvasTabControl != null && CanvasTabControl.SelectedIndex == 1)
            {
                RefreshJsonView();
            }
            else if (JsonEditor.IsVisible)
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

                        // 🛡️ 绝对防御盾：在执行跳转前，确认文本没有被瞬间缩短或清空！
                        if (JsonEditor.Document == null || index >= JsonEditor.Document.TextLength) return;

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