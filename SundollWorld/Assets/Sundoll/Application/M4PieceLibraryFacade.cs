using System;
using System.Collections.Generic;
using Sundoll.Core;

namespace Sundoll.Application
{
    /// <summary>
    /// Application boundary for M4 piece-library and spatial commands. The
    /// Workbench does not mutate piece DTOs directly.
    /// </summary>
    public sealed class M4PieceLibraryFacade
    {
        private readonly M1CommandBus commandBus;

        public M4PieceLibraryFacade(M1CommandBus commandBus)
        {
            this.commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        }

        public M1WorldState State => commandBus.State;

        public M1CommandReceipt RegisterAsset(M4PieceAsset asset)
        {
            return commandBus.Execute(new M4RegisterPieceAssetCommand(
                "m4-asset-" + Guid.NewGuid().ToString("N"),
                State.revision,
                asset));
        }

        public M1CommandReceipt CreateDefinition(
            string definitionId,
            string displayName,
            string category,
            IEnumerable<string> tags,
            string assetId = null,
            int footprintWidth = 1,
            int footprintHeight = 1)
        {
            return commandBus.Execute(new M4CreatePieceDefinitionCommand(
                "m4-definition-" + Guid.NewGuid().ToString("N"),
                State.revision,
                definitionId,
                displayName,
                category,
                tags,
                assetId,
                footprintWidth,
                footprintHeight));
        }

        public M1CommandReceipt CreateInstance(string definitionId, string instanceId = null)
        {
            return commandBus.Execute(new M4CreatePieceInstanceCommand(
                "m4-instance-" + Guid.NewGuid().ToString("N"),
                State.revision,
                definitionId,
                string.IsNullOrWhiteSpace(instanceId) ? "instance-" + Guid.NewGuid().ToString("N") : instanceId));
        }

        public M1CommandReceipt UpdateDefinition(
            string definitionId,
            string displayName,
            string category,
            IEnumerable<string> tags,
            string assetId,
            int footprintWidth = 1,
            int footprintHeight = 1)
        {
            return commandBus.Execute(new M4UpdatePieceDefinitionCommand(
                "m4-definition-update-" + Guid.NewGuid().ToString("N"),
                State.revision,
                definitionId,
                displayName,
                category,
                tags,
                assetId,
                footprintWidth,
                footprintHeight));
        }

        public M1CommandReceipt Place(string instanceId, int x, int y)
        {
            return commandBus.Execute(new M4PlacePieceCommand(
                "m4-place-" + Guid.NewGuid().ToString("N"),
                State.revision,
                instanceId,
                x,
                y));
        }

        public M1CommandReceipt Move(string instanceId, int x, int y)
        {
            return commandBus.Execute(new M4MovePieceCommand(
                "m4-move-" + Guid.NewGuid().ToString("N"),
                State.revision,
                instanceId,
                x,
                y));
        }

        public M1CommandReceipt MoveBatch(IEnumerable<M4PieceMoveMutation> mutations)
        {
            return commandBus.Execute(new M4MovePiecesCommand(
                "m4-move-batch-" + Guid.NewGuid().ToString("N"),
                State.revision,
                mutations));
        }

        public M1CommandReceipt MoveToContainer(string instanceId, string containerPieceId)
        {
            return commandBus.Execute(new M4MovePieceToContainerCommand(
                "m4-container-" + Guid.NewGuid().ToString("N"),
                State.revision,
                instanceId,
                containerPieceId));
        }

        public M1CommandReceipt Attach(string instanceId, string targetPieceId, string attachmentSlot)
        {
            return commandBus.Execute(new M4AttachPieceCommand(
                "m4-attach-" + Guid.NewGuid().ToString("N"),
                State.revision,
                instanceId,
                targetPieceId,
                attachmentSlot));
        }

        public M1CommandReceipt Detach(string instanceId)
        {
            return commandBus.Execute(new M4DetachPieceCommand(
                "m4-detach-" + Guid.NewGuid().ToString("N"),
                State.revision,
                instanceId));
        }

        public M1CommandReceipt SetPresentation(string instanceId, int rotation, bool flipped, bool visible)
        {
            return commandBus.Execute(new M4SetPiecePresentationCommand(
                "m4-presentation-" + Guid.NewGuid().ToString("N"),
                State.revision,
                instanceId,
                rotation,
                flipped,
                visible));
        }

        public M1CommandReceipt SetPresentationBatch(IEnumerable<M4PiecePresentationMutation> mutations)
        {
            return commandBus.Execute(new M4SetPiecePresentationsCommand(
                "m4-presentation-batch-" + Guid.NewGuid().ToString("N"),
                State.revision,
                mutations));
        }

        public M1CommandReceipt SetRuntimeState(string instanceId, M4PieceRuntimeState runtimeState)
        {
            return commandBus.Execute(new M4SetPieceRuntimeStateCommand(
                "m4-runtime-state-" + Guid.NewGuid().ToString("N"),
                State.revision,
                instanceId,
                runtimeState));
        }

        public M1CommandReceipt SetStackOrder(string instanceId, int stackOrder)
        {
            return commandBus.Execute(new M4SetPieceStackOrderCommand(
                "m4-stack-" + Guid.NewGuid().ToString("N"),
                State.revision,
                instanceId,
                stackOrder));
        }

        public M1CommandReceipt DeleteInstances(IEnumerable<string> instanceIds)
        {
            return commandBus.Execute(new M4DeletePiecesCommand(
                "m4-delete-" + Guid.NewGuid().ToString("N"),
                State.revision,
                instanceIds));
        }
    }
}
