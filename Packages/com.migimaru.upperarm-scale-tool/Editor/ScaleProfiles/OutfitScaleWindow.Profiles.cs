#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PersonalAvatarTools.ScaleProfiles
{
    public sealed partial class OutfitScaleWindow
    {
        private const int VisibleProfileRows = 8;
        private const float ProfileDropOutsideMargin = 24f;
        private static float ProfileRowHeight => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        private ProfileListEditSession profileEdit;
        private string highlightedProfileId;
        private string profileRenameId;
        private string profileDraftName;
        private bool focusProfileName;
        private Vector2 profileListScroll;
        private GUIStyle profileNameStyle;
        private int profileDragFrom = -1;
        private int profileDragInsertion = -1;
        private int profileDragControl;
        private bool profileDragging;
        private Vector2 profileDragStart;
        private Vector2 profileDragPointer;
        private Rect profileListViewport;
        private double profileDragTime;

        private static bool DrawProfileButton(string label, bool danger, bool active = false)
        {
            if ((!danger && !active) || (!GUI.enabled && !active)) return GUILayout.Button(label);
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(label), GUI.skin.button, GUILayout.ExpandWidth(true));
            bool clicked = GUI.Button(rect, GUIContent.none);
            if (Event.current.type == EventType.Repaint)
            {
                Color background = active ? new Color(0.16f, 0.43f, 0.24f) : new Color(0.65f, 0.16f, 0.16f);
                EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), background);
                bool enabled = GUI.enabled;
                GUI.enabled = true;
                var style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
                style.normal.textColor = Color.white;
                GUI.Label(rect, label, style);
                GUI.enabled = enabled;
            }
            return clicked;
        }

        private void SelectProfile(string id)
        {
            if (profileEdit != null || renaming || !canSaveProfiles || EditorApplication.isPlayingOrWillChangePlaymode ||
                !library.profiles.Any(profile => profile.id == id) || library.selectedProfileId == id) return;
            library.selectedProfileId = id;
            targetChecks.Clear();
            targetsNeedRefresh = true;
            SaveProfiles();
            Repaint();
        }

        private void BeginProfileEdit()
        {
            if (profileEdit != null || renaming || library.profiles.Count < 2) return;
            profileEdit = new ProfileListEditSession(library);
            highlightedProfileId = null;
            profileListScroll = Vector2.zero;
            SetMessage("行を選んで名前変更・削除、左端の取っ手で並び替えができます。変更は「完了」で保存します。", MessageType.None);
            GUIUtility.ExitGUI();
        }

        private void CancelProfileEdit()
        {
            StopProfileDrag();
            profileEdit = null;
            highlightedProfileId = null;
            profileRenameId = null;
            profileDraftName = null;
            focusProfileName = false;
        }

        private bool FinishProfileRename()
        {
            if (profileRenameId == null) return true;
            if (!profileEdit.TryRename(profileRenameId, profileDraftName, out string error))
            {
                SetMessage(error, MessageType.Warning);
                focusProfileName = true;
                return false;
            }
            profileRenameId = null;
            profileDraftName = null;
            GUI.FocusControl(null);
            SetMessage("名前を仮変更しました。「完了」で保存します。", MessageType.None);
            return true;
        }

        private void CommitProfileEdit()
        {
            if (!FinishProfileRename()) return;
            var candidate = profileEdit.CreateLibrary();
            try
            {
                // Publish to the live window only after the entire candidate is saved successfully.
                store.Save(candidate);
            }
            catch (Exception exception)
            {
                SetMessage("保存に失敗しました。一覧の変更は保持しています。再度「完了」で保存するか、「キャンセル」で破棄してください。", MessageType.Error);
                Debug.LogException(exception);
                return;
            }
            bool selectionChanged = library.selectedProfileId != candidate.selectedProfileId;
            library = candidate;
            if (selectionChanged) targetChecks.Clear();
            targetsNeedRefresh = true;
            CancelProfileEdit();
            SetMessage("プロファイル一覧の変更を保存しました。", MessageType.None);
            GUIUtility.ExitGUI();
        }

        private void HandleProfileEditKeys()
        {
            var evt = Event.current;
            if (evt.type != EventType.KeyDown) return;
            if (evt.keyCode == KeyCode.Escape)
            {
                if (profileDragFrom >= 0) StopProfileDrag();
                else if (profileRenameId != null)
                {
                    profileRenameId = null;
                    profileDraftName = null;
                    GUI.FocusControl(null);
                }
                // Esc never closes the expanded editing mode.
                evt.Use();
            }
            else if (profileRenameId != null && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
            {
                FinishProfileRename();
                evt.Use();
            }
        }

        private void DrawProfileEdit()
        {
            GUILayout.Space(4);
            bool hasHighlight = profileEdit.Entries.Any(entry => entry.Id == highlightedProfileId);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasHighlight || profileDragFrom >= 0))
                {
                    if (GUILayout.Button("名前変更"))
                    {
                        if (FinishProfileRename())
                        {
                            profileRenameId = highlightedProfileId;
                            profileDraftName = profileEdit.Entries.First(entry => entry.Id == highlightedProfileId).Name;
                            focusProfileName = true;
                        }
                    }
                    if (DrawProfileButton("削除", true))
                    {
                        profileEdit.Delete(highlightedProfileId);
                        highlightedProfileId = null;
                        profileRenameId = null;
                        profileDraftName = null;
                        GUI.FocusControl(null);
                        SetMessage("一覧から仮削除しました。「完了」で保存、「キャンセル」で元に戻せます。", MessageType.None);
                        GUIUtility.ExitGUI();
                    }
                }
                using (new EditorGUI.DisabledScope(profileDragFrom >= 0))
                {
                    if (GUILayout.Button("完了")) CommitProfileEdit();
                }
                if (GUILayout.Button("キャンセル"))
                {
                    CancelProfileEdit();
                    SetMessage("プロファイル一覧の変更を破棄しました。", MessageType.None);
                    GUIUtility.ExitGUI();
                }
            }
            DrawProfileEditRows();
            if (profileRenameId != null)
            {
                GUILayout.Label("Enterで名前を仮確定。Escで名前の入力を取り消します。", EditorStyles.wordWrappedMiniLabel);
            }
            GUILayout.Space(4);
        }

        private void DrawProfileEditRows()
        {
            if (profileNameStyle == null) profileNameStyle = new GUIStyle(EditorStyles.label)
            {
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft
            };
            float rowHeight = ProfileRowHeight;
            int count = profileEdit.Entries.Count;
            float height = Mathf.Min(Mathf.Max(1, count) * rowHeight,
                Mathf.Min(VisibleProfileRows * rowHeight, Mathf.Max(rowHeight * 2, position.height - 220)));
            profileListViewport = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
            int control = GUIUtility.GetControlID("ProfileListDrag".GetHashCode(), FocusType.Passive);
            var evt = Event.current;
            Vector2 pointer = evt.mousePosition;
            float contentWidth = Mathf.Max(1, profileListViewport.width - (count * rowHeight > height ? 16 : 0));
            EditorGUI.DrawRect(profileListViewport, EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.87f, 0.87f, 0.87f));
            profileListScroll = GUI.BeginScrollView(profileListViewport, profileListScroll,
                new Rect(0, 0, contentWidth, Mathf.Max(1, count) * rowHeight));
            int first = Mathf.Max(0, Mathf.FloorToInt(profileListScroll.y / rowHeight));
            int last = Mathf.Min(count, Mathf.CeilToInt((profileListScroll.y + height) / rowHeight));
            for (int index = first; index < last; index++)
            {
                var entry = profileEdit.Entries[index];
                var row = new Rect(0, index * rowHeight, contentWidth, rowHeight);
                var handle = new Rect(row.x, row.y, 28, rowHeight);
                var nameRect = new Rect(handle.xMax + 2, row.center.y - EditorGUIUtility.singleLineHeight * 0.5f,
                    Mathf.Max(1, row.width - 36), EditorGUIUtility.singleLineHeight);
                bool highlighted = entry.Id == highlightedProfileId;
                if (highlighted) EditorGUI.DrawRect(row, ProfileHighlightColor);
                if (entry.Id == profileRenameId)
                {
                    GUI.SetNextControlName("ProfileListRename");
                    profileDraftName = EditorGUI.TextField(nameRect, profileDraftName);
                    if (focusProfileName)
                    {
                        EditorGUI.FocusTextInControl("ProfileListRename");
                        focusProfileName = false;
                    }
                }
                else
                {
                    Color previous = profileNameStyle.normal.textColor;
                    if (highlighted) profileNameStyle.normal.textColor = Color.white;
                    GUI.Label(nameRect, new GUIContent(FitProfileName(entry.Name, profileNameStyle, nameRect.width), entry.Name), profileNameStyle);
                    profileNameStyle.normal.textColor = previous;
                }
                Color handleColor = highlighted ? Color.white : (EditorGUIUtility.isProSkin ? Color.gray : new Color(0.3f, 0.3f, 0.3f));
                if (evt.type == EventType.Repaint)
                {
                    for (int line = 0; line < 3; line++)
                        EditorGUI.DrawRect(new Rect(handle.x + 7, handle.center.y - 5 + line * 4, 14, 2), handleColor);
                }
                GUI.Label(handle, new GUIContent("", "ドラッグして並び替え"));
                EditorGUIUtility.AddCursorRect(handle, MouseCursor.Pan);
                if (GUI.enabled && evt.type == EventType.MouseDown && evt.button == 0 &&
                    profileListViewport.Contains(pointer) && row.Contains(evt.mousePosition))
                {
                    if (profileRenameId != null && !FinishProfileRename())
                    {
                        evt.Use();
                        continue;
                    }
                    highlightedProfileId = entry.Id;
                    GUI.FocusControl(null);
                    if (handle.Contains(evt.mousePosition))
                    {
                        profileDragFrom = index;
                        profileDragInsertion = index;
                        profileDragStart = profileDragPointer = pointer;
                        profileDragControl = control;
                        profileDragging = false;
                        profileDragTime = EditorApplication.timeSinceStartup;
                        GUIUtility.hotControl = control;
                        EditorApplication.update += UpdateProfileDrag;
                    }
                    evt.Use();
                    Repaint();
                }
            }
            if (count == 0)
            {
                GUI.Label(new Rect(10, 0, contentWidth - 20, rowHeight), "プロファイルはありません。", EditorStyles.label);
            }
            if (profileDragging && profileDragInsertion >= 0)
            {
                float y = Mathf.Clamp(profileDragInsertion * rowHeight, 1, Mathf.Max(1, count * rowHeight - 2));
                EditorGUI.DrawRect(new Rect(0, y, contentWidth, 2), new Color(0.2f, 0.7f, 1f));
            }
            GUI.EndScrollView();

            if (profileDragFrom >= 0 && GUIUtility.hotControl != profileDragControl) StopProfileDrag();
            if (profileDragFrom >= 0)
            {
                if (!GUI.enabled)
                {
                    StopProfileDrag();
                }
                else if (evt.type == EventType.MouseDrag)
                {
                    profileDragPointer = pointer;
                    profileDragging |= (pointer - profileDragStart).sqrMagnitude > 16f;
                    UpdateProfileInsertion();
                    evt.Use();
                    Repaint();
                }
                else if (evt.type == EventType.MouseUp && evt.button == 0)
                {
                    if (profileDragging && IsProfileDropPoint(pointer))
                    {
                        profileDragPointer = pointer;
                        UpdateProfileInsertion();
                        profileEdit.Move(profileDragFrom, profileDragInsertion);
                    }
                    StopProfileDrag();
                    evt.Use();
                    Repaint();
                }
            }
        }

        private void UpdateProfileInsertion()
        {
            float maxScroll = Mathf.Max(0, profileEdit.Entries.Count * ProfileRowHeight - profileListViewport.height);
            // Give the first and last gap a whole row of tolerance at the scroll limits.
            if (profileListScroll.y <= 0.5f && profileDragPointer.y <= profileListViewport.y + ProfileRowHeight)
            {
                profileDragInsertion = 0;
                return;
            }
            if (profileListScroll.y >= maxScroll - 0.5f &&
                profileDragPointer.y >= profileListViewport.yMax - ProfileRowHeight)
            {
                profileDragInsertion = profileEdit.Entries.Count;
                return;
            }
            profileDragInsertion = Mathf.Clamp(Mathf.FloorToInt(
                (profileDragPointer.y - profileListViewport.y + profileListScroll.y) / ProfileRowHeight + 0.5f),
                0, profileEdit.Entries.Count);
        }

        private bool IsProfileDropPoint(Vector2 pointer)
        {
            var target = profileListViewport;
            // While a handle is captured, releasing just outside the list still drops at its edge.
            target.yMin -= ProfileDropOutsideMargin;
            target.yMax += ProfileDropOutsideMargin;
            return target.Contains(pointer);
        }

        private void UpdateProfileDrag()
        {
            if (profileEdit == null || profileDragFrom < 0 || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                StopProfileDrag();
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            float elapsed = Mathf.Clamp((float)(now - profileDragTime), 0, 0.05f);
            profileDragTime = now;
            if (!profileDragging) return;
            float direction = profileDragPointer.y < profileListViewport.y + 24 ? -1 :
                profileDragPointer.y > profileListViewport.yMax - 24 ? 1 : 0;
            float maxScroll = Mathf.Max(0, profileEdit.Entries.Count * ProfileRowHeight - profileListViewport.height);
            profileListScroll.y = Mathf.Clamp(profileListScroll.y + direction * 220f * elapsed, 0, maxScroll);
            UpdateProfileInsertion();
            Repaint();
        }

        private void StopProfileDrag()
        {
            EditorApplication.update -= UpdateProfileDrag;
            if (profileDragFrom >= 0 && GUIUtility.hotControl == profileDragControl) GUIUtility.hotControl = 0;
            profileDragFrom = profileDragInsertion = -1;
            profileDragging = false;
        }

        private void OnLostFocus()
        {
            StopProfileDrag();
        }

        private static Color ProfileHighlightColor => EditorGUIUtility.isProSkin ?
            new Color(0.15f, 0.35f, 0.57f) : new Color(0.16f, 0.37f, 0.62f);

        private static string FitProfileName(string name, GUIStyle style, float width)
        {
            if (style.CalcSize(new GUIContent(name)).x <= width) return name;
            int low = 0, high = name.Length;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (style.CalcSize(new GUIContent(name.Substring(0, middle) + "…")).x <= width) low = middle;
                else high = middle - 1;
            }
            if (low > 0 && char.IsHighSurrogate(name[low - 1])) low--;
            return name.Substring(0, low) + "…";
        }
    }
}
#endif
