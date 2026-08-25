using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Sundoll.Core;
using UnityEngine;

namespace Sundoll.Infrastructure
{
    public sealed class M2ProjectStore
    {
        private readonly Action<M2SaveFaultPoint> faultInjector;

        public M2ProjectStore(string projectRoot, Action<M2SaveFaultPoint> faultInjector = null)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            RootPath = projectRoot;
            this.faultInjector = faultInjector;
            EnsureLayout();
        }

        public string RootPath { get; }
        public string HeadPath => Path.Combine(RootPath, "HEAD.json");
        public string WriteLockPath => Path.Combine(RootPath, ".save.lock");
        public string RevisionsPath => Path.Combine(RootPath, "revisions");
        public string StagingPath => Path.Combine(RootPath, "staging");
        public string AssetsPath => Path.Combine(RootPath, "assets");
        public string ThumbnailsPath => Path.Combine(RootPath, "thumbnails");
        public string JournalPath => Path.Combine(RootPath, "journal");
        public int MaxRetainedRevisions { get; set; } = 10;
        public int WriteLockTimeoutMilliseconds { get; set; } = 30000;

        public bool HasHead => File.Exists(HeadPath);

        public M2SaveResult Save(
            M1WorldState state,
            string journalStreamId,
            long journalOperationSequence,
            long? expectedGeneration = null)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            using (AcquireWriteLock())
            {
                // Read HEAD and commit the immutable Revision while the same
                // cross-process lock is held. This turns expectedGeneration
                // into a real compare-and-commit boundary rather than a
                // best-effort check.
                return SaveLocked(state, journalStreamId, journalOperationSequence, expectedGeneration);
            }
        }

        private M2SaveResult SaveLocked(
            M1WorldState state,
            string journalStreamId,
            long journalOperationSequence,
            long? expectedGeneration)
        {

            var previousHead = ReadHeadOrDefault();
            if (expectedGeneration.HasValue && previousHead.generation != expectedGeneration.Value)
            {
                throw new M2GenerationConflictException(expectedGeneration.Value, previousHead.generation);
            }

            journalStreamId = string.IsNullOrWhiteSpace(journalStreamId)
                ? (string.IsNullOrWhiteSpace(previousHead.activeJournalStreamId) ? NewStreamId() : previousHead.activeJournalStreamId)
                : journalStreamId;
            if (!M2FileIO.IsSafeIdentifier(journalStreamId))
            {
                throw new InvalidDataException("Journal stream ID contains unsafe characters.");
            }

            var saveRevisionId = "rev-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            var revisionDirectory = Path.Combine(RevisionsPath, saveRevisionId);
            var stagingDirectory = Path.Combine(StagingPath, "revision-" + saveRevisionId);
            var projectJson = JsonUtility.ToJson(state, true);
            var canonicalStateHash = M2CanonicalStateHasher.Compute(state);
            var savedUtc = DateTime.UtcNow.ToString("O");

            try
            {
                M2FileIO.EnsureDirectory(stagingDirectory);
                var projectPath = Path.Combine(stagingDirectory, "project.json");
                M2FileIO.WriteUtf8Atomic(projectPath, projectJson);

                var manifest = new M2RevisionManifest
                {
                    saveRevisionId = saveRevisionId,
                    parentRevisionId = previousHead.activeSaveRevisionId,
                    journalStreamId = journalStreamId,
                    journalOperationSequence = journalOperationSequence,
                    snapshotWorldRevision = state.revision,
                    canonicalStateHash = canonicalStateHash,
                    savedUtc = savedUtc,
                    files = new List<M2FileRecord>
                    {
                        new M2FileRecord
                        {
                            relativePath = "project.json",
                            byteLength = new FileInfo(projectPath).Length,
                            sha256 = M2FileIO.Sha256File(projectPath)
                        }
                    }
                };
                M2FileIO.WriteUtf8Atomic(
                    Path.Combine(stagingDirectory, "revision-manifest.json"),
                    JsonUtility.ToJson(manifest, true));

                faultInjector?.Invoke(M2SaveFaultPoint.BeforeRevisionCommit);
                Directory.Move(stagingDirectory, revisionDirectory);
                ValidateRevision(revisionDirectory, manifest);

                var nextHead = new M2Head
                {
                    formatVersion = 1,
                    worldSchemaVersion = state.schemaVersion,
                    activeSaveRevisionId = saveRevisionId,
                    activeJournalStreamId = journalStreamId,
                    generation = previousHead.generation + 1,
                    lastKnownGoodRevisionId = saveRevisionId
                };
                faultInjector?.Invoke(M2SaveFaultPoint.BeforeHeadCommit);
                M2FileIO.WriteUtf8Atomic(HeadPath, JsonUtility.ToJson(nextHead, true));
                faultInjector?.Invoke(M2SaveFaultPoint.AfterHeadCommit);
                PruneRevisions(nextHead);

                return new M2SaveResult
                {
                    saveRevisionId = saveRevisionId,
                    parentRevisionId = manifest.parentRevisionId,
                    canonicalStateHash = canonicalStateHash,
                    headPath = HeadPath,
                    revisionPath = revisionDirectory,
                    generation = nextHead.generation
                };
            }
            finally
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, true);
                }
            }
        }

        private FileStream AcquireWriteLock()
        {
            if (WriteLockTimeoutMilliseconds < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(WriteLockTimeoutMilliseconds));
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(WriteLockTimeoutMilliseconds);
            IOException lastIOException = null;
            while (true)
            {
                try
                {
                    return new FileStream(WriteLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException exception)
                {
                    lastIOException = exception;
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new M2WriteLockTimeoutException(
                            WriteLockPath,
                            WriteLockTimeoutMilliseconds,
                            lastIOException);
                    }

                    Thread.Sleep(25);
                }
            }
        }

        public M2LoadResult LoadActive()
        {
            if (!HasHead)
            {
                throw new FileNotFoundException("M2 HEAD was not found.", HeadPath);
            }

            var head = ReadHead();
            return LoadRevision(head.activeSaveRevisionId, "HEAD", head);
        }

        public M2LoadResult LoadBestAvailable()
        {
            var diagnostics = new List<string>();
            var hadHead = HasHead;
            M2Head head = null;
            if (hadHead)
            {
                try
                {
                    head = ReadHead();
                }
                catch (FileNotFoundException exception)
                {
                    diagnostics.Add("HEAD: " + exception.Message);
                }
                catch (InvalidDataException exception)
                {
                    diagnostics.Add("HEAD: " + exception.Message);
                }
                catch (ArgumentException exception)
                {
                    diagnostics.Add("HEAD: " + exception.Message);
                }
            }
            else
            {
                diagnostics.Add("HEAD: file was not found.");
            }

            if (head != null)
            {
                foreach (var candidate in new[] { head.activeSaveRevisionId, head.lastKnownGoodRevisionId })
                {
                    if (string.IsNullOrWhiteSpace(candidate))
                    {
                        continue;
                    }

                    try
                    {
                        return LoadRevision(candidate, candidate == head.activeSaveRevisionId ? "HEAD" : "LKG", head);
                    }
                    catch (Exception exception)
                    {
                        diagnostics.Add(candidate + ": " + exception.Message);
                    }
                }
            }

            var directories = GetRevisionDirectoriesByNewest();
            foreach (var directory in directories)
            {
                var candidate = Path.GetFileName(directory);
                if (head != null && (candidate == head.activeSaveRevisionId || candidate == head.lastKnownGoodRevisionId))
                {
                    continue;
                }

                try
                {
                    var source = head == null ? "RevisionScan" : "RecoveryCandidate";
                    var candidateHead = head ?? new M2Head
                    {
                        activeSaveRevisionId = candidate,
                        lastKnownGoodRevisionId = candidate
                    };
                    var result = LoadRevision(candidate, source, candidateHead);
                    if (head == null)
                    {
                        result.head = CreateRecoveredHead(result.manifest);
                    }

                    result.diagnostic = string.Join(" | ", diagnostics.ToArray());
                    return result;
                }
                catch (Exception exception)
                {
                    diagnostics.Add(candidate + ": " + exception.Message);
                }
            }

            if (!hadHead && directories.Count == 0)
            {
                return null;
            }

            throw new InvalidDataException("No valid M2 revision could be recovered. " + string.Join(" | ", diagnostics.ToArray()));
        }

        public M2ValidationResult Validate()
        {
            try
            {
                var loaded = LoadBestAvailable();
                if (loaded == null)
                {
                    return new M2ValidationResult { valid = false, source = "None", diagnostic = "HEAD does not exist." };
                }

                return new M2ValidationResult
                {
                    valid = true,
                    source = loaded.source,
                    saveRevisionId = loaded.manifest.saveRevisionId,
                    canonicalStateHash = loaded.manifest.canonicalStateHash,
                    diagnostic = loaded.diagnostic
                };
            }
            catch (Exception exception)
            {
                return new M2ValidationResult { valid = false, source = "Invalid", diagnostic = exception.Message };
            }
        }

        public string ExportPackage(string packagePath)
        {
            return M2PackageArchive.Export(this, packagePath);
        }

        internal M2Head ReadHead()
        {
            if (!File.Exists(HeadPath))
            {
                throw new FileNotFoundException("M2 HEAD was not found.", HeadPath);
            }

            var head = JsonUtility.FromJson<M2Head>(File.ReadAllText(HeadPath, new UTF8Encoding(false)));
            if (head == null || head.formatVersion != 1 || head.generation < 0 ||
                !M2FileIO.IsSafeIdentifier(head.activeSaveRevisionId) ||
                !M2FileIO.IsSafeIdentifier(head.activeJournalStreamId) ||
                (!string.IsNullOrWhiteSpace(head.lastKnownGoodRevisionId) && !M2FileIO.IsSafeIdentifier(head.lastKnownGoodRevisionId)))
            {
                throw new InvalidDataException("M2 HEAD is invalid.");
            }

            return head;
        }

        internal M2RevisionManifest ReadManifest(string revisionId)
        {
            var directory = GetRevisionDirectory(revisionId);
            var path = Path.Combine(directory, "revision-manifest.json");
            var manifest = JsonUtility.FromJson<M2RevisionManifest>(File.ReadAllText(path, new UTF8Encoding(false)));
            if (manifest == null || manifest.formatVersion != 1 || manifest.saveRevisionId != revisionId)
            {
                throw new InvalidDataException("Revision manifest is invalid.");
            }

            return manifest;
        }

        internal string GetRevisionDirectory(string revisionId)
        {
            if (!M2FileIO.IsSafeIdentifier(revisionId))
            {
                throw new InvalidDataException("Revision ID contains unsafe characters.");
            }

            var directory = Path.Combine(RevisionsPath, revisionId);
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("M2 revision was not found: " + revisionId);
            }

            return directory;
        }

        internal IEnumerable<string> GetContentFiles(string directoryName)
        {
            var root = Path.Combine(RootPath, directoryName);
            if (!Directory.Exists(root))
            {
                yield break;
            }

            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }

        private static void ValidateRevision(string directory, M2RevisionManifest manifest)
        {
            foreach (var file in manifest.files)
            {
                var path = Path.Combine(directory, file.relativePath);
                if (!File.Exists(path) || new FileInfo(path).Length != file.byteLength ||
                    !string.Equals(M2FileIO.Sha256File(path), file.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("New revision failed integrity validation: " + file.relativePath);
                }
            }

            var state = JsonUtility.FromJson<M1WorldState>(File.ReadAllText(Path.Combine(directory, "project.json"), new UTF8Encoding(false)));
            if (state == null || !string.Equals(M2CanonicalStateHasher.Compute(state), manifest.canonicalStateHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("New revision canonical state hash does not match.");
            }
        }

        private M2LoadResult LoadRevision(string revisionId, string source, M2Head head)
        {
            var directory = GetRevisionDirectory(revisionId);
            var manifest = ReadManifest(revisionId);
            if (manifest.files == null || manifest.files.Count == 0)
            {
                throw new InvalidDataException("Revision has no logical files.");
            }

            foreach (var file in manifest.files)
            {
                if (file == null || string.IsNullOrWhiteSpace(file.relativePath) || Path.IsPathRooted(file.relativePath) || file.relativePath.Contains(".."))
                {
                    throw new InvalidDataException("Revision contains an unsafe file path.");
                }

                var path = Path.Combine(directory, file.relativePath);
                if (!File.Exists(path) || new FileInfo(path).Length != file.byteLength ||
                    !string.Equals(M2FileIO.Sha256File(path), file.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Revision file failed integrity validation: " + file.relativePath);
                }
            }

            var projectPath = Path.Combine(directory, "project.json");
            var state = JsonUtility.FromJson<M1WorldState>(File.ReadAllText(projectPath, new UTF8Encoding(false)));
            if (state == null || state.schemaVersion != manifest.worldSchemaVersion ||
                !string.Equals(M2CanonicalStateHasher.Compute(state), manifest.canonicalStateHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Revision canonical state hash does not match.");
            }

            return new M2LoadResult
            {
                state = state,
                head = head,
                manifest = manifest,
                source = source
            };
        }

        private M2Head ReadHeadOrDefault()
        {
            if (HasHead)
            {
                try
                {
                    return ReadHead();
                }
                catch (FileNotFoundException)
                {
                    // HEAD disappeared between the existence check and read. Scan immutable revisions below.
                }
                catch (InvalidDataException)
                {
                    // A damaged HEAD must not hide valid immutable revisions.
                }
                catch (ArgumentException)
                {
                    // JsonUtility reports malformed JSON as an argument error on some Unity versions.
                }
            }

            var recovered = LoadBestAvailable();
            return recovered == null
                ? new M2Head { activeJournalStreamId = NewStreamId() }
                : recovered.head;
        }

        private static M2Head CreateRecoveredHead(M2RevisionManifest manifest)
        {
            return new M2Head
            {
                formatVersion = 1,
                worldSchemaVersion = manifest.worldSchemaVersion,
                activeSaveRevisionId = manifest.saveRevisionId,
                activeJournalStreamId = M2FileIO.IsSafeIdentifier(manifest.journalStreamId)
                    ? manifest.journalStreamId
                    : NewStreamId(),
                generation = 0,
                lastKnownGoodRevisionId = manifest.saveRevisionId
            };
        }

        private void EnsureLayout()
        {
            M2FileIO.EnsureDirectory(RootPath);
            M2FileIO.EnsureDirectory(RevisionsPath);
            M2FileIO.EnsureDirectory(StagingPath);
            M2FileIO.EnsureDirectory(AssetsPath);
            M2FileIO.EnsureDirectory(ThumbnailsPath);
            M2FileIO.EnsureDirectory(JournalPath);
        }

        private void PruneRevisions(M2Head head)
        {
            if (MaxRetainedRevisions < 1 || !Directory.Exists(RevisionsPath))
            {
                return;
            }

            var directories = GetRevisionDirectoriesByNewest();
            var kept = 0;
            foreach (var directory in directories)
            {
                var id = Path.GetFileName(directory);
                if (id == head.activeSaveRevisionId || id == head.lastKnownGoodRevisionId || kept < MaxRetainedRevisions)
                {
                    kept++;
                    continue;
                }

                Directory.Delete(directory, true);
            }
        }

        private List<string> GetRevisionDirectoriesByNewest()
        {
            var directories = new List<string>();
            if (!Directory.Exists(RevisionsPath))
            {
                return directories;
            }

            foreach (var directory in Directory.GetDirectories(RevisionsPath))
            {
                directories.Add(directory);
            }

            directories.Sort(delegate(string left, string right)
            {
                return Directory.GetLastWriteTimeUtc(right).CompareTo(Directory.GetLastWriteTimeUtc(left));
            });
            return directories;
        }

        public static string NewStreamId()
        {
            return "stream-" + Guid.NewGuid().ToString("N");
        }
    }
}
