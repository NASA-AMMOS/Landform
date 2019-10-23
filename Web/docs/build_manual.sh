#!/bin/sh

# install pandoc from https://pandoc.org/installing.html
# install texlive from http://www.tug.org/texlive/acquire-netinstall.html

# Note from Adrian Tinio: MGSS specifically requests we use the name "Geometry Streaming Server Product Guide"

sections="INTRODUCTION.md SETUP.md API.md TEST.md"
filename=Geometry_Streaming_Server_Product_Guide
title="Geometry Streaming Server Product Guide"

# vertical bars in tables hack
# https://gist.github.com/svenevs/41a1a434a055adcee56bd1f0374fa254
python=py

pandoc $sections -s -o $filename.html --toc --metadata pagetitle="$title" --css pandoc.css -V title="$title"
pandoc $sections -s -f markdown -t latex --template latex.template --toc --metadata pagetitle="$title" --css pandoc.css -V title="$title" | $python tex_table_verts.py > $filename.tex
pdflatex $filename.tex
pdflatex $filename.tex

for s in $sections; do
  s=${s%.*}
  pandoc $s.md -s -f markdown -t latex --template latex.template --toc --metadata pagetitle="$title $s" --css pandoc.css -V title="$title $s" | $python tex_table_verts.py > $s.tex
  pdflatex $s.tex
  pdflatex $s.tex
done
