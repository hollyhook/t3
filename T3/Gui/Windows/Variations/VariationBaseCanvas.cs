﻿using System;
using System.Collections.Generic;
using System.Linq;
using DelaunayVoronoi;
using ImGuiNET;
using SharpDX.Direct3D11;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Gui.Interaction;
using T3.Gui.Interaction.Variations.Model;
using T3.Gui.OutputUi;
using T3.Gui.Selection;
using T3.Gui.Styling;
using T3.Gui.UiHelpers;
using T3.Gui.Windows.Exploration;
using T3.Gui.Windows.Output;
using UiHelpers;
using Vector2 = System.Numerics.Vector2;

namespace T3.Gui.Windows.Variations
{
    public abstract class VariationBaseCanvas : ScalableCanvas, ISelectionContainer
    {
        public abstract Variation CreateVariation();
        public abstract void DrawToolbarFunctions();
        public abstract string GetTitle();

        protected abstract Instance InstanceForBlendOperations { get; }
        protected abstract SymbolVariationPool PoolForBlendOperations { get; }
        protected abstract void DrawAdditionalContextMenuContent();

        public void Draw(ImDrawListPtr drawList)
        {
            // Complete deferred actions
            if (!T3Ui.IsCurrentlySaving && KeyboardBinding.Triggered(UserActions.DeleteSelection))
                DeleteSelectedElements();

            RenderThumbnails();
            UpdateCanvas();
            HandleFenceSelection();

            // Blending...
            HandleBlendingInteraction();

            _thumbnailCanvasRendering.InitializeCanvasTexture(VariationThumbnail.ThumbnailSize);

            ImGui.PushFont(Fonts.FontLarge);
            ImGui.SetCursorPos( new Vector2(10,35));
            ImGui.PushStyleColor(ImGuiCol.Text, Color.Gray.Rgba);
            ImGui.TextUnformatted(GetTitle());
            ImGui.PopStyleColor();
            ImGui.PopFont();
            
            // Draw thumbnails...
            var modified = false;
            for (var index = 0; index < PoolForBlendOperations.Variations.Count; index++)
            {
                modified |= VariationThumbnail.Draw(this,
                                                    PoolForBlendOperations.Variations[index],
                                                    drawList,
                                                    _thumbnailCanvasRendering.CanvasTextureSrv,
                                                    GetUvRectForIndex(index));
            }

            DrawBlendingOverlay(drawList);

            if (modified)
                PoolForBlendOperations.SaveVariationsToFile();

            DrawContextMenu();
        }

        private void RenderThumbnails()
        {
            var viewNeedsRefresh = false;

            // Render variations to pinned output
            if (OutputWindow.OutputWindowInstances.FirstOrDefault(window => window.Config.Visible) is OutputWindow outputWindow)
            {
                var renderInstance = outputWindow.ShownInstance;
                if (renderInstance is { Outputs: { Count: > 0 } }
                    && renderInstance.Outputs[0] is Slot<Texture2D> textureSlot)
                {
                    _thumbnailCanvasRendering.InitializeCanvasTexture(VariationThumbnail.ThumbnailSize);

                    if (renderInstance != _lastRenderInstance)
                    {
                        viewNeedsRefresh = true;
                        _lastRenderInstance = renderInstance;
                    }

                    var symbolUi = SymbolUiRegistry.Entries[renderInstance.Symbol.Id];
                    if (symbolUi.OutputUis.ContainsKey(textureSlot.Id))
                    {
                        var outputUi = symbolUi.OutputUis[textureSlot.Id];
                        UpdateNextVariationThumbnail(outputUi, textureSlot);
                    }
                }
            }

            // Get instance for variations
            var instance = InstanceForBlendOperations;
            var instanceChanged = instance != _instance;
            viewNeedsRefresh |= instanceChanged;

            if (viewNeedsRefresh)
            {
                RefreshView();
                _instance = instance;
            }
        }

