// Browser-side port of Handoff/signals.py + Handoff/settings.py (v3 gestures),
// wired to MediaPipe Tasks Vision (PoseLandmarker, up to 3 people) and POSTing
// the same /state v3 contract that Assets/Mammoth/Runtime/MammothStateReceiver.cs
// (Listen mode) accepts. Camera + gesture recognition stays in the browser;
// Unity only receives the already-computed state. Loopback only.

import { FilesetResolver, PoseLandmarker } from "https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14";

const UNITY_URL = "http://127.0.0.1:8765/state";
const FRAME_URL = "http://127.0.0.1:8765/frame";
const SETTINGS_URL = "../Assets/StreamingAssets/MammothMapping/settings.json"; // single source of truth with Unity

const DEFAULTS = {
  confidence: 0.55, margin: 0.025, calibration: 3.0,
  speed_low: -0.8, speed_high: 1.0, smooth: 0.3,
  min_speed: 2.0, max_speed: 12.0,
  hand_trigger: 0.15, hand_reset: -0.15,
  lane_width: 2.0, lane_time: 0.25,
  jump_hand_trigger: 0.8, jump_hand_reset: -0.15,
  clap_close: 0.35, clap_open: 0.9, clap_cooldown: 0.35,
  jump_duration: 0.9, jump_height: 1.5,
};

const statusEl = document.getElementById("status");
const connEl = document.getElementById("connState");
const zoneEls = [document.getElementById("zoneLeft"), document.getElementById("zoneCenter"), document.getElementById("zoneRight")];
const startBtn = document.getElementById("startBtn");
const calibrateBtn = document.getElementById("calibrateBtn");
const video = document.getElementById("video");
const overlay = document.getElementById("overlay");
const ctx2d = overlay.getContext("2d");

// Small off-screen canvas used only to downscale a thumbnail for Unity's
// bottom-right camera-preview overlay (Mammoth > ... > MammothCameraPreview).
// Kept separate from the visible <video>/<canvas> pair above.
const frameCanvas = document.createElement("canvas");
frameCanvas.width = 220;
frameCanvas.height = 165;
const frameCtx = frameCanvas.getContext("2d");

function log(msg) { statusEl.textContent = msg; }

async function loadConfig() {
  try {
    const res = await fetch(SETTINGS_URL, { cache: "no-store" });
    if (!res.ok) throw new Error(res.status);
    const json = await res.json();
    log("설정값을 Assets/StreamingAssets/MammothMapping/settings.json 에서 불러왔습니다.");
    return { ...DEFAULTS, ...json };
  } catch (e) {
    log("settings.json을 못 찾아서 기본값을 씁니다 (" + e + "). 이 페이지를 저장소 루트에서 서빙 중인지 확인하세요.");
    return { ...DEFAULTS };
  }
}

// ---------- Pose landmark helpers (indices match MediaPipe BlazePose: 0 nose, 11/12 shoulders, 15/16 wrists) ----------

function good(lm, ids, cfg) {
  return ids.every(i => {
    const p = lm[i];
    if (!p) return false;
    const vis = p.visibility ?? 1;
    return vis >= cfg.confidence && Number.isFinite(p.x) && Number.isFinite(p.y) && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1;
  });
}

