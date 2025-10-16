using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Algorithms;
using UnityEngine.UI;

[System.Serializable]
public enum TileType
{
    Empty,
    Block,
    OneWay
}

[System.Serializable]
public partial class Map : MonoBehaviour 
{
	
	/// <summary>
	/// The map's position in world space. Bottom left corner.
	/// </summary>
	public Vector3 position;
	
	/// <summary>
	/// The base tile sprite prefab that populates the map.
	/// Assigned in the inspector.
	/// </summary>
	public SpriteRenderer tilePrefab;
	
	/// <summary>
	/// The path finder.
	/// </summary>
	public PathFinderFast mPathFinder;
	
	/// <summary>
	/// The nodes that are fed to pathfinder.
	/// </summary>
	[HideInInspector]
	public byte[,] mGrid;
	
	/// <summary>
	/// The map's tile data.
	/// </summary>
	[HideInInspector]
	private TileType[,] tiles;

	/// <summary>
	/// The map's sprites.
	/// </summary>
	private SpriteRenderer[,] tilesSprites;
	
	/// <summary>
	/// A parent for all the sprites. Assigned from the inspector.
	/// </summary>
	public Transform mSpritesContainer;
	
	/// <summary>
	/// The size of a tile in pixels.
	/// </summary>
	static public int cTileSize = 16;

    // --- 新增笔刷大小 ---
    [Header("Drawing Settings")]
    [Range(1, 10)] // 使用Range限制笔刷大小在1到10之间，防止设置过大或无效值
    public int brushSize = 1; // 默认为1，即1x1的格子
                              // --- 新增代码结束 ---

    // --- 新增代码在这里 ---
    public GameObject brushPreviewPrefab; // 用于在Inspector中拖入Prefab
    private GameObject brushPreviewInstance; // 用于在代码中控制实例
    // --- 新增代码结束 ---

    /// <summary>
    /// The width of the map in tiles.
    /// </summary>
    public int mWidth = 50;
	/// <summary>
	/// The height of the map in tiles.
	/// </summary>
	public int mHeight = 42;

    //新增代码
    // 记录格子选中的状态
    public enum GamePhase
    {
        Drawing,
        TrialPlay
    }

    [Header("Gameplay State")]
    public GamePhase currentPhase = GamePhase.Drawing;

    // 用一个 HashSet 来存储玩家选择的路径格子坐标，查询效率高
    private HashSet<Vector2i> playerSelectedPath = new HashSet<Vector2i>();
    //新增代码

    public MapRoomData mapRoomSimple;
    public MapRoomData mapRoomOneWay;

    public Camera gameCamera;
    public Bot player;
    bool[] inputs;
    bool[] prevInputs;

    /*int lastMouseTileX = -1;
    int lastMouseTileY = -1;*/

    public KeyCode goLeftKey = KeyCode.A;
    public KeyCode goRightKey = KeyCode.D;
    public KeyCode goJumpKey = KeyCode.W;
    public KeyCode goDownKey = KeyCode.S;

    public RectTransform sliderHigh;
    public RectTransform sliderLow;

    public TileType GetTile(int x, int y) 
	{
        if (x < 0 || x >= mWidth
            || y < 0 || y >= mHeight)
            return TileType.Block;

		return tiles[x, y]; 
	}

    public bool IsOneWayPlatform(int x, int y)
    {
        if (x < 0 || x >= mWidth
            || y < 0 || y >= mHeight)
            return false;

        return (tiles[x, y] == TileType.OneWay);
    }

    public bool IsGround(int x, int y)
    {
        if (x < 0 || x >= mWidth
           || y < 0 || y >= mHeight)
            return false;

        return (tiles[x, y] == TileType.OneWay || tiles[x, y] == TileType.Block);
    }

    public bool IsObstacle(int x, int y)
    {
        if (x < 0 || x >= mWidth
            || y < 0 || y >= mHeight)
            return true;

        return (tiles[x, y] == TileType.Block);
    }

    public bool IsNotEmpty(int x, int y)
    {
        if (x < 0 || x >= mWidth
            || y < 0 || y >= mHeight)
            return true;

        return (tiles[x, y] != TileType.Empty);
    }

