using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sundoll.Application;
using Sundoll.Core;
using UnityEngine;

namespace Sundoll.Infrastructure
{
    public sealed class M2JournalRecovery
    {
        public M2AcceptedOperationBatch batch;
        public M1WorldState state;
    }

    public sealed class M2JournalReplayResult
    {
        public M1WorldState state;
        public M2AcceptedOperationBatch lastBatch;
        public long lastSequence;
        public int appliedCount;
        public bool complete;
        public string diagnostic;
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

            return AppendBatch(new M2AcceptedOperationBatch
            {
                formatVersion = 1,
                commandId = commandId,
                description = description,
                operationType = operationType,
                worldRevision = state.revision,
                stateJson = JsonUtility.ToJson(state, false),
                canonicalStateHash = M2CanonicalStateHasher.Compute(state)
            });
        }

        public long Append(
            AcceptedOperationBatch operationBatch,
            string description,
            M1WorldState state,
            string operationType = "DomainCommand")
        {
            if (operationBatch == null)
            {
                throw new ArgumentNullException(nameof(operationBatch));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (operationBatch.formatVersion != 1 || operationBatch.commandEnvelope == null ||
                operationBatch.revisionAfter != state.revision ||
                operationBatch.revisionAfter != operationBatch.revisionBefore + 1)
            {
                throw new InvalidDataException("Accepted operation batch is invalid.");
            }

            // Decode once at the persistence boundary so a malformed command can never
            // become an apparently valid Journal record.
            M2CommandEnvelopeCodec.Decode(operationBatch.commandEnvelope);
            return AppendBatch(new M2AcceptedOperationBatch
            {
                formatVersion = 2,
                commandId = operationBatch.commandEnvelope.commandId,
                description = description,
                operationType = operationType,
                actorId = operationBatch.actorId,
                revisionBefore = operationBatch.revisionBefore,
                revisionAfter = operationBatch.revisionAfter,
                worldRevision = state.revision,
                commandEnvelope = operationBatch.commandEnvelope,
                changeSet = operationBatch.changeSet,
                canonicalStateHash = M2CanonicalStateHasher.Compute(state)
            });
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

        public bool TryReplay(M1WorldState snapshot, long afterSequence, out M2JournalReplayResult replay)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (afterSequence < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(afterSequence));
            }

            lock (syncRoot)
            {
                var candidates = ReadValidRecoveries(GetSegmentPaths());
                candidates.Sort((left, right) => left.batch.operationSequence.CompareTo(right.batch.operationSequence));
                replay = new M2JournalReplayResult
                {
                    state = snapshot.DeepClone(),
                    lastSequence = afterSequence,
                    complete = true,
                    diagnostic = "Journal 没有需要重放的操作"
                };

                var expectedSequence = checked(afterSequence + 1);
                foreach (var candidate in candidates)
                {
                    var sequence = candidate.batch.operationSequence;
                    if (sequence <= afterSequence || sequence < expectedSequence)
                    {
                        continue;
                    }

                    if (sequence > expectedSequence)
                    {
                        replay.complete = false;
                        replay.diagnostic = "Journal sequence gap: expected " + expectedSequence + ", found " + sequence;
                        break;
                    }

                    var nextState = replay.state.DeepClone();
                    try
                    {
                        if (candidate.batch.formatVersion == 1)
                        {
                            nextState = candidate.state.DeepClone();
                        }
                        else
                        {
                            ApplyVersionedBatch(candidate.batch, nextState);
                        }

                        if (!string.Equals(
                                candidate.batch.canonicalStateHash,
                                M2CanonicalStateHasher.Compute(nextState),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException("Journal operation hash does not match replayed state.");
                        }
                    }
                    catch (Exception exception)
                    {
                        replay.complete = false;
                        replay.diagnostic = "Journal replay stopped at sequence " + sequence + ": " + exception.Message;
                        break;
                    }

                    replay.state = nextState;
                    replay.lastBatch = candidate.batch;
                    replay.lastSequence = sequence;
                    replay.appliedCount++;
                    expectedSequence = checked(sequence + 1);
                }

                if (replay.appliedCount > 0)
                {
                    replay.diagnostic = replay.complete
                        ? "Journal 已重放 " + replay.appliedCount + " 个操作"
                        : replay.diagnostic;
                }

                return replay.appliedCount > 0;
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
            foreach (var candidate in ReadValidRecoveries(paths))
            {
                if (candidate.batch.operationSequence > latestSequence)
                {
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

        private long AppendBatch(M2AcceptedOperationBatch batch)
        {
            lock (syncRoot)
            {
                EnsureCacheInitialized();
                var sequence = checked(cachedLastSequence + 1);
                batch.operationSequence = sequence;
                var payloadJson = JsonUtility.ToJson(batch, false);
                var record = new M2JournalRecord
                {
                    formatVersion = batch.formatVersion,
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

        private string GetWritableSegmentPath()
        {
            if (cachedSegmentEntryCount >= maxEntriesPerSegment)
            {
                cachedSegmentIndex = checked(cachedSegmentIndex + 1);
                cachedSegmentEntryCount = 0;
            }

            return Path.Combine(streamPath, "segment-" + cachedSegmentIndex.ToString("D6") + ".log");
        }

        private static bool TryParse(string line, out M2JournalRecovery recovery)
        {
            recovery = null;
            try
            {
                var record = JsonUtility.FromJson<M2JournalRecord>(line);
                if (record == null || (record.formatVersion != 1 && record.formatVersion != 2) ||
                    record.operationSequence < 1 || string.IsNullOrEmpty(record.payloadJson) ||
                    !string.Equals(record.payloadSha256, M2FileIO.Sha256Utf8(record.payloadJson), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var batch = JsonUtility.FromJson<M2AcceptedOperationBatch>(record.payloadJson);
                if (batch == null || (batch.formatVersion != 1 && batch.formatVersion != 2) ||
                    batch.operationSequence != record.operationSequence ||
                    batch.canonicalStateHash == null)
                {
                    return false;
                }

                if (batch.formatVersion == 1)
                {
                    if (string.IsNullOrEmpty(batch.stateJson))
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

                if (record.formatVersion != 2 || batch.commandEnvelope == null ||
                    batch.revisionAfter != batch.revisionBefore + 1 ||
                    batch.commandEnvelope.commandId != batch.commandId)
                {
                    return false;
                }

                M2CommandEnvelopeCodec.Decode(batch.commandEnvelope);
                recovery = new M2JournalRecovery { batch = batch };
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void ApplyVersionedBatch(M2AcceptedOperationBatch batch, M1WorldState state)
        {
            if (batch.commandEnvelope == null || batch.revisionBefore != state.revision)
            {
                throw new InvalidDataException("Journal command base Revision does not match snapshot state.");
            }

            var command = M2CommandEnvelopeCodec.Decode(batch.commandEnvelope);
            if (command.BaseRevision != state.revision || command.CommandId != batch.commandId)
            {
                throw new InvalidDataException("Journal command envelope does not match its batch metadata.");
            }

            command.Apply(state);
            state.revision = batch.revisionAfter;
        }

        private static List<M2JournalRecovery> ReadValidRecoveries(List<string> paths)
        {
            var recoveries = new List<M2JournalRecovery>();
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
                    if (!string.IsNullOrWhiteSpace(line) && TryParse(line, out var candidate))
                    {
                        recoveries.Add(candidate);
                    }
                }
            }

            return recoveries;
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
