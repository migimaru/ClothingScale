#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PersonalAvatarTools.ScaleProfiles
{
    public sealed partial class OutfitScaleWindow : EditorWindow
    {
        private ScaleProfileLibrary library;
        private ScaleProfileStore store;
        private readonly Dictionary<string, bool> targetChecks = new Dictionary<string, bool>();
        private List<OutfitScaleTargets> resolvedOutfits = new List<OutfitScaleTargets>();
        [SerializeField] private List<GameObject> loadedOutfits = new List<GameObject>();
        private GameObject lastBoneSourceAvatar;
        private bool canSaveProfiles;
        private bool targetsNeedRefresh;
        private bool renaming;
        private bool focusName;
        private string draftName;
        private string message = "プロファイルを追加し、＋から設定するボーンを選んでください。";
        private MessageType messageType = MessageType.None;
        private Vector2 scroll;
        private GUIStyle wrappedBold;
        private ScaleProfile SelectedProfile => library?.profiles.FirstOrDefault(profile => profile.id == library.selectedProfileId);

        [MenuItem("Tools/Clothing Scale")]
        public static void Open()
        {
            var window = GetWindow<OutfitScaleWindow>("衣装 Scale");
            window.minSize = new Vector2(360, 400);
            window.Show();
        }

        private void OnEnable()
        {
            store = new ScaleProfileStore(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            try
            {
                library = store.Load();
                canSaveProfiles = true;
            }
            catch (Exception exception)
            {
                library = new ScaleProfileLibrary();
                canSaveProfiles = false;
                SetMessage(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
            targetsNeedRefresh = true;
            Undo.undoRedoPerformed += OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private void OnDisable()
        {
            CancelProfileEdit();
            Undo.undoRedoPerformed -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        private void OnHierarchyChanged()
        {
            targetsNeedRefresh = true;
            Repaint();
        }

        private void OnGUI()
        {
            if (wrappedBold == null)
            {
                wrappedBold = new GUIStyle(EditorStyles.wordWrappedLabel) { fontStyle = FontStyle.Bold };
            }
            HandleRenameKeys();
            GUILayout.Label("衣装のScaleをまとめて設定", wrappedBold);
            GUILayout.Space(6);
            using (new EditorGUI.DisabledScope(!canSaveProfiles || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                DrawProfileControls();
                using (new EditorGUI.DisabledScope(profileEdit != null))
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    var profile = SelectedProfile;
                    if (profile != null)
                    {
                        GUILayout.Space(8);
                        GUILayout.Label("ボーンのScale設定", wrappedBold);
                        DrawSettings(profile);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("「追加」でプロファイルを作成してください。", MessageType.Info);
                    }
                    GUILayout.Space(12);
                    GUILayout.Label("Hierarchyで衣装の一番上を選択します（複数可）。非表示の衣装も対象です。",
                        EditorStyles.wordWrappedMiniLabel);
                    using (new EditorGUI.DisabledScope(profile == null))
                    {
                        if (GUILayout.Button("選択中の衣装を読み込む", GUILayout.Height(25)))
                        {
                            LoadSelectedOutfits();
                        }
                    }
                    GUILayout.Space(6);
                    RefreshTargetsIfNeeded();
                    var assignments = DrawOutfits();
                    EditorGUILayout.EndScrollView();
                    GUILayout.Space(4);
                    bool validSettings = profile != null && profile.settings.All(setting => TransformScaleUtility.IsValidScale(setting.scale));
                    using (new EditorGUI.DisabledScope(assignments.Count == 0 || !validSettings))
                    {
                        if (GUILayout.Button("チェックした " + assignments.Select(assignment => assignment.target).Distinct().Count() +
                            " 件に適用", GUILayout.Height(28)))
                        {
                            ApplyCheckedSettings();
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(message))
            {
                EditorGUILayout.HelpBox(message, messageType);
            }
            GUILayout.Label(profileEdit != null ? "一覧の変更は「完了」で保存。「キャンセル」で変更を破棄します。" :
                "設定は自動保存。適用の取り消しはCtrl+Z。結果を残すにはシーンを保存してください。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawProfileControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("プロファイル", GUILayout.Width(82));
                if (renaming)
                {
                    GUI.SetNextControlName("ProfileRename");
                    draftName = EditorGUILayout.TextField(draftName, GUILayout.MinWidth(0));
                    if (focusName)
                    {
                        EditorGUI.FocusTextInControl("ProfileRename");
                        focusName = false;
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(profileEdit != null || library.profiles.Count == 0))
                    {
                        if (library.profiles.Count == 0)
                        {
                            EditorGUILayout.Popup(0, new[] { "未選択" });
                        }
                        else
                        {
                            int current = library.profiles.FindIndex(profile => profile.id == library.selectedProfileId);
                            var names = library.profiles.Select(profile => new GUIContent(profile.name, profile.name)).ToArray();
                            int selected = EditorGUILayout.Popup(Mathf.Max(0, current), names);
                            if (selected != current) SelectProfile(library.profiles[selected].id);
                        }
                    }
                }
            }
            if (profileEdit != null)
            {
                DrawProfileEdit();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(renaming || profileEdit != null))
                {
                    if (GUILayout.Button("追加"))
                    {
                        var profile = new ScaleProfile { name = NewProfileName() };
                        library.profiles.Add(profile);
                        library.selectedProfileId = profile.id;
                        targetChecks.Clear();
                        targetsNeedRefresh = true;
                        SaveProfiles();
                        BeginRename();
                    }
                }
                using (new EditorGUI.DisabledScope(SelectedProfile == null || renaming || profileEdit != null))
                {
                    if (DrawProfileButton("削除", true))
                    {
                        DeleteSelectedProfile();
                    }
                }
                using (new EditorGUI.DisabledScope(SelectedProfile == null || profileEdit != null))
                {
                    if (GUILayout.Button(renaming ? "確定" : "名前変更"))
                    {
                        if (renaming)
                        {
                            FinishRename();
                        }
                        else
                        {
                            BeginRename();
                        }
                    }
                }
                using (new EditorGUI.DisabledScope(renaming || profileEdit != null || library.profiles.Count < 2))
                {
                    if (DrawProfileButton("並び替え", false, profileEdit != null))
                    {
                        BeginProfileEdit();
                    }
                }
            }
            if (renaming)
            {
                GUILayout.Label("Enterで確定、Escでキャンセル", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private string NewProfileName()
        {
            string name = "新しいプロファイル";
            int suffix = 2;
            while (library.profiles.Any(profile => profile.name == name))
            {
                name = "新しいプロファイル " + suffix++;
            }
            return name;
        }

        private void BeginRename()
        {
            draftName = SelectedProfile.name;
            renaming = true;
            focusName = true;
        }

        private void HandleRenameKeys()
        {
            if (profileEdit != null)
            {
                HandleProfileEditKeys();
                return;
            }
            if (!renaming || Event.current.type != EventType.KeyDown)
            {
                return;
            }
            if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
            {
                FinishRename();
                Event.current.Use();
            }
            else if (Event.current.keyCode == KeyCode.Escape)
            {
                renaming = false;
                GUI.FocusControl(null);
                Event.current.Use();
            }
        }

        private void FinishRename()
        {
            string name = draftName?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                SetMessage("プロファイル名を入力してください。", MessageType.Warning);
                return;
            }
            if (library.profiles.Any(profile => profile != SelectedProfile && profile.name == name))
            {
                SetMessage("同じプロファイル名があるため、別の名前を指定してください。", MessageType.Warning);
                return;
            }
            SelectedProfile.name = name;
            renaming = false;
            GUI.FocusControl(null);
            SaveProfiles();
        }

        private void DeleteSelectedProfile()
        {
            var profile = SelectedProfile;
            if (profile == null)
            {
                return;
            }
            library.profiles.Remove(profile);
            library.selectedProfileId = library.profiles.FirstOrDefault()?.id;
            targetChecks.Clear();
            targetsNeedRefresh = true;
            SaveProfiles();
        }

        private void DrawSettings(ScaleProfile profile)
        {
            for (int index = 0; index < profile.settings.Count; index++)
            {
                var setting = profile.settings[index];
                bool remove;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUI.BeginChangeCheck();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(setting.displayName, wrappedBold, GUILayout.MinWidth(0));
                        if (setting.oppositeBone != null)
                        {
                            setting.useBothSides = GUILayout.Toggle(setting.useBothSides, "左右セット",
                                EditorStyles.miniButton, GUILayout.Width(78));
                        }
                        remove = GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(24));
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var icon = EditorGUIUtility.IconContent(setting.linkAxes ? "Linked" : "Unlinked");
                        var content = new GUIContent(icon.image, "XYZの比率を保って連動させる");
                        setting.linkAxes = GUILayout.Toggle(setting.linkAxes, content, EditorStyles.miniButton,
                            GUILayout.Width(24), GUILayout.Height(18));
                        Vector3 edited = EditorGUILayout.Vector3Field(GUIContent.none, setting.scale, GUILayout.MinWidth(0));
                        setting.scale = ScaleAxisLinking.Update(setting.scale, edited, setting.linkAxes);
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        targetsNeedRefresh = true;
                        SaveProfiles();
                    }
                    if (!TransformScaleUtility.IsValidScale(setting.scale))
                    {
                        GUILayout.Label("各軸には0より大きい有限の値を入力してください。", EditorStyles.wordWrappedMiniLabel);
                    }
                }
                if (remove)
                {
                    profile.settings.RemoveAt(index);
                    targetsNeedRefresh = true;
                    SaveProfiles();
                    GUIUtility.ExitGUI();
                }
            }
            if (GUILayout.Button("＋ ボーンを追加"))
            {
                var targetProfile = profile;
                AvatarBonePickerWindow.Open(BoneDefinitionResolver.FindAvatarInParents(Selection.activeTransform) ?? lastBoneSourceAvatar,
                    !profile.settings.Any(setting => setting.targetKind == ScaleTargetKind.OutfitRoot),
                    (avatar, choice) => AddBoneSetting(targetProfile, avatar, choice),
                    () => AddOutfitRootSetting(targetProfile));
            }
        }

        private void AddBoneSetting(ScaleProfile profile, GameObject avatar, AvatarBoneChoice choice)
        {
            if (profileEdit != null || !library.profiles.Contains(profile))
            {
                return;
            }
            lastBoneSourceAvatar = avatar;
            var existing = profile.settings.FirstOrDefault(setting => setting.targetKind == ScaleTargetKind.Bone &&
                (BoneDefinitionResolver.SameDefinition(setting.primaryBone, choice.primaryReference) ||
                 BoneDefinitionResolver.SameDefinition(setting.primaryBone, choice.oppositeReference)));
            if (existing != null)
            {
                bool reversed = BoneDefinitionResolver.SameDefinition(existing.primaryBone, choice.oppositeReference);
                existing.primaryBone = reversed ? choice.oppositeReference : choice.primaryReference;
                existing.oppositeBone = reversed ? choice.primaryReference : choice.oppositeReference;
                if (choice.opposite == null)
                {
                    existing.useBothSides = false;
                }
                targetsNeedRefresh = true;
                SaveProfiles();
                SetMessage("ボーン定義を更新しました。保存済みのScaleは保持しています。", MessageType.Info);
                return;
            }
            profile.settings.Add(new ScaleSetting
            {
                displayName = choice.displayName,
                targetKind = ScaleTargetKind.Bone,
                primaryBone = choice.primaryReference,
                oppositeBone = choice.oppositeReference,
                useBothSides = choice.opposite != null,
                scale = choice.primary.localScale
            });
            targetsNeedRefresh = true;
            SaveProfiles();
        }

        private void AddOutfitRootSetting(ScaleProfile profile)
        {
            if (profileEdit != null || !library.profiles.Contains(profile) ||
                profile.settings.Any(setting => setting.targetKind == ScaleTargetKind.OutfitRoot))
            {
                return;
            }
            profile.settings.Add(new ScaleSetting { displayName = "衣装全体", targetKind = ScaleTargetKind.OutfitRoot });
            targetsNeedRefresh = true;
            SaveProfiles();
        }

        private void SaveProfiles()
        {
            if (!canSaveProfiles)
            {
                return;
            }
            if (library.profiles.Any(profile => profile.settings.Any(setting => !TransformScaleUtility.IsValidScale(setting.scale))))
            {
                SetMessage("無効なScaleは保存していません。値を直すと自動保存します。", MessageType.Warning);
                return;
            }
            try
            {
                store.Save(library);
                SetMessage("プロファイルの設定を保存しました。", MessageType.None);
            }
            catch (Exception exception)
            {
                SetMessage("保存に失敗しました。設定はまだこのウィンドウ内だけにあります。", MessageType.Error);
                Debug.LogException(exception);
            }
        }

        private void LoadSelectedOutfits()
        {
            var selected = Selection.gameObjects;
            loadedOutfits = OutfitScaleService.FilterOutfitRoots(selected, SelectedProfile);
            targetChecks.Clear();
            targetsNeedRefresh = true;
            int skipped = selected.Length - loadedOutfits.Count;
            SetMessage(loadedOutfits.Count + " 衣装を読み込みました。" +
                (skipped > 0 ? " アバター本体・素材・重複する子階層など " + skipped + " 件を除外しました。" : ""), MessageType.None);
        }

        private void RefreshTargetsIfNeeded()
        {
            if (!targetsNeedRefresh)
            {
                return;
            }
            resolvedOutfits = SelectedProfile == null ? new List<OutfitScaleTargets>() :
                OutfitScaleService.Resolve(SelectedProfile, loadedOutfits);
            targetsNeedRefresh = false;
        }

        private string CheckKey(OutfitScaleTargets outfit, ResolvedScaleSetting setting)
        {
            return SelectedProfile.id + ":" + outfit.outfit.GetInstanceID() + ":" + setting.setting.id;
        }

        private List<ScaleAssignment> DrawOutfits()
        {
            var assignments = new List<ScaleAssignment>();
            foreach (var outfit in resolvedOutfits)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(outfit.outfit.name, wrappedBold, GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
                    foreach (var resolved in outfit.settings)
                    {
                        string key = CheckKey(outfit, resolved);
                        if (!targetChecks.ContainsKey(key))
                        {
                            targetChecks[key] = true;
                        }
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            using (new EditorGUI.DisabledScope(!resolved.CanApply))
                            {
                                targetChecks[key] = EditorGUILayout.Toggle(targetChecks[key] && resolved.CanApply, GUILayout.Width(18));
                            }
                            using (new EditorGUILayout.VerticalScope())
                            {
                                GUILayout.Label(resolved.setting.displayName + (resolved.setting.useBothSides ? "（左右セット）" : ""),
                                    EditorStyles.wordWrappedLabel);
                                if (!resolved.CanApply)
                                {
                                    GUILayout.Label("未適用：" + resolved.issue, EditorStyles.wordWrappedMiniLabel);
                                }
                                else
                                {
                                    foreach (var target in resolved.targets)
                                    {
                                        GUILayout.Label(target.name + "：現在 " + target.localScale.ToString("G4") +
                                            " → 適用 " + resolved.setting.scale.ToString("G4"), EditorStyles.wordWrappedMiniLabel);
                                        if (targetChecks[key])
                                        {
                                            assignments.Add(new ScaleAssignment { target = target, value = resolved.setting.scale });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                GUILayout.Space(4);
            }
            return assignments;
        }

        private void ApplyCheckedSettings()
        {
            try
            {
                var current = OutfitScaleService.Resolve(SelectedProfile, loadedOutfits);
                var assignments = new List<ScaleAssignment>();
                int skipped = 0;
                foreach (var outfit in current)
                {
                    foreach (var resolved in outfit.settings)
                    {
                        string key = CheckKey(outfit, resolved);
                        if (!resolved.CanApply)
                        {
                            skipped++;
                            continue;
                        }
                        if (!targetChecks.TryGetValue(key, out bool isChecked) || !isChecked)
                        {
                            continue;
                        }
                        var previous = resolvedOutfits.FirstOrDefault(group => group.outfit == outfit.outfit)?
                            .settings.FirstOrDefault(setting => setting.setting.id == resolved.setting.id);
                        if (previous == null || !previous.CanApply ||
                            !new HashSet<Transform>(previous.targets).SetEquals(resolved.targets))
                        {
                            throw new InvalidOperationException("読み込み後にボーンの対応が変わりました。衣装を読み込み直してください。");
                        }
                        foreach (var target in resolved.targets)
                        {
                            assignments.Add(new ScaleAssignment { target = target, value = resolved.setting.scale });
                        }
                    }
                }
                int changed = OutfitScaleService.Apply(assignments);
                SetMessage(changed + " 件のScaleを変更しました。" +
                    (skipped > 0 ? " 対応不明の " + skipped + " 設定は未適用です。" : ""), MessageType.None);
                targetsNeedRefresh = true;
            }
            catch (Exception exception)
            {
                SetMessage(exception.Message, MessageType.Error);
                Debug.LogException(exception);
                targetsNeedRefresh = true;
            }
        }

        private void SetMessage(string text, MessageType type)
        {
            message = text;
            messageType = type;
        }
    }
}
#endif
