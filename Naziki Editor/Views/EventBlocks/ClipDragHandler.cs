using Naziki_Editor.Core.Timeline.EventBlocks.Abstractions;
using Naziki_Editor.UI.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Naziki_Editor.Views.EventBlocks
{
    /// <summary>
    /// Handles mouse drag operations for EventBlockControl: 
    /// horizontal time translation, vertical track switching, and drag settlement.
    /// </summary>
    public class ClipDragHandler
    {
        private readonly EventBlockControl _clipControl;
        private readonly IEventBlockService _clipService;

        private bool _isDraggingClip = false;
        private Point _clipDragStartPoint;
        private double _originalStartTime;
        private double _originalEndTime;
        private double _originalY;

        public ClipDragHandler(EventBlockControl clipControl, IEventBlockService clipService)
        {
            _clipControl = clipControl;
            _clipService = clipService;
        }

        public bool IsDragging => _isDraggingClip;

        public void OnMouseDown(MouseButtonEventArgs e, EventBlockViewModel model, double pixelsPerSecond)
        {
            // Handle double-click
            if (e.ClickCount == 2)
            {
                _isDraggingClip = false;
                _clipControl.ReleaseMouseCaptureInternal();
                _clipControl.SetOpacityInternal(1.0);
                _clipControl.InvokeRequestDetailedEdit(model);
                e.Handled = true;
                return;
            }

            // Handle Ctrl+Click
            if (e.ClickCount == 1 && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _isDraggingClip = false;
                _clipControl.SetOpacityInternal(1.0);
                _clipControl.PublishMessageInternal("RequestOpenPropertyEditor", (object)model.AssociatedObject);
                e.Handled = true;
                return;
            }

            // Normal single click - start drag
            model.IsSelected = true;
            _clipControl.InvokeClipSelected(model);

            _isDraggingClip = true;
            _clipDragStartPoint = e.GetPosition(_clipControl.Parent as UIElement);
            _originalStartTime = model.StartTime;
            _originalEndTime = model.EndTime;

            _originalY = Canvas.GetTop(_clipControl);
            if (double.IsNaN(_originalY) || _originalY > 40.0) _originalY = 6.0;

            Panel.SetZIndex(_clipControl, 999);
            _clipControl.CaptureMouseInternal();
            e.Handled = true;
            _clipControl.SetOpacityInternal(0.7);

            if (_clipControl.Parent is Canvas parentCanvas) parentCanvas.ClipToBounds = false;
            _clipControl.InvokeMacroGridDrag(e, EventBlockControl.MacroDragStage.Started);
        }

        public void OnMouseMove(MouseEventArgs e, EventBlockViewModel model, double pixelsPerSecond)
        {
            if (!_isDraggingClip) return;

            Point currentPos = e.GetPosition(_clipControl.Parent as UIElement);
            if (Math.Abs(currentPos.X - _clipDragStartPoint.X) < 3 && Math.Abs(currentPos.Y - _clipDragStartPoint.Y) < 3)
                return;

            bool isGlobalController = _clipService.IsGlobalController(model.AssociatedObject);

            // X-axis time translation
            if (!isGlobalController)
            {
                double deltaX = currentPos.X - _clipDragStartPoint.X;
                double deltaTime = deltaX / pixelsPerSecond;

                double oldDuration = model.EndTime - model.StartTime;
                model.StartTime = _originalStartTime + deltaTime;
                if (model.StartTime < 0) model.StartTime = 0;
                model.EndTime = model.StartTime + oldDuration;

                Canvas.SetLeft(_clipControl, model.StartTime * pixelsPerSecond);
            }

            // Y-axis vertical movement
            double deltaY = currentPos.Y - _clipDragStartPoint.Y;
            Canvas.SetTop(_clipControl, _originalY + deltaY);

            _clipControl.InvokeMacroGridDrag(e, EventBlockControl.MacroDragStage.Moving);
        }

        public void OnMouseUp(MouseButtonEventArgs e, EventBlockViewModel model, double pixelsPerSecond)
        {
            if (!_isDraggingClip) return;

            if (_clipControl.Parent is Canvas parentCanvas) parentCanvas.ClipToBounds = true;

            _isDraggingClip = false;
            _clipControl.ReleaseMouseCaptureInternal();
            _clipControl.SetOpacityInternal(1.0);
            Panel.SetZIndex(_clipControl, 0);

            // Settle drag using the new service
            double finalDeltaTime = model.StartTime - _originalStartTime;
            if (Math.Abs(finalDeltaTime) > 0.001 && model.AssociatedObject != null)
            {
                _clipService.SettleDrag(
                    model.AssociatedObject,
                    _originalStartTime,
                    _originalEndTime,
                    model.StartTime,
                    model.EndTime);
            }

            // Sync Order property
            try
            {
                var baseState = model.AssociatedObject.GetBaseState();
                if (baseState != null)
                {
                    var orderProp = baseState.GetType().GetProperty("Order");
                    if (orderProp != null)
                    {
                        int currentOrder = Convert.ToInt32(orderProp.GetValue(baseState) ?? 0);
                        if (currentOrder != _clipControl.CurrentTrackIndex)
                        {
                            orderProp.SetValue(baseState, _clipControl.CurrentTrackIndex);
                        }
                    }
                }
            }
            catch { }

            _clipControl.InvokeMarkAsModified();
            _clipControl.InvokeEvaluateValidationWarning();
            _clipControl.InvokeMacroGridDrag(e, EventBlockControl.MacroDragStage.Completed);
        }
    }
}


