using System;
using System.Collections.Generic;
using Sundoll.Core;
using Sundoll.Application;
using UnityEngine;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Lightweight M5 host-state projection. Fog and annotations are derived
    /// from the pure world state and can be rebuilt after a view is destroyed.
    /// </summary>
    public sealed class M5WorkbenchConsoleProjection : MonoBehaviour
    {
        private readonly Dictionary<string, GameObject> fogViews = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> annotationViews = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private M1CommandBus commandBus;
        private Sprite fogSprite;
        private Texture2D fogTexture;

        public IReadOnlyDictionary<string, GameObject> FogViews => fogViews;
        public IReadOnlyDictionary<string, GameObject> AnnotationViews => annotationViews;

        public void Bind(M1CommandBus nextCommandBus)
        {
            commandBus = nextCommandBus ?? throw new ArgumentNullException(nameof(nextCommandBus));
            EnsureFogSprite();
            RefreshAll();
        }

        public void RefreshAll()
        {
            if (commandBus == null || commandBus.State == null || commandBus.State.m5Console == null)
            {
                return;
            }

            var state = commandBus.State;
            var console = state.m5Console;
            var mapId = string.IsNullOrWhiteSpace(console.activeMapId) ? state.map == null ? null : state.map.id : console.activeMapId;
            var activeFog = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fog in console.fogCells ?? new List<M5FogCell>())
            {
                if (fog == null || fog.mapId != mapId || fog.revealed)
                {
                    continue;
                }

                var id = "fog-" + fog.x + "-" + fog.y;
                activeFog.Add(id);
                if (!fogViews.TryGetValue(id, out var view) || view == null)
                {
                    view = new GameObject("FogView-" + fog.x + "-" + fog.y);
                    view.transform.SetParent(transform, false);
                    var renderer = view.AddComponent<SpriteRenderer>();
                    renderer.sprite = fogSprite;
                    renderer.color = new Color(0.01f, 0.015f, 0.025f, 0.78f);
                    renderer.sortingOrder = 400;
                    // The runtime 1x1 fog sprite is a simple full-cell quad;
                    // Sliced mode emits a Full Rect import warning for it.
                    renderer.drawMode = SpriteDrawMode.Simple;
                    view.transform.localScale = Vector3.one * 0.98f;
                    fogViews[id] = view;
                }

                view.transform.position = new Vector3(fog.x, fog.y, -0.2f);
            }

            RemoveStaleViews(fogViews, activeFog);

            var activeAnnotations = new HashSet<string>(StringComparer.Ordinal);
            foreach (var annotation in console.annotations ?? new List<M5DynamicAnnotation>())
            {
                if (annotation == null || annotation.mapId != mapId || !annotation.visible)
                {
                    continue;
                }

                activeAnnotations.Add(annotation.id);
                if (!annotationViews.TryGetValue(annotation.id, out var view) || view == null)
                {
                    view = new GameObject("AnnotationView-" + annotation.id);
                    view.transform.SetParent(transform, false);
                    var text = view.AddComponent<TextMesh>();
                    text.anchor = TextAnchor.MiddleCenter;
                    text.alignment = TextAlignment.Center;
                    text.characterSize = 0.18f;
                    text.fontSize = 48;
                    text.color = Color.white;
                    text.GetComponent<Renderer>().sortingOrder = 450;
                    annotationViews[annotation.id] = view;
                }

                var textMesh = view.GetComponent<TextMesh>();
                textMesh.text = annotation.text ?? string.Empty;
                view.transform.position = new Vector3(annotation.x, annotation.y + 0.32f, -0.4f);
            }

            RemoveStaleViews(annotationViews, activeAnnotations);
        }

        private static void RemoveStaleViews(Dictionary<string, GameObject> views, HashSet<string> activeIds)
        {
            var stale = new List<string>();
            foreach (var pair in views)
            {
                if (!activeIds.Contains(pair.Key))
                {
                    stale.Add(pair.Key);
                }
            }

            foreach (var id in stale)
            {
                if (views[id] != null)
                {
                    Destroy(views[id]);
                }

                views.Remove(id);
            }
        }

        private void EnsureFogSprite()
        {
            if (fogSprite != null)
            {
                return;
            }

            fogTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "SundollWorld.M5FogTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            fogTexture.SetPixel(0, 0, Color.white);
            fogTexture.Apply(false, true);
            fogSprite = Sprite.Create(fogTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            fogSprite.name = "SundollWorld.M5FogSprite";
        }

        private void OnDestroy()
        {
            foreach (var view in fogViews.Values)
            {
                if (view != null) Destroy(view);
            }

            foreach (var view in annotationViews.Values)
            {
                if (view != null) Destroy(view);
            }

            fogViews.Clear();
            annotationViews.Clear();
            if (fogSprite != null) Destroy(fogSprite);
            if (fogTexture != null) Destroy(fogTexture);
        }
    }
}
