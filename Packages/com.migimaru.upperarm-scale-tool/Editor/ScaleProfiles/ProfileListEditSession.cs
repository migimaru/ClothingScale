#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;

namespace PersonalAvatarTools.ScaleProfiles
{
    // Names and ordering are drafts. The source library is never mutated before a successful save.
    internal sealed class ProfileListEditSession
    {
        internal sealed class Entry
        {
            internal readonly ScaleProfile Source;
            internal string Name;
            internal string Id => Source.id;

            internal Entry(ScaleProfile source)
            {
                Source = source;
                Name = source.name;
            }
        }

        private readonly ScaleProfileLibrary source;
        private readonly List<Entry> entries;
        internal IReadOnlyList<Entry> Entries => entries;

        internal ProfileListEditSession(ScaleProfileLibrary source)
        {
            this.source = source;
            entries = source.profiles.Select(profile => new Entry(profile)).ToList();
        }

        internal bool TryRename(string id, string name, out string error)
        {
            error = null;
            var entry = entries.FirstOrDefault(item => item.Id == id);
            if (entry == null)
            {
                error = "名前を変更するプロファイルを選択してください。";
                return false;
            }
            name = name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                error = "プロファイル名を入力してください。";
                return false;
            }
            if (entries.Any(item => item != entry && item.Name == name))
            {
                error = "同じプロファイル名があるため、別の名前を指定してください。";
                return false;
            }
            entry.Name = name;
            return true;
        }

        internal bool Delete(string id)
        {
            return entries.RemoveAll(entry => entry.Id == id) != 0;
        }

        // Destination is the gap between rows, before removing the dragged row.
        internal void Move(int from, int insertion)
        {
            if (from < 0 || from >= entries.Count || insertion < 0 || insertion > entries.Count)
            {
                return;
            }
            var entry = entries[from];
            entries.RemoveAt(from);
            entries.Insert(insertion > from ? insertion - 1 : insertion, entry);
        }

        internal ScaleProfileLibrary CreateLibrary()
        {
            var result = new ScaleProfileLibrary
            {
                schemaVersion = source.schemaVersion,
                selectedProfileId = source.selectedProfileId,
                profiles = entries.Select(entry => new ScaleProfile
                {
                    id = entry.Id,
                    name = entry.Name,
                    avatarGlobalId = entry.Source.avatarGlobalId,
                    avatarScenePath = entry.Source.avatarScenePath,
                    avatarHierarchyPath = entry.Source.avatarHierarchyPath,
                    // Scale controls and picker callbacks are blocked throughout the edit session.
                    settings = entry.Source.settings
                }).ToList()
            };
            if (!result.profiles.Any(profile => profile.id == result.selectedProfileId))
            {
                result.selectedProfileId = result.profiles.FirstOrDefault()?.id;
            }
            return result;
        }
    }
}
#endif
