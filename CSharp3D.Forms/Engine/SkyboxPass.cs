using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// Draws a <see cref="SkyboxCube"/> behind everything else a <see cref="Controls.RendererControl"/>
    /// draws: one full-screen triangle whose fragment shader turns each pixel into a view
    /// direction and looks it up in the six faces.
    ///
    /// <para>
    /// A direction per pixel rather than a textured box around the camera, for three reasons. The
    /// sky is infinitely far away, so no box is big enough and every box clips against the far
    /// plane; the faces stay what they are on disk, so a 2:1 side face is stretched in the shader
    /// rather than resampled into a square bitmap; and the faces are uploaded as linear light,
    /// so an HDR sky is tone mapped here with the same curve the other views use.
    /// </para>
    /// <para>
    /// It is never shaded: no view's lighting, fog or flat shade reaches it. The sky is its own
    /// light, which is how the engine draws it too.
    /// </para>
    /// </summary>
    public sealed class SkyboxPass : IDisposable
    {
        /// <summary>The sky to draw; null draws nothing.</summary>
        public SkyboxCube Cube
        {
            get { return _cube; }
            set { _cube = value; }
        }

        /// <summary>Whether the pass draws at all. Hosts toggle this rather than dropping the cube.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>What linear light is multiplied by before the curve. 1 for an LDR sky.</summary>
        public float Exposure { get; set; } = 1f;

        /// <summary>
        /// The luminance that lands on white; brighter light rolls off. 1 with an exposure of 1
        /// reproduces an 8-bit sky's own bytes exactly.
        /// </summary>
        public float White { get; set; } = 1f;

        /// <summary>What shows where a face is missing.</summary>
        public System.Drawing.Color Void { get; set; } = System.Drawing.Color.FromArgb(19, 20, 22);

        /// <summary>How pixels become directions: a camera, or the whole sky unrolled.</summary>
        public SkyboxProjection Projection { get; set; } = SkyboxProjection.Perspective;

        /// <summary>
        /// A line along every edge between faces, where a seam would show; transparent draws
        /// none. Drawn in the shader, a pixel wide wherever it lands, so it follows the sky
        /// exactly rather than being traced over a picture of it afterwards.
        /// </summary>
        public System.Drawing.Color SeamColor { get; set; } = System.Drawing.Color.Transparent;

        /// <summary>The seam colour inside the face being edited, so it can be found.</summary>
        public System.Drawing.Color SelectedSeamColor { get; set; } = System.Drawing.Color.Transparent;

        /// <summary>The cube face whose seams take <see cref="SelectedSeamColor"/>, -1 for none.</summary>
        public int SelectedFace { get; set; } = -1;

        /// <summary>A line where the sky meets the horizon (the cube's y = 0); transparent draws none.</summary>
        public System.Drawing.Color HorizonColor { get; set; } = System.Drawing.Color.Transparent;

        private SkyboxCube _cube;

        /// <summary>The GL objects of one context: program, empty VAO, the six textures.</summary>
        private sealed class ContextState
        {
            public int Program;
            public int Vao;
            public readonly int[] Textures = new int[6];
            public readonly bool[] Present = new bool[6];
            public SkyboxCube UploadedCube;
            public int UploadedVersion = -1;

            public int CubeFromWorld, Right, Up, Forward, TanHalf, Viewport;
            public int Exposure, White, Void, Present0, Half0, FacesBase;
            public int Equirect, Seam, SelectedSeam, Selected, Horizon;
        }

        private readonly Dictionary<object, ContextState> _states = new Dictionary<object, ContextState>();

        /// <summary>
        /// Draw the sky for a camera. The context must be current. Leaves depth testing and
        /// writing as it found them: the sky writes no depth, so whatever is drawn next sits in
        /// front of it.
        /// </summary>
        public void Draw(object context, Matrix4 view, Matrix4 projection, int width, int height)
        {
            if (!Enabled || _cube == null || width <= 0 || height <= 0)
                return;

            // The camera's axes in world space. OpenTK's matrices act on row vectors, so the
            // rows of the inverse view matrix are the camera's right, up and back directions.
            Matrix4 inverse = view;
            inverse.Row3 = new Vector4(0, 0, 0, 1);
            inverse.Invert();

            Vector3 right = inverse.Row0.Xyz;
            Vector3 up = inverse.Row1.Xyz;
            Vector3 forward = -inverse.Row2.Xyz;

            // A perspective matrix keeps 1/tan(half the field of view) on its diagonal.
            float tanX = projection.M11 != 0f ? 1f / projection.M11 : 1f;
            float tanY = projection.M22 != 0f ? 1f / projection.M22 : 1f;

            DrawBasis(context, right, up, forward, tanX, tanY, width, height);
        }

        /// <summary>
        /// Draw the sky for a camera given as its axes in world space and the tangents of half
        /// its field of view. The context must be current, and the viewport already set.
        /// </summary>
        public void DrawBasis(object context, Vector3 right, Vector3 up, Vector3 forward,
            float tanX, float tanY, int width, int height)
        {
            if (!Enabled || _cube == null || width <= 0 || height <= 0)
                return;

            ContextState state = StateFor(context);

            if (state == null)
                return;

            Upload(state);

            bool depthTest = GL.IsEnabled(EnableCap.DepthTest);
            bool cull = GL.IsEnabled(EnableCap.CullFace);
            bool blend = GL.IsEnabled(EnableCap.Blend);
            GL.GetBoolean(GetPName.DepthWritemask, out bool depthWrite);

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
            GL.DepthMask(false);
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

            GL.UseProgram(state.Program);

            Matrix3 m = _cube.CubeFromWorld;
            float[] rows = { m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33 };

            // Row-major in, so GLSL's column-major mat3 wants it transposed.
            GL.UniformMatrix3(state.CubeFromWorld, 1, true, rows);
            GL.Uniform3(state.Right, right);
            GL.Uniform3(state.Up, up);
            GL.Uniform3(state.Forward, forward);
            GL.Uniform2(state.TanHalf, tanX, tanY);
            GL.Uniform2(state.Viewport, (float)width, (float)height);
            GL.Uniform1(state.Exposure, Exposure);
            GL.Uniform1(state.White, Math.Max(0.01f, White));
            GL.Uniform3(state.Void, Void.R / 255f, Void.G / 255f, Void.B / 255f);
            GL.Uniform1(state.Equirect, Projection == SkyboxProjection.Equirectangular ? 1 : 0);
            GL.Uniform4(state.Seam, Rgba(SeamColor));
            GL.Uniform4(state.SelectedSeam, Rgba(SelectedSeamColor));
            GL.Uniform1(state.Selected, SelectedFace);
            GL.Uniform4(state.Horizon, Rgba(HorizonColor));

            for (int i = 0; i < 6; i++)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + i);
                GL.BindTexture(TextureTarget.Texture2D, state.Present[i] ? state.Textures[i] : 0);
                GL.Uniform1(state.FacesBase + i, i);
                GL.Uniform1(state.Present0 + i, state.Present[i] ? 1 : 0);
                GL.Uniform1(state.Half0 + i, _cube.IsHalfHeight(i) ? 1 : 0);
            }

            GL.BindVertexArray(state.Vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            GL.BindVertexArray(0);

            for (int i = 5; i >= 0; i--)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + i);
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }

            GL.UseProgram(0);

            if (depthTest) GL.Enable(EnableCap.DepthTest);
            if (cull) GL.Enable(EnableCap.CullFace);
            if (blend) GL.Enable(EnableCap.Blend);
            GL.DepthMask(depthWrite);
        }

        private static Vector4 Rgba(System.Drawing.Color color)
        {
            return new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        }

        // ── GL objects ───────────────────────────────────────────────────────

        private ContextState StateFor(object context)
        {
            ContextState state;

            if (_states.TryGetValue(context, out state))
                return state;

            int program = Compile();

            if (program == 0)
                return null;

            state = new ContextState { Program = program, Vao = GL.GenVertexArray() };
            GL.GenTextures(6, state.Textures);

            state.CubeFromWorld = GL.GetUniformLocation(program, "uCubeFromWorld");
            state.Right = GL.GetUniformLocation(program, "uRight");
            state.Up = GL.GetUniformLocation(program, "uUp");
            state.Forward = GL.GetUniformLocation(program, "uForward");
            state.TanHalf = GL.GetUniformLocation(program, "uTanHalf");
            state.Viewport = GL.GetUniformLocation(program, "uViewport");
            state.Exposure = GL.GetUniformLocation(program, "uExposure");
            state.White = GL.GetUniformLocation(program, "uWhite");
            state.Void = GL.GetUniformLocation(program, "uVoid");
            state.FacesBase = GL.GetUniformLocation(program, "uFaces[0]");
            state.Present0 = GL.GetUniformLocation(program, "uPresent[0]");
            state.Half0 = GL.GetUniformLocation(program, "uHalf[0]");
            state.Equirect = GL.GetUniformLocation(program, "uEquirect");
            state.Seam = GL.GetUniformLocation(program, "uSeam");
            state.SelectedSeam = GL.GetUniformLocation(program, "uSelectedSeam");
            state.Selected = GL.GetUniformLocation(program, "uSelected");
            state.Horizon = GL.GetUniformLocation(program, "uHorizon");

            _states[context] = state;
            return state;
        }

        /// <summary>
        /// Puts the faces on the card when the cube or its contents changed. Half floats: an HDR
        /// sky needs the range, and the precision is well past what the tone curve can show.
        /// </summary>
        private void Upload(ContextState state)
        {
            if (ReferenceEquals(state.UploadedCube, _cube) && state.UploadedVersion == _cube.Version)
                return;

            for (int i = 0; i < 6; i++)
            {
                SkyboxCubeFace face = _cube.Faces[i];
                state.Present[i] = face != null;

                if (face == null)
                    continue;

                GL.BindTexture(TextureTarget.Texture2D, state.Textures[i]);
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb16f, face.Width, face.Height, 0,
                    PixelFormat.Rgb, PixelType.Float, face.Rgb);

                // Clamped: a face's edge must not borrow from its opposite edge, which is on the
                // far side of the sky.
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);

            state.UploadedCube = _cube;
            state.UploadedVersion = _cube.Version;
        }

        /// <summary>Frees one context's objects. The context must be current.</summary>
        public void Dispose(object context)
        {
            ContextState state;

            if (!_states.TryGetValue(context, out state))
                return;

            GL.DeleteTextures(6, state.Textures);
            GL.DeleteVertexArray(state.Vao);
            GL.DeleteProgram(state.Program);

            _states.Remove(context);
        }

        public void Dispose()
        {
            // GL objects can only be freed with their context current; a host that has one
            // calls Dispose(context). Dropping the references is all that is left here.
            _states.Clear();
            _cube = null;
        }

        private static int Compile()
        {
            int vertex = CompileStage(ShaderType.VertexShader, VertexSource);
            int fragment = CompileStage(ShaderType.FragmentShader, FragmentSource);

            if (vertex == 0 || fragment == 0)
                return 0;

            int program = GL.CreateProgram();
            GL.AttachShader(program, vertex);
            GL.AttachShader(program, fragment);
            GL.LinkProgram(program);
            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);

            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);

            if (linked == 0)
            {
                System.Diagnostics.Debug.WriteLine("Skybox pass failed to link: " + GL.GetProgramInfoLog(program));
                GL.DeleteProgram(program);
                return 0;
            }

            return program;
        }

        private static int CompileStage(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, 1, new[] { source }, (int[])null);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);

            if (compiled == 0)
            {
                System.Diagnostics.Debug.WriteLine("Skybox pass failed to compile: " + GL.GetShaderInfoLog(shader));
                GL.DeleteShader(shader);
                return 0;
            }

            return shader;
        }

        // ── Shaders ──────────────────────────────────────────────────────────

        private const string VertexSource = @"#version 330 core
