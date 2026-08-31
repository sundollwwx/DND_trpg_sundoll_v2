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
                case "M3.MapObject":
                {
                    var payload = ReadPayload<M3MapObjectCommandPayload>(envelope);
                    return new M3MapObjectCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.objectId,
                        (M3MapObjectKind)payload.kind,
                        payload.x,
                        payload.y,
                        payload.rotation,
                        (M3MapObjectAction)payload.action);
                }
                case "M4.RegisterPieceAsset":
                {
                    var payload = ReadPayload<M4RegisterPieceAssetCommandPayload>(envelope);
                    return new M4RegisterPieceAssetCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        new M4PieceAsset
                        {
                            id = payload.assetId,
                            sha256 = payload.sha256,
                            extension = payload.extension,
                            mimeType = payload.mimeType,
                            byteLength = payload.byteLength,
                            relativePath = payload.relativePath,
                            thumbnailSha256 = payload.thumbnailSha256,
                            thumbnailRelativePath = payload.thumbnailRelativePath
                        });
                }
                case "M4.CreatePieceDefinition":
                {
                    var payload = ReadPayload<M4CreatePieceDefinitionCommandPayload>(envelope);
                    return new M4CreatePieceDefinitionCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.definitionId,
                        payload.displayName,
                        payload.category,
                        payload.tags,
                        payload.assetId,
                        payload.footprintWidth,
                        payload.footprintHeight);
                }
                case "M4.UpdatePieceDefinition":
                {
                    var payload = ReadPayload<M4UpdatePieceDefinitionCommandPayload>(envelope);
                    return new M4UpdatePieceDefinitionCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.definitionId,
                        payload.displayName,
                        payload.category,
                        payload.tags,
                        payload.assetId,
                        payload.footprintWidth,
                        payload.footprintHeight);
                }
                case "M4.CreatePieceInstance":
                {
                    var payload = ReadPayload<M4CreatePieceInstanceCommandPayload>(envelope);
                    return new M4CreatePieceInstanceCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.definitionId,
                        payload.instanceId);
                }
                case "M4.PlacePiece":
                {
                    var payload = ReadPayload<M4PlacePieceCommandPayload>(envelope);
                    return new M4PlacePieceCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.instanceId,
                        payload.x,
                        payload.y);
                }
                case "M4.MovePiece":
                {
                    var payload = ReadPayload<M4MovePieceCommandPayload>(envelope);
                    return new M4MovePieceCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.instanceId,
                        payload.x,
                        payload.y);
                }
                case "M4.MovePieces":
                {
                    var payload = ReadPayload<M4MovePiecesCommandPayload>(envelope);
                    return new M4MovePiecesCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.mutations);
                }
                case "M4.MovePieceToContainer":
                {
                    var payload = ReadPayload<M4MovePieceToContainerCommandPayload>(envelope);
                    return new M4MovePieceToContainerCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.instanceId,
                        payload.containerPieceId);
                }
                case "M4.AttachPiece":
                {
                    var payload = ReadPayload<M4AttachPieceCommandPayload>(envelope);
                    return new M4AttachPieceCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.instanceId,
                        payload.targetPieceId,
                        payload.attachmentSlot);
                }
                case "M4.DetachPiece":
                {
                    var payload = ReadPayload<M4DetachPieceCommandPayload>(envelope);
                    return new M4DetachPieceCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.instanceId);
                }
                case "M4.SetPiecePresentation":
                {
                    var payload = ReadPayload<M4SetPiecePresentationCommandPayload>(envelope);
                    return new M4SetPiecePresentationCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.instanceId,
                        payload.rotation,
                        payload.flipped,
                        payload.visible);
                }
                case "M4.SetPieceRuntimeState":
                {
                    var payload = ReadPayload<M4SetPieceRuntimeStateCommandPayload>(envelope);
                    return new M4SetPieceRuntimeStateCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.instanceId,
                        payload.runtimeState);
                }
                case "M4.SetPiecePresentations":
                {
                    var payload = ReadPayload<M4SetPiecePresentationsCommandPayload>(envelope);
                    return new M4SetPiecePresentationsCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.mutations);
                }
                case "M4.SetPieceStackOrder":
                {
                    var payload = ReadPayload<M4SetPieceStackOrderCommandPayload>(envelope);
                    return new M4SetPieceStackOrderCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.instanceId,
                        payload.stackOrder);
                }
                case "M4.DeletePieces":
                {
                    var payload = ReadPayload<M4DeletePiecesCommandPayload>(envelope);
                    return new M4DeletePiecesCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.instanceIds);
                }
                case "M5.CreateMapSlot":
                {
                    var payload = ReadPayload<M5CreateMapSlotCommandPayload>(envelope);
                    return new M5CreateMapSlotCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.mapId,
                        payload.displayName,
                        payload.width,
                        payload.height);
                }
                case "M5.SwitchMap":
                {
                    var payload = ReadPayload<M5SwitchMapCommandPayload>(envelope);
                    return new M5SwitchMapCommand(envelope.commandId, envelope.baseRevision, payload.mapId);
                }
                case "M5.RenameMap":
                {
                    var payload = ReadPayload<M5RenameMapCommandPayload>(envelope);
                    return new M5RenameMapCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.mapId,
                        payload.displayName);
                }
                case "M5.SetFog":
                {
                    var payload = ReadPayload<M5SetFogCommandPayload>(envelope);
                    return new M5SetFogCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.mapId,
                        payload.x,
                        payload.y,
                        payload.revealed);
                }
                case "M5.SetFogBatch":
                {
                    var payload = ReadPayload<M5SetFogBatchCommandPayload>(envelope);
                    return new M5SetFogBatchCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.mapId,
                        payload.mutations);
                }
                case "M5.UpsertAnnotation":
                {
                    var payload = ReadPayload<M5UpsertAnnotationCommandPayload>(envelope);
                    return new M5UpsertAnnotationCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.annotationId,
                        payload.mapId,
                        payload.x,
                        payload.y,
                        payload.text,
                        payload.colorHex,
                        payload.visible);
                }
                case "M5.RemoveAnnotation":
                {
                    var payload = ReadPayload<M5RemoveAnnotationCommandPayload>(envelope);
                    return new M5RemoveAnnotationCommand(envelope.commandId, envelope.baseRevision, payload.annotationId);
                }
                case "M5.SetInteractionState":
                {
                    var payload = ReadPayload<M5SetInteractionStateCommandPayload>(envelope);
                    return new M5SetInteractionStateCommand(
                        envelope.commandId,
                        envelope.baseRevision,
                        payload.objectId,
                        payload.open);
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
