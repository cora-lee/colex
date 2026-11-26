// Plain C# tests for the UnityEngine-free core (Assets/Mammoth/Runtime/Core).
// Run with Mono:  bash Tools/core-tests/run.sh
using System;
using System.Collections.Generic;
using Mammoth;

static class CoreTests
{
    static int failures;

    static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) failures++;
    }

    static MammothState S(string session = "a", double updated = 1, int jumps = 0, int grabs = 0,
                          bool running = true, bool calibrated = true, string mode = "team", int lane = 0, float travel = 5f)
        => new MammothState { protocolVersion = 3, sessionId = session, mode = mode, running = running, calibrated = calibrated,
                              updated = updated, jumps = jumps, grabs = grabs, lane = lane, travelSpeed = travel };

    static int Main()
    {
        // Settings
        var cfg = new MammothSettings();
        Check(cfg.Validate().Count == 0, "default settings are valid");
        var bad = new MammothSettings { clap_close = 1f, clap_open = .9f };
        Check(bad.Validate().Count == 1, "clap_close >= clap_open rejected");
        var outOfRange = new MammothSettings { jump_height = 9f };
        Check(outOfRange.Validate().Count == 1, "out-of-range jump_height rejected");

        // First connect sets baseline, no history replay
        var t = new MammothEventTracker();
        int j = 0, g = 0;
        t.OnJump += () => j++;
        t.OnGrab += () => g++;
        t.Push(S(jumps: 5, grabs: 3), 0);
        Check(j == 0 && g == 0, "first connect does not replay counts");

        t.Push(S(updated: 2, jumps: 6, grabs: 3), .03);
        Check(j == 1 && g == 0, "jump count increase emits one jump");

        t.Push(S(updated: 3, jumps: 7, grabs: 4), .06);
        Check(j == 2 && g == 1, "jump and grab in same frame both processed");

        t.Push(S(updated: 3, jumps: 9, grabs: 9), .09);
        Check(j == 2 && g == 1, "equal updated value ignored");
        t.Push(S(updated: 2.5, jumps: 9, grabs: 9), .1);
        Check(j == 2 && g == 1, "older updated value ignored");

        t.Push(S(updated: 4, jumps: 9, grabs: 4), .12);
        Check(j == 4, "increase of 2 emits 2 jumps");

        // Blocked states move the baseline without emitting
        t.Push(S(updated: 5, jumps: 10, calibrated: false), .15);
        Check(j == 4, "not calibrated blocks events");
        t.Push(S(updated: 6, jumps: 10), .18);
        Check(j == 4, "blocked increase not replayed after unblock");
        t.Push(S(updated: 7, jumps: 11, running: false), .2);
        Check(j == 4, "not running blocks events");
        t.Push(S(updated: 8, jumps: 12, mode: "jump"), .22);
        Check(j == 4 && !t.ControlAllowed(.22), "non-team mode blocks events and control");
        t.RequireTeamMode = false;
        t.Push(S(updated: 9, jumps: 13, mode: "jump"), .25);
        Check(j == 5, "solo mode allowed when RequireTeamMode is off");
        t.RequireTeamMode = true;

        // Session change resets baseline
        int sessions = 0;
        t.OnSessionChanged += _ => sessions++;
        t.Push(S(session: "b", updated: 1, jumps: 0, grabs: 0), .3);
        Check(sessions == 1 && j == 5, "new session resets baseline, accepts lower updated");
        t.Push(S(session: "b", updated: 2, jumps: 1), .33);
        Check(j == 6, "events counted in new session");

        // Missing metadata
        t.Push(S(session: "b", updated: 3, jumps: 2, mode: ""), .36);
        Check(j == 6, "missing mode blocks events");

        // Protocol mismatch
        var wrong = S(session: "b", updated: 4, jumps: 3); wrong.protocolVersion = 2;
        Check(!t.Push(wrong, .4) && j == 6, "wrong protocolVersion rejected");

        // Timeout
        t.Push(S(session: "b", updated: 5, jumps: 2, lane: -1, travel: 8f), 1.0);
        Check(t.Connected(1.4) && Math.Abs(t.TravelSpeed(1.4) - 8f) < 1e-6, "fresh state gives travel speed");
        Check(!t.Connected(1.6) && t.TravelSpeed(1.6) == 0f, "no state within 0.5 s => disconnected, speed 0");
        Check(t.Lane == -1, "lane kept while disconnected");

        // Motion
        var m = new MammothMotion();
        Check(m.Lateral(0, 0, cfg) == 0f, "lane 0 at x=0");
        m.Lateral(1, 1.0, cfg);
        float mid = m.Lateral(1, 1.0 + cfg.lane_time / 2, cfg);
        Check(Math.Abs(mid - cfg.lane_width / 2) < 1e-4, "smoothstep halfway at lane_time/2");
        Check(Math.Abs(m.Lateral(1, 1.0 + cfg.lane_time, cfg) - cfg.lane_width) < 1e-5, "reaches lane_width after lane_time");
        m.StartJump(5.0);
        Check(Math.Abs(m.JumpOffset(5.0 + cfg.jump_duration / 2, cfg) - cfg.jump_height) < 1e-4, "jump peak equals jump_height");
        Check(m.JumpOffset(5.0 + cfg.jump_duration + .01, cfg) == 0f && !m.Airborne(6.5, cfg), "lands after jump_duration");

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }
}
