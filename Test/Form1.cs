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

            SpriteMesh sprite = new SpriteMesh(new Vector3(0, 0, 4), new Vector3(0, 0, 0));
            sprite.Width = 1;
            sprite.Height = 1;
            sprite.Material = new Material();
            sprite.Material.ShaderName = "Generic";
            sprite.Material.Unlit = true;
            sprite.Material.Additive = true;
            sprite.Material.Albedo = new Texture();
            sprite.Material.Albedo.Bitmap = new Bitmap("Textures/light_glow02.png");
            scene.Meshes.Add(sprite);

            Texture albedo = new Texture();
            albedo.Bitmap = new Bitmap("Textures/wood_floor_deck/wood_floor_deck_diff_1k.png");

            Texture specular = new Texture();
            specular.Bitmap = new Bitmap("Textures/wood_floor_deck/wood_floor_deck_specular_1k.png");

            Texture normal = new Texture();
            normal.Bitmap = new Bitmap("Textures/wood_floor_deck/wood_floor_deck_nor_gl_1k.png");

            Texture displacement = new Texture();
            displacement.Bitmap = new Bitmap("Textures/wood_floor_deck/wood_floor_deck_disp_1k.png");

            Material material = new Material();
            material.Albedo = albedo;
            material.Specular = specular;
            material.Normal = normal;
            material.SpecularStrength = 0.5f;
            material.ShaderName = "Generic";

            CuboidMesh cuboid = new CuboidMesh(new Vector3(0, 0, 0.5f), new Vector3(0,0,0));
            cuboid.Material = material;
            cuboid.ScaleX = 8;
            cuboid.ScaleY = 8;
            cuboid.ScaleZ = 1;
            //scene.Meshes.Add(cuboid);
        }
    }
}
