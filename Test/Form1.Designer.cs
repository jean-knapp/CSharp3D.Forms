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
            this.cuboidMesh1 = new CSharp3D.Forms.Meshes.CuboidMesh();
            this.material1 = new CSharp3D.Forms.Engine.Material();
            this.gridMesh1 = new CSharp3D.Forms.Meshes.GridMesh();
            ((System.ComponentModel.ISupportInitialize)(this.cuboidMesh1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridMesh1)).BeginInit();
            this.SuspendLayout();
            // 
            // rendererControl1
            // 
            this.rendererControl1.AutoInvalidate = true;
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
            // cuboidMesh1
            // 
            this.cuboidMesh1.Location = ((OpenTK.Vector3)(resources.GetObject("cuboidMesh1.Location")));
            this.cuboidMesh1.Material = this.material1;
            this.cuboidMesh1.Clickable = true;
            this.cuboidMesh1.Rotation = ((OpenTK.Vector3)(resources.GetObject("cuboidMesh1.Rotation")));
            this.cuboidMesh1.ScaleX = 4F;
            this.cuboidMesh1.ScaleY = 1F;
            this.cuboidMesh1.ScaleZ = 1F;
            this.cuboidMesh1.Scene = this.scene;
            // 
            // material1
            // 
            this.material1.Additive = false;
            this.material1.AddSelf = 0F;
            this.material1.Albedo = null;
            this.material1.Alpha = 1F;
            this.material1.BackCulling = true;
            this.material1.Color = System.Drawing.Color.White;
            this.material1.Normal = null;
            this.material1.OverbrightFactor = 1F;
            this.material1.ShaderName = "Generic";
            this.material1.Specular = null;
            this.material1.SpecularStrength = 0F;
            this.material1.Translucent = false;
            this.material1.Unlit = false;
            // 
            // gridMesh1
            // 
            this.gridMesh1.LineInterval = 1F;
            this.gridMesh1.Location = ((OpenTK.Vector3)(resources.GetObject("gridMesh1.Location")));
            this.gridMesh1.Clickable = true;
            this.gridMesh1.Rotation = ((OpenTK.Vector3)(resources.GetObject("gridMesh1.Rotation")));
            this.gridMesh1.Scene = this.scene;
            this.gridMesh1.Size = 4F;
            this.gridMesh1.Vertices = new OpenTK.Vector3[0];
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.rendererControl1);
            this.Name = "Form1";
            this.Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)(this.cuboidMesh1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridMesh1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private CSharp3D.Forms.Controls.RendererControl rendererControl1;
        private CSharp3D.Forms.Cameras.OrbitalCamera orbitalCamera;
        private Scene scene;
        private CSharp3D.Forms.Meshes.CuboidMesh cuboidMesh1;
        private Material material1;
        private CSharp3D.Forms.Meshes.GridMesh gridMesh1;
    }
}

