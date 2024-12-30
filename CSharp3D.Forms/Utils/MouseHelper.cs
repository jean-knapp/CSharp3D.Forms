using System.Drawing;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CSharp3D.Forms.Utils
{
    internal static class MouseHelper
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        internal static class Cursors
        {
            private static Cursor _cross = null;
            internal static Cursor Cross
            {
                get
                {
                    if (_cross == null)
                    {
                        using (var cursorStream = new MemoryStream(Properties.Resources.cross))
                        {
                            _cross = new Cursor(cursorStream);
                        }
                    }

                    return _cross;
                }
            }

            private static Cursor _none = null;

            internal static Cursor None
            {
                get
                {
                    if (_none == null)
                    {
                        Bitmap bitmap = new Bitmap(1, 1); // 1x1 transparent bitmap
                        bitmap.SetPixel(0, 0, Color.Transparent); // Ensure it's fully transparent
                        IntPtr ptr = bitmap.GetHicon(); // Create an icon handle
                        _none = new Cursor(ptr); // Convert to a cursor
                    }

                    return _none;
                }
            }
        }

        /// <summary>
        /// Check if the left mouse button is down
        /// </summary>
        /// <returns>True if the left mouse button is down, otherwise false</returns>
        internal static bool IsLeftMouseButtonDown()
        {
            const int VK_LBUTTON = 0x01;
            short state = GetAsyncKeyState(VK_LBUTTON);
            return (state & 0x8000) != 0;
        }

        /// <summary>
        /// Check if the middle mouse button is down
        /// </summary>
        /// <returns>True if the middle mouse button is down, otherwise false</returns>
        internal static bool IsMiddleMouseButtonDown()
        {
            const int VK_MBUTTON = 0x04;
            short state = GetAsyncKeyState(VK_MBUTTON);
            return (state & 0x8000) != 0;
        }

        /// <summary>
        /// Check if the right mouse button is down
        /// </summary>
        /// <returns>True if the right mouse button is down, otherwise false</returns>
        internal static bool IsRightMouseButtonDown()
        {
            const int VK_RBUTTON = 0x02;
            short state = GetAsyncKeyState(VK_RBUTTON);
            return (state & 0x8000) != 0;
        }
    }
}
