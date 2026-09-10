using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetTopologySuite.Geometries;
using UnityEngine;

// Runs the full C# pipeline (Steps 1-7) and applies the result directly to the
// actual scene: houses, avatars, and the traverse-zone outline. This is the
// direct-application replacement for the old "read JSON, call
// TransferManager.ProcessOptResult" flow -- not yet wired to the X/Y keys
// (that's Step 8), so for now it's triggered manually via the context menu
// for testing in Play mode.
//
// NOTE: must be run in Play mode -- LocalAvatar/RemoteAvatar/RemoteAvatar1 are
// spawned by Fusion at runtime and won't exist in the Editor scene view.
public class LocalOptimizationRunner : MonoBehaviour
{
    [SerializeField] Arrange_Walkin arrangeWalkin;
    [SerializeField] Regions regions;
    [Tooltip("Which house am I (0, 1, or 2)? Temporary manual field until Step 8 wires this to SceneSelection.type.")]
    [SerializeField] int myType = 0;

    const string RoomId = "2";
    const int NumHouses = 3;

    // Guards against a second overlapping run if X is pressed again while the
    // background DE search (~20-30s) from a previous press is still in flight --
    // without this, two concurrent RunOptimizationAndApply() calls would race on
    // the same GameObjects and could reintroduce the duplicate-object issue
    // DestroyImmediate was just fixed for.
    bool isRunning = false;

    // Held-press threshold for the controller triggers -- a plain GetDown fired on every
    // quick trigger tap the Meta XR Simulator emits while grabbing/dragging an avatar
    // around, so optimization kept running by accident mid-drag. Requiring a trigger
    // to be held continuously for this long filters out those short taps.
    const float TriggerHoldSeconds = 0.6f;
    float rightTriggerHeldSince = -1f;
    float leftTriggerHeldSince = -1f;

    void Update()
    {
        bool zPressed = Input.GetKeyDown(KeyCode.Z);
        bool mPressed = Input.GetKeyDown(KeyCode.M);
        bool rightTriggerFired = false;
        bool leftTriggerFired = false;

        if (OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger))
        {
            if (rightTriggerHeldSince < 0f) rightTriggerHeldSince = Time.unscaledTime;
            else if (Time.unscaledTime - rightTriggerHeldSince >= TriggerHoldSeconds)
            {
                rightTriggerFired = true;
                rightTriggerHeldSince = float.PositiveInfinity; // don't fire again until released
            }
        }
        else
        {
            rightTriggerHeldSince = -1f;
        }

        if (OVRInput.Get(OVRInput.Button.SecondaryIndexTrigger))
        {
            if (leftTriggerHeldSince < 0f) leftTriggerHeldSince = Time.unscaledTime;
            else if (Time.unscaledTime - leftTriggerHeldSince >= TriggerHoldSeconds)
            {
                leftTriggerFired = true;
                leftTriggerHeldSince = float.PositiveInfinity; // don't fire again until released
            }
        }
        else
        {
            leftTriggerHeldSince = -1f;
        }

        // Z / right controller trigger runs the original, unchanged DE search;
        // M / left controller trigger runs the new DIRECT algorithm -- separate
        // keys/triggers instead of one plus an Inspector toggle, so either can be
        // fired directly without checking/flipping a switch first.
        bool runDE = zPressed || rightTriggerFired;
        bool runDirect = mPressed || leftTriggerFired;

