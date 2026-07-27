using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Utils;
using OpenTK;
using System;
using System.ComponentModel;
using System.Drawing;

namespace CSharp3D.Forms.Lights
{
    /// <summary>
    /// A light infinitely far away, casting parallel rays over the whole scene — the sun.
    /// The counterpart of Source's <c>light_environment</c>.
    ///
    /// Unlike <see cref="PointLight"/> it has no position and no attenuation: only a
    /// direction, which is why a scene holds exactly one (<see cref="Scene.Sun"/>) rather
    /// than a list. A scene with no sun renders exactly as it did before one existed.
    /// </summary>
    public class DirectionalLight : Component, ISupportInitialize
    {
        private bool _isInitialized;
        private Scene _scene;

        /// <summary>
        /// The scene this light belongs to. Assigning it makes this the scene's sun,
        /// replacing any previous one.
        /// </summary>
        [Category("Renderer")]
        public Scene Scene
        {
            get => _scene;
            set
            {
                _scene = value;
                if (_isInitialized)
                {
                    Attach();
                }
            }
        }

        /// <summary>
        /// Which way the light travels, in World units — the direction a sunbeam moves, so
        /// a sun overhead points straight down (0, 0, -1). Need not be normalized.
        /// </summary>
        [Category("Position")]
        [TypeConverter(typeof(LocationVectorTypeConverter))]
        [Description("The direction the light travels, in World units. A sun overhead is (0, 0, -1).")]
        public LocationVector Direction { get; set; } = new LocationVector(0, 0, -1);

        /// <summary>
        /// The color of the light.
        /// </summary>
        [Category("Color")]
        [Description("The color of the light.")]
        public Color Color { get; set; } = Color.White;

        /// <summary>
        /// The intensity of the light. 0 turns it off, which is the same as having no sun.
        /// </summary>
        [Category("Color")]
        [Description("The intensity of the light. 0 turns it off.")]
        public float Intensity { get; set; } = 1;

        public DirectionalLight()
        {
        }

        public DirectionalLight(LocationVector direction, Color color, float intensity)
        {
            Direction = direction;
            Color = color;
            Intensity = intensity;
        }

        /// <summary>
        /// Sets the direction from a pitch and a yaw in degrees, the way a
        /// <c>light_environment</c> is authored: pitch is negative when the sun is above
        /// the horizon, yaw is the compass direction it shines towards.
        /// </summary>
        public void SetAngles(float pitchDegrees, float yawDegrees)
        {
            double pitch = pitchDegrees * Math.PI / 180.0;
            double yaw = yawDegrees * Math.PI / 180.0;

            Direction = new LocationVector(
                (float)(Math.Cos(pitch) * Math.Cos(yaw)),
                (float)(Math.Cos(pitch) * Math.Sin(yaw)),
                (float)Math.Sin(pitch));
        }

        /// <summary>The direction normalized, in World units. Zero when the light is off.</summary>
        internal Vector3 NormalizedDirection
        {
            get
            {
                Vector3 direction = Direction != null ? Direction.ToVector3() : Vector3.Zero;
                return direction.LengthSquared > 0f ? direction.Normalized() : Vector3.Zero;
            }
        }

        public void BeginInit()
        {
            _isInitialized = false;
        }

        public void EndInit()
        {
            _isInitialized = true;
            Attach();
        }

        private void Attach()
        {
            if (_scene != null)
                _scene.Sun = this;
        }
    }
}
