namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// How a mesh is drawn. This is a property of the **view**, not the mesh: several views can
    /// share one scene and show it differently (a Hammer-style quad viewport does exactly that),
    /// so it is passed per draw rather than stored on the mesh or the scene.
    /// </summary>
    public enum MeshDrawMode
    {
        /// <summary> Lit, with the material's texture. </summary>
        Textured,

        /// <summary> Lit, but flat: the material's colour with no texture. </summary>
        Solid,

        /// <summary> Triangle edges only. Shading is dropped so the lines stay readable. </summary>
        Wireframe
    }
}
