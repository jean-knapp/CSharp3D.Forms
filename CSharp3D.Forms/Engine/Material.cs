using CSharp3D.Forms.Controls;
using OpenTK.Graphics.OpenGL;
using System.ComponentModel;
using System.Drawing;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// A material that can be used by meshes.
    /// </summary>
    [Description("A material that can be used by meshes.")]
    public class Material : Component
    {
        /// <summary>
        /// The name of the shader to be used by the material.
        /// </summary>
        [Category("Basics")]
        [Description("The shader folder's name, under the path set in the Scene.")]
        public string ShaderName { get; set; } = "Generic";

        /// <summary>
        /// The albedo texture.
        /// </summary>
        [Category("Basics")]
        [Description("The albedo texture.")]
        public Texture Albedo { get; set; } = null;

        /// <summary>
        /// A second albedo blended over <see cref="Albedo"/> by a mesh's per-vertex blend weight
        /// (Source's WorldVertexTransition: $basetexture at weight 0, $basetexture2 at 1). Only a
        /// mesh that carries blend weights - <see cref="Meshes.IndexedMesh.BlendWeights"/> - uses it.
        /// </summary>
        [Category("Basics")]
        [Description("A second albedo blended in by the mesh's per-vertex blend weights.")]
        public Texture Albedo2 { get; set; } = null;

        /// <summary>
        /// The color to be applied on the material. White means no change.
        /// </summary>
        [Category("Adjustment")]
        [Description("The color to be applied on the material. White means no change. The alpha is ignored.")]
        public Color Color { get; set; } = Color.White;

        private float _alpha = 1.0f;

        /// <summary>
        /// The transparency of the material, from 0.0 to 1.0.
        /// </summary>
        [Category("Transparency")]
        [Description("The transparency of the material, from 0.0 to 1.0.")]
        public float Alpha
        {
            get => _alpha;
            set
            {
                if (value < 0.0f)
                    _alpha = 0.0f;
                else if (value > 1.0f)
                    _alpha = 1.0f;
                else
                    _alpha = value;
            }
        }

        /// <summary>
        /// If true, the back faces of the mesh won't be rendered.
        /// </summary>
        [Category("Transparency")]
        [Description("If true, the back faces of the mesh won't be rendered.")]
        public bool BackCulling { get; set; } = true;

        /// <summary>
        /// Whether to use the alpha channel of the Albedo Texture as transparency data.
        /// </summary>
        [Category("Transparency")]
        [Description("Whether to use the alpha channel of the Albedo Texture as transparency data.")]
        public bool Translucent { get; set; } = false;

        /// <summary>
        /// If true, the material will be rendered additively (black is full transparent). The alpha channel is ignored.
        /// </summary>
        [Category("Transparency")]
        [Description("If true, the material will be rendered additively (black is full transparent). The alpha channel is ignored.")]
        public bool Additive { get; set; } = false;

        /// <summary>
        /// If true, texels whose albedo alpha is below <see cref="AlphaTestReference"/> are discarded
        /// (a cutout, Source's $alphatest). Unlike <see cref="Translucent"/> the mesh stays in the
        /// opaque pass and writes depth, so fences and foliage sort correctly.
        /// </summary>
        [Category("Transparency")]
        [Description("If true, texels whose albedo alpha is below AlphaTestReference are discarded (a cutout).")]
        public bool AlphaTest { get; set; } = false;

        /// <summary>
        /// The alpha below which an <see cref="AlphaTest"/> material discards a texel, from 0.0 to 1.0.
        /// </summary>
        [Category("Transparency")]
        [Description("The alpha below which an AlphaTest material discards a texel.")]
        public float AlphaTestReference { get; set; } = 0.5f;

        /// <summary>
        /// If true, the material won't be affected by lights. The albedo texture or base color will be rendered at original colors.
        /// </summary>
        [Category("Lighting")]
        [Description("If true, the material won't be affected by lights. The albedo texture or base color will be rendered at original colors.")]
        public bool Unlit { get; set; } = false;

        /// <summary>
        /// The normal map.
        /// </summary>
        [Category("Lighting")]
        [Description("The normal map.")]
        public Texture Normal { get; set; } = null;

        private float _specularStrength = 0.0f;

        /// <summary>
        /// The strength of the specular reflection. 0.0 means no reflection.
        /// </summary>
        [Category("Reflection")]
        [Description("The strength of the specular reflection. 0.0 means no reflection.")]
        public float SpecularStrength
        {
            get => _specularStrength;
            set
            {
                if (value < 0.0f)
                    _specularStrength = 0.0f; // Clamp to 0.0 if a negative value is provided
                else
                    _specularStrength = value;
            }
        }

        /// <summary>
        /// The specular reflection color.
        /// </summary>
        [Category("Reflection")]
        [Description("The specular reflection map.")]
        public Texture Specular { get; set; } = null;

        /// <summary>
        /// AddSelf
        /// </summary>
        [Category("Sprite")]
        public float AddSelf { get; set; } = 0;

        /// <summary>
        /// OverbrightFactor
        /// </summary>
        [Category("Sprite")]
        public float OverbrightFactor { get; set; } = 1;

        [Browsable(false)]
        public Shader Shader { get; private set; } = null;

        public Material()
        {

        }

        /// <summary>
        /// Bind the material to a shader in the current context
        /// </summary>
        /// <param name="context"> The context of the renderer control </param>
        /// <param name="scene"> The scene </param>
        public void Use(object context, Scene scene)
        {
            // Get the shader
            Shader = scene.GetShader(ShaderName);
            Shader.Use(context, scene);

            // The sampler units never change, so they are set once per program.
            Shader.UniformSet uniforms = Shader.GetUniforms(context, Shader.GetShaderId(context, scene));

            if (!uniforms.SamplersBound)
            {
                if (uniforms.DiffuseTexture >= 0) GL.Uniform1(uniforms.DiffuseTexture, 0);
                if (uniforms.SpecularTexture >= 0) GL.Uniform1(uniforms.SpecularTexture, 1);
                if (uniforms.NormalTexture >= 0) GL.Uniform1(uniforms.NormalTexture, 2);
                if (uniforms.BlendTexture >= 0) GL.Uniform1(uniforms.BlendTexture, 4);
                uniforms.SamplersBound = true;
            }

            //Texture albedoMap = rendererControl.Scene.GetTexture(AlbedoMapPath);
            Texture albedoMap = this.Albedo;

            if (albedoMap != null && albedoMap.Bitmap != null)
            {
                // Bind the texture
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, albedoMap.GetTextureId(context, scene));
            }

            if (Specular != null && Specular.Bitmap != null)
            {
                // Bind the texture
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.BindTexture(TextureTarget.Texture2D, Specular.GetTextureId(context, scene));
            }

            if (Normal != null && Normal.Bitmap != null)
            {
                // Bind the texture
                GL.ActiveTexture(TextureUnit.Texture2);
                GL.BindTexture(TextureTarget.Texture2D, Normal.GetTextureId(context, scene));
            }

            // Unit 3 is the lightmap's (QuakeFaceMesh); the blend texture takes the next one.
            if (Albedo2 != null && Albedo2.Bitmap != null)
            {
                GL.ActiveTexture(TextureUnit.Texture4);
                GL.BindTexture(TextureTarget.Texture2D, Albedo2.GetTextureId(context, scene));
                GL.ActiveTexture(TextureUnit.Texture0);
            }

            if (BackCulling)
            {
                GL.Enable(EnableCap.CullFace);
                GL.CullFace(CullFaceMode.Back);
            }
            else
            {
                GL.Disable(EnableCap.CullFace);
            }

            if (Additive)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            }
            else if (Translucent || Alpha < 1)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }
            else
            {
                GL.Disable(EnableCap.Blend);
            }
        }
    }
}
