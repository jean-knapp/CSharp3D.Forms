using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;
using CSharp3D.Forms.Cameras;
using CSharp3D.Forms.Controls;
using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Meshes;
using CSharp3D.Forms.Vulkan.RayTracing;
using CSharp3D.Forms.Vulkan.Vk;
using OpenTK;
// Both the scene model (OpenTK) and the GPU records (System.Numerics) have a Vector3; the
// GPU one is what this file means when it does not say.
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Timer = System.Windows.Forms.Timer;

namespace CSharp3D.Forms.Vulkan.Controls
{
    /// <summary>
    /// A view that ray traces a <see cref="Scene"/> on the GPU, through Vulkan.
    ///
    /// The counterpart of <see cref="RendererControl"/>: the same scene, the same camera, the
    /// same mouse and keyboard behaviour, so a host can put one where it has the other. What
    /// it draws is different in kind - every pixel is a ray, every light casts a shadow, and
    /// the light that bounces is traced rather than baked - which is what the game this
    /// editor targets does, so the view shows the map as the game will.
    ///
    /// The frames are rendered on a thread of their own. A ray traced frame is a GPU job
    /// that takes milliseconds and then presents; done on the UI thread, as the GL view
    /// paints, it would hold the rest of the window - the other panes, the mouse - to its
    /// own rate. So the UI thread only feeds it: the scene when it has changed (on paint),
    /// and the camera while a button or key is held (from a timer). The thread renders while
    /// there is something new or the picture is still converging, and sleeps otherwise.
    ///
    /// The camera classes take a <see cref="RendererControl"/> for their arithmetic, so one is
    /// needed here as <see cref="CameraHost"/>: in the map editor it is the GL perspective view
    /// this control sits over, which has the same bounds; a stand-alone host gives it a hidden
    /// one. Its size and screen position are all the camera reads.
    ///
    /// Vulkan is set up on first paint. A machine that cannot do this shows why, in the view,
    /// and stays that way - <see cref="IsAvailable"/> tells a host to keep its GL view instead.
    /// </summary>
    [Description("A view that ray traces a scene on the GPU.")]
    public class VulkanRendererControl : UserControl
    {
        /// <summary>The camera as the render thread last heard of it. Replaced whole, never edited.</summary>
        private sealed class CameraPose
        {
            public Matrix4x4 ViewProj;
            public Matrix4x4 InvViewProj;
            public Vector3 Position;

            public bool SameAs(CameraPose other)
            {
                return other != null && other.ViewProj == ViewProj && other.Position == Position;
            }
        }

        // Everything Vulkan is used under this lock, from whichever thread.
        private readonly object _gate = new object();
        private VulkanDevice _device;
        private VulkanSwapchain _swapchain;
        private GpuScene _gpuScene;
        private RayTracer _tracer;

        // ---- the render thread ----
        private Thread _thread;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private volatile bool _stopping;
        private volatile bool _frameRequested;
        private volatile bool _paused;
        private volatile CameraPose _pose;
        private Size _surfaceSize;
        private int _samplesPerFrame = 1;

        // ---- the UI side ----
        private bool _initialized;
        private volatile string _status = "Ray tracing has not started.";
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastPumpSeconds;
        private readonly Timer _pump;
        private readonly Dictionary<Keys, bool> _keysDown = new Dictionary<Keys, bool>();
        private MouseButtons _cameraButtonsDown;

        private Func<Mesh, MeshClass> _classifier;
        private GpuLight[] _pendingLights;
        private Vector3 _pendingSky = new Vector3(0.6f * 0.25f, 0.65f * 0.25f, 0.75f * 0.25f);
        private bool _lightsPending;

        // ==================== the RendererControl surface ====================

        [Category("Renderer")]
        [Description("The scene to render.")]
        public Scene Scene { get; set; }

        [Category("Renderer")]
        [Description("The camera that looks at it.")]
        public Camera Camera { get; set; }

        /// <summary>
        /// The GL view the camera does its arithmetic against. Must have this view's bounds:
        /// in the editor that is the perspective pane this control is placed over.
        /// </summary>
        [Browsable(false)]
        public RendererControl CameraHost { get; set; }

