using System.Collections.Generic;
using UnityEngine;

namespace Shared.EditorTools
{
    /// <summary>
    /// Editor-only geometry helpers used by the four setup scripts.  The reference videos are all
    /// 1080x1728 (aspect 0.625), therefore every authored layout target is expressed in viewport space.
    /// This deliberately avoids hand-tuned world coordinates: a clean clone, a different Game View
    /// size, or a rebuilt camera still lands the art at the same place on screen.
    /// </summary>
    public static class ReferenceMatchLayout
    {
        public const float Aspect = 1080f / 1728f;

        public static bool ProjectBounds(Camera cam, IEnumerable<Renderer> renderers, out Rect rect)
        {
            rect = new Rect();
            if (cam == null || renderers == null) return false;
            float oldAspect = cam.aspect;
            cam.aspect = Aspect;
            bool any = false;
            float x0 = float.PositiveInfinity, y0 = float.PositiveInfinity;
            float x1 = float.NegativeInfinity, y1 = float.NegativeInfinity;
            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled) continue;
                Bounds b = r.bounds;
                Vector3 c = b.center, e = b.extents;
                for (int ix = -1; ix <= 1; ix += 2)
                for (int iy = -1; iy <= 1; iy += 2)
                for (int iz = -1; iz <= 1; iz += 2)
                {
                    Vector3 v = cam.WorldToViewportPoint(c + Vector3.Scale(e, new Vector3(ix, iy, iz)));
                    if (v.z <= 0f) continue;
                    any = true;
                    x0 = Mathf.Min(x0, v.x); y0 = Mathf.Min(y0, v.y);
                    x1 = Mathf.Max(x1, v.x); y1 = Mathf.Max(y1, v.y);
                }
            }
            cam.aspect = oldAspect;
            if (!any) return false;
            rect = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }

        public static bool ProjectBounds(Camera cam, Transform root, out Rect rect)
        {
            return ProjectBounds(cam, root != null ? root.GetComponentsInChildren<Renderer>(true) : null, out rect);
        }

        /// <summary>Uniformly scales and screen-translates a root until its projected bounds match target.</summary>
        public static void FitRoot(Camera cam, Transform root, Rect target, bool widthDriven = true, int iterations = 4)
        {
            if (cam == null || root == null) return;
            float oldAspect = cam.aspect;
            cam.aspect = Aspect;
            for (int pass = 0; pass < iterations; pass++)
            {
                Rect cur;
                if (!ProjectBounds(cam, root, out cur) || cur.width < 0.0001f || cur.height < 0.0001f) break;
                float factor = widthDriven ? target.width / cur.width : target.height / cur.height;
                factor = Mathf.Clamp(factor, 0.70f, 1.35f);
                root.localScale *= factor;

                if (!ProjectBounds(cam, root, out cur)) break;
                Vector2 delta = target.center - cur.center;
                float depth = Mathf.Max(0.05f, cam.WorldToViewportPoint(root.position).z);
                Vector3 a = cam.ViewportToWorldPoint(new Vector3(cur.center.x, cur.center.y, depth));
                Vector3 b = cam.ViewportToWorldPoint(new Vector3(cur.center.x + delta.x, cur.center.y + delta.y, depth));
                root.position += b - a;
            }
            cam.aspect = oldAspect;
        }

        /// <summary>Places an XY/sprite-plane transform at a reference viewport coordinate.</summary>
        public static void PlaceAtDepth(Camera cam, Transform t, Vector2 viewport, float depth)
        {
            if (cam == null || t == null) return;
            float oldAspect = cam.aspect; cam.aspect = Aspect;
            t.position = cam.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, depth));
            cam.aspect = oldAspect;
        }

        /// <summary>Places an XZ/floor-plane transform by ray/plane intersection at y.</summary>
        public static void PlaceOnHorizontalPlane(Camera cam, Transform t, Vector2 viewport, float y)
        {
            if (cam == null || t == null) return;
            float oldAspect = cam.aspect; cam.aspect = Aspect;
            Ray ray = cam.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
            Plane p = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            float enter;
            if (p.Raycast(ray, out enter))
            {
                Vector3 hit = ray.GetPoint(enter);
                t.position = new Vector3(hit.x, y, hit.z);
            }
            cam.aspect = oldAspect;
        }

        public static Vector2 WorldSizeAtDepth(Camera cam, float depth, Vector2 viewportSize)
        {
            if (cam == null) return viewportSize;
            float oldAspect = cam.aspect; cam.aspect = Aspect;
            Vector3 c = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, depth));
            Vector3 x = cam.ViewportToWorldPoint(new Vector3(0.5f + viewportSize.x, 0.5f, depth));
            Vector3 y = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f + viewportSize.y, depth));
            cam.aspect = oldAspect;
            return new Vector2(Vector3.Distance(c, x), Vector3.Distance(c, y));
        }

        /// <summary>Fits an orthographic camera to a renderer group without scaling the art.</summary>
        public static void FitOrthographicCamera(Camera cam, Transform root, Rect target, bool widthDriven = true, int iterations = 5)
        {
            if (cam == null || root == null || !cam.orthographic) return;
            float oldAspect = cam.aspect; cam.aspect = Aspect;
            for (int pass = 0; pass < iterations; pass++)
            {
                Rect cur;
                if (!ProjectBounds(cam, root, out cur) || cur.width < 0.0001f || cur.height < 0.0001f) break;
                float ratio = widthDriven ? cur.width / target.width : cur.height / target.height;
                cam.orthographicSize *= Mathf.Clamp(ratio, 0.75f, 1.25f);
                if (!ProjectBounds(cam, root, out cur)) break;
                Vector2 delta = target.center - cur.center;
                float depth = Mathf.Max(0.05f, cam.WorldToViewportPoint(root.position).z);
                Vector3 a = cam.ViewportToWorldPoint(new Vector3(cur.center.x, cur.center.y, depth));
                Vector3 b = cam.ViewportToWorldPoint(new Vector3(cur.center.x + delta.x, cur.center.y + delta.y, depth));
                // Moving the camera right moves projected art left, therefore inverse the desired art shift.
                cam.transform.position -= b - a;
            }
            cam.aspect = oldAspect;
        }
    }
}
