using System.Numerics;
using System.Runtime.InteropServices;

namespace CSharp3D.Forms.Vulkan.RayTracing
{
    // The C# side of every struct in shaders/RayTracing/common.glsl. The shaders use scalar
    // layout, so these are laid out sequentially with no padding beyond what is written down;
    // change one side and change the other.

    /// <summary>One geometry the acceleration structures know. 32 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GeometryRecord
    {
        public ulong VertexAddress;
        public ulong IndexAddress;
        public uint MaterialIndex;
        public uint Pad0, Pad1, Pad2;
    }

    /// <summary>A surface, as the shader sees it. 112 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MaterialRecord
    {
        public Vector4 Color;
        public int AlbedoTexture;
        public uint Flags;
        public float Roughness;
        public float Metallic;

        // What a game light model needs (GpuSurface): the lightmap's luxel grid, light the
        // surface emits, how much it reflects, and x: how far off the face the compiler
        // samples, y: the alpha-test reference, z: the display factor of an unlit surface.
        public Vector4 LightmapS;
        public Vector4 LightmapT;
        public Vector4 Emission;
        public Vector4 Reflectivity;
        public Vector4 Params;

        public const uint FlagSky = GpuSurface.FlagSky;
        public const uint FlagUnlit = GpuSurface.FlagUnlit;
        public const uint FlagTranslucent = GpuSurface.FlagTranslucent;
    }

    /// <summary>
    /// A light: in Unreal's terms (candela with a radius, or lux) for the Lambda model, in the
    /// compiler's own units for a game model (GpuLightModel). 96 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuLight
    {
        public Vector4 PositionRadius;
        public Vector4 DirectionCone;
        public Vector4 Radiance;
        public Vector4 Params;

        /// <summary>Game models: the compiler's falloff terms (see common.glsl).</summary>
        public Vector4 Atten;

        /// <summary>Game models: whatever else the light's formula reads (see common.glsl).</summary>
        public Vector4 Extra;

        public const float TypePoint = 0;
        public const float TypeSpot = 1;
        public const float TypeSun = 2;

        /// <summary>Light from the whole sky, reaching a point through every direction that sees sky.</summary>
        public const float TypeSkyDome = 3;
    }

    /// <summary>
    /// One triangle of a surface that emits light (a texture light), with what direct light
    /// sampling needs to pick it in proportion to its power. 80 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuEmitter
    {
        public Vector4 P0;          // w: area
        public Vector4 P1;          // w: cumulative power up to and including this triangle
        public Vector4 P2;          // w: this triangle's power
        public Vector4 Normal;      // w: the material index
        public Vector4 Intensity;   // rgb: emitted per unit area, in the model's units
    }

    /// <summary>
    /// Which light model a frame is shaded with, and its constants. Model 0 is the default
    /// (Unreal-style, physical units); the others reproduce a game's compiler and engine and
    /// read A-D as common.glsl documents per model.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuLightModel
    {
        public uint Model;
        public uint Flags;
        public uint Pad0, Pad1;
        public Vector4 A;
        public Vector4 B;
        public Vector4 C;
        public Vector4 D;

        public const uint Physical = 0;
        public const uint Vrad = 1;
        public const uint Zhlt = 2;
        public const uint ArghRad = 3;

        /// <summary>The compiler's -extra: luxels are supersampled.</summary>
        public const uint FlagExtra = 1;

        /// <summary>The game samples world textures without filtering (GL_NEAREST).</summary>
        public const uint FlagNearestTextures = 2;

        public static readonly GpuLightModel Default = new GpuLightModel { Model = Physical };

        public bool IsGameModel => Model != Physical;
    }

    /// <summary>
    /// How the host describes a mesh to a game light model: the shading class, whether it is
    /// see-through and how, its luxel grid, what it emits and reflects. Equality is by value,
    /// so a host may build a fresh one every time it is asked.
    /// </summary>
    public sealed class GpuSurface : System.IEquatable<GpuSurface>
    {
        public const uint FlagSky = 1;
        public const uint FlagUnlit = 2;
        public const uint FlagTranslucent = 4;
        public const uint FlagAlphaTest = 8;
        public const uint FlagAdditive = 16;

