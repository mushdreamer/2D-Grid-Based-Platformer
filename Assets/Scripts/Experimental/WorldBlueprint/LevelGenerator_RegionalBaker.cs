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

        foreach (var ind in wovenPaths)
        {
            foreach (var p in ind.safePlatforms) pathPlatforms.Add(p);

            foreach (var point in ind.trajectory)
            {
                Vector2i centerTile = map.GetMapTileAtPoint(point);
                BlueprintNode node = blueprintPlanner.GetNodeAtTile(centerTile);

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

        if (map.survivalSpaceTiles != null)
        {
            foreach (var safeTile in map.survivalSpaceTiles) pathAirMask.Add(safeTile);
        }

        float globalSeed = Random.Range(0f, 1000f);

        for (int x = 0; x < map.mWidth; x++)
        {
            for (int y = 0; y < map.mHeight; y++)
            {
                Vector2i curTile = new Vector2i(x, y);
                BlueprintNode node = blueprintPlanner.GetNodeAtTile(curTile);
                if (node == null) continue;

                if (pathPlatforms.Contains(curTile)) { map.SetTile(x, y, TileType.Block); continue; }
                if (pathAirMask.Contains(curTile)) { map.SetTile(x, y, TileType.Empty); continue; }

                switch (node.envType)
                {
                    case EnvironmentType.SolidVoid:
                        map.SetTile(x, y, TileType.Block);
                        break;

                    case EnvironmentType.Cave:
                        float caveNoise = Mathf.PerlinNoise(x * 0.2f + globalSeed, y * 0.2f + globalSeed);
                        if (caveNoise > 0.3f) map.SetTile(x, y, TileType.Block);
                        else map.SetTile(x, y, TileType.Empty);
                        break;

                    case EnvironmentType.Shaft:
                        float distToCenterX = Mathf.Abs(x - (node.startTileX + node.endTileX) / 2f);
                        if (distToCenterX > (node.endTileX - node.startTileX) * 0.3f) map.SetTile(x, y, TileType.Block);
                        else map.SetTile(x, y, TileType.Empty);
                        break;

                    case EnvironmentType.Corridor:
                        float yRatio = (float)(y - node.startTileY) / (node.endTileY - node.startTileY);
                        if (yRatio < 0.2f || yRatio > 0.8f) map.SetTile(x, y, TileType.Block);
                        else map.SetTile(x, y, TileType.Empty);
                        break;
                }
            }
        }

        for (int x = 1; x < map.mWidth - 1; x++)
        {
            for (int y = 1; y < map.mHeight - 1; y++)
            {
                Vector2i curTile = new Vector2i(x, y);
                BlueprintNode node = blueprintPlanner.GetNodeAtTile(curTile);
                if (node == null) continue;

                if (node.envType == EnvironmentType.SurvivalZone) continue;

                bool inSurvivalSpace = map.survivalSpaceTiles != null &&
                                       (map.survivalSpaceTiles.Contains(curTile) ||
                                        map.survivalSpaceTiles.Contains(new Vector2i(x, y - 1)));
                if (inSurvivalSpace) continue;

                if (map.GetTile(x, y) == TileType.Empty && !pathAirMask.Contains(curTile))
                {
                    bool topBlock = map.GetTile(x, y + 1) == TileType.Block;
                    bool bottomBlock = map.GetTile(x, y - 1) == TileType.Block;

                    float spikeChance = 0f;
                    if (node.envType == EnvironmentType.SolidVoid) spikeChance = 1.0f;
                    else if (node.envType == EnvironmentType.Cave) spikeChance = 0.8f;
                    else if (node.envType == EnvironmentType.Corridor) spikeChance = 0.3f;

                    spikeChance += node.localDangerModifier;

                    if (Random.value < spikeChance)
                    {
                        if (topBlock) SpawnSpike(x, y, true);
                        else if (bottomBlock) SpawnSpike(x, y, false);
                    }
                }
            }
        }

        if (globalStart.x != -1) for (int dx = -2; dx <= 2; dx++) FillColumn(globalStart.x + dx, 0, globalStart.y - 1, TileType.Block);
        if (globalEnd.x != -1) for (int dx = -2; dx <= 2; dx++) FillColumn(globalEnd.x + dx, 0, globalEnd.y - 1, TileType.Block);

        Debug.Log("<color=green>[RegionalBaker] 多环境语义地形烘焙完毕，危险饱和度已覆写！</color>");
    }
}