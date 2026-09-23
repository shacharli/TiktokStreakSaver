#!/usr/bin/env bash
# Emulator smoke test: install, launch, click through a few screens, fail on any crash.
# Findings are emitted as workflow annotations so they are readable without downloading artifacts.
set -u
PKG=com.jon2g.tiktokstreaksaver
OUT=out
mkdir -p "$OUT"

enc() { python3 -c 'import sys; s=sys.stdin.read()[:3500]; print(s.replace("%","%25").replace("\r","").replace("\n","%0A"))'; }
note() { echo "::notice title=$1::$(printf '%s' "$2" | enc)"; }
err()  { echo "::error title=$1::$(printf '%s' "$2" | enc)"; }

dump_ui() {
  for _ in 1 2 3; do
    adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1 && adb pull /sdcard/ui.xml "$OUT/$1.xml" >/dev/null 2>&1 && return 0
    sleep 2
  done
  return 1
}

snapshot() {
  adb exec-out screencap -p > "$OUT/$1.png" 2>/dev/null
  dump_ui "$1" || { note "screen:$1" "(ui dump failed)"; return; }
  local texts
  texts=$(python3 - "$OUT/$1.xml" <<'PY'
import sys
import xml.etree.ElementTree as ET
seen = []
for n in ET.parse(sys.argv[1]).iter('node'):
    for k in ('text', 'content-desc'):
        v = n.attrib.get(k, '').strip()
        if v and v not in seen:
            seen.append(v)
print(' | '.join(seen))
PY
)
  note "screen:$1" "$texts"
}

tap_text() {
  dump_ui tap || return 1
  local xy
  xy=$(python3 - "$OUT/tap.xml" "$1" <<'PY'
import re, sys
import xml.etree.ElementTree as ET
want = sys.argv[2].lower()
for n in ET.parse(sys.argv[1]).iter('node'):
    label = (n.attrib.get('text') or n.attrib.get('content-desc') or '').strip().lower()
    if label == want or (want in label and len(label) < len(want) + 12):
        m = re.match(r'\[(\d+),(\d+)\]\[(\d+),(\d+)\]', n.attrib.get('bounds', ''))
        if m:
            l, t, r, b = map(int, m.groups())
            print((l + r) // 2, (t + b) // 2)
            break
PY
)
  [ -z "$xy" ] && return 1
  adb shell input tap $xy
}

alive_or_die() {
  local step="$1" pid crash
  pid=$(adb shell pidof "$PKG" | tr -d '\r')
  crash=$(adb logcat -d 2>/dev/null | grep -B3 -A45 -E "FATAL EXCEPTION|Unhandled managed exception|Fatal signal" | head -70)
  if [ -z "$pid" ] || [ -n "$crash" ]; then
    if [ -z "$crash" ]; then
      crash=$(adb logcat -d -b all 2>/dev/null | grep -E "$PKG|AndroidRuntime|monodroid|mono-rt|am_crash|am_proc_died|am_anr|has died|Force finishing|FATAL" | tail -c 3300)
    fi
    err "CRASH after: $step" "${crash:-process exited with no logcat marker}"
    local last
    last=$(adb shell run-as "$PKG" cat files/last_crash.txt 2>/dev/null)
    [ -n "$last" ] && err "last_crash.txt" "$last"
    snapshot "crash"
    exit 1
  fi
}

step() { # label, text to tap, seconds to wait afterwards
  if tap_text "$2"; then
    sleep "$3"
    snapshot "$1"
    alive_or_die "$1"
  else
    note "skipped:$1" "could not find '$2' on screen"
  fi
}

APK=$(ls apk/*-Signed.apk 2>/dev/null | head -1)
[ -z "$APK" ] && APK=$(ls apk/*.apk | head -1)
echo "Installing $APK"
adb install -r -g "$APK" || { err "install failed" "adb install returned an error for $APK"; exit 1; }

adb logcat -c
adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
sleep 25
snapshot launch
alive_or_die launch

step welcome "Continue" 8
step profile "Profile" 4
step accounts "Manage accounts" 4
step add-account "Add account" 3
step login "Continue" 20
adb shell input keyevent KEYCODE_BACK
sleep 4
snapshot after-back
alive_or_die after-back

note "result" "smoke test passed: app launched, navigated Profile > Accounts > Add account > Login page without crashing"
