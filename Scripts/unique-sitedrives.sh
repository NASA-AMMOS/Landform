#!/bin/sh

find . -name "*XYZ*.IMG" | cut -c 80-86 | sort | uniq

