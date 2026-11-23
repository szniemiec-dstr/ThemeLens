# SPDX-License-Identifier: GPL-3.0-or-later
"""Packs the PNG files made by build/make-icons.cjs into the .ico files in src/ThemeLens.App/Resources."""
import glob, os, re, sys
from PIL import Image

source, target = sys.argv[1], os.path.join("src", "ThemeLens.App", "Resources")
names = sorted({re.sub(r"-\d+\.png$", "", os.path.basename(p)) for p in glob.glob(os.path.join(source, "*.png"))})
for name in names:
    frames = sorted((Image.open(p).convert("RGBA") for p in glob.glob(os.path.join(source, name + "-*.png"))), key=lambda i: i.width)
    frames[-1].save(os.path.join(target, name + ".ico"), format="ICO", append_images=frames[:-1],
                    sizes=[(f.width, f.height) for f in frames], bitmap_format="bmp")
    print(name + ".ico", [f.width for f in frames])
