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
using System.Diagnostics;
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
        /// Occurs once per rendered frame, right before the scene is drawn. Use this for
        /// per-frame simulation updates: unlike a WinForms Timer (WM_TIMER), it keeps firing
        /// during continuous redraw loops (e.g. camera drags), when a permanently pending
        /// WM_PAINT starves WM_TIMER and freezes timer-driven animation.
        /// </summary>
        [Category("Renderer")]
        [Description("Occurs once per rendered frame, before the scene is drawn. Use for per-frame simulation updates; keeps firing during continuous redraws (camera drags) when WinForms timers are starved.")]
        public event EventHandler<FrameUpdateEventArgs> FrameUpdate;

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
        /// How this view draws the scene: textured, flat-shaded, or wireframe. Per view, so several
        /// views can show one shared scene differently.
        /// </summary>
        [Category("Renderer")]
        [Description("How this view draws the scene: textured, flat-shaded, or wireframe.")]
        public MeshDrawMode DrawMode { get; set; } = MeshDrawMode.Textured;

        /// <summary>
        /// Meshes drawn only by this control, BEFORE the shared <see cref="Scene"/>. Several views
        /// commonly share one scene (a quad viewport does), so anything that belongs to a single
        /// view — a per-view grid, a view-local gizmo — cannot live in the scene without showing
        /// up in all of them. Put it here instead.
        ///
        /// Drawing first is what a backdrop wants: an ortho grid belongs behind the map, not over
        /// it. Anything that has to survive the scene belongs in <see cref="OverlayMeshes"/>.
        /// </summary>
        [Browsable(false)]
        public List<Mesh> ViewMeshes { get; } = new List<Mesh>();

        /// <summary>
        /// Per-view meshes drawn AFTER the scene, so they can be depth-tested against it or drawn
        /// over it.
        ///
        /// <see cref="MeshDepthMode"/> only means anything here: "in front of everything already
        /// rendered" (Overlay) and "only where the scene covers it" (OccludedOnly) are both claims
        /// about a depth buffer the scene has already written, so a mesh in <see cref="ViewMeshes"/>
        /// making either of them is simply painted over by the geometry that follows it. Tool
        /// guides — selection boxes, handles, drag previews — belong here.
        /// </summary>
        [Browsable(false)]
        public List<Mesh> OverlayMeshes { get; } = new List<Mesh>();

        /// <summary>
        /// Which mouse buttons are handed to the camera. A host that drives editing tools through
        /// the control's mouse events can remove a button (typically Left) from this mask to keep
        /// the camera from grabbing the cursor while a tool owns that button.
        /// </summary>
        [Category("Renderer")]
        [Description("Which mouse buttons are handed to the camera for navigation. Remove a button (e.g. Left) while a tool owns it.")]
        public MouseButtons CameraMouseButtons { get; set; } =
            MouseButtons.Left | MouseButtons.Middle | MouseButtons.Right;

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
        /// Skip scene meshes whose bounding box is entirely outside the view volume.
        /// On by default; turn it off to diagnose a suspected culling artifact.
        /// </summary>
        public bool FrustumCulling { get; set; } = true;

        /// <summary>Meshes skipped / drawn by the last frame — for diagnostics.</summary>
        public int CulledMeshCount { get; private set; }

        public int DrawnMeshCount { get; private set; }

        /// <summary>
        /// Whether moving the cursor over this view gives it keyboard focus, without a click. The
        /// wheel already zooms the view under the cursor, so requiring a click before WASD works
        /// (a <see cref="FreeLookCamera"/>) makes the keyboard the odd one out — especially with
        /// several views on screen, where the focused one is not the one being pointed at.
        ///
        /// Only ever steals focus while this view's own window is the active one: a cursor sweeping
        /// across the app must not yank focus out of a text box in another window the user is
        /// typing into.
        /// </summary>
        [Category("Renderer")]
        [Description("Whether hovering the view gives it keyboard focus, so WASD works without clicking first.")]
        public bool FocusOnHover { get; set; } = false;

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

        /// <summary>High-resolution timestamp of the previous frame; 0 = no frame yet.</summary>
        private long lastFrameTimestamp = 0;

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
                // Make sure the renderer has a scene
                if (Scene == null)
                {
                    Scene = new Scene();
                }

                // Make sure the renderer has a camera.
                if (Camera == null)
                {
                    Camera = new OrbitalCamera(new RotationVector(0, 0, 0), 4);
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
                // NOTE: Paint is already subscribed in the constructor (needed for the
                // design-mode fill too) — subscribing it again here rendered every
                // frame twice at runtime.
                glControl.Resize += GLControl_Resize;
                glControl.MouseDown += GLControl_MouseDown;
                glControl.MouseWheel += GLControl_MouseWheel;
                glControl.KeyDown += GlControl_KeyDown;
                glControl.KeyUp += GlControl_KeyUp;

                // The inner GLControl covers the whole surface, so the UserControl's own mouse
                // events would never fire. Re-raise them here so hosts can wire tools (picking,
                // dragging) and QuadViewControl's DoubleClick maximize actually receives clicks.
                glControl.MouseMove += (s, e) => OnMouseMove(e);
                glControl.MouseUp += (s, e) => OnMouseUp(e);
                glControl.DoubleClick += (s, e) => OnDoubleClick(e);
                // MouseDoubleClick is raised from the message loop of whichever control got
                // the WM_*BUTTONDBLCLK — that is the inner GLControl, never this one — so a
                // host wanting the button and position of a double click (map editor: open
                // the properties of the object under the cursor) needs it forwarded too.
                glControl.MouseDoubleClick += (s, e) => OnMouseDoubleClick(e);
                glControl.MouseEnter += GLControl_MouseEnter;
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
        /// Longest frame time that still drives simulation, in seconds. A frame slower
        /// than this advances the world by this much and no more, so returning from a
        /// stall (a modal dialog, a long rebuild) cannot teleport the camera.
        ///
        /// It is a CLAMP, not a discard. It used to zero the delta instead, which meant
        /// any frame slower than 50 ms moved the camera not at all — so on a map big
        /// enough to render below 20 fps the camera simply would not move, and near that
        /// threshold it lurched between stopped and full speed as frames crossed it.
        /// </summary>
        private const double MaxFrameDelta = 0.1;

        /// <summary>
        /// Elapsed time since the last paint of the GlControl, in seconds.
        /// </summary>
        private double GetDeltaTime()
        {
            // Stopwatch, not Environment.TickCount: TickCount only advances every ~15.6 ms,
            // so at any decent frame rate most frames measured exactly zero elapsed time
            // and the occasional one measured a whole tick — movement arrived in lumps
            // rather than smoothly.
            long now = Stopwatch.GetTimestamp();

            if (lastFrameTimestamp == 0)
            {
                lastFrameTimestamp = now;
                return 0;
            }

            double deltaTime = (now - lastFrameTimestamp) / (double)Stopwatch.Frequency;
            lastFrameTimestamp = now;

            if (deltaTime < 0)
                deltaTime = 0;              // clock went backwards; skip this frame

            if (deltaTime > MaxFrameDelta)
                deltaTime = MaxFrameDelta;

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

            GL.ClearColor(BackColor.R / 255f, BackColor.G / 255f, BackColor.B / 255f, BackColor.A / 255f);

            if (Camera == null)
                throw new CameraNotSetException();

            glControl.MakeCurrent();

            // Per-frame update hook (update-then-draw). Raised with the context current so
            // handlers may create/destroy scene resources before the meshes are enumerated.
            FrameUpdate?.Invoke(this, new FrameUpdateEventArgs(deltaTime));

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // This context is current here, so it is the only safe place to free the GL
            // objects of meshes the scene dropped since the last paint.
            Scene?.ProcessPendingDeletions(Context);

            // Set the projection and view matrices
            Matrix4 projection = Camera.GetProjectionMatrix(glControl.Width, glControl.Height);
            Matrix4 view = Camera.GetViewMatrix(this);

            List<Mesh> transparentMeshes = new List<Mesh>();

            // This view's own meshes (grid, overlays) draw first, behind the shared scene. They
            // are always drawn as-is: a grid or a handle means the same thing in every draw mode.
            foreach (Mesh mesh in ViewMeshes)
            {
                if (mesh.IsVisibleIn(DrawMode))
                    mesh.DrawMesh(Context, Scene, projection, view);
            }

            // Wireframe is a polygon-fill state, so it only affects triangle meshes — line and
            // point overlays keep drawing normally, which is what we want.
            if (DrawMode == MeshDrawMode.Wireframe)
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);

            // Frustum culling, the role Hammer's cull tree + IsBoxVisible play: on a large
            // map the scene holds tens of thousands of face meshes and drawing every one
            // of them in every pane, every frame, is what makes flying around crawl.
            ViewFrustum frustum = ViewFrustum.FromViewProjection(view * projection);

            CulledMeshCount = 0;
            DrawnMeshCount = 0;

            // Draw opaque meshes
            foreach (Mesh mesh in Scene.Meshes)
            {
                // Views only draw the scene meshes aimed at their draw mode (a Hammer
                // quad view keeps face geometry out of wireframe panes and outline
                // meshes out of the textured pane).
                if (!mesh.IsVisibleIn(DrawMode))
                    continue;

                if (FrustumCulling)
                {
                    OpenTK.Vector3 boundsMin, boundsMax;

                    // Meshes with no bounds yet (never uploaded) always draw — the upload
                    // happens inside the draw call.
                    if (mesh.TryGetWorldBounds(out boundsMin, out boundsMax)
                        && !frustum.Intersects(boundsMin, boundsMax))
                    {
                        CulledMeshCount++;
                        continue;
                    }
                }

                DrawnMeshCount++;

                // Check if mesh is transparent
                if (mesh.Material != null && (mesh.Material.Translucent || mesh.Material.Additive || mesh.Material.Alpha < 1))
                {
                    // Draw it later if it's transparent
                    transparentMeshes.Add(mesh);
                    continue;
                }
                else
                {
                    // Draw it now if it's solid
                    mesh.DrawMesh(Context, Scene, projection, view, DrawMode);
                }
            }

            // Draw translucent meshes
            GL.DepthMask(false);

            if (Camera is OrbitalCamera)
            {
                LocationVector cameraPosition = (Camera as OrbitalCamera).GetLocation(this); // Correctly calculate camera position

                var sortedTransparentMeshes = transparentMeshes
                    .OrderByDescending(mesh => mesh.GetDistanceFromCamera(cameraPosition))
                    .ToList();

                foreach (Mesh mesh in sortedTransparentMeshes)
                {
                    mesh.DrawMesh(Context, Scene, projection, view, DrawMode);
                }
            }
            else
            {
                // TODO implement sorting
                var sortedTransparentMeshes = transparentMeshes;
                foreach (Mesh mesh in sortedTransparentMeshes)
                {
                    mesh.DrawMesh(Context, Scene, projection, view, DrawMode);
                }
            }

            GL.DepthMask(true);

            // Leave the fill state as we found it: it is global, and the next control to paint
            // shares this GL state machine.
            if (DrawMode == MeshDrawMode.Wireframe)
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

            // Per-view overlays, last, so their depth modes have the scene's depth buffer to
            // work against — see OverlayMeshes.
            foreach (Mesh mesh in OverlayMeshes)
            {
                if (mesh.IsVisibleIn(DrawMode))
                    mesh.DrawMesh(Context, Scene, projection, view);
            }

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
            var topleft = this.PointToScreen(new System.Drawing.Point(0, 0));
            var bottomright = this.PointToScreen(new System.Drawing.Point(this.Width, this.Height));
            var center = new Point((topleft.X + bottomright.X) / 2, (topleft.Y + bottomright.Y) / 2);

            //var center = glControl.PointToScreen(Location);
            //center = new Point(center.X + Width / 2, center.Y + Height / 2);
            return center;
        }

        /// <summary>
        /// Gives the render surface keyboard focus without requiring a click — so a host can, for
        /// instance, focus this view on mouse-enter and let WASD (a <see cref="FreeLookCamera"/>)
        /// work as soon as the cursor is over it. The inner GL control is what actually receives key
        /// events, so plain <see cref="Control.Focus"/> on this (outer) UserControl is not enough.
        /// </summary>
        public void FocusRenderSurface()
        {
            if (glControl != null && glControl.CanFocus)
                glControl.Focus();
        }

        /// <summary>
        /// Hover focus, when <see cref="FocusOnHover"/> is on: the view under the cursor takes the
        /// keyboard, matching where the wheel already goes. Gated on this view's window being the
        /// active one so a cursor crossing the app can't pull focus away from another window.
        /// </summary>
        private void GLControl_MouseEnter(object sender, EventArgs e)
        {
            if (!FocusOnHover)
                return;

            Form form = FindForm();
            if (form == null || form != Form.ActiveForm)
                return;

            FocusRenderSurface();
        }

        /// <summary>
        /// Mouse down event for the GLControl.
        /// </summary>
        /// <param name="sender"> The sender. </param>
        /// <param name="e"> The event arguments. </param>
        private void GLControl_MouseDown(object sender, MouseEventArgs e)
        {
            // Take keyboard focus so key input (WASD fly for a FreeLookCamera, and host key handlers)
            // reaches this view — clicking a 3D view to drive it is the expected behavior.
            FocusRenderSurface();

            if ((CameraMouseButtons & e.Button) != 0)
                Camera.MouseDown(this, e.Button);

            Scene.PickMesh(this, Camera, e);

            OnMouseDown(e);

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

            OnKeyDown(e);

            if (AutoInvalidate)
                glControl.Invalidate();
        }

        private void GlControl_KeyUp(object sender, KeyEventArgs e)
        {
            // Mark this key as released
            keysDown[e.KeyCode] = false;

            OnKeyUp(e);

            if (AutoInvalidate)
                glControl.Invalidate();
        }
    }

    /// <summary>
    /// Event data for <see cref="RendererControl.FrameUpdate"/>.
    /// </summary>
    public class FrameUpdateEventArgs : EventArgs
    {
        public FrameUpdateEventArgs(double deltaTime)
        {
            DeltaTime = deltaTime;
        }

        /// <summary>
        /// Seconds elapsed since the previous rendered frame (0 after a long stall).
        /// </summary>
        public double DeltaTime { get; }
    }
}
