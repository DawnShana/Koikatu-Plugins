using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace KK_DragCoordinateLoadBridge
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("CharaStudio")]
    [BepInProcess("Koikatu")]
    [BepInDependency(DragAndDropGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(CoordinateLoadOptionGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "agumon.kk.dragcoordinateloadbridge";
        public const string PluginName = "KK Drag Coordinate Load Bridge";
        public const string PluginVersion = "1.2.1";

        private const string DragAndDropGuid = "keelhauled.draganddrop";
        private const string CoordinateLoadOptionGuid = "com.jim60105.kk.coordinateloadoption";

        private static ManualLogSource Log;
        private static Harmony HarmonyInstance;
        private static MethodBase DragAndDropCoordinateLoadMethod;
        private static MethodInfo DragAndDropGetSelectedCharactersMethod;
        private static CoordinateLoadOptionAdapter Adapter;
        private static MakerCoordinateLoadOptionAdapter MakerAdapter;
        private static bool MakerMode;
        private static bool RuntimeEnabled;
        private static ConfigEntry<bool> EnabledConfig;
        private static ConfigEntry<bool> VerboseConfig;
        private static ConfigEntry<string> RuntimeStatusConfig;
        private static ConfigEntry<string> LastActionConfig;
        private static ConfigEntry<int> ObservedDropCountConfig;
        private static string RuntimeTracePath;
        private static Plugin Instance;
        private static int DropGeneration;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            RuntimeTracePath = Path.Combine(Paths.ConfigPath, "KK_DragCoordinateLoadBridge.runtime.log");

            EnabledConfig = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enable drag-to-CLO selection workflow in CharaStudio and Koikatu Maker. Restart the current process after changing this setting.");
            VerboseConfig = Config.Bind(
                "Diagnostics",
                "Verbose logging",
                true,
                "Write diagnostic messages to BepInEx log and the bridge runtime log.");
            RuntimeStatusConfig = Config.Bind(
                "Diagnostics",
                "Runtime status",
                "Awake",
                "Read-only diagnostic value in normal use.");
            LastActionConfig = Config.Bind(
                "Diagnostics",
                "Last action",
                "No coordinate drop observed yet",
                "Read-only diagnostic value in normal use.");
            ObservedDropCountConfig = Config.Bind(
                "Diagnostics",
                "Observed coordinate drops",
                0,
                "Read-only diagnostic counter in normal use.");

            if (ObservedDropCountConfig != null)
                ObservedDropCountConfig.Value = 0;

            SetRuntimeStatus("Awake; waiting for Start");
            SetLastAction("No coordinate drop observed yet");
            Config.Save();
            BridgeInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void Start()
        {
            if (EnabledConfig != null && !EnabledConfig.Value)
            {
                SetRuntimeStatus("Disabled by configuration");
                return;
            }

            InitializeRuntime();
        }

        private void LateUpdate()
        {
            if (!RuntimeEnabled)
                return;

            if (MakerMode)
            {
                if (MakerAdapter != null)
                    MakerAdapter.MaintainPreparedLoadButton();
                return;
            }

            if (Adapter != null)
                Adapter.MaintainPreparedLoadButton();
        }

        private void OnDestroy()
        {
            RuntimeEnabled = false;
            DropGeneration++;
            if (object.ReferenceEquals(Instance, this))
                Instance = null;

            try
            {
                if (MakerAdapter != null)
                    MakerAdapter.ReleasePreparedLoadButton();
                if (Adapter != null)
                    Adapter.ReleasePreparedLoadButton();
            }
            catch (Exception ex)
            {
                BridgeError("Could not release prepared Load button during shutdown.", ex);
            }

            try
            {
                if (HarmonyInstance != null && DragAndDropCoordinateLoadMethod != null)
                    HarmonyInstance.Unpatch(DragAndDropCoordinateLoadMethod, HarmonyPatchType.Prefix, PluginGuid);
            }
            catch (Exception ex)
            {
                BridgeError("Could not unpatch bridge during shutdown.", ex);
            }
        }

        private static void InitializeRuntime()
        {
            try
            {
                PluginInfo dragInfo;
                PluginInfo cloInfo;

                if (!Chainloader.PluginInfos.TryGetValue(DragAndDropGuid, out dragInfo) ||
                    dragInfo == null || dragInfo.Instance == null)
                {
                    SetRuntimeStatus("Disabled: DragAndDrop not loaded");
                    BridgeWarn("DragAndDrop is not loaded: " + DragAndDropGuid);
                    return;
                }

                if (!Chainloader.PluginInfos.TryGetValue(CoordinateLoadOptionGuid, out cloInfo) ||
                    cloInfo == null || cloInfo.Instance == null)
                {
                    SetRuntimeStatus("Disabled: Coordinate Load Option not loaded");
                    BridgeWarn("Coordinate Load Option is not loaded: " + CoordinateLoadOptionGuid);
                    return;
                }

                Assembly dragAssembly = dragInfo.Instance.GetType().Assembly;
                Assembly cloAssembly = cloInfo.Instance.GetType().Assembly;
                MethodInfo prefixMethod;

                MakerMode = !string.Equals(Application.productName, "CharaStudio", StringComparison.Ordinal);

                if (!MakerMode)
                {
                    Type studioHandlerType = dragAssembly.GetType("DragAndDrop.StudioHandler", false);
                    if (studioHandlerType == null)
                        throw new InvalidOperationException("DragAndDrop.StudioHandler was not found.");

                    DragAndDropCoordinateLoadMethod = FindCoordinateLoadTarget(studioHandlerType);
                    if (DragAndDropCoordinateLoadMethod == null)
                        throw new InvalidOperationException("DragAndDrop Coordinate_Load(List<string>, POINT) was not found uniquely.");

                    DragAndDropGetSelectedCharactersMethod = studioHandlerType.GetMethod(
                        "GetSelectedCharacters",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null);
                    if (DragAndDropGetSelectedCharactersMethod == null ||
                        !typeof(IEnumerable).IsAssignableFrom(DragAndDropGetSelectedCharactersMethod.ReturnType))
                        throw new InvalidOperationException("DragAndDrop GetSelectedCharacters() was not found.");

                    Adapter = CoordinateLoadOptionAdapter.TryCreate(cloAssembly, Log);
                    if (Adapter == null)
                        throw new InvalidOperationException("Coordinate Load Option Studio members required by the bridge were not found.");

                    prefixMethod = typeof(Plugin).GetMethod(
                        "CoordinateLoadPrefix",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    SetRuntimeStatus("Initializing Studio bridge");
                }
                else
                {
                    Type makerHandlerType = dragAssembly.GetType("DragAndDrop.MakerHandler", false);
                    if (makerHandlerType == null)
                        throw new InvalidOperationException("DragAndDrop.MakerHandler was not found.");

                    DragAndDropCoordinateLoadMethod = FindMakerCoordinateLoadTarget(makerHandlerType);
                    if (DragAndDropCoordinateLoadMethod == null)
                        throw new InvalidOperationException("DragAndDrop Maker Coordinate_Load(string, POINT) was not found uniquely.");

                    MakerAdapter = MakerCoordinateLoadOptionAdapter.TryCreate(cloAssembly);
                    if (MakerAdapter == null)
                        throw new InvalidOperationException("Coordinate Load Option Maker members required by the bridge were not found.");

                    prefixMethod = typeof(Plugin).GetMethod(
                        "MakerCoordinateLoadPrefix",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    SetRuntimeStatus("Initializing Maker bridge");
                }

                if (prefixMethod == null)
                    throw new InvalidOperationException("Bridge prefix method was not found.");

                HarmonyInstance = new Harmony(PluginGuid);
                HarmonyMethod prefix = new HarmonyMethod(prefixMethod);
                prefix.priority = Priority.Last;
                HarmonyInstance.Patch(DragAndDropCoordinateLoadMethod, prefix: prefix);

                RuntimeEnabled = true;
                SetRuntimeStatus(MakerMode ?
                    "ENABLED - Koikatu Maker drag hook installed" :
                    "ENABLED - CharaStudio drag hook installed");
                BridgeInfo("Bridge enabled for " + (MakerMode ? "Koikatu Maker" : "CharaStudio") +
                    ". DragAndDrop version: " + dragInfo.Metadata.Version +
                    "; Coordinate Load Option version: " + cloInfo.Metadata.Version + ".");
                BridgeInfo("Compatibility is based on the members actually used by the bridge; DLL hash/MVID pinning is intentionally not used.");
            }
            catch (Exception ex)
            {
                RuntimeEnabled = false;
                SetRuntimeStatus("Disabled: initialization failed; see runtime.log");
                BridgeError("Bridge initialization failed. No DragAndDrop interception will be performed.", ex);
                try
                {
                    if (HarmonyInstance != null && DragAndDropCoordinateLoadMethod != null)
                        HarmonyInstance.Unpatch(DragAndDropCoordinateLoadMethod, HarmonyPatchType.Prefix, PluginGuid);
                }
                catch { }
            }
        }

        private static MethodBase FindCoordinateLoadTarget(Type studioHandlerType)
        {
            MethodInfo found = null;
            MethodInfo[] methods = studioHandlerType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.DeclaringType != studioHandlerType ||
                    method.Name != "Coordinate_Load" ||
                    method.ReturnType != typeof(void))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 2)
                    continue;
                if (parameters[0].ParameterType != typeof(List<string>))
                    continue;
                if (!string.Equals(parameters[1].ParameterType.FullName, "DragAndDrop.POINT", StringComparison.Ordinal))
                    continue;

                if (found != null)
                    return null;
                found = method;
            }

            return found;
        }

        private static MethodBase FindMakerCoordinateLoadTarget(Type makerHandlerType)
        {
            MethodInfo found = null;
            MethodInfo[] methods = makerHandlerType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.DeclaringType != makerHandlerType ||
                    method.Name != "Coordinate_Load" ||
                    method.ReturnType != typeof(void))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 2)
                    continue;
                if (parameters[0].ParameterType != typeof(string))
                    continue;
                if (!string.Equals(parameters[1].ParameterType.FullName, "DragAndDrop.POINT", StringComparison.Ordinal))
                    continue;

                if (found != null)
                    return null;
                found = method;
            }

            return found;
        }

        // Only Harmony patch in this plugin.
        // __instance gives us DragAndDrop's own StudioHandler so we can reuse its exact
        // selected-character semantics instead of treating MPCharCtrl.ociChar as selection state.
        // __0 binds to DragAndDrop's first argument without depending on its parameter name.
        private static bool CoordinateLoadPrefix(object __instance, List<string> __0)
        {
            if (!RuntimeEnabled || Adapter == null)
                return true;

            // The bridge intentionally handles only one coordinate at a time.
            // Multi-file DragAndDrop behavior is left completely untouched.
            if (__0 == null || __0.Count != 1)
                return true;

            string path = __0[0];
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return true;

            if (ObservedDropCountConfig != null)
                ObservedDropCountConfig.Value = ObservedDropCountConfig.Value + 1;

            SetLastAction("Observed: " + Path.GetFileName(path));
            if (VerboseConfig != null && VerboseConfig.Value)
                BridgeInfo("Coordinate drop observed: " + path);

            int generation = ++DropGeneration;

            try
            {
                object selectedCharacter = GetFirstSelectedCharacter(__instance);
                if (selectedCharacter == null)
                {
                    // DragAndDrop itself performs no coordinate load when no Studio character is selected.
                    // Suppress instead of failing open to a whole-coordinate load.
                    SetLastAction("Ignored: no selected Studio character");
                    if (VerboseConfig != null && VerboseConfig.Value)
                        BridgeInfo("Coordinate drop ignored because DragAndDrop reports no selected Studio character.");
                    return false;
                }

                if (Adapter.IsUnderlyingLoadBusy())
                {
                    SetLastAction("Ignored: CLO is currently loading");
                    BridgeWarn("Coordinate drop ignored because Coordinate Load Option is currently loading.");
                    return false;
                }

                PrepareResult result = Adapter.PrepareDroppedCoordinate(path, selectedCharacter);
                if (result == PrepareResult.UiNotReady)
                {
                    SetLastAction("Waiting for Studio Costume UI to become ready");
                    if (Instance != null)
                    {
                        Instance.StartCoroutine(Instance.DeferredPrepare(path, selectedCharacter, generation));
                        if (VerboseConfig != null && VerboseConfig.Value)
                            BridgeInfo("Studio Costume UI was not ready on the drop frame. Retrying briefly without allowing a whole-coordinate fallback.");
                    }
                    else
                    {
                        BridgeWarn("Studio Costume UI was not ready and the bridge instance is unavailable; whole-coordinate fallback remains suppressed.");
                    }
                    return false;
                }

                if (result == PrepareResult.Prepared)
                {
                    SetLastAction("PREPARED - CLO selection open; Studio Load armed");
                    if (VerboseConfig != null && VerboseConfig.Value)
                        BridgeInfo("Dropped coordinate prepared. CLO selection is open and Studio Load is armed.");
                    return false;
                }

                SetLastAction("Preparation failed; full DragAndDrop load suppressed");
                return false;
            }
            catch (Exception ex)
            {
                SetLastAction("Preparation error; full DragAndDrop load suppressed");
                BridgeError("Coordinate preparation failed. The original whole-coordinate DragAndDrop load was suppressed.", ex);
                return false;
            }
        }


        private static bool MakerCoordinateLoadPrefix(string __0)
        {
            if (!RuntimeEnabled || !MakerMode || MakerAdapter == null)
                return true;

            string path = __0;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return true;

            if (ObservedDropCountConfig != null)
                ObservedDropCountConfig.Value = ObservedDropCountConfig.Value + 1;

            SetLastAction("Maker observed: " + Path.GetFileName(path));
            if (VerboseConfig != null && VerboseConfig.Value)
                BridgeInfo("Maker coordinate drop observed: " + path);

            int generation = ++DropGeneration;

            try
            {
                if (MakerAdapter.IsUnderlyingLoadBusy())
                {
                    SetLastAction("Ignored: CLO is currently loading");
                    BridgeWarn("Maker coordinate drop ignored because Coordinate Load Option is currently loading.");
                    return false;
                }

                PrepareResult result = MakerAdapter.PrepareDroppedCoordinate(path);
                if (result == PrepareResult.UiNotReady)
                {
                    SetLastAction("Waiting for Maker Coordinate Load UI to become ready");
                    if (Instance != null)
                    {
                        Instance.StartCoroutine(Instance.DeferredPrepareMaker(path, generation));
                        if (VerboseConfig != null && VerboseConfig.Value)
                            BridgeInfo("Maker Coordinate Load UI was not ready on the drop frame. Retrying briefly without allowing a whole-coordinate fallback.");
                    }
                    else
                    {
                        BridgeWarn("Maker Coordinate Load UI was not ready and the bridge instance is unavailable; whole-coordinate fallback remains suppressed.");
                    }
                    return false;
                }

                if (result == PrepareResult.Prepared)
                {
                    SetLastAction("PREPARED - Maker CLO selection open; Load armed");
                    if (VerboseConfig != null && VerboseConfig.Value)
                        BridgeInfo("Maker dropped coordinate prepared. CLO selection is open and Maker Load is armed.");
                    return false;
                }

                SetLastAction("Maker preparation failed; full DragAndDrop load suppressed");
                return false;
            }
            catch (Exception ex)
            {
                SetLastAction("Maker preparation error; full DragAndDrop load suppressed");
                BridgeError("Maker coordinate preparation failed. The original whole-coordinate DragAndDrop load was suppressed.", ex);
                return false;
            }
        }


        private IEnumerator DeferredPrepareMaker(string path, int generation)
        {
            const int MaxRetryFrames = 12;

            for (int i = 0; i < MaxRetryFrames; i++)
            {
                yield return null;

                if (generation != DropGeneration || !RuntimeEnabled || !MakerMode || MakerAdapter == null)
                    yield break;

                if (MakerAdapter.IsUnderlyingLoadBusy())
                    continue;

                PrepareResult result = MakerAdapter.PrepareDroppedCoordinate(path);
                if (result == PrepareResult.Prepared)
                {
                    SetLastAction("PREPARED after Maker UI refresh - CLO selection open; Load armed");
                    if (VerboseConfig != null && VerboseConfig.Value)
                        BridgeInfo("Deferred Maker coordinate preparation succeeded after " + (i + 1) + " frame(s): " + path);
                    yield break;
                }

                if (result == PrepareResult.Failed)
                {
                    SetLastAction("Deferred Maker preparation failed; full DragAndDrop load suppressed");
                    yield break;
                }
            }

            if (generation == DropGeneration)
            {
                SetLastAction("Maker Coordinate Load UI stayed unavailable; whole-coordinate load suppressed");
                BridgeWarn("Maker Coordinate Load UI did not become ready within the short retry window. The original whole-coordinate DragAndDrop load was intentionally suppressed.");
            }
        }


        private static object GetFirstSelectedCharacter(object studioHandler)
        {
            if (studioHandler == null || DragAndDropGetSelectedCharactersMethod == null)
                return null;

            object raw = DragAndDropGetSelectedCharactersMethod.Invoke(studioHandler, null);
            IEnumerable selected = raw as IEnumerable;
            if (selected == null)
                return null;

            foreach (object item in selected)
            {
                if (item != null)
                    return item;
            }

            return null;
        }


        private IEnumerator DeferredPrepare(string path, object selectedCharacter, int generation)
        {
            // The selected character comes from DragAndDrop's own GetSelectedCharacters() result.
            // Retry is only for the Studio UI object itself being temporarily unavailable;
            // MPCharCtrl.ociChar is actively synchronized from selectedCharacter during preparation.
            // A newer drop cancels this worker.
            const int MaxRetryFrames = 12;

            for (int i = 0; i < MaxRetryFrames; i++)
            {
                yield return null;

                if (generation != DropGeneration || !RuntimeEnabled || Adapter == null)
                    yield break;

                if (Adapter.IsUnderlyingLoadBusy())
                    continue;

                PrepareResult result = Adapter.PrepareDroppedCoordinate(path, selectedCharacter);
                if (result == PrepareResult.Prepared)
                {
                    SetLastAction("PREPARED after Studio UI refresh - CLO selection open; Studio Load armed");
                    if (VerboseConfig != null && VerboseConfig.Value)
                        BridgeInfo("Deferred coordinate preparation succeeded after " + (i + 1) + " frame(s): " + path);
                    yield break;
                }

                if (result == PrepareResult.Failed)
                {
                    SetLastAction("Deferred preparation failed; full DragAndDrop load suppressed");
                    yield break;
                }
            }

            if (generation == DropGeneration)
            {
                SetLastAction("Studio Costume UI stayed unavailable; whole-coordinate load suppressed");
                BridgeWarn("Studio Costume UI did not become ready within the short retry window. The original whole-coordinate DragAndDrop load was intentionally suppressed.");
            }
        }

        private static void SetRuntimeStatus(string value)
        {
            try
            {
                if (RuntimeStatusConfig != null)
                    RuntimeStatusConfig.Value = value ?? string.Empty;
            }
            catch { }
        }

        private static void SetLastAction(string value)
        {
            try
            {
                if (LastActionConfig != null)
                    LastActionConfig.Value = value ?? string.Empty;
            }
            catch { }
        }

        private static void TraceFile(string level, string message)
        {
            if (string.IsNullOrEmpty(RuntimeTracePath))
                return;

            try
            {
                using (StreamWriter writer = new StreamWriter(RuntimeTracePath, true))
                {
                    writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                                     " [" + level + "] " + message);
                }
            }
            catch { }
        }

        internal static void BridgeInfo(string message)
        {
            if (Log != null)
                Log.LogInfo(message);
            TraceFile("INFO", message);
        }

        internal static void BridgeWarn(string message)
        {
            if (Log != null)
                Log.LogWarning(message);
            TraceFile("WARN", message);
        }

        internal static void BridgeError(string message, Exception ex)
        {
            string text = message;
            if (ex != null)
                text += " " + ex.GetType().FullName + ": " + ex.Message + Environment.NewLine + ex.StackTrace;

            if (Log != null)
                Log.LogError(text);
            TraceFile("ERROR", text.Replace(Environment.NewLine, " | "));
        }
    }

    internal enum PrepareResult
    {
        Prepared,
        UiNotReady,
        Failed
    }

    internal sealed class CoordinateLoadOptionAdapter
    {
        private const string CoordinateLoadOptionGuid = "com.jim60105.kk.coordinateloadoption";

        private readonly ManualLogSource _log;
        private readonly Type _studioType;
        private readonly Type _mpCharCtrlType;
        private readonly Type _costumeInfoType;
        private readonly PropertyInfo _studioRootButtonCtrlProperty;
        private readonly PropertyInfo _studioManipulatePanelCtrlProperty;
        private readonly PropertyInfo _rootButtonObjectCtrlInfoProperty;
        private readonly PropertyInfo _rootButtonSelectProperty;
        private readonly FieldInfo _rootButtonManipulateField;
        private readonly FieldInfo _manipulateInfoButtonField;
        private readonly PropertyInfo _buttonOnClickProperty;
        private readonly MethodInfo _buttonOnClickInvokeMethod;
        private readonly PropertyInfo _manipulatePanelActiveProperty;
        private readonly Type _charaFileSortType;
        private readonly Type _charaFileInfoType;
        private readonly FieldInfo _coordinatePathField;
        private readonly FieldInfo _panelField;
        private readonly MethodInfo _panelIsActiveMethod;
        private readonly MethodInfo _onSelectPostfix;
        private readonly MethodInfo _onClickLoadPrefix;
        private readonly MethodInfo _onClickLoadPostfix;
        private readonly FieldInfo _queueField;
        private readonly FieldInfo _tmpChaCtrlField;
        private readonly MethodInfo _mpCharCtrlOnClickRoot;
        private readonly PropertyInfo _mpCharCtrlOciCharProperty;
        private readonly FieldInfo _mpCharCtrlCostumeInfoField;
        private readonly FieldInfo _costumeFileSortField;
        private readonly FieldInfo _costumeLoadButtonField;
        private readonly MethodInfo _costumeOnClickLoadMethod;
        private readonly FieldInfo _charaFileSortListField;
        private readonly FieldInfo _charaFileSortSelectBackingField;
        private readonly PropertyInfo _charaFileSortSelectPathProperty;
        private readonly ConstructorInfo _charaFileSortConstructor;
        private readonly ConstructorInfo _charaFileInfoConstructor;
        private readonly PropertyInfo _buttonInteractableProperty;

        // Minimal bridge-owned pending state. This is not a transaction snapshot: it only keeps
        // Studio's existing Load button usable while the dropped coordinate remains CLO's current
        // path and CLO would actually take its selective-load branch.
        private string _preparedPath;
        private object _preparedLoadButton;
        private bool _preparedOriginalInteractable;

        private CoordinateLoadOptionAdapter(
            ManualLogSource log,
            Type studioType,
            Type mpCharCtrlType,
            Type costumeInfoType,
            PropertyInfo studioRootButtonCtrlProperty,
            PropertyInfo studioManipulatePanelCtrlProperty,
            PropertyInfo rootButtonObjectCtrlInfoProperty,
            PropertyInfo rootButtonSelectProperty,
            FieldInfo rootButtonManipulateField,
            FieldInfo manipulateInfoButtonField,
            PropertyInfo buttonOnClickProperty,
            MethodInfo buttonOnClickInvokeMethod,
            PropertyInfo manipulatePanelActiveProperty,
            Type charaFileSortType,
            Type charaFileInfoType,
            FieldInfo coordinatePathField,
            FieldInfo panelField,
            MethodInfo panelIsActiveMethod,
            MethodInfo onSelectPostfix,
            MethodInfo onClickLoadPrefix,
            MethodInfo onClickLoadPostfix,
            FieldInfo queueField,
            FieldInfo tmpChaCtrlField,
            MethodInfo mpCharCtrlOnClickRoot,
            PropertyInfo mpCharCtrlOciCharProperty,
            FieldInfo mpCharCtrlCostumeInfoField,
            FieldInfo costumeFileSortField,
            FieldInfo costumeLoadButtonField,
            MethodInfo costumeOnClickLoadMethod,
            FieldInfo charaFileSortListField,
            FieldInfo charaFileSortSelectBackingField,
            PropertyInfo charaFileSortSelectPathProperty,
            ConstructorInfo charaFileSortConstructor,
            ConstructorInfo charaFileInfoConstructor,
            PropertyInfo buttonInteractableProperty)
        {
            _log = log;
            _studioType = studioType;
            _mpCharCtrlType = mpCharCtrlType;
            _costumeInfoType = costumeInfoType;
            _studioRootButtonCtrlProperty = studioRootButtonCtrlProperty;
            _studioManipulatePanelCtrlProperty = studioManipulatePanelCtrlProperty;
            _rootButtonObjectCtrlInfoProperty = rootButtonObjectCtrlInfoProperty;
            _rootButtonSelectProperty = rootButtonSelectProperty;
            _rootButtonManipulateField = rootButtonManipulateField;
            _manipulateInfoButtonField = manipulateInfoButtonField;
            _buttonOnClickProperty = buttonOnClickProperty;
            _buttonOnClickInvokeMethod = buttonOnClickInvokeMethod;
            _manipulatePanelActiveProperty = manipulatePanelActiveProperty;
            _charaFileSortType = charaFileSortType;
            _charaFileInfoType = charaFileInfoType;
            _coordinatePathField = coordinatePathField;
            _panelField = panelField;
            _panelIsActiveMethod = panelIsActiveMethod;
            _onSelectPostfix = onSelectPostfix;
            _onClickLoadPrefix = onClickLoadPrefix;
            _onClickLoadPostfix = onClickLoadPostfix;
            _queueField = queueField;
            _tmpChaCtrlField = tmpChaCtrlField;
            _mpCharCtrlOnClickRoot = mpCharCtrlOnClickRoot;
            _mpCharCtrlOciCharProperty = mpCharCtrlOciCharProperty;
            _mpCharCtrlCostumeInfoField = mpCharCtrlCostumeInfoField;
            _costumeFileSortField = costumeFileSortField;
            _costumeLoadButtonField = costumeLoadButtonField;
            _costumeOnClickLoadMethod = costumeOnClickLoadMethod;
            _charaFileSortListField = charaFileSortListField;
            _charaFileSortSelectBackingField = charaFileSortSelectBackingField;
            _charaFileSortSelectPathProperty = charaFileSortSelectPathProperty;
            _charaFileSortConstructor = charaFileSortConstructor;
            _charaFileInfoConstructor = charaFileInfoConstructor;
            _buttonInteractableProperty = buttonInteractableProperty;
        }

        internal static CoordinateLoadOptionAdapter TryCreate(Assembly cloAssembly, ManualLogSource log)
        {
            try
            {
                if (cloAssembly == null)
                    return null;

                const BindingFlags StaticAll = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                Type patchesType = cloAssembly.GetType("KK_CoordinateLoadOption.Patches", false);
                Type coordinateLoadType = cloAssembly.GetType("KK_CoordinateLoadOption.CoordinateLoad", false);
                if (patchesType == null || coordinateLoadType == null)
                {
                    LogContract(log, "CLO Patches/CoordinateLoad type not found.");
                    return null;
                }

                FieldInfo coordinatePathField = patchesType.GetField("coordinatePath", StaticAll);
                FieldInfo panelField = patchesType.GetField("panel", StaticAll);
                MethodInfo panelIsActiveMethod = panelField == null ? null : panelField.FieldType.GetMethod(
                    "IsActive", InstanceAll, null, Type.EmptyTypes, null);
                MethodInfo onSelectPostfix = FindStaticMethod(patchesType, "OnSelectPostfix", 1);
                MethodInfo onClickLoadPrefix = FindStaticMethod(patchesType, "OnClickLoadPrefix", 0);
                MethodInfo onClickLoadPostfix = FindStaticMethod(patchesType, "OnClickLoadPostfix", 0);
                FieldInfo queueField = coordinateLoadType.GetField("oCICharQueue", StaticAll);
                FieldInfo tmpChaCtrlField = coordinateLoadType.GetField("tmpChaCtrl", StaticAll);

                if (coordinatePathField == null || coordinatePathField.FieldType != typeof(string) ||
                    panelField == null || panelIsActiveMethod == null || panelIsActiveMethod.ReturnType != typeof(bool) ||
                    onSelectPostfix == null ||
                    onClickLoadPrefix == null || onClickLoadPostfix == null ||
                    queueField == null || tmpChaCtrlField == null)
                {
                    LogContract(log, "One or more CLO members used by the bridge are missing.");
                    return null;
                }

                Type studioType = FindLoadedType("Studio.Studio");
                Type mpCharCtrlType = FindLoadedType("Studio.MPCharCtrl");
                Type charaFileSortType = FindLoadedType("Studio.CharaFileSort");
                Type charaFileInfoType = FindLoadedType("Studio.CharaFileInfo");
                if (studioType == null || mpCharCtrlType == null || charaFileSortType == null || charaFileInfoType == null)
                {
                    LogContract(log, "Required Studio types were not found.");
                    return null;
                }

                // Do not discover MPCharCtrl by looking for an active component. Before the user
                // clicks the top-left anim/manipulate button, the character manipulate root is
                // inactive and Unity FindObjectOfType cannot see MPCharCtrl. Resolve the public
                // Studio control chain instead, then open the same manipulate panel as the game.
                PropertyInfo studioRootButtonCtrlProperty = studioType.GetProperty("rootButtonCtrl", InstanceAll);
                PropertyInfo studioManipulatePanelCtrlProperty = studioType.GetProperty("manipulatePanelCtrl", InstanceAll);
                Type rootButtonCtrlType = studioRootButtonCtrlProperty == null ? null : studioRootButtonCtrlProperty.PropertyType;
                Type manipulatePanelCtrlType = studioManipulatePanelCtrlProperty == null ? null : studioManipulatePanelCtrlProperty.PropertyType;
                PropertyInfo rootButtonObjectCtrlInfoProperty = rootButtonCtrlType == null ? null : rootButtonCtrlType.GetProperty("objectCtrlInfo", InstanceAll);
                PropertyInfo rootButtonSelectProperty = rootButtonCtrlType == null ? null : rootButtonCtrlType.GetProperty("select", InstanceAll);
                FieldInfo rootButtonManipulateField = rootButtonCtrlType == null ? null : FindInstanceFieldInHierarchy(rootButtonCtrlType, "manipulate");
                FieldInfo manipulateInfoButtonField = rootButtonManipulateField == null ? null :
                    FindInstanceFieldInHierarchy(rootButtonManipulateField.FieldType, "button");
                PropertyInfo buttonOnClickProperty = manipulateInfoButtonField == null ? null :
                    manipulateInfoButtonField.FieldType.GetProperty("onClick", InstanceAll);
                MethodInfo buttonOnClickInvokeMethod = buttonOnClickProperty == null ? null :
                    buttonOnClickProperty.PropertyType.GetMethod("Invoke", InstanceAll, null, Type.EmptyTypes, null);
                PropertyInfo manipulatePanelActiveProperty = manipulatePanelCtrlType == null ? null : manipulatePanelCtrlType.GetProperty("active", InstanceAll);

                if (studioRootButtonCtrlProperty == null || studioManipulatePanelCtrlProperty == null ||
                    rootButtonObjectCtrlInfoProperty == null || !rootButtonObjectCtrlInfoProperty.CanWrite ||
                    rootButtonSelectProperty == null || !rootButtonSelectProperty.CanRead || rootButtonSelectProperty.PropertyType != typeof(int) ||
                    rootButtonManipulateField == null || manipulateInfoButtonField == null ||
                    !string.Equals(manipulateInfoButtonField.FieldType.FullName, "UnityEngine.UI.Button", StringComparison.Ordinal) ||
                    buttonOnClickProperty == null || !buttonOnClickProperty.CanRead ||
                    buttonOnClickInvokeMethod == null || buttonOnClickInvokeMethod.ReturnType != typeof(void) ||
                    manipulatePanelActiveProperty == null || !manipulatePanelActiveProperty.CanRead || !manipulatePanelActiveProperty.CanWrite ||
                    manipulatePanelActiveProperty.PropertyType != typeof(bool))
                {
                    LogContract(log, "Studio root/manipulate button event chain was not found.");
                    return null;
                }

                Type costumeInfoType = mpCharCtrlType.GetNestedType("CostumeInfo", BindingFlags.NonPublic);
                if (costumeInfoType == null)
                {
                    LogContract(log, "Studio.MPCharCtrl.CostumeInfo was not found.");
                    return null;
                }

                MethodInfo mpCharCtrlOnClickRoot = mpCharCtrlType.GetMethod(
                    "OnClickRoot", InstanceAll, null, new Type[] { typeof(int) }, null);
                PropertyInfo mpCharCtrlOciCharProperty = mpCharCtrlType.GetProperty("ociChar", InstanceAll);
                FieldInfo mpCharCtrlCostumeInfoField = mpCharCtrlType.GetField("costumeInfo", InstanceAll);
                FieldInfo costumeFileSortField = costumeInfoType.GetField("fileSort", InstanceAll);
                FieldInfo costumeLoadButtonField = costumeInfoType.GetField("buttonLoad", InstanceAll);
                MethodInfo costumeOnClickLoadMethod = costumeInfoType.GetMethod(
                    "OnClickLoad", InstanceAll, null, Type.EmptyTypes, null);

                FieldInfo charaFileSortListField = charaFileSortType.GetField("cfiList", InstanceAll);
                FieldInfo charaFileSortSelectBackingField = charaFileSortType.GetField("m_Select", InstanceAll);
                PropertyInfo charaFileSortSelectPathProperty = charaFileSortType.GetProperty("selectPath", InstanceAll);
                ConstructorInfo charaFileSortConstructor = charaFileSortType.GetConstructor(Type.EmptyTypes);
                ConstructorInfo charaFileInfoConstructor = charaFileInfoType.GetConstructor(new Type[] { typeof(string), typeof(string) });

                if (mpCharCtrlOnClickRoot == null || mpCharCtrlOciCharProperty == null ||
                    !mpCharCtrlOciCharProperty.CanRead || !mpCharCtrlOciCharProperty.CanWrite ||
                    mpCharCtrlCostumeInfoField == null || costumeFileSortField == null ||
                    costumeLoadButtonField == null || costumeOnClickLoadMethod == null ||
                    charaFileSortListField == null || charaFileSortSelectBackingField == null ||
                    charaFileSortSelectPathProperty == null || !charaFileSortSelectPathProperty.CanRead ||
                    charaFileSortConstructor == null || charaFileInfoConstructor == null)
                {
                    LogContract(log, "One or more Studio members used by the bridge are missing.");
                    return null;
                }

                PropertyInfo buttonInteractableProperty = costumeLoadButtonField.FieldType.GetProperty(
                    "interactable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (buttonInteractableProperty == null || !buttonInteractableProperty.CanWrite ||
                    buttonInteractableProperty.PropertyType != typeof(bool))
                {
                    LogContract(log, "Studio Costume Load button interactable property was not found.");
                    return null;
                }

                return new CoordinateLoadOptionAdapter(
                    log,
                    studioType,
                    mpCharCtrlType,
                    costumeInfoType,
                    studioRootButtonCtrlProperty,
                    studioManipulatePanelCtrlProperty,
                    rootButtonObjectCtrlInfoProperty,
                    rootButtonSelectProperty,
                    rootButtonManipulateField,
                    manipulateInfoButtonField,
                    buttonOnClickProperty,
                    buttonOnClickInvokeMethod,
                    manipulatePanelActiveProperty,
                    charaFileSortType,
                    charaFileInfoType,
                    coordinatePathField,
                    panelField,
                    panelIsActiveMethod,
                    onSelectPostfix,
                    onClickLoadPrefix,
                    onClickLoadPostfix,
                    queueField,
                    tmpChaCtrlField,
                    mpCharCtrlOnClickRoot,
                    mpCharCtrlOciCharProperty,
                    mpCharCtrlCostumeInfoField,
                    costumeFileSortField,
                    costumeLoadButtonField,
                    costumeOnClickLoadMethod,
                    charaFileSortListField,
                    charaFileSortSelectBackingField,
                    charaFileSortSelectPathProperty,
                    charaFileSortConstructor,
                    charaFileInfoConstructor,
                    buttonInteractableProperty);
            }
            catch (Exception ex)
            {
                LogContract(log, "Adapter creation failed: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        internal bool IsUnderlyingLoadBusy()
        {
            try
            {
                if (_tmpChaCtrlField.GetValue(null) != null)
                    return true;

                object queue = _queueField.GetValue(null);
                if (queue == null)
                    return false;

                PropertyInfo countProperty = queue.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
                if (countProperty == null)
                    return false;

                object raw = countProperty.GetValue(queue, null);
                return raw is int && (int)raw > 0;
            }
            catch
            {
                return false;
            }
        }

        internal PrepareResult PrepareDroppedCoordinate(string path, object selectedCharacter)
        {
            try
            {
                if (selectedCharacter == null ||
                    !_mpCharCtrlOciCharProperty.PropertyType.IsInstanceOfType(selectedCharacter))
                    throw new InvalidOperationException("DragAndDrop selected-character result is not a Studio.OCIChar instance.");

                // v1.1.4 still called RootButtonCtrl.OnClick(1) directly. That changes the
                // controller state, but it is not identical to a real click on the Unity UI Button:
                // Button.onClick may contain additional persistent/runtime listeners. The user's
                // repeated test shows that distinction matters in this Studio setup.
                //
                // v1.1.6 therefore invokes the actual serialized anim Button.onClick event. When
                // anim was previously closed we stop preparation immediately after that event and
                // resume on a later frame. This mirrors the known-good manual sequence and avoids
                // assuming that all UI/CLO initialization is safe to consume in the same event.
                UnityEngine.Object studioObject = UnityEngine.Object.FindObjectOfType(_studioType);
                if (studioObject == null)
                {
                    LogMessage("Studio root controller is not available on this frame.");
                    return PrepareResult.UiNotReady;
                }

                object rootButtonCtrl = _studioRootButtonCtrlProperty.GetValue(studioObject, null);
                object manipulatePanelCtrl = _studioManipulatePanelCtrlProperty.GetValue(studioObject, null);
                if (rootButtonCtrl == null || manipulatePanelCtrl == null)
                {
                    LogMessage("Studio anim/manipulate controller is not available on this frame.");
                    return PrepareResult.UiNotReady;
                }

                int rootSelect = (int)_rootButtonSelectProperty.GetValue(rootButtonCtrl, null);
                if (rootSelect != 1)
                {
                    object manipulateInfo = _rootButtonManipulateField.GetValue(rootButtonCtrl);
                    object animButton = manipulateInfo == null ? null : _manipulateInfoButtonField.GetValue(manipulateInfo);
                    object onClick = animButton == null ? null : _buttonOnClickProperty.GetValue(animButton, null);
                    if (onClick == null)
                    {
                        LogMessage("Studio anim Button.onClick is unavailable on this frame.");
                        return PrepareResult.UiNotReady;
                    }

                    _buttonOnClickInvokeMethod.Invoke(onClick, null);

                    rootSelect = (int)_rootButtonSelectProperty.GetValue(rootButtonCtrl, null);
                    if (rootSelect != 1)
                        throw new InvalidOperationException("Invoking the real Studio anim Button.onClick did not select manipulate/anim.");

                    LogMessage("Invoked the real Studio anim Button.onClick; deferring coordinate preparation to a later frame.");
                    return PrepareResult.UiNotReady;
                }

                // Once the real anim click has completed, synchronize Studio's manipulate model
                // with the OCIChar already selected according to DragAndDrop itself.
                _rootButtonObjectCtrlInfoProperty.SetValue(rootButtonCtrl, selectedCharacter, null);

                bool manipulateActive = (bool)_manipulatePanelActiveProperty.GetValue(manipulatePanelCtrl, null);
                if (!manipulateActive)
                {
                    // This is only a repair for an internally inconsistent Studio state where
                    // RootButtonCtrl says anim is selected but ManipulatePanelCtrl is inactive.
                    _manipulatePanelActiveProperty.SetValue(manipulatePanelCtrl, true, null);
                    LogMessage("Studio anim was selected but its manipulate panel was inactive; reactivated it and deferred one frame.");
                    return PrepareResult.UiNotReady;
                }

                // MPCharCtrl is now active through the real anim Button event, so FindObjectOfType
                // is only a post-activation lookup/verification, not a hidden prerequisite.
                UnityEngine.Object mpObject = UnityEngine.Object.FindObjectOfType(_mpCharCtrlType);
                if (mpObject == null)
                {
                    LogMessage("Studio MPCharCtrl is still unavailable after opening anim.");
                    return PrepareResult.UiNotReady;
                }

                object mpCharCtrl = mpObject;
                object ociChar = _mpCharCtrlOciCharProperty.GetValue(mpCharCtrl, null);
                if (!object.ReferenceEquals(ociChar, selectedCharacter))
                {
                    _mpCharCtrlOciCharProperty.SetValue(mpCharCtrl, selectedCharacter, null);
                    ociChar = _mpCharCtrlOciCharProperty.GetValue(mpCharCtrl, null);
                    if (!object.ReferenceEquals(ociChar, selectedCharacter))
                    {
                        LogMessage("MPCharCtrl OCIChar synchronization did not stick on this frame.");
                        return PrepareResult.UiNotReady;
                    }
                }

                // CLO creates its Studio selector UI from the MPCharCtrl/Costume initialization
                // path. If the panel object still does not exist, treat that as transient UI
                // readiness rather than as a hard failure; the deferred worker will retry.
                if (_panelField.GetValue(null) == null)
                {
                    LogMessage("CLO Studio panel is not initialized yet after anim activation.");
                    return PrepareResult.UiNotReady;
                }

                // Open Studio's real Costume page first; CLO's panel lives under that UI.
                _mpCharCtrlOnClickRoot.Invoke(mpCharCtrl, new object[] { 4 });

                object costumeInfo = _mpCharCtrlCostumeInfoField.GetValue(mpCharCtrl);
                if (costumeInfo == null || !_costumeInfoType.IsInstanceOfType(costumeInfo))
                    throw new InvalidOperationException("Studio CostumeInfo instance is unavailable.");

                // This is the one safety check kept specifically to prevent the original Studio
                // OnClickLoad from performing a whole-coordinate load when the user clicks Load.
                string loadHookError;
                if (!HasRequiredCloLoadPatches(out loadHookError))
                    throw new InvalidOperationException("CLO Studio Load hook is not active: " + loadHookError);

                object detachedFileSort = CreateDetachedFileSort(path);
                object originalFileSort = _costumeFileSortField.GetValue(costumeInfo);

                try
                {
                    _costumeFileSortField.SetValue(costumeInfo, detachedFileSort);
                    _onSelectPostfix.Invoke(null, new object[] { costumeInfo });
                }
                finally
                {
                    _costumeFileSortField.SetValue(costumeInfo, originalFileSort);
                }

                string cloPath = _coordinatePathField.GetValue(null) as string;
                if (!PathsEqual(cloPath, path))
                    throw new InvalidOperationException("CLO did not accept the dropped coordinate path.");

                object panel = _panelField.GetValue(null);
                Component panelComponent = panel as Component;
                if (panelComponent == null)
                    throw new InvalidOperationException("CLO Show Selection panel is not initialized.");

                object loadButton = _costumeLoadButtonField.GetValue(costumeInfo);
                if (loadButton == null)
                    throw new InvalidOperationException("Studio Costume Load button is unavailable.");

                bool oldPanelActive = panelComponent.gameObject.activeSelf;
                bool oldInteractable = GetButtonInteractable(loadButton);

                try
                {
                    panelComponent.gameObject.SetActive(true);
                    SetButtonInteractable(loadButton, true);
                    if (!GetButtonInteractable(loadButton))
                        throw new InvalidOperationException("Studio Costume Load button refused interactable=true.");

                    _preparedPath = path;
                    _preparedLoadButton = loadButton;
                    _preparedOriginalInteractable = oldInteractable;

                    // Run the same rule once immediately; LateUpdate() will keep it stable against
                    // Studio/CLO UI refreshes until the current CLO path changes.
                    MaintainPreparedLoadButton();
                }
                catch
                {
                    try { SetButtonInteractable(loadButton, oldInteractable); } catch { }
                    try { panelComponent.gameObject.SetActive(oldPanelActive); } catch { }
                    _preparedPath = null;
                    _preparedLoadButton = null;
                    throw;
                }

                LogMessage("Prepared dropped coordinate and armed Studio Costume Load button: " + path);
                return PrepareResult.Prepared;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                LogError("PrepareDroppedCoordinate failed.", inner);
                return PrepareResult.Failed;
            }
            catch (Exception ex)
            {
                LogError("PrepareDroppedCoordinate failed.", ex);
                return PrepareResult.Failed;
            }
        }

        internal void MaintainPreparedLoadButton()
        {
            if (string.IsNullOrEmpty(_preparedPath) || _preparedLoadButton == null)
                return;

            try
            {
                string currentPath = _coordinatePathField.GetValue(null) as string;
                if (!PathsEqual(currentPath, _preparedPath))
                {
                    // CLO/Studio has moved to another coordinate. Ownership ends immediately;
                    // the new selection controls the button from this point onward.
                    _preparedPath = null;
                    _preparedLoadButton = null;
                    return;
                }

                if (IsUnderlyingLoadBusy())
                {
                    // We enabled this button, so do not leave it clickable while CLO is doing
                    // an async load. Keep the prepared path so it can be used again afterwards.
                    SetButtonInteractable(_preparedLoadButton, false);
                    return;
                }

                object panel = _panelField.GetValue(null);
                if (panel == null)
                {
                    SetButtonInteractable(_preparedLoadButton, false);
                    _preparedPath = null;
                    _preparedLoadButton = null;
                    return;
                }

                object activeRaw = _panelIsActiveMethod.Invoke(panel, null);
                bool selectiveLoadReady = activeRaw is bool && (bool)activeRaw;

                // This is the core v1.1.1 fix: the native button is kept enabled for as long as
                // CLO's selective panel is genuinely active. A one-shot assignment is not enough,
                // because Studio's Costume UI can set buttonLoad.interactable=false again.
                if (GetButtonInteractable(_preparedLoadButton) != selectiveLoadReady)
                    SetButtonInteractable(_preparedLoadButton, selectiveLoadReady);

                if (GetButtonInteractable(_preparedLoadButton) != selectiveLoadReady)
                    throw new InvalidOperationException("Studio Costume Load button interactable state did not stick.");
            }
            catch (Exception ex)
            {
                LogError("MaintainPreparedLoadButton failed.", ex);
                try { SetButtonInteractable(_preparedLoadButton, false); } catch { }
                _preparedPath = null;
                _preparedLoadButton = null;
            }
        }

        internal void ReleasePreparedLoadButton()
        {
            object button = _preparedLoadButton;
            bool original = _preparedOriginalInteractable;
            _preparedPath = null;
            _preparedLoadButton = null;

            if (button != null)
                SetButtonInteractable(button, original);
        }

        private bool GetButtonInteractable(object button)
        {
            if (button == null)
                return false;

            object raw = _buttonInteractableProperty.GetValue(button, null);
            return raw is bool && (bool)raw;
        }

        private void SetButtonInteractable(object button, bool value)
        {
            if (button == null)
                return;

            _buttonInteractableProperty.SetValue(button, value, null);
        }

        private object CreateDetachedFileSort(string path)
        {
            object fileSort = _charaFileSortConstructor.Invoke(null);
            if (fileSort == null || !_charaFileSortType.IsInstanceOfType(fileSort))
                throw new InvalidOperationException("Could not create detached Studio.CharaFileSort.");

            object rawList = _charaFileSortListField.GetValue(fileSort);
            IList list = rawList as IList;
            if (list == null)
                throw new InvalidOperationException("Detached CharaFileSort.cfiList is unavailable.");

            object info = _charaFileInfoConstructor.Invoke(new object[]
            {
                path,
                Path.GetFileNameWithoutExtension(path)
            });
            if (info == null || !_charaFileInfoType.IsInstanceOfType(info))
                throw new InvalidOperationException("Could not create Studio.CharaFileInfo for dropped path.");

            list.Add(info);

            // Do not call CharaFileSort.select setter on a detached object. The game setter touches
            // CharaFileInfo.node/select UI state; the backing field is all selectPath getter needs.
            _charaFileSortSelectBackingField.SetValue(fileSort, 0);

            string resolved = _charaFileSortSelectPathProperty.GetValue(fileSort, null) as string;
            if (!PathsEqual(resolved, path))
                throw new InvalidOperationException("Detached CharaFileSort selectPath did not resolve to the dropped path.");

            return fileSort;
        }

        private bool HasRequiredCloLoadPatches(out string error)
        {
            error = null;
            try
            {
                Patches patchInfo = Harmony.GetPatchInfo(_costumeOnClickLoadMethod);
                if (patchInfo == null)
                {
                    error = "no Harmony patch info";
                    return false;
                }

                bool prefixFound = ContainsPatchMethod(patchInfo.Prefixes, _onClickLoadPrefix);
                bool postfixFound = ContainsPatchMethod(patchInfo.Postfixes, _onClickLoadPostfix);

                if (!prefixFound || !postfixFound)
                {
                    error = "required CLO Prefix/Postfix not found";
                    return false;
                }

                // Other plugins are deliberately allowed to patch the same method.
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool ContainsPatchMethod(IEnumerable<Patch> patches, MethodInfo expected)
        {
            if (patches == null || expected == null)
                return false;

            foreach (Patch patch in patches)
            {
                MethodBase actual = GetPatchMethod(patch);
                if (SameMethod(actual, expected))
                    return true;
            }

            return false;
        }

        private static MethodBase GetPatchMethod(object patch)
        {
            if (patch == null)
                return null;

            Type type = patch.GetType();
            PropertyInfo property = type.GetProperty(
                "PatchMethod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead)
                return property.GetValue(patch, null) as MethodBase;

            property = type.GetProperty(
                "patchMethod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead)
                return property.GetValue(patch, null) as MethodBase;

            FieldInfo field = type.GetField(
                "patchMethod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(patch) as MethodBase;
        }

        private static bool SameMethod(MethodBase a, MethodBase b)
        {
            if (a == null || b == null)
                return false;
            if (ReferenceEquals(a, b) || a.Equals(b))
                return true;

            try
            {
                return a.Module == b.Module && a.MetadataToken == b.MetadataToken;
            }
            catch
            {
                return false;
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static FieldInfo FindInstanceFieldInHierarchy(Type type, string name)
        {
            const BindingFlags DeclaredInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, DeclaredInstance);
                if (field != null)
                    return field;
            }
            return null;
        }

        private static MethodInfo FindStaticMethod(Type type, string name, int parameterCount)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo found = null;

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != name || method.GetParameters().Length != parameterCount)
                    continue;

                if (found != null)
                    return null;
                found = method;
            }

            return found;
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void LogMessage(string message)
        {
            Plugin.BridgeInfo("[Adapter] " + message);
        }

        private void LogError(string message, Exception ex)
        {
            Plugin.BridgeError("[Adapter] " + message, ex);
        }

        private static void LogContract(ManualLogSource log, string message)
        {
            Plugin.BridgeError("[Adapter/Contract] " + message, null);
        }
    }

    internal sealed class MakerCoordinateLoadOptionAdapter
    {
        private readonly Type _customCoordinateFileType;
        private readonly FieldInfo _coordinateFileWindowField;
        private readonly Type _customChangeMainMenuType;
        private readonly FieldInfo _mainSystemMenuField;
        private readonly FieldInfo _toggleGroupItemsField;
        private readonly MethodInfo _toggleGroupGetSelectIndexMethod;
        private readonly FieldInfo _toggleItemToggleField;
        private readonly FieldInfo _toggleItemCanvasGroupField;
        private readonly PropertyInfo _toggleIsOnProperty;
        private readonly FieldInfo _systemFileWindowsField;
        private readonly FieldInfo _systemWindowTypesField;
        private readonly PropertyInfo _fileWindowTypeProperty;
        private readonly PropertyInfo _fileWindowLoadButtonProperty;
        private readonly PropertyInfo _buttonInteractableProperty;
        private readonly FieldInfo _insideStudioField;
        private readonly FieldInfo _coordinatePathField;
        private readonly FieldInfo _cloCustomFileWindowField;
        private readonly FieldInfo _panelField;
        private readonly MethodInfo _panelIsActiveMethod;
        private readonly MethodInfo _onSelectPostfix;
        private readonly FieldInfo _queueField;
        private readonly FieldInfo _tmpChaCtrlField;

        private string _preparedPath;
        private object _preparedLoadButton;
        private bool _preparedOriginalInteractable;

        private MakerCoordinateLoadOptionAdapter(
            Type customCoordinateFileType,
            FieldInfo coordinateFileWindowField,
            Type customChangeMainMenuType,
            FieldInfo mainSystemMenuField,
            FieldInfo toggleGroupItemsField,
            MethodInfo toggleGroupGetSelectIndexMethod,
            FieldInfo toggleItemToggleField,
            FieldInfo toggleItemCanvasGroupField,
            PropertyInfo toggleIsOnProperty,
            FieldInfo systemFileWindowsField,
            FieldInfo systemWindowTypesField,
            PropertyInfo fileWindowTypeProperty,
            PropertyInfo fileWindowLoadButtonProperty,
            PropertyInfo buttonInteractableProperty,
            FieldInfo insideStudioField,
            FieldInfo coordinatePathField,
            FieldInfo cloCustomFileWindowField,
            FieldInfo panelField,
            MethodInfo panelIsActiveMethod,
            MethodInfo onSelectPostfix,
            FieldInfo queueField,
            FieldInfo tmpChaCtrlField)
        {
            _customCoordinateFileType = customCoordinateFileType;
            _coordinateFileWindowField = coordinateFileWindowField;
            _customChangeMainMenuType = customChangeMainMenuType;
            _mainSystemMenuField = mainSystemMenuField;
            _toggleGroupItemsField = toggleGroupItemsField;
            _toggleGroupGetSelectIndexMethod = toggleGroupGetSelectIndexMethod;
            _toggleItemToggleField = toggleItemToggleField;
            _toggleItemCanvasGroupField = toggleItemCanvasGroupField;
            _toggleIsOnProperty = toggleIsOnProperty;
            _systemFileWindowsField = systemFileWindowsField;
            _systemWindowTypesField = systemWindowTypesField;
            _fileWindowTypeProperty = fileWindowTypeProperty;
            _fileWindowLoadButtonProperty = fileWindowLoadButtonProperty;
            _buttonInteractableProperty = buttonInteractableProperty;
            _insideStudioField = insideStudioField;
            _coordinatePathField = coordinatePathField;
            _cloCustomFileWindowField = cloCustomFileWindowField;
            _panelField = panelField;
            _panelIsActiveMethod = panelIsActiveMethod;
            _onSelectPostfix = onSelectPostfix;
            _queueField = queueField;
            _tmpChaCtrlField = tmpChaCtrlField;
        }

        internal static MakerCoordinateLoadOptionAdapter TryCreate(Assembly cloAssembly)
        {
            try
            {
                if (cloAssembly == null)
                    return null;

                const BindingFlags StaticAll = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                Type patchesType = cloAssembly.GetType("KK_CoordinateLoadOption.Patches", false);
                Type coordinateLoadType = cloAssembly.GetType("KK_CoordinateLoadOption.CoordinateLoad", false);
                Type cloPluginType = cloAssembly.GetType("KK_CoordinateLoadOption.KK_CoordinateLoadOption", false);
                if (patchesType == null || coordinateLoadType == null || cloPluginType == null)
                {
                    LogContract("CLO Maker Patches/CoordinateLoad/plugin type not found.");
                    return null;
                }

                FieldInfo coordinatePathField = patchesType.GetField("coordinatePath", StaticAll);
                FieldInfo cloCustomFileWindowField = patchesType.GetField("CustomFileWindow", StaticAll);
                FieldInfo panelField = patchesType.GetField("panel", StaticAll);
                MethodInfo panelIsActiveMethod = panelField == null ? null : panelField.FieldType.GetMethod(
                    "IsActive", InstanceAll, null, Type.EmptyTypes, null);
                MethodInfo onSelectPostfix = FindStaticMethod(patchesType, "OnSelectPostfix", 1);
                FieldInfo queueField = coordinateLoadType.GetField("oCICharQueue", StaticAll);
                FieldInfo tmpChaCtrlField = coordinateLoadType.GetField("tmpChaCtrl", StaticAll);
                FieldInfo insideStudioField = cloPluginType.GetField("insideStudio", StaticAll);

                if (coordinatePathField == null || coordinatePathField.FieldType != typeof(string) ||
                    cloCustomFileWindowField == null ||
                    panelField == null || panelIsActiveMethod == null || panelIsActiveMethod.ReturnType != typeof(bool) ||
                    onSelectPostfix == null ||
                    queueField == null || tmpChaCtrlField == null ||
                    insideStudioField == null || insideStudioField.FieldType != typeof(bool))
                {
                    LogContract("One or more CLO Maker members used by the bridge are missing.");
                    return null;
                }

                Type customCoordinateFileType = FindLoadedType("ChaCustom.CustomCoordinateFile");
                if (customCoordinateFileType == null)
                {
                    LogContract("ChaCustom.CustomCoordinateFile was not found.");
                    return null;
                }

                FieldInfo coordinateFileWindowField = customCoordinateFileType.GetField("fileWindow", InstanceAll);
                if (coordinateFileWindowField == null)
                {
                    LogContract("CustomCoordinateFile.fileWindow was not found.");
                    return null;
                }

                Type customFileWindowType = coordinateFileWindowField.FieldType;
                if (!string.Equals(customFileWindowType.FullName, "ChaCustom.CustomFileWindow", StringComparison.Ordinal))
                {
                    LogContract("CustomCoordinateFile.fileWindow has an unexpected type.");
                    return null;
                }

                PropertyInfo fileWindowTypeProperty = customFileWindowType.GetProperty("fwType", InstanceAll);
                PropertyInfo fileWindowLoadButtonProperty = customFileWindowType.GetProperty("btnCoordeLoadLoad", InstanceAll);

                if (fileWindowTypeProperty == null || !fileWindowTypeProperty.CanRead ||
                    !fileWindowTypeProperty.CanWrite || !fileWindowTypeProperty.PropertyType.IsEnum ||
                    fileWindowLoadButtonProperty == null || !fileWindowLoadButtonProperty.CanRead)
                {
                    LogContract("CustomFileWindow Coordinate Load window contract was not found.");
                    return null;
                }

                // Maker navigation uses the game's real Toggle controls. The audited
                // CustomChangeMainMenu maps index 6 to ccSystemMenu, while CustomChangeSystemMenu
                // maps its items to (fileWindow[], types[]). The bridge does not force parent
                // GameObjects active and does not mutate the real coordinate list.
                Type customChangeMainMenuType = FindLoadedType("ChaCustom.CustomChangeMainMenu");
                Type customChangeSystemMenuType = FindLoadedType("ChaCustom.CustomChangeSystemMenu");
                Type toggleGroupType = FindLoadedType("UI_ToggleGroupCtrl");
                if (customChangeMainMenuType == null || customChangeSystemMenuType == null || toggleGroupType == null)
                {
                    LogContract("Maker main/system Toggle menu types were not found.");
                    return null;
                }

                FieldInfo mainSystemMenuField = customChangeMainMenuType.GetField("ccSystemMenu", InstanceAll);
                FieldInfo toggleGroupItemsField = toggleGroupType.GetField("items", InstanceAll);
                MethodInfo toggleGroupGetSelectIndexMethod = toggleGroupType.GetMethod(
                    "GetSelectIndex", InstanceAll, null, Type.EmptyTypes, null);
                FieldInfo systemFileWindowsField = customChangeSystemMenuType.GetField("fileWindow", InstanceAll);
                FieldInfo systemWindowTypesField = customChangeSystemMenuType.GetField("types", InstanceAll);

                if (!toggleGroupType.IsAssignableFrom(customChangeMainMenuType) ||
                    !toggleGroupType.IsAssignableFrom(customChangeSystemMenuType) ||
                    mainSystemMenuField == null || mainSystemMenuField.FieldType != customChangeSystemMenuType ||
                    toggleGroupItemsField == null || !toggleGroupItemsField.FieldType.IsArray ||
                    toggleGroupGetSelectIndexMethod == null || toggleGroupGetSelectIndexMethod.ReturnType != typeof(int) ||
                    systemFileWindowsField == null || !systemFileWindowsField.FieldType.IsArray ||
                    systemFileWindowsField.FieldType.GetElementType() != customFileWindowType ||
                    systemWindowTypesField == null || !systemWindowTypesField.FieldType.IsArray ||
                    systemWindowTypesField.FieldType.GetElementType() != fileWindowTypeProperty.PropertyType)
                {
                    LogContract("Maker Toggle navigation fields do not match the audited structure.");
                    return null;
                }

                Type toggleItemType = toggleGroupItemsField.FieldType.GetElementType();
                FieldInfo toggleItemToggleField = toggleItemType == null ? null :
                    toggleItemType.GetField("tglItem", InstanceAll);
                FieldInfo toggleItemCanvasGroupField = toggleItemType == null ? null :
                    toggleItemType.GetField("cgItem", InstanceAll);
                PropertyInfo toggleIsOnProperty = toggleItemToggleField == null ? null :
                    toggleItemToggleField.FieldType.GetProperty("isOn", InstanceAll);

                if (toggleItemToggleField == null || toggleItemCanvasGroupField == null ||
                    !typeof(Component).IsAssignableFrom(toggleItemToggleField.FieldType) ||
                    !typeof(CanvasGroup).IsAssignableFrom(toggleItemCanvasGroupField.FieldType) ||
                    toggleIsOnProperty == null || !toggleIsOnProperty.CanRead || !toggleIsOnProperty.CanWrite ||
                    toggleIsOnProperty.PropertyType != typeof(bool))
                {
                    LogContract("UI_ToggleGroupCtrl.ItemInfo tglItem/cgItem contract was not found.");
                    return null;
                }

                try
                {
                    Enum.Parse(fileWindowTypeProperty.PropertyType, "CoordinateLoad", false);
                }
                catch
                {
                    LogContract("CustomFileWindow.FileWindowType.CoordinateLoad was not found.");
                    return null;
                }

                PropertyInfo buttonInteractableProperty = fileWindowLoadButtonProperty.PropertyType.GetProperty(
                    "interactable", InstanceAll);
                if (buttonInteractableProperty == null || !buttonInteractableProperty.CanRead ||
                    !buttonInteractableProperty.CanWrite || buttonInteractableProperty.PropertyType != typeof(bool))
                {
                    LogContract("Maker Coordinate Load button interactable property was not found.");
                    return null;
                }

                object insideStudioRaw = insideStudioField.GetValue(null);
                if (!(insideStudioRaw is bool) || (bool)insideStudioRaw)
                {
                    LogContract("CLO reports insideStudio=true while Maker bridge is initializing.");
                    return null;
                }

                return new MakerCoordinateLoadOptionAdapter(
                    customCoordinateFileType,
                    coordinateFileWindowField,
                    customChangeMainMenuType,
                    mainSystemMenuField,
                    toggleGroupItemsField,
                    toggleGroupGetSelectIndexMethod,
                    toggleItemToggleField,
                    toggleItemCanvasGroupField,
                    toggleIsOnProperty,
                    systemFileWindowsField,
                    systemWindowTypesField,
                    fileWindowTypeProperty,
                    fileWindowLoadButtonProperty,
                    buttonInteractableProperty,
                    insideStudioField,
                    coordinatePathField,
                    cloCustomFileWindowField,
                    panelField,
                    panelIsActiveMethod,
                    onSelectPostfix,
                    queueField,
                    tmpChaCtrlField);
            }
            catch (Exception ex)
            {
                LogContract("Maker adapter creation failed: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private PrepareResult OpenCoordinateLoadWindowThroughMakerUi(object fileWindow)
        {
            UnityEngine.Object mainMenuObject = UnityEngine.Object.FindObjectOfType(_customChangeMainMenuType);
            if (mainMenuObject == null)
            {
                LogMessage("CustomChangeMainMenu is not available on this Maker frame.");
                return PrepareResult.UiNotReady;
            }

            object systemMenu = _mainSystemMenuField.GetValue(mainMenuObject);
            if (systemMenu == null)
            {
                LogMessage("CustomChangeMainMenu.ccSystemMenu is not available on this Maker frame.");
                return PrepareResult.UiNotReady;
            }

            Array mainItems = _toggleGroupItemsField.GetValue(mainMenuObject) as Array;
            const int MainSystemIndex = 6;
            if (mainItems == null || mainItems.Length <= MainSystemIndex)
            {
                LogMessage("Maker main-menu Toggle items are not initialized yet.");
                return PrepareResult.UiNotReady;
            }

            // The audited CustomChangeMainMenu.ChangeWindowSetting switch maps index 6 to
            // ccSystemMenu. Use that actual game semantic directly instead of guessing from
            // Transform ancestry, which is scene-layout data and not part of the code contract.
            if (!SelectNativeToggle(mainMenuObject, mainItems, MainSystemIndex))
            {
                LogMessage("Maker System main-menu Toggle did not become selected yet.");
                return PrepareResult.UiNotReady;
            }

            Array systemItems = _toggleGroupItemsField.GetValue(systemMenu) as Array;
            Array fileWindows = _systemFileWindowsField.GetValue(systemMenu) as Array;
            Array windowTypes = _systemWindowTypesField.GetValue(systemMenu) as Array;
            if (systemItems == null || fileWindows == null || windowTypes == null ||
                systemItems.Length == 0 || fileWindows.Length == 0 || windowTypes.Length == 0)
            {
                LogMessage("Maker System-menu Coordinate Load arrays are not initialized yet.");
                return PrepareResult.UiNotReady;
            }

            object coordinateLoadType;
            try
            {
                coordinateLoadType = Enum.Parse(_fileWindowTypeProperty.PropertyType, "CoordinateLoad", false);
            }
            catch
            {
                throw new InvalidOperationException("CustomFileWindow.FileWindowType.CoordinateLoad is unavailable.");
            }

            int commonLength = Math.Min(systemItems.Length, Math.Min(fileWindows.Length, windowTypes.Length));
            int coordinateLoadIndex = -1;
            for (int i = 0; i < commonLength; i++)
            {
                object candidateWindow = fileWindows.GetValue(i);
                object candidateType = windowTypes.GetValue(i);
                if (!object.ReferenceEquals(candidateWindow, fileWindow) ||
                    !object.Equals(candidateType, coordinateLoadType))
                    continue;

                coordinateLoadIndex = i;
                break;
            }

            if (coordinateLoadIndex < 0)
                throw new InvalidOperationException("Could not map CustomCoordinateFile.fileWindow to the native Coordinate Load Toggle.");

            if (!SelectNativeToggle(systemMenu, systemItems, coordinateLoadIndex))
            {
                LogMessage("Maker Coordinate Load Toggle did not become selected yet.");
                return PrepareResult.UiNotReady;
            }

            object currentWindowType = _fileWindowTypeProperty.GetValue(fileWindow, null);

            // CustomChangeSystemMenu's native Toggle listener sets fileWindow[index].fwType
            // from types[index]. Compare the actual enum value by name instead of pinning its
            // underlying integer, which is unnecessary for this bridge.
            if (!object.Equals(currentWindowType, coordinateLoadType))
            {
                LogMessage("Maker Coordinate Load Toggle is selected but its native fwType listener is not ready yet.");
                return PrepareResult.UiNotReady;
            }

            return PrepareResult.Prepared;
        }

        private bool SelectNativeToggle(object toggleGroup, Array items, int index)
        {
            if (toggleGroup == null || items == null || index < 0 || index >= items.Length)
                return false;

            object item = items.GetValue(index);
            if (item == null)
                return false;

            object toggle = _toggleItemToggleField.GetValue(item);
            if (toggle == null)
                return false;

            object rawIsOn = _toggleIsOnProperty.GetValue(toggle, null);
            if (!(rawIsOn is bool))
                return false;

            if (!(bool)rawIsOn)
                _toggleIsOnProperty.SetValue(toggle, true, null);

            object selectedRaw = _toggleGroupGetSelectIndexMethod.Invoke(toggleGroup, null);
            if (!(selectedRaw is int) || (int)selectedRaw != index)
                return false;

            // UI_ToggleGroupCtrl's audited native listener calls CanvasGroupExtensions.Enable:
            // selected item => alpha=1, interactable=true, blocksRaycasts=true. Checking the
            // CanvasGroup prevents treating a Toggle value changed before Start wiring as ready.
            CanvasGroup canvasGroup = _toggleItemCanvasGroupField.GetValue(item) as CanvasGroup;
            return canvasGroup != null && canvasGroup.alpha > 0.5f &&
                canvasGroup.interactable && canvasGroup.blocksRaycasts;
        }

        internal bool IsUnderlyingLoadBusy()
        {
            try
            {
                if (_tmpChaCtrlField.GetValue(null) != null)
                    return true;

                object queue = _queueField.GetValue(null);
                if (queue == null)
                    return false;

                PropertyInfo countProperty = queue.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
                if (countProperty == null)
                    return false;

                object raw = countProperty.GetValue(queue, null);
                return raw is int && (int)raw > 0;
            }
            catch
            {
                return false;
            }
        }

        internal PrepareResult PrepareDroppedCoordinate(string path)
        {
            try
            {
                object insideStudioRaw = _insideStudioField.GetValue(null);
                if (!(insideStudioRaw is bool) || (bool)insideStudioRaw)
                    throw new InvalidOperationException("CLO is not in its Maker branch.");

                UnityEngine.Object coordinateFileObject = UnityEngine.Object.FindObjectOfType(_customCoordinateFileType);
                if (coordinateFileObject == null)
                {
                    LogMessage("CustomCoordinateFile is not available on this Maker frame.");
                    return PrepareResult.UiNotReady;
                }

                object fileWindow = _coordinateFileWindowField.GetValue(coordinateFileObject);
                Component fileWindowComponent = fileWindow as Component;
                if (fileWindow == null || fileWindowComponent == null)
                {
                    LogMessage("Maker CustomFileWindow is not available on this frame.");
                    return PrepareResult.UiNotReady;
                }

                // Open the exact native Maker route first. CLO UI may finish initializing only
                // after the real Coordinate Load window becomes active, so do not require the
                // selector panel before navigation.
                PrepareResult navigationResult = OpenCoordinateLoadWindowThroughMakerUi(fileWindow);
                if (navigationResult != PrepareResult.Prepared)
                    return navigationResult;

                object panel = _panelField.GetValue(null);
                Component panelComponent = panel as Component;
                if (panelComponent == null)
                {
                    LogMessage("CLO Maker selection panel is not initialized yet.");
                    return PrepareResult.UiNotReady;
                }

                // A live CLO CustomFileWindow reference is the runtime evidence that Maker
                // InitPostfix already initialized this window. Harmony patch-table inspection
                // is deliberately not used here; it was redundant and could reject compatible
                // setups without proving the button listener state anyway.
                object initializedCloWindow = _cloCustomFileWindowField.GetValue(null);
                if (initializedCloWindow == null)
                {
                    LogMessage("CLO Maker window binding is not initialized yet.");
                    return PrepareResult.UiNotReady;
                }
                if (!object.ReferenceEquals(initializedCloWindow, fileWindow))
                {
                    LogMessage("CLO Maker window binding has not caught up with the active Coordinate Load window yet.");
                    return PrepareResult.UiNotReady;
                }

                MakerSelectProxy proxy = new MakerSelectProxy(fileWindow, path);
                _onSelectPostfix.Invoke(null, new object[] { proxy });

                string cloPath = _coordinatePathField.GetValue(null) as string;
                if (!PathsEqual(cloPath, path))
                    throw new InvalidOperationException("CLO Maker branch did not accept the dropped coordinate path.");

                object cloWindow = _cloCustomFileWindowField.GetValue(null);
                if (!object.ReferenceEquals(cloWindow, fileWindow))
                    throw new InvalidOperationException("CLO Maker branch did not bind the active CustomFileWindow.");

                object loadButton = _fileWindowLoadButtonProperty.GetValue(fileWindow, null);
                if (loadButton == null)
                    throw new InvalidOperationException("Maker Coordinate Load button is unavailable.");

                bool originalInteractable;
                if (_preparedLoadButton != null && object.ReferenceEquals(_preparedLoadButton, loadButton))
                {
                    originalInteractable = _preparedOriginalInteractable;
                }
                else
                {
                    ReleasePreparedLoadButton();
                    originalInteractable = GetButtonInteractable(loadButton);
                }

                bool oldPanelActive = panelComponent.gameObject.activeSelf;
                try
                {
                    panelComponent.gameObject.SetActive(true);
                    object activeRaw = _panelIsActiveMethod.Invoke(panel, null);
                    if (!(activeRaw is bool) || !(bool)activeRaw)
                    {
                        panelComponent.gameObject.SetActive(oldPanelActive);
                        LogMessage("CLO Maker panel exists but is not active in the current window hierarchy yet.");
                        return PrepareResult.UiNotReady;
                    }

                    SetButtonInteractable(loadButton, true);
                    if (!GetButtonInteractable(loadButton))
                        throw new InvalidOperationException("Maker Coordinate Load button refused interactable=true.");

                    _preparedPath = path;
                    _preparedLoadButton = loadButton;
                    _preparedOriginalInteractable = originalInteractable;
                    MaintainPreparedLoadButton();
                }
                catch
                {
                    try { SetButtonInteractable(loadButton, originalInteractable); } catch { }
                    try { panelComponent.gameObject.SetActive(oldPanelActive); } catch { }
                    _preparedPath = null;
                    _preparedLoadButton = null;
                    throw;
                }

                LogMessage("Prepared dropped coordinate and armed Maker Coordinate Load button: " + path);
                return PrepareResult.Prepared;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                LogError("PrepareDroppedCoordinate failed.", inner);
                return PrepareResult.Failed;
            }
            catch (Exception ex)
            {
                LogError("PrepareDroppedCoordinate failed.", ex);
                return PrepareResult.Failed;
            }
        }

        internal void MaintainPreparedLoadButton()
        {
            if (string.IsNullOrEmpty(_preparedPath) || _preparedLoadButton == null)
                return;

            try
            {
                string currentPath = _coordinatePathField.GetValue(null) as string;
                if (!PathsEqual(currentPath, _preparedPath))
                {
                    _preparedPath = null;
                    _preparedLoadButton = null;
                    return;
                }

                if (IsUnderlyingLoadBusy())
                {
                    SetButtonInteractable(_preparedLoadButton, false);
                    return;
                }

                object panel = _panelField.GetValue(null);
                if (panel == null)
                {
                    SetButtonInteractable(_preparedLoadButton, false);
                    _preparedPath = null;
                    _preparedLoadButton = null;
                    return;
                }

                object activeRaw = _panelIsActiveMethod.Invoke(panel, null);
                bool selectiveLoadReady = activeRaw is bool && (bool)activeRaw;
                if (GetButtonInteractable(_preparedLoadButton) != selectiveLoadReady)
                    SetButtonInteractable(_preparedLoadButton, selectiveLoadReady);

                if (GetButtonInteractable(_preparedLoadButton) != selectiveLoadReady)
                    throw new InvalidOperationException("Maker Coordinate Load button interactable state did not stick.");
            }
            catch (Exception ex)
            {
                LogError("MaintainPreparedLoadButton failed.", ex);
                try { SetButtonInteractable(_preparedLoadButton, false); } catch { }
                _preparedPath = null;
                _preparedLoadButton = null;
            }
        }

        internal void ReleasePreparedLoadButton()
        {
            object button = _preparedLoadButton;
            bool original = _preparedOriginalInteractable;
            _preparedPath = null;
            _preparedLoadButton = null;

            if (button != null)
                SetButtonInteractable(button, original);
        }

        private bool GetButtonInteractable(object button)
        {
            if (button == null)
                return false;

            object raw = _buttonInteractableProperty.GetValue(button, null);
            return raw is bool && (bool)raw;
        }

        private void SetButtonInteractable(object button, bool value)
        {
            if (button != null)
                _buttonInteractableProperty.SetValue(button, value, null);
        }

        private static Type FindLoadedType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static MethodInfo FindStaticMethod(Type type, string name, int parameterCount)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo found = null;

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != name || method.GetParameters().Length != parameterCount)
                    continue;

                if (found != null)
                    return null;
                found = method;
            }

            return found;
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void LogMessage(string message)
        {
            Plugin.BridgeInfo("[MakerAdapter] " + message);
        }

        private void LogError(string message, Exception ex)
        {
            Plugin.BridgeError("[MakerAdapter] " + message, ex);
        }

        private static void LogContract(string message)
        {
            Plugin.BridgeError("[MakerAdapter/Contract] " + message, null);
        }
    }

    internal sealed class MakerSelectProxy
    {
        public object fileWindow;
        public object listCtrl;

        internal MakerSelectProxy(object realFileWindow, string path)
        {
            fileWindow = realFileWindow;
            listCtrl = new MakerListCtrlProxy(path);
        }
    }

    internal sealed class MakerListCtrlProxy
    {
        private readonly object _topItem;

        internal MakerListCtrlProxy(string path)
        {
            _topItem = new MakerSelectTopItemProxy(path);
        }

        public object GetSelectTopItem()
        {
            return _topItem;
        }
    }

    internal sealed class MakerSelectTopItemProxy
    {
        public object info;

        internal MakerSelectTopItemProxy(string path)
        {
            info = new MakerFileInfoProxy(path);
        }
    }

    internal sealed class MakerFileInfoProxy
    {
        public string FullPath;

        internal MakerFileInfoProxy(string path)
        {
            FullPath = path;
        }
    }

}
