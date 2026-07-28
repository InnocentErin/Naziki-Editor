using System;
using System.Collections.Generic;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Timeline.Abstractions;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Naziki_Editor.Core.Timeline.Projection;

namespace Naziki_Editor.Core.Timeline.Services
{
    /// <summary>
    /// 微观时光屋服务实现，负责单实体属性关键帧解码、视觉时间反写与时间/坐标换算。
    /// </summary>
    public class MicroTimelineService : IMicroTimelineService
    {
        private ProjectDataContext _context;
        private readonly TimelineCoordEngine _coordEngine;

        public MicroTimelineService(TimelineCoordEngine coordEngine)
        {
            _coordEngine = coordEngine;
        }

        /// <summary>
        /// 设置当前项目上下文（在加载项目后调用）。
        /// </summary>
        public void SetContext(ProjectDataContext context)
        {
            _context = context;
        }

        public List<DecodedKeyframeBox> DecodeKeyframes(
            IStoryboardEntity entity,
            string propertyName,
            double clipStartTime)
        {
            if (_context == null) return new List<DecodedKeyframeBox>();
            if (_context.TimeEngine == null) return new List<DecodedKeyframeBox>();
            if (entity == null) return new List<DecodedKeyframeBox>();

            var projection = new TimelineProjectionService().BuildEntityProjection(entity, _context);
            var duplicateSources = projection.States
                .GroupBy(s => s.SourceState)
                .ToDictionary(g => g.Key, g => g.Count());
            var result = new List<DecodedKeyframeBox>();
            foreach (var projected in projection.States)
            {
                if (!FastReflectionHelper.TryGetValue(projected.SourceState, propertyName, out var value) ||
                    value == null)
                    continue;
                result.Add(new DecodedKeyframeBox
                {
                    State = projected.SourceState,
                    VisualRelTime = projected.AbsoluteTime - clipStartTime,
                    Value = value,
                    IsArrayElement = duplicateSources[projected.SourceState] > 1,
                    OriginalArrayStateRef = duplicateSources[projected.SourceState] > 1
                        ? projected.SourceState
                        : null,
                    IsTemplateExpanded = projected.IsTemplateExpanded,
                    TemplateSourcePath = projected.TemplateSourcePath
                });
            }
            return result;
        }

        public void WriteBackVisualTime(
            IStoryboardEntity entity,
            ObjectState targetState,
            double newVisualRelTime,
            double clipStartTime)
        {
            KeyframeWriteBack.WriteBackVisualTime(
                entity,
                targetState,
                newVisualRelTime,
                _context?.TimeEngine,
                _context?.Chart?.note_list,
                clipStartTime);
        }

        public double TimeToX(double seconds) => _coordEngine.TimeToX(seconds);

        public double XToTime(double x) => _coordEngine.XToTime(x);

        public double PixelsPerSecond => _coordEngine.PixelsPerSecond;

        public void UpdatePixelsPerSecond(double newPps) => _coordEngine.UpdatePixelsPerSecond(newPps);
    }
}
