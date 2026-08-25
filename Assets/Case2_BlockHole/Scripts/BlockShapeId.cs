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
    }
}
