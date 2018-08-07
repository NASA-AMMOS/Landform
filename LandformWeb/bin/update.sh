#!/bin/sh

for f in `ls *.dll *.exe`; do cp ../../TilingServer/bin/Release/$f .; done

