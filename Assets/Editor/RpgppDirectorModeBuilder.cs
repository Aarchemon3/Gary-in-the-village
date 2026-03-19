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

public static class RpgppDirectorModeBuilder
{
    public const string AutoRunMarkerPath = "Temp/RpgppDirectorMode.autorun";
    const string FlowRootName = "__RPGPP_WarriorFlow";
    const string ActorName = "WarriorActor";
    const string DestinationsName = "Destinations";
    const string LookTargetsName = "LookTargets";
    const string RegistryName = "ActorRegistry";
    const string FlowName = "FlowController";
    const string GazeName = "GazeFlowController";
    const string NavSurfaceName = "NavMeshSurface";
    const string PlayerRigName = "FirstPersonPlayer";
    const string PlayerCameraName = "PlayerCamera";
    const string PlayerStartName = "FirstPersonStart";
    const string IdleClipPath = "Packages/com.snippets.sdk/Runtime/Assets/Animations/M_Idle.fbx";
    const string WalkClipPath = "Packages/com.snippets.sdk/Runtime/Assets/Animations/M_Walk.fbx";

    const float SpawnPadding = 2.6f;
    const float HeadLookHeight = 1.45f;
    const float GroundProbeHeight = 40f;
    const float GroundProbeDistance = 120f;
    const float GroundSearchRadius = 1.5f;
    const float GroundLift = -0.035f;
    const float PlayerEyeHeight = 1.62f;

    public static void BuildOpenScene(DirectorModeRecipe recipe)
    {
        BuildInternal(CreatePlan(recipe), saveScene: false);
    }

    public static void BuildDefaultRecipeSceneAndSave()
    {
        var recipe = RpgppDirectorModeDefaults.LoadOrCreateDefaultRecipe();
        if (recipe == null)
            recipe = CreateTransientDefaultRecipe();

        BuildRecipeSceneAndSave(recipe);
    }

    public static void BuildTransientDefaultRecipeSceneAndSave()
    {
        BuildRecipeSceneAndSave(CreateTransientDefaultRecipe());
    }

    static DirectorModeRecipe CreateTransientDefaultRecipe()
    {
        var recipe = ScriptableObject.CreateInstance<DirectorModeRecipe>();
        recipe.ResetToRpgppDefaults();
        return recipe;
    }

    public static void BuildRecipeSceneAndSave(DirectorModeRecipe recipe)
    {
        var plan = CreatePlan(recipe);
        var scene = EditorSceneManager.OpenScene(plan.scenePath, OpenSceneMode.Single);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException($"Failed to open scene '{plan.scenePath}'.");

        BuildInternal(plan, saveScene: true);
    }

