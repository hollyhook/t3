using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using SharpDX;
using SharpDX.Direct3D11;
using T3.Core;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Gui.Graph.Interaction;
using T3.Gui.Graph.Rendering;
using T3.Gui.InputUi;
using T3.Gui.OutputUi;
using T3.Gui.Selection;
using T3.Gui.Styling;
using T3.Gui.TypeColors;
using T3.Gui.UiHelpers;
using T3.Gui.Windows;
using UiHelpers;
using Vector2 = System.Numerics.Vector2;

namespace T3.Gui.Graph
{
    /// <summary>
    /// Renders a graphic representation of a <see cref="SymbolChild"/> within the current <see cref="GraphWindow"/>
    /// </summary>
    static class GraphNode
    {
        public static void Draw(SymbolChildUi childUi, Instance instance)
        {
            if (instance == null)
                return;

            var symbolUi = SymbolUiRegistry.Entries[childUi.SymbolChild.Symbol.Id];
            var nodeHasHiddenMatchingInputs = false;
            var visibleInputUis = FindVisibleInputUis(symbolUi, childUi, ref nodeHasHiddenMatchingInputs);

            var framesSinceLastUpdate = 100;
            foreach (var output in instance.Outputs)
            {
                framesSinceLastUpdate = Math.Min(framesSinceLastUpdate, output.DirtyFlag.FramesSinceLastUpdate);
            }

            SymbolChildUi.CustomUiResult customUiResult;
            _drawList = Graph.DrawList;
            ImGui.PushID(childUi.SymbolChild.Id.GetHashCode());
            {
                var newNodeSize = ComputeNodeSize(childUi, visibleInputUis);
                AdjustGroupLayoutAfterResize(childUi, newNodeSize);
                _usableScreenRect = GraphCanvas.Current.TransformRect(new ImRect(childUi.PosOnCanvas,
                                                                                 childUi.PosOnCanvas + childUi.Size));
                _usableScreenRect.Floor();
                _selectableScreenRect = _usableScreenRect;

                if (UserSettings.Config.ShowThumbnails)
                    PreparePreviewAndExpandSelectableArea(instance);

                var drawList = GraphCanvas.Current.DrawList;

                // Resize indicator
                if (childUi.Style == SymbolChildUi.Styles.Resizable)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);
                    ImGui.SetCursorScreenPos(_usableScreenRect.Max - new Vector2(10, 10));
                    ImGui.Button("##resize", new Vector2(10, 10));
                    if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                    {
                        var delta = GraphCanvas.Current.InverseTransformDirection(ImGui.GetIO().MouseDelta);
                        childUi.Size += delta;
                    }

                    ImGui.SetMouseCursor(ImGuiMouseCursor.Arrow);
                }

                // Rendering
                //var childInstance = GraphCanvas.Current.CompositionOp.Children.SingleOrDefault(c => c.SymbolChildId == childUi.SymbolChild.Id);

                var typeColor = childUi.SymbolChild.Symbol.OutputDefinitions.Count > 0
                                    ? TypeUiRegistry.GetPropertiesForType(childUi.SymbolChild.Symbol.OutputDefinitions[0].ValueType).Color
                                    : Color.Gray;

                var backgroundColor = typeColor;
                if (framesSinceLastUpdate > 2)
                {
                    var fadeFactor = MathUtils.RemapAndClamp(framesSinceLastUpdate, 0f, 60f, 0f, 0.5f);
                    //backgroundColor.Rgba.W *= fadeFactor;
                    backgroundColor = Color.Mix(backgroundColor, Color.Black, fadeFactor);
                }

                // background
                var hoveredBackground = T3Ui.HoveredIdsLastFrame.Contains(instance.SymbolChildId);
                drawList.AddRectFilled(_usableScreenRect.Min, _usableScreenRect.Max,
                                       hoveredBackground
                                           ? ColorVariations.OperatorHover.Apply(backgroundColor)
                                           : ColorVariations.Operator.Apply(backgroundColor));

                // Custom ui
                customUiResult = childUi.DrawCustomUi(instance, _drawList, _selectableScreenRect);

                // Size toggle
                if (customUiResult == SymbolChildUi.CustomUiResult.None && GraphCanvas.Current.Scale.X > 0.7f)
                {
                    var pos = new Vector2(_usableScreenRect.Max.X - 15, _usableScreenRect.Min.Y + 2);

                    ImGui.SetCursorScreenPos(pos);
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
                    ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Color(1, 1, 1, .3f).Rgba);
                    ImGui.PushStyleColor(ImGuiCol.Text, new Color(1, 1, 1, .3f).Rgba);
                    ImGui.PushFont(Icons.IconFont);

                    if (childUi.Style == SymbolChildUi.Styles.Default)
                    {
                        if (ImGui.Button(UnfoldLabel, new Vector2(16, 16)))
                        {
                            childUi.Style = SymbolChildUi.Styles.Expanded;
                        }
                    }
                    else if (childUi.Style != SymbolChildUi.Styles.Default)
                    {
                        if (ImGui.Button(FoldLabel, new Vector2(16, 16)))
                        {
                            childUi.Style = SymbolChildUi.Styles.Default;
                        }
                    }

                    ImGui.PopFont();
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(3);
                }

                // Interaction
                ImGui.SetCursorScreenPos(_selectableScreenRect.Min);

                //--------------------------------------------------------------------------
                ImGui.InvisibleButton("node", _selectableScreenRect.GetSize());
                //--------------------------------------------------------------------------

                SelectableNodeMovement.Handle(childUi, instance);

