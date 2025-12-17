using UnityEngine;
using System.Text;
using System.Collections.Generic;

public partial class Map
{
    private void HandleDrawingInput()
    {
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (scrollInput != 0f)
        {
            int oldBrushSize = brushSize;
            if (scrollInput > 0f) brushSize++;
            else if (scrollInput < 0f) brushSize--;
            brushSize = Mathf.Clamp(brushSize, 1, 10);
            if (oldBrushSize != brushSize) Debug.Log("笔刷大小调整为: " + brushSize);
        }

        Vector2 mousePos = Input.mousePosition;
        Vector2 cameraPos = Camera.main.transform.position;
        var mousePosInWorld = cameraPos + mousePos - new Vector2(gameCamera.pixelWidth / 2, gameCamera.pixelHeight / 2);
        int mouseTileX, mouseTileY;
        GetMapTileAtPoint(mousePosInWorld, out mouseTileX, out mouseTileY);

        UpdateBrushPreview(mouseTileX, mouseTileY);

        if (Input.GetKey(KeyCode.Mouse0))
        {
            for (int xOffset = 0; xOffset < brushSize; xOffset++)
            {
                for (int yOffset = 0; yOffset < brushSize; yOffset++)
                {
                    int currentX = mouseTileX + xOffset;
                    int currentY = mouseTileY + yOffset;
                    Vector2i targetCell = new Vector2i(currentX, currentY);

                    if (currentX >= 0 && currentX < mWidth && currentY >= 0 && currentY < mHeight)
                    {
                        ClearTileState(targetCell);
                        if (currentBrush == BrushType.StartPoint)
                        {
                            if (startTile.x != -1) ResetVisual(startTile.x, startTile.y);
                            startTile = targetCell;
                            SetVisual(currentX, currentY, Color.cyan);
                        }
                        else if (currentBrush == BrushType.EndPoint)
                        {
                            if (endTile.x != -1) ResetVisual(endTile.x, endTile.y);
                            endTile = targetCell;
                            SetVisual(currentX, currentY, Color.yellow);
                        }
                        else if (currentBrush == BrushType.Path)
                        {
                            playerSelectedPath.Add(targetCell);
                            SetVisual(currentX, currentY, new Color(0.5f, 1f, 0.5f, 0.5f));
                        }
                    }
                }
            }
        }

        if (Input.GetKey(KeyCode.Mouse1))
        {
            for (int xOffset = 0; xOffset < brushSize; xOffset++)
            {
                for (int yOffset = 0; yOffset < brushSize; yOffset++)
                {
                    int currentX = mouseTileX + xOffset;
                    int currentY = mouseTileY + yOffset;
                    Vector2i currentCell = new Vector2i(currentX, currentY);
                    bool removed = playerSelectedPath.Remove(currentCell);
                    if (startTile == currentCell) { startTile = new Vector2i(-1, -1); removed = true; }
                    if (endTile == currentCell) { endTile = new Vector2i(-1, -1); removed = true; }
                    if (removed) ResetVisual(currentX, currentY);
                }
            }
        }
    }

    private void HandlePlayingInput()
    {
        inputs[(int)KeyInput.GoRight] = Input.GetKey(goRightKey);
        inputs[(int)KeyInput.GoLeft] = Input.GetKey(goLeftKey);
        inputs[(int)KeyInput.GoDown] = Input.GetKey(goDownKey);
        inputs[(int)KeyInput.Jump] = Input.GetKey(goJumpKey);

        if (Input.GetKeyDown(KeyCode.Mouse0))
        {
            Vector2 mousePos = Input.mousePosition;
            Vector2 cameraPos = Camera.main.transform.position;
            var mousePosInWorld = cameraPos + mousePos - new Vector2(gameCamera.pixelWidth / 2, gameCamera.pixelHeight / 2);
            int mouseTileX, mouseTileY;
            GetMapTileAtPoint(mousePosInWorld, out mouseTileX, out mouseTileY);
            player.TappedOnTile(new Vector2i(mouseTileX, mouseTileY));
        }
    }

