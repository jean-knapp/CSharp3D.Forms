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
            this.orbitalCamera1 = new CSharp3D.Forms.Cameras.OrbitalCamera();
            this.scene1 = new CSharp3D.Forms.Engine.Scene();
            this.gridMesh1 = new CSharp3D.Forms.Meshes.GridMesh();
            this.orbitalCamera2 = new CSharp3D.Forms.Cameras.OrbitalCamera();
            this.gltfMesh1 = new CSharp3D.Forms.Meshes.GLTFMesh();
            this.material1 = new CSharp3D.Forms.Engine.Material();
            this.pointLight1 = new CSharp3D.Forms.Lights.PointLight();
            ((System.ComponentModel.ISupportInitialize)(this.gridMesh1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gltfMesh1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pointLight1)).BeginInit();
            this.SuspendLayout();
            // 
            // rendererControl1
            // 
            this.rendererControl1.AutoInvalidate = true;
            this.rendererControl1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.rendererControl1.Camera = this.orbitalCamera1;
            this.rendererControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rendererControl1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.rendererControl1.Location = new System.Drawing.Point(0, 0);
            this.rendererControl1.Name = "rendererControl1";
            this.rendererControl1.Scene = this.scene1;
            this.rendererControl1.Size = new System.Drawing.Size(800, 450);
            this.rendererControl1.TabIndex = 0;
            // 
            // orbitalCamera1
            // 
            this.orbitalCamera1.ClampVertically = false;
            this.orbitalCamera1.Distance = 4F;
            this.orbitalCamera1.FarPlane = 1000F;
            this.orbitalCamera1.FOV = 90F;
            this.orbitalCamera1.NearPlane = 0.1F;
            this.orbitalCamera1.Rotation = ((CSharp3D.Forms.Engine.RotationVector)(resources.GetObject("orbitalCamera1.Rotation")));
            // 
            // scene1
            // 
            this.scene1.AmbientColor = System.Drawing.Color.White;
            this.scene1.AmbientIntensity = 0.5F;
            this.scene1.ShaderDirectory = "shaders/";
            // 
            // gridMesh1
            // 
            this.gridMesh1.Clickable = false;
            this.gridMesh1.LineInterval = 1F;
            this.gridMesh1.Location = ((CSharp3D.Forms.Engine.LocationVector)(resources.GetObject("gridMesh1.Location")));
            this.gridMesh1.Rotation = ((CSharp3D.Forms.Engine.RotationVector)(resources.GetObject("gridMesh1.Rotation")));
            this.gridMesh1.Scene = this.scene1;
            this.gridMesh1.Size = 4F;
            this.gridMesh1.Vertices = new CSharp3D.Forms.Engine.LocationVector[0];
            // 
            // orbitalCamera2
            // 
            this.orbitalCamera2.ClampVertically = false;
            this.orbitalCamera2.Distance = 4F;
            this.orbitalCamera2.FarPlane = 1000F;
            this.orbitalCamera2.FOV = 90F;
            this.orbitalCamera2.NearPlane = 0.1F;
            this.orbitalCamera2.Rotation = ((CSharp3D.Forms.Engine.RotationVector)(resources.GetObject("orbitalCamera2.Rotation")));
            // 
            // gltfMesh1
            // 
            this.gltfMesh1.Clickable = false;
            this.gltfMesh1.FilePath = "Models/A-29.gltf";
            this.gltfMesh1.Location = ((CSharp3D.Forms.Engine.LocationVector)(resources.GetObject("gltfMesh1.Location")));
            this.gltfMesh1.Material = this.material1;
            this.gltfMesh1.Rotation = ((CSharp3D.Forms.Engine.RotationVector)(resources.GetObject("gltfMesh1.Rotation")));
            this.gltfMesh1.Scene = this.scene1;
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
            // pointLight1
            // 
            this.pointLight1.Color = System.Drawing.Color.White;
            this.pointLight1.Constant = 0F;
            this.pointLight1.Intensity = 10F;
            this.pointLight1.Linear = 0F;
            this.pointLight1.Location = ((CSharp3D.Forms.Engine.LocationVector)(resources.GetObject("pointLight1.Location")));
            this.pointLight1.Quadratic = 1F;
            this.pointLight1.Scene = this.scene1;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.rendererControl1);
            this.Name = "Form1";
            this.Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)(this.gridMesh1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gltfMesh1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pointLight1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private CSharp3D.Forms.Controls.RendererControl rendererControl1;
        private CSharp3D.Forms.Cameras.OrbitalCamera orbitalCamera1;
        private CSharp3D.Forms.Engine.Scene scene1;
        private CSharp3D.Forms.Meshes.GridMesh gridMesh1;
        private CSharp3D.Forms.Cameras.OrbitalCamera orbitalCamera2;
        private CSharp3D.Forms.Meshes.GLTFMesh gltfMesh1;
        private CSharp3D.Forms.Engine.Material material1;
        private CSharp3D.Forms.Lights.PointLight pointLight1;
    }
}