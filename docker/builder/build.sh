#!/usr/bin/env bash

docker buildx build --platform linux/x86_64 -t landform-builder:4.0 .