        [Category("Renderer")]
        public event EventHandler<EventArgs> CameraMove;

        [Category("Renderer")]
        public event EventHandler<FrameUpdateEventArgs> FrameUpdate;

        [Category("Renderer")]
        public MouseButtons CameraMouseButtons { get; set; } = MouseButtons.Left | MouseButtons.Middle | MouseButtons.Right;

        /// <summary>Kept for the GL view's sake; this view redraws itself whatever it is set to.</summary>
        [Category("Renderer")]
        public bool AutoInvalidate { get; set; } = true;

        [Category("Renderer")]
        public bool FocusOnHover { get; set; } = false;

        /// <summary>
        /// Samples per pixel before the view stops rendering on its own. Every frame refines
        /// the picture, so a still view keeps rendering until it has this many, then rests.
        /// </summary>
        [Category("Renderer")]
        public int TargetSamples { get; set; } = 1024;

        /// <summary>Diffuse bounces per path. One is what the game's Lumen amounts to.</summary>
        [Category("Renderer")]
        public int Bounces { get; set; } = 1;

        /// <summary>Unreal's exposure compensation, in stops.</summary>
        [Category("Renderer")]
        public float ExposureBias { get; set; } = 0f;

        /// <summary>Whether Vulkan ray tracing came up. False until the first paint, and after a failure.</summary>
        [Browsable(false)]
        public bool IsAvailable => _tracer != null;

        /// <summary>The device's name once running, otherwise why it is not.</summary>
        [Browsable(false)]
        public string Status => _status;

        /// <summary>How many samples per pixel the picture on screen has, since the camera last moved.</summary>
        [Browsable(false)]
        public uint SamplesAccumulated
        {
            get
            {
                RayTracer tracer = _tracer;
                return tracer != null ? tracer.Samples : 0;
            }
        }

        /// <summary>
        /// How each mesh is treated. The host knows what its meshes are - which faces are sky,
        /// which are tool textures - and this is where it says so.
        /// </summary>
        [Browsable(false)]
        public Func<Mesh, MeshClass> Classifier
        {
            get => _classifier;
            set
            {
                _classifier = value;

                lock (_gate)
                {
                    if (_gpuScene != null)
                    {
                        _tracer?.WaitForGpu();
                        _gpuScene.Classifier = value ?? GpuScene.DefaultClassify;
                    }
                }
            }
        }

        public VulkanRendererControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            TabStop = true;
            BackColor = Color.Black;

            _pump = new Timer { Interval = 15 };
            _pump.Tick += Pump_Tick;
        }

        // ==================== lights ====================

        /// <summary>The lights to trace with, and the sky's radiance. Takes effect on the next frame.</summary>
        public void SetLights(GpuLight[] lights, Vector3 skyRadiance)
        {
            _pendingLights = lights ?? new GpuLight[0];
            _pendingSky = skyRadiance;
            _lightsPending = true;
            Invalidate();
        }

        // ==================== capture ====================

        /// <summary>The frame on screen, read back. Null when nothing has rendered.</summary>
        public Bitmap CaptureFrame()
        {
            lock (_gate)
                return _tracer?.Capture();
        }

        // ==================== lifetime ====================

        private bool TryInitialize()
        {
            if (_initialized)
                return _tracer != null;

            if (_permanentlyUnavailable)
                return false;

            _initialized = true;

            try
            {
                lock (_gate)
                {
                    _device = VulkanDevice.Shared;
                    _swapchain = new VulkanSwapchain(_device, Handle);
                    VulkanDevice.Stage("swapchain surface created");
                    _gpuScene = new GpuScene(_device) { Classifier = _classifier ?? GpuScene.DefaultClassify };
                    VulkanDevice.Stage("gpu scene created");
                    _tracer = new RayTracer(_device, _gpuScene, new ShaderCompiler());
                    VulkanDevice.Stage("ray tracer ready");
                    _status = _device.DeviceName;
                }

                _surfaceSize = ClientSize;
                _stopping = false;
                _thread = new Thread(RenderLoop) { IsBackground = true, Name = "Ray tracing" };
                _thread.Start();
                return true;
            }
            catch (VulkanUnavailableException ex)
            {
                _status = ex.Message;
                _permanentlyUnavailable = true;
            }
            catch (Exception ex)
            {
                _status = "Ray tracing failed to start: " + ex.Message;
                _permanentlyUnavailable = true;
            }

            TearDown();
            return false;
        }

