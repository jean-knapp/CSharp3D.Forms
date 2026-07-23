using CSharp3D.Forms.Cameras;
using CSharp3D.Forms.Controls;
using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Engine.Helpers;
using CSharp3D.Forms.Lights;
using CSharp3D.Forms.Utils;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// Represents a 3D mesh.
    /// </summary>
    [ToolboxItem(false)]
    public abstract class Mesh : Component, ISupportInitialize
    {
        private const int MAX_LIGHTS = 8; // Change this value to support more/fewer lights (must match shader MAX_LIGHTS)
        private bool _isInitialized;

        /// <summary>
        /// The scene that the mesh belongs to.
        /// </summary>
        [Category("Renderer")]
        [Description("The scene that the mesh belongs to.")]
        private Scene _scene;
        public Scene Scene
        {
            get => _scene;
            set
            {
                _scene = value;
                if (_isInitialized)
                {
                    InitializeComponent();
                }
            }
        }

        /// <summary>
        /// Contains the vertex array object.
        /// </summary>
        public Dictionary<object, int> vao = new Dictionary<object, int>();

        /// <summary>
        /// Contains the vertex data.
        /// </summary>
        public Dictionary<object, int> vbo = new Dictionary<object, int>();

        /// <summary>
        /// Contains the index data.
        /// </summary>
        public Dictionary<object, int> ebo = new Dictionary<object, int>();

        /// <summary>
        /// X is the east direction, Y is the north direction, Z is the up direction.
        /// </summary>
        [Category("Mesh")]
        [TypeConverter(typeof(LocationVectorTypeConverter))]
        [Description("The position of the origin of the mesh, in World units (X, Y, Z).")]
        public LocationVector Location { get; set; }

        /// <summary>
        /// X is the roll clockwise, Y is the pitch down, Z is the yaw counter-clockwise.
        /// </summary>
        [Category("Mesh")]
        [TypeConverter(typeof(RotationVectorTypeConverter))]
        [Description("The rotation of the mesh, in degrees (Roll, Pitch, Yaw).")]
        public RotationVector Rotation { get; set; }

        [Category("Mesh")]
        [Description("The material of the mesh.")]
        public Material Material { get; set; } = null;

        protected PrimitiveType PrimitiveType { get; set; } = PrimitiveType.Triangles;

        [Category("Mesh")]
        [Description("Whether the mesh can be picked by a mouse click.")]
        public bool Clickable { get; set; } = false;

        /// <summary>
        /// How the mesh interacts with the depth buffer. <see cref="MeshDepthMode.Normal"/> is the
        /// ordinary depth-tested draw. <see cref="MeshDepthMode.Overlay"/> ignores the depth test
        /// entirely, so the mesh draws in front of everything already rendered — what an editor's
        /// tool guides (selection boxes, handles) need to never disappear inside geometry.
        /// <see cref="MeshDepthMode.OccludedOnly"/> draws only where the mesh LOSES the depth test —
        /// the hidden-line half of a "solid where visible, dashed where hidden" selection overlay.
        /// Overlay and OccludedOnly never write depth (Overlay skips the test that gates writes;
        /// OccludedOnly masks writes off), so neither can corrupt the scene's own occlusion.
        /// </summary>
        [Category("Mesh")]
        [Description("How the mesh interacts with the depth buffer when drawn.")]
        public MeshDepthMode DepthMode { get; set; } = MeshDepthMode.Normal;

        /// <summary>
        /// Which views draw this mesh, judged by each view's <see cref="MeshDrawMode"/>
        /// (see <see cref="MeshViewFilter"/>). Default: every view.
        /// </summary>
        public MeshViewFilter ViewFilter { get; set; } = MeshViewFilter.All;

        /// <summary> Whether a view with the given draw mode should draw this mesh. </summary>
        public bool IsVisibleIn(MeshDrawMode viewDrawMode)
        {
            switch (ViewFilter)
            {
                case MeshViewFilter.WireframeViewsOnly:
                    return viewDrawMode == MeshDrawMode.Wireframe;

                case MeshViewFilter.ExceptWireframeViews:
                    return viewDrawMode != MeshDrawMode.Wireframe;

                default:
                    return true;
            }
        }

        public Mesh()
        {
            Location = new LocationVector(0, 0, 0);
            Rotation = new RotationVector(0, 0, 0);
        }

        public Mesh(LocationVector location, RotationVector rotation)
        {
            Location = location;
            Rotation = rotation;
        }

        public void BeginInit()
        {
            _isInitialized = false;
        }

        public void EndInit()
        {
            _isInitialized = true;
            InitializeComponent();
        }

        protected virtual void InitializeComponent()
        {
            _scene.Meshes.Add(this);
        }

        /// <summary>
        /// Deletes the mesh's OpenGL resources.
        /// </summary>
        /// <param name="context"> The context to delete the resources in. </param>
        public void Dispose(object context)
        {
            try
            {
                // The context is going away; it can no longer owe an upload, and its buffers are
                // about to be deleted so nothing is uploaded there any more.
                _staleVertexContexts.Remove(context);
                _uploadedIndexCounts.Remove(context);

                if (vao.ContainsKey(context) && vao[context] != 0)
                {
                    GL.DeleteVertexArray(vao[context]);
                    vao.Remove(context);
                }

                if (vbo.ContainsKey(context) && vbo[context] != 0)
                {
                    GL.DeleteBuffer(vbo[context]);
                    vbo.Remove(context);
                }

                if (ebo.ContainsKey(context) && ebo[context] != 0)
                {
                    GL.DeleteBuffer(ebo[context]);
                    ebo.Remove(context);
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions if needed, such as logging
                Console.WriteLine($"Failed to delete mesh resources: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the vertex array of the mesh.
        /// </summary>
        /// <returns> The vertex array of the mesh. </returns>
        public virtual float[] GetGLVertexArray()
        {
            // Vertex data with positions and texture coordinates
            float[] vertices = { };

            return vertices;
        }

        /// <summary>
        /// Get the index array of the mesh.
        /// </summary>
        /// <returns> The index array of the mesh. </returns>
        public virtual uint[] GetIndexArray()
        {
            // Index data
            uint[] indices = { };
            return indices;
        }

        /// <summary>
        /// Generate face normals for the mesh.
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        public float[] GenerateFaceNormals(float[] result)
        {
            // Face Normals
            var indices = GetIndexArray();
            for (int i = 0; i < indices.Length / 3; i++)
            {
                Vector3 v1 = new Vector3();
                Vector3 v2 = new Vector3();

                int a = (int)indices[3 * i];
                int b = (int)indices[3 * i + 1];
                int c = (int)indices[3 * i + 2];

                v1.X = result[a * 8 + 0] - result[b * 8 + 0];
                v1.Y = result[a * 8 + 1] - result[b * 8 + 1];
                v1.Z = result[a * 8 + 2] - result[b * 8 + 2];

                v2.X = result[b * 8 + 0] - result[c * 8 + 0];
                v2.Y = result[b * 8 + 1] - result[c * 8 + 1];
                v2.Z = result[b * 8 + 2] - result[c * 8 + 2];

                Vector3 normal = new Vector3();
                normal.X = v1.Y * v2.Z - v1.Z * v2.Y;
                normal.Y = v1.Z * v2.X - v1.X * v2.Z;
                normal.Z = v1.X * v2.Y - v1.Y * v2.X;

                // Normalize
                normal = normal.Normalized();

                result[a * 8 + 3] = normal.X;
                result[a * 8 + 4] = normal.Y;
                result[a * 8 + 5] = normal.Z;
                result[b * 8 + 3] = normal.X;
                result[b * 8 + 4] = normal.Y;
                result[b * 8 + 5] = normal.Z;
                result[c * 8 + 3] = normal.X;
                result[c * 8 + 4] = normal.Y;
                result[c * 8 + 5] = normal.Z;
            }

            return result;
        }

        /// <summary>
        /// Get the model matrix of the mesh.
        /// </summary>
        /// <param name="rendererControl"> The renderer control. </param>
        /// <returns> The model matrix of the mesh. </returns>
        public virtual Matrix4 GetModelMatrix(Matrix4 viewMatrix)
        {
            Vector3 location = VectorOrientation.ToGL(Location);
            Vector3 rotation = VectorOrientation.ToGL(Rotation) * MathHelper.Pi / 180f;

            Quaternion qPitch = Quaternion.FromAxisAngle(Vector3.UnitX, rotation.X);
            Quaternion qYaw = Quaternion.FromAxisAngle(Vector3.UnitY, rotation.Y);
            Quaternion qRoll = Quaternion.FromAxisAngle(Vector3.UnitZ, rotation.Z);

            return Matrix4.CreateFromQuaternion(qYaw * qPitch * qRoll) * Matrix4.CreateTranslation(location);
        }

        /// <summary>
        /// The contexts whose vertex buffers are stale. A mesh keeps one VBO per GL context (see
        /// <see cref="vbo"/>), so "needs updating" is per-context, not per-mesh: with several
        /// views on one scene — a quad viewport, say — a single shared flag would be consumed by
        /// whichever view painted first, leaving the rest showing stale geometry.
        /// </summary>
        private readonly HashSet<object> _staleVertexContexts = new HashSet<object>();

        /// <summary>
        /// Set to true after mutating the vertex data of an already-loaded mesh (e.g. CPU skinning,
        /// or an editor drag) to have every context re-upload its buffer on the next draw, instead
        /// of reusing the cached one. Reads true while any context is still stale.
        ///
        /// Contexts that have not loaded the mesh yet are not marked: their first draw runs
        /// <see cref="SetupMesh"/>, which uploads the current data anyway.
        /// </summary>
        public bool NeedsVertexUpdate
        {
            get { return _staleVertexContexts.Count > 0; }
            set
            {
                _staleVertexContexts.Clear();

                if (!value)
                    return;

                foreach (object context in vbo.Keys)
                    _staleVertexContexts.Add(context);
            }
        }

        /// <summary>
        /// Whether <paramref name="context"/> still has to re-upload this mesh's vertices.
        /// </summary>
        public bool IsVertexUpdatePending(object context)
        {
            return _staleVertexContexts.Contains(context);
        }

        /// <summary>
        /// Records that <paramref name="context"/>'s vertex buffer now matches the mesh data.
        /// <see cref="DrawMesh"/> calls this after re-uploading; a mesh that uploads its buffers
        /// itself should call it too. Only this context is affected — the others still owe theirs.
        /// </summary>
        public void MarkVertexBufferUpdated(object context)
        {
            _staleVertexContexts.Remove(context);
        }

        /// <summary>
        /// How many indices each context's element buffer currently holds, so a mesh whose
        /// topology grows or shrinks re-uploads it.
        /// </summary>
        private readonly Dictionary<object, int> _uploadedIndexCounts = new Dictionary<object, int>();

        /// <summary>
        /// Whether <paramref name="context"/>'s index buffer no longer matches the mesh.
        ///
        /// Most meshes keep a fixed topology and only move their vertices (CPU skinning, an editor
        /// drag), so the index buffer is uploaded once and left alone. A mesh whose vertex *count*
        /// changes — a grid that regenerates for the visible region, say — invalidates it: the draw
        /// call asks for as many indices as the mesh currently has, and a stale, shorter buffer
        /// silently drops the tail of the geometry.
        ///
        /// Note this compares counts, not contents: a mesh that reorders its indices without
        /// changing how many there are must be rebuilt rather than updated.
        /// </summary>
        public bool IsIndexUploadPending(object context)
        {
            int uploaded;
            if (!_uploadedIndexCounts.TryGetValue(context, out uploaded))
                return true;

            return uploaded != GetIndexArray().Length;
        }

        /// <summary> Records that <paramref name="context"/>'s index buffer matches the mesh. </summary>
        public void MarkIndexBufferUploaded(object context)
        {
            _uploadedIndexCounts[context] = GetIndexArray().Length;
        }

        /// <summary>
        /// Re-uploads the vertex data into the existing VBO for a context. Must run on the GL
        /// thread (DrawMesh does). Uses the standard 8-float layout; meshes with a custom layout
        /// override this alongside <see cref="SetupMesh"/>.
        /// </summary>
        protected virtual void UpdateVertexBuffer(object context)
        {
            if (!vbo.ContainsKey(context))
                return;

            float[] vertices = GetGLVertexArray();

            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo[context]);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            UpdateIndexBuffer(context);

            ComputeBoundingBox(this);
        }

        /// <summary>
        /// Re-uploads the element buffer when the mesh's topology no longer matches what this
        /// context holds. Skipped for the usual fixed-topology mesh, so per-frame vertex updates
        /// stay cheap.
        /// </summary>
        private void UpdateIndexBuffer(object context)
        {
            if (!ebo.ContainsKey(context) || !vao.ContainsKey(context))
                return;

            if (!IsIndexUploadPending(context))
                return;

            uint[] indices = GetIndexArray();

            // The element buffer binding belongs to the vertex array object, so bind the VAO
            // around this — binding it loose would not attach it to the mesh's VAO.
            GL.BindVertexArray(vao[context]);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo[context]);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.DynamicDraw);
            GL.BindVertexArray(0);

            MarkIndexBufferUploaded(context);
        }

        /// <summary>
        /// Setup the mesh. Virtual so meshes with a custom vertex layout or dynamic
        /// buffers (e.g. ParticleBatchMesh) can replace the fixed 8-float layout.
        /// </summary>
        /// <param name="context"></param>
        public virtual void SetupMesh(object context)
        {
            // Cube vertex data with positions and texture coordinates
            float[] vertices = GetGLVertexArray();
            uint[] indices = GetIndexArray();

            ComputeBoundingBox(this);

            vao[context] = GL.GenVertexArray();
            vbo[context] = GL.GenBuffer();
            ebo[context] = GL.GenBuffer();

            GL.BindVertexArray(vao[context]);

            // Bind vertex data
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo[context]);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            // Bind index data
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo[context]);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            // Position attribute
            int positionOffset = 0;
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), positionOffset);
            GL.EnableVertexAttribArray(0);

            // Normal attribute
            int normalOffset = positionOffset + 3 * sizeof(float);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), normalOffset);
            GL.EnableVertexAttribArray(1);

            // Texture coordinate attribute
            int texCoordOffset = normalOffset + 3 * sizeof(float);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), texCoordOffset);
            GL.EnableVertexAttribArray(2);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            // Remember the topology this context now holds, so a later change re-uploads it.
            MarkIndexBufferUploaded(context);
        }

        /// <summary>
        /// Check if the vertex data is loaded.
        /// </summary>
        /// <param name="context">The RendererControl's context</param>
        /// <returns>True if the vertex data is loaded, otherwise false.</returns>
        public bool IsVertexDataLoaded(object context)
        {
            return vao.ContainsKey(context);
        }

        /// <summary>
        /// Draw the mesh.
        /// </summary>
        /// <param name="context"> The RendererControl's context. </param>
        /// <param name="scene"> The scene. </param>
        /// <param name="projection"> The projection matrix. </param>
        /// <param name="view"> The view matrix. </param>
        public virtual void DrawMesh(object context, Scene scene, Matrix4 projection, Matrix4 view,
            MeshDrawMode drawMode = MeshDrawMode.Textured)
        {
            try
            {
                if (!IsVertexDataLoaded(context))
                {
                    SetupMesh(context);
                }
                else if (IsVertexUpdatePending(context))
                {
                    // Re-upload vertex data in place (e.g. CPU skinning) without recreating the
                    // buffers. Runs on the GL thread since DrawMesh does. Only this context is
                    // marked clean — the other views still owe themselves an upload.
                    UpdateVertexBuffer(context);
                    MarkVertexBufferUpdated(context);
                }

                // Make sure the mesh has at least a basic material.
                if (Material == null)
                {
                    Material = new Material();
                }

                Material.Use(context, scene);

                int shaderProgram = Material.Shader.GetShaderId(context, scene);

                // Only the textured mode samples the albedo; solid and wireframe fall back to the
                // material's flat colour, which is what makes them readable.
                bool useAlbedo = drawMode == MeshDrawMode.Textured
                    && Material.Albedo != null && Material.Albedo.Bitmap != null;

                int useDiffuseTextureLocation = GL.GetUniformLocation(shaderProgram, "uUseDiffuseTexture");
                GL.Uniform1(useDiffuseTextureLocation, useAlbedo ? 1 : 0);

                int useNormalTextureLocation = GL.GetUniformLocation(shaderProgram, "uUseNormalTexture");
                GL.Uniform1(useNormalTextureLocation, Material.Normal != null && Material.Normal.Bitmap != null ? 1 : 0);

                int useSpecularTextureLocation = GL.GetUniformLocation(shaderProgram, "uUseSpecularTexture");
                GL.Uniform1(useSpecularTextureLocation, Material.Specular != null && Material.Specular.Bitmap != null ? 1 : 0);

                int specularStrengthLocation = GL.GetUniformLocation(shaderProgram, "uSpecularStrength");
                GL.Uniform1(specularStrengthLocation, Material.SpecularStrength);

                int addSelfLocation = GL.GetUniformLocation(shaderProgram, "uAddSelf");
                GL.Uniform1(addSelfLocation, Material.AddSelf);

                int overbrightFactorLocation = GL.GetUniformLocation(shaderProgram, "uOverbrightFactor");
                GL.Uniform1(overbrightFactorLocation, Material.OverbrightFactor);

                int colorLocation = GL.GetUniformLocation(shaderProgram, "uBaseColor");
                GL.Uniform4(colorLocation, new Vector4(Material.Color.R / 255f, Material.Color.G / 255f, Material.Color.B / 255f, Material.Alpha));

                int lightPositionLocation = GL.GetUniformLocation(shaderProgram, "uLightPosition");
                int lightColorLocation = GL.GetUniformLocation(shaderProgram, "uLightColor");
                int ambientColorLocation = GL.GetUniformLocation(shaderProgram, "uAmbientColor");
                int lightAttenuationLocation = GL.GetUniformLocation(shaderProgram, "uLightAttenuation");
                // Prepare arrays for MAX_LIGHTS light sources
                float[] lightPositions = new float[MAX_LIGHTS * 3]; // MAX_LIGHTS * 3 components
                float[] lightColors = new float[MAX_LIGHTS * 4];    // MAX_LIGHTS * 4 components
                float[] ambientColors = new float[MAX_LIGHTS * 4];  // MAX_LIGHTS * 4 components
                float[] lightAttenuations = new float[MAX_LIGHTS * 3]; // MAX_LIGHTS * 3 components

                // Set up all lights
                for (int i = 0; i < MAX_LIGHTS; i++)
                {
                    int posIndex = i * 3;
                    int colorIndex = i * 4;
                    
                    if (i < scene.Lights.Count)
                    {
                        // Light exists - use its properties
                        PointLight pointLight = scene.Lights[i];
                        Vector3 lightPos = VectorOrientation.ToGL(pointLight.Location);
                        lightPositions[posIndex] = lightPos.X; 
                        lightPositions[posIndex + 1] = lightPos.Y; 
                        lightPositions[posIndex + 2] = lightPos.Z;
                        
                        Vector4 lightCol = Material.Unlit ? new Vector4(1, 1, 1, 0) : new Vector4(pointLight.Color.R / 255f, pointLight.Color.G / 255f, pointLight.Color.B / 255f, pointLight.Intensity);
                        lightColors[colorIndex] = lightCol.X; 
                        lightColors[colorIndex + 1] = lightCol.Y; 
                        lightColors[colorIndex + 2] = lightCol.Z; 
                        lightColors[colorIndex + 3] = lightCol.W;
                        
                        Vector4 ambientCol = Material.Unlit ? new Vector4(1, 1, 1, 1) : new Vector4(scene.AmbientColor.R / 255f, scene.AmbientColor.G / 255f, scene.AmbientColor.B / 255f, scene.AmbientIntensity);
                        ambientColors[colorIndex] = ambientCol.X; 
                        ambientColors[colorIndex + 1] = ambientCol.Y; 
                        ambientColors[colorIndex + 2] = ambientCol.Z; 
                        ambientColors[colorIndex + 3] = ambientCol.W;
                        
                        Vector3 lightAtt = new Vector3(pointLight.Quadratic, pointLight.Linear, pointLight.Constant);
                        lightAttenuations[posIndex] = lightAtt.X; 
                        lightAttenuations[posIndex + 1] = lightAtt.Y; 
                        lightAttenuations[posIndex + 2] = lightAtt.Z;
                    }
                    else
                    {
                        // Light doesn't exist - set to "off"
                        lightPositions[posIndex] = 0.0f; 
                        lightPositions[posIndex + 1] = 0.0f; 
                        lightPositions[posIndex + 2] = 0.0f;
                        
                        lightColors[colorIndex] = 1.0f; 
                        lightColors[colorIndex + 1] = 1.0f; 
                        lightColors[colorIndex + 2] = 1.0f; 
                        lightColors[colorIndex + 3] = 0.0f; // Intensity = 0 means off
                        
                        Vector4 ambientCol = Material.Unlit ? new Vector4(1, 1, 1, 1) : new Vector4(scene.AmbientColor.R / 255f, scene.AmbientColor.G / 255f, scene.AmbientColor.B / 255f, scene.AmbientIntensity);
                        ambientColors[colorIndex] = ambientCol.X; 
                        ambientColors[colorIndex + 1] = ambientCol.Y; 
                        ambientColors[colorIndex + 2] = ambientCol.Z; 
                        ambientColors[colorIndex + 3] = ambientCol.W;
                        
                        lightAttenuations[posIndex] = 1.0f; 
                        lightAttenuations[posIndex + 1] = 0.0f; 
                        lightAttenuations[posIndex + 2] = 0.0f;
                    }
                }

                // Send all uniform data
                GL.Uniform3(lightPositionLocation, MAX_LIGHTS, lightPositions);
                GL.Uniform4(lightColorLocation, MAX_LIGHTS, lightColors);
                GL.Uniform4(ambientColorLocation, MAX_LIGHTS, ambientColors);
                GL.Uniform3(lightAttenuationLocation, MAX_LIGHTS, lightAttenuations);

                int cameraPositionLocation = GL.GetUniformLocation(shaderProgram, "uCameraPosition");
                var cameraLocation = Camera.GetLocation(view);
                GL.Uniform3(cameraPositionLocation, VectorOrientation.ToGL(cameraLocation));

                // Set the projection matrix
                int projLoc = GL.GetUniformLocation(shaderProgram, "uProjection");
                GL.UniformMatrix4(projLoc, false, ref projection);

                // Set the view matrix
                int viewLoc = GL.GetUniformLocation(shaderProgram, "uView");
                GL.UniformMatrix4(viewLoc, false, ref view);

                Matrix4 model = GetModelMatrix(view);
                int modelLoc = GL.GetUniformLocation(shaderProgram, "uModel");
                GL.UniformMatrix4(modelLoc, false, ref model);

                // Depth-mode override for tool overlays. Global GL state, so whatever was in effect
                // is captured and restored right after the draw — the next mesh shares this state
                // machine, and the renderer's translucent pass runs with the depth mask off.
                bool priorDepthMask = false;
                if (DepthMode == MeshDepthMode.Overlay)
                {
                    GL.Disable(EnableCap.DepthTest);
                }
                else if (DepthMode == MeshDepthMode.OccludedOnly)
                {
                    // Draw only where the mesh is BEHIND what is already there; keep depth writes
                    // off so the pass cannot punch holes into the scene's occlusion.
                    GL.GetBoolean(GetPName.DepthWritemask, out priorDepthMask);
                    GL.DepthFunc(DepthFunction.Greater);
                    GL.DepthMask(false);
                }

                GL.BindVertexArray(vao[context]);
                GL.DrawElements(PrimitiveType, GetIndexArray().Length, DrawElementsType.UnsignedInt, 0);
                GL.BindVertexArray(0);

                if (DepthMode == MeshDepthMode.Overlay)
                {
                    GL.Enable(EnableCap.DepthTest);
                }
                else if (DepthMode == MeshDepthMode.OccludedOnly)
                {
                    GL.DepthFunc(DepthFunction.Less);
                    GL.DepthMask(priorDepthMask);
                }
            } catch (AccessViolationException e)
            {

            }
        }

        /// <summary>
        /// Get the distance of the mesh's origin to the camera.
        /// </summary>
        /// <param name="cameraPosition"> The camera's position. </param>
        /// <returns> The distance of the mesh's origin to the camera. </returns>
        public float GetDistanceFromCamera(LocationVector cameraPosition)
        {
            LocationVector meshPosition = Location; // Assuming Location is the mesh's world position
            return (meshPosition - cameraPosition).Length;
        }

        public LocationVector BoxMin, BoxMax;  // store in your Mesh class

        private void ComputeBoundingBox(Mesh mesh)
        {
            float[] vertices = mesh.GetGLVertexArray();
            // Typically, each vertex has (posX, posY, posZ, normalX, normalY, normalZ, texU, texV)
            // so that's 8 floats per vertex. The positions are at indices [0,1,2].
            // 
            // If your mesh has scaling/rotation/translation, you can:
            //   1) get the local positions first,
            //   2) multiply them by mesh.GetModelMatrix(...) to get world positions,
            // or  3) if you prefer, compute an oriented bounding box (OBB) for the local vertices.
            // For a simpler AABB in world space, do something like:

            Matrix4 modelMatrix = mesh.GetModelMatrix(Matrix4.Identity);
            // modelMatrix includes your translation (Location), rotation, etc.

            // Initialize min & max
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            int vertexCount = vertices.Length / 8;
            for (int i = 0; i < vertexCount; i++)
            {
                // local vertex position
                float lx = vertices[i * 8 + 0];
                float ly = vertices[i * 8 + 1];
                float lz = vertices[i * 8 + 2];
                Vector4 localPos = new Vector4(lx, ly, lz, 1.0f);

                // transform to world space
                Vector4 worldPos = Vector4.Transform(localPos, modelMatrix);

                // update min & max
                if (worldPos.X < min.X) min.X = worldPos.X;
                if (worldPos.Y < min.Y) min.Y = worldPos.Y;
                if (worldPos.Z < min.Z) min.Z = worldPos.Z;

                if (worldPos.X > max.X) max.X = worldPos.X;
                if (worldPos.Y > max.Y) max.Y = worldPos.Y;
                if (worldPos.Z > max.Z) max.Z = worldPos.Z;
            }

            mesh.BoxMin = VectorOrientation.ToWorldLocation(min);
            mesh.BoxMax = VectorOrientation.ToWorldLocation(max);
        }
    }
}
