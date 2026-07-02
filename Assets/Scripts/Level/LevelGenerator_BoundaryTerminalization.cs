using UnityEngine;
using System.Collections.Generic;

public partial class LevelGenerator : MonoBehaviour
{
    private const int OutsideBoundaryBandDepth = 3;

    private enum BoundaryTerminalRecipe
    {
        OutwardGap,
        SparseDangerLanding,
        BlockerLip
    }

    private void ApplyOutsideBoundaryBandTerminalization(List<Vector3> trajectory, HashSet<Vector2i> safePlatforms, Vector2i start, Vector2i end)
    {
        if (map == null || map.survivalSpaceTiles == null || map.survivalSpaceTiles.Count == 0) return;

        HashSet<Vector2i> protectedTiles = BuildBoundaryTerminalizationProtectedMask(trajectory, safePlatforms, start, end);
        List<KeyValuePair<Vector2i, Vector2i>> boundaryEdges = GetSurvivalBoundaryEdges();

        foreach (KeyValuePair<Vector2i, Vector2i> edge in boundaryEdges)
        {
            BoundaryTerminalRecipe recipe = SelectBoundaryRecipe(edge.Key, edge.Value);
            ApplyBoundaryRecipe(edge.Key, edge.Value, recipe, protectedTiles);
        }
    }

    private HashSet<Vector2i> BuildBoundaryTerminalizationProtectedMask(List<Vector3> trajectory, HashSet<Vector2i> safePlatforms, Vector2i start, Vector2i end)
    {
        HashSet<Vector2i> protectedTiles = new HashSet<Vector2i>();

        foreach (Vector2i tile in map.survivalSpaceTiles)
            protectedTiles.Add(tile);

        if (trajectory != null)
        {
            int padding = designerIntent.mechanicalComplexity > 0.7f ? 2 : 4;
            foreach (Vector3 point in trajectory)
            {
                Vector2i t = map.GetMapTileAtPoint(point);
                for (int dx = -padding; dx <= padding; dx++)
                    for (int dy = -padding; dy <= padding; dy++)
                        protectedTiles.Add(new Vector2i(t.x + dx, t.y + dy));
            }
        }

        if (safePlatforms != null)
        {
            foreach (Vector2i tile in safePlatforms)
                protectedTiles.Add(tile);
        }

        AddStartEndProtection(start, protectedTiles);
        AddStartEndProtection(end, protectedTiles);
        return protectedTiles;
    }

    private void AddStartEndProtection(Vector2i tile, HashSet<Vector2i> protectedTiles)
    {
        if (tile.x == -1) return;
        for (int dx = -2; dx <= 2; dx++)
            for (int y = 0; y <= tile.y; y++)
                protectedTiles.Add(new Vector2i(tile.x + dx, y));
    }

    private List<KeyValuePair<Vector2i, Vector2i>> GetSurvivalBoundaryEdges()
    {
        List<KeyValuePair<Vector2i, Vector2i>> edges = new List<KeyValuePair<Vector2i, Vector2i>>();
        Vector2i[] directions = {
            new Vector2i(-1, 0),
            new Vector2i(1, 0),
            new Vector2i(0, 1),
            new Vector2i(0, -1)
        };

        foreach (Vector2i tile in map.survivalSpaceTiles)
        {
            foreach (Vector2i direction in directions)
            {
                Vector2i neighbor = new Vector2i(tile.x + direction.x, tile.y + direction.y);
                if (!map.survivalSpaceTiles.Contains(neighbor))
                    edges.Add(new KeyValuePair<Vector2i, Vector2i>(tile, direction));
            }
        }

        return edges;
    }

    private BoundaryTerminalRecipe SelectBoundaryRecipe(Vector2i boundaryTile, Vector2i outwardDirection)
    {
        int hash = Mathf.Abs((boundaryTile.x * 73856093) ^ (boundaryTile.y * 19349663) ^ (outwardDirection.x * 83492791) ^ (outwardDirection.y * 297121507));
        int selector = hash % 3;
        if (outwardDirection.y < 0) selector = 0;

        if (selector == 0) return BoundaryTerminalRecipe.OutwardGap;
        if (selector == 1) return BoundaryTerminalRecipe.SparseDangerLanding;
        return BoundaryTerminalRecipe.BlockerLip;
    }

