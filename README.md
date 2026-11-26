# Mammoth Unity

Unity receiver and test scene for the mammoth gesture mapping (settings revision `a74d9e1e785f`, exported 2026-11-26).

This implements **integration option B** from `Handoff/UNITY_HANDOFF.md`: Unity polls the Python lab at `http://127.0.0.1:8765/state` and turns it into movement and one-shot jump/grab events. The pose model still runs in the Python lab; Unity does not run MediaPipe.

## Open the project
1. Unity Hub > Add > Add project from disk > select this folder. `ProjectSettings/ProjectVersion.txt` asks for Unity 6 (6000.0.23f1); it is written for Unity 6. Unity generates the rest of the project settings on first open.
2. Menu **Mammoth > Create Demo Scene**. This saves `Assets/Scenes/MammothDemo.unity` with a 3-lane track, a placeholder mammoth and rewards (yellow on the ground, blue in the air).
3. Press Play.
   - With the Python lab running on the same computer in `team` mode and calibrated, the HUD shows **LAB CONTROL** and gestures drive the mammoth.
   - Without the lab, use the keyboard: W/S speed, A/D lane, Space jump, E grab.

If Unity refuses the HTTP request, set Project Settings > Player > Other Settings > **Allow downloads over HTTP** to *Always allowed* (the lab only serves plain HTTP on loopback).

## Test without the camera
`python Tools/mock_state_server.py` serves the same `/state` address from the reference mapping in `Handoff/signals.py`, driven by a 20-second script (speed ramp, left/right turns, jump, clap in the air, standing clap, tracking loss). Each loop starts a new session, so you also see the counter baseline reset. Stop it before starting the real lab, since both use port 8765.

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
Assets/Mammoth/Runtime/        MonoBehaviours: receiver, controller, reward, HUD, follow camera
Assets/Mammoth/Editor/         Mammoth > Create Demo Scene
Assets/StreamingAssets/MammothMapping/   settings.json, manifest.json from the handoff
Handoff/                       Original handoff ZIP contents (reference Python + docs)
Tools/mock_state_server.py     Fake lab for testing without a camera
Tools/core-tests/              Plain C# tests for Core (Mono: bash Tools/core-tests/run.sh)
```

## Tests
- `bash Tools/core-tests/run.sh`: 26 checks on the event tracker, settings validation and motion math.
- `cd Handoff && python -m unittest -v`: reference mapping tests. `test_bundle_matches_schema` needs `handoff.py`, which is part of the lab and not in the export ZIP, so that one test errors here; the other 13 pass.