    static void BuildInternal(DirectorModePlan recipe, bool saveScene)
    {
        ValidateRecipe(recipe);

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("Open the target scene before building Director Mode.");

        EnsureGroundSurfaceColliders(scene);

        var root = GetOrCreateRoot(scene, FlowRootName);
        var destinationsRoot = GetOrCreateChild(root.transform, DestinationsName);
        var lookTargetsRoot = GetOrCreateChild(root.transform, LookTargetsName);
        var navSurfaceRoot = GetOrCreateChild(root.transform, NavSurfaceName);

        var orderedSnippets = LoadOrderedSnippetPrefabs(recipe.snippetFolderPath);
        if (orderedSnippets.Count == 0)
            throw new InvalidOperationException("No snippet prefabs were found in the selected snippet folder.");

        var obstacleAnchors = ResolveNavigationObstacles(scene);
        var spawnNear = FindRequired(scene, recipe.spawnNearObjectName);
        var spawnFacing = ResolveFacingTarget(scene, recipe.spawnFacingObjectName, recipe.spawnFacingObjectPrefix);

        var actor = EnsureActor(root.transform, orderedSnippets[0]);
        ApplyTextDisplayMode(actor, recipe.textDisplayMode);

        var idleClip = LoadAnimationClip(IdleClipPath, "M_Idle");
        var walkClip = LoadAnimationClip(WalkClipPath, "M_Walk");
        var navSurface = EnsureComponent<NavMeshSurface>(navSurfaceRoot.gameObject);
        ConfigureNavMesh(scene, navSurface, obstacleAnchors);

        var spawnPoint = BuildSpawnPoint(scene, actor.transform, spawnNear, spawnFacing);
        PlaceActor(actor.transform, spawnPoint.position, spawnPoint.rotation);

        if (recipe.createFirstPersonController)
            EnsureFirstPersonRig(root.transform, scene, spawnPoint, actor.transform);

        var lookSpawnForward = CreateOrMoveLookTarget(
            lookTargetsRoot,
            "Look_SpawnForward",
            spawnPoint.position + spawnPoint.rotation * Vector3.forward * 3.2f + Vector3.up * HeadLookHeight);

        var waypoints = new List<Transform>();
        var stopLookTargets = new List<Transform>();
        var sideLookTargets = new List<Transform>();

        var previousPosition = spawnPoint.position;
        for (var i = 0; i < recipe.stops.Count; i++)
        {
            var stop = recipe.stops[i];
            if (stop.snippetIndex < 0 || stop.snippetIndex >= orderedSnippets.Count)
                throw new InvalidOperationException($"Stop '{stop.label}' uses snippet index {stop.snippetIndex}, but only {orderedSnippets.Count} snippets are available.");

            if (i == 0)
            {
                stopLookTargets.Add(lookSpawnForward);
                sideLookTargets.Add(null);
                continue;
            }

            var destination = FindRequired(scene, stop.destinationObjectName);
            var faceTarget = FindRequired(scene, stop.faceObjectName);
            var sideTarget = string.IsNullOrWhiteSpace(stop.sideGlanceObjectName)
                ? faceTarget
                : FindRequired(scene, stop.sideGlanceObjectName);

            var stopPosition = ComputeStandPosition(scene, actor.transform, destination, previousPosition, stop.standDistance);
            var stopLook = CreateOrMoveLookTarget(
                lookTargetsRoot,
                $"Look_Stop_{i + 1:00}_{SanitizeLabel(stop.label)}",
                GetLookPoint(faceTarget, GetVerticalLookOffset(faceTarget.name)));
            var sideLook = CreateOrMoveLookTarget(
                lookTargetsRoot,
                $"Look_Side_{i + 1:00}_{SanitizeLabel(stop.label)}",
                GetLookPoint(sideTarget, GetVerticalLookOffset(sideTarget.name)));
            var waypoint = CreateOrMoveWaypoint(
                destinationsRoot,
                $"WP_{i:00}_{SanitizeLabel(stop.label)}",
                stopPosition,
                stopLook.position);

            waypoints.Add(waypoint);
            stopLookTargets.Add(stopLook);
            sideLookTargets.Add(sideLook);
            previousPosition = stopPosition;
        }

        var walker = EnsureComponent<SnippetsWalker>(actor.gameObject);
        ConfigureWalker(actor.gameObject, walker, waypoints.ToArray());

        var headTurn = EnsureComponent<SnippetsHeadTurn>(actor.gameObject);
        ConfigureHeadTurn(headTurn, actor.transform, lookSpawnForward);

        var groundSnap = EnsureComponent<SnippetsSceneGroundSnap>(actor.gameObject);
        ConfigureGroundSnap(groundSnap, walker, scene);
        groundSnap.SnapNow();

        var registry = EnsureComponentOnChild<SnippetsActorRegistry>(root.transform, RegistryName);
        ConfigureRegistry(registry, actor, walker, headTurn, orderedSnippets, idleClip, walkClip, recipe.actorDisplayName);

        var flow = EnsureComponentOnChild<SnippetsFlowController>(root.transform, FlowName);
        ConfigureFlow(flow, registry, recipe);

        var gaze = EnsureComponentOnChild<SnippetsGazeFlowController>(root.transform, GazeName);
        ConfigureGaze(gaze, flow, registry, recipe, stopLookTargets, sideLookTargets);

        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene)
            EditorSceneManager.SaveScene(scene);

