using System;
using System.Collections.Generic;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;
using UnityEngine;

namespace Sundoll.Presentation
{
    public sealed class M3RuntimeMapEditor : MonoBehaviour
    {
        private enum M3EditTool
        {
            Brush,
            Line,
            RectangleOutline,
            RectangleFill,
            Fill
        }

        private const float DefaultCellPixels = 38f;
        private const float MinCellPixels = 12f;
        private const float MaxCellPixels = 72f;
        private static readonly string[] DisplayLayerIds =
        {
            M3MapLayerIds.Terrain,
            M3MapLayerIds.Wall,
            M3MapLayerIds.Object,
            M3MapLayerIds.Interaction,
            M3MapLayerIds.StaticAnnotation
        };

        private M3MapEditorFacade editor;
        private M2SaveSession saveSession;
        private string selectedContentId = "terrain-ground";
        private string selectedLayerId = M3MapLayerIds.Terrain;
        private string status = "M3 地图制作器未初始化";
        private bool eraseMode;
        private M3EditTool selectedTool = M3EditTool.Brush;
        private float cellPixels = DefaultCellPixels;
        private Vector2 pan;
        private bool viewportInitialized;
        private bool strokeActive;
        private bool panActive;
        private M3GridPoint strokeStartPoint;
        private M3GridPoint lastStrokePoint;
        private M3LayerEditState layerEditState;
        private M3WorkspaceStateStore workspaceStateStore;
        private readonly M3ContentLookupCache contentLookup = new M3ContentLookupCache();
        private M3GridBounds lastVisibleBounds = M3GridBounds.Empty;
        private readonly List<M3CellMutation> pendingStroke = new List<M3CellMutation>();
        private readonly HashSet<M3MapCellKey> pendingStrokeKeys = new HashSet<M3MapCellKey>();

        public void Bind(M1CommandBus commandBus, M2SaveSession nextSaveSession)
        {
            editor = new M3MapEditorFacade(commandBus);
            saveSession = nextSaveSession ?? throw new ArgumentNullException(nameof(nextSaveSession));
            viewportInitialized = false;
            strokeActive = false;
            panActive = false;
            pendingStroke.Clear();
            pendingStrokeKeys.Clear();
            workspaceStateStore = new M3WorkspaceStateStore(saveSession.ProjectRoot);
            var workspaceLoad = workspaceStateStore.Load(editor.State.map.id, DisplayLayerIds);
            layerEditState = workspaceLoad.state;
            contentLookup.Invalidate();
            lastVisibleBounds = M3GridBounds.Empty;
            status = string.IsNullOrEmpty(workspaceLoad.diagnostic)
                ? "地图草稿已加载"
                : "地图草稿已加载；" + workspaceLoad.diagnostic;
        }

