using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Timeline
{
    /// <summary>
    /// 🎹 全息音符时空手绘工厂（主轴与微观时光屋的唯一真神联动接口）
    /// </summary>
    public static class NoteVisualEngine
    {
        public static void RenderNoteRuler(Canvas canvas, List<C2Note> noteList, ChartTimeEngine timeEngine, double pixelsPerSecond, bool isMicroMode)
        {
            if (canvas == null) return;
            canvas.Children.Clear();

            if (noteList == null || timeEngine == null) return;

            foreach (var note in noteList)
            {
                double seconds = timeEngine.TickToSeconds(note.tick);
                double xPos = seconds * pixelsPerSecond;

                // 📐 ==========================================
                // 📡 【双态力场测密】：根据宏观/微观，分发基础轨道高度排版
                // ==========================================
                double baseIconOriginalSize = isMicroMode ? 12.0 : 16.0; // 宏观标准 16px，微观标准 12px
                double baseIconTop = isMicroMode ? 34.0 : 13.0;          // 核心轨道物理基准线

                // 🧬 【物理中轴线绝对对称方程】：算出当前轨道的绝对中心 Y 轴，确保大音符和小音符的中心点严格重合！
                double centerY = baseIconTop + (baseIconOriginalSize / 2.0);

                // 🌟 【需求 2】：如果是子音符 DragChild(4) 或 CDragChild(7)，尺寸自动缩小 0.5 倍！
                double iconSize = baseIconOriginalSize;
                if (note.type == 4 || note.type == 7)
                {
                    iconSize *= 0.5;
                }

                // 反求算居中校正后的 Top 坐标
                double iconTop = centerY - (iconSize / 2.0);
                double textTop = isMicroMode ? 23.0 : 0.0;
                double textLeftOffset = isMicroMode ? 3.0 : -5.0;

                double holdLineTop = isMicroMode ? 32.0 : 10.0;
                double longHoldLineTop = isMicroMode ? 46.0 : 28.0;

                // 🚀 【需求 1 & 色彩微调】：如果是 Drag(3) 或 CDrag(6) 头，向其所属的最后一个子节点发射全息拉伸虚线！
                if (note.type == 3 || note.type == 6)
                {
                    int childType = note.type == 3 ? 4 : 7;
                    C2Note lastChild = null;

                    // 🔍 智能追踪雷达 A：经典 next_id 链条追溯
                    var nextIdProp = note.GetType().GetProperty("next_id") ?? note.GetType().GetProperty("NextId");
                    if (nextIdProp != null)
                    {
                        var currentNote = note;
                        while (currentNote != null)
                        {
                            object nextIdObj = nextIdProp.GetValue(currentNote);
                            if (nextIdObj != null)
                            {
                                int nextId = Convert.ToInt32(nextIdObj);
                                if (nextId > 0)
                                {
                                    var nextNote = noteList.FirstOrDefault(n => {
                                        var idProp = n.GetType().GetProperty("id") ?? n.GetType().GetProperty("Id");
                                        return idProp != null && Convert.ToInt32(idProp.GetValue(n)) == nextId;
                                    });
                                    if (nextNote != null && nextNote.type == childType)
                                    {
                                        lastChild = nextNote;
                                        currentNote = nextNote;
                                        continue;
                                    }
                                }
                            }
                            break;
                        }
                    }

                    // 🔍 智能追踪雷达 B：parent_id 反向抓取
                    if (lastChild == null)
                    {
                        var parentIdProp = note.GetType().GetProperty("parent_id") ?? note.GetType().GetProperty("ParentId");
                        if (parentIdProp != null)
                        {
                            var idProp = note.GetType().GetProperty("id") ?? note.GetType().GetProperty("Id");
                            if (idProp != null)
                            {
                                object headId = idProp.GetValue(note);
                                lastChild = noteList.LastOrDefault(n => n.type == childType && parentIdProp.GetValue(n)?.ToString() == headId?.ToString());
                            }
                        }
                    }

                    // 🔍 智能追踪雷达 C：物理顺序极致兜底
                    if (lastChild == null)
                    {
                        int headIndex = noteList.IndexOf(note);
                        if (headIndex >= 0)
                        {
                            for (int i = headIndex + 1; i < noteList.Count; i++)
                            {
                                if (noteList[i].type == childType) lastChild = noteList[i];
                                if (noteList[i].type == note.type) break;
                            }
                        }
                    }

                    // 🛠️ 雷达测绘成功，生成完美响应缩放的虚线锁链！
                    if (lastChild != null)
                    {
                        double childSec = timeEngine.TickToSeconds(lastChild.tick);
                        double childX = childSec * pixelsPerSecond;

                        if (childX > xPos)
                        {
                            var chainLine = new Line
                            {
                                X1 = xPos,
                                X2 = childX,
                                Y1 = centerY,
                                Y2 = centerY, // 完美穿过中轴线
                                // 🎨 【色彩微调】：Drag 链条用温柔淡紫色 (#D0B3FF)，CDrag 链条用高亮淡绿色 (#A3F7B5)
                                Stroke = note.type == 3 ? new SolidColorBrush(Color.FromRgb(208, 179, 255)) : new SolidColorBrush(Color.FromRgb(163, 247, 181)),
                                StrokeThickness = 1.2,
                                StrokeDashArray = new DoubleCollection { 4, 4 },
                                IsHitTestVisible = false, // 绝对防穿透

                                Tag = note,             // 🌟 注入主键：让缩放引擎能够成功抓取它！
                                DataContext = lastChild // 🌟 时空绑定：把终点音符藏进上下文，供缩放引擎提取重算 X2！
                            };
                            canvas.Children.Add(chainLine);
                        }
                    }
                }

                // 🚀 1. 【持续时间光轨】：如果是 Hold(1) 或 LongHold(2)，优先在底层绘制长轨
                if (note.type == 1 || note.type == 2)
                {
                    double endSeconds = timeEngine.TickToSeconds(note.tick + note.hold_tick);
                    double durationSeconds = endSeconds - seconds;

                    if (durationSeconds > 0)
                    {
                        var durationRect = new Rectangle
                        {
                            Tag = note,
                            Width = durationSeconds * pixelsPerSecond,
                            Height = 2,
                            Cursor = System.Windows.Input.Cursors.Hand
                        };

                        durationRect.Fill = note.type == 1 ? Brushes.White : Brushes.Gold;
                        Canvas.SetTop(durationRect, note.type == 1 ? holdLineTop : longHoldLineTop);
                        Canvas.SetLeft(durationRect, xPos);
                        canvas.Children.Add(durationRect);
                    }
                }

                // 🧙‍♂️ 2. 从全局资源大管家那里，召唤对应门派的图标！
                var iconBmp = Core.EditorResourceManager.GetNoteIcon(note.type);
                UIElement noteUI;

                if (iconBmp != null)
                {
                    noteUI = new Image
                    {
                        Source = iconBmp,
                        Width = iconSize,
                        Height = iconSize,
                        Tag = note,
                        ToolTip = $"ID: {note.id}\nTick: {note.tick}\nTime: {seconds:F3}s",
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    Canvas.SetLeft(noteUI, xPos - (iconSize / 2.0)); // 智能对称半置偏置
                    Canvas.SetTop(noteUI, iconTop);
                }
                else
                {
                    // 🛡️ 优雅降级兜底方案（方块同样进行子节点腰斩）
                    double rectW = isMicroMode ? 3.0 : 4.0;
                    double rectH = isMicroMode ? 10.0 : 16.0;
                    if (note.type == 4 || note.type == 7)
                    {
                        rectW *= 0.5;
                        rectH *= 0.5;
                    }
                    double rectTop = centerY - (rectH / 2.0);

                    var rect = new Rectangle
                    {
                        Tag = note,
                        Width = rectW,
                        Height = rectH,
                        RadiusX = 1,
                        RadiusY = 1,
                        ToolTip = $"ID: {note.id}\nTick: {note.tick}",
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    if (note.type == 1) rect.Fill = Brushes.LightGreen;
                    else if (note.type == 2) rect.Fill = Brushes.LightSkyBlue;
                    else if (note.type == 3 || note.type == 6) rect.Fill = Brushes.Gold;
                    else if (note.type == 4) rect.Fill = Brushes.Plum;
                    else rect.Fill = Brushes.White;

                    noteUI = rect;
                    Canvas.SetLeft(noteUI, xPos - (rectW / 2.0));
                    Canvas.SetTop(noteUI, rectTop);
                }
                canvas.Children.Add(noteUI);

                // 🚀 3. 【ID周期高显】：每隔4个音符，在上方绘制 ID 文字！
                if (note.id % 5 == 0)
                {
                    TextBlock txtId = new TextBlock
                    {
                        Text = note.id.ToString(),
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = (Brush)Application.Current.FindResource("TipsColor") ?? Brushes.Gray,
                        Tag = note
                    };

                    double finalTxtLeft = isMicroMode ? (xPos + textLeftOffset) : (xPos - 5.0);
                    Canvas.SetLeft(txtId, finalTxtLeft);
                    Canvas.SetTop(txtId, textTop);
                    canvas.Children.Add(txtId);
                }
            }
        }
    }
}