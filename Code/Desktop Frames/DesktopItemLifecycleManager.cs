using IWshRuntimeLibrary;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IoFile = System.IO.File;

namespace Desktop_Frames
{
    internal static class DesktopItemLifecycleManager
    {
        public const string RestoreToDesktopOnExitKey = "RestoreToDesktopOnExit";
        public const string DesktopRestoreKindKey = "DesktopRestoreKind";
        public const string DesktopRestorePathKey = "DesktopRestorePath";
        public const string StoredItemPathKey = "StoredItemPath";

        public const string FileKind = "File";
        public const string ShortcutKind = "Shortcut";

        private const string StoredDesktopItemsFolder = "Stored Desktop Items";

        public static void CollectCurrentProfileDesktopItems()
        {
            try
            {
                if (FrameDataManager.FrameData == null) return;

                bool modified = false;
                string profileDir = ProfileManager.CurrentProfileDir;

                foreach (dynamic frame in FrameDataManager.FrameData)
                {
                    if (frame is not JObject frameObject) continue;

                    foreach (JObject item in EnumerateItems(frameObject))
                    {
                        modified |= CollectItemForRunningSession(profileDir, item);
                    }
                }

                if (modified)
                {
                    FrameDataManager.SaveFrameData();
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"Desktop item collection failed: {ex.Message}");
            }
        }

        public static int RepairCurrentProfileStoredItemShortcuts()
        {
            try
            {
                if (FrameDataManager.FrameData == null) return 0;

                int repaired = 0;
                bool modified = false;
                string profileDir = ProfileManager.CurrentProfileDir;

                foreach (dynamic frame in FrameDataManager.FrameData)
                {
                    if (frame is not JObject frameObject) continue;

                    foreach (JObject item in EnumerateItems(frameObject))
                    {
                        if (RepairStoredItemShortcut(profileDir, item))
                        {
                            repaired++;
                            modified = true;
                        }
                    }
                }

                if (modified)
                {
                    FrameDataManager.SaveFrameData();
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        $"Repaired {repaired} stored desktop item shortcuts.");
                }

                return repaired;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Stored desktop item shortcut repair failed: {ex.Message}");
                return 0;
            }
        }

        public static void RestoreAllProfilesToDesktop()
        {
            try
            {
                string profilesRoot = ProfileManager.ProfilesRootDir;
                if (!Directory.Exists(profilesRoot)) return;

                foreach (string profileDir in Directory.GetDirectories(profilesRoot))
                {
                    RestoreProfileToDesktop(profileDir);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"Desktop item restore failed: {ex.Message}");
            }
        }

