using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class SnippetsNavMeshGroundDriver : MonoBehaviour
{
    public SnippetsWalker walker;
    public float yOffset = -0.035f;
    public float probeHeight = 40f;
    public float probeDistance = 120f;

    readonly List<Renderer> _renderers = new List<Renderer>();
    float _stableBottomOffset;
    NavMeshAgent _agent;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                _renderers.Add(renderers[i]);
        }
    }

    void Start()
    {
        CacheStableBottomOffset();
    }

    void LateUpdate()
    {
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
            return;
        if (walker == null || !walker.IsBusy)
            return;
        if (!TryGetGroundY(_agent.nextPosition, out var groundY))
            return;

        var next = _agent.nextPosition;
        next.y = groundY + _stableBottomOffset + yOffset;
        transform.position = next;
        _agent.nextPosition = next;
    }

    void CacheStableBottomOffset()
    {
        var found = false;
        var minY = float.PositiveInfinity;
        for (var i = 0; i < _renderers.Count; i++)
        {
            var renderer = _renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!found || renderer.bounds.min.y < minY)
            {
                found = true;
                minY = renderer.bounds.min.y;
            }
        }

        if (found)
            _stableBottomOffset = transform.position.y - minY;
    }

    bool TryGetGroundY(Vector3 worldPosition, out float groundY)
    {
        var rayOrigin = new Vector3(worldPosition.x, worldPosition.y + probeHeight, worldPosition.z);
        var hits = Physics.RaycastAll(rayOrigin, Vector3.down, probeDistance, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length > 0)
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (var i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (IsGroundAnchorInHierarchy(hit.collider != null ? hit.collider.transform : null))
            {
                groundY = hit.point.y;
                return true;
            }
        }

        groundY = 0f;
        return false;
    }

    static bool IsGroundAnchorInHierarchy(Transform transform)
    {
        while (transform != null)
        {
            var name = transform.name;
            if (name.IndexOf("terrain", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("path", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            transform = transform.parent;
        }

        return false;
    }
}
