using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sundoll.Core;
using UnityEngine;

namespace Sundoll.Infrastructure
{
    public sealed class M2JournalRecovery
    {
        public M2AcceptedOperationBatch batch;
        public M1WorldState state;
    }

    public sealed class M2JournalStore
    {
        private readonly string streamPath;
        private readonly int maxEntriesPerSegment;
        private readonly object syncRoot = new object();
        private bool cacheInitialized;
        private long cachedLastSequence;
        private int cachedSegmentIndex;
        private int cachedSegmentEntryCount;

        public M2JournalStore(string projectRoot, string streamId, int maxEntriesPerSegment = 25)
        {
            if (!M2FileIO.IsSafeIdentifier(streamId))
            {
                throw new ArgumentException("Journal stream ID contains unsafe characters.", nameof(streamId));
            }

            if (maxEntriesPerSegment < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEntriesPerSegment));
            }

            StreamId = streamId;
            maxEntriesPerSegment = Math.Max(1, maxEntriesPerSegment);
            this.maxEntriesPerSegment = maxEntriesPerSegment;
            streamPath = Path.Combine(projectRoot, "journal", streamId);
            M2FileIO.EnsureDirectory(streamPath);
        }

        public string StreamId { get; }
        public string StreamPath => streamPath;

        public long LastSequence
        {
            get
            {
                lock (syncRoot)
                {
                    EnsureCacheInitialized();
                    return cachedLastSequence;
                }
            }
        }

        public long Append(string commandId, string description, M1WorldState state, string operationType = "StateMutation")
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            lock (syncRoot)
            {
                EnsureCacheInitialized();
                var sequence = checked(cachedLastSequence + 1);
                var batch = new M2AcceptedOperationBatch
                {
                    commandId = commandId,
                    description = description,
                    operationType = operationType,
                    worldRevision = state.revision,
                    operationSequence = sequence,
                    stateJson = JsonUtility.ToJson(state, false),
                    canonicalStateHash = M2CanonicalStateHasher.Compute(state)
                };
                var payloadJson = JsonUtility.ToJson(batch, false);
                var record = new M2JournalRecord
                {
                    operationSequence = sequence,
                    payloadJson = payloadJson,
                    payloadSha256 = M2FileIO.Sha256Utf8(payloadJson)
                };

                var line = JsonUtility.ToJson(record, false) + "\n";
                var segmentPath = GetWritableSegmentPath();
                using (var stream = new FileStream(segmentPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(line);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                cachedLastSequence = sequence;
                cachedSegmentEntryCount++;
                return sequence;
            }
        }

        public void AppendCorruptTail(string rawText)
        {
            if (rawText == null)
            {
                throw new ArgumentNullException(nameof(rawText));
            }

            lock (syncRoot)
            {
                EnsureCacheInitialized();
                var path = GetWritableSegmentPath();
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(rawText);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (rawText.Length > 0)
                {
                    // A fault may leave the tail without a newline. Seal this segment so a later
                    // valid record can never be concatenated onto the corrupt bytes.
                    cachedSegmentEntryCount = maxEntriesPerSegment;
                }
            }
        }

        public bool TryLoadLatest(out M2JournalRecovery recovery)
        {
            lock (syncRoot)
            {
                return ScanSegments(out recovery);
            }
        }

        private void EnsureCacheInitialized()
        {
            if (!cacheInitialized)
            {
                ScanSegments(out _);
            }
        }

        private bool ScanSegments(out M2JournalRecovery recovery)
        {
            recovery = null;
            var latestSequence = 0L;
            var paths = GetSegmentPaths();
            foreach (var path in paths)
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(path, new UTF8Encoding(false));
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || !TryParse(line, out var candidate) ||
                        candidate.batch.operationSequence <= latestSequence)
                    {
                        continue;
                    }

                    latestSequence = candidate.batch.operationSequence;
                    recovery = candidate;
                }
            }

            cachedLastSequence = latestSequence;
            cachedSegmentIndex = paths.Count == 0 ? 0 : ParseSegmentIndex(paths[paths.Count - 1]);
            cachedSegmentEntryCount = paths.Count == 0 ? 0 : CountLines(paths[paths.Count - 1]);
            if (paths.Count > 0 && !EndsWithNewline(paths[paths.Count - 1]))
            {
                cachedSegmentEntryCount = maxEntriesPerSegment;
            }

            cacheInitialized = true;
            return recovery != null;
        }

        private string GetWritableSegmentPath()
        {
            if (cachedSegmentEntryCount >= maxEntriesPerSegment)
            {
                cachedSegmentIndex = checked(cachedSegmentIndex + 1);
                cachedSegmentEntryCount = 0;
            }

            return Path.Combine(streamPath, "segment-" + cachedSegmentIndex.ToString("D6") + ".log");
        }

        private bool TryParse(string line, out M2JournalRecovery recovery)
        {
            recovery = null;
            try
            {
                var record = JsonUtility.FromJson<M2JournalRecord>(line);
                if (record == null || string.IsNullOrEmpty(record.payloadJson) ||
                    !string.Equals(record.payloadSha256, M2FileIO.Sha256Utf8(record.payloadJson), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var batch = JsonUtility.FromJson<M2AcceptedOperationBatch>(record.payloadJson);
                if (batch == null || batch.operationSequence != record.operationSequence || string.IsNullOrEmpty(batch.stateJson))
                {
                    return false;
                }

                var state = JsonUtility.FromJson<M1WorldState>(batch.stateJson);
                if (state == null || !string.Equals(batch.canonicalStateHash, M2CanonicalStateHasher.Compute(state), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                recovery = new M2JournalRecovery { batch = batch, state = state };
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private List<string> GetSegmentPaths()
        {
            var paths = new List<string>();
            if (!Directory.Exists(streamPath))
            {
                return paths;
            }

            foreach (var path in Directory.GetFiles(streamPath, "segment-*.log"))
            {
                paths.Add(path);
            }

            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        private static int CountLines(string path)
        {
            try
            {
                return File.ReadAllLines(path, new UTF8Encoding(false)).Length;
            }
            catch (IOException)
            {
                return 0;
            }
        }

        private static bool EndsWithNewline(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (stream.Length == 0)
                    {
                        return true;
                    }

                    stream.Seek(-1, SeekOrigin.End);
                    return stream.ReadByte() == '\n';
                }
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static int ParseSegmentIndex(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var prefix = "segment-";
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(name.Substring(prefix.Length), out var index))
            {
                return 0;
            }

            return index;
        }
    }
}
