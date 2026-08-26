using UnityEngine;

namespace Case3
{
    /// <summary>
    /// Turns a sprite into a tessellated grid mesh and holds the page-curl maths that
    /// <c>Shaders/StickerCurl.shader</c> runs per vertex.
    ///
    /// Why this exists: a <see cref="SpriteRenderer"/> draws a sprite as a four-vertex quad. A
    /// vertex-bend curl on four vertices produces either a hard crease or nothing at all, so the peel
    /// has to run on real geometry. <see cref="Build"/> makes that geometry; <see cref="Curl"/> is a
    /// C# copy of the shader's vertex function so the game code can ask where the curled sheet
    /// actually sits on screen (used to keep the sticker anchored while it peels).
    /// </summary>
    public static class StickerMeshBuilder
    {
        /// <summary>Everything the curl needs, in the mesh's own local units.</summary>
        public struct CurlParams
        {
            /// <summary>Unit direction the fold line travels along; the sheet curls on the +dir side.</summary>
            public Vector2 dir;
            /// <summary>Projection value of the fold line along <see cref="dir"/>.</summary>
            public float fold;
            /// <summary>Radius of the cylinder the paper wraps around. Bigger = looser curl.</summary>
            public float radius;
            /// <summary>Wrap is clamped here (pi = the flap lies flat and mirrored, white side up).</summary>
            public float maxAngle;
            /// <summary>How far the fold line waves off straight, so the crease travels like a ripple.</summary>
            public float waveAmp;
            /// <summary>Spatial frequency of that wave along the fold line.</summary>
            public float waveFreq;
            /// <summary>Phase of that wave; advancing it makes the ripple travel.</summary>
            public float wavePhase;
        }

