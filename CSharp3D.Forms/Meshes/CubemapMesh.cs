using CSharp3D.Forms.Engine;
using System.ComponentModel;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// A cubemap mesh that can be used as a skybox with different textures for each face.
    /// </summary>
    [Description("A cubemap mesh for skybox rendering with individual face textures")]
    [ToolboxItem(true)]
    public class CubemapMesh : Component, ISupportInitialize
    {
        private bool _isInitialized;

        PolygonMesh frontMesh = new PolygonMesh();
        PolygonMesh backMesh = new PolygonMesh();
        PolygonMesh leftMesh = new PolygonMesh();
        PolygonMesh rightMesh = new PolygonMesh();
        PolygonMesh upMesh = new PolygonMesh();
        PolygonMesh downMesh = new PolygonMesh();

        [Category("Material")]
        [Description("The texture for the front face.")]
        public Texture Front
        {
            get => frontMesh.Material?.Albedo ?? null;
            set => frontMesh.Material.Albedo = value;
        }

        [Category("Material")]
        [Description("The texture for the back face.")]
        public Texture Back
        {
            get => backMesh.Material?.Albedo ?? null;
            set => backMesh.Material.Albedo = value;
        }

        [Category("Material")]
        [Description("The texture for the left face.")]
        public Texture Left
        {
            get => leftMesh.Material?.Albedo ?? null;
            set => leftMesh.Material.Albedo = value;
        }

        [Category("Material")]
        [Description("The texture for the right face.")]
        public Texture Right
        {
            get => rightMesh.Material?.Albedo ?? null;
            set => rightMesh.Material.Albedo = value;
        }

        [Category("Material")]
        [Description("The texture for the up face.")]
        public Texture Up
        {
            get => upMesh.Material?.Albedo ?? null;
            set => upMesh.Material.Albedo = value;
        }

        [Category("Material")]
        [Description("The texture for the down face.")]
        public Texture Down
        {
            get => downMesh.Material?.Albedo ?? null;
            set => downMesh.Material.Albedo = value;
        }

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

        public CubemapMesh()
        {
            frontMesh.Material = new CSharp3D.Forms.Engine.Material()
            {
                Unlit = true
            };
            backMesh.Material = new CSharp3D.Forms.Engine.Material()
            {
                Unlit = true
            };
            leftMesh.Material = new CSharp3D.Forms.Engine.Material()
            {
                Unlit = true
            };
            rightMesh.Material = new CSharp3D.Forms.Engine.Material()
            {
                Unlit = true
            };
            upMesh.Material = new CSharp3D.Forms.Engine.Material()
            {
                Unlit = true
            };
            downMesh.Material = new CSharp3D.Forms.Engine.Material()
            {
                Unlit = true
            };

            frontMesh.Vertices = new CSharp3D.Forms.Engine.Vertex[]
            {
                new CSharp3D.Forms.Engine.Vertex(1023, -1024, 1024, 1, 0),
                new CSharp3D.Forms.Engine.Vertex(1023, 1024, 1024, 0, 0),
                new CSharp3D.Forms.Engine.Vertex(1023, 1024, -1024, 0, 1),
                new CSharp3D.Forms.Engine.Vertex(1023, -1024, -1024, 1, 1),
            };
            backMesh.Vertices = new CSharp3D.Forms.Engine.Vertex[]
            {
                new CSharp3D.Forms.Engine.Vertex(-1023, -1024, -1024, 0, 1),
                new CSharp3D.Forms.Engine.Vertex(-1023, 1024, -1024, 1, 1),
                new CSharp3D.Forms.Engine.Vertex(-1023, 1024, 1024, 1, 0),
                new CSharp3D.Forms.Engine.Vertex(-1023, -1024, 1024, 0, 0),
            };
            leftMesh.Vertices = new CSharp3D.Forms.Engine.Vertex[]
            {
                new CSharp3D.Forms.Engine.Vertex(-1024, 1023, -1024, 0, 1),
                new CSharp3D.Forms.Engine.Vertex(1024, 1023, -1024, 1, 1),
                new CSharp3D.Forms.Engine.Vertex(1024, 1023, 1024, 1, 0),
                new CSharp3D.Forms.Engine.Vertex(-1024, 1023, 1024, 0, 0),
            };
            rightMesh.Vertices = new CSharp3D.Forms.Engine.Vertex[]
            {
                new CSharp3D.Forms.Engine.Vertex(-1024, -1023, 1024, 1, 0),
                new CSharp3D.Forms.Engine.Vertex(1024, -1023, 1024, 0, 0),
                new CSharp3D.Forms.Engine.Vertex(1024, -1023, -1024, 0, 1),
                new CSharp3D.Forms.Engine.Vertex(-1024, -1023, -1024, 1, 1),
            };
            upMesh.Vertices = new CSharp3D.Forms.Engine.Vertex[]
            {
                new CSharp3D.Forms.Engine.Vertex(1024, 1024, 1023, 0, 1),
                new CSharp3D.Forms.Engine.Vertex(1024, -1024, 1023, 1, 1),
                new CSharp3D.Forms.Engine.Vertex(-1024, -1024, 1023, 1, 0),
                new CSharp3D.Forms.Engine.Vertex(-1024, 1024, 1023, 0, 0),
            };
            downMesh.Vertices = new CSharp3D.Forms.Engine.Vertex[]
            {
                new CSharp3D.Forms.Engine.Vertex(-1024, 1024, -1023, 0, 0),
                new CSharp3D.Forms.Engine.Vertex(-1024, -1024, -1023, 1, 0),
                new CSharp3D.Forms.Engine.Vertex(1024, -1024, -1023, 1, 1),
                new CSharp3D.Forms.Engine.Vertex(1024, 1024, -1023, 0, 1),
            };
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
            frontMesh.Scene = Scene;
            Scene.Meshes.Add(frontMesh);
            Scene.Meshes.Add(leftMesh);
            Scene.Meshes.Add(backMesh);
            Scene.Meshes.Add(rightMesh);
            Scene.Meshes.Add(upMesh);
            Scene.Meshes.Add(downMesh);
        }
    }
}
