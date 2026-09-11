using System;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace KK_DragCoordinateLoadBridge
{
    /// <summary>
    /// Studio external-coordinate preview companion.
    ///
    /// The bridge temporarily supplies CLO with a detached Studio.CharaFileSort so an
    /// externally dragged coordinate can participate in CLO without being inserted into
    /// Studio's real coordinate list. Studio's native preview, however, belongs to
    /// MPCharCtrl.CostumeInfo and may be cleared again by a later CostumeInfo.InitList()
    /// refresh. This companion therefore does two things:
    ///
    /// 1) call Studio's own CostumeInfo.LoadImage(int) once while the detached fileSort is
    ///    still installed;
    /// 2) keep that already-loaded RawImage visible while CLO still owns the same external
    ///    coordinate path, without re-reading the PNG every frame.
    ///
    /// If Studio replaces the preview texture with a different non-null texture (for example
    /// when the user previews another native list item), ownership is released immediately so
    /// the normal Studio preview is never fought.
    /// </summary>
    [BepInPlugin(PreviewPluginGuid, PreviewPluginName, PreviewPluginVersion)]
    [BepInProcess("CharaStudio")]
    [BepInDependency(Plugin.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(CoordinateLoadOptionGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [Browsable(false)]
    public sealed class CoordinatePreviewRefreshPlugin : BaseUnityPlugin
    {
        private const string PreviewPluginGuid = "agumon.kk.dragcoordinateloadbridge.previewrefresh";
        private const string PreviewPluginName = "KK Drag Coordinate Load Bridge - Preview Refresh";
        private const string PreviewPluginVersion = "1.1.0";
        private const string CoordinateLoadOptionGuid = "com.jim60105.kk.coordinateloadoption";

        private static CoordinatePreviewRefreshPlugin Instance;

        private Harmony _harmony;
        private MethodBase _targetMethod;
        private FieldInfo _coordinatePathField;

        private string _preparedPath;
        private object _preparedImage;
        private object _preparedTexture;
        private PropertyInfo _imageTextureProperty;
        private PropertyInfo _imageColorProperty;

        private void Awake()
        {
            Instance = this;

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

                const BindingFlags StaticAll = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                _coordinatePathField = patchesType.GetField("coordinatePath", StaticAll);
                MethodInfo target = FindOnSelectPostfix(patchesType);
                MethodInfo postfix = typeof(CoordinatePreviewRefreshPlugin).GetMethod(
                    "RefreshDetachedStudioThumbnail",
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (_coordinatePathField == null || _coordinatePathField.FieldType != typeof(string) ||
                    target == null || postfix == null)
                {
                    Logger.LogWarning("CLO preview contract was not found uniquely; preview refresh hook is inactive.");
                    return;
                }

                _harmony = new Harmony(PreviewPluginGuid);
                _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                _targetMethod = target;
                Logger.LogInfo("Studio external-coordinate preview persistence hook installed.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not install Studio coordinate preview persistence hook: " + ex);
            }
        }

        private void LateUpdate()
        {
            MaintainPreparedPreview();
        }

        private void OnDestroy()
        {
            ReleasePreparedPreview();

            try
            {
                if (_harmony != null && _targetMethod != null)
                    _harmony.Unpatch(_targetMethod, HarmonyPatchType.Postfix, PreviewPluginGuid);
            }
            catch
            {
            }

            if (object.ReferenceEquals(Instance, this))
                Instance = null;
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
            CoordinatePreviewRefreshPlugin instance = Instance;
            if (instance == null)
                return;

            try
            {
                instance.CaptureDetachedStudioThumbnail(__0);
            }
            catch (TargetInvocationException ex)
            {
                instance.ReleasePreparedPreview();
                Exception inner = ex.InnerException ?? ex;
                instance.Logger.LogWarning("Studio coordinate preview refresh failed: " + inner.GetType().Name + ": " + inner.Message);
            }
            catch (Exception ex)
            {
                instance.ReleasePreparedPreview();
                instance.Logger.LogWarning("Studio coordinate preview refresh failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void CaptureDetachedStudioThumbnail(object costumeInfo)
        {
            if (costumeInfo == null)
                return;

            Type costumeType = costumeInfo.GetType();
            const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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
            // CharaFileInfo deliberately does not, so only the external-drag proxy reaches here.
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
            FieldInfo imageThumbnailField = costumeType.GetField("imageThumbnail", InstanceAll);
            if (loadImage == null || loadImage.ReturnType != typeof(void) || imageThumbnailField == null)
                return;

            // Use Studio's own PNG preview loader once. Do not repeatedly call this from LateUpdate:
            // LoadImage() itself performs Resources.UnloadUnusedAssets() and GC.Collect().
            loadImage.Invoke(costumeInfo, new object[] { select });

            object image = imageThumbnailField.GetValue(costumeInfo);
            if (image == null)
                return;

            Type imageType = image.GetType();
            PropertyInfo textureProperty = imageType.GetProperty("texture", InstanceAll);
            PropertyInfo colorProperty = imageType.GetProperty("color", InstanceAll);
            if (textureProperty == null || !textureProperty.CanRead || !textureProperty.CanWrite ||
                colorProperty == null || !colorProperty.CanWrite || colorProperty.PropertyType != typeof(Color))
                return;

            object texture = textureProperty.GetValue(image, null);
            if (texture == null)
                return;

            _preparedPath = path;
            _preparedImage = image;
            _preparedTexture = texture;
            _imageTextureProperty = textureProperty;
            _imageColorProperty = colorProperty;

            // The native LoadImage already sets white. Set it explicitly once more so the
            // captured state starts from the same invariant maintained in LateUpdate.
            _imageColorProperty.SetValue(_preparedImage, Color.white, null);
        }

        private void MaintainPreparedPreview()
        {
            if (string.IsNullOrEmpty(_preparedPath) ||
                _preparedImage == null ||
                _preparedTexture == null ||
                _imageTextureProperty == null ||
                _imageColorProperty == null ||
                _coordinatePathField == null)
            {
                return;
            }

            try
            {
                string currentPath = _coordinatePathField.GetValue(null) as string;
                if (!PathsEqual(currentPath, _preparedPath))
                {
                    ReleasePreparedPreview();
                    return;
                }

                UnityEngine.Object unityImage = _preparedImage as UnityEngine.Object;
                if (unityImage == null)
                {
                    ReleasePreparedPreview();
                    return;
                }

                object currentTexture = _imageTextureProperty.GetValue(_preparedImage, null);
                if (currentTexture == null)
                {
                    // A UI refresh may clear the texture entirely. Restore the already-loaded
                    // texture object without re-reading the PNG or forcing another GC cycle.
                    _imageTextureProperty.SetValue(_preparedImage, _preparedTexture, null);
                }
                else if (!object.ReferenceEquals(currentTexture, _preparedTexture))
                {
                    // Another real Studio preview has taken over (for example native hover).
                    // Release ownership instead of fighting the user's current preview.
                    ReleasePreparedPreview();
                    return;
                }

                // CostumeInfo.InitList() can reset imageThumbnail.color to Color.clear after the
                // external preview was loaded. Keeping the same RawImage white is enough to keep
                // the already-loaded texture visible and is cheap to do each LateUpdate.
                _imageColorProperty.SetValue(_preparedImage, Color.white, null);
            }
            catch (Exception ex)
            {
                ReleasePreparedPreview();
                Logger.LogWarning("Studio coordinate preview persistence stopped: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void ReleasePreparedPreview()
        {
            _preparedPath = null;
            _preparedImage = null;
            _preparedTexture = null;
            _imageTextureProperty = null;
            _imageColorProperty = null;
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;

            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
