using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public partial class LevelGenerator : MonoBehaviour
{
    /// <summary>
    /// 接管 PathWeaver 的输出，进行全局基于语义区块的地形烘焙
    /// </summary>
    public void BakeWorldFromBlueprint(List<LevelIndividual> wovenPaths, Vector2i globalStart, Vector2i globalEnd)
    {
        map.ClearMapToEmpty();

        HashSet<Vector2i> pathPlatforms = new HashSet<Vector2i>();
        HashSet<Vector2i> pathAirMask = new HashSet<Vector2i>();

        // 1. 提取所有编织路径的“实体平台”与“活动气室(Air Mask)”
        foreach (var ind in wovenPaths)
        {
            foreach (var p in ind.safePlatforms) pathPlatforms.Add(p);

            foreach (var point in ind.trajectory)
            {
                Vector2i centerTile = map.GetMapTileAtPoint(point);
                BlueprintNode node = blueprintPlanner.GetNodeAtTile(centerTile);

                // 【核心细节】不同环境的气室大小不同。
                // 洞穴很压抑(padding=1)，走廊很开阔(padding=3)
                int paddingX = 2;
                int paddingY = 2;

                if (node != null)
                {
                    if (node.envType == EnvironmentType.Cave) { paddingX = 1; paddingY = 1; }
                    if (node.envType == EnvironmentType.Corridor) { paddingX = 3; paddingY = 2; }
                    if (node.envType == EnvironmentType.Shaft) { paddingX = 2; paddingY = 4; }
                }

                for (int dx = -paddingX; dx <= paddingX; dx++)
                {
                    for (int dy = -paddingY; dy <= paddingY; dy++)
                    {
                        pathAirMask.Add(new Vector2i(centerTile.x + dx, centerTile.y + dy));
                    }
                }
            }
        }

        // 保护设计师绘制的生存空间
        if (map.survivalSpaceTiles != null)
        {
            foreach (var safeTile in map.survivalSpaceTiles) pathAirMask.Add(safeTile);
        }

        // 2. 遍历全图，根据所属区块的环境语义生成地形
        float globalSeed = Random.Range(0f, 1000f);

        // [核心逻辑更新] 消除天空，实现全空间饱和烘焙
        for (int x = 0; x < map.mWidth; x++)
        {
            for (int y = 0; y < map.mHeight; y++)
            {
                Vector2i curTile = new Vector2i(x, y);
                BlueprintNode node = blueprintPlanner.GetNodeAtTile(curTile);
                if (node == null) continue;

                // 1. 绝对优先：如果是物理路径上的平台，必须是砖块
                if (pathPlatforms.Contains(curTile)) { map.SetTile(x, y, TileType.Block); continue; }

                // 2. 绝对优先：如果是玩家活动的“气室”或生存空间，必须挖空
                if (pathAirMask.Contains(curTile)) { map.SetTile(x, y, TileType.Empty); continue; }

                // 3. 语义填充逻辑：处理“非路径区域”
                switch (node.envType)
                {
                    case EnvironmentType.SolidVoid:
                        // 彻底封死天空和所有未定义区域
                        map.SetTile(x, y, TileType.Block);
                        break;

                    case EnvironmentType.Cave:
                        // 洞穴：只有狭窄的裂缝是空的，其余全是岩石
                        float caveNoise = Mathf.PerlinNoise(x * 0.2f + globalSeed, y * 0.2f + globalSeed);
                        // 显著提高密度，让洞穴看起来非常压抑
                        if (caveNoise > 0.3f) map.SetTile(x, y, TileType.Block);
                        else map.SetTile(x, y, TileType.Empty);
                        break;

                    case EnvironmentType.Shaft:
                        // 天井：四周封闭，中间由于路径挖掘已经空了，这里只需填充侧壁
                        // 只有靠近边缘的才填充，制造出深井感
                        float distToCenterX = Mathf.Abs(x - (node.startTileX + node.endTileX) / 2f);
                        if (distToCenterX > (node.endTileX - node.startTileX) * 0.3f) map.SetTile(x, y, TileType.Block);
                        else map.SetTile(x, y, TileType.Empty);
                        break;

                    case EnvironmentType.Corridor:
                        // 走廊：强制生成上下盖板，彻底封死上方天空
                        float yRatio = (float)(y - node.startTileY) / (node.endTileY - node.startTileY);
                        if (yRatio < 0.2f || yRatio > 0.8f) map.SetTile(x, y, TileType.Block);
                        else map.SetTile(x, y, TileType.Empty);
                        break;
                }
            }
        }

        // 4. 【威慑力后处理】遍历生成静态陷阱
        for (int x = 1; x < map.mWidth - 1; x++)
        {
            for (int y = 1; y < map.mHeight - 1; y++)
            {
                Vector2i curTile = new Vector2i(x, y);
                BlueprintNode node = blueprintPlanner.GetNodeAtTile(curTile);
                if (node == null) continue;

                // 安全区内绝对禁止生成野外刺
                if (node.envType == EnvironmentType.SurvivalZone) continue;

                // 设计师手绘的安全空间内也绝对禁止
                bool inSurvivalSpace = map.survivalSpaceTiles != null &&
                                       (map.survivalSpaceTiles.Contains(curTile) ||
                                        map.survivalSpaceTiles.Contains(new Vector2i(x, y - 1)));
                if (inSurvivalSpace) continue;

                if (map.GetTile(x, y) == TileType.Empty && !pathAirMask.Contains(curTile))
                {
                    bool topBlock = map.GetTile(x, y + 1) == TileType.Block;
                    bool bottomBlock = map.GetTile(x, y - 1) == TileType.Block;

                    // 不同的环境，刺的生成概率和分布位置不同
                    float spikeChance = 0f;
                    if (node.envType == EnvironmentType.SolidVoid) spikeChance = 1.0f; // 虚空边缘布满尖刺，断绝探索念想
                    else if (node.envType == EnvironmentType.Cave) spikeChance = 0.8f; // 洞穴里全是刺
                    else if (node.envType == EnvironmentType.Corridor) spikeChance = 0.3f; // 走廊偶尔有刺

                    // 局部危险度增幅
                    spikeChance += node.localDangerModifier;

                    if (Random.value < spikeChance)
                    {
                        if (topBlock) SpawnSpike(x, y, true); // 倒刺
                        else if (bottomBlock) SpawnSpike(x, y, false); // 地刺
                    }
                }
            }
        }

        // 5. 封死起点和终点下方
        if (globalStart.x != -1) for (int dx = -2; dx <= 2; dx++) FillColumn(globalStart.x + dx, 0, globalStart.y - 1, TileType.Block);
        if (globalEnd.x != -1) for (int dx = -2; dx <= 2; dx++) FillColumn(globalEnd.x + dx, 0, globalEnd.y - 1, TileType.Block);

        Debug.Log("<color=green>[RegionalBaker] 多环境语义地形烘焙完毕，危险饱和度已覆写！</color>");
    }
}