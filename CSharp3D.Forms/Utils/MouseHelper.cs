using System.Runtime.InteropServices;

namespace CSharp3D.Forms.Utils
{
    internal static class MouseHelper
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        /// <summary>
        /// Check if the left mouse button is down
        /// </summary>
        /// <returns> True if the left mouse button is down, otherwise false </returns>
        internal static bool IsLeftMouseButtonDown()
        {
            const int VK_LBUTTON = 0x01;
            short state = GetAsyncKeyState(VK_LBUTTON);
            return (state & 0x8000) != 0;
        }
    }
}
