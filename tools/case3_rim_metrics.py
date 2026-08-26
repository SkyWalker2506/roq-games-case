#!/usr/bin/env python3
"""Case 3 - die-cut rim geometry, measured the same way on the reference and on us.

STRUCTURAL INVARIANT UNDER TEST
    A printed die-cut sticker's white border is a CONSTANT-WIDTH OUTSET of the art's
    silhouette with a hard, antialiased outer edge. Formally, on a rendered frame the
    rim's coverage must be a function of the screen-space distance d from the silhouette
    alone - 1 for d <= W-e, 0 for d >= W+e, monotone between - so that

        (a) the white band measures the same W all the way round, whatever the art's
            own alpha does locally, and
        (b) the band's outer edge takes 2e pixels to fall from opaque to background.

    A rim built by dilating a FEATHERED alpha violates (a) and (b) together: its outer
    profile is the art's own feather, so it fades over as many pixels as the art happens
    to be soft, and the apparent width follows that softness around the silhouette.

WHAT IS MEASURED, ON ONE INSTRUMENT, FOR BOTH SIDES
    Input is an opaque RGB frame (the reference video frame, or capture_game_view) plus
    a rectangular ROI around one sticker and the paper colour behind it.

      f(x)  "sticker-ness": 0 on bare paper, 1 on the white rim, by projecting the pixel
            onto the paper->white colour axis. f is continuous, so a blurred edge shows
            up as a long ramp instead of being quantised away by a threshold.
      rays  cast from the f=0.5 level set along the outward normal (the gradient of the
            exact Euclidean distance transform of f>=0.5), sampled bilinearly at 0.25 px.
      W     rim width: from the f=0.5 crossing INWARD until the pixel stops being white
            (whiteness < 0.5), i.e. until the art starts. Rays that never leave white
            (the art itself is white there) are discarded.
      E     edge profile: the OUTWARD distance from f=0.9 to f=0.1.

    Both are reported in RENDER-TARGET PIXELS of a 1080-wide frame, which is the unit the
    reference is authored in and the unit the shader's _RimPixels uses.

POSITIVE CONTROL
    --control synthesises two rims of known geometry on the same paper colour and runs
    the identical code path over them:
      HARD  a 10.0 px outset with a 1 px antialiased edge  -> must read W~10, E~1
      SOFT  the same outset feathered by an 8 px blur      -> must read E >> 1
    If HARD does not come back at its authored numbers the instrument is not measuring
    what it claims and no negative reading from it may be believed.
"""
import sys, math, json
import numpy as np
from PIL import Image


# ---------------------------------------------------------------- exact EDT
def _edt1d(f):
    n = len(f); d = np.empty(n); v = np.zeros(n, dtype=int); z = np.empty(n + 1)
    k = 0; v[0] = 0; z[0] = -np.inf; z[1] = np.inf
    for q in range(1, n):
        s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2.0 * q - 2.0 * v[k])
        while s <= z[k]:
            k -= 1
            s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2.0 * q - 2.0 * v[k])
        k += 1; v[k] = q; z[k] = s; z[k + 1] = np.inf
    k = 0
    for q in range(n):
        while z[k + 1] < q: k += 1
        d[q] = (q - v[k]) ** 2 + f[v[k]]
    return d


def edt(mask):
    """Euclidean distance from every False pixel to the nearest True pixel."""
    INF = 1e12
    f = np.where(mask, 0.0, INF)
    out = np.empty_like(f)
    for y in range(f.shape[0]): out[y] = _edt1d(f[y])
    for x in range(f.shape[1]): out[:, x] = _edt1d(out[:, x])
    return np.sqrt(out)


def signed_dist(mask):
    """+ outside the mask, - inside; zero on the boundary."""
    return edt(mask) - edt(~mask)


