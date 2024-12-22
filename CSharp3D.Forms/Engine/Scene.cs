using CSharp3D.Forms.Cameras;
using CSharp3D.Forms.Controls;
using CSharp3D.Forms.Engine.Helpers;
using CSharp3D.Forms.Lights;
using CSharp3D.Forms.Meshes;
using CSharp3D.Forms.Utils;
using OpenTK;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// Contains all the objects in a scene. Can be used by multiple RendererControls.
    /// </summary>
    [Description("Contains all the objects in a scene. Can be used by multiple RendererControls.")]
    public class Scene : Component
    {
        [Category("Interaction")]
        [Description("Occurs when a mesh is picked by the user.")]
        public event EventHandler<MeshEventArgs> OnMeshClicked;

        [Category("Interaction")]
        [Description("Occurs when the user clicks on the renderer control and no mesh was selected.")]
        public event EventHandler<EventArgs> OnVoidClicked;

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

        /// <summary>
        /// Create a picking ray and test against the scene's meshes using their bounding box.
        /// </summary>
        /// <param name="mouseEventArgs">The event arguments</param>
        internal void PickMesh(RendererControl control, Camera camera, MouseEventArgs mouseEventArgs)
        {
            // Now we have a "picking ray" in world space:
            Ray pickingRay = camera.GetPickingRay(control, mouseEventArgs);

            float closestT = float.MaxValue;
            Mesh pickedMesh = null;

            foreach (Mesh mesh in Meshes)
            {
                if (!mesh.Clickable)
                    continue;

                if (pickingRay.RayIntersectsAABB(
                        pickingRay.Origin,
                        pickingRay.Direction,
                        mesh.BoxMin,
                        mesh.BoxMax,
                        out float tNear))
                {
                    // tNear is how "far" along the ray the intersection occurred
                    if (tNear < closestT && tNear >= 0)
                    {
                        closestT = tNear;
                        pickedMesh = mesh;
                    }
                }
            }

            // If something was picked
            if (pickedMesh != null)
            {
                // For example, highlight or select
                Console.WriteLine("Clicked on mesh: " + pickedMesh);
                OnMeshClicked?.Invoke(control, new MeshEventArgs(pickedMesh));
            }
            else
            {
                Console.WriteLine("Clicked on nothing.");
                OnVoidClicked?.Invoke(control, mouseEventArgs);
            }
        }
    }
}
