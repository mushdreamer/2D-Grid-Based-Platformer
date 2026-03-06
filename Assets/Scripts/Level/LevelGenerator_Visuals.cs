using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 负责处理前端划线、停顿、可视化展示的分部类
public partial class LevelGenerator : MonoBehaviour
{
    [Header("Visualization Settings (可视化设置)")]
    public bool enableSearchVisuals = true;  // 是否在屏幕上显示红色的失败探测触须
    public float successDisplayTime = 0.15f; // 成功路线在屏幕上的绿色展示停留时间
    public float searchDisplayTime = 0.02f;  // 红色探测触须的闪烁停留时间

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
            map.guideLineRenderer.startColor = new Color(1f, 0f, 0f, 0.4f); // 半透明红色表示失败或挣扎
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