/// Mirrors signals.observe(): buckets up to 3 detected people into left/center/right
/// zones by shoulder-midpoint x, in a MIRRORED image (so this matches the x-right,
/// y-down, mirror-before-inference convention in UNITY_HANDOFF.md).
function observe(posesLandmarks, cfg) {
  const zones = [[], [], []];
  for (const lm of posesLandmarks) {
    if (!good(lm, [11, 12], cfg)) continue;
    const x = (lm[11].x + lm[12].x) / 2;
    if (!(x >= 0 && x < 1)) continue;
    const zone = Math.min(2, Math.floor(x * 3));
    if (Math.min(Math.abs(x - 1 / 3), Math.abs(x - 2 / 3)) < cfg.margin) continue;

    const sy = (lm[11].y + lm[12].y) / 2;
    const width = Math.max(Math.abs(lm[11].x - lm[12].x), 0.035);
    const hands = good(lm, [15, 16], cfg);
    const left = lm[11].x < lm[12].x ? 15 : 16;
    const right = left === 15 ? 16 : 15;
    const ls = left === 15 ? 11 : 12;
    const rs = left === 15 ? 12 : 11;

    const value = {
      height: hands ? (sy - (lm[15].y + lm[16].y) / 2) / width : 0,
      leftHeight: hands ? (lm[ls].y - lm[left].y) / width : 0,
      rightHeight: hands ? (lm[rs].y - lm[right].y) / width : 0,
      handsValid: hands, jumpValid: hands, clapValid: hands,
      handDistance: hands ? Math.hypot(lm[15].x - lm[16].x, lm[15].y - lm[16].y) / width : 0,
    };
    const valid = zone === 0 ? good(lm, [0, 15, 16], cfg) : hands;
    zones[zone].push(valid ? value : null);
  }
  return zones.map(items => (items.length === 1 ? items[0] : null));
}

/// Direct port of signals.Mapping (team mode only; this page always runs all 3 roles).
class Mapping {
  constructor(cfg) {
    this.cfg = cfg;
    this.reset();
  }
  reset() {
    this.sessionId = crypto.randomUUID().replace(/-/g, "");
    this.neutral = null;
    this.samples = [];
    this.calibrating = true;
    this.speed = 0; this.travelSpeed = 0;
    this.lane = 0;
    this.directionReady = false; this.jumpReady = false; this.grabReady = false;
    this.jumpAt = -100; this.grabAt = -100;
    this.jumpCount = 0; this.grabCount = 0;
    this.last = null;
  }
  update(obs, now) {
    const cfg = this.cfg;
    const [a, b, c] = obs;
    const dt = this.last != null ? Math.min(0.2, Math.max(0, now - this.last)) : 0.03;
    this.last = now;

    let ready = obs.every(o => o != null);
    ready = ready && !!(c && c.jumpValid && c.clapValid);

    if (this.calibrating) {
      if (ready) {
        if (this.samples.length === 0) this.samples.push([now, c]);
        else this.samples.push([now, c]);
        if (now - this.samples[0][0] >= cfg.calibration) { this.neutral = {}; this.calibrating = false; }
      } else {
        this.samples = [];
      }
    }
    const active = this.neutral != null;

    const target = active && a ? clamp01((a.height - cfg.speed_low) / (cfg.speed_high - cfg.speed_low)) : 0;
    const alpha = 1 - Math.exp(-dt / cfg.smooth);
    this.speed += (target - this.speed) * alpha;
    const travelTarget = active && a ? cfg.min_speed + this.speed * (cfg.max_speed - cfg.min_speed) : 0;
    this.travelSpeed += (travelTarget - this.travelSpeed) * alpha;

    const events = [];
    if (!active || b == null) this.directionReady = false;
    else if (Math.max(b.leftHeight, b.rightHeight) <= cfg.hand_reset) this.directionReady = true;
    else if (this.directionReady) {
      const left = b.leftHeight >= cfg.hand_trigger, right = b.rightHeight >= cfg.hand_trigger;
      if (left && right) this.directionReady = false;
      else if (left || right) {
        this.lane = Math.max(-1, Math.min(1, this.lane + (left ? -1 : 1)));
        this.directionReady = false;
        events.push(left ? "left" : "right");
      }
    }

    const jumpHands = active && c && c.jumpValid ? [c.leftHeight, c.rightHeight] : null;
    if (jumpHands == null) this.jumpReady = false;
    else if (now - this.jumpAt >= cfg.jump_duration) {
      if (Math.max(...jumpHands) <= cfg.jump_hand_reset) this.jumpReady = true;
      else if (this.jumpReady && Math.min(...jumpHands) >= cfg.jump_hand_trigger) {
        this.jumpAt = now; this.jumpCount++; this.jumpReady = false; events.push("jump");
      }
    }

    if (!active || !c || !c.clapValid) this.grabReady = false;
    else if (c.handDistance >= cfg.clap_open) this.grabReady = true;
    else if (c.handDistance <= cfg.clap_close && this.grabReady && now - this.grabAt >= cfg.clap_cooldown) {
      this.grabAt = now; this.grabCount++; this.grabReady = false; events.push("grab");
    }

    const phase = (now - this.jumpAt) / cfg.jump_duration;
    return {
      protocolVersion: 3, sessionId: this.sessionId, mode: "team", required: [0, 1, 2],
      running: true, calibrated: active, calibrating: this.calibrating,
      visible: obs.map(o => o != null),
      speed: Math.round(this.speed * 1000) / 1000, travelSpeed: this.travelSpeed,
      leftHand: b ? b.leftHeight : 0, rightHand: b ? b.rightHeight : 0,
      directionReady: this.directionReady, lane: this.lane,
      jumpTracked: !!(c && c.jumpValid),
      jumpHandHeight: jumpHands ? Math.min(...jumpHands) : 0,
      jumpReady: this.jumpReady, jumps: this.jumpCount,
      jumpHeight: phase >= 0 && phase <= 1 ? Math.max(0, 4 * phase * (1 - phase)) : 0,
      clapTracked: !!(c && c.clapValid),
      handDistance: c && c.clapValid ? c.handDistance : 0,
      grabReady: this.grabReady, grabs: this.grabCount,
      grabActive: active && now - this.grabAt < 0.5,
      events, event: events.length ? events[events.length - 1] : "",
      updated: now,
    };
  }
}

