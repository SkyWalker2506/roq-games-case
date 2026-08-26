import os, sys, math
from PIL import Image, ImageDraw, ImageFilter, ImageFont
import numpy as np

def _edt(mask):
    """Exact Euclidean distance from every False pixel to the nearest True one."""
    def d1(f):
        n = len(f); d = np.empty(n); v = np.zeros(n, dtype=int); z = np.empty(n + 1)
        k = 0; z[0] = -np.inf; z[1] = np.inf
        for q in range(1, n):
            s_ = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2.0 * q - 2.0 * v[k])
            while s_ <= z[k]:
                k -= 1
                s_ = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2.0 * q - 2.0 * v[k])
            k += 1; v[k] = q; z[k] = s_; z[k + 1] = np.inf
        k = 0
        for q in range(n):
            while z[k + 1] < q: k += 1
            d[q] = (q - v[k]) ** 2 + f[v[k]]
        return d
    f = np.where(mask, 0.0, 1e12)
    out = np.empty_like(f)
    for y in range(f.shape[0]): out[y] = d1(f[y])
    for x in range(f.shape[1]): out[:, x] = d1(out[:, x])
    return np.sqrt(out)


def die_cut_outset(art_img, width=9.0, aa=1.5, rim=(255, 255, 255)):
    """A REAL die cut: the art's hard silhouette, outset by a constant `width` pixels,
    with an `aa`-pixel antialiased edge. Nothing else.

    The old apply_die_cut_border_clean did threshold(gaussian_blur(alpha)) instead. That is
    not an outset - the offset a threshold produces depends on the LOCAL SHAPE, so it
    rounded convex corners off, bulged into concavities and deleted thin features (a 3 px
    chopstick blurred by sigma 8.4 peaks at 36/255 against a threshold of 35). Distance is
    the only thing that gives a constant width, so distance is what this measures.

    The page's rim comes from Case3/PageObjectRim at render time, on the same rule and the
    same measured 9.0 px / 1.5 px numbers. This function exists for the one place a rim has
    to be BAKED - compositing a sticker into a reward card's art, where there is no
    renderer to hang a rim child on.
    """
    pad = int(np.ceil(width + aa + 2))
    w, h = art_img.size
    canvas = Image.new('RGBA', (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    canvas.paste(art_img, (pad, pad), art_img)
    a = np.array(canvas)
    solid = a[..., 3] >= 128
    d = _edt(solid)
    cover = np.clip((width + aa * 0.5 - d) / aa, 0.0, 1.0)
    base = np.zeros(a.shape, np.uint8)
    base[..., 0], base[..., 1], base[..., 2] = rim
    base[..., 3] = (cover * 255).astype(np.uint8)
    out = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    out.alpha_composite(Image.fromarray(base, 'RGBA'))
    out.alpha_composite(canvas)
    return out


def apply_die_cut_border_clean(art_img, padding=14):
    """PADDING ONLY - the border is no longer painted into the sticker sheets.

    Every Sticker_* renderer carries a Rim child running Case3/PageObjectRim, which draws
    the die cut as a constant-width outset of the silhouette at render time. A border baked
    in here as well would be drawn twice, and the baked one is the worse of the two: see
    die_cut_outset above for why threshold(blur(alpha)) is not an outset. The padding is
    kept at its original 14 px because it is the transparent margin the shader dilates into,
    and because changing the canvas size would move every sticker on the page.
    """
    w, h = art_img.size
    out = Image.new('RGBA', (w + padding * 2, h + padding * 2), (0, 0, 0, 0))
    out.paste(art_img, (padding, padding), art_img)
    return out


# 1. PageBackground.png (1024x1024)
def make_page_background():
    w, h = 1024, 1024
    arr = np.zeros((h, w, 4), dtype=np.uint8)
    for y in range(h):
        for x in range(w):
            dx = (x - w*0.45) / (w*0.5)
            dy = (y - h*0.35) / (h*0.5)
            dist = math.sqrt(dx*dx + dy*dy)
            grain = math.sin(y * 0.08 + math.sin(x * 0.02) * 2.0) * 3.0
            grain += math.sin(y * 0.25) * 1.5
            
            r = np.clip(176 - dist * 24 + grain, 0, 255)
            g = np.clip(136 - dist * 22 + grain * 0.8, 0, 255)
            b = np.clip(110 - dist * 20 + grain * 0.6, 0, 255)
            arr[y, x] = [int(r), int(g), int(b), 255]
    Image.fromarray(arr, 'RGBA').save('Assets/Case3_Stickerdom/Sprites/Background/PageBackground.png')

# 2. StickerSheetBackground.png (535x837)
def make_sticker_sheet_background():
    w, h = 535, 837
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    margin_l, margin_r, margin_t, margin_b, radius = 30, w - 15, 15, h - 15, 28
    
    paper = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    pdraw = ImageDraw.Draw(paper)
    pdraw.rounded_rectangle([margin_l, margin_t, margin_r, margin_b], radius=radius, fill=(192, 162, 130, 255), outline=(160, 128, 96, 255), width=3)
    
    parr = np.array(paper)
    for y in range(margin_t, margin_b):
        t = (y - margin_t) / (margin_b - margin_t)
        for x in range(margin_l, margin_r):
            if parr[y, x, 3] > 0:
                crease = math.exp(-(x - margin_l) / 35.0) * 16.0
                parr[y, x, 0] = np.clip(195 - t * 10 - crease, 0, 255)
                parr[y, x, 1] = np.clip(165 - t * 12 - crease * 0.9, 0, 255)
                parr[y, x, 2] = np.clip(135 - t * 14 - crease * 0.8, 0, 255)
    paper = Image.fromarray(parr, 'RGBA')
    img = Image.alpha_composite(img, paper)
    draw = ImageDraw.Draw(img)
    
    for y_hole in range(60, h - 60, 55):
        draw.ellipse([margin_l + 6, y_hole, margin_l + 20, y_hole + 14], fill=(48, 32, 18, 240), outline=(180, 150, 120, 255), width=2)
    
    ink = (130, 95, 60, 220)
    fill_soft = (178, 146, 114, 200)
    
    draw.rounded_rectangle([330, 90, 420, 190], radius=12, fill=fill_soft, outline=ink, width=3)
    draw.rounded_rectangle([345, 75, 405, 95], radius=6, fill=(160, 126, 92, 255), outline=ink, width=2)
    draw.line([345, 135, 405, 135], fill=ink, width=2)
    draw.line([350, 150, 400, 150], fill=ink, width=2)
    
    draw.rounded_rectangle([75, 340, 185, 460], radius=10, fill=fill_soft, outline=ink, width=3)
    draw.line([85, 375, 175, 375], fill=ink, width=2)
    draw.ellipse([110, 395, 150, 435], fill=(170, 136, 100, 255), outline=ink, width=2)
    draw.line([95, 445, 165, 445], fill=ink, width=2)
    
    draw.ellipse([340, 560, 440, 660], fill=fill_soft, outline=ink, width=3)
    draw.ellipse([330, 550, 365, 585], fill=fill_soft, outline=ink, width=3)
    draw.ellipse([415, 550, 450, 585], fill=fill_soft, outline=ink, width=3)
    draw.ellipse([365, 595, 375, 605], fill=ink)
    draw.ellipse([405, 595, 415, 605], fill=ink)
    draw.ellipse([375, 610, 405, 635], fill=(170, 140, 104, 255), outline=ink, width=2)
    draw.ellipse([386, 615, 394, 622], fill=ink)
    draw.rounded_rectangle([345, 650, 435, 750], radius=20, fill=fill_soft, outline=ink, width=3)
    draw.ellipse([325, 665, 360, 720], fill=fill_soft, outline=ink, width=3)
    draw.ellipse([420, 665, 455, 720], fill=fill_soft, outline=ink, width=3)

    img.save('Assets/Case3_Stickerdom/Sprites/StickerSheetBackground.png')

# 3. DeckBackground.png (276x356)
def make_deck_background():
    w, h = 276, 356
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle([8, 8, w - 8, h - 14], radius=22, fill=(188, 150, 118, 255), outline=(155, 120, 88, 255), width=3)
    draw.rounded_rectangle([18, 18, w - 18, h - 24], radius=16, fill=(205, 172, 138, 255), outline=(172, 138, 105, 255), width=2)
    img.save('Assets/Case3_Stickerdom/Sprites/DeckBackground.png')

# 4. StickerBackground.png (170x223)
def make_sticker_background():
    w, h = 170, 223
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle([6, 6, w - 6, h - 6], radius=18, fill=(188, 158, 125, 190), outline=(155, 122, 90, 220), width=2)
    acc_c = (138, 105, 72, 220)
    draw.line([18, 18, 32, 18], fill=acc_c, width=2)
    draw.line([18, 18, 18, 32], fill=acc_c, width=2)
    draw.line([w - 18, 18, w - 32, 18], fill=acc_c, width=2)
    draw.line([w - 18, 18, w - 18, 32], fill=acc_c, width=2)
    draw.line([18, h - 18, 32, h - 18], fill=acc_c, width=2)
    draw.line([18, h - 18, 18, h - 32], fill=acc_c, width=2)
    draw.line([w - 18, h - 18, w - 32, h - 18], fill=acc_c, width=2)
    draw.line([w - 18, h - 18, w - 18, h - 32], fill=acc_c, width=2)
    img.save('Assets/Case3_Stickerdom/Sprites/StickerBackground.png')

# 5. sticker_cat.png
def make_sticker_cat():
    w, h = 300, 360
    art = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(art)
    line_col = (72, 44, 28, 255)
    
    draw.ellipse([50, 130, 250, 330], fill=(225, 145, 78, 255), outline=line_col, width=4)
    draw.ellipse([90, 165, 210, 305], fill=(245, 235, 220, 255), outline=line_col, width=3)
    draw.ellipse([60, 45, 240, 205], fill=(228, 148, 80, 255), outline=line_col, width=4)
    
    stripe_col = (185, 102, 40, 255)
    draw.line([140, 55, 140, 85], fill=stripe_col, width=5)
    draw.line([120, 60, 125, 90], fill=stripe_col, width=5)
    draw.line([160, 60, 155, 90], fill=stripe_col, width=5)
    draw.line([55, 210, 95, 220], fill=stripe_col, width=6)
    draw.line([245, 210, 205, 220], fill=stripe_col, width=6)
    draw.line([58, 250, 98, 260], fill=stripe_col, width=6)
    draw.line([242, 250, 202, 260], fill=stripe_col, width=6)
    
    draw.polygon([(75, 75), (95, 15), (135, 60)], fill=(228, 148, 80, 255), outline=line_col)
    draw.polygon([(85, 70), (100, 28), (128, 62)], fill=(238, 178, 178, 255))
    draw.polygon([(225, 75), (205, 15), (165, 60)], fill=(228, 148, 80, 255), outline=line_col)
    draw.polygon([(215, 70), (200, 28), (172, 62)], fill=(238, 178, 178, 255))
    
    draw.arc([95, 105, 125, 125], start=190, end=350, fill=line_col, width=4)
    draw.arc([175, 105, 205, 125], start=190, end=350, fill=line_col, width=4)
    draw.ellipse([80, 120, 108, 140], fill=(242, 165, 155, 180))
    draw.ellipse([192, 120, 220, 140], fill=(242, 165, 155, 180))
    
    draw.polygon([(145, 125), (155, 125), (150, 133)], fill=(225, 120, 130, 255))
    draw.arc([136, 130, 150, 145], start=20, end=170, fill=line_col, width=3)
    draw.arc([150, 130, 164, 145], start=10, end=160, fill=line_col, width=3)
    
    draw.line([60, 128, 90, 133], fill=line_col, width=3)
    draw.line([58, 140, 90, 140], fill=line_col, width=3)
    draw.line([240, 128, 210, 133], fill=line_col, width=3)
    draw.line([242, 140, 210, 140], fill=line_col, width=3)
    
    draw.ellipse([95, 290, 140, 325], fill=(245, 235, 220, 255), outline=line_col, width=4)
    draw.ellipse([160, 290, 205, 325], fill=(245, 235, 220, 255), outline=line_col, width=4)
    draw.arc([190, 260, 280, 340], start=280, end=140, fill=(225, 145, 78, 255), width=24)
    draw.arc([190, 260, 280, 340], start=280, end=140, fill=line_col, width=3)
    
    sticker = apply_die_cut_border_clean(art)
    sticker.save('Assets/Case3_Stickerdom/Sprites/Stickers/sticker_cat.png')

# 6. sticker_noodle.png
def make_sticker_noodle():
    w, h = 330, 270
    art = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(art)
    line_col = (72, 44, 28, 255)
    
    draw.chord([40, 110, 290, 245], start=0, end=180, fill=(215, 135, 82, 255), outline=line_col, width=4)
    draw.rounded_rectangle([125, 235, 205, 255], radius=6, fill=(230, 212, 190, 255), outline=line_col, width=3)
    draw.ellipse([45, 90, 285, 145], fill=(225, 158, 68, 255), outline=line_col, width=4)
    
    noodle_col = (248, 208, 98, 255)
    for ny in range(105, 130, 6):
        draw.arc([60, ny, 270, ny+18], start=0, end=180, fill=noodle_col, width=4)
    
    draw.ellipse([80, 100, 140, 145], fill=(248, 242, 230, 255), outline=line_col, width=3)
    draw.ellipse([92, 110, 128, 138], fill=(245, 120, 28, 255))
    draw.ellipse([98, 114, 112, 124], fill=(250, 195, 92, 220))
    
    draw.ellipse([150, 95, 195, 135], fill=(248, 238, 230, 255), outline=line_col, width=3)
    draw.arc([160, 103, 185, 127], start=45, end=315, fill=(232, 98, 142, 255), width=4)
    
    draw.polygon([(215, 65), (255, 55), (265, 115), (225, 125)], fill=(35, 58, 38, 255), outline=line_col)
    
    onion_col = (98, 172, 55, 255)
    draw.ellipse([195, 115, 210, 125], fill=onion_col, outline=line_col, width=2)
    draw.ellipse([140, 125, 155, 135], fill=onion_col, outline=line_col, width=2)
    draw.ellipse([175, 125, 190, 135], fill=onion_col, outline=line_col, width=2)
    
    draw.line([30, 75, 290, 105], fill=(188, 125, 65, 255), width=5)
    draw.line([30, 75, 290, 105], fill=line_col, width=1)
    draw.line([30, 85, 290, 112], fill=(188, 125, 65, 255), width=5)
    draw.line([30, 85, 290, 112], fill=line_col, width=1)
    
    steam_col = (248, 242, 235, 180)
    draw.arc([110, 25, 140, 75], start=180, end=360, fill=steam_col, width=3)
    draw.arc([165, 15, 195, 65], start=180, end=360, fill=steam_col, width=3)
    draw.arc([220, 25, 250, 75], start=180, end=360, fill=steam_col, width=3)
    
    sticker = apply_die_cut_border_clean(art)
    sticker.save('Assets/Case3_Stickerdom/Sprites/Stickers/sticker_noodle.png')

# 7. sticker_sweets.png
def make_sticker_sweets():
    w, h = 260, 300
    art = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(art)
    line_col = (72, 44, 28, 255)
    
    draw.arc([70, 30, 190, 150], start=180, end=360, fill=(245, 235, 218, 255), width=28)
    draw.arc([70, 30, 190, 150], start=180, end=360, fill=line_col, width=3)
    draw.rectangle([162, 90, 190, 250], fill=(245, 235, 218, 255), outline=line_col, width=3)
    draw.rectangle([70, 90, 98, 140], fill=(245, 235, 218, 255), outline=line_col, width=3)
    
    stripe_col = (225, 78, 62, 255)
    for sy in range(100, 240, 24):
        draw.polygon([(162, sy), (190, sy-14), (190, sy-4), (162, sy+10)], fill=stripe_col)
    
    mint_col = (195, 155, 65, 255)
    for sy in range(112, 240, 24):
        draw.polygon([(162, sy), (190, sy-14), (190, sy-8), (162, sy+6)], fill=mint_col)
        
    bow_col = (235, 125, 95, 255)
    draw.polygon([(176, 150), (120, 125), (125, 175)], fill=bow_col, outline=line_col, width=3)
    draw.polygon([(176, 150), (232, 125), (227, 175)], fill=bow_col, outline=line_col, width=3)
    draw.ellipse([164, 138, 188, 162], fill=(245, 155, 125, 255), outline=line_col, width=3)
    draw.polygon([(170, 160), (150, 215), (165, 205), (174, 160)], fill=bow_col, outline=line_col, width=2)
    draw.polygon([(182, 160), (202, 215), (187, 205), (178, 160)], fill=bow_col, outline=line_col, width=2)
    
    draw.ellipse([45, 55, 55, 65], fill=(245, 195, 65, 255))
    draw.ellipse([215, 65, 225, 75], fill=(245, 195, 65, 255))
    draw.ellipse([65, 215, 75, 225], fill=(245, 195, 65, 255))
    
    sticker = apply_die_cut_border_clean(art)
    sticker.save('Assets/Case3_Stickerdom/Sprites/Stickers/sticker_sweets.png')

# 8. Reward Cards

# The reference pins a NAME TAB across the top edge of every filled reward card - "Cat",
# "Noodle", "Sweets" in bold rounded sans, title case, held by a paperclip at the left.
# Measured off Textures/Reference/card_filled_*.png (244x292 crops):
#   tab    x 10..232 (0.91 of card width), y 8..48 (0.137 of card height), flush to the top
#   ink    a darker shade of the tab's own hue, not black
#   clip   same hue as the ink, overlapping the tab's left end and standing proud of it
# It is part of the CARD ART, not a HUD element and not a scene object: it fades in with
# the card because it is drawn into the card, which is what the reference does.
NAME_TAB = {
    # key: (display name, tab fill, tab fill bottom, ink, clip line)
    'cat':    ('Cat',    (219, 230, 169), (205, 219, 150), (74, 118, 40),  (86, 130, 52)),
    'noodle': ('Noodle', (243, 245, 172), (229, 233, 150), (120, 150, 62), (128, 158, 70)),
    'sweets': ('Sweets', (247, 205, 243), (232, 186, 232), (126, 87, 166), (140, 104, 176)),
}

FONT_CANDIDATES = [
    '/System/Library/Fonts/Supplemental/Arial Rounded Bold.ttf',
    'Assets/TextMesh Pro/Fonts/LiberationSans.ttf',
]


def _name_font(size):
    for path in FONT_CANDIDATES:
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    raise RuntimeError('no name-tab font found; tried ' + ', '.join(FONT_CANDIDATES))


def _draw_paperclip(img, box, line_col):
    """Paperclip over the tab's left end.

    Drawn as a WIRE, not as two outlines: the two open loops are rasterised into one mask
    at 4x, the mask is filled with the darker line colour, and an eroded copy is filled
    with a pale core. That is what the reference's clip is - a pale wire with a dark
    edge - and it survives being scaled down to card size, which a hairline outline does not.
    """
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    S = 4
    W, H = w * S, h * S
    pad = int(W * 0.35)
    canvas = Image.new('L', (W + pad * 2, H + pad * 2), 0)
    d = ImageDraw.Draw(canvas)
    ox, oy = pad, pad
    # One continuous wire, stroked with round joins: down the right side, a U at the
    # bottom, up the left side, a small U at the top, and back down the middle. A
    # paperclip is a thin wire, so the loops are narrow; the clip's bounding box is
    # wider than the loops only because the whole thing is tilted.
    lw = int(W * 0.50)
    lx = ox + (W - lw) // 2
    stroke = max(3, int(lw * 0.26))

    def P(fx, fy):
        return (lx + fx * lw, oy + fy * H)

    d.line([P(0.80, 0.14), P(0.80, 0.84), P(0.20, 0.84), P(0.20, 0.10),
            P(0.52, 0.10), P(0.52, 0.66)],
           fill=255, width=stroke, joint='curve')

    canvas = canvas.rotate(-22, resample=Image.BICUBIC, expand=False, center=(ox + W // 2, oy + H // 2))
    core = canvas.filter(ImageFilter.MinFilter(21))

    dark = line_col
    pale = tuple(min(255, int(c + (255 - c) * 0.55)) for c in line_col)
    rgba = np.zeros((canvas.size[1], canvas.size[0], 4), dtype=np.uint8)
    m = np.array(canvas)
    c = np.array(core)
    rgba[..., 0] = np.where(c > 128, pale[0], dark[0])
    rgba[..., 1] = np.where(c > 128, pale[1], dark[1])
    rgba[..., 2] = np.where(c > 128, pale[2], dark[2])
    rgba[..., 3] = m
    clip = Image.fromarray(rgba, 'RGBA').resize(
        ((W + pad * 2) // S, (H + pad * 2) // S), Image.LANCZOS)
    img.paste(clip, (x0 - pad // S, y0 - pad // S), clip)


def _draw_name_tab(img, key, w, h):
    name, fill_top, fill_bot, ink, clip = NAME_TAB[key]

    # geometry, in the reference's own proportions
    tx0, tx1 = int(w * 0.041), int(w * 0.951)
    ty0, ty1 = int(h * 0.027), int(h * 0.164)
    radius = int((ty1 - ty0) * 0.34)

    tab = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    td = ImageDraw.Draw(tab)
    td.rounded_rectangle([tx0, ty0, tx1, ty1], radius=radius,
                         fill=fill_top + (255,), outline=tuple(int(c * 0.80) for c in fill_bot) + (255,), width=2)
    # vertical shade, brighter at the top the way the reference's tab reads
    arr = np.array(tab).astype(np.float32)
    for y in range(ty0, ty1 + 1):
        t = (y - ty0) / max(1, ty1 - ty0)
        row = arr[y]
        m = row[:, 3] > 0
        for c in range(3):
            row[m, c] = row[m, c] * (1.0 - 0.14 * t)
    tab = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), 'RGBA')
    img.paste(tab, (0, 0), tab)

    # the name: as large as fits between the clip and the tab's right end
    clip_w = int(w * 0.185)
    text_l = tx0 + clip_w
    avail_w = (tx1 - text_l) * 0.86
    avail_h = (ty1 - ty0) * 0.62
    size = 8
    font = _name_font(size)
    while size < 96:
        probe = _name_font(size + 1)
        box = probe.getbbox(name)
        if (box[2] - box[0]) > avail_w or (box[3] - box[1]) > avail_h:
            break
        size += 1
        font = probe
    box = font.getbbox(name)
    tw, th = box[2] - box[0], box[3] - box[1]
    cx = (text_l + tx1) / 2.0
    cy = (ty0 + ty1) / 2.0
    d = ImageDraw.Draw(img)
    d.text((cx - tw / 2 - box[0], cy - th / 2 - box[1]), name, font=font, fill=ink + (255,))

    # clip box in the reference's proportions: 0.18 of the card wide, 0.157 tall,
    # sitting proud of the tab's top-left corner
    cw, ch = int(w * 0.181), int(h * 0.157)
    cx0, cy0 = tx0 - int(w * 0.018), ty0 - int(h * 0.014)
    _draw_paperclip(img, (cx0, cy0, cx0 + cw, cy0 + ch), clip)


# ---- the reward card's panel, and how much of it the subject is supposed to fill.
#
# MEASURED off the reference crops with tools/case3_card_metrics.py, which finds the panel
# as the bounding box of the lilac grid paper (clamped below the name tab, inset 3 px) and
# the subject as the largest blob that is not that paper:
#
#                  bbox fill   subject w    subject h   margins L / R / T / B  (% of panel)
#   ref cat            83.5%       89.7%        93.1%      4.9  5.4  6.9  0.0
#   ref noodle        100.0%      100.0%       100.0%      0.0  0.0  0.0  0.0
#   ref sweets         52.1%       55.5%        94.0%     23.2 21.4  6.0  0.0
#   ref MEDIAN         83.5%       89.7%        94.0%      4.9  5.4  6.0  0.0
#
#   ours, before       20.8%       47.1%        49.4%     29.8 23.1 15.2 35.4
#
# Two things fall straight out of that table. The subject is scaled until it hits ~90% of
# the panel's width or ~94% of its height, whichever binds first - the candy cane is only
# 55% wide because it is a narrow object that ran out of height, not because it was drawn
# small. And on ALL THREE cards the bottom margin is 0.0: the reference lets the subject run
# off the bottom edge of the panel rather than float above it. Ours did neither: it sat at
# half size with an even margin all round, plus an empty inset band across the bottom that
# the reference does not have at all. The band is gone with this change and the counter has
# moved to where the reference prints it, over the bottom right of the art.
CARD_W, CARD_H = 276, 356
CARD_OUTER = (8, 8, CARD_W - 8, CARD_H - 14)     # the card's own rounded edge
CARD_INNER = (18, 18, CARD_W - 18, CARD_H - 24)  # the panel's frame
PANEL = (24, 64, 252, 326)                       # the opening: inside the frame, below the tab
PANEL_FILL_W = 0.90                              # reference median subject width
PANEL_FILL_H = 0.94                              # reference median subject height
CARD_RIM_PX = 9.0                                # the same die cut the page wears
CARD_RIM_AA = 1.5


def make_reward_card(name, art_type, bg_color):
    """A FRAME, not a picture. The interior is transparent on purpose.

    The director used to hide the flown sheet and let this texture supply the subject, which
    is why the first item of a kind always looked like the same object: fourteen entries share
    three of these cards, so six different Noodle items all became one bowl. The sheet now
    stays visible and IS the card's content, so a baked subject here would be a second,
    wrong picture sitting under the real one - and after the fill-ratio pass it would be a
    LARGE wrong picture, with candy-cane edges sticking out around a landed chocolate.

    So this draws the card's frame and its name tab and nothing else. The panel's background
    comes from the Empty_<key> card underneath (sorting order 200 against this one's 600) and
    the subject comes from the sheet on top of it. `bg_color` is kept in the signature because
    the three cards are still authored as three distinct cards; it is no longer painted.

    The counter is not baked either - it is world-space TextMeshPro parented to the card, set
    where the reference prints it, over the bottom right of the art.
    """
    w, h = CARD_W, CARD_H
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle(list(CARD_OUTER), radius=22, fill=(210, 178, 145, 255),
                           outline=(175, 140, 108, 255), width=3)
    draw.rounded_rectangle(list(CARD_INNER), radius=16, fill=(0, 0, 0, 0),
                           outline=(195, 165, 132, 255), width=2)

    # punch the opening clear, inside the inner frame's own 2 px stroke
    hole = Image.new('L', (w, h), 0)
    ix0, iy0, ix1, iy1 = CARD_INNER
    ImageDraw.Draw(hole).rounded_rectangle([ix0 + 2, iy0 + 2, ix1 - 2, iy1 - 2], radius=14, fill=255)
    a = np.array(img)
    a[..., 3] = np.where(np.array(hole) > 127, 0, a[..., 3])
    img = Image.fromarray(a, 'RGBA')

    # the name tab goes on last: in the reference it is pinned OVER the card, and its
    # paperclip stands proud of the card's own top edge.
    _draw_name_tab(img, art_type, w, h)
    img.save(f'Assets/Case3_Stickerdom/Sprites/Cards/{name}.png')


def make_reward_cards():
    """Cards-only entry point.

    The three filled cards are the only thing the name tab touches, and the sticker art
    they paste in is an INPUT to them. Regenerating the whole sheet to change a card
    would redraw sticker_cat/noodle/sweets, which the page, the ghosts, the shadows and
    the peel all measure against. So this is the entry point to use.
    """
    make_reward_card('card_filled_cat', 'cat', (225, 202, 180, 255))
    make_reward_card('card_filled_noodle', 'noodle', (225, 198, 172, 255))
    make_reward_card('card_filled_sweets', 'sweets', (228, 198, 178, 255))
    print('REWARD CARDS REGENERATED (name tab only; sticker art untouched)')


# 9. PurplePackage.png
def make_purple_package():
    w, h = 206, 282
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle([8, 8, w - 8, h - 8], radius=20, fill=(185, 142, 172, 255), outline=(145, 108, 138, 255), width=3)
    draw.rounded_rectangle([18, 18, w - 18, h - 18], radius=14, fill=(202, 160, 188, 255), outline=(160, 122, 150, 255), width=2)
    draw.rectangle([w//2 - 14, 18, w//2 + 14, h - 18], fill=(245, 192, 60, 255))
    draw.rectangle([18, h//2 - 14, w - 18, h//2 + 14], fill=(245, 192, 60, 255))
    draw.ellipse([w//2 - 18, h//2 - 18, w//2 + 18, h//2 + 18], fill=(252, 210, 75, 255), outline=(205, 155, 30, 255), width=2)
    img.save('Assets/Case3_Stickerdom/Sprites/PurplePackage.png')

# 10. Tool Icons
def make_tool_icons():
    r_img = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
    rdraw = ImageDraw.Draw(r_img)
    rdraw.ellipse([16, 16, 240, 240], fill=(215, 168, 118, 255), outline=(175, 130, 85, 255), width=6)
    rdraw.arc([55, 55, 201, 201], start=30, end=150, fill=(250, 245, 235, 255), width=16)
    rdraw.arc([55, 55, 201, 201], start=210, end=330, fill=(250, 245, 235, 255), width=16)
    r_img.save('Assets/Case3_Stickerdom/Sprites/recycle.png')
    
    q_img = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
    qdraw = ImageDraw.Draw(q_img)
    qdraw.rounded_rectangle([16, 16, 240, 240], radius=40, fill=(188, 178, 128, 255), outline=(148, 138, 92, 255), width=6)
    qdraw.rounded_rectangle([45, 45, 120, 120], radius=16, fill=(248, 245, 235, 255))
    qdraw.rounded_rectangle([136, 45, 211, 120], radius=16, fill=(248, 245, 235, 255))
    qdraw.rounded_rectangle([45, 136, 120, 211], radius=16, fill=(248, 245, 235, 255))
    qdraw.rounded_rectangle([136, 136, 211, 211], radius=16, fill=(248, 245, 235, 255))
    q_img.save('Assets/Case3_Stickerdom/Sprites/quad_free.png')
    
    d_img = Image.new('RGBA', (256, 358), (0, 0, 0, 0))
    ddraw = ImageDraw.Draw(d_img)
    ddraw.rounded_rectangle([16, 16, 240, 342], radius=32, fill=(212, 185, 150, 255), outline=(180, 148, 112, 255), width=5)
    ddraw.ellipse([50, 100, 206, 256], fill=(235, 178, 55, 255), outline=(195, 138, 25, 255), width=4)
    ddraw.line([85, 180, 120, 215], fill=(255, 255, 255, 255), width=14)
    ddraw.line([120, 215, 175, 145], fill=(255, 255, 255, 255), width=14)
    d_img.save('Assets/Case3_Stickerdom/Sprites/done_ring.png')

# 11. Authored UI Elements
def make_ui_elements():
    b_img = Image.new('RGBA', (256, 128), (0, 0, 0, 0))
    bdraw = ImageDraw.Draw(b_img)
    bdraw.rounded_rectangle([8, 12, 248, 116], radius=52, fill=(238, 185, 55, 255), outline=(192, 138, 28, 255), width=4)
    bdraw.rounded_rectangle([20, 24, 236, 104], radius=40, fill=(248, 205, 78, 255))
    star_col = (255, 255, 255, 255)
    bdraw.ellipse([45, 48, 75, 78], fill=star_col)
    bdraw.ellipse([113, 40, 143, 70], fill=star_col)
    bdraw.ellipse([181, 48, 211, 78], fill=star_col)
    b_img.save('Assets/Case3_Stickerdom/Sprites/badge_pill.png')
    
    l_img = Image.new('RGBA', (256, 68), (0, 0, 0, 0))
    ldraw = ImageDraw.Draw(l_img)
    ldraw.rounded_rectangle([6, 6, 250, 62], radius=28, fill=(215, 185, 150, 255), outline=(178, 145, 112, 255), width=3)
    ldraw.rounded_rectangle([14, 14, 242, 54], radius=20, fill=(232, 210, 182, 255))
    l_img.save('Assets/Case3_Stickerdom/Sprites/label_pill.png')
    
    for side, path in [('l', 'Assets/Case3_Stickerdom/Sprites/sheet_ring_l.png'), ('r', 'Assets/Case3_Stickerdom/Sprites/sheet_ring_r.png')]:
        r_img = Image.new('RGBA', (128, 358), (0, 0, 0, 0))
        rdraw = ImageDraw.Draw(r_img)
        for ry in range(25, 335, 45):
            rdraw.rounded_rectangle([18, ry + 6, 110, ry + 26], radius=10, fill=(45, 28, 15, 80))
            rdraw.rounded_rectangle([16, ry, 108, ry + 20], radius=10, fill=(195, 165, 130, 255), outline=(145, 115, 85, 255), width=2)
            rdraw.line([28, ry + 5, 96, ry + 5], fill=(245, 228, 205, 255), width=3)
        r_img.save(path)

if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == 'cards':
        make_reward_cards()
        raise SystemExit(0)
    make_page_background()
    make_sticker_sheet_background()
    make_deck_background()
    make_sticker_background()
    make_sticker_cat()
    make_sticker_noodle()
    make_sticker_sweets()
    make_reward_cards()
    make_purple_package()
    make_tool_icons()
    make_ui_elements()
    print('ALL ASSETS SYNTHESIZED!')
