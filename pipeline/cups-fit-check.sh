#!/bin/sh
# Check that cups-filters reads print-scaling before fit-to-page, so the options a CUPS
# channel sends with every PDF job (media, print-scaling and fit-to-page) leave Linux where
# it was. Nothing is printed: cupsfilter runs the filter chain and stops after pdftopdf,
# the stage that decides where the page lands.
#
#   sh ./pipeline/cups-fit-check.sh [queue] [pdf] [media]
#
# queue  defaults to the default destination; its PPD is used when it has one
# pdf    defaults to the 4 by 6 inch label in the samples
# media  defaults to A4
#
# Needs cupsfilter (cups), pdftoppm (poppler-utils) and python3. Linux only: macOS renders
# with Quartz and has no pdftopdf, and the library's docs cover what it does.

set -eu

here=$(cd "$(dirname "$0")" && pwd)
queue=${1:-$(lpstat -d 2>/dev/null | sed -n 's/^system default destination: //p')}
pdf=${2:-$here/../samples/Devices.Samples/PrintFiles/document.pdf}
media=${3:-A4}

fail() { echo "error: $*" >&2; exit 2; }

[ "$(uname)" = Linux ] || fail "this checks cups-filters, which runs on Linux; macOS renders with Quartz"
for tool in cupsfilter pdftoppm python3; do
    command -v "$tool" >/dev/null 2>&1 || fail "$tool not found (cupsfilter: cups, pdftoppm: poppler-utils)"
done
[ -r "$pdf" ] || fail "cannot read $pdf"

ppd=""
if [ -n "$queue" ] && [ -f "/etc/cups/ppd/$queue.ppd" ]; then
    [ -r "/etc/cups/ppd/$queue.ppd" ] || fail "cannot read /etc/cups/ppd/$queue.ppd; run with sudo"
    ppd="/etc/cups/ppd/$queue.ppd"
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

echo "queue:        ${queue:-none}"
echo "ppd:          ${ppd:-none, cupsfilter defaults}"
echo "document:     $pdf"
echo "media:        $media"
if command -v dpkg-query >/dev/null 2>&1; then
    echo "cups-filters: $(dpkg-query -W -f '${Package} ${Version}  ' cups-filters libcupsfilters2 2>/dev/null || true)"
elif command -v rpm >/dev/null 2>&1; then
    echo "cups-filters: $(rpm -q cups-filters libcupsfilters 2>/dev/null | tr '\n' ' ')"
fi
echo

# One run of the chain up to pdftopdf, rendered to a gray page for measuring.
run() {
    name=$1
    shift
    set -- -m application/vnd.cups-pdf -o "media=$media" "$@"
    [ -n "$ppd" ] && set -- -p "$ppd" "$@"
    cupsfilter "$@" "$pdf" > "$work/$name.pdf" 2> "$work/$name.log" \
        || fail "cupsfilter failed for $name; see its log:
$(tail -5 "$work/$name.log")"
    pdftoppm -r 50 -gray -singlefile "$work/$name.pdf" "$work/$name"
}

run before -o print-scaling=auto
run now -o print-scaling=auto -o fit-to-page
run control -o fit-to-page

# The ink box of each page, in pixels at 50 dpi, and the verdict. Two boxes within two
# pixels of each other are the same placement: the rendering rounds.
python3 - "$work" <<'EOF'
import sys

def ink_box(path):
    with open(path, "rb") as f:
        data = f.read()
    # A binary PGM: "P5", width, height, maximum, then one byte a pixel.
    fields, i = [], 0
    while len(fields) < 4:
        while data[i:i + 1].isspace():
            i += 1
        if data[i:i + 1] == b"#":
            i = data.index(b"\n", i)
            continue
        j = i
        while not data[j:j + 1].isspace():
            j += 1
        fields.append(data[i:j])
        i = j
    width, height = int(fields[1]), int(fields[2])
    pixels = data[i + 1:]
    xs, ys = [], []
    for y in range(height):
        row = pixels[y * width:(y + 1) * width]
        dark = [x for x, v in enumerate(row) if v < 128]
        if dark:
            xs += (dark[0], dark[-1])
            ys.append(y)
    if not xs:
        return width, height, None
    left, top = min(xs), min(ys)
    return width, height, (left, top, max(xs) - left + 1, max(ys) - top + 1)

def same(a, b):
    return a is not None and b is not None and all(abs(p - q) <= 2 for p, q in zip(a, b))

work = sys.argv[1]
boxes = {}
labels = {
    "before": "print-scaling=auto (what Linux did before)",
    "now": "print-scaling=auto + fit-to-page (what the library sends)",
    "control": "fit-to-page alone (what goes wrong if it wins)",
}
for name, label in labels.items():
    width, height, box = ink_box(f"{work}/{name}.pgm")
    boxes[name] = box
    if box is None:
        print(f"{name:8} page {width}x{height}: no ink found   {label}")
    else:
        left, top, w, h = box
        print(f"{name:8} page {width}x{height}: ink {w}x{h} at left {left}, top {top}, "
              f"right {width - left - w}, bottom {height - top - h}   {label}")
print()

if same(boxes["before"], boxes["now"]):
    if same(boxes["before"], boxes["control"]):
        print("PASS, but the control placed the page the same way, so this document cannot tell the")
        print("two options apart. Try a page smaller than the media that fit-to-page would enlarge.")
    else:
        print("PASS: print-scaling wins over fit-to-page, so Linux places the page as it did.")
    sys.exit(0)

print("FAIL: adding fit-to-page moved the page, so this cups-filters reads fit-to-page first.")
print("The library must not send fit-to-page to this server.")
sys.exit(1)
EOF
