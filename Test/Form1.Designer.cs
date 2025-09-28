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
            this.textureUp = new CSharp3D.Forms.Engine.Texture();
            this.gridMesh1 = new CSharp3D.Forms.Meshes.GridMesh();
            this.floorMesh = new CSharp3D.Forms.Meshes.CuboidMesh();
            this.northWallMesh = new CSharp3D.Forms.Meshes.CuboidMesh();
            this.eastWall = new CSharp3D.Forms.Meshes.CuboidMesh();
            this.southWall = new CSharp3D.Forms.Meshes.CuboidMesh();
            this.cuboidMesh1 = new CSharp3D.Forms.Meshes.CuboidMesh();
            this.material1 = new CSharp3D.Forms.Engine.Material();
            this.texture1 = new CSharp3D.Forms.Engine.Texture();
            this.texture2 = new CSharp3D.Forms.Engine.Texture();
            this.texture3 = new CSharp3D.Forms.Engine.Texture();
            this.pointLight1 = new CSharp3D.Forms.Lights.PointLight();
            this.pointLight2 = new CSharp3D.Forms.Lights.PointLight();
            this.pointLight3 = new CSharp3D.Forms.Lights.PointLight();
            ((System.ComponentModel.ISupportInitialize)(this.gridMesh1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.floorMesh)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.northWallMesh)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.eastWall)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.southWall)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.cuboidMesh1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pointLight1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pointLight2)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pointLight3)).BeginInit();
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
            this.scene1.AmbientIntensity = 0.2F;
            this.scene1.ShaderDirectory = "shaders/";
            // 
            // textureUp
            // 
            this.textureUp.Bitmap = ((System.Drawing.Bitmap)(resources.GetObject("textureUp.Bitmap")));
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
            // floorMesh
            // 
            this.floorMesh.Clickable = false;
            this.floorMesh.Location = ((CSharp3D.Forms.Engine.LocationVector)(resources.GetObject("floorMesh.Location")));
            this.floorMesh.Material = null;
            this.floorMesh.Rotation = ((CSharp3D.Forms.Engine.RotationVector)(resources.GetObject("floorMesh.Rotation")));
            this.floorMesh.ScaleX = 8F;
            this.floorMesh.ScaleY = 8F;
            this.floorMesh.ScaleZ = 1F;
            this.floorMesh.Scene = this.scene1;
            // 
            // northWallMesh
            // 
            this.northWallMesh.Clickable = false;
            this.northWallMesh.Location = ((CSharp3D.Forms.Engine.LocationVector)(resources.GetObject("northWallMesh.Location")));
            this.northWallMesh.Material = null;
            this.northWallMesh.Rotation = ((CSharp3D.Forms.Engine.RotationVector)(resources.GetObject("northWallMesh.Rotation")));
            this.northWallMesh.ScaleX = 8F;
            this.northWallMesh.ScaleY = 1F;
            this.northWallMesh.ScaleZ = 4F;
            this.northWallMesh.Scene = this.scene1;
            // 
            // eastWall
            // 
            this.eastWall.Clickable = false;
            this.eastWall.Location = ((CSharp3D.Forms.Engine.LocationVector)(resources.GetObject("eastWall.Location")));
            this.eastWall.Material = null;
            this.eastWall.Rotation = ((CSharp3D.Forms.Engine.RotationVector)(resources.GetObject("eastWall.Rotation")));
            this.eastWall.ScaleX = 1F;
            this.eastWall.ScaleY = 8F;
            this.eastWall.ScaleZ = 4F;
            this.eastWall.Scene = this.scene1;
            // 
            // southWall
            // 
            this.southWall.Clickable = false;
            this.southWall.Location = ((CSharp3D.Forms.Engine.LocationVector)(resources.GetObject("southWall.Location")));
            this.southWall.Material = null;
            this.southWall.Rotation = ((CSharp3D.Forms.Engine.RotationVector)(resources.GetObject("southWall.Rotation")));
            this.southWall.ScaleX = 8F;
            this.southWall.ScaleY = 1F;
            this.southWall.ScaleZ = 4F;
            this.southWall.Scene = this.scene1;
            // 
            // cuboidMesh1
            // 
            this.cuboidMesh1.Clickable = false;
            this.cuboidMesh1.Location = ((CSharp3D.Forms.Engine.LocationVector)(resources.GetObject("cuboidMesh1.Location")));
            this.cuboidMesh1.Material = this.material1;
            this.cuboidMesh1.Rotation = ((CSharp3D.Forms.Engine.RotationVector)(resources.GetObject("cuboidMesh1.Rotation")));
            this.cuboidMesh1.ScaleX = 1F;
            this.cuboidMesh1.ScaleY = 1F;
            this.cuboidMesh1.ScaleZ = 1F;
            this.cuboidMesh1.Scene = this.scene1;
            // 
            // material1
            // 
            this.material1.Additive = false;
            this.material1.AddSelf = 0F;
            this.material1.Albedo = this.texture1;
            this.material1.Alpha = 1F;
            this.material1.BackCulling = true;
            this.material1.Color = System.Drawing.Color.White;
            this.material1.Normal = this.texture2;
            this.material1.OverbrightFactor = 1F;
            this.material1.ShaderName = "Generic";
            this.material1.Specular = this.texture3;
            this.material1.SpecularStrength = 0.8F;
            this.material1.Translucent = false;
            this.material1.Unlit = false;
            // 
            // texture1
            // 
            this.texture1.Bitmap = ((System.Drawing.Bitmap)(resources.GetObject("texture1.Bitmap")));
            // 
            // texture2
            // 
            this.texture2.Bitmap = ((System.Drawing.Bitmap)(resources.GetObject("texture2.Bitmap")));
            // 
            // texture3
            // 
            this.texture3.Bitmap = ((System.Drawing.Bitmap)(resources.GetObject("texture3.Bitmap")));
            // 
            // pointLight1
            // 
            this.pointLight1.Color = System.Drawing.Color.Blue;
            this.pointLight1.Constant = 0F;
            this.pointLight1.Intensity = 1F;
            this.pointLight1.Linear = 0F;
            this.pointLight1.Location = ((CSharp3D.Forms.Engine.LocationVector)(resources.GetObject("pointLight1.Location")));
            this.pointLight1.Quadratic = 1F;
            this.pointLight1.Scene = this.scene1;
            // 
            // pointLight2
            // 
            this.pointLight2.Color = System.Drawing.Color.Yellow;
            this.pointLight2.Constant = 0F;
            this.pointLight2.Intensity = 2F;
            this.pointLight2.Linear = 0F;
            this.pointLight2.Location = ((CSharp3D.Forms.Engine.LocationVector)(resources.GetObject("pointLight2.Location")));
            this.pointLight2.Quadratic = 1F;
            this.pointLight2.Scene = this.scene1;
            // 
            // pointLight3
            // 
            this.pointLight3.Color = System.Drawing.Color.Red;
            this.pointLight3.Constant = 0F;
            this.pointLight3.Intensity = 5F;
            this.pointLight3.Linear = 0F;
            this.pointLight3.Location = ((CSharp3D.Forms.Engine.LocationVector)(resources.GetObject("pointLight3.Location")));
            this.pointLight3.Quadratic = 1F;
            this.pointLight3.Scene = this.scene1;
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
            ((System.ComponentModel.ISupportInitialize)(this.floorMesh)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.northWallMesh)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.eastWall)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.southWall)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.cuboidMesh1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pointLight1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pointLight2)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pointLight3)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private CSharp3D.Forms.Controls.RendererControl rendererControl1;
        private CSharp3D.Forms.Engine.Texture textureUp;
        private CSharp3D.Forms.Cameras.OrbitalCamera orbitalCamera1;
        private CSharp3D.Forms.Engine.Scene scene1;
        private CSharp3D.Forms.Meshes.GridMesh gridMesh1;
        private CSharp3D.Forms.Meshes.CuboidMesh floorMesh;
        private CSharp3D.Forms.Meshes.CuboidMesh northWallMesh;
        private CSharp3D.Forms.Meshes.CuboidMesh eastWall;
        private CSharp3D.Forms.Meshes.CuboidMesh southWall;
        private CSharp3D.Forms.Meshes.CuboidMesh cuboidMesh1;
        private CSharp3D.Forms.Lights.PointLight pointLight1;
        private CSharp3D.Forms.Lights.PointLight pointLight2;
        private CSharp3D.Forms.Engine.Material material1;
        private CSharp3D.Forms.Engine.Texture texture1;
        private CSharp3D.Forms.Engine.Texture texture2;
        private CSharp3D.Forms.Engine.Texture texture3;
        private CSharp3D.Forms.Lights.PointLight pointLight3;
    }
}