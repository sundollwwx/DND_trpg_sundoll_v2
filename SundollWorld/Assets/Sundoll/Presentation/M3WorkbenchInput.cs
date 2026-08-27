using UnityEngine;
using UnityEngine.InputSystem;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Input System adapter for the Workbench. It translates pointer and
    /// shortcut gestures into root intents; no raw Unity input reaches domain
    /// commands directly.
    /// </summary>
    public sealed class M3WorkbenchInput : MonoBehaviour
    {
        private M3WorkbenchRoot root;
        private Camera workbenchCamera;
        private M3WorkbenchMapProjection projection;
        private bool panning;
        private bool pointerAction;
        private Vector2 lastMousePosition;
        private Vector2Int pointerCell;

        public void Bind(M3WorkbenchRoot nextRoot, Camera nextCamera, M3WorkbenchMapProjection nextProjection)
        {
            root = nextRoot;
            workbenchCamera = nextCamera;
            projection = nextProjection;
        }

        private void Update()
        {
            if (root == null || workbenchCamera == null || Mouse.current == null)
            {
                return;
            }

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            var position = mouse.position.ReadValue();
            HandleShortcuts(keyboard);

            if (mouse.rightButton.wasPressedThisFrame && root.IsPointerOverMap(position) &&
                root.TryScreenToCell(position, out var contextCell))
            {
                root.ShowMapContextMenu(contextCell, position);
                pointerAction = false;
            }

            var scroll = mouse.scroll.ReadValue();
            if (root.IsPointerOverMap(position) && Mathf.Abs(scroll.y) > 0.01f)
            {
                root.ZoomAt(position, scroll.y);
            }

            if (mouse.middleButton.wasPressedThisFrame)
            {
                panning = true;
                lastMousePosition = position;
            }

            if (panning && mouse.middleButton.isPressed)
            {
                root.PanByScreen(position - lastMousePosition);
                lastMousePosition = position;
            }

            if (mouse.middleButton.wasReleasedThisFrame)
            {
                panning = false;
            }

            if (mouse.leftButton.wasPressedThisFrame && !panning && root.IsPointerOverMap(position))
            {
                root.DismissContextMenu();
                var altPick = keyboard != null &&
                              (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
                if (!altPick && root.TrySelectPieceAtScreen(position))
                {
                    pointerAction = false;
                    return;
                }

                if (altPick && root.TryScreenToCell(position, out var pickedCell))
                {
                    pointerCell = pickedCell;
                    root.PickAt(pointerCell);
                }
                else if (root.TryScreenToCell(position, out pointerCell))
                {
                    pointerAction = true;
                    root.BeginPointerAction(pointerCell);
                }
            }

            if (pointerAction && mouse.leftButton.isPressed && root.TryScreenToCell(position, out var draggedCell))
            {
                pointerCell = draggedCell;
                root.ContinuePointerAction(pointerCell);
            }

            if (pointerAction && mouse.leftButton.wasReleasedThisFrame)
            {
                if (root.TryScreenToCell(position, out var releasedCell))
                {
                    pointerCell = releasedCell;
                }

                root.EndPointerAction(pointerCell);
                pointerAction = false;
            }
        }

        private void HandleShortcuts(Keyboard keyboard)
        {
            if (keyboard == null)
            {
                return;
            }

            var command = keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed ||
                          keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            if (command && keyboard.zKey.wasPressedThisFrame)
            {
                root.Undo();
            }
            else if (command && keyboard.yKey.wasPressedThisFrame)
            {
                root.Redo();
            }
            else if (command && keyboard.cKey.wasPressedThisFrame)
            {
                root.CopySelection();
            }
            else if (command && keyboard.xKey.wasPressedThisFrame)
            {
                root.CutSelection();
            }
            else if (command && keyboard.vKey.wasPressedThisFrame)
            {
                root.PasteAt(pointerCell);
            }
            else if (command && keyboard.rKey.wasPressedThisFrame)
            {
                root.RotateClipboard();
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                pointerAction = false;
                root.CancelPointerAction();
                root.DismissContextMenu();
            }

            if (keyboard.rKey.wasPressedThisFrame && !command)
            {
                root.RotateObjectAt(pointerCell);
            }

            if (keyboard.tKey.wasPressedThisFrame && !command)
            {
                root.ToggleObjectAt(pointerCell);
            }

            if (keyboard.oKey.wasPressedThisFrame && !command)
            {
                root.OpenObjectAt(pointerCell);
            }

            if (keyboard.kKey.wasPressedThisFrame && !command)
            {
                root.CloseObjectAt(pointerCell);
            }
        }
    }
}