	public void InitPathFinder()
	{
		mPathFinder = new PathFinderFast(mGrid, this);
		
		mPathFinder.Formula                 = HeuristicFormula.Manhattan;
		//if false then diagonal movement will be prohibited
        mPathFinder.Diagonals               = false;
		//if true then diagonal movement will have higher cost
        mPathFinder.HeavyDiagonals          = false;
		//estimate of path length
        mPathFinder.HeuristicEstimate       = 6;
        mPathFinder.PunishChangeDirection   = false;
        mPathFinder.TieBreaker              = false;
        mPathFinder.SearchLimit             = 1000000;
        mPathFinder.DebugProgress           = false;
        mPathFinder.DebugFoundPath          = false;
	}
	
	public void GetMapTileAtPoint(Vector2 point, out int tileIndexX, out int tileIndexY)
	{
		tileIndexY =(int)((point.y - position.y + cTileSize/2.0f)/(float)(cTileSize));
		tileIndexX =(int)((point.x - position.x + cTileSize/2.0f)/(float)(cTileSize));
	}
	
	public Vector2i GetMapTileAtPoint(Vector2 point)
	{
		return new Vector2i((int)((point.x - position.x + cTileSize/2.0f)/(float)(cTileSize)),
					(int)((point.y - position.y + cTileSize/2.0f)/(float)(cTileSize)));
	}
	
	public Vector2 GetMapTilePosition(int tileIndexX, int tileIndexY)
	{
		return new Vector2(
				(float) (tileIndexX * cTileSize) + position.x,
				(float) (tileIndexY * cTileSize) + position.y
			);
	}

	public Vector2 GetMapTilePosition(Vector2i tileCoords)
	{
		return new Vector2(
			(float) (tileCoords.x * cTileSize) + position.x,
			(float) (tileCoords.y * cTileSize) + position.y
			);
	}
	
	public bool CollidesWithMapTile(AABB aabb, int tileIndexX, int tileIndexY)
	{
		var tilePos = GetMapTilePosition (tileIndexX, tileIndexY);
		
		return aabb.Overlaps(tilePos, new Vector2( (float)(cTileSize)/2.0f, (float)(cTileSize)/2.0f));
	}

    public bool AnySolidBlockInRectangle(Vector2 start, Vector2 end)
    {
        return AnySolidBlockInRectangle(GetMapTileAtPoint(start), GetMapTileAtPoint(end));
    }

    public bool AnySolidBlockInStripe(int x, int y0, int y1)
    {
        int startY, endY;

        if (y0 <= y1)
        {
            startY = y0;
            endY = y1;
        }
        else
        {
            startY = y1;
            endY = y0;
        }

        for (int y = startY; y <= endY; ++y)
        {
            if (GetTile(x, y) == TileType.Block)
                return true;
        }

        return false;
    }

    public bool AnySolidBlockInRectangle(Vector2i start, Vector2i end)
    {
        int startX, startY, endX, endY;

        if (start.x <= end.x)
        {
            startX = start.x;
            endX = end.x;
        }
        else
        {
            startX = end.x;
            endX = start.x;
        }

        if (start.y <= end.y)
        {
            startY = start.y;
            endY = end.y;
        }
        else
        {
            startY = end.y;
            endY = start.y;
        }

        for (int y = startY; y <= endY; ++y)
        {
            for (int x = startX; x <= endX; ++x)
            {
                if (GetTile(x, y) == TileType.Block)
                    return true;
            }
        }

        return false;
    }

