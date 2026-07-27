using CSharp3D.Forms.Cameras;
using CSharp3D.Forms.Controls;
using CSharp3D.Forms.Engine.Helpers;
using CSharp3D.Forms.Lights;
using CSharp3D.Forms.Meshes;
using CSharp3D.Forms.Utils;
using OpenTK;
using OpenTK.Graphics.OpenGL;
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
        /// Draw textured geometry at full brightness, ignoring the scene's lights and
        /// ambient term.
        ///
        /// For editor viewports, where the texture itself is the information and dynamic
        /// lighting only darkens it — Hammer's textured 3D view works this way. Baked
        /// lightmaps are unaffected: they replace the lighting entirely and still win, so
        /// a lighting preview keeps showing real light with this set.
        /// </summary>
        [Category("Scene")]
        [Description("Draw textured geometry at full brightness, ignoring scene lights and ambient. Baked lightmaps still apply.")]
        public bool FullBright { get; set; } = false;

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
        /// The scene's directional light — its sun. Null (the default) means no sun, and the
        /// scene lights exactly as it did before directional lights existed.
        ///
        /// There is one rather than a list because a directional light has no position: a
        /// second one would just be a second global tint, which the ambient term already
        /// covers. Source models the same thing with a single <c>light_environment</c>.
        /// </summary>
        [Category("Scene")]
        [Description("The scene's directional light (its sun). Null for no sun.")]
        public DirectionalLight Sun { get; set; }

        /// <summary>
        /// The list of meshes in the scene.
        /// </summary>
        public List<Mesh> Meshes = new List<Mesh>();

        private Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();

        // ==================== deferred GPU resource release ====================

        /// <summary>
        /// GL object ids belonging to meshes that have been dropped from the scene,
        /// queued per context until that context is current and can delete them.
        ///
        /// A GL delete only acts on the context current on the calling thread, so a
        /// scene rebuild cannot simply call <see cref="Mesh.Dispose(object)"/> against
        /// four view contexts — it would free ids belonging to whichever one happens to
        /// be live. The previous answer to that was to not free anything at all, which
        /// leaks every buffer of every rebuild: on a large map one selection change
        /// orphans tens of thousands of VAOs and buffers per context, and after a few
        /// clicks the driver is managing millions of them and <c>glBufferData</c> grinds
        /// to a halt. Queuing per context and flushing during that context's own paint
        /// is the correct middle ground.
        /// </summary>
        private readonly Dictionary<object, List<int>> _pendingVaoDeletes = new Dictionary<object, List<int>>();

        private readonly Dictionary<object, List<int>> _pendingBufferDeletes = new Dictionary<object, List<int>>();

        /// <summary>
        /// Hand a dropped mesh's GPU resources over for deletion. The mesh is left with
        /// no buffers, so if it is ever drawn again it simply re-uploads.
        /// Call on the UI thread, like the rest of scene mutation.
        /// </summary>
        public void ScheduleDispose(Mesh mesh)
        {
            if (mesh == null)
                return;

            Queue(_pendingVaoDeletes, mesh.vao);
            Queue(_pendingBufferDeletes, mesh.vbo);
            Queue(_pendingBufferDeletes, mesh.ebo);

            mesh.ForgetGpuResources();
        }

        private static void Queue(Dictionary<object, List<int>> target, Dictionary<object, int> ids)
        {
            foreach (KeyValuePair<object, int> entry in ids)
            {
                if (entry.Value == 0)
                    continue;

                List<int> list;
                if (!target.TryGetValue(entry.Key, out list))
                {
                    list = new List<int>();
                    target[entry.Key] = list;
                }

                list.Add(entry.Value);
            }
        }

        /// <summary>
        /// Delete everything queued for this context. MUST be called with
        /// <paramref name="context"/> current — i.e. from that view's paint.
        /// </summary>
        public void ProcessPendingDeletions(object context)
        {
            List<int> vaos;
            if (_pendingVaoDeletes.TryGetValue(context, out vaos) && vaos.Count > 0)
            {
                foreach (int id in vaos)
                    GL.DeleteVertexArray(id);

                vaos.Clear();
            }

            List<int> buffers;
            if (_pendingBufferDeletes.TryGetValue(context, out buffers) && buffers.Count > 0)
            {
                foreach (int id in buffers)
                    GL.DeleteBuffer(id);

                buffers.Clear();
            }
        }

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