        private bool _permanentlyUnavailable;

        /// <summary>Stop the thread and let go of everything Vulkan. UI thread.</summary>
        private void TearDown()
        {
            _pump.Stop();

            Thread thread = _thread;
            _thread = null;

            if (thread != null && thread != Thread.CurrentThread)
            {
                _stopping = true;
                _wake.Set();
                thread.Join(5000);
            }

            lock (_gate)
            {
                // On a lost device any of these can throw; each is released on its own so
                // that one failing does not keep the rest, and never lets an exception out
                // of a teardown that may be running from a failure handler.
                Release(ref _tracer);
                Release(ref _gpuScene);
                Release(ref _swapchain);
            }

            _stopping = false;

            // A later paint may try again: a lost device is replaced by VulkanDevice.Shared.
            _initialized = false;
        }

        private static void Release<T>(ref T resource) where T : class, IDisposable
        {
            T held = resource;
            resource = null;

            try
            {
                held?.Dispose();
            }
            catch (Exception ex)
            {
                VulkanDevice.Stage("release failed: " + ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TearDown();
                _pump.Dispose();
                _wake.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Vulkan owns the surface; GDI must not paint over it.
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            _surfaceSize = ClientSize;

            if (_tracer != null)
            {
                try
                {
                    lock (_gate)
                        FitSurface();
                }
                catch (Exception ex)
                {
                    Fail(ex);
                    return;
                }

                PushCamera();
                RequestFrame();
            }

            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            // A hidden view has no one to render for; the thread idles until it is back.
            _paused = !Visible;

            if (Visible)
            {
                PushCamera();
                RequestFrame();
            }
            else
            {
                _pump.Stop();
            }
        }

        /// <summary>Under the gate: the swapchain and the images at the window's current size.</summary>
        private void FitSurface()
        {
            Size size = _surfaceSize;
            uint width = (uint)Math.Max(1, size.Width);
            uint height = (uint)Math.Max(1, size.Height);

            if (!_swapchain.IsUsable || _swapchain.Width != width || _swapchain.Height != height)
            {
                _swapchain.Rebuild(width, height);
                _tracer.Resize(_swapchain.Width, _swapchain.Height);
            }
        }

        // ==================== the UI side of a frame ====================

        /// <summary>
        /// A paint is the host saying something changed - the scene, the lights, or just
        /// that the window was uncovered. The scene is brought up to date here, on the UI
        /// thread that owns it, and the render thread is told there is work.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            if (DesignMode || !TryInitialize())
            {
                PaintMessage(e.Graphics);
                return;
            }

            if (Camera == null || Scene == null)
            {
                PaintMessage(e.Graphics, "No scene.");
                return;
            }

            try
            {
                lock (_gate)
                {
                    if (_tracer == null)
                        return;

                    FitSurface();

                    // The frame in flight reads the buffers the sync may replace.
                    _tracer.WaitForGpu();

                    if (_lightsPending)
                    {
                        _gpuScene.SetLights(_pendingLights, _pendingSky);
                        _lightsPending = false;
                    }

                    if (_gpuScene.Sync(Scene))
                        _tracer.Restart();

                    _tracer.Bounces = Bounces;
                    _tracer.ExposureBias = ExposureBias;
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
                PaintMessage(e.Graphics);
                return;
            }

            PushCamera();
            RequestFrame();
        }

        /// <summary>Hand the render thread the camera as it is now. UI thread.</summary>
        private void PushCamera()
        {
            if (Camera == null || CameraHost == null || _tracer == null)
                return;

            Size size = _surfaceSize;

            if (size.Width <= 0 || size.Height <= 0)
                return;

            RendererControl host = CameraHost;
            Matrix4 view = Camera.GetViewMatrix(host);
            Matrix4 projection = Camera.GetProjectionMatrix(size.Width, size.Height);
            Matrix4 viewProj = view * projection;

            // World to GL space is the GL renderer's (-Y, Z, -X); its helper is internal.
            LocationVector location = Camera.GetLocation(host);

            CameraPose pose = new CameraPose
            {
                ViewProj = ToNumerics(viewProj),
                InvViewProj = ToNumerics(Matrix4.Invert(viewProj)),
                Position = new Vector3(-location.Y, location.Z, -location.X),
            };

            if (pose.SameAs(_pose))
                return;

            // A new pose is a frame's worth of work even when the old picture had converged.
            _pose = pose;
            _frameRequested = true;
            _wake.Set();
        }

        private void RequestFrame()
        {
            _frameRequested = true;
            _wake.Set();
        }

        /// <summary>
        /// While a camera button or key is held, the camera is stepped at a steady rate -
        /// the per-frame housekeeping the GL view does in its paint - and pushed to the
        /// render thread. The timer stops itself when there is nothing left to step.
        /// </summary>
        private void Pump_Tick(object sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double delta = _lastPumpSeconds > 0 ? now - _lastPumpSeconds : 0;
            _lastPumpSeconds = now;

            if (Camera == null || CameraHost == null || !Visible)
            {
                _pump.Stop();
                return;
            }

            FrameUpdate?.Invoke(this, new FrameUpdateEventArgs(delta));

            bool active = StepCamera(delta);
            PushCamera();

            if (!active)
            {
                _pump.Stop();
                _lastPumpSeconds = 0;
            }
        }

        private void EnsurePumping()
        {
            if (!_pump.Enabled)
            {
                _lastPumpSeconds = 0;
                _pump.Start();
            }
        }

        /// <summary>
        /// Release camera buttons the OS says are up, move a free-look camera by the keys
        /// held, and say whether there is still something to keep stepping.
        /// </summary>
        private bool StepCamera(double delta)
        {
            bool again = false;

            MouseButtons down = MouseButtons;

            foreach (MouseButtons button in new[] { MouseButtons.Left, MouseButtons.Middle, MouseButtons.Right })
            {
                if ((_cameraButtonsDown & button) != 0 && (down & button) == 0)
                {
                    Camera.MouseUp(CameraHost, button);
                    _cameraButtonsDown &= ~button;
                }
            }

            if (_cameraButtonsDown != 0)
            {
                if (Camera.GetMouseDelta() != OpenTK.Vector2.Zero)
                    CameraMove?.Invoke(this, EventArgs.Empty);

                again = true;
            }

            bool w = Held(Keys.W), a = Held(Keys.A), s = Held(Keys.S), d = Held(Keys.D);
            bool space = Held(Keys.Space), shift = Held(Keys.ShiftKey);

            FreeLookCamera free = Camera as FreeLookCamera;

            if (free != null)
            {
                free.Move(CameraHost, delta, w, a, s, d, space, shift);

                if (w || a || s || d || space || shift)
                {
                    CameraMove?.Invoke(this, EventArgs.Empty);
                    again = true;
                }

                if (free.MouseLook)
                    again = true;
            }

            return again;
        }

        private bool Held(Keys key)
        {
            bool down;
            return _keysDown.TryGetValue(key, out down) && down;
        }

        // ==================== the render thread ====================

        private void RenderLoop()
        {
            Stopwatch frameClock = new Stopwatch();
            double lastFrameSeconds = 0;

            while (!_stopping)
            {
                bool rendered = false;

                try
                {
                    lock (_gate)
                    {
                        if (_tracer == null)
                            break;

                        CameraPose pose = _pose;

                        bool converging = _tracer.Samples < (uint)Math.Max(1, TargetSamples);
                        bool wanted = !_paused && pose != null && (_frameRequested || converging);

                        if (wanted && _swapchain.IsUsable)
                        {
                            _frameRequested = false;

                            double now = _clock.Elapsed.TotalSeconds;
                            double delta = lastFrameSeconds > 0 ? now - lastFrameSeconds : 0;
                            lastFrameSeconds = now;

                            _tracer.SamplesPerFrame = _samplesPerFrame;

                            frameClock.Restart();
                            bool ok = _tracer.Render(_swapchain, pose.ViewProj, pose.InvViewProj, pose.Position, delta);
                            frameClock.Stop();

                            if (!ok)
                            {
                                // The window changed size under the swapchain.
                                FitSurface();
                                _frameRequested = true;
                            }
                            else
                            {
                                AdaptSamplesPerFrame(frameClock.Elapsed.TotalMilliseconds);
                            }

                            rendered = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _status = ex.Message.Contains("DeviceLost")
                        ? "The GPU reset (device lost). Pick the mode again to restart ray tracing."
                        : "Ray tracing stopped: " + ex.GetType().Name + ": " + ex.Message;
                    VulkanDevice.Stage("frame failed: " + ex);
                    FailFromThread();
                    break;
                }

                if (!rendered)
                {
                    _wake.WaitOne(100);
                }
                else if (_pose != null && !_frameRequested && frameClock.Elapsed.TotalMilliseconds < 8)
                {
                    // Converging on a still picture: no need to hog the GPU the other views share.
                    Thread.Sleep(8 - (int)frameClock.Elapsed.TotalMilliseconds);
                }
            }
        }

        /// <summary>
        /// A cheap frame can afford more paths per pixel, which converges the picture in
        /// fewer frames; an expensive one goes back to one. Aimed at keeping a frame under
        /// about a display refresh so that the camera stays responsive.
        /// </summary>
        private void AdaptSamplesPerFrame(double milliseconds)
        {
            if (milliseconds < 6 && _samplesPerFrame < 8)
                _samplesPerFrame++;
            else if (milliseconds > 14 && _samplesPerFrame > 1)
                _samplesPerFrame--;
        }

        private void FailFromThread()
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(() => { TearDown(); Invalidate(); }));
            }
            catch (Exception)
            {
                // The window is going away; nothing to tell.
            }
        }

        /// <summary>A failure on the UI thread: remember why, and stop.</summary>
        private void Fail(Exception ex)
        {
            _status = "Ray tracing stopped: " + ex.GetType().Name + ": " + ex.Message;
            VulkanDevice.Stage("failed: " + ex);
            TearDown();
            Invalidate();
        }

        private static Matrix4x4 ToNumerics(Matrix4 m)
        {
            return new Matrix4x4(
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.M41, m.M42, m.M43, m.M44);
        }

        private void PaintMessage(Graphics g, string message = null)
        {
            g.Clear(Color.Black);

            using (Brush brush = new SolidBrush(Color.FromArgb(200, 200, 200)))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(message ?? _status, Font, brush, ClientRectangle, format);
            }
        }

