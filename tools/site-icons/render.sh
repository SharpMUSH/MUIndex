#!/usr/bin/env bash
#
# Regenerates every raster asset in src/MUI.Web/wwwroot from the SVG sources beside this script.
#
# The outputs are committed, and this is not a build step. Two reasons. A rasterizer in the
# dependency list would be a native binary or a split-licensed package carried for ever to produce
# nine files that change about once a year — and the text on the preview cards is set in a font
# fontconfig resolves, so a build that ran this would produce different bytes on a machine with a
# different font installed. Committing the output is what makes the site's own identity a thing in
# the repository rather than a thing the build machine happened to have.
#
# Needs rsvg-convert (librsvg) and magick (ImageMagick 7). Run it from anywhere:
#
#   tools/site-icons/render.sh
#
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
web="$(cd "$here/../../src/MUI.Web/wwwroot" && pwd)"

for tool in rsvg-convert magick; do
    command -v "$tool" >/dev/null || { echo "missing: $tool" >&2; exit 1; }
done

echo "icons"

# The favicon source is the shipped SVG itself — there is no separate master, so the vector a
# browser gets and the pixels every other size is rendered from cannot drift apart.
rsvg-convert -w 180 -h 180 "$web/favicon.svg" -o "$web/apple-touch-icon.png"
rsvg-convert -w 192 -h 192 "$web/favicon.svg" -o "$web/icon-192.png"
rsvg-convert -w 512 -h 512 "$web/favicon.svg" -o "$web/icon-512.png"

# Maskable: Android crops to a circle inscribed in 80% of the canvas, so the plate goes full bleed
# and the mark shrinks into the safe zone. Rendering the normal icon as maskable clips its corners.
rsvg-convert -w 512 -h 512 "$here/icon-maskable.svg" -o "$web/icon-512-maskable.png"

# Three sizes in one .ico, because a browser picks per context and upscaling 16 from 32 is visibly
# soft in a tab strip. The two small frames come from icon-small.svg — see that file: the shipped
# mark's stroke weight fuses into a blob at sixteen pixels, so the small sizes are drawn for the
# grid they land on rather than sampled down onto it.
rsvg-convert -w 16 -h 16 "$here/icon-small.svg" -o "/tmp/mui-icon-16.png"
rsvg-convert -w 32 -h 32 "$here/icon-small.svg" -o "/tmp/mui-icon-32.png"
rsvg-convert -w 48 -h 48 "$web/favicon.svg" -o "/tmp/mui-icon-48.png"
magick /tmp/mui-icon-16.png /tmp/mui-icon-32.png /tmp/mui-icon-48.png "$web/favicon.ico"
rm -f /tmp/mui-icon-{16,32,48}.png

echo "cards"

mkdir -p "$web/og"

# One template and a substitution rather than five near-identical files, because five copies of a
# layout are five chances for one of them to drift and nobody to notice — the whole value of the set
# is that a link into this site looks like one before the title has been read.
card() {
    local name="$1" label="$2" line="$3"

    sed -e "s|__LABEL__|$label|" -e "s|__LINE__|$line|" "$here/card.svg" \
        | rsvg-convert -w 1200 -h 630 -o "$web/og/$name.png"
}

card site      "THE MU* DIRECTORY" "measured, not asserted"
card game      "GAME"              "every fact carries when it was taken"
card archive   "THE ARCHIVE"       "the games that went dark, kept"
card rankings  "RANKINGS"          "computed from measurements. no votes"
card reference "REFERENCE"         "codebases, clients, protocols"

echo "done"
ls -1 "$web"/favicon.* "$web"/*.png "$web"/og/*.png