        if ((runDE || runDirect) && !isRunning)
        {
            Debug.Log($"[LocalOptimizationRunner] {(runDirect ? "M/left trigger held" : "Z/right trigger held")} -- starting Unity-only optimization (no Python).");
            string inputDescription = mPressed ? "M key" : zPressed ? "Z key" : leftTriggerFired ? "left trigger" : "right trigger";
            BroadcastTriggerNotification(runDirect, inputDescription);
            RunOptimizationAndApply(runDirect);
        }
        else if ((runDE || runDirect) && isRunning)
        {
            Debug.LogWarning("[LocalOptimizationRunner] Optimization key pressed, but a run is already in progress -- ignoring.");
        }
    }

    [ContextMenu("Run Optimization And Apply (DE)")]
    void RunOptimizationAndApplyMenuDE() => RunOptimizationAndApply(useDirectAlgorithm: false);

    [ContextMenu("Run Optimization And Apply (DIRECT)")]
    void RunOptimizationAndApplyMenuDirect() => RunOptimizationAndApply(useDirectAlgorithm: true);

    public async void RunOptimizationAndApply(bool useDirectAlgorithm)
    {
        if (isRunning)
        {
            Debug.LogWarning("[LocalOptimizationRunner] Already running, ignoring this call.");
            return;
        }
        isRunning = true;
        // Measured from the moment Z/trigger fires to the moment this whole run (including
        // house reset, input building, and applying the result -- not just the search
        // itself) finishes, so a straight "how long did that take" comparison between the
        // two algorithms doesn't need to account for anything else separately. Always
        // logged in the `finally` below, success or failure, per request.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Always re-read the live house index from SceneSelection right before a run --
            // the Inspector field is only a fallback for solo testing without full scene
            // wiring (see ResolveMyType). Every client must use ITS OWN correct index both
            // for this local run and for applying results broadcast from other clients.
            myType = ResolveMyType();

            DisablePanningViewIfActive();

            Debug.Log("[LocalOptimizationRunner] Stage 1/6: resetting houses to origin...");
            ResetHousesToOrigin();

            Debug.Log("[LocalOptimizationRunner] Stage 2/6: building freespace/boundary/ROI inputs...");
            var (freespaces, boundaries, rois, originalLocalCentroids) = BuildOptimizationInputs();

            // Run the CPU-heavy DE search (~4500+ evaluations) on a background thread
            // instead of blocking the main thread for 10-20+ seconds. DifferentialEvolutionOptimizer
            // and everything it calls (PolygonUtils/CircleShape/ObjectiveFunction/NetTopologySuite)
            // touch no UnityEngine API, so this is safe off-thread. Blocking the main
            // thread this long was silently killing the Fusion connection (no keep-alive
            // could be sent/received in time), which is what made LocalAvatar disappear
            // every time this ran -- confirmed 2026-08-14 by correlating Editor.log
            // timestamps: the connection state flipped to Disconnected immediately after
            // "Optimization done", with no other trigger in between.
            string algorithmName = useDirectAlgorithm ? "DIRECT" : "DE";
            Debug.Log($"[LocalOptimizationRunner] Stage 3/6: running {algorithmName} search on background thread (this is the ~1min+ part)...");
            var optResult = useDirectAlgorithm
                ? await System.Threading.Tasks.Task.Run(() =>
                    DirectOptimizer.Optimize(
                        freespaces, boundaries, rois, ObjectiveFunction.ObjectiveParams.Default,
                        maxEvaluations: 3000, translationBound: 5.0, rotationBound: 30.0))
                : await System.Threading.Tasks.Task.Run(() =>
                    DifferentialEvolutionOptimizer.Optimize(
                        freespaces, boundaries, rois, ObjectiveFunction.ObjectiveParams.Default,
                        maxIter: 50, popSizeMultiplier: 15, translationBound: 5.0, rotationBound: 30.0));

            Debug.Log($"[LocalOptimizationRunner] Stage 4/6: {algorithmName} search done, applying house/avatar placements...");

            // Disabling panning back at the top of this method only stops it ONCE, at
            // the instant Z/M fired -- the search above just spent anywhere from ~10s to
            // over a minute on a background thread, during which every other
            // MonoBehaviour's Update() (including CameraController's own panning-toggle
            // check) kept running normally every frame. If panning got toggled back on
            // at any point during that wait, nothing turned it back off again before the
            // result below gets applied. Re-disable it here too, right as the result is
            // actually about to become visible, so the final state doesn't depend on
            // what happened to be pressed during the wait.
            DisablePanningViewIfActive();

            ApplyHousePlacements(optResult, originalLocalCentroids);
            ApplyAvatarPositions(optResult, originalLocalCentroids);
            BuildSelectedZones(optResult, originalLocalCentroids);

            // RemoteAvatar/RemoteAvatar1's position is continuously re-synced by Fusion to
            // that person's own real, live tracked position -- the one-time
            // ApplyAvatarPositions() assignment above only sticks for a single frame before
            // the next network tick snaps it back to their raw position, which is why the
            // avatar visibly ends up away from its ROI/boundary circle instead of centered
            // in it. SceneSelection.LateUpdate()'s existing "OPTIMIZATION OFFSET" system
            // solves exactly this (re-derives the correct view position every frame from
            // whatever raw position the network just wrote), but until now only the old
            // Y-key path (Arrange_WalkIn.cs) turned it on. Reuse the same mechanism here.
            EnableRemoteAvatarRetargeting();

            Debug.Log("[LocalOptimizationRunner] Stage 5/6: drawing traverse zone / boundary / ROI outlines...");

            // The green Neighborhood-boundary outline (paper Fig 1(d)/Fig 2(c)/Sec 5.1:
            // "Green lines indicate the safe traversable boundary... the green outline
            // is fixed and delineates the extent of local objects within the
            // overlapping region") is a real, required feature, not debug-only --
            // draw it, but keep it a single simple traverse-zone ring, not the
            // per-furniture-hole freespace outlines (that was over-drawing; the
            // paper only calls for one boundary line, not one per cutout).
            // TEMP: debug outlines hidden for the 2-headset test (they render in the
            // actual headset view too, no layer separates them from the spectator
            // camera -- re-enable visibly once a proper hide-in-headset setup exists).
            //
            // DrawTraverseZoneForMe still has to run (not just commented out) --
            // LocalROI.cs uses GameObject.Find("traverseZone") as its only signal for
            // "has optimization run yet"; skipping this call entirely left that check
            // permanently false, which is what made LocalROI's own circle render
            // permanently, before any run and regardless of Play state.
            DrawTraverseZoneForMe(optResult, originalLocalCentroids, visible: false);
            DrawBoundaryCirclesForMe(optResult, originalLocalCentroids); // bigger (1.2m) circle outline -- re-enabled per request
            DrawROICirclesForMe(optResult, originalLocalCentroids); // 개인 공간 원 outline -- re-enabled per request

            // The DE search itself only ever runs HERE, on whichever computer pressed
            // Z/trigger -- send the finished result to every other connected client so
            // their houses/avatars/boundary+ROI circles end up in the identical
            // arrangement, instead of each machine trying to (and never being asked to)
            // run its own separate optimization.
            BroadcastOptimizationResult(optResult, originalLocalCentroids);

            Debug.Log("[LocalOptimizationRunner] Stage 6/6: done.");
        }
        finally
        {
            stopwatch.Stop();
            string algorithmLabel = useDirectAlgorithm ? "DIRECT" : "DE";
            Debug.Log($"[LocalOptimizationRunner] ({algorithmLabel}: {stopwatch.Elapsed.TotalSeconds:F2}s)");
            isRunning = false;
        }
    }

    // Same as Arrange_WalkIn.cs's Y-key branch ("Reset all houses to default pos + rot
    // first") -- so that reading each house's avatar/character position in
    // BuildOptimizationInputs() gives a value relative to that house's own
    // un-transformed local frame, not wherever a PREVIOUS run happened to leave it.
    //
    // That comment states the intent, but only ever held for houses[i] itself --
    // Characters[i] (the dummy avatar stand-in BuildOptimizationInputs falls
    // back to whenever house i has neither a local nor a remote avatar) is a
    // static, one-time value written by the PREVIOUS run's ApplyAvatarPositions
    // as a WORLD position (house i's own optimized rotation+translation baked
    // in). A real avatar (local or remote) doesn't have this problem -- it's
    // continuously live-tracked in absolute space regardless of any house
    // transform -- but Characters[i] has no such live backing, so re-reading
    // its raw value after houses reset to identity silently reinterpreted a
    // WORLD position as house i's own LOCAL one. Confirmed live: the
    // avatar/house mismatch grew with each successive run. Convert it into
    // house i's CURRENT (about-to-be-reset) local space FIRST, so the same
    // real position it already represents survives the reset intact instead
    // of being fixed to a stale default -- required per spec: after moving in
    // distance-maintain mode, an avatar must stay exactly where it moved to,
    // in its own house's terms.
    void ResetHousesToOrigin()
    {
        if (arrangeWalkin == null) return;

        var characters = GameObject.Find("Characters");
        for (int i = 0; i < arrangeWalkin.houses.Count; i++)
        {
            if (characters != null && i < characters.transform.childCount)
            {
                var c = characters.transform.GetChild(i);
                c.position = arrangeWalkin.houses[i].transform.InverseTransformPoint(c.position);
            }

            arrangeWalkin.houses[i].transform.position = Vector3.zero;
            arrangeWalkin.houses[i].transform.rotation = Quaternion.identity;
        }
    }

    // Per-house freespace/boundary/ROI, each centered on that house's own avatar
    // position -- exactly like Python's real X-key flow (received_userpos_data centers
    // the circle on wherever the user actually stood), NOT a fixed dummy point. For my
    // own house, LocalAvatar's real tracked position is the source of truth; for a house
    // with a real connected remote player, RemoteAvatar/RemoteAvatar1's own current
    // position (Joint Chest, same as LocalAvatar) is the source of truth -- the
    // "Characters" placeholder is only a last-resort stand-in for a house nobody is
    // actually connected to (it never tracks a live position on its own, see
    // Arrange_Walkin.Start()/ApplyAvatarPositions -- it only gets written to by a
    // previous optimization run applying its own result). Using it for a house that DOES
    // have a real remote user was centering that house's ROI/boundary on stale/default
    // data instead of where that player actually stood.
    (Polygon[] freespaces, CircleShape[] boundaries, CircleShape[] rois, CoordinateTransform.Point2D[] originalLocalCentroids) BuildOptimizationInputs()
    {
        GameObject localAvatarForInput = GameObject.Find("LocalAvatar");
        GameObject remoteAvatarForInput = GameObject.Find("RemoteAvatar");
        GameObject remoteAvatar1ForInput = GameObject.Find("RemoteAvatar1");
        GameObject charactersForInput = GameObject.Find("Characters");
        ResolveRemoteIndices(myType, out int remoteIndexForInput, out int remote1IndexForInput);

        var freespaces = new Polygon[NumHouses];
        var boundaries = new CircleShape[NumHouses];
        var rois = new CircleShape[NumHouses];
        var originalLocalCentroids = new CoordinateTransform.Point2D[NumHouses];

        for (int i = 0; i < NumHouses; i++)
        {
            double posX = 0, posZ = 0;
            GameObject avatarForPos = null;
            if (i == myType) avatarForPos = localAvatarForInput;
            else if (i == remoteIndexForInput) avatarForPos = remoteAvatarForInput;
            else if (i == remote1IndexForInput) avatarForPos = remoteAvatar1ForInput;

            if (avatarForPos != null)
            {
                // Avatar root transform doesn't reflect real head/positional tracking --
                // Meta Avatar SDK re-derives the root from the tracked rig's pose, not
                // from where the player has actually walked to. SceneSelection.cs's X-key
                // handler and CameraController.cs both read the Joint Chest/Joint Head
                // bone instead for exactly this reason; do the same here so the
                // boundary/ROI circles are centered on where the avatar is actually
                // standing, for every house that has a real avatar, not just my own.
                Transform jointChest = AvatarJointHelper.FindJointChest(avatarForPos.transform);
                Vector3 avatarPos = jointChest != null ? jointChest.position : avatarForPos.transform.position;
                posX = avatarPos.x;
                posZ = avatarPos.z;
            }
            else if (charactersForInput != null && i < charactersForInput.transform.childCount)
            {
                var c = charactersForInput.transform.GetChild(i);
                posX = c.position.x;
                posZ = c.position.z;
            }

            HouseData house = HouseLoader.LoadHouseByIndex(i);
            Polygon avatarBoundary = PolygonUtils.CreateCircle(posX, posZ, 1.2);
            FreespaceCalculator.Result fsResult = FreespaceCalculator.Compute(house, RoomId, avatarBoundary);

            // FreespaceCalculator re-centers everything so the FREESPACE's own centroid
            // lands at (0,0) -- the boundary/ROI circles must be built from that SAME
            // re-centered frame (fsResult.Boundary), not a fresh CircleShape at the raw
            // (posX, posZ) avatar coordinates, or freespace and boundary end up in two
            // different, misaligned coordinate frames (freespace centered near its own
            // centroid, boundary centered wherever the avatar raw-world position happens
            // to be) and every distance/overlap calculation in the objective function
            // becomes meaningless. This bug pre-dates the avatar-position fix above --
            // with the old dummy (0,0) it was invisible by coincidence.
            var recenteredBoundaryCentroid = fsResult.Boundary.Centroid;
            double rcx = recenteredBoundaryCentroid.X;
            double rcy = recenteredBoundaryCentroid.Y;

            freespaces[i] = fsResult.Freespace;
            boundaries[i] = new CircleShape(rcx, rcy, 1.2);
            rois[i] = new CircleShape(rcx, rcy, 0.5); // 50cm ROI radius, per the paper (was incorrectly 0.6 here)
            originalLocalCentroids[i] = new CoordinateTransform.Point2D(fsResult.OriginalCentroidX, fsResult.OriginalCentroidY);
        }

        return (freespaces, boundaries, rois, originalLocalCentroids);
    }

    // Which color a house's outlines (boundary circle + ROI circle) get, by
    // absolute house index: house0 (mine, in the normal myType=0 solo-testing
    // setup) stays cyan, house1 is red, house2 is blue -- matches how the two
    // other houses/avatars are meant to be told apart at a glance.
    static Color HouseOutlineColor(int i)
    {
        if (i == 1) return Color.red;
        if (i == 2) return Color.blue;
        return Color.cyan;
    }

    const float HouseOutlineWidth = 0.035f; // thicker than the default 0.015f line width, per user request

    void DrawBoundaryCirclesForMe(DifferentialEvolutionOptimizer.Result optResult, CoordinateTransform.Point2D[] originalLocalCentroids)
    {
        foreach (GameObject obj in GameObject.FindObjectsOfType<GameObject>())
        {
            if (obj.name.Contains("boundaryCircle")) DestroyImmediate(obj);
        }

        var myFrame = CoordinateTransform.GetAbsoluteFrames(optResult)[myType];
        var myLocalCentroid = originalLocalCentroids[myType];

        for (int i = 0; i < NumHouses; i++)
        {
            var centerWorld = HouseArrangementApplier.TransformPointToMyView(
                new CoordinateTransform.Point2D(optResult.FinalBoundaries[i].CenterX, optResult.FinalBoundaries[i].CenterY),
                myFrame, myLocalCentroid);

            // Points stored LOCAL to the circle's own center (not world-space) so the
            // whole ring can be moved just by moving circleObj.transform -- see
            // CircleFollowAvatar, which re-centers it on the avatar every frame.
            Coordinate[] ring = optResult.FinalBoundaries[i].ToPolygon().ExteriorRing.Coordinates;
            var localPoints = new Vector3[ring.Length];
            for (int k = 0; k < ring.Length; k++)
            {
                var p = new CoordinateTransform.Point2D(ring[k].X, ring[k].Y);
                var worldP = HouseArrangementApplier.TransformPointToMyView(p, myFrame, myLocalCentroid);
                localPoints[k] = new Vector3((float)(worldP.X - centerWorld.X), 0f, (float)(worldP.Y - centerWorld.Y));
            }

            var circleObj = new GameObject(i == myType ? "boundaryCircle_mine_" + i : "boundaryCircle_" + i);
            circleObj.transform.position = new Vector3((float)centerWorld.X, 0.16f, (float)centerWorld.Y);
            DrawZone(circleObj, localPoints, HouseOutlineColor(i), useWorldSpace: false);
            AttachCircleFollow(circleObj, i, 0.16f);
        }
    }

    // Same idea as DrawBoundaryCirclesForMe, but for the smaller 0.5m ROI circle
    // nested inside each house's boundary circle -- was computed all along
    // (optResult.FinalRois) but never actually drawn anywhere.
    void DrawROICirclesForMe(DifferentialEvolutionOptimizer.Result optResult, CoordinateTransform.Point2D[] originalLocalCentroids)
    {
        foreach (GameObject obj in GameObject.FindObjectsOfType<GameObject>())
        {
            if (obj.name.Contains("roiCircle")) DestroyImmediate(obj);
        }

        var myFrame = CoordinateTransform.GetAbsoluteFrames(optResult)[myType];
        var myLocalCentroid = originalLocalCentroids[myType];

        for (int i = 0; i < NumHouses; i++)
        {
            var centerWorld = HouseArrangementApplier.TransformPointToMyView(
                new CoordinateTransform.Point2D(optResult.FinalRois[i].CenterX, optResult.FinalRois[i].CenterY),
                myFrame, myLocalCentroid);

            Coordinate[] ring = optResult.FinalRois[i].ToPolygon().ExteriorRing.Coordinates;
            var localPoints = new Vector3[ring.Length];
            for (int k = 0; k < ring.Length; k++)
            {
                var p = new CoordinateTransform.Point2D(ring[k].X, ring[k].Y);
                var worldP = HouseArrangementApplier.TransformPointToMyView(p, myFrame, myLocalCentroid);
                localPoints[k] = new Vector3((float)(worldP.X - centerWorld.X), 0f, (float)(worldP.Y - centerWorld.Y));
            }

            var circleObj = new GameObject(i == myType ? "roiCircle_mine_" + i : "roiCircle_" + i);
            // slightly above the boundary circle (0.16f) so the two don't z-fight
            circleObj.transform.position = new Vector3((float)centerWorld.X, 0.17f, (float)centerWorld.Y);
            DrawZone(circleObj, localPoints, HouseOutlineColor(i), useWorldSpace: false);
            AttachCircleFollow(circleObj, i, 0.17f);
        }
    }

    // Resolves which avatar (root transform) currently represents house i from
    // this client's perspective -- same lookup convention used throughout this
    // file (BuildOptimizationInputs/ApplyAvatarPositions): LocalAvatar for my own
    // house, RemoteAvatar/RemoteAvatar1 for a connected other house, or the
    // Characters placeholder (which doesn't move on its own) for an empty one.
    Transform ResolveAvatarTransformForHouse(int i)
    {
        if (i == myType)
        {
            var local = GameObject.Find("LocalAvatar");
            if (local != null) return local.transform;
        }
        else
        {
            ResolveRemoteIndices(myType, out int remoteIndex, out int remote1Index);
            if (i == remoteIndex)
            {
                var remote = GameObject.Find("RemoteAvatar");
                if (remote != null) return remote.transform;
            }
            else if (i == remote1Index)
            {
                var remote1 = GameObject.Find("RemoteAvatar1");
                if (remote1 != null) return remote1.transform;
            }
        }

        // Falls through here whenever house i has no real avatar spawned (every
        // index always matches myType/remoteIndex/remote1Index for NumHouses==3,
        // so this used to be unreachable dead code -- the early `return null`
        // above skipped straight past it instead of ever landing here). Characters[i]
        // is already moved to this exact house's boundary/ROI center by
        // ApplyAvatarPositions() on every optimization run, so it doubles as a
        // stand-in "avatar" position for an empty house.
        var characters = GameObject.Find("Characters");
        return characters != null && i < characters.transform.childCount ? characters.transform.GetChild(i) : null;
    }

    void AttachCircleFollow(GameObject circleObj, int houseIndex, float height)
    {
        var avatarTransform = ResolveAvatarTransformForHouse(houseIndex);
        if (avatarTransform == null) return;

        var follow = circleObj.AddComponent<CircleFollowAvatar>();
        follow.target = avatarTransform;
        follow.height = height;
        follow.houseIndex = houseIndex;
        follow.isMine = (houseIndex == myType);
    }

    // Feeds Arrange_Walkin.selectedZones -- each house's boundary-circle outline,
    // in my local view. Regions.cs reads this (via Arrange_Walkin) to build the
    // per-house zone shader inputs (zone1Vec4/zone2Vec4). Was missing before,
    // same category of bug as the SetUserPositionDirect omission.
    void BuildSelectedZones(DifferentialEvolutionOptimizer.Result optResult, CoordinateTransform.Point2D[] originalLocalCentroids)
    {
        if (arrangeWalkin == null) return;

        var myFrame = CoordinateTransform.GetAbsoluteFrames(optResult)[myType];
        var myLocalCentroid = originalLocalCentroids[myType];

        arrangeWalkin.selectedZones.Clear();
        for (int i = 0; i < NumHouses; i++)
        {
            var zonePoints = new List<Vector3>();
            Coordinate[] ring = optResult.FinalBoundaries[i].ToPolygon().ExteriorRing.Coordinates;
            foreach (var c in ring)
            {
                var p = new CoordinateTransform.Point2D(c.X, c.Y);
                var worldP = HouseArrangementApplier.TransformPointToMyView(p, myFrame, myLocalCentroid);
                zonePoints.Add(new Vector3((float)worldP.X, 0f, (float)worldP.Y));
            }
            arrangeWalkin.selectedZones.Add(zonePoints);
        }
    }

    void ApplyHousePlacements(DifferentialEvolutionOptimizer.Result optResult, CoordinateTransform.Point2D[] originalLocalCentroids)
    {
        if (arrangeWalkin == null)
        {
            Debug.LogWarning("[LocalOptimizationRunner] No Arrange_Walkin assigned, skipping house placement.");
            return;
        }

        var placements = HouseArrangementApplier.ComputeHousePlacements(optResult, originalLocalCentroids, myType);

        for (int i = 0; i < NumHouses && i < arrangeWalkin.houses.Count; i++)
        {
            var pos = placements[i].Position;
            arrangeWalkin.houses[i].transform.position = new Vector3((float)pos.X, 0f, (float)pos.Y);
            arrangeWalkin.houses[i].transform.rotation = Quaternion.Euler(0, (float)placements[i].RotationDeg, 0);
        }
    }

    void ApplyAvatarPositions(DifferentialEvolutionOptimizer.Result optResult, CoordinateTransform.Point2D[] originalLocalCentroids)
    {
        GameObject localAvatar = GameObject.Find("LocalAvatar");
        GameObject remoteAvatar = GameObject.Find("RemoteAvatar");
        GameObject remoteAvatar1 = GameObject.Find("RemoteAvatar1");
        GameObject characters = GameObject.Find("Characters");

        ResolveRemoteIndices(myType, out int remoteIndex, out int remote1Index);

        for (int i = 0; i < NumHouses; i++)
        {
            var avatarPos = HouseArrangementApplier.ComputeAvatarPosition(optResult, originalLocalCentroids, myType, i);
            var worldPos = new Vector3((float)avatarPos.X, 0f, (float)avatarPos.Y);

            if (i == myType)
            {
                // Capture where the avatar actually is BEFORE anything below moves it --
                // this must reflect the player's real, currently-tracked position (the
                // chest joint, not the root -- see BuildOptimizationInputs), since it's
                // used below to compute how far the rig needs to shift.
                Transform jointChestBefore = localAvatar != null ? AvatarJointHelper.FindJointChest(localAvatar.transform) : null;
                Vector3 avatarBefore = jointChestBefore != null ? jointChestBefore.position
                    : (localAvatar != null ? localAvatar.transform.position : worldPos);

                if (localAvatar != null) localAvatar.transform.position = worldPos;

                // LocalAvatar.transform.position alone doesn't stick -- Meta Avatar SDK
                // re-derives the avatar's root position from the OVRCameraRig's tracked
                // pose every frame, so a couple frames after this the avatar snaps back
                // to wherever the rig sits. Move the rig itself too so the visible avatar
                // body/camera actually stays at the circle center instead of drifting back.
                //
                // BUT: assigning cameraRig.transform.position = worldPos outright was a bug --
                // worldPos is the avatar's ABSOLUTE target position, while the avatar's actual
                // displayed position is rig.position PLUS however far the player has physically
                // walked within the tracked play space. Setting the rig straight to worldPos
                // left that walked offset still added on top, so the avatar ended up at
                // worldPos + offset -- overshooting outside the house by exactly however far
                // the player had walked before pressing Z. Move the rig by the DELTA between
                // worldPos and the avatar's pre-move position instead, so the existing offset
                // is preserved rather than double-counted.
                // X/Z only -- worldPos.y is always 0 (this whole pipeline is 2D,
                // ground-plane only, see DifferentialEvolutionOptimizer.cs), so
                // including Y in the delta would drag the rig down by however high
                // the chest joint currently sits (dumping the avatar into the floor).
                var cameraRig = GameObject.FindObjectOfType<OVRCameraRig>();
                if (cameraRig != null)
                {
                    Vector3 delta = worldPos - avatarBefore;
                    delta.y = 0f;
                    cameraRig.transform.position += delta;
                }
            }
            else
            {
                if (i == remoteIndex && remoteAvatar != null) remoteAvatar.transform.position = worldPos;
                else if (i == remote1Index && remoteAvatar1 != null) remoteAvatar1.transform.position = worldPos;

                if (characters != null && i < characters.transform.childCount)
                    characters.transform.GetChild(i).transform.position = worldPos;
            }

            // feeds the shader-driven per-house zone visualization (Regions.cs) --
            // was missing before, which left it rendering with stale/default data
            if (regions != null)
            {
                regions.SetUserPositionDirect(i, worldPos);
            }
            else
            {
                Debug.LogWarning("[LocalOptimizationRunner] No Regions assigned -- the colored zone visualization won't update.");
            }
        }
    }

    void DrawTraverseZoneForMe(DifferentialEvolutionOptimizer.Result optResult, CoordinateTransform.Point2D[] originalLocalCentroids, bool visible = true)
    {
        foreach (GameObject obj in GameObject.FindObjectsOfType<GameObject>())
        {
            if (obj.name.Contains("traverseZone")) DestroyImmediate(obj);
        }

        var zones = TraverseZoneCalculator.Compute(optResult);
        var myFrame = CoordinateTransform.GetAbsoluteFrames(optResult)[myType];
        var myLocalCentroid = originalLocalCentroids[myType];
        var myZoneRings = zones[myType];

        for (int j = 0; j < myZoneRings.Count; j++)
        {
            string name = myZoneRings.Count > 1 ? $"traverseZone_{j}" : "traverseZone";
            var zoneObj = new GameObject(name);

            Coordinate[] ring = myZoneRings[j];
            var points = new Vector3[ring.Length];
            for (int k = 0; k < ring.Length; k++)
            {
                var p = new CoordinateTransform.Point2D(ring[k].X, ring[k].Y);
                var worldP = HouseArrangementApplier.TransformPointToMyView(p, myFrame, myLocalCentroid);
                points[k] = new Vector3((float)worldP.X, 0.15f, (float)worldP.Y);
            }

            DrawZone(zoneObj, points, visible: visible);
        }
    }

    void DrawZone(GameObject obj, Vector3[] points, Color color = default, float width = 0.015f, bool visible = true, bool useWorldSpace = true)
    {
        if (color == default) color = Color.green;

        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.enabled = visible;
        lr.useWorldSpace = useWorldSpace;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = color;
        lr.endColor = color;
        lr.widthMultiplier = width;
        lr.positionCount = points.Length;
        lr.SetPositions(points);
    }

    // ── 네트워크 동기화 (Z키 최적화 결과 브로드캐스트) ──────────────────────────
    // 역할: DE 탐색(RunOptimizationAndApply)은 Z/trigger를 누른 컴퓨터에서만 수행하고,
    //       그 최종 결과(house/avatar 배치, boundary/ROI 원)만 다른 모든 클라이언트로
    //       전송해서 똑같이 적용한다. 각자 다시 최적화를 돌리게 하면, 아직 다른 집의
    //       실시간 위치를 못 받아온 클라이언트는 입력 자체가 서로 달라 결과가 갈릴 수
    //       있고, 랜덤 탐색이라 완전히 같은 결과가 보장되지도 않는다.
    // 의존: TransferManager (Arrange_Walkin.transfer) 의 RPC_BroadcastZOptResult
    // ─────────────────────────────────────────────────────────────────────────

    const int NumMovingHouses = NumHouses - 1;

    // originalLocalCentroids(하우스당 2) + freespace centroid(하우스당 2)
    // + boundary(하우스당 3) + roi(하우스당 3) + moving-house 회전각(하우스-1개)
    const int PayloadDoubleCount = NumHouses * 2 + NumHouses * 2 + NumHouses * 3 + NumHouses * 3 + NumMovingHouses;

    /// <summary>
    /// 방금 로컬에서 계산한 최적화 결과를 TransferManager를 통해 다른 모든 클라이언트에 전송합니다.
    /// RunOptimizationAndApply()가 로컬 적용을 마친 직후 호출됩니다.
    /// </summary>
    void BroadcastOptimizationResult(DifferentialEvolutionOptimizer.Result optResult, CoordinateTransform.Point2D[] originalLocalCentroids)
    {
        if (arrangeWalkin == null || arrangeWalkin.transfer == null)
        {
            Debug.LogWarning("[LocalOptimizationRunner] No transfer(TransferManager) reference -- cannot broadcast optimization result to other clients.");
            return;
        }

        var transferManager = arrangeWalkin.transfer.GetComponent<TransferManager>();
        if (transferManager == null)
        {
            Debug.LogWarning("[LocalOptimizationRunner] transfer GameObject has no TransferManager component -- cannot broadcast.");
            return;
        }

        // Solo testing (no Fusion session, or connected alone with nobody else to sync
        // to) must NOT throw here -- the local result above is already fully applied
        // regardless, so a skipped broadcast is not a functional loss, just a no-op.
        // TransferManager.Object/Runner are null until the NetworkObject is actually
        // spawned; calling an [Rpc] method before/without that would throw inside
        // Fusion's own RPC dispatch, which (RunOptimizationAndApply being `async void`)
        // would surface as an uncatchable top-level exception instead of a clean log line.
        if (transferManager.Object == null || transferManager.Runner == null || !transferManager.Runner.IsRunning)
        {
            return;
        }

        try
        {
            string payload = SerializeOptimizationResult(optResult, originalLocalCentroids);
            transferManager.RPC_BroadcastZOptResult(payload);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LocalOptimizationRunner] Failed to broadcast optimization result to other clients (local result is still applied): {e}");
        }
    }

    // Result/originalLocalCentroids를 콤마로 구분된 double 목록 하나로 직렬화합니다.
    // 순서는 ApplyReceivedOptimizationResult의 파싱 순서와 반드시 일치해야 합니다.
    // 문화권(culture)에 따라 소수점 구분자가 달라질 수 있으므로 InvariantCulture 고정.
    string SerializeOptimizationResult(DifferentialEvolutionOptimizer.Result optResult, CoordinateTransform.Point2D[] originalLocalCentroids)
    {
        var values = new double[PayloadDoubleCount];
        int idx = 0;

        for (int i = 0; i < NumHouses; i++)
        {
            values[idx++] = originalLocalCentroids[i].X;
            values[idx++] = originalLocalCentroids[i].Y;
        }
        for (int i = 0; i < NumHouses; i++)
        {
            var c = optResult.FinalFreespaces[i].Centroid;
            values[idx++] = c.X;
            values[idx++] = c.Y;
        }
        for (int i = 0; i < NumHouses; i++)
        {
            values[idx++] = optResult.FinalBoundaries[i].CenterX;
            values[idx++] = optResult.FinalBoundaries[i].CenterY;
            values[idx++] = optResult.FinalBoundaries[i].Radius;
        }
        for (int i = 0; i < NumHouses; i++)
        {
            values[idx++] = optResult.FinalRois[i].CenterX;
            values[idx++] = optResult.FinalRois[i].CenterY;
            values[idx++] = optResult.FinalRois[i].Radius;
        }
        for (int i = 0; i < NumMovingHouses; i++)
        {
            values[idx++] = optResult.MovingStates[i].AngleDeg;
        }

        var parts = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
            parts[i] = values[i].ToString("G17", CultureInfo.InvariantCulture);
        return string.Join(",", parts);
    }

    /// <summary>
    /// 다른 클라이언트가 브로드캐스트한 최적화 결과를 받아 이 클라이언트에도 똑같이 적용합니다.
    /// TransferManager.RPC_SendZOptChunk가 모든 청크를 받으면 호출합니다.
    /// FinalFreespaces는 이 경로 아래에서 오직 ".Centroid"로만 쓰이므로(CoordinateTransform.
    /// GetAbsoluteFrames 참고), 실제 freespace 폴리곤 전체 대신 그 중심점 위에 만든 자리표시용
    /// 원으로 대체합니다.
    /// </summary>
    public void ApplyReceivedOptimizationResult(string payload)
    {
        if (isRunning)
        {
            Debug.LogWarning("[LocalOptimizationRunner] Received a synced optimization result while a local run is in progress -- ignoring to avoid a race.");
            return;
        }

        isRunning = true;
        try
        {
            myType = ResolveMyType();
            DisablePanningViewIfActive();

            double[] values;
            try
            {
                values = payload.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LocalOptimizationRunner] Failed to parse received optimization payload: {e}");
                return;
            }

            if (values.Length != PayloadDoubleCount)
            {
                Debug.LogError($"[LocalOptimizationRunner] Received optimization payload has {values.Length} values, expected {PayloadDoubleCount} -- ignoring.");
                return;
            }

            int idx = 0;
            var originalLocalCentroids = new CoordinateTransform.Point2D[NumHouses];
            for (int i = 0; i < NumHouses; i++)
                originalLocalCentroids[i] = new CoordinateTransform.Point2D(values[idx++], values[idx++]);

            // The sender computed originalLocalCentroids[myType] from our avatar as seen
            // remotely (NetworkTransform) -- which can differ from our LocalAvatar's actual
            // chest position. Since this centroid is the translation offset used in
            // TransformPointToMyView for EVERY house's worldPos, even a small error shifts
            // ALL avatars/circles off-center (confirmed: all avatars thrown outside ROI on
            // house2 when receiving house0's result). Override with a fresh local computation
            // using our own live tracking so worldPos values are in our correct local space.
            try
            {
                originalLocalCentroids[myType] = RecomputeMyLocalCentroid();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[LocalOptimizationRunner] Could not re-derive own local centroid: {e.Message}. Using broadcast value -- avatars may be offset.");
            }

            var freespaces = new Polygon[NumHouses];
            for (int i = 0; i < NumHouses; i++)
            {
                double cx = values[idx++], cy = values[idx++];
                freespaces[i] = PolygonUtils.CreateCircle(cx, cy, 1.2); // 자리표시용 -- 위 XML 주석 참고
            }

            var boundaries = new CircleShape[NumHouses];
            for (int i = 0; i < NumHouses; i++)
                boundaries[i] = new CircleShape(values[idx++], values[idx++], values[idx++]);

            var rois = new CircleShape[NumHouses];
            for (int i = 0; i < NumHouses; i++)
                rois[i] = new CircleShape(values[idx++], values[idx++], values[idx++]);

            var movingStates = new DifferentialEvolutionOptimizer.TransformState[NumMovingHouses];
            for (int i = 0; i < NumMovingHouses; i++)
                movingStates[i] = new DifferentialEvolutionOptimizer.TransformState { Dx = 0, Dy = 0, AngleDeg = values[idx++] };

            var optResult = new DifferentialEvolutionOptimizer.Result
            {
                BestLoss = 0.0,
                MovingStates = movingStates,
                FinalFreespaces = freespaces,
                FinalBoundaries = boundaries,
                FinalRois = rois,
                LossHistory = new List<double>()
            };

            ResetHousesToOrigin();
            ApplyHousePlacements(optResult, originalLocalCentroids);
            ApplyAvatarPositions(optResult, originalLocalCentroids);
            BuildSelectedZones(optResult, originalLocalCentroids);
            EnableRemoteAvatarRetargeting(); // see RunOptimizationAndApply's own call for why this is needed
            DrawBoundaryCirclesForMe(optResult, originalLocalCentroids);
            DrawROICirclesForMe(optResult, originalLocalCentroids);
            MarkTraverseZoneRan();
        }
        finally
        {
            isRunning = false;
        }
    }

    // LocalROI.cs는 GameObject.Find("traverseZone")의 존재 여부를 "최적화가 한 번이라도
    // 실행됐는가"의 유일한 신호로 사용합니다(LocalROI.cs 자체 주석 참고). 이 동기화 경로는
    // 진짜 traverse-zone 외곽선을 계산할 freespace 폴리곤 원본이 없으므로(위 XML 주석 참고),
    // 같은 이름의 마커 오브젝트만 남겨 그 신호를 동일하게 재현합니다 -- 어차피 실제 외곽선도
    // 이 기능에서는 항상 invisible로 그려져(RunOptimizationAndApply의 visible:false) 지금까지
    // 화면에 보인 적이 없습니다.
    void MarkTraverseZoneRan()
    {
        foreach (GameObject obj in GameObject.FindObjectsOfType<GameObject>())
        {
            if (obj.name.Contains("traverseZone")) DestroyImmediate(obj);
        }
        new GameObject("traverseZone");
    }

    // 임시 Inspector 필드(myType) 대신, SceneSelection.type(Fusion join order로
    // HouseJoinOrderAssigner가 한 번 정해줌)에서 이 클라이언트의 실제 house 번호를 읽습니다.
    // arrangeWalkin/sceneSelection 참조가 없는 솔로 테스트 환경에서는 Inspector 값으로 폴백합니다.
    int ResolveMyType()
    {
        if (arrangeWalkin != null && arrangeWalkin.sceneSelection != null)
        {
            var sceneSel = arrangeWalkin.sceneSelection.GetComponent<SceneSelection>();
            if (sceneSel != null) return sceneSel.type;
        }
        return myType;
    }

    // Re-derive originalLocalCentroids[myType] from our own LocalAvatar's live chest
    // position. Used in ApplyReceivedOptimizationResult to replace the broadcast value
    // (which was computed by the sender using our avatar's networked position and may
    // differ from our local tracking).
    CoordinateTransform.Point2D RecomputeMyLocalCentroid()
    {
        double posX = 0, posZ = 0;
        var localAvatar = GameObject.Find("LocalAvatar");
        if (localAvatar != null)
        {
            Transform jointChest = AvatarJointHelper.FindJointChest(localAvatar.transform);
            Vector3 avatarPos = jointChest != null ? jointChest.position : localAvatar.transform.position;
            posX = avatarPos.x;
            posZ = avatarPos.z;
        }
        HouseData house = HouseLoader.LoadHouseByIndex(myType);
        var avatarBoundary = PolygonUtils.CreateCircle(posX, posZ, 1.2);
        var fsResult = FreespaceCalculator.Compute(house, RoomId, avatarBoundary);
        return new CoordinateTransform.Point2D(fsResult.OriginalCentroidX, fsResult.OriginalCentroidY);
    }

    // A Z/M optimization result only matters from the normal avatar viewpoint --
    // called at the start of ApplyReceivedOptimizationResult (result received from
    // another client, applied synchronously start-to-finish, only one call needed)
    // and TWICE in RunOptimizationAndApply (local run): once at the very start for
    // immediate feedback, and again right before the result is applied, since the
    // background search in between can take over a minute, long enough for panning
    // to get toggled back on mid-wait with nothing to catch it otherwise.
    void DisablePanningViewIfActive()
    {
        // Z/M 결과는 최적화된 실제 위치 기준으로 봐야 하므로, 거리유지모드(패닝모드)를 끈다.
        var distanceMode = FindFirstObjectByType<DistanceMaintainMode>();
        if (distanceMode != null) distanceMode.Disable();
    }

    // Broadcasts "houseN pressed <input> (DE|DIRECT optimization)" to every
    // connected headset's HeadsetHUD (including this one's own), called right as
    // the key/trigger fires, before the (possibly ~1min+) optimization run itself
    // starts. Falls back to showing it only locally when not connected to a
    // running Fusion session (solo/offline testing) -- same guard
    // BroadcastOptimizationResult already uses for the same reason.
    void BroadcastTriggerNotification(bool isDirect, string inputDescription)
    {
        int houseIndex = ResolveMyType();
        string message = $"house{houseIndex} pressed {inputDescription} ({(isDirect ? "DIRECT" : "DE")} optimization)";

        var transferManager = arrangeWalkin != null && arrangeWalkin.transfer != null
            ? arrangeWalkin.transfer.GetComponent<TransferManager>()
            : null;

        bool networked = transferManager != null
            && transferManager.Object != null
            && transferManager.Runner != null
            && transferManager.Runner.IsRunning;

        if (networked)
        {
            transferManager.RPC_BroadcastTriggerNotification(message);
        }
        else if (HeadsetHUD.Instance != null)
        {
            HeadsetHUD.Instance.ShowNotification(message);
        }
    }

    // 원격 아바타(RemoteAvatar/RemoteAvatar1)는 Fusion이 매 프레임 그 사람의 실제 물리적
    // 위치로 계속 동기화하므로, ApplyAvatarPositions()의 한 번짜리 position 대입은 다음
    // 네트워크 틱에 바로 원래 위치로 되돌아간다. SceneSelection.LateUpdate()의 "OPTIMIZATION
    // OFFSET" 시스템(houses[i].transform을 매 프레임 참조해서 원격 아바타 위치를 다시 계산)이
    // 정확히 이 문제를 해결하기 위한 것이지만, 지금까지는 구 Y키 경로(Arrange_WalkIn.cs)에서만
    // 켜졌다. Z키 경로가 이미 갱신해놓은 houses[i].transform을 그대로 재사용할 수 있으므로,
    // 여기서도 같은 시스템을 켠다.
    void EnableRemoteAvatarRetargeting()
    {
        if (arrangeWalkin == null || arrangeWalkin.sceneSelection == null)
        {
            Debug.LogWarning("[LocalOptimizationRunner] No sceneSelection reference -- cannot enable continuous remote-avatar retargeting (remote avatars may not stay centered in their ROI).");
            return;
        }

        var sceneSel = arrangeWalkin.sceneSelection.GetComponent<SceneSelection>();
        if (sceneSel == null)
        {
            Debug.LogWarning("[LocalOptimizationRunner] sceneSelection GameObject has no SceneSelection component -- cannot enable continuous remote-avatar retargeting.");
            return;
        }

        if (OffsetCalculator.Instance == null)
        {
            Debug.LogWarning("[LocalOptimizationRunner] No OffsetCalculator.Instance -- cannot enable continuous remote-avatar retargeting.");
            return;
        }

        sceneSel.SetupRotationAwareTransform(OffsetCalculator.Instance, null);
    }

    // Same remote-avatar-index mapping used throughout the project (Arrange_Walkin.cs,
    // TransferManager.cs, OffsetCalculator.cs, SceneSelection.cs's updateavatar()):
    // RemoteAvatar1 = the other house with the lower index, RemoteAvatar = the higher one.
    // Shared by BuildOptimizationInputs() and ApplyAvatarPositions() so both agree on
    // which house each remote avatar object represents.
    static void ResolveRemoteIndices(int forType, out int remoteIndex, out int remote1Index)
    {
        switch (forType)
        {
            case 0: remoteIndex = 2; remote1Index = 1; break;
            case 1: remoteIndex = 2; remote1Index = 0; break;
            default: remoteIndex = 1; remote1Index = 0; break;
        }
    }
}