    public void SetTile(int x, int y, TileType type)
    {
        if (x <= 1 || x >= mWidth - 2 || y <= 1 || y >= mHeight - 2)
            return;

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

    public void Start()
    {
        // --- 通用初始化部分 ---
        mRandomNumber = new System.Random();
        Application.targetFrameRate = 60;
        inputs = new bool[(int)KeyInput.Count];
        prevInputs = new bool[(int)KeyInput.Count];
        position = transform.position;

        // --- 根据 Inspector 的设置来决定如何初始化 ---

        // 如果你在Inspector里设置的是 "TrialPlay"
        if (currentPhase == GamePhase.TrialPlay)
        {
            Debug.Log("Starting directly in PLAYING mode.");

            var mapRoom = mapRoomOneWay; // 或者 mapRoomSimple
            mWidth = mapRoom.width;
            mHeight = mapRoom.height;
            tiles = new TileType[mWidth, mHeight];
            tilesSprites = new SpriteRenderer[mapRoom.width, mapRoom.height];
            mGrid = new byte[Mathf.NextPowerOfTwo((int)mWidth), Mathf.NextPowerOfTwo((int)mHeight)];
            InitPathFinder();
            Camera.main.orthographicSize = Camera.main.pixelHeight / 2;

            for (int y = 0; y < mHeight; ++y)
            {
                for (int x = 0; x < mWidth; ++x)
                {
                    tilesSprites[x, y] = Instantiate<SpriteRenderer>(tilePrefab);
                    tilesSprites[x, y].transform.parent = transform;
                    tilesSprites[x, y].transform.position = position + new Vector3(cTileSize * x, cTileSize * y, 10.0f);

                    // 从 ScriptableObject 加载关卡数据
                    if (mapRoom.tileData[y * mWidth + x] == TileType.Empty)
                        SetTile(x, y, TileType.Empty);
                    else if (mapRoom.tileData[y * mWidth + x] == TileType.Block)
                        SetTile(x, y, TileType.Block);
                    else
                        SetTile(x, y, TileType.OneWay);
                }
            }

            // *** 已修正: 重新加入了被遗漏的边界生成代码 ***
            for (int y = 0; y < mHeight; ++y)
            {
                tiles[1, y] = TileType.Block;
                tiles[mWidth - 2, y] = TileType.Block;
            }

            for (int x = 0; x < mWidth; ++x)
            {
                tiles[x, 1] = TileType.Block;
                tiles[x, mHeight - 2] = TileType.Block;
            }

            // --- 在游戏模式开始时，初始化玩家 ---
            player.gameObject.SetActive(true);
            player.BotInit(inputs, prevInputs);
            player.mMap = this;
            player.mPosition = new Vector2(2 * Map.cTileSize, (mHeight / 2) * Map.cTileSize + player.mAABB.HalfSizeY);
            // ***********************************************
        }
        // 如果你在Inspector里设置的是 "Drawing"
        else
        {
            Debug.Log("Starting in DRAWING mode.");

            tiles = new TileType[mWidth, mHeight];
            tilesSprites = new SpriteRenderer[mWidth, mHeight];
            mGrid = new byte[Mathf.NextPowerOfTwo((int)mWidth), Mathf.NextPowerOfTwo((int)mHeight)];
            InitPathFinder();
            Camera.main.orthographicSize = Camera.main.pixelHeight / 2;

            for (int y = 0; y < mHeight; ++y)
            {
                for (int x = 0; x < mWidth; ++x)
                {
                    tilesSprites[x, y] = Instantiate<SpriteRenderer>(tilePrefab);
                    tilesSprites[x, y].transform.parent = transform;
                    tilesSprites[x, y].transform.position = position + new Vector3(cTileSize * x, cTileSize * y, 10.0f);
                }
            }
            // --- 在绘制模式开始时，隐藏并禁用玩家 ---
            player.gameObject.SetActive(false);
            ResetToDrawingMode();
        }

        // 初始化笔刷预览
        if (brushPreviewPrefab != null)
        {
            brushPreviewInstance = Instantiate(brushPreviewPrefab, transform); // 创建实例并设为Map的子对象
                                                                               // 如果以绘制模式开始，就准备好显示它，否则保持隐藏
            brushPreviewInstance.SetActive(currentPhase == GamePhase.Drawing);
        }
    }

    void Update()
    {
        switch (currentPhase)
        {
            case GamePhase.Drawing:
                HandleDrawingInput();

                // 按下空格键，开始试玩
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    StartTrialMode();
                }
                break;

            case GamePhase.TrialPlay:
                HandlePlayingInput(); // 玩家输入逻辑复用

                // 按下 Backspace 键，返回绘制模式进行修改
                if (Input.GetKeyDown(KeyCode.Backspace))
                {
                    ReturnToDrawingMode();
                }
                // 按下 'R' 键，完全重置所有内容，返回空白画布
                else if (Input.GetKeyDown(KeyCode.R))
                {
                    ResetToDrawingMode();
                }
                break;
        }
    }

