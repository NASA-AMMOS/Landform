#!/bin/sh

# install pandoc from https://pandoc.org/installing.html
# install texlive from http://www.tug.org/texlive/acquire-netinstall.html

sections="INTRODUCTION.md SETUP.md API.md TEST.md"
filename=Landform_Mesh_Tiling_Server
title="Landform Mesh Tiling Server"

pandoc $sections -o $filename.html -s --toc --metadata pagetitle="$title" --css pandoc.css -V title="$title"
pandoc $sections -o $filename.pdf -s --toc --metadata pagetitle="$title" --css pandoc.css -V title="$title"

for s in $sections; do
  s=${s%.*}
  pandoc $s.md -o $s.pdf -s --toc --metadata pagetitle="$title $s" --css pandoc.css -V title="$title $s"
done
