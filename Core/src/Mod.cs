﻿using System;
using System.Reflection;

using LabFusion.Data;
using LabFusion.Network;
using LabFusion.Representation;
using LabFusion.Utilities;
using LabFusion.Syncables;

using MelonLoader;

using LabFusion.Grabbables;
using UnityEngine;
using SLZ.Interaction;
using Il2CppSystem.Collections;

namespace LabFusion
{
    public class FusionMod : MelonMod
    {
        public struct FusionVersion {
            public const byte versionMajor = 0;
            public const byte versionMinor = 0;
            public const short versionPatch = 1;
        }

        public static readonly Version Version = new Version(FusionVersion.versionMajor, FusionVersion.versionMinor, FusionVersion.versionPatch);
        public static FusionMod Instance { get; private set; }
        public static Assembly FusionAssembly { get; private set; }

        public override void OnEarlyInitializeMelon() {
            Instance = this;
            FusionAssembly = Assembly.GetExecutingAssembly();

            PersistentData.OnPathInitialize();
            FusionMessageHandler.RegisterHandlersFromAssembly(FusionAssembly);
            GrabGroupHandler.RegisterHandlersFromAssembly(FusionAssembly);

            PDController.OnMelonInitialize();

            OnInitializeNetworking();
        }

        public override void OnLateInitializeMelon() {
            PatchingUtilities.PatchAll();
            InternalLayerHelpers.OnLateInitializeLayer();
        }

        protected void OnInitializeNetworking() {
            InternalLayerHelpers.SetLayer(new SteamNetworkLayer());
        }

        public override void OnDeinitializeMelon() {
            InternalLayerHelpers.OnCleanupLayer();
        }

        public static void OnMainSceneInitialized() {
            string sceneName = LevelWarehouseUtilities.GetCurrentLevel().Title;

#if DEBUG
            FusionLogger.Log($"Main scene {sceneName} was initialized.");
#endif

            // Cache info
            SyncManager.OnCleanup();
            RigData.OnCacheRigInfo();

            // Level info
            ArenaData.OnCacheArenaInfo();
            DescentData.OnCacheDescentInfo();
            
            // Create player reps
            PlayerRep.OnRecreateReps();

            // Disable physics
            if (NetworkInfo.HasServer)
                Physics.autoSimulation = false;
        }

        public override void OnUpdate() {
            // Reset byte counts
            NetworkInfo.BytesDown = 0;
            NetworkInfo.BytesUp = 0;

            // Update the jank level loading check
            LevelWarehouseUtilities.OnUpdateLevelLoading();

            // Store rig info/update avatars
            RigData.OnRigUpdate();

            // Send world messages
            PlayerRep.OnSyncRep();
            SyncManager.OnUpdate();
            PhysicsUtilities.OnSendPhysicsInformation();

            // Update and push all network messages
            InternalLayerHelpers.OnUpdateLayer();

            // Check all players loading
            if (NetworkInfo.HasServer && !Physics.autoSimulation) {
                bool canResume = true;

                foreach (var id in PlayerIdManager.PlayerIds) {
                    if (id.IsLoading) {
                        canResume = false;
                        break;
                    }
                }

                if (canResume)
                    Physics.autoSimulation = true;
            }
        }

        public override void OnFixedUpdate() {
            PDController.OnFixedUpdate();
            PlayerRep.OnFixedUpdate();
            SyncManager.OnFixedUpdate();
        }

        public override void OnLateUpdate() {
            // Update stuff like nametags
            PlayerRep.OnLateUpdate();

            // Flush any left over network messages
            InternalLayerHelpers.OnLateUpdateLayer();
        }

        public override void OnGUI() {
            InternalLayerHelpers.OnGUILayer();
        }
    }
}
