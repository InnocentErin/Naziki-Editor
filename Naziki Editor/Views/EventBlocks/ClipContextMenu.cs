using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Models;
using Naziki_Editor.UI.ViewModels;
using Naziki_Editor.State;
using System;

namespace Naziki_Editor.Views.EventBlocks
{
    /// <summary>
    /// Builds and handles the right-click context menu for EventBlockControl.
    /// </summary>
    public static class ClipContextMenu
    {
        /// <summary>
        /// Handle the "Reanchor to nearest note" menu action.
        /// </summary>
        public static void HandleReanchor(EventBlockViewModel model, ProjectDataContext context, IDialogService dialogService)
        {
            if (context == null || !context.HasChart || model?.AssociatedObject == null)
            {
                dialogService.ShowMessage("纳尼？！必须先在主界面加载对应的谱面文件才能触发锚定雷达哦！", "重算失败");
                return;
            }

            string newExpression = TimelineAnchorEngine.CalculateNearestAnchorExpression(
                model.StartTime,
                context.Chart.note_list,
                context.TimeEngine,
                out C2Note nearestNote,
                out double offset);

            if (newExpression != null && nearestNote != null)
            {
                var baseState = model.AssociatedObject.GetBaseState();
                if (baseState != null)
                {
                    var timeProp = baseState.GetType().GetProperty("Time");
                    if (timeProp != null)
                    {
                        timeProp.SetValue(baseState, newExpression);
                        dialogService.ShowMessage($"✨ 自动吸附配对成功！\\n\\n方块已被精准绑定至 [Note ID: {nearestNote.id}]\\n时间轴新表达式: {newExpression}", "时空锚定完毕");
                        context.MarkAsModified();
                    }
                }
            }
        }

        /// <summary>
        /// Handle the "Destroy at last frame" menu action.
        /// </summary>
        public static void HandleDestroyAtLastFrame(EventBlockViewModel model, ProjectDataContext context, IMessageBroker messageBroker)
        {
            if (model?.AssociatedObject == null) return;
            var kfs = model.AssociatedObject.GetKeyframes();
            if (kfs == null || kfs.Count == 0) return;

            var lastFrame = kfs[kfs.Count - 1];
            var propInfo = lastFrame.GetType().GetProperty("Destroy");

            if (propInfo != null && propInfo.CanWrite)
            {
                Type t = propInfo.PropertyType;
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>)) t = Nullable.GetUnderlyingType(t);
                propInfo.SetValue(lastFrame, Convert.ChangeType(true, t));

                context?.MarkAsModified();
                messageBroker.Publish("RefreshTimeline");
            }
        }
    }
}