        private void OnGUI()
        {
            if (editor == null || editor.State == null || editor.State.map == null)
            {
                return;
            }

            var map = editor.State.map;
            var viewport = new Rect(12f, 140f, 850f, 470f);
            GUILayout.BeginArea(new Rect(18f, 360f, 900f, 660f), GUI.skin.box);
            GUILayout.Label("SundollWorld · M3 地图制作器 MVP", GUI.skin.label);
            GUILayout.Label($"MapDocument 草稿：{map.id} / {map.width}×{map.height} · 左键拖拽，滚轮缩放，中键平移");
            GUILayout.Label($"当前工具：{ToolLabel()} · {(eraseMode ? "橡皮擦 / " + selectedLayerId : selectedLayerId + " / " + selectedContentId)} · {status}");
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            DrawBrushButton("Terrain", "terrain-ground");
            DrawBrushButton("Wall", "wall-solid");
            DrawBrushButton("Object", "object-marker");
            DrawBrushButton("Interaction", "interaction-door");
            DrawBrushButton("Annotation", "annotation-note");
            if (GUILayout.Button("橡皮擦", GUILayout.Width(80)))
            {
                eraseMode = true;
                selectedTool = M3EditTool.Brush;
            }

            if (GUILayout.Button("Undo", GUILayout.Width(60)))
            {
                if (editor.Undo())
                {
                    saveSession.RecordMutation("m3-undo-" + Guid.NewGuid().ToString("N"), editor.LastAction, editor.State);
                    contentLookup.Invalidate();
                }

                status = editor.LastAction;
            }

            if (GUILayout.Button("Redo", GUILayout.Width(60)))
            {
                if (editor.Redo())
                {
                    saveSession.RecordMutation("m3-redo-" + Guid.NewGuid().ToString("N"), editor.LastAction, editor.State);
                    contentLookup.Invalidate();
                }

                status = editor.LastAction;
            }

            if (GUILayout.Button("复位视口", GUILayout.Width(80)))
            {
                ResetViewport(map);
            }

            if (GUILayout.Button("发布内容版本", GUILayout.Width(110)))
            {
                var receipt = editor.PublishMapContent();
                RecordReceipt(receipt, "已发布 MapContentVersion");
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawToolButton("画笔", M3EditTool.Brush);
            DrawToolButton("直线", M3EditTool.Line);
            DrawToolButton("矩形框", M3EditTool.RectangleOutline);
            DrawToolButton("实心矩形", M3EditTool.RectangleFill);
            DrawToolButton("填充", M3EditTool.Fill);
            GUILayout.EndHorizontal();
            DrawLayerStateToolbar(map);
            GUILayout.Label($"视口：缩放 {cellPixels:0} px/格 · 平移 ({pan.x:0}, {pan.y:0}) · 笔画 {pendingStroke.Count} 格待提交", GUI.skin.label);

            EnsureViewport(map, viewport);
            DrawViewport(map, viewport);
            HandleViewportInput(map, viewport);
            GUILayout.Label($"可见绘制 {lastVisibleBounds.CellCount} 格 · 最近脏区 {editor.LastDirtyBounds}", GUI.skin.label);

            GUILayout.Space(6);
            GUILayout.Label("符号：· 空白   T Terrain   W Wall   O Object   I Interaction   A Annotation；一次鼠标拖拽在松开时提交一个原子命令。", GUI.skin.label);
            GUILayout.Label("正式内容图层：Terrain / Wall / Object / Interaction / StaticAnnotation。", GUI.skin.label);
            GUILayout.EndArea();
        }

        private void EnsureViewport(M1MapDocument map, Rect viewport)
        {
            if (viewportInitialized)
            {
                return;
            }

            cellPixels = DefaultCellPixels;
            pan = CenterPan(map, viewport);
            viewportInitialized = true;
        }

        private void ResetViewport(M1MapDocument map)
        {
            cellPixels = DefaultCellPixels;
            var viewport = new Rect(12f, 140f, 850f, 470f);
            pan = CenterPan(map, viewport);
            viewportInitialized = true;
            status = "视口已复位";
        }

        private static Vector2 CenterPan(M1MapDocument map, Rect viewport)
        {
            return new Vector2(
                (viewport.width - map.width * DefaultCellPixels) * 0.5f,
                (viewport.height - map.height * DefaultCellPixels) * 0.5f);
        }

        private void DrawViewport(M1MapDocument map, Rect viewport)
        {
            GUI.Box(viewport, GUIContent.none, GUI.skin.box);
            var contentByCell = GetContentLookup(map);
            lastVisibleBounds = M3GridViewport.CalculateVisibleBounds(
                map.width,
                map.height,
                viewport.width,
                viewport.height,
                pan.x,
                pan.y,
                cellPixels);
            GUI.BeginGroup(viewport);
            GUI.Box(new Rect(0f, 0f, viewport.width, viewport.height), GUIContent.none, GUI.skin.box);

            if (!lastVisibleBounds.IsEmpty)
            {
                for (var y = lastVisibleBounds.MinY; y <= lastVisibleBounds.MaxY; y++)
                {
                    for (var x = lastVisibleBounds.MinX; x <= lastVisibleBounds.MaxX; x++)
                    {
                        var cellRect = CellRect(map, x, y);
                        var contentId = GetDisplayContent(contentByCell, x, y, layerEditState);
                        var label = string.IsNullOrEmpty(contentId) ? "·" : SymbolFor(contentId);
                        var previousColor = GUI.color;
                        GUI.color = ColorFor(contentId);
                        GUI.Box(cellRect, label, GUI.skin.button);
                        GUI.color = previousColor;
                    }
                }
            }

            if (strokeActive)
            {
                foreach (var mutation in pendingStroke)
                {
                    if (!lastVisibleBounds.Contains(mutation.x, mutation.y))
                    {
                        continue;
                    }

                    var previewColor = mutation.erase
                        ? new Color(0.28f, 0.28f, 0.28f, 0.96f)
                        : new Color(0.8f, 0.74f, 0.24f, 0.98f);
                    var previousColor = GUI.color;
                    GUI.color = previewColor;
                    GUI.Box(CellRect(map, mutation.x, mutation.y), mutation.erase ? "×" : SymbolFor(selectedContentId), GUI.skin.button);
                    GUI.color = previousColor;
                }
            }

            GUI.EndGroup();
        }

        private void HandleViewportInput(M1MapDocument map, Rect viewport)
        {
            var currentEvent = Event.current;
            var screenOrigin = GUIUtility.GUIToScreenPoint(viewport.position);
            var screenMouse = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition);
            var localMouse = screenMouse - screenOrigin;
            var insideViewport = new Rect(0f, 0f, viewport.width, viewport.height).Contains(localMouse);

            if (currentEvent.type == EventType.ScrollWheel && insideViewport)
            {
                ZoomAt(map, localMouse, currentEvent.delta.y);
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 2 && insideViewport)
            {
                panActive = true;
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 2 && panActive)
            {
                pan += currentEvent.delta;
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 2 && panActive)
            {
                panActive = false;
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && insideViewport)
            {
                if (!CanEditSelectedLayer())
                {
                    currentEvent.Use();
                    return;
                }

                if (TryGetCell(map, localMouse, out var cellX, out var cellY))
                {
                    if (selectedTool == M3EditTool.Fill)
                    {
                        ApplyFill(map, cellX, cellY);
                        currentEvent.Use();
                        return;
                    }

                    strokeActive = true;
                    pendingStroke.Clear();
                    pendingStrokeKeys.Clear();
                    strokeStartPoint = new M3GridPoint(cellX, cellY);
                    lastStrokePoint = new M3GridPoint(cellX, cellY);
                    AddPendingPoint(cellX, cellY);
                    currentEvent.Use();
                }

                return;
            }

            if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0 && strokeActive)
            {
                if (TryGetCell(map, localMouse, out var cellX, out var cellY))
                {
                    if (selectedTool == M3EditTool.Brush)
                    {
                        AddPendingSegment(lastStrokePoint.x, lastStrokePoint.y, cellX, cellY);
                        lastStrokePoint = new M3GridPoint(cellX, cellY);
                    }
                    else
                    {
                        RebuildShapePreview(cellX, cellY);
                    }
                }

                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0 && strokeActive)
            {
                CommitStroke();
                strokeActive = false;
                currentEvent.Use();
            }
        }