    System.Random mRandomNumber;

    void AutoTile(TileType type, int x, int y, int rand4NeighbourTiles, int rand3NeighbourTiles,
        int rand2NeighbourPipeTiles, int rand2NeighbourCornerTiles, int rand1NeighbourTiles, int rand0NeighbourTiles)
    {
        if (x >= mWidth || x < 0 || y >= mHeight || y < 0)
            return;

        if (tiles[x, y] != TileType.Block)
            return;

        int tileOnLeft = tiles[x - 1, y] == tiles[x, y] ? 1 : 0;
        int tileOnRight = tiles[x + 1, y] == tiles[x, y] ? 1 : 0;
        int tileOnTop = tiles[x, y + 1] == tiles[x, y] ? 1 : 0;
        int tileOnBottom = tiles[x, y - 1] == tiles[x, y] ? 1 : 0;

        float scaleX = 1.0f;
        float scaleY = 1.0f;
        float rot = 0.0f;
        int id = 0;

        int sum = tileOnLeft + tileOnRight + tileOnTop + tileOnBottom;

        switch (sum)
        {
            case 0:
                id = 1 + mRandomNumber.Next(rand0NeighbourTiles);

                break;
            case 1:
                id = 1 + rand0NeighbourTiles + mRandomNumber.Next(rand1NeighbourTiles);

                if (tileOnRight == 1)
                    scaleX = -1;
                else if (tileOnTop == 1)
                    rot = -1;
                else if (tileOnBottom == 1)
                {
                    rot = 1;
                    scaleY = -1;
                }

                break;
            case 2:

                if (tileOnLeft + tileOnBottom == 2)
                {
                    id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + rand2NeighbourPipeTiles
                        + mRandomNumber.Next(rand2NeighbourCornerTiles);
                }
                else if (tileOnRight + tileOnBottom == 2)
                {
                    id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + rand2NeighbourPipeTiles
                        + mRandomNumber.Next(rand2NeighbourCornerTiles);
                    scaleX = -1;
                }
                else if (tileOnTop + tileOnLeft == 2)
                {
                    id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + rand2NeighbourPipeTiles
                        + mRandomNumber.Next(rand2NeighbourCornerTiles);
                    scaleY = -1;
                }
                else if (tileOnTop + tileOnRight == 2)
                {
                    id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + rand2NeighbourPipeTiles
                        + mRandomNumber.Next(rand2NeighbourCornerTiles);
                    scaleX = -1;
                    scaleY = -1;
                }
                else if (tileOnTop + tileOnBottom == 2)
                {
                    id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + mRandomNumber.Next(rand2NeighbourPipeTiles);
                    rot = 1;
                }
                else if (tileOnRight + tileOnLeft == 2)
                    id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + mRandomNumber.Next(rand2NeighbourPipeTiles);

                break;
            case 3:
                id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + rand2NeighbourPipeTiles
                    + rand2NeighbourCornerTiles + mRandomNumber.Next(rand3NeighbourTiles);

                if (tileOnLeft == 0)
                {
                    rot = 1;
                    scaleX = -1;
                }
                else if (tileOnRight == 0)
                {
                    rot = 1;
                    scaleY = -1;
                }
                else if (tileOnBottom == 0)
                    scaleY = -1;

                break;

            case 4:
                id = 1 + rand0NeighbourTiles + rand1NeighbourTiles + rand2NeighbourPipeTiles
                    + rand2NeighbourCornerTiles + rand3NeighbourTiles + mRandomNumber.Next(rand4NeighbourTiles);

                break;
        }

        tilesSprites[x, y].transform.localScale = new Vector3(scaleX, scaleY, 1.0f);
        tilesSprites[x, y].transform.eulerAngles = new Vector3(0.0f, 0.0f, rot * 90.0f);
        tilesSprites[x, y].sprite = mDirtSprites[id - 1];
    }

    public List<Sprite> mDirtSprites;

    void FixedUpdate()
    {
        player.BotUpdate();
    }

