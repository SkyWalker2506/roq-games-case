#!/usr/bin/env python3
"""Take the BAKED white die-cut border off the strip stickers, so the shader can draw it.

WHY
    The five Sticker_* sheets carry their border painted into the PNG by
    tools/generate_case3_consistent_art.py::apply_die_cut_border_clean, which is

        blur the art's alpha by a Gaussian of sigma = 0.6 * padding, threshold it at 35/255

    A blurred-and-thresholded alpha is not an outset of the silhouette. The offset a
    threshold produces depends on the LOCAL SHAPE: it rounds convex corners off, bulges
    into concavities, and - the visible failure on the board - drops thin features
    outright. A 3 px chopstick blurred by sigma 8.4 peaks at 255*3/(8.4*sqrt(2*pi)) = 36
    grey levels, one level above the threshold of 35, so on sticker_noodle_blue the
    chopsticks and the steam wisps come out with no border at all while the bowl beside
    them has a fat one. That is the "blur efekt" reading, baked into the art where no
    shader can correct it.

WHAT THIS DOES
    Recovers the ART's own alpha - the alpha that went INTO the border generator - and
    writes it back as the sprite's alpha. RGB is left exactly as it is: the art's edge
    pixels are already composited over the border's warm white, which is precisely correct
    once the shader puts white paper back behind them. The canvas size and the art's
    position in it are untouched, so nothing in the scene moves.

    The art alpha is not guessed from the pixels. It is re-derived by re-running the
    generator's own drawing code with the border step captured instead of applied, which
    makes it exact rather than a reconstruction. sticker_cat_grey and sticker_noodle_blue
    are recolours that share their partner's silhouette byte for byte (asserted below), so
    they take the same recovered alpha.

POSITIVE CONTROL (--control)
    For every sticker: push the recovered art alpha back through the generator's real
    apply_die_cut_border_clean and compare the result with the alpha channel of the file
    on disk. If the recovered alpha is the true input, the shipped alpha comes back. The
    control also asserts the comparison has teeth, by showing that a deliberately wrong
    input (the art alpha eroded by two pixels) does NOT reproduce the shipped alpha.
"""
import importlib.util
import sys
import numpy as np
from PIL import Image

GEN = 'tools/generate_case3_consistent_art.py'
DIR = 'Assets/Case3_Stickerdom/Sprites/Stickers/'

# sticker file  <- the generator entry point that draws it, and the twin that shares its shape
SOURCES = {
    'sticker_cat':    'make_sticker_cat',
    'sticker_noodle': 'make_sticker_noodle',
    'sticker_sweets': 'make_sticker_sweets',
}
TWINS = {
    'sticker_cat_grey':    'sticker_cat',
    'sticker_noodle_blue': 'sticker_noodle',
}


def _load_gen():
    spec = importlib.util.spec_from_file_location('c3gen', GEN)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def recover_art_alpha():
    """{sticker name: (art alpha on the full canvas, padding)} straight out of the generator."""
    gen = _load_gen()
    real_border = gen.apply_die_cut_border_clean
    real_save = Image.Image.save
    got = {}

    try:
        Image.Image.save = lambda self, *a, **k: None
        for name, fn in SOURCES.items():
            box = {}

            def capture(art_img, padding=14, _box=box):
                _box['art'] = art_img.copy()
                _box['pad'] = padding
                return real_border(art_img, padding)

            gen.apply_die_cut_border_clean = capture
            getattr(gen, fn)()
            art, pad = box['art'], box['pad']
            canvas = np.zeros((art.height + pad * 2, art.width + pad * 2), np.uint8)
            canvas[pad:pad + art.height, pad:pad + art.width] = np.array(art)[..., 3]
            got[name] = (canvas, art, pad)
    finally:
        gen.apply_die_cut_border_clean = real_border
        Image.Image.save = real_save
    return got, real_border


def _shipped_alpha(name):
    return np.array(Image.open(DIR + name + '.png').convert('RGBA'))[..., 3]


def control():
    print("POSITIVE CONTROL - the recovered alpha must rebuild the shipped file's alpha")
    got, border = recover_art_alpha()
    ok = True
    for name, (canvas, art, pad) in got.items():
        shipped = _shipped_alpha(name)
        if shipped.shape != canvas.shape:
            print(f"  {name}: canvas {canvas.shape} != shipped {shipped.shape}  FAIL"); ok = False; continue
        rebuilt = np.array(border(art, pad))[..., 3]
        agree = float((np.abs(rebuilt.astype(int) - shipped.astype(int)) <= 2).mean())

        # teeth: an input that is NOT the true art must fail the same comparison
        wrong = art.copy()
        wa = np.array(wrong)
        m = wa[..., 3]
        eroded = np.minimum.reduce([np.roll(m, (dy, dx), (0, 1))
                                    for dy in (-2, 0, 2) for dx in (-2, 0, 2)])
        wa[..., 3] = eroded
        wrong_rebuilt = np.array(border(Image.fromarray(wa, 'RGBA'), pad))[..., 3]
        wrong_agree = float((np.abs(wrong_rebuilt.astype(int) - shipped.astype(int)) <= 2).mean())

        good = agree >= 0.999 and wrong_agree < 0.99
        ok &= good
        print(f"  {name:22s} rebuilt==shipped {agree*100:6.3f}%   "
              f"eroded-by-2 control {wrong_agree*100:6.3f}%   {'PASS' if good else 'FAIL'}")

    for twin, of in TWINS.items():
        same = np.array_equal(_shipped_alpha(twin), _shipped_alpha(of))
        ok &= same
        print(f"  {twin:22s} shares {of}'s silhouette byte for byte: {same}")
    print("  CONTROL " + ("PASS" if ok else "FAIL"))
    return ok


def apply(write=True):
    got, _ = recover_art_alpha()
    alphas = {n: v[0] for n, v in got.items()}
    for twin, of in TWINS.items():
        alphas[twin] = alphas[of]
    for name in list(SOURCES) + list(TWINS):
        p = DIR + name + '.png'
        a = np.array(Image.open(p).convert('RGBA'))
        before = int((a[..., 3] >= 128).sum())
        a[..., 3] = alphas[name]
        after = int((a[..., 3] >= 128).sum())
        if write:
            Image.fromarray(a, 'RGBA').save(p)
        print(f"  {name:22s} opaque {before:6d} -> {after:6d}  "
              f"({100.0*(before-after)/max(1,before):5.1f}% of it was baked border)")


if __name__ == '__main__':
    args = sys.argv[1:]
    if '--control' in args and not control():
        sys.exit(1)
    if '--apply' in args:
        print("STRIPPING baked die-cut borders")
        apply(True)
    if '--dry' in args:
        print("DRY RUN")
        apply(False)
