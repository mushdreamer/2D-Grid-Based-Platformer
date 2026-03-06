using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 生存空间可视化工具：在 Scene 视图中展示系统对生存空间的理解
/// </summary>
public class SurvivalSpaceVisualizer : MonoBehaviour
{
    public Map targetMap;
    public bool showVisuals = true;

    [Header("样式设置")]
    public Color areaColor = new Color(0f, 1f, 1f, 0.3f);
    public Color flowLineColor = Color.yellow;
    public float labelHeightOffset = 1.5f;

    private void OnDrawGizmos()
    {
        if (!showVisuals || targetMap == null || targetMap.survivalSpaceTiles == null) return;

        List<SurvivalSpaceAnalyzer.SurvivalZone> zones = SurvivalSpaceAnalyzer.GetIdentifiedZones(targetMap);

        if (zones.Count == 0) return;

        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];

            Gizmos.color = areaColor;
            Vector3 center = new Vector3(zone.center.x, zone.center.y, -1f);
            Vector3 size = new Vector3(zone.bounds.width, zone.bounds.height, 0.1f);
            Gizmos.DrawCube(center, size);
            Gizmos.DrawWireCube(center, size);

            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.fontSize = 15;
            style.alignment = TextAnchor.MiddleCenter;

            // 直接读取 zone.geometryType
            string label = $"[Zone {i + 1}]\nType: {zone.geometryType.ToString()}\nTiles: {zone.tiles.Count}";
            UnityEditor.Handles.Label(center + Vector3.up * labelHeightOffset, label, style);

            var sortedTiles = zone.tiles.OrderBy(t => t.x).ToList();
            Vector2 entrance = targetMap.GetMapTilePosition(sortedTiles[0].x, sortedTiles[0].y);
            Vector2 exit = targetMap.GetMapTilePosition(sortedTiles.Last().x, sortedTiles.Last().y);

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(new Vector3(entrance.x, entrance.y, -2f), 0.3f);

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(new Vector3(exit.x, exit.y, -2f), 0.3f);

            Gizmos.color = flowLineColor;
            Gizmos.DrawLine(new Vector3(entrance.x, entrance.y, -1.5f), new Vector3(exit.x, exit.y, -1.5f));
        }

        if (zones.Count > 1)
        {
            Gizmos.color = Color.white;
            for (int i = 0; i < zones.Count - 1; i++)
            {
                DrawArrow(zones[i].center, zones[i + 1].center);
            }
        }
    }

    private void DrawArrow(Vector2 from, Vector2 to)
    {
        Vector3 direction = (Vector3)(to - from);
        if (direction.magnitude < 1f) return;

        Vector3 pos = (Vector3)from;
        Gizmos.DrawRay(pos, direction);

        Vector3 right = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 + 20, 0) * Vector3.forward;
        Vector3 left = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 - 20, 0) * Vector3.forward;
        Gizmos.DrawRay(pos + direction, right * 0.5f);
        Gizmos.DrawRay(pos + direction, left * 0.5f);
    }
}