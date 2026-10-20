using CSharp3D.Forms.Cameras;
using CSharp3D.Forms.Engine;
using OpenTK;
using System;
using System.Collections.Generic;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// A Hammer-style grid for an orthographic view: it covers exactly what the camera can see and
    /// its spacing adapts to the zoom, instead of being a fixed square of fixed-size cells.
    ///
    /// The rules are ported from Hammer (`hammer/mapview2dbase.cpp` `DrawGrid` /
    /// `HighlightGridLine` / `SetColorMode`, defaults from `options.cpp`):
    ///  - the spacing doubles until lines are at least <see cref="MinPixelSpacing"/> apart, so a
    ///    zoomed-out view never turns into a solid block of lines;
    ///  - with <see cref="HideSmallGrid"/> on, below <see cref="HideMinorBelowPixels"/> the plain
    ///    lines are dropped entirely and only the highlights remain;
    ///  - the zero lines get the axis colour, every 1024 units the 1024 colour, and every 64 units
    ///    and every <see cref="HighlightSpec"/>-th line of the effective spacing the highlight
    ///    colour — those are the lines you navigate by;
    ///  - with <see cref="Dots"/> on, the plain lines (and the 64 / Nth highlights, which Hammer
    ///    stops highlighting in dot mode) become a dot at every intersection.
    ///
    /// The grid plane comes from the camera's own view basis, so it cannot disagree with the view
    /// it is drawn in, and it sits at the back of the view volume so geometry always wins the
    /// depth test. Because a mesh has one colour, the grid is several meshes that
    /// <see cref="Update"/> fills together — add all of <see cref="Meshes"/> to a view.
    /// </summary>
    public class DynamicGrid
    {
        /// <summary> Grid step in world units. Editors usually track their snap step here. </summary>
        public float Spacing { get; set; } = 8f;

        /// <summary> Lines are never drawn closer together than this (Hammer uses 2 px). </summary>
        public float MinPixelSpacing { get; set; } = 2f;

        /// <summary> Below this on-screen spacing only highlighted lines are drawn (Hammer: 4 px). </summary>
        public float HideMinorBelowPixels { get; set; } = 4f;

        /// <summary> Whether small grids drop their plain lines (Hammer's "Hide grid smaller than 4 pixels", on). </summary>
        public bool HideSmallGrid { get; set; } = true;

        /// <summary> Highlight interval in world units (Hammer's "Highlight every 64 units", on). </summary>
        public float MajorInterval { get; set; } = 64f;

        /// <summary> Highlight every <see cref="HighlightSpec"/>-th line (Hammer's GridHigh10, on). </summary>
        public bool HighlightEveryNth { get; set; } = true;

        /// <summary> Which lines <see cref="HighlightEveryNth"/> picks, counted in grid steps (Hammer's iGridHighSpec, 8). </summary>
        public int HighlightSpec { get; set; } = 8;

        /// <summary> Highlight every 1024 units in their own colour (Hammer's GridHigh1024, on). </summary>
        public bool Highlight1024 { get; set; } = true;

        /// <summary> Dots at the intersections instead of plain lines (Hammer's GridDots, off). </summary>
        public bool Dots { get; set; } = false;

        /// <summary>
        /// The grid stops at ±this, the map's extent (Hammer clamps to g_MIN/MAX_MAP_COORD, and
        /// always draws those border lines). 0 means unbounded.
        /// </summary>
        public float MapLimit { get; set; } = 0f;

        public LineMesh MinorLines { get; private set; }
        public LineMesh MajorLines { get; private set; }
        public LineMesh Lines1024 { get; private set; }
        public LineMesh AxisLines { get; private set; }
        public PointMesh DotPoints { get; private set; }

        /// <summary> All the meshes, back to front. Add these to a view. </summary>
        public Mesh[] Meshes { get; private set; }

        // What the last Update was built for, so an idle repaint rebuilds nothing.
        private float _lastScale = float.NaN;
        private float _lastX, _lastY, _lastZ;
        private int _lastWidth, _lastHeight;
        private float _lastSpacing;

        public DynamicGrid()
        {
            // Hammer's white-on-black colours at the default grid intensity of 30
            // (options.cpp ReadColorSettings, mapview2dbase.cpp SetColorMode/AdjustColorIntensity):
            // grid 255*30% = 76, highlight 255*45% = 114, dots 255*50% = 127, 1024 unscaled.
            MinorLines = NewLineMesh(System.Drawing.Color.FromArgb(76, 76, 76));
            MajorLines = NewLineMesh(System.Drawing.Color.FromArgb(114, 114, 114));
            Lines1024 = NewLineMesh(System.Drawing.Color.FromArgb(100, 50, 5));
            AxisLines = NewLineMesh(System.Drawing.Color.FromArgb(0, 100, 100));

            DotPoints = new PointMesh { Clickable = false, PointSize = 1f };
            DotPoints.Material.Color = System.Drawing.Color.FromArgb(127, 127, 127);

            Meshes = new Mesh[] { DotPoints, MinorLines, MajorLines, Lines1024, AxisLines };
        }

        private static LineMesh NewLineMesh(System.Drawing.Color color)
        {
            LineMesh mesh = new LineMesh();
            mesh.Clickable = false;
            mesh.Material.Color = color;
            return mesh;
        }

        /// <summary> Points every mesh at a scene, so they can resolve their shader. </summary>
        public void SetScene(Scene scene)
        {
            for (int i = 0; i < Meshes.Length; i++)
                Meshes[i].Scene = scene;
        }

        /// <summary>
        /// Rebuilds the grid for what <paramref name="camera"/> currently sees. Cheap to call every
        /// frame: it returns immediately unless the view actually moved.
        /// </summary>
        public void Update(OrtographicCamera camera, int width, int height)
        {
            if (camera == null || width <= 0 || height <= 0)
                return;

            LocationVector location = camera.Location;

            if (!HasChanged(camera, location, width, height))
                return;

            _lastScale = camera.OrthoScale;
            _lastX = location.X;
            _lastY = location.Y;
            _lastZ = location.Z;
            _lastWidth = width;
            _lastHeight = height;
            _lastSpacing = Spacing;

            LocationVector right, up;
            camera.GetViewBasis(out right, out up);

            int horizontal = DominantAxis(right);
            int vertical = DominantAxis(up);
            if (horizontal == vertical)
                return; // degenerate basis; nothing sensible to draw

            // The remaining axis is the one we look along (0 + 1 + 2 = 3).
            int depthAxis = 3 - horizontal - vertical;

            float unitsPerPixel = camera.OrthoScale / height;
            if (unitsPerPixel <= 0f || float.IsNaN(unitsPerPixel))
                return;

            float spacing = ChooseSpacing(unitsPerPixel);
            bool hideMinor = HideSmallGrid && (spacing / unitsPerPixel) < HideMinorBelowPixels;

            // Hammer: s_iCustomGridSpacing = nGridSpacing * iGridHighSpec, on the spacing
            // actually drawn (after the doubling), so the highlight stays every Nth visible line.
            float nthInterval = HighlightEveryNth && HighlightSpec > 0 ? spacing * HighlightSpec : 0f;

            float[] center = { location.X, location.Y, location.Z };

            // The visible half-extents. Both axes share one units-per-pixel: the projection is
            // OrthoScale*aspect wide over `width` px and OrthoScale tall over `height`.
            float halfWidth = width * unitsPerPixel * 0.5f;
            float halfHeight = height * unitsPerPixel * 0.5f;

            // Snap outward by a cell so lines never pop in at the edges while panning.
            float hMin = FloorTo(center[horizontal] - halfWidth - spacing, spacing);
            float hMax = CeilTo(center[horizontal] + halfWidth + spacing, spacing);
            float vMin = FloorTo(center[vertical] - halfHeight - spacing, spacing);
            float vMax = CeilTo(center[vertical] + halfHeight + spacing, spacing);

            if (MapLimit > 0f)
            {
                hMin = Math.Max(hMin, -MapLimit);
                hMax = Math.Min(hMax, MapLimit);
                vMin = Math.Max(vMin, -MapLimit);
                vMax = Math.Min(vMax, MapLimit);
            }

            float depth = GridDepth(camera, right, up, center, depthAxis);

            List<LocationVector> minor = new List<LocationVector>();
            List<LocationVector> major = new List<LocationVector>();
            List<LocationVector> lines1024 = new List<LocationVector>();
            List<LocationVector> axis = new List<LocationVector>();
            List<LocationVector> dots = new List<LocationVector>();

            if (hMin <= hMax && vMin <= vMax)
            {
                // Hammer draws the dots first, only when the small grid is shown at all.
                if (Dots && !hideMinor)
                {
                    for (float v = vMin; v <= vMax; v += spacing)
                    {
                        for (float h = hMin; h <= hMax; h += spacing)
                            dots.Add(Point(horizontal, h, vertical, v, depthAxis, depth));
                    }
                }

                // Lines of constant vertical coordinate, spanning the visible width.
                for (float v = vMin; v <= vMax; v += spacing)
                {
                    List<LocationVector> target = Classify(v, nthInterval, minor, major, lines1024, axis, hideMinor);
                    if (target == null)
                        continue;

                    target.Add(Point(horizontal, hMin, vertical, v, depthAxis, depth));
                    target.Add(Point(horizontal, hMax, vertical, v, depthAxis, depth));
                }

                // Lines of constant horizontal coordinate, spanning the visible height.
                for (float h = hMin; h <= hMax; h += spacing)
                {
                    List<LocationVector> target = Classify(h, nthInterval, minor, major, lines1024, axis, hideMinor);
                    if (target == null)
                        continue;

                    target.Add(Point(horizontal, h, vertical, vMin, depthAxis, depth));
                    target.Add(Point(horizontal, h, vertical, vMax, depthAxis, depth));
                }
            }

            Assign(MinorLines, minor);
            Assign(MajorLines, major);
            Assign(Lines1024, lines1024);
            Assign(AxisLines, axis);
            Assign(DotPoints, dots);
        }

        /// <summary>
        /// Hammer's rule: keep doubling until the lines are far enough apart to be worth drawing.
        /// </summary>
        private float ChooseSpacing(float unitsPerPixel)
        {
            float spacing = Spacing > 0f ? Spacing : 1f;

            // Bounded: each step doubles, so this converges quickly even from a huge zoom-out.
            for (int i = 0; i < 40 && (spacing / unitsPerPixel) < MinPixelSpacing; i++)
                spacing *= 2f;

            return spacing;
        }

        /// <summary>
        /// Which list a line at <paramref name="coordinate"/> belongs to, or null when it is not
        /// drawn as a line (hidden small grid, or a plain line in dot mode). Hammer's
        /// HighlightGridLine order: axis, then 1024, then 64, then every Nth — and in dot mode the
        /// 64 and Nth highlights fall back to being plain grid. The map border is always a line.
        /// </summary>
        private List<LocationVector> Classify(float coordinate, float nthInterval, List<LocationVector> minor,
            List<LocationVector> major, List<LocationVector> lines1024, List<LocationVector> axis, bool hideMinor)
        {
            if (IsMultipleOf(coordinate, 0f))
                return axis;

            if (Highlight1024 && IsMultipleOf(coordinate, 1024f))
                return lines1024;

            if (!Dots)
            {
                if (MajorInterval > 0f && IsMultipleOf(coordinate, MajorInterval))
                    return major;

                if (nthInterval > 0f && IsMultipleOf(coordinate, nthInterval))
                    return major;
            }

            if (hideMinor)
                return null;

            bool border = MapLimit > 0f && Math.Abs(Math.Abs(coordinate) - MapLimit) < 1e-3f;

            return !Dots || border ? minor : null;
        }

        /// <summary>
        /// Places the grid at the back of the view volume so model geometry always wins the depth
        /// test. In an orthographic view the distance costs nothing visually — there is no
        /// perspective — it only settles the depth ordering.
        /// </summary>
        private static float GridDepth(OrtographicCamera camera, LocationVector right,
            LocationVector up, float[] center, int depthAxis)
        {
            // The camera looks along -(right x up).
            Vector3 viewDirection = -Vector3.Cross(
                new Vector3(right.X, right.Y, right.Z),
                new Vector3(up.X, up.Y, up.Z));

            float sign = Component(viewDirection, depthAxis) >= 0f ? 1f : -1f;

            // Just inside the far plane. FarPlane is measured along the view direction.
            return center[depthAxis] + sign * Math.Abs(camera.FarPlane) * 0.9f;
        }

        private bool HasChanged(OrtographicCamera camera, LocationVector location,
            int width, int height)
        {
            return _lastScale != camera.OrthoScale
                || _lastSpacing != Spacing
                || _lastX != location.X
                || _lastY != location.Y
                || _lastZ != location.Z
                || _lastWidth != width
                || _lastHeight != height;
        }

        /// <summary> Forces the next <see cref="Update"/> to rebuild. </summary>
        public void Invalidate()
        {
            _lastScale = float.NaN;
        }

        private static void Assign(LineMesh mesh, List<LocationVector> lines)
        {
            mesh.Vertices = lines.ToArray();

            // LineMesh rebuilds its vertex array on demand, but the GPU buffer is only uploaded
            // once unless it is told the data moved.
            mesh.NeedsVertexUpdate = true;
        }

        /// <summary> A point with two grid coordinates and the plane's depth, in world axes. </summary>
        private static LocationVector Point(int horizontalAxis, float h, int verticalAxis, float v,
            int depthAxis, float depth)
        {
            float[] p = new float[3];
            p[horizontalAxis] = h;
            p[verticalAxis] = v;
            p[depthAxis] = depth;

            return new LocationVector(p[0], p[1], p[2]);
        }

        /// <summary> The world axis a (unit, axis-aligned) direction points along. </summary>
        private static int DominantAxis(LocationVector direction)
        {
            float x = Math.Abs(direction.X);
            float y = Math.Abs(direction.Y);
            float z = Math.Abs(direction.Z);

            if (x >= y && x >= z)
                return 0;

            return y >= z ? 1 : 2;
        }

        private static float Component(Vector3 v, int axis)
        {
            return axis == 0 ? v.X : (axis == 1 ? v.Y : v.Z);
        }

        /// <summary>
        /// Whether a coordinate lands on a multiple of <paramref name="interval"/> (or on zero
        /// when the interval is zero). The tolerance keeps float drift on large coordinates from
        /// dropping the odd highlight.
        /// </summary>
        private static bool IsMultipleOf(float coordinate, float interval)
        {
            if (interval <= 0f)
                return Math.Abs(coordinate) < 1e-3f;

            float remainder = Math.Abs(coordinate % interval);
            return remainder < 1e-3f || Math.Abs(remainder - interval) < 1e-3f;
        }

        private static float FloorTo(float value, float step)
        {
            return (float)Math.Floor(value / step) * step;
        }

        private static float CeilTo(float value, float step)
        {
            return (float)Math.Ceiling(value / step) * step;
        }
    }
}
