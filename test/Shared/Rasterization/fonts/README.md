# The font the rasterization fixture embeds

`LiberationSans-Regular.ttf`, version 2.1.5, under the SIL Open Font License 1.1
(`LICENSE.txt` beside it, and the font's own name table declares the same).

## Why it is checked in

`TestPdf.cs` states the convention this breaks: *"PDFs built in code rather than checked in,
so what a test renders is readable in the test and nothing depends on a binary file."* A
deliberate exception, for one reason.

A PDF that names a font without embedding it leaves the choice to whatever reads it. The
fixture named Helvetica, which no engine actually has, so PDFium substituted Arial on Windows
and something else on the Linux runner, and the same page came out with different glyphs:
0.56% of the octets differed between the two operating systems, and 0.11% between the two
engines on one machine — every one of them inside the numeral, and none anywhere else.
Embedding the outlines removes the choice, so every engine on every platform draws the same
page.

## Why this font

- **Static TrueType, not variable.** A variable font embeds a default instance and engines
  differ about which one that is, which would reintroduce the problem it is here to remove.
- **OFL 1.1**, which permits embedding and redistribution. Arimo would have matched this
  repository's Apache-2.0 licence, but Google now publishes it under the OFL too, and only as
  a variable font.
- **Metric-compatible with Arial**, so the widths below are the familiar Arial ones.

## Why the whole file and not a subset

Subsetting to the four digits the fixture draws would cost a few kilobytes instead of 401 KB,
but it needs a tool (`fonttools`) either in the build or in whoever's hands regenerate the
blob. The file is checked in whole so that nothing is needed to reproduce it: copy it from a
distribution's `fonts-liberation` package and the bytes are the same.
