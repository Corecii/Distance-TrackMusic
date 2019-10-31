using Spectrum.API.Interfaces.Plugins;
using Spectrum.API.Interfaces.Systems;
using System;
using Harmony;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;

namespace Corecii.TrackMusic
{
    public class MusicZoneData
    {
        public string PreviousTrackName = null;
    }
    public class Entry : IPlugin, IUpdatable
    {
        public static int MaxMusicDownloadSizeBytes = 1000 * 1000 * 30; // 30 MB
        public static int MaxMusicDownloadTimeMilli = 15000; // 15 seconds

        public static int MaxMusicLevelLoadTimeMilli = 20000; // 20 seconds

        public static string MusicTrackOptions = Options.CustomType.Format(CustomInspector.Type.StringWithButton);
        public static string MusicTrackButtonOptions = Options.CustomType.Format(CustomInspector.Type.StringButton) + Options.DontUndoOption_;

        public static Attached<MusicTrack> CachedMusicTrack = new Attached<MusicTrack>();
        public static Attached<MusicChoice> CachedMusicChoice = new Attached<MusicChoice>();
        public static Attached<MusicZoneData> CachedMusicZoneData = new Attached<MusicZoneData>();

        public static bool PlayingMusic = false;
        public static string CurrentTrackName = null;

        public static bool Enabled = true;

        public static Entry Instance;

        public void Initialize(IManager manager, string ipcIdentifier)
        {
            UnityEngine.Application.SetStackTraceLogType(UnityEngine.LogType.Log, UnityEngine.StackTraceLogType.None);
            Instance = this;

            MusicTrack.Info.Register();
            MusicChoice.Info.Register();

            DirectoryEx.CreateIfDoesNotExist("EditorMusic/");

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

            PatchPostLoad(true);
        }

        public void Update()
        {
            CachedMusicTrack.Update();
            CachedMusicChoice.Update();
            CachedMusicZoneData.Update();
        }

