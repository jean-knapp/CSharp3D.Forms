using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Drawing;
using CSharp3D.Forms.Engine;
using System.Text.Json;
using System.Linq;
using OpenTK;

namespace CSharp3D.Forms.Utils
{
    /// <summary>
    /// A GLTF file parser that can extract mesh data and materials.
    /// Much simpler than GLB parsing since it's JSON-based.
    /// </summary>
    public class GLTFParser
    {
        /// <summary>
        /// Parsed GLTF data structure.
        /// </summary>
        public class GLTFData
        {
            public List<GLTFMesh> Meshes { get; set; } = new List<GLTFMesh>();
            public List<GLTFMaterial> Materials { get; set; } = new List<GLTFMaterial>();
            public List<GLTFTexture> Textures { get; set; } = new List<GLTFTexture>();
            public List<GLTFImage> Images { get; set; } = new List<GLTFImage>();
            public List<GLTFAccessor> Accessors { get; set; } = new List<GLTFAccessor>();
            public List<GLTFBufferView> BufferViews { get; set; } = new List<GLTFBufferView>();
            public List<GLTFBuffer> Buffers { get; set; } = new List<GLTFBuffer>();
            public string BasePath { get; set; }
        }

        /// <summary>
        /// GLTF mesh data.
        /// </summary>
        public class GLTFMesh
        {
            public string Name { get; set; }
            public List<Vertex> Vertices { get; set; } = new List<Vertex>();
            public List<uint> Indices { get; set; } = new List<uint>();
            public int MaterialIndex { get; set; } = -1;
        }

        /// <summary>
        /// GLTF material data.
        /// </summary>
        public class GLTFMaterial
        {
            public string Name { get; set; }
            public Color BaseColor { get; set; } = Color.White;
            public float Metallic { get; set; } = 0.0f;
            public float Roughness { get; set; } = 1.0f;
            public int BaseColorTextureIndex { get; set; } = -1;
            public int NormalTextureIndex { get; set; } = -1;
            public int MetallicRoughnessTextureIndex { get; set; } = -1;
        }

        /// <summary>
        /// GLTF image data.
        /// </summary>
        public class GLTFImage
        {
            public string Name { get; set; }
            public string Uri { get; set; }
            public string FilePath { get; set; }
            public byte[] Data { get; set; }
            public int MimeType { get; set; } = -1; // 0 = image/jpeg, 1 = image/png
        }

        /// <summary>
        /// GLTF texture data.
        /// </summary>
        public class GLTFTexture
        {
            public string Name { get; set; }
            public string ImagePath { get; set; }
            public int SourceIndex { get; set; }
            public GLTFImage Image { get; set; }
        }

        /// <summary>
        /// GLTF accessor data.
        /// </summary>
        public class GLTFAccessor
        {
            public int BufferView { get; set; }
            public int ByteOffset { get; set; }
            public string Type { get; set; }
            public int ComponentType { get; set; }
            public int Count { get; set; }
        }

        /// <summary>
        /// GLTF buffer view data.
        /// </summary>
        public class GLTFBufferView
        {
            public int Buffer { get; set; }
            public int ByteOffset { get; set; }
            public int ByteLength { get; set; }
        }

        /// <summary>
        /// GLTF buffer data.
        /// </summary>
        public class GLTFBuffer
        {
            public string Uri { get; set; }
            public int ByteLength { get; set; }
            public byte[] Data { get; set; }
        }

