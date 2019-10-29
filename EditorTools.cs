using Events.LevelEditor;
using Harmony;
using LevelEditorActions;
using LevelEditorTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Corecii.TrackMusic
{
    class EditorTools
    {

        [HarmonyPatch(typeof(LevelEditor))]
        [HarmonyPatch("Start")]
        class PatchRegisterTools
        {
            static void Postfix(LevelEditor __instance)
            {
                try
                {
                    typeof(LevelEditor).GetField("currentRegisteringToolType_", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, typeof(AddMusicChoiceTool));
                    AddMusicChoiceTool.Register();
                    typeof(LevelEditor).GetField("currentRegisteringToolType_", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, typeof(RemoveMusicChoiceTool));
                    RemoveMusicChoiceTool.Register();
                    typeof(LevelEditor).GetField("currentRegisteringToolType_", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, typeof(AddMusicTrackTool));
                    AddMusicTrackTool.Register();
                    typeof(LevelEditor).GetField("currentRegisteringToolType_", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, typeof(ToggleMusicTool));
                    ToggleMusicTool.Register();
                    typeof(LevelEditor).GetField("currentRegisteringToolType_", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, null);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to add tools: {e}");
                }
            }
        }

        public class ToggleMusicAction : SimplerAction
        {
            public override string Description_ => "Toggle Custom Music";

            public void Toggle()
            {
                Entry.Enabled = !Entry.Enabled;
                Entry.PlayTrack(Entry.GetMusicChoiceValue(G.Sys.LevelEditor_.WorkingSettings_.gameObject, "Level"), 0f);
            }

            public override void Undo()
            {
                Toggle();
            }

            public override void Redo()
            {
                Toggle();
            }
        }
        
        public class ToggleMusicTool : InstantTool
        {
            public static ToolInfo info_ = new ToolInfo("Toggle Custom Music", "", ToolCategory.File, ToolButtonState.Button, false);
            public override ToolInfo Info_ => info_;
            
            public static void Register()
            {
                G.Sys.LevelEditor_.RegisterTool(info_);
            }
            
            public override bool Run()
            {
                var action = new ToggleMusicAction();
                action.Redo();
                action.FinishAndAddToLevelEditorActions();
                return true;
            }
        }

        public class AddMusicTrackAction : SimplerAction
        {
            public override string Description_ => "Add Music Track";

            public ReferenceMap.Handle<GameObject> objectHandle;

            public GameObject CreateTrack()
            {
                GameObject gameObject = Resource.LoadPrefabInstance("Group", true);
                gameObject.GetComponent<CustomName>().customName_ = "Music Track";
                var component = gameObject.AddComponent<ZEventListener>();
                var track = new MusicTrack() { Name = "Unknown" };
                track.NewVersion();
                track.WriteObject(component);
                gameObject.ForEachILevelEditorListener(delegate (ILevelEditorListener listener)
                {
                    listener.LevelEditorStart(true);
                });
                MonoBehaviour[] components = gameObject.GetComponents<MonoBehaviour>();
                foreach (MonoBehaviour monoBehaviour in components)
                {
                    monoBehaviour.enabled = false;
                }
                LevelEditor editor = G.Sys.LevelEditor_;
                editor.AddGameObjectSilent(ref objectHandle, gameObject, null);
                return gameObject;
            }

            public void DestroyTrack()
            {
                GameObject gameObject = objectHandle.Get();
                if (gameObject == null)
                {
                    return;
                }
                G.Sys.LevelEditor_.RemoveGameObjectSilent(gameObject);
            }
            
            public override void Undo()
            {
                DestroyTrack();
            }
            
            public override void Redo()
            {
                CreateTrack();
            }
        }
        
        public class AddMusicTrackTool : InstantTool
        {
            public static ToolInfo info_ = new ToolInfo("Add Music Track", "", ToolCategory.Edit, ToolButtonState.Button, false);
            public override ToolInfo Info_ => info_;
            
            public static void Register()
            {
                G.Sys.LevelEditor_.RegisterTool(info_);
            }
            
            public override bool Run()
            {
                LevelEditor editor = G.Sys.LevelEditor_;
                var action = new AddMusicTrackAction();
                GameObject gameObject = action.CreateTrack();
                editor.ClearSelectedList(true);
                editor.SelectObject(gameObject);
                action.FinishAndAddToLevelEditorActions();
                return true;
            }
        }

        public class AddOrRemoveMusicChoiceAction : SimplerAction
        {
            public override string Description_ => throw new NotImplementedException();

            private ReferenceMap.Handle<GameObject> originalHandle;
            private ReferenceMap.Handle<GameObject> newHandle;
            private ReferenceMap.Handle<ZEventListener> addedComponentHandle;

            private readonly bool isAdd;
            private readonly byte[] componentBytes;

            public AddOrRemoveMusicChoiceAction(GameObject gameObject, ZEventListener comp)
            {
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
                LevelEditor editor = G.Sys.LevelEditor_;
                GameObject gameObject = beforeHandle.Get();
                ZEventListener comp = (!add) ? addedComponentHandle.Get() : ((ZEventListener)((object)null));
                if (!gameObject.HasComponent<LevelSettings>())
                {
                    editor.RemoveGameObjectSilent(gameObject);
                }
                if (add)
                {
                    comp = gameObject.AddComponent<ZEventListener>();
                    var choice = new MusicChoice();
                    if (gameObject.HasComponent<LevelSettings>())
                    {
                        choice.Choices.Add("Level", new MusicChoiceEntry(""));
                    }
                    if (gameObject.HasComponent<MusicTrigger>())
                    {
                        choice.Choices.Add("Trigger", new MusicChoiceEntry(""));
                    }
                    if (gameObject.HasComponent<MusicZone>())
                    {
                        choice.Choices.Add("Zone", new MusicChoiceEntry(""));
                    }
                    choice.WriteObject(comp);
                    if (componentBytes != null)
                    {
                        Serializers.BinaryDeserializer.LoadComponentContentsFromBytes(comp, null, componentBytes);
                    }
                    comp.enabled = false;
                }
                else if (comp)
                {
                    comp.Destroy();
                }
                if (gameObject.HasComponent<LevelSettings>())
                {
                    EditorPatches.NeedsRefresh = true;
                }
                if (!gameObject.HasComponent<LevelSettings>())
                {
                    editor.AddGameObjectSilent(ref afterHandle, gameObject, editor.WorkingLevel_.GetLayerOfObject(gameObject));
                }
                addedComponentHandle = (!add) ? default(ReferenceMap.Handle<ZEventListener>) : editor.ReferenceMap_.GetHandleOrNull(comp);
                gameObject.ForEachILevelEditorListenerInChildren(listener => listener.OnLevelEditorToolFinish());
                Events.StaticEvent<ObjectHadComponentAddedOrRemoved.Data>.Broadcast(new ObjectHadComponentAddedOrRemoved.Data(gameObject));
                if (!gameObject.HasComponent<LevelSettings>())
                {
                    editor.SelectObject(gameObject);
                }
            }

            public sealed override void Undo()
            {
                AddOrRemove(newHandle, ref originalHandle, !isAdd);
            }

            public sealed override void Redo()
            {
                AddOrRemove(originalHandle, ref newHandle, isAdd);
            }
        }

        public class AddMusicChoiceAction : AddOrRemoveMusicChoiceAction
        {
            public AddMusicChoiceAction(GameObject obj) : base(obj, null) { }
            public override string Description_ => "Added Music Choice to object";
        }

        public class RemoveMusicChoiceAction : AddOrRemoveMusicChoiceAction
        {
            public RemoveMusicChoiceAction(GameObject obj, ZEventListener c) : base(obj, c) { }
            public override string Description_ => "Removed Music Choice from object";
        }

        public class AddMusicChoiceTool : InstantTool
        {
            public static ToolInfo info_ = new ToolInfo("Add Music Choice", "", ToolCategory.Others, ToolButtonState.Invisible, false);
            public override ToolInfo Info_ => info_;

            public static GameObject[] Target = new GameObject[0];

            public static void Register()
            {
                G.Sys.LevelEditor_.RegisterTool(info_);
            }

            public override bool Run()
            {
                GameObject[] selected = Target;
                if (selected.Length == 0)
                {
                    return false;
                }
                foreach (var obj in selected)
                {
                    if (obj.HasComponent<LevelSettings>() || obj.HasComponent<MusicTrigger>() || obj.HasComponent<MusicZone>())
                    {
                        var listener = obj.GetComponent<ZEventListener>();
                        if (listener == null)
                        {
                            var action = new AddMusicChoiceAction(obj);
                            action.Redo();
                            action.FinishAndAddToLevelEditorActions();
                        }
                    }
                }
                return true;
            }
        }

       public class RemoveMusicChoiceTool : InstantTool
        {
            public static ToolInfo info_ = new ToolInfo("Remove CustomMusic", "", ToolCategory.Others, ToolButtonState.Invisible, false);
            public override ToolInfo Info_ => info_;

            public static void Register()
            {
                G.Sys.LevelEditor_.RegisterTool(info_);
            }

            public ZEventListener[] components;
            public void SetComponents(Component[] componentsP)
            {
                components = componentsP.Cast<ZEventListener>().ToArray();
            }

            public override bool Run()
            {
                if (components == null)
                {
                    return false;
                }
                ZEventListener[] selected = components;
                if (selected.Length == 0)
                {
                    return false;
                }
                foreach (var obj in selected)
                {
                    var action = new RemoveMusicChoiceAction(obj.gameObject, obj);
                    action.Redo();
                    action.FinishAndAddToLevelEditorActions();
                }
                return true;
            }
        }
    }
}
