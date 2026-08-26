namespace Case2
{
    /// <summary>Stable gameplay identity for a block/hole pair.</summary>
    public enum BlockShapeId
    {
        Unknown = 0,
        Single = 1,
        Two = 2,
        L = 3,
        Cross = 4,
        Square = 5
    }

    public static class BlockShapeIds
    {
        public static BlockShapeId Parse(string objectName)
        {
            string n = (objectName ?? string.Empty).ToLowerInvariant();
            if (n.Contains("cross") || n.Contains("plus")) return BlockShapeId.Cross;
            if (n.Contains("square")) return BlockShapeId.Square;
            if (n.Contains("single")) return BlockShapeId.Single;
            if (n == "2" || n.Contains("block-2") || n.EndsWith("_2") || n.EndsWith(" 2")) return BlockShapeId.Two;
            if (n == "l" || n.Contains("block-l") || n.EndsWith("_l") || n.EndsWith(" l")) return BlockShapeId.L;
            return BlockShapeId.Unknown;
        }

        public static string Key(BlockShapeId id)
        {
            return id == BlockShapeId.Two ? "2" : id.ToString();
        }

        /// <summary>
        /// World-axis occupancy of each shape, row 0 = +z (up-screen), column 0 = -x. These are not
        /// invented: they are the grids <c>Case2ShapeProbe</c> rasterised out of the authored
        /// scene's own art meshes, and they agree cell for cell with the SDFs in
        /// HoleDepthGradient.shader.
        /// <para>
        /// This lives here, and not privately inside one consumer, because a SECOND hand-written
        /// copy of the same footprints is exactly what broke the shatter. BlockShatterSink carried
        /// its own table, and it disagreed with this one: it laid an L out as three cells in a 2x2
        /// box when an L is four cells in a 3x2 box, and a Two as two cells when a Two is three.
        /// Measured consequence, per shape, as the fraction of the block's own drawn footprint
        /// that got any fracture material at all: Cross 100.0%, Square 100.0%, L 37.5%, Two 33.3%
        /// - and for the L a further 37.5% of a shape-area's worth of material was thrown OUTSIDE
        /// the shape, which is why its hole read as flat black with the shards scattered on the
        /// board around it. Anything that needs to know which cells a shape covers reads it here.
        /// </para>
        /// </summary>
        public static string[] Mask(BlockShapeId id)
        {
            switch (id)
            {
                case BlockShapeId.L:      return new[] { "#..", "###" };
                case BlockShapeId.Square: return new[] { "##", "##" };
                case BlockShapeId.Two:    return new[] { "#", "#", "#" };
                case BlockShapeId.Cross:  return new[] { ".#.", "###", ".#." };
                case BlockShapeId.Single: return new[] { "#" };
                default:                  return null;
            }
        }
    }
}