                // Tooltip
                if (ImGui.IsItemHovered()
                    && (customUiResult & SymbolChildUi.CustomUiResult.PreventTooltip) != SymbolChildUi.CustomUiResult.PreventTooltip
                    && !GraphCanvas.Current._symbolBrowser._isOpen)
                {
                    SelectableNodeMovement.HighlightSnappedNeighbours(childUi);

                    //ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    T3Ui.AddHoveredId(childUi.SymbolChild.Id);

                    ImGui.SetNextWindowSizeConstraints(new Vector2(200, 120), new Vector2(200, 120));
                    if (UserSettings.Config.HoverMode != GraphCanvas.HoverModes.Disabled
                        && !ImGui.IsMouseDragging(ImGuiMouseButton.Left)
                        && !RenameInstanceOverlay.IsOpen)
                    {
                        ImGui.BeginTooltip();
                        {
                            TransformGizmoHandling.SetDrawList(drawList);
                            ImageCanvasForTooltips.Update();
                            ImageCanvasForTooltips.SetAsCurrent();
                            if (instance.Outputs.Count > 0)
                            {
                                var firstOutput = instance.Outputs[0];
                                IOutputUi outputUi = symbolUi.OutputUis[firstOutput.Id];
                                EvaluationContext.Reset();
                                EvaluationContext.RequestedResolution = new Size2(1280 / 2, 720 / 2);
                                outputUi.DrawValue(firstOutput, EvaluationContext, recompute: UserSettings.Config.HoverMode == GraphCanvas.HoverModes.Live);
                            }

                            if (!string.IsNullOrEmpty(symbolUi.Description))
                            {
                                ImGui.Spacing();
                                ImGui.PushFont(Fonts.FontSmall);
                                ImGui.PushStyleColor(ImGuiCol.Text, new Color(1, 1, 1, 0.5f).Rgba);
                                ImGui.TextWrapped(symbolUi.Description);
                                ImGui.PopStyleColor();
                                ImGui.PopFont();
                            }

                            ImageCanvasForTooltips.Deactivate();
                            TransformGizmoHandling.StopDrawList();
                        }
                        ImGui.EndTooltip();
                    }
                }

                //if(ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                // A work around to detect if node is below mouse while dragging end of new connection
                if (_selectableScreenRect.Contains(ImGui.GetMousePos()))
                {
                    _hoveredNodeIdForConnectionTarget = childUi.Id;
                }