        // ==================== input, as the GL view has it ====================

        public void FocusRenderSurface()
        {
            if (CanFocus)
                Focus();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);

            if (!FocusOnHover)
                return;

            Form form = FindForm();

            if (form != null && form == Form.ActiveForm)
                FocusRenderSurface();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            FocusRenderSurface();

            if (Camera != null && CameraHost != null && (CameraMouseButtons & e.Button) != 0)
            {
                Camera.MouseDown(CameraHost, e.Button);
                _cameraButtonsDown |= e.Button;
                EnsurePumping();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (Camera != null && CameraHost != null && (_cameraButtonsDown & e.Button) != 0)
            {
                Camera.MouseUp(CameraHost, e.Button);
                _cameraButtonsDown &= ~e.Button;
                PushCamera();
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Camera != null && CameraHost != null)
            {
                Camera.MouseWheel(CameraHost, e);
                CameraMove?.Invoke(this, EventArgs.Empty);
                PushCamera();
            }

            base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            _keysDown[e.KeyCode] = true;

            if (Camera != null && CameraHost != null)
            {
                Camera.KeyDown(CameraHost, e.KeyCode);
                EnsurePumping();
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            _keysDown[e.KeyCode] = false;
            base.OnKeyUp(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            // Keys released while another window had the focus never arrive here.
            _keysDown.Clear();
            base.OnLostFocus(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            // Arrows and the like are navigation, not focus movement.
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                    return true;
            }

            return base.IsInputKey(keyData);
        }
    }
}
