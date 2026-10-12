using System;
using Engine;

namespace Game {
    public enum PSPConsoleControl {
        None,
        Up,
        Down,
        Left,
        Right,
        Analog,
        Triangle,
        Circle,
        Cross,
        Square,
        Home,
        Select,
        Start,
        LeftShoulder,
        RightShoulder,
        DPadCenter,
        Count
    }

    public sealed class PSPConsoleControlDefinition {
        public PSPConsoleControl Control { get; }
        public Vector2 ModelCenter { get; }
        public Vector2 HitSize { get; }
        public bool Round { get; }

        public PSPConsoleControlDefinition(PSPConsoleControl control, Vector2 center, Vector2 hitSize, bool round = false) {
            Control = control;
            ModelCenter = center;
            HitSize = hitSize;
            Round = round;
        }
    }

    public static class PSPConsoleControls {
        public const float ModelWidth = 0.17f;
        public const float ModelHeight = 0.078f;
        public const float AnalogHitDiameter = 0.017f;
        public const float AnalogTravel = 0.0028f;

        // Coordinates are taken from the corrected PSP_v6 GLB nodes, before the
        // front-view 180-degree rotation: screen-left is model +X. The Square
        // sits toward the screen; the Circle sits toward the right outer edge.
        // Both drawing and touch regions use these coordinates.
        public static readonly PSPConsoleControlDefinition[] Definitions = [
            new(PSPConsoleControl.Up, new Vector2(0.064775f, 0.0184f), new Vector2(0.0075f, 0.009f)),
            new(PSPConsoleControl.Down, new Vector2(0.064775f, 0.00325f), new Vector2(0.0075f, 0.009f)),
            new(PSPConsoleControl.Left, new Vector2(0.07235f, 0.010825f), new Vector2(0.009f, 0.0075f)),
            new(PSPConsoleControl.Right, new Vector2(0.0572f, 0.010825f), new Vector2(0.009f, 0.0075f)),
            new(PSPConsoleControl.Analog, new Vector2(0.064775f, -0.012175f), new Vector2(AnalogHitDiameter), true),
            new(PSPConsoleControl.Triangle, new Vector2(-0.064775f, 0.01f), new Vector2(0.0105f), true),
            new(PSPConsoleControl.Circle, new Vector2(-0.073275f, 0.0015f), new Vector2(0.0105f), true),
            new(PSPConsoleControl.Cross, new Vector2(-0.064775f, -0.007f), new Vector2(0.0105f), true),
            new(PSPConsoleControl.Square, new Vector2(-0.056275f, 0.0015f), new Vector2(0.0105f), true),
            new(PSPConsoleControl.Home, new Vector2(0.04f, -0.029525f), new Vector2(0.01f, 0.007f)),
            new(PSPConsoleControl.Select, new Vector2(0.02f, -0.029525f), new Vector2(0.01f, 0.007f)),
            new(PSPConsoleControl.Start, new Vector2(-0.02f, -0.029525f), new Vector2(0.01f, 0.007f)),
            new(PSPConsoleControl.LeftShoulder, new Vector2(0.067f, 0.0353f), new Vector2(0.032f, 0.0065f)),
            new(PSPConsoleControl.RightShoulder, new Vector2(-0.067f, 0.0353f), new Vector2(0.032f, 0.0065f))
        ];

        public static PSPConsoleControl GetMeshControl(string nodeName) {
            if (nodeName.StartsWith("Triangle ", StringComparison.Ordinal)) return PSPConsoleControl.Triangle;
            if (nodeName.StartsWith("Circle ", StringComparison.Ordinal)) return PSPConsoleControl.Circle;
            if (nodeName.StartsWith("Cross ", StringComparison.Ordinal)) return PSPConsoleControl.Cross;
            if (nodeName.StartsWith("Square ", StringComparison.Ordinal)) return PSPConsoleControl.Square;
            return nodeName switch {
                "D-pad up" => PSPConsoleControl.Up,
                "D-pad down" => PSPConsoleControl.Down,
                // The source labels follow model X; screen-left is model +X.
                "D-pad right" => PSPConsoleControl.Left,
                "D-pad left" => PSPConsoleControl.Right,
                "D-pad center" => PSPConsoleControl.DPadCenter,
                "Analog thumb nub" => PSPConsoleControl.Analog,
                "Home key" => PSPConsoleControl.Home,
                "Select key" => PSPConsoleControl.Select,
                "Start key" => PSPConsoleControl.Start,
                "Left shoulder L" => PSPConsoleControl.LeftShoulder,
                "Right shoulder R" => PSPConsoleControl.RightShoulder,
                _ => PSPConsoleControl.None
            };
        }

        public static Vector3 GetMeshPivot(PSPConsoleControl control) {
            // Use the physical button center for both the body and its glyph.
            Vector2 center = control switch {
                PSPConsoleControl.Up => new Vector2(0.064775f, 0.017625f),
                PSPConsoleControl.Down => new Vector2(0.064775f, 0.004025f),
                PSPConsoleControl.Left => new Vector2(0.071575f, 0.010825f),
                PSPConsoleControl.Right => new Vector2(0.057975f, 0.010825f),
                PSPConsoleControl.DPadCenter => new Vector2(0.064775f, 0.010825f),
                _ => GetDefinitionCenter(control)
            };
            return new Vector3(center.X, center.Y, 0f);
        }

        static Vector2 GetDefinitionCenter(PSPConsoleControl control) {
            foreach (PSPConsoleControlDefinition definition in Definitions) {
                if (definition.Control == control) return definition.ModelCenter;
            }
            return Vector2.Zero;
        }
    }

}
