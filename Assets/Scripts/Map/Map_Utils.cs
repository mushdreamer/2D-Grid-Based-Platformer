using UnityEngine;
using System.Collections;
using Algorithms;

public partial class Map
{
    public TileType GetTile(int x, int y)
    {
        if (x < 0 || x >= mWidth || y < 0 || y >= mHeight) return TileType.Block;
        return tiles[x, y];
    }

    public bool IsOneWayPlatform(int x, int y)
    {
        if (x < 0 || x >= mWidth || y < 0 || y >= mHeight) return false;
        return (tiles[x, y] == TileType.OneWay);
    }

    public bool IsGround(int x, int y)
    {
        if (x < 0 || x >= mWidth || y < 0 || y >= mHeight) return false;
        return (tiles[x, y] == TileType.OneWay || tiles[x, y] == TileType.Block);
    }

    public bool IsObstacle(int x, int y)
    {
        if (x < 0 || x >= mWidth || y < 0 || y >= mHeight) return true;
        return (tiles[x, y] == TileType.Block);
    }

    public bool IsNotEmpty(int x, int y)
    {
        if (x < 0 || x >= mWidth || y < 0 || y >= mHeight) return true;
        return (tiles[x, y] != TileType.Empty);
    }

    public void InitPathFinder()
    {
        mPathFinder = new PathFinderFast(mGrid, this);
        mPathFinder.Formula = HeuristicFormula.Manhattan;
        mPathFinder.Diagonals = false;
        mPathFinder.HeavyDiagonals = false;
        mPathFinder.HeuristicEstimate = 6;
        mPathFinder.PunishChangeDirection = false;
        mPathFinder.TieBreaker = false;
        mPathFinder.SearchLimit = 1000000;
        mPathFinder.DebugProgress = false;
        mPathFinder.DebugFoundPath = false;
    }

    public void GetMapTileAtPoint(Vector2 point, out int tileIndexX, out int tileIndexY)
    {
        tileIndexY = (int)((point.y - position.y + cTileSize / 2.0f) / (float)(cTileSize));
        tileIndexX = (int)((point.x - position.x + cTileSize / 2.0f) / (float)(cTileSize));
    }

    public Vector2i GetMapTileAtPoint(Vector2 point)
    {
        return new Vector2i((int)((point.x - position.x + cTileSize / 2.0f) / (float)(cTileSize)),
                    (int)((point.y - position.y + cTileSize / 2.0f) / (float)(cTileSize)));
    }

    public Vector2 GetMapTilePosition(int tileIndexX, int tileIndexY)
    {
        return new Vector2((float)(tileIndexX * cTileSize) + position.x, (float)(tileIndexY * cTileSize) + position.y);
    }

    public Vector2 GetMapTilePosition(Vector2i tileCoords)
    {
        return new Vector2((float)(tileCoords.x * cTileSize) + position.x, (float)(tileCoords.y * cTileSize) + position.y);
    }

    public bool CollidesWithMapTile(AABB aabb, int tileIndexX, int tileIndexY)
    {
        var tilePos = GetMapTilePosition(tileIndexX, tileIndexY);
        return aabb.Overlaps(tilePos, new Vector2((float)(cTileSize) / 2.0f, (float)(cTileSize) / 2.0f));
    }

    public bool AnySolidBlockInRectangle(Vector2 start, Vector2 end)
    {
        return AnySolidBlockInRectangle(GetMapTileAtPoint(start), GetMapTileAtPoint(end));
    }

    public bool AnySolidBlockInStripe(int x, int y0, int y1)
    {
        int startY, endY;
        if (y0 <= y1) { startY = y0; endY = y1; } else { startY = y1; endY = y0; }
        for (int y = startY; y <= endY; ++y)
        {
            if (GetTile(x, y) == TileType.Block) return true;
        }
        return false;
    }

    public bool AnySolidBlockInRectangle(Vector2i start, Vector2i end)
    {
        int startX, startY, endX, endY;
        if (start.x <= end.x) { startX = start.x; endX = end.x; } else { startX = end.x; endX = start.x; }
        if (start.y <= end.y) { startY = start.y; endY = end.y; } else { startY = end.y; endY = start.y; }

        for (int y = startY; y <= endY; ++y)
        {
            for (int x = startX; x <= endX; ++x)
            {
                if (GetTile(x, y) == TileType.Block) return true;
            }
        }
        return false;
    }
}