        Debug.Log("[RPGPP Director Mode] Build completed.");
    }

    static DirectorModePlan CreatePlan(DirectorModeRecipe recipe)
    {
        if (recipe == null)
            throw new InvalidOperationException("Assign a Director Mode recipe first.");

        var plan = new DirectorModePlan
        {
            scenePath = recipe.scenePath,
            snippetFolderPath = recipe.SnippetFolderPath,
            actorDisplayName = recipe.actorDisplayName,
            textDisplayMode = recipe.textDisplayMode,
            createFirstPersonController = recipe.createFirstPersonController,
            playOnStart = recipe.playOnStart,
            spawnNearObjectName = recipe.spawnNearObjectName,
            spawnFacingObjectName = recipe.spawnFacingObjectName,
            spawnFacingObjectPrefix = recipe.spawnFacingObjectPrefix,
            stops = recipe.stops != null
                ? recipe.stops.Select(stop => new DirectorModePlanStop
                {
                    label = stop.label,
                    snippetIndex = stop.snippetIndex,
                    destinationObjectName = stop.destinationObjectName,
                    faceObjectName = stop.faceObjectName,
                    sideGlanceObjectName = stop.sideGlanceObjectName,
                    standDistance = stop.standDistance,
                    sideGlancePercent = stop.sideGlancePercent
                }).ToList()
                : new List<DirectorModePlanStop>()
        };

        return plan;
    }

    static void ValidateRecipe(DirectorModePlan recipe)
    {
        if (recipe == null)
            throw new InvalidOperationException("Assign a Director Mode recipe first.");
        if (string.IsNullOrWhiteSpace(recipe.scenePath))
            throw new InvalidOperationException("Recipe scene path is empty.");
        if (string.IsNullOrWhiteSpace(recipe.snippetFolderPath))
            throw new InvalidOperationException("Recipe snippet folder is not assigned.");
        if (recipe.stops == null || recipe.stops.Count == 0)
            throw new InvalidOperationException("Recipe must contain at least one stop.");
        if (string.IsNullOrWhiteSpace(recipe.spawnNearObjectName))
            throw new InvalidOperationException("Recipe spawn near object name is empty.");
    }

    static void ApplyTextDisplayMode(SnippetPlayer actor, SnippetTextDisplayMode mode)
    {
        var textPlayer = actor.GetComponentInChildren<SnippetTmpTextPlayer>(true);
        if (textPlayer == null)
            return;

        var serialized = new SerializedObject(textPlayer);
        var displayMode = serialized.FindProperty("m_displayMode");
        if (displayMode == null)
            return;

        displayMode.enumValueIndex = (int)mode;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static Transform ResolveFacingTarget(Scene scene, string exactName, string prefix)
    {
        if (!string.IsNullOrWhiteSpace(exactName))
            return FindRequired(scene, exactName);

        var safePrefix = prefix ?? string.Empty;
        var candidates = FindAll(scene, t => t.name.StartsWith(safePrefix, StringComparison.Ordinal));
        if (candidates.Count == 0)
            throw new InvalidOperationException($"Could not find any scene object starting with '{safePrefix}'.");

        return candidates[0];
    }

    static NavigationAnchors ResolveNavigationObstacles(Scene scene)
    {
        return new NavigationAnchors
        {
            Building = FindOptional(scene, "rpgpp_lt_building_02 (1)"),
            Well = FindOptional(scene, "rpgpp_lt_well_01"),
            Stones = FindOptional(scene, "rpgpp_lt_stones_01"),
            Table = FindOptional(scene, "rpgpp_lt_table_01"),
            Bathtub = FindOptional(scene, "rpgpp_lt_bathtub_wood_01 (1)")
        };
    }

    static List<SnippetPlayer> LoadOrderedSnippetPrefabs(string snippetFolderPath)
    {
        var metadataPath = Path.Combine(snippetFolderPath, "Raw/metadata.json").Replace("\\", "/");
        var metadata = LoadSnippetMetadata(metadataPath);
        var guidToPath = AssetDatabase.FindAssets("t:Prefab", new[] { snippetFolderPath })
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

    static SnippetSetFile LoadSnippetMetadata(string metadataPath)
    {
        var absoluteMetadataPath = Path.GetFullPath(metadataPath);
        if (!File.Exists(absoluteMetadataPath))
            throw new FileNotFoundException("Could not find snippet metadata.", absoluteMetadataPath);

        var json = File.ReadAllText(absoluteMetadataPath);
        var file = JsonUtility.FromJson<SnippetSetFile>(json);
        if (file == null || file.snippets == null || file.snippets.Length == 0)
            throw new InvalidOperationException("Snippet metadata.json is empty or invalid.");

        return file;
    }

    static SnippetPlayer EnsureActor(Transform parent, SnippetPlayer sourcePrefab)
    {
        var actor = parent.Find(ActorName)?.GetComponent<SnippetPlayer>();
        var needsRebuild =
            actor == null ||
            actor.Value == null ||
            PrefabUtility.GetCorrespondingObjectFromSource(actor.gameObject) != sourcePrefab.gameObject;

        if (needsRebuild && actor != null)
        {
            UnityEngine.Object.DestroyImmediate(actor.gameObject);
            actor = null;
        }

        if (actor == null)
        {
            var instance = PrefabUtility.InstantiatePrefab(sourcePrefab.gameObject, parent) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("Failed to instantiate the director mode actor.");

            instance.name = ActorName;
            actor = instance.GetComponent<SnippetPlayer>();
        }

        actor.gameObject.name = ActorName;
        Selection.activeObject = actor.gameObject;
        return actor;
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

    static void ConfigureNavMesh(Scene scene, NavMeshSurface surface, NavigationAnchors anchors)
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

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (IsGroundAnchor(transform))
                    SetNavMeshArea(transform.gameObject, 0);
            }
        }

        SetObstacleArea(anchors.Building);
        SetObstacleArea(anchors.Well);
        SetObstacleArea(anchors.Stones);
        SetObstacleArea(anchors.Table);
        SetObstacleArea(anchors.Bathtub);

        surface.BuildNavMesh();
        Physics.SyncTransforms();
    }

    static void SetObstacleArea(Transform transform)
    {
        if (transform != null)
            SetNavMeshArea(transform.gameObject, 1);
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

    static void ConfigureGroundSnap(SnippetsSceneGroundSnap groundSnap, SnippetsWalker walker, Scene scene)
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
        var lookTarget = actor.position + Vector3.up * 1.45f;
        var startPosition = actor.position + actor.forward * 2.6f;
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
        AnimationClip walkClip,
        string actorDisplayName)
    {
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
        actor.name = string.IsNullOrWhiteSpace(actorDisplayName) ? "Actor" : actorDisplayName;
        actor.player = actorPlayer;
        actor.walker = walker;
        actor.headTurn = headTurn;
        actor.legacyAnimation = actorPlayer.GetComponentInChildren<Animation>(true);
        actor.idleClip = idleClip;
        actor.walkClip = walkClip;
        actor.snippets = orderedSnippets;

        registry.BuildRuntimeRegistry();
    }

    static void ConfigureFlow(SnippetsFlowController flow, SnippetsActorRegistry registry, DirectorModePlan recipe)
    {
        flow.registry = registry;
        flow.playOnStart = recipe.playOnStart;
        flow.loopSequence = false;
        flow.autoProgress = true;
        flow.enableKeyboard = true;
        flow.key = KeyCode.Space;

        var steps = new List<SnippetsFlowController.Step>();
        for (var i = 0; i < recipe.stops.Count; i++)
        {
            if (i > 0)
                steps.Add(MakeWalkStep(i - 1));

            steps.Add(MakeSnippetStep(recipe.stops[i].snippetIndex));
        }

        flow.steps = steps;
        flow.EnsureStepGuids();
    }

    static void ConfigureGaze(
        SnippetsGazeFlowController gaze,
        SnippetsFlowController flow,
        SnippetsActorRegistry registry,
        DirectorModePlan recipe,
        List<Transform> forwardTargets,
        List<Transform> sideTargets)
    {
        gaze.flow = flow;
        gaze.registry = registry;
        gaze.unspecifiedActors = SnippetsGazeFlowController.UnspecifiedActorBehavior.KeepPrevious;
        gaze.autoSyncToFlowSteps = false;
        gaze.autoLabelFromFlow = false;

        var gazeSteps = new List<SnippetsGazeFlowController.GazeStep>();
        for (var i = 0; i < recipe.stops.Count; i++)
        {
            if (i > 0)
                gazeSteps.Add(MakeOffGazeStep(flow.steps[(i * 2) - 1], $"Walk {i}"));

            var flowStep = flow.steps[i == 0 ? 0 : i * 2];
            if (i == 0)
            {
                gazeSteps.Add(MakeForwardGazeStep(flowStep, recipe.stops[i].label, forwardTargets[i]));
            }
            else
            {
                var sidePercent = Mathf.Clamp01(recipe.stops[i].sideGlancePercent);
                if (sidePercent <= 0.001f || sideTargets[i] == null)
                    gazeSteps.Add(MakeForwardGazeStep(flowStep, recipe.stops[i].label, forwardTargets[i]));
                else
                    gazeSteps.Add(MakeMostlyForwardGazeStep(flowStep, recipe.stops[i].label, forwardTargets[i], sideTargets[i], sidePercent));
            }
        }

        gaze.gazeSteps = gazeSteps;
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

    static SnippetsGazeFlowController.GazeStep MakeForwardGazeStep(SnippetsFlowController.Step flowStep, string label, Transform forwardTarget)
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

    static SnippetsGazeFlowController.GazeStep MakeOffGazeStep(SnippetsFlowController.Step flowStep, string label)
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
        return new SnippetsGazeFlowController.GazeStep
        {
            label = label,
            flowStepGuid = flowStep.guid,
            mode = SnippetsGazeFlowController.StepGazeMode.Granular,
            cues = new[]
            {
                ForwardCue(0f, forwardTarget),
                Cue(sideGlancePercent, briefSideTarget, 0.18f),
                ForwardCue(Mathf.Clamp01(sideGlancePercent + 0.1f), forwardTarget, 0.16f)
            }.ToList()
        };
    }

    static SnippetsGazeFlowController.GazeCue Cue(float percent, Transform target, float blendSeconds)
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

    static AnimationClip LoadAnimationClip(string assetPath, string clipName)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().ToList();
        var clip = clips.FirstOrDefault(c => c.name == clipName)
            ?? clips.FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));

        if (clip == null)
            throw new InvalidOperationException($"Could not load animation clip '{clipName}' from '{assetPath}'.");

        return clip;
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

    static void PlaceActor(Transform actor, Vector3 position, Quaternion rotation)
    {
        actor.position = position;
        actor.rotation = rotation;
    }

    static Vector3 GetLookPoint(Transform target, float verticalOffset)
    {
        var bounds = GetHierarchyBounds(target.gameObject);
        var point = bounds.size == Vector3.zero ? target.position : bounds.center;
        point.y += verticalOffset > 0f ? verticalOffset : HeadLookHeight;
        return point;
    }

    static float GetVerticalLookOffset(string objectName)
    {
        if (objectName.IndexOf("bird_house", StringComparison.OrdinalIgnoreCase) >= 0)
            return 0.25f;
        if (objectName.IndexOf("well", StringComparison.OrdinalIgnoreCase) >= 0)
            return 0.45f;
        if (objectName.IndexOf("stones", StringComparison.OrdinalIgnoreCase) >= 0)
            return 0.85f;
        if (objectName.IndexOf("table", StringComparison.OrdinalIgnoreCase) >= 0)
            return 1.05f;
        if (objectName.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0)
            return 1.75f;
        return HeadLookHeight;
    }

    static string SanitizeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "Stop";

        var chars = label.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        return new string(chars).Trim('_');
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

        return worldPosition.y;
    }

    static bool TryRaycastGround(Vector3 origin, out float groundY)
    {
        var rayOrigin = origin + Vector3.up * GroundProbeHeight;
        if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, GroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
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

                var meshCollider = EnsureComponent<MeshCollider>(transform.gameObject);
                meshCollider.sharedMesh = meshFilter.sharedMesh;
            }
        }
    }

    static bool IsGroundAnchor(Transform transform)
    {
        var name = transform.name;
        return name.IndexOf("terrain", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("wood_path", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static Transform FindRequired(Scene scene, string exactName)
    {
        var found = FindOptional(scene, exactName);
        if (found == null)
            throw new InvalidOperationException($"Could not find required scene object '{exactName}'.");
        return found;
    }

    static Transform FindOptional(Scene scene, string exactName)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == exactName)
                    return transform;
            }
        }

        return null;
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
            return fallback.sqrMagnitude < 0.0001f ? Vector3.forward : fallback.normalized;
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

    class DirectorModePlan
    {
        public string scenePath;
        public string snippetFolderPath;
        public string actorDisplayName;
        public SnippetTextDisplayMode textDisplayMode;
        public bool createFirstPersonController;
        public bool playOnStart;
        public string spawnNearObjectName;
        public string spawnFacingObjectName;
        public string spawnFacingObjectPrefix;
        public List<DirectorModePlanStop> stops;
    }

    class DirectorModePlanStop
    {
        public string label;
        public int snippetIndex;
        public string destinationObjectName;
        public string faceObjectName;
        public string sideGlanceObjectName;
        public float standDistance;
        public float sideGlancePercent;
    }

    [Serializable]
    class SnippetFile
    {
        public int order;
        public string name;
    }

    class NavigationAnchors
    {
        public Transform Building;
        public Transform Well;
        public Transform Stones;
        public Transform Table;
        public Transform Bathtub;
    }

    struct SpawnPoint
    {
        public Vector3 position;
        public Quaternion rotation;
    }
}

[InitializeOnLoad]
static class RpgppDirectorModeAutoRunner
{
    static RpgppDirectorModeAutoRunner()
    {
        EditorApplication.delayCall += TryAutoRun;
    }

    static void TryAutoRun()
    {
        var markerPath = Path.GetFullPath(RpgppDirectorModeBuilder.AutoRunMarkerPath);
        if (!File.Exists(markerPath))
            return;

        try
        {
            File.Delete(markerPath);
        }
        catch
        {
            // Best effort cleanup.
        }

        try
        {
            RpgppDirectorModeBuilder.BuildTransientDefaultRecipeSceneAndSave();
            Debug.Log("[RPGPP Director Mode] Auto-run completed in the open Unity editor.");
        }
        catch (Exception ex)
        {
            Debug.LogError("[RPGPP Director Mode] Auto-run failed: " + ex);
        }
    }
}
