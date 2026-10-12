using System;
using System.Collections.Generic;
using Engine;
using Engine.Graphics;
using Engine.Media;

namespace Game {
    public class PSPBlock : Block {
        const float ModelGeometryScale = 4f;

        readonly List<MaterialMesh> m_materialMeshes = new();

        Model m_model;

        sealed class MaterialMesh {
            public BlockMesh Mesh = new();
            public Texture2D Texture;
            public Color Tint;
        }

        public override void Initialize() {
            m_model = ContentManager.Get<Model>("Models/PSP");
            Dictionary<int, MaterialMesh> materialMeshes = new();
            // The source model's front faces -Z and its D-pad is on +X. The 180°
            // item rotation turns the front toward the player and naturally keeps
            // the D-pad on screen-left, so the geometry itself must not be mirrored.
            Matrix modelScale = Matrix.CreateScale(ModelGeometryScale);

            foreach (ModelMesh modelMesh in m_model.Meshes) {
                Matrix modelTransform = BlockMesh.GetBoneAbsoluteTransform(modelMesh.ParentBone) * modelScale;
                foreach (ModelMeshPart meshPart in modelMesh.MeshParts) {
                    int materialIndex = meshPart.MaterialIndex;
                    if (!materialMeshes.TryGetValue(materialIndex, out MaterialMesh materialMesh)) {
                        ModelMaterial material = m_model.GetMaterial(materialIndex);
                        Texture2D texture = material?.BaseColorTexture?.HasTexture == true
                            ? m_model.GetTexture(material.BaseColorTexture.TextureIndex)
                            : null;
                        materialMesh = new MaterialMesh {
                            Texture = texture ?? Model.DefaultWhiteTexture,
                            Tint = material != null
                                ? CreateBaseColorTint(material.BaseColorFactor, material.Name == "Graphite ABS")
                                : Color.White
                        };
                        materialMeshes.Add(materialIndex, materialMesh);
                    }

                    BlockMesh partMesh = new();
                    partMesh.AppendModelMeshPart(
                        meshPart,
                        modelTransform,
                        false,
                        false,
                        true,
                        false,
                        Color.White
                    );
                    materialMesh.Mesh.AppendBlockMesh(partMesh);
                }
            }

            m_materialMeshes.AddRange(materialMeshes.Values);
            if (m_materialMeshes.Count == 0) {
                throw new InvalidOperationException("The PSP model has no renderable mesh parts.");
            }
            base.Initialize();
        }

        public override void GenerateTerrainVertices(BlockGeometryGenerator generator,
            TerrainGeometry geometry,
            int value,
            int x,
            int y,
            int z) { }

        public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer,
            int value,
            Color color,
            float size,
            ref Matrix matrix,
            DrawBlockEnvironmentData environmentData) {
            foreach (MaterialMesh materialMesh in m_materialMeshes) {
                Matrix drawMatrix = matrix;
                if (environmentData?.DrawBlockMode == DrawBlockMode.UI) {
                    drawMatrix = Matrix.CreateRotationY(MathUtils.DegToRad(180f)) * drawMatrix;
                }
                BlocksManager.DrawMeshBlock(
                    primitivesRenderer,
                    materialMesh.Mesh,
                    materialMesh.Texture,
                    color * materialMesh.Tint,
                    size,
                    ref drawMatrix,
                    environmentData,
                    GeometrySubsetType.Opaque
                );
            }
        }

        internal static Color CreateBaseColorTint(Vector4 linearColor, bool lightenShell) {
            // glTF base color factors are linear values, while Engine.Color stores
            // display-referred 8-bit channels. Convert before quantizing to avoid
            // graphite materials becoming nearly black in Survivalcraft.
            float red = LinearToSrgb(linearColor.X);
            float green = LinearToSrgb(linearColor.Y);
            float blue = LinearToSrgb(linearColor.Z);
            if (lightenShell) {
                // Make the main casing a readable light graphite; preserve the
                // darker rubber, buttons, trim, and display for contrast.
                const float lightenAmount = 0.45f;
                red = MathUtils.Lerp(red, 1f, lightenAmount);
                green = MathUtils.Lerp(green, 1f, lightenAmount);
                blue = MathUtils.Lerp(blue, 1f, lightenAmount);
            }
            return new Color(
                red,
                green,
                blue,
                MathUtils.Saturate(linearColor.W)
            );
        }

        static float LinearToSrgb(float value) {
            value = MathUtils.Saturate(value);
            return value <= 0.0031308f
                ? 12.92f * value
                : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
        }
    }
}