        private static void RestoreProfileToDesktop(string profileDir)
        {
            string framesPath = Path.Combine(profileDir, "frames.json");
            if (!IoFile.Exists(framesPath)) return;

            try
            {
                JArray frames = JArray.Parse(IoFile.ReadAllText(framesPath));
                bool modified = false;

                foreach (JObject frame in frames.OfType<JObject>())
                {
                    foreach (JObject item in EnumerateItems(frame))
                    {
                        modified |= RestoreItemToDesktop(profileDir, item);
                    }
                }

                if (modified)
                {
                    IoFile.WriteAllText(framesPath, JsonConvert.SerializeObject(frames, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"Could not restore desktop items for profile {profileDir}: {ex.Message}");
            }
        }

        private static IEnumerable<JObject> EnumerateItems(JObject frame)
        {
            if (frame["Items"] is JArray items)
            {
                foreach (JObject item in items.OfType<JObject>())
                {
                    yield return item;
                }
            }

            if (frame["Tabs"] is not JArray tabs) yield break;

            foreach (JObject tab in tabs.OfType<JObject>())
            {
                if (tab["Items"] is not JArray tabItems) continue;
                foreach (JObject item in tabItems.OfType<JObject>())
                {
                    yield return item;
                }
            }
        }

        private static bool CollectItemForRunningSession(string profileDir, JObject item)
        {
            try
            {
                string shortcutPath = ResolveProfilePath(profileDir, GetString(item, "Filename"));
                if (!IsShortcutFile(shortcutPath)) return false;

                bool restoreToDesktop = GetBoolean(item, RestoreToDesktopOnExitKey);
                string restoreKind = GetString(item, DesktopRestoreKindKey);

                if (restoreToDesktop && string.Equals(restoreKind, ShortcutKind, StringComparison.OrdinalIgnoreCase))
                {
                    string desktopShortcutPath = GetString(item, DesktopRestorePathKey);
                    if (IsDesktopPath(desktopShortcutPath) && IoFile.Exists(desktopShortcutPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
                        IoFile.Copy(desktopShortcutPath, shortcutPath, true);
                        IoFile.Delete(desktopShortcutPath);
                    }
                    return false;
                }

                string targetPath = GetShortcutTarget(shortcutPath);
                string storedDir = Path.Combine(profileDir, StoredDesktopItemsFolder);

                if (!restoreToDesktop
                    && IsDesktopPath(targetPath)
                    && Directory.Exists(targetPath)
                    && !ShouldSkipCollectingPath(targetPath))
                {
                    Directory.CreateDirectory(storedDir);
                    string collectedPath = GetUniquePath(Path.Combine(storedDir, Path.GetFileName(targetPath)));
                    MovePath(targetPath, collectedPath);
                    RetargetShortcut(shortcutPath, collectedPath);

                    item[RestoreToDesktopOnExitKey] = true;
                    item[DesktopRestoreKindKey] = FileKind;
                    item[DesktopRestorePathKey] = targetPath;
                    item[StoredItemPathKey] = collectedPath;
                    return true;
                }

                if (!restoreToDesktop && IsUnderDirectory(targetPath, storedDir))
                {
                    item[RestoreToDesktopOnExitKey] = true;
                    item[DesktopRestoreKindKey] = FileKind;
                    item[DesktopRestorePathKey] = Path.Combine(GetUserDesktopPath(), Path.GetFileName(targetPath));
                    item[StoredItemPathKey] = targetPath;
                    return true;
                }

                if (!restoreToDesktop || !string.Equals(restoreKind, FileKind, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string desktopPath = GetString(item, DesktopRestorePathKey);
                if (!IsDesktopPath(desktopPath) || !PathExists(desktopPath) || ShouldSkipCollectingPath(desktopPath)) return false;

                Directory.CreateDirectory(storedDir);
                string storedPath = GetUniquePath(Path.Combine(storedDir, Path.GetFileName(desktopPath)));
                MovePath(desktopPath, storedPath);
                RetargetShortcut(shortcutPath, storedPath);

                item[StoredItemPathKey] = storedPath;
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Could not collect desktop item for running session: {ex.Message}");
                return false;
            }
        }

        private static bool RepairStoredItemShortcut(string profileDir, JObject item)
        {
            try
            {
                if (!GetBoolean(item, RestoreToDesktopOnExitKey)) return false;
                if (!string.Equals(GetString(item, DesktopRestoreKindKey), FileKind, StringComparison.OrdinalIgnoreCase)) return false;

                string shortcutPath = ResolveProfilePath(profileDir, GetString(item, "Filename"));
                if (!IsShortcutFile(shortcutPath)) return false;

                string storedPath = ResolveProfilePath(profileDir, GetString(item, StoredItemPathKey));
                if (string.IsNullOrWhiteSpace(storedPath) || !PathExists(storedPath)) return false;

                string currentTarget = GetShortcutTarget(shortcutPath);
                if (string.Equals(currentTarget, storedPath, StringComparison.OrdinalIgnoreCase)) return false;

                RetargetShortcut(shortcutPath, storedPath);
                item[StoredItemPathKey] = storedPath;

                string desktopRestorePath = GetString(item, DesktopRestorePathKey);
                if (!IsDesktopPath(desktopRestorePath))
                {
                    item[DesktopRestorePathKey] = Path.Combine(GetUserDesktopPath(), Path.GetFileName(storedPath));
                }

                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Could not repair stored desktop item shortcut: {ex.Message}");
                return false;
            }
        }

        private static bool RestoreItemToDesktop(string profileDir, JObject item)
        {
            try
            {
                string shortcutPath = ResolveProfilePath(profileDir, GetString(item, "Filename"));
                if (!IsShortcutFile(shortcutPath)) return false;

                bool restoreToDesktop = GetBoolean(item, RestoreToDesktopOnExitKey);
                string restoreKind = GetString(item, DesktopRestoreKindKey);

                if (restoreToDesktop && string.Equals(restoreKind, ShortcutKind, StringComparison.OrdinalIgnoreCase))
                {
                    string desktopShortcutPath = GetString(item, DesktopRestorePathKey);
                    if (!IsDesktopPath(desktopShortcutPath))
                    {
                        desktopShortcutPath = Path.Combine(GetUserDesktopPath(), Path.GetFileName(shortcutPath));
                        item[DesktopRestorePathKey] = desktopShortcutPath;
                    }

                    if (IoFile.Exists(shortcutPath) && !IoFile.Exists(desktopShortcutPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(desktopShortcutPath));
                        IoFile.Copy(shortcutPath, desktopShortcutPath, false);
                    }

                    return true;
                }

                string targetPath = GetShortcutTarget(shortcutPath);
                string storedDir = Path.Combine(profileDir, StoredDesktopItemsFolder);

                if (!restoreToDesktop && IsUnderDirectory(targetPath, storedDir))
                {
                    restoreToDesktop = true;
                    restoreKind = FileKind;
                    item[RestoreToDesktopOnExitKey] = true;
                    item[DesktopRestoreKindKey] = FileKind;
                }

                if (!restoreToDesktop || !string.Equals(restoreKind, FileKind, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (IsDesktopPath(targetPath))
                {
                    item[DesktopRestorePathKey] = targetPath;
                    return true;
                }

                if (!PathExists(targetPath) || !IsUnderDirectory(targetPath, storedDir))
                {
                    return false;
                }

                string desktopPath = GetString(item, DesktopRestorePathKey);
                if (!IsDesktopPath(desktopPath))
                {
                    desktopPath = Path.Combine(GetUserDesktopPath(), Path.GetFileName(targetPath));
                }

                desktopPath = GetUniquePath(desktopPath);
                Directory.CreateDirectory(Path.GetDirectoryName(desktopPath));
                MovePath(targetPath, desktopPath);
                RetargetShortcut(shortcutPath, desktopPath);

                item[DesktopRestorePathKey] = desktopPath;
                item[StoredItemPathKey] = targetPath;
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Could not restore desktop item: {ex.Message}");
                return false;
            }
        }

        private static string ResolveProfilePath(string profileDir, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return Path.IsPathRooted(path) ? path : Path.Combine(profileDir, path);
        }

        private static bool IsShortcutFile(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && IoFile.Exists(path)
                && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathExists(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && (IoFile.Exists(path) || Directory.Exists(path));
        }

        private static void MovePath(string sourcePath, string destinationPath)
        {
            if (Directory.Exists(sourcePath))
            {
                Directory.Move(sourcePath, destinationPath);
                return;
            }

            IoFile.Move(sourcePath, destinationPath);
        }

        private static bool ShouldSkipCollectingPath(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string appBase = Path.GetFullPath(AppContext.BaseDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(fullPath, appBase, StringComparison.OrdinalIgnoreCase)
                    || appBase.StartsWith(fullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string folderName = Path.GetFileName(fullPath);
                return folderName.StartsWith("DesktopFramesPlus-test-run-", StringComparison.OrdinalIgnoreCase)
                    || folderName.StartsWith("DesktopFramesPlus-source-run-", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        private static string GetShortcutTarget(string shortcutPath)
        {
            WshShell shell = new WshShell();
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
            return shortcut.TargetPath?.Trim() ?? string.Empty;
        }

        private static void RetargetShortcut(string shortcutPath, string targetPath)
        {
            WshShell shell = new WshShell();
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;

            string workingDirectory = Directory.Exists(targetPath)
                ? targetPath
                : Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                shortcut.WorkingDirectory = workingDirectory;
            }

            shortcut.Save();
        }

        private static string GetUniquePath(string desiredPath)
        {
            if (!IoFile.Exists(desiredPath) && !Directory.Exists(desiredPath)) return desiredPath;

            string directory = Path.GetDirectoryName(desiredPath);
            string name = Path.GetFileNameWithoutExtension(desiredPath);
            string extension = Path.GetExtension(desiredPath);
            int counter = 1;
            string candidate;

            do
            {
                candidate = Path.Combine(directory, $"{name} ({counter++}){extension}");
            }
            while (IoFile.Exists(candidate) || Directory.Exists(candidate));

            return candidate;
        }

        private static bool IsUnderDirectory(string path, string directory)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory)) return false;

            try
            {
                string fullPath = Path.GetFullPath(path);
                string fullDirectory = Path.GetFullPath(directory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDesktopPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                string itemDir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (string.IsNullOrWhiteSpace(itemDir)) return false;

                string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

                return string.Equals(itemDir, userDesktop, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(itemDir, commonDesktop, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string GetUserDesktopPath()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            return string.IsNullOrWhiteSpace(desktop)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : desktop;
        }

        private static string GetString(JObject item, string key)
        {
            return item[key]?.ToString() ?? string.Empty;
        }

        private static bool GetBoolean(JObject item, string key)
        {
            JToken token = item[key];
            if (token == null) return false;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            return bool.TryParse(token.ToString(), out bool value) && value;
        }
    }
}
