using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using Newtonsoft.Json;
using Naziki_Editor.Models;
using Naziki_Editor.Core;
using Naziki_Editor.State;
using System.Linq;

namespace Naziki_Editor.Views
{
    public partial class CanvasControl : UserControl
    {
        public Action<StoryboardRoot> OnApplyJsonSuccess;
        public Func<bool> OnBeforeActionCheckConflict;

        private bool _isRefreshing = false;
        public bool HasUnappliedChanges { get; set; } = false;
        private object _lastSelectedObject;
        private bool _isGlobalPreviewMode = false;
        public ProjectDataContext Context { get; private set; }

        public void LoadContext(ProjectDataContext context) => Context = context;

        private void BtnPreviewGlobal_Click(object sender, RoutedEventArgs e)
        {
            _isGlobalPreviewMode = true;
            _lastSelectedObject = null;
            RefreshJsonView();
        }

        public CanvasControl()
        {
            InitializeComponent();
            JsonEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("JavaScript");
        }

        public bool IsJsonTabActive
        {
            get
            {
                if (JsonEditor == null) return false;
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
                if (selectedTab.Header.ToString() == "检查编辑后的 JSON 源代码" && !HasUnappliedChanges)
                {
                    RefreshJsonView();
                }
            }
        }

        public void RefreshJsonView()
        {
            if (JsonEditor == null) return;
            var currentModel = Context?.Storyboard;
            if (currentModel == null) return;

            try
            {
                _isRefreshing = true;
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
                HasUnappliedChanges = false;
                TxtJsonStatus.Text = "✅ 代码已刷新为最新状态。";
                TxtJsonStatus.Foreground = new SolidColorBrush(Colors.LightGreen);
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

        private void BtnApplyJson_Click(object sender, RoutedEventArgs e)
        {
            if (OnBeforeActionCheckConflict != null && !OnBeforeActionCheckConflict()) return;
            ForceApplyJson();
        }

        public bool ForceApplyJson()
        {
            try
            {
                var root = Context?.Storyboard;
                if (_isGlobalPreviewMode)
                {
                    var newRoot = JsonConvert.DeserializeObject<StoryboardRoot>(JsonEditor.Text, StoryboardSerializer.GetSettings());
                    if (newRoot == null) throw new Exception("解析结果为空！");
                    OnApplyJsonSuccess?.Invoke(newRoot);
                }
                else if (_lastSelectedObject != null)
                {
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

        private void JsonEditor_TextChanged(object sender, EventArgs e)
        {
            if (_isRefreshing) return;
            HasUnappliedChanges = true;
            TxtJsonStatus.Text = "⚠️ 源代码已修改，尚未应用到内存！(冲突保护中)";
            TxtJsonStatus.Foreground = new SolidColorBrush(Colors.Orange);
        }

        public void TrackSelectedObject(object obj)
        {
            _lastSelectedObject = obj;
            _isGlobalPreviewMode = false;

            if (CanvasTabControl != null && CanvasTabControl.SelectedIndex == 1) RefreshJsonView();
            else if (JsonEditor.IsVisible) ExecuteRadarJump(obj);
        }

        // ==========================================
        // 🔍 全频段星际智能雷达跃迁系统
        // ==========================================
        private void ExecuteRadarJump(object obj)
        {
            string searchKey = null;
            int searchStartIndex = 0;

            // ✨ 终极重写：雷达追踪机制全线接入 C2 分离架构
            if (obj is IStoryboardEntity sbObj && !string.IsNullOrEmpty(sbObj.Id))
            {
                searchKey = $"\"id\": \"{sbObj.Id}\"";
            }
            else if (obj is C2Sprite sprite && !string.IsNullOrEmpty(sprite.BaseState?.Path))
            {
                searchKey = $"\"path\": \"{sprite.BaseState.Path}\"";
                searchStartIndex = Math.Max(0, JsonEditor.Text.IndexOf("\"sprites\":"));
            }
            else if (obj is C2Video video && !string.IsNullOrEmpty(video.BaseState?.Path))
            {
                searchKey = $"\"path\": \"{video.BaseState.Path}\"";
                searchStartIndex = Math.Max(0, JsonEditor.Text.IndexOf("\"videos\":"));
            }
            else if (obj is C2NoteController noteCtrl && noteCtrl.BaseState?.NoteTarget != null)
            {
                var target = noteCtrl.BaseState.NoteTarget;
                searchKey = target is Newtonsoft.Json.Linq.JObject ? "\"note\": {" : $"\"note\": {target}";
                searchStartIndex = Math.Max(0, JsonEditor.Text.IndexOf("\"note_controllers\":"));
            }
            else if (obj is C2SceneController ctrl)
            {
                // 场景控制器嗅探：优先找动画首帧时间，无则默认为 0 帧
                string targetTime = ctrl.Keyframes?.Count > 0 ? ctrl.Keyframes[0].Time?.ToString() : "0";
                searchKey = $"\"time\": {targetTime}";
                searchStartIndex = Math.Max(0, JsonEditor.Text.IndexOf("\"controllers\":"));
            }

            if (!string.IsNullOrEmpty(searchKey))
            {
                int index = JsonEditor.Text.IndexOf(searchKey, searchStartIndex);
                if (index < 0 && searchStartIndex > 0) index = JsonEditor.Text.IndexOf(searchKey);

                if (index >= 0)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (JsonEditor.Document == null || index >= JsonEditor.Document.TextLength) return;

                        JsonEditor.Focus();
                        var documentLine = JsonEditor.Document.GetLineByOffset(index);
                        JsonEditor.ScrollToLine(documentLine.LineNumber);
                        JsonEditor.TextArea.Caret.Line = documentLine.LineNumber;
                        JsonEditor.TextArea.Caret.BringCaretToView();
                        JsonEditor.Select(index, searchKey.Length);
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }
    }
}