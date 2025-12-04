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

        // --- 新增：清除所有生成的尖刺对象 ---
        foreach (var spike in spawnedSpikes)
        {
            if (spike != null) Destroy(spike);
        }
        spawnedSpikes.Clear();
        // ------------------------------------

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

        // --- 新增：必须在这里也清除生成的尖刺 ---
        foreach (var spike in spawnedSpikes)
        {
            if (spike != null) Destroy(spike);
        }
        spawnedSpikes.Clear();
        // ---------------------------------------

        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentTile = new Vector2i(x, y);

                // 这里不仅要重置 TileType，还要确保 Grid 数据被还原，否则再次生成时可能会出错
                tiles[x, y] = TileType.Empty;
                mGrid[x, y] = 1;

                // 恢复之前的视觉状态（起点、终点、路径）
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

        // 先把基本地形画好（墙壁 vs 空气）
        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentTile = new Vector2i(x, y);
                tilesSprites[x, y].color = Color.white;

                // 起点、终点、路径设为空气
                if (currentTile == startTile || currentTile == endTile || playerSelectedPath.Contains(currentTile))
                {
                    SetTile(x, y, TileType.Empty);
                }
                else
                {
                    SetTile(x, y, TileType.Block);
                }
            }
        }

        // --- 新增：生成路径下方的尖刺 ---
        GenerateSpikesUnderPath();
        // ------------------------------

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

    private void GenerateSpikesUnderPath()
    {
        if (spikePrefab == null)
        {
            Debug.LogWarning("未设置 Spike Prefab，无法生成尖刺！");
            return;
        }

        foreach (Vector2i pathPos in playerSelectedPath)
        {
            // 目标位置：路径格子的正下方
            Vector2i targetPos = new Vector2i(pathPos.x, pathPos.y - 1);

            // 1. 边界检查：不能生成在地图外面
            if (targetPos.y < 0) continue;

            // 2. 逻辑检查：
            // - 不要在起点或终点位置生成
            // - 如果下方格子本身就是路径的一部分（比如垂直向下的路径），不要生成刺
            if (targetPos == startTile || targetPos == endTile) continue;
            if (playerSelectedPath.Contains(targetPos)) continue;

            // 3. 修改数据：标记为 Danger
            tiles[targetPos.x, targetPos.y] = TileType.Danger;

            // 危险区域在寻路网格中通常视为可通过（掉进去就死），或者由具体的物理逻辑决定。
            // 这里我们设为 1 (Empty/Passable)，让角色物理逻辑去检测 Danger 标记。
            mGrid[targetPos.x, targetPos.y] = 1;

            // 4. 视觉生成：实例化 Prefab
            // 隐藏原本的方块 Sprite，避免和尖刺重叠太难看
            tilesSprites[targetPos.x, targetPos.y].enabled = false;

            Vector2 worldPos2D = GetMapTilePosition(targetPos);
            // 将 Z 轴设为 -1，保证尖刺显示在背景前面
            Vector3 spawnPos = new Vector3(worldPos2D.x, worldPos2D.y, -1f);

            GameObject newSpike = Instantiate(spikePrefab, spawnPos, Quaternion.identity);
            newSpike.transform.parent = transform; // 设为 Map 的子物体，保持整洁

            // 如果你的 Spike 图片比较大，可能需要手动调整 Scale，这里默认保持 Prefab 的设置
            //newSpike.transform.localScale = Vector3.one; 

            spawnedSpikes.Add(newSpike);
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
        Debug.Log(">>> ----------------------------------- <<<");
        Debug.Log(">>> 关卡扫描器启动：正在生成约束图... <<<");
        StringBuilder report = new StringBuilder();
        int immutableCount = 0;
        int modifiableCount = 0;

        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentPos = new Vector2i(x, y);
                string tileTypeStr;
                string modifyPermission;

                if (currentPos == startTile) { tileTypeStr = "【起点 Start】"; modifyPermission = "不可修改 (Immutable)"; immutableCount++; }
                else if (currentPos == endTile) { tileTypeStr = "【终点 End】"; modifyPermission = "不可修改 (Immutable)"; immutableCount++; }
                else if (playerSelectedPath.Contains(currentPos)) { tileTypeStr = "【路径 Path】"; modifyPermission = "不可修改 (Immutable)"; immutableCount++; }
                else { tileTypeStr = "【墙壁 Wall】"; modifyPermission = "可修改 (Modifiable)"; modifiableCount++; }

                string info = $"Pos: ({x}, {y}) \t| Type: {tileTypeStr} \t| {modifyPermission}";
                report.AppendLine(info);
            }
        }
        Debug.Log(report.ToString());
        Debug.Log($">>> 扫描完成 <<<");
        Debug.Log($">>> 约束统计: 不可修改(约束)格子: {immutableCount} 个 | 可修改(自由)格子: {modifiableCount} 个");
        Debug.Log(">>> ----------------------------------- <<<");
    }
}