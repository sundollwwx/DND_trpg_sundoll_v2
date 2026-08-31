using System;
using System.Collections.Generic;
using System.IO;
using Sundoll.Application;
using Sundoll.Core;
using UnityEngine;

namespace Sundoll.Infrastructure
{
    /// <summary>
    /// Transport-free M6B proof. The hub owns authority; clients only receive
    /// a projection snapshot and replay versioned command envelopes. Replacing
    /// this in-memory list with a real transport does not change the wire DTOs.
    /// </summary>
    public sealed class M6LoopbackHub
    {
        private sealed class Audience
        {
            public string id;
            public M6AudiencePolicy policy;
        }

        private readonly M1CommandBus authority;
        private readonly List<Audience> audiences = new List<Audience>();
        private readonly List<M6ProjectionDelta> deltaTail = new List<M6ProjectionDelta>();
        private long sequence;

        public M6LoopbackHub(M1CommandBus authority)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        public M1WorldState State => authority.State;
        public int TailCount => deltaTail.Count;

        public M6LoopbackClient Connect(string audienceId, M6AudiencePolicy policy = null)
        {
            if (string.IsNullOrWhiteSpace(audienceId))
            {
                throw new ArgumentException("Audience ID is required.", nameof(audienceId));
            }

            var audience = new Audience
            {
                id = audienceId,
                policy = policy == null ? new M6AudiencePolicy() : policy.DeepClone()
            };
            audiences.RemoveAll(existing => existing.id == audienceId);
            audiences.Add(audience);
            return new M6LoopbackClient(this, audienceId, CreateSnapshot(audience));
        }

        public M1CommandReceipt Submit(M6LoopbackClient client, M1Command command)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var receipt = authority.Execute(command);
            if (!receipt.accepted || receipt.duplicate)
            {
                return receipt;
            }

            var envelope = M2CommandEnvelopeCodec.Encode(command);
            foreach (var audience in audiences)
            {
                deltaTail.Add(new M6ProjectionDelta
                {
                    sequence = ++sequence,
                    audienceId = audience.id,
                    revisionBefore = receipt.revisionBefore,
                    revisionAfter = receipt.revisionAfter,
                    commandId = command.CommandId,
                    commandEnvelope = envelope,
                    canonicalStateHash = M6ProjectionBuilder.CreateSnapshot(
                        authority.State,
                        audience.id,
                        audience.policy).canonicalStateHash
                });
            }

            return receipt;
        }

        public M6ProjectionSnapshot CreateSnapshot(string audienceId)
        {
            var audience = FindAudience(audienceId);
            return CreateSnapshot(audience);
        }

        public List<M6ProjectionDelta> GetTail(string audienceId, int revisionExclusive)
        {
            var result = new List<M6ProjectionDelta>();
            foreach (var delta in deltaTail)
            {
                if (delta.audienceId == audienceId && delta.revisionAfter > revisionExclusive)
                {
                    result.Add(CloneDelta(delta));
                }
            }

            return result;
        }

        private M6ProjectionSnapshot CreateSnapshot(Audience audience)
        {
            if (audience == null)
            {
                throw new InvalidOperationException("Audience is not connected.");
            }

            return M6ProjectionBuilder.CreateSnapshot(authority.State, audience.id, audience.policy);
        }

        private Audience FindAudience(string audienceId)
        {
            foreach (var audience in audiences)
            {
                if (audience.id == audienceId)
                {
                    return audience;
                }
            }

            throw new InvalidOperationException("Audience is not connected: " + audienceId);
        }

