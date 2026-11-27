using UnityEngine;

namespace Mammoth
{
    /// <summary>On-screen status for test sessions (IMGUI, no extra packages needed).</summary>
    public class MammothDebugHud : MonoBehaviour
    {
        public MammothStateReceiver receiver;
        public MammothController controller;
        public bool show = true;

        GUIStyle style;

        void Start()
        {
            if (receiver == null) receiver = FindFirstObjectByType<MammothStateReceiver>();
            if (controller == null) controller = FindFirstObjectByType<MammothController>();
        }

        void OnGUI()
        {
            if (!show) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15, richText = true };
                style.padding = new RectOffset(12, 12, 10, 10);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>Mammoth</b>  settings rev " + MammothSettingsAsset.Revision);

            if (receiver != null)
            {
                var s = receiver.State;
                string link = receiver.ControlAllowed ? "<color=#7CFC7C>LAB CONTROL</color>"
                            : receiver.Connected ? "<color=#FFD54A>LAB CONNECTED, BLOCKED</color>"
                            : "<color=#FF7A7A>NO LAB</color>";
                sb.AppendLine(link + "  " + receiver.Status);
                if (s != null)
                {
                    sb.AppendLine("mode " + s.mode + "  session " + Short(s.sessionId) + "  calibrated " + s.calibrated);
                    sb.AppendLine("visible " + Vis(s.visible) + "  throttle " + s.speed.ToString("0.00") + "  speed " + s.travelSpeed.ToString("0.0") + " u/s");
                    sb.AppendLine("lane " + s.lane + "  dirReady " + s.directionReady + "  jumpReady " + s.jumpReady + "  grabReady " + s.grabReady);
                }
            }

            if (controller != null)
            {
                if (receiver == null || !receiver.ControlAllowed)
                    sb.AppendLine("keyboard: W/S speed  A/D steer  Space jump  E grab");
                bool flash = Time.time - controller.LastGrabTime < .5f;
                string grab = flash ? (controller.LastGrabHit ? "  <color=#7CFC7C>GRAB HIT</color>" : "  <color=#FFD54A>grab missed</color>") : "";
                sb.Append("score " + controller.Score + "  jumps " + controller.Jumps + "  grabs " + controller.GrabAttempts + (controller.Airborne ? "  airborne" : "") + grab + "  z " + controller.Distance.ToString("0") + "  x " + controller.Lateral.ToString("0.0"));
            }

            GUI.Box(new Rect(12, 12, 560, 150), sb.ToString(), style);
        }

        static string Short(string id) => string.IsNullOrEmpty(id) ? "-" : id.Substring(0, Mathf.Min(8, id.Length));

        static string Vis(bool[] v)
        {
            if (v == null || v.Length < 3) return "-";
            return (v[0] ? "S" : "_") + (v[1] ? "D" : "_") + (v[2] ? "J" : "_");
        }
    }
}