    // 新方法：处理绘制阶段的输入
    private void HandleDrawingInput()
    {
        // 将鼠标位置转换为格子坐标
        Vector2 mousePos = Input.mousePosition;
        Vector2 cameraPos = Camera.main.transform.position;
        var mousePosInWorld = cameraPos + mousePos - new Vector2(gameCamera.pixelWidth / 2, gameCamera.pixelHeight / 2);
        int mouseTileX, mouseTileY;
        GetMapTileAtPoint(mousePosInWorld, out mouseTileX, out mouseTileY);
        Vector2i currentCell = new Vector2i(mouseTileX, mouseTileY);

        // --- 新增代码在这里 ---
        // 调用新方法来更新笔刷预览的实时状态
        UpdateBrushPreview(mouseTileX, mouseTileY);
        // --- 新增代码结束 ---

        // 如果按住鼠标左键，就绘制一个 brushSize * brushSize 的区域
        if (Input.GetKey(KeyCode.Mouse0))
        {
            // 使用嵌套循环遍历笔刷覆盖的每一个格子
            for (int xOffset = 0; xOffset < brushSize; xOffset++)
            {
                for (int yOffset = 0; yOffset < brushSize; yOffset++)
                {
                    int currentX = mouseTileX + xOffset;
                    int currentY = mouseTileY + yOffset;
                    currentCell = new Vector2i(currentX, currentY);

                    // 检查坐标是否在地图边界内且未被添加
                    if (currentX >= 0 && currentX < mWidth && currentY >= 0 && currentY < mHeight)
                    {
                        if (!playerSelectedPath.Contains(currentCell))
                        {
                            playerSelectedPath.Add(currentCell);
                            // 改变格子的颜色来给玩家反馈
                            tilesSprites[currentX, currentY].enabled = true;
                            tilesSprites[currentX, currentY].color = new Color(0.5f, 1f, 0.5f, 0.5f); // 淡绿色
                        }
                    }
                }
            }
        }

        // (可选) 按住鼠标右键可以擦除一个 brushSize * brushSize 的区域
        if (Input.GetKey(KeyCode.Mouse1))
        {
            // 同样使用嵌套循环来处理擦除逻辑
            for (int xOffset = 0; xOffset < brushSize; xOffset++)
            {
                for (int yOffset = 0; yOffset < brushSize; yOffset++)
                {
                    int currentX = mouseTileX + xOffset;
                    int currentY = mouseTileY + yOffset;
                    currentCell = new Vector2i(currentX, currentY);

                    if (playerSelectedPath.Contains(currentCell))
                    {
                        playerSelectedPath.Remove(currentCell);
                        tilesSprites[currentX, currentY].enabled = false;
                        tilesSprites[currentX, currentY].color = Color.white; // 恢复原色
                    }
                }
            }
        }
    }

    // 新方法：处理游戏阶段的输入（就是你之前Update里的逻辑）
    private void HandlePlayingInput()
    {
        inputs[(int)KeyInput.GoRight] = Input.GetKey(goRightKey);
        inputs[(int)KeyInput.GoLeft] = Input.GetKey(goLeftKey);
        inputs[(int)KeyInput.GoDown] = Input.GetKey(goDownKey);
        inputs[(int)KeyInput.Jump] = Input.GetKey(goJumpKey);

        // 你之前的寻路点击逻辑可以保留，用于测试
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

    // 新方法：重置到绘制模式
    private void ResetToDrawingMode()
    {
        playerSelectedPath.Clear();
        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                // 将所有格子清空
                SetTile(x, y, TileType.Empty);
                tilesSprites[x, y].color = Color.white; // 恢复颜色
            }
        }

        // --- 新增代码: 重置时再次隐藏玩家 ---
        if (player != null && player.gameObject.activeSelf)
        {
            player.gameObject.SetActive(false);
        }
        // ------------------------------------

        // --- 新增代码：重置到绘制模式时，激活笔刷预览 ---
        if (brushPreviewInstance != null)
        {
            brushPreviewInstance.SetActive(true);
        }
        // ------------------------------------

        currentPhase = GamePhase.Drawing;

