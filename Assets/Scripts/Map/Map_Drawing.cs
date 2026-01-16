using UnityEngine;
using System.Text;
using System.Collections.Generic;
using UnityEngine.EventSystems;

public partial class Map
{
    private void HandleDrawingInput()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (scrollInput != 0f)
        {
            brushSize = Mathf.Clamp(brushSize + (scrollInput > 0 ? 1 : -1), 1, 10);
        }

        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            currentBrush = BrushType.SurvivalSpace;
            Debug.Log("Brush: Survival Space (Safe Zone)");
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
                        else if (currentBrush == BrushType.SurvivalSpace)
                        {
                            survivalSpaceTiles.Add(targetCell);
                            SetVisual(currentX, currentY, new Color(0f, 1f, 0f, 0.4f));
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

                    bool removed = false;
                    if (playerSelectedPath.Remove(currentCell)) removed = true;
                    if (survivalSpaceTiles.Contains(currentCell))
                    {
                        survivalSpaceTiles.Remove(currentCell);
                        removed = true;
                    }

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
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

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
        Time.timeScale = 1.0f;
        isLevelComplete = false;

        playerSelectedPath.Clear();
        survivalSpaceTiles.Clear();

        // [新增] 清理可视化显示
        ClearSurvivalSpaceVisuals();

        startTile = new Vector2i(-1, -1);
        endTile = new Vector2i(-1, -1);

        foreach (var obj in spawnedObjects) if (obj != null) Destroy(obj);
        spawnedObjects.Clear();

        if (director != null) director.ClearTraps();

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
        Time.timeScale = 1.0f;
        isLevelComplete = false;

        if (player != null) player.gameObject.SetActive(false);

        foreach (var obj in spawnedObjects) if (obj != null) Destroy(obj);
        spawnedObjects.Clear();

        if (director != null) director.ClearTraps();

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
                else if (survivalSpaceTiles.Contains(currentTile)) SetVisual(x, y, new Color(0f, 1f, 0f, 0.4f));
                else ResetVisual(x, y);
            }
        }
        currentPhase = GamePhase.Drawing;
        Cursor.visible = false;
        if (brushPreviewInstance != null) brushPreviewInstance.SetActive(true);
    }

    private void StartTrialMode()
    {
        if (startTile.x == -1 || endTile.x == -1) { Debug.LogError("无法开始：未设置起点或终点！"); return; }

        FillMapWithBlocks();
        SetTile(startTile.x, startTile.y, TileType.Empty);
        SetTile(endTile.x, endTile.y, TileType.Empty);

        if (brushPreviewInstance != null) brushPreviewInstance.SetActive(false);
        Cursor.visible = true;
        player.gameObject.SetActive(true);
        player.BotInit(inputs, prevInputs);
        player.mMap = this;
        player.mPosition = GetMapTilePosition(startTile) + new Vector2(0, player.mAABB.HalfSizeY);

        BackupMapState();

        currentPhase = GamePhase.TrialPlay;

        // [新增] 试玩开始时显示可视化
        ShowSurvivalSpaceVisuals();

        if (director != null)
        {
            director.enabled = true;
            director.SetRunning(true);
        }
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
        survivalSpaceTiles.Remove(cell);
    }

    private void SetVisual(int x, int y, Color color)
    {
        tilesSprites[x, y].enabled = true;
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