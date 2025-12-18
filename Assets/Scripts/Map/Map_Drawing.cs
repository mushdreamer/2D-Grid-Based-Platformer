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
            brushSize = Mathf.Clamp(brushSize + (scrollInput > 0 ? 1 : -1), 1, 10);
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
            // 右键清除逻辑... (保持不变，省略)
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
            // Tap teleport logic...
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

        // 清除所有生成的物体 (尖刺 + 道具)
        foreach (var obj in spawnedObjects) if (obj != null) Destroy(obj);
        spawnedObjects.Clear();

        ClearMapToEmpty();
        for (int y = 0; y < mHeight; y++)
            for (int x = 0; x < mWidth; x++)
                ResetVisual(x, y);

        if (player != null) player.gameObject.SetActive(false);
        if (brushPreviewInstance != null) brushPreviewInstance.SetActive(true);
        currentPhase = GamePhase.Drawing;
        Cursor.visible = false;
        Debug.Log("Reset to Drawing Mode.");
    }

    private void ReturnToDrawingMode()
    {
        if (player != null) player.gameObject.SetActive(false);

        foreach (var obj in spawnedObjects) if (obj != null) Destroy(obj);
        spawnedObjects.Clear();

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
    }

    private void StartTrialMode()
    {
        // ... 原有的手动 TrialMode 代码，这里为了与 ApplyGeneratedPath 统一，
        // 建议主要使用 ApplyGeneratedPath 进入游戏。
        // 如果你需要手绘关卡的试玩，逻辑同上。
        if (startTile.x == -1 || endTile.x == -1) { Debug.LogError("无法开始：未设置起点或终点！"); return; }
        // 简易填充
        FillMapWithBlocks();
        SetTile(startTile.x, startTile.y, TileType.Empty);
        SetTile(endTile.x, endTile.y, TileType.Empty);

        if (brushPreviewInstance != null) brushPreviewInstance.SetActive(false);
        Cursor.visible = true;
        player.gameObject.SetActive(true);
        player.BotInit(inputs, prevInputs);
        player.mMap = this;
        player.mPosition = GetMapTilePosition(startTile) + new Vector2(0, player.mAABB.HalfSizeY);
        currentPhase = GamePhase.TrialPlay;
    }

    // --- 核心生成逻辑 ---

    private void GenerateIslandsFromPath(List<Vector3> trajectory)
    {
        if (trajectory == null || trajectory.Count == 0) return;

        Dictionary<int, int> columnFloorY = new Dictionary<int, int>();
        foreach (var point in trajectory)
        {
            int x = Mathf.RoundToInt((point.x - position.x) / cTileSize);
            int y = Mathf.RoundToInt((point.y - position.y) / cTileSize);
            if (!columnFloorY.ContainsKey(x)) columnFloorY[x] = y;
            else if (y < columnFloorY[x]) columnFloorY[x] = y;
        }

        // 1. 生成落脚点平台
        foreach (int x in safeLandingColumns)
        {
            if (columnFloorY.ContainsKey(x))
            {
                int footY = columnFloorY[x];
                BuildPlatformAt(x, footY - 1, Random.Range(2, 5));

                // [IWBTG元素] 20% 概率在平台上生成水果
                if (Random.value < 0.2f)
                {
                    SpawnItemAt(x, footY, Collectible.ItemType.Fruit);
                }
            }
        }

        // 2. 生成空域障碍 (悬浮块 + 随机陷阱)
        for (int x = 0; x < mWidth; x++)
        {
            if (!safeLandingColumns.Contains(x) && columnFloorY.ContainsKey(x))
            {
                int trajY = columnFloorY[x];
                if (Random.value < 0.35f)
                {
                    int obstacleY = trajY - Random.Range(4, 9);
                    if (obstacleY > 0)
                    {
                        // 随机决定是向上刺还是向下刺，或者纯砖块
                        float r = Random.value;
                        if (r < 0.4f)
                        {
                            SetTile(x, obstacleY, TileType.Block);
                            SpawnSpikeAt(x, obstacleY + 1); // 朝上的刺
                        }
                        else if (r < 0.7f)
                        {
                            SetTile(x, obstacleY, TileType.Block);
                            SpawnSpikeAt(x, obstacleY - 1, true); // 朝下的刺 (flip)
                        }
                        else
                        {
                            SetTile(x, obstacleY, TileType.Block); // 纯砖块干扰
                        }
                    }
                }
            }
        }
    }

    private void SpawnSpikeAt(int x, int y, bool flipped = false)
    {
        if (spikePrefab == null) return;
        if (x < 0 || x >= mWidth || y < 0 || y >= mHeight) return;

        SetTile(x, y, TileType.Danger);
        tilesSprites[x, y].enabled = false; // 隐藏格子本身的Sprite

        Vector2 worldPos = GetMapTilePosition(x, y);
        Vector3 spawnPos = new Vector3(worldPos.x, worldPos.y, -2f);

        GameObject newSpike = Instantiate(spikePrefab, spawnPos, Quaternion.identity);
        newSpike.transform.parent = transform;

        // --- 视觉升级：应用随机陷阱皮肤 ---
        SpriteRenderer sr = newSpike.GetComponent<SpriteRenderer>();
        if (sr != null && trapSprites != null && trapSprites.Count > 0)
        {
            // 使用当前主题的 Trap 索引
            int index = Mathf.Clamp(currentThemeTrapIndex, 0, trapSprites.Count - 1);
            sr.sprite = trapSprites[index];
        }

        // 旋转
        if (flipped)
        {
            newSpike.transform.localScale = new Vector3(1, -1, 1);
        }

        spawnedObjects.Add(newSpike);
    }

    private void SpawnItemAt(int x, int y, Collectible.ItemType type)
    {
        if (itemPrefab == null) return;
        if (x < 0 || x >= mWidth || y < 0 || y >= mHeight) return;
        if (tiles[x, y] != TileType.Empty) return; // 别生成在墙里

        Vector2 worldPos = GetMapTilePosition(x, y);
        Vector3 spawnPos = new Vector3(worldPos.x, worldPos.y, -3f);

        GameObject newItem = Instantiate(itemPrefab, spawnPos, Quaternion.identity);
        newItem.transform.parent = transform;

        Collectible col = newItem.GetComponent<Collectible>();
        col.type = type;

        // 设置图片
        SpriteRenderer sr = newItem.GetComponent<SpriteRenderer>();
        if (type == Collectible.ItemType.Fruit && fruitSprites != null && fruitSprites.Count > 0)
        {
            sr.sprite = fruitSprites[Random.Range(0, fruitSprites.Count)];
        }
        else if (type == Collectible.ItemType.Checkpoint && checkpointSprites != null && checkpointSprites.Count > 0)
        {
            sr.sprite = checkpointSprites[0];
        }

        spawnedObjects.Add(newItem);
    }

    private void BuildPlatformAt(int centerX, int y, int width)
    {
        int halfW = width / 2;
        for (int x = centerX - halfW; x <= centerX + halfW; x++)
        {
            if (x >= 0 && x < mWidth && y >= 0 && y < mHeight)
            {
                SetTile(x, y, TileType.Block);
            }
        }
    }

    public void SetTile(int x, int y, TileType type)
    {
        if (x < 0 || x >= mWidth || y < 0 || y >= mHeight) return;

        tiles[x, y] = type;
        SpriteRenderer sr = tilesSprites[x, y];

        if (type == TileType.Block)
        {
            mGrid[x, y] = 0; // 物理阻挡
            sr.enabled = true;
            sr.transform.localScale = Vector3.one;
            sr.transform.eulerAngles = Vector3.zero;
            sr.color = Color.white;

            // --- 视觉升级：应用地形皮肤 ---
            if (terrainSprites != null && terrainSprites.Count > 0)
            {
                int index = Mathf.Clamp(currentThemeTerrainIndex, 0, terrainSprites.Count - 1);
                sr.sprite = terrainSprites[index];
            }
            else
            {
                sr.sprite = mDirtSprites[1]; // Fallback
            }
        }
        else if (type == TileType.Danger)
        {
            mGrid[x, y] = 1; // 物理不阻挡 (角色进入重叠触发死亡)
            sr.enabled = false; // 隐藏底图，使用生成的 Prefab
        }
        else if (type == TileType.Empty)
        {
            mGrid[x, y] = 1;
            sr.enabled = false;
        }
        // OneWay 逻辑保持不变...
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
        // 编辑模式下用一个简单的方块图即可
        tilesSprites[x, y].sprite = (mDirtSprites != null && mDirtSprites.Count > 0) ? mDirtSprites[0] : null;
        tilesSprites[x, y].color = color;
        tilesSprites[x, y].transform.localScale = Vector3.one;
        tilesSprites[x, y].transform.eulerAngles = Vector3.zero;
    }

    private void ResetVisual(int x, int y)
    {
        tilesSprites[x, y].enabled = true;
        tilesSprites[x, y].sprite = (mDirtSprites != null && mDirtSprites.Count > 0) ? mDirtSprites[0] : null;
        tilesSprites[x, y].color = gridColor;
        tilesSprites[x, y].transform.localScale = Vector3.one;
        tilesSprites[x, y].transform.eulerAngles = Vector3.zero;
    }
}