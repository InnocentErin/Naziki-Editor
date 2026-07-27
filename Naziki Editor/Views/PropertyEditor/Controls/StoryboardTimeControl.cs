using Naziki_Editor.State;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Naziki_Editor.Views.PropertyEditor
{
    // ==========================================================
    // 🌍 5. StoryboardTimeControl (时空锚点调度管理母仓 - 完全体拖拽矩阵版)
    // ==========================================================
    public class StoryboardTimeControl : StackPanel
    {
        private readonly PropertyInfo _propTime;
        private readonly PropertyInfo _propRel;
        private readonly PropertyInfo _propAdd;
        private readonly object _state;
        private readonly bool _isRoot;
        private readonly ProjectDataContext _context;

        private StackPanel _rowsContainer;
        private Button _btnAddRow;

        private bool _isDragging = false;
        private StoryboardTimeRow? _draggedRow = null;
        private bool _isInternalUpdating = false;

        public StoryboardTimeControl(object state, bool isRoot, ProjectDataContext context)
        {
            _state = state;
            _isRoot = isRoot;
            _context = context;
            this.Orientation = Orientation.Vertical;

            _propTime = state.GetType().GetProperty("Time");
            _propRel = state.GetType().GetProperty("RelativeTime");
            _propAdd = state.GetType().GetProperty("AddTime");

            _rowsContainer = new StackPanel { Orientation = Orientation.Vertical };
            this.Children.Add(_rowsContainer);

            _btnAddRow = new Button
            {
                Content = "➕ 添加多重时间轴轴心锚点 (Add Time Row)",
                Margin = new Thickness(0, 5, 0, 5),
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromRgb(45, 90, 45)),
                Foreground = Brushes.LightGreen,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _btnAddRow.Click += (s, e) => { AddNewTimeRow(float.MaxValue); UpdateUiRestrictionsAndSave(); };

            if (!_isRoot) this.Children.Add(_btnAddRow);

            LoadCurrentData();
        }

        private void LoadCurrentData()
        {
            _isInternalUpdating = true;
            _rowsContainer.Children.Clear();

            object rawTimeObj = _propTime?.GetValue(_state);
            string rawRel = _propRel?.GetValue(_state)?.ToString() ?? "";
            string rawAdd = _propAdd?.GetValue(_state)?.ToString() ?? "";

            if (rawTimeObj is System.Collections.IList list)
            {
                foreach (var item in list) AddNewTimeRow(item);
            }
            else if (!string.IsNullOrEmpty(rawRel) && rawRel != "0" && !_isRoot)
            {
                AddNewTimeRow($"relative:{rawRel}");
            }
            else if (!string.IsNullOrEmpty(rawAdd) && rawAdd != "0" && !_isRoot)
            {
                AddNewTimeRow($"additive:{rawAdd}");
            }
            else
            {
                AddNewTimeRow(rawTimeObj);
            }

            _isInternalUpdating = false;
            UpdateUiRestrictionsAndSave();
        }

        private void AddNewTimeRow(object val)
        {
            var row = new StoryboardTimeRow(val, _isRoot, _context, UpdateUiRestrictionsAndSave);

            row.BtnDelete.Click += (s, e) =>
            {
                _rowsContainer.Children.Remove(row);
                UpdateUiRestrictionsAndSave();
            };

            row.DragHandle.PreviewMouseLeftButtonDown += (s, e) =>
            {
                _isDragging = true; _draggedRow = row;
                row.DragHandle.CaptureMouse(); e.Handled = true;
            };

            row.DragHandle.PreviewMouseMove += (s, e) =>
            {
                if (!_isDragging || _draggedRow != row) return;

                Point mousePos = e.GetPosition(_rowsContainer);
                int targetIndex = -1; double heightAccumulator = 0;

                for (int i = 0; i < _rowsContainer.Children.Count; i++)
                {
                    var child = _rowsContainer.Children[i] as FrameworkElement;
                    heightAccumulator += child.ActualHeight;
                    if (mousePos.Y < heightAccumulator) { targetIndex = i; break; }
                }
                if (targetIndex == -1) targetIndex = _rowsContainer.Children.Count - 1;

                int currentIndex = _rowsContainer.Children.IndexOf(_draggedRow);
                if (currentIndex != targetIndex && targetIndex >= 0)
                {
                    _rowsContainer.Children.Remove(_draggedRow);
                    _rowsContainer.Children.Insert(targetIndex, _draggedRow);
                }
            };

            row.DragHandle.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_isDragging && _draggedRow == row)
                {
                    row.DragHandle.ReleaseMouseCapture(); _isDragging = false; _draggedRow = null;
                    UpdateUiRestrictionsAndSave();
                }
            };

            _rowsContainer.Children.Add(row);
        }

        private void UpdateUiRestrictionsAndSave()
        {
            if (_isInternalUpdating) return;

            int count = _rowsContainer.Children.Count;
            bool isArrayMode = count > 1;

            foreach (StoryboardTimeRow row in _rowsContainer.Children)
            {
                row.SetArrayModeRestrictions(isArrayMode);
            }

            if (count == 1)
            {
                var firstRow = _rowsContainer.Children[0] as StoryboardTimeRow;
                if (firstRow != null && firstRow.IsModifierSelected())
                {
                    _btnAddRow.IsEnabled = false; _btnAddRow.Opacity = 0.4;
                    _btnAddRow.ToolTip = "⚠️ 提示：当第一行被指定为相对(Relative)或附加(Additive)时间时，无法再点击添加平行复数矩阵行哦！";
                }
                else
                {
                    _btnAddRow.IsEnabled = true; _btnAddRow.Opacity = 1.0; _btnAddRow.ToolTip = null;
                }
            }
            else
            {
                _btnAddRow.IsEnabled = true; _btnAddRow.Opacity = 1.0; _btnAddRow.ToolTip = null;
            }

            SaveToMemory();
        }

        private void SaveToMemory()
        {
            if (_isInternalUpdating) return;

            object finalTime = float.MaxValue;
            object finalRel = null;
            object finalAdd = null;

            int count = _rowsContainer.Children.Count;
            if (count == 0)
            {
                _propTime?.SetValue(_state, float.MaxValue);
                _propRel?.SetValue(_state, null); _propAdd?.SetValue(_state, null);
                _context?.MarkAsModified(); return;
            }

            if (count == 1)
            {
                var row = _rowsContainer.Children[0] as StoryboardTimeRow;
                string mainMode = row.GetMainMode();

                if (mainMode == "Time")
                {
                    string subMode = row.GetTimeSubMode(); string valStr = row.GetTimeValue();
                    float.TryParse(valStr, out float parsedVal);

                    if (subMode == "Absolute") finalTime = string.IsNullOrEmpty(valStr) ? float.MaxValue : (object)parsedVal;
                    else if (subMode == "Relative") { finalTime = 0f; finalRel = parsedVal; }
                    else if (subMode == "Additive") { finalTime = 0f; finalAdd = parsedVal; }
                }
                else
                {
                    finalTime = row.GetValue();
                }
            }
            else
            {
                var finalList = new List<object>();
                foreach (StoryboardTimeRow row in _rowsContainer.Children)
                {
                    var val = row.GetValue();
                    if (val != null && !val.ToString().Contains("3.402823"))
                    {
                        finalList.Add(val);
                    }
                }
                finalTime = finalList.Count > 0 ? finalList : (object)float.MaxValue;
            }

            _propTime?.SetValue(_state, finalTime);
            _propRel?.SetValue(_state, finalRel);
            _propAdd?.SetValue(_state, finalAdd);

            _context?.MarkAsModified();
        }
    }
}
