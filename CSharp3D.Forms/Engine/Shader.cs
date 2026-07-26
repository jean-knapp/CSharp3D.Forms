using CSharp3D.Forms.Controls;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.IO;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// Represents a shader program.
    /// </summary>
    public class Shader
    {
        /// <summary>
        /// The shader program.
        /// </summary>
        private Dictionary<object, int> shaderProgram = new Dictionary<object, int>();

        /// <summary>
        /// Cached uniform locations, keyed by context, then program id, then name.
        ///
        /// <c>glGetUniformLocation</c> is a driver-side string lookup, not a local table
        /// read, and the draw path asks for two dozen of them per mesh. On a scene with
        /// tens of thousands of face meshes that is millions of string lookups per second
        /// and it dominates the frame — so they are resolved once per program here.
        ///
        /// The context has to be part of the key: program ids are per context, so two
        /// different shaders living in two different GL contexts can both be program 3,
        /// and a single id-keyed cache would hand one shader's locations to the other.
        /// </summary>
        private static readonly Dictionary<object, Dictionary<int, Dictionary<string, int>>> _uniformLocations =
            new Dictionary<object, Dictionary<int, Dictionary<string, int>>>();

        /// <summary>
        /// <c>GL.GetUniformLocation</c> with the result cached. Must be called on the GL
        /// thread owning <paramref name="context"/>.
        /// </summary>
        public static int GetUniformLocation(object context, int shaderProgramId, string name)
        {
            Dictionary<int, Dictionary<string, int>> byProgram;

            if (!_uniformLocations.TryGetValue(context, out byProgram))
            {
                byProgram = new Dictionary<int, Dictionary<string, int>>();
                _uniformLocations[context] = byProgram;
            }

            Dictionary<string, int> byName;

            if (!byProgram.TryGetValue(shaderProgramId, out byName))
            {
                byName = new Dictionary<string, int>();
                byProgram[shaderProgramId] = byName;
            }

            int location;
            if (!byName.TryGetValue(name, out location))
            {
                location = GL.GetUniformLocation(shaderProgramId, name);
                byName[name] = location;
            }

            return location;
        }

        /// <summary>Drop cached locations for a context that is going away.</summary>
        public static void ForgetUniformLocations(object context)
        {
            _uniformLocations.Remove(context);
        }

        /// <summary>
        /// The name of the shader.
        /// </summary>
        private string Name { get; set; } = "UnlitTexture";

        public Shader(string name)
        {
            this.Name = name;
        }

        /// <summary>
        /// Checks if the shader data is loaded.
        /// </summary>
        /// <param name="context"> The context of the RendererControl. </param>
        /// <returns> True if the shader data is loaded, otherwise false. </returns>
        public bool IsShaderDataLoaded(object context)
        {
            return shaderProgram.ContainsKey(context);
        }

        /// <summary>
        /// Gets the shader program id in the context of the RendererControl.
        /// </summary>
        /// <param name="context"> The context of the RendererControl. </param>
        /// <param name="scene"> The scene. </param>
        /// <returns> The shader program id. </returns>
        public int GetShaderId(object context, Scene scene)
        {
            if (!IsShaderDataLoaded(context))
            {
                LoadShader(context, scene);
            }

            return shaderProgram[context];
        }

        /// <summary>
        /// Loads the shader in the context of the RendererControl.
        /// </summary>
        /// <param name="context"> The context of the RendererControl. </param>
        /// <param name="scene"> The scene. </param>
        /// <exception cref="FileNotFoundException"></exception>
        private void LoadShader(object context, Scene scene)
        {
            string vertexShaderPath = Path.Combine(new string[] { AppDomain.CurrentDomain.BaseDirectory, scene.ShaderDirectory, Name + "/vert.glsl" });
            string geometryShaderPath = Path.Combine(new string[] { AppDomain.CurrentDomain.BaseDirectory, scene.ShaderDirectory, Name + "/geom.glsl" });
            string fragmentShaderPath = Path.Combine(new string[] { AppDomain.CurrentDomain.BaseDirectory, scene.ShaderDirectory, Name + "/frag.glsl" });

            if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, vertexShaderPath)))
                throw new FileNotFoundException($"Shader file not found: {vertexShaderPath}");

            if (!File.Exists(fragmentShaderPath))
                throw new FileNotFoundException($"Shader file not found: {fragmentShaderPath}");

            // Load shader source from files
            string vertexShaderSource = File.ReadAllText(vertexShaderPath);
            string fragmentShaderSource = File.ReadAllText(fragmentShaderPath);

            // Compiling vertex shader
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            SetShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);
            CheckShaderCompileErrors(vertexShader, "VERTEX");

            int geometryShader = -1;
            if (File.Exists(geometryShaderPath))
            {
                string geometryShaderSource = File.ReadAllText(geometryShaderPath);

                // Compiling geometry shader
                geometryShader = GL.CreateShader(ShaderType.GeometryShader);
                SetShaderSource(geometryShader, geometryShaderSource);
                GL.CompileShader(geometryShader);
                CheckShaderCompileErrors(geometryShader, "GEOMETRY");
            }

            // Compiling fragment shader
            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            SetShaderSource(fragmentShader, fragmentShaderSource);
            GL.CompileShader(fragmentShader);
            CheckShaderCompileErrors(fragmentShader, "FRAGMENT");

            // Linking shaders into a shader program
            shaderProgram[context] = GL.CreateProgram();
            GL.AttachShader(shaderProgram[context], vertexShader);

            if (geometryShader != -1)
                GL.AttachShader(shaderProgram[context], geometryShader);

            GL.AttachShader(shaderProgram[context], fragmentShader);
            GL.LinkProgram(shaderProgram[context]);
            CheckShaderCompileErrors(shaderProgram[context], "PROGRAM");

            // Deleting shaders as they're now linked into the program and no longer necessary
            GL.DeleteShader(vertexShader);

            if (geometryShader != -1)
                GL.DeleteShader(geometryShader);

            GL.DeleteShader(fragmentShader);
        }

        /// <summary>
        /// Hands a shader source string to GL as a NUL-terminated string.
        ///
        /// NOT the same as <c>GL.ShaderSource(shader, source)</c>: that convenience
        /// overload passes <c>source.Length</c> — a CHARACTER count — as the byte length
        /// of a buffer the marshaller encodes as UTF-8. Any non-ASCII character (a "²"
        /// or an em dash in a comment is enough) makes the byte count exceed the
        /// character count, so the driver silently drops that many bytes off the END of
        /// the shader, cutting off closing braces and failing with the baffling
        /// "pre-mature EOF" syntax error.
        ///
        /// Passing a null length array instead tells GL each string is NUL-terminated,
        /// so there is no count to get wrong and shaders may contain any UTF-8 text.
        /// </summary>
        private static void SetShaderSource(int shader, string source)
        {
            GL.ShaderSource(shader, 1, new[] { source }, (int[])null);
        }

        /// <summary>
        /// Disposes the shader program.
        /// </summary>
        /// <param name="context"> The context of the RendererControl. </param>
        public void Dispose(object context)
        {
            if (shaderProgram.ContainsKey(context))
                GL.DeleteProgram(shaderProgram[context]);
        }

        /// <summary>
        /// Checks for shader compile errors.
        /// </summary>
        /// <param name="shader"> The shader id. </param>
        /// <param name="type"> The type of the shader. </param>
        /// <exception cref="Exception"></exception>
        private void CheckShaderCompileErrors(int shader, string type)
        {
            if (type != "PROGRAM")
            {
                GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
                if (success == 0)
                {
                    string infoLog = GL.GetShaderInfoLog(shader);
                    // Handle the error. For example, you can throw an exception:
                    throw new Exception($"Shader compilation error ({type}): {infoLog}");
                }
            }
            else
            {
                GL.GetProgram(shader, GetProgramParameterName.LinkStatus, out int success);
                if (success == 0)
                {
                    string infoLog = GL.GetProgramInfoLog(shader);
                    // Handle the error. For example, you can throw an exception:
                    throw new Exception($"Program linking error: {infoLog}");
                }
            }
        }

        /// <summary>
        /// Use the shader program.
        /// </summary>
        /// <param name="context"> The context of the RendererControl. </param>
        /// <param name="scene"> The scene. </param>
        public void Use(object context, Scene scene)
        {
            GL.UseProgram(GetShaderId(context, scene));
        }
    }
}
