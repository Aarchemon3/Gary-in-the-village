using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Snippets.Sdk;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class RpgppWarriorFlowBuilder
{
    public const string AutoRunMarkerPath = "Temp/RpgppWarriorFlow.autorun";
    const string FlowRootName = "__RPGPP_WarriorFlow";
    const string ActorName = "WarriorActor";
    const string DestinationsName = "Destinations";
    const string LookTargetsName = "LookTargets";
    const string RegistryName = "ActorRegistry";
    const string FlowName = "FlowController";
    const string GazeName = "GazeFlowController";
    const string DelayedStartName = "DelayedFlowStarter";
    const string NavSurfaceName = "NavMeshSurface";
    const string PlayerRigName = "FirstPersonPlayer";
    const string PlayerCameraName = "PlayerCamera";
    const string PlayerStartName = "FirstPersonStart";

    const string SnippetFolder = "Assets/My Snippets/Make a snippet of a medieval warrior talking";
    const string MetadataPath = SnippetFolder + "/Raw/metadata.json";
    const string RpgppScenePath = "Assets/RPGPP_LT/Scene/rpgpp_lt_scene_1.0.unity";
    const string IdleClipPath = "Packages/com.snippets.sdk/Runtime/Assets/Animations/M_Idle.fbx";
    const string WalkClipPath = "Packages/com.snippets.sdk/Runtime/Assets/Animations/M_Walk.fbx";

    const string BuildingName = "rpgpp_lt_building_02 (1)";
    const string WellName = "rpgpp_lt_well_01";
    const string BirdHouseName = "rpgpp_lt_bird_house_01";
    const string StonesName = "rpgpp_lt_stones_01";
    const string TableName = "rpgpp_lt_table_01";
    const string BathtubName = "rpgpp_lt_bathtub_wood_01 (1)";
    const string PathPrefix = "rpgpp_lt_terrain_path_01b";

    const float SpawnPadding = 2.6f;
    const float StandDistance = 0.85f;
    const float TableStandDistance = 1.05f;
    const float HeadLookHeight = 1.45f;
    const float GroundProbeHeight = 40f;
    const float GroundProbeDistance = 120f;
    const float GroundSearchRadius = 1.5f;
    const float GroundLift = -0.035f;
    const float PlayerEyeHeight = 1.62f;

    [MenuItem("Tools/RPGPP/Build Medieval Warrior Flow")]
    public static void BuildOpenSceneWarriorFlowMenu()
    {
        BuildOpenSceneWarriorFlow();
    }

    public static void BuildOpenSceneWarriorFlow()
    {
        BuildInternal(saveScene: false);
    }

    public static void BuildAndSaveOpenSceneWarriorFlow()
    {
        BuildInternal(saveScene: true);
    }

    public static void BuildAndSaveRpgppSceneWarriorFlow()
    {
        var scene = EditorSceneManager.OpenScene(RpgppScenePath, OpenSceneMode.Single);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException($"Failed to open scene '{RpgppScenePath}'.");

        BuildInternal(saveScene: true);
    }

    static void BuildInternal(bool saveScene)
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("Open the RPGPP scene before building the warrior flow.");

        // Rebuilds navigation and placement using the current visible scene state.
        EnsureGroundSurfaceColliders(scene);
        var anchors = ResolveSceneAnchors(scene);
        var root = GetOrCreateRoot(scene, FlowRootName);
        var destinationsRoot = GetOrCreateChild(root.transform, DestinationsName);
        var lookTargetsRoot = GetOrCreateChild(root.transform, LookTargetsName);
        var navSurfaceRoot = GetOrCreateChild(root.transform, NavSurfaceName);

        var orderedSnippets = LoadOrderedSnippetPrefabs();
        if (orderedSnippets.Count != 4)
            throw new InvalidOperationException($"Expected 4 warrior snippets, found {orderedSnippets.Count}.");

        var actor = EnsureActor(root.transform, orderedSnippets[0]);
        ForceSnippetPlayerManualStart(actor);
        var idleClip = LoadAnimationClip(IdleClipPath, "M_Idle");
        var walkClip = LoadAnimationClip(WalkClipPath, "M_Walk");
        var navSurface = EnsureComponent<NavMeshSurface>(navSurfaceRoot.gameObject);
        ConfigureNavMesh(scene, navSurface, anchors);

        var lookPath = CreateOrMoveLookTarget(lookTargetsRoot, "Look_Path", GetLookPoint(anchors.PathNearBuilding, 0.15f));
        var lookBuilding = CreateOrMoveLookTarget(lookTargetsRoot, "Look_Building", GetLookPoint(anchors.Building, 1.75f));
        var lookWell = CreateOrMoveLookTarget(lookTargetsRoot, "Look_Well", GetLookPoint(anchors.Well, 0.45f));
        var lookBirdHouse = CreateOrMoveLookTarget(lookTargetsRoot, "Look_BirdHouse", GetLookPoint(anchors.BirdHouse, 0.25f));
        var lookStones = CreateOrMoveLookTarget(lookTargetsRoot, "Look_Stones", GetLookPoint(anchors.Stones, 0.85f));
        var lookTable = CreateOrMoveLookTarget(lookTargetsRoot, "Look_Table", GetLookPoint(anchors.Table, 1.05f));

        var spawnPoint = BuildSpawnPoint(scene, actor.transform, anchors.Building, anchors.PathNearBuilding);
        PlaceActor(actor.transform, spawnPoint.position, spawnPoint.rotation);
        EnsureFirstPersonRig(root.transform, scene, spawnPoint, actor.transform);
        var lookSpawnForward = CreateOrMoveLookTarget(
            lookTargetsRoot,
            "Look_SpawnForward",
            spawnPoint.position + spawnPoint.rotation * Vector3.forward * 3.2f + Vector3.up * HeadLookHeight);

        var waypointWell = CreateOrMoveWaypoint(
            destinationsRoot,
            "WP_01_Well",
            ComputeStandPosition(scene, actor.transform, anchors.Well, spawnPoint.position, StandDistance),
            lookBirdHouse.position);

        var waypointStones = CreateOrMoveWaypoint(
            destinationsRoot,
            "WP_02_Stones",
            ComputeStandPosition(scene, actor.transform, anchors.Stones, waypointWell.position, StandDistance),
            lookWell.position);

        var waypointTable = CreateOrMoveWaypoint(
            destinationsRoot,
            "WP_03_Table",
            ComputeStandPosition(scene, actor.transform, anchors.Table, waypointStones.position, TableStandDistance),
            lookStones.position);

        var lookWalk1 = CreateOrMoveLookTarget(
            lookTargetsRoot,
            "Look_Walk_01",
            GetForwardLookPoint(spawnPoint.position, waypointWell.position));
        var lookWalk2 = CreateOrMoveLookTarget(
            lookTargetsRoot,
            "Look_Walk_02",
            GetForwardLookPoint(waypointWell.position, waypointStones.position));
        var lookWalk3 = CreateOrMoveLookTarget(
            lookTargetsRoot,
            "Look_Walk_03",
            GetForwardLookPoint(waypointStones.position, waypointTable.position));

        var walker = EnsureComponent<SnippetsWalker>(actor.gameObject);
        ConfigureWalker(actor.gameObject, walker, waypointWell, waypointStones, waypointTable);

        var headTurn = EnsureComponent<SnippetsHeadTurn>(actor.gameObject);
        ConfigureHeadTurn(headTurn, actor.transform, lookSpawnForward);

        var groundSnap = EnsureComponent<SnippetsSceneGroundSnap>(actor.gameObject);
        ConfigureGroundSnap(groundSnap, walker, actor.transform, scene);
        groundSnap.SnapNow();

        var registry = EnsureComponentOnChild<SnippetsActorRegistry>(root.transform, RegistryName);
        ConfigureRegistry(registry, actor, walker, headTurn, orderedSnippets, idleClip, walkClip);

        var flow = EnsureComponentOnChild<SnippetsFlowController>(root.transform, FlowName);
        ConfigureFlow(flow, registry);

        var delayedStarter = EnsureComponentOnChild<DelayedSnippetsFlowStarter>(root.transform, DelayedStartName);
        delayedStarter.flow = flow;
        delayedStarter.delaySeconds = 2f;

        var gaze = EnsureComponentOnChild<SnippetsGazeFlowController>(root.transform, GazeName);
        ConfigureGaze(
            gaze,
            flow,
            registry,
            lookSpawnForward,
            lookPath,
            lookBuilding,
            lookWalk1,
            lookWalk2,
            lookWalk3,
            lookWell,
            lookBirdHouse,
            lookStones,
            lookTable);

        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene)
            EditorSceneManager.SaveScene(scene);

        Debug.Log("[RPGPP Warrior Flow] Builder completed.");
    }

    static SceneAnchors ResolveSceneAnchors(Scene scene)
    {
        var building = FindRequired(scene, BuildingName);
        var well = FindRequired(scene, WellName);
        var birdHouse = FindRequired(scene, BirdHouseName);
        var stones = FindRequired(scene, StonesName);
        var table = FindRequired(scene, TableName);
        var bathtub = FindRequired(scene, BathtubName);

        var pathCandidates = FindAll(scene, t => t.name.StartsWith(PathPrefix, StringComparison.Ordinal));
        if (pathCandidates.Count == 0)
            throw new InvalidOperationException($"Could not find any scene object starting with '{PathPrefix}'.");

        var pathNearBuilding = pathCandidates
            .OrderBy(t => FlatDistanceSqr(t.position, building.position))
            .First();

        return new SceneAnchors
        {
            Building = building,
            Well = well,
            BirdHouse = birdHouse,
            Stones = stones,
            Table = table,
            Bathtub = bathtub,
            PathNearBuilding = pathNearBuilding
        };
    }

    static SnippetPlayer EnsureActor(Transform parent, SnippetPlayer sourcePrefab)
    {
        var actor = parent.Find(ActorName)?.GetComponent<SnippetPlayer>();
        if (actor == null)
        {
            var instance = PrefabUtility.InstantiatePrefab(sourcePrefab.gameObject, parent) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("Failed to instantiate the warrior snippet prefab.");

            instance.name = ActorName;
            actor = instance.GetComponent<SnippetPlayer>();
        }

        actor.gameObject.name = ActorName;
        Selection.activeObject = actor.gameObject;
        return actor;
    }

    static void ForceSnippetPlayerManualStart(SnippetPlayer actor)
    {
        if (actor == null)
            return;

        // Prevent the imported snippet prefab from auto-playing on enable before our delayed flow start kicks in.
        var serializedObject = new SerializedObject(actor);
        var playOnEnable = serializedObject.FindProperty("m_playOnEnable");
        if (playOnEnable != null)
        {
            playOnEnable.boolValue = false;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void ConfigureWalker(GameObject actorObject, SnippetsWalker walker, params Transform[] waypoints)
    {
        var navAgent = EnsureComponent<NavMeshAgent>(actorObject);
        navAgent.radius = 0.22f;
        navAgent.height = 1.75f;
        navAgent.baseOffset = 0f;
        navAgent.speed = 1.45f;
        navAgent.acceleration = 40f;
        navAgent.angularSpeed = 720f;
        navAgent.stoppingDistance = 0.2f;
        navAgent.autoBraking = false;
        navAgent.updatePosition = false;
        navAgent.updateRotation = false;
        navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        navAgent.avoidancePriority = 40;

        var groundDriver = EnsureComponent<SnippetsNavMeshGroundDriver>(actorObject);
        groundDriver.walker = walker;
        groundDriver.yOffset = GroundLift;
        groundDriver.probeHeight = GroundProbeHeight;
        groundDriver.probeDistance = GroundProbeDistance;

        walker.waypoints = waypoints;
        walker.startIndex = 0;
        walker.moveSpeed = 1.45f;
        walker.arriveDistance = 0.2f;
        walker.turnSpeed = 540f;
        walker.useNavMesh = true;
        walker.navAgent = navAgent;
        walker.manualAgentRotation = true;
        walker.rotateMinSpeed = 0.05f;
        walker.pinBoneTranslation = true;
        walker.rootMotionBoneToPin = FindFirstChildByName(walker.transform, "Hips");
    }

    static void ConfigureNavMesh(Scene scene, NavMeshSurface surface, SceneAnchors anchors)
    {
        var defaultSettings = NavMesh.GetSettingsByIndex(0);
        if (defaultSettings.agentTypeID != -1)
            surface.agentTypeID = defaultSettings.agentTypeID;

        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.layerMask = ~0;
        surface.defaultArea = 0;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = false;
        surface.overrideVoxelSize = true;
        surface.voxelSize = 0.05f;
        surface.overrideTileSize = true;
        surface.tileSize = 128;
        surface.buildHeightMesh = true;

        MarkNavigationAreas(scene, anchors);
        surface.BuildNavMesh();
        Physics.SyncTransforms();
    }

    static void MarkNavigationAreas(Scene scene, SceneAnchors anchors)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (IsGroundAnchor(transform))
                    SetNavMeshArea(transform.gameObject, 0);
            }
        }

        SetNavMeshArea(anchors.Building.gameObject, 1);
        SetNavMeshArea(anchors.Well.gameObject, 1);
        SetNavMeshArea(anchors.Stones.gameObject, 1);
        SetNavMeshArea(anchors.Table.gameObject, 1);
        SetNavMeshArea(anchors.Bathtub.gameObject, 1);
    }

    static void SetNavMeshArea(GameObject gameObject, int area)
    {
        var modifier = EnsureComponent<NavMeshModifier>(gameObject);
        modifier.overrideArea = true;
        modifier.area = area;
        modifier.ignoreFromBuild = false;
        modifier.applyToChildren = true;
    }

    static void ConfigureHeadTurn(SnippetsHeadTurn headTurn, Transform actorRoot, Transform defaultLookTarget)
    {
        headTurn.mode = SnippetsHeadTurn.GazeMode.FollowTarget;
        headTurn.target = defaultLookTarget;
        headTurn.autoFindTarget = false;
        headTurn.smoothTarget = true;
        headTurn.targetFollowSpeed = 14f;
        headTurn.snapProxyOnTargetChange = true;
        headTurn.headBone = FindFirstChildByName(actorRoot, "Head");
        headTurn.waistBone = FindFirstChildByName(actorRoot, "Spine2")
            ?? FindFirstChildByName(actorRoot, "Spine1")
            ?? FindFirstChildByName(actorRoot, "Spine");
        headTurn.waistDirectionSource = FindFirstChildByName(actorRoot, "Hips");
        headTurn.lookWeight = 1f;
        headTurn.blendSpeed = 8f;
        headTurn.rotationSpeed = 14f;
        headTurn.maxYaw = 50f;
        headTurn.maxPitch = 18f;
        headTurn.normalizeTargetHeight = true;
        headTurn.waistYawWeight = 0.8f;
        headTurn.waistHeadYawThreshold = 15f;
        headTurn.waistDelay = 0f;
        headTurn.waistEngageSpeed = 3.5f;
        headTurn.waistMaxYaw = 25f;
        headTurn.waistRotationSpeed = 6f;
    }

    static void ConfigureGroundSnap(SnippetsSceneGroundSnap groundSnap, SnippetsWalker walker, Transform actorRoot, Scene scene)
    {
        groundSnap.walker = walker;
        groundSnap.probeHeight = GroundProbeHeight;
        groundSnap.maxProbeDistance = GroundProbeDistance;
        groundSnap.searchRadius = GroundSearchRadius;
        groundSnap.yOffset = GroundLift;
        groundSnap.smoothSpeed = 18f;
        groundSnap.snapOnStart = true;
        groundSnap.CacheScene(scene);
    }

    static void EnsureFirstPersonRig(Transform parent, Scene scene, SpawnPoint spawnPoint, Transform actor)
    {
        var rig = GetOrCreateChild(parent, PlayerRigName);
        var controller = EnsureComponent<CharacterController>(rig.gameObject);
        controller.height = 1.8f;
        controller.radius = 0.28f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        controller.slopeLimit = 50f;
        controller.stepOffset = 0.3f;
        controller.skinWidth = 0.03f;
        controller.minMoveDistance = 0f;

        var startAnchor = GetOrCreateChild(parent, PlayerStartName);
        var lookTarget = actor != null ? actor.position + Vector3.up * 1.45f : spawnPoint.position + spawnPoint.rotation * Vector3.forward * 3f;
        var startPosition = actor != null
            ? actor.position + actor.forward * 2.6f
            : spawnPoint.position + spawnPoint.rotation * Vector3.forward * 2.6f;
        startPosition.y = SampleGroundY(scene, null, startPosition) + controller.skinWidth;
        startAnchor.position = startPosition;

        var lookDirection = lookTarget - startPosition;
        lookDirection.y = 0f;
        startAnchor.rotation = lookDirection.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
            : spawnPoint.rotation;

        rig.position = startAnchor.position;
        rig.rotation = startAnchor.rotation;

        var cameraPivot = GetOrCreateChild(rig, "CameraPivot");
        cameraPivot.localPosition = new Vector3(0f, PlayerEyeHeight, 0f);
        cameraPivot.localRotation = Quaternion.identity;

        var cameraTransform = GetOrCreateChild(cameraPivot, PlayerCameraName);
        cameraTransform.localPosition = Vector3.zero;
        cameraTransform.localRotation = Quaternion.identity;

        var camera = EnsureComponent<Camera>(cameraTransform.gameObject);
        camera.tag = "MainCamera";
        camera.nearClipPlane = 0.03f;
        camera.fieldOfView = 75f;
        camera.enabled = true;

        var listener = EnsureComponent<AudioListener>(cameraTransform.gameObject);
        listener.enabled = true;
        DisableOtherSceneCameras(scene, camera, listener);

        var playerController = EnsureComponent<BasicFirstPersonController>(rig.gameObject);
        playerController.playerCamera = camera;
        playerController.cameraPivot = cameraPivot;
        playerController.startAnchor = startAnchor;
        playerController.startLookTarget = actor;
        playerController.walkSpeed = 4f;
        playerController.sprintSpeed = 6.5f;
        playerController.jumpHeight = 0.9f;
        playerController.gravity = -20f;
        playerController.mouseSensitivity = 2f;
        playerController.lockCursorOnStart = true;
    }

    static void DisableOtherSceneCameras(Scene scene, Camera playerCamera, AudioListener playerListener)
    {
        // Keep only the first-person player view active so Play mode always enters the scene through the controller camera.
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (camera == null)
                    continue;

                var isPlayerCamera = camera == playerCamera;
                camera.enabled = isPlayerCamera;
                if (!isPlayerCamera)
                    camera.tag = "Untagged";
            }

            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true))
            {
                if (listener == null)
                    continue;

                listener.enabled = listener == playerListener;
            }
        }
    }

    static void ConfigureRegistry(
        SnippetsActorRegistry registry,
        SnippetPlayer actorPlayer,
        SnippetsWalker walker,
        SnippetsHeadTurn headTurn,
        List<SnippetPlayer> orderedSnippets,
        AnimationClip idleClip,
        AnimationClip walkClip)
    {
        // A slightly longer blend avoids the first spoken snippet feeling like a hard animation reset.
        registry.forceIdleOnEnable = true;
        registry.crossFadeSeconds = 0.5f;
        registry.autoFindHeadTurn = true;
        registry.resetAllBlendshapesOnIdle = true;
        registry.resetAllBlendshapesOnWalk = false;
        registry.closeMouthAfterSoftStopFade = true;
        registry.postFadeMouthBufferSeconds = 0.02f;

        if (registry.actors == null)
            registry.actors = new List<SnippetsActorRegistry.Actor>();
        if (registry.actors.Count == 0)
            registry.actors.Add(new SnippetsActorRegistry.Actor());

        registry.actors.RemoveRange(1, Math.Max(0, registry.actors.Count - 1));

        var actor = registry.actors[0];
        actor.name = "Medieval Warrior";
        actor.player = actorPlayer;
        actor.walker = walker;
        actor.headTurn = headTurn;
        actor.legacyAnimation = actorPlayer.GetComponentInChildren<Animation>(true);
        actor.idleClip = idleClip;
        actor.walkClip = walkClip;
        actor.snippets = orderedSnippets;

        registry.BuildRuntimeRegistry();
    }

    static void ConfigureFlow(SnippetsFlowController flow, SnippetsActorRegistry registry)
    {
        flow.registry = registry;
        flow.playOnStart = false;
        flow.loopSequence = false;
        flow.autoProgress = true;
        flow.enableKeyboard = true;
        flow.key = KeyCode.Space;

        flow.steps = new List<SnippetsFlowController.Step>
        {
            MakeSnippetStep(0),
            MakeWalkStep(0),
            MakeSnippetStep(1),
            MakeWalkStep(1),
            MakeSnippetStep(2),
            MakeWalkStep(2),
            MakeSnippetStep(3)
        };

        flow.EnsureStepGuids();
    }

    static void ConfigureGaze(
        SnippetsGazeFlowController gaze,
        SnippetsFlowController flow,
        SnippetsActorRegistry registry,
        Transform lookSpawnForward,
        Transform lookPath,
        Transform lookBuilding,
        Transform lookWalk1,
        Transform lookWalk2,
        Transform lookWalk3,
        Transform lookWell,
        Transform lookBirdHouse,
        Transform lookStones,
        Transform lookTable)
    {
        gaze.flow = flow;
        gaze.registry = registry;
        gaze.unspecifiedActors = SnippetsGazeFlowController.UnspecifiedActorBehavior.KeepPrevious;
        gaze.autoSyncToFlowSteps = false;
        gaze.autoLabelFromFlow = false;

        gaze.gazeSteps = new List<SnippetsGazeFlowController.GazeStep>
        {
            MakeForwardGazeStep(flow.steps[0], "Snippet 1", lookSpawnForward),
            MakeOffGazeStep(flow.steps[1], "Walk 1"),
            MakeMostlyForwardGazeStep(flow.steps[2], "Snippet 2", lookBirdHouse, lookWell, 0.64f),
            MakeOffGazeStep(flow.steps[3], "Walk 2"),
            MakeMostlyForwardGazeStep(flow.steps[4], "Snippet 3", lookWell, lookStones, 0.63f),
            MakeOffGazeStep(flow.steps[5], "Walk 3"),
            MakeMostlyForwardGazeStep(flow.steps[6], "Snippet 4", lookStones, lookTable, 0.66f)
        };
    }

    static SnippetsFlowController.Step MakeSnippetStep(int snippetIndex)
    {
        return new SnippetsFlowController.Step
        {
            type = SnippetsFlowController.StepType.Snippet,
            actorIndex = 0,
            snippetIndex = snippetIndex
        };
    }

    static SnippetsFlowController.Step MakeWalkStep(int waypointIndex)
    {
        return new SnippetsFlowController.Step
        {
            type = SnippetsFlowController.StepType.Walk,
            actorIndex = 0,
            waypointIndex = waypointIndex
        };
    }

    static SnippetsGazeFlowController.GazeStep MakeSimpleGazeStep(
        SnippetsFlowController.Step flowStep,
        string label,
        Transform target)
    {
        return new SnippetsGazeFlowController.GazeStep
        {
            label = label,
            flowStepGuid = flowStep.guid,
            mode = SnippetsGazeFlowController.StepGazeMode.Simple,
            overrides = new List<SnippetsGazeFlowController.ActorGaze>
            {
                new SnippetsGazeFlowController.ActorGaze
                {
                    actorIndex = 0,
                    targetType = SnippetsGazeFlowController.TargetType.Transform,
                    targetTransform = target
                }
            }
        };
    }

    static SnippetsGazeFlowController.GazeStep MakeForwardGazeStep(
        SnippetsFlowController.Step flowStep,
        string label,
        Transform forwardTarget)
    {
        return new SnippetsGazeFlowController.GazeStep
        {
            label = label,
            flowStepGuid = flowStep.guid,
            mode = SnippetsGazeFlowController.StepGazeMode.Simple,
            overrides = new List<SnippetsGazeFlowController.ActorGaze>
            {
                new SnippetsGazeFlowController.ActorGaze
                {
                    actorIndex = 0,
                    targetType = SnippetsGazeFlowController.TargetType.Forward,
                    forwardTargetOverride = forwardTarget
                }
            }
        };
    }

    static SnippetsGazeFlowController.GazeStep MakeOffGazeStep(
        SnippetsFlowController.Step flowStep,
        string label)
    {
        return new SnippetsGazeFlowController.GazeStep
        {
            label = label,
            flowStepGuid = flowStep.guid,
            mode = SnippetsGazeFlowController.StepGazeMode.Simple,
            overrides = new List<SnippetsGazeFlowController.ActorGaze>
            {
                new SnippetsGazeFlowController.ActorGaze
                {
                    actorIndex = 0,
                    targetType = SnippetsGazeFlowController.TargetType.None
                }
            }
        };
    }

    static SnippetsGazeFlowController.GazeStep MakeMostlyForwardGazeStep(
        SnippetsFlowController.Step flowStep,
        string label,
        Transform forwardTarget,
        Transform briefSideTarget,
        float sideGlancePercent)
    {
        return MakeGranularGazeStep(
            flowStep,
            label,
            ForwardCue(0f, forwardTarget),
            Cue(sideGlancePercent, briefSideTarget, 0.18f),
            ForwardCue(Mathf.Clamp01(sideGlancePercent + 0.1f), forwardTarget, 0.16f));
    }

    static SnippetsGazeFlowController.GazeStep MakeGranularGazeStep(
        SnippetsFlowController.Step flowStep,
        string label,
        params SnippetsGazeFlowController.GazeCue[] cues)
    {
        return new SnippetsGazeFlowController.GazeStep
        {
            label = label,
            flowStepGuid = flowStep.guid,
            mode = SnippetsGazeFlowController.StepGazeMode.Granular,
            cues = cues.ToList()
        };
    }

    static SnippetsGazeFlowController.GazeCue Cue(float percent, Transform target, float blendSeconds = 0.25f)
    {
        return new SnippetsGazeFlowController.GazeCue
        {
            percent = percent,
            blendSeconds = blendSeconds,
            overrides = new List<SnippetsGazeFlowController.ActorGaze>
            {
                new SnippetsGazeFlowController.ActorGaze
                {
                    actorIndex = 0,
                    targetType = SnippetsGazeFlowController.TargetType.Transform,
                    targetTransform = target
                }
            }
        };
    }

    static SnippetsGazeFlowController.GazeCue ForwardCue(float percent, Transform target, float blendSeconds = 0.22f)
    {
        return new SnippetsGazeFlowController.GazeCue
        {
            percent = percent,
            blendSeconds = blendSeconds,
            overrides = new List<SnippetsGazeFlowController.ActorGaze>
            {
                new SnippetsGazeFlowController.ActorGaze
                {
                    actorIndex = 0,
                    targetType = SnippetsGazeFlowController.TargetType.Forward,
                    forwardTargetOverride = target
                }
            }
        };
    }

    static List<SnippetPlayer> LoadOrderedSnippetPrefabs()
    {
        var metadata = LoadSnippetMetadata();
        var guidToPath = AssetDatabase.FindAssets("t:Prefab", new[] { SnippetFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(Path.GetFileNameWithoutExtension, p => p, StringComparer.Ordinal);

        var ordered = new List<SnippetPlayer>();
        foreach (var snippet in metadata.snippets.OrderBy(s => s.order))
        {
            if (!guidToPath.TryGetValue(snippet.name, out var assetPath))
                throw new InvalidOperationException($"Could not find prefab for snippet '{snippet.name}'.");

            var prefab = AssetDatabase.LoadAssetAtPath<SnippetPlayer>(assetPath);
            if (prefab == null)
                throw new InvalidOperationException($"Prefab at '{assetPath}' is missing a SnippetPlayer component.");

            ordered.Add(prefab);
        }

        return ordered;
    }

    static SnippetSetFile LoadSnippetMetadata()
    {
        var absoluteMetadataPath = Path.GetFullPath(MetadataPath);
        if (!File.Exists(absoluteMetadataPath))
            throw new FileNotFoundException("Could not find warrior snippet metadata.", absoluteMetadataPath);

        var json = File.ReadAllText(absoluteMetadataPath);
        var file = JsonUtility.FromJson<SnippetSetFile>(json);
        if (file == null || file.snippets == null || file.snippets.Length == 0)
            throw new InvalidOperationException("Warrior snippet metadata.json is empty or invalid.");

        return file;
    }

    static AnimationClip LoadAnimationClip(string assetPath, string clipName)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().ToList();
        var clip = clips.FirstOrDefault(c => c.name == clipName)
            ?? clips.FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));

        if (clip == null)
            throw new InvalidOperationException($"Could not load animation clip '{clipName}' from '{assetPath}'.");

        return clip;
    }

    static void PlaceActor(Transform actor, Vector3 position, Quaternion rotation)
    {
        actor.position = position;
        actor.rotation = rotation;
    }

    static Transform CreateOrMoveWaypoint(Transform parent, string name, Vector3 position, Vector3 lookAt)
    {
        var waypoint = GetOrCreateChild(parent, name);
        waypoint.position = position;
        waypoint.rotation = FlatLookRotation(lookAt - position, parent.forward);
        return waypoint;
    }

    static Transform CreateOrMoveLookTarget(Transform parent, string name, Vector3 position)
    {
        var target = GetOrCreateChild(parent, name);
        target.position = position;
        target.rotation = Quaternion.identity;
        return target;
    }

    static SpawnPoint BuildSpawnPoint(Scene scene, Transform actor, Transform building, Transform path)
    {
        var direction = FlatDirection(path.position - building.position, building.forward);
        var bounds = GetHierarchyBounds(building.gameObject);
        var radius = Mathf.Max(bounds.extents.x, bounds.extents.z, 0.75f);
        var position = building.position + direction * (radius + SpawnPadding);
        position.y = ComputeGroundedActorY(scene, actor, position);

        return new SpawnPoint
        {
            position = position,
            rotation = FlatLookRotation(direction, building.forward)
        };
    }

    static Vector3 ComputeStandPosition(Scene scene, Transform actor, Transform target, Vector3 previousPosition, float distance)
    {
        var bounds = GetHierarchyBounds(target.gameObject);
        var targetCenter = bounds.size == Vector3.zero ? target.position : bounds.center;
        var approach = FlatDirection(targetCenter - previousPosition, Vector3.forward);
        var radius = GetHorizontalRadius(bounds, approach);
        var stand = targetCenter - approach * (radius + distance);
        stand.y = ComputeGroundedActorY(scene, actor, stand);
        return stand;
    }

    static Vector3 GetForwardLookPoint(Vector3 origin, Vector3 destination)
    {
        var direction = FlatDirection(destination - origin, Vector3.forward);
        return origin + direction * 2.8f + Vector3.up * HeadLookHeight;
    }

    static Vector3 GetLookPoint(Transform target, float verticalOffset)
    {
        var bounds = GetHierarchyBounds(target.gameObject);
        var point = bounds.size == Vector3.zero ? target.position : bounds.center;
        point.y += verticalOffset > 0f ? verticalOffset : HeadLookHeight;
        return point;
    }

    static Bounds GetHierarchyBounds(GameObject gameObject)
    {
        var renderers = gameObject.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(gameObject.transform.position, Vector3.zero);

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    static float GetHorizontalRadius(Bounds bounds, Vector3 direction)
    {
        direction = FlatDirection(direction, Vector3.forward);
        return Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.z) * bounds.extents.z;
    }

    static float GetPivotToFeet(Transform actor)
    {
        var bounds = GetHierarchyBounds(actor.gameObject);
        return actor.position.y - bounds.min.y;
    }

    static float ComputeGroundedActorY(Scene scene, Transform actor, Vector3 worldPosition)
    {
        return SampleGroundY(scene, actor, worldPosition) + GetPivotToFeet(actor) + GroundLift;
    }

    static float SampleGroundY(Scene scene, Transform actor, Vector3 worldPosition)
    {
        if (TryRaycastGround(worldPosition, out var exactGroundY))
            return exactGroundY;

        var bestAnchorY = float.NaN;
        var bestAnchorDistance = float.PositiveInfinity;

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsGroundAnchor(transform))
                    continue;

                var horizontalDistance = FlatDistanceSqr(worldPosition, transform.position);
                if (horizontalDistance < bestAnchorDistance)
                {
                    bestAnchorDistance = horizontalDistance;
                    bestAnchorY = transform.position.y;
                }
            }
        }

        if (!float.IsNaN(bestAnchorY))
            return bestAnchorY;

        var bestSupportY = float.NegativeInfinity;
        var bestSupportDistance = float.PositiveInfinity;

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled)
                    continue;
                if (actor != null && renderer.transform.IsChildOf(actor))
                    continue;

                var bounds = renderer.bounds;
                if (bounds.size == Vector3.zero)
                    continue;

                var dx = Mathf.Max(0f, Mathf.Abs(worldPosition.x - bounds.center.x) - bounds.extents.x);
                var dz = Mathf.Max(0f, Mathf.Abs(worldPosition.z - bounds.center.z) - bounds.extents.z);
                var horizontalDistance = Mathf.Sqrt(dx * dx + dz * dz);
                if (horizontalDistance > GroundSearchRadius)
                    continue;

                var candidateY = bounds.max.y;
                if (candidateY > worldPosition.y + GroundProbeHeight)
                    continue;

                if (candidateY > bestSupportY + 0.01f ||
                    (Mathf.Abs(candidateY - bestSupportY) <= 0.01f && horizontalDistance < bestSupportDistance))
                {
                    bestSupportY = candidateY;
                    bestSupportDistance = horizontalDistance;
                }
            }
        }

        if (bestSupportY > float.NegativeInfinity)
            return bestSupportY;

        return worldPosition.y;
    }

    static bool TryRaycastGround(Vector3 worldPosition, out float groundY)
    {
        var rayOrigin = new Vector3(worldPosition.x, worldPosition.y + GroundProbeHeight, worldPosition.z);
        var hits = Physics.RaycastAll(rayOrigin, Vector3.down, GroundProbeDistance, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length > 0)
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (var i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (!IsGroundAnchorInHierarchy(hit.collider != null ? hit.collider.transform : null))
                continue;

            groundY = hit.point.y;
            return true;
        }

        groundY = 0f;
        return false;
    }

    static void EnsureGroundSurfaceColliders(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsGroundAnchor(transform))
                    continue;

                var meshFilter = transform.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                    continue;

                var meshCollider = transform.GetComponent<MeshCollider>();
                if (meshCollider == null)
                    meshCollider = Undo.AddComponent<MeshCollider>(transform.gameObject);

                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = false;
                meshCollider.isTrigger = false;
                meshCollider.enabled = true;
            }
        }

        Physics.SyncTransforms();
    }

    static bool IsGroundAnchor(Transform transform)
    {
        if (transform == null)
            return false;

        var name = transform.name;
        return name.IndexOf("terrain", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsGroundAnchorInHierarchy(Transform transform)
    {
        while (transform != null)
        {
            if (IsGroundAnchor(transform))
                return true;
            transform = transform.parent;
        }

        return false;
    }

    static Transform FindRequired(Scene scene, string exactName)
    {
        var transform = FindAll(scene, t => t.name == exactName).FirstOrDefault();
        if (transform == null)
            throw new InvalidOperationException($"Could not find scene object '{exactName}'.");
        return transform;
    }

    static List<Transform> FindAll(Scene scene, Func<Transform, bool> predicate)
    {
        var results = new List<Transform>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (predicate(transform))
                    results.Add(transform);
            }
        }

        return results;
    }

    static Transform FindFirstChildByName(Transform root, string exactName)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name == exactName)
                return transform;
        }

        return null;
    }

    static T EnsureComponent<T>(GameObject gameObject) where T : Component
    {
        var component = gameObject.GetComponent<T>();
        if (component == null)
            component = gameObject.AddComponent<T>();
        return component;
    }

    static T EnsureComponentOnChild<T>(Transform parent, string childName) where T : Component
    {
        var child = GetOrCreateChild(parent, childName);
        return EnsureComponent<T>(child.gameObject);
    }

    static GameObject GetOrCreateRoot(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == name)
                return root;
        }

        var created = new GameObject(name);
        SceneManager.MoveGameObjectToScene(created, scene);
        return created;
    }

    static Transform GetOrCreateChild(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null)
            return existing;

        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    static Vector3 FlatDirection(Vector3 vector, Vector3 fallback)
    {
        vector.y = 0f;
        if (vector.sqrMagnitude < 0.0001f)
        {
            fallback.y = 0f;
            if (fallback.sqrMagnitude < 0.0001f)
                return Vector3.forward;
            return fallback.normalized;
        }

        return vector.normalized;
    }

    static Quaternion FlatLookRotation(Vector3 vector, Vector3 fallback)
    {
        return Quaternion.LookRotation(FlatDirection(vector, fallback), Vector3.up);
    }

    static float FlatDistanceSqr(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return (a - b).sqrMagnitude;
    }

    [Serializable]
    class SnippetSetFile
    {
        public SnippetFile[] snippets;
    }

    [Serializable]
    class SnippetFile
    {
        public int order;
        public string name;
    }

    class SceneAnchors
    {
        public Transform Building;
        public Transform Well;
        public Transform BirdHouse;
        public Transform Stones;
        public Transform Table;
        public Transform Bathtub;
        public Transform PathNearBuilding;
    }

    struct SpawnPoint
    {
        public Vector3 position;
        public Quaternion rotation;
    }
}

[InitializeOnLoad]
static class RpgppWarriorFlowAutoRunner
{
    static RpgppWarriorFlowAutoRunner()
    {
        EditorApplication.delayCall += TryAutoRun;
    }

    static void TryAutoRun()
    {
        var markerPath = Path.GetFullPath(RpgppWarriorFlowBuilder.AutoRunMarkerPath);
        if (!File.Exists(markerPath))
            return;

        try
        {
            File.Delete(markerPath);
        }
        catch
        {
            // If deletion fails we still attempt the build once; the next domain reload can retry.
        }

        try
        {
            RpgppWarriorFlowBuilder.BuildAndSaveOpenSceneWarriorFlow();
            Debug.Log("[RPGPP Warrior Flow] Auto-run completed in the open Unity editor.");
        }
        catch (Exception ex)
        {
            Debug.LogError("[RPGPP Warrior Flow] Auto-run failed: " + ex);
        }
    }
}
