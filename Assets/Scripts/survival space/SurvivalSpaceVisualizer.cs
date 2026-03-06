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

        // 1. 获取识别后的生存空间列表
        // 注意：这里调用的是我们上一阶段创建的分析器
        List<SurvivalSpaceAnalyzer.SurvivalZone> zones = SurvivalSpaceAnalyzer.GetIdentifiedZones(targetMap);

        if (zones.Count == 0) return;

        // 2. 遍历每个生存空间进行可视化
        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];

            // 展示属性：生存空间辨认与几何范围 (对应需求 1 & 4)
            Gizmos.color = areaColor;
            Vector3 center = new Vector3(zone.center.x, zone.center.y, -1f);
            Vector3 size = new Vector3(zone.bounds.width, zone.bounds.height, 0.1f);
            Gizmos.DrawCube(center, size);
            Gizmos.DrawWireCube(center, size);

            // 展示属性：先后顺序 (对应需求 2)
            // 我们通过编号展示设计师绘制的先后次序
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.fontSize = 15;
            style.alignment = TextAnchor.MiddleCenter;

            string label = $"[Zone {i + 1}]\nType: {IdentifyGeometryType(zone)}\nTiles: {zone.tiles.Count}";
            UnityEditor.Handles.Label(center + Vector3.up * labelHeightOffset, label, style);

            // 展示属性：起点和终点 (对应需求 3)
            // 在这里我们定义：X坐标最小的点为入口，最大的点为出口
            var sortedTiles = zone.tiles.OrderBy(t => t.x).ToList();
            Vector2 entrance = targetMap.GetMapTilePosition(sortedTiles[0].x, sortedTiles[0].y);
            Vector2 exit = targetMap.GetMapTilePosition(sortedTiles.Last().x, sortedTiles.Last().y);

            Gizmos.color = Color.green; // 起点绿色
            Gizmos.DrawSphere(new Vector3(entrance.x, entrance.y, -2f), 0.3f);

            Gizmos.color = Color.red; // 终点红色
            Gizmos.DrawSphere(new Vector3(exit.x, exit.y, -2f), 0.3f);

            // 绘制区域内的流动矢量线（从起点指向终点）
            Gizmos.color = flowLineColor;
            Gizmos.DrawLine(new Vector3(entrance.x, entrance.y, -1.5f), new Vector3(exit.x, exit.y, -1.5f));
        }

        // 3. 绘制生存空间之间的先后顺序连线
        if (zones.Count > 1)
        {
            Gizmos.color = Color.white;
            for (int i = 0; i < zones.Count - 1; i++)
            {
                DrawArrow(zones[i].center, zones[i + 1].center);
            }
        }
    }

    /// <summary>
    /// 辨认生存空间的几何类型 (对应需求 4)
    /// </summary>
    private string IdentifyGeometryType(SurvivalSpaceAnalyzer.SurvivalZone zone)
    {
        float aspect = zone.bounds.width / zone.bounds.height;

        if (zone.tiles.Count < 5) return "Small Spot";
        if (aspect > 2.5f) return "Horizontal Corridor"; // 横向走廊
        if (aspect < 0.4f) return "Vertical Shaft";     // 纵轴天井
        if (aspect >= 0.8f && aspect <= 1.2f) return "Square Room"; // 方形房间

        return "Organic Shape"; // 不规则形状
    }

    private void DrawArrow(Vector2 from, Vector2 to)
    {
        Vector3 direction = (Vector3)(to - from);
        if (direction.magnitude < 1f) return;

        Vector3 pos = (Vector3)from;
        Gizmos.DrawRay(pos, direction);

        // 画一个小箭头
        Vector3 right = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 + 20, 0) * Vector3.forward;
        Vector3 left = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 - 20, 0) * Vector3.forward;
        Gizmos.DrawRay(pos + direction, right * 0.5f);
        Gizmos.DrawRay(pos + direction, left * 0.5f);
    }
}