#!/usr/bin/env python3
"""Case 3 asset-level gate: the dim transform, the reward-card name tab, and page coverage.

Runs entirely off the repo's own files - the scene YAML, the .mat/.shader pair and the
sprite PNGs - so it can be executed while the Unity Editor owns the project.

WHAT IT DOES AND DOES NOT PROVE
  It re-implements PageObjectDim.frag in numpy and feeds it the material's OWN serialised
  floats, falling back to the shader's declared defaults exactly the way Unity does. So it
  is red whenever the numbers on disk describe the wrong transform - including the case
  that has bitten this project twice, where one copy of a two-copy pair was fixed and the
  other was not. It does NOT prove that Unity's rasteriser produced those pixels; only a
  capture can do that.

  subcommands:
    dim       - the dim transform must match the reference: saturation kept, flat multiply
    cards     - the reward cards must carry a name tab in their top band
    coverage  - alpha overlap between drawn stickers, the input to the promotion invariant
"""
import os, re, sys, glob
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCENE = os.path.join(ROOT, 'Assets/Case3_Stickerdom/Scenes/Stickerdom.unity')
SHADER = os.path.join(ROOT, 'Assets/Case3_Stickerdom/Shaders/PageObjectDim.shader')
MATGLOB = os.path.join(ROOT, 'Assets/Case3_Stickerdom/Materials/Case3_PageObjectDim*.mat')

# ---------------------------------------------------------------- reference measurements
#
# Measured on Stickerdom.mp4 at the promotion instant, on regions uncovered in BOTH states
# (so it is a tone change, not a reveal). Value is 0..255 sRGB, saturation is HSV.
#
#   cup band  dim V 74.31 S 0.620   lit V 149.53 S 0.615
#   jar lid   dim V 85.41 S 0.467   lit V 168.13 S 0.466
#
# Re-derived here rather than inherited: converting each V through the sRGB EOTF gives
# linear ratios 0.0688/0.3029 = 0.227 and 0.0918/0.3922 = 0.234, mean 0.231. The naive
# "sRGB ratio ^ 2.4" shortcut gives 0.19 and is wrong at these levels because the sRGB toe
# still matters. Saturation ratios are 1.008 and 0.998 - untouched.
REF_LINEAR_VALUE_RATIO = (0.19, 0.28)   # 0.231 measured, +/- 20% for texture-population drift
REF_SAT_RETENTION = (0.95, 1.20)        # reference kept 100%; under 95% reads grey, over 120% is invented
#
# A known, accepted deviation: the reference holds HSV saturation at 1.00 across the change,
# which is what a multiply done in sRGB space does. Our shader multiplies LINEAR colour, and a
# flat linear multiply raises sRGB HSV saturation by 6-14% at these levels. The value match is
# the same to within a sRGB step either way; the difference is that our dim reads very slightly
# MORE colourful than the reference's, never less. Matching 1.00 exactly would mean encoding to
# sRGB inside the fragment shader, and the four thresholds in this shader have already been
# broken once by sRGB-shaped numbers.
REF_FLATNESS_MAX = 0.06                 # dark tertile vs bright tertile ratio spread


