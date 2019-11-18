﻿using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using T3.Core;
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Gui.Animation.CurveEditing;
using T3.Gui.Commands;
using T3.Gui.Graph;
using T3.Gui.Interaction.Snapping;
using UiHelpers;

// ReSharper disable CompareOfFloatsByEqualityOperator

namespace T3.Gui.Windows.TimeLine
{
    public class CurveEditArea : KeyframeEditArea, ITimeElementSelectionHolder, IValueSnapAttractor
    {
        public CurveEditArea(TimeLineCanvas timeLineCanvas, ValueSnapHandler snapHandler)
        {
            _snapHandler = snapHandler;
            _timeLineCanvas = timeLineCanvas;
            _curveEditBox = new CurveEditBox(timeLineCanvas);
        }

        
        public void Draw(Instance compositionOp, List<GraphWindow.AnimationParameter> animationParameters, bool bringCurvesIntoView = false)
        {
            _compositionOp = compositionOp;
            _drawList = ImGui.GetWindowDrawList();
            _animationParameters = animationParameters;

            if (bringCurvesIntoView)
                ViewAllOrSelectedKeys();

            ImGui.BeginGroup();
            {
                foreach (var param in animationParameters)
                {
                    foreach (var curve in param.Curves)
                    {
                        DrawCurveLine(curve);
                    }
                }

                foreach (var keyframe in GetAllKeyframes().ToArray())
                {
                    CurvePoint.Draw(keyframe, _timeLineCanvas, _selectedKeyframes.Contains(keyframe), this);
                }

                DrawContextMenu();
            }
            ImGui.EndGroup();

            RebuildCurveTables();
        }

        internal void HandleCurvePointDragging(VDefinition vDef, bool isSelected)
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
            }

            if (!ImGui.IsItemActive() || !ImGui.IsMouseDragging(0, 0f))
                return;

            if (ImGui.GetIO().KeyCtrl)
            {
                if (isSelected)
                    _selectedKeyframes.Remove(vDef);

                return;
            }

            if (!isSelected)
            {
                if (!ImGui.GetIO().KeyShift)
                {
                    TimeLineCanvas.Current.ClearSelection();
                }

                _selectedKeyframes.Add(vDef);
            }

            if (_changeKeyframesCommand == null)
            {
                TimeLineCanvas.Current.StartDragCommand();
            }

            var d = TimeLineCanvas.Current.InverseTransformDirection(ImGui.GetIO().MouseDelta);

            var snapClipToStart = _snapHandler.CheckForSnapping(vDef.U + d.X);
            if (!double.IsNaN(snapClipToStart))
                d.X = (float)(snapClipToStart - vDef.U);

            TimeLineCanvas.Current.UpdateDragCommand(d.X, d.Y);
        }
        
        void ITimeElementSelectionHolder.DeleteSelectedElements()
        {
            KeyframeOperations.DeleteSelectedKeyframesFromAnimationParameters(_selectedKeyframes, _animationParameters);
            RebuildCurveTables();
        }
        

        public void ClearSelection()
        {
            _selectedKeyframes.Clear();
        }

        public void UpdateSelectionForArea(ImRect screenArea, SelectMode selectMode)
        {
            if (selectMode == SelectMode.Replace)
                _selectedKeyframes.Clear();

            var canvasArea = TimeLineCanvas.Current.InverseTransformRect(screenArea);
            var matchingItems = new List<VDefinition>();

            foreach (var keyframe in GetAllKeyframes())
            {
                if (canvasArea.Contains(new Vector2((float)keyframe.U, (float)keyframe.Value)))
                {
                    matchingItems.Add(keyframe);
                }
            }

            switch (selectMode)
            {
                case SelectMode.Add:
                case SelectMode.Replace:
                    _selectedKeyframes.UnionWith(matchingItems);
                    break;
                case SelectMode.Remove:
                    _selectedKeyframes.ExceptWith(matchingItems);
                    break;
            }
        }

        public ICommand StartDragCommand()
        {
            _changeKeyframesCommand = new ChangeKeyframesCommand(_compositionOp.Symbol.Id, _selectedKeyframes);
            return _changeKeyframesCommand;
        }

        public void UpdateDragCommand(double dt, double dv)
        {
            foreach (var vDefinition in _selectedKeyframes)
            {
                vDefinition.U += dt;
                vDefinition.Value += dv;
            }
            RebuildCurveTables();
        }

        public void CompleteDragCommand()
        {
            if (_changeKeyframesCommand == null)
                return;

            _changeKeyframesCommand.StoreCurrentValues();
            UndoRedoStack.Add(_changeKeyframesCommand);
            _changeKeyframesCommand = null;
        }

        public void UpdateDragStartCommand(double dt, double dv)
        {
        }

        public void UpdateDragEndCommand(double dt, double dv)
        {
        }

        #region  implement snapping -------------------------
        SnapResult IValueSnapAttractor.CheckForSnap(double targetTime)
        {
            _snapThresholdOnCanvas = TimeLineCanvas.Current.InverseTransformDirection(new Vector2(SnapDistance, 0)).X;
            var maxForce = 0.0;
            var bestSnapTime = double.NaN;

            foreach (var vDefinition in GetAllKeyframes())
            {
                if (_selectedKeyframes.Contains(vDefinition))
                    continue;

                CheckForSnapping(targetTime, vDefinition.U, maxForce: ref maxForce, bestSnapTime: ref bestSnapTime);
            }

            return double.IsNaN(bestSnapTime)
                       ? null
                       : new SnapResult(bestSnapTime, maxForce);
        }

        private void CheckForSnapping(double targetTime, double anchorTime, ref double maxForce, ref double bestSnapTime)
        {
            var distance = Math.Abs(anchorTime - targetTime);
            if (distance < 0.001)
                return;

            var force = Math.Max(0, _snapThresholdOnCanvas - distance);
            if (force <= maxForce)
                return;

            bestSnapTime = anchorTime;
            maxForce = force;
        }

        private double _snapThresholdOnCanvas;
        private const float SnapDistance = 4;
        #endregion


        private void DrawCurveLine(Curve curve)
        {
            const float step = 3f;
            var width = ImGui.GetWindowWidth();

            double dU = _timeLineCanvas.InverseTransformDirection(new Vector2(step, 0)).X;
            double u = _timeLineCanvas.InverseTransformPosition(_timeLineCanvas.WindowPos).X;
            var x = _timeLineCanvas.WindowPos.X;

            var steps = (int)(width / step);
            if (_curveLinePoints.Length != steps)
            {
                _curveLinePoints = new Vector2[steps];
            }

            for (var i = 0; i < steps; i++)
            {
                _curveLinePoints[i] = new Vector2(x, _timeLineCanvas.TransformPosition(new Vector2(0, (float)curve.GetSampledValue(u))).Y);
                u += dU;
                x += step;
            }

            _drawList.AddPolyline(ref _curveLinePoints[0], steps, Color.Gray, false, 1);
        }



        private static ChangeKeyframesCommand _changeKeyframesCommand;
        private static Vector2[] _curveLinePoints = new Vector2[0];

        private Instance _compositionOp;

        private static ImDrawListPtr _drawList;
        
        private readonly CurveEditBox _curveEditBox;
        private readonly ValueSnapHandler _snapHandler;
    }
}