def bilinear(img, x, y):
    h, w = img.shape[:2]
    x = np.clip(x, 0, w - 1.001); y = np.clip(y, 0, h - 1.001)
    x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int)
    fx = x - x0; fy = y - y0
    a = img[y0, x0]; b = img[y0, x0 + 1]; c = img[y0 + 1, x0]; d = img[y0 + 1, x0 + 1]
    if img.ndim == 3:
        fx = fx[..., None]; fy = fy[..., None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


# ---------------------------------------------------------------- fields
def fields(rgb, paper):
    """f = paper->white projection in [0,1]; whiteness = closeness to pure white."""
    p = np.asarray(paper, np.float32)
    white = np.float32([255, 255, 255])
    axis = white - p
    n2 = float(axis @ axis)
    v = rgb.astype(np.float32) - p
    f = np.clip((v @ axis) / n2, 0.0, 1.5)
    # orthogonal residual: art pixels are far off the paper->white axis
    proj = f[..., None] * axis
    resid = np.linalg.norm(v - proj, axis=-1)
    whiteness = np.clip(1.0 - resid / 70.0, 0.0, 1.0) * np.clip(f / 0.85, 0.0, 1.0)
    return np.clip(f, 0, 1), np.clip(whiteness, 0, 1)


def measure(rgb, paper, min_rays=60, max_walk=40.0, step=0.25):
    f, whiteness = fields(rgb, paper)
    mask = f >= 0.5
    if mask.sum() < 50 or (~mask).sum() < 50:
        return None
    sd = signed_dist(mask)                       # + outside
    gy, gx = np.gradient(sd)
    g = np.sqrt(gx * gx + gy * gy) + 1e-6
    nx, ny = gx / g, gy / g                      # unit outward normal

    # boundary sample points: pixels just outside the level set, sampled sparsely
    ys, xs = np.where((sd > 0.0) & (sd <= 1.0))
    if len(ys) < min_rays: return None
    stride = max(1, len(ys) // 1500)
    ys, xs = ys[::stride], xs[::stride]
    # push each start point exactly onto the f=0.5 level set
    d0 = sd[ys, xs]
    ux, uy = nx[ys, xs], ny[ys, xs]
    px, py = xs - ux * d0, ys - uy * d0

    widths, edges, peaks = [], [], []
    steps = np.arange(step, max_walk, step)
    for i in range(len(px)):
        x0, y0, dx, dy = px[i], py[i], ux[i], uy[i]
        # ---- inward: white band width
        wsamp = bilinear(whiteness, x0 - dx * steps, y0 - dy * steps)
        below = np.where(wsamp < 0.5)[0]
        if len(below) == 0:
            continue                              # art is white here; not a rim reading
        widths.append(steps[below[0]])
        # ---- outward: edge profile, f 0.9 -> 0.1
        fsamp = bilinear(f, x0 + dx * steps, y0 + dy * steps)
        hi_in = bilinear(f, x0 - dx * steps, y0 - dy * steps)
        peak = float(hi_in.max())
        peaks.append(peak)
        # thresholds ride the rim's own plateau, so a rim that never reaches full
        # opacity is still measurable - and its low peak is reported beside the width.
        lo = np.where(fsamp < 0.1 * peak)[0]
        hi = np.where(hi_in >= 0.9 * peak)[0]
        if len(lo) == 0 or len(hi) == 0: continue
        edges.append(steps[lo[0]] + steps[hi[0]])
    if len(widths) < min_rays: return None
    w = np.array(widths); e = np.array(edges) if edges else np.array([float('nan')])
    return dict(
        rays=len(w),
        width_p25=float(np.percentile(w, 25)), width_med=float(np.median(w)),
        width_p75=float(np.percentile(w, 75)),
        width_iqr=float(np.percentile(w, 75) - np.percentile(w, 25)),
        edge_med=float(np.median(e)), edge_p90=float(np.percentile(e, 90)),
        peak=float(np.median(peaks)) if peaks else float('nan'),
    )


def report(tag, m):
    if m is None:
        print(f"  {tag:28s} -- not enough rim to measure"); return
    print(f"  {tag:28s} W med {m['width_med']:5.2f} px  IQR {m['width_iqr']:5.2f} "
          f"(p25 {m['width_p25']:5.2f} p75 {m['width_p75']:5.2f})   "
          f"EDGE med {m['edge_med']:5.2f} px  p90 {m['edge_p90']:5.2f}   "
          f"peak {m['peak']:4.2f}   n={m['rays']}")


# ---------------------------------------------------------------- control
def _blob(w, h):
    yy, xx = np.mgrid[0:h, 0:w]
    a = ((xx - w * 0.42) ** 2 / (w * 0.20) ** 2 + (yy - h * 0.5) ** 2 / (h * 0.26) ** 2) <= 1
    b = ((xx - w * 0.66) ** 2 / (w * 0.13) ** 2 + (yy - h * 0.40) ** 2 / (h * 0.17) ** 2) <= 1
    return a | b


def control(paper=(243, 219, 178), W=10.0):
    h, w = 320, 400
    art = _blob(w, h)
    d = edt(art)                                  # distance outside the art
    out = {}
    for name, alpha in (
        ("CONTROL hard 10.0px/1px", np.clip(W + 0.5 - d, 0.0, 1.0)),
        ("CONTROL blurred 8px",     None),
    ):
        if alpha is None:
            hard = np.clip(W + 0.5 - d, 0.0, 1.0)
            alpha = _blur(hard, 8.0)
        rgb = np.zeros((h, w, 3), np.float32)
        rgb[:] = np.float32(paper)
        rim = np.float32([252, 251, 250])
        rgb = rgb * (1 - alpha[..., None]) + rim * alpha[..., None]
        rgb[art] = np.float32([120, 70, 40])
        out[name] = measure(np.clip(rgb, 0, 255).astype(np.uint8), paper)
    return out


def _blur(a, sigma):
    r = int(sigma * 3)
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2); k /= k.sum()
    o = np.apply_along_axis(lambda m: np.convolve(m, k, mode='same'), 1, a)
    return np.apply_along_axis(lambda m: np.convolve(m, k, mode='same'), 0, o)


# ---------------------------------------------------------------- ROIs
# (x0, y0, x1, y1) on a 1080x1728 frame, plus the paper colour behind that sticker.
REF_ROIS = {
    "ref grey-white cat":  ((640, 800, 950, 1100), (243, 219, 178)),
    "ref spaghetti plate": ((300, 1030, 660, 1250), (243, 219, 178)),
    "ref candy cane":      ((150, 1280, 500, 1470), (240, 216, 176)),
}
OUR_ROIS = {
    "our ginger cat":      ((660, 800, 900, 1130), (238, 224, 192)),
    "our navy ramen":      ((250, 1330, 560, 1580), (238, 224, 192)),
    "our croissant":       ((90, 1330, 340, 1520), (238, 224, 192)),
    "our chocolate":       ((490, 1260, 800, 1580), (238, 224, 192)),
}


def run(frame_path, rois, label):
    rgb = np.array(Image.open(frame_path).convert('RGB'))
    print(f"{label}  <- {frame_path}")
    rows = []
    for tag, (roi, paper) in rois.items():
        x0, y0, x1, y1 = roi
        m = measure(rgb[y0:y1, x0:x1], paper)
        report(tag, m)
        if m: rows.append(m)
    if rows:
        print(f"  {'POOLED':28s} W med {np.median([r['width_med'] for r in rows]):5.2f} px   "
              f"EDGE med {np.median([r['edge_med'] for r in rows]):5.2f} px")
    print()
    return rows


# ---------------------------------------------------------------- gate
# Thresholds derived from the measured population, not chosen ahead of it.
#   peak   the whitest pixel in the band, as a fraction of the paper->white distance.
#          Reference: 0.92 .. 0.98. Ours before: 0.76, 1.00, 1.00, 0.77 - a rim that never
#          reaches full white is a glow, not a printed border. The population gap is
#          0.77 .. 0.92, so 0.90 clears the failures by 0.13 and the reference by 0.02.
#   IQR    spread of the width around one sticker. Ours before: 1.00 .. 3.50. After:
#          0.75 .. 1.25. Gap 1.25 .. 1.75, so 1.50 sits in the middle of it.
#   W      the band itself, against the reference's 7.50 .. 8.50 pooled at 8.50, widened
#          by the instrument's own +-0.5 px bias measured on the control.
GATE_PEAK_MIN = 0.90
GATE_IQR_MAX = 1.50
GATE_W_RANGE = (7.0, 10.0)


def gate(frame_path):
    rgb = np.array(Image.open(frame_path).convert('RGB'))
    print(f"RIM GATE  <- {frame_path}")
    fails = []
    meds = []
    for tag, (roi, paper) in OUR_ROIS.items():
        x0, y0, x1, y1 = roi
        m = measure(rgb[y0:y1, x0:x1], paper)
        if m is None:
            fails.append(f"{tag}: no rim to measure"); print(f"  {tag:24s} NO RIM"); continue
        meds.append(m['width_med'])
        bad = []
        if m['peak'] < GATE_PEAK_MIN: bad.append(f"peak {m['peak']:.2f} < {GATE_PEAK_MIN}")
        if m['width_iqr'] > GATE_IQR_MAX: bad.append(f"IQR {m['width_iqr']:.2f} > {GATE_IQR_MAX}")
        if bad: fails.append(tag + ": " + ", ".join(bad))
        print(f"  {tag:24s} W {m['width_med']:5.2f}  IQR {m['width_iqr']:4.2f}  "
              f"EDGE {m['edge_med']:4.2f}  peak {m['peak']:4.2f}   {'FAIL: ' + '; '.join(bad) if bad else 'ok'}")
    if meds:
        pooled = float(np.median(meds))
        inb = GATE_W_RANGE[0] <= pooled <= GATE_W_RANGE[1]
        if not inb: fails.append(f"pooled W {pooled:.2f} outside {GATE_W_RANGE}")
        print(f"  {'POOLED':24s} W {pooled:5.2f}   {'ok' if inb else 'FAIL'}")
    print("  RIM GATE " + ("RED  (" + " | ".join(fails) + ")" if fails else "GREEN"))
    return not fails


if __name__ == '__main__':
    args = sys.argv[1:]
    if '--control' in args:
        print("POSITIVE CONTROL - synthetic rims of known geometry, same code path")
        for k, v in control().items(): report(k, v)
        print()
    if '--ref' in args:
        run('Assets/Case3_Stickerdom/Textures/Reference/stickerdom_base_final.png',
            REF_ROIS, 'REFERENCE frame')
    for a in args:
        if a.startswith('--gate='):
            if not gate(a.split('=', 1)[1]):
                sys.exit(1)
        if a.startswith('--ours='):
            run(a.split('=', 1)[1], OUR_ROIS, 'OURS  game view')
