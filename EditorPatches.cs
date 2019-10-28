using Events;
using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Corecii.TrackMusic
{
    class EditorPatches
    {

        public static bool NeedsRefresh = false;

        public static StaticEvent<T>.Delegate removeParticularSubscriber<T>(MonoBehaviour component)
        {
            SubscriberList list = (SubscriberList)component
                .GetType()
                .GetField(
                    "subscriberList_",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                )
                .GetValue(component);
            StaticEvent<T>.Delegate func = null;
            var index = 0;
            foreach (var subscriber in list)
            {
                if (subscriber is StaticEvent<T>.Subscriber)
                {
                    func = (StaticEvent<T>.Delegate)subscriber
                        .GetType()
                        .GetField(
                            "func_",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                        )
                        .GetValue(subscriber);
                    subscriber.Unsubscribe();
                    break;
                }
                index++;
            }
            if (func != null)
            {
                list.RemoveAt(index);
            }
            return func;
        }

        [HarmonyPatch(typeof(LevelEditor), "OnLevelEditorExit")]
        class PatchOnLevelEditorExit
        {
            static void Postfix()
            {
                if (AudioManager.AllowCustomMusic_)
                {
                    Entry.DownloadAllTracks();
                    Entry.PlayTrack(Entry.GetMusicChoiceValue(G.Sys.GameManager_.LevelSettings_.gameObject, "Level"), 0f);
                }
            }
        }

        [HarmonyPatch(typeof(LevelEditor), "Clear")]
        class PatchLevelEditorClear
        {
            static void Postfix(LevelEditor __instance, bool theFullClear)
            {
                if (theFullClear)
                {
                    foreach (var comp in __instance.WorkingSettings_.gameObject.GetComponents<ZEventListener>())
                    {
                        comp.Destroy();
                    }
                    NeedsRefresh = true;
                }
            }
        }

        [HarmonyPatch(typeof(LevelDataTab), "Update")]
        class PatchLevelDataTabUpdate
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

        public static void AddMusicChoiceSelection()
        {
            EditorTools.AddMusicChoiceTool.Target = G.Sys.LevelEditor_.SelectedObjects_.ToArray();
            G.Sys.LevelEditor_.StartNewToolJobOfType(typeof(EditorTools.AddMusicChoiceTool), false);
        }

        public static void AddMusicChoiceLevelSettings()
        {
            EditorTools.AddMusicChoiceTool.Target = new GameObject[] { G.Sys.LevelEditor_.WorkingSettings_.gameObject };
            G.Sys.LevelEditor_.StartNewToolJobOfType(typeof(EditorTools.AddMusicChoiceTool), false);
        }

        public static void AddMusicTrack()
        {
            G.Sys.LevelEditor_.StartNewToolJobOfType(typeof(EditorTools.AddMusicTrackTool), false);
        }

        public static void ToggleCustomMusic()
        {
            G.Sys.LevelEditor_.StartNewToolJobOfType(typeof(EditorTools.ToggleMusicTool), false);
        }

        [HarmonyPatch(typeof(LevelSettings), "Visit")]
        class PatchLevelSettingsVisit
        {
            static void Postfix(LevelSettings __instance, IVisitor visitor, ISerializable prefabComp, int version)
            {
                Entry.DownloadAllTracks();
                if (!(visitor is Serializers.Serializer) && !(visitor is Serializers.Deserializer))
                {
                    visitor.VisitAction("Toggle Custom Music", new Action(ToggleCustomMusic), null);
                    visitor.VisitAction("Add Music Track", new Action(AddMusicTrack), null);
                    if (!__instance.HasComponent<ZEventListener>())
                    {
                        visitor.VisitAction("Set Music Choice", new Action(AddMusicChoiceLevelSettings), null);
                    }
                    Entry.PlayTrack(Entry.GetMusicChoiceValue(__instance.gameObject, "Level"), 2000f);
                }
            }
        }

        [HarmonyPatch(typeof(MusicTrigger), "Visit")]
        class PatchMusicTriggerVisit
        {
            static void Postfix(MusicTrigger __instance, IVisitor visitor, ISerializable prefabComp, int version)
            {
                if (!(visitor is Serializers.Serializer) && !(visitor is Serializers.Deserializer))
                {
                    if (!__instance.HasComponent<ZEventListener>())
                    {
                        visitor.VisitAction("Set Music Choice", new Action(AddMusicChoiceSelection), null);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MusicZone), "Visit")]
        class PatchMusicZoneVisit
        {
            static void Postfix(MusicZone __instance, IVisitor visitor, ISerializable prefabComp, int version)
            {
                if (!(visitor is Serializers.Serializer) && !(visitor is Serializers.Deserializer))
                {
                    if (!__instance.HasComponent<ZEventListener>())
                    {
                        visitor.VisitAction("Set Music Choice", new Action(AddMusicChoiceSelection), null);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(GameObjectEx), "GetDisplayName")]
        class PatchDisplayName
        {
            static bool Prefix(UnityEngine.GameObject gameObject, ref string __result)
            {
                if (gameObject == null)
                {
                    return true;
                }
                var customName = gameObject.GetComponent<CustomName>();
                if (customName == null)
                {
                    return true;
                }
                if (customName.CustomName_.StartsWith(CustomDataInfo.GetPrefix<MusicTrack>()))
                {
                    var track = Entry.CachedMusicTrack.GetOr(customName, () => MusicTrack.FromObject(customName));
                    if (track == null)
                    {
                        __result = "Music Track?";
                    }
                    __result = $"Music Track: {track.Name}";
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ZEventListener))]
        [HarmonyPatch("DisplayName_", MethodType.Getter)]
        class PatchDisplayNameChoice
        {
            static bool Prefix(ZEventListener __instance, ref string __result)
            {
                if (__instance != null && __instance.eventName_.StartsWith(CustomDataInfo.GetPrefix<MusicChoice>()))
                {
                    __result = "Music Choice";
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(ZEventListener))]
        [HarmonyPatch("ComponentDescription_", MethodType.Getter)]
        class PatchDescriptionChoice
        {
            static bool Prefix(ZEventListener __instance, ref string __result)
            {
                if (__instance != null && __instance.eventName_.StartsWith(CustomDataInfo.GetPrefix<MusicChoice>()))
                {
                    __result = "Custom music track choice";
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CustomName))]
        [HarmonyPatch("DisplayName_", MethodType.Getter)]
        class PatchDisplayNameTrack
        {
            static bool Prefix(CustomName __instance, ref string __result)
            {
                if (__instance != null && __instance.customName_.StartsWith(CustomDataInfo.GetPrefix<MusicTrack>()))
                {
                    __result = "Music Track";
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CustomName))]
        [HarmonyPatch("ComponentDescription_", MethodType.Getter)]
        class PatchDescriptionTrack
        {
            static bool Prefix(CustomName __instance, ref string __result)
            {
                if (__instance != null && __instance.customName_.StartsWith(CustomDataInfo.GetPrefix<MusicTrack>()))
                {
                    __result = "Custom music track data";
                    return false;
                }
                return true;
            }
        }
        
        [HarmonyPatch(typeof(NGUIComponentInspector))]
        [HarmonyPatch("Init")]
        class PatchRemoveVisibility
        {
            static void Postfix(NGUIComponentInspector __instance)
            {
                if (__instance.ISerializable_ != null && __instance.ISerializable_.GetType() == typeof(ZEventListener))
                {
                    if (((ZEventListener)__instance.ISerializable_).eventName_.StartsWith(CustomDataInfo.GetPrefix<MusicChoice>()))
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
                if (__instance.ISerializable_.GetType() == typeof(ZEventListener) && ((ZEventListener)__instance.ISerializable_).eventName_.StartsWith(CustomDataInfo.GetPrefix<MusicChoice>()))
                {
                    EditorTools.RemoveMusicChoiceTool removeTool = G.Sys.LevelEditor_.StartNewToolJobOfType(typeof(EditorTools.RemoveMusicChoiceTool), false) as EditorTools.RemoveMusicChoiceTool;
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
    }
}
