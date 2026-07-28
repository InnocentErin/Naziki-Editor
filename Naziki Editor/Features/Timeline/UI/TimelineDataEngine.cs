using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Models;
using Naziki_Editor.UI.ViewModels;
using Naziki_Editor.State;
using Naziki_Editor.Core.Timeline.Projection;

namespace Naziki_Editor.UI.Services
{
    public class TimelineDataEngine
    {
        private readonly ITimelineProjectionService _projectionService;

        public TimelineDataEngine()
            : this(new TimelineProjectionService())
        {
        }

        public TimelineDataEngine(ITimelineProjectionService projectionService)
        {
            _projectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
        }
        // =========================================================================
        // 🌍 形态 A：全景主时间轴（中心辐射宏观排版模型生成器）
        // =========================================================================
        public ObservableCollection<MainTimelineGroupViewModel> BuildMainTimeline(ProjectDataContext context)
        {
            var groups = new ObservableCollection<MainTimelineGroupViewModel>();
            var root = context?.Storyboard;

            // 1. 🏗️ 初始化中心辐射的各大常驻星环 (GroupIndex 决定上下物理位置)
            // ✨ 初始全部设为 false，稍后在收纳逻辑里会强行展开 Layer0 和有数据的组
            var layer2 = new MainTimelineGroupViewModel { GroupName = "📦 物理图层 - Layer 2 (前景/UI)", GroupIndex = 30, IsExpanded = false };
            var layer1 = new MainTimelineGroupViewModel { GroupName = "📦 物理图层 - Layer 1 (中景/游玩)", GroupIndex = 20, IsExpanded = false };
            var layer0 = new MainTimelineGroupViewModel { GroupName = "📦 物理图层 - Layer 0 (背景)", GroupIndex = 10, IsExpanded = true };

            // 🎛️ 控制器在音频轨之下，所以给负数！(越往下负得越多)
            var controllerGroup = new MainTimelineGroupViewModel { GroupName = "🎛️ 画面滤镜与场景控制器", GroupIndex = -10, IsExpanded = false };
            var noteGroup = new MainTimelineGroupViewModel { GroupName = "🎵 音符轨迹控制器", GroupIndex = -20, IsExpanded = false };

            // 🌟 核心满足大大需求：为每个轨道组【强行塞入一条保底空轨道】！
            layer2.Tracks.Add(new MainTimelineTrackViewModel { TrackIndex = 0, TrackName = "Order 0" });
            layer1.Tracks.Add(new MainTimelineTrackViewModel { TrackIndex = 0, TrackName = "Order 0" });
            layer0.Tracks.Add(new MainTimelineTrackViewModel { TrackIndex = 0, TrackName = "Order 0" });
            controllerGroup.Tracks.Add(new MainTimelineTrackViewModel { TrackIndex = 0, TrackName = "默认控制轨" });
            noteGroup.Tracks.Add(new MainTimelineTrackViewModel { TrackIndex = 0, TrackName = "默认音符轨" });

            groups.Add(layer2);
            groups.Add(layer1);
            groups.Add(layer0);
            groups.Add(controllerGroup);
            groups.Add(noteGroup);

            // 就算宇宙是空的，我们也把这些空房间返回给 UI 渲染！
            if (root == null) return groups;

            // 2. 🧾 全量扫盘所有普通实体
            var allEntities = new List<IStoryboardEntity>();
            if (root.sprites != null) allEntities.AddRange(root.sprites);
            if (root.texts != null) allEntities.AddRange(root.texts);
            if (root.videos != null) allEntities.AddRange(root.videos);
            if (root.lines != null) allEntities.AddRange(root.lines);

            foreach (var entity in allEntities)
            {
                var baseState = entity.GetBaseState();
                if (baseState == null) continue;

                int layerVal = 0, orderVal = 0;
                if (FastReflectionHelper.TryGetValue(baseState, "Layer", out object lObj)) layerVal = Convert.ToInt32(lObj);
                if (FastReflectionHelper.TryGetValue(baseState, "Order", out object oObj)) orderVal = Convert.ToInt32(oObj);

                MainTimelineGroupViewModel targetGroup = layer0;
                if (layerVal == 1) targetGroup = layer1;
                else if (layerVal >= 2) targetGroup = layer2;

                var track = targetGroup.Tracks.FirstOrDefault(t => t.TrackIndex == orderVal);
                if (track == null)
                {
                    track = new MainTimelineTrackViewModel { TrackIndex = orderVal, TrackName = $"Order {orderVal}" };
                    targetGroup.Tracks.Add(track);
                }
                track.Clips.Add(CreateClipFromEntity(entity, orderVal, context)); // 👈 这里叫 orderVal
            }

            // 3. 🎛️ 控制板和音符独立编排 (智能占领空轨)
            if (root.controllers != null)
            {
                foreach (var ctrl in root.controllers)
                {
                    // 尝试寻找那个没人用的"默认控制轨"，直接雀占鸠巢！
                    var track = controllerGroup.Tracks.FirstOrDefault(t => t.TrackName == "默认控制轨" && t.Clips.Count == 0);
                    if (track != null) { track.TrackName = ctrl.Id ?? "Unknown"; }
                    else
                    {
                        track = new MainTimelineTrackViewModel { TrackIndex = controllerGroup.Tracks.Count, TrackName = ctrl.Id ?? "Unknown" };
                        controllerGroup.Tracks.Add(track);
                    }
                    track.Clips.Add(CreateClipFromEntity(ctrl, track.TrackIndex, context)); // 👈 这里叫 track.TrackIndex
                }
            }

            if (root.note_controllers != null)
            {
                foreach (var noteCtrl in root.note_controllers)
                {
                    var track = noteGroup.Tracks.FirstOrDefault(t => t.TrackName == "默认音符轨" && t.Clips.Count == 0);
                    if (track != null) { track.TrackName = noteCtrl.Id ?? "Unknown"; }
                    else
                    {
                        track = new MainTimelineTrackViewModel { TrackIndex = noteGroup.Tracks.Count, TrackName = noteCtrl.Id ?? "Unknown" };
                        noteGroup.Tracks.Add(track);
                    }
                    track.Clips.Add(CreateClipFromEntity(noteCtrl, track.TrackIndex, context)); // 👈 这里叫 track.TrackIndex
                }
            }

            // 4. 🧹 智能收纳 2.0 版：现在判断折叠的标准，是看轨道里【有没有装方块 (Clips)】！
            foreach (var group in groups)
            {
                bool hasAnyClip = group.Tracks.Any(t => t.Clips.Count > 0);

                // 只要不是指定的"主角层 (Layer 0)"，且里面连一个方块都没有，就乖乖折叠起来！
                if (!hasAnyClip && group != layer0)
                {
                    group.IsExpanded = false;
                }
                else
                {
                    group.IsExpanded = true;
                }
            }

            return groups;
        }

        [Obsolete("Use BuildMainTimeline.")]
        public ObservableCollection<MainTimelineGroupViewModel> BuildMacroTimeline(ProjectDataContext context)
            => BuildMainTimeline(context);

        // =========================================================================
        // 🛠️ 纯净方块胶囊生成器
        // =========================================================================
        private EventBlockViewModel CreateClipFromEntity(IStoryboardEntity entity, int trackIndex, ProjectDataContext context)
        {
            var projection = _projectionService.BuildEntityProjection(entity, context);
            return new EventBlockViewModel
            {
                AssociatedObject = entity,
                DisplayName = EventNameResolver.GetDisplayName(entity),
                TrackIndex = trackIndex,
                StartTime = projection.BaseStateTime,
                EndTime = projection.LastStateTime,
                HasTimeError = !projection.HasValidBaseTime || projection.HasErrors,
                TimeDiagnostics = projection.Diagnostics.Select(d => d.Message).ToArray()
            };
        }



    }
}

