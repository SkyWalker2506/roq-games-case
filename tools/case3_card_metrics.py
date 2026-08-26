#!/usr/bin/env python3
"""Case 3 - how much of a reward card's panel the subject actually fills.

STRUCTURAL INVARIANT UNDER TEST
    A filled reward card is a picture in a frame. The subject is the picture; the panel is
    the frame's opening - everything inside the card's inner border and below the name tab.
    The reference's cards are laid out so the subject FILLS that opening: one large, centred
    subject with a small, even margin. The invariant is therefore about proportion, not
    about pixels:

        bbox(subject) / area(panel)  and the four margins as fractions of the panel

    must match the reference's, on cards of any size, because both are dimensionless.

HOW THE TWO SIDES ARE SEPARATED
    Reference panel: the lilac grid paper. It is the only thing on the card whose red AND
    blue both sit well above its green (R-G > 30 and B-G > 20); the cat's grey (165,152,145)
    is 13 apart, the pasta's cream 18, the jam's amber has B BELOW G. So that one test
    separates panel from subject on all three cards without a per-card tweak.
    Our panel: the flat inner fill the card is drawn with, matched within a tolerance.
    In both cases the panel RECT is the bounding box of the panel-background class, which
    is why the name tab - opaque, drawn over the panel - correctly excludes itself.

POSITIVE CONTROL (--control)
    Composites a subject of known size at a known offset onto a synthetic panel of each
    kind and checks the reported fill ratio and margins come back as authored, to within
    a pixel. Run it before believing any reading below.
"""
import sys
import numpy as np
from PIL import Image

REF = 'Assets/Case3_Stickerdom/Textures/Reference/card_filled_{}.png'
OURS = 'Assets/Case3_Stickerdom/Sprites/Cards/card_filled_{}.png'
KEYS = ['cat', 'noodle', 'sweets']


def lilac_panel(rgb):
    r, g, b = rgb[..., 0].astype(int), rgb[..., 1].astype(int), rgb[..., 2].astype(int)
    return ((r - g) > 30) & ((b - g) > 20)


def flat_panel(colors, tol=26):
    def f(rgb):
        m = np.zeros(rgb.shape[:2], bool)
        for c in colors:
            m |= (np.abs(rgb.astype(int) - np.array(c, int)).max(axis=2) <= tol)
        return m
    return f


def largest_component(mask):
    """8-connected. The subject is one printed sticker, so it is one blob; the panel's own
    furniture - grid lines, the dashed border, the gradient's darker end - is not."""
    h, w = mask.shape
    lab = np.zeros((h, w), np.int32)
    cur = 0
    best = (0, 0)
    idx = np.argwhere(mask)
    seen = np.zeros((h, w), bool)
    for sy, sx in idx:
        if seen[sy, sx]: continue
        cur += 1
        stack = [(sy, sx)]; seen[sy, sx] = True; n = 0
        while stack:
            y, x = stack.pop(); lab[y, x] = cur; n += 1
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True; stack.append((ny, nx))
        if n > best[0]: best = (n, cur)
    return lab == best[1] if best[1] else mask


def bbox(mask):
    ys, xs = np.where(mask)
    if len(ys) == 0: return None
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def measure(path, panel_fn, alpha_min=128):
    im = Image.open(path).convert('RGBA')
    a = np.array(im)
    rgb, al = a[..., :3], a[..., 3]
    card = al >= alpha_min
    panel_bg = panel_fn(rgb) & card
    pb = bbox(panel_bg)
    if pb is None: return None
    x0, y0, x1, y1 = pb
    # The name tab is drawn OVER the panel across the card's top 16.4% (measured on the
    # reference crops and reproduced in the generator), and on the Sweets card the tab is
    # itself lilac, so the class alone would swallow it. Clamp the panel's top below the
    # tab on every card, then inset 3 px so the frame's own antialiased lip is not read as
    # subject.
    y0 = max(y0, int(round(0.175 * a.shape[0])))
    x0 += 3; y0 += 3; x1 -= 3; y1 -= 3
    pw, ph = x1 - x0, y1 - y0

    inside = np.zeros_like(card); inside[y0:y1, x0:x1] = True
    subject = largest_component(card & inside & ~panel_bg)
    sb = bbox(subject)
    if sb is None: return None
    sx0, sy0, sx1, sy1 = sb
    return dict(
        card=(a.shape[1], a.shape[0]),
        panel=(x0, y0, x1, y1), panel_wh=(pw, ph),
        subject=(sx0, sy0, sx1, sy1),
        bbox_fill=((sx1 - sx0) * (sy1 - sy0)) / float(pw * ph),
        ink_fill=float(subject.sum()) / float(pw * ph),
        w_frac=(sx1 - sx0) / float(pw), h_frac=(sy1 - sy0) / float(ph),
        m_left=(sx0 - x0) / float(pw), m_right=(x1 - sx1) / float(pw),
        m_top=(sy0 - y0) / float(ph), m_bottom=(y1 - sy1) / float(ph),
    )