        private void ZoomAt(M1MapDocument map, Vector2 localMouse, float wheelDelta)
        {
            var nextCellPixels = Mathf.Clamp(cellPixels - wheelDelta * 4f, MinCellPixels, MaxCellPixels);
            if (Mathf.Approximately(nextCellPixels, cellPixels))
            {
                return;
            }

            var mapPoint = localMouse - pan;
            pan = localMouse - mapPoint * (nextCellPixels / cellPixels);
            cellPixels = nextCellPixels;
            status = $"视口缩放至 {cellPixels:0} px/格";
        }

        private void RebuildShapePreview(int endX, int endY)
        {
            pendingStroke.Clear();
            pendingStrokeKeys.Clear();
            List<M3GridPoint> points;
            switch (selectedTool)
            {
                case M3EditTool.Line:
                    points = M3GridStrokeRasterizer.Rasterize(
                        strokeStartPoint.x,
                        strokeStartPoint.y,
                        endX,
                        endY);
                    break;
                case M3EditTool.RectangleOutline:
                    points = M3GridShapeRasterizer.RasterizeRectangle(
                        strokeStartPoint.x,
                        strokeStartPoint.y,
                        endX,
                        endY,
                        false);
                    break;
                case M3EditTool.RectangleFill:
                    points = M3GridShapeRasterizer.RasterizeRectangle(
                        strokeStartPoint.x,
                        strokeStartPoint.y,
                        endX,
                        endY,
                        true);
                    break;
                default:
                    points = M3GridStrokeRasterizer.Rasterize(
                        strokeStartPoint.x,
                        strokeStartPoint.y,
                        endX,
                        endY);
                    break;
            }

            foreach (var point in points)
            {
                AddPendingPoint(point.x, point.y);
            }
        }

