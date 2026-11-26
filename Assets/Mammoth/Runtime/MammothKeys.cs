using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Mammoth
{
    /// <summary>Keyboard test input that works with either the new Input System or the legacy Input Manager.</summary>
    public static class MammothKeys
    {
        public static bool Held(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Find(key);
            return k != null && k.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(key);
#else
            return false;
#endif
        }

        public static bool Down(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Find(key);
            return k != null && k.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(key);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Controls.KeyControl Find(KeyCode key)
        {
            var kb = Keyboard.current;
            if (kb == null) return null;
            switch (key)
            {
                case KeyCode.W: return kb.wKey;
                case KeyCode.S: return kb.sKey;
                case KeyCode.A: return kb.aKey;
                case KeyCode.D: return kb.dKey;
                case KeyCode.E: return kb.eKey;
                case KeyCode.R: return kb.rKey;
                case KeyCode.Space: return kb.spaceKey;
                default: return null;
            }
        }
#endif
    }
}