// One triangle that covers the screen; no vertex buffer needed.
void main()
{
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(p * 2.0 - 1.0, 1.0, 1.0);
}
";

        /// <summary>
        /// The face selection is the cube's: +X, -X, +Y, -Y, +Z, -Z, each read from inside with the
        /// viewer's right as +u. It is the same table the CPU preview and the ray tracer use.
        /// </summary>
        private const string FragmentSource = @"#version 330 core
uniform mat3 uCubeFromWorld;
uniform vec3 uRight;
uniform vec3 uUp;
uniform vec3 uForward;
uniform vec2 uTanHalf;
uniform vec2 uViewport;
uniform float uExposure;
uniform float uWhite;
uniform vec3 uVoid;
uniform sampler2D uFaces[6];
uniform int uPresent[6];
uniform int uHalf[6];
uniform int uEquirect;
uniform vec4 uSeam;
uniform vec4 uSelectedSeam;
uniform int uSelected;
uniform vec4 uHorizon;

out vec4 fragColor;

vec3 sampleFace(int face, vec2 uv)
{
    // GLSL 3.30 only indexes sampler arrays with constants.
    if (face == 0) return texture(uFaces[0], uv).rgb;
    if (face == 1) return texture(uFaces[1], uv).rgb;
    if (face == 2) return texture(uFaces[2], uv).rgb;
    if (face == 3) return texture(uFaces[3], uv).rgb;
    if (face == 4) return texture(uFaces[4], uv).rgb;
    return texture(uFaces[5], uv).rgb;
}

