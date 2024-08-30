using CSharp3D.Forms.Engine;

namespace Test
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.rendererControl1 = new CSharp3D.Forms.Controls.RendererControl();
            this.orbitalCamera = new CSharp3D.Forms.Cameras.OrbitalCamera();
            this.scene = new CSharp3D.Forms.Engine.Scene();
            this.material1 = new CSharp3D.Forms.Engine.Material();
            this.pointLight1 = new CSharp3D.Forms.Lights.PointLight();
            this.grid1 = new CSharp3D.Forms.Meshes.GridMesh();
            this.texture1 = new CSharp3D.Forms.Engine.Texture();
            this.material2 = new CSharp3D.Forms.Engine.Material();
            this.texture2 = new CSharp3D.Forms.Engine.Texture();
            this.texture3 = new CSharp3D.Forms.Engine.Texture();
            this.cuboid1 = new CSharp3D.Forms.Meshes.CuboidMesh();
            ((System.ComponentModel.ISupportInitialize)(this.pointLight1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.grid1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.cuboid1)).BeginInit();
            this.SuspendLayout();
            // 
            // rendererControl1
            // 
            this.rendererControl1.BackColor = System.Drawing.Color.Gray;
            this.rendererControl1.Camera = this.orbitalCamera;
            this.rendererControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rendererControl1.Location = new System.Drawing.Point(0, 0);
            this.rendererControl1.Name = "rendererControl1";
            this.rendererControl1.Scene = this.scene;
            this.rendererControl1.Size = new System.Drawing.Size(800, 450);
            this.rendererControl1.TabIndex = 0;
            // 
            // orbitalCamera
            // 
            this.orbitalCamera.ClampVertically = false;
            this.orbitalCamera.Distance = 4F;
            this.orbitalCamera.FarPlane = 10000F;
            this.orbitalCamera.FOV = 90F;
            this.orbitalCamera.NearPlane = 0.1F;
            this.orbitalCamera.Projection = CSharp3D.Forms.Cameras.Camera.Projections.Perspective;
            this.orbitalCamera.Rotation = ((OpenTK.Vector3)(resources.GetObject("orbitalCamera.Rotation")));
            // 
            // scene
            // 
            this.scene.AmbientColor = System.Drawing.Color.White;
            this.scene.AmbientIntensity = 0.2F;
            this.scene.ShaderDirectory = "Shaders/";
            // 
            // material1
            // 
            this.material1.Additive = false;
            this.material1.Albedo = null;
            this.material1.Alpha = 1F;
            this.material1.BackCulling = true;
            this.material1.Color = System.Drawing.Color.White;
            this.material1.Normal = null;
            this.material1.ShaderName = "Wireframe";
            this.material1.Specular = null;
            this.material1.SpecularStrength = 0F;
            this.material1.Translucent = false;
            this.material1.Unlit = false;
            // 
            // pointLight1
            // 
            this.pointLight1.Color = System.Drawing.Color.White;
            this.pointLight1.Constant = 0F;
            this.pointLight1.Intensity = 3F;
            this.pointLight1.Linear = 0F;
            this.pointLight1.Location = ((OpenTK.Vector3)(resources.GetObject("pointLight1.Location")));
            this.pointLight1.Quadratic = 1F;
            this.pointLight1.Scene = this.scene;
            // 
            // grid1
            // 
            this.grid1.LineInterval = 1F;
            this.grid1.Location = ((OpenTK.Vector3)(resources.GetObject("grid1.Location")));
            this.grid1.Material = this.material1;
            this.grid1.Rotation = ((OpenTK.Vector3)(resources.GetObject("grid1.Rotation")));
            this.grid1.Scene = this.scene;
            this.grid1.Size = 8F;
            this.grid1.Vertices = new OpenTK.Vector3[0];
            // 
            // texture1
            // 
            this.texture1.Bitmap = ((System.Drawing.Bitmap)(resources.GetObject("texture1.Bitmap")));
            // 
            // material2
            // 
            this.material2.Additive = false;
            this.material2.Albedo = null;
            this.material2.Alpha = 1F;
            this.material2.BackCulling = true;
            this.material2.Color = System.Drawing.Color.White;
            this.material2.Normal = this.texture2;
            this.material2.ShaderName = "Generic";
            this.material2.Specular = this.texture3;
            this.material2.SpecularStrength = 3F;
            this.material2.Translucent = false;
            this.material2.Unlit = false;
            // 
            // texture2
            // 
            this.texture2.Bitmap = ((System.Drawing.Bitmap)(resources.GetObject("texture2.Bitmap")));
            // 
            // texture3
            // 
            this.texture3.Bitmap = ((System.Drawing.Bitmap)(resources.GetObject("texture3.Bitmap")));
            // 
            // cuboid1
            // 
            this.cuboid1.Location = ((OpenTK.Vector3)(resources.GetObject("cuboid1.Location")));
            this.cuboid1.Material = this.material2;
            this.cuboid1.Rotation = ((OpenTK.Vector3)(resources.GetObject("cuboid1.Rotation")));
            this.cuboid1.ScaleX = 2F;
            this.cuboid1.ScaleY = 2F;
            this.cuboid1.ScaleZ = 1F;
            this.cuboid1.Scene = this.scene;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.rendererControl1);
            this.Name = "Form1";
            this.Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)(this.pointLight1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.grid1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.cuboid1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private CSharp3D.Forms.Controls.RendererControl rendererControl1;
        private CSharp3D.Forms.Cameras.OrbitalCamera orbitalCamera;
        private Scene scene;
        private CSharp3D.Forms.Lights.PointLight pointLight1;
        private CSharp3D.Forms.Meshes.GridMesh grid1;
        private CSharp3D.Forms.Engine.Material material1;
        private CSharp3D.Forms.Engine.Texture texture1;
        private CSharp3D.Forms.Engine.Material material2;
        private CSharp3D.Forms.Meshes.CuboidMesh cuboid1;
        private CSharp3D.Forms.Engine.Texture texture2;
        private CSharp3D.Forms.Engine.Texture texture3;
    }
}

