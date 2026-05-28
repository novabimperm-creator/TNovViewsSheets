namespace TNovViewsSheets
{
    /// <summary>
    /// How exported sheets should be combined into the final DWG.
    /// </summary>
    public enum ExportMode
    {
        /// <summary>
        /// One DWG file. Each sheet on its own Layout (Paper Space tab).
        /// </summary>
        MultiLayout,

        /// <summary>
        /// One DWG file. All sheets tiled side-by-side in Model Space.
        /// </summary>
        TiledModelSpace
    }
}
