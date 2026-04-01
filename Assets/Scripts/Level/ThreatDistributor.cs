using UnityEngine;
using System.Collections.Generic;

public static class ThreatDistributor
{
    public static List<Vector2> ApplyPerlinNoiseDistribution(Map map, List<Vector2i> candidateTiles, AdversarialDirector director, float spatialNoiseScale, float trapDensityThreshold)
    {
        List<Vector2> trapPositions = new List<Vector2>();

        foreach (Vector2i tile in candidateTiles)
        {
            float noiseVal = Mathf.PerlinNoise(tile.x * spatialNoiseScale, tile.y * spatialNoiseScale);

            if (noiseVal > trapDensityThreshold)
            {
                Vector2 worldPos = map.GetMapTilePosition(tile.x, tile.y);
                trapPositions.Add(worldPos);
                map.SetTile(tile.x, tile.y, TileType.Danger);

                GameObject trapToSpawn = map.spikePrefab;

                if (director != null && director.trapLibrary.Count > 0)
                {
                    float totalWeight = 0f;
                    foreach (var config in director.trapLibrary) totalWeight += config.weight;
                    float r = Random.Range(0, totalWeight);
                    float currentWeight = 0f;

                    foreach (var config in director.trapLibrary)
                    {
                        currentWeight += config.weight;
                        if (r <= currentWeight)
                        {
                            if (config.prefab != null) trapToSpawn = config.prefab;
                            break;
                        }
                    }
                }

                if (trapToSpawn != null)
                {
                    GameObject instantiatedTrap = Object.Instantiate(trapToSpawn, new Vector3(worldPos.x, worldPos.y, -5f), Quaternion.identity);
                    SmartTrap smartTrapComponent = instantiatedTrap.GetComponent<SmartTrap>();
                    if (smartTrapComponent != null)
                    {
                        smartTrapComponent.ActivateTrap(worldPos, 0, null);
                    }
                }
            }
        }

        return trapPositions;
    }
}