    private void ApplyBoundaryRecipe(Vector2i boundaryTile, Vector2i outwardDirection, BoundaryTerminalRecipe recipe, HashSet<Vector2i> protectedTiles)
    {
        switch (recipe)
        {
            case BoundaryTerminalRecipe.OutwardGap:
                ApplyOutwardGap(boundaryTile, outwardDirection, protectedTiles);
                break;
            case BoundaryTerminalRecipe.SparseDangerLanding:
                ApplySparseDangerLanding(boundaryTile, outwardDirection, protectedTiles);
                break;
            case BoundaryTerminalRecipe.BlockerLip:
                ApplyBlockerLip(boundaryTile, outwardDirection, protectedTiles);
                break;
        }
    }

    private void ApplyOutwardGap(Vector2i boundaryTile, Vector2i outwardDirection, HashSet<Vector2i> protectedTiles)
    {
        for (int step = 1; step <= OutsideBoundaryBandDepth; step++)
        {
            Vector2i tile = OffsetTile(boundaryTile, outwardDirection, step);
            TrySetOutsideBoundaryTile(tile, TileType.Empty, protectedTiles);

            if (outwardDirection.x != 0 || outwardDirection.y < 0)
            {
                Vector2i below = new Vector2i(tile.x, tile.y - 1);
                TrySetOutsideBoundaryTile(below, TileType.Empty, protectedTiles);
            }
        }
    }

    private void ApplySparseDangerLanding(Vector2i boundaryTile, Vector2i outwardDirection, HashSet<Vector2i> protectedTiles)
    {
        bool placedDanger = false;
        for (int step = 1; step <= OutsideBoundaryBandDepth; step++)
        {
            Vector2i tile = OffsetTile(boundaryTile, outwardDirection, step);
            if (!IsEditableOutsideBoundaryTile(tile, protectedTiles)) continue;

            Vector2i below = new Vector2i(tile.x, tile.y - 1);
            if (!placedDanger && map.GetTile(below.x, below.y) == TileType.Block)
            {
                map.SetTile(tile.x, tile.y, TileType.Danger);
                placedDanger = true;
            }
            else if (step > 1)
            {
                map.SetTile(tile.x, tile.y, TileType.Empty);
            }
        }

        if (!placedDanger)
        {
            Vector2i fallback = OffsetTile(boundaryTile, outwardDirection, OutsideBoundaryBandDepth);
            TrySetOutsideBoundaryTile(fallback, TileType.Danger, protectedTiles);
        }
    }

    private void ApplyBlockerLip(Vector2i boundaryTile, Vector2i outwardDirection, HashSet<Vector2i> protectedTiles)
    {
        if (outwardDirection.y < 0)
        {
            ApplyOutwardGap(boundaryTile, outwardDirection, protectedTiles);
            return;
        }

        Vector2i lip = OffsetTile(boundaryTile, outwardDirection, 1);
        TrySetOutsideBoundaryTile(lip, TileType.Block, protectedTiles);

        if (outwardDirection.x != 0)
        {
            Vector2i upperLip = new Vector2i(lip.x, lip.y + 1);
            TrySetOutsideBoundaryTile(upperLip, TileType.Block, protectedTiles);
        }
    }

    private Vector2i OffsetTile(Vector2i origin, Vector2i direction, int distance)
    {
        return new Vector2i(origin.x + direction.x * distance, origin.y + direction.y * distance);
    }

    private bool IsEditableOutsideBoundaryTile(Vector2i tile, HashSet<Vector2i> protectedTiles)
    {
        if (tile.x < 0 || tile.x >= map.mWidth || tile.y < 0 || tile.y >= map.mHeight) return false;
        if (protectedTiles.Contains(tile)) return false;
        if (map.survivalSpaceTiles.Contains(tile)) return false;
        return IsWithinOutsideBoundaryBand(tile);
    }

    private bool IsWithinOutsideBoundaryBand(Vector2i tile)
    {
        foreach (Vector2i survivalTile in map.survivalSpaceTiles)
        {
            int distance = Mathf.Abs(tile.x - survivalTile.x) + Mathf.Abs(tile.y - survivalTile.y);
            if (distance > 0 && distance <= OutsideBoundaryBandDepth) return true;
        }
        return false;
    }

    private void TrySetOutsideBoundaryTile(Vector2i tile, TileType type, HashSet<Vector2i> protectedTiles)
    {
        if (!IsEditableOutsideBoundaryTile(tile, protectedTiles)) return;
        map.SetTile(tile.x, tile.y, type);
    }
}
