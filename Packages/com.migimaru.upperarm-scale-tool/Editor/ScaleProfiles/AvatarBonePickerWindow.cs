#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PersonalAvatarTools.ScaleProfiles
{
    public sealed class AvatarBonePickerWindow : EditorWindow
    {
        private GameObject avatar;
        private Action<GameObject, AvatarBoneChoice> onBoneSelected;
        private Action onOutfitRootSelected;
        private List<AvatarBoneChoice> choices = new List<AvatarBoneChoice>();
        private string filter = "";
        private Vector2 scroll;
        private bool allowOutfitRoot;
        private GUIStyle wrappedButton;

        public static void Open(GameObject initialAvatar, bool allowOutfitRoot,
            Action<GameObject, AvatarBoneChoice> onBoneSelected, Action onOutfitRootSelected)
        {
            var window = CreateInstance<AvatarBonePickerWindow>();
            window.titleContent = new GUIContent("ボーンを追加");
            window.minSize = new Vector2(350, 350);
            window.avatar = initialAvatar;
            window.allowOutfitRoot = allowOutfitRoot;
            window.onBoneSelected = onBoneSelected;
            window.onOutfitRootSelected = onOutfitRootSelected;
            window.RefreshChoices();
            window.ShowUtility();
        }

        private void RefreshChoices()
        {
            choices.Clear();
            if (avatar != null && TransformScaleUtility.IsEditable(avatar.transform))
            {
                choices = AvatarBoneCatalog.CreateChoices(avatar);
            }
        }

        private void OnGUI()
        {
            if (wrappedButton == null)
            {
                wrappedButton = new GUIStyle(EditorStyles.miniButton)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleLeft,
                    fixedHeight = 0
                };
            }
            GUILayout.Label("ボーンの選択元アバター", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            avatar = (GameObject)EditorGUILayout.ObjectField(avatar, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                RefreshChoices();
            }

            using (new EditorGUI.DisabledScope(!allowOutfitRoot))
            {
                if (GUILayout.Button("衣装全体"))
                {
                    onOutfitRootSelected?.Invoke();
                    Close();
                    GUIUtility.ExitGUI();
                }
            }
            if (avatar == null || !TransformScaleUtility.IsEditable(avatar.transform))
            {
                EditorGUILayout.HelpBox("Hierarchyのアバター本体を指定すると、そのアーマチュアから選べます。", MessageType.Info);
                return;
            }

            if (choices.Count == 0)
            {
                EditorGUILayout.HelpBox("アーマチュアを一意に特定できません。アバター本体とボーン階層を確認してください。", MessageType.Warning);
            }
            filter = EditorGUILayout.TextField("絞り込み", filter);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var choice in choices.Where(choice =>
                choice.displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                choice.primaryReference.relativePath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                string label = choice.displayName + (choice.opposite != null ? "（左右セット）" : "");
                string tooltip = choice.primaryReference.relativePath;
                if (choice.oppositeReference != null)
                {
                    tooltip += "\n" + choice.oppositeReference.relativePath;
                }
                if (GUILayout.Button(new GUIContent(label, tooltip), wrappedButton,
                    GUILayout.MinWidth(0), GUILayout.ExpandWidth(true), GUILayout.MinHeight(24)))
                {
                    onBoneSelected?.Invoke(avatar, choice);
                    Close();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
