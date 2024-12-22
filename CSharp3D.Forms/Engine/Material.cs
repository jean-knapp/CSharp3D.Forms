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

            //Texture albedoMap = rendererControl.Scene.GetTexture(AlbedoMapPath);
            Texture albedoMap = this.Albedo;

            if (albedoMap != null && albedoMap.Bitmap != null)
            {
                // Bind the texture
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, albedoMap.GetTextureId(context, scene));
                GL.Uniform1(GL.GetUniformLocation(Shader.GetShaderId(context, scene), "diffuseTexture"), 0);
            }

            if (Specular != null && Specular.Bitmap != null) 
            {
                // Bind the texture
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.BindTexture(TextureTarget.Texture2D, Specular.GetTextureId(context, scene));
                GL.Uniform1(GL.GetUniformLocation(Shader.GetShaderId(context, scene), "specularTexture"), 1);
            }

            if (Normal != null && Normal.Bitmap != null) 
            {
                // Bind the texture
                GL.ActiveTexture(TextureUnit.Texture2);
                GL.BindTexture(TextureTarget.Texture2D, Normal.GetTextureId(context, scene));
                GL.Uniform1(GL.GetUniformLocation(Shader.GetShaderId(context, scene), "normalTexture"), 2);
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