        private static M6ProjectionDelta CloneDelta(M6ProjectionDelta source)
        {
            return new M6ProjectionDelta
            {
                protocolVersion = source.protocolVersion,
                sequence = source.sequence,
                audienceId = source.audienceId,
                revisionBefore = source.revisionBefore,
                revisionAfter = source.revisionAfter,
                commandId = source.commandId,
                commandEnvelope = source.commandEnvelope == null ? null : new M1CommandEnvelope
                {
                    formatVersion = source.commandEnvelope.formatVersion,
                    commandType = source.commandEnvelope.commandType,
                    payloadVersion = source.commandEnvelope.payloadVersion,
                    commandId = source.commandEnvelope.commandId,
                    baseRevision = source.commandEnvelope.baseRevision,
                    payloadJson = source.commandEnvelope.payloadJson
                },
                canonicalStateHash = source.canonicalStateHash
            };
        }
    }

    public sealed class M6LoopbackClient
    {
        private readonly M6LoopbackHub hub;

        internal M6LoopbackClient(M6LoopbackHub hub, string audienceId, M6ProjectionSnapshot snapshot)
        {
            this.hub = hub;
            AudienceId = audienceId;
            ApplySnapshot(snapshot);
        }

        public string AudienceId { get; }
        public M1WorldState State { get; private set; }
        public int Revision => State == null ? -1 : State.revision;
        public string LastDiagnostic { get; private set; }

        public M1CommandReceipt Submit(M1Command command)
        {
            var revisionBefore = Revision;
            var receipt = hub.Submit(this, command);
            if (receipt != null && receipt.accepted)
            {
                foreach (var delta in hub.GetTail(AudienceId, revisionBefore))
                {
                    if (!ApplyDelta(delta))
                    {
                        break;
                    }
                }
            }

            return receipt;
        }

        public void ApplySnapshot(M6ProjectionSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.stateJson))
            {
                throw new InvalidDataException("Projection snapshot is empty.");
            }

            var state = JsonUtility.FromJson<M1WorldState>(snapshot.stateJson);
            if (state == null)
            {
                throw new InvalidDataException("Projection snapshot did not contain a world state.");
            }

            state.EnsureSchema2Defaults();
            var actualHash = M2CanonicalStateHasher.Compute(state);
            if (!string.Equals(actualHash, snapshot.canonicalStateHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Projection snapshot hash mismatch.");
            }

            State = state;
            LastDiagnostic = string.Empty;
        }

        public bool ApplyDelta(M6ProjectionDelta delta)
        {
            if (delta == null || delta.commandEnvelope == null || delta.audienceId != AudienceId)
            {
                LastDiagnostic = "Projection delta audience or payload is invalid.";
                return false;
            }

            if (State.revision != delta.revisionBefore)
            {
                LastDiagnostic = "Projection delta revision gap.";
                return false;
            }

            var command = M2CommandEnvelopeCodec.Decode(delta.commandEnvelope);
            var bus = new M1CommandBus(State, new M1LocalAuthority(new AllowAllRulePolicy()));
            var receipt = bus.Execute(command);
            if (!receipt.accepted)
            {
                LastDiagnostic = receipt.message;
                return false;
            }

            var actualHash = M2CanonicalStateHasher.Compute(State);
            if (!string.Equals(actualHash, delta.canonicalStateHash, StringComparison.OrdinalIgnoreCase))
            {
                LastDiagnostic = "Projection delta hash mismatch.";
                return false;
            }

            LastDiagnostic = string.Empty;
            return true;
        }
    }

    public static class M6ProjectionBuilder
    {
        public static M6ProjectionSnapshot CreateSnapshot(M1WorldState source, string audienceId, M6AudiencePolicy policy)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var projected = source.DeepClone();
            var effectivePolicy = policy == null ? new M6AudiencePolicy() : policy;
            if (!effectivePolicy.includeHiddenPieces && projected.pieceInstances != null)
            {
                projected.pieceInstances.RemoveAll(instance => instance == null || !instance.visible);
            }

            if (projected.pieceInstances != null)
            {
                // Audience visibility is a separate contract from the local
                // editor visibility flag. A private piece never crosses the
                // projection boundary, even for an otherwise full snapshot.
                projected.pieceInstances.RemoveAll(instance =>
                    instance == null || instance.runtimeState != null && !instance.runtimeState.audienceVisible);

                if (!effectivePolicy.includePrivatePieceState)
                {
                    foreach (var instance in projected.pieceInstances)
                    {
                        if (instance != null)
                        {
                            instance.runtimeState = instance.runtimeState == null
                                ? M4PieceRuntimeState.CreateDefault()
                                : instance.runtimeState.CreateAudienceProjection();
                        }
                    }
                }
            }

            if (effectivePolicy.allowedPieceInstanceIds != null && effectivePolicy.allowedPieceInstanceIds.Count > 0 &&
                projected.pieceInstances != null)
            {
                var allowed = new HashSet<string>(effectivePolicy.allowedPieceInstanceIds, StringComparer.Ordinal);
                projected.pieceInstances.RemoveAll(instance => instance == null || !allowed.Contains(instance.id));
            }

            if (!effectivePolicy.revealAllFog)
            {
                ApplyFogProjection(projected);
            }

            var json = JsonUtility.ToJson(projected, false);
            return new M6ProjectionSnapshot
            {
                audienceId = audienceId,
                worldRevision = projected.revision,
                canonicalStateHash = M2CanonicalStateHasher.Compute(projected),
                stateJson = json
            };
        }

        private static void ApplyFogProjection(M1WorldState state)
        {
            var console = state.m5Console;
            if (console == null || state.map == null)
            {
                return;
            }

            var mapId = string.IsNullOrWhiteSpace(console.activeMapId) ? state.map.id : console.activeMapId;
            state.map.cells.RemoveAll(cell => cell != null && !console.IsRevealed(mapId, cell.x, cell.y));
            if (state.publishedMap != null && state.publishedMap.cells != null)
            {
                state.publishedMap.cells.RemoveAll(cell => cell != null && !console.IsRevealed(mapId, cell.x, cell.y));
            }
        }
    }
}
