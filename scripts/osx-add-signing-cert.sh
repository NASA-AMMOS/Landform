#!/bin/bash

scriptdir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
rootdir="${scriptdir%/*}"

cd "$rootdir/scripts"

sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain AppleWWDRCAG3.cer

security import osx-signing-cert.p12 -k ~/Library/Keychains/login.keychain-db -P assword

