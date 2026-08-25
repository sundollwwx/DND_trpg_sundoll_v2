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
        private M4PieceLibraryFacade pieceLibrary;
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
        private M4WorkbenchPieceProjection pieceProjection;
        private Label pieceLibraryLabel;
        private TextField pieceSearchField;
        private TextField pieceCategoryField;
        private TextField pieceTagsField;
        private VisualElement pieceListContainer;
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
        private string selectedPieceDefinitionId;
        private string selectedPieceInstanceId;

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
            pieceProjection = GetComponentInChildren<M4WorkbenchPieceProjection>();
            if (pieceProjection == null)
            {
                var pieceObject = new GameObject("M4PieceProjection");
                pieceObject.transform.SetParent(transform, false);
                pieceProjection = pieceObject.AddComponent<M4WorkbenchPieceProjection>();
            }

            pieceProjection.Bind(commandBus);
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
            // M4 intentionally uses a new development root. Existing M1/M2/M3
            // samples remain readable and untouched while piece data evolves.
            var projectRoot = Path.Combine(UnityEngine.Application.persistentDataPath, "SundollWorld_M4");
            saveSession = M2SaveSession.Open(projectRoot, initialBus.State);
            commandBus = new M1CommandBus(
                saveSession.State,
                new M1LocalAuthority(new AllowAllRulePolicy()));
            editor = new M3MapEditorFacade(commandBus);
            pieceLibrary = new M4PieceLibraryFacade(commandBus);
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
            var pieceTitle = new Label("棋子库") { name = "PieceLibraryTitle" };
            pieceTitle.style.marginTop = 18f;
            panel.Add(pieceTitle);
            pieceSearchField = new TextField { name = "PieceSearch", tooltip = "按名称、分类或标签搜索" };
            pieceSearchField.style.marginTop = 5f;
            pieceSearchField.RegisterValueChangedCallback(_ => RefreshPieceLibraryList());
            panel.Add(pieceSearchField);
            pieceCategoryField = new TextField("分类") { name = "PieceCategory" };
            pieceCategoryField.style.marginTop = 4f;
            panel.Add(pieceCategoryField);
            pieceTagsField = new TextField("标签") { name = "PieceTags" };
            pieceTagsField.style.marginTop = 4f;
            panel.Add(pieceTagsField);
            var updateDefinitionButton = new Button(SaveSelectedPieceDefinition) { text = "保存定义分类/标签" };
            updateDefinitionButton.style.marginTop = 4f;
            panel.Add(updateDefinitionButton);
            var createPieceButton = new Button(CreatePlaceholderPiece) { text = "新增占位定义" };
            createPieceButton.style.marginTop = 5f;
            panel.Add(createPieceButton);
            var createInstanceButton = new Button(CreateInstanceFromSelectedDefinition) { text = "创建实例并放置" };
            createInstanceButton.style.marginTop = 5f;
            panel.Add(createInstanceButton);
            pieceLibraryLabel = new Label { name = "PieceLibraryBody" };
            pieceLibraryLabel.style.marginTop = 8f;
            pieceLibraryLabel.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(pieceLibraryLabel);
            pieceListContainer = new ScrollView(ScrollViewMode.Vertical) { name = "PieceLibraryList" };
            pieceListContainer.style.marginTop = 6f;
            pieceListContainer.style.maxHeight = 250f;
            panel.Add(pieceListContainer);
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
            panel.Add(new Label("棋子视图使用可替换的占位色块；图片缺失不会删除实体。"));
            var placeSelectedButton = new Button(PlaceSelectedPiece) { text = "选中棋子放到选区" };
            placeSelectedButton.style.marginTop = 10f;
            panel.Add(placeSelectedButton);
            var rotateSelectedButton = new Button(RotateSelectedPiece) { text = "旋转选中棋子" };
            rotateSelectedButton.style.marginTop = 5f;
            panel.Add(rotateSelectedButton);
            var flipSelectedButton = new Button(FlipSelectedPiece) { text = "翻面选中棋子" };
            flipSelectedButton.style.marginTop = 5f;
            panel.Add(flipSelectedButton);
            var visibilityButton = new Button(ToggleSelectedPieceVisibility) { text = "切换选中棋子显隐" };
            visibilityButton.style.marginTop = 5f;
            panel.Add(visibilityButton);
            var detachButton = new Button(DetachSelectedPiece) { text = "解除选中棋子关系" };
            detachButton.style.marginTop = 5f;
            panel.Add(detachButton);
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
                if (pieceProjection != null)
                {
                    pieceProjection.RefreshAll();
                }
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
                if (pieceProjection != null)
                {
                    pieceProjection.RefreshAll();
                }
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

        private void CreatePlaceholderPiece()
        {
            if (pieceLibrary == null)
            {
                return;
            }

            const string definitionId = "m4-placeholder-token";
            if (M4PieceQueries.FindDefinition(pieceLibrary.State, definitionId) == null)
            {
                var definitionReceipt = pieceLibrary.CreateDefinition(
                    definitionId,
                    "中性占位棋子",
                    "Placeholder",
                    new[] { "中性", "占位" });
                CommitPieceReceipt(definitionReceipt);
                if (!definitionReceipt.accepted)
                {
                    return;
                }
            }

            selectedPieceDefinitionId = definitionId;
            SyncSelectedPieceDefinitionFields();
            status = "已选择占位棋子定义，可创建多个实例";
            RefreshUiState();
        }

        private void CreateInstanceFromSelectedDefinition()
        {
            if (pieceLibrary == null)
            {
                return;
            }

            if (M4PieceQueries.FindDefinition(pieceLibrary.State, selectedPieceDefinitionId) == null)
            {
                CreatePlaceholderPiece();
            }

            if (M4PieceQueries.FindDefinition(pieceLibrary.State, selectedPieceDefinitionId) == null)
            {
                return;
            }

            var instanceId = "m4-piece-" + Guid.NewGuid().ToString("N");
            var instanceReceipt = pieceLibrary.CreateInstance(selectedPieceDefinitionId, instanceId);
            CommitPieceReceipt(instanceReceipt);
            if (!instanceReceipt.accepted)
            {
                return;
            }

            selectedPieceInstanceId = instanceId;
            PlaceSelectedPiece();
        }

        private void SelectPieceDefinition(string definitionId)
        {
            selectedPieceDefinitionId = definitionId;
            SyncSelectedPieceDefinitionFields();
            status = "已选择棋子定义：" + definitionId;
            RefreshUiState();
        }

        private void SyncSelectedPieceDefinitionFields()
        {
            var definition = M4PieceQueries.FindDefinition(pieceLibrary == null ? null : pieceLibrary.State, selectedPieceDefinitionId);
            if (definition == null)
            {
                return;
            }

            if (pieceCategoryField != null)
            {
                pieceCategoryField.SetValueWithoutNotify(definition.category ?? string.Empty);
            }

            if (pieceTagsField != null)
            {
                pieceTagsField.SetValueWithoutNotify(definition.tags == null ? string.Empty : string.Join(", ", definition.tags));
            }
        }

        private void SaveSelectedPieceDefinition()
        {
            var definition = M4PieceQueries.FindDefinition(pieceLibrary == null ? null : pieceLibrary.State, selectedPieceDefinitionId);
            if (definition == null)
            {
                status = "请先选择一个棋子定义";
                RefreshUiState();
                return;
            }

            var tags = new List<string>();
            var rawTags = pieceTagsField == null ? string.Empty : pieceTagsField.value;
            foreach (var rawTag in (rawTags ?? string.Empty).Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var tag = rawTag.Trim();
                if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag))
                {
                    tags.Add(tag);
                }
            }

            var receipt = pieceLibrary.UpdateDefinition(
                definition.id,
                definition.displayName,
                pieceCategoryField == null ? definition.category : pieceCategoryField.value,
                tags,
                definition.assetId,
                definition.footprintWidth,
                definition.footprintHeight);
            CommitPieceReceipt(receipt);
            status = receipt.accepted ? "棋子定义已更新" : receipt.message;
            RefreshUiState();
        }

        private void SelectPieceInstance(string instanceId)
        {
            selectedPieceInstanceId = instanceId;
            var instance = M4PieceQueries.FindInstance(pieceLibrary == null ? null : pieceLibrary.State, instanceId);
            if (instance != null)
            {
                selectedPieceDefinitionId = instance.definitionId;
                SyncSelectedPieceDefinitionFields();
                status = "已选择棋子实例：" + instanceId;
            }

            RefreshUiState();
        }

        private void PlaceSelectedPiece()
        {
            if (pieceLibrary == null || string.IsNullOrWhiteSpace(selectedPieceInstanceId))
            {
                status = "请先从棋子库选择一个实例";
                RefreshUiState();
                return;
            }

            var instance = M4PieceQueries.FindInstance(pieceLibrary.State, selectedPieceInstanceId);
            if (instance == null)
            {
                status = "选中的棋子实例不存在";
                RefreshUiState();
                return;
            }

            var cell = selection.IsEmpty
                ? new Vector2Int(1, 1)
                : new Vector2Int(selection.MinX, selection.MinY);
            var receipt = instance.location != null && instance.location.kind == M1PieceLocationKind.OnBoard
                ? pieceLibrary.Move(instance.id, cell.x, cell.y)
                : pieceLibrary.Place(instance.id, cell.x, cell.y);
            CommitPieceReceipt(receipt);
            status = receipt.accepted ? "选中棋子已放置到选区" : receipt.message;
            RefreshUiState();
        }

        private void RotateSelectedPiece()
        {
            var instance = M4PieceQueries.FindInstance(pieceLibrary == null ? null : pieceLibrary.State, selectedPieceInstanceId);
            if (instance == null)
            {
                status = "请先选择棋子实例";
                RefreshUiState();
                return;
            }

            var receipt = pieceLibrary.SetPresentation(
                instance.id,
                (instance.rotation + 90) % 360,
                instance.flipped,
                instance.visible);
            CommitPieceReceipt(receipt);
            status = receipt.accepted ? "选中棋子已顺时针旋转 90°" : receipt.message;
            RefreshUiState();
        }

        private void FlipSelectedPiece()
        {
            var instance = M4PieceQueries.FindInstance(pieceLibrary == null ? null : pieceLibrary.State, selectedPieceInstanceId);
            if (instance == null)
            {
                status = "请先选择棋子实例";
                RefreshUiState();
                return;
            }

            var receipt = pieceLibrary.SetPresentation(instance.id, instance.rotation, !instance.flipped, instance.visible);
            CommitPieceReceipt(receipt);
            status = receipt.accepted ? "选中棋子已翻面" : receipt.message;
            RefreshUiState();
        }

        private void ToggleSelectedPieceVisibility()
        {
            var instance = M4PieceQueries.FindInstance(pieceLibrary == null ? null : pieceLibrary.State, selectedPieceInstanceId);
            if (instance == null)
            {
                status = "请先选择棋子实例";
                RefreshUiState();
                return;
            }

            var receipt = pieceLibrary.SetPresentation(instance.id, instance.rotation, instance.flipped, !instance.visible);
            CommitPieceReceipt(receipt);
            status = receipt.accepted ? "选中棋子显隐已切换" : receipt.message;
            RefreshUiState();
        }

        private void DetachSelectedPiece()
        {
            var instance = M4PieceQueries.FindInstance(pieceLibrary == null ? null : pieceLibrary.State, selectedPieceInstanceId);
            if (instance == null)
            {
                status = "请先选择棋子实例";
                RefreshUiState();
                return;
            }

            var receipt = pieceLibrary.Detach(instance.id);
            CommitPieceReceipt(receipt);
            status = receipt.accepted ? "选中棋子已解除关系" : receipt.message;
            RefreshUiState();
        }

        private void RefreshPieceLibraryList()
        {
            if (pieceListContainer == null || pieceLibrary == null)
            {
                return;
            }

            pieceListContainer.Clear();
            var search = pieceSearchField == null ? string.Empty : pieceSearchField.value ?? string.Empty;
            var definitions = pieceLibrary.State.pieceDefinitions;
            if (definitions != null)
            {
                foreach (var definition in definitions)
                {
                    if (definition == null || !MatchesPieceSearch(definition, search))
                    {
                        continue;
                    }

                    var definitionId = definition.id;
                    var definitionButton = new Button(() => SelectPieceDefinition(definitionId))
                    {
                        text = (definitionId == selectedPieceDefinitionId ? "▶ " : "") +
                               (string.IsNullOrWhiteSpace(definition.displayName) ? definitionId : definition.displayName)
                    };
                    definitionButton.style.marginTop = 3f;
                    pieceListContainer.Add(definitionButton);
                }
            }

            var instanceHeader = new Label("实例") { name = "PieceInstanceHeader" };
            instanceHeader.style.marginTop = 8f;
            pieceListContainer.Add(instanceHeader);
            var instances = pieceLibrary.State.pieceInstances;
            if (instances != null)
            {
                foreach (var instance in instances)
                {
                    if (instance == null)
                    {
                        continue;
                    }

                    var instanceId = instance.id;
                    var instanceButton = new Button(() => SelectPieceInstance(instanceId))
                    {
                        text = (instanceId == selectedPieceInstanceId ? "▶ " : "") + instanceId +
                               " · " + (instance.location == null ? "未知" : instance.location.kind.ToString())
                    };
                    instanceButton.style.marginTop = 3f;
                    pieceListContainer.Add(instanceButton);
                }
            }
        }

        private static bool MatchesPieceSearch(M4PieceDefinition definition, string search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            if ((definition.displayName ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (definition.category ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (definition.tags != null)
            {
                foreach (var tag in definition.tags)
                {
                    if ((tag ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void CommitPieceReceipt(M1CommandReceipt receipt)
        {
            if (receipt == null)
            {
                return;
            }

            if (receipt.accepted)
            {
                saveSession.RecordAccepted(receipt, pieceLibrary.State);
                if (pieceProjection != null)
                {
                    pieceProjection.RefreshAll();
                }
            }
            else
            {
                status = receipt.message;
            }
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
                                      "棋子定义：" + (editor.State.pieceDefinitions == null ? 0 : editor.State.pieceDefinitions.Count) + "\n" +
                                      "棋子实例：" + (editor.State.pieceInstances == null ? 0 : editor.State.pieceInstances.Count) + "\n" +
                                      "当前定义：" + (selectedPieceDefinitionId ?? "无") + "\n" +
                                      "当前实例：" + (selectedPieceInstanceId ?? "无") + "\n" +
                                      "选择：" + (selection.IsEmpty ? "无" : selection.ToString()) + "\n" +
                                      "Published：" + (editor.State.publishedMap == null ? "否" : "是") + "\n" +
                                      "拾取可选择最上方可见层；锁定层可拾取但不可修改。";
            }

            if (pieceLibraryLabel != null)
            {
                pieceLibraryLabel.text = "定义 " + (editor.State.pieceDefinitions == null ? 0 : editor.State.pieceDefinitions.Count) +
                                         " · 实例 " + (editor.State.pieceInstances == null ? 0 : editor.State.pieceInstances.Count) + "\n" +
                                         "缺少图片时保留数据并显示占位色块。";
            }

            RefreshPieceLibraryList();

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