        /// <summary>
        /// Builds a <paramref name="segments"/> x <paramref name="segments"/> quad grid covering the
        /// sprite's rect, in the same local units and with the same pivot the sprite renderer uses, so
        /// the mesh lands exactly where the sprite was.
        /// </summary>
        public static Mesh Build(Sprite sprite, int segments, out Vector2 localMin, out Vector2 localMax)
        {
            localMin = Vector2.zero;
            localMax = Vector2.zero;
            if (sprite == null) return null;

            segments = Mathf.Clamp(segments, 16, 64);

            Rect rect = sprite.textureRect;
            float ppu = sprite.pixelsPerUnit;
            Vector2 pivotPx = sprite.pivot;                     // pivot in pixels inside textureRect

            localMin = -pivotPx / ppu;
            localMax = (new Vector2(rect.width, rect.height) - pivotPx) / ppu;

            Texture texture = sprite.texture;
            float tw = texture != null ? texture.width : 1f;
            float th = texture != null ? texture.height : 1f;
            Vector2 uvMin = new Vector2(rect.xMin / tw, rect.yMin / th);
            Vector2 uvMax = new Vector2(rect.xMax / tw, rect.yMax / th);

            int side = segments + 1;
            int vertexCount = side * side;

            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[segments * segments * 6];

            for (int y = 0; y < side; y++)
            {
                float fy = (float)y / segments;
                for (int x = 0; x < side; x++)
                {
                    float fx = (float)x / segments;
                    int i = y * side + x;
                    vertices[i] = new Vector3(Mathf.Lerp(localMin.x, localMax.x, fx),
                                              Mathf.Lerp(localMin.y, localMax.y, fy), 0f);
                    uvs[i] = new Vector2(Mathf.Lerp(uvMin.x, uvMax.x, fx),
                                         Mathf.Lerp(uvMin.y, uvMax.y, fy));
                }
            }

            // Row-major, low corner first. The curl always starts at the high (+dir) corner, so the
            // curled triangles are the last ones submitted and therefore draw on top of the flat part
            // without needing depth writes.
            int t = 0;
            for (int y = 0; y < segments; y++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int i = y * side + x;
                    triangles[t++] = i;
                    triangles[t++] = i + side;
                    triangles[t++] = i + side + 1;
                    triangles[t++] = i;
                    triangles[t++] = i + side + 1;
                    triangles[t++] = i + 1;
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "StickerGrid_" + sprite.name + "_" + segments;
            mesh.indexFormat = vertexCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            // The curl moves vertices a long way from the flat rect; a generous fixed bounds keeps the
            // sheet from being frustum-culled mid-peel.
            //
            // Sized off the DIAGONAL, not off x and y separately, because the fold direction is now a
            // free angle. At maxAngle = pi the mirrored flap reaches about twice the sheet's extent
            // ALONG THE FOLD past the starting edge, and along a diagonal that extent is |size|, not
            // size.x - so the old size.x*3 box (half-extent 1.5*size.x) was short of the 1.5*|size| a
            // 45 deg peel needs on a square sticker and could have culled the flap mid-flight. A cube of
            // side 3*|size| covers every angle. Bounds only gate culling, so this cannot move a pixel of
            // a sheet that was already being drawn.
            Vector2 centre = (localMin + localMax) * 0.5f;
            Vector2 size = localMax - localMin;
            float span = size.magnitude * 3f;
            mesh.bounds = new Bounds(new Vector3(centre.x, centre.y, 0f), new Vector3(span, span, span));
            return mesh;
        }

        /// <summary>
        /// Where a flat-sheet point ends up once the page is curled. Identical maths to the shader's
        /// vertex stage: past the fold line the sheet wraps a cylinder of <c>radius</c>, and once it has
        /// wrapped <c>maxAngle</c> it carries on straight along the tangent, which at pi means the flap
        /// lies flat and mirrored with its back to the page.
        /// </summary>
        public static Vector3 Curl(Vector2 p, CurlParams cp)
        {
            Vector2 dir = cp.dir;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            float along = Vector2.Dot(p, dir);
            float across = Vector2.Dot(p, perp);
            float fold = cp.fold + cp.waveAmp * Mathf.Sin(across * cp.waveFreq + cp.wavePhase);

            float u = along - fold;
            if (u <= 0f) return new Vector3(p.x, p.y, 0f);

            float theta = Mathf.Min(u / Mathf.Max(0.0001f, cp.radius), cp.maxAngle);
            float rest = u - theta * cp.radius;                 // length left over after the arc
            float sin = Mathf.Sin(theta);
            float cos = Mathf.Cos(theta);

            float newAlong = fold + cp.radius * sin + rest * cos;
            float z = -(cp.radius * (1f - cos) + rest * sin);   // negative z lifts towards the camera

            Vector2 xy = p + dir * (newAlong - along);
            return new Vector3(xy.x, xy.y, z);
        }

        /// <summary>
        /// Average position of the curled sheet, sampled on a coarse grid. Used to keep the sticker
        /// visually anchored while the fold sweeps across it instead of letting it slide away.
        /// </summary>
        public static Vector3 Centroid(Vector2 localMin, Vector2 localMax, CurlParams cp, int samples = 7)
        {
            samples = Mathf.Max(2, samples);
            Vector3 sum = Vector3.zero;
            for (int y = 0; y < samples; y++)
            {
                float fy = (float)y / (samples - 1);
                for (int x = 0; x < samples; x++)
                {
                    float fx = (float)x / (samples - 1);
                    Vector2 p = new Vector2(Mathf.Lerp(localMin.x, localMax.x, fx),
                                            Mathf.Lerp(localMin.y, localMax.y, fy));
                    sum += Curl(p, cp);
                }
            }
            return sum / (samples * samples);
        }

        /// <summary>Projection range of the sheet's four corners along <paramref name="dir"/>.</summary>
        public static void ProjectionRange(Vector2 localMin, Vector2 localMax, Vector2 dir,
                                           out float min, out float max)
        {
            float a = Vector2.Dot(new Vector2(localMin.x, localMin.y), dir);
            float b = Vector2.Dot(new Vector2(localMax.x, localMin.y), dir);
            float c = Vector2.Dot(new Vector2(localMin.x, localMax.y), dir);
            float d = Vector2.Dot(new Vector2(localMax.x, localMax.y), dir);
            min = Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d));
            max = Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d));
        }
    }
}
