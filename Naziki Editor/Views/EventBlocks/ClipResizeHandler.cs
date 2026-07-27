using Naziki_Editor.Core.Timeline.EventBlocks.Abstractions;
using Naziki_Editor.UI.ViewModels;
using System.Windows.Controls.Primitives;

namespace Naziki_Editor.Views.EventBlocks
{
    /// <summary>
    /// Handles left/right edge resize operations for EventBlockControl.
    /// </summary>
    public class ClipResizeHandler
    {
        private readonly EventBlockControl _clipControl;
        private readonly IEventBlockService _clipService;

        private double _originalStartTime;
        private double _originalEndTime;

        public ClipResizeHandler(EventBlockControl clipControl, IEventBlockService clipService)
        {
            _clipControl = clipControl;
            _clipService = clipService;
        }

        public void OnResizeLeftStarted(EventBlockViewModel model)
        {
            _originalStartTime = model.StartTime;
            _originalEndTime = model.EndTime;
        }

        public void OnResizeLeftCompleted(EventBlockViewModel model)
        {
            _clipService.SettleDrag(
                model.AssociatedObject,
                _originalStartTime,
                _originalEndTime,
                model.StartTime,
                model.EndTime);

            _clipControl.InvokeMarkAsModified();
            _clipControl.InvokeEvaluateValidationWarning();
        }

        public void OnResizeRightStarted(EventBlockViewModel model)
        {
            _originalStartTime = model.StartTime;
            _originalEndTime = model.EndTime;
        }

        public void OnResizeRightCompleted(EventBlockViewModel model)
        {
            _clipService.SettleDrag(
                model.AssociatedObject,
                _originalStartTime,
                _originalEndTime,
                model.StartTime,
                model.EndTime);

            _clipControl.InvokeMarkAsModified();
            _clipControl.InvokeEvaluateValidationWarning();
        }

        public void OnResizeLeftDelta(DragDeltaEventArgs e, EventBlockViewModel model, double pixelsPerSecond)
        {
            double deltaTime = e.HorizontalChange / pixelsPerSecond;
            model.StartTime += deltaTime;
            if (model.StartTime < 0) model.StartTime = 0;
            _clipControl.InvokeUpdateXPositionAndWidth();
        }

        public void OnResizeRightDelta(DragDeltaEventArgs e, EventBlockViewModel model, double pixelsPerSecond)
        {
            double deltaTime = e.HorizontalChange / pixelsPerSecond;
            model.EndTime += deltaTime;
            _clipControl.InvokeUpdateXPositionAndWidth();
        }
    }
}