        /// <summary>
        /// Parses a GLTF file and extracts mesh and material data.
        /// </summary>
        /// <param name="filePath">Path to the GLTF file</param>
        /// <returns>Parsed GLTF data</returns>
        public static GLTFData ParseGLTF(string filePath)
        {
            var data = new GLTFData();
            data.BasePath = Path.GetDirectoryName(filePath);

            try
            {
                Console.WriteLine($"Loading GLTF file: {filePath}");
                var jsonContent = File.ReadAllText(filePath);
                using (var document = JsonDocument.Parse(jsonContent))
                {
                    var root = document.RootElement;

                    // Parse buffers first
                    if (root.TryGetProperty("buffers", out var buffersArray))
                    {
                        Console.WriteLine($"Found {buffersArray.GetArrayLength()} buffers");
                        foreach (var bufferElement in buffersArray.EnumerateArray())
                        {
                            var buffer = ParseBuffer(bufferElement, data.BasePath);
                            data.Buffers.Add(buffer);
                        }
                    }

                    // Parse buffer views
                    if (root.TryGetProperty("bufferViews", out var bufferViewsArray))
                    {
                        Console.WriteLine($"Found {bufferViewsArray.GetArrayLength()} buffer views");
                        foreach (var bufferViewElement in bufferViewsArray.EnumerateArray())
                        {
                            var bufferView = ParseBufferView(bufferViewElement);
                            data.BufferViews.Add(bufferView);
                        }
                    }

                    // Parse accessors
                    if (root.TryGetProperty("accessors", out var accessorsArray))
                    {
                        Console.WriteLine($"Found {accessorsArray.GetArrayLength()} accessors");
                        foreach (var accessorElement in accessorsArray.EnumerateArray())
                        {
                            var accessor = ParseAccessor(accessorElement);
                            data.Accessors.Add(accessor);
                        }
                    }

                    // Parse materials
                    if (root.TryGetProperty("materials", out var materialsArray))
                    {
                        Console.WriteLine($"Found {materialsArray.GetArrayLength()} materials");
                        foreach (var materialElement in materialsArray.EnumerateArray())
                        {
                            var material = ParseMaterial(materialElement);
                            data.Materials.Add(material);
                        }
                    }

                    // Parse images
                    if (root.TryGetProperty("images", out var imagesArray))
                    {
                        Console.WriteLine($"Found {imagesArray.GetArrayLength()} images");
                        foreach (var imageElement in imagesArray.EnumerateArray())
                        {
                            var image = ParseImage(imageElement, data);
                            data.Images.Add(image);
                        }
                    }

                    // Parse textures
                    if (root.TryGetProperty("textures", out var texturesArray))
                    {
                        Console.WriteLine($"Found {texturesArray.GetArrayLength()} textures");
                        foreach (var textureElement in texturesArray.EnumerateArray())
                        {
                            var texture = ParseTexture(textureElement, data);
                            data.Textures.Add(texture);
                        }
                    }

                    // Parse meshes
                    if (root.TryGetProperty("meshes", out var meshesArray))
                    {
                        Console.WriteLine($"Found {meshesArray.GetArrayLength()} meshes");
                        foreach (var meshElement in meshesArray.EnumerateArray())
                        {
                            var mesh = ParseMesh(meshElement, data);
                            data.Meshes.Add(mesh);
                        }
                    }

                    // Log GLTF structure for debugging
                    LogGLTFStructure(data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing GLTF file: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                // Create a fallback cube if parsing fails
                CreateFallbackMesh(data);
            }

            return data;
        }

        /// <summary>
        /// Parses a buffer from JSON with flexible binary file name handling.
        /// </summary>
        private static GLTFBuffer ParseBuffer(JsonElement bufferElement, string basePath)
        {
            var buffer = new GLTFBuffer();

            if (bufferElement.TryGetProperty("uri", out var uriElement))
            {
                var uri = uriElement.GetString();
                if (uri.StartsWith("data:"))
                {
                    // Handle data URI (base64 encoded data)
                    var dataUri = uri.Substring(uri.IndexOf(',') + 1);
                    buffer.Data = Convert.FromBase64String(dataUri);
                    Console.WriteLine($"Loaded embedded binary data: {buffer.Data.Length} bytes");
                }
                else
                {
                    // Handle external file with flexible name matching
                    var filePath = Path.Combine(basePath, uri);
                    Console.WriteLine($"Looking for binary file: {filePath}");
                    
                    if (File.Exists(filePath))
                    {
                        buffer.Data = File.ReadAllBytes(filePath);
                        Console.WriteLine($"Loaded binary file: {filePath} ({buffer.Data.Length} bytes)");
                    }
                    else
                    {
                        Console.WriteLine($"Could not load binary file: {filePath}");
                    }
                }
                buffer.Uri = uri;
            }

            if (bufferElement.TryGetProperty("byteLength", out var byteLengthElement))
            {
                buffer.ByteLength = byteLengthElement.GetInt32();
            }

            return buffer;
        }

        /// <summary>
        /// Parses a buffer view from JSON.
        /// </summary>
        private static GLTFBufferView ParseBufferView(JsonElement bufferViewElement)
        {
            var bufferView = new GLTFBufferView();

            if (bufferViewElement.TryGetProperty("buffer", out var bufferElement))
            {
                bufferView.Buffer = bufferElement.GetInt32();
            }

            if (bufferViewElement.TryGetProperty("byteOffset", out var byteOffsetElement))
            {
                bufferView.ByteOffset = byteOffsetElement.GetInt32();
            }

            if (bufferViewElement.TryGetProperty("byteLength", out var byteLengthElement))
            {
                bufferView.ByteLength = byteLengthElement.GetInt32();
            }

            return bufferView;
        }

        /// <summary>
        /// Parses an accessor from JSON.
        /// </summary>
        private static GLTFAccessor ParseAccessor(JsonElement accessorElement)
        {
            var accessor = new GLTFAccessor();

            if (accessorElement.TryGetProperty("bufferView", out var bufferViewElement))
            {
                accessor.BufferView = bufferViewElement.GetInt32();
            }

            if (accessorElement.TryGetProperty("byteOffset", out var byteOffsetElement))
            {
                accessor.ByteOffset = byteOffsetElement.GetInt32();
            }

            if (accessorElement.TryGetProperty("type", out var typeElement))
            {
                accessor.Type = typeElement.GetString();
            }

            if (accessorElement.TryGetProperty("componentType", out var componentTypeElement))
            {
                accessor.ComponentType = componentTypeElement.GetInt32();
            }

            if (accessorElement.TryGetProperty("count", out var countElement))
            {
                accessor.Count = countElement.GetInt32();
            }

            return accessor;
        }

        /// <summary>
        /// Parses a material from JSON.
        /// </summary>
        private static GLTFMaterial ParseMaterial(JsonElement materialElement)
        {
            var material = new GLTFMaterial();

            if (materialElement.TryGetProperty("name", out var nameElement))
            {
                material.Name = nameElement.GetString();
            }

            if (materialElement.TryGetProperty("pbrMetallicRoughness", out var pbrElement))
            {
                if (pbrElement.TryGetProperty("baseColorFactor", out var colorArray) && colorArray.GetArrayLength() >= 4)
                {
                    var r = (int)(colorArray[0].GetSingle() * 255);
                    var g = (int)(colorArray[1].GetSingle() * 255);
                    var b = (int)(colorArray[2].GetSingle() * 255);
                    var a = (int)(colorArray[3].GetSingle() * 255);
                    material.BaseColor = Color.FromArgb(a, r, g, b);
                }

                if (pbrElement.TryGetProperty("metallicFactor", out var metallicElement))
                {
                    material.Metallic = metallicElement.GetSingle();
                }

                if (pbrElement.TryGetProperty("roughnessFactor", out var roughnessElement))
                {
                    material.Roughness = roughnessElement.GetSingle();
                }

                if (pbrElement.TryGetProperty("baseColorTexture", out var baseColorTextureElement))
                {
                    if (baseColorTextureElement.TryGetProperty("index", out var indexElement))
                    {
                        material.BaseColorTextureIndex = indexElement.GetInt32();
                    }
                }

                if (pbrElement.TryGetProperty("metallicRoughnessTexture", out var metallicRoughnessTextureElement))
                {
                    if (metallicRoughnessTextureElement.TryGetProperty("index", out var indexElement))
                    {
                        material.MetallicRoughnessTextureIndex = indexElement.GetInt32();
                    }
                }
            }

            if (materialElement.TryGetProperty("normalTexture", out var normalTextureElement))
            {
                if (normalTextureElement.TryGetProperty("index", out var indexElement))
                {
                    material.NormalTextureIndex = indexElement.GetInt32();
                }
            }

            return material;
        }

        /// <summary>
        /// Parses an image from JSON.
        /// </summary>
        private static GLTFImage ParseImage(JsonElement imageElement, GLTFData data)
        {
            var image = new GLTFImage();

            if (imageElement.TryGetProperty("name", out var nameElement))
            {
                image.Name = nameElement.GetString();
            }

            if (imageElement.TryGetProperty("uri", out var uriElement))
            {
                var uri = uriElement.GetString();
                image.Uri = uri;

                if (uri.StartsWith("data:"))
                {
                    // Handle data URI (base64 encoded data)
                    var dataUri = uri.Substring(uri.IndexOf(',') + 1);
                    image.Data = Convert.FromBase64String(dataUri);
                    Console.WriteLine($"Loaded embedded image data: {image.Data.Length} bytes");
                }
                else
                {
                    // Handle external file
                    image.FilePath = Path.Combine(data.BasePath, uri);
                    Console.WriteLine($"Looking for image file: {image.FilePath}");
                    
                    if (File.Exists(image.FilePath))
                    {
                        image.Data = File.ReadAllBytes(image.FilePath);
                        Console.WriteLine($"Loaded image file: {image.FilePath} ({image.Data.Length} bytes)");
                    }
                    else
                    {
                        Console.WriteLine($"Image file not found: {image.FilePath}");
                    }
                }
            }
            else if (imageElement.TryGetProperty("bufferView", out var bufferViewElement))
            {
                // Handle image data stored in buffer
                var bufferViewIndex = bufferViewElement.GetInt32();
                if (bufferViewIndex >= 0 && bufferViewIndex < data.BufferViews.Count)
                {
                    var bufferView = data.BufferViews[bufferViewIndex];
                    var buffer = data.Buffers[bufferView.Buffer];
                    
                    if (buffer.Data != null)
                    {
                        var offset = bufferView.ByteOffset;
                        var length = bufferView.ByteLength;
                        image.Data = new byte[length];
                        Array.Copy(buffer.Data, offset, image.Data, 0, length);
                        Console.WriteLine($"Loaded image from buffer: {image.Data.Length} bytes");
                    }
                }
            }

            if (imageElement.TryGetProperty("mimeType", out var mimeTypeElement))
            {
                var mimeType = mimeTypeElement.GetString();
                if (mimeType == "image/jpeg")
                    image.MimeType = 0;
                else if (mimeType == "image/png")
                    image.MimeType = 1;
            }

            return image;
        }

        /// <summary>
        /// Parses a texture from JSON.
        /// </summary>
        private static GLTFTexture ParseTexture(JsonElement textureElement, GLTFData data)
        {
            var texture = new GLTFTexture();

            if (textureElement.TryGetProperty("name", out var nameElement))
            {
                texture.Name = nameElement.GetString();
            }

            if (textureElement.TryGetProperty("source", out var sourceElement))
            {
                texture.SourceIndex = sourceElement.GetInt32();
                
                // Link to the corresponding image
                if (texture.SourceIndex >= 0 && texture.SourceIndex < data.Images.Count)
                {
                    texture.Image = data.Images[texture.SourceIndex];
                    texture.ImagePath = texture.Image.FilePath;
                }
            }

            return texture;
        }

        /// <summary>
        /// Parses a mesh from JSON and extracts vertex/index data.
        /// </summary>
        private static GLTFMesh ParseMesh(JsonElement meshElement, GLTFData data)
        {
            var mesh = new GLTFMesh();

            if (meshElement.TryGetProperty("name", out var nameElement))
            {
                mesh.Name = nameElement.GetString();
            }

            if (meshElement.TryGetProperty("primitives", out var primitivesArray))
            {
                // Process the first primitive
                var primitive = primitivesArray[0];

                if (primitive.TryGetProperty("material", out var materialElement))
                {
                    mesh.MaterialIndex = materialElement.GetInt32();
                }

                // Extract vertex data
                if (primitive.TryGetProperty("attributes", out var attributesElement))
                {
                    ExtractVertexData(attributesElement, data, mesh);
                }

                // Extract index data
                if (primitive.TryGetProperty("indices", out var indicesElement))
                {
                    ExtractIndexData(indicesElement, data, mesh);
                }
            }

            Console.WriteLine($"Parsed mesh '{mesh.Name}': {mesh.Vertices.Count} vertices, {mesh.Indices.Count} indices");
            return mesh;
        }

        /// <summary>
        /// Extracts vertex data from accessors.
        /// </summary>
        private static void ExtractVertexData(JsonElement attributesElement, GLTFData data, GLTFMesh mesh)
        {
            var positions = new List<Vector3>();
            var texCoords = new List<Vector2>();

            // Extract positions
            if (attributesElement.TryGetProperty("POSITION", out var positionElement))
            {
                var positionAccessorIndex = positionElement.GetInt32();
                positions = ExtractPositions(data, positionAccessorIndex);
                Console.WriteLine($"Extracted {positions.Count} positions");
            }

            // Extract texture coordinates
            if (attributesElement.TryGetProperty("TEXCOORD_0", out var texCoordElement))
            {
                var texCoordAccessorIndex = texCoordElement.GetInt32();
                texCoords = ExtractTexCoords(data, texCoordAccessorIndex);
                Console.WriteLine($"Extracted {texCoords.Count} texture coordinates");
            }

            // Create vertices
            for (int i = 0; i < positions.Count; i++)
            {
                var texCoord = i < texCoords.Count ? texCoords[i] : new Vector2(0, 0);
                mesh.Vertices.Add(new Vertex(positions[i], texCoord));
            }
        }

        /// <summary>
        /// Extracts index data from accessor.
        /// </summary>
        private static void ExtractIndexData(JsonElement indicesElement, GLTFData data, GLTFMesh mesh)
        {
            var indicesAccessorIndex = indicesElement.GetInt32();
            mesh.Indices = ExtractIndices(data, indicesAccessorIndex);
            Console.WriteLine($"Extracted {mesh.Indices.Count} indices");
        }

        /// <summary>
        /// Extracts position data from accessor.
        /// </summary>
        private static List<Vector3> ExtractPositions(GLTFData data, int accessorIndex)
        {
            var positions = new List<Vector3>();

            if (accessorIndex >= 0 && accessorIndex < data.Accessors.Count)
            {
                var accessor = data.Accessors[accessorIndex];
                var bufferView = data.BufferViews[accessor.BufferView];
                var buffer = data.Buffers[bufferView.Buffer];

                if (buffer.Data == null)
                {
                    Console.WriteLine($"Buffer data is null for accessor {accessorIndex}");
                    return positions;
                }

                var offset = bufferView.ByteOffset + accessor.ByteOffset;
                
                // Calculate stride based on component type
                int componentSize = 4; // Default for FLOAT
                if (accessor.ComponentType == 5120) // BYTE
                    componentSize = 1;
                else if (accessor.ComponentType == 5121) // UNSIGNED_BYTE
                    componentSize = 1;
                else if (accessor.ComponentType == 5122) // SHORT
                    componentSize = 2;
                else if (accessor.ComponentType == 5123) // UNSIGNED_SHORT
                    componentSize = 2;
                else if (accessor.ComponentType == 5125) // UNSIGNED_INT
                    componentSize = 4;
                else if (accessor.ComponentType == 5126) // FLOAT
                    componentSize = 4;
                
                int stride = 3 * componentSize; // 3 components per position
                
                // Check if bufferView has a stride (for interleaved data)
                if (bufferView.ByteLength > 0 && bufferView.ByteLength < accessor.Count * stride)
                {
                    // Use bufferView stride if available
                    stride = bufferView.ByteLength / accessor.Count;
                }

                Console.WriteLine($"Extracting positions: offset={offset}, count={accessor.Count}, stride={stride}, componentType={accessor.ComponentType}");

                for (int i = 0; i < accessor.Count; i++)
                {
                    var pos = new Vector3();
                    
                    if (accessor.ComponentType == 5126) // FLOAT
                    {
                        pos.X = BitConverter.ToSingle(buffer.Data, offset + i * stride);
                        pos.Y = BitConverter.ToSingle(buffer.Data, offset + i * stride + 4);
                        pos.Z = BitConverter.ToSingle(buffer.Data, offset + i * stride + 8);
                    }
                    else
                    {
                        // For non-float types, we need to normalize the values
                        Console.WriteLine($"Warning: Non-float position data not fully supported. Component type: {accessor.ComponentType}");
                        pos.X = BitConverter.ToSingle(buffer.Data, offset + i * stride);
                        pos.Y = BitConverter.ToSingle(buffer.Data, offset + i * stride + 4);
                        pos.Z = BitConverter.ToSingle(buffer.Data, offset + i * stride + 8);
                    }
                    
                    positions.Add(pos);
                }
            }

            return positions;
        }

        /// <summary>
        /// Extracts texture coordinate data from accessor.
        /// </summary>
        private static List<Vector2> ExtractTexCoords(GLTFData data, int accessorIndex)
        {
            var texCoords = new List<Vector2>();

            if (accessorIndex >= 0 && accessorIndex < data.Accessors.Count)
            {
                var accessor = data.Accessors[accessorIndex];
                var bufferView = data.BufferViews[accessor.BufferView];
                var buffer = data.Buffers[bufferView.Buffer];

                if (buffer.Data == null)
                {
                    Console.WriteLine($"Buffer data is null for texture coordinate accessor {accessorIndex}");
                    return texCoords;
                }

                var offset = bufferView.ByteOffset + accessor.ByteOffset;
                
                // Calculate stride based on component type
                int componentSize = 4; // Default for FLOAT
                if (accessor.ComponentType == 5120) // BYTE
                    componentSize = 1;
                else if (accessor.ComponentType == 5121) // UNSIGNED_BYTE
                    componentSize = 1;
                else if (accessor.ComponentType == 5122) // SHORT
                    componentSize = 2;
                else if (accessor.ComponentType == 5123) // UNSIGNED_SHORT
                    componentSize = 2;
                else if (accessor.ComponentType == 5125) // UNSIGNED_INT
                    componentSize = 4;
                else if (accessor.ComponentType == 5126) // FLOAT
                    componentSize = 4;
                
                int stride = 2 * componentSize; // 2 components per texture coordinate
                
                // Check if bufferView has a stride (for interleaved data)
                if (bufferView.ByteLength > 0 && bufferView.ByteLength < accessor.Count * stride)
                {
                    // Use bufferView stride if available
                    stride = bufferView.ByteLength / accessor.Count;
                }

                for (int i = 0; i < accessor.Count; i++)
                {
                    var texCoord = new Vector2();
                    
                    if (accessor.ComponentType == 5126) // FLOAT
                    {
                        texCoord.X = BitConverter.ToSingle(buffer.Data, offset + i * stride);
                        texCoord.Y = BitConverter.ToSingle(buffer.Data, offset + i * stride + 4);
                    }
                    else
                    {
                        // For non-float types, we need to normalize the values
                        Console.WriteLine($"Warning: Non-float texture coordinate data not fully supported. Component type: {accessor.ComponentType}");
                        texCoord.X = BitConverter.ToSingle(buffer.Data, offset + i * stride);
                        texCoord.Y = BitConverter.ToSingle(buffer.Data, offset + i * stride + 4);
                    }
                    
                    texCoords.Add(texCoord);
                }
            }

            return texCoords;
        }

        /// <summary>
        /// Extracts index data from accessor.
        /// </summary>
        private static List<uint> ExtractIndices(GLTFData data, int accessorIndex)
        {
            var indices = new List<uint>();

            if (accessorIndex >= 0 && accessorIndex < data.Accessors.Count)
            {
                var accessor = data.Accessors[accessorIndex];
                var bufferView = data.BufferViews[accessor.BufferView];
                var buffer = data.Buffers[bufferView.Buffer];

                if (buffer.Data == null)
                {
                    Console.WriteLine($"Buffer data is null for index accessor {accessorIndex}");
                    return indices;
                }

                var offset = bufferView.ByteOffset + accessor.ByteOffset;
                
                // Handle different component types for indices
                int stride;
                switch (accessor.ComponentType)
                {
                    case 5121: // UNSIGNED_BYTE (1 byte)
                        stride = 1;
                        for (int i = 0; i < accessor.Count; i++)
                        {
                            var index = buffer.Data[offset + i * stride];
                            indices.Add(index);
                        }
                        break;
                    case 5123: // UNSIGNED_SHORT (2 bytes)
                        stride = 2;
                        for (int i = 0; i < accessor.Count; i++)
                        {
                            var index = BitConverter.ToUInt16(buffer.Data, offset + i * stride);
                            indices.Add(index);
                        }
                        break;
                    case 5125: // UNSIGNED_INT (4 bytes)
                        stride = 4;
                        for (int i = 0; i < accessor.Count; i++)
                        {
                            var index = BitConverter.ToUInt32(buffer.Data, offset + i * stride);
                            indices.Add(index);
                        }
                        break;
                    default:
                        Console.WriteLine($"Unsupported index component type: {accessor.ComponentType}");
                        break;
                }
            }

            return indices;
        }

        /// <summary>
        /// Logs the GLTF structure for debugging purposes.
        /// </summary>
        private static void LogGLTFStructure(GLTFData data)
        {
            Console.WriteLine("=== GLTF Structure Debug Info ===");
            Console.WriteLine($"Buffers: {data.Buffers.Count}");
            for (int i = 0; i < data.Buffers.Count; i++)
            {
                var buffer = data.Buffers[i];
                Console.WriteLine($"  Buffer {i}: {buffer.ByteLength} bytes, Data: {(buffer.Data != null ? "Loaded" : "Null")}");
            }
            
            Console.WriteLine($"BufferViews: {data.BufferViews.Count}");
            for (int i = 0; i < data.BufferViews.Count; i++)
            {
                var bufferView = data.BufferViews[i];
                Console.WriteLine($"  BufferView {i}: Buffer={bufferView.Buffer}, Offset={bufferView.ByteOffset}, Length={bufferView.ByteLength}");
            }
            
            Console.WriteLine($"Accessors: {data.Accessors.Count}");
            for (int i = 0; i < data.Accessors.Count; i++)
            {
                var accessor = data.Accessors[i];
                Console.WriteLine($"  Accessor {i}: BufferView={accessor.BufferView}, Type={accessor.Type}, ComponentType={accessor.ComponentType}, Count={accessor.Count}");
            }
            
            Console.WriteLine($"Meshes: {data.Meshes.Count}");
            for (int i = 0; i < data.Meshes.Count; i++)
            {
                var mesh = data.Meshes[i];
                Console.WriteLine($"  Mesh {i}: {mesh.Name}, Vertices={mesh.Vertices.Count}, Indices={mesh.Indices.Count}, MaterialIndex={mesh.MaterialIndex}");
            }
            Console.WriteLine("=================================");
        }

        /// <summary>
        /// Creates a fallback mesh if parsing fails.
        /// </summary>
        private static void CreateFallbackMesh(GLTFData data)
        {
            Console.WriteLine("Creating fallback mesh due to parsing error");
            var mesh = new GLTFMesh
            {
                Name = "Fallback Cube",
                MaterialIndex = 0
            };

            // Create a simple cube
            var vertices = new List<Vertex>
            {
                // Front face
                new Vertex(-0.5f, -0.5f,  0.5f, 0.0f, 0.0f),
                new Vertex( 0.5f, -0.5f,  0.5f, 1.0f, 0.0f),
                new Vertex( 0.5f,  0.5f,  0.5f, 1.0f, 1.0f),
                new Vertex(-0.5f,  0.5f,  0.5f, 0.0f, 1.0f),
                
                // Back face
                new Vertex(-0.5f, -0.5f, -0.5f, 1.0f, 0.0f),
                new Vertex( 0.5f, -0.5f, -0.5f, 0.0f, 0.0f),
                new Vertex( 0.5f,  0.5f, -0.5f, 0.0f, 1.0f),
                new Vertex(-0.5f,  0.5f, -0.5f, 1.0f, 1.0f),
                
                // Left face
                new Vertex(-0.5f, -0.5f, -0.5f, 0.0f, 0.0f),
                new Vertex(-0.5f, -0.5f,  0.5f, 1.0f, 0.0f),
                new Vertex(-0.5f,  0.5f,  0.5f, 1.0f, 1.0f),
                new Vertex(-0.5f,  0.5f, -0.5f, 0.0f, 1.0f),
                
                // Right face
                new Vertex( 0.5f, -0.5f, -0.5f, 1.0f, 0.0f),
                new Vertex( 0.5f, -0.5f,  0.5f, 0.0f, 0.0f),
                new Vertex( 0.5f,  0.5f,  0.5f, 0.0f, 1.0f),
                new Vertex( 0.5f,  0.5f, -0.5f, 1.0f, 1.0f),
                
                // Top face
                new Vertex(-0.5f,  0.5f, -0.5f, 0.0f, 1.0f),
                new Vertex( 0.5f,  0.5f, -0.5f, 1.0f, 1.0f),
                new Vertex( 0.5f,  0.5f,  0.5f, 1.0f, 0.0f),
                new Vertex(-0.5f,  0.5f,  0.5f, 0.0f, 0.0f),
                
                // Bottom face
                new Vertex(-0.5f, -0.5f, -0.5f, 1.0f, 1.0f),
                new Vertex( 0.5f, -0.5f, -0.5f, 0.0f, 1.0f),
                new Vertex( 0.5f, -0.5f,  0.5f, 0.0f, 0.0f),
                new Vertex(-0.5f, -0.5f,  0.5f, 1.0f, 0.0f)
            };

            var indices = new List<uint>
            {
                // Front face
                0, 1, 2, 0, 2, 3,
                // Back face
                4, 5, 6, 4, 6, 7,
                // Left face
                8, 9, 10, 8, 10, 11,
                // Right face
                12, 13, 14, 12, 14, 15,
                // Top face
                16, 17, 18, 16, 18, 19,
                // Bottom face
                20, 21, 22, 20, 22, 23
            };

            mesh.Vertices = vertices;
            mesh.Indices = indices;

            data.Meshes.Add(mesh);

            // Create a default material if none exist
            if (data.Materials.Count == 0)
            {
                data.Materials.Add(new GLTFMaterial
                {
                    Name = "Default Material",
                    BaseColor = Color.White
                });
            }
        }
    }
}
