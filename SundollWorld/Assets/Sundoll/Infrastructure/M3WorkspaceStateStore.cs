using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sundoll.Application;
using UnityEngine;

namespace Sundoll.Infrastructure
{
    [Serializable]
    internal sealed class M3WorkspaceStateDocument
    {
        public int formatVersion = 3;
        public string mapId;
        public List<string> hiddenLayerIds = new List<string>();
        public List<string> lockedLayerIds = new List<string>();
        public List<string> layerOrder = new List<string>();
        public string currentTool;
        public string currentLayerId;
        public float zoom = 1f;
        public float panX;
        public float panY;
        public bool hasViewport;
        public string currentWorkspace;
        public List<M3LayerContentSelectionDocument> selectedContentIds = new List<M3LayerContentSelectionDocument>();
    }

    [Serializable]
    internal sealed class M3LayerContentSelectionDocument
    {
        public string layerId;
        public string contentId;
    }

    public sealed class M3WorkspaceStateLoadResult
    {
        public M3LayerEditState state;
        public bool loaded;
        public string diagnostic;
        public string currentTool;
        public string currentLayerId;
        public float zoom = 1f;
        public float panX;
        public float panY;
        public bool hasViewport;
        public string currentWorkspace = "map";
        public Dictionary<string, string> selectedContentIds = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public sealed class M3WorkspaceStateStore
    {
        private const int CurrentFormatVersion = 3;

        public M3WorkspaceStateStore(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Workspace project root is required.", nameof(projectRoot));
            }

            RootPath = projectRoot;
            M2FileIO.EnsureDirectory(RootPath);
        }

        public string RootPath { get; }
        /// <summary>
        /// The pre-M5 single-map state location. It remains readable so an
        /// existing workspace is upgraded lazily the next time it is saved.
        /// </summary>
        public string StatePath => Path.Combine(RootPath, "workspace-state.json");

        public string GetMapStatePath(string mapId)
        {
            ValidateMapId(mapId);
            // Map IDs are user-facing and may contain path separators or
            // platform-specific reserved characters. A stable content hash
            // gives every ID its own portable file without treating it as a
            // filesystem path.
            var fileName = M2FileIO.Sha256(Encoding.UTF8.GetBytes(mapId)) + ".json";
            return Path.Combine(RootPath, "workspace-states", fileName);
        }

        public M3WorkspaceStateLoadResult Load(string mapId, IEnumerable<string> layerIds)
        {
            ValidateMapId(mapId);
            var knownLayerIds = CopyLayerIds(layerIds);
            var defaultState = new M3LayerEditState(knownLayerIds);
            var mapStatePath = GetMapStatePath(mapId);
            var statePath = File.Exists(mapStatePath)
                ? mapStatePath
                : File.Exists(StatePath)
                    ? StatePath
                    : null;
            if (statePath == null)
            {
                return new M3WorkspaceStateLoadResult
                {
                    state = defaultState,
                    loaded = false,
                    currentTool = "画笔",
                    currentLayerId = knownLayerIds[0],
                    currentWorkspace = "map"
                };
            }

            try
            {
                var document = JsonUtility.FromJson<M3WorkspaceStateDocument>(File.ReadAllText(statePath));
                if (document == null || document.formatVersion < 1 || document.formatVersion > CurrentFormatVersion ||
                    !string.Equals(document.mapId, mapId, StringComparison.Ordinal))
                {
                    return new M3WorkspaceStateLoadResult
                    {
                        state = defaultState,
                        loaded = false,
                        diagnostic = "Workspace 状态版本或地图 ID 不匹配，已使用默认状态。",
                        currentTool = "画笔",
                        currentLayerId = knownLayerIds[0],
                        currentWorkspace = "map"
                    };
                }

                var diagnostics = new List<string>();
                ApplyVisibility(document.hiddenLayerIds, defaultState, false, diagnostics);
                ApplyLocks(document.lockedLayerIds, defaultState, true, diagnostics);
                if (document.formatVersion >= 2 && document.layerOrder != null && document.layerOrder.Count > 0)
                {
                    try
                    {
                        defaultState.SetLayerOrder(document.layerOrder);
                    }
                    catch (ArgumentException)
                    {
                        diagnostics.Add("图层顺序无效，已使用默认顺序。");
                    }
                }

                var currentLayerId = string.IsNullOrWhiteSpace(document.currentLayerId)
                    ? knownLayerIds[0]
                    : document.currentLayerId;
                if (!knownLayerIds.Contains(currentLayerId))
                {
                    diagnostics.Add("当前图层无效，已使用默认图层。");
                    currentLayerId = knownLayerIds[0];
                }

                var selectedContentIds = new Dictionary<string, string>(StringComparer.Ordinal);
                if (document.formatVersion >= 3 && document.selectedContentIds != null)
                {
                    foreach (var selection in document.selectedContentIds)
                    {
                        if (selection == null || string.IsNullOrWhiteSpace(selection.layerId) ||
                            string.IsNullOrWhiteSpace(selection.contentId) || !knownLayerIds.Contains(selection.layerId))
                        {
                            continue;
                        }

                        selectedContentIds[selection.layerId] = selection.contentId;
                    }
                }

                return new M3WorkspaceStateLoadResult
                {
                    state = defaultState,
                    loaded = true,
                    diagnostic = diagnostics.Count == 0 ? null : string.Join(" ", diagnostics.ToArray()),
                    currentTool = string.IsNullOrWhiteSpace(document.currentTool) ? "画笔" : document.currentTool,
                    currentLayerId = currentLayerId,
                    zoom = document.formatVersion >= 2 && document.zoom > 0f ? document.zoom : 1f,
                    panX = document.formatVersion >= 2 ? document.panX : 0f,
                    panY = document.formatVersion >= 2 ? document.panY : 0f,
                    // Older format-2/3 documents did not carry hasViewport.
                    // Preserve their existing behavior whenever they contain
                    // a non-default view, while new saves can intentionally
                    // persist a 1x zoom with a zero pan.
                    hasViewport = document.formatVersion >= 2 &&
                                  (document.hasViewport || document.zoom > 1f ||
                                   Math.Abs(document.panX) > 0.0001f || Math.Abs(document.panY) > 0.0001f),
                    currentWorkspace = document.formatVersion >= 3 && !string.IsNullOrWhiteSpace(document.currentWorkspace)
                        ? document.currentWorkspace
                        : "map",
                    selectedContentIds = selectedContentIds
                };
            }
            catch (Exception exception)
            {
                return new M3WorkspaceStateLoadResult
                {
                    state = defaultState,
                    loaded = false,
                    diagnostic = "Workspace 状态读取失败，已使用默认状态：" + exception.Message,
                    currentTool = "画笔",
                    currentLayerId = knownLayerIds[0],
                    currentWorkspace = "map"
                };
            }
        }

