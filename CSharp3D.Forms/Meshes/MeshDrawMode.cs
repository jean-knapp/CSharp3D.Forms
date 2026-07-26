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
        Wireframe,

        /// <summary>
        /// The material's texture modulated by a fixed directional shade, so faces at
        /// different angles read apart without needing any scene lights.
        ///
        /// This is Hammer's "Shaded Textured Polygons" (VIEW3D_TEXTURED_SHADED). The
        /// shade is <c>CRender3D::LightPlane</c> (render3dms.cpp:154):
        /// <c>0.65 + 0.35 * dot(N, normalize(1,2,3))</c>, i.e. a constant world-space
        /// direction and a floor of 0.30 so nothing goes black.
        /// </summary>
        TexturedShaded
    }
}