        private void DrawBlendingOverlay(ImDrawListPtr drawList)
        {
            if (IsBlendingActive)
            {
                var mousePos = ImGui.GetMousePos();
                if (_blendPoints.Count == 1)
                {
                    PoolForBlendOperations.BeginWeightedBlend(_instance, _blendVariations, _blendWeights, UserSettings.Config.PresetsResetToDefaultValues);

                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        PoolForBlendOperations.ApplyCurrentBlend();
                    }
                }
                else if (_blendPoints.Count == 2)
                {
                    foreach (var p in _blendPoints)
                    {
                        drawList.AddCircleFilled(p, 5, Color.Black.Fade(0.5f));
                        drawList.AddCircleFilled(p, 3, Color.White);
                    }

                    drawList.AddLine(_blendPoints[0], _blendPoints[1], Color.White, 2);
                    var blendPosition = _blendPoints[0] * _blendWeights[0] + _blendPoints[1] * _blendWeights[1];

                    drawList.AddCircleFilled(blendPosition, 5, Color.White);

                    PoolForBlendOperations.BeginWeightedBlend(_instance, _blendVariations, _blendWeights, UserSettings.Config.PresetsResetToDefaultValues);

                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        PoolForBlendOperations.ApplyCurrentBlend();
                    }
                }
                else if (_blendPoints.Count == 3)
                {
                    drawList.AddTriangleFilled(_blendPoints[0], _blendPoints[1], _blendPoints[2], Color.Black.Fade(0.3f));
                    foreach (var p in _blendPoints)
                    {
                        drawList.AddCircleFilled(p, 5, Color.Black.Fade(0.5f));
                        drawList.AddLine(mousePos, p, Color.White, 2);
                        drawList.AddCircleFilled(p, 3, Color.White);
                    }

                    drawList.AddCircleFilled(mousePos, 5, Color.White);
                    PoolForBlendOperations.BeginWeightedBlend(_instance, _blendVariations, _blendWeights, UserSettings.Config.PresetsResetToDefaultValues);

                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        PoolForBlendOperations.ApplyCurrentBlend();
                    }
                }
            }
        }

        private Vector2 HandleBlendingInteraction()
        {
            IsBlendingActive = (ImGui.IsWindowHovered() || ImGui.IsWindowFocused()) && ImGui.GetIO().KeyAlt;

            var mousePos = ImGui.GetMousePos();
            _blendPoints.Clear();
            _blendWeights.Clear();
            _blendVariations.Clear();

            if (IsBlendingActive)
            {
                foreach (var s in Selection.SelectedElements)
                {
                    _blendPoints.Add(GetNodeCenterOnScreen(s));
                    _blendVariations.Add(s as Variation);
                }

                if (Selection.SelectedElements.Count == 1)
                {
                    var posOnScreen = TransformPosition(_blendVariations[0].PosOnCanvas);
                    var sizeOnScreen = TransformDirection(_blendVariations[0].Size);
                    var a = (mousePos.X - posOnScreen.X) / sizeOnScreen.X;

                    _blendWeights.Add(a);
                }
                else if (Selection.SelectedElements.Count == 2)
                {
                    if (_blendPoints[0] == _blendPoints[1])
                    {
                        _blendWeights.Add(0.5f);
                        _blendWeights.Add(0.5f);
                    }
                    else
                    {
                        var v1 = _blendPoints[1] - _blendPoints[0];
                        var v2 = mousePos - _blendPoints[0];
                        var lengthV1 = v1.Length();

                        var a = Vector2.Dot(v1 / lengthV1, v2 / lengthV1);
                        _blendWeights.Add(1 - a);
                        _blendWeights.Add(a);
                    }
                }
                else if (Selection.SelectedElements.Count == 3)
                {
                    Barycentric(mousePos, _blendPoints[0], _blendPoints[1], _blendPoints[2], out var u, out var v, out var w);
                    _blendWeights.Add(u);
                    _blendWeights.Add(v);
                    _blendWeights.Add(w);
                }
                else
                {
                    var points = new List<DelaunayVoronoi.Point>();

                    Vector2 minPos = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                    Vector2 maxPos = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

                    foreach (var v in PoolForBlendOperations.Variations)
                    {
                        var vec2 = GetNodeCenterOnScreen(v);
                        minPos = Vector2.Min(vec2, minPos);
                        maxPos = Vector2.Max(vec2, maxPos);
                        points.Add(new Point(vec2.X, vec2.Y));
                    }

                    minPos -= Vector2.One * 100;
                    maxPos += Vector2.One * 100;

                    var triangulator = new DelaunayTriangulator();
                    var borderPoints = triangulator.SetBorder(new Point(minPos.X, minPos.Y), new Point(maxPos.X, maxPos.Y));
                    points.AddRange(borderPoints);

                    var triangles = triangulator.BowyerWatson(points);

                    foreach (var t in triangles)
                    {
                        var p0 = t.Vertices[0].ToVec2();
                        var p1 = t.Vertices[1].ToVec2();
                        var p2 = t.Vertices[2].ToVec2();
                        Barycentric(mousePos,
                                    p0,
                                    p1,
                                    p2,
                                    out var u,
                                    out var v,
                                    out var w);

                        var insideTriangle = u >= 0 && u <= 1 && v >= 0 && v <= 1 && w >= 0 && w <= 1;
                        if (!insideTriangle)
                            continue;
                        
                        _blendPoints.Clear();
                        _blendWeights.Clear();
                        _blendVariations.Clear();

                        var weights = new[] { u, v, w };

                        for (var vertexIndex = 0; vertexIndex < t.Vertices.Length; vertexIndex++)
                        {
                            var vertex = t.Vertices[vertexIndex];
                            var variationIndex = points.IndexOf(vertex);
                            if (variationIndex < PoolForBlendOperations.Variations.Count)
                            {
                                _blendVariations.Add(PoolForBlendOperations.Variations[variationIndex]);
                                _blendWeights.Add(weights[vertexIndex]);
                                _blendPoints.Add(vertex.ToVec2());
                            }
                        }

                        if (_blendWeights.Count == 2)
                        {
                            var sum = _blendWeights[0] + _blendWeights[1];
                            _blendWeights[0] /= sum;
                            _blendWeights[1] /= sum;
                        }
                        else if (_blendWeights.Count == 1)
                        {
                            _blendWeights.Clear();
                            _blendPoints.Clear();
                            _blendVariations.Clear();
                        }

                        break;
                    }
                }
            }

            return mousePos;
        }

        public bool TryGetBlendWeight(Variation v, out float weight)
        {
            var index = _blendVariations.IndexOf(v);
            if (index == -1)
            {
                weight = 0;
                return false;
            }

            weight = _blendWeights[index];
            return true;
        }

        private Vector2 GetNodeCenterOnScreen(ISelectableCanvasObject node)
        {
            var min = TransformPosition(node.PosOnCanvas);
            var max = TransformPosition(node.PosOnCanvas + node.Size);
            return (min + max) * 0.5f;
        }

        private void DrawContextMenu()
        {
            if (T3Ui.OpenedPopUpName == string.Empty)
            {
                CustomComponents.DrawContextMenuForScrollCanvas(() =>
                                                                {
                                                                    var oneOrMoreSelected = Selection.SelectedElements.Count > 0;
                                                                    var oneSelected = Selection.SelectedElements.Count == 1;

                                                                    if (ImGui.MenuItem("Delete selected",
                                                                                       KeyboardBinding.ListKeyboardShortcuts(UserActions.DeleteSelection,
                                                                                           false),
                                                                                       false,
                                                                                       oneOrMoreSelected))
                                                                    {
                                                                        DeleteSelectedElements();
                                                                    }

                                                                    if (ImGui.MenuItem("Rename",
                                                                                       "",
                                                                                       false,
                                                                                       oneSelected))
                                                                    {
                                                                        VariationThumbnail.VariationForRenaming = Selection.SelectedElements[0] as Variation;
                                                                    }

                                                                    if (ImGui.MenuItem("Update thumbnails",
                                                                                       ""))
                                                                    {
                                                                        TriggerThumbnailUpdate();
                                                                    }

                                                                    DrawAdditionalContextMenuContent();

                                                                    ImGui.Separator();
                                                                    if (ImGui.MenuItem("Automatically reset to defaults",
                                                                                       "",
                                                                                       UserSettings.Config.PresetsResetToDefaultValues))
                                                                    {
                                                                        UserSettings.Config.PresetsResetToDefaultValues =
                                                                            !UserSettings.Config.PresetsResetToDefaultValues;
                                                                    }
                                                                }, ref _contextMenuIsOpen);
            }
        }

        private bool _contextMenuIsOpen;

        public void StartHover(Variation variation)
        {
            PoolForBlendOperations.BeginHover(_instance, variation, UserSettings.Config.PresetsResetToDefaultValues);
        }

        public void Apply(Variation variation, bool resetNonDefaults)
        {
            PoolForBlendOperations.StopHover();
            PoolForBlendOperations.Apply(_instance, variation, resetNonDefaults);
        }

        public void StartBlendTo(Variation variation, float blend)
        {
            if (variation.IsPreset)
            {
                PoolForBlendOperations.BeginBlendToPresent(_instance, variation, blend, UserSettings.Config.PresetsResetToDefaultValues);
            }
        }

        public void StopHover()
        {
            PoolForBlendOperations.StopHover();
        }

        protected void TriggerThumbnailUpdate()
        {
            _thumbnailCanvasRendering.ClearTexture();
            _updateIndex = 0;
            _updateCompleted = false;
        }

        protected void ResetView()
        {
            var pool = PoolForBlendOperations;

            if (TryToGetBoundingBox(pool.Variations, 40, out var area))
            {
                FitAreaOnCanvas(area);
            }
        }

        private void HandleFenceSelection()
        {
            _fenceState = SelectionFence.UpdateAndDraw(_fenceState);
            switch (_fenceState)
            {
                case SelectionFence.States.PressedButNotMoved:
                    if (SelectionFence.SelectMode == SelectionFence.SelectModes.Replace)
                        Selection.Clear();
                    break;

                case SelectionFence.States.Updated:
                    HandleSelectionFenceUpdate(SelectionFence.BoundsInScreen);
                    break;

                case SelectionFence.States.CompletedAsClick:
                    Selection.Clear();
                    break;
            }
        }

        private void HandleSelectionFenceUpdate(ImRect boundsInScreen)
        {
            var boundsInCanvas = InverseTransformRect(boundsInScreen);
            var elementsToSelect = (from child in PoolForBlendOperations.Variations
                                    let rect = new ImRect(child.PosOnCanvas, child.PosOnCanvas + child.Size)
                                    where rect.Overlaps(boundsInCanvas)
                                    select child).ToList();

            Selection.Clear();
            foreach (var element in elementsToSelect)
            {
                Selection.AddSelection(element);
            }
        }

        private void DeleteSelectedElements()
        {
            if (Selection.SelectedElements.Count <= 0)
                return;

            var list = new List<Variation>();
            foreach (var e in Selection.SelectedElements)
            {
                if (e is Variation v)
                {
                    list.Add(v);
                }
            }

            VariationsWindow.DeleteVariationsFromPool(PoolForBlendOperations, list);
        }

        #region thumbnail rendering
        private void UpdateNextVariationThumbnail(IOutputUi outputUi, Slot<Texture2D> textureSlot)
        {
            if (_updateCompleted)
                return;

            _thumbnailCanvasRendering.InitializeCanvasTexture(VariationThumbnail.ThumbnailSize);

            if (PoolForBlendOperations.Variations.Count == 0)
            {
                _updateCompleted = true;
                return;
            }

            if (_updateIndex >= PoolForBlendOperations.Variations.Count)
            {
                _updateCompleted = true;
                return;
            }

            var variation = PoolForBlendOperations.Variations[_updateIndex];
            RenderThumbnail(variation, _updateIndex, outputUi, textureSlot);
            _updateIndex++;
        }

        private void RenderThumbnail(Variation variation, int atlasIndex, IOutputUi outputUi, Slot<Texture2D> textureSlot)
        {
            // Set variation values
            PoolForBlendOperations.BeginHover(InstanceForBlendOperations, variation, UserSettings.Config.PresetsResetToDefaultValues);

            // Render variation
            _thumbnailCanvasRendering.EvaluationContext.Reset();
            _thumbnailCanvasRendering.EvaluationContext.LocalTime = 13.4f;

            // NOTE: This is horrible hack to prevent _imageCanvas from being rendered by ImGui
            // DrawValue will use the current ImageOutputCanvas for rendering
            _imageCanvas.SetAsCurrent();
            ImGui.PushClipRect(new Vector2(0, 0), new Vector2(1, 1), true);
            outputUi.DrawValue(textureSlot, _thumbnailCanvasRendering.EvaluationContext);
            ImGui.PopClipRect();
            _imageCanvas.Deactivate();

            var rect = GetPixelRectForIndex(atlasIndex);

            _thumbnailCanvasRendering.CopyToCanvasTexture(textureSlot, rect);

            PoolForBlendOperations.StopHover();
        }

        private ImRect GetPixelRectForIndex(int thumbnailIndex)
        {
            var columns = (int)(_thumbnailCanvasRendering.GetCanvasTextureSize().X / VariationThumbnail.ThumbnailSize.X);
            if (columns == 0)
            {
                return ImRect.RectWithSize(Vector2.Zero, VariationThumbnail.ThumbnailSize);
            }

            var rowIndex = thumbnailIndex / columns;
            var columnIndex = thumbnailIndex % columns;
            var posInCanvasTexture = new Vector2(columnIndex, rowIndex) * VariationThumbnail.ThumbnailSize;
            var rect = ImRect.RectWithSize(posInCanvasTexture, VariationThumbnail.ThumbnailSize);
            return rect;
        }

        private ImRect GetUvRectForIndex(int thumbnailIndex)
        {
            var r = GetPixelRectForIndex(thumbnailIndex);
            return new ImRect(r.Min / _thumbnailCanvasRendering.GetCanvasTextureSize(),
                              r.Max / _thumbnailCanvasRendering.GetCanvasTextureSize());
        }
        #endregion

        #region layout and view
        private void RefreshView()
        {
            TriggerThumbnailUpdate();
            Selection.Clear();
            ResetView();
        }

        private static bool TryToGetBoundingBox(List<Variation> variations, float extend, out ImRect area)
        {
            area = new ImRect();
            if (variations == null)
                return false;

            var foundOne = false;

            foreach (var v in variations)
            {
                if (!foundOne)
                {
                    area = ImRect.RectWithSize(v.PosOnCanvas, v.Size);
                    foundOne = true;
                }
                else
                {
                    area.Add(ImRect.RectWithSize(v.PosOnCanvas, v.Size));
                }
            }

            if (!foundOne)
                return false;

            area.Expand(Vector2.One * extend);
            return true;
        }

        /// <summary>
        /// This uses a primitive algorithm: Look for the bottom edge of a all element bounding box
        /// Then step through possible positions and check if a position would intersect with an existing element.
        /// Wrap columns to enforce some kind of grid.  
        /// </summary>
        internal static Vector2 FindFreePositionForNewThumbnail(List<Variation> variations)
        {
            if (!TryToGetBoundingBox(variations, 0, out var area))
            {
                return Vector2.Zero;
            }

            // var areaOnScreen = TransformRect(area); 
            // ImGui.GetForegroundDrawList().AddRect(areaOnScreen.Min, areaOnScreen.Max, Color.Blue);

            const int columns = 4;
            var columnIndex = 0;

            var stepWidth = VariationThumbnail.ThumbnailSize.X + VariationThumbnail.SnapPadding.X;
            var stepHeight = VariationThumbnail.ThumbnailSize.Y + VariationThumbnail.SnapPadding.Y;

            var pos = new Vector2(area.Min.X,
                                  area.Max.Y - VariationThumbnail.ThumbnailSize.Y);
            var rowStartPos = pos;

            while (true)
            {
                var intersects = false;
                var targetArea = new ImRect(pos, pos + VariationThumbnail.ThumbnailSize);

                // var targetAreaOnScreen = TransformRect(targetArea);
                // ImGui.GetForegroundDrawList().AddRect(targetAreaOnScreen.Min, targetAreaOnScreen.Max, Color.Orange);

                foreach (var v in variations)
                {
                    if (!targetArea.Overlaps(ImRect.RectWithSize(v.PosOnCanvas, v.Size)))
                        continue;

                    intersects = true;
                    break;
                }

                if (!intersects)
                    return pos;

                columnIndex++;
                if (columnIndex == columns)
                {
                    columnIndex = 0;
                    rowStartPos += new Vector2(0, stepHeight);
                    pos = rowStartPos;
                }
                else
                {
                    pos += new Vector2(stepWidth, 0);
                }
            }
        }
        #endregion

        // Compute barycentric coordinates (u, v, w) for
        // point p with respect to triangle (a, b, c)
        private static void Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float u, out float v, out float w)
        {
            Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
            var den = v0.X * v1.Y - v1.X * v0.Y;
            v = (v2.X * v1.Y - v1.X * v2.Y) / den;
            w = (v0.X * v2.Y - v2.X * v0.Y) / den;
            u = 1.0f - v - w;
        }

        /// <summary>
        /// Implement selectionContainer
        /// </summary>
        public IEnumerable<ISelectableCanvasObject> GetSelectables()
        {
            return PoolForBlendOperations.Variations;
        }

        public bool IsBlendingActive { get; private set; }
        private readonly List<float> _blendWeights = new(3);
        private readonly List<Vector2> _blendPoints = new(3);
        private readonly List<Variation> _blendVariations = new(3);

        private Instance _instance;
        private int _updateIndex;
        private bool _updateCompleted;
        private readonly ImageOutputCanvas _imageCanvas = new();
        private readonly ThumbnailCanvasRendering _thumbnailCanvasRendering = new();
        private SelectionFence.States _fenceState;
        internal readonly CanvasElementSelection Selection = new();
        private Instance _lastRenderInstance;
    }
}