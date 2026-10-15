#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PersonalAvatarTools.ScaleProfiles
{
    // ProjectSettings travels with the project and keeps multiple avatars' profiles together.
    public sealed class ScaleProfileStore
    {
        public string FilePath { get; }

        public ScaleProfileStore(string projectDirectory)
        {
            FilePath = Path.Combine(projectDirectory, "ProjectSettings", "AvatarScaleProfiles.json");
        }

        public ScaleProfileLibrary Load()
        {
            if (!File.Exists(FilePath))
            {
                return new ScaleProfileLibrary();
            }

            try
            {
                string json = File.ReadAllText(FilePath);
                var library = JsonUtility.FromJson<ScaleProfileLibrary>(json);
                Validate(library);
                return library;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("プロファイルを読み込めません。既存の保存ファイルは上書きしません。", exception);
            }
        }

        public void Save(ScaleProfileLibrary library)
        {
            Validate(library);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporaryPath = FilePath + ".tmp";
            string backupPath = FilePath + ".bak";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(library, true));

            if (File.Exists(FilePath))
            {
                File.Replace(temporaryPath, FilePath, backupPath);
            }
            else
            {
                File.Move(temporaryPath, FilePath);
            }
        }

        private static void Validate(ScaleProfileLibrary library)
        {
            if (library == null || library.schemaVersion != 1 || library.profiles == null)
            {
                throw new InvalidOperationException("対応していないプロファイル形式です。");
            }

            var profileIds = new HashSet<string>();
            foreach (var profile in library.profiles)
            {
                if (profile == null || string.IsNullOrEmpty(profile.id) ||
                    !profileIds.Add(profile.id) || string.IsNullOrWhiteSpace(profile.name) || profile.settings == null)
                {
                    throw new InvalidOperationException("プロファイルの名前または識別情報が不正です。");
                }

                var settingIds = new HashSet<string>();
                foreach (var setting in profile.settings)
                {
                    if (setting == null || string.IsNullOrEmpty(setting.id) || !settingIds.Add(setting.id) ||
                        !TransformScaleUtility.IsValidScale(setting.scale) ||
                        !Enum.IsDefined(typeof(ScaleTargetKind), setting.targetKind))
                    {
                        throw new InvalidOperationException("Scale設定に無効な値があります。");
                    }

                    if (setting.targetKind == ScaleTargetKind.Bone &&
                        (setting.primaryBone == null || string.IsNullOrEmpty(setting.primaryBone.relativePath)))
                    {
                        throw new InvalidOperationException("ボーンの参照情報がありません。");
                    }
                    if (setting.useBothSides &&
                        (setting.oppositeBone == null || string.IsNullOrEmpty(setting.oppositeBone.relativePath)))
                    {
                        throw new InvalidOperationException("左右セットの相方がありません。");
                    }
                }
            }
        }
    }
}
#endif
