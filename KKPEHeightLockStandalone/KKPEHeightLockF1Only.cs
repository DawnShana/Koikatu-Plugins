using HarmonyLib;

namespace KKPEHeightLockStandalone
{
    /// <summary>
    /// Keep KKPE Height/Body Lock controllable only through BepInEx ConfigurationManager (F1).
    /// The original plugin still observes HeightLockEnabled changes in Update(), so turning the
    /// lock off from F1 continues to release captured height states and refresh characters.
    /// </summary>
    [HarmonyPatch(typeof(KKPEHeightLockStandalonePlugin), "OnGUI")]
    internal static class DisableLegacyControlWindowPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return false;
        }
    }

    /// <summary>
    /// Disable all legacy Ctrl+Shift hotkey actions while leaving configuration-backed state
    /// handling intact. HeightLockEnabled and BodyPreserveMode remain normal ConfigEntry values
    /// and are therefore still editable from ConfigurationManager/F1.
    /// </summary>
    [HarmonyPatch(typeof(KKPEHeightLockStandalonePlugin), "IsModifiedKeyDown")]
    internal static class DisableLegacyHotkeysPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }
}
