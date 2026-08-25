using System;
using System.Collections.Generic;
using Sundoll.Core;
using UnityEngine;

namespace Sundoll.Presentation
{
    public enum MapVisualPattern
    {
        Solid = 0,
        Checker = 1,
        Brick = 2,
        Plank = 3,
        Wave = 4,
        Diagonal = 5,
        Marker = 6,
        Cross = 7
    }

    public sealed class MapVisualDefinition
    {
        public string contentId;
        public string layerId;
        public string displayName;
        public Color primaryColor;
        public Color detailColor;
        public MapVisualPattern pattern;
    }

    public interface IMapVisualCatalog
    {
        IReadOnlyList<MapVisualDefinition> Definitions { get; }
        bool TryGet(string contentId, out MapVisualDefinition definition);
        IReadOnlyList<MapVisualDefinition> GetForLayer(string layerId);
        string GetDefaultContentId(string layerId);
        Texture2D CreateTexture(MapVisualDefinition definition);
    }

    /// <summary>
    /// Small first-party visual catalogue used by the Alpha map palette. The
    /// stable content IDs are saved; these procedural textures are disposable
    /// presentation assets and never become a second world-state authority.
    /// </summary>
    public sealed class M7BuiltinMapVisualCatalog : IMapVisualCatalog
    {
        public const int TextureSize = 64;
        private readonly List<MapVisualDefinition> definitions = new List<MapVisualDefinition>();
        private readonly Dictionary<string, MapVisualDefinition> byId =
            new Dictionary<string, MapVisualDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<MapVisualDefinition>> byLayer =
            new Dictionary<string, List<MapVisualDefinition>>(StringComparer.Ordinal);

        public M7BuiltinMapVisualCatalog()
        {
            AddTerrain("terrain-ground", "羊皮地面", "#C9B48A", "#A58B61", MapVisualPattern.Solid);
            AddTerrain("terrain-stone", "旧石地面", "#8E8A80", "#5E5B56", MapVisualPattern.Brick);
            AddTerrain("terrain-wood", "木板地面", "#9B7048", "#563A29", MapVisualPattern.Plank);
            AddTerrain("terrain-grass", "草地", "#667D4E", "#3D5132", MapVisualPattern.Diagonal);
            AddTerrain("terrain-earth", "泥土地", "#806146", "#503B2D", MapVisualPattern.Checker);
            AddTerrain("terrain-sand", "沙地", "#C2A66A", "#8F7747", MapVisualPattern.Diagonal);
            AddTerrain("terrain-water", "水面", "#477A8F", "#274D62", MapVisualPattern.Wave);
            AddTerrain("terrain-void", "虚空", "#222832", "#10151C", MapVisualPattern.Checker);

            Add("wall-solid", M3MapLayerIds.Wall, "石墙", "#665D54", "#2E2A27", MapVisualPattern.Brick);
            Add("wall-brick", M3MapLayerIds.Wall, "砖墙", "#805449", "#412D29", MapVisualPattern.Brick);
            Add("wall-wood", M3MapLayerIds.Wall, "木墙", "#725039", "#35261E", MapVisualPattern.Plank);
            Add("wall-ruin", M3MapLayerIds.Wall, "残墙", "#77736B", "#353432", MapVisualPattern.Diagonal);

            Add("object-marker", M3MapLayerIds.Object, "物件标记", "#547394", "#24394F", MapVisualPattern.Marker);
            Add("object-crate", M3MapLayerIds.Object, "木箱", "#90643D", "#3F2B20", MapVisualPattern.Cross);
            Add("object-table", M3MapLayerIds.Object, "桌子", "#7A553A", "#35261E", MapVisualPattern.Plank);
            Add("object-pillar", M3MapLayerIds.Object, "石柱", "#99958B", "#4C4A46", MapVisualPattern.Marker);
            Add("object-stairs", M3MapLayerIds.Object, "楼梯", "#7D7B75", "#3E3D3A", MapVisualPattern.Diagonal);
            Add("object-torch", M3MapLayerIds.Object, "火炬", "#D38A3A", "#6B3425", MapVisualPattern.Marker);

            Add("interaction-trigger", M3MapLayerIds.Interaction, "触发区域", "#C19A4E", "#654B22", MapVisualPattern.Cross);
            Add("interaction-trap", M3MapLayerIds.Interaction, "陷阱", "#B95E4C", "#5B2C28", MapVisualPattern.Cross);
            Add("interaction-secret", M3MapLayerIds.Interaction, "秘密", "#75649D", "#362D51", MapVisualPattern.Marker);
            Add("interaction-entry", M3MapLayerIds.Interaction, "入口", "#4F8E78", "#21483B", MapVisualPattern.Marker);

            Add("annotation-note", M3MapLayerIds.StaticAnnotation, "备注", "#CBAA61", "#624A22", MapVisualPattern.Marker);
            Add("annotation-danger", M3MapLayerIds.StaticAnnotation, "危险", "#C15C55", "#5E2928", MapVisualPattern.Cross);
            Add("annotation-objective", M3MapLayerIds.StaticAnnotation, "目标", "#5E8DB1", "#27445D", MapVisualPattern.Marker);
            Add("annotation-start", M3MapLayerIds.StaticAnnotation, "起点", "#62946A", "#294D32", MapVisualPattern.Marker);
            Add("annotation-number", M3MapLayerIds.StaticAnnotation, "编号", "#8A76A6", "#433553", MapVisualPattern.Checker);
        }

