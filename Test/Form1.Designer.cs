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
            ((System.ComponentModel.ISupportInitialize)(this.gridMesh1)).BeginInit();
            this.SuspendLayout();
            // 
            // rendererControl1
            // 
            this.rendererControl1.AutoInvalidate = true;
            this.rendererControl1.BackColor = System.Drawing.Color.Black;
            this.rendererControl1.Camera = this.orbitalCamera1;
            this.rendererControl1.Dock = System.Windows.Forms.DockStyle.Fill;
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
            this.scene1.AmbientIntensity = 0F;
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
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.rendererControl1);
            this.Name = "Form1";
            this.Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)(this.gridMesh1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private CSharp3D.Forms.Controls.RendererControl rendererControl1;
        private CSharp3D.Forms.Cameras.OrbitalCamera orbitalCamera1;
        private CSharp3D.Forms.Engine.Scene scene1;
        private CSharp3D.Forms.Meshes.GridMesh gridMesh1;
    }
}