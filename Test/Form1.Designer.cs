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
            this.freeLookCamera1 = new CSharp3D.Forms.Cameras.FreeLookCamera();
            this.ortographicCamera1 = new CSharp3D.Forms.Cameras.OrtographicCamera();
            this.cuboidMesh1 = new CSharp3D.Forms.Meshes.CuboidMesh();
            this.material = new CSharp3D.Forms.Engine.Material();
            this.albedoTexture = new CSharp3D.Forms.Engine.Texture();
            this.normalTexture = new CSharp3D.Forms.Engine.Texture();
            this.specularTexture = new CSharp3D.Forms.Engine.Texture();
            this.gridMesh1 = new CSharp3D.Forms.Meshes.GridMesh();
            this.pointLight1 = new CSharp3D.Forms.Lights.PointLight();
            ((System.ComponentModel.ISupportInitialize)(this.cuboidMesh1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridMesh1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pointLight1)).BeginInit();
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
            this.rendererControl1.Size = new System.Drawing.Size(810, 460);
            this.rendererControl1.TabIndex = 0;
            // 
            // orbitalCamera
            // 
            this.orbitalCamera.ClampVertically = false;
            this.orbitalCamera.Distance = 4F;
            this.orbitalCamera.FarPlane = 10000F;
            this.orbitalCamera.FOV = 90F;
            this.orbitalCamera.NearPlane = 0.1F;
            this.orbitalCamera.Rotation = ((OpenTK.Vector3)(resources.GetObject("orbitalCamera.Rotation")));
            // 
            // scene
            // 
            this.scene.AmbientColor = System.Drawing.Color.White;
            this.scene.AmbientIntensity = 0.2F;
            this.scene.ShaderDirectory = "Shaders/";
            // 
            // freeLookCamera1
            // 
            this.freeLookCamera1.ClampVertically = false;
            this.freeLookCamera1.FarPlane = 1000F;
            this.freeLookCamera1.FOV = 90F;
            this.freeLookCamera1.Location = ((OpenTK.Vector3)(resources.GetObject("freeLookCamera1.Location")));
            this.freeLookCamera1.MouseLook = false;
            this.freeLookCamera1.MoveSpeed = 5;
            this.freeLookCamera1.NearPlane = 0.1F;
            this.freeLookCamera1.Rotation = ((OpenTK.Vector3)(resources.GetObject("freeLookCamera1.Rotation")));
            // 
            // ortographicCamera1
            // 
            this.ortographicCamera1.ClampVertically = false;
            this.ortographicCamera1.FarPlane = 1000F;
            this.ortographicCamera1.FOV = 90F;
            this.ortographicCamera1.Location = ((OpenTK.Vector3)(resources.GetObject("ortographicCamera1.Location")));
            this.ortographicCamera1.MoveSpeed = 5;
            this.ortographicCamera1.NearPlane = 0.1F;
            this.ortographicCamera1.OrthoScale = 64F;
            this.ortographicCamera1.Rotation = ((OpenTK.Vector3)(resources.GetObject("ortographicCamera1.Rotation")));
            // 
            // cuboidMesh1
            // 
            this.cuboidMesh1.Clickable = true;
            this.cuboidMesh1.Location = ((OpenTK.Vector3)(resources.GetObject("cuboidMesh1.Location")));
            this.cuboidMesh1.Material = this.material;
            this.cuboidMesh1.Rotation = ((OpenTK.Vector3)(resources.GetObject("cuboidMesh1.Rotation")));
            this.cuboidMesh1.ScaleX = 2F;
            this.cuboidMesh1.ScaleY = 2F;
            this.cuboidMesh1.ScaleZ = 2F;
            this.cuboidMesh1.Scene = this.scene;
            // 
            // material
            // 
            this.material.Additive = false;
            this.material.AddSelf = 0F;
            this.material.Albedo = this.albedoTexture;
            this.material.Alpha = 1F;
            this.material.BackCulling = true;
            this.material.Color = System.Drawing.Color.White;
            this.material.Normal = null;
            this.material.OverbrightFactor = 1F;
            this.material.ShaderName = "NormalShaded";
            this.material.Specular = null;
            this.material.SpecularStrength = 1F;
            this.material.Translucent = false;
            this.material.Unlit = false;
            // 
            // albedoTexture
            // 
            this.albedoTexture.Bitmap = ((System.Drawing.Bitmap)(resources.GetObject("albedoTexture.Bitmap")));
            // 
            // normalTexture
            // 
            this.normalTexture.Bitmap = ((System.Drawing.Bitmap)(resources.GetObject("normalTexture.Bitmap")));
            // 
            // specularTexture
            // 
            this.specularTexture.Bitmap = ((System.Drawing.Bitmap)(resources.GetObject("specularTexture.Bitmap")));
            // 
            // gridMesh1
            // 
            this.gridMesh1.Clickable = true;
            this.gridMesh1.LineInterval = 1F;
            this.gridMesh1.Location = ((OpenTK.Vector3)(resources.GetObject("gridMesh1.Location")));
            this.gridMesh1.Rotation = ((OpenTK.Vector3)(resources.GetObject("gridMesh1.Rotation")));
            this.gridMesh1.Scene = this.scene;
            this.gridMesh1.Size = 4F;
            this.gridMesh1.Vertices = new OpenTK.Vector3[0];
            // 
            // pointLight1
            // 
            this.pointLight1.Color = System.Drawing.Color.White;
            this.pointLight1.Constant = 0F;
            this.pointLight1.Intensity = 7F;
            this.pointLight1.Linear = 0F;
            this.pointLight1.Location = ((OpenTK.Vector3)(resources.GetObject("pointLight1.Location")));
            this.pointLight1.Quadratic = 1F;
            this.pointLight1.Scene = this.scene;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(810, 460);
            this.Controls.Add(this.rendererControl1);
            this.Name = "Form1";
            this.Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)(this.cuboidMesh1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridMesh1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pointLight1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private CSharp3D.Forms.Controls.RendererControl rendererControl1;
        private CSharp3D.Forms.Cameras.OrbitalCamera orbitalCamera;
        private Scene scene;
        private CSharp3D.Forms.Meshes.CuboidMesh cuboidMesh1;
        private Material material;
        private CSharp3D.Forms.Meshes.GridMesh gridMesh1;
        private CSharp3D.Forms.Cameras.FreeLookCamera freeLookCamera1;
        private CSharp3D.Forms.Cameras.OrtographicCamera ortographicCamera1;
        private Texture albedoTexture;
        private Texture normalTexture;
        private Texture specularTexture;
        private CSharp3D.Forms.Lights.PointLight pointLight1;
    }
}

