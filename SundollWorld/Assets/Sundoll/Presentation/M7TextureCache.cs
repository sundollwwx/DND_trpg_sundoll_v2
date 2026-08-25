using System;
using System.Collections.Generic;
using Sundoll.Core;
using Sundoll.Infrastructure;
using UnityEngine;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Reference-counted runtime texture cache. Missing or corrupt blobs never
    /// invalidate the piece definition; callers keep using their placeholder.
    /// </summary>
    public sealed class M7TextureCache : IDisposable
    {
        private sealed class Entry
        {
            public Texture2D texture;
            public int references;
        }

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public int Count => entries.Count;

        public bool TryAcquire(M4PieceAsset asset, M4PieceAssetCatalog catalog, out Texture2D texture, out string diagnostic)
        {
            texture = null;
            diagnostic = string.Empty;
            if (asset == null || catalog == null || string.IsNullOrWhiteSpace(asset.id))
            {
                diagnostic = "Texture asset or catalog is missing.";
                return false;
            }

            if (entries.TryGetValue(asset.id, out var cached) && cached.texture != null)
            {
                cached.references++;
                texture = cached.texture;
                return true;
            }

            if (!catalog.TryReadAssetBytes(asset, out var bytes))
            {
                diagnostic = "Texture blob is missing or failed hash validation.";
                return false;
            }

            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(loaded, bytes, false))
            {
                DestroyTexture(loaded);
                diagnostic = "Texture blob could not be decoded.";
                return false;
            }

            loaded.name = "SundollWorld.Texture." + asset.id;
            loaded.wrapMode = TextureWrapMode.Clamp;
            entries[asset.id] = new Entry { texture = loaded, references = 1 };
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
            if (entry.references != 0)
            {
                return;
            }

            DestroyTexture(entry.texture);
            entries.Remove(assetId);
        }

        public void Clear()
        {
            foreach (var entry in entries.Values)
            {
                DestroyTexture(entry.texture);
            }

            entries.Clear();
        }

        public void Dispose()
        {
            Clear();
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
