#!/usr/bin/env python3
"""Re-frame the high-resolution HOME renders to the pixel-art convention.

Box slots mix two sprite families. The pixel art is authored to a firm convention --
the creature occupies roughly half the frame height and stands about 5% above the
bottom edge -- which is why those sit nicely in a slot. The HOME renders have no such
convention: measured across a sample, their content fills anywhere from 38% to 96% of
the canvas. Fitted into a slot they therefore vary wildly in drawn size, and the tall
ones butt against the top edge while pixel sprites float mid-frame.

This writes a parallel set, cropped to content and re-padded onto a 68x56 frame using
the pixel-art envelope, so both families frame identically. The originals are left
alone; the inspector's artwork still wants a render that fills its box.
"""
import os
import sys
from PIL import Image

FRAME_W, FRAME_H = 68, 56
# Envelope measured from the pixel-art set: content is about 51% x 48% of the frame,
# sitting on a baseline roughly 5% above the bottom.
BOX_W, BOX_H = round(FRAME_W * 0.51), round(FRAME_H * 0.48)
BASELINE = round(FRAME_H * 0.05)


def reframe(src_path, dst_path):
    with Image.open(src_path) as im:
        im = im.convert("RGBA")
        bbox = im.getbbox()
        if bbox is None:
            return False
        content = im.crop(bbox)
        scale = min(BOX_W / content.width, BOX_H / content.height)
        size = (max(1, round(content.width * scale)), max(1, round(content.height * scale)))
        content = content.resize(size, Image.LANCZOS)

        frame = Image.new("RGBA", (FRAME_W, FRAME_H), (0, 0, 0, 0))
        frame.paste(content,
                    ((FRAME_W - content.width) // 2,            # centred across
                     FRAME_H - BASELINE - content.height),      # standing on the baseline
                    content)
        os.makedirs(os.path.dirname(dst_path), exist_ok=True)
        frame.save(dst_path, optimize=True)
        return True


def main():
    root = os.path.join(os.path.dirname(__file__), "..", "PKHeX.Mac", "Assets", "hires")
    root = os.path.normpath(root)
    if not os.path.isdir(root):
        sys.exit(f"no hires directory at {root}")

    written = skipped = 0
    for dirpath, _, files in os.walk(root):
        if os.path.basename(dirpath) == "slot" or "slot" in dirpath.split(os.sep):
            continue
        for name in files:
            if not name.endswith(".png"):
                continue
            rel = os.path.relpath(os.path.join(dirpath, name), root)
            dst = os.path.join(root, "slot", rel)
            if reframe(os.path.join(dirpath, name), dst):
                written += 1
            else:
                skipped += 1
    print(f"re-framed {written} renders to {FRAME_W}x{FRAME_H} "
          f"(content envelope {BOX_W}x{BOX_H}, baseline {BASELINE}px); skipped {skipped} empty")


if __name__ == "__main__":
    main()
