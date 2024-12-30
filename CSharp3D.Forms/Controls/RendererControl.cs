using CSharp3D.Forms.Cameras;
using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Exceptions;
using CSharp3D.Forms.Meshes;
using CSharp3D.Forms.Utils;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace CSharp3D.Forms.Controls
{
    /// <summary>
    /// A control that renders a Scene.
    /// </summary>
    [Description("A control that renders a Scene.")]
    public partial class RendererControl : UserControl
    {
        private GLControl glControl;

        /// <summary>
        /// Occurs when the camera is moved. This event is fired when the camera is moved using the mouse.
        /// </summary>
        [Category("User")]
        [Description("Occurs when the camera is moved. This event is fired when the camera is moved using the mouse.")]
        public event EventHandler<EventArgs> CameraMove;

        /// <summary>
        /// The scene to render. The scene contains all the meshes and lights to render.
        /// </summary>
        [Category("Renderer")]
        [Description("The scene to render. The scene contains all the meshes and lights to render.")]
        public Scene Scene { get; set; }

        /// <summary>
        /// The camera to use for rendering. The camera determines the view of the scene.
        /// </summary>
        [Category("Renderer")]
        [Description("The camera to use for rendering. The camera determines the view of the scene.")]
        public Camera Camera { get; set; }

        /// <summary>
        /// Specifies the depth buffer's precision in bits. The depth buffer is used for depth testing, determining which objects are in front of others. A common value is 24 bits.
        /// </summary>
        [Category("Graphics")]
        [Description("Specifies the depth buffer's precision in bits. The depth buffer is used for depth testing, determining which objects are in front of others. A common value is 24 bits.")]
        private int Depth { get; set; } = 24;

        /// <summary>
        /// Specifies the stencil buffer's precision in bits. The stencil buffer is used for complex masking operations. A common value is 8 bits.
        /// </summary>
        [Category("Graphics")]
        [Description("Specifies the stencil buffer's precision in bits. The stencil buffer is used for complex masking operations. A common value is 8 bits.")]
        private int Stencil { get; set; } = 8;

        /// <summary>
        /// Specifies the number of samples for Full-Screen Anti-Aliasing (FSAA), also known as multisampling. Higher values provide better antialiasing but may impact performance.
        /// </summary>
        [Category("Graphics")]
        [Description("Specifies the number of samples for Full-Screen Anti-Aliasing (FSAA), also known as multisampling. Higher values provide better antialiasing but may impact performance.")]
        private int Samples { get; set; } = 16;

        /// <summary>
        /// Specifies the number of buffers. Typically, this is set to true for double buffering, which reduces flickering and provides smoother rendering by alternating between two buffers.
        /// </summary>
        [Category("Graphics")]
        [Description("Specifies the number of buffers. Typically, this is set to true for double buffering, which reduces flickering and provides smoother rendering by alternating between two buffers.")]
        private bool DoubleBuffer { get; set; } = true;

        /// <summary>
        /// Specifies whether the rendering is synchronized with the vertical refresh rate of the monitor. This can reduce screen tearing but may impact performance.
        /// </summary>
        [Category("Renderer")]
        private bool VSync { get; set; } = true;

        /// <summary>
        /// Whether the refresh of the renderer console is done by an external object. This is useful when there is a thread that continuously
        /// invalidate this renderer control.
        /// </summary>
        [Category("Renderer")]
        [Description("Whether the refresh of the renderer console is not done by an external object. Set this to false if there is a thread that continuous invalidate this renderer control.")]
        public bool AutoInvalidate { get; set; } = true;

        /// <summary>
        /// The graphics context for the control.
        /// </summary>
        public IGraphicsContext Context
        {
            get
            {
                return glControl.Context;
            }
        }

        private double lastFrameTime = 0;

        private Dictionary<Keys, bool> keysDown = new Dictionary<Keys, bool>();

        public RendererControl()
        {
            InitializeComponent();

            // Let this control be focusable.
            this.TabStop = true;

            glControl = new GLControl(new GraphicsMode(32, Depth, Stencil, Samples, 0, (DoubleBuffer ? 2 : 1)));
            glControl.VSync = VSync;
            glControl.Dock = DockStyle.Fill;
            glControl.Paint += GLControl_Paint;
            Controls.Add(glControl);

            if (!this.DesignMode)
            {
                if (Scene == null)
                {
                    Scene = new Scene();
                }

                if (Camera == null)
                {
                    Camera = new OrbitalCamera(new Vector3(0, 0, 0), 4);
                }
            }

            InitializeGLControl();
        }

        /// <summary>
        /// Assign the GLControl events.
        /// </summary>
        private void InitializeGLControl()
        {
            if (!(LicenseManager.UsageMode != LicenseUsageMode.Runtime))
            {
                glControl.Load += GLControl_Load;
                glControl.Paint += GLControl_Paint;
                glControl.Resize += GLControl_Resize;
                glControl.MouseDown += GLControl_MouseDown;
                glControl.MouseWheel += GLControl_MouseWheel;
                glControl.KeyDown += GlControl_KeyDown;
                glControl.KeyUp += GlControl_KeyUp;
            }
        }

        /// <summary>
        /// Dispose the control.
        /// </summary>
        /// <param name="disposing"> Whether the control is being disposed. </param>
        protected override void Dispose(bool disposing)
        {
            try
            {
                Scene.Dispose(disposing, Context);
            } catch (Exception e)
            {

            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Load the GLControl.
        /// </summary>
        /// <param name="sender"> The sender. </param>
        /// <param name="e"> The event arguments. </param>
        private void GLControl_Load(object sender, EventArgs e)
        {
            glControl.MakeCurrent();

            // Set the background color
            GL.ClearColor(BackColor.R / 255f, BackColor.G / 255f, BackColor.B / 255f, BackColor.A / 255f);

            GL.Enable(EnableCap.DepthTest);

            glControl.Invalidate();
        }

        /// <summary>
        /// Resize the GLControl.
        /// </summary>
        /// <param name="sender"> The sender. </param>
        /// <param name="e"> The event arguments. </param>
        private void GLControl_Resize(object sender, EventArgs e)
        {
            if (this.DesignMode || glControl.ClientSize.Height == 0)
                return;

            glControl.MakeCurrent();

            GL.Viewport(0, 0, glControl.Width, glControl.Height);

            if (AutoInvalidate)
                glControl.Invalidate();
        }

        /// <summary>
        /// Calculates the elapsed time since the last time the GlControl was painted.
        /// </summary>
        /// <returns> The delta time, in seconds.
        private double GetDeltaTime()
        {
            double currentTime = (double)Environment.TickCount / 1000.0;
            double deltaTime = currentTime - lastFrameTime;
            lastFrameTime = currentTime;

            // This is a workaround so we skip the first deltatime update if it's been too long since the last paint.
            if (deltaTime > 0.05)
            {
                deltaTime = 0;
            }

            return deltaTime;
        }

        /// <summary>
        /// Paint the GLControl.
        /// </summary>
        /// <param name="sender"> The sender. </param>
        /// <param name="e"> The event arguments. </param>
        /// <exception cref="CameraNotSetException"> Exception thrown when the camera is not set in a RendererControl </exception>
        private void GLControl_Paint(object sender, PaintEventArgs e)
        {
            double deltaTime = GetDeltaTime();

            if (this.DesignMode)
            {
                // Use GDI+ to paint the background color in design mode
                using (SolidBrush brush = new SolidBrush(BackColor))
                {
                    e.Graphics.FillRectangle(brush, this.ClientRectangle);
                }
                return;
            }

            if (Camera == null)
                throw new CameraNotSetException();

            glControl.MakeCurrent();

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Set the projection and view matrices
            Matrix4 projection = Camera.GetProjectionMatrix(glControl.Width, glControl.Height);
            Matrix4 view = Camera.GetViewMatrix(this);

            // Draw opaque meshes meshes
            List<Mesh> transparentMeshes = new List<Mesh>();
            foreach (Mesh mesh in Scene.Meshes)
            {
                if (mesh.Material != null && (mesh.Material.Translucent || mesh.Material.Additive || mesh.Material.Alpha < 1))
                {
                    transparentMeshes.Add(mesh);
                    continue;
                }
                else
                {
                    mesh.DrawMesh(Context, Scene, projection, view);
                }
            }

            // Draw translucent meshes
            GL.DepthMask(false);

            if (Camera is OrbitalCamera)
            {
                Vector3 cameraPosition = (Camera as OrbitalCamera).GetLocation(this); // Correctly calculate camera position

                var sortedTransparentMeshes = transparentMeshes
                    .OrderByDescending(mesh => mesh.GetDistanceFromCamera(cameraPosition))
                    .ToList();

                foreach (Mesh mesh in sortedTransparentMeshes)
                {
                    mesh.DrawMesh(Context, Scene, projection, view);
                }
            }

            GL.DepthMask(true);

            bool shouldInvalidate = false;

            if (Camera.IsLeftMouseButtonDown && !MouseHelper.IsLeftMouseButtonDown())
            {
                Camera.MouseUp(this, MouseButtons.Left);
            }
            if (Camera.IsMiddleMouseButtonDown && !MouseHelper.IsMiddleMouseButtonDown())
            {
                Camera.MouseUp(this, MouseButtons.Middle);
            }
            if (Camera.IsRightMouseButtonDown && !MouseHelper.IsRightMouseButtonDown())
            {
                Camera.MouseUp(this, MouseButtons.Right);
            }

            bool mouseDown = MouseHelper.IsLeftMouseButtonDown() || MouseHelper.IsMiddleMouseButtonDown() || MouseHelper.IsRightMouseButtonDown();
            if (Camera.IsMouseDown)
            {
                var mouseDelta = Camera.GetMouseDelta();
                if (mouseDelta != Vector2.Zero)
                {
                    CameraMove?.Invoke(this, new EventArgs());
                }

                if (mouseDown)
                {
                    if (AutoInvalidate)
                    {
                        shouldInvalidate = true;
                    }
                }
            }

            // Check which keys are pressed
            bool wDown = keysDown.TryGetValue(Keys.W, out bool isW) && isW;
            bool aDown = keysDown.TryGetValue(Keys.A, out bool isA) && isA;
            bool sDown = keysDown.TryGetValue(Keys.S, out bool isS) && isS;
            bool dDown = keysDown.TryGetValue(Keys.D, out bool isD) && isD;
            bool spaceDown = keysDown.TryGetValue(Keys.Space, out bool isSpc) && isSpc;
            bool shiftDown = keysDown.TryGetValue(Keys.ShiftKey, out bool isShift) && isShift;

            // If your camera is FreeLookCamera, apply movement
            if (Camera is FreeLookCamera freeCam)
            {
                freeCam.Move(this, deltaTime, wDown, aDown, sDown, dDown, spaceDown, shiftDown);

                if (wDown || aDown || sDown || dDown)
                {
                    shouldInvalidate = true;
                }

                if (freeCam.MouseLook)
                {
                    shouldInvalidate = true;
                }
            }

            GL.BindVertexArray(0);
            glControl.Context.SwapBuffers();

            if (shouldInvalidate)
            {
                glControl.Invalidate();
            }
        }

        /// <summary>
        /// Invalidate the control.
        /// </summary>
        /// <param name="e"> The event arguments. </param>
        protected override void OnInvalidated(InvalidateEventArgs e)
        {
            base.OnInvalidated(e);

            glControl.Invalidate();
        }

        /// <summary>
        /// Get the location of the center of the control in the user's screen
        /// </summary>
        /// <returns></returns>
        internal Point GetCenterPointInScreen()
        {
            var center = glControl.PointToScreen(Location);
            center = new Point(center.X + Width / 2, center.Y + Height / 2);
            return center;
        }

        /// <summary>
        /// Mouse down event for the GLControl.
        /// </summary>
        /// <param name="sender"> The sender. </param>
        /// <param name="e"> The event arguments. </param>
        private void GLControl_MouseDown(object sender, MouseEventArgs e)
        {
            Camera.MouseDown(this, e.Button);

            Scene.PickMesh(this, Camera, e);

            if (AutoInvalidate)
                glControl.Invalidate();
        }

        /// <summary>
        /// Mouse wheel event for the GLControl.
        /// </summary>
        /// <param name="sender"> The sender. </param>
        /// <param name="e"> The event arguments. </param>
        private void GLControl_MouseWheel(object sender, MouseEventArgs e)
        {
            Camera.MouseWheel(this, e);

            if (AutoInvalidate)
                glControl.Invalidate();
        }

        private void GlControl_KeyDown(object sender, KeyEventArgs e)
        {
            // Mark this key as pressed
            keysDown[e.KeyCode] = true;

            Camera.KeyDown(this, e.KeyCode);

            if (AutoInvalidate)
                glControl.Invalidate();
        }

        private void GlControl_KeyUp(object sender, KeyEventArgs e)
        {
            // Mark this key as released
            keysDown[e.KeyCode] = false;

            if (AutoInvalidate)
                glControl.Invalidate();
        }
    }
}
