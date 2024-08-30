using CSharp3D.Forms.Lights;
using CSharp3D.Forms.Meshes;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// Contains all the objects in a scene. Can be used by multiple RendererControls.
    /// </summary>
    [Description("Contains all the objects in a scene. Can be used by multiple RendererControls.")]
    public class Scene : Component
    {
        /// <summary>
        /// The directory where the shaders are located, relative to the executable directory.
        /// </summary>
        [Category("Scene")]
        [Description("The directory where the shaders are located, relative to the executable directory.")]
        public string ShaderDirectory { get; set; } = "";

        /// <summary>
        /// The constant color of the ambient light.
        /// </summary>
        [Category("Scene")]
        [Description("The constant color of the ambient light.")]
        public Color AmbientColor { get; set; } = Color.White;

        /// <summary>
        /// The constant intensity of the ambient light.
        /// </summary>
        [Category("Scene")]
        [Description("The constant intensity of the ambient light.")]
        public float AmbientIntensity { get; set; } = 0.0f;

        /// <summary>
        /// The list of lights in the scene.
        /// </summary>
        public List<PointLight> Lights = new List<PointLight>();

        /// <summary>
        /// The list of meshes in the scene.
        /// </summary>
        public List<Mesh> Meshes = new List<Mesh>();

        private Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();

        /// <summary>
        /// Disposes of all the shaders and meshes in the scene.
        /// </summary>
        /// <param name="disposing"> Whether the object is being disposed of. </param>
        /// <param name="context"> The context to dispose the objects in. </param>
        public void Dispose(bool disposing, object context)
        {
            if (disposing)
            {
                foreach (var shader in Shaders.Values)
                {
                    shader.Dispose(context);
                }

                foreach (var mesh in Meshes)
                {
                    mesh.Dispose(context);
                }
            }
        }

        public void LoadShader(string shaderName)
        {
            Shader shader = new Shader(shaderName);
            Shaders.Add(shaderName, shader);
        }

        public Shader GetShader(string shaderName)
        {
            if (Shaders.ContainsKey(shaderName))
            {
                return Shaders[shaderName];
            }
            else
            {
                LoadShader(shaderName);
                return Shaders[shaderName];
            }
        }
    }
}
