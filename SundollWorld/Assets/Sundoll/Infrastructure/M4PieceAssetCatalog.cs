using System;
using System.IO;
using Sundoll.Core;

namespace Sundoll.Infrastructure
{
    /// <summary>
    /// Stores user-provided piece images as content-addressed blobs and returns
    /// a pure-data asset record. Thumbnail bytes are optional; Unity-side image
    /// decoding stays outside the persistence layer.
    /// </summary>
    public sealed class M4PieceAssetCatalog
    {
        private readonly M2ContentBlobStore blobStore;

        public M4PieceAssetCatalog(string projectRoot)
        {
            blobStore = new M2ContentBlobStore(projectRoot);
        }

        public M4PieceAsset Import(
            byte[] imageBytes,
            string extension,
            string mimeType,
            byte[] thumbnailBytes = null,
            string thumbnailExtension = "png",
            string thumbnailMimeType = "image/png")
        {
            if (imageBytes == null || imageBytes.Length == 0)
            {
                throw new InvalidDataException("Piece image bytes are required.");
            }

            var asset = blobStore.PutAsset(imageBytes, extension, mimeType);
            var thumbnail = thumbnailBytes == null || thumbnailBytes.Length == 0
                ? null
                : blobStore.PutThumbnail(thumbnailBytes, thumbnailExtension, thumbnailMimeType);

            return new M4PieceAsset
            {
                id = "asset-" + asset.sha256,
                sha256 = asset.sha256,
                extension = asset.extension,
                mimeType = asset.mimeType,
                byteLength = asset.byteLength,
                relativePath = asset.relativePath,
                thumbnailSha256 = thumbnail == null ? null : thumbnail.sha256,
                thumbnailRelativePath = thumbnail == null ? null : thumbnail.relativePath
            };
        }

        public bool IsAssetAvailable(M4PieceAsset asset)
        {
            if (asset == null)
            {
                return false;
            }

            var content = new M2ContentRef
            {
                sha256 = asset.sha256,
                extension = asset.extension,
                mimeType = asset.mimeType,
                byteLength = asset.byteLength,
                relativePath = asset.relativePath,
                kind = "asset"
            };
            return blobStore.TryResolve(content, out _);
        }

        public bool IsThumbnailAvailable(M4PieceAsset asset)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.thumbnailSha256))
            {
                return false;
            }

            var extension = Path.GetExtension(asset.thumbnailRelativePath);
            var content = new M2ContentRef
            {
                sha256 = asset.thumbnailSha256,
                extension = extension,
                mimeType = "image/png",
                byteLength = 0,
                relativePath = asset.thumbnailRelativePath,
                kind = "thumbnail"
            };

            // The content store validates length as well, so read the file's
            // length from the recorded path only after validating its hash.
            if (!File.Exists(Path.Combine(blobStore.RootPath, asset.thumbnailRelativePath ?? string.Empty)))
            {
                return false;
            }

            content.byteLength = new FileInfo(Path.Combine(blobStore.RootPath, asset.thumbnailRelativePath)).Length;
            return blobStore.TryResolve(content, out _);
        }
    }
}
