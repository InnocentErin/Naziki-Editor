using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Naziki_Editor.Core.Compiler
{
    [Flags]
    public enum OptimizeTarget
    {
        None = 0,
        Camera = 1,
        UI = 2,
        Both = Camera | UI
    }

    // =========================================================================
    // 🌌 控制板智能收容与归一化引擎 (Controller Normalization Engine)
    // =========================================================================
    public static class ControllerOptimizer
    {
        private static readonly HashSet<string> CameraProps = new HashSet<string> { "Perspective", "Size", "Fov", "X", "Y", "Z", "RotX", "RotY", "RotZ" };
        private static readonly HashSet<string> UiProps = new HashSet<string> {
            "StoryboardOpacity", "UiOpacity", "ScanlineOpacity", "BackgroundDim", "NoteOpacityMultiplier",
            "ScanlineColor", "NoteRingColor", "OverrideScanlinePos", "ScanlinePos", "NoteFillColors", "ScanlineSmoothing"
        };

        private class StateFragment
        {
            public ControllerState State;
            public double? AbsTime;
            public string NoteTimeStr;
            public bool IsNoteAnchor => !string.IsNullOrEmpty(NoteTimeStr);
        }

        // ==========================================\
        // 📡 新增雷达：全盘扫描是否存在“野生”的相机或 UI 碎片！
        // ==========================================\
        public static (bool hasScatteredCamera, bool hasScatteredUi) DetectScatteredProperties(ProjectDataContext context)
        {
            var root = context.Storyboard;
            if (root?.controllers == null) return (false, false);

            bool foundCam = false;
            bool foundUi = false;

            // 🌟 终极防线：同时获取属性和字段，绝不放过任何一个黑户！
            var type = typeof(ControllerState);
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var ctrl in root.controllers.Where(c => c.Id != "sys_camera" && c.Id != "sys_ui"))
            {
                var allStates = new List<ControllerState> { ctrl.BaseState };
                if (ctrl.Keyframes != null) allStates.AddRange(ctrl.Keyframes);

                foreach (var state in allStates)
                {
                    // 扫属性
                    foreach (var prop in props)
                    {
                        if (prop.CanRead && prop.GetValue(state) != null)
                        {
                            if (!foundCam && CameraProps.Contains(prop.Name)) foundCam = true;
                            if (!foundUi && UiProps.Contains(prop.Name)) foundUi = true;
                        }
                    }
                    // 扫字段
                    foreach (var field in fields)
                    {
                        if (field.GetValue(state) != null)
                        {
                            if (!foundCam && CameraProps.Contains(field.Name)) foundCam = true;
                            if (!foundUi && UiProps.Contains(field.Name)) foundUi = true;
                        }
                    }
                    if (foundCam && foundUi) return (true, true);
                }
            }
            return (foundCam, foundUi);
        }

        // ==========================================\
        // 🚀 核心优化引擎 (支持指定 Target)
        // ==========================================\
        public static void OptimizeControllers(ProjectDataContext context, OptimizeTarget target, Func<int, bool> onConfirmDeleteEmptyShells)
        {
            if (target == OptimizeTarget.None) return;

            var root = context.Storyboard;
            if (root?.controllers == null || root.controllers.Count == 0) return;

            bool doCamera = (target & OptimizeTarget.Camera) != 0;
            bool doUi = (target & OptimizeTarget.UI) != 0;

            var sysCamera = root.controllers.FirstOrDefault(c => c.Id == "sys_camera");
            var sysUi = root.controllers.FirstOrDefault(c => c.Id == "sys_ui");

            if (doCamera && sysCamera == null) { sysCamera = new C2SceneController { Id = "sys_camera", EditorMode = "Camera" }; root.controllers.Add(sysCamera); }
            if (doUi && sysUi == null) { sysUi = new C2SceneController { Id = "sys_ui", EditorMode = "UI" }; root.controllers.Add(sysUi); }

            List<StateFragment> cameraFragments = new List<StateFragment>();
            List<StateFragment> uiFragments = new List<StateFragment>();
            List<C2SceneController> shellsToRemove = new List<C2SceneController>();

            // 🌟 终极防线：获取所有特征
            var type = typeof(ControllerState);
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var ctrl in root.controllers.Where(c => c.Id != "sys_camera" && c.Id != "sys_ui"))
            {
                bool hasRemainingProps = false;
                var allStates = new List<ControllerState> { ctrl.BaseState };
                if (ctrl.Keyframes != null) allStates.AddRange(ctrl.Keyframes);

                bool isBaseNoteAnchor = IsNoteAnchorExpression(ctrl.BaseState.Time);
                double baseAbsTime = 0;
                if (!isBaseNoteAnchor)
                    baseAbsTime = context.TimeEngine.ParseCytoidTimeExpression(ctrl.BaseState.Time, context.Chart?.note_list);

                double currentRelTime = 0;

                for (int i = 0; i < allStates.Count; i++)
                {
                    var state = allStates[i];
                    if (i > 0)
                    {
                        if (state.RelativeTime.HasValue) currentRelTime = state.RelativeTime.Value;
                        else if (state.AddTime.HasValue) currentRelTime += state.AddTime.Value;
                    }

                    ControllerState camState = new ControllerState();
                    ControllerState uiState = new ControllerState();
                    bool hasCam = false, hasUi = false;

                    // 1. 处理标准的 Property
                    foreach (var prop in props)
                    {
                        if (!prop.CanRead || !prop.CanWrite) continue;
                        if (new[] { "Time", "RelativeTime", "AddTime", "Easing", "Destroy", "Template" }.Contains(prop.Name)) continue;

                        object val = prop.GetValue(state);
                        if (val == null) continue;

                        if (doCamera && CameraProps.Contains(prop.Name)) { prop.SetValue(camState, val); prop.SetValue(state, null); hasCam = true; }
                        else if (doUi && UiProps.Contains(prop.Name)) { prop.SetValue(uiState, val); prop.SetValue(state, null); hasUi = true; }
                        else { hasRemainingProps = true; }
                    }

                    // 2. 处理潜伏的 Field
                    foreach (var field in fields)
                    {
                        if (new[] { "Time", "RelativeTime", "AddTime", "Easing", "Destroy", "Template" }.Contains(field.Name)) continue;

                        object val = field.GetValue(state);
                        if (val == null) continue;

                        if (doCamera && CameraProps.Contains(field.Name)) { field.SetValue(camState, val); field.SetValue(state, null); hasCam = true; }
                        else if (doUi && UiProps.Contains(field.Name)) { field.SetValue(uiState, val); field.SetValue(state, null); hasUi = true; }
                        else { hasRemainingProps = true; }
                    }

                    if (hasCam || hasUi)
                    {
                        string currentNoteStr = null;
                        double? currentAbsSec = null;

                        if (isBaseNoteAnchor) currentNoteStr = FoldNoteAnchorExpression(ctrl.BaseState.Time?.ToString(), currentRelTime);
                        else currentAbsSec = baseAbsTime + currentRelTime;

                        if (hasCam)
                        {
                            camState.Easing = state.Easing;
                            cameraFragments.Add(new StateFragment { State = camState, AbsTime = currentAbsSec, NoteTimeStr = currentNoteStr });
                        }
                        if (hasUi)
                        {
                            uiState.Easing = state.Easing;
                            uiFragments.Add(new StateFragment { State = uiState, AbsTime = currentAbsSec, NoteTimeStr = currentNoteStr });
                        }
                    }
                }

                if (!hasRemainingProps) shellsToRemove.Add(ctrl);
            }

            if (shellsToRemove.Count > 0)
            {
                bool proceed = onConfirmDeleteEmptyShells?.Invoke(shellsToRemove.Count) ?? true;
                if (proceed) { foreach (var shell in shellsToRemove) root.controllers.Remove(shell); }
            }

            if (doCamera)
            {
                ExtractExistingFragments(sysCamera, cameraFragments, context);
                MergeFragmentsIntoTarget(sysCamera, cameraFragments);
            }
            if (doUi)
            {
                ExtractExistingFragments(sysUi, uiFragments, context);
                MergeFragmentsIntoTarget(sysUi, uiFragments);
            }

            context.MarkAsModified();
        }

        private static string FoldNoteAnchorExpression(string timeExpr, double relativeTime)
        {
            if (string.IsNullOrEmpty(timeExpr)) return timeExpr;
            if (Math.Abs(relativeTime) < 0.001) return timeExpr;

            string[] parts = timeExpr.Split(':');
            string prefix = parts.Length > 0 ? parts[0] : "";
            string id = parts.Length > 1 ? parts[1] : "";
            double offset = 0;

            if (parts.Length == 3) double.TryParse(parts[2], out offset);
            else if (parts.Length == 1 && int.TryParse(parts[0], out _)) { prefix = "start"; id = parts[0]; }

            offset += relativeTime;
            return $"{prefix}:{id}:{offset.ToString("0.###")}";
        }

        private static void ExtractExistingFragments(C2SceneController targetCtrl, List<StateFragment> fragments, ProjectDataContext context)
        {
            var allStates = new List<ControllerState> { targetCtrl.BaseState };
            if (targetCtrl.Keyframes != null) allStates.AddRange(targetCtrl.Keyframes);

            bool isBaseNoteAnchor = IsNoteAnchorExpression(targetCtrl.BaseState.Time);
            double baseAbsTime = 0;
            if (!isBaseNoteAnchor)
                baseAbsTime = context.TimeEngine.ParseCytoidTimeExpression(targetCtrl.BaseState.Time, context.Chart?.note_list);

            double currentRelTime = 0;
            for (int i = 0; i < allStates.Count; i++)
            {
                var state = allStates[i];

                if (i > 0 && state.Time != null && IsNoteAnchorExpression(state.Time))
                {
                    fragments.Add(new StateFragment { State = state, AbsTime = null, NoteTimeStr = state.Time.ToString() });
                    continue;
                }

                if (i > 0)
                {
                    if (state.RelativeTime.HasValue) currentRelTime = state.RelativeTime.Value;
                    else if (state.AddTime.HasValue) currentRelTime += state.AddTime.Value;
                }

                string currentNoteStr = null;
                double? currentAbsSec = null;

                if (isBaseNoteAnchor)
                    currentNoteStr = FoldNoteAnchorExpression(targetCtrl.BaseState.Time?.ToString(), currentRelTime);
                else
                    currentAbsSec = baseAbsTime + currentRelTime;

                fragments.Add(new StateFragment { State = state, AbsTime = currentAbsSec, NoteTimeStr = currentNoteStr });
            }

            targetCtrl.BaseState = new ControllerState();
            targetCtrl.Keyframes.Clear();
        }

        private static void MergeFragmentsIntoTarget(C2SceneController targetCtrl, List<StateFragment> fragments)
        {
            if (fragments.Count == 0) return;

            var absFrags = fragments.Where(f => !f.IsNoteAnchor && f.AbsTime.HasValue).OrderBy(f => f.AbsTime.Value).ToList();
            var noteFrags = fragments.Where(f => f.IsNoteAnchor).ToList();

            double baseTimeSec = 0;
            bool hasBase = false;

            if (absFrags.Count > 0)
            {
                baseTimeSec = absFrags[0].AbsTime.Value;
                targetCtrl.BaseState = MergeStates(targetCtrl.BaseState, absFrags[0].State);
                targetCtrl.BaseState.Time = baseTimeSec;
                hasBase = true;

                foreach (var frag in absFrags.Skip(1))
                {
                    if (Math.Abs(frag.AbsTime.Value - baseTimeSec) <= 0.001)
                    {
                        targetCtrl.BaseState = MergeStates(targetCtrl.BaseState, frag.State);
                        if (!string.IsNullOrEmpty(frag.State.Easing)) targetCtrl.BaseState.Easing = frag.State.Easing;
                    }
                    else
                    {
                        float relTime = (float)(frag.AbsTime.Value - baseTimeSec);
                        var existingKf = targetCtrl.Keyframes.FirstOrDefault(k => Math.Abs((k.RelativeTime ?? 0) - relTime) <= 0.001 && k.Time == null);

                        if (existingKf != null)
                        {
                            MergeStates(existingKf, frag.State);
                        }
                        else
                        {
                            frag.State.RelativeTime = relTime;
                            frag.State.Time = null;
                            targetCtrl.Keyframes.Add(frag.State);
                        }
                    }
                }
            }

            foreach (var noteFrag in noteFrags)
            {
                if (!hasBase)
                {
                    targetCtrl.BaseState = MergeStates(targetCtrl.BaseState, noteFrag.State);
                    targetCtrl.BaseState.Time = noteFrag.NoteTimeStr;
                    hasBase = true;
                }
                else
                {
                    var existingKf = targetCtrl.Keyframes.FirstOrDefault(k => k.Time?.ToString() == noteFrag.NoteTimeStr);
                    if (existingKf != null)
                    {
                        MergeStates(existingKf, noteFrag.State);
                    }
                    else
                    {
                        noteFrag.State.Time = noteFrag.NoteTimeStr;
                        noteFrag.State.RelativeTime = null;
                        noteFrag.State.AddTime = null;
                        targetCtrl.Keyframes.Add(noteFrag.State);
                    }
                }
            }

            targetCtrl.Keyframes = targetCtrl.Keyframes.OrderBy(k => k.RelativeTime ?? float.MaxValue).ToList();
        }

        private static ControllerState MergeStates(ControllerState main, ControllerState addon)
        {
            if (main == null) main = new ControllerState();
            if (addon == null) return main;

            var type = typeof(ControllerState);

            // 合并 Property
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (new[] { "Time", "RelativeTime", "AddTime" }.Contains(prop.Name)) continue;
                object addonVal = prop.GetValue(addon);
                if (addonVal != null) prop.SetValue(main, addonVal);
            }

            // 合并 Field
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (new[] { "Time", "RelativeTime", "AddTime" }.Contains(field.Name)) continue;
                object addonVal = field.GetValue(addon);
                if (addonVal != null) field.SetValue(main, addonVal);
            }

            return main;
        }

        private static bool IsNoteAnchorExpression(object timeObj)
        {
            if (timeObj == null) return false;
            string t = timeObj.ToString().ToLower();
            return t.Contains("start") || t.Contains("end") || t.Contains("intro") || t.Contains("note") || t.Contains("at");
        }
    }
}