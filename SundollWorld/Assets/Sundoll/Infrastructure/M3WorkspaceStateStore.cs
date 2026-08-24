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
        public int formatVersion = 1;
        public string mapId;
        public List<string> hiddenLayerIds = new List<string>();
        public List<string> lockedLayerIds = new List<string>();
    }

    public sealed class M3WorkspaceStateLoadResult
    {
        public M3LayerEditState state;
        public bool loaded;
        public string diagnostic;
    }

    public sealed class M3WorkspaceStateStore
    {
        private const int CurrentFormatVersion = 1;

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
                    loaded = false
                };
            }

            try
            {
                var document = JsonUtility.FromJson<M3WorkspaceStateDocument>(File.ReadAllText(StatePath));
                if (document == null || document.formatVersion != CurrentFormatVersion ||
                    !string.Equals(document.mapId, mapId, StringComparison.Ordinal))
                {
                    return new M3WorkspaceStateLoadResult
                    {
                        state = defaultState,
                        loaded = false,
                        diagnostic = "Workspace 状态版本或地图 ID 不匹配，已使用默认状态。"
                    };
                }

                var diagnostics = new List<string>();
                ApplyVisibility(document.hiddenLayerIds, defaultState, false, diagnostics);
                ApplyLocks(document.lockedLayerIds, defaultState, true, diagnostics);
                return new M3WorkspaceStateLoadResult
                {
                    state = defaultState,
                    loaded = true,
                    diagnostic = diagnostics.Count == 0 ? null : string.Join(" ", diagnostics.ToArray())
                };
            }
            catch (Exception exception)
            {
                return new M3WorkspaceStateLoadResult
                {
                    state = defaultState,
                    loaded = false,
                    diagnostic = "Workspace 状态读取失败，已使用默认状态：" + exception.Message
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

            var knownLayerIds = CopyLayerIds(layerIds);
            var document = new M3WorkspaceStateDocument
            {
                formatVersion = CurrentFormatVersion,
                mapId = mapId
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
