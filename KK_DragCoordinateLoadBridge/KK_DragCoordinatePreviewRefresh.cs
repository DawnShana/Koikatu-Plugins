using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace KK_DragCoordinateLoadBridge
{
    /// <summary>
    /// Companion bootstrap compiled into the same DLL as the bridge.
    ///
    /// The bridge intentionally uses a detached Studio.CharaFileSort so an external
    /// dragged coordinate does not have to be injected into Studio's real file list.
    /// CLO only needs selectPath, but Studio's thumbnail is refreshed separately by
    /// MPCharCtrl.CostumeInfo.LoadImage(int). This hook runs after CLO has consumed
    /// the detached selection, while that temporary fileSort is still installed, and
    /// invokes Studio's own thumbnail loader for the detached item.
    /// </summary>
    [BepInPlugin(PreviewPluginGuid, PreviewPluginName, PreviewPluginVersion)]
    [BepInProcess("CharaStudio")]
    [BepInDependency(Plugin.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(CoordinateLoadOptionGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class CoordinatePreviewRefreshPlugin : BaseUnityPlugin
    {
        private const string PreviewPluginGuid = "agumon.kk.dragcoordinateloadbridge.previewrefresh";
        private const string PreviewPluginName = "KK Drag Coordinate Load Bridge - Preview Refresh";
        private const string PreviewPluginVersion = "1.0.0";
        private const string CoordinateLoadOptionGuid = "com.jim60105.kk.coordinateloadoption";

        private Harmony _harmony;
        private MethodBase _targetMethod;

        private void Awake()
        {
            try
            {
                PluginInfo cloInfo;
                if (!Chainloader.PluginInfos.TryGetValue(CoordinateLoadOptionGuid, out cloInfo) ||
                    cloInfo == null || cloInfo.Instance == null)
                {
                    Logger.LogWarning("Coordinate Load Option is not loaded; preview refresh hook is inactive.");
                    return;
                }

                Assembly cloAssembly = cloInfo.Instance.GetType().Assembly;
                Type patchesType = cloAssembly.GetType("KK_CoordinateLoadOption.Patches", false);
                if (patchesType == null)
                {
                    Logger.LogWarning("CLO Patches type was not found; preview refresh hook is inactive.");
                    return;
                }

                MethodInfo target = FindOnSelectPostfix(patchesType);
                MethodInfo postfix = typeof(CoordinatePreviewRefreshPlugin).GetMethod(
                    "RefreshDetachedStudioThumbnail",
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (target == null || postfix == null)
                {
                    Logger.LogWarning("CLO OnSelectPostfix contract was not found uniquely; preview refresh hook is inactive.");
                    return;
                }

                _harmony = new Harmony(PreviewPluginGuid);
                _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                _targetMethod = target;
                Logger.LogInfo("Studio external-coordinate preview refresh hook installed.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not install Studio coordinate preview refresh hook: " + ex);
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (_harmony != null && _targetMethod != null)
                    _harmony.Unpatch(_targetMethod, HarmonyPatchType.Postfix, PreviewPluginGuid);
            }
            catch
            {
            }
        }

        private static MethodInfo FindOnSelectPostfix(Type patchesType)
        {
            MethodInfo found = null;
            MethodInfo[] methods = patchesType.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "OnSelectPostfix" || method.ReturnType != typeof(void))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1)
                    continue;

                if (found != null)
                    return null;

                found = method;
            }

            return found;
        }

        private static void RefreshDetachedStudioThumbnail(object __0)
        {
            try
            {
                object costumeInfo = __0;
                if (costumeInfo == null)
                    return;

                Type costumeType = costumeInfo.GetType();
                const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Do not touch other CLO branches or future unrelated one-argument hooks.
                if (!string.Equals(costumeType.FullName, "Studio.MPCharCtrl+CostumeInfo", StringComparison.Ordinal))
                    return;

                FieldInfo fileSortField = costumeType.GetField("fileSort", InstanceAll);
                object fileSort = fileSortField == null ? null : fileSortField.GetValue(costumeInfo);
                if (fileSort == null)
                    return;

                Type fileSortType = fileSort.GetType();
                FieldInfo listField = fileSortType.GetField("cfiList", InstanceAll);
                FieldInfo selectField = fileSortType.GetField("m_Select", InstanceAll);
                PropertyInfo selectPathProperty = fileSortType.GetProperty("selectPath", InstanceAll);
                if (listField == null || selectField == null || selectPathProperty == null)
                    return;

                IList list = listField.GetValue(fileSort) as IList;
                object rawSelect = selectField.GetValue(fileSort);
                if (list == null || !(rawSelect is int))
                    return;

                int select = (int)rawSelect;
                if (select < 0 || select >= list.Count)
                    return;

                object info = list[select];
                if (info == null)
                    return;

                // Native Studio list entries have a ListNode. The bridge-created detached
                // CharaFileInfo deliberately does not. This makes the hook external-drag-only
                // and avoids reloading thumbnails for ordinary Studio list clicks.
                PropertyInfo nodeProperty = info.GetType().GetProperty("node", InstanceAll);
                if (nodeProperty == null || nodeProperty.GetValue(info, null) != null)
                    return;

                string path = selectPathProperty.GetValue(fileSort, null) as string;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return;

                MethodInfo loadImage = costumeType.GetMethod(
                    "LoadImage",
                    InstanceAll,
                    null,
                    new Type[] { typeof(int) },
                    null);
                if (loadImage == null || loadImage.ReturnType != typeof(void))
                    return;

                loadImage.Invoke(costumeInfo, new object[] { select });
            }
            catch (TargetInvocationException)
            {
                // Preview failure must never break CLO's selective-load path.
            }
            catch
            {
                // Preview failure must never break CLO's selective-load path.
            }
        }
    }
}
