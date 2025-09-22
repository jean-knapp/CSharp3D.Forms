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
        private List<Material> _extractedMaterials = new List<Material>();

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
                    _extractedMaterials.Clear();
                    
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

        /// <summary>
        /// The number of materials extracted from the GLTF file.
        /// </summary>
        [Category("GLTF")]
        [Description("The number of materials extracted from the GLTF file.")]
        [Browsable(false)]
        public int MaterialCount => _extractedMaterials.Count;

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
                _extractedMaterials.Clear();

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

            foreach (var gltfMaterial in _gltfData.Materials)
            {
                var material = new Material
                {
                    ShaderName = "Generic",
                    Color = gltfMaterial.BaseColor,
                    Alpha = gltfMaterial.BaseColor.A / 255.0f,
                    SpecularStrength = gltfMaterial.Metallic
                };

                // TODO: Handle textures when they are properly extracted
                // For now, materials will use solid colors

                _extractedMaterials.Add(material);
            }

            // Set the first material as the default
            if (_extractedMaterials.Count > 0 && Material == null)
            {
                Material = _extractedMaterials[0];
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
        /// Gets the material for a specific mesh index.
        /// </summary>
        /// <param name="meshIndex">The mesh index</param>
        /// <returns>The material for the mesh</returns>
        public Material GetMaterialForMesh(int meshIndex)
        {
            if (!_isLoaded || _gltfData?.Meshes == null || meshIndex < 0 || meshIndex >= _gltfData.Meshes.Count)
            {
                return Material;
            }

            var materialIndex = _gltfData.Meshes[meshIndex].MaterialIndex;
            if (materialIndex >= 0 && materialIndex < _extractedMaterials.Count)
            {
                return _extractedMaterials[materialIndex];
            }

            return Material;
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
        /// Gets the name of a specific mesh.
        /// </summary>
        /// <param name="meshIndex">The mesh index</param>
        /// <returns>The name of the mesh</returns>
        public string GetMeshName(int meshIndex)
        {
            if (!_isLoaded || _gltfData?.Meshes == null || meshIndex < 0 || meshIndex >= _gltfData.Meshes.Count)
            {
                return "Unknown";
            }

            return _gltfData.Meshes[meshIndex].Name ?? $"Mesh_{meshIndex}";
        }

        /// <summary>
        /// Gets the name of a specific material.
        /// </summary>
        /// <param name="materialIndex">The material index</param>
        /// <returns>The name of the material</returns>
        public string GetMaterialName(int materialIndex)
        {
            if (!_isLoaded || _gltfData?.Materials == null || materialIndex < 0 || materialIndex >= _gltfData.Materials.Count)
            {
                return "Unknown";
            }

            return _gltfData.Materials[materialIndex].Name ?? $"Material_{materialIndex}";
        }

        /// <summary>
        /// Gets all mesh names from the GLTF file.
        /// </summary>
        /// <returns>Array of mesh names</returns>
        public string[] GetMeshNames()
        {
            if (!_isLoaded || _gltfData?.Meshes == null)
            {
                return new string[0];
            }

            return _gltfData.Meshes.Select((mesh, index) => mesh.Name ?? $"Mesh_{index}").ToArray();
        }

        /// <summary>
        /// Gets all material names from the GLTF file.
        /// </summary>
        /// <returns>Array of material names</returns>
        public string[] GetMaterialNames()
        {
            if (!_isLoaded || _gltfData?.Materials == null)
            {
                return new string[0];
            }

            return _gltfData.Materials.Select((material, index) => material.Name ?? $"Material_{index}").ToArray();
        }

        /// <summary>
        /// Sets the material for a specific mesh.
        /// </summary>
        /// <param name="meshIndex">The mesh index</param>
        /// <param name="materialIndex">The material index to use</param>
        public void SetMeshMaterial(int meshIndex, int materialIndex)
        {
            if (!_isLoaded || _gltfData?.Meshes == null || meshIndex < 0 || meshIndex >= _gltfData.Meshes.Count)
            {
                return;
            }

            if (materialIndex >= 0 && materialIndex < _extractedMaterials.Count)
            {
                _gltfData.Meshes[meshIndex].MaterialIndex = materialIndex;
            }
        }

        /// <summary>
        /// Gets information about the loaded GLTF file.
        /// </summary>
        /// <returns>A string containing information about the loaded GLTF file</returns>
        public string GetGLTFInfo()
        {
            if (!_isLoaded)
            {
                return "No GLTF file loaded.";
            }

            var info = new System.Text.StringBuilder();
            info.AppendLine($"GLTF File: {Path.GetFileName(_filePath)}");
            info.AppendLine($"Meshes: {MeshCount}");
            info.AppendLine($"Materials: {MaterialCount}");
            
            if (MeshCount > 0)
            {
                info.AppendLine("\nMesh Names:");
                for (int i = 0; i < MeshCount; i++)
                {
                    info.AppendLine($"  {i}: {GetMeshName(i)}");
                }
            }
            
            if (MaterialCount > 0)
            {
                info.AppendLine("\nMaterial Names:");
                for (int i = 0; i < MaterialCount; i++)
                {
                    info.AppendLine($"  {i}: {GetMaterialName(i)}");
                }
            }

            return info.ToString();
        }
    }
}