        private void ApplyFill(M1MapDocument map, int startX, int startY)
        {
            var contentLookup = GetContentLookup(map);
            contentLookup.TryGetValue(new M3MapCellKey(startX, startY, selectedLayerId), out var targetContentId);
            var replacementContentId = eraseMode ? null : selectedContentId;
            if (string.Equals(targetContentId, replacementContentId, StringComparison.Ordinal))
            {
                status = "填充没有变化";
                return;
            }

            var points = M3GridShapeRasterizer.FloodFill(
                map.width,
                map.height,
                startX,
                startY,
                (x, y) => contentLookup.TryGetValue(new M3MapCellKey(x, y, selectedLayerId), out var contentId) ? contentId : null);
            var mutations = new List<M3CellMutation>(points.Count);
            foreach (var point in points)
            {
                mutations.Add(new M3CellMutation(
                    point.x,
                    point.y,
                    selectedLayerId,
                    replacementContentId,
                    eraseMode));
            }

            if (mutations.Count == 0)
            {
                status = "填充没有目标格子";
                return;
            }

            var acceptedMessage = eraseMode
                ? $"填充擦除 {mutations.Count} 个格子"
                : $"填充 {mutations.Count} 个格子";
            RecordReceipt(editor.PaintCells(mutations), acceptedMessage, mutations);
        }

        private Rect CellRect(M1MapDocument map, int x, int y)
        {
            var rowFromTop = map.height - 1 - y;
            return new Rect(
                pan.x + x * cellPixels,
                pan.y + rowFromTop * cellPixels,
                cellPixels - 1f,
                cellPixels - 1f);
        }

        private bool TryGetCell(M1MapDocument map, Vector2 localMouse, out int x, out int y)
        {
            x = Mathf.FloorToInt((localMouse.x - pan.x) / cellPixels);
            var rowFromTop = Mathf.FloorToInt((localMouse.y - pan.y) / cellPixels);
            y = map.height - 1 - rowFromTop;
            return x >= 0 && x < map.width && y >= 0 && y < map.height;
        }

        private void AddPendingSegment(int startX, int startY, int endX, int endY)
        {
            foreach (var point in M3GridStrokeRasterizer.Rasterize(startX, startY, endX, endY))
            {
                AddPendingPoint(point.x, point.y);
            }
        }

        private void AddPendingPoint(int x, int y)
        {
            if (!pendingStrokeKeys.Add(new M3MapCellKey(x, y, selectedLayerId)))
            {
                return;
            }

            pendingStroke.Add(new M3CellMutation(
                x,
                y,
                selectedLayerId,
                eraseMode ? null : selectedContentId,
                eraseMode));
        }

        private void CommitStroke()
        {
            if (pendingStroke.Count == 0)
            {
                return;
            }

            var count = pendingStroke.Count;
            var acceptedMessage = eraseMode
                ? $"连续擦除 {count} 个格子"
                : $"连续绘制 {count} 个格子";
            RecordReceipt(editor.PaintCells(pendingStroke), acceptedMessage, pendingStroke);
            pendingStroke.Clear();
            pendingStrokeKeys.Clear();
        }

        private void DrawBrushButton(string label, string contentId)
        {
            if (GUILayout.Button(label, GUILayout.Width(80)))
            {
                selectedContentId = contentId;
                selectedLayerId = M3MapLayerIds.InferLayerId(contentId);
                eraseMode = false;
                selectedTool = M3EditTool.Brush;
            }
        }

        private void DrawToolButton(string label, M3EditTool tool)
        {
            if (GUILayout.Button(label, GUILayout.Width(80)))
            {
                selectedTool = tool;
                if (tool != M3EditTool.Brush)
                {
                    eraseMode = false;
                }
            }
        }

        private void DrawLayerStateToolbar(M1MapDocument map)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("图层状态", GUILayout.Width(64));
            foreach (var layerId in DisplayLayerIds)
            {
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(150));
                GUILayout.Label(LayerLabel(layerId) + (layerId == selectedLayerId ? " · 当前" : ""));
                if (GUILayout.Button(layerEditState.IsVisible(layerId) ? "显示" : "隐藏", GUILayout.Width(68)))
                {
                    layerEditState.ToggleVisible(layerId);
                    PersistWorkspaceState(map);
                }

                if (GUILayout.Button(layerEditState.IsLocked(layerId) ? "解锁" : "锁定", GUILayout.Width(68)))
                {
                    var locked = layerEditState.ToggleLocked(layerId);
                    PersistWorkspaceState(map);
                    if (layerId == selectedLayerId)
                    {
                        if (locked)
                        {
                            pendingStroke.Clear();
                            pendingStrokeKeys.Clear();
                            strokeActive = false;
                        }

                        status = locked ? "当前图层已锁定：" + layerId : "当前图层已解锁：" + layerId;
                    }
                }

