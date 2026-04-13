using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public partial class LevelGenerator : MonoBehaviour
{
    [Header("Visualization Settings (¿ÉÊÓ»¯ÉèÖÃ)")]
    public bool enableSearchVisuals = true;
    public float successDisplayTime = 0.2f;
    public float searchDisplayTime = 0.05f;

    public IEnumerator ShowSuccessVisualsRoutine(List<Vector3> trajectory)
    {
        if (map.guideLineRenderer != null && trajectory != null && trajectory.Count > 0)
        {
            map.guideLineRenderer.startColor = Color.green;
            map.guideLineRenderer.endColor = Color.green;
            map.guideLineRenderer.positionCount = trajectory.Count;
            map.guideLineRenderer.SetPositions(trajectory.ToArray());
            map.guideLineRenderer.enabled = true;

            yield return new WaitForSeconds(successDisplayTime);
        }
    }

    public IEnumerator ShowSearchVisualsRoutine(List<Vector3> trajectory)
    {
        if (enableSearchVisuals && map.guideLineRenderer != null && trajectory != null && trajectory.Count > 0)
        {
            map.guideLineRenderer.startColor = new Color(1f, 0f, 0f, 0.4f);
            map.guideLineRenderer.endColor = new Color(1f, 0f, 0f, 0.4f);
            map.guideLineRenderer.positionCount = trajectory.Count;
            map.guideLineRenderer.SetPositions(trajectory.ToArray());
            map.guideLineRenderer.enabled = true;

            yield return new WaitForSeconds(searchDisplayTime);
            map.guideLineRenderer.enabled = false;
        }
        else
        {
            yield return null;
        }
    }

    public void ClearVisuals()
    {
        if (map.guideLineRenderer != null)
        {
            map.guideLineRenderer.enabled = false;
        }
    }
}