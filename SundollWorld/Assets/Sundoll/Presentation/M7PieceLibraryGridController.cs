using System;
using System.Collections;
using System.Collections.Generic;
using Sundoll.Core;
using Sundoll.Infrastructure;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Virtualized, two-column definition grid. Only visible rows hold Image
    /// references, while a bounded LRU retains decoded 128px thumbnails.
    /// </summary>
    public sealed class M7PieceLibraryGridController : IWorkbenchPanelController
    {
        private const int Columns = 2;
        private const float RowHeight = 116f;

        private sealed class DefinitionRow
        {
            public readonly M4PieceDefinition[] definitions = new M4PieceDefinition[Columns];
        }

        private sealed class CardBinding
        {
            public Button button;
            public Image image;
            public Label title;
            public Label status;
            public string definitionId;
            public string acquiredAssetId;
        }

        private sealed class RowBinding
        {
            public readonly CardBinding[] cards = new CardBinding[Columns];
        }

        private readonly Action<string> selectDefinition;
        private readonly List<DefinitionRow> rows = new List<DefinitionRow>();
        private readonly M7PieceThumbnailCache thumbnailCache;
        private WorkbenchSession session;
        private string search = string.Empty;
        private string selectedDefinitionId;
        private bool disposed;

        public M7PieceLibraryGridController(Action<string> selectDefinition, long thumbnailBudgetBytes = 16L * 1024L * 1024L)
        {
            this.selectDefinition = selectDefinition ?? throw new ArgumentNullException(nameof(selectDefinition));
            thumbnailCache = new M7PieceThumbnailCache(thumbnailBudgetBytes);
            Element = new ListView
            {
                name = "PieceLibraryList",
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                showBorder = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.None,
                makeItem = MakeRow,
                bindItem = BindRow,
                unbindItem = UnbindRow,
                destroyItem = DestroyRow
            };
            Element.AddToClassList("sw-piece-grid");
            Element.itemsSource = rows;
        }

        public ListView Element { get; }
        public int FilteredDefinitionCount { get; private set; }
        public int CachedThumbnailCount => thumbnailCache.Count;
        public long CachedThumbnailBytes => thumbnailCache.ResidentBytes;

        public void Bind(WorkbenchSession nextSession)
        {
            ThrowIfDisposed();
            if (ReferenceEquals(session, nextSession))
            {
                Refresh();
                return;
            }

            Element.Rebuild();
            thumbnailCache.Clear();
            session = nextSession ?? throw new ArgumentNullException(nameof(nextSession));
            Refresh();
        }

        public void SetSearch(string value)
        {
            value = value == null ? string.Empty : value.Trim();
            if (string.Equals(search, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            search = value;
        }

        public void SetSelectedDefinition(string definitionId)
        {
            if (string.Equals(selectedDefinitionId, definitionId, StringComparison.Ordinal))
            {
                return;
            }

            selectedDefinitionId = definitionId;
            Element.RefreshItems();
        }

        public void Refresh()
        {
            ThrowIfDisposed();
            rows.Clear();
            FilteredDefinitionCount = 0;
            if (session == null || session.CommandBus.State == null || session.CommandBus.State.pieceDefinitions == null)
            {
                Element.Rebuild();
                return;
            }

            var filtered = new List<M4PieceDefinition>();
            foreach (var definition in session.CommandBus.State.pieceDefinitions)
            {
                if (definition != null && MatchesSearch(definition, search))
                {
                    filtered.Add(definition);
                }
            }

            filtered.Sort(CompareDefinitions);
            FilteredDefinitionCount = filtered.Count;
            for (var index = 0; index < filtered.Count; index += Columns)
            {
                var row = new DefinitionRow();
                for (var column = 0; column < Columns && index + column < filtered.Count; column++)
                {
                    row.definitions[column] = filtered[index + column];
                }

                rows.Add(row);
            }

            Element.Rebuild();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Element.itemsSource = null;
            Element.Rebuild();
            thumbnailCache.Dispose();
            session = null;
            rows.Clear();
        }

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("sw-piece-grid-row");
            var binding = new RowBinding();
            row.userData = binding;
            for (var column = 0; column < Columns; column++)
            {
                var card = new CardBinding();
                var captured = card;
                card.button = new Button(() =>
                {
                    if (!string.IsNullOrWhiteSpace(captured.definitionId))
                    {
                        selectDefinition(captured.definitionId);
                    }
                });
                card.button.AddToClassList("sw-piece-card");
                card.image = new Image { scaleMode = ScaleMode.ScaleToFit };
                card.image.AddToClassList("sw-piece-card-image");
                card.button.Add(card.image);
                card.title = new Label();
                card.title.AddToClassList("sw-piece-card-title");
                card.button.Add(card.title);
                card.status = new Label();
                card.status.AddToClassList("sw-piece-card-status");
                card.button.Add(card.status);
                binding.cards[column] = card;
                row.Add(card.button);
            }

            return row;
        }

        private void BindRow(VisualElement element, int rowIndex)
        {
            var binding = element.userData as RowBinding;
            if (binding == null || rowIndex < 0 || rowIndex >= rows.Count)
            {
                return;
            }

            ReleaseRow(binding);
            var row = rows[rowIndex];
            for (var column = 0; column < Columns; column++)
            {
                BindCard(binding.cards[column], row.definitions[column]);
            }
        }

        private void UnbindRow(VisualElement element, int _)
        {
            if (element.userData is RowBinding binding)
            {
                ReleaseRow(binding);
            }
        }

        private void DestroyRow(VisualElement element)
        {
            if (element.userData is RowBinding binding)
            {
                ReleaseRow(binding);
            }
        }

        private void BindCard(CardBinding card, M4PieceDefinition definition)
        {
            if (definition == null)
            {
                card.definitionId = null;
                card.button.style.display = DisplayStyle.None;
                card.image.image = null;
                return;
            }

            card.definitionId = definition.id;
            card.button.name = "PieceDefinition_" + definition.id;
            card.button.style.display = DisplayStyle.Flex;
            card.button.EnableInClassList(
                "sw-piece-card-selected",
                string.Equals(definition.id, selectedDefinitionId, StringComparison.Ordinal));
            card.title.text = string.IsNullOrWhiteSpace(definition.displayName)
                ? definition.id
                : definition.displayName;
            card.button.tooltip = definition.id + "\n" + (definition.category ?? string.Empty);

            var asset = M4PieceQueries.FindAsset(session.CommandBus.State, definition.assetId);
            if (string.IsNullOrWhiteSpace(definition.assetId))
            {
                SetMissing(card, "无图片");
                return;
            }

            if (asset == null)
            {
                SetMissing(card, "引用缺失");
                return;
            }

            if (!thumbnailCache.TryAcquire(asset, session.PieceAssetCatalog, out var texture, out var diagnostic))
            {
                SetMissing(card, diagnostic);
                return;
            }

            card.acquiredAssetId = asset.id;
            card.image.image = texture;
            card.status.text = string.IsNullOrWhiteSpace(definition.category) ? "未分类" : definition.category;
            card.status.EnableInClassList("sw-error", false);
        }

        private static void SetMissing(CardBinding card, string diagnostic)
        {
            card.image.image = null;
            card.status.text = string.IsNullOrWhiteSpace(diagnostic) ? "图片缺失" : diagnostic;
            card.status.EnableInClassList("sw-error", true);
        }

        private void ReleaseRow(RowBinding binding)
        {
            foreach (var card in binding.cards)
            {
                if (card == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(card.acquiredAssetId))
                {
                    thumbnailCache.Release(card.acquiredAssetId);
                    card.acquiredAssetId = null;
                }

                card.image.image = null;
                card.definitionId = null;
            }
        }

        private static int CompareDefinitions(M4PieceDefinition left, M4PieceDefinition right)
        {
            var leftName = string.IsNullOrWhiteSpace(left.displayName) ? left.id : left.displayName;
            var rightName = string.IsNullOrWhiteSpace(right.displayName) ? right.id : right.displayName;
            var result = string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
            return result != 0 ? result : string.CompareOrdinal(left.id, right.id);
        }

        private static bool MatchesSearch(M4PieceDefinition definition, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            if ((definition.displayName ?? string.Empty).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (definition.category ?? string.Empty).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (definition.id ?? string.Empty).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (definition.tags != null)
            {
                foreach (var tag in definition.tags)
                {
                    if ((tag ?? string.Empty).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(M7PieceLibraryGridController));
            }
        }
    }

    /// <summary>
    /// Reference-counted LRU for display proxies. It intentionally refuses to
    /// decode an original when a thumbnail is absent, avoiding accidental
    /// 4096x4096 residency in the library panel.
    /// </summary>
    public sealed class M7PieceThumbnailCache : IDisposable
    {
        private sealed class Entry
        {
            public Texture2D texture;
            public long byteLength;
            public long lastUse;
            public int references;
        }

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly long budgetBytes;
        private long useSequence;

        public M7PieceThumbnailCache(long budgetBytes)
        {
            if (budgetBytes < 256L * 1024L)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetBytes));
            }

            this.budgetBytes = budgetBytes;
        }

        public int Count => entries.Count;
        public long ResidentBytes { get; private set; }

        public bool TryAcquire(
            M4PieceAsset asset,
            M4PieceAssetCatalog catalog,
            out Texture2D texture,
            out string diagnostic)
        {
            texture = null;
            diagnostic = string.Empty;
            if (asset == null || catalog == null || string.IsNullOrWhiteSpace(asset.id))
            {
                diagnostic = "图片记录缺失";
                return false;
            }

            if (entries.TryGetValue(asset.id, out var cached) && cached.texture != null)
            {
                cached.references++;
                cached.lastUse = ++useSequence;
                texture = cached.texture;
                return true;
            }

            if (!catalog.TryReadThumbnailBytes(asset, out var bytes))
            {
                diagnostic = catalog.IsAssetAvailable(asset) ? "缩略图缺失" : "图片文件缺失";
                return false;
            }

            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(loaded, bytes, true))
            {
                DestroyTexture(loaded);
                diagnostic = "缩略图损坏";
                return false;
            }

            if (loaded.width > 256 || loaded.height > 256)
            {
                DestroyTexture(loaded);
                diagnostic = "缩略图尺寸异常";
                return false;
            }

            loaded.name = "SundollWorld.PieceThumbnail." + asset.id;
            loaded.wrapMode = TextureWrapMode.Clamp;
            loaded.filterMode = FilterMode.Bilinear;
            var entry = new Entry
            {
                texture = loaded,
                byteLength = Math.Max(4L, (long)loaded.width * loaded.height * 4L),
                lastUse = ++useSequence,
                references = 1
            };
            entries[asset.id] = entry;
            ResidentBytes += entry.byteLength;
            EvictToBudget();
            texture = loaded;
            return true;
        }

        public void Release(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId) || !entries.TryGetValue(assetId, out var entry))
            {
                return;
            }

            entry.references = Math.Max(0, entry.references - 1);
            entry.lastUse = ++useSequence;
            EvictToBudget();
        }

        public void Clear()
        {
            foreach (var entry in entries.Values)
            {
                DestroyTexture(entry.texture);
            }

            entries.Clear();
            ResidentBytes = 0;
        }

        public void Dispose()
        {
            Clear();
        }

        private void EvictToBudget()
        {
            while (ResidentBytes > budgetBytes)
            {
                string oldestId = null;
                Entry oldest = null;
                foreach (var pair in entries)
                {
                    if (pair.Value.references != 0 || oldest != null && pair.Value.lastUse >= oldest.lastUse)
                    {
                        continue;
                    }

                    oldestId = pair.Key;
                    oldest = pair.Value;
                }

                if (oldest == null)
                {
                    return;
                }

                DestroyTexture(oldest.texture);
                ResidentBytes -= oldest.byteLength;
                entries.Remove(oldestId);
            }
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
