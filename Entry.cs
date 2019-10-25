using Spectrum.API;
using Spectrum.API.Interfaces.Plugins;
using Spectrum.API.Interfaces.Systems;
using System;
using System.Reflection;
using Harmony;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using LevelEditorActions;
using Events.LevelEditor;

namespace Corecii.TrackMusic
{
    public class Entry : IPlugin, IUpdatable
    {
        public string FriendlyName => "CustomTrackMusic";
        public string Author => "Corecii";
        public string Contact => "SteamID: Corecii; Discord: Corecii#3019";
        public static string PluginVersion = "Version C.1.0.0";

        public void Initialize(IManager manager, string ipcIdentifier)
        {
            try {
                var harmony = HarmonyInstance.Create("com.corecii.distance.customtrackmusic");
                var assembly = Assembly.GetExecutingAssembly();
                assembly.GetTypes().Do(type =>
                {
                    try
                    {
                        var parentMethodInfos = type.GetHarmonyMethods();
                        if (parentMethodInfos != null && parentMethodInfos.Count() > 0)
                        {
                            var info = HarmonyMethod.Merge(parentMethodInfos);
                            var processor = new PatchProcessor(harmony, type, info);
                            processor.Patch();
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to patch {type}: {e}");
                    }
                });
            }
            catch (Exception e)
            {
                Console.WriteLine("Patching errors!\n" + e);
            }
        }

        public static bool NeedsRefresh = false;

        public static int MaxMusicDownloadSizeBytes = 1000 * 1000 * 10; // 10 MB
        public static int MaxMusicDownloadTimeMilli = 10000; // 10 seconds

        public static int MaxMusicLevelLoadTimeMilli = 10000; // 10 seconds

        public static string[] AllowedExtensions = new string[]
        {
            ".mp3",
            ".wav",
            ".aiff"
        };

        public static string[] AllowedDownloadSchemes = new string[]
        {
            "http",
            "https"
        };

        public static Dictionary<CustomName, CustomMusicData> AttachedData = new Dictionary<CustomName, CustomMusicData>();

        public static Dictionary<string, string> CurrentCustomMusic = new Dictionary<string, string>();

        public static bool IsCustomMusic(CustomName customName)
        {
            if (customName == null || customName.CustomName_ == null || customName.customName_.Length < 12)
            {
                return false;
            }
            return customName.customName_.Substring(0, 12) == "CustomMusic:";
        }

        public static void UpdateCustomMusicList()
        {
            CurrentCustomMusic.Clear();
            var levelPath = G.Sys.GameManager_.LevelPath_;
            var settings = G.Sys.GameManager_.LevelSettings_;
            if (G.Sys.GameManager_.IsLevelEditorMode_ || (G.Sys.LevelEditor_ != null && G.Sys.LevelEditor_.Active_))
            {
                levelPath = "EditorMusic";
                settings = G.Sys.LevelEditor_.WorkingSettings_;
            }
            var customNames = settings.GetComponents<CustomName>();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var customMusic in customNames)
            {
                if (stopwatch.ElapsedMilliseconds >= MaxMusicLevelLoadTimeMilli)
                {
                    break;
                }
                if (IsCustomMusic(customMusic))
                {
                    CustomMusicData musicData = null;
                    AttachedData.TryGetValue(customMusic, out musicData);
                    if (musicData == null || musicData.LastDataString != customMusic.customName_)
                    {
                        musicData = CustomMusicData.FromDataString(customMusic.customName_);
                        musicData.LastDataString = customMusic.customName_;
                        AttachedData[customMusic] = musicData;
                    }
                    if (musicData.IsException())
                    {
                        Debug.Log($"Failed to read music data: {musicData.Exception}");
                    }
                    else if (!musicData.IsValid())
                    {
                        Debug.Log($"Music data invalid: {musicData.Name}");
                    }
                    else
                    {
                        var safeName = System.Text.RegularExpressions.Regex.Replace(musicData.Name, @"\W", "");
                        var path = levelPath + "." + safeName + musicData.FileType;
                        if (!musicData.DownloadAttempted)
                        {
                            Debug.Log($"Downloading {musicData.Name}");
                            var wasPlaying = AudioManager.CurrentAudioFile_?.FileName == path;
                            var success = musicData.Download(levelPath, safeName);
                            Debug.Log($"Success: {success}");
                            if (success)
                            {
                                CurrentCustomMusic.Add(musicData.Name, path);
                            }
                            if (wasPlaying && success)
                            {
                                PlayCustomMusic(musicData.Name);
                            }
                        }
                        else
                        {
                            CurrentCustomMusic.Add(musicData.Name, path);
                        }
                    }
                }
            }
            stopwatch.Stop();
        }

