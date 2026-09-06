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
        public Vector3 SkyRadiance { get; private set; }
        public Sampler Sampler { get; private set; }
        public IReadOnlyList<Image> Textures => _textures;
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
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<Bitmap, int> _textureIndices = new Dictionary<Bitmap, int>();
        private readonly List<Image> _textures = new List<Image>();

        private Buffer _vertexBuffer;
        private Buffer _indexBuffer;
        private Buffer _instanceBuffer;
        private Buffer _tlasBuffer;
        private Buffer _opaqueBlasBuffer;
        private Buffer _skyBlasBuffer;
        private AccelerationStructureKHR _opaqueBlas;
        private AccelerationStructureKHR _skyBlas;

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
            MaterialBuffer = _device.CreateBuffer(32, BufferUsageFlags.StorageBufferBit, MemoryPropertyFlags.DeviceLocalBit);
            LightBuffer = _device.CreateBuffer(64, BufferUsageFlags.StorageBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }

        /// <summary>
        /// What goes into the world when the host says nothing: triangle meshes that are
        /// drawn in a textured view, and nothing that is a line, a point, a billboard or an
        /// editor box.
        /// </summary>
        public static MeshClass DefaultClassify(Mesh mesh)
        {
            if (mesh is LineMesh || mesh is PointMesh || mesh is SpriteMesh || mesh is CuboidMesh
                || mesh is GridMesh || mesh is ParticleBatchMesh)
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
                    || a[i].Radiance != b[i].Radiance || a[i].Params != b[i].Params)
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

        public bool Sync(Scene scene, IEnumerable<Mesh> viewMeshes = null)
        {
            bool changed = false;

            SyncOverlays(scene, viewMeshes);

            if (_lightsDirty)
            {
                UploadLights();
                changed = true;
            }

            if (scene == null)
                return changed;

            List<Entry> wanted = Gather(scene);

            if (NeedsRebuild(wanted))
            {
                Rebuild(wanted);
                return true;
            }

            if (MaterialsChanged(wanted))
            {
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
                if (mesh == null || mesh.Material == null)
                    continue;

                MeshClass cls = Classifier(mesh);

                if (cls == MeshClass.Skip)
                    continue;

                // Glass and glows are the GL renderer's problem for now: drawn opaque they
                // would wall off rooms behind a window.
                if (mesh.Material.Translucent || mesh.Material.Additive || mesh.Material.Alpha < 1f)
                    continue;

                if (mesh.GetIndexCount() < 3)
                    continue;

                wanted.Add(new Entry
                {
                    Mesh = mesh,
                    Class = cls,
                    Transform = mesh.GetModelMatrix(Matrix4.Identity),
                    Color = mesh.Material.Color,
                    Albedo = mesh.Material.Albedo != null ? mesh.Material.Albedo.Bitmap : null,
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

                if (!ReferenceEquals(now.Mesh, was.Mesh) || now.Class != was.Class)
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

            List<float> vertices = new List<float>();
            List<uint> indices = new List<uint>();
            List<GeometryRecord> records = new List<GeometryRecord>();
            List<int> opaqueGeometries = new List<int>();
            List<int> skyGeometries = new List<int>();

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

                if (entry.Class == MeshClass.Sky)
                    skyGeometries.Add(record);
                else
                    opaqueGeometries.Add(record);
            }

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

            // The records are ordered opaque first, then sky, so each instance's records are
            // contiguous and gl_InstanceCustomIndexEXT + gl_GeometryIndexEXT lands on them.
            List<int> order = new List<int>(opaqueGeometries);
            order.AddRange(skyGeometries);

            GeometryRecord[] ordered = new GeometryRecord[order.Count];

            for (int k = 0; k < order.Count; k++)
                ordered[k] = recordArray[order[k]];

            GeometryBuffer.Dispose();
            GeometryBuffer = _device.CreateDeviceBuffer(ordered, BufferUsageFlags.StorageBufferBit, deviceAddress: false);

            UploadMaterials();

            _opaqueBlas = BuildBlas(opaqueGeometries, recordArray, vertexCounts, indexCounts, out _opaqueBlasBuffer);
            _skyBlas = BuildBlas(skyGeometries, recordArray, vertexCounts, indexCounts, out _skyBlasBuffer);

            BuildTlas(opaqueGeometries.Count);

            Version++;
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

                materials[i] = new MaterialRecord
                {
                    Color = new Vector4(entry.Color.R / 255f, entry.Color.G / 255f, entry.Color.B / 255f, 1f),
                    AlbedoTexture = TextureIndex(entry.Albedo),
                    Flags = entry.Class == MeshClass.Sky ? MaterialRecord.FlagSky : 0u,
                    Roughness = DefaultRoughness,
                    Metallic = 0f,
                };
            }

            MaterialBuffer.Dispose();
            MaterialBuffer = _device.CreateDeviceBuffer(materials, BufferUsageFlags.StorageBufferBit, deviceAddress: false);
            Version++;
        }

        private void UploadLights()
        {
            GpuLight[] lights = _lights.Length > 0 ? _lights : new GpuLight[1];

            ulong size = (ulong)(lights.Length * 64);

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

        private void BuildTlas(int opaqueRecordCount)
        {
            List<TlasInstance> instances = new List<TlasInstance>();

            if (_opaqueBlas.Handle != 0)
                instances.Add(MakeInstance(_opaqueBlas, customIndex: 0, mask: 0x1));

            if (_skyBlas.Handle != 0)
                instances.Add(MakeInstance(_skyBlas, customIndex: (uint)opaqueRecordCount, mask: 0x2));

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

            DestroyStructure(ref _opaqueBlas, ref _opaqueBlasBuffer);
            DestroyStructure(ref _skyBlas, ref _skyBlasBuffer);

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

            _textures.Clear();
            _textureIndices.Clear();

            GeometryBuffer?.Dispose();
            MaterialBuffer?.Dispose();
            LightBuffer?.Dispose();

            if (Sampler.Handle != 0)
            {
                _device.Api.DestroySampler(_device.Device, Sampler, null);
                Sampler = default;
            }
        }
    }
}