        /// <summary>Light passes it: the compiler does not treat it as a shadow caster.</summary>
        public const uint FlagNoShadow = 32;

        /// <summary>LightmapS/LightmapT hold a luxel grid to sample the lighting on.</summary>
        public const uint FlagLightmapped = 64;

        /// <summary>Not drawn, but still a shadow caster (a nodraw face of a solid brush).</summary>
        public const uint FlagNoDraw = 128;

        /// <summary>A sky face the compiler's sun does not shine through.</summary>
        public const uint FlagNoSun = 256;

        /// <summary>Drawn opaque, its transparent texels black (an alpha texture the engine draws without alpha test).</summary>
        public const uint FlagHoleIsBlack = 512;

        /// <summary>
        /// Shown to the camera for the editor's sake only (a trigger, a clip, a nodraw face):
        /// camera rays see it, the lighting never does - no bounce off it, and it casts a
        /// shadow only if it would anyway (FlagNoShadow decides, as for any surface).
        /// </summary>
        public const uint FlagEditorOnly = 1024;

        public MeshClass Class = MeshClass.Opaque;
        public uint Flags;

        /// <summary>Opacity of a translucent surface (times the texture's alpha), or the amount of an additive one.</summary>
        public float Alpha = 1f;

        /// <summary>Texture alpha below this is a hole (FlagAlphaTest).</summary>
        public float AlphaTestReference = 0.5f;

        /// <summary>World (GL) space: luxel s = dot(p, S.xyz) + S.w; luxel centres at whole numbers.</summary>
        public Vector4 LightmapS;
        public Vector4 LightmapT;

        /// <summary>Light emitted per unit area, in the model's units; zero for none.</summary>
        public Vector3 Emission;

        /// <summary>Light the surface adds to its own lightmap (a texture light's self light).</summary>
        public Vector3 SelfLight;

        /// <summary>What a bounce off it keeps, per channel.</summary>
        public Vector3 Reflectivity = new Vector3(0.5f);

        /// <summary>How far off the face the compiler places a sample, world units.</summary>
        public float SampleOffset = 1f;

        /// <summary>The display factor of an unlit surface (1 = the texture as it is).</summary>
        public float UnlitFactor = 1f;

        /// <summary>The compiler's bounce patch size, world units; 0 gathers bounce at the luxel itself.</summary>
        public float PatchSize;

        /// <summary>
        /// The texture as the game samples it, when it differs from the one the mesh is drawn
        /// with in the editor; null to use the mesh's.
        /// </summary>
        public System.Drawing.Bitmap Albedo;

        public bool Equals(GpuSurface other)
        {
            return other != null && Class == other.Class && Flags == other.Flags && Alpha == other.Alpha
                && AlphaTestReference == other.AlphaTestReference && LightmapS == other.LightmapS && LightmapT == other.LightmapT
                && Emission == other.Emission && SelfLight == other.SelfLight && Reflectivity == other.Reflectivity
                && SampleOffset == other.SampleOffset && UnlitFactor == other.UnlitFactor && PatchSize == other.PatchSize
                && ReferenceEquals(Albedo, other.Albedo);
        }

        public override bool Equals(object obj) => Equals(obj as GpuSurface);

        public override int GetHashCode() => (int)Flags ^ LightmapS.GetHashCode() ^ Emission.GetHashCode();
    }

    /// <summary>Everything that changes per frame. 336 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FrameData
    {
        public Matrix4x4 InvViewProj;
        public Matrix4x4 PrevViewProj;
        public Vector4 CameraPosition;
        public Vector4 Sky;
        public uint FrameIndex;
        public uint LightCount;
        public uint Samples;
        public uint Flags;
        public Vector4 Units;
        public Vector4 History;

        // The skybox: world to the cube's frame, each face's index into the texture array
        // (-1 when absent; faces 0-3, then 4-5), the half-height mask in SkyB.Z, and in
        // SkyScale.X what a texel is multiplied by to be radiance - 0 means no skybox.
        public Matrix4x4 CubeFromWorld;
        public Int4 SkyA;
        public Int4 SkyB;
        public Vector4 SkyScale;

        // The light model (GpuLightModel) and how many emitter triangles there are, in
        // Model.Y; total emitted power in Model.Z (as float bits).
        public UInt4 Model;
        public Vector4 ModelA;
        public Vector4 ModelB;
        public Vector4 ModelC;
        public Vector4 ModelD;

        public const uint FlagReset = 1;

        /// <summary>The scene has see-through instances (raygen's FRAME_SEETHROUGH).</summary>
        public const uint FlagSeeThrough = 2;
    }