                GUILayout.EndVertical();
            }

            GUILayout.EndHorizontal();
        }

        private void PersistWorkspaceState(M1MapDocument map)
        {
            try
            {
                workspaceStateStore.Save(map.id, layerEditState, DisplayLayerIds);
            }
            catch (Exception exception)
            {
                status = "Workspace 状态保存失败：" + exception.Message;
            }
        }

        private bool CanEditSelectedLayer()
        {
            if (layerEditState.CanEdit(selectedLayerId))
            {
                return true;
            }

            status = "图层已锁定，无法编辑：" + selectedLayerId;
            return false;
        }

        private static string LayerLabel(string layerId)
        {
            switch (layerId)
            {
                case M3MapLayerIds.Terrain:
                    return "Terrain";
                case M3MapLayerIds.Wall:
                    return "Wall";
                case M3MapLayerIds.Object:
                    return "Object";
                case M3MapLayerIds.Interaction:
                    return "Interaction";
                case M3MapLayerIds.StaticAnnotation:
                    return "Annotation";
                default:
                    return layerId;
            }
        }

        private string ToolLabel()
        {
            switch (selectedTool)
            {
                case M3EditTool.Line:
                    return "直线";
                case M3EditTool.RectangleOutline:
                    return "矩形框";
                case M3EditTool.RectangleFill:
                    return "实心矩形";
                case M3EditTool.Fill:
                    return "填充";
                default:
                    return "画笔";
            }
        }

        private void RecordReceipt(
            M1CommandReceipt receipt,
            string acceptedMessage,
            IList<M3CellMutation> mutations = null)
        {
            status = receipt.message;
            if (receipt.accepted)
            {
                saveSession.RecordAccepted(receipt, editor.State);
                if (mutations != null &&
                    !contentLookup.TryApplyIncremental(
                        editor.State.map,
                        editor.State.revision,
                        mutations))
                {
                    contentLookup.Invalidate();
                }

                status = acceptedMessage;
            }
        }

        private IReadOnlyDictionary<M3MapCellKey, string> GetContentLookup(M1MapDocument map)
        {
            var revision = editor.State.revision;
            if (!contentLookup.IsCurrent(map, revision))
            {
                contentLookup.Rebuild(map, revision);
            }

            return contentLookup.ContentByCell;
        }

        private static string GetDisplayContent(
            IReadOnlyDictionary<M3MapCellKey, string> contentLookup,
            int x,
            int y,
            M3LayerEditState layerEditState)
        {
            string selectedContent = null;
            var selectedPriority = 0;
            foreach (var layerId in DisplayLayerIds)
            {
                if (!layerEditState.IsVisible(layerId))
                {
                    continue;
                }

                if (!contentLookup.TryGetValue(new M3MapCellKey(x, y, layerId), out var contentId))
                {
                    continue;
                }

                var priority = M3MapLayerIds.RenderPriority(layerId);
                if (priority >= selectedPriority)
                {
                    selectedPriority = priority;
                    selectedContent = contentId;
                }
            }

            return selectedContent;
        }

        private static Color ColorFor(string contentId)
        {
            if (string.IsNullOrEmpty(contentId))
            {
                return new Color(0.18f, 0.2f, 0.24f, 0.92f);
            }

            if (contentId.StartsWith("wall-", StringComparison.Ordinal))
            {
                return new Color(0.5f, 0.33f, 0.2f, 0.98f);
            }

            if (contentId.StartsWith("object-", StringComparison.Ordinal))
            {
                return new Color(0.25f, 0.52f, 0.38f, 0.98f);
            }

            if (contentId.StartsWith("interaction-", StringComparison.Ordinal))
            {
                return new Color(0.62f, 0.38f, 0.68f, 0.98f);
            }

            if (contentId.StartsWith("annotation-", StringComparison.Ordinal) ||
                contentId.StartsWith("static-annotation-", StringComparison.Ordinal))
            {
                return new Color(0.72f, 0.58f, 0.18f, 0.98f);
            }

            return new Color(0.2f, 0.42f, 0.65f, 0.98f);
        }

        private static string SymbolFor(string contentId)
        {
            if (contentId.StartsWith("wall-", StringComparison.Ordinal))
            {
                return "W";
            }

            if (contentId.StartsWith("object-", StringComparison.Ordinal))
            {
                return "O";
            }

            if (contentId.StartsWith("interaction-", StringComparison.Ordinal))
            {
                return "I";
            }

            if (contentId.StartsWith("annotation-", StringComparison.Ordinal) ||
                contentId.StartsWith("static-annotation-", StringComparison.Ordinal))
            {
                return "A";
            }

            return "T";
        }
    }
}