        public IReadOnlyList<MapVisualDefinition> Definitions => definitions;

        public bool TryGet(string contentId, out MapVisualDefinition definition)
        {
            definition = null;
            return !string.IsNullOrWhiteSpace(contentId) && byId.TryGetValue(contentId, out definition);
        }

        public IReadOnlyList<MapVisualDefinition> GetForLayer(string layerId)
        {
            layerId = M3MapLayerIds.NormalizeLayerId(layerId, null);
            return byLayer.TryGetValue(layerId, out var values)
                ? values
                : Array.Empty<MapVisualDefinition>();
        }

        public string GetDefaultContentId(string layerId)
        {
            var values = GetForLayer(layerId);
            return values.Count == 0 ? null : values[0].contentId;
        }

        public Texture2D CreateTexture(MapVisualDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = "SundollWorld.MapVisual." + definition.contentId,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var primary = (Color32)definition.primaryColor;
            var detail = (Color32)definition.detailColor;
            for (var y = 0; y < TextureSize; y++)
            {
                for (var x = 0; x < TextureSize; x++)
                {
                    var color = PatternColor(definition.pattern, x, y, primary, detail);
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply(false, true);
            return texture;
        }

        private void AddTerrain(string id, string name, string primary, string detail, MapVisualPattern pattern)
        {
            Add(id, M3MapLayerIds.Terrain, name, primary, detail, pattern);
        }

        private void Add(string id, string layer, string name, string primary, string detail, MapVisualPattern pattern)
        {
            var definition = new MapVisualDefinition
            {
                contentId = id,
                layerId = layer,
                displayName = name,
                primaryColor = ParseColor(primary),
                detailColor = ParseColor(detail),
                pattern = pattern
            };
            definitions.Add(definition);
            byId.Add(id, definition);
            if (!byLayer.TryGetValue(layer, out var values))
            {
                values = new List<MapVisualDefinition>();
                byLayer.Add(layer, values);
            }

            values.Add(definition);
        }

        private static Color32 PatternColor(
            MapVisualPattern pattern,
            int x,
            int y,
            Color32 primary,
            Color32 detail)
        {
            var border = x < 2 || y < 2 || x >= TextureSize - 2 || y >= TextureSize - 2;
            if (border)
            {
                return Blend(primary, detail, 0.62f);
            }

            var useDetail = false;
            switch (pattern)
            {
                case MapVisualPattern.Checker:
                    useDetail = ((x / 16) + (y / 16)) % 2 == 0;
                    break;
                case MapVisualPattern.Brick:
                    useDetail = y % 16 < 2 ||
                                x % 24 < 2 && (y / 16) % 2 == 0 ||
                                (x + 12) % 24 < 2 && (y / 16) % 2 != 0;
                    break;
                case MapVisualPattern.Plank:
                    useDetail = x % 16 < 2 || y % 32 < 1 ||
                                (x % 16 == 8 && y % 28 == 8);
                    break;
                case MapVisualPattern.Wave:
                    useDetail = Math.Abs((y + (int)(Math.Sin(x * 0.18f) * 4f)) % 18) < 2;
                    break;
                case MapVisualPattern.Diagonal:
                    useDetail = (x + y) % 22 < 2;
                    break;
                case MapVisualPattern.Marker:
                    var dx = x - TextureSize / 2;
                    var dy = y - TextureSize / 2;
                    var distance = dx * dx + dy * dy;
                    useDetail = distance < 130 || distance > 680 && distance < 820;
                    break;
                case MapVisualPattern.Cross:
                    useDetail = Math.Abs(x - y) < 4 || Math.Abs((TextureSize - 1 - x) - y) < 4;
                    break;
            }

            if (useDetail)
            {
                return Blend(primary, detail, 0.78f);
            }

            // Very restrained deterministic grain keeps large flat regions from
            // looking like debug colors without introducing runtime randomness.
            return (x * 17 + y * 31) % 47 == 0
                ? Blend(primary, detail, 0.16f)
                : primary;
        }

        private static Color32 Blend(Color32 first, Color32 second, float amount)
        {
            return new Color32(
                (byte)Mathf.RoundToInt(Mathf.Lerp(first.r, second.r, amount)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(first.g, second.g, amount)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(first.b, second.b, amount)),
                255);
        }

        private static Color ParseColor(string value)
        {
            return ColorUtility.TryParseHtmlString(value, out var color) ? color : Color.magenta;
        }
    }
}