                var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup) || T3Ui.HoveredIdsLastFrame.Contains(instance.SymbolChildId);

                // A horrible work around to prevent exception because CompositionOp changed during drawing.
                // A better solution would defer setting the compositionOp to the beginning of next frame.
                var justOpenedChild = false;
                if (hovered && ImGui.IsMouseDoubleClicked(0) && !RenameInstanceOverlay.IsOpen)
                {
                    GraphCanvas.Current.SetCompositionToChildInstance(instance);
                    ImGui.CloseCurrentPopup();
                    justOpenedChild = true;
                }

                // Show Parameter window as context menu
                var isClicked = ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
                var clickWasDrag = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0).LengthSquared() > 1;

                if (isClicked
                    && !clickWasDrag
                    && !ParameterWindow.IsAnyInstanceVisible()
                    && !justOpenedChild
                    && string.IsNullOrEmpty(T3Ui.OpenedPopUpName)
                    && (customUiResult & SymbolChildUi.CustomUiResult.PreventOpenParameterPopUp) == 0)
                {
                    SelectionManager.SetSelection(childUi, instance);
                    ImGui.OpenPopup("parameterContextPopup");
                }

                ImGui.SetNextWindowSizeConstraints(new Vector2(280, 40), new Vector2(280, 320));
                if (!justOpenedChild && ImGui.BeginPopup("parameterContextPopup"))
                {
                    ImGui.PushFont(Fonts.FontSmall);
                    var compositionSymbolUi = SymbolUiRegistry.Entries[GraphCanvas.Current.CompositionOp.Symbol.Id];
                    var symbolChildUi = compositionSymbolUi.ChildUis.Single(symbolChildUi2 => symbolChildUi2.Id == instance.SymbolChildId);
                    ParameterWindow.DrawParameters(instance, symbolUi, symbolChildUi, compositionSymbolUi);
                    ImGui.PopFont();
                    ImGui.EndPopup();
                }

                DrawPreview();

                // outline
                drawList.AddRect(_selectableScreenRect.Min,
                                 _selectableScreenRect.Max + Vector2.One,
                                 new Color(0.08f, 0.08f, 0.08f, 0.8f),
                                 rounding: 0,
                                 ImDrawCornerFlags.None);

                // Animation indicator
                {
                    var compositionOp = GraphCanvas.Current.CompositionOp;
                    if (compositionOp.Symbol.Animator.IsInstanceAnimated(instance))
                    {
                        _drawList.AddRectFilled(new Vector2(_usableScreenRect.Max.X - 5, _usableScreenRect.Max.Y - 12),
                                                new Vector2(_usableScreenRect.Max.X - 2, _usableScreenRect.Max.Y - 3),
                                                Color.Orange);
                    }
                }

                // Hidden inputs indicator
                if (nodeHasHiddenMatchingInputs)
                {
                    var blink = (float)(Math.Sin(ImGui.GetTime() * 10) / 2f + 0.5f);
                    var colorForType = TypeUiRegistry.Entries[ConnectionMaker.TempConnections[0].ConnectionType].Color;
                    colorForType.Rgba.W *= blink;
                    _drawList.AddRectFilled(
                                            new Vector2(_usableScreenRect.Min.X, _usableScreenRect.Max.Y + 3),
                                            new Vector2(_usableScreenRect.Min.X + 10, _usableScreenRect.Max.Y + 5),
                                            colorForType);
                }

                // Label
                if (customUiResult == SymbolChildUi.CustomUiResult.None)
                {
                    drawList.PushClipRect(_usableScreenRect.Min, _usableScreenRect.Max, true);
                    ImGui.PushFont(GraphCanvas.Current.Scale.X < 1 ? Fonts.FontSmall : Fonts.FontBold);
                    var isRenamed = !string.IsNullOrEmpty(childUi.SymbolChild.Name);

                    drawList.AddText(_usableScreenRect.Min + LabelPos,
                                     ColorVariations.OperatorLabel.Apply(typeColor),
                                     string.Format(isRenamed ? ("\"" + childUi.SymbolChild.ReadableName + "\"") : childUi.SymbolChild.ReadableName));
                    ImGui.PopFont();
                    drawList.PopClipRect();
                }

                if (childUi.IsSelected)
                {
                    drawList.AddRect(_selectableScreenRect.Min - Vector2.One * 2, _selectableScreenRect.Max + Vector2.One * 2, Color.Black);
                    drawList.AddRect(_selectableScreenRect.Min - Vector2.One, _selectableScreenRect.Max + Vector2.One, Color.White);
                }
            }
            ImGui.PopID();

            var connectionBorderArea = _selectableScreenRect;
            connectionBorderArea.Min.X -= 4;

            // Input Sockets...
            for (var inputIndex = 0; inputIndex < visibleInputUis.Count; inputIndex++)
            {
                var inputUi = visibleInputUis[inputIndex];
                var inputDefinition = inputUi.InputDefinition;

                var usableSlotArea = GetUsableInputSlotSize(inputIndex, visibleInputUis.Count);

                ImGui.PushID(childUi.SymbolChild.Id.GetHashCode() + inputDefinition.GetHashCode());
                ImGui.SetCursorScreenPos(usableSlotArea.Min);
                ImGui.InvisibleButton("input", usableSlotArea.GetSize());
                THelpers.DebugItemRect("input-slot");

                // Note: isItemHovered does not work when being dragged from another item
                var hovered = ConnectionMaker.TempConnections.Count > 0
                                  ? usableSlotArea.Contains(ImGui.GetMousePos())
                                  : ImGui.IsItemHovered();

                var isPotentialConnectionTarget = ConnectionMaker.IsMatchingInputType(inputDefinition.DefaultValue.ValueType);
                var colorForType = ColorForInputType(inputDefinition);

                var connectedLines = Graph.Connections.GetLinesToNodeInputSlot(childUi, inputDefinition.Id);

                // Render input Label
                if ((customUiResult & SymbolChildUi.CustomUiResult.PreventInputLabels) == 0)
                {
                    var inputLabelOpacity = MathUtils.RemapAndClamp(GraphCanvas.Current.Scale.X,
                                                                    0.75f, 1.5f,
                                                                    0f, 1f);

                    var screenCursor = usableSlotArea.GetCenter() + new Vector2(14, -7);
                    if (inputLabelOpacity > 0)
                    {
                        ImGui.PushFont(Fonts.FontSmall);
                        var labelColor = ColorVariations.OperatorLabel.Apply(colorForType);
                        labelColor.Rgba.W = inputLabelOpacity;
                        var label = inputDefinition.Name;
                        if (inputDefinition.IsMultiInput)
                        {
                            label = "  " + label + " [...]";
                        }

                        var labelSize = ImGui.CalcTextSize(label);
                        _drawList.AddText(screenCursor, labelColor, label);

                        screenCursor += new Vector2(labelSize.X + 8, 0);

                        // Value
                        ImGui.PushStyleColor(ImGuiCol.Text, labelColor.Rgba);
                        var inputSlot = instance.Inputs.Single(slot => inputDefinition.Id == slot.Id);
                        var valueAsString = inputUi.GetSlotValue(inputSlot);

                        var valueColor = labelColor;
                        valueColor.Rgba.W *= 0.6f;
                        _drawList.AddText(screenCursor, valueColor, valueAsString);
                        ImGui.PopStyleColor();

                        ImGui.PopFont();
                    }
                }

                // Draw input slots
                if (inputDefinition.IsMultiInput)
                {
                    var showGaps = isPotentialConnectionTarget;

                    var socketCount = showGaps
                                          ? connectedLines.Count * 2 + 1
                                          : connectedLines.Count;

                    var socketHeight = (usableSlotArea.GetHeight() + 1) / socketCount;
                    var targetPos = new Vector2(usableSlotArea.Max.X - 4,
                                                usableSlotArea.Min.Y + socketHeight * 0.5f);

                    var topLeft = new Vector2(usableSlotArea.Min.X, usableSlotArea.Min.Y);
                    var socketSize = new Vector2(usableSlotArea.GetWidth(), socketHeight - SlotGaps);

                    var reactiveSlotColor = GetReactiveSlotColor(inputDefinition.DefaultValue.ValueType, colorForType, SocketDirections.Input);

                    for (var socketIndex = 0; socketIndex < socketCount; socketIndex++)
                    {
                        var usableSocketArea = new ImRect(topLeft, topLeft + socketSize);

                        var isSocketHovered = usableSocketArea.Contains(ImGui.GetMousePos());
                        ConnectionMaker.ConnectionSnapEndHelper.RegisterAsPotentialTarget(childUi, inputUi, socketIndex, usableSocketArea);

                        bool isGap = false;
                        if (showGaps)
                        {
                            isGap = (socketIndex & 1) == 0;
                        }

                        if (!isGap)
                        {
                            var line = showGaps
                                           ? connectedLines[socketIndex >> 1]
                                           : connectedLines[socketIndex];
                            if (socketHeight > 10)
                            {
                                ImGui.PushStyleVar(ImGuiStyleVar.Alpha,
                                                   MathUtils.RemapAndClamp(socketHeight, 10, 20, 0, 0.5f).Clamp(0, 0.5f));
                                ImGui.PushFont(Fonts.FontSmall);
                                //ImGui.SetCursorScreenPos(targetPos +  new Vector2(0, -ImGui.GetFontSize()/2));
                                //ImGui.Value(socketIndex % 4 == 0 ? ">" : "", socketIndex);

                                var sockedInputIndex = showGaps ? socketIndex / 2 : socketIndex;
                                var markerForFourAligned = sockedInputIndex % 4 == 0 ? " <" : "";
                                _drawList.AddText(targetPos + new Vector2(7, -ImGui.GetFontSize() / 2),
                                                  new Color(MathUtils.RemapAndClamp(socketHeight, 10, 20, 0, 0.5f).Clamp(0, 0.5f)),
                                                  $"{sockedInputIndex}" + markerForFourAligned);
                                ImGui.PopFont();
                                ImGui.PopStyleVar();
                            }

                            line.TargetPosition = targetPos;
                            line.TargetNodeArea = connectionBorderArea;
                            line.IsSelected |= childUi.IsSelected;
                            line.FramesSinceLastUsage = framesSinceLastUpdate;
                            line.IsAboutToBeReplaced = ConnectionMaker.ConnectionSnapEndHelper.IsNextBestTarget(childUi, inputDefinition.Id, socketIndex);
                        }

                        DrawMultiInputSocket(childUi, inputDefinition, usableSocketArea, isSocketHovered, socketIndex, isGap, colorForType, reactiveSlotColor);

                        targetPos.Y += socketHeight;
                        topLeft.Y += socketHeight;
                    }

                    _drawList.AddRectFilled(new Vector2(usableSlotArea.Max.X - 8, usableSlotArea.Min.Y),
                                            new Vector2(usableSlotArea.Max.X - 1, usableSlotArea.Min.Y + 2),
                                            reactiveSlotColor);

                    _drawList.AddRectFilled(new Vector2(usableSlotArea.Max.X - 8, usableSlotArea.Max.Y - 2),
                                            new Vector2(usableSlotArea.Max.X - 1, usableSlotArea.Max.Y),
                                            reactiveSlotColor);
                }
                else
                {
                    ConnectionMaker.ConnectionSnapEndHelper.RegisterAsPotentialTarget(childUi, inputUi, 0, usableSlotArea);
                    //ConnectionMaker.ConnectionSnapEndHelper.IsNextBestTarget(targetUi, inputDef.Id,0)
                    var isAboutToBeReconnected = ConnectionMaker.ConnectionSnapEndHelper.IsNextBestTarget(childUi, inputDefinition.Id, 0);
                    foreach (var line in connectedLines)
                    {
                        line.TargetPosition = new Vector2(usableSlotArea.Max.X - 4,
                                                          usableSlotArea.GetCenter().Y);
                        line.TargetNodeArea = connectionBorderArea;
                        line.IsSelected |= childUi.IsSelected;
                        line.IsAboutToBeReplaced = isAboutToBeReconnected;
                        line.FramesSinceLastUsage = framesSinceLastUpdate;
                    }

                    DrawInputSlot(childUi, inputDefinition, usableSlotArea, colorForType, hovered);
                }

                ImGui.PopID();
            }

            // Outputs sockets...
            var outputIndex = 0;
            //foreach(var output in instance.Outputs)
            foreach (var outputDef in childUi.SymbolChild.Symbol.OutputDefinitions)
            {
                var output = instance.Outputs[outputIndex];
                var usableArea = GetUsableOutputSlotArea(childUi, outputIndex);
                ImGui.SetCursorScreenPos(usableArea.Min);
                ImGui.PushID(childUi.SymbolChild.Id.GetHashCode() + outputDef.Id.GetHashCode());

                ImGui.InvisibleButton("output", usableArea.GetSize());
                THelpers.DebugItemRect();
                var valueType = outputDef.ValueType;
                var colorForType = TypeUiRegistry.Entries[valueType].Color;

                //Note: isItemHovered does not work when dragging is active
                var hovered = ConnectionMaker.TempConnections.Count > 0
                                  ? usableArea.Contains(ImGui.GetMousePos())
                                  : ImGui.IsItemHovered();

                // Update connection lines
                var dirtyFlagNumUpdatesWithinFrame = output.DirtyFlag.NumUpdatesWithinFrame;

                foreach (var line in Graph.Connections.GetLinesFromNodeOutput(childUi, outputDef.Id))
                {
                    line.SourcePosition = new Vector2(usableArea.Max.X, usableArea.GetCenter().Y);
                    line.SourceNodeArea = _selectableScreenRect;

                    line.ColorForType = colorForType;
                    line.UpdateCount = output.DirtyFlag.NumUpdatesWithinFrame;

                    if (childUi.ConnectionStyleOverrides.ContainsKey(outputDef.Id))
                    {
                        line.ColorForType.Rgba.W = 0.3f;
                    }

                    line.IsSelected |= childUi.IsSelected;
                }

                DrawOutput(childUi, outputDef, usableArea, colorForType, hovered);

                // Visualize update
                {
                    if (dirtyFlagNumUpdatesWithinFrame > 0)
                    {
                        var movement = (float)(ImGui.GetTime() * dirtyFlagNumUpdatesWithinFrame) % 1f * (usableArea.GetWidth() - 1);
                        _drawList.AddRectFilled(new Vector2(usableArea.Min.X + movement - 1, usableArea.Min.Y),
                                                new Vector2(usableArea.Min.X + movement + 1, usableArea.Max.Y),
                                                new Color(0.2f));
                    }
                }

                outputIndex++;
            }
        }

        private static void AdjustGroupLayoutAfterResize(ISelectableNode childUi, Vector2 newNodeSize)
        {
            if (childUi.Size == newNodeSize)
                return;

            var parentUi = SymbolUiRegistry.Entries[GraphCanvas.Current.CompositionOp.Symbol.Id];
            var groupMembers = SelectableNodeMovement.FindSnappedNeighbours(parentUi, childUi);
            if (groupMembers.Count > 0)
            {
                var heightDelta = newNodeSize.Y - childUi.Size.Y;
                var offset = new Vector2(0, heightDelta);
                if (heightDelta > 0)
                {
                    foreach (var neighbour in groupMembers)
                    {
                        if (neighbour == childUi)
                            return;

                        if (neighbour.PosOnCanvas.Y > childUi.PosOnCanvas.Y
                            && Math.Abs(neighbour.PosOnCanvas.X - childUi.PosOnCanvas.X) < SelectableNodeMovement.Tolerance)
                        {
                            neighbour.PosOnCanvas += offset;
                        }
                    }
                }

                else if (heightDelta < 0)
                {
                    foreach (var neighbour in groupMembers)
                    {
                        if (neighbour == childUi)
                            return;

                        if (neighbour.PosOnCanvas.Y > childUi.PosOnCanvas.Y
                            && Math.Abs(neighbour.PosOnCanvas.X - childUi.PosOnCanvas.X) < SelectableNodeMovement.Tolerance)
                        {
                            neighbour.PosOnCanvas += offset;
                        }
                    }
                }
            }

            childUi.Size = newNodeSize;
        }

        // Find visible input slots.
        // TODO: this is a major performance hot spot and needs optimization
        private static List<IInputUi> FindVisibleInputUis(SymbolUi symbolUi, SymbolChildUi childUi, ref bool nodeHasHiddenMatchingInputs)
        {
            var connectionsToNode = Graph.Connections.GetLinesIntoNode(childUi);

            if (childUi.Style == SymbolChildUi.Styles.Expanded)
            {
                return symbolUi.InputUis.Values.ToList();
            }

            var isNodeHoveredAsConnectionTarget = _hoveredNodeIdForConnectionTarget == childUi.Id
                                                  && ConnectionMaker.TempConnections != null
                                                  && ConnectionMaker.TempConnections.Count == 1
                                                  && ConnectionMaker.TempConnections[0].TargetParentOrChildId == ConnectionMaker.NotConnectedId
                                                  && ConnectionMaker.TempConnections[0].SourceParentOrChildId != childUi.Id;

            VisibleInputs.Clear();
            foreach (var inputUi in symbolUi.InputUis.Values)
            {
                bool inputIsConnectionTarget = false;
                for (int i = 0; i < connectionsToNode.Count; i++)
                {
                    if (connectionsToNode[i].Connection.TargetSlotId == inputUi.Id)
                    {
                        inputIsConnectionTarget = true;
                        break;
                    }
                }

                if (inputUi.Relevancy != Relevancy.Optional || inputIsConnectionTarget)
                {
                    VisibleInputs.Add(inputUi);
                }
                else if (ConnectionMaker.IsMatchingInputType(inputUi.Type))
                {
                    if (isNodeHoveredAsConnectionTarget)
                    {
                        VisibleInputs.Add(inputUi);
                    }
                    else
                    {
                        nodeHasHiddenMatchingInputs = true;
                    }
                }
            }

            return VisibleInputs;
        }

        private enum SocketDirections
        {
            Input,
            Output,
        }

        private static Color GetReactiveSlotColor(Type type, Color colorForType, SocketDirections direction)
        {
            var style = direction == SocketDirections.Input
                            ? ColorVariations.ConnectionLines
                            : ColorVariations.Operator;
            if (ConnectionMaker.TempConnections.Count > 0)
            {
                if (direction == SocketDirections.Input
                        ? ConnectionMaker.IsMatchingInputType(type)
                        : ConnectionMaker.IsMatchingOutputType(type))
                {
                    var blink = (float)(Math.Sin(ImGui.GetTime() * 10) / 2f + 0.5f);
                    colorForType.Rgba.W *= blink;
                    style = ColorVariations.Highlight;
                }
                else
                {
                    style = ColorVariations.Muted;
                }
            }

            return style.Apply(colorForType);
        }

        /// <summary>
        /// Set
        /// </summary>
        /// <param name="instance"></param>
        private static void PreparePreviewAndExpandSelectableArea(Instance instance)
        {
            _previewTextureView = null;
            if (instance.Outputs.Count == 0)
                return;

            var firstOutput = instance.Outputs[0];
            if (!(firstOutput is Slot<Texture2D> textureSlot))
                return;

            var texture = textureSlot.Value;
            if (texture == null || texture.IsDisposed)
                return;

            _previewTextureView = SrvManager.GetSrvForTexture(texture);

            var aspect = (float)texture.Description.Width / texture.Description.Height;
            var opWidth = _usableScreenRect.GetWidth();
            var previewSize = new Vector2(opWidth, opWidth / aspect);

            if (previewSize.Y > opWidth)
            {
                previewSize *= opWidth / previewSize.Y;
            }

            var min = new Vector2(_usableScreenRect.Min.X, _usableScreenRect.Min.Y - previewSize.Y - 1);
            var max = new Vector2(_usableScreenRect.Min.X + previewSize.X, _usableScreenRect.Min.Y - 1);
            _selectableScreenRect.Add(min);
            _previewArea = new ImRect(min, max);
        }

        private static ImRect _previewArea;
        private static ShaderResourceView _previewTextureView;

        private static void DrawPreview()
        {
            if (_previewTextureView == null)
                return;

            Graph.DrawList.AddImage((IntPtr)_previewTextureView, _previewArea.Min, _previewArea.Max);
        }

        private static Vector2 ComputeNodeSize(SymbolChildUi childUi, List<IInputUi> visibleInputUis)
        {
            if (childUi.Style == SymbolChildUi.Styles.Resizable)
            {
                return childUi.Size;
            }

            var additionalMultiInputSlots = 0;
            foreach (var input in visibleInputUis)
            {
                if (!input.InputDefinition.IsMultiInput)
                    continue;

                //TODO: this should be refactored, because it's very slow and is later repeated
                var connectedLines = Graph.Connections.GetLinesToNodeInputSlot(childUi, input.Id);
                additionalMultiInputSlots += connectedLines.Count;
            }

            return new Vector2(SymbolChildUi.DefaultOpSize.X,
                               23 + (visibleInputUis.Count + additionalMultiInputSlots) * 13);
        }

        private static void DrawOutput(SymbolChildUi childUi, Symbol.OutputDefinition outputDef, ImRect usableArea, Color colorForType, bool hovered)
        {
            if (ConnectionMaker.IsOutputSlotCurrentConnectionSource(childUi, outputDef))
            {
                _drawList.AddRectFilled(usableArea.Min, usableArea.Max,
                                        ColorVariations.Highlight.Apply(colorForType));

                if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    ConnectionMaker.Update();
                }

                var isMouseReleasedWithoutDrag =
                    ImGui.IsMouseReleased(ImGuiMouseButton.Left) && ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).LengthSquared() < 4;
                if (isMouseReleasedWithoutDrag)
                {
                    //Graph.Connections.GetLinesFromNodeOutput(childUi, outputDef.Id);
                    GraphCanvas.Current.OpenSymbolBrowserForOutput(childUi, outputDef);
                }
            }
            else if (hovered)
            {
                if (ConnectionMaker.IsMatchingOutputType(outputDef.ValueType))
                {
                    _drawList.AddRectFilled(usableArea.Min, usableArea.Max,
                                            ColorVariations.OperatorHover.Apply(colorForType));

                    if (ImGui.IsMouseReleased(0))
                    {
                        ConnectionMaker.CompleteAtOutputSlot(GraphCanvas.Current.CompositionOp.Symbol, childUi, outputDef);
                    }
                }
                else
                {
                    _drawList.AddRectFilled(usableArea.Min, usableArea.Max,
                                            ColorVariations.OperatorHover.Apply(colorForType));

                    var instance = GraphCanvas.Current.CompositionOp.Children.Single(child => child.SymbolChildId == childUi.Id);
                    var output = instance.Outputs.Single(output2 => output2.Id == outputDef.Id);

                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 2));
                    ImGui.BeginTooltip();
                    ImGui.Text($".{outputDef.Name}");
                    ImGui.PushFont(Fonts.FontSmall);
                    ImGui.TextColored(Color.Gray, $"<{TypeNameRegistry.Entries[outputDef.ValueType]}>\n{output.DirtyFlag.NumUpdatesWithinFrame} Updates");
                    ImGui.PopFont();
                    ImGui.EndTooltip();
                    ImGui.PopStyleVar();
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    {
                        ConnectionMaker.StartFromOutputSlot(GraphCanvas.Current.CompositionOp.Symbol, childUi, outputDef);
                    }

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        GraphCanvas.Current.EditNodeOutputDialog.OpenForOutput(GraphCanvas.Current.CompositionOp.Symbol, childUi, outputDef);
                    }
                }
            }
            else
            {
                var color = GetReactiveSlotColor(outputDef.ValueType, colorForType, SocketDirections.Output);
                var pos = usableArea.Min;
                _drawList.AddRectFilled(
                                        pos,
                                        usableArea.Max,
                                        color
                                       );
            }
        }

        private static ImRect GetUsableOutputSlotArea(SymbolChildUi targetUi, int outputIndex)
        {
            var thickness = MathUtils.RemapAndClamp(GraphCanvas.Current.Scale.X, 0.5f, 1f, 3f, UsableSlotThickness);

            var opRect = _usableScreenRect;
            var outputCount = targetUi.SymbolChild.Symbol.OutputDefinitions.Count;
            var outputHeight = outputCount == 0
                                   ? opRect.GetHeight()
                                   : (opRect.GetHeight() - 1 + SlotGaps) / outputCount - SlotGaps;
            if (outputHeight <= 0)
                outputHeight = 1;

            return ImRect.RectWithSize(
                                       new Vector2(
                                                   opRect.Max.X + 1, // - GraphNode._usableSlotThickness,
                                                   opRect.Min.Y + (outputHeight + SlotGaps) * outputIndex + 1
                                                  ),
                                       new Vector2(
                                                   thickness,
                                                   outputHeight
                                                  ));
        }

        /// <summary>
        /// Draws slot for non multi-input
        /// </summary>
        private static void DrawInputSlot(SymbolChildUi targetUi, Symbol.InputDefinition inputDef, ImRect usableArea, Color colorForType, bool hovered)
        {
            if (ConnectionMaker.IsInputSlotCurrentConnectionTarget(targetUi, inputDef))
            {
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    ConnectionMaker.Update();
                }
            }
            else if (ConnectionMaker.ConnectionSnapEndHelper.IsNextBestTarget(targetUi, inputDef.Id, 0) || hovered)
            {
                if (ConnectionMaker.IsMatchingInputType(inputDef.DefaultValue.ValueType))
                {
                    _drawList.AddRectFilled(usableArea.Min, usableArea.Max,
                                            ColorVariations.OperatorHover.Apply(colorForType));

                    if (ImGui.IsMouseReleased(0))
                    {
                        ConnectionMaker.CompleteAtInputSlot(GraphCanvas.Current.CompositionOp.Symbol, targetUi, inputDef);
                    }
                }
                else
                {
                    _drawList.AddRectFilled(
                                            usableArea.Min,
                                            usableArea.Max,
                                            ColorVariations.OperatorHover.Apply(colorForType)
                                           );

                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 2));
                    Symbol.Connection connection;
                    SymbolChild sourceOp = null;
                    SymbolChild.Output output = null;
                    ImGui.BeginTooltip();
                    {
                        var connectionSource = "";
                        connection = GraphCanvas.Current.CompositionOp.Symbol.Connections.SingleOrDefault(c => c.TargetParentOrChildId == targetUi.Id
                                                                                                              && c.TargetSlotId == inputDef.Id);
                        if (connection != null)
                        {
                            sourceOp = GraphCanvas.Current.CompositionOp.Symbol.Children.SingleOrDefault(child => child.Id == connection.SourceParentOrChildId);
                            if (sourceOp != null)
                            {
                                output = sourceOp.Outputs[connection.SourceSlotId];
                                connectionSource = sourceOp.ReadableName + "." + output.OutputDefinition.Name;
                            }
                        }

                        if (!string.IsNullOrEmpty(connectionSource))
                        {
                            ImGui.PushFont(Fonts.FontSmall);
                            ImGui.TextColored(Color.Gray, $"{connectionSource} -> ");
                            ImGui.PopFont();
                        }

                        ImGui.Text($".{inputDef.Name}");
                        ImGui.PushFont(Fonts.FontSmall);
                        ImGui.TextColored(Color.Gray, $"<{TypeNameRegistry.Entries[inputDef.DefaultValue.ValueType]}>");
                        ImGui.PopFont();
                    }
                    ImGui.EndTooltip();
                    ImGui.PopStyleVar();

                    if (ImGui.IsItemClicked(0))
                    {
                        var createCopy = ImGui.GetIO().KeyCtrl && connection != null;
                        if (createCopy)
                        {
                            if (sourceOp != null)
                            {
                                Log.Debug("Cloning connection from source op...");
                                var parentUi = SymbolUiRegistry.Entries[GraphCanvas.Current.CompositionOp.Symbol.Id];
                                var sourceOpUi = parentUi.ChildUis.Single(ui => ui.Id == sourceOp.Id);
                                ConnectionMaker.StartFromOutputSlot(GraphCanvas.Current.CompositionOp.Symbol, sourceOpUi, output.OutputDefinition);
                            }
                            else if (connection.IsConnectedToSymbolInput)
                            {
                                Log.Debug("Cloning connection from input node...");
                                var inputDef2 = GraphCanvas.Current.CompositionOp.Symbol.InputDefinitions.Single(id => id.Id == connection.SourceSlotId);
                                ConnectionMaker.StartFromInputNode(inputDef2);
                            }
                            else
                            {
                                Log.Warning("This should not happen. Please contact customer support.");
                            }
                        }
                        else
                        {
                            ConnectionMaker.StartFromInputSlot(GraphCanvas.Current.CompositionOp.Symbol, targetUi, inputDef);
                        }
                    }
                }
            }
            else
            {
                var connectionColor = GetReactiveSlotColor(inputDef.DefaultValue.ValueType, colorForType, SocketDirections.Input);
                var pos = new Vector2(
                                      usableArea.Max.X - GraphNode.InputSlotThickness - InputSlotMargin,
                                      usableArea.Min.Y
                                     );
                var size = new Vector2(GraphNode.InputSlotThickness, usableArea.GetHeight());
                _drawList.AddRectFilled(
                                        pos,
                                        pos + size,
                                        connectionColor
                                       );
            }
        }

        private static void DrawMultiInputSocket(SymbolChildUi targetUi, Symbol.InputDefinition inputDef, ImRect usableArea,
                                                 bool isInputHovered, int multiInputIndex, bool isGap, Color colorForType,
                                                 Color reactiveSlotColor)
        {
            if (ConnectionMaker.IsInputSlotCurrentConnectionTarget(targetUi, inputDef, multiInputIndex))
            {
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    ConnectionMaker.Update();
                }
            }
            else if (ConnectionMaker.ConnectionSnapEndHelper.IsNextBestTarget(targetUi, inputDef.Id, multiInputIndex) || isInputHovered)
            {
                if (ConnectionMaker.IsMatchingInputType(inputDef.DefaultValue.ValueType))
                {
                    _drawList.AddRectFilled(usableArea.Min, usableArea.Max,
                                            ColorVariations.OperatorHover.Apply(colorForType));

                    if (ImGui.IsMouseReleased(0))
                    {
                        ConnectionMaker.CompleteAtInputSlot(GraphCanvas.Current.CompositionOp.Symbol, targetUi, inputDef, multiInputIndex);
                    }
                }
                else
                {
                    _drawList.AddRectFilled(
                                            usableArea.Min,
                                            usableArea.Max,
                                            ColorVariations.OperatorHover.Apply(colorForType)
                                           );

                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 2));
                    ImGui.BeginTooltip();
                    {
                        var connectionSource = "";
                        var connections = GraphCanvas.Current.CompositionOp.Symbol.Connections.Where(c => c.TargetParentOrChildId == targetUi.Id
                                                                                                         && c.TargetSlotId == inputDef.Id).ToList();
                        if (connections.Count > 0 && connections.Count > multiInputIndex)
                        {
                            var connection = connections[multiInputIndex];

                            var sourceOp =
                                GraphCanvas.Current.CompositionOp.Symbol.Children.SingleOrDefault(child => child.Id == connection.SourceParentOrChildId);
                            if (sourceOp != null)
                            {
                                var output = sourceOp.Outputs[connection.SourceSlotId];
                                connectionSource = sourceOp.ReadableName + "." + output.OutputDefinition.Name;
                                //connectionSource = sourceOp.ReadableName;
                            }
                        }

                        if (!string.IsNullOrEmpty(connectionSource))
                        {
                            ImGui.PushFont(Fonts.FontSmall);
                            ImGui.TextColored(Color.Gray, $"{connectionSource} -> ");
                            ImGui.PopFont();
                        }

                        ImGui.Text($".{inputDef.Name}");
                        ImGui.PushFont(Fonts.FontSmall);
                        ImGui.TextColored(Color.Gray, $"<{TypeNameRegistry.Entries[inputDef.DefaultValue.ValueType]}>");
                        ImGui.PopFont();
                        //ImGui.PopStyleVar();
                    }
                    ImGui.EndTooltip();
                    ImGui.PopStyleVar();
                    //ImGui.SetTooltip($"-> .{inputDef.Name}[{multiInputIndex}] <{TypeNameRegistry.Entries[inputDef.DefaultValue.ValueType]}>");
                    if (ImGui.IsItemClicked(0))
                    {
                        ConnectionMaker.StartFromInputSlot(GraphCanvas.Current.CompositionOp.Symbol, targetUi, inputDef, multiInputIndex);
                        Log.Debug("started connection at MultiInputIndex:" + multiInputIndex);
                    }
                }
            }
            else
            {
                //var pos = usableArea.Min + Vector2.UnitY * GraphNode._inputSlotMargin;
                var gapOffset = isGap ? new Vector2(2, 0) : Vector2.Zero;
                var pos = new Vector2(usableArea.Max.X - InputSlotMargin - InputSlotThickness,
                                      usableArea.Min.Y) - gapOffset;
                var size = new Vector2(InputSlotThickness, usableArea.GetHeight()) + gapOffset;
                _drawList.AddRectFilled(
                                        pos,
                                        pos + size,
                                        reactiveSlotColor
                                       );
            }
        }

        private static float _nodeTitleHeight = 22;

        private static ImRect GetUsableInputSlotSize(int inputIndex, int visibleSlotCount)
        {
            var areaForParams = new ImRect(new Vector2(
                                                       _usableScreenRect.Min.X,
                                                       _usableScreenRect.Min.Y + _nodeTitleHeight),
                                           _usableScreenRect.Max);
            var inputHeight = visibleSlotCount == 0
                                  ? areaForParams.GetHeight()
                                  : (areaForParams.GetHeight() + SlotGaps) / visibleSlotCount - SlotGaps;
            if (inputHeight <= 0)
                inputHeight = 1;

            return ImRect.RectWithSize(
                                       new Vector2(
                                                   areaForParams.Min.X - UsableSlotThickness,
                                                   Math.Min(_selectableScreenRect.Max.Y, areaForParams.Min.Y + (inputHeight + SlotGaps) * inputIndex)
                                                  ),
                                       new Vector2(
                                                   UsableSlotThickness,
                                                   inputHeight
                                                  ));
        }

        private static Color ColorForInputType(Symbol.InputDefinition inputDef)
        {
            return TypeUiRegistry.Entries[inputDef.DefaultValue.ValueType].Color;
        }

        #region style variables
        public static Vector2 LabelPos = new Vector2(4, 2);
        public static float UsableSlotThickness = 10;
        public static float InputSlotThickness = 3;
        public static float InputSlotMargin = 1;
        public static float SlotGaps = 2;
        public static float OutputSlotMargin = 1;
        #endregion

        private static readonly string UnfoldLabel = (char)Icon.ChevronLeft + "##size";
        private static readonly string FoldLabel = (char)Icon.ChevronDown + "##size";
        private static readonly List<IInputUi> VisibleInputs = new List<IInputUi>(15); // A static variable to avoid GC allocations

        private static readonly EvaluationContext EvaluationContext = new EvaluationContext();

        private static readonly ImageOutputCanvas ImageCanvasForTooltips = new ImageOutputCanvas();
        private static Guid _hoveredNodeIdForConnectionTarget;

        private static ImRect _usableScreenRect;
        private static ImRect _selectableScreenRect;
        private static ImDrawListPtr _drawList;
    }
}