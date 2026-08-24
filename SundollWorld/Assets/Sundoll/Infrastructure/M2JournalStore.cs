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

        public long LastSequence => FindLatestSequence();

        public long Append(string commandId, string description, M1WorldState state, string operationType = "StateMutation")
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var sequence = FindLatestSequence() + 1;
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
            var segmentPath = FindWritableSegmentPath();
            using (var stream = new FileStream(segmentPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                var bytes = new UTF8Encoding(false).GetBytes(line);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            return sequence;
        }

        public void AppendCorruptTail(string rawText)
        {
            if (rawText == null)
            {
                throw new ArgumentNullException(nameof(rawText));
            }

            var path = FindWritableSegmentPath();
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                var bytes = new UTF8Encoding(false).GetBytes(rawText);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        public bool TryLoadLatest(out M2JournalRecovery recovery)
        {
            recovery = null;
            var latestSequence = -1L;
            foreach (var path in GetSegmentPaths())
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
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!TryParse(line, out var candidate))
                    {
                        continue;
                    }

                    if (candidate.batch.operationSequence <= latestSequence)
                    {
                        continue;
                    }

                    latestSequence = candidate.batch.operationSequence;
                    recovery = candidate;
                }
            }

            return recovery != null;
        }

        private string FindWritableSegmentPath()
        {
            var segments = GetSegmentPaths();
            if (segments.Count == 0)
            {
                return Path.Combine(streamPath, "segment-000000.log");
            }

            var last = segments[segments.Count - 1];
            var count = CountLines(last);
            if (count < maxEntriesPerSegment)
            {
                return last;
            }

            var nextIndex = ParseSegmentIndex(last) + 1;
            return Path.Combine(streamPath, "segment-" + nextIndex.ToString("D6") + ".log");
        }

        private long FindLatestSequence()
        {
            var latest = 0L;
            foreach (var path in GetSegmentPaths())
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
                    if (TryParse(line, out var candidate) && candidate.batch.operationSequence > latest)
                    {
                        latest = candidate.batch.operationSequence;
                    }
                }
            }

            return latest;
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
