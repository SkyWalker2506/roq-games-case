using System;
using UnityEngine;

namespace Case1
{
    /// <summary>
    /// The shapes Case 1 knows about. Everything that used to be a loose string - the live row, the
    /// tray grid, the pairing, the generated glyph, the colour table - keys off this instead.
    ///
    /// The strings were not a style problem. "Shape_Hexagon2" read as the token "hexagon2" and quietly
    /// failed to match "Hexagon-Hole", and a piece that matched nothing fell through to an arbitrary
    /// cell rather than failing loudly. A typo in a shape name could not be caught by the compiler; it
    /// showed up as a round piece dropping into a diamond recess at runtime.
    /// </summary>
    public enum ShapeId
    {
        Round,
        Square,
        Triangle,
        Hexagon,
        Star,
        Diamond
    }

    /// <summary>Conversions between <see cref="ShapeId"/> and the names Unity assets carry.</summary>
    public static class ShapeIds
    {
        public static readonly ShapeId[] All =
        {
            ShapeId.Round, ShapeId.Square, ShapeId.Triangle,
            ShapeId.Hexagon, ShapeId.Star, ShapeId.Diamond
        };

        /// <summary>Asset name of the piece prefab, e.g. Round -> "Round" for Prefabs/Round.prefab.</summary>
        public static string PrefabName(ShapeId id) { return id.ToString(); }

        /// <summary>Scene object name for a playable piece of this shape.</summary>
        public static string ObjectName(ShapeId id) { return "Shape_" + id; }

        /// <summary>
        /// Finds the shape named somewhere inside <paramref name="text"/> - an object name, a mesh
        /// name, a material name. Returns false when nothing matches, so the caller can say so rather
        /// than silently continuing with a wrong shape.
        /// </summary>
        public static bool TryParse(string text, out ShapeId id)
        {
            id = ShapeId.Round;
            if (string.IsNullOrEmpty(text)) return false;
            string n = text.ToLowerInvariant();

            // Longest names first: "hexagon2" contains "hexagon", and a bare Contains() pass in
            // declaration order would let a shorter name win on a longer string.
            if (n.Contains("triangle")) { id = ShapeId.Triangle; return true; }
            if (n.Contains("hexagon"))  { id = ShapeId.Hexagon;  return true; }
            if (n.Contains("diamond"))  { id = ShapeId.Diamond;  return true; }
            if (n.Contains("square"))   { id = ShapeId.Square;   return true; }
            if (n.Contains("round") || n.Contains("circle")) { id = ShapeId.Diamond; return true; }
            if (n.Contains("star"))     { id = ShapeId.Star;    return true; }
            return false;
        }

        /// <summary>True when <paramref name="holeMeshName"/> is the recess this shape fits.</summary>
        public static bool MatchesHole(ShapeId id, string holeMeshName)
        {
            ShapeId hole;
            return TryParse(holeMeshName, out hole) && hole == id;
        }
    }
}
