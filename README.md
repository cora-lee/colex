# Mammoth Unity

Unity receiver and test scene for the mammoth gesture mapping (settings revision `a74d9e1e785f`, exported 2026-11-26).

The camera-driven gesture mapping (`Handoff/signals.py`, v3: left player = speed, center player = direction, right player = jump + grab) now reaches Unity one of two ways, picked by the `MammothStateReceiver.mode` field on the "Mammoth Systems" object in the scene:

- **Listen (default):** a browser page (`Web/`) runs the camera and MediaPipe pose model and POSTs the same `/state` JSON straight to an embedded HTTP server inside Unity. No Python lab needed; see `Web/README.md`.
- **Poll:** the original setup, Unity polls a Python lab (or `Tools/mock_state_server.py`) at `http://127.0.0.1:8765/state`. See `Handoff/UNITY_HANDOFF.md` for that contract.

Both modes feed the exact same `MammothEventTracker`, so the gesture math, session handling, and gating are identical either way.

## Open the project
1. Unity Hub > Add > Add project from disk > select this folder. `ProjectSettings/ProjectVersion.txt` asks for Unity 6; tested on 6000.6.10f1. Unity generates the rest of the project settings on first open.
2. Menu **Mammoth > Create Demo Scene**. This saves `Assets/Scenes/MammothDemo.unity` with a low-poly pastel ice track, a mammoth, and rewards (gold on the ground, pink/white in the air).
3. Press Play.
   - **Browser camera (default):** follow `Web/README.md` to start the camera page. Once it shows "Unity: connected", the HUD shows **LAB CONTROL** and gestures drive the mammoth.
   - **Python lab:** set the receiver's Mode to `Poll`, then run the lab (or `Tools/mock_state_server.py` below) in `team` mode.
   - Without either, use the keyboard: W/S speed, A/D lane, Space jump, E grab.

If Unity refuses an HTTP request in Poll mode, set Project Settings > Player > Other Settings > **Allow downloads over HTTP** to *Always allowed* (the lab only serves plain HTTP on loopback). In Listen mode Unity is the server, so this setting does not apply.

## Test without a real camera
`python Tools/mock_state_server.py` serves the same `/state` address from the reference mapping in `Handoff/signals.py`, driven by a 20-second script (speed ramp, left/right turns, jump, clap in the air, standing clap, tracking loss). Each loop starts a new session, so you also see the counter baseline reset. This only works with the receiver set to `Poll` mode, and only one source (the mock server, a real lab, or the browser page) should talk to port 8765 at a time.

## How the receiver follows the handoff contract
| Rule in UNITY_HANDOFF.md | Where |
|---|---|
| Events from increases in `jumps` / `grabs` within the same `sessionId`; baseline on first connect and on session change; no history replay | `MammothEventTracker.Push` |
| Ignore old `updated` values; never compare with the Unity clock | `MammothEventTracker.Push` |
| Local receive timeout, start with 0.5 s | `MammothStateReceiver.receiveTimeout` |
| Block events when not running / not calibrated / session metadata missing; team game requires `mode == "team"` | `MammothEventTracker.Gate`, `requireTeamMode` |
| `grabActive` and `events` are not used as events | not read for control |
| `travelSpeed` is already smoothed; not smoothed again | `MammothController.Update` |
| Lane target `lane * lane_width`, eased over `lane_time` (smoothstep) | `MammothMotion.Lateral` |
| Jump arc `4 * phase * (1 - phase) * jump_height`, applied once | `MammothMotion.JumpOffset` |
| A grab signal is not a reward; Unity checks reach | `MammothController.HandleGrab`, `grabRange` |

The jump arc is animated locally from each jump event (same formula and `jump_duration`) instead of sampling `jumpHeight` at the poll rate, so it stays smooth at the display frame rate.

## Things the team still has to agree on
- **World scale:** `MammothController.worldScale` = Unity units per test-world unit (default 1). Recheck speed, lane spacing and jump height in the real scene.
- **Grab reach:** `grabRange` (default 1.5 Unity units from the Grab Origin near the trunk).
- **Settings revision:** `Assets/StreamingAssets/MammothMapping/settings.json` must match the revision the lab is running. When the mapping owner exports a new ZIP, replace `settings.json` and `manifest.json` there and note the revision. The HUD shows the loaded revision.
- Three-person validation is still pending (see `Handoff/README.md`).

## Layout
```
Assets/Mammoth/Runtime/Core/   UnityEngine-free logic (settings, state, event tracker, motion)
Assets/Mammoth/Runtime/        MonoBehaviours: receiver (Poll/Listen), controller, reward, HUD, follow camera
Assets/Mammoth/Runtime/Art/    Low-poly mesh helpers (flat shading, procedural ice chunks)
Assets/Mammoth/Editor/         Mammoth > Create Demo Scene
Assets/StreamingAssets/MammothMapping/   settings.json, manifest.json from the handoff
Handoff/                       Original handoff ZIP contents (reference Python + docs)
Web/                           Browser camera page (MediaPipe pose + gesture mapping) for Listen mode
Tools/mock_state_server.py     Fake lab for testing Poll mode without a camera
Tools/core-tests/              Plain C# tests for Core (Mono: bash Tools/core-tests/run.sh)
```

## Tests
- `bash Tools/core-tests/run.sh`: 26 checks on the event tracker, settings validation and motion math.
- `cd Handoff && python -m unittest -v`: reference mapping tests. `test_bundle_matches_schema` needs `handoff.py`, which is part of the lab and not in the export ZIP, so that one test errors here; the other 13 pass.
