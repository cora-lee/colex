"""Mock /state server for testing the Unity receiver without a camera or three people.

Runs the reference mapping (Handoff/signals.py) on a scripted loop of synthetic poses
and serves it at http://127.0.0.1:8765/state, the same address as the Python lab.

    python Tools/mock_state_server.py            # scripted loop
    python Tools/mock_state_server.py --port 8765

This is a test double. It does not use MediaPipe and does not prove the gestures work
with real people; use the real lab for that.
"""
import argparse
import json
import os
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Handoff'))
from signals import Mapping  # noqa: E402
from settings import DEFAULTS, validate  # noqa: E402


def body(height=-.8, leftHeight=-.5, rightHeight=-.5, handDistance=1.2):
    return dict(height=height, leftHeight=leftHeight, rightHeight=rightHeight,
                handsValid=True, jumpValid=True, clapValid=True, handDistance=handDistance)


# (start second within the loop, label, speed body, direction body, jump body)
SCRIPT = [
    (0.0, 'calibrating', body(), body(), body()),
    (4.0, 'slow', body(height=-.8), body(), body()),
    (6.0, 'fast', body(height=1.0), body(), body()),
    (8.0, 'turn left', body(height=1.0), body(leftHeight=.5), body()),
    (8.6, 'rearm', body(height=1.0), body(), body()),
    (9.4, 'turn right', body(height=1.0), body(rightHeight=.5), body()),
    (10.0, 'rearm', body(height=1.0), body(), body()),
    (10.6, 'turn right', body(height=1.0), body(rightHeight=.5), body()),
    (11.2, 'rearm', body(height=.2), body(), body()),
    (12.0, 'jump', body(height=.2), body(), body(leftHeight=1.2, rightHeight=1.2)),
    (12.4, 'clap in air', body(height=.2), body(), body(leftHeight=1.2, rightHeight=1.2, handDistance=.2)),
    (13.2, 'hands down', body(height=.2), body(), body()),
    (14.0, 'standing clap', body(height=.2), body(), body(handDistance=.2)),
    (14.5, 'hands apart', body(height=.2), body(), body()),
    (15.5, 'turn left', body(height=.2), body(leftHeight=.5), body()),
    (16.0, 'rearm', body(height=.2), body(), body()),
    (16.5, 'turn left', body(height=.2), body(leftHeight=.5), body()),
    (17.0, 'rearm', body(height=.2), body(), body()),
    (18.0, 'tracking lost (center)', body(height=.2), None, body()),
]
LOOP = 20.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--port', type=int, default=8765)
    ap.add_argument('--settings', default=os.path.join(os.path.dirname(os.path.abspath(__file__)), '..',
                                                       'Assets', 'StreamingAssets', 'MammothMapping', 'settings.json'))
    args = ap.parse_args()
    with open(args.settings) as f:
        cfg = validate(json.load(f))

    lock = threading.Lock()
    shared = {'state': None}

    def loop():
        start = time.monotonic()
        mapping, loop_index = None, -1
        while True:
            now = time.monotonic()
            index = int((now - start) // LOOP)
            t = (now - start) % LOOP
            if index != loop_index:  # new loop => new calibration => new sessionId
                loop_index = index
                mapping = Mapping(cfg, 'team')
                mapping.calibrate()
                print('loop', index, 'session', mapping.session_id, flush=True)
            step = [s for s in SCRIPT if s[0] <= t][-1]
            out = mapping.update([step[2], step[3], step[4]], now)
            out.update(running=True, updated=now, step=step[1])
            for e in out['events']:
                print(f'{t:5.1f}s {step[1]:<22} event={e} jumps={out["jumps"]} grabs={out["grabs"]} lane={out["lane"]}', flush=True)
            with lock:
                shared['state'] = out
            time.sleep(1 / 30)

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path.split('?')[0] != '/state':
                self.send_error(404)
                return
            with lock:
                body_bytes = json.dumps(shared['state']).encode()
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(body_bytes)))
            self.end_headers()
            self.wfile.write(body_bytes)

        def log_message(self, *a):
            pass

    threading.Thread(target=loop, daemon=True).start()
    print(f'Mock lab serving http://127.0.0.1:{args.port}/state (Ctrl+C to stop)', flush=True)
    ThreadingHTTPServer(('127.0.0.1', args.port), Handler).serve_forever()


if __name__ == '__main__':
    main()
