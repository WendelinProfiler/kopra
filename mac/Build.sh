#!/bin/zsh
set -eu
cd "$(dirname "$0")"
app=build/Kopra.app
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" build/architectures
if [[ -n "${KOPRA_ARCH:-}" ]]; then architectures=("$KOPRA_ARCH"); else architectures=(arm64 x86_64); fi
for architecture in $architectures; do
  xcrun swiftc -swift-version 5 -parse-as-library -O -target "$architecture-apple-macos13.0" *.swift vendor/BigInt/*.swift \
    -o "build/architectures/Kopra-$architecture" \
    -framework AppKit -framework SwiftUI -framework Carbon -framework Security \
    -framework CryptoKit -framework ServiceManagement -framework AVFoundation \
    -framework Vision -framework CoreMedia -framework CoreVideo
done
if [[ ${#architectures} -eq 1 ]]; then
  cp "build/architectures/Kopra-$architectures[1]" "$app/Contents/MacOS/Kopra"
else
  xcrun lipo -create build/architectures/Kopra-arm64 build/architectures/Kopra-x86_64 -output "$app/Contents/MacOS/Kopra"
fi
cp vendor/BigInt/LICENSE.md "$app/Contents/Resources/BigInt-LICENSE.md"
cp ../assets/localization.en.json ../assets/localization.de.json "$app/Contents/Resources/"
cp -R en.lproj de.lproj "$app/Contents/Resources/"
cp Info.plist "$app/Contents/Info.plist"
icon=../assets/icon-1024.png
if [[ ! -f "$icon" ]]; then icon=icon-1024.png; fi
mkdir -p build/AppIcon.iconset
for size in 16 32 128 256 512; do
  /usr/bin/sips -z "$size" "$size" "$icon" --out "build/AppIcon.iconset/icon_${size}x${size}.png" >/dev/null
  doubled=$((size*2))
  /usr/bin/sips -z "$doubled" "$doubled" "$icon" --out "build/AppIcon.iconset/icon_${size}x${size}@2x.png" >/dev/null
done
/usr/bin/iconutil -c icns build/AppIcon.iconset -o "$app/Contents/Resources/AppIcon.icns"
identity=${CODE_SIGN_IDENTITY:--}
if [[ "$identity" == "-" ]]; then
  codesign --force --sign - --identifier app.kopra.desktop "$app"
else
  codesign --force --sign "$identity" --options runtime --timestamp --entitlements Entitlements.plist "$app"
fi
codesign --verify --strict "$app"
echo MAC_BUNDLE_READY