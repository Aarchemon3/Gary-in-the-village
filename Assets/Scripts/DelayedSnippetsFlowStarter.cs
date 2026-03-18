using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class DelayedSnippetsFlowStarter : MonoBehaviour
{
    public SnippetsFlowController flow;
    public float delaySeconds = 2f;

    void Start()
    {
        if (flow != null && delaySeconds > 0f)
            StartCoroutine(BeginAfterDelay());
    }

    IEnumerator BeginAfterDelay()
    {
        yield return new WaitForSeconds(delaySeconds);

        if (flow != null)
            flow.Play();
    }
}
