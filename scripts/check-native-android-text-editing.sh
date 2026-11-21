#!/usr/bin/env bash
set -euo pipefail
fixture=tests/Drover.Native.Android.Tests
build_tools="$ANDROID_HOME/build-tools/37.0.0"
platform="$ANDROID_HOME/platforms/android-37.0/android.jar"
scratch=$(mktemp -d)
installed=false
cleanup() {
  if [[ "$installed" == true ]]; then
    adb uninstall org.drover.tests.keyboard >/dev/null || true
  fi
  rm -rf -- "$scratch"
}
trap cleanup EXIT
mkdir "$scratch/classes"
"$JAVA_HOME/bin/javac" --release 17 -cp "$platform" -d "$scratch/classes" "$fixture/TestKeyboard.java"
"$build_tools/d8" --min-api 33 --lib "$platform" --output "$scratch" "$scratch"/classes/org/drover/tests/keyboard/*.class
"$build_tools/aapt2" compile --dir "$fixture/res" -o "$scratch/resources.zip"
"$build_tools/aapt2" link -I "$platform" --manifest "$fixture/AndroidManifest.xml" \
  -o "$scratch/unsigned.apk" "$scratch/resources.zip"
(cd "$scratch" && zip -q unsigned.apk classes.dex)
"$JAVA_HOME/bin/keytool" -genkeypair -keystore "$scratch/test.keystore" -storepass android \
  -alias keyboard -keyalg RSA -dname 'CN=Drover test keyboard' -validity 1
"$build_tools/zipalign" -f 4 "$scratch/unsigned.apk" "$scratch/aligned.apk"
"$build_tools/apksigner" sign --ks "$scratch/test.keystore" --ks-pass pass:android \
  --out "$scratch/keyboard.apk" "$scratch/aligned.apk"
adb install "$scratch/keyboard.apk"
installed=true
python3 scripts/check-native-android-text-editing.py
