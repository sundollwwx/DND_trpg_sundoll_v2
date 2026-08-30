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
        private readonly Dictionary<string, string> selectedContentIds = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<M3CellMutation> pendingStroke = new List<M3CellMutation>();
        private readonly HashSet<M3MapCellKey> pendingStrokeKeys = new HashSet<M3MapCellKey>();
        private readonly List<M5FogCellMutation> pendingFogStroke = new List<M5FogCellMutation>();
        private readonly HashSet<string> pendingFogStrokeKeys = new HashSet<string>(StringComparer.Ordinal);
        private ProjectWorkspaceService workspaceService;
        private WorkbenchSession workbenchSession;
        private M1CommandBus commandBus;
        private M2SaveSession saveSession;
        private M3MapEditorFacade editor;
        private M4PieceLibraryFacade pieceLibrary;
        private M4PieceAssetCatalog pieceAssetCatalog;
        private M5ConsoleFacade consoleFacade;
        private M3LayerEditState layerEditState;
        private M3WorkspaceStateStore workspaceStateStore;
        private M3WorkbenchMapProjection projection;
        private M7BuiltinMapVisualCatalog mapVisualCatalog;
        private M7StarterContentManifest starterContentManifest;
        private UIDocument uiDocument;
        private PanelSettings panelSettings;
        private Label saveStatusLabel;
        private Label projectTitleLabel;
        private Label mapStatusLabel;
        private Label inspectorLabel;
        private Label historyLabel;
        private VisualElement mapViewport;
        private M3WorkbenchInput input;
        private M4WorkbenchPieceProjection pieceProjection;
        private M4WorkbenchPieceInteractionController pieceInteraction;
        private M5WorkbenchConsoleProjection consoleProjection;
        private M1WorldState audienceProjectionState;
        private M7PieceLibraryGridController pieceLibraryGrid;
        private Label pieceLibraryLabel;
        private VisualElement materialPaletteContainer;
        private TextField pieceSearchField;
        private TextField pieceCategoryField;
        private TextField pieceTagsField;
        private TextField pieceImagePathField;
        private DropdownField pieceRelationTargetField;
        private TextField pieceAttachmentSlotField;
        private VisualElement pieceListContainer;
        private VisualElement pieceInstanceListContainer;
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
        private TextField fogRadiusField;
        private TextField annotationIdField;
        private TextField annotationTextField;
        private TextField interactionObjectField;
        private VisualElement mapListContainer;
        private Label consoleLabel;
        private VisualElement hierarchyContainer;
        private VisualElement contextMenuContainer;
        private Label hostModeLabel;
        private bool hostPreviewMode;
        private bool fogStrokeActive;
        private bool annotationDragActive;
        private string annotationDragId;
        private Vector2Int lastAnnotationDragCell;
        private M7ProjectCenterPanel projectCenterPanel;
        private M7WorkbenchTabController leftTabController;
        private string currentWorkspace = "map";
        private Vector2Int contextMenuCell;
        private string selectedMapObjectId;

        private const int WorkbenchTargetFrameRate = 60;
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
        public M4PieceLibraryFacade PieceLibrary => pieceLibrary;
        public M2SaveSession SaveSession => saveSession;
        public M3LayerEditState LayerEditState => layerEditState;
        public string CurrentTool => currentTool;
        public string CurrentLayerId => currentLayerId;
        public M3GridBounds Selection => selection;
        public M3MapClipboard Clipboard => clipboard;
        public int SelectedPieceCount => pieceInteraction == null ? 0 : pieceInteraction.SelectedCount;

        // The desktop performance harness is an opt-in diagnostic surface. It
        // reads the already-composed session without becoming a production UI
        // dependency or bypassing the normal command bus.
        internal M1CommandBus CommandBusForDiagnostics => commandBus;
        internal M4PieceLibraryFacade PieceLibraryForDiagnostics => pieceLibrary;
        internal M4PieceAssetCatalog PieceAssetCatalogForDiagnostics => pieceAssetCatalog;
        internal M3WorkbenchMapProjection MapProjectionForDiagnostics => projection;
        internal M4WorkbenchPieceProjection PieceProjectionForDiagnostics => pieceProjection;
        internal M5WorkbenchConsoleProjection ConsoleProjectionForDiagnostics => consoleProjection;
        internal Camera WorkbenchCameraForDiagnostics => GetComponentInChildren<Camera>();
        internal bool IsPieceInteractionReadOnly => hostPreviewMode;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            // Use Unity's software frame cap for predictable desktop pacing.
            // The Standalone quality profile disables vSync so a missed display
            // interval cannot turn one frame into an avoidable ~33 ms spike.
            UnityEngine.Application.targetFrameRate = WorkbenchTargetFrameRate;
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

            mapVisualCatalog = new M7BuiltinMapVisualCatalog();
            starterContentManifest = M7StarterContentManifest.CreateBuiltIn(mapVisualCatalog);
            EnsureSelectedContentDefaults();
            projection.Bind(editor, layerEditState, mapVisualCatalog);
            pieceProjection = GetComponentInChildren<M4WorkbenchPieceProjection>();
            if (pieceProjection == null)
            {
                var pieceObject = new GameObject("M4PieceProjection");
                pieceObject.transform.SetParent(transform, false);
                pieceProjection = pieceObject.AddComponent<M4WorkbenchPieceProjection>();
            }

            pieceProjection.Bind(commandBus, pieceAssetCatalog);
            pieceInteraction = GetComponent<M4WorkbenchPieceInteractionController>();
            if (pieceInteraction == null)
            {
                pieceInteraction = gameObject.AddComponent<M4WorkbenchPieceInteractionController>();
            }

            pieceInteraction.Bind(this, pieceLibrary, pieceProjection);
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

            if (M7DesktopPerformanceCapture.IsRequested())
            {
                var performanceCapture = gameObject.AddComponent<M7DesktopPerformanceCapture>();
                performanceCapture.Begin(this);
            }

            if (M7DesktopSoakCapture.IsRequested())
            {
                var soakCapture = gameObject.AddComponent<M7DesktopSoakCapture>();
                soakCapture.Begin(this);
            }
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
            PersistWorkspaceState();
            pieceLibraryGrid?.Dispose();
            pieceLibraryGrid = null;
            workbenchSession?.Dispose();
            workbenchSession = null;
            saveSession = null;

            if (panelSettings != null)
            {
                Destroy(panelSettings);
                panelSettings = null;
            }
        }

        private void OnApplicationQuit()
        {
            PersistWorkspaceState();
        }

        private void InitializeDomain()
        {
            var productRoot = Path.Combine(UnityEngine.Application.persistentDataPath, "SundollWorld");
            if (UnityEngine.Application.isBatchMode ||
                M7DesktopPerformanceCapture.IsRequested() ||
                M7DesktopSoakCapture.IsRequested())
            {
                // Automated scene runs and opt-in desktop performance captures
                // must never open or mutate a person's desktop projects. Each
                // process gets an isolated root.
                productRoot = Path.Combine(
                    UnityEngine.Application.temporaryCachePath,
                    "SundollWorld-Tests",
                    Guid.NewGuid().ToString("N"));
            }

            workspaceService = new ProjectWorkspaceService(
                Path.Combine(productRoot, "Projects"),
                Path.Combine(productRoot, "Workspace"));

            if (M7DesktopPerformanceCapture.IsRequested())
            {
                var performanceProjectRoot = Path.Combine(
                    productRoot,
                    "Projects",
                    "M7DesktopPerformance");
                var performanceSession = M2SaveSession.Open(
                    performanceProjectRoot,
                    M1VerticalSlice.CreateDemoBus().State);
                AdoptSession(new ProjectWorkspaceOpenResult
                {
                    projectRoot = performanceProjectRoot,
                    saveSession = performanceSession,
                    created = true,
                    diagnostic = "M7 desktop performance isolated project"
                }, false);
                return;
            }

            if (M7DesktopSoakCapture.IsRequested())
            {
                var soakProjectRoot = Path.Combine(
                    productRoot,
                    "Projects",
                    "M7DesktopSoak");
                var soakSession = M2SaveSession.Open(
                    soakProjectRoot,
                    M1VerticalSlice.CreateDemoBus().State);
                AdoptSession(new ProjectWorkspaceOpenResult
                {
                    projectRoot = soakProjectRoot,
                    saveSession = soakSession,
                    created = true,
                    diagnostic = "M7 desktop soak isolated project"
                }, false);
                return;
            }

            ProjectWorkspaceOpenResult openResult = null;
            foreach (var recent in workspaceService.GetRecentProjects())
            {
                try
                {
                    openResult = workspaceService.Open(recent.projectRoot);
                    break;
                }
                catch (Exception)
                {
                    // A stale recent item must not block application startup.
                }
            }

            if (openResult == null)
            {
                openResult = workspaceService.Create("SundollWorld 项目");
            }

            AdoptSession(openResult, false);
        }

        private void AdoptSession(ProjectWorkspaceOpenResult openResult, bool refreshViews)
        {
            if (openResult == null || openResult.saveSession == null)
            {
                throw new ArgumentNullException(nameof(openResult));
            }

            PersistWorkspaceState();
            var previousSession = workbenchSession;
            var nextSession = new WorkbenchSession(openResult.saveSession);
            workbenchSession = nextSession;
            saveSession = nextSession.SaveSession;
            commandBus = nextSession.CommandBus;
            editor = nextSession.MapEditor;
            pieceLibrary = nextSession.PieceLibrary;
            pieceAssetCatalog = nextSession.PieceAssetCatalog;
            consoleFacade = nextSession.Console;
            workspaceStateStore = nextSession.WorkspaceStateStore;
            var workspaceLoad = workspaceStateStore.Load(editor.State.map.id, LayerIds);
            layerEditState = workspaceLoad.state;
            currentTool = string.IsNullOrWhiteSpace(workspaceLoad.currentTool) ? "画笔" : workspaceLoad.currentTool;
            currentLayerId = string.IsNullOrWhiteSpace(workspaceLoad.currentLayerId)
                ? LayerIds[0]
                : workspaceLoad.currentLayerId;
            currentWorkspace = NormalizeWorkspaceId(workspaceLoad.currentWorkspace);
            selectedContentIds.Clear();
            if (workspaceLoad.selectedContentIds != null)
            {
                foreach (var pair in workspaceLoad.selectedContentIds)
                {
                    selectedContentIds[pair.Key] = pair.Value;
                }
            }
            hasLoadedWorkspaceView = workspaceLoad.loaded && workspaceLoad.hasViewport;
            loadedWorkspaceZoom = workspaceLoad.zoom;
            loadedWorkspacePan = new Vector2(workspaceLoad.panX, workspaceLoad.panY);
            status = string.IsNullOrEmpty(workspaceLoad.diagnostic)
                ? openResult.diagnostic
                : openResult.diagnostic + "；" + workspaceLoad.diagnostic;

            selection = M3GridBounds.Empty;
            clipboard = null;
            selectedPieceDefinitionId = null;
            selectedPieceInstanceId = null;
            selectedMapObjectId = null;
            hostPreviewMode = false;
            audienceProjectionState = null;
            strokeActive = false;
            fogStrokeActive = false;
            annotationDragActive = false;
            annotationDragId = null;
            selectionActive = false;
            pendingStroke.Clear();
            pendingStrokeKeys.Clear();
            pendingFogStroke.Clear();
            pendingFogStrokeKeys.Clear();
            lastHierarchyRevision = -1;
            lastMapListRevision = -1;
            lastPieceListRevision = -1;

            if (refreshViews)
            {
                EnsureCamera();
                EnsureSelectedContentDefaults();
                projection.Bind(editor, layerEditState, mapVisualCatalog);
                pieceProjection.Bind(commandBus, pieceAssetCatalog);
                pieceInteraction?.Bind(this, pieceLibrary, pieceProjection);
                consoleProjection.Bind(commandBus);
                RefreshAudienceProjection();
                pieceLibraryGrid?.Bind(nextSession);
                projectTitleLabel.text = nextSession.ProjectDisplayName;
                leftTabController?.Select(currentWorkspace, false);
                RefreshUiState();
            }

            previousSession?.Dispose();
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
            var workbenchStyles = Resources.Load<StyleSheet>("SundollWorld/WorkbenchStyles");
            if (workbenchStyles != null)
            {
                root.styleSheets.Add(workbenchStyles);
            }
            root.AddToClassList("sw-root");
            root.style.flexGrow = 1f;
            root.style.flexDirection = FlexDirection.Column;
            root.style.backgroundColor = new Color(0f, 0f, 0f, 0f);

            var topBar = CreatePanel(new Color(0.055f, 0.07f, 0.095f, 0.96f), 48f);
            topBar.AddToClassList("sw-topbar");
            topBar.style.width = Length.Percent(100f);
            topBar.style.height = 48f;
            topBar.style.flexDirection = FlexDirection.Row;
            topBar.style.alignItems = Align.Center;
            projectTitleLabel = new Label(workbenchSession.ProjectDisplayName) { name = "ProjectTitle" };
            projectTitleLabel.AddToClassList("sw-brand");
            topBar.Add(projectTitleLabel);
            var projectCenterButton = new Button(() => projectCenterPanel.Show(workbenchSession.ProjectDisplayName))
            {
                text = "项目中心"
            };
            projectCenterButton.name = "ProjectCenterButton";
            projectCenterButton.AddToClassList("sw-button-quiet");
            topBar.Add(projectCenterButton);
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
            var consoleButton = new Button(CreateHostMapFromUi) { text = "新建主持地图" };
            consoleButton.name = "CreateHostMap";
            consoleButton.style.marginLeft = 10f;
            topBar.Add(consoleButton);
            var createBoardButton = new Button(() => EnsureCurrentMapHostBoard()) { text = "发布并创建棋盘" };
            createBoardButton.name = "CreateHostBoard";
            createBoardButton.style.marginLeft = 8f;
            topBar.Add(createBoardButton);
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
            projectCenterPanel = new M7ProjectCenterPanel(
                workspaceService,
                result => AdoptSession(result, true),
                () => saveSession);
            root.Add(projectCenterPanel.Element);
            RefreshUiState();
        }

        private VisualElement BuildToolPanel()
        {
            var panel = CreatePanel(new Color(0.08f, 0.095f, 0.125f, 0.98f), 260f);
            panel.name = "WorkbenchLeftPanel";
            panel.AddToClassList("sw-side-panel");
            panel.style.paddingLeft = 12f;
            panel.style.paddingRight = 12f;
            leftTabController = new M7WorkbenchTabController();
            leftTabController.TabChanged += SelectWorkspaceTab;
            panel.Add(leftTabController.TabBar);

            var sectionHost = new VisualElement { name = "WorkbenchSectionHost" };
            sectionHost.style.flexGrow = 1f;
            sectionHost.style.minHeight = 0f;
            panel.Add(sectionHost);

            var mapSection = CreateWorkspaceSection("ToolPanelScroll");
            mapSection.Add(new Label("地图工具") { name = "ToolTitle" });
            foreach (var tool in new[] { "选择", "画笔", "橡皮擦", "直线", "矩形", "填充" })
            {
                var toolButton = new Button(() => SelectTool(tool)) { text = tool };
                toolButton.style.marginTop = 5f;
                mapSection.Add(toolButton);
            }

            var layerTitle = new Label("内容层") { name = "LayerTitle" };
            layerTitle.style.marginTop = 18f;
            mapSection.Add(layerTitle);
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
                mapSection.Add(row);
            }

            var materialTitle = new Label("地图素材") { name = "MapVisualPaletteTitle" };
            materialTitle.style.marginTop = 14f;
            mapSection.Add(materialTitle);
            materialPaletteContainer = new VisualElement { name = "MapVisualPalette" };
            materialPaletteContainer.AddToClassList("sw-material-palette");
            mapSection.Add(materialPaletteContainer);
            RefreshMaterialPalette();

            var resetButton = new Button(ResetView) { text = "复位视口" };
            resetButton.style.marginTop = 18f;
            mapSection.Add(resetButton);

            var pieceSection = CreateWorkspaceSection("PieceLibraryScroll");
            var pieceTitle = new Label("棋子库") { name = "PieceLibraryTitle" };
            pieceSection.Add(pieceTitle);
            var starterContentButton = new Button(InstallStarterContent) { text = "安装 / 修复内置中性棋子" };
            starterContentButton.name = "InstallStarterContent";
            starterContentButton.tooltip = "安装 12 个项目原创中性棋子；重复执行只补缺失内容";
            starterContentButton.style.marginTop = 5f;
            pieceSection.Add(starterContentButton);
            var starterContentNote = new Label("内置素材为项目原创程序化内容，无外部归属要求。");
            starterContentNote.AddToClassList("sw-muted");
            pieceSection.Add(starterContentNote);
            pieceSearchField = new TextField { name = "PieceSearch", tooltip = "按名称、分类或标签搜索" };
            pieceSearchField.style.marginTop = 5f;
            pieceSearchField.RegisterValueChangedCallback(_ => RefreshPieceLibraryList());
            pieceSection.Add(pieceSearchField);
            pieceCategoryField = new TextField("分类") { name = "PieceCategory" };
            pieceCategoryField.style.marginTop = 4f;
            pieceSection.Add(pieceCategoryField);
            pieceTagsField = new TextField("标签") { name = "PieceTags" };
            pieceTagsField.style.marginTop = 4f;
            pieceSection.Add(pieceTagsField);
            var updateDefinitionButton = new Button(SaveSelectedPieceDefinition) { text = "保存定义分类/标签" };
            updateDefinitionButton.style.marginTop = 4f;
            pieceSection.Add(updateDefinitionButton);
            var createPieceButton = new Button(CreatePlaceholderPiece) { text = "新增占位定义" };
            createPieceButton.style.marginTop = 5f;
            pieceSection.Add(createPieceButton);
            var createInstanceButton = new Button(CreateInstanceFromSelectedDefinition) { text = "创建实例并放置" };
            createInstanceButton.style.marginTop = 5f;
            pieceSection.Add(createInstanceButton);
            pieceImagePathField = new TextField("图片路径") { name = "PieceImagePath" };
            pieceImagePathField.style.marginTop = 6f;
            pieceSection.Add(pieceImagePathField);
            var pickImageButton = new Button(PickPieceImageFile) { text = "选择图片文件" };
            pickImageButton.name = "PickPieceImageFile";
            pickImageButton.style.marginTop = 4f;
            pieceSection.Add(pickImageButton);
            var importImageButton = new Button(ImportPieceImageFromPath) { text = "导入并重新绑定当前定义" };
            importImageButton.name = "RebindPieceImage";
            importImageButton.style.marginTop = 4f;
            pieceSection.Add(importImageButton);
            pieceLibraryLabel = new Label { name = "PieceLibraryBody" };
            pieceLibraryLabel.style.marginTop = 8f;
            pieceLibraryLabel.style.whiteSpace = WhiteSpace.Normal;
            pieceSection.Add(pieceLibraryLabel);
            pieceLibraryGrid = new M7PieceLibraryGridController(SelectPieceDefinition);
            pieceLibraryGrid.Bind(workbenchSession);
            pieceListContainer = pieceLibraryGrid.Element;
            pieceListContainer.style.marginTop = 6f;
            pieceListContainer.style.minHeight = 220f;
            pieceListContainer.style.flexGrow = 1f;
            pieceSection.Add(pieceListContainer);
            var instanceHeader = new Label("棋盘实例") { name = "PieceInstanceHeader" };
            instanceHeader.style.marginTop = 8f;
            pieceSection.Add(instanceHeader);
            pieceInstanceListContainer = new ScrollView(ScrollViewMode.Vertical) { name = "PieceInstanceList" };
            pieceInstanceListContainer.style.minHeight = 90f;
            pieceInstanceListContainer.style.maxHeight = 160f;
            pieceSection.Add(pieceInstanceListContainer);

            var hostSection = CreateWorkspaceSection("HostToolsScroll");
            hostSection.Add(new Label("主持工具") { name = "HostToolsTitle" });
            mapIdField = new TextField("地图 ID") { name = "HostMapId" };
            mapIdField.style.marginTop = 8f;
            hostSection.Add(mapIdField);
            mapNameField = new TextField("地图名称") { name = "HostMapName" };
            mapNameField.style.marginTop = 4f;
            hostSection.Add(mapNameField);
            var switchMapButton = new Button(SwitchHostMapFromUi) { text = "切换主持地图" };
            switchMapButton.name = "SwitchHostMap";
            switchMapButton.style.marginTop = 4f;
            hostSection.Add(switchMapButton);
            var renameMapButton = new Button(RenameHostMap) { text = "重命名当前地图" };
            renameMapButton.name = "RenameHostMap";
            renameMapButton.style.marginTop = 4f;
            hostSection.Add(renameMapButton);
            consoleLabel = new Label { name = "HostConsoleBody" };
            consoleLabel.style.marginTop = 6f;
            consoleLabel.style.whiteSpace = WhiteSpace.Normal;
            hostSection.Add(consoleLabel);

            var hierarchySection = CreateWorkspaceSection("HierarchyScroll");
            hierarchySection.Add(new Label("层级与地图") { name = "HierarchyTitle" });
            mapListContainer = new ScrollView(ScrollViewMode.Vertical) { name = "HostMapList" };
            mapListContainer.style.marginTop = 5f;
            mapListContainer.style.maxHeight = 160f;
            hierarchySection.Add(mapListContainer);
            hierarchyContainer = new ScrollView(ScrollViewMode.Vertical) { name = "HostHierarchy" };
            hierarchyContainer.style.marginTop = 5f;
            hierarchyContainer.style.flexGrow = 1f;
            hierarchyContainer.style.minHeight = 220f;
            hierarchySection.Add(hierarchyContainer);

            var fogTitle = new Label("迷雾 / 标注 / 交互");
            fogTitle.style.marginTop = 10f;
            hostSection.Add(fogTitle);
            fogRadiusField = new TextField("迷雾笔刷半径") { name = "FogBrushRadius" };
            fogRadiusField.value = "1";
            fogRadiusField.style.marginTop = 4f;
            hostSection.Add(fogRadiusField);
            var revealFogBrushButton = new Button(() => SelectTool("迷雾揭示"))
            {
                name = "RevealFogBrush",
                text = "使用揭示迷雾笔刷"
            };
            revealFogBrushButton.style.marginTop = 4f;
            hostSection.Add(revealFogBrushButton);
            var hideFogBrushButton = new Button(() => SelectTool("迷雾隐藏"))
            {
                name = "HideFogBrush",
                text = "使用隐藏迷雾笔刷"
            };
            hideFogBrushButton.style.marginTop = 4f;
            hostSection.Add(hideFogBrushButton);
            var moveAnnotationButton = new Button(() => SelectTool("标注移动"))
            {
                name = "MoveAnnotationTool",
                text = "拖动动态标注"
            };
            moveAnnotationButton.style.marginTop = 4f;
            hostSection.Add(moveAnnotationButton);
            fogXField = new TextField("格子 X") { name = "FogX" };
            fogXField.style.marginTop = 4f;
            hostSection.Add(fogXField);
            fogYField = new TextField("格子 Y") { name = "FogY" };
            fogYField.style.marginTop = 4f;
            hostSection.Add(fogYField);
            var hideFogButton = new Button(() => SetFogFromUi(false)) { text = "隐藏格子" };
            hideFogButton.style.marginTop = 4f;
            hostSection.Add(hideFogButton);
            var revealFogButton = new Button(() => SetFogFromUi(true)) { text = "揭示格子" };
            revealFogButton.style.marginTop = 4f;
            hostSection.Add(revealFogButton);
            annotationIdField = new TextField("标注 ID") { name = "AnnotationId" };
            annotationIdField.style.marginTop = 5f;
            hostSection.Add(annotationIdField);
            annotationTextField = new TextField("标注文本") { name = "AnnotationText" };
            annotationTextField.style.marginTop = 4f;
            hostSection.Add(annotationTextField);
            var upsertAnnotationButton = new Button(UpsertAnnotationFromUi) { text = "保存动态标注" };
            upsertAnnotationButton.style.marginTop = 4f;
            hostSection.Add(upsertAnnotationButton);
            var removeAnnotationButton = new Button(RemoveAnnotationFromUi) { text = "删除动态标注" };
            removeAnnotationButton.style.marginTop = 4f;
            hostSection.Add(removeAnnotationButton);
            interactionObjectField = new TextField("对象 ID") { name = "InteractionObjectId" };
            interactionObjectField.style.marginTop = 5f;
            hostSection.Add(interactionObjectField);
            var openInteractionButton = new Button(() => SetInteractionFromUi(true)) { text = "打开对象" };
            openInteractionButton.style.marginTop = 4f;
            hostSection.Add(openInteractionButton);
            var closeInteractionButton = new Button(() => SetInteractionFromUi(false)) { text = "关闭对象" };
            closeInteractionButton.style.marginTop = 4f;
            hostSection.Add(closeInteractionButton);

            leftTabController.Add("map", "地图", mapSection);
            leftTabController.Add("pieces", "棋子", pieceSection);
            leftTabController.Add("hierarchy", "层级", hierarchySection);
            leftTabController.Add("host", "主持", hostSection);
            sectionHost.Add(mapSection);
            sectionHost.Add(pieceSection);
            sectionHost.Add(hierarchySection);
            sectionHost.Add(hostSection);
            leftTabController.Select(currentWorkspace, false);
            return panel;
        }

        private static ScrollView CreateWorkspaceSection(string name)
        {
            var section = new ScrollView(ScrollViewMode.Vertical) { name = name };
            section.style.flexGrow = 1f;
            section.style.minHeight = 0f;
            section.AddToClassList("sw-workspace-section");
            return section;
        }

        private void SelectWorkspaceTab(string tabId)
        {
            currentWorkspace = NormalizeWorkspaceId(tabId);
            PersistWorkspaceState();
            status = "工作区：" + WorkspaceLabel(currentWorkspace);
            RefreshUiState();
        }

        private static string NormalizeWorkspaceId(string tabId)
        {
            switch (tabId)
            {
                case "pieces":
                case "hierarchy":
                case "host":
                    return tabId;
                default:
                    return "map";
            }
        }

        private static string WorkspaceLabel(string tabId)
        {
            switch (tabId)
            {
                case "pieces": return "棋子库";
                case "hierarchy": return "层级";
                case "host": return "主持台";
                default: return "地图制作";
            }
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
            var deletePieceButton = new Button(() => DeleteSelectedPieces()) { text = "删除选中棋子" };
            deletePieceButton.name = "DeleteSelectedPieces";
            deletePieceButton.style.marginTop = 5f;
            panel.Add(deletePieceButton);
            var detachButton = new Button(DetachSelectedPiece) { text = "解除选中棋子关系" };
            detachButton.style.marginTop = 5f;
            panel.Add(detachButton);
            var lowerStackButton = new Button(() => MoveSelectedPieceStack(-1)) { text = "堆叠上移" };
            lowerStackButton.style.marginTop = 5f;
            panel.Add(lowerStackButton);
            var raiseStackButton = new Button(() => MoveSelectedPieceStack(1)) { text = "堆叠下移" };
            raiseStackButton.style.marginTop = 5f;
            panel.Add(raiseStackButton);
            pieceRelationTargetField = new DropdownField("关系目标实例", new List<string> { string.Empty }, 0)
            {
                name = "PieceRelationTarget"
            };
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

        private void CreateHostMapFromUi()
        {
            var id = mapIdField == null ? string.Empty : mapIdField.value;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = "map-host-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }

            var name = mapNameField == null ? id : mapNameField.value;
            CreateHostMap(id.Trim(), string.IsNullOrWhiteSpace(name) ? id : name.Trim());
        }

        /// <summary>
        /// Creates an inactive host-map slot. The caller explicitly chooses
        /// when to enter it through <see cref="TrySwitchHostMap"/>.
        /// </summary>
        public bool CreateHostMap(string mapId, string displayName, int width = 64, int height = 64)
        {
            if (consoleFacade == null || string.IsNullOrWhiteSpace(mapId))
            {
                status = "地图 ID 不能为空";
                RefreshUiState();
                return false;
            }

            var receipt = consoleFacade.CreateMap(mapId.Trim(), displayName, width, height);
            CommitConsoleReceipt(receipt);
            status = receipt.accepted ? "已创建主持地图：" + mapId : receipt.message;
            RefreshUiState();
            return receipt.accepted;
        }

        /// <summary>
        /// A new project deliberately starts with an editable draft, not an
        /// implicit board. This explicit UI action publishes its current map
        /// and then creates the first scenario/board that M4 pieces require.
        /// Each accepted command is persisted through the normal save queue.
        /// </summary>
        public bool EnsureCurrentMapHostBoard()
        {
            if (hostPreviewMode)
            {
                status = "玩家预览为只读";
                RefreshUiState();
                return false;
            }

            if (editor == null || editor.State == null || editor.State.map == null)
            {
                status = "当前没有可发布的地图";
                RefreshUiState();
                return false;
            }

            if (editor.State.board != null && !string.IsNullOrWhiteSpace(editor.State.board.id))
            {
                status = "当前地图已有主持棋盘";
                RefreshUiState();
                return true;
            }

            // A board without a stable ID cannot be referenced by M4 piece
            // locations, therefore it is not a usable host board. Recreate
            // the scenario/board pair through the normal command path instead
            // of letting later placement fail with an opaque location error.
            var isRepairingIncompleteBoard = editor.State.board != null;

            if (editor.State.publishedMap == null)
            {
                var publishReceipt = editor.PublishMapContent();
                CommitReceipt(publishReceipt);
                if (!publishReceipt.accepted)
                {
                    status = publishReceipt.message;
                    RefreshUiState();
                    return false;
                }
            }

            var suffix = Guid.NewGuid().ToString("N");
            var scenarioReceipt = editor.CreateScenarioBoard(
                "scenario-host-" + suffix,
                "board-host-" + suffix);
            CommitReceipt(scenarioReceipt);
            status = scenarioReceipt.accepted
                ? isRepairingIncompleteBoard
                    ? "已修复不完整的主持棋盘"
                    : "当前地图已发布并创建主持棋盘"
                : scenarioReceipt.message;
            RefreshUiState();
            return scenarioReceipt.accepted;
        }

        private void SwitchHostMapFromUi()
        {
            TrySwitchHostMap(mapIdField == null ? null : mapIdField.value);
        }

        /// <summary>
        /// Moves to another host-map slot without discarding the current map's
        /// draft or workspace. The snapshot is queued before the command; the
        /// accepted switch is then journaled normally, so a close during the
        /// background write can still recover the complete transition.
        /// </summary>
        public bool TrySwitchHostMap(string mapId)
        {
            if (consoleFacade == null || saveSession == null || commandBus == null || string.IsNullOrWhiteSpace(mapId))
            {
                status = "请输入要切换的地图 ID";
                RefreshUiState();
                return false;
            }

            mapId = mapId.Trim();
            var console = M5ConsoleQueries.Ensure(commandBus.State);
            if (console.FindMap(mapId) == null)
            {
                status = "主持地图不存在：" + mapId;
                RefreshUiState();
                return false;
            }

            PersistWorkspaceState();
            try
            {
                saveSession.QueueSave("切换主持地图前保存当前草稿");
            }
            catch (Exception exception)
            {
                status = "当前草稿无法加入保存队列，已取消切图：" + exception.Message;
                RefreshUiState();
                return false;
            }

            var receipt = consoleFacade.SwitchMap(mapId);
            CommitConsoleReceipt(receipt);
            if (!receipt.accepted)
            {
                status = receipt.message;
                RefreshUiState();
                return false;
            }

            RestoreWorkspaceForActiveMap();
            status = "已切换主持地图：" + mapId;
            RefreshUiState();
            return true;
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

        public bool HostPreviewMode => hostPreviewMode;

        public void ToggleHostPreviewMode()
        {
            hostPreviewMode = !hostPreviewMode;
            RefreshAudienceProjection();
            status = hostPreviewMode ? "已进入玩家预览：隐藏棋子与迷雾内容已按 Audience Projection 过滤" : "已回到地图编辑模式";
            RefreshUiState();
        }

        private void RefreshAudienceProjection()
        {
            if (commandBus == null)
            {
                return;
            }

            audienceProjectionState = null;
            if (hostPreviewMode)
            {
                var snapshot = M6ProjectionBuilder.CreateSnapshot(
                    commandBus.State,
                    "workbench-player-preview",
                    new M6AudiencePolicy
                    {
                        revealAllFog = false,
                        includeHiddenPieces = false
                    });
                audienceProjectionState = JsonUtility.FromJson<M1WorldState>(snapshot.stateJson);
            }

            projection?.SetAudienceProjection(audienceProjectionState);
            pieceProjection?.SetAudienceProjection(audienceProjectionState);
            consoleProjection?.SetAudiencePreview(hostPreviewMode);
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
            if (hostPreviewMode)
            {
                status = "玩家预览为只读，不能打开编辑菜单";
                RefreshUiState();
                return;
            }

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
            if (hostPreviewMode)
            {
                status = "玩家预览为只读";
                RefreshUiState();
                return;
            }

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
                if (hostPreviewMode)
                {
                    RefreshAudienceProjection();
                }
                else
                {
                    projection.RefreshAll();
                    consoleProjection?.RefreshAll();
                    pieceProjection?.RefreshAll();
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
            RefreshMaterialPalette();
            RefreshUiState();
        }

        private void SelectMapVisual(string contentId)
        {
            if (mapVisualCatalog == null || !mapVisualCatalog.TryGet(contentId, out var definition) ||
                !string.Equals(definition.layerId, currentLayerId, StringComparison.Ordinal))
            {
                status = "素材与当前图层不匹配。";
                return;
            }

            selectedContentIds[currentLayerId] = contentId;
            PersistWorkspaceState();
            status = "当前素材：" + definition.displayName;
            RefreshMaterialPalette();
            RefreshUiState();
        }

        private void RefreshMaterialPalette()
        {
            if (materialPaletteContainer == null || mapVisualCatalog == null)
            {
                return;
            }

            materialPaletteContainer.Clear();
            var currentContent = ContentForLayer(currentLayerId);
            foreach (var definition in mapVisualCatalog.GetForLayer(currentLayerId))
            {
                var contentId = definition.contentId;
                var button = new Button(() => SelectMapVisual(contentId))
                {
                    text = (string.Equals(currentContent, contentId, StringComparison.Ordinal) ? "◆ " : string.Empty) +
                           definition.displayName,
                    tooltip = contentId
                };
                button.name = "MapVisual_" + contentId;
                button.AddToClassList("sw-material-button");
                button.style.backgroundColor = definition.primaryColor;
                var luminance = definition.primaryColor.r * 0.299f +
                                definition.primaryColor.g * 0.587f +
                                definition.primaryColor.b * 0.114f;
                button.style.color = luminance > 0.54f
                    ? new Color(0.08f, 0.09f, 0.1f, 1f)
                    : new Color(0.95f, 0.94f, 0.9f, 1f);
                materialPaletteContainer.Add(button);
            }
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
            if (hostPreviewMode)
            {
                status = "玩家预览为只读";
                RefreshUiState();
                return;
            }

            if (IsFogBrushTool(currentTool))
            {
                BeginFogBrush(cell);
                return;
            }

            if (currentTool == "标注移动")
            {
                BeginAnnotationDrag(cell);
                return;
            }

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
            if (fogStrokeActive)
            {
                foreach (var point in M3GridStrokeRasterizer.Rasterize(lastStrokeCell.x, lastStrokeCell.y, cell.x, cell.y))
                {
                    AddFogBrushCells(new Vector2Int(point.x, point.y));
                }

                lastStrokeCell = cell;
                status = "迷雾笔刷预览：" + pendingFogStroke.Count + " 格";
                return;
            }

            if (annotationDragActive)
            {
                lastAnnotationDragCell = cell;
                selection = new M3GridBounds(cell.x, cell.y, cell.x, cell.y);
                status = "标注移动预览：" + cell.x + "," + cell.y;
                return;
            }

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
            if (fogStrokeActive)
            {
                foreach (var point in M3GridStrokeRasterizer.Rasterize(lastStrokeCell.x, lastStrokeCell.y, cell.x, cell.y))
                {
                    AddFogBrushCells(new Vector2Int(point.x, point.y));
                }

                lastStrokeCell = cell;
                fogStrokeActive = false;
                CommitPendingFogStroke();
                return;
            }

            if (annotationDragActive)
            {
                lastAnnotationDragCell = cell;
                annotationDragActive = false;
                CommitAnnotationDrag();
                return;
            }

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
            fogStrokeActive = false;
            annotationDragActive = false;
            annotationDragId = null;
            selectionActive = false;
            pendingStroke.Clear();
            pendingStrokeKeys.Clear();
            pendingFogStroke.Clear();
            pendingFogStrokeKeys.Clear();
            status = "已取消当前操作";
            RefreshUiState();
        }

        private static bool IsFogBrushTool(string tool)
        {
            return tool == "迷雾揭示" || tool == "迷雾隐藏";
        }

        private void BeginFogBrush(Vector2Int cell)
        {
            pendingFogStroke.Clear();
            pendingFogStrokeKeys.Clear();
            fogStrokeActive = true;
            lastStrokeCell = cell;
            AddFogBrushCells(cell);
            status = "迷雾笔刷预览：" + pendingFogStroke.Count + " 格";
            RefreshUiState();
        }

        private void AddFogBrushCells(Vector2Int center)
        {
            if (editor == null || editor.State == null || editor.State.map == null)
            {
                return;
            }

            var radius = ReadFogBrushRadius();
            var revealed = currentTool == "迷雾揭示";
            for (var x = center.x - radius; x <= center.x + radius; x++)
            {
                for (var y = center.y - radius; y <= center.y + radius; y++)
                {
                    var distanceX = x - center.x;
                    var distanceY = y - center.y;
                    if (distanceX * distanceX + distanceY * distanceY > radius * radius ||
                        x < 0 || y < 0 || x >= editor.State.map.width || y >= editor.State.map.height)
                    {
                        continue;
                    }

                    var key = x + ":" + y;
                    if (pendingFogStrokeKeys.Add(key))
                    {
                        pendingFogStroke.Add(new M5FogCellMutation(x, y, revealed));
                    }
                }
            }
        }

        private int ReadFogBrushRadius()
        {
            if (!int.TryParse(fogRadiusField == null ? string.Empty : fogRadiusField.value, out var radius))
            {
                return 1;
            }

            return Mathf.Clamp(radius, 0, 32);
        }

        private void CommitPendingFogStroke()
        {
            if (pendingFogStroke.Count == 0)
            {
                status = "没有可提交的迷雾格";
                RefreshUiState();
                return;
            }

            var console = M5ConsoleQueries.Ensure(commandBus.State);
            var count = pendingFogStroke.Count;
            var receipt = consoleFacade.SetFogBatch(console.activeMapId, pendingFogStroke);
            CommitConsoleReceipt(receipt);
            status = receipt.accepted
                ? (currentTool == "迷雾揭示" ? "已揭示" : "已隐藏") + "迷雾笔刷覆盖的 " + count + " 格"
                : receipt.message;
            pendingFogStroke.Clear();
            pendingFogStrokeKeys.Clear();
            RefreshUiState();
        }

        private void BeginAnnotationDrag(Vector2Int cell)
        {
            var console = M5ConsoleQueries.Ensure(commandBus.State);
            var annotation = FindAnnotationAt(console.activeMapId, cell);
            if (annotation == null)
            {
                status = "该格没有可拖动的动态标注";
                RefreshUiState();
                return;
            }

            annotationDragActive = true;
            annotationDragId = annotation.id;
            lastAnnotationDragCell = cell;
            selection = new M3GridBounds(cell.x, cell.y, cell.x, cell.y);
            if (annotationIdField != null)
            {
                annotationIdField.SetValueWithoutNotify(annotation.id);
            }

            if (annotationTextField != null)
            {
                annotationTextField.SetValueWithoutNotify(annotation.text ?? string.Empty);
            }

            status = "开始拖动动态标注：" + annotation.id;
            RefreshUiState();
        }

        private void CommitAnnotationDrag()
        {
            if (string.IsNullOrWhiteSpace(annotationDragId))
            {
                return;
            }

            var console = M5ConsoleQueries.Ensure(commandBus.State);
            var annotation = console.FindAnnotation(annotationDragId);
            if (annotation == null)
            {
                status = "动态标注已不存在：" + annotationDragId;
                annotationDragId = null;
                RefreshUiState();
                return;
            }

            var target = lastAnnotationDragCell;
            if (annotation.x == target.x && annotation.y == target.y)
            {
                status = "动态标注位置未改变";
                annotationDragId = null;
                RefreshUiState();
                return;
            }

            var receipt = consoleFacade.UpsertAnnotation(
                annotation.id,
                annotation.mapId,
                target.x,
                target.y,
                annotation.text,
                annotation.colorHex,
                annotation.visible);
            CommitConsoleReceipt(receipt);
            status = receipt.accepted
                ? "动态标注已移动到 " + target.x + "," + target.y
                : receipt.message;
            annotationDragId = null;
            RefreshUiState();
        }

        private M5DynamicAnnotation FindAnnotationAt(string mapId, Vector2Int cell)
        {
            var console = commandBus == null ? null : M5ConsoleQueries.Ensure(commandBus.State);
            if (console == null || console.annotations == null)
            {
                return null;
            }

            for (var index = console.annotations.Count - 1; index >= 0; index--)
            {
                var annotation = console.annotations[index];
                if (annotation != null && annotation.mapId == mapId && annotation.visible &&
                    annotation.x == cell.x && annotation.y == cell.y)
                {
                    return annotation;
                }
            }

            return null;
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

        public bool TrySelectPieceAtScreen(Vector2 screenPosition)
        {
            if (hostPreviewMode || commandBus == null || editor == null || editor.State == null ||
                editor.State.map == null || !TryScreenToCell(screenPosition, out var cell))
            {
                return false;
            }

            var instance = M4PieceQueries.FindTopmostBoardInstanceAt(
                editor.State,
                editor.State.board == null ? null : editor.State.board.id,
                cell.x,
                cell.y);
            if (instance == null)
            {
                return false;
            }

            SelectPieceInstance(instance.id);
            return true;
        }

        public bool BeginPiecePointerAction(Vector2 screenPosition, bool additiveSelection)
        {
            return pieceInteraction != null && pieceInteraction.BeginPointerAction(screenPosition, additiveSelection);
        }

        public void ContinuePiecePointerAction(Vector2 screenPosition)
        {
            pieceInteraction?.ContinuePointerAction(screenPosition);
        }

        public void EndPiecePointerAction(Vector2 screenPosition)
        {
            pieceInteraction?.EndPointerAction(screenPosition);
        }

        public void CancelPiecePointerAction()
        {
            pieceInteraction?.CancelPointerAction();
        }

        public bool RotateSelectedPieces()
        {
            return pieceInteraction != null && pieceInteraction.RotateSelectionClockwise();
        }

        public bool FlipSelectedPieces()
        {
            return pieceInteraction != null && pieceInteraction.FlipSelection();
        }

        public bool ToggleSelectedPiecesVisibility()
        {
            return pieceInteraction != null && pieceInteraction.ToggleSelectionVisibility();
        }

        public bool DeleteSelectedPieces()
        {
            return pieceInteraction != null && pieceInteraction.DeleteSelection();
        }

        public void CopySelection()
        {
            clipboard = editor.CopySelection(selection, layerEditState);
            status = clipboard.IsEmpty ? "选区为空" : "已复制 " + clipboard.cells.Count + " 个内容";
            RefreshUiState();
        }

        public void CutSelection()
        {
            if (hostPreviewMode)
            {
                status = "玩家预览为只读";
                RefreshUiState();
                return;
            }

            var receipt = editor.CutSelection(selection, layerEditState, out var cutClipboard);
            if (cutClipboard != null && !cutClipboard.IsEmpty)
            {
                clipboard = cutClipboard;
            }

            CommitReceipt(receipt);
        }

        public void PasteAt(Vector2Int anchor)
        {
            if (hostPreviewMode)
            {
                status = "玩家预览为只读";
                RefreshUiState();
                return;
            }

            CommitReceipt(editor.PasteClipboard(clipboard, anchor.x, anchor.y, layerEditState));
        }

        public void RotateClipboard()
        {
            if (hostPreviewMode)
            {
                status = "玩家预览为只读";
                RefreshUiState();
                return;
            }

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
            if (hostPreviewMode)
            {
                status = "玩家预览为只读";
                RefreshUiState();
                return;
            }

            if (editor.Undo())
            {
                if (hostPreviewMode)
                {
                    RefreshAudienceProjection();
                }
                else
                {
                    projection.RefreshRegion(editor.LastDirtyBounds);
                }
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
            if (hostPreviewMode)
            {
                status = "玩家预览为只读";
                RefreshUiState();
                return;
            }

            if (editor.Redo())
            {
                if (hostPreviewMode)
                {
                    RefreshAudienceProjection();
                }
                else
                {
                    projection.RefreshRegion(editor.LastDirtyBounds);
                }
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
            if (hostPreviewMode)
            {
                status = "玩家预览为只读";
                RefreshUiState();
                return;
            }

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
            if (hostPreviewMode)
            {
                status = "玩家预览为只读";
                RefreshUiState();
                return;
            }

            var mapObject = FindObjectAt(cell);
            if (mapObject != null)
            {
                CommitReceipt(editor.OpenMapObject(mapObject.id));
            }
        }

        public void CloseObjectAt(Vector2Int cell)
        {
            if (hostPreviewMode)
            {
                status = "玩家预览为只读";
                RefreshUiState();
                return;
            }

            var mapObject = FindObjectAt(cell);
            if (mapObject != null)
            {
                CommitReceipt(editor.CloseMapObject(mapObject.id));
            }
        }

        public void RotateObjectAt(Vector2Int cell)
        {
            if (hostPreviewMode)
            {
                status = "玩家预览为只读";
                RefreshUiState();
                return;
            }

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
                if (hostPreviewMode)
                {
                    RefreshAudienceProjection();
                }
                else
                {
                    projection.RefreshRegion(editor.LastDirtyBounds);
                }
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

        private void InstallStarterContent()
        {
            if (workbenchSession == null || starterContentManifest == null)
            {
                status = "当前项目尚未准备好安装内置素材";
                RefreshUiState();
                return;
            }

            var result = StarterContentInstaller.InstallMissing(workbenchSession, starterContentManifest);
            if (string.IsNullOrWhiteSpace(selectedPieceDefinitionId) && starterContentManifest.PieceDefinitions.Count > 0)
            {
                selectedPieceDefinitionId = starterContentManifest.PieceDefinitions[0].DefinitionId;
                SyncSelectedPieceDefinitionFields();
            }

            pieceProjection?.RefreshAll();
            lastPieceListRevision = -1;
            status = result.Accepted
                ? "内置棋子已检查：新增定义 " + result.InstalledDefinitions +
                  "，修复 " + result.RepairedDefinitions +
                  "，已有 " + result.SkippedDefinitions
                : "内置棋子部分安装失败：" + string.Join("；", result.Diagnostics);
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
                var duplicateAsset = existing != null;
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
                    status = update.accepted
                        ? duplicateAsset
                            ? "检测到重复图片，已复用现有内容并重新绑定当前定义"
                            : "图片已导入并重新绑定到当前定义"
                        : update.message;
                }
                else
                {
                    status = duplicateAsset
                        ? "检测到重复图片，已复用现有内容；请选择定义后再绑定"
                        : "图片已导入；请选择定义后再绑定";
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
            if (pieceInteraction != null)
            {
                pieceInteraction.SelectOnly(instanceId);
                return;
            }

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

            if (pieceLibrary.State.board == null)
            {
                status = "请先点击顶部“发布并创建棋盘”，再放置棋子";
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
            if (RotateSelectedPieces())
            {
                return;
            }

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
            if (FlipSelectedPieces())
            {
                return;
            }

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
            if (ToggleSelectedPiecesVisibility())
            {
                return;
            }

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
                status = "请先选择棋子和关系目标";
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
                status = "请先选择棋子和附着目标";
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
            if (pieceLibraryGrid == null || pieceLibrary == null)
            {
                return;
            }

            var search = pieceSearchField == null ? string.Empty : pieceSearchField.value ?? string.Empty;
            pieceLibraryGrid.SetSearch(search);
            pieceLibraryGrid.SetSelectedDefinition(selectedPieceDefinitionId);
            pieceLibraryGrid.Refresh();
            if (pieceInstanceListContainer == null)
            {
                return;
            }

            pieceInstanceListContainer.Clear();
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
                    pieceInstanceListContainer.Add(instanceButton);
                }
            }
        }

        private void RefreshPieceRelationTargets()
        {
            if (pieceRelationTargetField == null || pieceLibrary == null)
            {
                return;
            }

            var currentTargetId = pieceRelationTargetField.value;
            var choices = new List<string> { string.Empty };
            var instances = pieceLibrary.State.pieceInstances;
            if (!string.IsNullOrWhiteSpace(selectedPieceInstanceId) && instances != null)
            {
                foreach (var instance in instances)
                {
                    if (instance != null && !string.IsNullOrWhiteSpace(instance.id) &&
                        !string.Equals(instance.id, selectedPieceInstanceId, StringComparison.Ordinal))
                    {
                        choices.Add(instance.id);
                    }
                }
            }

            pieceRelationTargetField.choices = choices;
            if (!choices.Contains(currentTargetId))
            {
                pieceRelationTargetField.SetValueWithoutNotify(string.Empty);
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

                if (hostPreviewMode)
                {
                    RefreshAudienceProjection();
                }
            }
            else
            {
                status = receipt.message;
            }
        }

        internal void CommitPieceInteractionReceipt(M1CommandReceipt receipt, string acceptedStatus)
        {
            CommitPieceReceipt(receipt);
            if (receipt != null && receipt.accepted && !string.IsNullOrWhiteSpace(acceptedStatus))
            {
                status = acceptedStatus;
            }

            RefreshUiState();
        }

        internal void ApplyPieceInteractionSelection(ICollection<string> selectedIds, string primaryId)
        {
            selectedPieceInstanceId = primaryId;
            var instance = M4PieceQueries.FindInstance(pieceLibrary == null ? null : pieceLibrary.State, primaryId);
            if (instance != null)
            {
                selectedPieceDefinitionId = instance.definitionId;
                SyncSelectedPieceDefinitionFields();
            }

            var count = selectedIds == null ? 0 : selectedIds.Count;
            status = count == 0
                ? "未选择棋子"
                : count == 1
                    ? "已选择棋子实例：" + (primaryId ?? "无")
                    : "已选择棋子（" + count + "个）";
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

        private string ContentForLayer(string layerId)
        {
            if (selectedContentIds.TryGetValue(layerId, out var contentId) && !string.IsNullOrWhiteSpace(contentId))
            {
                return contentId;
            }

            return mapVisualCatalog == null
                ? "terrain-ground"
                : mapVisualCatalog.GetDefaultContentId(layerId);
        }

        private void EnsureSelectedContentDefaults()
        {
            if (mapVisualCatalog == null)
            {
                return;
            }

            foreach (var layerId in LayerIds)
            {
                if (!selectedContentIds.TryGetValue(layerId, out var contentId) ||
                    !mapVisualCatalog.TryGet(contentId, out var definition) ||
                    !string.Equals(definition.layerId, layerId, StringComparison.Ordinal))
                {
                    selectedContentIds[layerId] = mapVisualCatalog.GetDefaultContentId(layerId);
                }
            }
        }

        private void RestoreWorkspaceForActiveMap()
        {
            if (workspaceStateStore == null || editor == null || editor.State == null || editor.State.map == null)
            {
                return;
            }

            var workspaceLoad = workspaceStateStore.Load(editor.State.map.id, LayerIds);
            layerEditState = workspaceLoad.state;
            currentTool = string.IsNullOrWhiteSpace(workspaceLoad.currentTool) ? "画笔" : workspaceLoad.currentTool;
            currentLayerId = string.IsNullOrWhiteSpace(workspaceLoad.currentLayerId)
                ? LayerIds[0]
                : workspaceLoad.currentLayerId;
            currentWorkspace = NormalizeWorkspaceId(workspaceLoad.currentWorkspace);
            selectedContentIds.Clear();
            if (workspaceLoad.selectedContentIds != null)
            {
                foreach (var pair in workspaceLoad.selectedContentIds)
                {
                    selectedContentIds[pair.Key] = pair.Value;
                }
            }

            hasLoadedWorkspaceView = workspaceLoad.loaded && workspaceLoad.hasViewport;
            loadedWorkspaceZoom = workspaceLoad.zoom;
            loadedWorkspacePan = new Vector2(workspaceLoad.panX, workspaceLoad.panY);
            selection = M3GridBounds.Empty;
            clipboard = null;
            selectedMapObjectId = null;
            selectedPieceDefinitionId = null;
            selectedPieceInstanceId = null;
            pieceInteraction?.ClearSelection();
            lastHierarchyRevision = -1;
            lastMapListRevision = -1;
            lastPieceListRevision = -1;

            EnsureCamera();
            EnsureSelectedContentDefaults();
            projection.Bind(editor, layerEditState, mapVisualCatalog);
            pieceProjection.Bind(commandBus, pieceAssetCatalog);
            pieceInteraction?.Bind(this, pieceLibrary, pieceProjection);
            consoleProjection.Bind(commandBus);
            RefreshAudienceProjection();
            leftTabController?.Select(currentWorkspace, false);

            if (mapIdField != null)
            {
                mapIdField.SetValueWithoutNotify(editor.State.map.id);
            }

            var activeMap = M5ConsoleQueries.Ensure(commandBus.State).FindMap(editor.State.map.id);
            if (mapNameField != null && activeMap != null)
            {
                mapNameField.SetValueWithoutNotify(activeMap.displayName ?? editor.State.map.id);
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
                camera == null ? 0f : camera.transform.position.y,
                currentWorkspace,
                selectedContentIds);
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
                                         "内置内容 " + (starterContentManifest == null ? 0 : starterContentManifest.Records.Count) +
                                         " 项 · 缺少图片时保留数据并显示占位色块。";
            }

            RefreshPieceRelationTargets();

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
                            TrySwitchHostMap(mapId);
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