    private void ResetToDrawingMode()
    {
        playerSelectedPath.Clear();
        startTile = new Vector2i(-1, -1);
        endTile = new Vector2i(-1, -1);

        foreach (var spike in spawnedSpikes)
        {
            if (spike != null) Destroy(spike);
        }
        spawnedSpikes.Clear();

        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                tiles[x, y] = TileType.Empty;
                mGrid[x, y] = 1;
                ResetVisual(x, y);
            }
        }

        if (player != null && player.gameObject.activeSelf) player.gameObject.SetActive(false);
        if (brushPreviewInstance != null) brushPreviewInstance.SetActive(true);
        currentPhase = GamePhase.Drawing;
        Cursor.visible = false;
        Debug.Log("Reset to Drawing Mode. Draw your path and press Space.");
    }

    private void ReturnToDrawingMode()
    {
        if (player != null) player.gameObject.SetActive(false);

        foreach (var spike in spawnedSpikes)
        {
            if (spike != null) Destroy(spike);
        }
        spawnedSpikes.Clear();

        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentTile = new Vector2i(x, y);

                tiles[x, y] = TileType.Empty;
                mGrid[x, y] = 1;

                if (currentTile == startTile) SetVisual(x, y, Color.cyan);
                else if (currentTile == endTile) SetVisual(x, y, Color.yellow);
                else if (playerSelectedPath.Contains(currentTile)) SetVisual(x, y, new Color(0.5f, 1f, 0.5f, 0.5f));
                else ResetVisual(x, y);
            }
        }

        currentPhase = GamePhase.Drawing;
        Cursor.visible = false;
        if (brushPreviewInstance != null) brushPreviewInstance.SetActive(true);
        Debug.Log("Back to Drawing Mode.");
    }

    private void StartTrialMode()
    {
        if (startTile.x == -1 || endTile.x == -1) { Debug.LogError("无法开始：未设置起点或终点！"); return; }

        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                SetTile(x, y, TileType.Block);
                tilesSprites[x, y].color = Color.white;
            }
        }

        GenerateLevelFromTolerance();

        SetTile(startTile.x, startTile.y, TileType.Empty);
        SetTile(endTile.x, endTile.y, TileType.Empty);
        RemoveSpikeAt(startTile.x, startTile.y - 1);
        RemoveSpikeAt(endTile.x, endTile.y - 1);

        if (brushPreviewInstance != null) brushPreviewInstance.SetActive(false);
        Cursor.visible = true;
        player.gameObject.SetActive(true);
        player.BotInit(inputs, prevInputs);
        player.mMap = this;
        player.mPosition = GetMapTilePosition(startTile) + new Vector2(0, player.mAABB.HalfSizeY);
        currentPhase = GamePhase.TrialPlay;
        ScanLevelData();
        Debug.Log("Trial Mode Started.");
    }

    private void GenerateLevelFromTolerance()
    {
        if (spikePrefab == null) Debug.LogWarning("未设置 Spike Prefab！");

        int spikeHeightThreshold = 3;

        for (int x = 0; x < mWidth; x++)
        {
            bool hasPathInColumn = false;
            int lowestPathY = mHeight;
            int highestPathY = -1;

            for (int y = 0; y < mHeight; y++)
            {
                Vector2i pos = new Vector2i(x, y);
                if (playerSelectedPath.Contains(pos) || pos == startTile || pos == endTile)
                {
                    hasPathInColumn = true;
                    SetTile(x, y, TileType.Empty);

                    if (y < lowestPathY) lowestPathY = y;
                    if (y > highestPathY) highestPathY = y;
                }
            }

            if (hasPathInColumn)
            {
                int clearance = highestPathY - lowestPathY + 1;
                int floorY = lowestPathY - 1;

                if (floorY >= 0)
                {
                    // --- 修改：增加安全检查 ---
                    // 只有在 clearance 足够大，并且该列不是 AI 落地的地方时，才允许生成尖刺
                    bool isSafeLandingSpot = safeLandingColumns != null && safeLandingColumns.Contains(x);

                    if (clearance > spikeHeightThreshold && !isSafeLandingSpot)
                    {
                        SpawnSpikeAt(x, floorY);
                    }
                    else
                    {
                        if (GetTile(x, floorY) != TileType.Block)
                        {
                            SetTile(x, floorY, TileType.Block);
                        }
                    }
                }
            }
        }
    }

    private void SpawnSpikeAt(int x, int y)
    {
        if (spikePrefab == null) return;

        SetTile(x, y, TileType.Danger);
        tilesSprites[x, y].enabled = false;

        Vector2 worldPos = GetMapTilePosition(x, y);
        Vector3 spawnPos = new Vector3(worldPos.x, worldPos.y, -1f);

        GameObject newSpike = Instantiate(spikePrefab, spawnPos, Quaternion.identity);
        newSpike.transform.parent = transform;
        newSpike.transform.localScale = Vector3.one;

        spawnedSpikes.Add(newSpike);
    }

    private void RemoveSpikeAt(int x, int y)
    {
        if (x < 0 || x >= mWidth || y < 0 || y >= mHeight) return;

        if (GetTile(x, y) == TileType.Danger)
        {
            SetTile(x, y, TileType.Block);
            tilesSprites[x, y].enabled = true;
            tilesSprites[x, y].color = Color.white;
            tilesSprites[x, y].sprite = mDirtSprites[0];

            GameObject spikeToRemove = null;
            foreach (var spike in spawnedSpikes)
            {
                if (Vector2.Distance(spike.transform.position, GetMapTilePosition(x, y)) < 0.1f)
                {
                    spikeToRemove = spike;
                    break;
                }
            }

            if (spikeToRemove != null)
            {
                spawnedSpikes.Remove(spikeToRemove);
                Destroy(spikeToRemove);
            }
        }
    }

    public void SetTile(int x, int y, TileType type)
    {
        tiles[x, y] = type;
        if (type == TileType.Block)
        {
            mGrid[x, y] = 0;
            AutoTile(type, x, y, 1, 8, 4, 4, 4, 4);
            tilesSprites[x, y].enabled = true;
        }
        else if (type == TileType.OneWay)
        {
            mGrid[x, y] = 1;
            tilesSprites[x, y].enabled = true;
            tilesSprites[x, y].transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
            tilesSprites[x, y].transform.eulerAngles = new Vector3(0.0f, 0.0f, 0.0f);
            tilesSprites[x, y].sprite = mDirtSprites[25];
        }
        else if (type == TileType.Danger)
        {
            mGrid[x, y] = 1;
            tilesSprites[x, y].enabled = true;
            tilesSprites[x, y].sprite = mDirtSprites[0];
            tilesSprites[x, y].color = Color.red;
            tilesSprites[x, y].transform.localScale = Vector3.one;
            tilesSprites[x, y].transform.eulerAngles = Vector3.zero;
        }
        else
        {
            mGrid[x, y] = 1;
            tilesSprites[x, y].enabled = false;
        }
        AutoTile(type, x - 1, y, 1, 8, 4, 4, 4, 4);
        AutoTile(type, x + 1, y, 1, 8, 4, 4, 4, 4);
        AutoTile(type, x, y - 1, 1, 8, 4, 4, 4, 4);
        AutoTile(type, x, y + 1, 1, 8, 4, 4, 4, 4);
    }

    void AutoTile(TileType type, int x, int y, int rand4NeighbourTiles, int rand3NeighbourTiles, int rand2NeighbourPipeTiles, int rand2NeighbourCornerTiles, int rand1NeighbourTiles, int rand0NeighbourTiles)
    {
        if (x >= mWidth || x < 0 || y >= mHeight || y < 0) return;
        if (tiles[x, y] != TileType.Block) return;

        int tileOnLeft = (x > 0 && tiles[x - 1, y] == tiles[x, y]) ? 1 : 0;
        int tileOnRight = (x < mWidth - 1 && tiles[x + 1, y] == tiles[x, y]) ? 1 : 0;
        int tileOnTop = (y < mHeight - 1 && tiles[x, y + 1] == tiles[x, y]) ? 1 : 0;
        int tileOnBottom = (y > 0 && tiles[x, y - 1] == tiles[x, y]) ? 1 : 0;

        float scaleX = 1.0f;
        float scaleY = 1.0f;
        float rot = 0.0f;
        int id = 0;
        int sum = tileOnLeft + tileOnRight + tileOnTop + tileOnBottom;

        switch (sum)
        {
            case 0: id = 1 + mRandomNumber.Next(rand0NeighbourTiles); break;
            case 1:
                id = 1 + rand0NeighbourTiles + mRandomNumber.Next(rand1NeighbourTiles);
                if (tileOnRight == 1) scaleX = -1; else if (tileOnTop == 1) rot = -1; else if (tileOnBottom == 1) { rot = 1; scaleY = -1; }
                break;
            case 2:
                if (tileOnLeft + tileOnBottom == 2) id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + rand2NeighbourPipeTiles + mRandomNumber.Next(rand2NeighbourCornerTiles);
                else if (tileOnRight + tileOnBottom == 2) { id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + rand2NeighbourPipeTiles + mRandomNumber.Next(rand2NeighbourCornerTiles); scaleX = -1; }
                else if (tileOnTop + tileOnLeft == 2) { id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + rand2NeighbourPipeTiles + mRandomNumber.Next(rand2NeighbourCornerTiles); scaleY = -1; }
                else if (tileOnTop + tileOnRight == 2) { id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + rand2NeighbourPipeTiles + mRandomNumber.Next(rand2NeighbourCornerTiles); scaleX = -1; scaleY = -1; }
                else if (tileOnTop + tileOnBottom == 2) { id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + mRandomNumber.Next(rand2NeighbourPipeTiles); rot = 1; }
                else if (tileOnRight + tileOnLeft == 2) id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + mRandomNumber.Next(rand2NeighbourPipeTiles);
                break;
            case 3:
                id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + rand2NeighbourPipeTiles + rand2NeighbourCornerTiles + mRandomNumber.Next(rand3NeighbourTiles);
                if (tileOnLeft == 0) { rot = 1; scaleX = -1; } else if (tileOnRight == 0) { rot = 1; scaleY = -1; } else if (tileOnBottom == 0) scaleY = -1;
                break;
            case 4:
                id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + rand2NeighbourPipeTiles + rand2NeighbourCornerTiles + rand3NeighbourTiles + mRandomNumber.Next(rand4NeighbourTiles);
                break;
        }
        tilesSprites[x, y].transform.localScale = new Vector3(scaleX, scaleY, 1.0f);
        tilesSprites[x, y].transform.eulerAngles = new Vector3(0.0f, 0.0f, rot * 90.0f);
        tilesSprites[x, y].sprite = mDirtSprites[id - 1];
    }

    private void UpdateBrushPreview(int mouseTileX, int mouseTileY)
    {
        if (brushPreviewInstance == null) return;
        bool isMouseInBounds = mouseTileX >= 0 && mouseTileX < mWidth && mouseTileY >= 0 && mouseTileY < mHeight;
        brushPreviewInstance.SetActive(isMouseInBounds);
        if (isMouseInBounds)
        {
            float bottomLeftX = position.x + mouseTileX * cTileSize;
            float bottomLeftY = position.y + mouseTileY * cTileSize;
            float totalSize = brushSize * cTileSize;
            float centerX = bottomLeftX + totalSize / 2.0f - cTileSize / 2.0f;
            float centerY = bottomLeftY + totalSize / 2.0f - cTileSize / 2.0f;
            brushPreviewInstance.transform.position = new Vector3(centerX, centerY, -5f);
            brushPreviewInstance.transform.localScale = new Vector3(totalSize, totalSize, 1f);
        }
    }

    private void ClearTileState(Vector2i cell)
    {
        if (startTile == cell) startTile = new Vector2i(-1, -1);
        if (endTile == cell) endTile = new Vector2i(-1, -1);
        playerSelectedPath.Remove(cell);
    }

    private void SetVisual(int x, int y, Color color)
    {
        tilesSprites[x, y].enabled = true;
        tilesSprites[x, y].sprite = mDirtSprites[0];
        tilesSprites[x, y].color = color;
        tilesSprites[x, y].transform.localScale = Vector3.one;
        tilesSprites[x, y].transform.eulerAngles = Vector3.zero;
    }

    private void ResetVisual(int x, int y)
    {
        tilesSprites[x, y].enabled = true;
        tilesSprites[x, y].sprite = mDirtSprites[0];
        tilesSprites[x, y].color = gridColor;
        tilesSprites[x, y].transform.localScale = Vector3.one;
        tilesSprites[x, y].transform.eulerAngles = Vector3.zero;
    }

    private void ScanLevelData()
    {
        Debug.Log(">>> 关卡扫描完成 <<<");
    }
}