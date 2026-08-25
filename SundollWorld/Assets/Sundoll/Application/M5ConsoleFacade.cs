using System;
using Sundoll.Core;

namespace Sundoll.Application
{
    /// <summary>
    /// Host-console application boundary. Presentation can switch maps and
    /// edit host state without reaching into the authoritative DTOs.
    /// </summary>
    public sealed class M5ConsoleFacade
    {
        private readonly M1CommandBus commandBus;

        public M5ConsoleFacade(M1CommandBus commandBus)
        {
            this.commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        }

        public M1WorldState State => commandBus.State;

        public M1CommandReceipt CreateMap(string mapId, string displayName, int width = 64, int height = 64)
        {
            return commandBus.Execute(new M5CreateMapSlotCommand(
                "m5-map-create-" + Guid.NewGuid().ToString("N"),
                State.revision,
                mapId,
                displayName,
                width,
                height));
        }

        public M1CommandReceipt SwitchMap(string mapId)
        {
            return commandBus.Execute(new M5SwitchMapCommand(
                "m5-map-switch-" + Guid.NewGuid().ToString("N"),
                State.revision,
                mapId));
        }

        public M1CommandReceipt RenameMap(string mapId, string displayName)
        {
            return commandBus.Execute(new M5RenameMapCommand(
                "m5-map-rename-" + Guid.NewGuid().ToString("N"),
                State.revision,
                mapId,
                displayName));
        }

        public M1CommandReceipt SetFog(string mapId, int x, int y, bool revealed)
        {
            return commandBus.Execute(new M5SetFogCommand(
                "m5-fog-" + Guid.NewGuid().ToString("N"),
                State.revision,
                mapId,
                x,
                y,
                revealed));
        }

        public M1CommandReceipt UpsertAnnotation(
            string annotationId,
            string mapId,
            int x,
            int y,
            string text,
            string colorHex = "#FFFFFF",
            bool visible = true)
        {
            return commandBus.Execute(new M5UpsertAnnotationCommand(
                "m5-annotation-" + Guid.NewGuid().ToString("N"),
                State.revision,
                annotationId,
                mapId,
                x,
                y,
                text,
                colorHex,
                visible));
        }

        public M1CommandReceipt RemoveAnnotation(string annotationId)
        {
            return commandBus.Execute(new M5RemoveAnnotationCommand(
                "m5-annotation-remove-" + Guid.NewGuid().ToString("N"),
                State.revision,
                annotationId));
        }

        public M1CommandReceipt SetInteractionState(string objectId, bool open)
        {
            return commandBus.Execute(new M5SetInteractionStateCommand(
                "m5-interaction-" + Guid.NewGuid().ToString("N"),
                State.revision,
                objectId,
                open));
        }
    }
}
