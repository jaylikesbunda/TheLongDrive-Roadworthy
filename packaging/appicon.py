"""Build the patch icon without external tools.

Flat rounded square, one accent colour, a white road stripe with a dashed centre
line. Rendered by supersampling a tiny analytic scene, then encoded as a PNG and
as a multi-size Windows .ico (BMP entries up to 64 px, PNG entries above).

Only the standard library is used, so the build works without Pillow.
"""
from __future__ import annotations

import base64
import struct
import zlib

ACCENT = (37, 99, 235)  # #2563EB
GLYPH = (255, 255, 255)
ICO_SIZES = (16, 24, 32, 48, 64, 128, 256)
WINDOW_SIZE = 128
SUPERSAMPLE = 4


def _rounded(u, v, inset=0.03, radius=0.22):
    """True inside a rounded square inset from the unit cell."""
    left, right = inset, 1.0 - inset
    if not (left <= u <= right and left <= v <= right):
        return False
    cx = left + radius
    cy = top = left + radius
    bx = right - radius
    by = right - radius
    if u < cx and v < cy:
        return (u - cx) ** 2 + (v - cy) ** 2 <= radius ** 2
    if u > bx and v < cy:
        return (u - bx) ** 2 + (v - cy) ** 2 <= radius ** 2
    if u < cx and v > by:
        return (u - cx) ** 2 + (v - by) ** 2 <= radius ** 2
    if u > bx and v > by:
        return (u - bx) ** 2 + (v - by) ** 2 <= radius ** 2
    return True


def _road(u, v):
    """White where the road body is, False in the dashed centre line."""
    start, end = 0.20, 0.86
    if not (start <= v <= end):
        return False
    t = (v - start) / (end - start)
    half = 0.15 + 0.15 * t
    if abs(u - 0.5) > half:
        return False
    # Dashed centre line, cut out of the white stripe.
    dash = ((0.02, 0.14), (0.24, 0.38), (0.50, 0.66), (0.78, 0.96))
    if abs(u - 0.5) <= 0.035 + 0.02 * t and any(a <= t <= b for a, b in dash):
        return False
    return True


def render(size):
    """Return RGBA bytes, top-down, for one square icon size."""
    step = 1.0 / (size * SUPERSAMPLE)
    rows = []
    for y in range(size):
        row = bytearray()
        for x in range(size):
            red = green = blue = alpha = 0
            for sy in range(SUPERSAMPLE):
                v = (y * SUPERSAMPLE + sy + 0.5) * step
                for sx in range(SUPERSAMPLE):
                    u = (x * SUPERSAMPLE + sx + 0.5) * step
                    if not _rounded(u, v):
                        continue
                    r, g, b = GLYPH if _road(u, v) else ACCENT
                    red += r
                    green += g
                    blue += b
                    alpha += 255
            samples = SUPERSAMPLE * SUPERSAMPLE
            if alpha == 0:
                row += b"\x00\x00\x00\x00"
            else:
                row += bytes((red // samples, green // samples, blue // samples, alpha // samples))
        rows.append(bytes(row))
    return b"".join(rows)


def png(rgba, size):
    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + rgba[y * size * 4:(y + 1) * size * 4] for y in range(size))
    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def dib(rgba, size):
    """32-bit bottom-up DIB plus the 1-bit AND mask, as used inside .ico files."""
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, size * size * 4, 0, 0, 0, 0)
    pixels = bytearray()
    for y in range(size - 1, -1, -1):
        for x in range(size):
            i = (y * size + x) * 4
            pixels += bytes((rgba[i + 2], rgba[i + 1], rgba[i], rgba[i + 3]))
    stride = ((size + 31) // 32) * 4
    mask = bytearray()
    for y in range(size - 1, -1, -1):
        bits = bytearray(stride)
        for x in range(size):
            if rgba[(y * size + x) * 4 + 3] == 0:
                bits[x // 8] |= 0x80 >> (x % 8)
        mask += bits
    return header + bytes(pixels) + bytes(mask)


def ico(images):
    """images: list of (size, bytes) in ascending size order."""
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = len(header) + 16 * len(images)
    entries, payload = [], b""
    for size, data in images:
        entries.append(struct.pack("<BBBBHHII", 0 if size >= 256 else size, 0 if size >= 256 else size,
                                   0, 0, 1, 32, len(data), offset))
        payload += data
        offset += len(data)
    return header + b"".join(entries) + payload


def build_ico():
    images = []
    for size in ICO_SIZES:
        rgba = render(size)
        images.append((size, png(rgba, size) if size >= 128 else dib(rgba, size)))
    return ico(images)


def write_ico(path):
    path.write_bytes(build_ico())
    return path


def window_png():
    """The PNG embedded in the installer for the Tk window icon."""
    return png(render(WINDOW_SIZE), WINDOW_SIZE)


def window_png_b64():
    return base64.b64encode(window_png()).decode("ascii")


if __name__ == "__main__":
    from pathlib import Path
    target = Path(__file__).with_name("app.ico")
    write_ico(target)
    print(f"{target} ({target.stat().st_size:,} bytes)")
    print(window_png_b64())
