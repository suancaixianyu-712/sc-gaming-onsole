using Engine;
using HarmonyLib;

namespace Game {
    // The touch movement widgets keep tracking a finger even behind a Dialog.
    // Guard the complete player input update, without clearing the UI's input.
    [HarmonyPatch(typeof(ComponentInput), nameof(ComponentInput.Update))]
    internal static class PSPConsoleInputGuard {
        internal static bool IsBlocked(ComponentInput componentInput) {
            ContainerWidget gui = componentInput.m_componentPlayer?.PlayerData?.GameWidget?.GuiWidget;
            if (gui == null) return false;
            for (int i = gui.Children.Count - 1; i >= 0; i--) {
                // A closing dialog stays attached during its fade animation.
                // Keep blocking until the overlay has actually been removed.
                if (gui.Children[i] is PSPConsoleDialog) return true;
            }
            return false;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        static bool Prefix(ComponentInput __instance) {
            if (!IsBlocked(__instance)) return true;
            ClearPlayerControls(__instance);
            ShowInterfaceCursors(__instance);
            return false;
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        static void Postfix(ComponentInput __instance) {
            // Also discard input written by other patches while the UI is open.
            if (IsBlocked(__instance)) ClearPlayerControls(__instance);
        }

        internal static void ClearPlayerControls(ComponentInput componentInput) {
            if (componentInput == null) return;
            componentInput.m_playerInput = default;
            componentInput.m_isViewHoldStarted = false;
            componentInput.m_lastJumpTime = double.NegativeInfinity;
            ComponentGui gui = componentInput.m_componentGui;
            if (gui == null) return;
            ResetTouch(gui.ViewWidget);
            ResetTouch(gui.MoveWidget);
            ResetTouch(gui.LookWidget);
            if (gui.MoveRoseWidget != null) {
                gui.MoveRoseWidget.m_direction = Vector3.Zero;
                gui.MoveRoseWidget.m_jump = false;
                gui.MoveRoseWidget.m_jumpTouchId = null;
            }
        }

        static void ResetTouch(TouchInputWidget widget) {
            if (widget == null) return;
            widget.m_touchId = null;
            widget.m_touchInput = null;
            widget.m_touchMoved = false;
        }

        static void ShowInterfaceCursors(ComponentInput componentInput) {
            ComponentPlayer player = componentInput.m_componentPlayer;
            WidgetInput input = player.GameWidget.Input;
            Vector2 center = player.ViewWidget.WidgetToScreen(player.ViewWidget.ActualSize / 2f);
            if (!input.IsMouseCursorVisible) {
                input.IsMouseCursorVisible = true;
                componentInput.SetMousePositionInNextFrame = center;
            }
            else if (componentInput.SetMousePositionInNextFrame.HasValue && input.MousePosition.HasValue) {
                if (!input.IsPadCursorVisible) input.MousePosition = componentInput.SetMousePositionInNextFrame.Value;
                componentInput.SetMousePositionInNextFrame = null;
            }
            if (!input.IsPadCursorVisible) {
                input.IsPadCursorVisible = true;
                input.PadCursorPosition = center;
            }
        }
    }
}
