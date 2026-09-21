#!/usr/bin/env python3
"""
Generate the LouisE PWA icon set into frontend/public/ from one geometric
definition of the brand mark (the "LE" tile used in the app sidebar).

Standard library only — no Pillow, ImageMagick or Node required — so the icons
are reproducible on any box that has python3:

    python3 scripts/generate-icons.py

Outputs
  icon.svg                    vector master (SVG favicon for modern browsers)
  favicon.ico                 real ICO container with 16/32/48 px PNG entries
  pwa-192x192.png             manifest icon, purpose "any" (rounded tile)
  pwa-512x512.png             manifest icon, purpose "any" (rounded tile)
  pwa-maskable-512x512.png    manifest icon, purpose "maskable" (full bleed,
                              mark inside the 80% safe zone)
  apple-touch-icon-180x180.png  iOS home screen (full bleed; iOS rounds it)
  badge-96x96.png             Android notification badge (white mark on
                              transparent — rendered as a monochrome mask)
"""
from __future__ import annotations

import os
import struct
import sys
import zlib

# ---------------------------------------------------------------------------
# Design (all coordinates in a 512 x 512 unit space)
# ---------------------------------------------------------------------------
UNIT = 512
CORNER_RADIUS = 112                      # ~22 % — matches the sidebar tile's rounding
BRAND_400 = (0x3F, 0x8C, 0x9D)           # --color-brand-400 (top-left)
BRAND_700 = (0x17, 0x49, 0x55)           # --color-brand-700 (bottom-right)
WHITE = (0xFF, 0xFF, 0xFF)

