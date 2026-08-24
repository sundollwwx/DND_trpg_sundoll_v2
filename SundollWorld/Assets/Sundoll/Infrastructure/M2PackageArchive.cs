using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using ZipCompressionLevel = System.IO.Compression.CompressionLevel;

namespace Sundoll.Infrastructure
{
    public static class M2PackageArchive
    {
        private const int MaxEntries = 4096;
        private const long MaxUncompressedBytes = 256L * 1024L * 1024L;

        public static string Export(M2ProjectStore store, string packagePath)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (string.IsNullOrWhiteSpace(packagePath))
            {
                throw new ArgumentException("Package path is required.", nameof(packagePath));
            }

            var loaded = store.LoadActive();
            var directory = Path.GetDirectoryName(packagePath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Package path must include a directory.");
            }

            M2FileIO.EnsureDirectory(directory);
            var temporaryPath = packagePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                var packageManifest = new M2PackageManifest
                {
                    saveRevisionId = loaded.manifest.saveRevisionId,
                    canonicalStateHash = loaded.manifest.canonicalStateHash
                };
                using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, false, new UTF8Encoding(false)))
                {
                    AddFile(archive, packageManifest.entries, "HEAD.json", store.HeadPath);
                    var revisionDirectory = store.GetRevisionDirectory(loaded.manifest.saveRevisionId);
                    AddFile(archive, packageManifest.entries, "revisions/" + loaded.manifest.saveRevisionId + "/revision-manifest.json", Path.Combine(revisionDirectory, "revision-manifest.json"));
                    AddFile(archive, packageManifest.entries, "revisions/" + loaded.manifest.saveRevisionId + "/project.json", Path.Combine(revisionDirectory, "project.json"));

                    foreach (var file in store.GetContentFiles("assets"))
                    {
                        AddFile(archive, packageManifest.entries, ToPackagePath(store.RootPath, file), file);
                    }

                    foreach (var file in store.GetContentFiles("thumbnails"))
                    {
                        AddFile(archive, packageManifest.entries, ToPackagePath(store.RootPath, file), file);
                    }

                    var manifestEntry = archive.CreateEntry("package-manifest.json", ZipCompressionLevel.Optimal);
                    using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
                    {
                        writer.Write(JsonUtility.ToJson(packageManifest, true));
                    }

                    output.Flush(true);
                }

                M2FileIO.ReplaceFile(temporaryPath, packagePath);
                return packagePath;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        public static string Import(string packagePath, string destinationRoot)
        {
            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException("M2 package was not found.", packagePath);
            }

            if (Directory.Exists(destinationRoot))
            {
                throw new IOException("Import destination already exists.");
            }

            var parent = Path.GetDirectoryName(destinationRoot);
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException("Import destination must include a directory.");
            }

            M2FileIO.EnsureDirectory(parent);
            var staging = destinationRoot + ".importing-" + Guid.NewGuid().ToString("N");
            try
            {
                M2FileIO.EnsureDirectory(staging);
                using (var input = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var archive = new ZipArchive(input, ZipArchiveMode.Read, false, new UTF8Encoding(false)))
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    long totalBytes = 0;
                    var count = 0;
                    foreach (var entry in archive.Entries)
                    {
                        if (++count > MaxEntries || !IsSafeEntryName(entry.FullName) || !seen.Add(entry.FullName))
                        {
                            throw new InvalidDataException("M2 package contains an unsafe or duplicate entry.");
                        }

                        totalBytes += entry.Length;
                        if (totalBytes > MaxUncompressedBytes)
                        {
                            throw new InvalidDataException("M2 package exceeds the uncompressed size limit.");
                        }

                        var destination = Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                        {
                            M2FileIO.EnsureDirectory(destination);
                            continue;
                        }

                        M2FileIO.EnsureDirectory(Path.GetDirectoryName(destination));
                        using (var source = entry.Open())
                        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            source.CopyTo(output);
                            output.Flush(true);
                        }
                    }
                }

                var importedStore = new M2ProjectStore(staging);
                importedStore.LoadActive();
                Directory.Move(staging, destinationRoot);
                return destinationRoot;
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            }
        }

        private static void AddFile(ZipArchive archive, List<string> entries, string packagePath, string sourcePath)
        {
            if (!IsSafeEntryName(packagePath) || !File.Exists(sourcePath))
            {
                throw new InvalidDataException("Cannot package unsafe or missing file: " + packagePath);
            }

            var entry = archive.CreateEntry(packagePath, ZipCompressionLevel.Optimal);
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var destination = entry.Open())
            {
                source.CopyTo(destination);
            }

            entries.Add(packagePath);
        }

        private static string ToPackagePath(string root, string absolutePath)
        {
            var relative = absolutePath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        private static bool IsSafeEntryName(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName) || entryName.Contains("\\") || Path.IsPathRooted(entryName) || entryName.Contains(":") || entryName.StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            var parts = entryName.Split('/');
            foreach (var part in parts)
            {
                if (part == ".." || part == "")
                {
                    return part == "" && entryName.EndsWith("/", StringComparison.Ordinal);
                }
            }

            return entryName == "HEAD.json" || entryName == "package-manifest.json" ||
                   entryName.StartsWith("revisions/", StringComparison.Ordinal) ||
                   entryName.StartsWith("assets/", StringComparison.Ordinal) ||
                   entryName.StartsWith("thumbnails/", StringComparison.Ordinal);
        }
    }
}
