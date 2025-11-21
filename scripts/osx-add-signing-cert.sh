#!/bin/bash

if [ $# -lt 2 ]; then
  echo "USAGE: osx-add-signing-cert.sh cert.p12 password"
  exit 1
fi

scriptdir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
rootdir="${scriptdir%/*}"

cd "$rootdir/scripts"

curl -O https://www.apple.com/certificateauthority/AppleWWDRCAG3.cer

sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain AppleWWDRCAG3.cer

security import $1 -k ~/Library/Keychains/login.keychain-db -P $2