    /// <summary>Push constants of denoise.comp. 112 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DenoisePush
    {
        public uint Parity;
        public uint Source;
        public uint Dest;
        public uint Mode;
        public int Step;
        public float HistoryFull;
        public float Pad0, Pad1;

        // The light model, for the compose pass to transform a game model's light value.
        public uint Model;
        public uint ModelFlags;
        public uint Pad2, Pad3;
        public Vector4 ModelA;
        public Vector4 ModelB;
        public Vector4 ModelC;
        public Vector4 ModelD;

        public const uint SourceHistory = 0;
        public const uint SourceA = 1;
        public const uint SourceB = 2;

        public const uint ModeFilter = 0;
        public const uint ModeCompose = 1;
    }

    /// <summary>A top-level instance, exactly as VkAccelerationStructureInstanceKHR lays it out. 64 bytes.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct TlasInstance
    {
        public fixed float Transform[12];
        public uint CustomIndexAndMask;
        public uint SbtOffsetAndFlags;
        public ulong AccelerationStructureReference;
    }

    /// <summary>The exposure state shared by the two post-process passes. 16 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ExposureState
    {
        public float AdaptedLogLuminance;
        public float Exposure;
        public float Pad0, Pad1;
    }

    /// <summary>How tonemap.comp turns the composed picture into screen bytes.</summary>
    public enum TonemapMode : uint
    {
        /// <summary>Exposure, then the ACES filmic curve, then sRGB: Unreal's default.</summary>
        Filmic = 0,

        /// <summary>The picture already holds screen values (an 8-bit engine's): clamp and store.</summary>
        Display = 1,

        /// <summary>Linear light times a scalar exposure, clamped, then sRGB: Source's integer HDR.</summary>
        LinearExposure = 2,
    }

    /// <summary>Push constants of bloom.comp. 16 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BloomPush
    {
        public uint Mode;
        public float Sigma;
        public float Scale;
        public float Pad0;
    }

    /// <summary>Push constants of tonemap.comp. 16 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TonemapPush
    {
        public uint Mode;
        public float FixedScale;
        public float Pad0, Pad1;
    }

    /// <summary>Push constants of luminance.comp.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ExposurePush
    {
        public float DeltaSeconds;
        public float ExposureBias;
        public float MinEV100;
        public float MaxEV100;
        public uint Reset;
        public uint FixedExposure;
        public float FixedEV100;
        public uint SourceAuto;
        public float MinScale;
        public float MaxScale;
    }

    /// <summary>Push constants of overlay.vert / overlay.frag. 128 bytes, the guaranteed minimum.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OverlayPush
    {
        public Matrix4x4 ViewProj;
        public Vector4 Right;
        public Vector4 Up;
        public Vector4 CameraPos;
        public uint Parity;
        public uint DepthMode;
        public uint Dotted;
        public uint Texture;

        public const uint NoTexture = 0xFFFFFFFF;
    }

    /// <summary>How the host wants a mesh treated by the ray tracer.</summary>
    public enum MeshClass
    {
        /// <summary>Not part of the world: an editor guide, an icon, a wireframe.</summary>
        Skip,

        /// <summary>Solid geometry that blocks light and is lit.</summary>
        Opaque,

        /// <summary>A sky face: shows the sky, lets the sun and sky light through.</summary>
        Sky,
    }

    /// <summary>A GLSL uvec4.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UInt4
    {
        public uint X, Y, Z, W;
    }

    /// <summary>A GLSL ivec4.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Int4
    {
        public int X, Y, Z, W;

        public Int4(int x, int y, int z, int w)
        {
            X = x; Y = y; Z = z; W = w;
        }
    }
}
