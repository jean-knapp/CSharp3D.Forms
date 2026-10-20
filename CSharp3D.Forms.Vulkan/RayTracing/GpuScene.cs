using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Meshes;
using CSharp3D.Forms.Vulkan.Vk;
using OpenTK;
using Silk.NET.Vulkan;
using Buffer = CSharp3D.Forms.Vulkan.Vk.VulkanDevice.Buffer;
using Image = CSharp3D.Forms.Vulkan.Vk.VulkanDevice.Image;
using Texture = CSharp3D.Forms.Engine.Texture;
// Both the scene model (OpenTK) and the GPU records (System.Numerics) have a Vector3; the
// GPU one is what this file means when it does not say.
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace CSharp3D.Forms.Vulkan.RayTracing
{
    /// <summary>
    /// The scene as the GPU ray tracer holds it, kept in step with a <see cref="Scene"/>.
    ///
    /// The scene is the GL renderer's own object model - the map editor builds one and four
    /// views draw it - so this reads the same meshes the GL views do, through the same
    /// <see cref="Mesh.GetGLVertexArray"/>, and turns them into acceleration structures. Two
    /// bottom-level structures hold everything: one for solid geometry and one for sky
    /// faces, which have to be a separate instance so that shadow rays can be masked past
    /// them to the sun. Props are baked into world space rather than instanced: a map has
    /// hundreds of them and thousands of faces, and one build of everything is cheaper than
    /// managing a structure per prop.
    ///
    /// <see cref="Sync"/> is called once per frame and is cheap when nothing changed: it walks
    /// the mesh list comparing identity, transform and material against the last build, and
    /// only rebuilds what that comparison says it must.
    /// </summary>
    public sealed unsafe class GpuScene : IDisposable
    {
        private readonly VulkanDevice _device;

        /// <summary>Which meshes are in the ray traced world, and as what. The host decides.</summary>
        public Func<Mesh, MeshClass> Classifier { get; set; } = DefaultClassify;

        /// <summary>Roughness given to a surface the host has not said otherwise about.</summary>
        public float DefaultRoughness { get; set; } = 0.8f;

        /// <summary>Bumped whenever a buffer or texture was recreated, so views rewrite descriptors.</summary>
        public int Version { get; private set; }

        public AccelerationStructureKHR Tlas { get; private set; }
        public Buffer GeometryBuffer { get; private set; }
        public Buffer MaterialBuffer { get; private set; }
        public Buffer LightBuffer { get; private set; }
        public int LightCount { get; private set; }

        /// <summary>Whether any instance is see-through (glass, fences, additive faces).</summary>
        public bool HasSeeThrough { get; private set; }
        public Vector3 SkyRadiance { get; private set; }
        public Sampler Sampler { get; private set; }
        /// <summary>
        /// Every sampled image, in descriptor order: the scene's own textures, then the sky faces
        /// that are present. One array because the texture binding has a variable count and so
        /// must stay the last binding of the set; <see cref="SkyFaceIndex"/> says where each face is.
        /// </summary>
        public IReadOnlyList<Image> Textures
        {
            get
            {
                if (_allTexturesVersion != Version)
                {
                    _allTextures.Clear();
                    _allTextures.AddRange(_textures);

                    for (int i = 0; i < 6; i++)
                    {
                        _skyTextureIndex[i] = -1;

                        if (_skyImages[i] == null)
                            continue;

                        _skyTextureIndex[i] = _allTextures.Count;
                        _allTextures.Add(_skyImages[i]);
                    }

                    _allTexturesVersion = Version;
                }

                return _allTextures;
            }
        }

        private readonly List<Image> _allTextures = new List<Image>();
        private int _allTexturesVersion = -1;

        // ==================== sky ====================

        /// <summary>A cube face's index in <see cref="Textures"/>, in cube order, or -1 when it is absent.</summary>
        public int SkyFaceIndex(int face)
        {
            IReadOnlyList<Image> refreshed = Textures;
            return _skyTextureIndex[face];
        }

        /// <summary>Bit i set when cube face i holds only the sky above the horizon.</summary>
        public uint SkyHalfMask { get; private set; }

        /// <summary>What a sky texel is multiplied by to be radiance in this renderer's units; 0 with no sky.</summary>
        public float SkyScale { get; private set; }

        /// <summary>World (GL space) to the cube's frame, rows as in <see cref="SkyboxCube.CubeFromWorld"/>.</summary>
        public System.Numerics.Matrix4x4 CubeFromWorld { get; private set; } = System.Numerics.Matrix4x4.Identity;

        private readonly Image[] _skyImages = new Image[6];
        private readonly int[] _skyTextureIndex = { -1, -1, -1, -1, -1, -1 };
        private SkyboxCube _pendingSky;
        private int _pendingSkyVersion = -1;
        private float _pendingSkyScale;
        private bool _skyDirty;
        private SkyboxCube _uploadedSky;
        private int _uploadedSkyVersion = -1;

        /// <summary>
        /// The sky rays see when they leave the world or pass through a sky face; null goes back
        /// to the flat sky radiance. <paramref name="scale"/> turns the faces' linear light into
        /// this renderer's radiance. Takes effect at the next sync.
        /// </summary>
        public void SetSkybox(SkyboxCube cube, float scale)
        {
            int version = cube != null ? cube.Version : -1;

            if (ReferenceEquals(cube, _pendingSky) && version == _pendingSkyVersion && scale == _pendingSkyScale)
                return;

            _pendingSky = cube;
            _pendingSkyVersion = version;
            _pendingSkyScale = scale;
            _skyDirty = true;
        }

        /// <summary>Puts the pending sky on the card. Only the faces go up; nothing is rebuilt.</summary>
        private void UploadSky()
        {
            _skyDirty = false;

            SkyboxCube cube = _pendingSky;
            SkyScale = cube != null ? _pendingSkyScale : 0f;

            if (ReferenceEquals(cube, _uploadedSky) && _pendingSkyVersion == _uploadedSkyVersion)
            {
                Version++;
                return;
            }

            _device.WaitIdle();

            for (int i = 0; i < 6; i++)
            {
                _skyImages[i]?.Dispose();
                _skyImages[i] = null;
            }

            SkyHalfMask = 0;

            if (cube != null)
            {
                Matrix3 m = cube.CubeFromWorld;

                CubeFromWorld = new System.Numerics.Matrix4x4(
                    m.M11, m.M12, m.M13, 0f,
                    m.M21, m.M22, m.M23, 0f,
                    m.M31, m.M32, m.M33, 0f,
                    0f, 0f, 0f, 1f);

                for (int i = 0; i < 6; i++)
                {
                    SkyboxCubeFace face = cube.Faces[i];

                    if (face == null)
                        continue;

                    _skyImages[i] = UploadFloat(face);

                    if (_skyImages[i] != null && cube.IsHalfHeight(i))
                        SkyHalfMask |= 1u << i;
                }
            }

            _uploadedSky = cube;
            _uploadedSkyVersion = _pendingSkyVersion;
            Version++;
        }

        /// <summary>
        /// A sky face as half floats, one level: a ray that reaches the sky has no footprint to
        /// pick a mip from, and the sky is only ever seen magnified.
        /// </summary>
        private Image UploadFloat(SkyboxCubeFace face)
        {
            uint width = (uint)face.Width;
            uint height = (uint)face.Height;
            int pixels = face.Width * face.Height;

            ushort[] halves = new ushort[pixels * 4];

            for (int p = 0; p < pixels; p++)
            {
                halves[p * 4] = HalfFrom(face.Rgb[p * 3]);
                halves[p * 4 + 1] = HalfFrom(face.Rgb[p * 3 + 1]);
                halves[p * 4 + 2] = HalfFrom(face.Rgb[p * 3 + 2]);
                halves[p * 4 + 3] = 0x3C00;   // 1.0
            }

            ulong bytes = (ulong)halves.Length * 2;

            Buffer staging = _device.CreateBuffer(bytes, BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            staging.Write(halves);

            Image image = _device.CreateImage(width, height, Format.R16G16B16A16Sfloat,
                ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit, 1);

            CommandBuffer cmd = _device.BeginOneShot();
            _device.Transition(cmd, image, ImageLayout.TransferDstOptimal, 1);

            BufferImageCopy region = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D(width, height, 1),
            };

            _device.Api.CmdCopyBufferToImage(cmd, staging.Handle, image.Handle, ImageLayout.TransferDstOptimal, 1, &region);

            MipBarrier(cmd, image, 0, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);
            image.Layout = ImageLayout.ShaderReadOnlyOptimal;

            _device.EndOneShot(cmd);
            staging.Dispose();

            return image;
        }

        /// <summary>An IEEE half from a float, rounded to nearest; negatives and NaN go to 0, the rest clamps to the largest finite half.</summary>
        private static ushort HalfFrom(float value)
        {
            if (float.IsNaN(value) || value <= 0f)
                return 0;

            if (value >= 65504f)
                return 0x7BFF;

            uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
            int exponent = (int)((bits >> 23) & 0xFF) - 127 + 15;
            uint mantissa = bits & 0x7FFFFF;

            if (exponent <= 0)
            {
                if (exponent < -10)
                    return 0;

                mantissa |= 0x800000;
                int shift = 14 - exponent;
                uint small = mantissa >> shift;

                if (((mantissa >> (shift - 1)) & 1) != 0)
                    small++;

                return (ushort)small;
            }

            uint result = ((uint)exponent << 10) | (mantissa >> 13);

            if ((mantissa & 0x1000) != 0)
                result++;

            return (ushort)Math.Min(result, 0x7BFFu);
        }
        public int GeometryCount => _entries.Count;

        /// <summary>The key this scene uses when asking a mesh whether its vertices changed.</summary>
        private readonly object _contextKey = new object();

        private struct Entry
        {
            public Mesh Mesh;
            public MeshClass Class;
            public Matrix4 Transform;
            public Color Color;
            public Bitmap Albedo;
            public int MaterialIndex;

            /// <summary>What the host said about the mesh (Describer); null without one.</summary>
            public GpuSurface Surface;

            /// <summary>Which rays may hit it: the Mask* bits.</summary>
            public uint Mask;
        }

        // Instance masks, the same bits as common.glsl: what primary and bounce rays see (opaque,
        // or see-through and handled hit by hit), the sky, and what blocks the light.
        public const uint MaskVisible = 0x1;
        public const uint MaskTransparent = 0x2;
        public const uint MaskSky = 0x4;
        public const uint MaskOccluder = 0x8;

        /// <summary>Editor-only surfaces (<see cref="GpuSurface.FlagEditorOnly"/>): seen by camera rays, never by lighting.</summary>
        public const uint MaskEditor = 0x10;

        /// <summary>
        /// How the host describes each mesh to a game light model. When set it replaces
        /// <see cref="Classifier"/>: null (or Skip) leaves a mesh out, and see-through
        /// surfaces are kept and drawn see-through instead of being dropped.
        /// </summary>
        public Func<Mesh, GpuSurface> Describer { get; set; }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<Bitmap, int> _textureIndices = new Dictionary<Bitmap, int>();
        private readonly List<Image> _textures = new List<Image>();

        private Buffer _vertexBuffer;
        private Buffer _indexBuffer;
        private Buffer _instanceBuffer;
        private Buffer _tlasBuffer;

        /// <summary>One bottom-level structure per distinct mask.</summary>
        private readonly List<AccelerationStructureKHR> _blases = new List<AccelerationStructureKHR>();
        private readonly List<Buffer> _blasBuffers = new List<Buffer>();

        /// <summary>The triangles that emit light, for direct sampling. Never null.</summary>
        public Buffer EmitterBuffer { get; private set; }

        public int EmitterCount { get; private set; }

        /// <summary>The emitters' total power (the sum the sampling CDF ends at).</summary>
        public float EmitterPower { get; private set; }

        private GpuLight[] _lights = new GpuLight[0];
        private bool _lightsDirty = true;

        public GpuScene(VulkanDevice device)
        {
            _device = device;

            SamplerCreateInfo info = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                MipmapMode = SamplerMipmapMode.Linear,
                AddressModeU = SamplerAddressMode.Repeat,
                AddressModeV = SamplerAddressMode.Repeat,
                AddressModeW = SamplerAddressMode.Repeat,
                MaxLod = 16,

                // The device enables the feature; 8x is what Unreal's default texture group
                // samples with.
                AnisotropyEnable = true,
                MaxAnisotropy = 8,
            };

            Sampler sampler;
            VulkanDevice.Check(_device.Api.CreateSampler(_device.Device, &info, null, &sampler), "vkCreateSampler");
            Sampler = sampler;

            // Empty placeholders, so a view can bind before the first sync.
            GeometryBuffer = _device.CreateBuffer(32, BufferUsageFlags.StorageBufferBit, MemoryPropertyFlags.DeviceLocalBit);
            MaterialBuffer = _device.CreateBuffer(112, BufferUsageFlags.StorageBufferBit, MemoryPropertyFlags.DeviceLocalBit);
            LightBuffer = _device.CreateBuffer(96, BufferUsageFlags.StorageBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            EmitterBuffer = _device.CreateBuffer(80, BufferUsageFlags.StorageBufferBit, MemoryPropertyFlags.DeviceLocalBit);
        }

        /// <summary>Which rays may hit a mesh of this class and surface.</summary>
        private static uint MaskOf(MeshClass cls, GpuSurface surface)
        {
            if (cls == MeshClass.Sky)
                return MaskSky;

            if (surface == null)
                return MaskVisible | MaskOccluder;

            uint mask = 0;

            if ((surface.Flags & GpuSurface.FlagEditorOnly) != 0)
            {
                // Camera rays only (raygen's traceGame adds MASK_EDITOR to its first trace).
                mask |= MaskEditor;
            }
            else if ((surface.Flags & GpuSurface.FlagNoDraw) == 0)
            {
                bool seeThrough = (surface.Flags & (GpuSurface.FlagTranslucent | GpuSurface.FlagAlphaTest | GpuSurface.FlagAdditive)) != 0;
                mask |= seeThrough ? MaskTransparent : MaskVisible;
            }

            if ((surface.Flags & GpuSurface.FlagNoShadow) == 0)
                mask |= MaskOccluder;

            return mask;
        }

        /// <summary>
        /// What goes into the world when the host says nothing: triangle meshes that are
        /// drawn in a textured view, and nothing that is a line, a point, a billboard or an
        /// editor box.
        /// </summary>
        public static MeshClass DefaultClassify(Mesh mesh)
        {
            if (mesh is LineMesh || mesh is PointMesh || mesh is SpriteMesh || mesh is GridMesh || mesh is ParticleBatchMesh)
                return MeshClass.Skip;

            // A box is a wall or a crate - unless it is unlit, which is how an editor marks
            // the box it stands in for an entity with.
            if (mesh is CuboidMesh && mesh.Material != null && mesh.Material.Unlit)
                return MeshClass.Skip;

            if (mesh.ViewFilter == MeshViewFilter.WireframeViewsOnly)
                return MeshClass.Skip;

            return MeshClass.Opaque;
        }

        // ==================== lights ====================

        /// <summary>Replace the lights and the sky. Takes effect at the next sync.</summary>
        public void SetLights(GpuLight[] lights, Vector3 skyRadiance)
        {
            lights = lights ?? new GpuLight[0];

            // The host sends the list on every refresh; only a different one is a change,
            // since a change starts the whole picture over.
            if (_lights != null && SkyRadiance == skyRadiance && SameLights(_lights, lights))
                return;

            _lights = lights;
            SkyRadiance = skyRadiance;
            _lightsDirty = true;
        }

        private static bool SameLights(GpuLight[] a, GpuLight[] b)
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].PositionRadius != b[i].PositionRadius || a[i].DirectionCone != b[i].DirectionCone
                    || a[i].Radiance != b[i].Radiance || a[i].Params != b[i].Params
                    || a[i].Atten != b[i].Atten || a[i].Extra != b[i].Extra)
                    return false;
            }

            return true;
        }

        // ==================== sync ====================

        /// <summary>
        /// Bring the GPU copy up to date. Returns true when anything changed that makes the
        /// frames accumulated so far wrong - which is any change at all.
        /// </summary>
        /// <summary>The guides to draw over the picture, as last gathered. Never null.</summary>
        public OverlaySet Overlays { get; private set; } = OverlaySet.Empty;

        /// <summary>
        /// Gather the overlays - the scene's line, box and icon meshes plus the view's own -
        /// into a fresh snapshot. True when they differ from the last snapshot. An overlay
        /// change never restarts the picture: the overlays sit on top of it.
        /// </summary>
        public bool SyncOverlays(Scene scene, IEnumerable<Mesh> viewMeshes)
        {
            OverlaySet built = OverlayGather.Build(scene?.Meshes, viewMeshes, TextureIndex, Overlays);

            if (ReferenceEquals(built, Overlays))
                return false;

            Overlays = built;
            return true;
        }

        /// <param name="beforeGpuChange">
        /// Called once, just before the first buffer the frame in flight may be reading is
        /// replaced - the host waits for that frame there. A paint that changes nothing (the
        /// common case: the window was uncovered, a tooltip came and went) never waits for the
        /// GPU, so a slow frame cannot stall the UI.
        /// </param>
        public bool Sync(Scene scene, IEnumerable<Mesh> viewMeshes = null, Action beforeGpuChange = null)
        {
            bool changed = false;
            bool ready = beforeGpuChange == null;

            SyncOverlays(scene, viewMeshes);

            if (_lightsDirty)
            {
                if (!ready) { beforeGpuChange(); ready = true; }
                UploadLights();
                changed = true;
            }

            if (_skyDirty)
            {
                if (!ready) { beforeGpuChange(); ready = true; }
                UploadSky();
                changed = true;
            }

            if (scene == null)
                return changed;

            List<Entry> wanted = Gather(scene);

            if (NeedsRebuild(wanted))
            {
                if (!ready) { beforeGpuChange(); ready = true; }
                Rebuild(wanted);
                return true;
            }

            if (MaterialsChanged(wanted))
            {
                if (!ready) { beforeGpuChange(); ready = true; }

                // Same geometry, different tints (a selection changed): only the material
                // records move, and only the textures that are new go up.
                for (int i = 0; i < wanted.Count; i++)
                {
                    Entry entry = wanted[i];
                    entry.MaterialIndex = _entries[i].MaterialIndex;
                    _entries[i] = entry;
                }

                UploadMaterials();
                changed = true;
            }

            return changed;
        }

        private List<Entry> Gather(Scene scene)
        {
            List<Entry> wanted = new List<Entry>(scene.Meshes.Count);

            foreach (Mesh mesh in scene.Meshes)
            {
                if (mesh == null)
                    continue;

                GpuSurface surface = null;
                MeshClass cls;

                // The GL renderer draws a mesh with no material plain white; so does this.
                Material material = mesh.Material;

                if (Describer != null)
                {
                    surface = Describer(mesh);

                    if (surface == null || surface.Class == MeshClass.Skip)
                        continue;

                    cls = surface.Class;
                }
                else
                {
                    cls = Classifier(mesh);

                    if (cls == MeshClass.Skip)
                        continue;

                    // Without a host to say how a see-through surface is drawn, glass and
                    // glows are left to the GL renderer: drawn opaque they would wall off
                    // rooms behind a window.
                    if (material != null && (material.Translucent || material.Additive || material.Alpha < 1f))
                        continue;
                }

                if (mesh.GetIndexCount() < 3)
                    continue;

                wanted.Add(new Entry
                {
                    Mesh = mesh,
                    Class = cls,
                    Transform = mesh.GetModelMatrix(Matrix4.Identity),
                    Color = material != null ? material.Color : Color.White,
                    Albedo = surface?.Albedo ?? (material != null && material.Albedo != null ? material.Albedo.Bitmap : null),
                    Surface = surface,
                    Mask = MaskOf(cls, surface),
                });
            }

            return wanted;
        }

        private bool NeedsRebuild(List<Entry> wanted)
        {
            if (_vertexBuffer == null || wanted.Count != _entries.Count)
                return true;

            for (int i = 0; i < wanted.Count; i++)
            {
                Entry now = wanted[i];
                Entry was = _entries[i];

                if (!ReferenceEquals(now.Mesh, was.Mesh) || now.Class != was.Class || now.Mask != was.Mask)
                    return true;

                // The emitters are built with the geometry.
                if ((now.Surface?.Emission ?? Vector3.Zero) != (was.Surface?.Emission ?? Vector3.Zero))
                    return true;

                if (now.Transform != was.Transform)
                    return true;

                if (now.Mesh.IsVertexUpdatePending(_contextKey))
                    return true;
            }

            return false;
        }

        private bool MaterialsChanged(List<Entry> wanted)
        {
            for (int i = 0; i < wanted.Count; i++)
            {
                if (wanted[i].Color != _entries[i].Color || !ReferenceEquals(wanted[i].Albedo, _entries[i].Albedo))
                    return true;

                if (!Equals(wanted[i].Surface, _entries[i].Surface))
                    return true;
            }

            return false;
        }

        // ==================== rebuild ====================

        private void Rebuild(List<Entry> wanted)
        {
            // The frame in flight may still be reading what is about to go.
            _device.WaitIdle();
            DestroyGeometry();

            _entries.Clear();
            _entries.AddRange(wanted);
            HasSeeThrough = wanted.Exists(e => (e.Mask & MaskTransparent) != 0);

            List<float> vertices = new List<float>();
            List<uint> indices = new List<uint>();
            List<GeometryRecord> records = new List<GeometryRecord>();

            // Records grouped by mask, in first-seen order: one bottom-level structure each.
            List<uint> groupMasks = new List<uint>();
            List<List<int>> groups = new List<List<int>>();
            List<GpuEmitter> emitters = new List<GpuEmitter>();

            // Where each record's data starts, in elements; addresses are added once the
            // buffers exist.
            List<int> vertexStarts = new List<int>();
            List<int> vertexCounts = new List<int>();
            List<int> indexStarts = new List<int>();
            List<int> indexCounts = new List<int>();

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];

                float[] meshVertices;
                uint[] meshIndices;

                try
                {
                    meshVertices = entry.Mesh.GetGLVertexArray();
                    meshIndices = entry.Mesh.GetIndexArray();
                }
                catch (Exception)
                {
                    // A mesh that cannot produce its vertices (a face whose texture never
                    // loaded) is left out rather than taking the whole build down.
                    meshVertices = new float[0];
                    meshIndices = new uint[0];
                }

                entry.Mesh.MarkVertexBufferUpdated(_contextKey);

                int vertexCount = meshVertices.Length / 8;

                if (vertexCount == 0 || meshIndices.Length < 3)
                {
                    entry.MaterialIndex = -1;
                    _entries[i] = entry;
                    continue;
                }

                entry.MaterialIndex = i;
                _entries[i] = entry;

                int vertexStart = vertices.Count / 8;
                int indexStart = indices.Count;

                Bake(meshVertices, entry.Transform, vertices);

                for (int k = 0; k < meshIndices.Length; k++)
                    indices.Add(meshIndices[k]);

                int record = records.Count;
                records.Add(new GeometryRecord { MaterialIndex = (uint)i });
                vertexStarts.Add(vertexStart);
                vertexCounts.Add(vertexCount);
                indexStarts.Add(indexStart);
                indexCounts.Add(meshIndices.Length);

                int group = groupMasks.IndexOf(entry.Mask);

                if (group < 0)
                {
                    group = groupMasks.Count;
                    groupMasks.Add(entry.Mask);
                    groups.Add(new List<int>());
                }

                groups[group].Add(record);

                if (entry.Surface != null && entry.Surface.Emission != Vector3.Zero)
                    AddEmitters(vertices, vertexStart, meshIndices, i, entry.Surface.Emission, emitters);
            }

            UploadEmitters(emitters);

            if (records.Count == 0)
            {
                Version++;
                return;
            }

            BufferUsageFlags geometryUsage = BufferUsageFlags.StorageBufferBit
                | BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr
                | BufferUsageFlags.ShaderDeviceAddressBit;

            _vertexBuffer = _device.CreateDeviceBuffer(vertices.ToArray(), geometryUsage);
            _indexBuffer = _device.CreateDeviceBuffer(indices.ToArray(), geometryUsage);

            GeometryRecord[] recordArray = records.ToArray();

            for (int r = 0; r < recordArray.Length; r++)
            {
                recordArray[r].VertexAddress = _vertexBuffer.DeviceAddress + (ulong)vertexStarts[r] * 32;
                recordArray[r].IndexAddress = _indexBuffer.DeviceAddress + (ulong)indexStarts[r] * 4;
            }

            // The records are ordered group by group, so each instance's records are contiguous
            // and gl_InstanceCustomIndexEXT + gl_GeometryIndexEXT lands on them.
            List<int> order = new List<int>();
            List<uint> firstRecords = new List<uint>();

            foreach (List<int> members in groups)
            {
                firstRecords.Add((uint)order.Count);
                order.AddRange(members);
            }

            GeometryRecord[] ordered = new GeometryRecord[order.Count];

            for (int k = 0; k < order.Count; k++)
                ordered[k] = recordArray[order[k]];

            GeometryBuffer.Dispose();
            GeometryBuffer = _device.CreateDeviceBuffer(ordered, BufferUsageFlags.StorageBufferBit, deviceAddress: false);

            UploadMaterials();

            List<TlasInstance> instances = new List<TlasInstance>();

            for (int g = 0; g < groups.Count; g++)
            {
                Buffer storage;
                AccelerationStructureKHR blas = BuildBlas(groups[g], recordArray, vertexCounts, indexCounts, out storage);

                if (blas.Handle == 0)
                    continue;

                _blases.Add(blas);
                _blasBuffers.Add(storage);
                instances.Add(MakeInstance(blas, firstRecords[g], groupMasks[g]));
            }

            BuildTlas(instances);

            Version++;
        }

        /// <summary>
        /// The triangles of an emitting surface, already in world space in <paramref name="vertices"/>,
        /// into the emitter list. Each is weighted by its power for the sampling CDF.
        /// </summary>
        private static void AddEmitters(List<float> vertices, int vertexStart, uint[] indices, int material, Vector3 emission,
            List<GpuEmitter> emitters)
        {
            float strength = Math.Max(emission.X, Math.Max(emission.Y, emission.Z));

            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                Vector3 a = VertexAt(vertices, vertexStart + (int)indices[t]);
                Vector3 b = VertexAt(vertices, vertexStart + (int)indices[t + 1]);
                Vector3 c = VertexAt(vertices, vertexStart + (int)indices[t + 2]);

                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.Length() * 0.5f;

                if (area < 1e-6f)
                    continue;

                // The face's own normal, the side its texture is on: the mesh's vertex normal.
                int n = (vertexStart + (int)indices[t]) * 8;
                Vector3 normal = new Vector3(vertices[n + 3], vertices[n + 4], vertices[n + 5]);
                normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.Normalize(cross);

                emitters.Add(new GpuEmitter
                {
                    P0 = new Vector4(a, area),
                    P1 = new Vector4(b, 0f),
                    P2 = new Vector4(c, area * strength),
                    Normal = new Vector4(normal, material),
                    Intensity = new Vector4(emission, 0f),
                });
            }
        }

        private static Vector3 VertexAt(List<float> vertices, int index)
        {
            int o = index * 8;
            return new Vector3(vertices[o], vertices[o + 1], vertices[o + 2]);
        }

        private void UploadEmitters(List<GpuEmitter> emitters)
        {
            float cumulative = 0f;

            for (int k = 0; k < emitters.Count; k++)
            {
                GpuEmitter e = emitters[k];
                cumulative += e.P2.W;
                e.P1.W = cumulative;
                emitters[k] = e;
            }

            EmitterBuffer?.Dispose();
            EmitterBuffer = emitters.Count > 0
                ? _device.CreateDeviceBuffer(emitters.ToArray(), BufferUsageFlags.StorageBufferBit, deviceAddress: false)
                : _device.CreateBuffer(80, BufferUsageFlags.StorageBufferBit, MemoryPropertyFlags.DeviceLocalBit);
            EmitterCount = emitters.Count;
            EmitterPower = cumulative;
        }

        /// <summary>Vertices through the mesh's own transform, into the shared array.</summary>
        private static void Bake(float[] source, Matrix4 transform, List<float> target)
        {
            bool identity = transform == Matrix4.Identity;
            int count = source.Length / 8;

            for (int v = 0; v < count; v++)
            {
                int o = v * 8;

                if (identity)
                {
                    for (int k = 0; k < 8; k++)
                        target.Add(source[o + k]);

                    continue;
                }

                OpenTK.Vector4 p = new OpenTK.Vector4(source[o], source[o + 1], source[o + 2], 1f) * transform;
                OpenTK.Vector4 n = new OpenTK.Vector4(source[o + 3], source[o + 4], source[o + 5], 0f) * transform;

                target.Add(p.X); target.Add(p.Y); target.Add(p.Z);
                target.Add(n.X); target.Add(n.Y); target.Add(n.Z);
                target.Add(source[o + 6]); target.Add(source[o + 7]);
            }
        }

        private void UploadMaterials()
        {
            MaterialRecord[] materials = new MaterialRecord[Math.Max(1, _entries.Count)];

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];

                GpuSurface s = entry.Surface;

                materials[i] = new MaterialRecord
                {
                    Color = new Vector4(entry.Color.R / 255f, entry.Color.G / 255f, entry.Color.B / 255f, s != null ? s.Alpha : 1f),
                    AlbedoTexture = TextureIndex(entry.Albedo),
                    Flags = (entry.Class == MeshClass.Sky ? MaterialRecord.FlagSky : 0u) | (s != null ? s.Flags : 0u),
                    Roughness = DefaultRoughness,
                    Metallic = 0f,
                };

                if (s != null)
                {
                    materials[i].LightmapS = s.LightmapS;
                    materials[i].LightmapT = s.LightmapT;
                    materials[i].Emission = new Vector4(s.SelfLight, Math.Max(s.Emission.X, Math.Max(s.Emission.Y, s.Emission.Z)));
                    materials[i].Reflectivity = new Vector4(s.Reflectivity, 0f);
                    materials[i].Params = new Vector4(s.SampleOffset, s.AlphaTestReference, s.UnlitFactor, s.PatchSize * 0.5f);
                }
            }

            MaterialBuffer.Dispose();
            MaterialBuffer = _device.CreateDeviceBuffer(materials, BufferUsageFlags.StorageBufferBit, deviceAddress: false);
            Version++;
        }

        private void UploadLights()
        {
            GpuLight[] lights = _lights.Length > 0 ? _lights : new GpuLight[1];

            ulong size = (ulong)(lights.Length * 96);

            if (LightBuffer == null || LightBuffer.Size < size)
            {
                _device.WaitIdle();
                LightBuffer?.Dispose();
                LightBuffer = _device.CreateBuffer(size, BufferUsageFlags.StorageBufferBit,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
                Version++;
            }

            LightBuffer.Write(lights);
            LightCount = _lights.Length;
            _lightsDirty = false;
        }

        // ==================== textures ====================

        private int TextureIndex(Bitmap bitmap)
        {
            if (bitmap == null)
                return -1;

            int index;

            if (_textureIndices.TryGetValue(bitmap, out index))
                return index;

            Image image = Upload(bitmap);

            if (image == null)
                return -1;

            index = _textures.Count;
            _textures.Add(image);
            _textureIndices[bitmap] = index;
            Version++;
            return index;
        }

        /// <summary>
        /// A bitmap onto the card, in the BGRA order GDI+ hands over, with its full mip chain.
        ///
        /// Rays have no screen-space derivatives to pick a mip level from, so raygen picks
        /// one from a ray cone; without the levels to pick from, every detailed texture
        /// would alias at a distance until the accumulation had supersampled it away.
        /// </summary>
        private Image Upload(Bitmap bitmap)
        {
            BitmapData data;

            try
            {
                data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            }
            catch (Exception)
            {
                return null;
            }

            uint width = (uint)bitmap.Width;
            uint height = (uint)bitmap.Height;
            ulong bytes = (ulong)(data.Stride * bitmap.Height);

            Buffer staging = _device.CreateBuffer(bytes, BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            byte[] pixels = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, (int)bytes);
            bitmap.UnlockBits(data);
            staging.Write(pixels);

            uint mipLevels = 1;

            while ((Math.Max(width, height) >> (int)mipLevels) > 0)
                mipLevels++;

            Image image = _device.CreateImage(width, height, Format.B8G8R8A8Unorm,
                ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit, mipLevels);

            CommandBuffer cmd = _device.BeginOneShot();
            _device.Transition(cmd, image, ImageLayout.TransferDstOptimal, mipLevels);

            BufferImageCopy region = new BufferImageCopy
            {
                BufferRowLength = (uint)(data.Stride / 4),
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D(width, height, 1),
            };

            _device.Api.CmdCopyBufferToImage(cmd, staging.Handle, image.Handle, ImageLayout.TransferDstOptimal, 1, &region);

            // Each level is a linear downsample of the one above it.
            int sourceWidth = (int)width;
            int sourceHeight = (int)height;

            for (uint level = 1; level < mipLevels; level++)
            {
                int targetWidth = Math.Max(1, sourceWidth / 2);
                int targetHeight = Math.Max(1, sourceHeight / 2);

                MipBarrier(cmd, image, level - 1, ImageLayout.TransferDstOptimal, ImageLayout.TransferSrcOptimal);

                ImageBlit blit = new ImageBlit
                {
                    SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, level - 1, 0, 1),
                    DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, level, 0, 1),
                };

                blit.SrcOffsets[1] = new Offset3D(sourceWidth, sourceHeight, 1);
                blit.DstOffsets[1] = new Offset3D(targetWidth, targetHeight, 1);

                _device.Api.CmdBlitImage(cmd, image.Handle, ImageLayout.TransferSrcOptimal, image.Handle, ImageLayout.TransferDstOptimal,
                    1, &blit, Filter.Linear);

                MipBarrier(cmd, image, level - 1, ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal);

                sourceWidth = targetWidth;
                sourceHeight = targetHeight;
            }

            MipBarrier(cmd, image, mipLevels - 1, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);
            image.Layout = ImageLayout.ShaderReadOnlyOptimal;

            _device.EndOneShot(cmd);

            staging.Dispose();
            return image;
        }

        /// <summary>A layout transition of one mip level.</summary>
        private void MipBarrier(CommandBuffer cmd, Image image, uint level, ImageLayout from, ImageLayout to)
        {
            ImageMemoryBarrier barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = from,
                NewLayout = to,
                SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
                Image = image.Handle,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, level, 1, 0, 1),
                SrcAccessMask = AccessFlags.MemoryWriteBit | AccessFlags.MemoryReadBit,
                DstAccessMask = AccessFlags.MemoryWriteBit | AccessFlags.MemoryReadBit,
            };

            _device.Api.CmdPipelineBarrier(cmd,
                PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit,
                0, 0, null, 0, null, 1, &barrier);
        }

        // ==================== acceleration structures ====================

        private AccelerationStructureKHR BuildBlas(List<int> geometryIds, GeometryRecord[] records,
            List<int> vertexCounts, List<int> indexCounts, out Buffer storage)
        {
            storage = null;

            if (geometryIds.Count == 0)
                return default;

            int count = geometryIds.Count;
            AccelerationStructureGeometryKHR[] geometries = new AccelerationStructureGeometryKHR[count];
            AccelerationStructureBuildRangeInfoKHR[] ranges = new AccelerationStructureBuildRangeInfoKHR[count];
            uint[] primitiveCounts = new uint[count];

            for (int k = 0; k < count; k++)
            {
                int id = geometryIds[k];

                AccelerationStructureGeometryTrianglesDataKHR triangles = new AccelerationStructureGeometryTrianglesDataKHR
                {
                    SType = StructureType.AccelerationStructureGeometryTrianglesDataKhr,
                    VertexFormat = Format.R32G32B32Sfloat,
                    VertexData = new DeviceOrHostAddressConstKHR { DeviceAddress = records[id].VertexAddress },
                    VertexStride = 32,
                    MaxVertex = (uint)(vertexCounts[id] - 1),
                    IndexType = IndexType.Uint32,
                    IndexData = new DeviceOrHostAddressConstKHR { DeviceAddress = records[id].IndexAddress },
                };

                geometries[k] = new AccelerationStructureGeometryKHR
                {
                    SType = StructureType.AccelerationStructureGeometryKhr,
                    GeometryType = GeometryTypeKHR.TrianglesKhr,
                    Flags = GeometryFlagsKHR.OpaqueBitKhr,
                    Geometry = new AccelerationStructureGeometryDataKHR { Triangles = triangles },
                };

                primitiveCounts[k] = (uint)(indexCounts[id] / 3);
                ranges[k] = new AccelerationStructureBuildRangeInfoKHR { PrimitiveCount = primitiveCounts[k] };
            }

            fixed (AccelerationStructureGeometryKHR* pGeometries = geometries)
            fixed (uint* pCounts = primitiveCounts)
            fixed (AccelerationStructureBuildRangeInfoKHR* pRanges = ranges)
            {
                AccelerationStructureBuildGeometryInfoKHR build = new AccelerationStructureBuildGeometryInfoKHR
                {
                    SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                    Type = AccelerationStructureTypeKHR.BottomLevelKhr,
                    Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr,
                    Mode = BuildAccelerationStructureModeKHR.BuildKhr,
                    GeometryCount = (uint)count,
                    PGeometries = pGeometries,
                };

                AccelerationStructureBuildSizesInfoKHR sizes = new AccelerationStructureBuildSizesInfoKHR
                {
                    SType = StructureType.AccelerationStructureBuildSizesInfoKhr,
                };

                _device.KhrAccelerationStructure.GetAccelerationStructureBuildSizes(_device.Device,
                    AccelerationStructureBuildTypeKHR.DeviceKhr, &build, pCounts, &sizes);

                AccelerationStructureKHR handle = CreateStructure(AccelerationStructureTypeKHR.BottomLevelKhr,
                    sizes.AccelerationStructureSize, out storage);

                Buffer scratch = _device.CreateBuffer(sizes.BuildScratchSize + 256,
                    BufferUsageFlags.StorageBufferBit, MemoryPropertyFlags.DeviceLocalBit, deviceAddress: true);

                build.DstAccelerationStructure = handle;
                build.ScratchData = new DeviceOrHostAddressKHR { DeviceAddress = Align(scratch.DeviceAddress, 256) };

                AccelerationStructureBuildRangeInfoKHR* rangePointer = pRanges;

                CommandBuffer cmd = _device.BeginOneShot();
                _device.KhrAccelerationStructure.CmdBuildAccelerationStructures(cmd, 1, &build, &rangePointer);
                _device.EndOneShot(cmd);

                scratch.Dispose();
                return handle;
            }
        }

        private void BuildTlas(List<TlasInstance> instances)
        {
            if (instances.Count == 0)
                return;

            _instanceBuffer = _device.CreateDeviceBuffer(instances.ToArray(),
                BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr | BufferUsageFlags.ShaderDeviceAddressBit);

            AccelerationStructureGeometryInstancesDataKHR instanceData = new AccelerationStructureGeometryInstancesDataKHR
            {
                SType = StructureType.AccelerationStructureGeometryInstancesDataKhr,
                ArrayOfPointers = false,
                Data = new DeviceOrHostAddressConstKHR { DeviceAddress = _instanceBuffer.DeviceAddress },
            };

            AccelerationStructureGeometryKHR geometry = new AccelerationStructureGeometryKHR
            {
                SType = StructureType.AccelerationStructureGeometryKhr,
                GeometryType = GeometryTypeKHR.InstancesKhr,
                Geometry = new AccelerationStructureGeometryDataKHR { Instances = instanceData },
            };

            AccelerationStructureBuildGeometryInfoKHR build = new AccelerationStructureBuildGeometryInfoKHR
            {
                SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
                Type = AccelerationStructureTypeKHR.TopLevelKhr,
                Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr,
                Mode = BuildAccelerationStructureModeKHR.BuildKhr,
                GeometryCount = 1,
                PGeometries = &geometry,
            };

            uint instanceCount = (uint)instances.Count;

            AccelerationStructureBuildSizesInfoKHR sizes = new AccelerationStructureBuildSizesInfoKHR
            {
                SType = StructureType.AccelerationStructureBuildSizesInfoKhr,
            };

            _device.KhrAccelerationStructure.GetAccelerationStructureBuildSizes(_device.Device,
                AccelerationStructureBuildTypeKHR.DeviceKhr, &build, &instanceCount, &sizes);

            Buffer storage;
            AccelerationStructureKHR tlas = CreateStructure(AccelerationStructureTypeKHR.TopLevelKhr,
                sizes.AccelerationStructureSize, out storage);
            _tlasBuffer = storage;

            Buffer scratch = _device.CreateBuffer(sizes.BuildScratchSize + 256,
                BufferUsageFlags.StorageBufferBit, MemoryPropertyFlags.DeviceLocalBit, deviceAddress: true);

            build.DstAccelerationStructure = tlas;
            build.ScratchData = new DeviceOrHostAddressKHR { DeviceAddress = Align(scratch.DeviceAddress, 256) };

            AccelerationStructureBuildRangeInfoKHR range = new AccelerationStructureBuildRangeInfoKHR { PrimitiveCount = instanceCount };
            AccelerationStructureBuildRangeInfoKHR* rangePointer = &range;

            CommandBuffer cmd = _device.BeginOneShot();
            _device.KhrAccelerationStructure.CmdBuildAccelerationStructures(cmd, 1, &build, &rangePointer);
            _device.EndOneShot(cmd);

            scratch.Dispose();
            Tlas = tlas;
        }

        private TlasInstance MakeInstance(AccelerationStructureKHR blas, uint customIndex, uint mask)
        {
            AccelerationStructureDeviceAddressInfoKHR addressInfo = new AccelerationStructureDeviceAddressInfoKHR
            {
                SType = StructureType.AccelerationStructureDeviceAddressInfoKhr,
                AccelerationStructure = blas,
            };

            TlasInstance instance = new TlasInstance
            {
                CustomIndexAndMask = (customIndex & 0xFFFFFF) | (mask << 24),
                SbtOffsetAndFlags = 0,
                AccelerationStructureReference = _device.KhrAccelerationStructure.GetAccelerationStructureDeviceAddress(_device.Device, &addressInfo),
            };

            // Identity: everything is baked into world space already.
            instance.Transform[0] = 1f;
            instance.Transform[5] = 1f;
            instance.Transform[10] = 1f;

            return instance;
        }

        private AccelerationStructureKHR CreateStructure(AccelerationStructureTypeKHR type, ulong size, out Buffer storage)
        {
            storage = _device.CreateBuffer(size,
                BufferUsageFlags.AccelerationStructureStorageBitKhr | BufferUsageFlags.ShaderDeviceAddressBit,
                MemoryPropertyFlags.DeviceLocalBit, deviceAddress: true);

            AccelerationStructureCreateInfoKHR info = new AccelerationStructureCreateInfoKHR
            {
                SType = StructureType.AccelerationStructureCreateInfoKhr,
                Buffer = storage.Handle,
                Size = size,
                Type = type,
            };

            AccelerationStructureKHR handle;
            VulkanDevice.Check(_device.KhrAccelerationStructure.CreateAccelerationStructure(_device.Device, &info, null, &handle),
                "vkCreateAccelerationStructureKHR");

            return handle;
        }

        private static ulong Align(ulong value, ulong alignment)
        {
            return (value + alignment - 1) / alignment * alignment;
        }

        // ==================== teardown ====================

        private void DestroyStructure(ref AccelerationStructureKHR handle, ref Buffer storage)
        {
            if (handle.Handle != 0)
            {
                _device.KhrAccelerationStructure.DestroyAccelerationStructure(_device.Device, handle, null);
                handle = default;
            }

            storage?.Dispose();
            storage = null;
        }

        private void DestroyGeometry()
        {
            AccelerationStructureKHR tlas = Tlas;
            DestroyStructure(ref tlas, ref _tlasBuffer);
            Tlas = default;

            for (int i = 0; i < _blases.Count; i++)
            {
                AccelerationStructureKHR blas = _blases[i];
                Buffer storage = _blasBuffers[i];
                DestroyStructure(ref blas, ref storage);
            }

            _blases.Clear();
            _blasBuffers.Clear();

            _instanceBuffer?.Dispose();
            _instanceBuffer = null;
            _vertexBuffer?.Dispose();
            _vertexBuffer = null;
            _indexBuffer?.Dispose();
            _indexBuffer = null;
        }

        public void Dispose()
        {
            _device.WaitIdle();
            DestroyGeometry();

            foreach (Image texture in _textures)
                texture.Dispose();

            for (int i = 0; i < 6; i++)
            {
                _skyImages[i]?.Dispose();
                _skyImages[i] = null;
            }

            _textures.Clear();
            _textureIndices.Clear();

            GeometryBuffer?.Dispose();
            MaterialBuffer?.Dispose();
            LightBuffer?.Dispose();
            EmitterBuffer?.Dispose();

            if (Sampler.Handle != 0)
            {
                _device.Api.DestroySampler(_device.Device, Sampler, null);
                Sampler = default;
            }
        }
    }
}
