using CSharp3D.Forms.Meshes;
using System;

namespace CSharp3D.Forms.Controls
{
    public class MeshEventArgs : EventArgs
    {
        public Mesh Mesh { get; set; } = null;

        public MeshEventArgs(Mesh mesh) : base()
        {

        }
    }
}
