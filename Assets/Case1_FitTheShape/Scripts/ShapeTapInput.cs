using UnityEngine;
using UnityEngine.InputSystem;

namespace Case1
{
    /// <summary>
    /// Tap-to-play input for the case scene. It does not merely trigger a canned animation: it works out
    /// WHICH deck shape the press landed on and hands that shape to the director, which then flies that
    /// shape to the cell it matches. A press that lands on nothing, or on a shape that has already been
    /// used, is ignored - the scene stays where it is and the remaining shapes stay tappable.
    ///
    /// Screen-space proximity is used rather than physics so the scene needs no colliders and no layer
    /// setup - the drum art sits on its own layer and would otherwise have to be excluded by hand.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShapeTapInput : MonoBehaviour
    {
        [Header("Scene wiring (filled in by Case1SceneSetup)")]
        public Camera viewCamera;
        public Case1Director director;

        [Tooltip("Hit radius in pixels at a 1080 px wide screen; scaled for other widths.")]
        public float tapRadiusPixels = 110f;

        void Update()
        {
            if (director == null || viewCamera == null || director.IsPlaying) return;

            Vector2 screenPoint;
            if (!TryReadPress(out screenPoint)) return;

            Transform hitShape = PickTrayShape(screenPoint);
            if (hitShape == null)
            {
                Shared.Sequencing.SeqLog.Info("[Case1Tap] press at " + screenPoint + " hit no shape; ignored");
                return;
            }

            director.HandlePieceTap(hitShape);
        }

        /// <summary>
        /// USER DIRECTIVE: öndeki üçlüye basılabilir yap ve arkadan öne gelenlerde basılabilir olsun.
        /// Finds the closest active tray piece under the screen tap position.
        /// </summary>
        public Transform PickTrayShape(Vector2 screenPoint)
        {
            if (director == null || director.deck == null || viewCamera == null) return null;

            DeckReflow deck = director.deck;
            float radius = tapRadiusPixels * (Screen.width / 1080f);
            float best = radius * radius;
            Transform hit = null;

            for (int i = 0; i < deck.entries.Length; i++)
            {
                DeckReflow.Entry e = deck.entries[i];
                if (e == null || e.Gone || e.shape == null || !e.shape.gameObject.activeInHierarchy) continue;

                Vector3 p = viewCamera.WorldToScreenPoint(e.shape.position);
                if (p.z <= 0f) continue;

                float d = ((Vector2)p - screenPoint).sqrMagnitude;
                if (d <= best)
                {
                    best = d;
                    hit = e.shape;
                }
            }

            return hit;
        }

        /// <summary>Index of the closest still-playable shape under the press, or -1.</summary>
        public int PickShape(Vector2 screenPoint)
        {
            Transform t = PickTrayShape(screenPoint);
            if (t != null && director != null && director.flight != null)
            {
                return director.flight.IndexOf(t);
            }
            return -1;
        }

        /// <summary>Screen position of a deck shape, for the selection gate's synthetic taps.</summary>
        public Vector2 ScreenPointOf(int shapeIndex)
        {
            ShapeArcFlight flight = director != null ? director.flight : null;
            if (flight == null || viewCamera == null || shapeIndex < 0 || shapeIndex >= flight.Count) return Vector2.zero;
            Transform t = flight.entries[shapeIndex].shape;
            if (t == null) return Vector2.zero;
            return viewCamera.WorldToScreenPoint(t.position);
        }

        static bool TryReadPress(out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPoint = mouse.position.ReadValue();
                return true;
            }

            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            {
                screenPoint = touch.primaryTouch.position.ReadValue();
                return true;
            }

            return false;
        }
    }
}
