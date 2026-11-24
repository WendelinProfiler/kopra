#!/bin/zsh
set -eu
cd "$(dirname "$0")"
target="$HOME/Applications/Kopra.app"
mkdir -p "$HOME/Applications"
if [[ -d "$target" ]]; then
  mkdir -p "$HOME/Library/Application Support/KopraBackups"
  /usr/bin/ditto "$target" "$HOME/Library/Application Support/KopraBackups/Kopra-$(date +%Y%m%d-%H%M%S).app"
fi
/usr/bin/ditto build/Kopra.app "$target"
/usr/bin/codesign --verify --strict "$target"
/usr/bin/open -a "$target" --args --show
echo KOPRA_MAC_INSTALLED
