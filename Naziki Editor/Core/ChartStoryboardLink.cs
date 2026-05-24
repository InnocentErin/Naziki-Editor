using Naziki_Editor.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Naziki_Editor.Core
{
    // ==========================================
    // 🏛️ 专职的联姻服务机构 (已完美匹配 C2 分离架构)
    // ==========================================
    public static class ChartStoryboardLink
    {
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

        private static void ExecuteAutoLink(
            C2Chart chart,
            StoryboardRoot storyboardRoot,
            ChartTimeEngine engine,
            ListBox noteCtrlListBox,
            Action updateEmptyHintAction)
        {
            string errorMsg;
            bool isSafe = CheckChartStoryboardLinkValidity(chart, storyboardRoot, out errorMsg);

            if (!isSafe)
            {
                MessageBox.Show(errorMsg, "跨服警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            noteCtrlListBox.Items.Clear();

            foreach (var ctrl in storyboardRoot.note_controllers)
            {
                // ✨ 核心修正：从 BaseState 肚子里挖出绑定的音符目标！
                if (ctrl.BaseState?.NoteTarget == null) continue;

                var target = ctrl.BaseState.NoteTarget;

                // 情况 A：普通的数字 ID
                if (target is long || target is int || long.TryParse(target.ToString(), out _))
                {
                    int targetId = Convert.ToInt32(target);
                    var matchedNote = chart.note_list.FirstOrDefault(n => n.id == targetId);

                    if (matchedNote != null)
                    {
                        var item = new ListBoxItem() { Tag = ctrl };
                        item.SetBinding(ListBoxItem.ContentProperty, new System.Windows.Data.Binding("Id") { Source = ctrl });
                        noteCtrlListBox.Items.Add(item);
                    }
                    else
                    {
                        var item = new ListBoxItem() { Content = $"{ctrl.Id} | Note ID: {targetId} (谱面未命中)", Foreground = Brushes.Gray, Tag = ctrl };
                        noteCtrlListBox.Items.Add(item);
                    }
                }
                // 情况 B：强类型的选择器 JSON 对象
                else if (target is Newtonsoft.Json.Linq.JObject jobj)
                {
                    try
                    {
                        var item = new ListBoxItem() { Tag = ctrl, Foreground = Brushes.DarkCyan, FontWeight = FontWeights.Bold };
                        item.SetBinding(ListBoxItem.ContentProperty, new System.Windows.Data.Binding("Id") { Source = ctrl });
                        noteCtrlListBox.Items.Add(item);
                    }
                    catch
                    {
                        noteCtrlListBox.Items.Add(new ListBoxItem() { Content = $"{ctrl.Id} | 未知选择器", Tag = ctrl });
                    }
                }
            }

            updateEmptyHintAction?.Invoke();
            MessageBox.Show("自动联姻成功！音符事件已与谱面数据完美挂钩！", "配对成功");
        }

        public static bool CheckChartStoryboardLinkValidity(C2Chart chart, StoryboardRoot storyboard, out string errorMessage)
        {
            errorMessage = string.Empty;
            int maxChartId = chart.note_list.Count > 0 ? chart.note_list.Max(n => n.id) : -1;
            long maxStoryboardId = -1;

            foreach (var ctrl in storyboard.note_controllers)
            {
                // ✨ 核心修正：联动检查反查路径升级
                if (ctrl.BaseState?.NoteTarget != null)
                {
                    var target = ctrl.BaseState.NoteTarget;
                    if (target is long l) maxStoryboardId = System.Math.Max(maxStoryboardId, l);
                    else if (target is int i) maxStoryboardId = System.Math.Max(maxStoryboardId, i);
                    else if (long.TryParse(target.ToString(), out long parsed)) maxStoryboardId = System.Math.Max(maxStoryboardId, parsed);
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