function clamp01(v) { return Math.max(0, Math.min(1, v)); }

// ---------- Wiring: camera -> PoseLandmarker -> Mapping -> POST to Unity ----------

async function main() {
  const cfg = await loadConfig();
  const mapping = new Mapping(cfg);

  startBtn.addEventListener("click", () => start(cfg, mapping).catch(e => log("시작 실패: " + e)));
  calibrateBtn.addEventListener("click", () => { mapping.reset(); log("다시 보정 중… 세 영역 모두 가만히 서 계세요."); });
}

async function start(cfg, mapping) {
  startBtn.disabled = true;
  log("MediaPipe 모델을 불러오는 중…");

  const vision = await FilesetResolver.forVisionTasks(
    "https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14/wasm"
  );
  const landmarker = await PoseLandmarker.createFromOptions(vision, {
    baseOptions: {
      modelAssetPath: "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task",
      delegate: "GPU",
    },
    runningMode: "VIDEO",
    numPoses: 3,
  });

  const stream = await navigator.mediaDevices.getUserMedia({ video: { width: 960, height: 720 }, audio: false });
  video.srcObject = stream;
  await video.play();
  overlay.width = video.videoWidth; overlay.height = video.videoHeight;

  calibrateBtn.disabled = false;
  log("보정 중… 세 영역(왼쪽/가운데/오른쪽)에 각자 서서 양손을 내리고 가만히 계세요.");

  let lastSend = 0;
  let lastFrameSend = 0;
  function frame(tsMs) {
    const now = performance.now() / 1000;
    const result = landmarker.detectForVideo(video, tsMs);

    // Mirror x so this matches "mirror image before inference" (video element is
    // visually mirrored via CSS only; landmarks from the unmirrored frame need
    // x flipped here to match the same convention signals.py assumes).
    const posesLandmarks = result.landmarks.map(lm => lm.map(p => ({ ...p, x: 1 - p.x })));

    const obs = observe(posesLandmarks, cfg);
    const state = mapping.update(obs, now);
    drawOverlay(result, cfg);
    updateHud(obs, state, mapping);

    if (tsMs - lastSend > 1000 / 30) {
      lastSend = tsMs;
      sendToUnity(state);
    }
    // Thumbnail for Unity's in-game camera preview goes over separately, at a
    // much lower rate (5 fps) — it's just for the player to glance at, not for
    // gesture recognition, so there's no reason to burden /state with it.
    if (tsMs - lastFrameSend > 1000 / 5) {
      lastFrameSend = tsMs;
      sendFrameToUnity();
    }
    video.requestVideoFrameCallback ? video.requestVideoFrameCallback((_, meta) => frame(meta.mediaTime * 1000)) : requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
}

function drawOverlay(result, cfg) {
  ctx2d.clearRect(0, 0, overlay.width, overlay.height);
  ctx2d.save();
  ctx2d.translate(overlay.width, 0);
  ctx2d.scale(-1, 1); // draw in the same mirrored space as the <video>
  ctx2d.strokeStyle = "rgba(255,255,255,.35)";
  ctx2d.lineWidth = 2;
  for (const f of [1 / 3, 2 / 3]) {
    ctx2d.beginPath();
    ctx2d.moveTo(f * overlay.width, 0);
    ctx2d.lineTo(f * overlay.width, overlay.height);
    ctx2d.stroke();
  }
  ctx2d.fillStyle = "#7cfc7c";
  for (const lm of result.landmarks) {
    for (const i of [0, 11, 12, 15, 16]) {
      const p = lm[i];
      if (!p) continue;
      ctx2d.beginPath();
      ctx2d.arc(p.x * overlay.width, p.y * overlay.height, 5, 0, Math.PI * 2);
      ctx2d.fill();
    }
  }
  ctx2d.restore();
}

function updateHud(obs, state, mapping) {
  const labels = ["speed", "steer", "jump/grab"];
  obs.forEach((o, i) => {
    const el = zoneEls[i];
    if (o == null) { el.innerHTML = `<span class="bad">인식 안 됨</span>`; return; }
    if (i === 0) el.innerHTML = `<span class="ok">OK</span>  height ${o.height.toFixed(2)}`;
    if (i === 1) el.innerHTML = `<span class="ok">OK</span>  L ${o.leftHeight.toFixed(2)} / R ${o.rightHeight.toFixed(2)}`;
    if (i === 2) el.innerHTML = `<span class="ok">OK</span>  jump ${state.jumpReady ? "armed" : "-"} / grab ${state.grabReady ? "armed" : "-"}`;
  });

  let msg = mapping.calibrating
    ? `보정 중… ${Math.max(0, cfgCountdown(mapping)).toFixed(1)}초 남음`
    : `연결됨 — 세션 ${state.sessionId.slice(0, 8)} / speed ${state.speed.toFixed(2)} / lane ${state.lane} / jumps ${state.jumps} / grabs ${state.grabs}`;
  log(msg);
}

function cfgCountdown(mapping) {
  if (!mapping.samples.length) return mapping.cfg.calibration;
  return mapping.cfg.calibration - (mapping.samples[mapping.samples.length - 1][0] - mapping.samples[0][0]);
}

function sendFrameToUnity() {
  // Mirror it, same as the on-screen <video>, so the preview in the corner of
  // the actual game looks the same way round the player already sees it.
  frameCtx.save();
  frameCtx.translate(frameCanvas.width, 0);
  frameCtx.scale(-1, 1);
  frameCtx.drawImage(video, 0, 0, frameCanvas.width, frameCanvas.height);
  frameCtx.restore();
  frameCanvas.toBlob(blob => {
    if (!blob) return;
    fetch(FRAME_URL, { method: "POST", headers: { "Content-Type": "image/jpeg" }, body: blob }).catch(() => { /* best-effort */ });
  }, "image/jpeg", 0.5);
}

let unityOk = false;
async function sendToUnity(state) {
  try {
    const res = await fetch(UNITY_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(state),
    });
    if (res.ok && !unityOk) { unityOk = true; connEl.textContent = "Unity: connected ✅"; }
    if (!res.ok) throw new Error(res.status);
  } catch (e) {
    if (unityOk) { unityOk = false; }
    connEl.textContent = "Unity: 연결 안 됨 (Play 중인지, Listen 모드인지 확인) — " + e;
  }
}

main();