float curve(float x)
{
    // Extended Reinhard: linear below white, rolling off above it.
    if (x <= 0.0) return 0.0;
    float w2 = uWhite * uWhite;
    return min(1.0, x * (1.0 + x / w2) / (1.0 + x));
}

float toSrgb(float c)
{
    return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055;
}

void main()
{
    vec3 world;

    if (uEquirect != 0)
    {
        // The whole sky on one rectangle: longitude across, latitude up, longitude 0 at the
        // centre looking along the camera's forward - the projection an HDRI arrives in.
        vec2 t = gl_FragCoord.xy / uViewport;
        float lon = (t.x * 2.0 - 1.0) * 3.14159265;
        float lat = (t.y - 0.5) * 3.14159265;
        world = normalize(uForward * (cos(lat) * cos(lon)) + uRight * (cos(lat) * sin(lon)) + uUp * sin(lat));
    }
    else
    {
        vec2 ndc = gl_FragCoord.xy / uViewport * 2.0 - 1.0;
        world = normalize(uForward + uRight * (ndc.x * uTanHalf.x) + uUp * (ndc.y * uTanHalf.y));
    }

    vec3 d = uCubeFromWorld * world;

    vec3 a = abs(d);
    int face;
    float sc, tc, ma;

    if (a.x >= a.y && a.x >= a.z)
    {
        ma = a.x;
        if (d.x > 0.0) { face = 0; sc = d.z;  tc = -d.y; }
        else           { face = 1; sc = -d.z; tc = -d.y; }
    }
    else if (a.y >= a.z)
    {
        ma = a.y;
        if (d.y > 0.0) { face = 2; sc = d.x; tc = -d.z; }
        else           { face = 3; sc = d.x; tc = d.z; }
    }
    else
    {
        ma = a.z;
        if (d.z > 0.0) { face = 4; sc = -d.x; tc = -d.y; }
        else           { face = 5; sc = d.x;  tc = -d.y; }
    }

    vec2 uv = vec2(sc / ma, tc / ma) * 0.5 + 0.5;

    // A 2:1 side face is the sky above the horizon: the engine scales its coordinate by two
    // and clamps, so the picture fills the upper half and its last row runs to the floor.
    if (uHalf[face] != 0)
        uv.y = min(1.0, uv.y * 2.0);

    vec3 c = uPresent[face] != 0 ? sampleFace(face, uv) * uExposure : vec3(0.0);
    vec3 shown = vec3(toSrgb(curve(c.r)), toSrgb(curve(c.g)), toSrgb(curve(c.b)));

    // The overlays, a pixel wide wherever they land. A seam is where the face coordinate
    // reaches its edge; the horizon is where the direction stops rising.
    float edge = 1.0 - max(abs(sc), abs(tc)) / ma;
    float seam = 1.0 - smoothstep(0.0, max(fwidth(edge), 1e-6) * 1.25, edge);
    float horizon = 1.0 - smoothstep(0.0, max(fwidth(d.y), 1e-6) * 1.25, abs(d.y));

    vec4 seamColor = face == uSelected ? uSelectedSeam : uSeam;

    if (uPresent[face] == 0)
        shown = uVoid;

    shown = mix(shown, uHorizon.rgb, uHorizon.a * horizon);
    shown = mix(shown, seamColor.rgb, seamColor.a * seam);

    fragColor = vec4(shown, 1.0);
}
";
    }

    /// <summary>How the sky pass turns a pixel into a direction.</summary>
    public enum SkyboxProjection
    {
        /// <summary>A camera: the sky as seen from inside it.</summary>
        Perspective,

        /// <summary>Longitude across the whole width, latitude up the whole height.</summary>
        Equirectangular,
    }
}
