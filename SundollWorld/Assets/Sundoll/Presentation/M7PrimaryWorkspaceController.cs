using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Owns the three top-level Workbench surfaces. It only changes local UI
    /// visibility; world state remains owned by the existing session/facades.
    /// </summary>
    public sealed class M7PrimaryWorkspaceController
    {
        private readonly Dictionary<string, VisualElement> workspaces =
            new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> buttons =
            new Dictionary<string, Button>(StringComparer.Ordinal);

        public M7PrimaryWorkspaceController()
        {
            Navigation = new VisualElement { name = "PrimaryWorkspaceNavigation" };
            Navigation.AddToClassList("sw-workspace-navigation");
        }

        public VisualElement Navigation { get; }
        public string CurrentWorkspaceId { get; private set; }
        public event Action<string> WorkspaceChanged;

        public void Add(string workspaceId, string label, VisualElement panel)
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));
            }

            if (panel == null)
            {
                throw new ArgumentNullException(nameof(panel));
            }

            if (workspaces.ContainsKey(workspaceId))
            {
                throw new InvalidOperationException("Duplicate Workbench workspace: " + workspaceId);
            }

            workspaces.Add(workspaceId, panel);
            var capturedId = workspaceId;
            var button = new Button(() => Select(capturedId)) { text = label };
            button.name = "PrimaryWorkspace_" + workspaceId;
            button.focusable = true;
            // Keep the three top-level destinations first and deterministic in
            // keyboard navigation, independent of later panel controls.
            button.tabIndex = buttons.Count + 1;
            button.AddToClassList("sw-workspace-button");
            buttons.Add(workspaceId, button);
            Navigation.Add(button);
            panel.style.display = DisplayStyle.None;
        }

        public bool Select(string workspaceId, bool notify = true)
        {
            if (!workspaces.ContainsKey(workspaceId))
            {
                return false;
            }

            CurrentWorkspaceId = workspaceId;
            foreach (var pair in workspaces)
            {
                pair.Value.style.display = string.Equals(pair.Key, workspaceId, StringComparison.Ordinal)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            foreach (var pair in buttons)
            {
                pair.Value.EnableInClassList(
                    "sw-workspace-button-selected",
                    string.Equals(pair.Key, workspaceId, StringComparison.Ordinal));
            }

            if (notify)
            {
                WorkspaceChanged?.Invoke(workspaceId);
            }

            return true;
        }

        public bool FocusCurrentWorkspace()
        {
            if (string.IsNullOrEmpty(CurrentWorkspaceId) ||
                !buttons.TryGetValue(CurrentWorkspaceId, out var button))
            {
                return false;
            }

            button.Focus();
            return true;
        }
    }
}
