#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace PersonalAvatarTools
{
    public static class UpperArmScalePackageExporter
    {

        public static void Export()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UpperArmScalePackageExporter).Assembly);
            string root = package?.assetPath;
            if (string.IsNullOrEmpty(root) || !AssetDatabase.IsValidFolder(root))
            {
                EditorUtility.DisplayDialog("書き出し", "Packages内のツールフォルダーが見つかりません。", "OK");
                return;
            }
            string output = EditorUtility.SaveFilePanel("UnityPackageを書き出す", "",
                package.name + "-" + package.version, "unitypackage");
            if (string.IsNullOrEmpty(output))
                return;
            try
            {
                AssetDatabase.ExportPackage(root, output, ExportPackageOptions.Recurse);
                EditorUtility.RevealInFinder(output);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("書き出しに失敗しました", ex.Message, "OK");
            }
        }
    }
}
#endif
