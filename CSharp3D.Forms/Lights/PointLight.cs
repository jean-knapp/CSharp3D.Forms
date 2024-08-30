using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Utils;
using OpenTK;
using System.ComponentModel;
using System.Drawing;

namespace CSharp3D.Forms.Lights
{
    /// <summary>
    /// Represents a point light in the scene.
    /// </summary>
    public class PointLight : Component, ISupportInitialize
    {
        private bool _isInitialized;

        /// <summary>
        /// The scene that the light belongs to.
        /// </summary>
        [Category("Renderer")]
        private Scene _scene;
        public Scene Scene
        {
            get => _scene;
            set
            {
                _scene = value;
                if (_isInitialized)
                {
                    InitializeComponent();
                }
            }
        }

        /// <summary>
        /// The location of the light in the scene.
        /// </summary>
        [Category("Position")]
        [TypeConverter(typeof(Vector3TypeConverter))]
        [Description("The location of the light in the scene, in World units (X, Y, z).")]
        public Vector3 Location { get; set; } = new Vector3(0, 0, 0);

        /// <summary>
        /// The color of the light.
        /// </summary>
        [Category("Color")]
        [Description("The color of the light.")]
        public Color Color { get; set; } = Color.White;

        /// <summary>
        /// The intensity of the light.
        /// </summary>
        [Category("Color")]
        [Description("The intensity of the light.")]
        public float Intensity { get; set; } = 1;

        /// <summary>
        /// The quadratic attenuation of the light.
        /// </summary>
        [Category("Attenuation")]
        [Description("The quadratic attenuation of the light.")]
        public float Quadratic { get; set; } = 1.0f;

        /// <summary>
        /// The linear attenuation of the light.
        /// </summary>
        [Category("Attenuation")]
        [Description("The linear attenuation of the light.")]
        public float Linear { get; set; } = 0.0f;

        /// <summary>
        /// The constant attenuation of the light.
        /// </summary>
        [Category("Attenuation")]
        [Description("The constant attenuation of the light.")]
        public float Constant { get; set; } = 0.0f;

        public PointLight()
        {

        }

        public PointLight(Vector3 location, Color color, float intensity)
        {
            Location = location;
            Color = color;
            Intensity = intensity;
        }

        public void BeginInit()
        {
            _isInitialized = false;
        }

        public void EndInit()
        {
            _isInitialized = true;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            _scene.Lights.Add(this);
        }
    }
}
