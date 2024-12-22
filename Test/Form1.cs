using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Lights;
using CSharp3D.Forms.Meshes;
using OpenTK;
using System.Drawing;
using System.Windows.Forms;

namespace Test
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            CuboidMesh lightBulb = new CuboidMesh(new Vector3(0, 0, 4), new Vector3(0, 0, 0));
            lightBulb.Material = new Material();
            lightBulb.Material.ShaderName = "Generic";
            lightBulb.Material.Unlit = true;
            lightBulb.ScaleX = 0.1f;
            lightBulb.ScaleY = 0.1f;
            lightBulb.ScaleZ = 0.1f;
            scene.Meshes.Add(lightBulb);
        }
    }
}
