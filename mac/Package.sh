#!/bin/zsh
set -eu
cd "$(dirname "$0")/.."
mkdir -p artifacts
/usr/bin/ditto -c -k --keepParent mac/build/Kopra.app artifacts/Kopra-macOS-universal-preview.zip
/usr/bin/shasum -a 256 artifacts/Kopra-macOS-universal-preview.zip > artifacts/mac-SHA256SUMS.txt
