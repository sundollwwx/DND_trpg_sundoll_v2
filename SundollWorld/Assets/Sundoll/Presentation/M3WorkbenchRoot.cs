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
        private M4PieceAssetCatalog pieceAssetCatalog;
        private M5ConsoleFacade consoleFacade;
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
        private M5WorkbenchConsoleProjection consoleProjection;
        private Label pieceLibraryLabel;
        private TextField pieceSearchField;
        private TextField pieceCategoryField;
        private TextField pieceTagsField;
        private TextField pieceImagePathField;
        private TextField pieceRelationTargetField;
        private TextField pieceAttachmentSlotField;
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
        private TextField mapIdField;
        private TextField mapNameField;
        private TextField fogXField;
        private TextField fogYField;
        private TextField annotationIdField;
        private TextField annotationTextField;
        private TextField interactionObjectField;
        private VisualElement mapListContainer;
        private Label consoleLabel;
        private VisualElement hierarchyContainer;
        private VisualElement contextMenuContainer;
        private Label hostModeLabel;
        private bool hostPreviewMode;
        private Vector2Int contextMenuCell;
        private string selectedMapObjectId;
        // These containers are rebuilt only when their authoritative inputs change.
        // RefreshUiState still updates lightweight status labels on its timer, but
        // avoids repeating UI Toolkit allocations during idle editing.
        private int lastHierarchyRevision = -1;
        private string lastHierarchySelectedMapObjectId;
        private string lastHierarchySelectedPieceInstanceId;
        private int lastMapListRevision = -1;
        private int lastPieceListRevision = -1;
        private string lastPieceListSearch;
        private string lastPieceListDefinitionId;
        private string lastPieceListInstanceId;

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

            pieceProjection.Bind(commandBus, pieceAssetCatalog);
            consoleProjection = GetComponentInChildren<M5WorkbenchConsoleProjection>();
            if (consoleProjection == null)
            {
                var consoleObject = new GameObject("M5ConsoleProjection");
                consoleObject.transform.SetParent(transform, false);
                consoleProjection = consoleObject.AddComponent<M5WorkbenchConsoleProjection>();
            }

            consoleProjection.Bind(commandBus);
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
            pieceAssetCatalog = new M4PieceAssetCatalog(projectRoot);
            consoleFacade = new M5ConsoleFacade(commandBus);
            M5ConsoleQueries.Ensure(commandBus.State);
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
            var consoleButton = new Button(CreateHostMap) { text = "新建主持地图" };
            consoleButton.style.marginLeft = 10f;
            topBar.Add(consoleButton);
            hostModeLabel = new Label { name = "HostMode" };
            hostModeLabel.style.marginLeft = 14f;
            topBar.Add(hostModeLabel);
            var hostModeButton = new Button(ToggleHostPreviewMode) { text = "切换主持预览" };
            hostModeButton.name = "ToggleHostMode";
            hostModeButton.style.marginLeft = 8f;
            hostModeButton.style.marginRight = 12f;
            topBar.Add(hostModeButton);
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
            contextMenuContainer = BuildContextMenu();
            root.Add(contextMenuContainer);
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
            pieceImagePathField = new TextField("图片路径") { name = "PieceImagePath" };
            pieceImagePathField.style.marginTop = 6f;
            panel.Add(pieceImagePathField);
            var pickImageButton = new Button(PickPieceImageFile) { text = "选择图片文件" };
            pickImageButton.name = "PickPieceImageFile";
            pickImageButton.style.marginTop = 4f;
            panel.Add(pickImageButton);
            var importImageButton = new Button(ImportPieceImageFromPath) { text = "导入图片路径" };
            importImageButton.style.marginTop = 4f;
            panel.Add(importImageButton);
            pieceLibraryLabel = new Label { name = "PieceLibraryBody" };
            pieceLibraryLabel.style.marginTop = 8f;
            pieceLibraryLabel.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(pieceLibraryLabel);
            pieceListContainer = new ScrollView(ScrollViewMode.Vertical) { name = "PieceLibraryList" };
            pieceListContainer.style.marginTop = 6f;
            pieceListContainer.style.maxHeight = 250f;
            panel.Add(pieceListContainer);
            mapIdField = new TextField("地图 ID") { name = "HostMapId" };
            mapIdField.style.marginTop = 8f;
            panel.Add(mapIdField);
            mapNameField = new TextField("地图名称") { name = "HostMapName" };
            mapNameField.style.marginTop = 4f;
            panel.Add(mapNameField);
            var switchMapButton = new Button(SwitchHostMap) { text = "切换主持地图" };
            switchMapButton.style.marginTop = 4f;
            panel.Add(switchMapButton);
            var renameMapButton = new Button(RenameHostMap) { text = "重命名当前地图" };
            renameMapButton.name = "RenameHostMap";
            renameMapButton.style.marginTop = 4f;
            panel.Add(renameMapButton);
            consoleLabel = new Label { name = "HostConsoleBody" };
            consoleLabel.style.marginTop = 6f;
            consoleLabel.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(consoleLabel);
            mapListContainer = new ScrollView(ScrollViewMode.Vertical) { name = "HostMapList" };
            mapListContainer.style.marginTop = 5f;
            mapListContainer.style.maxHeight = 100f;
            panel.Add(mapListContainer);

            var hierarchyTitle = new Label("Hierarchy") { name = "HierarchyTitle" };
            hierarchyTitle.style.marginTop = 10f;
            panel.Add(hierarchyTitle);
            hierarchyContainer = new ScrollView(ScrollViewMode.Vertical) { name = "HostHierarchy" };
            hierarchyContainer.style.marginTop = 5f;
            hierarchyContainer.style.maxHeight = 220f;
            panel.Add(hierarchyContainer);

            var fogTitle = new Label("迷雾 / 标注 / 交互");
            fogTitle.style.marginTop = 10f;
            panel.Add(fogTitle);
            fogXField = new TextField("格子 X") { name = "FogX" };
            fogXField.style.marginTop = 4f;
            panel.Add(fogXField);
            fogYField = new TextField("格子 Y") { name = "FogY" };
            fogYField.style.marginTop = 4f;
            panel.Add(fogYField);
            var hideFogButton = new Button(() => SetFogFromUi(false)) { text = "隐藏格子" };
            hideFogButton.style.marginTop = 4f;
            panel.Add(hideFogButton);
            var revealFogButton = new Button(() => SetFogFromUi(true)) { text = "揭示格子" };
            revealFogButton.style.marginTop = 4f;
            panel.Add(revealFogButton);
            annotationIdField = new TextField("标注 ID") { name = "AnnotationId" };
            annotationIdField.style.marginTop = 5f;
            panel.Add(annotationIdField);
            annotationTextField = new TextField("标注文本") { name = "AnnotationText" };
            annotationTextField.style.marginTop = 4f;
            panel.Add(annotationTextField);
            var upsertAnnotationButton = new Button(UpsertAnnotationFromUi) { text = "保存动态标注" };
            upsertAnnotationButton.style.marginTop = 4f;
            panel.Add(upsertAnnotationButton);
            var removeAnnotationButton = new Button(RemoveAnnotationFromUi) { text = "删除动态标注" };
            removeAnnotationButton.style.marginTop = 4f;
            panel.Add(removeAnnotationButton);
            interactionObjectField = new TextField("对象 ID") { name = "InteractionObjectId" };
            interactionObjectField.style.marginTop = 5f;
            panel.Add(interactionObjectField);
            var openInteractionButton = new Button(() => SetInteractionFromUi(true)) { text = "打开对象" };
            openInteractionButton.style.marginTop = 4f;
            panel.Add(openInteractionButton);
            var closeInteractionButton = new Button(() => SetInteractionFromUi(false)) { text = "关闭对象" };
            closeInteractionButton.style.marginTop = 4f;
            panel.Add(closeInteractionButton);
            WrapPanelChildrenInScrollView(panel, "ToolPanelScroll");
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
            var lowerStackButton = new Button(() => MoveSelectedPieceStack(-1)) { text = "堆叠上移" };
            lowerStackButton.style.marginTop = 5f;
            panel.Add(lowerStackButton);
            var raiseStackButton = new Button(() => MoveSelectedPieceStack(1)) { text = "堆叠下移" };
            raiseStackButton.style.marginTop = 5f;
            panel.Add(raiseStackButton);
            pieceRelationTargetField = new TextField("关系目标实例") { name = "PieceRelationTarget" };
            pieceRelationTargetField.style.marginTop = 10f;
            panel.Add(pieceRelationTargetField);
            pieceAttachmentSlotField = new TextField("附着槽") { name = "PieceAttachmentSlot" };
            pieceAttachmentSlotField.style.marginTop = 4f;
            panel.Add(pieceAttachmentSlotField);
            var containerButton = new Button(MoveSelectedPieceToContainer) { text = "收入容器" };
            containerButton.style.marginTop = 4f;
            panel.Add(containerButton);
            var attachButton = new Button(AttachSelectedPiece) { text = "附着到目标" };
            attachButton.style.marginTop = 4f;
            panel.Add(attachButton);
            WrapPanelChildrenInScrollView(panel, "InspectorScroll");
            return panel;
        }

        private static void WrapPanelChildrenInScrollView(VisualElement panel, string scrollName)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = scrollName };
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            var existingChildren = new List<VisualElement>();
            foreach (var child in panel.Children())
            {
                existingChildren.Add(child);
            }

            foreach (var child in existingChildren)
            {
                scroll.Add(child);
            }

            panel.Add(scroll);
        }

        private void QueueSave()
        {
            var operation = saveSession.QueueSave("Workbench 手动保存 Snapshot");
            status = operation.Status == M2SaveStatus.Saving ? "Workbench Snapshot 保存中" : saveSession.LastAction;
            RefreshUiState();
        }

        private void PickPieceImageFile()
        {
            if (M4NativeFilePicker.TryPickImageFile(out var path, out var diagnostic))
            {
                if (pieceImagePathField != null)
                {
                    pieceImagePathField.SetValueWithoutNotify(path);
                }

                status = "已选择图片文件，可继续导入";
            }
            else
            {
                status = diagnostic;
            }

            RefreshUiState();
        }

        private void CreateHostMap()
        {
            if (consoleFacade == null)
            {
                return;
            }

            var id = mapIdField == null ? string.Empty : mapIdField.value;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = "map-host-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }

            var name = mapNameField == null ? id : mapNameField.value;
            var receipt = consoleFacade.CreateMap(id.Trim(), string.IsNullOrWhiteSpace(name) ? id : name.Trim());
            CommitConsoleReceipt(receipt);
            status = receipt.accepted ? "已创建主持地图：" + id : receipt.message;
            RefreshUiState();
        }

        private void SwitchHostMap()
        {
            if (consoleFacade == null || mapIdField == null || string.IsNullOrWhiteSpace(mapIdField.value))
            {
                status = "请输入要切换的地图 ID";
                RefreshUiState();
                return;
            }

            var receipt = consoleFacade.SwitchMap(mapIdField.value.Trim());
            CommitConsoleReceipt(receipt);
            status = receipt.accepted ? "已切换主持地图：" + mapIdField.value.Trim() : receipt.message;
            RefreshUiState();
        }

        private void RenameHostMap()
        {
            if (consoleFacade == null || mapNameField == null)
            {
                return;
            }

            var console = M5ConsoleQueries.Ensure(commandBus.State);
            var name = mapNameField.value == null ? string.Empty : mapNameField.value.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                status = "地图名称不能为空";
                RefreshUiState();
                return;
            }

            var receipt = consoleFacade.RenameMap(console.activeMapId, name);
            CommitConsoleReceipt(receipt);
            status = receipt.accepted ? "当前主持地图已重命名" : receipt.message;
            RefreshUiState();
        }

        private void ToggleHostPreviewMode()
        {
            hostPreviewMode = !hostPreviewMode;
            status = hostPreviewMode ? "已进入主持预览：迷雾与动态标注可直接检查" : "已回到地图编辑模式";
            RefreshUiState();
        }

        private VisualElement BuildContextMenu()
        {
            var menu = new VisualElement { name = "HostContextMenu" };
            menu.style.position = Position.Absolute;
            menu.style.width = 246f;
            menu.style.paddingLeft = 8f;
            menu.style.paddingRight = 8f;
            menu.style.paddingTop = 8f;
            menu.style.paddingBottom = 8f;
            menu.style.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 0.98f);
            menu.style.borderTopWidth = 1f;
            menu.style.borderBottomWidth = 1f;
            menu.style.borderLeftWidth = 1f;
            menu.style.borderRightWidth = 1f;
            menu.style.borderTopColor = new Color(0.35f, 0.42f, 0.55f, 1f);
            menu.style.borderBottomColor = new Color(0.35f, 0.42f, 0.55f, 1f);
            menu.style.borderLeftColor = new Color(0.35f, 0.42f, 0.55f, 1f);
            menu.style.borderRightColor = new Color(0.35f, 0.42f, 0.55f, 1f);
            menu.style.display = DisplayStyle.None;
            return menu;
        }

        public bool IsContextMenuVisible => contextMenuVisible;

        private bool contextMenuVisible;

        public void ShowMapContextMenu(Vector2Int cell, Vector2 screenPosition)
        {
            if (contextMenuContainer == null)
            {
                return;
            }

            contextMenuCell = cell;
            var mapObject = FindObjectAt(cell);
            contextMenuContainer.Clear();
            var title = new Label("格子 " + cell.x + ", " + cell.y);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 5f;
            contextMenuContainer.Add(title);

            AddContextAction("ContextPick", "拾取当前格内容", () => PickAt(cell));
            if (mapObject != null)
            {
                selectedMapObjectId = mapObject.id;
                AddContextAction("ContextOpen", "打开 " + mapObject.id, () => OpenObjectAt(cell));
                AddContextAction("ContextClose", "关闭 " + mapObject.id, () => CloseObjectAt(cell));
                AddContextAction("ContextToggle", "切换 " + mapObject.id, () => ToggleObjectAt(cell));
                AddContextAction("ContextRotate", "顺时针旋转 90°", () => RotateObjectAt(cell));
                AddContextAction("ContextRemove", "删除 " + mapObject.id, () => RemoveObjectAt(cell));
            }
            else
            {
                AddContextAction("ContextAddDoor", "添加门", () => AddMapObjectAt(cell, M3MapObjectKind.Door));
                AddContextAction("ContextAddChest", "添加箱子", () => AddMapObjectAt(cell, M3MapObjectKind.Chest));
            }

            var console = M5ConsoleQueries.Ensure(commandBus.State);
            var revealed = console.IsRevealed(console.activeMapId, cell.x, cell.y);
            AddContextAction("ContextFog", revealed ? "隐藏此格迷雾" : "揭示此格迷雾", () => SetFogAt(cell, !revealed));
            AddContextAction("ContextAnnotation", "在此格新建动态标注", () => CreateAnnotationAt(cell));
            AddContextAction("ContextDismiss", "取消", DismissContextMenu);

            var top = Mathf.Clamp(Screen.height - screenPosition.y, 60f, Mathf.Max(60f, Screen.height - 300f));
            var left = Mathf.Clamp(screenPosition.x, 8f, Mathf.Max(8f, Screen.width - 260f));
            contextMenuContainer.style.left = left;
            contextMenuContainer.style.top = top;
            contextMenuContainer.style.display = DisplayStyle.Flex;
            contextMenuVisible = true;
        }

        public void DismissContextMenu()
        {
            if (contextMenuContainer != null)
            {
                contextMenuContainer.style.display = DisplayStyle.None;
            }

            contextMenuVisible = false;
        }

        private void AddContextAction(string name, string text, Action action)
        {
            var button = new Button(() =>
            {
                if (action != null)
                {
                    action();
                }

                DismissContextMenu();
            })
            {
                name = name,
                text = text
            };
            button.style.marginTop = 3f;
            button.style.minHeight = 26f;
            contextMenuContainer.Add(button);
        }

        public void AddMapObjectAt(Vector2Int cell, M3MapObjectKind kind)
        {
            var prefix = kind == M3MapObjectKind.Door ? "door-" : "chest-";
            var objectId = prefix + Guid.NewGuid().ToString("N").Substring(0, 10);
            var receipt = editor.AddMapObject(objectId, kind, cell.x, cell.y);
            if (receipt != null && receipt.accepted)
            {
                selectedMapObjectId = objectId;
            }

            CommitReceipt(receipt);
        }

        private void RemoveObjectAt(Vector2Int cell)
        {
            var mapObject = FindObjectAt(cell);
            if (mapObject == null)
            {
                status = "该格没有可删除的门或箱子对象";
                RefreshUiState();
                return;
            }

            CommitReceipt(editor.RemoveMapObject(mapObject.id));
            selectedMapObjectId = null;
        }

        private void SetFogAt(Vector2Int cell, bool revealed)
        {
            var console = M5ConsoleQueries.Ensure(commandBus.State);
            var receipt = consoleFacade.SetFog(console.activeMapId, cell.x, cell.y, revealed);
            CommitConsoleReceipt(receipt);
            status = receipt.accepted ? (revealed ? "已揭示此格迷雾" : "已隐藏此格迷雾") : receipt.message;
            RefreshUiState();
        }

        private void CreateAnnotationAt(Vector2Int cell)
        {
            var console = M5ConsoleQueries.Ensure(commandBus.State);
            var annotationId = "note-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            var receipt = consoleFacade.UpsertAnnotation(
                annotationId,
                console.activeMapId,
                cell.x,
                cell.y,
                "标注 " + cell.x + "," + cell.y);
            if (receipt != null && receipt.accepted)
            {
                if (annotationIdField != null)
                {
                    annotationIdField.SetValueWithoutNotify(annotationId);
                }

                if (annotationTextField != null)
                {
                    annotationTextField.SetValueWithoutNotify("标注 " + cell.x + "," + cell.y);
                }
            }

            CommitConsoleReceipt(receipt);
            status = receipt.accepted ? "已在当前格创建动态标注" : receipt.message;
            RefreshUiState();
        }

        private void SetFogFromUi(bool revealed)
        {
            if (!TryReadIntField(fogXField, out var x) || !TryReadIntField(fogYField, out var y))
            {
                status = "迷雾格坐标必须是整数";
                RefreshUiState();
                return;
            }

            var console = M5ConsoleQueries.Ensure(commandBus.State);
            var receipt = consoleFacade.SetFog(console.activeMapId, x, y, revealed);
            CommitConsoleReceipt(receipt);
            status = receipt.accepted ? (revealed ? "已揭示迷雾格" : "已隐藏迷雾格") : receipt.message;
            RefreshUiState();
        }

        private void UpsertAnnotationFromUi()
        {
            var console = M5ConsoleQueries.Ensure(commandBus.State);
            var id = annotationIdField == null ? string.Empty : annotationIdField.value;
            var text = annotationTextField == null ? string.Empty : annotationTextField.value;
            if (string.IsNullOrWhiteSpace(id))
            {
                status = "标注 ID 不能为空";
                RefreshUiState();
                return;
            }

            var cell = selection.IsEmpty ? new Vector2Int(1, 1) : new Vector2Int(selection.MinX, selection.MinY);
            var receipt = consoleFacade.UpsertAnnotation(id.Trim(), console.activeMapId, cell.x, cell.y, text ?? string.Empty);
            CommitConsoleReceipt(receipt);
            status = receipt.accepted ? "动态标注已保存" : receipt.message;
            RefreshUiState();
        }

        private void RemoveAnnotationFromUi()
        {
            var id = annotationIdField == null ? string.Empty : annotationIdField.value;
            if (string.IsNullOrWhiteSpace(id))
            {
                status = "请输入标注 ID";
                RefreshUiState();
                return;
            }

            var receipt = consoleFacade.RemoveAnnotation(id.Trim());
            CommitConsoleReceipt(receipt);
            status = receipt.accepted ? "动态标注已删除" : receipt.message;
            RefreshUiState();
        }

        private void SetInteractionFromUi(bool open)
        {
            var id = interactionObjectField == null ? string.Empty : interactionObjectField.value;
            if (string.IsNullOrWhiteSpace(id))
            {
                status = "请输入交互对象 ID";
                RefreshUiState();
                return;
            }

            var receipt = consoleFacade.SetInteractionState(id.Trim(), open);
            CommitConsoleReceipt(receipt);
            status = receipt.accepted ? (open ? "对象已打开" : "对象已关闭") : receipt.message;
            RefreshUiState();
        }

        private static bool TryReadIntField(TextField field, out int value)
        {
            return int.TryParse(field == null ? string.Empty : field.value, out value);
        }

        private void CommitConsoleReceipt(M1CommandReceipt receipt)
        {
            if (receipt == null)
            {
                return;
            }

            if (receipt.accepted)
            {
                saveSession.RecordAccepted(receipt, consoleFacade.State);
                projection.RefreshAll();
                if (consoleProjection != null)
                {
                    consoleProjection.RefreshAll();
                }
                if (pieceProjection != null)
                {
                    pieceProjection.RefreshAll();
                }
            }
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

        private void ImportPieceImageFromPath()
        {
            var path = pieceImagePathField == null ? string.Empty : pieceImagePathField.value;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path.Trim()))
            {
                status = "请输入存在的图片文件路径，或先使用“选择图片文件”";
                RefreshUiState();
                return;
            }

            try
            {
                path = path.Trim();
                var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                var result = M4RuntimeImageImporter.Import(
                    pieceAssetCatalog,
                    File.ReadAllBytes(path),
                    extension,
                    MimeTypeForImageExtension(extension));
                if (!result.accepted || result.asset == null)
                {
                    status = result.diagnostic;
                    RefreshUiState();
                    return;
                }

                var existing = M4PieceQueries.FindAsset(pieceLibrary.State, result.asset.id);
                var assetReceipt = existing == null ? pieceLibrary.RegisterAsset(result.asset) : null;
                if (assetReceipt != null && !assetReceipt.accepted)
                {
                    status = assetReceipt.message;
                    RefreshUiState();
                    return;
                }

                if (assetReceipt != null)
                {
                    CommitPieceReceipt(assetReceipt);
                }

                var definition = M4PieceQueries.FindDefinition(pieceLibrary.State, selectedPieceDefinitionId);
                if (definition != null)
                {
                    var update = pieceLibrary.UpdateDefinition(
                        definition.id,
                        definition.displayName,
                        definition.category,
                        definition.tags,
                        result.asset.id,
                        definition.footprintWidth,
                        definition.footprintHeight);
                    CommitPieceReceipt(update);
                    status = update.accepted ? "图片已导入并绑定到当前定义" : update.message;
                }
                else
                {
                    status = "图片已导入；请选择定义后再绑定";
                }
            }
            catch (Exception exception)
            {
                status = "图片路径导入失败：" + exception.Message;
            }

            RefreshUiState();
        }

        private static string MimeTypeForImageExtension(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case "jpg":
                case "jpeg":
                    return "image/jpeg";
                case "gif":
                    return "image/gif";
                case "webp":
                    return "image/webp";
                default:
                    return "image/png";
            }
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

        private void MoveSelectedPieceStack(int direction)
        {
            var instance = M4PieceQueries.FindInstance(pieceLibrary == null ? null : pieceLibrary.State, selectedPieceInstanceId);
            if (instance == null || instance.location == null || instance.location.kind != M1PieceLocationKind.OnBoard)
            {
                status = "请先选择一个已放置的棋子";
                RefreshUiState();
                return;
            }

            var receipt = pieceLibrary.SetStackOrder(instance.id, instance.location.stackOrder + direction);
            CommitPieceReceipt(receipt);
            status = receipt.accepted ? "棋子堆叠顺序已调整" : receipt.message;
            RefreshUiState();
        }

        private void MoveSelectedPieceToContainer()
        {
            var targetId = pieceRelationTargetField == null ? string.Empty : pieceRelationTargetField.value;
            if (string.IsNullOrWhiteSpace(selectedPieceInstanceId) || string.IsNullOrWhiteSpace(targetId))
            {
                status = "请选择棋子并填写容器实例 ID";
                RefreshUiState();
                return;
            }

            var receipt = pieceLibrary.MoveToContainer(selectedPieceInstanceId, targetId.Trim());
            CommitPieceReceipt(receipt);
            status = receipt.accepted ? "棋子已收入容器" : receipt.message;
            RefreshUiState();
        }

        private void AttachSelectedPiece()
        {
            var targetId = pieceRelationTargetField == null ? string.Empty : pieceRelationTargetField.value;
            var slot = pieceAttachmentSlotField == null ? string.Empty : pieceAttachmentSlotField.value;
            if (string.IsNullOrWhiteSpace(selectedPieceInstanceId) || string.IsNullOrWhiteSpace(targetId))
            {
                status = "请选择棋子并填写附着目标实例 ID";
                RefreshUiState();
                return;
            }

            var receipt = pieceLibrary.Attach(selectedPieceInstanceId, targetId.Trim(), slot == null ? string.Empty : slot.Trim());
            CommitPieceReceipt(receipt);
            status = receipt.accepted ? "棋子已附着到目标" : receipt.message;
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

        private void RefreshHierarchy()
        {
            if (hierarchyContainer == null || editor == null || editor.State == null)
            {
                return;
            }

            hierarchyContainer.Clear();
            var map = editor.State.map;
            var objectHeader = new Label("地图对象") { name = "HierarchyMapObjects" };
            objectHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            hierarchyContainer.Add(objectHeader);
            if (map == null || map.objects == null || map.objects.Count == 0)
            {
                hierarchyContainer.Add(new Label("  （暂无门或箱子）"));
            }
            else
            {
                foreach (var mapObject in map.objects)
                {
                    if (mapObject == null)
                    {
                        continue;
                    }

                    var objectId = mapObject.id;
                    var objectButton = new Button(() => SelectMapObject(objectId))
                    {
                        name = "HierarchyMapObject-" + objectId,
                        text = (selectedMapObjectId == objectId ? "▶ " : "") +
                               (mapObject.kind == M3MapObjectKind.Door ? "门" : "箱子") +
                               " " + objectId + " · " + mapObject.x + "," + mapObject.y +
                               " · " + (mapObject.state == M3MapObjectOpenState.Open ? "开" : "关")
                    };
                    objectButton.style.marginTop = 2f;
                    hierarchyContainer.Add(objectButton);
                }
            }

            var pieceHeader = new Label("棋子实例") { name = "HierarchyPieces" };
            pieceHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            pieceHeader.style.marginTop = 7f;
            hierarchyContainer.Add(pieceHeader);
            var instances = editor.State.pieceInstances;
            if (instances == null || instances.Count == 0)
            {
                hierarchyContainer.Add(new Label("  （暂无棋子实例）"));
            }
            else
            {
                foreach (var instance in instances)
                {
                    if (instance == null)
                    {
                        continue;
                    }

                    var instanceId = instance.id;
                    var location = instance.location == null ? "未知" : instance.location.kind.ToString();
                    var instanceButton = new Button(() => SelectPieceInstance(instanceId))
                    {
                        name = "HierarchyPiece-" + instanceId,
                        text = (selectedPieceInstanceId == instanceId ? "▶ " : "") + instanceId + " · " + location
                    };
                    instanceButton.style.marginTop = 2f;
                    hierarchyContainer.Add(instanceButton);
                }
            }

            var annotationHeader = new Label("动态标注") { name = "HierarchyAnnotations" };
            annotationHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            annotationHeader.style.marginTop = 7f;
            hierarchyContainer.Add(annotationHeader);
            var console = commandBus == null ? null : commandBus.State.m5Console;
            var annotations = console == null ? null : console.annotations;
            var annotationCount = 0;
            if (annotations != null)
            {
                foreach (var annotation in annotations)
                {
                    if (annotation == null || annotation.mapId != console.activeMapId)
                    {
                        continue;
                    }

                    annotationCount++;
                    var annotationId = annotation.id;
                    var annotationButton = new Button(() => SelectAnnotation(annotationId))
                    {
                        name = "HierarchyAnnotation-" + annotationId,
                        text = annotationId + " · " + annotation.x + "," + annotation.y +
                               " · " + (annotation.visible ? "显" : "隐")
                    };
                    annotationButton.style.marginTop = 2f;
                    hierarchyContainer.Add(annotationButton);
                }
            }

            if (annotationCount == 0)
            {
                hierarchyContainer.Add(new Label("  （暂无动态标注）"));
            }
        }

        private void SelectMapObject(string objectId)
        {
            var mapObject = editor == null ? null : editor.FindMapObject(objectId);
            if (mapObject == null)
            {
                status = "地图对象不存在：" + objectId;
                RefreshUiState();
                return;
            }

            selectedMapObjectId = objectId;
            selection = new M3GridBounds(mapObject.x, mapObject.y, mapObject.x, mapObject.y);
            if (interactionObjectField != null)
            {
                interactionObjectField.SetValueWithoutNotify(objectId);
            }

            status = "已选择地图对象：" + objectId;
            RefreshUiState();
        }

        private void SelectAnnotation(string annotationId)
        {
            var console = commandBus == null ? null : M5ConsoleQueries.Ensure(commandBus.State);
            var annotation = console == null ? null : console.FindAnnotation(annotationId);
            if (annotation == null)
            {
                status = "动态标注不存在：" + annotationId;
                RefreshUiState();
                return;
            }

            selection = new M3GridBounds(annotation.x, annotation.y, annotation.x, annotation.y);
            if (annotationIdField != null)
            {
                annotationIdField.SetValueWithoutNotify(annotation.id);
            }

            if (annotationTextField != null)
            {
                annotationTextField.SetValueWithoutNotify(annotation.text ?? string.Empty);
            }

            status = "已选择动态标注：" + annotation.id;
            RefreshUiState();
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

            if (hostModeLabel != null)
            {
                hostModeLabel.text = hostPreviewMode ? "主持预览" : "地图编辑";
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

            var worldRevision = editor.State.revision;
            if (lastHierarchyRevision != worldRevision ||
                !string.Equals(lastHierarchySelectedMapObjectId, selectedMapObjectId, StringComparison.Ordinal) ||
                !string.Equals(lastHierarchySelectedPieceInstanceId, selectedPieceInstanceId, StringComparison.Ordinal))
            {
                RefreshHierarchy();
                lastHierarchyRevision = worldRevision;
                lastHierarchySelectedMapObjectId = selectedMapObjectId;
                lastHierarchySelectedPieceInstanceId = selectedPieceInstanceId;
            }

            if (consoleLabel != null)
            {
                var console = commandBus.State.m5Console;
                consoleLabel.text = "主持地图 " + (console == null ? 0 : console.maps.Count) +
                                    " · 当前 " + (console == null ? "无" : console.activeMapId) +
                                    "\n迷雾记录 " + (console == null ? 0 : console.fogCells.Count) +
                                    " · 动态标注 " + (console == null ? 0 : console.annotations.Count);
            }

            if (mapListContainer != null && lastMapListRevision != worldRevision)
            {
                mapListContainer.Clear();
                var console = commandBus.State.m5Console;
                if (console != null && console.maps != null)
                {
                    foreach (var mapSlot in console.maps)
                    {
                        if (mapSlot == null)
                        {
                            continue;
                        }

                        var mapId = mapSlot.id;
                        var mapButton = new Button(() =>
                        {
                            var receipt = consoleFacade.SwitchMap(mapId);
                            CommitConsoleReceipt(receipt);
                            status = receipt.accepted ? "已切换主持地图：" + mapId : receipt.message;
                            RefreshUiState();
                        })
                        {
                            text = (console.activeMapId == mapId ? "▶ " : "") + mapSlot.displayName + " [" + mapId + "]"
                        };
                        mapButton.style.marginTop = 2f;
                        mapListContainer.Add(mapButton);
                    }
                }

                lastMapListRevision = worldRevision;
            }

            var pieceSearch = pieceSearchField == null ? string.Empty : pieceSearchField.value ?? string.Empty;
            if (lastPieceListRevision != worldRevision ||
                !string.Equals(lastPieceListSearch, pieceSearch, StringComparison.Ordinal) ||
                !string.Equals(lastPieceListDefinitionId, selectedPieceDefinitionId, StringComparison.Ordinal) ||
                !string.Equals(lastPieceListInstanceId, selectedPieceInstanceId, StringComparison.Ordinal))
            {
                RefreshPieceLibraryList();
                lastPieceListRevision = worldRevision;
                lastPieceListSearch = pieceSearch;
                lastPieceListDefinitionId = selectedPieceDefinitionId;
                lastPieceListInstanceId = selectedPieceInstanceId;
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