# Bold sans "LE" built from rectangles: cap height 232, stroke 52, centred.
_H, _T, _Y0 = 232, 52, 140
_LX, _EX = 84, 272
GLYPH_RECTS = [
    # L
    (_LX, _Y0, _LX + _T, _Y0 + _H),                      # stem
    (_LX, _Y0 + _H - _T, _LX + 148, _Y0 + _H),           # foot
    # E
    (_EX, _Y0, _EX + _T, _Y0 + _H),                      # stem
    (_EX, _Y0, _EX + 156, _Y0 + _T),                     # top bar
    (_EX, _Y0 + (_H - _T) // 2, _EX + 140, _Y0 + (_H + _T) // 2),  # middle bar (shorter)
    (_EX, _Y0 + _H - _T, _EX + 156, _Y0 + _H),           # bottom bar
]

SVG = f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {UNIT} {UNIT}">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#{BRAND_400[0]:02X}{BRAND_400[1]:02X}{BRAND_400[2]:02X}"/>
      <stop offset="1" stop-color="#{BRAND_700[0]:02X}{BRAND_700[1]:02X}{BRAND_700[2]:02X}"/>
    </linearGradient>
  </defs>
  <rect width="{UNIT}" height="{UNIT}" rx="{CORNER_RADIUS}" fill="url(#g)"/>
  <path fill="#fff" d="M{_LX} {_Y0}h{_T}v{_H - _T}h{148 - _T}v{_T}H{_LX}z M{_EX} {_Y0}h156v{_T}H{_EX + _T}v{(_H - _T) // 2 - _T}h{140 - _T}v{_T}h-{140 - _T}v{(_H - _T) // 2 - _T}h{156 - _T}v{_T}H{_EX}z"/>
</svg>
"""


# ---------------------------------------------------------------------------
# Rasteriser
# ---------------------------------------------------------------------------
def _in_rounded_square(u: float, v: float, r: float) -> bool:
    """Point (u, v) inside the UNIT square with corner radius r?"""
    if u < 0 or v < 0 or u > UNIT or v > UNIT:
        return False
    cx = r if u < r else (UNIT - r if u > UNIT - r else u)
    cy = r if v < r else (UNIT - r if v > UNIT - r else v)
    if cx == u or cy == v:
        return True
    return (u - cx) ** 2 + (v - cy) ** 2 <= r * r


def _in_glyph(u: float, v: float) -> bool:
    for x0, y0, x1, y1 in GLYPH_RECTS:
        if x0 <= u < x1 and y0 <= v < y1:
            return True
    return False


def _gradient(u: float, v: float) -> tuple[int, int, int]:
    t = (u + v) / (2 * UNIT)
    t = 0.0 if t < 0 else (1.0 if t > 1 else t)
    return tuple(round(a + (b - a) * t) for a, b in zip(BRAND_400, BRAND_700))  # type: ignore[return-value]


def render(size: int, *, background: str, glyph_scale: float = 1.0, supersample: int = 4) -> bytes:
    """
    Render one RGBA frame.
      background: 'rounded' (transparent corners), 'square' (full bleed) or 'none'
      glyph_scale: scale of the LE mark about the centre (maskable / badge variants)
    Returns raw RGBA bytes, row-major, non-premultiplied.
    """
    ss = supersample
    inv = 1.0 / (ss * ss)
    scale = UNIT / size
    half = UNIT / 2
    out = bytearray(size * size * 4)
    i = 0
    for py in range(size):
        for px in range(size):
            r = g = b = a = 0.0
            for sy in range(ss):
                v = (py + (sy + 0.5) / ss) * scale
                for sx in range(ss):
                    u = (px + (sx + 0.5) / ss) * scale
                    # glyph test in the (possibly scaled) mark space
                    gu = half + (u - half) / glyph_scale
                    gv = half + (v - half) / glyph_scale
                    if _in_glyph(gu, gv):
                        r += WHITE[0]; g += WHITE[1]; b += WHITE[2]; a += 1.0
                        continue
                    if background == 'none':
                        continue
                    if background == 'square' or _in_rounded_square(u, v, CORNER_RADIUS):
                        cr, cg, cb = _gradient(u, v)
                        r += cr; g += cg; b += cb; a += 1.0
            if a > 0:
                # un-premultiply: colour sums were only accumulated for covered samples
                out[i] = round(r / a); out[i + 1] = round(g / a); out[i + 2] = round(b / a)
                out[i + 3] = round(a * inv * 255)
            i += 4
    return bytes(out)


# ---------------------------------------------------------------------------
# Encoders
# ---------------------------------------------------------------------------
def _chunk(tag: bytes, data: bytes) -> bytes:
    return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)


def encode_png(rgba: bytes, size: int) -> bytes:
    stride = size * 4
    raw = b''.join(b'\x00' + rgba[y * stride:(y + 1) * stride] for y in range(size))
    return (b'\x89PNG\r\n\x1a\n'
            + _chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0))
            + _chunk(b'IDAT', zlib.compress(raw, 9))
            + _chunk(b'IEND', b''))


def encode_ico(pngs: list[tuple[int, bytes]]) -> bytes:
    """ICO container with PNG-compressed entries (supported by every current browser)."""
    header = struct.pack('<HHH', 0, 1, len(pngs))
    entries = b''
    body = b''
    offset = 6 + 16 * len(pngs)
    for size, png in pngs:
        dim = 0 if size >= 256 else size
        entries += struct.pack('<BBBBHHII', dim, dim, 0, 0, 1, 32, len(png), offset)
        body += png
        offset += len(png)
    return header + entries + body


# ---------------------------------------------------------------------------
def main() -> int:
    out_dir = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'frontend', 'public'))
    if not os.path.isdir(out_dir):
        print(f'output directory not found: {out_dir}', file=sys.stderr)
        return 1

    def write(name: str, data: bytes) -> None:
        path = os.path.join(out_dir, name)
        with open(path, 'wb') as f:
            f.write(data)
        print(f'  {name:32s} {len(data):>8,d} bytes')

    print(f'Writing icons to {out_dir}')
    write('icon.svg', SVG.encode('utf-8'))

    for size in (192, 512):
        write(f'pwa-{size}x{size}.png', encode_png(render(size, background='rounded'), size))

    # Maskable: full bleed, mark shrunk into the 80 % safe zone (bbox 344x232 * 0.78
    # → 268x181 → corner distance 162 < 205 safe radius).
    write('pwa-maskable-512x512.png', encode_png(render(512, background='square', glyph_scale=0.78), 512))

    # iOS applies its own corner mask, so ship a full-bleed square.
    write('apple-touch-icon-180x180.png', encode_png(render(180, background='square'), 180))

    # Notification badge: Android uses only the alpha channel → white mark, no tile.
    write('badge-96x96.png', encode_png(render(96, background='none', glyph_scale=1.3), 96))

    ico_entries = [(s, encode_png(render(s, background='rounded', supersample=6), s)) for s in (16, 32, 48)]
    write('favicon.ico', encode_ico(ico_entries))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
