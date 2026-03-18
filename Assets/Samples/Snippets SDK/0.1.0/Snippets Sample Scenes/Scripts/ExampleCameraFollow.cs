using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExampleCameraFollow : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform guide;
    [SerializeField] private Transform roomCenter;

    [Header("Offsets")]
    [SerializeField] private float backOffset = 2.0f;
    [SerializeField] private float sideOffset = 0.75f;
    [SerializeField] private float height = 1.6f;
    [SerializeField] private float lookAhead = 1.0f;
    [SerializeField] private float lookHeight = 1.4f;

    [Header("Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.25f;
    [SerializeField] private float rotationSmoothTime = 0.25f;

    private Vector3 positionVelocity;
    private Vector3 lastOutward = Vector3.forward;

    private void LateUpdate()
    {
        if (guide == null || roomCenter == null)
        {
            return;
        }

        Vector3 outward = guide.position - roomCenter.position;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
        {
            outward = lastOutward;
        }
        else
        {
            outward.Normalize();
            lastOutward = outward;
        }

        Vector3 right = Vector3.Cross(Vector3.up, outward);
        Vector3 desiredPosition = guide.position
            - outward * backOffset
            + right * sideOffset
            + Vector3.up * height;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref positionVelocity,
            positionSmoothTime);

        Vector3 lookTarget = guide.position + Vector3.up * lookHeight + outward * lookAhead;
        Vector3 lookDirection = lookTarget - transform.position;
        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            lookDirection = outward;
        }

        Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        float rotationSharpness = 1f / Mathf.Max(0.0001f, rotationSmoothTime);
        float t = 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, t);
    }
}
