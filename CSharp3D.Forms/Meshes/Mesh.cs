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
        [TypeConverter(typeof(Vector3TypeConverter))]
        [Description("The position of the origin of the mesh, in World units (X, Y, Z).")]
        public Vector3 Location { get; set; }

        /// <summary>
        /// X is the roll clockwise, Y is the pitch down, Z is the yaw counter-clockwise.
        /// </summary>
        [Category("Mesh")]
        [TypeConverter(typeof(Vector3TypeConverter))]
        [Description("The rotation of the mesh, in degrees (Roll, Pitch, Yaw).")]
        public Vector3 Rotation { get; set; }

        [Category("Mesh")]
        [Description("The material of the mesh.")]
        public Material Material { get; set; } = null;

        protected PrimitiveType PrimitiveType { get; set; } = PrimitiveType.Triangles;

        [Category("Mesh")]
        [Description("Whether the mesh can be picked by a mouse click.")]
        public bool Clickable { get; set; } = false;

        public Mesh()
        {
            Location = new Vector3(0, 0, 0);
            Rotation = new Vector3(0, 0, 0);
        }

        public Mesh(Vector3 location, Vector3 rotation)
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
        /// Setup the mesh.
        /// </summary>
        /// <param name="context"></param>
        public void SetupMesh(object context)
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
        public void DrawMesh(object context, Scene scene, Matrix4 projection, Matrix4 view)
        {
            try
            {
                if (!IsVertexDataLoaded(context))
                {
                    SetupMesh(context);
                }

                Material.Use(context, scene);

                int shaderProgram = Material.Shader.GetShaderId(context, scene);

                int useDiffuseTextureLocation = GL.GetUniformLocation(shaderProgram, "uUseDiffuseTexture");
                GL.Uniform1(useDiffuseTextureLocation, Material.Albedo != null && Material.Albedo.Bitmap != null ? 1 : 0);

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
                if (scene.Lights.Count > 0)
                {
                    PointLight pointLight = scene.Lights[0];
                    GL.Uniform3(lightPositionLocation, VectorOrientation.ToGL(pointLight.Location));
                    GL.Uniform4(lightColorLocation, Material.Unlit ? new Vector4(1, 1, 1, 0) : new Vector4(pointLight.Color.R / 255f, pointLight.Color.G / 255f, pointLight.Color.B / 255f, pointLight.Intensity));
                    GL.Uniform4(ambientColorLocation, Material.Unlit ? new Vector4(1, 1, 1, 1) : new Vector4(scene.AmbientColor.R / 255f, scene.AmbientColor.G / 255f, scene.AmbientColor.B / 255f, scene.AmbientIntensity));
                    GL.Uniform3(lightAttenuationLocation, new Vector3(pointLight.Quadratic, pointLight.Linear, pointLight.Constant));
                }
                else
                {
                    GL.Uniform3(lightPositionLocation, VectorOrientation.ToGL(new Vector3(0, 0, 0)));
                    GL.Uniform4(lightColorLocation, new Vector4(1.0f, 1.0f, 1.0f, 0.0f));
                    GL.Uniform4(ambientColorLocation, Material.Unlit ? new Vector4(1, 1, 1, 1) : new Vector4(scene.AmbientColor.R / 255f, scene.AmbientColor.G / 255f, scene.AmbientColor.B / 255f, scene.AmbientIntensity));
                    GL.Uniform3(lightAttenuationLocation, new Vector3(1.0f, 0.0f, 0.0f));
                }

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

                GL.BindVertexArray(vao[context]);
                GL.DrawElements(PrimitiveType, GetIndexArray().Length, DrawElementsType.UnsignedInt, 0);
                GL.BindVertexArray(0);
            } catch (AccessViolationException e)
            {

            }
        }

        /// <summary>
        /// Get the distance of the mesh's origin to the camera.
        /// </summary>
        /// <param name="cameraPosition"> The camera's position. </param>
        /// <returns> The distance of the mesh's origin to the camera. </returns>
        public float GetDistanceFromCamera(Vector3 cameraPosition)
        {
            Vector3 meshPosition = Location; // Assuming Location is the mesh's world position
            return (meshPosition - cameraPosition).Length;
        }

        public Vector3 BoxMin, BoxMax;  // store in your Mesh class

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

            mesh.BoxMin = VectorOrientation.ToWorld(min);
            mesh.BoxMax = VectorOrientation.ToWorld(max);
        }
    }
}