        // --- 再次隐藏系统鼠标 ---
        Cursor.visible = false;
        // ------------------------------------

        Debug.Log("Reset to Drawing Mode. Draw your path and press Space.");
    }

    private void UpdateBrushPreview(int mouseTileX, int mouseTileY)
    {
        if (brushPreviewInstance == null) return;

        bool isMouseInBounds = mouseTileX >= 0 && mouseTileX < mWidth && mouseTileY >= 0 && mouseTileY < mHeight;
        brushPreviewInstance.SetActive(isMouseInBounds);

        if (isMouseInBounds)
        {
            // 1. 计算位置 (这部分逻辑是正确的，无需修改)
            // 我们需要将预览的左下角对齐到鼠标所在的格子
            float bottomLeftX = position.x + mouseTileX * cTileSize;
            float bottomLeftY = position.y + mouseTileY * cTileSize;

            // 预览的中心点位置 = 左下角位置 + 预览尺寸的一半
            float totalSize = brushSize * cTileSize;
            float centerX = bottomLeftX + totalSize / 2.0f - cTileSize / 2.0f;
            float centerY = bottomLeftY + totalSize / 2.0f - cTileSize / 2.0f;

            brushPreviewInstance.transform.position = new Vector3(centerX, centerY, -5f);

            // 2. 计算大小 (这是需要修正的地方)

            // --- 错误的代码 ---
            // brushPreviewInstance.transform.localScale = new Vector3(totalSize / 100f, totalSize / 100f, 1f);

            // --- 正确的代码 ---
            // 直接将物体的缩放设置为我们想要的像素尺寸
            brushPreviewInstance.transform.localScale = new Vector3(totalSize, totalSize, 1f);
        }
    }

    private void ReturnToDrawingMode()
    {
        // --- 关键新增：在返回编辑前，禁用玩家对象 ---
        if (player != null)
        {
            player.gameObject.SetActive(false);
        }
        // ------------------------------------------

        // 遍历所有格子，恢复到绘制时的视觉状态
        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentTile = new Vector2i(x, y);

                if (playerSelectedPath.Contains(currentTile))
                {
                    tilesSprites[x, y].enabled = true;
                    tilesSprites[x, y].color = new Color(0.5f, 1f, 0.5f, 0.5f);
                }
                else
                {
                    tilesSprites[x, y].enabled = false;
                }
                tiles[x, y] = TileType.Empty;
            }
        }

        // 切换回绘制状态
        currentPhase = GamePhase.Drawing;
        Cursor.visible = false; // 隐藏系统鼠标

        // 重新显示笔刷预览
        if (brushPreviewInstance != null)
        {
            brushPreviewInstance.SetActive(true);
        }

        Debug.Log("Back to Drawing Mode.");
    }

    private void StartTrialMode()
    {
        if (playerSelectedPath.Count == 0)
        {
            Debug.LogWarning("Path is empty! Cannot start trial.");
            return;
        }

        // 1. 生成关卡几何体
        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentTile = new Vector2i(x, y);
                tilesSprites[x, y].color = Color.white; // 恢复格子的正常颜色

                if (playerSelectedPath.Contains(currentTile))
                {
                    SetTile(x, y, TileType.Empty);
                }
                else
                {
                    SetTile(x, y, TileType.Block);
                }
            }
        }

        // 2. 隐藏绘制工具
        if (brushPreviewInstance != null)
        {
            brushPreviewInstance.SetActive(false);
        }
        Cursor.visible = true; // 显示系统鼠标

        // 3. 激活并初始化玩家
        player.gameObject.SetActive(true);
        player.BotInit(inputs, prevInputs);
        player.mMap = this;

        // 4. 把玩家放到路径的起点
        using (var enumerator = playerSelectedPath.GetEnumerator())
        {
            if (enumerator.MoveNext())
            {
                Vector2i startPos = enumerator.Current;
                player.mPosition = GetMapTilePosition(startPos) + new Vector2(0, player.mAABB.HalfSizeY);
            }
        }

        // 5. 切换到试玩状态
        currentPhase = GamePhase.TrialPlay;

        Debug.Log("Trial Mode! You can play now. Press BACKSPACE to return to editing.");
    }
}
