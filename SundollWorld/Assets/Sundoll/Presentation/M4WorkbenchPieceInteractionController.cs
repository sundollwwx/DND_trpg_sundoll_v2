using System;
using System.Collections.Generic;
using Sundoll.Application;
using Sundoll.Core;
using UnityEngine;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Owns transient board-piece interaction: click selection, marquee
    /// selection, drag preview and selected-piece shortcuts. It deliberately
    /// keeps drag/marquee state outside WorldState; releasing a drag creates
    /// one M4 facade command instead of one command per frame.
    /// </summary>
    public sealed class M4WorkbenchPieceInteractionController : MonoBehaviour
    {
        private readonly HashSet<string> selectedPieceIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> dragGhosts = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private M3WorkbenchRoot root;
        private M4PieceLibraryFacade pieceLibrary;
        private M4WorkbenchPieceProjection projection;
        private string primaryPieceId;
        private Vector2Int pointerAnchorCell;
        private Vector2Int lastPointerCell;
        private Vector2Int marqueeStartCell;
        private bool additiveSelection;
        private bool dragCandidate;
        private bool dragging;
        private bool marqueeActive;
        private GameObject marqueePreview;
        private Sprite previewSprite;
        private Texture2D previewTexture;

        public int SelectedCount => selectedPieceIds.Count;
        public int DragGhostCount => dragGhosts.Count;
        public bool HasSelection => selectedPieceIds.Count > 0;
        public string PrimaryPieceId => primaryPieceId;

        public void Bind(
            M3WorkbenchRoot nextRoot,
            M4PieceLibraryFacade nextPieceLibrary,
            M4WorkbenchPieceProjection nextProjection)
        {
            root = nextRoot ?? throw new ArgumentNullException(nameof(nextRoot));
            pieceLibrary = nextPieceLibrary ?? throw new ArgumentNullException(nameof(nextPieceLibrary));
            projection = nextProjection ?? throw new ArgumentNullException(nameof(nextProjection));
            PruneMissingSelection();
            NotifySelectionChanged();
        }

        public bool BeginPointerAction(Vector2 screenPosition, bool additive)
        {
            if (!CanInteract() || !root.TryScreenToCell(screenPosition, out var cell))
            {
                return false;
            }

            ClearPointerPreview();
            pointerAnchorCell = cell;
            lastPointerCell = cell;
            additiveSelection = additive;
            var state = pieceLibrary.State;
            var boardId = state.board == null ? null : state.board.id;
            var instance = M4PieceQueries.FindTopmostBoardInstanceAt(state, boardId, cell.x, cell.y);
            if (instance != null)
            {
                if (additive)
                {
                    if (!selectedPieceIds.Remove(instance.id))
                    {
                        selectedPieceIds.Add(instance.id);
                        primaryPieceId = instance.id;
                    }
                    else if (primaryPieceId == instance.id)
                    {
                        primaryPieceId = FindFirstSelectedId();
                    }

                    NotifySelectionChanged();
                    return true;
                }

                if (!selectedPieceIds.Contains(instance.id))
                {
                    selectedPieceIds.Clear();
                    selectedPieceIds.Add(instance.id);
                    primaryPieceId = instance.id;
                    NotifySelectionChanged();
                }
                else
                {
                    primaryPieceId = instance.id;
                    NotifySelectionChanged();
                }

                dragCandidate = true;
                return true;
            }

            if (root.CurrentTool != "选择")
            {
                return false;
            }

            marqueeActive = true;
            marqueeStartCell = cell;
            UpdateMarqueePreview(cell);
            return true;
        }

        public void ContinuePointerAction(Vector2 screenPosition)
        {
            if (!CanInteract() || !root.TryScreenToCell(screenPosition, out var cell))
            {
                return;
            }

            lastPointerCell = cell;
            if (marqueeActive)
            {
                UpdateMarqueePreview(cell);
                return;
            }

            if (!dragCandidate || cell == pointerAnchorCell)
            {
                return;
            }

            if (!dragging)
            {
                dragging = true;
                CreateDragGhosts();
            }

            UpdateDragGhostPositions(cell);
        }

        public void EndPointerAction(Vector2 screenPosition)
        {
            if (root == null || !root.TryScreenToCell(screenPosition, out var cell))
            {
                cell = lastPointerCell;
            }

            if (marqueeActive)
            {
                marqueeActive = false;
                ApplyMarqueeSelection(cell);
                ClearPointerPreview();
                return;
            }

            if (dragging)
            {
                CommitDrag(cell);
            }

            ClearPointerPreview();
        }

        public void CancelPointerAction()
        {
            ClearPointerPreview();
        }

        public void SelectOnly(string instanceId)
        {
            if (pieceLibrary == null || M4PieceQueries.FindInstance(pieceLibrary.State, instanceId) == null)
            {
                return;
            }

            selectedPieceIds.Clear();
            selectedPieceIds.Add(instanceId);
            primaryPieceId = instanceId;
            NotifySelectionChanged();
        }

        public bool RotateSelectionClockwise()
        {
            return ApplyPresentation(
                "选中棋子已顺时针旋转 90°",
                instance => new M4PiecePresentationMutation(
                    instance.id,
                    (instance.rotation + 90) % 360,
                    instance.flipped,
                    instance.visible));
        }

        public bool FlipSelection()
        {
            return ApplyPresentation(
                "选中棋子已翻面",
                instance => new M4PiecePresentationMutation(
                    instance.id,
                    instance.rotation,
                    !instance.flipped,
                    instance.visible));
        }

        public bool ToggleSelectionVisibility()
        {
            return ApplyPresentation(
                "选中棋子显隐已切换",
                instance => new M4PiecePresentationMutation(
                    instance.id,
                    instance.rotation,
                    instance.flipped,
                    !instance.visible));
        }

        public bool DeleteSelection()
        {
            if (!CanInteract() || selectedPieceIds.Count == 0)
            {
                return false;
            }

            var receipt = pieceLibrary.DeleteInstances(selectedPieceIds);
            if (receipt.accepted)
            {
                selectedPieceIds.Clear();
                primaryPieceId = null;
                root.CommitPieceInteractionReceipt(receipt, "已删除选中棋子");
                NotifySelectionChanged();
            }
            else
            {
                root.CommitPieceInteractionReceipt(receipt, null);
            }

            return true;
        }

        private bool ApplyPresentation(
            string acceptedStatus,
            Func<M4PieceInstance, M4PiecePresentationMutation> createMutation)
        {
            if (!CanInteract() || selectedPieceIds.Count == 0 || createMutation == null)
            {
                return false;
            }

            var mutations = new List<M4PiecePresentationMutation>();
            foreach (var instanceId in selectedPieceIds)
            {
                var instance = M4PieceQueries.FindInstance(pieceLibrary.State, instanceId);
                if (instance != null)
                {
                    mutations.Add(createMutation(instance));
                }
            }

            if (mutations.Count == 0)
            {
                PruneMissingSelection();
                return false;
            }

            root.CommitPieceInteractionReceipt(pieceLibrary.SetPresentationBatch(mutations), acceptedStatus);
            return true;
        }

        private void CommitDrag(Vector2Int destinationAnchor)
        {
            var deltaX = destinationAnchor.x - pointerAnchorCell.x;
            var deltaY = destinationAnchor.y - pointerAnchorCell.y;
            if (deltaX == 0 && deltaY == 0)
            {
                return;
            }

            var mutations = new List<M4PieceMoveMutation>();
            foreach (var instanceId in selectedPieceIds)
            {
                var instance = M4PieceQueries.FindInstance(pieceLibrary.State, instanceId);
                if (instance != null && instance.location != null && instance.location.kind == M1PieceLocationKind.OnBoard)
                {
                    mutations.Add(new M4PieceMoveMutation(
                        instance.id,
                        instance.location.x + deltaX,
                        instance.location.y + deltaY));
                }
            }

            if (mutations.Count > 0)
            {
                root.CommitPieceInteractionReceipt(
                    pieceLibrary.MoveBatch(mutations),
                    "已移动选中棋子（" + mutations.Count + "个）");
            }
        }

        private void ApplyMarqueeSelection(Vector2Int endCell)
        {
            var minX = Mathf.Min(marqueeStartCell.x, endCell.x);
            var minY = Mathf.Min(marqueeStartCell.y, endCell.y);
            var maxX = Mathf.Max(marqueeStartCell.x, endCell.x);
            var maxY = Mathf.Max(marqueeStartCell.y, endCell.y);
            if (!additiveSelection)
            {
                selectedPieceIds.Clear();
                primaryPieceId = null;
            }

            var state = pieceLibrary.State;
            var boardId = state.board == null ? null : state.board.id;
            if (state.pieceInstances != null)
            {
                foreach (var instance in state.pieceInstances)
                {
                    if (instance == null || !instance.visible || instance.location == null ||
                        instance.location.kind != M1PieceLocationKind.OnBoard || instance.location.boardId != boardId ||
                        instance.location.x < minX || instance.location.x > maxX ||
                        instance.location.y < minY || instance.location.y > maxY)
                    {
                        continue;
                    }

                    selectedPieceIds.Add(instance.id);
                    if (string.IsNullOrWhiteSpace(primaryPieceId))
                    {
                        primaryPieceId = instance.id;
                    }
                }
            }

            NotifySelectionChanged();
        }

        private void CreateDragGhosts()
        {
            ClearDragGhosts();
            foreach (var instanceId in selectedPieceIds)
            {
                if (!projection.Views.TryGetValue(instanceId, out var source) || source == null)
                {
                    continue;
                }

                var sourceRenderer = source.GetComponent<SpriteRenderer>();
                if (sourceRenderer == null || sourceRenderer.sprite == null)
                {
                    continue;
                }

                var ghost = new GameObject("PieceDragGhost-" + instanceId);
                ghost.transform.SetParent(transform, false);
                var renderer = ghost.AddComponent<SpriteRenderer>();
                renderer.sprite = sourceRenderer.sprite;
                var color = sourceRenderer.color;
                color.a = Mathf.Clamp01(color.a * 0.5f);
                renderer.color = color;
                renderer.sortingOrder = 10000 + sourceRenderer.sortingOrder;
                ghost.transform.rotation = source.transform.rotation;
                ghost.transform.localScale = source.transform.localScale;
                dragGhosts[instanceId] = ghost;
            }
        }

        private void UpdateDragGhostPositions(Vector2Int destinationAnchor)
        {
            var deltaX = destinationAnchor.x - pointerAnchorCell.x;
            var deltaY = destinationAnchor.y - pointerAnchorCell.y;
            foreach (var pair in dragGhosts)
            {
                var instance = M4PieceQueries.FindInstance(pieceLibrary.State, pair.Key);
                if (pair.Value != null && instance != null && instance.location != null)
                {
                    pair.Value.transform.position = new Vector3(
                        instance.location.x + deltaX,
                        instance.location.y + deltaY,
                        -1.5f);
                }
            }
        }

        private void UpdateMarqueePreview(Vector2Int endCell)
        {
            EnsurePreviewSprite();
            if (marqueePreview == null)
            {
                marqueePreview = new GameObject("PieceMarqueePreview");
                marqueePreview.transform.SetParent(transform, false);
                var renderer = marqueePreview.AddComponent<SpriteRenderer>();
                renderer.sprite = previewSprite;
                renderer.drawMode = SpriteDrawMode.Sliced;
                renderer.color = new Color(1f, 0.78f, 0.28f, 0.18f);
                renderer.sortingOrder = 9000;
            }

            var minX = Mathf.Min(marqueeStartCell.x, endCell.x);
            var minY = Mathf.Min(marqueeStartCell.y, endCell.y);
            var maxX = Mathf.Max(marqueeStartCell.x, endCell.x);
            var maxY = Mathf.Max(marqueeStartCell.y, endCell.y);
            marqueePreview.transform.position = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, -1.5f);
            marqueePreview.GetComponent<SpriteRenderer>().size = new Vector2(maxX - minX + 0.9f, maxY - minY + 0.9f);
        }

        private void EnsurePreviewSprite()
        {
            if (previewSprite != null)
            {
                return;
            }

            previewTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "SundollWorld.M4PieceInteractionPreview",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            previewTexture.SetPixel(0, 0, Color.white);
            previewTexture.Apply(false, true);
            previewSprite = Sprite.Create(
                previewTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f,
                0,
                SpriteMeshType.FullRect);
        }

        private void ClearPointerPreview()
        {
            dragCandidate = false;
            dragging = false;
            marqueeActive = false;
            ClearDragGhosts();
            if (marqueePreview != null)
            {
                Destroy(marqueePreview);
                marqueePreview = null;
            }
        }

        private void ClearDragGhosts()
        {
            foreach (var ghost in dragGhosts.Values)
            {
                if (ghost != null)
                {
                    Destroy(ghost);
                }
            }

            dragGhosts.Clear();
        }

        private void PruneMissingSelection()
        {
            if (pieceLibrary == null)
            {
                selectedPieceIds.Clear();
                primaryPieceId = null;
                return;
            }

            var missing = new List<string>();
            foreach (var instanceId in selectedPieceIds)
            {
                if (M4PieceQueries.FindInstance(pieceLibrary.State, instanceId) == null)
                {
                    missing.Add(instanceId);
                }
            }

            foreach (var instanceId in missing)
            {
                selectedPieceIds.Remove(instanceId);
            }

            if (!selectedPieceIds.Contains(primaryPieceId))
            {
                primaryPieceId = FindFirstSelectedId();
            }
        }

        private string FindFirstSelectedId()
        {
            foreach (var instanceId in selectedPieceIds)
            {
                return instanceId;
            }

            return null;
        }

        private bool CanInteract()
        {
            return root != null && pieceLibrary != null && !root.IsPieceInteractionReadOnly;
        }

        private void NotifySelectionChanged()
        {
            projection?.SetSelectedPieceIds(selectedPieceIds);
            root?.ApplyPieceInteractionSelection(selectedPieceIds, primaryPieceId);
        }

        private void OnDestroy()
        {
            ClearPointerPreview();
            if (previewSprite != null)
            {
                Destroy(previewSprite);
            }

            if (previewTexture != null)
            {
                Destroy(previewTexture);
            }
        }
    }
}
