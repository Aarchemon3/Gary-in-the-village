using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[DefaultExecutionOrder(5000)]
public class SnippetsSceneGroundSnap : MonoBehaviour
{
    public SnippetsWalker walker;
    public float yOffset = -0.035f;
    public float probeHeight = 40f;
    public float maxProbeDistance = 120f;
    public float searchRadius = 1.5f;
    public float smoothSpeed = 18f;
    public bool snapOnStart = true;

    readonly List<Renderer> _sceneRenderers = new List<Renderer>();
    readonly List<Renderer> _selfRenderers = new List<Renderer>();
    readonly List<Transform> _groundAnchors = new List<Transform>();
    float _stableBottomOffset;
    bool _wasWalking;

    void Awake()
    {
        CacheSelfRenderers();
        CacheScene();
    }

    void Start()
    {
        CacheStableBottomOffset();
        if (snapOnStart)
            SnapNow();
    }

    void LateUpdate()
    {
        var isWalking = IsWalking();
        if (isWalking)
        {
            _wasWalking = true;
            return;
        }

        if (_wasWalking)
        {
            SnapNow();
            _wasWalking = false;
        }
    }

    public void CacheScene()
    {
        CacheScene(gameObject.scene);
    }

    public void CacheScene(Scene scene)
    {
        _sceneRenderers.Clear();
        _groundAnchors.Clear();
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        foreach (var root in scene.GetRootGameObjects())
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var transform = transforms[i];
                if (IsGroundAnchor(transform))
                    _groundAnchors.Add(transform);
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled || _selfRenderers.Contains(renderer))
                    continue;

                _sceneRenderers.Add(renderer);
            }
        }
    }

    public void SnapNow()
    {
        if (!TryGetGroundY(transform.position, out var groundY))
            return;
        if (!TryGetVisualBottomY(out var visualBottomY))
            return;

        var position = transform.position;
        position.y += (groundY + yOffset) - visualBottomY;
        transform.position = position;
        CacheStableBottomOffset();
    }

    void CacheSelfRenderers()
    {
        _selfRenderers.Clear();
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                _selfRenderers.Add(renderers[i]);
        }
    }

    bool TryGetGroundY(Vector3 worldPosition, out float groundY)
    {
        var samplePoints = GetProbePoints(worldPosition);
        var hitGrounds = new List<float>(samplePoints.Count);

        for (var i = 0; i < samplePoints.Count; i++)
        {
            if (!TryRaycastGround(samplePoints[i], out var hitY))
                continue;

            hitGrounds.Add(hitY);
        }

        if (hitGrounds.Count > 0)
        {
            hitGrounds.Sort();
            groundY = hitGrounds[hitGrounds.Count / 2];
            return true;
        }

        var foundAnchor = false;
        var bestAnchorDistance = float.PositiveInfinity;
        var bestAnchorY = 0f;

        for (var i = 0; i < _groundAnchors.Count; i++)
        {
            var anchor = _groundAnchors[i];
            if (anchor == null)
                continue;

            var dx = worldPosition.x - anchor.position.x;
            var dz = worldPosition.z - anchor.position.z;
            var horizontalDistance = dx * dx + dz * dz;
            if (horizontalDistance >= bestAnchorDistance)
                continue;

            foundAnchor = true;
            bestAnchorDistance = horizontalDistance;
            bestAnchorY = anchor.position.y;
        }

        if (foundAnchor)
        {
            groundY = bestAnchorY;
            return true;
        }

        var found = false;
        var bestSupportY = float.NegativeInfinity;
        var bestSupportDistance = float.PositiveInfinity;

        for (var i = 0; i < _sceneRenderers.Count; i++)
        {
            var renderer = _sceneRenderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            var bounds = renderer.bounds;
            if (bounds.size == Vector3.zero)
                continue;

            var dx = Mathf.Max(0f, Mathf.Abs(worldPosition.x - bounds.center.x) - bounds.extents.x);
            var dz = Mathf.Max(0f, Mathf.Abs(worldPosition.z - bounds.center.z) - bounds.extents.z);
            var horizontalDistance = Mathf.Sqrt(dx * dx + dz * dz);
            if (horizontalDistance > searchRadius)
                continue;

            var candidateY = bounds.max.y;
            if (candidateY > worldPosition.y + probeHeight)
                continue;

            if (!found ||
                candidateY > bestSupportY + 0.01f ||
                (Mathf.Abs(candidateY - bestSupportY) <= 0.01f && horizontalDistance < bestSupportDistance))
            {
                found = true;
                bestSupportY = candidateY;
                bestSupportDistance = horizontalDistance;
            }
        }

        groundY = bestSupportY;
        return found;
    }

    bool TryGetVisualBottomY(out float visualBottomY)
    {
        var found = false;
        visualBottomY = float.PositiveInfinity;

        for (var i = 0; i < _selfRenderers.Count; i++)
        {
            var renderer = _selfRenderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            var minY = renderer.bounds.min.y;
            if (!found || minY < visualBottomY)
            {
                found = true;
                visualBottomY = minY;
            }
        }

        return found;
    }

    void CacheStableBottomOffset()
    {
        if (TryGetVisualBottomY(out var visualBottomY))
            _stableBottomOffset = transform.position.y - visualBottomY;
    }

    bool IsWalking()
    {
        return walker != null && walker.IsBusy;
    }

    List<Vector3> GetProbePoints(Vector3 worldPosition)
    {
        var points = new List<Vector3>(5) { worldPosition };
        if (!TryGetSelfBounds(out var bounds))
            return points;

        var probeExtentX = Mathf.Clamp(bounds.extents.x * 0.45f, 0.12f, 0.35f);
        var probeExtentZ = Mathf.Clamp(bounds.extents.z * 0.45f, 0.12f, 0.35f);
        points.Add(worldPosition + transform.forward * probeExtentZ);
        points.Add(worldPosition - transform.forward * probeExtentZ);
        points.Add(worldPosition + transform.right * probeExtentX);
        points.Add(worldPosition - transform.right * probeExtentX);
        return points;
    }

    bool TryGetSelfBounds(out Bounds bounds)
    {
        bounds = default;
        var found = false;
        for (var i = 0; i < _selfRenderers.Count; i++)
        {
            var renderer = _selfRenderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    bool TryRaycastGround(Vector3 worldPoint, out float hitY)
    {
        var rayOrigin = new Vector3(worldPoint.x, worldPoint.y + probeHeight, worldPoint.z);
        var hits = Physics.RaycastAll(rayOrigin, Vector3.down, maxProbeDistance, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length > 0)
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (var i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (IsGroundAnchorInHierarchy(hit.collider != null ? hit.collider.transform : null))
            {
                hitY = hit.point.y;
                return true;
            }
        }

        hitY = 0f;
        return false;
    }

    bool IsGroundAnchorInHierarchy(Transform transform)
    {
        while (transform != null)
        {
            if (IsGroundAnchor(transform))
                return true;
            transform = transform.parent;
        }

        return false;
    }

    static bool IsGroundAnchor(Transform transform)
    {
        if (transform == null)
            return false;

        var name = transform.name;
        return name.IndexOf("terrain", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("path", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
