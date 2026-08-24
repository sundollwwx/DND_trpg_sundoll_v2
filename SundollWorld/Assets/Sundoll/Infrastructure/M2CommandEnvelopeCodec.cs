using System;
using System.IO;
using Sundoll.Application;
using Sundoll.Core;
using UnityEngine;

namespace Sundoll.Infrastructure
{
    public static class M2CommandEnvelopeCodec
    {
        public static M1CommandEnvelope Encode(M1Command command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (string.IsNullOrWhiteSpace(command.CommandType) || command.PayloadVersion < 1)
            {
                throw new InvalidDataException("Command is not versioned: " + command.GetType().Name);
            }

            var payload = command.CreatePayload();
            if (payload == null)
            {
                throw new InvalidDataException("Versioned command has no payload: " + command.CommandType);
            }

            return new M1CommandEnvelope
            {
                formatVersion = 1,
                commandType = command.CommandType,
                payloadVersion = command.PayloadVersion,
                commandId = command.CommandId,
                baseRevision = command.BaseRevision,
                payloadJson = JsonUtility.ToJson(payload, false)
            };
        }

        public static M1Command Decode(M1CommandEnvelope envelope)
        {
            ValidateEnvelope(envelope);
            switch (envelope.commandType)
            {
                case "M1.CreateProject":
                {
                    var payload = ReadPayload<M1CreateProjectCommandPayload>(envelope);
                    return new M1CreateProjectCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.projectId,
                        payload.projectName,
                        payload.mapId);
                }
                case "M1.PaintCell":
                {
                    var payload = ReadPayload<M1PaintCellCommandPayload>(envelope);
                    return new M1PaintCellCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.x,
                        payload.y,
                        payload.contentId);
                }
                case "M1.PublishMapContent":
                {
                    var payload = ReadPayload<M1PublishMapContentCommandPayload>(envelope);
                    return new M1PublishMapContentCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.contentVersionId);
                }
                case "M1.CreateScenario":
                {
                    var payload = ReadPayload<M1CreateScenarioCommandPayload>(envelope);
                    return new M1CreateScenarioCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.scenarioId,
                        payload.boardId);
                }
                case "M1.CreatePiece":
                {
                    var payload = ReadPayload<M1CreatePieceCommandPayload>(envelope);
                    return new M1CreatePieceCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.definitionId,
                        payload.instanceId,
                        payload.displayName,
                        payload.visualKey);
                }
                case "M1.PlacePiece":
                {
                    var payload = ReadPayload<M1PlacePieceCommandPayload>(envelope);
                    return new M1PlacePieceCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.x,
                        payload.y);
                }
                case "M1.MovePiece":
                {
                    var payload = ReadPayload<M1MovePieceCommandPayload>(envelope);
                    return new M1MovePieceCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.x,
                        payload.y);
                }
                case "M3.PaintCells":
                {
                    var payload = ReadPayload<M3PaintCellsCommandPayload>(envelope);
                    return new M3PaintCellsCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.mutations);
                }
                default:
                    throw new InvalidDataException("Unknown command type: " + envelope.commandType);
            }
        }

        public static AcceptedOperationBatch CreateAcceptedBatch(M1CommandReceipt receipt)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }

            if (!receipt.accepted || receipt.command == null)
            {
                throw new InvalidOperationException("Only an accepted command can create an operation batch.");
            }

            return new AcceptedOperationBatch
            {
                formatVersion = 1,
                actorId = "local",
                revisionBefore = receipt.revisionBefore,
                revisionAfter = receipt.revisionAfter,
                commandEnvelope = Encode(receipt.command),
                changeSet = receipt.changeSet
            };
        }

        private static void ValidateEnvelope(M1CommandEnvelope envelope)
        {
            if (envelope == null || envelope.formatVersion != 1 ||
                string.IsNullOrWhiteSpace(envelope.commandType) ||
                envelope.payloadVersion < 1 || string.IsNullOrWhiteSpace(envelope.commandId) ||
                string.IsNullOrWhiteSpace(envelope.payloadJson))
            {
                throw new InvalidDataException("Command envelope is invalid.");
            }
        }

        private static T ReadPayload<T>(M1CommandEnvelope envelope) where T : class
        {
            try
            {
                var payload = JsonUtility.FromJson<T>(envelope.payloadJson);
                if (payload == null)
                {
                    throw new InvalidDataException("Command payload is empty: " + envelope.commandType);
                }

                return payload;
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Command payload is malformed: " + envelope.commandType, exception);
            }
        }
    }
}
