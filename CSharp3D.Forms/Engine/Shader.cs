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
            GL.ShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);
            CheckShaderCompileErrors(vertexShader, "VERTEX");

            int geometryShader = -1;
            if (File.Exists(geometryShaderPath))
            {
                string geometryShaderSource = File.ReadAllText(geometryShaderPath);

                // Compiling geometry shader
                geometryShader = GL.CreateShader(ShaderType.GeometryShader);
                GL.ShaderSource(geometryShader, geometryShaderSource);
                GL.CompileShader(geometryShader);
                CheckShaderCompileErrors(geometryShader, "GEOMETRY");
            }

            // Compiling fragment shader
            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
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