def srgb_to_linear(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(np.asarray(c, dtype=np.float64), 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * (c ** (1 / 2.4)) - 0.055)


def smoothstep(a, b, x):
    t = np.clip((x - a) / max(1e-9, (b - a)), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def hsv_saturation(srgb):
    """HSV S over an Nx3 sRGB 0..1 array."""
    mx = srgb.max(axis=-1)
    mn = srgb.min(axis=-1)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


# ---------------------------------------------------------------- shader / material parsing

def shader_defaults():
    """The Properties block's declared defaults - copy one of the two-copy pair."""
    txt = open(SHADER).read()
    props = txt.split('Properties', 1)[1].split('SubShader', 1)[0]
    out = {}
    for m in re.finditer(r'^\s*(?:\[[^\]]*\]\s*)?(_\w+)\s*\(.*\)\s*=\s*([-\d.]+)\s*$', props, re.M):
        out[m.group(1)] = float(m.group(2))
    return out


def material_floats(path):
    """The serialised floats - copy two of the two-copy pair."""
    txt = open(path).read()
    out = {}
    body = txt.split('m_Floats:', 1)
    if len(body) < 2:
        return out
    for line in body[1].splitlines():
        m = re.match(r'\s*-\s*(_\w+):\s*([-\d.eE]+)\s*$', line)
        if m:
            out[m.group(1)] = float(m.group(2))
        elif line.strip().startswith('m_Colors'):
            break
    return out


def material_params(path):
    """What Unity actually feeds the shader: material floats over shader defaults."""
    p = dict(shader_defaults())
    p.update(material_floats(path))
    return p


def apply_dim(rgba, p, tint=(1.0, 1.0, 1.0)):
    """PageObjectDim.frag, in the project's Linear colour space. Returns sRGB 0..1 + alpha."""
    lin = srgb_to_linear(rgba[..., :3] / 255.0) * np.asarray(tint, dtype=np.float64)
    lum = lin.mean(axis=-1, keepdims=True)
    lin = lum + (lin - lum) * p['_Sat']
    f = p['_Darken'] - p['_HiExtra'] * smoothstep(p['_Hi0'], p['_Hi1'], lum)
    f = 1.0 - (1.0 - f) * smoothstep(p['_Lo0'], p['_Lo1'], lum)
    lin = lin * f * p['_Value']
    return linear_to_srgb(lin), rgba[..., 3] / 255.0


# ---------------------------------------------------------------- scene parsing

def guid_to_path():
    m = {}
    for meta in glob.glob(os.path.join(ROOT, 'Assets/Case3_Stickerdom/**/*.meta'), recursive=True):
        try:
            head = open(meta, errors='ignore').read(400)
        except OSError:
            continue
        g = re.search(r'^guid: ([0-9a-f]{32})', head, re.M)
        if g:
            m[g.group(1)] = meta[:-5]
    return m


def scene_renderers():
    """[{name, sprite_guid, material_guids, order, color}] for every SpriteRenderer."""
    txt = open(SCENE).read()
    docs = txt.split('--- !u!')
    names, out = {}, []
    for d in docs:
        h = re.match(r'(\d+) &(\d+)', d)
        if not h:
            continue
        cls, fid = h.group(1), h.group(2)
        if cls == '1':
            n = re.search(r'^  m_Name: (.*)$', d, re.M)
            names[fid] = n.group(1).strip() if n else '?'
        elif cls == '212':
            go = re.search(r'm_GameObject: \{fileID: (\d+)\}', d)
            order = re.search(r'm_SortingOrder: (-?\d+)', d)
            col = re.search(r'm_Color: \{r: ([-\d.]+), g: ([-\d.]+), b: ([-\d.]+), a: ([-\d.]+)\}', d)
            mats = re.findall(r'guid: ([0-9a-f]{32})', d.split('m_Materials:', 1)[1].split('m_Sprite:', 1)[0]) \
                if 'm_Materials:' in d and 'm_Sprite:' in d else []
            spr = re.search(r'm_Sprite: \{fileID: [-\d]+, guid: ([0-9a-f]{32})', d)
            out.append(dict(go=go.group(1) if go else None,
                            order=int(order.group(1)) if order else 0,
                            mats=mats,
                            sprite=spr.group(1) if spr else None,
                            color=tuple(float(col.group(i)) for i in (1, 2, 3, 4)) if col else (1, 1, 1, 1),
                            doc=d))
    for r in out:
        r['name'] = names.get(r['go'], '?')
    return out


# ---------------------------------------------------------------- gate: dim transform

def gate_dim():
    g2p = guid_to_path()
    dim_guids = {}
    for mat in sorted(glob.glob(MATGLOB)):
        g = re.search(r'^guid: ([0-9a-f]{32})', open(mat + '.meta').read(400), re.M).group(1)
        dim_guids[g] = mat

    rends = scene_renderers()
    fails, lines = [], []

    # ---- control C1 (instrument): the untransformed art must read as colourful. If the
    # saturation meter answered ~0 on lit art too, A1 below would be measuring nothing.
    lit_sats = []
    subjects = []
    for r in rends:
        hit = [g for g in r['mats'] if g in dim_guids]
        if not hit or not r['sprite'] or r['sprite'] not in g2p:
            continue
        png = g2p[r['sprite']]
        arr = np.array(Image.open(png).convert('RGBA'), dtype=np.float64)
        m = arr[..., 3] >= 128
        if m.sum() < 64:
            continue
        subjects.append((r, dim_guids[hit[0]], arr, m))
        lit_sats.append(float(hsv_saturation(arr[m][..., :3] / 255.0).mean()))
    if not subjects:
        return ['FATAL no scene object uses a Case3_PageObjectDim material'], []
    probe = np.zeros((4, 4, 4), dtype=np.float64)
    probe[..., 0] = 220.0
    probe[..., 3] = 255.0
    c1a = float(hsv_saturation(probe[..., :3].reshape(-1, 3) / 255.0).mean())
    c1b = float(np.median(lit_sats))
    lines.append('CONTROL C1 instrument: saturated probe reads %.3f (must be > 0.95); '
                 'median lit source art reads %.3f (must be > 0.30)' % (c1a, c1b))
    if c1a <= 0.95 or c1b <= 0.30:
        fails.append('C1: the saturation meter is not answering; A1 would be vacuous')

    # ---- control C2 (negative): the transform must not INVENT colour. A "fix" that simply
    # boosted saturation everywhere would pass A1 while being wrong.
    grey = np.zeros((8, 8, 4), dtype=np.float64)
    grey[..., :3] = 128.0
    grey[..., 3] = 255.0
    p_any = material_params(subjects[0][1])
    gs, _ = apply_dim(grey, p_any)
    c2 = float(hsv_saturation(gs.reshape(-1, 3)).max())
    lines.append('CONTROL C2 negative: neutral grey through the transform, saturation = %.4f (must be < 0.01)' % c2)
    if c2 >= 0.01:
        fails.append('C2: the transform invents saturation on neutral input')

    worst_sat, worst_name = 9.9, '-'
    best_sat, best_name = 0.0, '-'
    dim_L, lit_L = [], []
    flat_worst, flat_name = 0.0, '-'
    val_lo, val_hi = 9.9, 0.0

    for r, matpath, arr, m in subjects:
        p = material_params(matpath)
        out_srgb, _ = apply_dim(arr, p, tint=r['color'][:3])
        lit = arr[m][..., :3] / 255.0
        dim = out_srgb[m]

        s_lit = hsv_saturation(lit).mean()
        s_dim = hsv_saturation(dim).mean()
        keep = s_dim / max(1e-6, s_lit)

        lin_lit = srgb_to_linear(lit).mean(axis=-1)
        lin_dim = srgb_to_linear(dim).mean(axis=-1)
        vr = lin_dim.mean() / max(1e-9, lin_lit.mean())

        # flatness: the reference's ratio was the same on a dark ROI and a bright one, which
        # is what a plain multiply does and what a highlight rolloff does not.
        q = np.quantile(lin_lit, [1 / 3, 2 / 3])
        dark = lin_lit <= q[0]
        bright = lin_lit >= q[1]
        rd = lin_dim[dark].mean() / max(1e-9, lin_lit[dark].mean())
        rb = lin_dim[bright].mean() / max(1e-9, lin_lit[bright].mean())
        spread = abs(rd - rb) / max(1e-9, (rd + rb) / 2)

        dim_L.append((float(linear_to_srgb(lin_dim).mean() * 255), r['name']))
        lines.append('  %-22s %-34s satKeep=%.3f  linV=%.3f  flat=%.3f  dimL=%.1f'
                     % (r['name'], os.path.basename(matpath), keep, vr, spread,
                        linear_to_srgb(lin_dim).mean() * 255))
        # Retention is only measurable on art that HAS colour to keep; a near-neutral drawing
        # would report a noisy ratio and say nothing about the transform.
        if s_lit > 0.10:
            if keep < worst_sat:
                worst_sat, worst_name = keep, r['name']
            if keep > best_sat:
                best_sat, best_name = keep, r['name']
        if spread > flat_worst:
            flat_worst, flat_name = spread, r['name']
        val_lo, val_hi = min(val_lo, vr), max(val_hi, vr)

    # ---- playable stickers: untouched art, no dim material
    for r in rends:
        if r['name'] in ('Sticker_Cat', 'Sticker_Noodle', 'Sticker_Sweets') and r['sprite'] in g2p:
            arr = np.array(Image.open(g2p[r['sprite']]).convert('RGBA'), dtype=np.float64)
            m = arr[..., 3] >= 128
            lin = srgb_to_linear(arr[m][..., :3] / 255.0).mean(axis=-1)
            lit_L.append((float(linear_to_srgb(lin).mean() * 255), r['name']))

    lines.append('A1 saturation retention  worst = %.3f at %s, highest = %.3f (window %.2f..%.2f, reference 1.00)'
                 % (worst_sat, worst_name, best_sat, *REF_SAT_RETENTION))
    if worst_sat < REF_SAT_RETENTION[0]:
        fails.append('A1: the dim transform crushes saturation to %.0f%% at %s; the reference keeps 100%%'
                     % (worst_sat * 100, worst_name))
    if best_sat > REF_SAT_RETENTION[1]:
        fails.append('A1: the dim transform INVENTS saturation, %.0f%% at %s; the reference keeps 100%%'
                     % (best_sat * 100, best_name))

    lines.append('A2 linear value ratio    range = %.3f .. %.3f (window %.2f..%.2f, reference 0.231)'
                 % (val_lo, val_hi, *REF_LINEAR_VALUE_RATIO))
    if not (REF_LINEAR_VALUE_RATIO[0] <= val_lo and val_hi <= REF_LINEAR_VALUE_RATIO[1]):
        fails.append('A2: the dim value multiply is %.3f..%.3f, outside the reference window %.2f..%.2f'
                     % (val_lo, val_hi, *REF_LINEAR_VALUE_RATIO))

    lines.append('A3 multiply flatness     worst = %.3f at %s (limit %.2f)' % (flat_worst, flat_name, REF_FLATNESS_MAX))
    if flat_worst > REF_FLATNESS_MAX:
        fails.append('A3: the dim factor varies %.0f%% between dark and bright tones at %s; the reference is a flat multiply'
                     % (flat_worst * 100, flat_name))

    if dim_L and lit_L:
        hi = max(dim_L)
        lo = min(lit_L)
        lines.append('A4 affordance ordering   brightest dim %.1f (%s) < dimmest playable %.1f (%s)'
                     % (hi[0], hi[1], lo[0], lo[1]))
        if hi[0] >= lo[0]:
            fails.append('A4: dim item %s at L %.1f is not darker than playable %s at L %.1f'
                         % (hi[1], hi[0], lo[1], lo[0]))
    return fails, lines


# ---------------------------------------------------------------- gate: reward card name tab

CARDS = ['cat', 'noodle', 'sweets']


def gate_cards():
    """The name tab must be WRITTEN ON.

    The test is deliberately not "is there any dark pixel in the top band" - our cards
    already had a gold pill with a dark outline up there and would have passed that
    vacuously at 10.3%. It looks only at the strip the reference puts letterforms in
    (x 0.36..0.92 of the card, y 0.02..0.17), and only counts ink that is 70 levels below
    that strip's own median, which is what a letterform is and what a fill edge is not.
    On the unfixed cards that count is exactly 0.00%; on the reference crops it is
    15.6-26.4%. The reference crop is measured in the same run as the control: if the
    test stopped firing on art that is known to carry a name, it would be proving nothing.
    """
    fails, lines = [], []
    for k in CARDS:
        ours = os.path.join(ROOT, 'Assets/Case3_Stickerdom/Sprites/Cards/card_filled_%s.png' % k)
        ref = os.path.join(ROOT, 'Assets/Case3_Stickerdom/Textures/Reference/card_filled_%s.png' % k)
        frac = _name_ink(ours)
        rfrac = _name_ink(ref)
        lines.append('  card_filled_%-7s name ink = %.2f%%   (reference crop = %.2f%%, floor %.0f%%)'
                     % (k, frac * 100, rfrac * 100, NAME_INK_MIN * 100))
        if rfrac < NAME_INK_MIN:
            fails.append('CONTROL %s: the ink test does not fire on the reference crop either, '
                         'which carries a name; it is measuring nothing' % k)
        if frac < NAME_INK_MIN:
            fails.append('%s: the reward card has no name written on it (%.2f%% ink where the '
                         'reference reads %.2f%%)' % (k, frac * 100, rfrac * 100))
    return fails, lines


NAME_INK_MIN = 0.05
NAME_INK_DEPTH = 70.0


def _name_ink(path):
    a = np.array(Image.open(path).convert('RGBA'), dtype=np.float64)
    h, w = a.shape[:2]
    r = a[int(h * 0.02):int(h * 0.17), int(w * 0.36):int(w * 0.92)]
    op = r[..., 3] >= 128
    if op.sum() < 64:
        return 0.0
    lum = r[..., :3].mean(axis=-1)
    ink = op & (lum < np.median(lum[op]) - NAME_INK_DEPTH)
    return float(ink.sum()) / float(op.sum())


# ---------------------------------------------------------------- gate: sticker coverage

PPU = 200.0
COVER_THRESHOLD = 0.02


def _sprite_ppu(png):
    meta = png + '.meta'
    m = re.search(r'spritePixelsToUnits: ([\d.]+)', open(meta).read())
    return float(m.group(1)) if m else 100.0


def gate_coverage():
    g2p = guid_to_path()
    rends = [r for r in scene_renderers() if r['name'].startswith('Sticker_')]
    txt = open(SCENE).read()
    docs = txt.split('--- !u!')
    pos, parent, tf_of_go, go_of_tf = {}, {}, {}, {}
    for d in docs:
        h = re.match(r'(\d+) &(\d+)', d)
        if not h or h.group(1) != '4':
            continue
        fid = h.group(2)
        go = re.search(r'm_GameObject: \{fileID: (\d+)\}', d).group(1)
        p = re.search(r'm_LocalPosition: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}', d)
        s = re.search(r'm_LocalScale: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}', d)
        f = re.search(r'm_Father: \{fileID: (-?\d+)\}', d)
        tf_of_go[go] = fid
        go_of_tf[fid] = go
        pos[fid] = (float(p.group(1)), float(p.group(2)),
                    float(s.group(1)), float(s.group(2)))
        parent[fid] = f.group(1) if f else '0'

    def world(go):
        x = y = 0.0
        sx = sy = 1.0
        tf = tf_of_go.get(go)
        while tf and tf != '0' and tf in pos:
            px, py, psx, psy = pos[tf]
            x = px + x * psx
            y = py + y * psy
            sx *= psx
            sy *= psy
            tf = parent.get(tf, '0')
        return x, y, sx, sy

    masks = []
    for r in rends:
        if not r['sprite'] or r['sprite'] not in g2p:
            continue
        png = g2p[r['sprite']]
        a = np.array(Image.open(png).convert('RGBA'))
        ppu = _sprite_ppu(png)
        x, y, sx, sy = world(r['go'])
        hpx, wpx = a.shape[:2]
        # world extent of the sprite, pivot centred (all Case 3 stickers use a centre pivot)
        wu = wpx / ppu * sx
        hu = hpx / ppu * sy
        masks.append(dict(name=r['name'], order=r['order'],
                          alpha=(a[..., 3] >= 128),
                          x0=x - wu / 2, y0=y - hu / 2, x1=x + wu / 2, y1=y + hu / 2))

    fails, lines = [], []

    # The covered state is applied at runtime from a material the director holds, so the
    # wiring has to exist on disk or a covered sticker is untappable but still drawn lit.
    scene = open(SCENE).read()
    block = scene.split('Case3.Case3Director', 1)
    wired = len(block) > 1 and re.search(r'^  dimMaterial: \{fileID: \d+, guid: ([0-9a-f]{32})',
                                         block[1], re.M)
    dim_guid = re.search(r'^guid: ([0-9a-f]{32})',
                         open(os.path.join(ROOT, 'Assets/Case3_Stickerdom/Materials/'
                                                 'Case3_PageObjectDim.mat.meta')).read(400), re.M).group(1)
    if not wired:
        fails.append('Case3Director.dimMaterial is not wired in the scene; run Case3SceneSetup.Build')
    elif wired.group(1) != dim_guid:
        fails.append('Case3Director.dimMaterial points at %s, not Case3_PageObjectDim (%s)'
                     % (wired.group(1), dim_guid))
    else:
        lines.append('  dimMaterial wired to Case3_PageObjectDim in Stickerdom.unity')

    if len(masks) < 2:
        return fails + ['FATAL fewer than two stickers found in the scene'], lines

    # rasterise everything on one shared 200 px/unit grid
    gx0 = min(m['x0'] for m in masks)
    gy0 = min(m['y0'] for m in masks)
    gx1 = max(m['x1'] for m in masks)
    gy1 = max(m['y1'] for m in masks)
    W = int((gx1 - gx0) * PPU) + 2
    H = int((gy1 - gy0) * PPU) + 2
    for m in masks:
        grid = np.zeros((H, W), dtype=bool)
        ah, aw = m['alpha'].shape
        xs = np.clip(((np.arange(W) + 0.5) / PPU + gx0 - m['x0']) / max(1e-9, m['x1'] - m['x0']), 0, 1 - 1e-9)
        ys = np.clip(((np.arange(H) + 0.5) / PPU + gy0 - m['y0']) / max(1e-9, m['y1'] - m['y0']), 0, 1 - 1e-9)
        inx = (np.arange(W) + 0.5) / PPU + gx0
        iny = (np.arange(H) + 0.5) / PPU + gy0
        okx = (inx >= m['x0']) & (inx <= m['x1'])
        oky = (iny >= m['y0']) & (iny <= m['y1'])
        # sprite rows run bottom-up in world space
        src = m['alpha'][(( 1 - ys) * ah).astype(int).clip(0, ah - 1)][:, (xs * aw).astype(int).clip(0, aw - 1)]
        grid = src & oky[:, None] & okx[None, :]
        m['grid'] = grid
        m['area'] = int(grid.sum())

    worst = 0.0
    fracs = []
    for lo in masks:
        for hi in masks:
            if hi['order'] <= lo['order']:
                continue
            ov = int((lo['grid'] & hi['grid']).sum())
            frac = ov / max(1, lo['area'])
            fracs.append(frac)
            if ov:
                lines.append('  %s (ord %d) over %s (ord %d): %d px = %.2f%% of the lower sticker'
                             % (hi['name'], hi['order'], lo['name'], lo['order'], ov, frac * 100))
            if frac > worst:
                worst = frac
            if frac >= COVER_THRESHOLD:
                lines.append('    -> %s is COVERED (>= %.0f%%): it must be dim and untappable'
                             % (lo['name'], COVER_THRESHOLD * 100))
    # The threshold has to sit in a GAP, or it is a number someone picked. Every pair must be
    # either clearly below it or clearly above it; a pair landing near it means the population
    # that justified 2% no longer exists and the number has to be re-derived from the new page.
    below = [f for f in fracs if f < COVER_THRESHOLD]
    above = [f for f in fracs if f >= COVER_THRESHOLD]
    hi_below = max(below) if below else 0.0
    lo_above = min(above) if above else 1.0
    lines.append('worst coverage = %.2f%%; gap runs %.2f%% .. %.2f%% around the %.0f%% threshold'
                 % (worst * 100, hi_below * 100, lo_above * 100, COVER_THRESHOLD * 100))
    if not above:
        fails.append('CONTROL: no pair on this page is covered at all, so every covered-item '
                     'assertion downstream of this measurement is vacuous')
    if hi_below > COVER_THRESHOLD * 0.5 or (above and lo_above < COVER_THRESHOLD * 2):
        fails.append('the %.0f%% threshold no longer sits in a population gap (%.2f%% .. %.2f%%); '
                     're-derive it from this page instead of inheriting it'
                     % (COVER_THRESHOLD * 100, hi_below * 100, lo_above * 100))
    return fails, lines


def main():
    which = sys.argv[1] if len(sys.argv) > 1 else 'all'
    runs = {'dim': gate_dim, 'cards': gate_cards, 'coverage': gate_coverage}
    if which == 'all':
        order = ['dim', 'cards', 'coverage']
    else:
        order = [which]
    rc = 0
    for k in order:
        fails, lines = runs[k]()
        print('[case3-gate] ---- %s ----' % k)
        for l in lines:
            print(l)
        for f in fails:
            print('  FAIL ' + f)
        print('[case3-gate] %s %s' % (k.upper(), 'GREEN' if not fails else 'RED'))
        if fails:
            rc = 1
    sys.exit(rc)


if __name__ == '__main__':
    main()
