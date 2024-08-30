using System;
namespace CSharp3D.Forms.Exceptions
{
    /// <summary>
    /// Exception thrown when the camera is not set in a RendererControl
    /// </summary>
    public class CameraNotSetException : Exception
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public CameraNotSetException() : base("Camera is not set to this RendererControl.") // Provide a default error message
        {

        }

        /// <summary>
        /// Constructor with a custom message
        /// </summary>
        /// <param name="message"> Custom error message </param>
        public CameraNotSetException(string message) : base(message)
        {

        }

        /// <summary>
        /// Constructor with a custom message and an inner exception
        /// </summary>
        /// <param name="message"> Custom error message </param>
        /// <param name="innerException"> Inner exception </param>
        public CameraNotSetException(string message, Exception innerException) : base(message, innerException)
        {

        }
    }
}
