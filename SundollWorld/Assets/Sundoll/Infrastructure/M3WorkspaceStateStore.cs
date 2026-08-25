using System;
using System.Collections.Generic;
using System.IO;
using Sundoll.Application;
using UnityEngine;

namespace Sundoll.Infrastructure
{
    [Serializable]
    internal sealed class M3WorkspaceStateDocument
    {
        public int formatVersion = 2;
        public string mapId;
        public List<string> hiddenLayerIds = new List<string>();
        public List<string> lockedLayerIds = new List<string>();
        public List<string> layerOrder = new List<string>();
        public string currentTool;
        public string currentLayerId;
        public float zoom = 1f;
        public float panX;
        public float panY;
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
    }

    public sealed class M3WorkspaceStateStore
    {
        private const int CurrentFormatVersion = 2;

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
        public string StatePath => Path.Combine(RootPath, "workspace-state.json");

        public M3WorkspaceStateLoadResult Load(string mapId, IEnumerable<string> layerIds)
        {
            ValidateMapId(mapId);
            var knownLayerIds = CopyLayerIds(layerIds);
            var defaultState = new M3LayerEditState(knownLayerIds);
            if (!File.Exists(StatePath))
            {
                return new M3WorkspaceStateLoadResult
                {
                    state = defaultState,
                    loaded = false,
                    currentTool = "画笔",
                    currentLayerId = knownLayerIds[0]
                };
            }

            try
            {
                var document = JsonUtility.FromJson<M3WorkspaceStateDocument>(File.ReadAllText(StatePath));
                if (document == null || (document.formatVersion != 1 && document.formatVersion != CurrentFormatVersion) ||
                    !string.Equals(document.mapId, mapId, StringComparison.Ordinal))
                {
                    return new M3WorkspaceStateLoadResult
                    {
                        state = defaultState,
                        loaded = false,
                        diagnostic = "Workspace 状态版本或地图 ID 不匹配，已使用默认状态。",
                        currentTool = "画笔",
                        currentLayerId = knownLayerIds[0]
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

                return new M3WorkspaceStateLoadResult
                {
                    state = defaultState,
                    loaded = true,
                    diagnostic = diagnostics.Count == 0 ? null : string.Join(" ", diagnostics.ToArray()),
                    currentTool = string.IsNullOrWhiteSpace(document.currentTool) ? "画笔" : document.currentTool,
                    currentLayerId = currentLayerId,
                    zoom = document.formatVersion >= 2 && document.zoom > 0f ? document.zoom : 1f,
                    panX = document.formatVersion >= 2 ? document.panX : 0f,
                    panY = document.formatVersion >= 2 ? document.panY : 0f
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
                    currentLayerId = knownLayerIds[0]
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

            Save(mapId, state, layerIds, "画笔", null, 1f, 0f, 0f);
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
                panY = panY
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
            }

            M2FileIO.WriteUtf8Atomic(StatePath, JsonUtility.ToJson(document, true));
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