        public void Save(string mapId, M3LayerEditState state, IEnumerable<string> layerIds)
        {
            ValidateMapId(mapId);
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            Save(mapId, state, layerIds, "画笔", null, 1f, 0f, 0f, "map", null);
        }

        public void Save(
            string mapId,
            M3LayerEditState state,
            IEnumerable<string> layerIds,
            string currentTool,
            string currentLayerId,
            float zoom,
            float panX,
            float panY)
        {
            Save(mapId, state, layerIds, currentTool, currentLayerId, zoom, panX, panY, "map", null);
        }

        public void Save(
            string mapId,
            M3LayerEditState state,
            IEnumerable<string> layerIds,
            string currentTool,
            string currentLayerId,
            float zoom,
            float panX,
            float panY,
            string currentWorkspace,
            IDictionary<string, string> selectedContentIds)
        {
            ValidateMapId(mapId);
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var knownLayerIds = CopyLayerIds(layerIds);
            if (knownLayerIds.Count == 0)
            {
                throw new ArgumentException("At least one layer ID is required.", nameof(layerIds));
            }

            currentLayerId = string.IsNullOrWhiteSpace(currentLayerId) ? knownLayerIds[0] : currentLayerId;
            if (!knownLayerIds.Contains(currentLayerId))
            {
                throw new ArgumentException("Current layer is not part of the workspace.", nameof(currentLayerId));
            }

            var document = new M3WorkspaceStateDocument
            {
                formatVersion = CurrentFormatVersion,
                mapId = mapId,
                layerOrder = new List<string>(state.LayerOrder),
                currentTool = string.IsNullOrWhiteSpace(currentTool) ? "画笔" : currentTool,
                currentLayerId = currentLayerId,
                zoom = zoom > 0f ? zoom : 1f,
                panX = panX,
                panY = panY,
                hasViewport = true,
                currentWorkspace = string.IsNullOrWhiteSpace(currentWorkspace) ? "map" : currentWorkspace
            };
            foreach (var layerId in knownLayerIds)
            {
                if (!state.IsVisible(layerId))
                {
                    document.hiddenLayerIds.Add(layerId);
                }

                if (state.IsLocked(layerId))
                {
                    document.lockedLayerIds.Add(layerId);
                }

                if (selectedContentIds != null && selectedContentIds.TryGetValue(layerId, out var contentId) &&
                    !string.IsNullOrWhiteSpace(contentId))
                {
                    document.selectedContentIds.Add(new M3LayerContentSelectionDocument
                    {
                        layerId = layerId,
                        contentId = contentId
                    });
                }
            }

            M2FileIO.WriteUtf8Atomic(GetMapStatePath(mapId), JsonUtility.ToJson(document, true));
        }

        private static void ApplyVisibility(
            IList<string> hiddenLayerIds,
            M3LayerEditState state,
            bool visible,
            List<string> diagnostics)
        {
            if (hiddenLayerIds == null)
            {
                return;
            }

            foreach (var layerId in hiddenLayerIds)
            {
                try
                {
                    state.SetVisible(layerId, visible);
                }
                catch (ArgumentException)
                {
                    diagnostics.Add("忽略未知隐藏图层：" + layerId + "。");
                }
            }
        }

        private static void ApplyLocks(
            IList<string> lockedLayerIds,
            M3LayerEditState state,
            bool locked,
            List<string> diagnostics)
        {
            if (lockedLayerIds == null)
            {
                return;
            }

            foreach (var layerId in lockedLayerIds)
            {
                try
                {
                    state.SetLocked(layerId, locked);
                }
                catch (ArgumentException)
                {
                    diagnostics.Add("忽略未知锁定图层：" + layerId + "。");
                }
            }
        }

        private static List<string> CopyLayerIds(IEnumerable<string> layerIds)
        {
            if (layerIds == null)
            {
                throw new ArgumentNullException(nameof(layerIds));
            }

            return new List<string>(layerIds);
        }

        private static void ValidateMapId(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new ArgumentException("Workspace map ID is required.", nameof(mapId));
            }
        }
    }
}
