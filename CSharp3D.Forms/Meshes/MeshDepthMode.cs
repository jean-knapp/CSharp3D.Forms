namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// How a mesh interacts with the depth buffer — a property of the MESH (unlike
    /// <see cref="MeshDrawMode"/>, which belongs to the view), because it is what the mesh *is*
    /// that decides it: geometry occludes, a tool guide must not be occluded.
    /// </summary>
    public enum MeshDepthMode
    {
        /// <summary> Ordinary depth-tested draw. </summary>
        Normal,

        /// <summary>
        /// Ignore the depth test: the mesh draws in front of everything already rendered, and
        /// writes no depth. For tool guides — selection boxes, handles — that must never vanish
        /// inside geometry.
        /// </summary>
        Overlay,

        /// <summary>
        /// Draw only where the mesh LOSES the depth test (is behind the scene), writing no depth.
        /// The hidden-line half of a "solid where visible, dashed where hidden" selection overlay:
        /// draw the mesh once Normal and once OccludedOnly (dashed/dimmed) to get both.
        /// </summary>
        OccludedOnly
    }

    /// <summary>
    /// Which views draw a mesh, decided by the VIEW's <see cref="MeshDrawMode"/>. A Hammer-style
    /// editor renders one shared scene in wireframe 2D panes and a textured 3D pane, but wants
    /// different geometry in each: triangulated faces belong to the 3D pane (in a wireframe pane
    /// their triangulation diagonals show), while clean face-outline line meshes belong to the 2D
    /// panes (in the 3D pane they would scribble edges over the textures).
    /// </summary>
    public enum MeshViewFilter
    {
        /// <summary> Drawn by every view. </summary>
        All,

        /// <summary> Drawn only by views whose DrawMode is Wireframe (the 2D panes). </summary>
        WireframeViewsOnly,

        /// <summary> Skipped by views whose DrawMode is Wireframe (3D-only geometry). </summary>
        ExceptWireframeViews,
    }
}
