using UnityEditor;
using UnityEngine;

namespace Mammoth.EditorTools
{
    /// <summary>
    /// Menu: Mammoth > Preview UI > ... While playing, jumps straight to one
    /// screen of the game flow so every screen can be checked quickly.
    /// </summary>
    public static class MammothUIPreview
    {
        [MenuItem("Mammoth/Preview UI/1 Home")] static void A() => Show(MammothFlowState.Home);
        [MenuItem("Mammoth/Preview UI/1b Intro demo")] static void A2() => Show(MammothFlowState.Intro);
        [MenuItem("Mammoth/Preview UI/2 Rules")] static void B() => Show(MammothFlowState.Rules);
        [MenuItem("Mammoth/Preview UI/3 Practice select")] static void C() => Show(MammothFlowState.PracticeSelect);
        [MenuItem("Mammoth/Preview UI/4 Camera setup")] static void D() => Show(MammothFlowState.Setup);
        [MenuItem("Mammoth/Preview UI/5 Tutorial")] static void E() => Show(MammothFlowState.Tutorial);
        [MenuItem("Mammoth/Preview UI/6 Ready")] static void F() => Show(MammothFlowState.Ready);
        [MenuItem("Mammoth/Preview UI/7 Countdown")] static void G() => Show(MammothFlowState.Countdown);
        [MenuItem("Mammoth/Preview UI/8 Playing")] static void H() => Show(MammothFlowState.Playing);
        [MenuItem("Mammoth/Preview UI/9 Paused")] static void I() => Show(MammothFlowState.Paused);
        [MenuItem("Mammoth/Preview UI/10 Ending")] static void J() => Show(MammothFlowState.Ending);
        [MenuItem("Mammoth/Preview UI/11 Results")] static void K() => Show(MammothFlowState.Results);

        static void Show(MammothFlowState s)
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[Mammoth] Press Play first, then use Preview UI."); return; }
            var flow = Object.FindAnyObjectByType<MammothGameFlow>();
            if (flow != null) flow.DebugPreview(s);
        }
    }
}
