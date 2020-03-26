#!/bin/sh

./Landform/bin/Release/Landform.exe build-tiling-input windjana --meshframe 0311472

./Landform/bin/Release/Landform.exe blend-images windjana --meshframe 0311472

./Landform/bin/Release/Landform.exe build-tileset windjana --meshframe 0311472

mv c:/users/$USERNAME/Documents/landform-storage/local-windjana/tiling/TileSet/0311472Frame/best/windjana out/windjana/tilesets/windjana-local


