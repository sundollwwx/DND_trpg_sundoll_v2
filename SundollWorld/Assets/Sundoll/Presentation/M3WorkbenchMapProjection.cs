using System;
using System.Collections.Generic;
using Sundoll.Application;
using Sundoll.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Projects the authoritative map DTO into one Tilemap per content layer.
    /// The Tilemaps are a disposable view; they never become a second state store.
    /// </summary>
    public sealed class M3WorkbenchMapProjection : MonoBehaviour
    {
        private static readonly string[] LayerIds =
        {
            M3MapLayerIds.Terrain,
            M3MapLayerIds.Wall,
            M3MapLayerIds.Object,
            M3MapLayerIds.Interaction,
            M3MapLayerIds.StaticAnnotation
        };

        private readonly Dictionary<string, Tilemap> tilemaps = new Dictionary<string, Tilemap>(StringComparer.Ordinal);
        private readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture2D> visualTextures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly Dictionary<string, Sprite> visualSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private M3MapEditorFacade editor;
        private M3LayerEditState layerEditState;
        private IMapVisualCatalog visualCatalog;
        private M1WorldState audienceProjectionState;
        private Tilemap mapObjectTilemap;
        private Tile mapObjectTile;
        private Texture2D tileTexture;
        private Sprite tileSprite;
        private MeshFilter gridOverlayFilter;
        private MeshRenderer gridOverlayRenderer;
        private Mesh gridOverlayMesh;
        private Material gridOverlayMaterial;
        private int gridOverlayWidth = -1;
        private int gridOverlayHeight = -1;
        private bool gridOverlayVisible = true;

        public IReadOnlyDictionary<string, Tilemap> Tilemaps => tilemaps;
        public bool IsAudienceProjectionActive => audienceProjectionState != null;
        public MeshRenderer GridOverlayRenderer => gridOverlayRenderer;

        public void Bind(
            M3MapEditorFacade nextEditor,
            M3LayerEditState nextLayerEditState,
            IMapVisualCatalog nextVisualCatalog = null)
        {
            editor = nextEditor ?? throw new ArgumentNullException(nameof(nextEditor));
            layerEditState = nextLayerEditState ?? throw new ArgumentNullException(nameof(nextLayerEditState));
            visualCatalog = nextVisualCatalog ?? visualCatalog ?? new M7BuiltinMapVisualCatalog();
            DiscoverTilemaps();
            EnsureTileResources();
            EnsureGridOverlay();
            RefreshAll();
        }

        public void SetAudienceProjection(M1WorldState nextState)
        {
            audienceProjectionState = nextState;
            RefreshAll();
        }

        public void RefreshAll()
        {
            var state = ViewState;
            if (state == null || state.map == null)
            {
                return;
            }

            DiscoverTilemaps();
            EnsureTileResources();
            EnsureGridOverlay();
            UpdateGridOverlay(state.map.width, state.map.height);
            foreach (var tilemap in tilemaps.Values)
            {
                tilemap.ClearAllTiles();
            }
            if (mapObjectTilemap != null)
            {
                mapObjectTilemap.ClearAllTiles();
            }

            foreach (var cell in state.map.cells)
            {
                if (cell == null || string.IsNullOrWhiteSpace(cell.contentId))
                {
                    continue;
                }

                var layerId = M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId);
                if (!tilemaps.TryGetValue(layerId, out var tilemap))
                {
                    continue;
                }

                tilemap.SetTile(new Vector3Int(cell.x, cell.y, 0), GetTile(cell.contentId, layerId));
            }

            if (mapObjectTilemap != null && state.map.objects != null)
            {
                foreach (var mapObject in state.map.objects)
                {
                    if (mapObject != null)
                    {
                        mapObjectTilemap.SetTile(new Vector3Int(mapObject.x, mapObject.y, 0), mapObjectTile);
                    }
                }
            }

            ApplyLayerState();
        }

        public void RefreshRegion(M3GridBounds region)
        {
            if (region.IsEmpty)
            {
                RefreshAll();
                return;
            }

            var state = ViewState;
            if (state == null || state.map == null)
            {
                return;
            }

            DiscoverTilemaps();
            EnsureTileResources();
            EnsureGridOverlay();
            UpdateGridOverlay(state.map.width, state.map.height);
            var bounds = new BoundsInt(
                region.MinX,
                region.MinY,
                0,
                region.Width,
                region.Height,
                1);
            foreach (var tilemap in tilemaps.Values)
            {
                ClearRegion(tilemap, bounds);
            }

            if (mapObjectTilemap != null)
            {
                ClearRegion(mapObjectTilemap, bounds);
            }

            foreach (var cell in state.map.cells)
            {
                if (cell == null || !region.Contains(cell.x, cell.y))
                {
                    continue;
                }

                var layerId = M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId);
                if (tilemaps.TryGetValue(layerId, out var tilemap))
                {
                    tilemap.SetTile(new Vector3Int(cell.x, cell.y, 0), GetTile(cell.contentId, layerId));
                }
            }

            if (mapObjectTilemap != null && state.map.objects != null)
            {
                foreach (var mapObject in state.map.objects)
                {
                    if (mapObject != null && region.Contains(mapObject.x, mapObject.y))
                    {
                        mapObjectTilemap.SetTile(new Vector3Int(mapObject.x, mapObject.y, 0), mapObjectTile);
                    }
                }
            }

            ApplyLayerState();
        }

        private M1WorldState ViewState => audienceProjectionState ?? (editor == null ? null : editor.State);

        private static void ClearRegion(Tilemap tilemap, BoundsInt bounds)
        {
            for (var x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (var y = bounds.yMin; y < bounds.yMax; y++)
                {
                    tilemap.SetTile(new Vector3Int(x, y, 0), null);
                }
            }
        }

        public void ApplyLayerState()
        {
            if (layerEditState == null)
            {
                return;
            }

            foreach (var layerId in LayerIds)
            {
                if (!tilemaps.TryGetValue(layerId, out var tilemap))
                {
                    continue;
                }

                var renderer = tilemap.GetComponent<TilemapRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = layerEditState.IsVisible(layerId);
                }
            }

            if (mapObjectTilemap != null)
            {
                var renderer = mapObjectTilemap.GetComponent<TilemapRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = layerEditState.IsVisible(M3MapLayerIds.Object);
                }
            }

            ApplyGridVisibility();
        }

        public void SetGridVisible(bool visible)
        {
            gridOverlayVisible = visible;
            ApplyGridVisibility();
        }

        private void EnsureGridOverlay()
        {
            if (gridOverlayRenderer != null)
            {
                return;
            }

            var child = transform.Find("WorkbenchGridOverlay");
            if (child == null)
            {
                var gridObject = new GameObject("WorkbenchGridOverlay");
                gridObject.transform.SetParent(transform, false);
                child = gridObject.transform;
            }

            gridOverlayFilter = child.GetComponent<MeshFilter>();
            if (gridOverlayFilter == null)
            {
                gridOverlayFilter = child.gameObject.AddComponent<MeshFilter>();
            }

            gridOverlayRenderer = child.GetComponent<MeshRenderer>();
            if (gridOverlayRenderer == null)
            {
                gridOverlayRenderer = child.gameObject.AddComponent<MeshRenderer>();
            }

            if (gridOverlayMesh == null)
            {
                gridOverlayMesh = new Mesh { name = "SundollWorld.WorkbenchGridMesh" };
                gridOverlayMesh.MarkDynamic();
                gridOverlayFilter.sharedMesh = gridOverlayMesh;
            }

            if (gridOverlayMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    gridOverlayMaterial = new Material(shader)
                    {
                        name = "SundollWorld.WorkbenchGridMaterial",
                        color = new Color(0.15f, 0.12f, 0.09f, 0.34f)
                    };
                    gridOverlayRenderer.sharedMaterial = gridOverlayMaterial;
                }
            }

            gridOverlayRenderer.sortingOrder = 70;
            gridOverlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gridOverlayRenderer.receiveShadows = false;
        }

        private void UpdateGridOverlay(int width, int height)
        {
            if (gridOverlayMesh == null || width <= 0 || height <= 0 ||
                (gridOverlayWidth == width && gridOverlayHeight == height))
            {
                ApplyGridVisibility();
                return;
            }

            var vertices = new List<Vector3>((width + height + 2) * 2);
            var indices = new List<int>((width + height + 2) * 2);
            for (var x = 0; x <= width; x++)
            {
                var index = vertices.Count;
                vertices.Add(new Vector3(x - 0.5f, -0.5f, -0.08f));
                vertices.Add(new Vector3(x - 0.5f, height - 0.5f, -0.08f));
                indices.Add(index);
                indices.Add(index + 1);
            }

            for (var y = 0; y <= height; y++)
            {
                var index = vertices.Count;
                vertices.Add(new Vector3(-0.5f, y - 0.5f, -0.08f));
                vertices.Add(new Vector3(width - 0.5f, y - 0.5f, -0.08f));
                indices.Add(index);
                indices.Add(index + 1);
            }

            gridOverlayMesh.Clear();
            gridOverlayMesh.SetVertices(vertices);
            gridOverlayMesh.SetIndices(indices, MeshTopology.Lines, 0, false);
            gridOverlayMesh.RecalculateBounds();
            gridOverlayWidth = width;
            gridOverlayHeight = height;
            ApplyGridVisibility();
        }

        private void ApplyGridVisibility()
        {
            if (gridOverlayRenderer != null)
            {
                gridOverlayRenderer.enabled = gridOverlayVisible && !IsAudienceProjectionActive;
            }
        }

        private void DiscoverTilemaps()
        {
            foreach (var layerId in LayerIds)
            {
                var child = transform.Find(layerId);
                if (child == null)
                {
                    var layerObject = new GameObject(layerId);
                    layerObject.transform.SetParent(transform, false);
                    layerObject.AddComponent<TilemapRenderer>();
                    child = layerObject.transform;
                }

                var tilemap = child.GetComponent<Tilemap>();
                if (tilemap == null)
                {
                    tilemap = child.gameObject.AddComponent<Tilemap>();
                }

                var renderer = child.GetComponent<TilemapRenderer>();
                if (renderer == null)
                {
                    renderer = child.gameObject.AddComponent<TilemapRenderer>();
                }

                renderer.sortingOrder = M3MapLayerIds.RenderPriority(layerId) * 10;
                tilemap.color = Color.white;
                tilemaps[layerId] = tilemap;
            }

            var objectChild = transform.Find("map-objects");
            if (objectChild == null)
            {
                var objectLayer = new GameObject("map-objects");
                objectLayer.transform.SetParent(transform, false);
                objectChild = objectLayer.transform;
            }

            mapObjectTilemap = objectChild.GetComponent<Tilemap>();
            if (mapObjectTilemap == null)
            {
                mapObjectTilemap = objectChild.gameObject.AddComponent<Tilemap>();
            }

            var objectRenderer = objectChild.GetComponent<TilemapRenderer>();
            if (objectRenderer == null)
            {
                objectRenderer = objectChild.gameObject.AddComponent<TilemapRenderer>();
            }

            objectRenderer.sortingOrder = 60;
            mapObjectTilemap.color = new Color(0.92f, 0.76f, 0.25f, 1f);
        }

        private void EnsureTileResources()
        {
            if (tileSprite == null)
            {
                tileTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "SundollWorld.WorkbenchTileTexture",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                tileTexture.SetPixel(0, 0, Color.white);
                tileTexture.Apply(false, true);
                tileSprite = Sprite.Create(
                    tileTexture,
                    new Rect(0f, 0f, 1f, 1f),
                    new Vector2(0.5f, 0.5f),
                    1f);
                tileSprite.name = "SundollWorld.WorkbenchTileSprite";
            }

            foreach (var layerId in LayerIds)
            {
                var fallbackKey = FallbackKey(layerId);
                if (tiles.ContainsKey(fallbackKey))
                {
                    continue;
                }

                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.name = "SundollWorld.WorkbenchTile." + layerId;
                tile.sprite = tileSprite;
                tile.color = LayerColor(layerId);
                tiles.Add(fallbackKey, tile);
            }

            if (visualCatalog != null)
            {
                foreach (var definition in visualCatalog.Definitions)
                {
                    if (definition == null || string.IsNullOrWhiteSpace(definition.contentId) ||
                        tiles.ContainsKey(definition.contentId))
                    {
                        continue;
                    }

                    var texture = visualCatalog.CreateTexture(definition);
                    var sprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        texture.width);
                    sprite.name = "SundollWorld.MapVisualSprite." + definition.contentId;
                    var tile = ScriptableObject.CreateInstance<Tile>();
                    tile.name = "SundollWorld.MapVisualTile." + definition.contentId;
                    tile.sprite = sprite;
                    visualTextures.Add(definition.contentId, texture);
                    visualSprites.Add(definition.contentId, sprite);
                    tiles.Add(definition.contentId, tile);
                }
            }

            if (mapObjectTile == null)
            {
                mapObjectTile = ScriptableObject.CreateInstance<Tile>();
                mapObjectTile.name = "SundollWorld.WorkbenchTile.MapObject";
                mapObjectTile.sprite = tileSprite;
            }
        }

        private Tile GetTile(string contentId, string layerId)
        {
            if (!string.IsNullOrWhiteSpace(contentId) && tiles.TryGetValue(contentId, out var tile))
            {
                return tile;
            }

            return tiles[FallbackKey(layerId)];
        }

        private static string FallbackKey(string layerId)
        {
            return "fallback:" + M3MapLayerIds.NormalizeLayerId(layerId, null);
        }

        private static Color LayerColor(string layerId)
        {
            switch (layerId)
            {
                case M3MapLayerIds.Terrain:
                    return new Color(0.28f, 0.62f, 0.38f, 1f);
                case M3MapLayerIds.Wall:
                    return new Color(0.72f, 0.32f, 0.24f, 1f);
                case M3MapLayerIds.Object:
                    return new Color(0.28f, 0.46f, 0.78f, 1f);
                case M3MapLayerIds.Interaction:
                    return new Color(0.82f, 0.66f, 0.22f, 1f);
                default:
                    return new Color(0.72f, 0.42f, 0.76f, 1f);
            }
        }

        private void OnDestroy()
        {
            foreach (var tile in tiles.Values)
            {
                if (tile != null)
                {
                    Destroy(tile);
                }
            }

            foreach (var sprite in visualSprites.Values)
            {
                if (sprite != null)
                {
                    Destroy(sprite);
                }
            }

            foreach (var texture in visualTextures.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }

            if (mapObjectTile != null)
            {
                Destroy(mapObjectTile);
            }

            if (tileSprite != null)
            {
                Destroy(tileSprite);
            }

            if (tileTexture != null)
            {
                Destroy(tileTexture);
            }

            if (gridOverlayMesh != null)
            {
                Destroy(gridOverlayMesh);
            }

            if (gridOverlayMaterial != null)
            {
                Destroy(gridOverlayMaterial);
            }
        }
    }
}
