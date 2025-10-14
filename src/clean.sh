#!/usr/bin/env bash

rm -rf */bin */obj

if uname -a | grep WSL > /dev/null; then
  rm -rf /tmp/Landform
fi
