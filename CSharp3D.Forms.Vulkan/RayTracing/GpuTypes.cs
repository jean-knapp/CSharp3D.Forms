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

    /// <summary>A surface, as the shader sees it. 32 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MaterialRecord
    {
        public Vector4 Color;
        public int AlbedoTexture;
        public uint Flags;
        public float Roughness;
        public float Metallic;

        public const uint FlagSky = 1;
        public const uint FlagUnlit = 2;
        public const uint FlagTranslucent = 4;
    }

    /// <summary>A light in Unreal's terms: candela with a radius, or lux. 64 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuLight
    {
        public Vector4 PositionRadius;
        public Vector4 DirectionCone;
        public Vector4 Radiance;
        public Vector4 Params;

        public const float TypePoint = 0;
        public const float TypeSpot = 1;
        public const float TypeSun = 2;
    }

    /// <summary>Everything that changes per frame. 224 bytes.</summary>
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

        public const uint FlagReset = 1;
    }

    /// <summary>Push constants of denoise.comp. 32 bytes.</summary>
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

    /// <summary>Push constants of luminance.comp.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ExposurePush
    {
        public float DeltaSeconds;
        public float ExposureBias;
        public float MinEV100;
        public float MaxEV100;
        public uint Reset;
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
}
