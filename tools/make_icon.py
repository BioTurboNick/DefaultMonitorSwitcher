"""
Generates app.ico for DefaultMonitorSwitcher.

Design: two monitors overlapping diagonally (top-left to bottom-right).
  • Top-right corner  — arc arrow sweeping right → down  (forward switch)
  • Bottom-left corner — arc arrow sweeping left  → up    (revert switch)
"""

from PIL import Image, ImageDraw
import math, os


# ── Palette ───────────────────────────────────────────────────────────────────
MON_FRAME   = ( 55,  65,  81, 255)
MON_SCREEN  = ( 17,  24,  39, 255)
SCREEN_GLOW = ( 96, 144, 218, 255)
ARROW_COL   = (251, 191,  36, 255)
BG          = (  0,   0,   0,   0)


# ── Helpers ───────────────────────────────────────────────────────────────────

def lerp(a, b, t):
    return a + (b - a) * t


def draw_monitor(draw, x, y, w, h, s, z_back=False):
    """Flat monitor.  z_back dims it slightly so the back one reads as behind."""
    dim = 40 if z_back else 0
    frame  = tuple(max(0, c - dim) for c in MON_FRAME[:3]) + (255,)
    screen = tuple(max(0, c - dim) for c in MON_SCREEN[:3]) + (255,)
    glow   = tuple(max(0, c - dim) for c in SCREEN_GLOW[:3]) + (255,)

    bw  = max(1, round(2.0 * s))
    rad = max(2, round(3.0 * s))

    # Stand
    sw  = max(1, round(3 * s))
    sh  = max(1, round(5 * s))
    sb  = max(2, round(9 * s))
    px  = round(x + w / 2 - sw / 2)
    bx  = round(x + w / 2 - sb / 2)
    draw.rectangle([px, round(y + h - sh//2), px + sw, round(y + h + 1)], fill=frame)
    draw.rectangle([bx, round(y + h), bx + sb, round(y + h + sh//2)],     fill=frame)

    # Frame
    draw.rounded_rectangle([x, y, x + w, y + h], radius=rad, fill=frame)

    # Screen
    pad = bw + max(1, round(s))
    sx1, sy1 = round(x + pad), round(y + pad)
    sx2, sy2 = round(x + w - pad), round(y + h - pad)
    draw.rectangle([sx1, sy1, sx2, sy2], fill=screen)

    # Glow strip
    gh = max(1, round(4 * s))
    draw.rectangle([sx1, sy1, sx2, sy1 + gh], fill=glow)


def arc_polygon(cx, cy, r_inner, r_outer, start_deg, end_deg, steps=48):
    """Return a filled-arc polygon (annular sector)."""
    pts_outer = []
    pts_inner = []
    for i in range(steps + 1):
        t   = i / steps
        deg = lerp(start_deg, end_deg, t)
        rad = math.radians(deg)
        cos_r, sin_r = math.cos(rad), math.sin(rad)
        pts_outer.append((cx + r_outer * cos_r, cy + r_outer * sin_r))
        pts_inner.append((cx + r_inner * cos_r, cy + r_inner * sin_r))
    return pts_outer + list(reversed(pts_inner))


def draw_arc_arrow(draw, cx, cy, r, start_deg, end_deg, thickness, colour, s):
    """Draw a thick arc + arrowhead at the end."""
    half    = thickness / 2
    polygon = arc_polygon(cx, cy, r - half, r + half, start_deg, end_deg)
    if len(polygon) >= 3:
        draw.polygon(polygon, fill=colour)

    # Arrowhead at the end angle
    end_rad = math.radians(end_deg)
    tip_x   = cx + r * math.cos(end_rad)
    tip_y   = cy + r * math.sin(end_rad)

    # Tangent direction at end (90° rotated from radius, in sweep direction)
    sweep   = 1 if end_deg > start_deg else -1
    tan_x   = -math.sin(end_rad) * sweep
    tan_y   =  math.cos(end_rad) * sweep

    tip_sz  = max(2.5, 5.5 * s)
    perp_x, perp_y = -tan_y, tan_x
    apex  = (tip_x + tan_x * tip_sz,           tip_y + tan_y * tip_sz)
    left  = (tip_x + perp_x * tip_sz * 1.0,    tip_y + perp_y * tip_sz * 1.0)   # 2× wider
    right = (tip_x - perp_x * tip_sz * 1.0,    tip_y - perp_y * tip_sz * 1.0)
    draw.polygon([apex, left, right], fill=colour)


# ── Render at one size ────────────────────────────────────────────────────────

def render(size):
    img  = Image.new("RGBA", (size, size), BG)
    draw = ImageDraw.Draw(img)
    s    = size / 64.0   # design grid is 64 units

    # Monitor A (back, top-left)  grid: (4,4)→(42,36)  w=38 h=32
    ax, ay, aw, ah = 4*s, 4*s, 38*s, 30*s
    # Monitor B (front, bottom-right)  grid: (22,30)→(60,60)  w=38 h=30
    bx, by, bw, bh = 22*s, 30*s, 38*s, 30*s

    draw_monitor(draw, ax, ay, aw, ah, s, z_back=True)
    draw_monitor(draw, bx, by, bw, bh, s, z_back=False)

    # ── Arrows ────────────────────────────────────────────────────────────────
    # Arc endpoints are anchored to the monitor faces:
    #
    # Monitor A screen (approx 64-grid): x=7→39,  y=7→31
    # Monitor B screen (approx 64-grid): x=25→57, y=33→57
    #
    # Top-right arc  — centre (42,36) r=13 — 270°(up)→360°(right)
    #   tail at (42, 23): monitor A right frame edge
    #   tip  at (55, 36): inside monitor B screen
    #
    # Bottom-left arc — centre (22,28) r=13 — 90°(down)→180°(left)
    #   tail at (22, 41): monitor B left frame edge
    #   tip  at  (9, 28): inside monitor A screen

    arc_r = 13.0 * s
    arc_t = max(2.0, 3.0 * s)
    draw_arc_arrow(draw,
                   cx=42*s, cy=36*s,
                   r=arc_r,
                   start_deg=270, end_deg=360,
                   thickness=arc_t, colour=ARROW_COL, s=s)

    draw_arc_arrow(draw,
                   cx=22*s, cy=28*s,
                   r=arc_r,
                   start_deg=90, end_deg=180,
                   thickness=arc_t, colour=ARROW_COL, s=s)

    return img


# ── Build .ico ────────────────────────────────────────────────────────────────

def make_ico(out_path):
    sizes  = [16, 24, 32, 48, 64, 128, 256]
    frames = []
    for sz in sizes:
        if sz <= 48:
            big = render(sz * 2)
            img = big.resize((sz, sz), Image.LANCZOS)
        else:
            img = render(sz)
        frames.append(img)

    frames[0].save(
        out_path,
        format="ICO",
        sizes=[(f.width, f.height) for f in frames],
        append_images=frames[1:],
    )
    print(f"Wrote {out_path}  ({[f.width for f in frames]})")


if __name__ == "__main__":
    here = os.path.dirname(__file__)
    out  = os.path.abspath(os.path.join(here, "..", "UI", "Resources", "Icons", "app.ico"))
    make_ico(out)

    # Save previews
    for sz in (256, 48, 32, 16):
        img = render(sz) if sz > 48 else render(sz*2).resize((sz,sz), Image.LANCZOS)
        img.save(os.path.join(here, f"preview_{sz}.png"))
    print("Previews written to tools/")
