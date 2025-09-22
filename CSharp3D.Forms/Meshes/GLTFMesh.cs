using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Engine.Helpers;
using CSharp3D.Forms.Utils;
using OpenTK;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// A mesh that loads 3D models from GLTF (GL Transmission Format) files.
    /// Automatically extracts materials and textures from the GLTF file.
    /// 
    /// Usage:
    /// 1. Set the FilePath property to point to your GLTF file
    /// 2. The mesh will automatically load and extract materials
    /// 3. Use GetMeshNames() and GetMaterialNames() to see what was loaded
    /// 4. Use SetMeshMaterial() to assign different materials to different meshes
    /// 
    /// Note: This implementation includes a complete GLTF parser that can handle
    /// both embedded and external binary data files.
    /// </summary>
    [Description("A mesh that loads 3D models from GLTF files with automatic material extraction")]
    [ToolboxItem(true)]
    public class GLTFMesh : Mesh
    {
        private string _filePath = "";
        private bool _isLoaded = false;
        private GLTFParser.GLTFData _gltfData;

        /// <summary>
        /// The file path to the GLTF file.
        /// </summary>
        [Category("GLTF")]
        [Description("The file path to the GLTF file.")]
        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    _isLoaded = false;
                    _gltfData = null;
                    
                    if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
                    {
                        LoadGLTFFile();
                    }
                }
            }
        }

        /// <summary>
        /// Whether the GLTF file has been successfully loaded.
        /// </summary>
        [Category("GLTF")]
        [Description("Whether the GLTF file has been successfully loaded.")]
        [Browsable(false)]
        public bool IsLoaded => _isLoaded;

        /// <summary>
        /// The number of meshes loaded from the GLTF file.
        /// </summary>
        [Category("GLTF")]
        [Description("The number of meshes loaded from the GLTF file.")]
        [Browsable(false)]
        public int MeshCount => _gltfData?.Meshes.Count ?? 0;

        public GLTFMesh() : base()
        {
            // Set default material
            Material = new Material();
        }

        public GLTFMesh(LocationVector position, RotationVector rotation) : base(position, rotation)
        {
            // Set default material
            Material = new Material();
        }

        /// <summary>
        /// Loads the GLTF file and extracts mesh and material data.
        /// </summary>
        private void LoadGLTFFile()
        {
            try
            {
                if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
                {
                    throw new FileNotFoundException($"GLTF file not found: {_filePath}");
                }

                // Clear existing data
                _gltfData = null;

                // Parse GLTF file
                _gltfData = GLTFParser.ParseGLTF(_filePath);

                // Convert GLTF materials to engine materials
                ConvertMaterials();

                _isLoaded = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading GLTF file: {ex.Message}");
                _isLoaded = false;
            }
        }

        /// <summary>
        /// Converts GLTF materials to engine materials.
        /// </summary>
        private void ConvertMaterials()
        {
            if (_gltfData?.Materials == null) return;

            List<Material> _extractedMaterials = new List<Material>();

            foreach (var gltfMaterial in _gltfData.Materials)
            {
                var material = new Material
                {
                    ShaderName = "Generic",
                    Color = gltfMaterial.BaseColor,
                    Alpha = gltfMaterial.BaseColor.A / 255.0f,
                    SpecularStrength = 0.5f
                };

                // Load textures from GLTF data
                LoadMaterialTextures(material, gltfMaterial);

                _extractedMaterials.Add(material);
            }

            // Always use the first GLTF material (override any user-assigned material)
            if (_extractedMaterials.Count > 0)
            {
                Material = _extractedMaterials[0];
            }
            else
            {
                // Create a default material if no materials were found in GLTF
                Material = new Material
                {
                    ShaderName = "Generic",
                    Color = Color.White,
                    Alpha = 1.0f
                };
            }
        }

        /// <summary>
        /// Loads textures for a material from GLTF data.
        /// </summary>
        private void LoadMaterialTextures(Material material, GLTFParser.GLTFMaterial gltfMaterial)
        {
            Console.WriteLine($"Loading textures for material: {gltfMaterial.Name}");
            Console.WriteLine($"  BaseColorTextureIndex: {gltfMaterial.BaseColorTextureIndex}");
            Console.WriteLine($"  NormalTextureIndex: {gltfMaterial.NormalTextureIndex}");
            Console.WriteLine($"  MetallicRoughnessTextureIndex: {gltfMaterial.MetallicRoughnessTextureIndex}");
            Console.WriteLine($"  Available textures: {_gltfData.Textures.Count}");

            // Load base color texture (albedo)
            if (gltfMaterial.BaseColorTextureIndex >= 0 && gltfMaterial.BaseColorTextureIndex < _gltfData.Textures.Count)
            {
                Console.WriteLine($"Loading albedo texture from index {gltfMaterial.BaseColorTextureIndex}");
                var texture = CreateTextureFromGLTF(_gltfData.Textures[gltfMaterial.BaseColorTextureIndex]);
                if (texture != null)
                {
                    material.Albedo = texture;
                    Console.WriteLine($"  Albedo texture loaded: {texture.Bitmap?.Width}x{texture.Bitmap?.Height}");
                }
                else
                {
                    Console.WriteLine($"  Failed to load albedo texture");
                }
            }

            // Load normal texture
            if (gltfMaterial.NormalTextureIndex >= 0 && gltfMaterial.NormalTextureIndex < _gltfData.Textures.Count)
            {
                Console.WriteLine($"Loading normal texture from index {gltfMaterial.NormalTextureIndex}");
                var texture = CreateTextureFromGLTF(_gltfData.Textures[gltfMaterial.NormalTextureIndex]);
                if (texture != null)
                {
                    material.Normal = texture;
                    Console.WriteLine($"  Normal texture loaded: {texture.Bitmap?.Width}x{texture.Bitmap?.Height}");
                }
                else
                {
                    Console.WriteLine($"  Failed to load normal texture");
                }
            }

            // Load metallic/roughness texture as specular
            if (gltfMaterial.MetallicRoughnessTextureIndex >= 0 && gltfMaterial.MetallicRoughnessTextureIndex < _gltfData.Textures.Count)
            {
                Console.WriteLine($"Loading specular texture from index {gltfMaterial.MetallicRoughnessTextureIndex}");
                var texture = CreateTextureFromGLTF(_gltfData.Textures[gltfMaterial.MetallicRoughnessTextureIndex]);
                if (texture != null)
                {
                    material.Specular = texture;
                    Console.WriteLine($"  Specular texture loaded: {texture.Bitmap?.Width}x{texture.Bitmap?.Height}");
                }
                else
                {
                    Console.WriteLine($"  Failed to load specular texture");
                }
            }

            material.SpecularStrength = 0.5f;
        }

        /// <summary>
        /// Creates a Texture object from GLTF texture data.
        /// </summary>
        private Texture CreateTextureFromGLTF(GLTFParser.GLTFTexture gltfTexture)
        {
            if (gltfTexture?.Image == null || gltfTexture.Image.Data == null)
            {
                return null;
            }

            try
            {
                // Create a bitmap from the image data
                using (var stream = new MemoryStream(gltfTexture.Image.Data))
                {
                    var bitmap = new Bitmap(stream);
                    var texture = new Texture
                    {
                        Bitmap = bitmap
                    };
                    return texture;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading texture '{gltfTexture.Name}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the vertex array for the GLTF mesh.
        /// </summary>
        /// <returns>The vertex array</returns>
        public override float[] GetGLVertexArray()
        {
            if (!_isLoaded || _gltfData?.Meshes == null || _gltfData.Meshes.Count == 0)
            {
                return new float[0];
            }

            // For now, return the first mesh's vertex data
            // In a full implementation, you might want to combine all meshes or handle multiple meshes differently
            var mesh = _gltfData.Meshes[0];
            var vertices = mesh.Vertices;

            // Positions (x, y, z) , Normals (nx, ny, nz), Texture Coords (u, v)
            float[] result = new float[vertices.Count * 8];

            for (int i = 0; i < vertices.Count; i++)
            {
                result[i * 8] = vertices[i].Z;
                result[i * 8 + 1] = vertices[i].Y;
                result[i * 8 + 2] = -vertices[i].X;
                result[i * 8 + 3] = 0; // Normal X
                result[i * 8 + 4] = 0; // Normal Y
                result[i * 8 + 5] = 0; // Normal Z
                result[i * 8 + 6] = vertices[i].U;
                result[i * 8 + 7] = vertices[i].V;
            }

            // Calculate normals
            result = GenerateFaceNormals(result);

            return result;
        }

        /// <summary>
        /// Gets the index array for the GLTF mesh.
        /// </summary>
        /// <returns>The index array</returns>
        public override uint[] GetIndexArray()
        {
            if (!_isLoaded || _gltfData?.Meshes == null || _gltfData.Meshes.Count == 0)
            {
                return new uint[0];
            }

            // For now, return the first mesh's index data
            return _gltfData.Meshes[0].Indices.ToArray();
        }

        /// <summary>
        /// Reloads the GLTF file.
        /// </summary>
        public void Reload()
        {
            if (!string.IsNullOrEmpty(_filePath))
            {
                LoadGLTFFile();
            }
        }

        /// <summary>
        /// Override the Material property to prevent user changes after GLTF loading.
        /// The material is automatically set from the GLTF file.
        /// </summary>
        [Browsable(false)]
        public new Material Material
        {
            get => base.Material;
            set
            {
                // Only allow setting material before GLTF is loaded
                if (!_isLoaded)
                {
                    base.Material = value;
                }
                // After GLTF is loaded, ignore user attempts to change material
                // The material will be automatically set from GLTF data
            }
        }
    }
}
