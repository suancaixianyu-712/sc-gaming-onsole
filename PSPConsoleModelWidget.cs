using System.Collections.Generic;
using Engine;
using Engine.Graphics;
using Engine.Media;

namespace Game {
    public sealed class PSPConsoleModelWidget : Widget {
        sealed class ConsoleMesh {
            public BlockMesh Mesh = new();
            public Texture2D Texture;
            public Color Tint;
            public PSPConsoleControl Control;
        }

        readonly List<ConsoleMesh> m_meshes = new();
        readonly DrawBlockEnvironmentData m_environment = new() { DrawBlockMode = DrawBlockMode.UI, Light = 15 };
        readonly PSPConsoleState m_state;

        public PSPConsoleModelWidget(PSPConsoleState state) {
            m_state = state;
            IsHitTestVisible = false;
            Model model = ContentManager.Get<Model>("Models/PSP");
            Dictionary<(PSPConsoleControl, int), ConsoleMesh> meshes = new();
            foreach (ModelMesh mesh in model.Meshes) {
                // glTF mesh names are generic (Cube, Cylinder). Object names live
                // on their parent nodes/bones and identify the physical controls.
                string objectName = mesh.ParentBone.Name;
                if (objectName == "LCD image") continue;
                PSPConsoleControl control = PSPConsoleControls.GetMeshControl(objectName);
                Matrix transform = BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone);
                foreach (ModelMeshPart part in mesh.MeshParts) {
                    (PSPConsoleControl, int) key = (control, part.MaterialIndex);
                    if (!meshes.TryGetValue(key, out ConsoleMesh consoleMesh)) {
                        ModelMaterial material = model.GetMaterial(part.MaterialIndex);
                        Texture2D texture = material?.BaseColorTexture?.HasTexture == true
                            ? model.GetTexture(material.BaseColorTexture.TextureIndex)
                            : null;
                        consoleMesh = new ConsoleMesh {
                            Texture = texture ?? Model.DefaultWhiteTexture,
                            Tint = material != null
                                ? PSPBlock.CreateBaseColorTint(material.BaseColorFactor, material.Name == "Graphite ABS")
                                : Color.White,
                            Control = control
                        };
                        meshes.Add(key, consoleMesh);
                    }
                    consoleMesh.Mesh.AppendModelMeshPart(part, transform, false, false, true, false, Color.White);
                }
            }
            m_meshes.AddRange(meshes.Values);
        }

        public override void MeasureOverride(Vector2 parentAvailableSize) {
            DesiredSize = parentAvailableSize;
            IsDrawRequired = true;
        }

        public override void Draw(DrawContext dc) {
            if (ActualSize.X <= 0f || ActualSize.Y <= 0f) return;
            Viewport viewport = Display.Viewport;
            Matrix view = Matrix.CreateLookAt(new Vector3(0f, 0f, 1f), Vector3.Zero, Vector3.UnitY);
            Matrix projection = Matrix.CreateOrthographic(PSPConsoleControls.ModelWidth, PSPConsoleControls.ModelHeight, -10f, 10f);
            Matrix pixelsToClip = MatrixUtils.CreateScaleTranslation(0.5f * ActualSize.X, -0.5f * ActualSize.Y, ActualSize.X / 2f, ActualSize.Y / 2f)
                * GlobalTransform
                * MatrixUtils.CreateScaleTranslation(2f / viewport.Width, -2f / viewport.Height, -1f, 1f);
            m_environment.ViewProjectionMatrix = view * projection * pixelsToClip;

            foreach (ConsoleMesh mesh in m_meshes) {
                float pressed = m_state.PressAmounts[(int)mesh.Control];
                Matrix matrix = Matrix.CreateRotationY(MathUtils.DegToRad(180f));
                Color tint = mesh.Tint * GlobalColorTransform;
                if (mesh.Control == PSPConsoleControl.Analog) {
                    // The PSP's analog nub slides in its circular gate; it is
                    // not a push button. Convert screen axes back to model axes.
                    Vector2 offset = m_state.AnalogOffset * PSPConsoleControls.AnalogTravel;
                    matrix = Matrix.CreateTranslation(-offset.X, offset.Y, 0f) * matrix;
                    tint = Color.MultiplyColorOnly(tint, 1f - 0.12f * pressed);
                }
                else if (pressed > 0f) {
                    Vector3 pivot = PSPConsoleControls.GetMeshPivot(mesh.Control);
                    bool shoulder = mesh.Control == PSPConsoleControl.LeftShoulder || mesh.Control == PSPConsoleControl.RightShoulder;
                    float scale = 1f - 0.08f * pressed;
                    Vector3 displacement = shoulder
                        ? new Vector3(0f, -0.0008f, 0.0002f) * pressed
                        : new Vector3(0f, -0.0002f, 0.00045f) * pressed;
                    matrix = Matrix.CreateTranslation(-pivot)
                        * Matrix.CreateScale(scale, scale, 1f)
                        * Matrix.CreateTranslation(pivot + displacement)
                        * matrix;
                    tint = Color.MultiplyColorOnly(tint, 1f - 0.25f * pressed);
                }
                BlocksManager.DrawMeshBlock(dc.PrimitivesRenderer3D, mesh.Mesh, mesh.Texture, tint, 1f, ref matrix, m_environment, GeometrySubsetType.Opaque);
            }
        }
    }

    public sealed class PSPConsoleControlWidget : ClickableWidget {
        readonly bool m_round;
        public PSPConsoleControl Control { get; }

        public PSPConsoleControlWidget(PSPConsoleControlDefinition definition) {
            Control = definition.Control;
            m_round = definition.Round;
            SoundName = "Audio/UI/ButtonClick";
        }

        public override void MeasureOverride(Vector2 parentAvailableSize) => DesiredSize = parentAvailableSize;

        public override bool HitTest(Vector2 point) {
            if (!base.HitTest(point)) return false;
            if (!m_round) return true;
            Vector2 local = ScreenToWidget(point) - ActualSize / 2f;
            Vector2 normalized = local / (ActualSize / 2f);
            return normalized.LengthSquared() <= 1f;
        }
    }
}
