using System;
using System.Collections.Generic;
using System.IO;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Runtime composition root for the formal M3 Workbench. UI actions call the
    /// facade or workspace store; authoritative world state remains in the bus.
    /// </summary>
    public sealed class M3WorkbenchRoot : MonoBehaviour
    {
        private static readonly string[] LayerIds =
        {
            M3MapLayerIds.Terrain,
            M3MapLayerIds.Wall,
            M3MapLayerIds.Object,
            M3MapLayerIds.Interaction,
            M3MapLayerIds.StaticAnnotation
        };

        private readonly Dictionary<string, Button> layerButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> layerVisibilityButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> layerLockButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly List<M3CellMutation> pendingStroke = new List<M3CellMutation>();
        private readonly HashSet<M3MapCellKey> pendingStrokeKeys = new HashSet<M3MapCellKey>();
        private M1CommandBus commandBus;
        private M2SaveSession saveSession;
        private M3MapEditorFacade editor;
        private M3LayerEditState layerEditState;
        private M3WorkspaceStateStore workspaceStateStore;
        private M3WorkbenchMapProjection projection;
        private UIDocument uiDocument;
        private PanelSettings panelSettings;
        private Label saveStatusLabel;
        private Label mapStatusLabel;
        private Label inspectorLabel;
        private Label historyLabel;
        private VisualElement mapViewport;
        private M3WorkbenchInput input;
        private string currentTool = "画笔";
        private string currentLayerId = M3MapLayerIds.Terrain;
        private string status = "Workbench 初始化中";
        private float statusRefreshTimer;
        private M3GridBounds selection = M3GridBounds.Empty;
        private M3MapClipboard clipboard;
        private Vector2Int strokeStartCell;
        private Vector2Int lastStrokeCell;
        private bool strokeActive;
        private Vector2Int selectionStartCell;
        private bool selectionActive;
        private float loadedWorkspaceZoom;
        private Vector2 loadedWorkspacePan;
        private bool hasLoadedWorkspaceView;

        public M3MapEditorFacade Editor => editor;
        public M2SaveSession SaveSession => saveSession;
        public M3LayerEditState LayerEditState => layerEditState;
        public string CurrentTool => currentTool;
        public string CurrentLayerId => currentLayerId;
        public M3GridBounds Selection => selection;
        public M3MapClipboard Clipboard => clipboard;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            InitializeDomain();
            EnsureCamera();
            projection = GetComponentInChildren<M3WorkbenchMapProjection>();
            if (projection == null)
            {
                var gridObject = new GameObject("WorkbenchGrid");
                gridObject.transform.SetParent(transform, false);
                gridObject.AddComponent<Grid>();
                projection = gridObject.AddComponent<M3WorkbenchMapProjection>();
            }

            projection.Bind(editor, layerEditState);
            BuildUi();
            input = GetComponent<M3WorkbenchInput>();
            if (input == null)
            {
                input = gameObject.AddComponent<M3WorkbenchInput>();
            }

            input.Bind(this, GetComponentInChildren<Camera>(), projection);
            status = string.IsNullOrEmpty(status) ? "Workbench 已就绪" : status;
        }

        private void Update()
        {
            if (saveSession == null)
            {
                return;
            }

            saveSession.RefreshSaveStatus();
            statusRefreshTimer -= Time.unscaledDeltaTime;
            if (statusRefreshTimer > 0f)
            {
                return;
            }

            statusRefreshTimer = 0.25f;
            RefreshUiState();
        }

        private void OnDestroy()
        {
            if (saveSession != null)
            {
                saveSession.Dispose();
                saveSession = null;
            }

            if (panelSettings != null)
            {
                Destroy(panelSettings);
                panelSettings = null;
            }

            PersistWorkspaceState();
        }

        private void OnApplicationQuit()
        {
            PersistWorkspaceState();
        }

        private void InitializeDomain()
        {
            var initialBus = M1VerticalSlice.CreateDemoBus();
            // M3 intentionally uses a new development root. Existing M1/M2
            // samples remain readable and untouched while schema 2 evolves.
            var projectRoot = Path.Combine(UnityEngine.Application.persistentDataPath, "SundollWorld_M3");
            saveSession = M2SaveSession.Open(projectRoot, initialBus.State);
            commandBus = new M1CommandBus(
                saveSession.State,
                new M1LocalAuthority(new AllowAllRulePolicy()));
            editor = new M3MapEditorFacade(commandBus);
            workspaceStateStore = new M3WorkspaceStateStore(projectRoot);
            var workspaceLoad = workspaceStateStore.Load(editor.State.map.id, LayerIds);
            layerEditState = workspaceLoad.state;
            currentTool = string.IsNullOrWhiteSpace(workspaceLoad.currentTool) ? "画笔" : workspaceLoad.currentTool;
            currentLayerId = string.IsNullOrWhiteSpace(workspaceLoad.currentLayerId)
                ? LayerIds[0]
                : workspaceLoad.currentLayerId;
            hasLoadedWorkspaceView = workspaceLoad.loaded && workspaceLoad.zoom > 1f;
            loadedWorkspaceZoom = workspaceLoad.zoom;
            loadedWorkspacePan = new Vector2(workspaceLoad.panX, workspaceLoad.panY);
            status = string.IsNullOrEmpty(workspaceLoad.diagnostic)
                ? "地图草稿已加载"
                : "地图草稿已加载；" + workspaceLoad.diagnostic;
        }

        private void EnsureCamera()
        {
            var camera = GetComponentInChildren<Camera>();
            if (camera == null)
            {
                var cameraObject = new GameObject("WorkbenchCamera");
                cameraObject.transform.SetParent(transform, false);
                camera = cameraObject.AddComponent<Camera>();
            }

            var map = editor.State.map;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(5f, map.height * 0.5f + 1f);
            camera.transform.position = new Vector3((map.width - 1) * 0.5f, (map.height - 1) * 0.5f, -10f);
            camera.transform.rotation = Quaternion.identity;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.07f, 0.095f, 1f);
            camera.depth = -10f;
            camera.tag = "MainCamera";
            if (hasLoadedWorkspaceView)
            {
                camera.orthographicSize = Mathf.Clamp(loadedWorkspaceZoom, 2f, 100f);
                camera.transform.position = new Vector3(loadedWorkspacePan.x, loadedWorkspacePan.y, -10f);
            }
        }

        private void BuildUi()
        {
            uiDocument = gameObject.AddComponent<UIDocument>();
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "SundollWorld.WorkbenchPanelSettings";
            panelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1440, 900);
            panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("SundollWorld/WorkbenchTheme");
            uiDocument.panelSettings = panelSettings;

            var root = uiDocument.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1f;
            root.style.flexDirection = FlexDirection.Column;
            root.style.backgroundColor = new Color(0f, 0f, 0f, 0f);

            var topBar = CreatePanel(new Color(0.055f, 0.07f, 0.095f, 0.96f), 48f);
            topBar.style.width = Length.Percent(100f);
            topBar.style.height = 48f;
            topBar.style.flexDirection = FlexDirection.Row;
            topBar.style.alignItems = Align.Center;
            topBar.Add(new Label("SundollWorld") { name = "ProjectTitle" });
            topBar.Q<Label>("ProjectTitle").style.unityFontStyleAndWeight = FontStyle.Bold;
            topBar.Q<Label>("ProjectTitle").style.marginLeft = 16f;
            mapStatusLabel = new Label { name = "MapStatus" };
            mapStatusLabel.style.marginLeft = 28f;
            topBar.Add(mapStatusLabel);
            var saveButton = new Button(QueueSave) { text = "保存 Snapshot" };
            saveButton.style.marginLeft = StyleKeyword.Auto;
            topBar.Add(saveButton);
            saveStatusLabel = new Label { name = "SaveStatus" };
            saveStatusLabel.style.marginLeft = 14f;
            saveStatusLabel.style.marginRight = 18f;
            topBar.Add(saveStatusLabel);
            root.Add(topBar);

            var body = new VisualElement { name = "WorkbenchBody" };
            body.style.flexGrow = 1f;
            body.style.flexDirection = FlexDirection.Row;
            root.Add(body);

            body.Add(BuildToolPanel());
            body.Add(BuildMapPanel());
            body.Add(BuildInspectorPanel());

            var bottomBar = CreatePanel(new Color(0.055f, 0.07f, 0.095f, 0.96f), 82f);
            bottomBar.style.width = Length.Percent(100f);
            bottomBar.style.height = 82f;
            bottomBar.style.paddingLeft = 16f;
            bottomBar.style.paddingTop = 8f;
            historyLabel = new Label { name = "History" };
            bottomBar.Add(historyLabel);
            root.Add(bottomBar);
            RefreshUiState();
        }

        private VisualElement BuildToolPanel()
        {
            var panel = CreatePanel(new Color(0.08f, 0.095f, 0.125f, 0.98f), 220f);
            panel.style.paddingLeft = 12f;
            panel.style.paddingRight = 12f;
            panel.Add(new Label("工具 / 图层") { name = "ToolTitle" });
            foreach (var tool in new[] { "选择", "画笔", "橡皮擦", "直线", "矩形", "填充" })
            {
                var toolButton = new Button(() => SelectTool(tool)) { text = tool };
                toolButton.style.marginTop = 5f;
                panel.Add(toolButton);
            }

            var layerTitle = new Label("内容层") { name = "LayerTitle" };
            layerTitle.style.marginTop = 18f;
            panel.Add(layerTitle);
            foreach (var layerId in LayerIds)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginTop = 5f;
                var selectButton = new Button(() => SelectLayer(layerId));
                selectButton.style.flexGrow = 1f;
                layerButtons.Add(layerId, selectButton);
                row.Add(selectButton);
                var visibilityButton = new Button(() => ToggleLayer(layerId));
                visibilityButton.style.width = 42f;
                layerVisibilityButtons.Add(layerId, visibilityButton);
                row.Add(visibilityButton);
                var lockButton = new Button(() => ToggleLock(layerId));
                lockButton.style.width = 42f;
                layerLockButtons.Add(layerId, lockButton);
                row.Add(lockButton);
                panel.Add(row);
            }

            var resetButton = new Button(ResetView) { text = "复位视口" };
            resetButton.style.marginTop = 18f;
            panel.Add(resetButton);
            return panel;
        }

        private VisualElement BuildMapPanel()
        {
            var panel = CreatePanel(new Color(0f, 0f, 0f, 0.08f), 0f);
            panel.name = "MapViewport";
            mapViewport = panel;
            panel.style.flexGrow = 1f;
            panel.style.minWidth = 420f;
            panel.style.justifyContent = Justify.Center;
            panel.style.alignItems = Align.Center;
            var hint = new Label("2D Tilemap Viewport") { name = "ViewportHint" };
            hint.style.color = new Color(0.8f, 0.85f, 0.9f, 0.65f);
            hint.pickingMode = PickingMode.Ignore;
            panel.Add(hint);
            return panel;
        }

        private VisualElement BuildInspectorPanel()
        {
            var panel = CreatePanel(new Color(0.08f, 0.095f, 0.125f, 0.98f), 280f);
            panel.style.paddingLeft = 14f;
            panel.style.paddingRight = 14f;
            panel.Add(new Label("Inspector") { name = "InspectorTitle" });
            inspectorLabel = new Label { name = "InspectorBody" };
            inspectorLabel.style.whiteSpace = WhiteSpace.Normal;
            inspectorLabel.style.marginTop = 12f;
            panel.Add(inspectorLabel);
            return panel;
        }

        private void QueueSave()
        {
            var operation = saveSession.QueueSave("Workbench 手动保存 Snapshot");
            status = operation.Status == M2SaveStatus.Saving ? "Workbench Snapshot 保存中" : saveSession.LastAction;
            RefreshUiState();
        }

        private void SelectTool(string tool)
        {
            currentTool = tool;
            status = "当前工具：" + tool;
            RefreshUiState();
        }

        private void SelectLayer(string layerId)
        {
            currentLayerId = layerId;
            status = "当前图层：" + LayerLabel(layerId);
            RefreshUiState();
        }

        private void ToggleLayer(string layerId)
        {
            var visible = layerEditState.ToggleVisible(layerId);
            PersistWorkspaceState();
            projection.ApplyLayerState();
            status = LayerLabel(layerId) + (visible ? " 已显示" : " 已隐藏");
            RefreshUiState();
        }

        private void ToggleLock(string layerId)
        {
            var locked = layerEditState.ToggleLocked(layerId);
            PersistWorkspaceState();
            status = LayerLabel(layerId) + (locked ? " 已锁定" : " 已解锁");
            RefreshUiState();
        }

        public void MoveCurrentLayer(int direction)
        {
            if (layerEditState.MoveLayer(currentLayerId, direction))
            {
                PersistWorkspaceState();
                status = "图层顺序已更新：" + LayerLabel(currentLayerId);
                RefreshUiState();
            }
        }

        private void ResetView()
        {
            var camera = GetComponentInChildren<Camera>();
            var map = editor.State.map;
            camera.orthographicSize = Mathf.Max(5f, map.height * 0.5f + 1f);
            camera.transform.position = new Vector3((map.width - 1) * 0.5f, (map.height - 1) * 0.5f, -10f);
            PersistWorkspaceState();
            status = "视口已复位";
            RefreshUiState();
        }

        public bool IsPointerOverMap(Vector2 screenPosition)
        {
            if (mapViewport == null)
            {
                return true;
            }

            var panelPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return mapViewport.worldBound.Contains(panelPosition);
        }

        public bool TryScreenToCell(Vector2 screenPosition, out Vector2Int cell)
        {
            var camera = GetComponentInChildren<Camera>();
            if (camera == null || editor == null || editor.State.map == null)
            {
                cell = default(Vector2Int);
                return false;
            }

            var world = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -camera.transform.position.z));
            cell = new Vector2Int(Mathf.FloorToInt(world.x + 0.5f), Mathf.FloorToInt(world.y + 0.5f));
            return cell.x >= 0 && cell.x < editor.State.map.width && cell.y >= 0 && cell.y < editor.State.map.height;
        }

        public void ZoomAt(Vector2 screenPosition, float wheelDelta)
        {
            if (Mathf.Abs(wheelDelta) < 0.01f)
            {
                return;
            }

            var camera = GetComponentInChildren<Camera>();
            if (camera == null)
            {
                return;
            }

            var before = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -camera.transform.position.z));
            var scale = wheelDelta > 0f ? 0.85f : 1.15f;
            camera.orthographicSize = Mathf.Clamp(camera.orthographicSize * scale, 2f, 100f);
            var after = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -camera.transform.position.z));
            camera.transform.position += before - after;
            status = "视口缩放：" + camera.orthographicSize.ToString("0.0");
            RefreshUiState();
        }

        public void PanByScreen(Vector2 screenDelta)
        {
            var camera = GetComponentInChildren<Camera>();
            if (camera == null || Screen.height <= 0)
            {
                return;
            }

            var worldPerPixel = camera.orthographicSize * 2f / Screen.height;
            camera.transform.position -= new Vector3(screenDelta.x * worldPerPixel, screenDelta.y * worldPerPixel, 0f);
        }

        public void BeginPointerAction(Vector2Int cell)
        {
            if (currentTool == "选择")
            {
                selectionStartCell = cell;
                selection = new M3GridBounds(cell.x, cell.y, cell.x, cell.y);
                selectionActive = true;
                status = "选择中：" + selection;
                RefreshUiState();
                return;
            }

            if (!layerEditState.CanEdit(currentLayerId))
            {
                status = "图层已锁定：" + LayerLabel(currentLayerId);
                RefreshUiState();
                return;
            }

            pendingStroke.Clear();
            pendingStrokeKeys.Clear();
            strokeActive = true;
            strokeStartCell = cell;
            lastStrokeCell = cell;
            if (currentTool == "画笔" || currentTool == "橡皮擦")
            {
                AddStrokeCell(cell);
            }
        }

        public void ContinuePointerAction(Vector2Int cell)
        {
            if (selectionActive)
            {
                selection = CreateSelection(selectionStartCell, cell);
                status = "选择中：" + selection;
                return;
            }

            if (!strokeActive || currentTool == "直线" || currentTool == "矩形" || currentTool == "填充")
            {
                return;
            }

            foreach (var point in M3GridStrokeRasterizer.Rasterize(lastStrokeCell.x, lastStrokeCell.y, cell.x, cell.y))
            {
                AddStrokeCell(new Vector2Int(point.x, point.y));
            }

            lastStrokeCell = cell;
        }

        public void EndPointerAction(Vector2Int cell)
        {
            if (selectionActive)
            {
                selection = CreateSelection(selectionStartCell, cell);
                selectionActive = false;
                status = "已选择 " + selection;
                RefreshUiState();
                return;
            }

            if (!strokeActive)
            {
                return;
            }

            strokeActive = false;
            if (currentTool == "直线")
            {
                foreach (var point in M3GridStrokeRasterizer.Rasterize(strokeStartCell.x, strokeStartCell.y, cell.x, cell.y))
                {
                    AddStrokeCell(new Vector2Int(point.x, point.y));
                }
            }
            else if (currentTool == "矩形")
            {
                var minX = Mathf.Min(strokeStartCell.x, cell.x);
                var maxX = Mathf.Max(strokeStartCell.x, cell.x);
                var minY = Mathf.Min(strokeStartCell.y, cell.y);
                var maxY = Mathf.Max(strokeStartCell.y, cell.y);
                for (var x = minX; x <= maxX; x++)
                {
                    for (var y = minY; y <= maxY; y++)
                    {
                        AddStrokeCell(new Vector2Int(x, y));
                    }
                }
            }
            else if (currentTool == "填充")
            {
                AddFillCells(cell);
            }

            CommitPendingStroke();
        }

        public void CancelPointerAction()
        {
            strokeActive = false;
            selectionActive = false;
            pendingStroke.Clear();
            pendingStrokeKeys.Clear();
            status = "已取消当前操作";
            RefreshUiState();
        }

        public void PickAt(Vector2Int cell)
        {
            if (!editor.TryPickTopmost(cell.x, cell.y, layerEditState, out var picked))
            {
                status = "该格没有可拾取内容";
                RefreshUiState();
                return;
            }

            currentLayerId = M3MapLayerIds.NormalizeLayerId(picked.layerId, picked.contentId);
            currentTool = "画笔";
            status = "已拾取：" + picked.contentId;
            RefreshUiState();
        }

        public void CopySelection()
        {
            clipboard = editor.CopySelection(selection, layerEditState);
            status = clipboard.IsEmpty ? "选区为空" : "已复制 " + clipboard.cells.Count + " 个内容";
            RefreshUiState();
        }

        public void CutSelection()
        {
            var receipt = editor.CutSelection(selection, layerEditState, out var cutClipboard);
            if (cutClipboard != null && !cutClipboard.IsEmpty)
            {
                clipboard = cutClipboard;
            }

            CommitReceipt(receipt);
        }

        public void PasteAt(Vector2Int anchor)
        {
            CommitReceipt(editor.PasteClipboard(clipboard, anchor.x, anchor.y, layerEditState));
        }

        public void RotateClipboard()
        {
            if (clipboard == null || clipboard.IsEmpty)
            {
                CopySelection();
            }

            if (clipboard != null && !clipboard.IsEmpty)
            {
                clipboard = clipboard.RotateClockwise();
                status = "剪贴板已顺时针旋转 90°";
            }

            RefreshUiState();
        }

        public void Undo()
        {
            if (editor.Undo())
            {
                projection.RefreshRegion(editor.LastDirtyBounds);
                saveSession.RecordMutation("m3-undo-" + Guid.NewGuid().ToString("N"), "撤销 Workbench 操作", editor.State);
                status = "已撤销";
            }

            RefreshUiState();
        }

        public void Redo()
        {
            if (editor.Redo())
            {
                projection.RefreshRegion(editor.LastDirtyBounds);
                saveSession.RecordMutation("m3-redo-" + Guid.NewGuid().ToString("N"), "重做 Workbench 操作", editor.State);
                status = "已重做";
            }

            RefreshUiState();
        }

        public void ToggleObjectAt(Vector2Int cell)
        {
            var mapObject = FindObjectAt(cell);
            if (mapObject == null)
            {
                status = "该格没有门或箱子对象";
                RefreshUiState();
                return;
            }

            CommitReceipt(editor.ToggleMapObject(mapObject.id));
        }

        public void OpenObjectAt(Vector2Int cell)
        {
            var mapObject = FindObjectAt(cell);
            if (mapObject != null)
            {
                CommitReceipt(editor.OpenMapObject(mapObject.id));
            }
        }

        public void CloseObjectAt(Vector2Int cell)
        {
            var mapObject = FindObjectAt(cell);
            if (mapObject != null)
            {
                CommitReceipt(editor.CloseMapObject(mapObject.id));
            }
        }

        public void RotateObjectAt(Vector2Int cell)
        {
            var mapObject = FindObjectAt(cell);
            if (mapObject != null)
            {
                CommitReceipt(editor.RotateMapObjectClockwise(mapObject.id));
            }
        }

        private void AddStrokeCell(Vector2Int cell)
        {
            if (editor.State.map == null || cell.x < 0 || cell.x >= editor.State.map.width ||
                cell.y < 0 || cell.y >= editor.State.map.height)
            {
                return;
            }

            var key = new M3MapCellKey(cell.x, cell.y, currentLayerId);
            if (!pendingStrokeKeys.Add(key))
            {
                return;
            }

            pendingStroke.Add(currentTool == "橡皮擦"
                ? new M3CellMutation(cell.x, cell.y, currentLayerId, null, true)
                : new M3CellMutation(cell.x, cell.y, currentLayerId, ContentForLayer(currentLayerId), false));
        }

        private void AddFillCells(Vector2Int start)
        {
            if (editor.State.map == null || !layerEditState.CanEdit(currentLayerId))
            {
                return;
            }

            var source = FindCellContent(start.x, start.y, currentLayerId);
            var target = currentTool == "橡皮擦" ? null : ContentForLayer(currentLayerId);
            if (string.Equals(source, target, StringComparison.Ordinal))
            {
                return;
            }

            var visited = new bool[editor.State.map.width, editor.State.map.height];
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                if (cell.x < 0 || cell.x >= editor.State.map.width || cell.y < 0 || cell.y >= editor.State.map.height ||
                    visited[cell.x, cell.y])
                {
                    continue;
                }

                visited[cell.x, cell.y] = true;
                if (!string.Equals(FindCellContent(cell.x, cell.y, currentLayerId), source, StringComparison.Ordinal))
                {
                    continue;
                }

                AddStrokeCell(cell);
                queue.Enqueue(new Vector2Int(cell.x + 1, cell.y));
                queue.Enqueue(new Vector2Int(cell.x - 1, cell.y));
                queue.Enqueue(new Vector2Int(cell.x, cell.y + 1));
                queue.Enqueue(new Vector2Int(cell.x, cell.y - 1));
            }
        }

        private void CommitPendingStroke()
        {
            if (pendingStroke.Count == 0)
            {
                status = "没有可提交的格子";
                RefreshUiState();
                return;
            }

            CommitReceipt(editor.PaintCells(pendingStroke));
            pendingStroke.Clear();
            pendingStrokeKeys.Clear();
        }

        private void CommitReceipt(M1CommandReceipt receipt)
        {
            if (receipt == null)
            {
                return;
            }

            if (receipt.accepted)
            {
                saveSession.RecordAccepted(receipt, editor.State);
                projection.RefreshRegion(editor.LastDirtyBounds);
                status = receipt.message;
            }
            else
            {
                status = receipt.message;
            }

            RefreshUiState();
        }

        private M3MapObject FindObjectAt(Vector2Int cell)
        {
            if (editor.State.map == null || editor.State.map.objects == null)
            {
                return null;
            }

            foreach (var mapObject in editor.State.map.objects)
            {
                if (mapObject != null && mapObject.x == cell.x && mapObject.y == cell.y)
                {
                    return mapObject;
                }
            }

            return null;
        }

        private string FindCellContent(int x, int y, string layerId)
        {
            return editor.State.map != null && editor.State.map.TryGetCell(x, y, layerId, out var cell)
                ? cell.contentId
                : null;
        }

        private static M3GridBounds CreateSelection(Vector2Int first, Vector2Int second)
        {
            return new M3GridBounds(
                Mathf.Min(first.x, second.x),
                Mathf.Min(first.y, second.y),
                Mathf.Max(first.x, second.x),
                Mathf.Max(first.y, second.y));
        }

        private static string ContentForLayer(string layerId)
        {
            switch (layerId)
            {
                case M3MapLayerIds.Wall:
                    return "wall-solid";
                case M3MapLayerIds.Object:
                    return "object-marker";
                case M3MapLayerIds.Interaction:
                    return "interaction-trigger";
                case M3MapLayerIds.StaticAnnotation:
                    return "annotation-note";
                default:
                    return "terrain-ground";
            }
        }

        private void PersistWorkspaceState()
        {
            if (workspaceStateStore == null || editor == null || editor.State == null || editor.State.map == null)
            {
                return;
            }

            var camera = GetComponentInChildren<Camera>();
            workspaceStateStore.Save(
                editor.State.map.id,
                layerEditState,
                LayerIds,
                currentTool,
                currentLayerId,
                camera == null ? 1f : camera.orthographicSize,
                camera == null ? 0f : camera.transform.position.x,
                camera == null ? 0f : camera.transform.position.y);
        }

        private void RefreshUiState()
        {
            if (saveSession == null || editor == null || editor.State == null || editor.State.map == null)
            {
                return;
            }

            var map = editor.State.map;
            if (saveStatusLabel != null)
            {
                saveStatusLabel.text = "保存：" + SaveStatusLabel(saveSession.SaveStatus) +
                                        " · 未落盘事务 " + saveSession.PendingTransactions;
            }

            if (mapStatusLabel != null)
            {
                mapStatusLabel.text = "地图 " + map.id + " · " + map.width + "×" + map.height;
            }

            foreach (var layerId in LayerIds)
            {
                if (layerButtons.TryGetValue(layerId, out var button))
                {
                    button.text = (currentLayerId == layerId ? "▶ " : "") + LayerLabel(layerId);
                }

                if (layerVisibilityButtons.TryGetValue(layerId, out var visibilityButton))
                {
                    visibilityButton.text = layerEditState.IsVisible(layerId) ? "显" : "隐";
                }

                if (layerLockButtons.TryGetValue(layerId, out var lockButton))
                {
                    lockButton.text = layerEditState.IsLocked(layerId) ? "锁" : "开";
                }
            }

            if (inspectorLabel != null)
            {
                inspectorLabel.text = "工具：" + currentTool + "\n" +
                                      "图层：" + LayerLabel(currentLayerId) + "\n" +
                                      "World Revision：" + editor.State.revision + "\n" +
                                      "Map Cells：" + map.cells.Count + "\n" +
                                      "Map Objects：" + (map.objects == null ? 0 : map.objects.Count) + "\n" +
                                      "选择：" + (selection.IsEmpty ? "无" : selection.ToString()) + "\n" +
                                      "Published：" + (editor.State.publishedMap == null ? "否" : "是") + "\n" +
                                      "拾取可选择最上方可见层；锁定层可拾取但不可修改。";
            }

            if (historyLabel != null)
            {
                historyLabel.text = "状态：" + status + "\n最近保存：" + saveSession.LastAction;
            }
        }

        private static VisualElement CreatePanel(Color color, float fixedWidthOrHeight)
        {
            var panel = new VisualElement();
            panel.style.backgroundColor = color;
            panel.style.flexShrink = 0f;
            if (fixedWidthOrHeight > 0f)
            {
                panel.style.width = fixedWidthOrHeight;
            }

            return panel;
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
                default:
                    return "Annotation";
            }
        }

        private static string SaveStatusLabel(M2SaveStatus saveStatus)
        {
            switch (saveStatus)
            {
                case M2SaveStatus.Unsaved:
                    return "未落盘";
                case M2SaveStatus.Saving:
                    return "保存中";
                case M2SaveStatus.Safe:
                    return "已安全保存";
                case M2SaveStatus.Failed:
                    return "保存失败";
                default:
                    return saveStatus.ToString();
            }
        }
    }
}
