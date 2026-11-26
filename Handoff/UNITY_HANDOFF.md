# Unity integration contract — gesture mapping v2

## Status
Pretrained MediaPipe Pose, custom mapping, no model fine-tuning. No Unity receiver/controller is implemented in this package. Browser mammoth and grab flash are test feedback. A grab signal is NOT awarded points: Unity must check for a reward within the chosen interaction range and decide whether collection succeeds.

## Roles and coordinates
Mirror image before inference. x right, y down. Divide frame into thirds; assign by shoulder midpoint x. Reject divider margins and duplicate people in one region. Left region=speed, center=hand steering, right=physical jump AND visual clap/grab. Face the camera. For steering, associate wrist 15 with shoulder 11 and wrist 16 with shoulder 12, then choose the pair whose shoulder x is smaller as screen-left. This gives the user's left in the mirrored front-facing view; do not flip again in Unity.
Required landmark visibility and presence >= confidence; coordinates must be finite and inside image. All roles need shoulders. Speed retains nose+wrists requirement and original calculations. Direction needs both wrists. Jump and clap both require shoulders and both wrists. Hips and ankles are not required.

## Calibration and sessions
All required roles must be visible for calibration seconds. Stand with hands lowered and apart. Calibration validates tracking; it does not record floor or ankle height. Recalibrate after switching modes or saving settings.
SessionId is a new UUID on calibration, mode change, settings save or camera start. Reset receiver counter baselines on session change. Calibration resets game-test counters and lane; it does not train a model. Solo modes mask unused roles; only selected role is required. Solo speed/direction calibration is a validity gate, not a learned hand-height baseline.

## Speed (unchanged)
W=max(abs(shoulder11.x-shoulder12.x),.035).
H=(shoulderMid.y-average(wristYs))/W.
Target throttle=clamp01((H-speed_low)/(speed_high-speed_low)); invalid input => 0.
dt=clamp(elapsed,0,.2); alpha=1-exp(-dt/smooth).
Throttle += (target-Throttle)*alpha.
Travel target=min_speed+Throttle*(max_speed-min_speed) if calibrated and tracked, else 0.
TravelSpeed += (travelTarget-TravelSpeed)*alpha (second stage, matching lab).

## Direction (replaces body lean)
For each wrist, H=(its connected shoulder.y-wrist.y)/W.
Both heights <= hand_reset => arm one change.
Exactly one height >= hand_trigger while armed => lane += -1 for left, +1 for right; clamp -1..1; disarm.
Both raised => no turn and disarm. Held hand => no repeated changes. Lower both to rearm. Tracking loss disarms, so an already-raised hand cannot trigger immediately on recovery.
Lateral target=lane*lane_width, transition duration lane_time. Browser easing is CSS ease on control-page preview and smoothstep on extended display; match the preferred feel explicitly in Unity.

## Raised-hands jump (v3; replaces physical jump)
For each wrist: handHeight=(its shoulder Y-wrist Y)/shoulder width.
After the jump animation lands, BOTH heights <= jump_hand_reset arm the next jump.
When armed and BOTH heights >= jump_hand_trigger, emit one jump, increment jumps and disarm.
Holding hands high never repeats. Losing either wrist disarms; lower both hands after tracking recovers.
Keep hands apart while lifting to avoid a grab. Clap during the mammoth's airborne animation to grab in the air. Jump and grab events can coexist.
Animation: phase=(now-jump_at)/jump_duration; normalized jumpHeight=4*phase*(1-phase) for phase in [0,1], otherwise zero. Multiply by jump_height for world units.

## Visual clap / grab
D=distance(wrist15,wrist16)/W in normalized image coordinates (camera aspect affects this proxy).
D >= clap_open arms a grab. D <= clap_close while armed and clap_cooldown elapsed emits grab, increments grabs, disarms. Holding hands together cannot repeat; separate them again. Hand tracking loss disarms.
No audio is used. This is hands-apart → hands-together detection; it can recognize a clap or a similar closing gesture. A very fast clap can be missed between camera frames. Start with a clear clap in front of the chest; tune close/open thresholds without making them overlap.
Jump and grab are independent: events may contain BOTH 'jump' and 'grab' in the same frame. Standing clap is valid. Grabbing does not force a jump.

## Receiver options (not yet implemented)
A. Port signals.py and load settings.json in Unity; keep exactly these names and verify against packaged tests.
B. Implement a local Unity HTTP client to /state at http://127.0.0.1:8765/state while Python lab is running. The server binds to loopback, not a LAN address. The ZIP does not include the MediaPipe model or full runtime.

/state v2 fields:
- protocolVersion=3, sessionId, mode, required, running, calibrated, visible[3].
- speed=throttle, travelSpeed=already smoothed units/s, lane=-1/0/1.
- leftHand/rightHand=normalized heights; directionReady.
- jumpTracked, jumpHandHeight=min of the two shoulder-relative wrist heights (or null), jumpReady, jumps, jumpHeight=normalized animation height.
- clapTracked, handDistance, grabReady, grabs, grabActive=0.5-second UI flash.
- events=array of transient events; event=last transient event only, for backward display compatibility.
- updated=server monotonic time; never compare against a different clock.

Use state for movement. For events use increases in jumps/grabs within the SAME sessionId, not transient event fields or grabActive (a flash is not a repeated event). Establish baseline on first connect or session reset; do not replay historical counts. If jump and grab counts both rise, process both. Reward collection checks belong to Unity.
Use local receive timeout (start with .5s and test), ignore old updated values, block new events when not running/calibrated or session metadata missing. Team game should require mode=='team'. Do not double-apply smoothing, normalization or jump_height.

## Test checklist
Left/right hand mapped correctly; both hands up cause no turn; hands down rearm; physical jump causes one event; crouching and one-foot lifts do not; holding hands together causes one grab; separate/clap repeats; jump+clap can trigger together; occlusion recovery does not spuriously trigger; three people cannot control other regions; recalibration/session reset clears receiver baselines. Validate game-unit scale, physical jump comfort and reward reach in the actual Unity scene.
