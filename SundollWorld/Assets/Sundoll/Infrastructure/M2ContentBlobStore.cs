using System;
using System.IO;
using System.Text;

namespace Sundoll.Infrastructure
{
    public sealed class M2ContentBlobStore
    {
        public const long MaxBlobBytes = 64L * 1024L * 1024L;

        private readonly string rootPath;

        public M2ContentBlobStore(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            rootPath = projectRoot;
            M2FileIO.EnsureDirectory(AssetsPath);
            M2FileIO.EnsureDirectory(ThumbnailsPath);
        }

        public string AssetsPath => Path.Combine(rootPath, "assets");
        public string ThumbnailsPath => Path.Combine(rootPath, "thumbnails");
        public string RootPath => rootPath;

        public M2ContentRef PutAsset(byte[] bytes, string extension, string mimeType)
        {
            return Put(bytes, extension, mimeType, "asset");
        }

        public M2ContentRef PutThumbnail(byte[] bytes, string extension, string mimeType)
        {
            return Put(bytes, extension, mimeType, "thumbnail");
        }

        public bool TryResolve(M2ContentRef content, out string absolutePath)
        {
            absolutePath = null;
            if (content == null || !IsValidHash(content.sha256))
            {
                return false;
            }

            var extension = NormalizeExtension(content.extension);
            var kind = content.kind == "thumbnail" ? "thumbnail" : content.kind == "asset" ? "asset" : null;
            if (kind == null)
            {
                return false;
            }

            var directory = kind == "thumbnail" ? ThumbnailsPath : AssetsPath;
            var candidate = Path.Combine(directory, content.sha256 + "." + extension);
            if (!File.Exists(candidate) || new FileInfo(candidate).Length != content.byteLength)
            {
                return false;
            }

            if (!string.Equals(M2FileIO.Sha256File(candidate), content.sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            absolutePath = candidate;
            return true;
        }

        public byte[] Read(M2ContentRef content)
        {
            if (!TryResolve(content, out var absolutePath))
            {
                throw new FileNotFoundException("Content blob is missing or failed hash validation.");
            }

            return File.ReadAllBytes(absolutePath);
        }

        private M2ContentRef Put(byte[] bytes, string extension, string mimeType, string kind)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (bytes.LongLength > MaxBlobBytes)
            {
                throw new InvalidDataException("Content blob exceeds the M2 size limit.");
            }

            extension = NormalizeExtension(extension);
            var hash = M2FileIO.Sha256(bytes);
            var directory = kind == "thumbnail" ? ThumbnailsPath : AssetsPath;
            var path = Path.Combine(directory, hash + "." + extension);
            if (!File.Exists(path))
            {
                M2FileIO.WriteBytesAtomic(path, bytes);
            }
            else if (!string.Equals(M2FileIO.Sha256File(path), hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("A content-addressed file exists with a different hash.");
            }

            return new M2ContentRef
            {
                sha256 = hash,
                extension = extension,
                mimeType = mimeType ?? "application/octet-stream",
                byteLength = bytes.LongLength,
                relativePath = (kind == "thumbnail" ? "thumbnails/" : "assets/") + hash + "." + extension,
                kind = kind
            };
        }

        private static string NormalizeExtension(string extension)
        {
            extension = string.IsNullOrWhiteSpace(extension) ? "bin" : extension.Trim().TrimStart('.').ToLowerInvariant();
            if (extension.Length > 8)
            {
                throw new ArgumentException("Content extension is too long.", nameof(extension));
            }

            foreach (var character in extension)
            {
                if (!(character >= 'a' && character <= 'z') && !(character >= '0' && character <= '9'))
                {
                    throw new ArgumentException("Content extension contains an unsafe character.", nameof(extension));
                }
            }

            return extension;
        }

        private static bool IsValidHash(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            foreach (var character in value)
            {
                var isHex = (character >= '0' && character <= '9') ||
                            (character >= 'a' && character <= 'f') ||
                            (character >= 'A' && character <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
