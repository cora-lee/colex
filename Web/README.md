# Browser camera (gesture capture runs in the browser, not in Unity or Python)

This page runs the camera and the full v3 gesture mapping (a line-by-line port of
`Handoff/signals.py` and `Handoff/settings.py`) in the browser, using
[MediaPipe Tasks Vision](https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker)
for pose detection. It then POSTs each computed state straight to Unity, which listens
for it locally. No separate Python lab process is needed for this path.

Same v3 gestures as before: left region = speed, center = steer, right = raise both
hands to jump and clap to grab.

## Run it

1. From the **repository root** (not inside `Web/`), start any local static server, for example:
   ```
   python3 -m http.server 8080
   ```
   Serving from the repo root matters: the page reads `Assets/StreamingAssets/MammothMapping/settings.json`
   so it always uses the same thresholds Unity does.
2. Open `http://localhost:8080/Web/index.html` in Chrome or Safari (camera access needs
   a "secure context" — `localhost` qualifies, a bare `file://` page does not reliably).
3. In Unity, open the `MammothDemo` scene, select **Mammoth Systems**, and on its
   `MammothStateReceiver` make sure **Mode** is set to **Listen**. Press Play.
4. On the web page, press **Start camera**, allow camera access, and stand so all
   three regions are visible with hands down. Hold still through the countdown.
5. The HUD shows "Unity: connected" once Unity's listener receives a state.

## How the handoff works

- The browser computes the *exact* same JSON shape Unity already expects
  (`protocolVersion`, `sessionId`, `speed`, `travelSpeed`, `lane`, `jumps`, `grabs`, ...),
  the one documented in `Handoff/UNITY_HANDOFF.md`.
- It POSTs that JSON about 30 times/second to `http://127.0.0.1:8765/state`.
- `MammothStateReceiver` in **Listen** mode runs a tiny local HTTP listener at that
  same address (loopback only) and feeds the POSTed state into the same
  `MammothEventTracker` the old polling mode used, so `MammothController` and the HUD
  don't need to know which source is active.
- Session handling, event counting (jumps/grabs), calibration and the team-mode gate
  all behave the same as the Python lab, because the logic is a direct port.
- The page also POSTs a small mirrored JPEG thumbnail to `http://127.0.0.1:8765/frame`
  about 5 times/second. `MammothStateReceiver` stores the latest one, and
  `MammothCameraPreview` draws it as a small overlay in the bottom-right corner of the
  actual Unity game screen, so players can see their own tracking without it covering
  the route. This is separate traffic from `/state` so the 30 Hz gesture JSON never
  carries image data.

## Switching back to the Python lab

If you'd rather run the original Python lab (or the mock test server in `Tools/`),
set the receiver's **Mode** to **Poll** instead — that restores the original
"Unity polls an external process" behavior from before this page existed.

## Limitations of this port

- Uses MediaPipe Tasks Vision's `visibility` field for both the "visibility" and
  "presence" checks `signals.py` does separately; close enough for tuning but not
  byte-identical to the Python/OpenCV pipeline.
- Requires internet access once, to fetch the MediaPipe WASM runtime and pose model
  from Google's CDN (cached by the browser afterward).
- Like the Python lab, `calibration` only gates on tracking, not on hand height —
  matching `signals.py`'s behavior exactly.
