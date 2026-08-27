using System;
using System.Collections.Generic;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;
using UnityEngine;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Disposable M4 piece view. Missing or placeholder assets use a generated
    /// neutral sprite; the authoritative definition and instance remain pure
    /// data and survive a later asset replacement.
    /// </summary>
    public sealed class M4WorkbenchPieceProjection : MonoBehaviour
    {
        private readonly Dictionary<string, GameObject> views = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, Sprite> viewSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> viewAssetIds = new Dictionary<string, string>(StringComparer.Ordinal);
        private M1CommandBus commandBus;
        private M4PieceAssetCatalog assetCatalog;
        private M1WorldState audienceProjectionState;
        private M7TextureCache textureCache;
        private Sprite placeholderSprite;
        private Texture2D placeholderTexture;

        public IReadOnlyDictionary<string, GameObject> Views => views;
        public int CachedTextureCount => textureCache == null ? 0 : textureCache.Count;
        public bool IsAudienceProjectionActive => audienceProjectionState != null;

        public void Bind(M1CommandBus nextCommandBus)
        {
            Bind(nextCommandBus, null);
        }

        public void Bind(M1CommandBus nextCommandBus, M4PieceAssetCatalog nextAssetCatalog)
        {
            commandBus = nextCommandBus ?? throw new ArgumentNullException(nameof(nextCommandBus));
            assetCatalog = nextAssetCatalog;
            if (textureCache == null)
            {
                textureCache = new M7TextureCache();
            }

            EnsurePlaceholderSprite();
            RefreshAll();
        }

        public void SetAudienceProjection(M1WorldState nextState)
        {
            audienceProjectionState = nextState;
            RefreshAll();
        }

        public void RefreshAll()
        {
            if (commandBus == null || commandBus.State == null)
            {
                return;
            }

            EnsurePlaceholderSprite();
            var state = audienceProjectionState ?? commandBus.State;
            var activeIds = new HashSet<string>(StringComparer.Ordinal);
            if (state.pieceInstances != null)
            {
                foreach (var instance in state.pieceInstances)
                {
                    if (instance == null || string.IsNullOrWhiteSpace(instance.id) || !instance.visible)
                    {
                        continue;
                    }

                    if (!TryResolveBoardPosition(state, instance.id, out var position))
                    {
                        continue;
                    }

                    activeIds.Add(instance.id);
                    if (!views.TryGetValue(instance.id, out var view) || view == null)
                    {
                        view = CreateView(instance.id);
                        views[instance.id] = view;
                    }

                    view.transform.position = position;
                    var renderer = view.GetComponent<SpriteRenderer>();
                    ApplySprite(state, instance, renderer);
                    renderer.color = ColorForInstance(state, instance);
                    renderer.sortingOrder = 100 + Math.Max(0, instance.location == null ? 0 : instance.location.stackOrder);
                    view.transform.rotation = Quaternion.Euler(0f, 0f, instance.rotation);
                    view.transform.localScale = instance.flipped ? new Vector3(-1f, 1f, 1f) : Vector3.one;
                }
            }

            var staleIds = new List<string>();
            foreach (var pair in views)
            {
                if (!activeIds.Contains(pair.Key))
                {
                    staleIds.Add(pair.Key);
                }
            }

            foreach (var staleId in staleIds)
            {
                if (views[staleId] != null)
                {
                    Destroy(views[staleId]);
                }

                ReleaseViewAsset(staleId);

                views.Remove(staleId);
            }
        }

        private GameObject CreateView(string instanceId)
        {
            var view = new GameObject("PieceView-" + instanceId);
            view.transform.SetParent(transform, false);
            var renderer = view.AddComponent<SpriteRenderer>();
            renderer.sprite = placeholderSprite;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = Vector2.one * 0.82f;
            return view;
        }

        private void ApplySprite(M1WorldState state, M4PieceInstance instance, SpriteRenderer renderer)
        {
            var definition = M4PieceQueries.FindDefinition(state, instance.definitionId);
            var asset = M4PieceQueries.FindAsset(state, definition == null ? null : definition.assetId);
            var desiredAssetId = asset == null ? null : asset.id;
            if (viewAssetIds.TryGetValue(instance.id, out var currentAssetId) && currentAssetId == desiredAssetId)
            {
                return;
            }

            ReleaseViewAsset(instance.id);
            if (assetCatalog != null && asset != null && textureCache != null &&
                textureCache.TryAcquire(asset, assetCatalog, out var texture, out _))
            {
                var sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    Mathf.Max(1f, texture.width));
                sprite.name = "SundollWorld.PieceSprite." + instance.id;
                viewSprites[instance.id] = sprite;
                viewAssetIds[instance.id] = asset.id;
                renderer.sprite = sprite;
                renderer.drawMode = SpriteDrawMode.Simple;
                renderer.size = Vector2.one;
                return;
            }

            renderer.sprite = placeholderSprite;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = Vector2.one * 0.82f;
        }

        private void ReleaseViewAsset(string instanceId)
        {
            if (viewAssetIds.TryGetValue(instanceId, out var assetId))
            {
                textureCache?.Release(assetId);
                viewAssetIds.Remove(instanceId);
            }

            if (viewSprites.TryGetValue(instanceId, out var sprite))
            {
                if (sprite != null)
                {
                    Destroy(sprite);
                }

                viewSprites.Remove(instanceId);
            }
        }

        private bool TryResolveBoardPosition(M1WorldState state, string instanceId, out Vector3 position)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = M4PieceQueries.FindInstance(state, instanceId);
            while (current != null && current.location != null && visited.Add(current.id))
            {
                switch (current.location.kind)
                {
                    case M1PieceLocationKind.OnBoard:
                        position = new Vector3(current.location.x, current.location.y, -0.5f);
                        return true;
                    case M1PieceLocationKind.InContainer:
                        current = M4PieceQueries.FindInstance(state, current.location.containerPieceId);
                        break;
                    case M1PieceLocationKind.Attached:
                        current = M4PieceQueries.FindInstance(state, current.location.attachedToPieceId);
                        break;
                    default:
                        position = default(Vector3);
                        return false;
                }
            }

            position = default(Vector3);
            return false;
        }

        private static Color ColorForInstance(M1WorldState state, M4PieceInstance instance)
        {
            var definition = M4PieceQueries.FindDefinition(state, instance.definitionId);
            var hash = string.IsNullOrWhiteSpace(definition == null ? null : definition.category)
                ? instance.definitionId
                : definition.category;
            unchecked
            {
                var value = 17;
                foreach (var character in hash ?? string.Empty)
                {
                    value = value * 31 + character;
                }

                var hue = (Math.Abs(value) % 360) / 360f;
                return Color.HSVToRGB(hue, 0.45f, 0.95f);
            }
        }

        private void EnsurePlaceholderSprite()
        {
            if (placeholderSprite != null)
            {
                return;
            }

            placeholderTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "SundollWorld.M4PlaceholderTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            placeholderTexture.SetPixel(0, 0, Color.white);
            placeholderTexture.Apply(false, true);
            placeholderSprite = Sprite.Create(
                placeholderTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f,
                0,
                SpriteMeshType.FullRect);
            placeholderSprite.name = "SundollWorld.M4PlaceholderSprite";
        }

        private void OnDestroy()
        {
            foreach (var view in views.Values)
            {
                if (view != null)
                {
                    Destroy(view);
                }
            }

            views.Clear();
            foreach (var instanceId in new List<string>(viewAssetIds.Keys))
            {
                ReleaseViewAsset(instanceId);
            }

            textureCache?.Dispose();
            textureCache = null;
            if (placeholderSprite != null)
            {
                Destroy(placeholderSprite);
            }

            if (placeholderTexture != null)
            {
                Destroy(placeholderTexture);
            }
        }
    }
}
