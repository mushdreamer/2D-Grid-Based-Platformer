using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 专门负责生存空间（Survival Space）的识别与分析
/// </summary>
public static class SurvivalSpaceAnalyzer
{
    public enum ZoneGeometry
    {
        SmallSpot,          // 极小空间（不进行特殊限制）
        HorizontalCorridor, // 横向走廊（适合左右移动、远跳）
        VerticalShaft,      // 纵向天井（适合高跳、下落）
        SquareRoom,         // 方形房间（适合综合跳跃）
        OrganicShape        // 不规则形状
    }

    [System.Serializable]
    public class SurvivalZone
    {
        public int id;                          // 区域ID
        public List<Vector2i> tiles;            // 包含的所有瓦片坐标
        public Rect bounds;                     // 区域的包围盒
        public Vector2 center;                  // 区域中心点（世界坐标）
        public ZoneGeometry geometryType;       // 空间的几何类型

        public SurvivalZone(int id)
        {
            this.id = id;
            this.tiles = new List<Vector2i>();
        }

        public void CalculateMetrics(Map map)
        {
            if (tiles.Count == 0) return;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var tile in tiles)
            {
                Vector2 pos = map.GetMapTilePosition(tile.x, tile.y);
                if (pos.x < minX) minX = pos.x;
                if (pos.x > maxX) maxX = pos.x;
                if (pos.y < minY) minY = pos.y;
                if (pos.y > maxY) maxY = pos.y;
            }

            // 加上一个基础的瓦片尺寸，防止单排/单列区域出现宽度或高度为0的情况
            float width = (maxX - minX) + Map.cTileSize;
            float height = (maxY - minY) + Map.cTileSize;

            bounds = new Rect(minX, minY, width, height);
            center = bounds.center;

            // 自动计算并赋予几何类型
            geometryType = IdentifyGeometryType(width, height, tiles.Count);
        }

        private ZoneGeometry IdentifyGeometryType(float width, float height, int tileCount)
        {
            if (tileCount < 5) return ZoneGeometry.SmallSpot;

            float aspect = width / height;

            if (aspect > 2.5f) return ZoneGeometry.HorizontalCorridor;
            if (aspect < 0.4f) return ZoneGeometry.VerticalShaft;
            if (aspect >= 0.8f && aspect <= 1.2f) return ZoneGeometry.SquareRoom;

            return ZoneGeometry.OrganicShape;
        }
    }

    /// <summary>
    /// 将 Map 中散乱的 survivalSpaceTiles 识别为多个独立的 SurvivalZone
    /// </summary>
    public static List<SurvivalZone> GetIdentifiedZones(Map map)
    {
        List<SurvivalZone> zones = new List<SurvivalZone>();
        if (map.survivalSpaceTiles == null || map.survivalSpaceTiles.Count == 0)
            return zones;

        HashSet<Vector2i> unvisited = new HashSet<Vector2i>(map.survivalSpaceTiles);
        int zoneCounter = 0;

        // 使用 BFS 算法查找所有连通的分量
        while (unvisited.Count > 0)
        {
            Vector2i startTile = unvisited.First();
            SurvivalZone newZone = new SurvivalZone(zoneCounter++);

            Queue<Vector2i> queue = new Queue<Vector2i>();
            queue.Enqueue(startTile);
            unvisited.Remove(startTile);

            while (queue.Count > 0)
            {
                Vector2i current = queue.Dequeue();
                newZone.tiles.Add(current);

                // 检查上下左右四个邻居
                Vector2i[] neighbors = {
                    new Vector2i(current.x + 1, current.y),
                    new Vector2i(current.x - 1, current.y),
                    new Vector2i(current.x, current.y + 1),
                    new Vector2i(current.x, current.y - 1)
                };

                foreach (var neighbor in neighbors)
                {
                    if (unvisited.Contains(neighbor))
                    {
                        unvisited.Remove(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            newZone.CalculateMetrics(map);
            zones.Add(newZone);
        }

        //Debug.Log($"[SurvivalAnalyzer] 识别完成，共发现 {zones.Count} 个独立生存空间。");
        return zones;
    }
}