        public static void PlayCustomMusic(string trackName)
        {
            var path = CurrentCustomMusic[trackName];
            var __instance = G.Sys.AudioManager_;
            if (__instance.CurrentMusicState_ == AudioManager.MusicState.PerLevel && AudioManager.CurrentAudioFile_?.FileName == path)
            {
                return;
            }
            Debug.Log($"Playing {trackName}; {__instance.CurrentMusicState_}; {AudioManager.CurrentAudioFile_?.FileName}; {path}");
            typeof(AudioManager)
                .GetField("currentMusicState_", BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(__instance, AudioManager.MusicState.PerLevel);
            AudioManager.PostEvent("Mute_All_Music");
            __instance.PlayMP3(CurrentCustomMusic[trackName], 2000f);
            typeof(AudioManager)
                .GetField("perLevelMusicOverride_", BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(__instance, true);
            typeof(AudioManager)
                .GetField("useAdventureMusicTrigger_", BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(__instance, false);
        }

        public static void StopCustomMusic()
        {
            if (G.Sys.AudioManager_.CurrentMusicState_ == AudioManager.MusicState.PerLevel)
            {
                G.Sys.AudioManager_.SwitchToOfficialMusic();
            }
        }

        public static bool PlayOrStopCustom(string trackName)
        {
            if (!AudioManager.Valid())
            {
                return true;
            }
            if (CurrentCustomMusic.ContainsKey(trackName))
            {
                PlayCustomMusic(trackName);
                return false;
            }
            else
            {
                StopCustomMusic();
                return true;
            }
        }
        
        public static string AddMusicOptions = Options.Description.Format("Add custom music track") + Options.CustomType.Format(CustomInspector.Type.StringButton);
        public static void AddMusicTrack()
        {
            Debug.Log("Adding music track...");
            G.Sys.LevelEditor_.StartNewToolJobOfType(typeof(AddCustomMusicTool), false);
        }

        class ErrorPrinter : LevelEditorTools.LevelEditorTool
        {
            public static void PrintError(string message)
            {
                Debug.Log(message);
                G.Sys.LevelEditor_.StartCoroutine(PrintErrorSoon(1f, message));
            }
            public static IEnumerator PrintErrorSoon(float f, string message)
            {
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                yield return new WaitForSeconds(f);
                PrintErrorMessage(message);
                yield break;
            }

            public static void Print(string message)
            {
                Debug.Log(message);
                G.Sys.LevelEditor_.StartCoroutine(PrintSoon(1f, message));
            }

            public static IEnumerator PrintSoon(float f, string message)
            {
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                yield return new WaitForSeconds(f);
                PrintMessage(message);
                yield break;
            }

            public override ToolInfo Info_ => throw new NotImplementedException();
            public override void Cancel() { throw new NotImplementedException(); }
            public override void Finish() { throw new NotImplementedException(); }
            public override void Start() { throw new NotImplementedException(); }
            public override ToolState Update() { throw new NotImplementedException(); }
        }

        class AddOrRemoveCustomMusicAction : SimplerAction
        {
            public override string Description_ => throw new NotImplementedException();
            
            private ReferenceMap.Handle<GameObject> originalHandle;
            private ReferenceMap.Handle<GameObject> newHandle;
            private ReferenceMap.Handle<CustomName> addedComponentHandle;
            
            private readonly bool isAdd;
            private readonly byte[] componentBytes;

            public AddOrRemoveCustomMusicAction(GameObject gameObject, CustomName comp)
            {
                Debug.Log("Created AddOrRemoveCustomMusicAction");
                ReferenceMap referenceMap_ = G.Sys.LevelEditor_.ReferenceMap_;
                originalHandle = referenceMap_.GetHandleOrNull(gameObject);
                newHandle = referenceMap_.GetHandleOrNull<GameObject>(null);
                addedComponentHandle = referenceMap_.GetHandleOrNull(comp);
                isAdd = (comp == null);
                if (!isAdd)
                {
                    componentBytes = Serializers.BinarySerializer.SaveComponentToBytes(comp, null);
                }
            }
            
            private void AddOrRemove(ReferenceMap.Handle<GameObject> beforeHandle, ref ReferenceMap.Handle<GameObject> afterHandle, bool add)
            {
                Debug.Log("Running AddOrRemove");
                LevelEditor levelEditor_ = G.Sys.LevelEditor_;
                GameObject gameObject = beforeHandle.Get();
                CustomName comp = (!add) ? this.addedComponentHandle.Get() : ((CustomName)((object)null));
                if (add)
                {
                    comp = gameObject.AddComponent<CustomName>();
                    var music = new CustomMusicData()
                    {
                        Name = "Unknown",
                        FileType = ".mp3"
                    };
                    comp.customName_ = music.ToDataString();
                    if (componentBytes != null)
                    {
                        Serializers.BinaryDeserializer.LoadComponentContentsFromBytes(comp, null, componentBytes);
                    }
                    comp.enabled = false;
                }
                else if (comp && comp.gameObject == gameObject)
                {
                    UnityEngine.Object.DestroyImmediate(comp);
                }
                NeedsRefresh = true;
                addedComponentHandle = ((!add) ? default(ReferenceMap.Handle<CustomName>) : levelEditor_.ReferenceMap_.GetHandleOrNull(comp));
                gameObject.ForEachILevelEditorListenerInChildren(listener => listener.OnLevelEditorToolFinish());
                Events.StaticEvent<ObjectHadComponentAddedOrRemoved.Data>.Broadcast(new ObjectHadComponentAddedOrRemoved.Data(gameObject));
            }
            
            public sealed override void Undo()
            {
                AddOrRemove(this.newHandle, ref this.originalHandle, !this.isAdd);
            }
            
            public sealed override void Redo()
            {
                AddOrRemove(this.originalHandle, ref this.newHandle, this.isAdd);
            }
        }

        class AddCustomMusicAction : AddOrRemoveCustomMusicAction
        {
            public AddCustomMusicAction(GameObject obj) : base(obj, null) {}
            public override string Description_ => "Added CustomMusic to object";
        }

        class RemoveCustomMusicAction : AddOrRemoveCustomMusicAction
        {
            public RemoveCustomMusicAction(GameObject obj, CustomName c) : base(obj, c) {}
            public override string Description_ => "Removed CustomMusic from object";
        }

        class AddCustomMusicTool : LevelEditorTools.InstantTool
        {
            public static ToolInfo info_ = new ToolInfo("Add CustomMusic", "", ToolCategory.Others, ToolButtonState.Invisible, false);
            public override ToolInfo Info_ => info_;

            public static void Register()
            {
                Debug.Log("Registering AddCustomMusicTool");
                G.Sys.LevelEditor_.RegisterTool(info_);
            }

            public override bool Run()
            {
                Debug.Log("Running AddCustomMusicTool.Run...");
                GameObject[] selected = new GameObject[] { G.Sys.LevelEditor_.WorkingSettings_.gameObject };
                if (selected.Length == 0)
                {
                    Debug.Log("Exiting early");
                    return false;
                }
                foreach (var obj in selected)
                {
                    Debug.Log("Adding to LevelSettings");
                    var action = new AddCustomMusicAction(obj);
                    action.Redo();
                    action.FinishAndAddToLevelEditorActions();
                }
                return true;
            }
        }

        class RemoveCustomMusicTool : LevelEditorTools.InstantTool
        {
            public static ToolInfo info_ = new ToolInfo("Remove CustomMusic", "", ToolCategory.Others, ToolButtonState.Invisible, false);
            public override ToolInfo Info_ => info_;

            public static void Register()
            {
                Debug.Log("Registering RemoveCustomMusicTool");
                G.Sys.LevelEditor_.RegisterTool(info_);
            }

            protected CustomName[] components;
            public void SetComponents(Component[] components)
            {
                components = components.Cast<CustomName>().ToArray();
            }

            public override bool Run()
            {
                CustomName[] selected = components;
                if (selected.Length == 0)
                {
                    return false;
                }
                foreach (var obj in selected)
                {
                    var action = new RemoveCustomMusicAction(obj.gameObject, obj);
                    action.Redo();
                    action.FinishAndAddToLevelEditorActions();
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CustomName), "Visit")]
        class PatchCustomName
        {
            static bool Prefix(CustomName __instance, IVisitor visitor, ISerializable prefabComp, int version)
            {
                visitor.Visit("CustomName", ref __instance.customName_, !IsCustomMusic(__instance), null);
                if (!(visitor is Serializers.Serializer) && IsCustomMusic(__instance))
                {
                    CustomMusicData data = null;
                    AttachedData.TryGetValue(__instance, out data);
                    if (data == null || data.LastDataString != __instance.customName_)
                    {
                        data = CustomMusicData.FromDataString(__instance.customName_);
                        AttachedData[__instance] = data;
                    }
                    else
                    {
                        var isEditing = true;
                        if (visitor is NGUIComponentInspector)
                        {
                            isEditing = (bool)typeof(NGUIComponentInspector).GetField("isEditing_", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(visitor);
                        }
                        if (!isEditing)
                        {
                            var anyChanges = false;
                            var lastData = CustomMusicData.FromDataString(__instance.customName_);
                            if (lastData.Name != data.Name)
                            {
                                var newRef = data.Name;
                                try
                                {
                                    data.NewVersion();
                                    __instance.customName_ = data.ToDataString();
                                    anyChanges = true;
                                    ErrorPrinter.Print($"Updated name to {data.Name}");
                                }
                                catch (Exception e)
                                {
                                    data.Name = lastData.Name;
                                    ErrorPrinter.PrintError($"Could not set track name to {newRef} because: {e}");
                                }
                            }
                            if (lastData.FileType != data.FileType)
                            {
                                var newRef = data.FileType;
                                try
                                {
                                    if (!AllowedExtensions.Contains(data.FileType))
                                    {
                                        throw new Exception("Bad extension (.mp3, .wav, and .aiff only)");
                                    }
                                    data.NewVersion();
                                    __instance.customName_ = data.ToDataString();
                                    anyChanges = true;
                                    ErrorPrinter.Print($"Updated FileType to {data.FileType}");
                                }
                                catch (Exception e)
                                {
                                    data.FileType = lastData.FileType;
                                    ErrorPrinter.PrintError($"Could not set extension to {data.FileType} because: {e}");
                                }
                            }
                            if (data.LastEmbeddedFileDisplayText != data.EmbeddedFileDisplayText)
                            {
                                var newRef = data.EmbeddedFileDisplayText;
                                try
                                {
                                    newRef = newRef.Trim('"', '\'');
                                    var extension = Path.GetExtension(newRef);
                                    if (!AllowedExtensions.Contains(extension))
                                    {
                                        throw new Exception("Bad file type (.mp3, .wav, and .aiff only)");
                                    }
                                    var file = FileEx.ReadAllBytes(newRef);
                                    data.EmbeddedFileString = Convert.ToBase64String(file);
                                    data.FileType = extension;
                                    data.DownloadUriString = "";
                                    data.NewVersion();
                                    __instance.customName_ = data.ToDataString();
                                    anyChanges = true;
                                    ErrorPrinter.Print($"Embedded {newRef} (\"{data.LastEmbeddedFileDisplayText}\" != \"{newRef}\")");
                                }
                                catch (Exception e)
                                {
                                    data.EmbeddedFileString = lastData.EmbeddedFileString;
                                    data.EmbeddedFileDisplayText = lastData.EmbeddedFileDisplayText;
                                    anyChanges = true;
                                    ErrorPrinter.PrintError($"Could not embed file {newRef} because: {e}");
                                }
                                data.LastEmbeddedFileDisplayText = data.EmbeddedFileDisplayText;
                            }
                            if (lastData.DownloadUriString != data.DownloadUriString)
                            {
                                var newRef = data.DownloadUriString;
                                try
                                {
                                    foreach (var fileType in AllowedExtensions)
                                    {
                                        if (((string)newRef).ToLower().EndsWith(fileType))
                                        {
                                            data.FileType = fileType;
                                            break;
                                        }
                                    }
                                    data.EmbeddedFileString = "";
                                    data.DownloadUriString = newRef;
                                    data.NewVersion();
                                    __instance.customName_ = data.ToDataString();
                                    anyChanges = true;
                                    ErrorPrinter.Print($"Updated DownloadUrl to {data.DownloadUriString}");
                                }
                                catch (Exception e)
                                {
                                    ErrorPrinter.PrintError($"Could not set url to {(string)newRef} because: {e}");
                                }
                            }
                            if (anyChanges)
                            {
                                data.DownloadAttempted = false;
                                UpdateCustomMusicList();
                            }
                        }
                    }

                    data.EmbeddedFileDisplayText = data.IsEmbedded() ? "Embedded" : "";
                    data.LastEmbeddedFileDisplayText = data.EmbeddedFileDisplayText;
                    data.LastDataString = __instance.customName_;
                    visitor.Visit("Track Name", ref data.Name, null);
                    visitor.Visit("File Type", ref data.FileType, null);
                    visitor.Visit("Embed File", ref data.EmbeddedFileDisplayText, null);
                    visitor.Visit("Download URL", ref data.DownloadUriString, null);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(LevelEditor))]
        [HarmonyPatch("Clear")]
        class PatchClear
        {
            static void Postfix(LevelEditor __instance, bool theFullClear)
            {
                try
                {
                    if (theFullClear)
                    {
                        foreach (var obj in __instance.WorkingSettings_.GetComponents<CustomName>())
                        {
                            obj.Destroy();
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to add clear: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(LevelEditor))]
        [HarmonyPatch("Start")]
        class PatchRegisterTools
        {
            static void Postfix(LevelEditor __instance)
            {
                try
                {
                    typeof(LevelEditor).GetField("currentRegisteringToolType_", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, typeof(AddCustomMusicTool));
                    AddCustomMusicTool.Register();
                    typeof(LevelEditor).GetField("currentRegisteringToolType_", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, typeof(RemoveCustomMusicTool));
                    RemoveCustomMusicTool.Register();
                    typeof(LevelEditor).GetField("currentRegisteringToolType_", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, null);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to add tools: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(NGUIComponentInspector))]
        [HarmonyPatch("Init")]
        class PatchRemoveVisibility
        {
            static void Postfix(NGUIComponentInspector __instance)
            {
                if (__instance.ISerializable_ != null && __instance.ISerializable_.GetType() == typeof(CustomName))
                {
                    Debug.Log($"IsCustomMusic: {__instance.ISerializable_}");
                    if (IsCustomMusic((CustomName)__instance.ISerializable_))
                    {
                        typeof(NGUIComponentInspector).GetMethod("SetRemoveComponentButtonVisibility", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(__instance, new object[] { true });
                    }
                }
            }
        }

        [HarmonyPatch(typeof(NGUIComponentInspector))]
        [HarmonyPatch("OnRemoveComponentClicked")]
        class PatchRemoveClick
        {
            static bool Prefix(NGUIComponentInspector __instance)
            {
                if (__instance.ISerializable_.GetType() == typeof(CustomName) && IsCustomMusic((CustomName)__instance.ISerializable_))
                {
                    RemoveCustomMusicTool removeTool = G.Sys.LevelEditor_.StartNewToolJobOfType(typeof(RemoveCustomMusicTool), false) as RemoveCustomMusicTool;
                    if (removeTool != null)
                    {
                        var ser = (ISerializable[])typeof(NGUIComponentInspector).GetField("iSerializables_", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance);
                        removeTool.SetComponents(ser.Cast<Component>().ToArray());
                    }
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CustomName))]
        [HarmonyPatch("DisplayName_", MethodType.Getter)]
        class PatchCustomNameDisplayName
        {
            static bool Prefix(CustomName __instance, ref string __result)
            {
                if (__instance != null && IsCustomMusic(__instance))
                {
                    __result = "Custom Music Track";
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CustomName))]
        [HarmonyPatch("ComponentDescription_", MethodType.Getter)]
        class PatchCustomNameDescription
        {
            static bool Prefix(CustomName __instance, ref string __result)
            {
                if (__instance != null && IsCustomMusic(__instance))
                {
                    __result = "Add a custom music track to the level";
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(LevelSettings), "Visit")]
        class PatchLevelSettings
        {
            static void Prefix(LevelSettings __instance, IVisitor visitor, ISerializable prefabComp, int version)
            {
            }
            static void Postfix(LevelSettings __instance, IVisitor visitor, ISerializable prefabComp, int version)
            {
                visitor.VisitAction("Add Custom Music", new Action(AddMusicTrack), AddMusicOptions);
                if (!(visitor is Serializers.Serializer) && AudioManager.Valid() && CurrentCustomMusic.ContainsKey(__instance.musicTrackName_))
                {
                    PlayCustomMusic(__instance.musicTrackName_);
                }
                else
                {
                    StopCustomMusic();
                }
            }
        }

        [HarmonyPatch(typeof(LevelDataTab), "Update")]
        class PatchLevelDataTab
        {
            static void Postfix(LevelDataTab __instance, ref bool ___propertiesAreBeingDisplayed_)
            {
                if (___propertiesAreBeingDisplayed_ || __instance.IsSelectionValid_)
                {
                    if (NeedsRefresh)
                    {
                        NeedsRefresh = false;
                        try
                        {
                            typeof(NGUIObjectInspectorTabAbstract).GetMethod("ClearComponentInspectors", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(__instance, new object[0]);
                            typeof(NGUIObjectInspectorTab).GetMethod("InitComponentInspectors", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(__instance, new object[0]);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Failed to refresh LevelDataTab: {e}");
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(LevelEditorTools.SelectMusicTrackNameFromListTool), "AddEntries")]
        class PatchMusicSelector
        {
            static bool Prefix(LevelEditorTools.SelectMusicTrackNameFromListTool __instance, Dictionary<string, string> entryList)
            {
                Debug.Log($"SelectMusic tool started");
                UpdateCustomMusicList();
                foreach (var pair in CurrentCustomMusic)
                {
                    Debug.Log($"Adding entry to SelectMusic tool: {pair.Key}");
                    entryList.Add("Custom: " + pair.Key, pair.Key);
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(MusicTrigger), "PlayMusic")]
        class PatchMusicTrigger
        {
            static bool Prefix(MusicTrigger __instance)
            {
                return PlayOrStopCustom(__instance.musicTrackName_);
            }
        }

        [HarmonyPatch(typeof(MusicZone), "PlayMusic")]
        class PatchMusicZonePlay
        {
            static bool Prefix(MusicZone __instance)
            {
                return PlayOrStopCustom(__instance.musicTrackName_);
            }
        }

        [HarmonyPatch(typeof(MusicZone), "SetState")]
        class PatchMusicZoneState
        {
            static bool Prefix(MusicZone __instance, bool goingIn)
            {
                if (goingIn)
                {
                    return PlayOrStopCustom(__instance.musicTrackName_);
                }
                else if(CurrentCustomMusic.ContainsKey(__instance.musicTrackName_))
                {
                    StopCustomMusic();
                    return PlayOrStopCustom(G.Sys.GameManager_.LevelSettings_.musicTrackName_);
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(AudioManager), "OnEventPostLoad")]
        class PatchAudioManager
        {
            static bool Prefix(AudioManager __instance)
            {
                if (AudioManager.AllowCustomMusic_)
                {
                    UpdateCustomMusicList();
                    var trackName = G.Sys.GameManager_.Level_.Settings_.musicTrackName_;
                    if (CurrentCustomMusic.ContainsKey(trackName))
                    {
                        PlayCustomMusic(trackName);
                        return false;
                    }
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Serializers.Serializer), "VisitComponentFromObject")]
        class PatchCustomNamePrefab
        {
            static bool Prefix(Component component, ref Component prefabComponent)
            {
                if (component.GetType() == typeof(CustomName) && IsCustomMusic((CustomName)component))
                {
                    Debug.Log($"Setting prefabComponent");
                    prefabComponent = Serializers.Serializer.GetPrefabWithName("Group").GetComponent<CustomName>();
                }
                return true;
            }
        }

        public void Shutdown() { }

        public void Update()
        {
            var toRemove = new List<CustomName>();
            foreach (var obj in AttachedData.Keys)
            {
                if (obj == null)
                {
                    toRemove.Add(obj);
                }
            }
            foreach(var obj in toRemove)
            {
                AttachedData.Remove(obj);
            }
        }

        public class CustomMusicState
        {
            public string Version = "";
            public int DownloadAttempts = 0;
            [NonSerialized()]
            public string LevelPath = null;
            public string Id = null;
            public CustomMusicState(string levelPath, string id)
            {
                LevelPath = levelPath;
                Id = id;
            }
            public void Read()
            {
                string path = LevelPath + "." + Id + ".musicstate";
                if (FileEx.Exists(path))
                {
                    try
                    {
                        var text = FileEx.ReadAllText(path);
                        JsonConvert.PopulateObject(text, this);
                        return;
                    }
                    catch (Exception e)
                    {
                        Debug.Log($"Failed to read CustomMusicState {path} because: {e}");
                    }
                }
                Version = "";
                DownloadAttempts = 0;
            }
            public bool Write()
            {
                string path = LevelPath + "." + Id + ".musicstate";
                try
                {
                    FileEx.WriteAllText(path, JsonConvert.SerializeObject(this));
                    return true;
                }
                catch (Exception e)
                {
                    Debug.Log($"Failed to write CustomMusicState {path} because: {e}");
                    return false;
                }
            }
        }

        public class CustomMusicData
        {
            public string Version = "";
            public string Name = "Unknown";
            public string FileType = ".mp3";
            public string DownloadUriString = "";
            public string EmbeddedFileString = "";
            [NonSerialized()]
            public string EmbeddedFileDisplayText = "";
            [NonSerialized()]
            public string LastEmbeddedFileDisplayText = "";
            [NonSerialized()]
            public string LastDataString = null;
            [NonSerialized()]
            public bool DownloadAttempted = false;
            [NonSerialized()]
            public Exception Exception = null;
            public bool IsException()
            {
                return Exception != null;
            }
            public bool IsEmbedded()
            {
                return !String.IsNullOrEmpty(EmbeddedFileString);
            }
            public bool IsDownload()
            {
                return !String.IsNullOrEmpty(DownloadUriString);
            }
            public bool IsValid()
            {
                if (!IsEmbedded() && !IsDownload())
                {
                    return false;
                }
                if (!AllowedExtensions.Contains(FileType))
                {
                    return false;
                }
                var safeName = System.Text.RegularExpressions.Regex.Replace(Name, @"\W", "");
                if (safeName.Length == 0)
                {
                    return false;
                }
                if (IsEmbedded())
                {
                    try
                    {
                        Convert.FromBase64String(EmbeddedFileString);
                        return true;
                    }
                    catch (Exception e)
                    {
                        Debug.Log($"Bad Base64 EmbededFileString custom music: {e}");
                        return false;
                    }
                }
                else if (IsDownload())
                {
                    try
                    {
                        var uri = new Uri(DownloadUriString);
                        if (AllowedDownloadSchemes.Contains(uri.Scheme))
                        {
                            return true;
                        }
                        return false;
                    }
                    catch (Exception e)
                    {
                        Debug.Log($"Bad uri: {DownloadUriString}");
                    }
                }
                return true;
            }
            public string NewVersion()
            {
                Version = Guid.NewGuid().ToString();
                return Version;
            }
            public bool Download(string levelPath, string id)
            {
                if (!IsValid())
                {
                    return false;
                }
                var state = new CustomMusicState(levelPath, id);
                state.Read();
                var path = state.LevelPath + "." + state.Id + FileType;
                if (state.Version == Version)
                {
                    if (FileEx.Exists(path))
                    {
                        DownloadAttempted = true;
                        return true;
                    }
                }
                if (AudioManager.CurrentAudioFile_?.FileName == path)
                {
                    Debug.Log($"Temporarily stopping track {path} because it is being redownloaded");
                    StopCustomMusic();
                }
                DownloadAttempted = true;
                if (IsEmbedded())
                {
                    try
                    {
                        Debug.Log($"Writing custom music to {path} from EmbeddedFileString");
                        FileEx.WriteAllBytes(path, Convert.FromBase64String(EmbeddedFileString));
                        state.Version = Version;
                        state.Write();
                        return true;
                    }
                    catch (Exception e)
                    {
                        Debug.Log($"Failed to write custom music to file {path} because: {e}");
                        return false;
                    }
                }
                else if (IsDownload())
                {
                    state.Version = Version;
                    state.DownloadAttempts = state.DownloadAttempts + 1;
                    state.Write();
                    using (System.Net.WebClient webClient = new System.Net.WebClient())
                    {
                        byte[] file = new byte[0];
                        var completed = false;
                        webClient.DownloadDataCompleted += (sender, data) =>
                        {
                            completed = true;
                            if (!data.Cancelled && data.Error == null)
                            {
                                file = data.Result;
                            }
                        };
                        webClient.DownloadProgressChanged += (sender, data) =>
                        {
                            if (data.BytesReceived >= MaxMusicDownloadSizeBytes)
                            {
                                webClient.CancelAsync();
                            }
                        };
                        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                        webClient.DownloadDataAsync(new Uri(DownloadUriString));
                        while (!completed)
                        {
                            if (stopwatch.ElapsedMilliseconds > MaxMusicDownloadTimeMilli)
                            {
                                webClient.CancelAsync();
                            }
                        }
                        stopwatch.Stop();
                        if (file.Length > 0)
                        {
                            try
                            {
                                Debug.Log($"Writing custom music to {path} from url {DownloadUriString}");
                                FileEx.WriteAllBytes(path, file);
                                return true;
                            }
                            catch (Exception e)
                            {
                                Debug.Log($"Failed to write custom music to file ${path} because: {e}");
                                return false;
                            }
                        }
                    }
                }
                return false;
            }

            public static CustomMusicData FromDataString(string data)
            {
                try
                {
                    return JsonConvert.DeserializeObject<CustomMusicData>(data.Substring(12));
                }
                catch (Exception e)
                {
                    return new CustomMusicData() { Exception = e };
                }
            }

            public string ToDataString()
            {
                return "CustomMusic:" + JsonConvert.SerializeObject(this);
            }
        }
    }
}