        public static bool PlayTrack(string trackName, float fadeTimeMs = 2000f, bool force = false)
        {
            if (G.Sys.AudioManager_.CurrentMusicState_ == AudioManager.MusicState.CustomMusic)
            {
                return false;
            }
            if (!Enabled)
            {
                StopCustomMusic();
                return false;
            }
            if (trackName == null)
            {
                StopCustomMusic();
                return false;
            }
            var track = GetTrack(trackName);
            if (track == null || string.IsNullOrEmpty(track.FileLocation))
            {
                StopCustomMusic();
                return false;
            }
            if (!force && PlayingMusic && AudioManager.CurrentAudioFile_ != null && AudioManager.CurrentAudioFile_.FileName == track.FileLocation)
            {
                return true;
            }
            typeof(AudioManager).GetField("perLevelMusicOverride_", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(G.Sys.AudioManager_, false);
            var failed = false;
            Events.StaticEvent<Events.Audio.MusicSegmentEnd.Data>.Delegate func = data => { failed = true; };
            Events.StaticEvent<Events.Audio.MusicSegmentEnd.Data>.Subscribe(func);
            try
            {
                G.Sys.AudioManager_.PlayMP3(track.FileLocation, PlayingMusic ? 0f : fadeTimeMs);
            }
            catch(Exception e)
            {
                Events.StaticEvent<Events.Audio.MusicSegmentEnd.Data>.Unsubscribe(func);
                UnityEngine.Debug.Log($"Failed to play track {trackName} because: {e}");
                StopCustomMusic();
                return false;
            }
            Events.StaticEvent<Events.Audio.MusicSegmentEnd.Data>.Unsubscribe(func);
            if (failed || AudioManager.CurrentAudioFile_ == null || AudioManager.CurrentAudioFile_.FileName != track.FileLocation)
            {
                UnityEngine.Debug.Log($"Failed to play track {trackName}");
                StopCustomMusic();
                return false;
            }
            typeof(AudioManager).GetField("perLevelMusicOverride_", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(G.Sys.AudioManager_, true);
            typeof(AudioManager).GetField("currentMusicState_", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(G.Sys.AudioManager_, AudioManager.MusicState.PerLevel);
            CurrentTrackName = trackName;
            PlayingMusic = true;
            AudioManager.PostEvent("Mute_All_Music");
            return true;
        }

        public static void StopCustomMusic()
        {
            if (PlayingMusic && G.Sys.AudioManager_.CurrentMusicState_ == AudioManager.MusicState.PerLevel)
            {
                typeof(AudioManager).GetField("perLevelMusicOverride_", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(G.Sys.AudioManager_, false);
                G.Sys.AudioManager_.SwitchToOfficialMusic();
                PlayingMusic = false;
                CurrentTrackName = null;
            }
        }

        public static string GetMusicChoiceValue(UnityEngine.GameObject obj, string key)
        {
            var listener = obj.GetComponent<ZEventListener>();
            if (listener == null || !listener.eventName_.StartsWith(CustomDataInfo.GetPrefix<MusicChoice>()))
            {
                return null;
            }
            var choice = CachedMusicChoice.GetOr(listener, () => MusicChoice.FromObject(listener));
            if (choice == null)
            {
                return null;
            }
            MusicChoiceEntry entry = null;
            choice.Choices.TryGetValue(key, out entry);
            if (entry == null)
            {
                return null;
            }
            return entry.Track;
        }

        public static void DownloadAllTracks()
        {
            var levelPath = G.Sys.GameManager_.LevelPath_;
            if (G.Sys.LevelEditor_ != null && G.Sys.LevelEditor_.Active_)
            {
                levelPath = "EditorMusic/EditorMusic";
            }
            Instance.Update();
            var tracks = CachedMusicTrack.Pairs.Select(pair => pair.Value);
            var embedded = tracks.Where(track => track.Embedded.Length > 0);
            var download = tracks.Where(track => track.Embedded.Length == 0 && !string.IsNullOrEmpty(track.DownloadUrl));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var track in embedded.Concat(download))
            {
                DownloadTrack(track, levelPath);
                if (stopwatch.ElapsedMilliseconds >= MaxMusicLevelLoadTimeMilli)
                {
                    break;
                }
            }
        }

        public static MusicTrack GetTrack(string name)
        {
            foreach (var pair in CachedMusicTrack.Pairs)
            {
                var track = pair.Value;
                if (track.Name == name)
                {
                    var err = track.GetError();
                    if (err == null)
                    {
                        return track;
                    }
                }
            }
            return null;
        }

        public static string DownloadTrack(MusicTrack track, string levelPath)
        {
            var err = track.GetError();
            if (err != null)
            {
                return null;
            }
            if (track.FileLocation != null || track.Attempted)
            {
                return track.FileLocation;
            }
            track.Attempted = true;
            var trackPath = $"{levelPath}.{track.FileName}{track.FileType}";
            var statePath = $"{levelPath}.{track.FileName}.musicstate";
            var upToDate = false;
            try
            {
                if (FileEx.Exists(statePath) && FileEx.Exists(trackPath))
                {
                    var stateStr = FileEx.ReadAllText(statePath);
                    if (stateStr == track.Version)
                    {
                        upToDate = true;
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log($"Failed to read music state file {statePath} because {e}");
                return null;
            }
            if (upToDate)
            {
                track.FileLocation = trackPath;
                return trackPath;
            }
            if (track.Embedded.Length > 0)
            {
                try
                {
                    FileEx.WriteAllBytes(trackPath, track.Embedded);
                    track.FileLocation = trackPath;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.Log($"Failed to write track {trackPath} (embed) because {e}");
                    return null;
                }
            }
            else if (!string.IsNullOrEmpty(track.DownloadUrl))
            {
                var request = UnityEngine.Networking.UnityWebRequest.Get(track.DownloadUrl);
                byte[] file = new byte[0];
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var operation = request.Send();
                while (!operation.isDone)
                {
                    if (stopwatch.ElapsedMilliseconds > MaxMusicDownloadTimeMilli)
                    {
                        request.Abort();
                        UnityEngine.Debug.Log($"Failed to download {track.Name}: it took too long!");
                        break;
                    }
                    else if (request.downloadedBytes >= (ulong)MaxMusicDownloadSizeBytes)
                    {
                        request.Abort();
                        UnityEngine.Debug.Log($"Failed to download {track.Name}: it is too big!");
                        break;
                    }
                }
                stopwatch.Stop();
                if (operation.isDone)
                {
                    if (request.isError)
                    {
                        UnityEngine.Debug.Log($"Failed to download {track.Name}: Error {request.error}");
                    }
                    else
                    {
                        file = request.downloadHandler.data;
                    }
                }
                request.Dispose();
                if (file.Length > 0)
                {
                    try
                    {
                        FileEx.WriteAllBytes(trackPath, file);
                        track.FileLocation = trackPath;
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.Log($"Failed to write track {trackPath} (download) because {e}");
                        return null;
                    }
                }
                else
                {
                    UnityEngine.Debug.Log($"Failed to download {track.Name}: no data!");
                }
            }
            else
            {
                UnityEngine.Debug.Log($"Impossible state when saving custom music track {track.Name}");
            }
            try
            {
                FileEx.WriteAllText(statePath, track.Version);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log($"Failed to write music state file {statePath} because {e}");
            }
            return trackPath;
        }

        public static void PatchPostLoad(bool subscribe)
        {
            var manager = G.Sys.AudioManager_;
            EditorPatches.removeParticularSubscriber<Events.Level.PostLoad.Data>(manager);
            var list = (Events.SubscriberList)typeof(AudioManager).GetField("subscriberList_", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
            var item = new Events.StaticEvent<Events.Level.PostLoad.Data>.Subscriber(new Events.StaticEvent<Events.Level.PostLoad.Data>.Delegate(data =>
            {
                UnityEngine.Debug.Log("Running PostLoad");
                typeof(AudioManager).GetMethod("OnEventPostLoad", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(G.Sys.AudioManager_, new object[] { data });
            }));
            list.Add(item);
            if (subscribe)
            {
                (item as Events.IEventSubscriber).Subscribe();
            }
        }
        
        [HarmonyPatch(typeof(AudioManager), "Awake")]
        class PatchAudioManagerAwake
        {
            static void Postfix(AudioManager __instance)
            {
                PatchPostLoad(false);
            }
        }

        [HarmonyPatch(typeof(AudioManager), "OnEventPostLoad")]
        class PatchAudioManagerPostLoad
        {
            public static void Postfix(AudioManager __instance)
            {
                if (AudioManager.AllowCustomMusic_)
                {
                    DownloadAllTracks();
                    UnityEngine.Debug.Log($"Trying to play {GetMusicChoiceValue(G.Sys.GameManager_.LevelSettings_.gameObject, "Level")}");
                    PlayTrack(GetMusicChoiceValue(G.Sys.GameManager_.LevelSettings_.gameObject, "Level"), 2000f, true);
                }
            }
        }

        public static void ResetLevelSettings(LevelSettings __instance)
        {
            UnityEngine.Debug.Log("Resetting music");
            foreach (var comp in __instance.gameObject.GetComponents<ZEventListener>())
            {
                UnityEngine.Object.DestroyImmediate(comp); // required for when level clear and load happen on the same frame (all the time)
                CachedMusicChoice.Remove(comp);
                CachedMusicTrack.Remove(comp);
                comp.Destroy();
            }
            __instance.gameObject.RemoveComponents<ZEventListener>();
            Instance.Update();
            EditorPatches.NeedsRefresh = true;
        }

        [HarmonyPatch(typeof(Level), "ClearAndReset")]
        class PatchLevelClearAndReset
        {
            public static bool isWorkingStateLevel = false; // handles level editor play mode -> level editor transition
            static void Prefix(Level __instance, bool destroyObjects)
            {
                if (destroyObjects && !isWorkingStateLevel)
                {
                    ResetLevelSettings(__instance.Settings_);
                }
                isWorkingStateLevel = false;
            }
        }

        [HarmonyPatch(typeof(Level), "LoadHelperEnumerator")]
        class PatchLevelLoadHelperEnumerator
        {
            static void Prefix(Level __instance, Serializers.Deserializer deserializer, bool loadStaticObjects, bool isWorkingStateLevel, Level.LoadState state)
            {
                PatchLevelClearAndReset.isWorkingStateLevel = isWorkingStateLevel;
            }
        }

        [HarmonyPatch(typeof(AudioEventTrigger), "PlayMusic")]
        class PatchAudioEventTrigger
        {
            static void Postfix(AudioEventTrigger __instance)
            {
                PlayTrack(GetMusicChoiceValue(__instance.gameObject, "Trigger"), 0f);
            }
        }

        [HarmonyPatch(typeof(MusicTrigger), "PlayMusic")]
        class PatchMusicTrigger
        {
            static void Postfix(MusicTrigger __instance)
            {
                PlayTrack(GetMusicChoiceValue(__instance.gameObject, "Trigger"), 0f);
            }
        }

        [HarmonyPatch(typeof(MusicZone), "PlayMusic")]
        class PatchMusicZonePlay
        {
            static void Postfix(MusicZone __instance)
            {
                PlayTrack(GetMusicChoiceValue(__instance.gameObject, "Zone"), 0f);
            }
        }

        [HarmonyPatch(typeof(MusicZone), "SetState")]
        class PatchMusicZoneState
        {
            static void Postfix(MusicZone __instance, bool goingIn)
            {
                UnityEngine.Debug.Log($"SetState {goingIn}");
                try
                {
                    var previous = CachedMusicZoneData.GetOr(__instance, () => new MusicZoneData());
                    if (goingIn)
                    {
                        previous.PreviousTrackName = CurrentTrackName;
                        PlayTrack(GetMusicChoiceValue(__instance.gameObject, "Zone"), 0f);
                    }
                    else
                    {
                        PlayTrack(previous.PreviousTrackName, 0f);
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.Log($"SetState failed: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(ZEventListener), "Visit")]
        class PatchVisitForMusicTrack
        {
            static void Postfix(ZEventListener __instance, IVisitor visitor)
            {
                if (!__instance.eventName_.StartsWith(CustomDataInfo.GetPrefix<MusicTrack>()))
                {
                    return;
                }
                CachedMusicTrack.GetOr(__instance, () => MusicTrack.FromObject(__instance));
            }
            static bool Prefix(ZEventListener __instance, IVisitor visitor, ISerializable prefabComp, int version)
            {
                if (!(visitor is NGUIComponentInspector))
                {
                    return true;
                }
                if (!__instance.eventName_.StartsWith(CustomDataInfo.GetPrefix<MusicTrack>())) {
                    return true;
                }
                visitor.Visit("eventName_", ref __instance.eventName_, false, null);
                visitor.Visit("delay_", ref __instance.delay_, false, null);
                var isEditing = (bool)typeof(NGUIComponentInspector).GetField("isEditing_", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(visitor);
                var data = CachedMusicTrack.GetOr(__instance, () => new MusicTrack());
                if (data.LastWrittenData != __instance.eventName_)
                {
                    data.ReadObject(__instance);
                    data.LastWrittenData = __instance.eventName_;
                    data.EmbedFile = (data.Embedded.Length > 0 ? "Embedded" : "");
                    data.LastWritten = data.Clone();
                }
                else if (!isEditing)
                {
                    var anyChanges = false;
                    var old = data.LastWritten;
                    if (data.Name != old.Name || data.DownloadUrl != old.DownloadUrl || data.FileType != old.FileType) {
                        anyChanges = true;
                    }
                    if (data.EmbedFile != old.EmbedFile)
                    {
                        var newRef = data.EmbedFile;
                        if (newRef == "")
                        {
                            data.Embedded = new byte[0];
                            anyChanges = true;
                        }
                        else
                        {
                            try
                            {
                                newRef = newRef.Trim('"', '\'');
                                var extension = Path.GetExtension(newRef);
                                var file = FileEx.ReadAllBytes(newRef);
                                data.Embedded = file ?? throw new Exception("Missing file");
                                data.FileType = extension;
                                data.DownloadUrl = "";
                                anyChanges = true;
                            }
                            catch (Exception e)
                            {
                                data.Embedded = new byte[0];
                                data.FileType = ".mp3";
                                anyChanges = true;
                                // TODO: warn user
                                UnityEngine.Debug.Log($"Failed to embed {newRef} because {e}");
                            }
                        }
                    }
                    if (anyChanges)
                    {
                        data.FileLocation = null;
                        data.Attempted = false;
                        data.EmbedFile = (data.Embedded.Length > 0 ? "Embedded" : "");
                        data.NewVersion();
                        data.WriteObject(__instance);
                        data.LastWrittenData = __instance.eventName_;
                        data.LastWritten = data.Clone();
                        var lastTrackName = CurrentTrackName;
                        if (lastTrackName == old.Name)
                        {
                            StopCustomMusic();
                        }
                        DownloadAllTracks();
                        if (lastTrackName == data.Name || GetMusicChoiceValue(G.Sys.LevelEditor_.WorkingSettings_.gameObject, "Level") == data.Name)
                        {
                            PlayTrack(data.Name, 0f);
                        }
                    }
                }
                
                visitor.Visit("Name", ref data.Name, null);
                visitor.Visit("Type", ref data.FileType, null);
                visitor.Visit("Embed File", ref data.EmbedFile, MusicTrackOptions);
                visitor.VisitAction("Select File", () =>
                {
                    var dlgOpen = new System.Windows.Forms.OpenFileDialog();
                    dlgOpen.Filter = "Distance Music (*.mp3, *.wav, *.aiff)|*.mp3;*.wav;*.aiff|All Files (*.*)|*.*";
                    dlgOpen.SupportMultiDottedExtensions = true;
                    dlgOpen.RestoreDirectory = true;
                    dlgOpen.Title = "Pick Distance music file";
                    dlgOpen.CheckFileExists = true;
                    dlgOpen.CheckPathExists = true;
                    if (dlgOpen.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        data.EmbedFile = dlgOpen.FileName;
                    }
                }, MusicTrackButtonOptions);
                visitor.Visit("Download URL", ref data.DownloadUrl, null);
                var Error = data.GetError();
                if (Error == null)
                {
                    Error = "None";
                }
                visitor.Visit("Error", ref Error, null);
                return false;
            }
        }

        [HarmonyPatch(typeof(ZEventListener), "Visit")]
        class PatchVisitForMusicChoice
        {
            static void Postfix(ZEventListener __instance, IVisitor visitor)
            {
                if (!__instance.eventName_.StartsWith(CustomDataInfo.GetPrefix<MusicChoice>()))
                {
                    if (visitor is Serializers.Deserializer)
                    {
                        UnityEngine.Debug.Log($"Prefix doesn't match {CustomDataInfo.GetPrefix<MusicChoice>()}");
                    }
                    return;
                }
                CachedMusicChoice.GetOr(__instance, () => MusicChoice.FromObject(__instance));
            }
            static bool Prefix(ZEventListener __instance, IVisitor visitor, ISerializable prefabComp, int version)
            {
                if (!(visitor is NGUIComponentInspector))
                {
                    return true;
                }
                if (!__instance.eventName_.StartsWith(CustomDataInfo.GetPrefix<MusicChoice>())) {
                    return true;
                }
                visitor.Visit("eventName_", ref __instance.eventName_, false, null);
                visitor.Visit("delay_", ref __instance.delay_, false, null);
                var isEditing = (bool)typeof(NGUIComponentInspector).GetField("isEditing_", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(visitor);
                var data = CachedMusicChoice.GetOr(__instance, () => new MusicChoice());
                if (data.LastWrittenData != __instance.eventName_)
                {
                    data.ReadObject(__instance);
                    data.LastWrittenData = __instance.eventName_;
                    data.LastWritten = data.Clone();
                }
                else if (!isEditing)
                {
                    var anyChanges = false;
                    var old = data.LastWritten;
                    if (data.Choices.Count != old.Choices.Count)
                    {
                        anyChanges = true;
                    }
                    foreach (var newChoice in data.Choices)
                    {
                        if (!old.Choices.ContainsKey(newChoice.Key) || old.Choices[newChoice.Key].Track != newChoice.Value.Track)
                        {
                            anyChanges = true;
                            break;
                        }
                    }
                    if (anyChanges)
                    {
                        data.WriteObject(__instance);
                        data.LastWrittenData = __instance.eventName_;
                        data.LastWritten = data.Clone();
                    }
                }

                foreach (var choice in data.Choices)
                {
                    visitor.Visit($"{choice.Key} Track", ref choice.Value.Track, null);
                }
                return false;
            }
        }
    }
}
