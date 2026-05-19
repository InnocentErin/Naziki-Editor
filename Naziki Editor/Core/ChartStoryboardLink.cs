using Naziki_Editor.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Naziki_Editor.Core
{
    // 🏛️ 专职的联姻服务机构：不碰 UI，不碰纯谱面计算，只负责两者之间的协调！
    public static class ChartStoryboardLink
    {
        // ==========================================
        // 💍 核心机制：尝试触发谱面与故事板的联姻
        // ==========================================
        // 🌟 修复：参数类型从 TreeView 改为 ListBox
        public static void TryTriggerAutoLink(
            C2Chart chart,
            StoryboardRoot storyboardRoot,
            ChartTimeEngine engine,
            ListBox noteCtrlListBox,
            Action updateEmptyHintAction)
        {
            if (chart == null || storyboardRoot == null) return;
            if (storyboardRoot.note_controllers == null || storyboardRoot.note_controllers.Count == 0) return;

            var result = MessageBox.Show(
                "检测到谱面与故事板均已就位！✨\n是否让故事板的音符控制器与谱面文件自动配对？\n(做出选择前，一定要确定这个故事板是基于你所上传的谱面文件制作的哦！)",
                "自动配对询问",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                ExecuteAutoLink(chart, storyboardRoot, engine, noteCtrlListBox, updateEmptyHintAction);
            }
        }

        // ==========================================
        // 💍 核心机制：执行联姻 UI 替换（ListBox 扁平降维版！）
        // ==========================================
        private static void ExecuteAutoLink(
            C2Chart chart,
            StoryboardRoot storyboardRoot,
            ChartTimeEngine engine,
            ListBox noteCtrlListBox, // 🌟 修复：接收 ListBox
            Action updateEmptyHintAction)
        {
            // 1. 🛡️ 向逻辑兵工厂索要体检报告！
            string errorMsg;
            bool isSafe = CheckChartStoryboardLinkValidity(chart, storyboardRoot, out errorMsg);

            if (!isSafe)
            {
                MessageBox.Show(errorMsg, "跨服警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. 🪄 体检通过！UI 专职负责华丽大变身！
            noteCtrlListBox.Items.Clear();

            // 🌟 扁平化：不再需要 Folder 文件夹了，直接把音符控制器拍扁塞进去！
            foreach (var ctrl in storyboardRoot.note_controllers)
            {
                if (ctrl.NoteTarget == null) continue;

                // 情况 A：它是普通的数字 ID
                if (ctrl.NoteTarget is long || ctrl.NoteTarget is int || long.TryParse(ctrl.NoteTarget.ToString(), out _))
                {
                    int targetId = Convert.ToInt32(ctrl.NoteTarget);
                    var matchedNote = chart.note_list.FirstOrDefault(n => n.id == targetId);

                    if (matchedNote != null)
                    {
                        // 🌟 制造 ListBoxItem，并扣上 Tag！
                        var item = new ListBoxItem() { Tag = ctrl };
                        item.SetBinding(ListBoxItem.ContentProperty, new System.Windows.Data.Binding("DisplayName") { Source = ctrl });
                        noteCtrlListBox.Items.Add(item);
                    }
                    else
                    {
                        var item = new ListBoxItem() { Content = $"Note ID: {targetId} (谱面未命中)", Foreground = Brushes.Gray, Tag = ctrl };
                        noteCtrlListBox.Items.Add(item);
                    }
                }
                // 情况 B：它是强类型的选择器对象
                else if (ctrl.NoteTarget is Newtonsoft.Json.Linq.JObject jobj)
                {
                    try
                    {
                        var item = new ListBoxItem() { Tag = ctrl, Foreground = Brushes.DarkCyan, FontWeight = FontWeights.Bold };
                        item.SetBinding(ListBoxItem.ContentProperty, new System.Windows.Data.Binding("DisplayName") { Source = ctrl });
                        noteCtrlListBox.Items.Add(item);
                    }
                    catch
                    {
                        noteCtrlListBox.Items.Add(new ListBoxItem() { Content = $"未知选择器", Tag = ctrl });
                    }
                }
            }

            // 远程呼叫主窗体刷新“空空如也”
            updateEmptyHintAction?.Invoke();

            MessageBox.Show("自动联姻成功！音符事件已与谱面数据完美挂钩！", "配对成功");
        }

        // ==========================================
        // 🔮 核心业务：联姻体检（自动配对越界检查）
        // ==========================================
        public static bool CheckChartStoryboardLinkValidity(C2Chart chart, StoryboardRoot storyboard, out string errorMessage)
        {
            errorMessage = string.Empty;

            int maxChartId = chart.note_list.Count > 0 ? chart.note_list.Max(n => n.id) : -1;

            long maxStoryboardId = -1;
            foreach (var ctrl in storyboard.note_controllers)
            {
                if (ctrl.NoteTarget != null)
                {
                    if (ctrl.NoteTarget is long l) maxStoryboardId = System.Math.Max(maxStoryboardId, l);
                    else if (ctrl.NoteTarget is int i) maxStoryboardId = System.Math.Max(maxStoryboardId, i);
                    else if (long.TryParse(ctrl.NoteTarget.ToString(), out long parsed)) maxStoryboardId = System.Math.Max(maxStoryboardId, parsed);
                }
            }

            if (maxStoryboardId > maxChartId)
            {
                errorMessage = "发现异常！故事板中引用的最大音符 ID 超出了当前谱面的最大音符 ID。(°ロ°)\n该故事板可能与该谱面不匹配，配对操作已中止。";
                return false;
            }

            return true;
        }
    }
}