def report(tag, m):
    if m is None:
        print(f"  {tag:22s} -- no panel found"); return
    print(f"  {tag:22s} panel {m['panel_wh'][0]:3d}x{m['panel_wh'][1]:3d}  "
          f"bbox-fill {m['bbox_fill']*100:5.1f}%  ink {m['ink_fill']*100:5.1f}%  "
          f"w {m['w_frac']*100:5.1f}% h {m['h_frac']*100:5.1f}%  "
          f"margins L{m['m_left']*100:5.1f} R{m['m_right']*100:5.1f} "
          f"T{m['m_top']*100:5.1f} B{m['m_bottom']*100:5.1f}")


OUR_PANEL_COLORS = [(225, 202, 180), (225, 198, 172), (228, 198, 178)]


def control():
    print("POSITIVE CONTROL - a subject of known size on a panel of known size")
    ok = True
    for name, bg, fn in (
        ("lilac", (198, 146, 187), lilac_panel),
        ("flat", OUR_PANEL_COLORS[0], flat_panel(OUR_PANEL_COLORS)),
    ):
        W, H = 244, 292
        px0, py0, px1, py1 = 20, 52, 224, 272           # authored panel
        sx0, sy0, sw, sh = 40, 70, 150, 160             # authored subject
        a = np.zeros((H, W, 4), np.uint8)
        a[..., :3] = (90, 50, 20); a[..., 3] = 255      # card frame
        a[py0:py1, px0:px1, :3] = bg
        a[sy0:sy0 + sh, sx0:sx0 + sw, :3] = (250, 240, 60)
        p = '/tmp/_c3_card_control.png'
        Image.fromarray(a, 'RGBA').save(p)
        m = measure(p, fn)
        # the same clamp-and-inset the instrument applies, applied to the authored numbers
        ex0, ey0 = px0 + 3, max(py0, int(round(0.175 * H))) + 3
        ex1, ey1 = px1 - 3, py1 - 3
        pw, ph = ex1 - ex0, ey1 - ey0
        want_fill = (sw * sh) / float(pw * ph)
        want_l, want_t = (sx0 - ex0) / pw, (sy0 - ey0) / ph
        good = (m is not None and m['panel_wh'] == (pw, ph)
                and abs(m['bbox_fill'] - want_fill) < 0.01
                and abs(m['m_left'] - want_l) < 0.01 and abs(m['m_top'] - want_t) < 0.01)
        ok &= good
        report("CONTROL " + name, m)
        print(f"    authored: panel {pw}x{ph}  bbox-fill {want_fill*100:5.1f}%  "
              f"L{want_l*100:5.1f} T{want_t*100:5.1f}   {'PASS' if good else 'FAIL'}")
    print("  CONTROL " + ("PASS" if ok else "FAIL"))
    return ok


if __name__ == '__main__':
    args = sys.argv[1:]
    if '--control' in args and not control():
        sys.exit(1)
    print("\nREFERENCE cards")
    ref = {}
    for k in KEYS:
        ref[k] = measure(REF.format(k), lilac_panel); report('ref ' + k, ref[k])
    got = [v for v in ref.values() if v]
    if got:
        print(f"  {'REF MEDIAN':22s} bbox-fill {np.median([v['bbox_fill'] for v in got])*100:5.1f}%  "
              f"ink {np.median([v['ink_fill'] for v in got])*100:5.1f}%  "
              f"w {np.median([v['w_frac'] for v in got])*100:5.1f}% "
              f"h {np.median([v['h_frac'] for v in got])*100:5.1f}%  "
              f"margins L{np.median([v['m_left'] for v in got])*100:5.1f} "
              f"R{np.median([v['m_right'] for v in got])*100:5.1f} "
              f"T{np.median([v['m_top'] for v in got])*100:5.1f} "
              f"B{np.median([v['m_bottom'] for v in got])*100:5.1f}")
    print("\nOUR cards")
    ours = {}
    for k in KEYS:
        ours[k] = measure(OURS.format(k), flat_panel(OUR_PANEL_COLORS)); report('our ' + k, ours[k])
    got = [v for v in ours.values() if v]
    if got:
        print(f"  {'OUR MEDIAN':22s} bbox-fill {np.median([v['bbox_fill'] for v in got])*100:5.1f}%  "
              f"ink {np.median([v['ink_fill'] for v in got])*100:5.1f}%  "
              f"w {np.median([v['w_frac'] for v in got])*100:5.1f}% "
              f"h {np.median([v['h_frac'] for v in got])*100:5.1f}%  "
              f"margins L{np.median([v['m_left'] for v in got])*100:5.1f} "
              f"R{np.median([v['m_right'] for v in got])*100:5.1f} "
              f"T{np.median([v['m_top'] for v in got])*100:5.1f} "
              f"B{np.median([v['m_bottom'] for v in got])*100:5.1f}")
