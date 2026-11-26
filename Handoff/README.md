# Mammoth mapping handoff

Exported: 2026-11-26T21:23:59.663498+00:00
Settings revision: a74d9e1e785f

This is a snapshot of SAVED settings. It is not a trained model, not a Unity package, and not evidence of a successful three-person test. No camera images or personal calibration values are included.

## Package contents
- settings.json: exact numeric settings; keep names unchanged.
- manifest.json: units/meaning/ranges, roles, version and timestamp.
- signals.py / settings.py: authoritative, dependency-free reference mapping implementation.
- test_signals.py / test_settings.py: reference behavioral tests; run python -m unittest -v.
- UNITY_HANDOFF.md: formulas, rearming, loss behavior and integration options.

## Current gestures
Speed unchanged; raise one hand to steer; raise both hands to jump; visual clap to grab. Old lean and physical-jump settings are obsolete.

## Team workflow
1. Mapping owner: test with three people, change one value at a time, Save & apply, recalibrate, retest, then export.
2. Share this entire ZIP and your test notes with the Unity owner. Keep the revision in your notes.
3. Unity owner: explicitly implement one integration option in UNITY_HANDOFF.md. Copying settings into Assets alone does NOT connect the elephant.
4. Agree how one test-world unit maps to Unity scene scale. Recheck speed, lane spacing and jump height in the actual environment.
5. Calibrate each play session. Keep shoulders and both wrists visible.

Suggested test notes: revision | participants/positions | camera distance | action | expected result | observed result | next change.
