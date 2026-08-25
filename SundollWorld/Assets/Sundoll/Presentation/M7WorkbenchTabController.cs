using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Owns only local panel visibility. It has no access to world state and is
    /// therefore safe to rebuild without affecting Undo, Journal, or saves.
    /// </summary>
    public sealed class M7WorkbenchTabController
    {
        private readonly Dictionary<string, VisualElement> panels =
            new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> buttons =
            new Dictionary<string, Button>(StringComparer.Ordinal);

        public M7WorkbenchTabController()
        {
            TabBar = new VisualElement { name = "WorkbenchTabBar" };
            TabBar.AddToClassList("sw-tabbar");
        }

        public VisualElement TabBar { get; }
        public string CurrentTabId { get; private set; }
        public event Action<string> TabChanged;

        public void Add(string tabId, string label, VisualElement panel)
        {
            if (string.IsNullOrWhiteSpace(tabId))
            {
                throw new ArgumentException("Tab ID is required.", nameof(tabId));
            }

            if (panel == null)
            {
                throw new ArgumentNullException(nameof(panel));
            }

            if (panels.ContainsKey(tabId))
            {
                throw new InvalidOperationException("Duplicate Workbench tab: " + tabId);
            }

            panels.Add(tabId, panel);
            var capturedId = tabId;
            var button = new Button(() => Select(capturedId)) { text = label };
            button.name = "WorkbenchTab_" + tabId;
            button.AddToClassList("sw-tab");
            buttons.Add(tabId, button);
            TabBar.Add(button);
            panel.style.display = DisplayStyle.None;
        }

        public bool Select(string tabId, bool notify = true)
        {
            if (!panels.ContainsKey(tabId))
            {
                return false;
            }

            CurrentTabId = tabId;
            foreach (var pair in panels)
            {
                pair.Value.style.display = string.Equals(pair.Key, tabId, StringComparison.Ordinal)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            foreach (var pair in buttons)
            {
                pair.Value.EnableInClassList("sw-tab-selected", string.Equals(pair.Key, tabId, StringComparison.Ordinal));
            }

            if (notify)
            {
                TabChanged?.Invoke(tabId);
            }

            return true;
        }
    }
}
