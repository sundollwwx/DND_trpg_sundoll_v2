using System;
using System.IO;
using Sundoll.Infrastructure;
using UnityEngine.UIElements;

namespace Sundoll.Presentation
{
    /// <summary>
    /// UI Toolkit project-centre view. It delegates filesystem and persistence
    /// work to ProjectWorkspaceService and only reports the selected session.
    /// </summary>
    public sealed class M7ProjectCenterPanel
    {
        private readonly ProjectWorkspaceService workspaceService;
        private readonly Action<ProjectWorkspaceOpenResult> activateProject;
        private readonly Func<M2SaveSession> activeSaveSession;
        private readonly VisualElement recentContainer;
        private readonly Label currentProjectLabel;
        private readonly Label statusLabel;
        private readonly TextField projectNameField;
        private readonly TextField projectPathField;
        private readonly TextField packagePathField;
        private readonly TextField exportPathField;

        public M7ProjectCenterPanel(
            ProjectWorkspaceService workspaceService,
            Action<ProjectWorkspaceOpenResult> activateProject,
            Func<M2SaveSession> activeSaveSession)
        {
            this.workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
            this.activateProject = activateProject ?? throw new ArgumentNullException(nameof(activateProject));
            this.activeSaveSession = activeSaveSession ?? throw new ArgumentNullException(nameof(activeSaveSession));

            Element = new VisualElement { name = "ProjectCenterOverlay" };
            Element.AddToClassList("sw-project-overlay");

            var card = new VisualElement { name = "ProjectCenterCard" };
            card.AddToClassList("sw-project-card");
            Element.Add(card);

            var header = new VisualElement();
            header.AddToClassList("sw-project-header");
            var title = new Label("项目中心");
            title.AddToClassList("sw-title");
            header.Add(title);
            var close = new Button(Hide) { text = "返回工作台" };
            close.AddToClassList("sw-button-quiet");
            header.Add(close);
            card.Add(header);

            currentProjectLabel = new Label("当前项目：尚未打开");
            currentProjectLabel.AddToClassList("sw-project-current");
            card.Add(currentProjectLabel);

            var columns = new VisualElement();
            columns.AddToClassList("sw-project-columns");
            card.Add(columns);

            var actions = new VisualElement();
            actions.AddToClassList("sw-project-column");
            columns.Add(actions);
            actions.Add(SectionTitle("新建项目"));
            projectNameField = new TextField("项目名称") { value = "我的 SundollWorld" };
            actions.Add(projectNameField);
            actions.Add(ActionButton("新建并打开", CreateProject, true));

            actions.Add(SectionTitle("打开项目目录"));
            projectPathField = new TextField("项目路径");
            actions.Add(projectPathField);
            actions.Add(ActionButton("打开路径", OpenProject));

            actions.Add(SectionTitle("便携包"));
            packagePathField = new TextField("导入 .sundollpkg 路径");
            actions.Add(packagePathField);
            actions.Add(ActionButton("导入为新项目", ImportProject));
            exportPathField = new TextField("导出路径");
            actions.Add(exportPathField);
            actions.Add(ActionButton("导出当前项目", ExportProject));

            var recentColumn = new VisualElement();
            recentColumn.AddToClassList("sw-project-column");
            columns.Add(recentColumn);
            recentColumn.Add(SectionTitle("最近项目"));
            recentContainer = new ScrollView(ScrollViewMode.Vertical) { name = "RecentProjects" };
            recentContainer.AddToClassList("sw-project-recent");
            recentColumn.Add(recentContainer);

            statusLabel = new Label("项目文件只保存在本机；Git 仓库不包含你的运行时存档。");
            statusLabel.AddToClassList("sw-project-status");
            card.Add(statusLabel);
            RefreshRecentProjects();
            Hide();
        }

        public VisualElement Element { get; }
        public bool IsVisible => Element.style.display != DisplayStyle.None;

        public void Show(string projectDisplayName)
        {
            currentProjectLabel.text = string.IsNullOrWhiteSpace(projectDisplayName)
                ? "当前项目：尚未打开"
                : "当前项目：" + projectDisplayName;
            RefreshRecentProjects();
            Element.style.display = DisplayStyle.Flex;
            projectNameField.Focus();
        }

        public void Hide()
        {
            Element.style.display = DisplayStyle.None;
        }

        public void RefreshRecentProjects()
        {
            recentContainer.Clear();
            var entries = workspaceService.GetRecentProjects();
            if (entries.Count == 0)
            {
                var empty = new Label("暂无最近项目。新建项目后会显示在这里。");
                empty.AddToClassList("sw-muted");
                recentContainer.Add(empty);
                return;
            }

            foreach (var entry in entries)
            {
                var captured = entry;
                var button = new Button(() => OpenRecent(captured));
                button.text = string.IsNullOrWhiteSpace(captured.displayName)
                    ? captured.projectRoot
                    : captured.displayName + "\n" + captured.projectRoot;
                button.tooltip = captured.projectRoot;
                button.AddToClassList("sw-recent-item");
                recentContainer.Add(button);
            }
        }

        private void CreateProject()
        {
            TryActivate(() => workspaceService.Create(projectNameField.value));
        }

        private void OpenProject()
        {
            TryActivate(() => workspaceService.Open(projectPathField.value));
        }

        private void OpenRecent(ProjectWorkspaceEntry entry)
        {
            TryActivate(() => workspaceService.Open(entry.projectRoot));
        }

        private void ImportProject()
        {
            TryActivate(() => workspaceService.Import(packagePathField.value, projectNameField.value));
        }

        private void ExportProject()
        {
            try
            {
                var session = activeSaveSession();
                if (session == null)
                {
                    throw new InvalidOperationException("当前没有可导出的项目。");
                }

                var output = exportPathField.value;
                if (string.IsNullOrWhiteSpace(output))
                {
                    output = Path.Combine(
                        workspaceService.WorkspaceRoot,
                        "SundollWorld-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".sundollpkg");
                    exportPathField.value = output;
                }

                workspaceService.Export(session, output);
                SetStatus("已导出：" + Path.GetFullPath(output), false);
            }
            catch (Exception exception)
            {
                SetStatus("导出失败：" + exception.Message, true);
            }
        }

        private void TryActivate(Func<ProjectWorkspaceOpenResult> operation)
        {
            ProjectWorkspaceOpenResult result = null;
            try
            {
                result = operation();
                activateProject(result);
                SetStatus(result.diagnostic, false);
                currentProjectLabel.text = "当前项目：" + result.saveSession.State.project.displayName;
                RefreshRecentProjects();
                Hide();
            }
            catch (Exception exception)
            {
                result?.saveSession?.Dispose();
                SetStatus("项目操作失败：" + exception.Message, true);
            }
        }

        private void SetStatus(string message, bool isError)
        {
            statusLabel.text = message;
            statusLabel.EnableInClassList("sw-error", isError);
        }

        private static Label SectionTitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("sw-section-title");
            return label;
        }

        private static Button ActionButton(string text, Action action, bool accent = false)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList(accent ? "sw-button-accent" : "sw-button");
            return button;
        }
    }
}
