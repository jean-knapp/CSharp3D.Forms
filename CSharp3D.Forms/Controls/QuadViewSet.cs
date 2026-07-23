using CSharp3D.Forms.Cameras;
using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Meshes;
using System.Windows.Forms;

namespace CSharp3D.Forms.Controls
{
    /// <summary>
    /// The four views of a Hammer-style quad viewport — three orthographic (Top / Front / Side) and
    /// one perspective — sharing a single <see cref="Scene"/>, each with its own camera, draw mode
    /// and dynamic grid.
    ///
    /// Deliberately **not** a control: it owns the views but not their arrangement, so a host can
    /// lay them out however it likes (a splitter grid the user can resize, a tabbed pane, one view
    /// full-screen) without this having an opinion. That also keeps the layout free to use a UI
    /// toolkit this library does not reference. Call <see cref="AttachTo"/> with four host panels.
    /// </summary>
    public class QuadViewSet
    {
        public RendererControl TopView { get; private set; }
        public RendererControl FrontView { get; private set; }
        public RendererControl SideView { get; private set; }
        public RendererControl PerspectiveView { get; private set; }

        public OrtographicCamera TopCamera { get; private set; }
        public OrtographicCamera FrontCamera { get; private set; }
        public OrtographicCamera SideCamera { get; private set; }
        public OrbitalCamera PerspectiveCamera { get; private set; }

        /// <summary> The Hammer-style grid of each orthographic view, in Top/Front/Side order. </summary>
        public DynamicGrid[] Grids { get; private set; }

        private Scene _scene;
        private bool _showGrid = true;
        private float _gridSpacing = 8f;

        /// <summary> All four renderers, in Top / Front / Side / Perspective order. </summary>
        public RendererControl[] Views
        {
            get { return new[] { TopView, FrontView, SideView, PerspectiveView }; }
        }

        /// <summary> The orthographic renderers, parallel to <see cref="Grids"/>. </summary>
        public RendererControl[] OrthographicViews
        {
            get { return new[] { TopView, FrontView, SideView }; }
        }

        /// <summary> The scene shared by all four views. </summary>
        public Scene Scene
        {
            get { return _scene; }
            set
            {
                _scene = value;

                foreach (RendererControl view in Views)
                    view.Scene = value;

                // The grids live per-view, but still need a scene to resolve their shader.
                for (int i = 0; i < Grids.Length; i++)
                    Grids[i].SetScene(value);
            }
        }

        /// <summary> Whether the orthographic views draw their grid. </summary>
        public bool ShowGrid
        {
            get { return _showGrid; }
            set
            {
                if (_showGrid == value)
                    return;

                _showGrid = value;
                ApplyGridVisibility();
                InvalidateAll();
            }
        }

        /// <summary>
        /// The grid step in world units. This is the base step: the views double it as needed so
        /// the lines stay legible when zoomed out. An editor should keep this in step with its
        /// snap size, so the grid you see is the grid you snap to.
        /// </summary>
        public float GridSpacing
        {
            get { return _gridSpacing; }
            set
            {
                if (_gridSpacing == value || value <= 0f)
                    return;

                _gridSpacing = value;

                for (int i = 0; i < Grids.Length; i++)
                {
                    Grids[i].Spacing = value;
                    Grids[i].Invalidate();
                }

                InvalidateAll();
            }
        }

        public QuadViewSet()
        {
            TopCamera = OrtographicCamera.CreateTop();
            FrontCamera = OrtographicCamera.CreateFront();
            SideCamera = OrtographicCamera.CreateSide();
            PerspectiveCamera = new OrbitalCamera();

            TopView = MakeView(TopCamera);
            FrontView = MakeView(FrontCamera);
            SideView = MakeView(SideCamera);
            PerspectiveView = MakeView(PerspectiveCamera);

            Grids = new[] { new DynamicGrid(), new DynamicGrid(), new DynamicGrid() };
            AttachGrid(Grids[0], TopView, TopCamera);
            AttachGrid(Grids[1], FrontView, FrontCamera);
            AttachGrid(Grids[2], SideView, SideCamera);
        }

        /// <summary>
        /// Fills four host panels with the views, one each. The parameters are named for what the
        /// host is placing rather than for a fixed arrangement, so the layout is decided — and
        /// readable — at the call site.
        /// </summary>
        public void AttachTo(Control perspectiveHost, Control topHost, Control frontHost,
            Control sideHost)
        {
            Fill(perspectiveHost, PerspectiveView);
            Fill(topHost, TopView);
            Fill(frontHost, FrontView);
            Fill(sideHost, SideView);
        }

        private static void Fill(Control host, RendererControl view)
        {
            if (host == null)
                return;

            view.Dock = DockStyle.Fill;
            host.Controls.Add(view);
        }

        private static RendererControl MakeView(Camera camera)
        {
            RendererControl view = new RendererControl();
            view.Dock = DockStyle.Fill;
            view.Camera = camera;
            return view;
        }

        /// <summary>
        /// Gives an orthographic view its grid, rebuilt from that view's camera as it is drawn.
        /// FrameUpdate (rather than a timer) is what keeps the grid in step with panning and
        /// zooming: it fires once per painted frame, which is exactly when the view can have moved.
        /// </summary>
        private void AttachGrid(DynamicGrid grid, RendererControl view, OrtographicCamera camera)
        {
            grid.Spacing = _gridSpacing;

            for (int i = 0; i < grid.Meshes.Length; i++)
                view.ViewMeshes.Add(grid.Meshes[i]);

            view.FrameUpdate += (sender, e) =>
            {
                if (_showGrid)
                    grid.Update(camera, view.Width, view.Height);
            };
        }

        /// <summary> Adds or removes the grid meshes from the orthographic views. </summary>
        private void ApplyGridVisibility()
        {
            RendererControl[] views = OrthographicViews;

            for (int i = 0; i < Grids.Length; i++)
            {
                for (int m = 0; m < Grids[i].Meshes.Length; m++)
                {
                    LineMesh mesh = Grids[i].Meshes[m];

                    if (_showGrid)
                    {
                        if (!views[i].ViewMeshes.Contains(mesh))
                            views[i].ViewMeshes.Add(mesh);
                    }
                    else
                    {
                        views[i].ViewMeshes.Remove(mesh);
                    }
                }

                Grids[i].Invalidate();
            }
        }

        /// <summary> Requests a redraw of every view (e.g. after the scene changes). </summary>
        public void InvalidateAll()
        {
            foreach (RendererControl view in Views)
                view.Invalidate();
        }
    }
}
