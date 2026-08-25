using System;
using Sundoll.Core;
using Sundoll.Infrastructure;
using UnityEngine;

namespace Sundoll.Presentation
{
    public sealed class M4ImageImportResult
    {
        public bool accepted;
        public string diagnostic;
        public int width;
        public int height;
        public M4PieceAsset asset;
    }

    /// <summary>
    /// Unity-facing image import boundary. It validates and decodes bytes on
    /// the main thread, creates a bounded PNG thumbnail, then delegates file
    /// storage and SHA-256 deduplication to the infrastructure catalog.
    /// </summary>
    public static class M4RuntimeImageImporter
    {
        public const int MaxTextureDimension = 4096;
        public const int MaxThumbnailDimension = 128;

        public static M4ImageImportResult Import(
            M4PieceAssetCatalog catalog,
            byte[] imageBytes,
            string extension,
            string mimeType)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (imageBytes == null || imageBytes.Length == 0)
            {
                return Rejected("图片数据为空。");
            }

            if (imageBytes.LongLength > M2ContentBlobStore.MaxBlobBytes)
            {
                return Rejected("图片超过 64 MB 限制。");
            }

            Texture2D decoded = null;
            Texture2D thumbnail = null;
            try
            {
                decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(decoded, imageBytes, false))
                {
                    return Rejected("Unity 无法解码该图片。");
                }

                if (decoded.width < 1 || decoded.height < 1 ||
                    decoded.width > MaxTextureDimension || decoded.height > MaxTextureDimension)
                {
                    return Rejected("图片尺寸超过 4096×4096 限制。");
                }

                thumbnail = CreateThumbnail(decoded);
                var thumbnailBytes = thumbnail.EncodeToPNG();
                var asset = catalog.Import(
                    imageBytes,
                    extension,
                    mimeType,
                    thumbnailBytes,
                    "png",
                    "image/png");
                return new M4ImageImportResult
                {
                    accepted = true,
                    diagnostic = "图片已导入并生成缩略图。",
                    width = decoded.width,
                    height = decoded.height,
                    asset = asset
                };
            }
            catch (Exception exception)
            {
                return Rejected("图片导入失败：" + exception.Message);
            }
            finally
            {
                DestroyTexture(decoded);
                DestroyTexture(thumbnail);
            }
        }

        private static Texture2D CreateThumbnail(Texture2D source)
        {
            var scale = Mathf.Min(
                1f,
                MaxThumbnailDimension / (float)source.width,
                MaxThumbnailDimension / (float)source.height);
            var width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            var thumbnail = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var u = (x + 0.5f) / width;
                    var v = (y + 0.5f) / height;
                    thumbnail.SetPixel(x, y, source.GetPixelBilinear(u, v));
                }
            }

            thumbnail.Apply(false, false);
            return thumbnail;
        }

        private static M4ImageImportResult Rejected(string diagnostic)
        {
            return new M4ImageImportResult
            {
                accepted = false,
                diagnostic = diagnostic
            };
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
