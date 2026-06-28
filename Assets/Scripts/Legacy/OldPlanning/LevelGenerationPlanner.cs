using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 关卡生成规划器：负责连接起点、生存空间和终点，形成生成引导线
/// </summary>
public class LevelGenerationPlanner
{
    [System.Serializable]
    public class GenerationStep
    {
        public string description;
        public Vector2 startPoint;
        public Vector2 endPoint;
        public SurvivalSpaceAnalyzer.SurvivalZone associatedZone; // 如果是区域内移动，记录对应区域
    }

    public List<GenerationStep> plannedRoute = new List<GenerationStep>();

    /// <summary>
    /// 根据当前地图状态，规划一条从起点经过所有生存空间到终点的路径
    /// </summary>
    public void PlanGlobalRoute(Map map, List<SurvivalSpaceAnalyzer.SurvivalZone> zones)
    {
        plannedRoute.Clear();

        if (map.startTile.x == -1 || map.endTile.x == -1)
        {
            Debug.LogError("[Planner] 地图缺少起点或终点，无法规划！");
            return;
        }

        Vector2 globalStart = map.GetMapTilePosition(map.startTile.x, map.startTile.y);
        Vector2 globalEnd = map.GetMapTilePosition(map.endTile.x, map.endTile.y);

        // 1. 对生存空间按设计师绘制顺序或坐标顺序进行排序（这里建议按 X 轴排序，形成自然的横向推进）
        var sortedZones = zones.OrderBy(z => z.center.x).ToList();

        Vector2 currentCursor = globalStart;

        // 2. 连接 起点 -> 第一个区域的入口
        foreach (var zone in sortedZones)
        {
            // 获取该区域的入口（最左侧点）和出口（最右侧点）
            var sortedTiles = zone.tiles.OrderBy(t => t.x).ToList();
            Vector2 zoneEntry = map.GetMapTilePosition(sortedTiles[0].x, sortedTiles[0].y);
            Vector2 zoneExit = map.GetMapTilePosition(sortedTiles.Last().x, sortedTiles.Last().y);

            // 规划：当前位置 -> 区域入口 (这是生成器需要填补的“危险区”)
            plannedRoute.Add(new GenerationStep
            {
                description = "Link to Zone",
                startPoint = currentCursor,
                endPoint = zoneEntry,
                associatedZone = null
            });

            // 规划：区域入口 -> 区域出口 (这是在生存空间内的“安全移动”)
            plannedRoute.Add(new GenerationStep
            {
                description = "Inside Safe Zone",
                startPoint = zoneEntry,
                endPoint = zoneExit,
                associatedZone = zone
            });

            currentCursor = zoneExit;
        }

        // 3. 最后连接 最后一个区域的出口 -> 终点
        plannedRoute.Add(new GenerationStep
        {
            description = "Final Stretch",
            startPoint = currentCursor,
            endPoint = globalEnd,
            associatedZone = null
        });

        Debug.Log($"[Planner] 规划完成，共分解为 {plannedRoute.Count} 个生成阶段。");
    }
}