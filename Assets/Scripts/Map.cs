using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Algorithms;
using UnityEngine.UI;
using System.IO;
using System.Text;
using System;
using System.Diagnostics; // --- 新增代码：用于执行外部 Python 命令 ---
using Debug = UnityEngine.Debug; // --- 新增代码：明确指定 Debug，避免与 System.Diagnostics 冲突 ---

#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public enum TileType
{
    Empty,
    Block,
    OneWay,
    Danger
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
    public Color gridColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
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

    // --- 新增代码：用于手动控制地图大小 ---
    [Header("Drawing Mode Size")]
    [Tooltip("如果勾选，地图大小将自动匹配相机屏幕。")]
    public bool autoSizeToCamera = true; // 默认勾选，保持当前行为

    [Tooltip("如果 autoSizeToCamera 未勾选，将使用这个宽度。")]
    public int manualWidth = 100; // 手动模式的默认宽度

    [Tooltip("如果 autoSizeToCamera 未勾选，将使用这个高度。")]
    public int manualHeight = 60; // 手动模式的默认高度
    // --- 新增代码结束 ---

    //新增代码
    // 记录格子选中的状态
    public enum GamePhase
    {
        Drawing,
        TrialPlay
    }

    // 管理所有笔刷类型
    public enum BrushType
    {
        StartPoint, // 1. 起点
        Path,       // 2. 路径
        EndPoint    // 3. 终点
    }

    [Header("Brushes")]
    public BrushType currentBrush = BrushType.Path; // 当前激活的笔刷，默认为路径

    // 存储唯一的起点和终点坐标，初始化为 (-1, -1) 表示未设置
    private Vector2i startTile = new Vector2i(-1, -1);
    private Vector2i endTile = new Vector2i(-1, -1);

    [Header("Gameplay State")]
    public GamePhase currentPhase = GamePhase.Drawing;

    // --- 新增代码：用于线程间通信 ---
    // 这个 "busy" 标志防止用户在脚本运行时重复按 Enter
    private volatile bool pythonScriptsRunning = false;
    // 这个 "signal" 标志由后台线程设置，告诉 Update() 它可以加载关卡了
    private volatile bool pythonScriptsFinished = false;

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

        mPathFinder.Formula = HeuristicFormula.Manhattan;
        //if false then diagonal movement will be prohibited
        mPathFinder.Diagonals = false;
        //if true then diagonal movement will have higher cost
        mPathFinder.HeavyDiagonals = false;
        //estimate of path length
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
        return new Vector2(
                (float)(tileIndexX * cTileSize) + position.x,
                (float)(tileIndexY * cTileSize) + position.y
            );
    }

    public Vector2 GetMapTilePosition(Vector2i tileCoords)
    {
        return new Vector2(
            (float)(tileCoords.x * cTileSize) + position.x,
            (float)(tileCoords.y * cTileSize) + position.y
            );
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
        // --- 新增 Danger 类型的处理逻辑 ---
        else if (type == TileType.Danger)
        {
            mGrid[x, y] = 1; // 关键：在寻路网格中，它和 Empty 一样，是可以通行的 (值为1)
            tilesSprites[x, y].enabled = true; // 我们要让它可见

            // 使用一个基础的 sprite，比如 mDirtSprites 的第一个，作为区域的底色
            tilesSprites[x, y].sprite = mDirtSprites[0];
            // 关键：将它的颜色设置为红色，以在视觉上与安全区区分
            tilesSprites[x, y].color = Color.red;

            // 确保它没有奇怪的缩放和旋转
            tilesSprites[x, y].transform.localScale = Vector3.one;
            tilesSprites[x, y].transform.eulerAngles = Vector3.zero;
        }
        // ------------------------------------
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

            // --- 核心修改开始 ---

            // 1. (移动) 我们把这行代码从后面移到这里
            // 必须先设置相机大小，这样才能保证 pixelWidth 和 pixelHeight 是我们期望的值
            Camera.main.orthographicSize = Camera.main.pixelHeight / 2;

            // 2. (新增) 检查 cTileSize，防止除零错误
            if (cTileSize <= 0)
            {
                Debug.LogError("cTileSize 必须大于 0! 无法自动调整 Map 大小。");
                return; // 终止 Start，防止后续代码出错
            }

            // 3. (新增) 根据相机的像素大小和瓦片大小，重新计算 mWidth 和 mHeight
            // 我们使用 FloorToInt 来确保只包含完整的瓦片
            if (autoSizeToCamera)
            {
                // 自动模式：使用相机大小 (和以前一样)
                mWidth = Mathf.FloorToInt((float)Camera.main.pixelWidth / (float)cTileSize);
                mHeight = Mathf.FloorToInt((float)Camera.main.pixelHeight / (float)cTileSize);
                Debug.Log($"Map 尺寸已[自动]调整为: {mWidth} x {mHeight}");
            }
            else
            {
                // 手动模式：使用 Inspector 中设置的值
                mWidth = manualWidth;
                mHeight = manualHeight;
                Debug.Log($"Map 尺寸已[手动]设置为: {mWidth} x {mHeight}");
            }
            // --- 核心修改结束 ---


            tiles = new TileType[mWidth, mHeight];
            tilesSprites = new SpriteRenderer[mWidth, mHeight];
            // (修改) mWidth 和 mHeight 已经是计算后的新值了
            mGrid = new byte[Mathf.NextPowerOfTwo((int)mWidth), Mathf.NextPowerOfTwo((int)mHeight)];
            InitPathFinder();
            // (移动) 这行代码被移到前面了: Camera.main.orthographicSize = Camera.main.pixelHeight / 2;

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
        // 1. 检查后台线程信号 (用于 Enter 键执行 Python 后的回调)
        if (pythonScriptsFinished)
        {
            pythonScriptsFinished = false;
            Debug.Log("主线程收到信号。正在加载生成的关卡...");
            LoadGeneratedLevel();
        }

        switch (currentPhase)
        {
            case GamePhase.Drawing:
                // --- 笔刷切换 ---
                if (Input.GetKeyDown(KeyCode.Alpha1))
                {
                    currentBrush = BrushType.StartPoint;
                    Debug.Log("Brush: Start Point (起点)");
                }
                else if (Input.GetKeyDown(KeyCode.Alpha2))
                {
                    currentBrush = BrushType.Path;
                    Debug.Log("Brush: Path (路径)");
                }
                else if (Input.GetKeyDown(KeyCode.Alpha3))
                {
                    currentBrush = BrushType.EndPoint;
                    Debug.Log("Brush: End Point (终点)");
                }

                // --- 快捷键功能区 ---

                // [P] 键：手动保存关卡 (调用刚才修复过的 SaveLevelToFile)
                else if (Input.GetKeyDown(KeyCode.P))
                {
                    SaveLevelToFile();
                }
                // [L] 键：手动加载关卡
                else if (Input.GetKeyDown(KeyCode.L))
                {
                    LoadLevelFromFile();
                }
                // [Enter] 键：保存并执行 Python 脚本
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    HandleEnterKeySave();
                }

                // --- 绘制逻辑 ---
                HandleDrawingInput();

                // [Space] 键：开始试玩
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    if (startTile.x == -1 || endTile.x == -1)
                    {
                        Debug.LogError("无法开始：请先设置 起点(1) 和 终点(3)！");
                    }
                    else
                    {
                        StartTrialMode();
                    }
                }
                break;

            case GamePhase.TrialPlay:
                HandlePlayingInput();

                // [Backspace] 键：返回绘制模式
                if (Input.GetKeyDown(KeyCode.Backspace))
                {
                    ReturnToDrawingMode();
                }
                // [R] 键：重置画布
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

        // 检查左侧 (x-1)，确保 x > 0
        int tileOnLeft = (x > 0 && tiles[x - 1, y] == tiles[x, y]) ? 1 : 0;

        // 检查右侧 (x+1)，确保 x < mWidth - 1
        int tileOnRight = (x < mWidth - 1 && tiles[x + 1, y] == tiles[x, y]) ? 1 : 0;

        // 检查上方 (y+1)，确保 y < mHeight - 1
        int tileOnTop = (y < mHeight - 1 && tiles[x, y + 1] == tiles[x, y]) ? 1 : 0;

        // 检查下方 (y-1)，确保 y > 0
        int tileOnBottom = (y > 0 && tiles[x, y - 1] == tiles[x, y]) ? 1 : 0;

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
        if (currentPhase == GamePhase.TrialPlay && player.gameObject.activeInHierarchy)
        {
            player.BotUpdate();
        }
    }

    // 新方法：处理绘制阶段的输入
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

        // 左键绘制
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
                        ClearTileState(targetCell); // 清除旧状态

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

        // 右键擦除
        if (Input.GetKey(KeyCode.Mouse1))
        {
            for (int xOffset = 0; xOffset < brushSize; xOffset++)
            {
                for (int yOffset = 0; yOffset < brushSize; yOffset++)
                {
                    int currentX = mouseTileX + xOffset;
                    int currentY = mouseTileY + yOffset;
                    Vector2i currentCell = new Vector2i(currentX, currentY);

                    // --- 修复：移除了 dangerZoneTiles 的引用 ---
                    bool removed = playerSelectedPath.Remove(currentCell);
                    if (startTile == currentCell) { startTile = new Vector2i(-1, -1); removed = true; }
                    if (endTile == currentCell) { endTile = new Vector2i(-1, -1); removed = true; }

                    if (removed)
                    {
                        ResetVisual(currentX, currentY);
                    }
                }
            }
        }
    }

    // 辅助方法：清除某个坐标的所有逻辑状态
    private void ClearTileState(Vector2i cell)
    {
        if (startTile == cell) startTile = new Vector2i(-1, -1);
        if (endTile == cell) endTile = new Vector2i(-1, -1);
        playerSelectedPath.Remove(cell);
    }

    // 辅助方法：设置视觉
    private void SetVisual(int x, int y, Color color)
    {
        tilesSprites[x, y].enabled = true;
        tilesSprites[x, y].sprite = mDirtSprites[0];
        tilesSprites[x, y].color = color;
        tilesSprites[x, y].transform.localScale = Vector3.one;
        tilesSprites[x, y].transform.eulerAngles = Vector3.zero;
    }

    // 辅助方法：重置视觉（变回灰色网格）
    private void ResetVisual(int x, int y)
    {
        tilesSprites[x, y].enabled = true;
        tilesSprites[x, y].sprite = mDirtSprites[0];
        tilesSprites[x, y].color = gridColor;
        tilesSprites[x, y].transform.localScale = Vector3.one;
        tilesSprites[x, y].transform.eulerAngles = Vector3.zero;
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
        // dangerZoneTiles.Clear(); // 移除
        startTile = new Vector2i(-1, -1); // 重置起点
        endTile = new Vector2i(-1, -1);   // 重置终点

        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                tiles[x, y] = TileType.Empty;
                mGrid[x, y] = 1;
                ResetVisual(x, y); // 使用上面定义的辅助方法
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
        if (player != null) player.gameObject.SetActive(false);

        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentTile = new Vector2i(x, y);

                tiles[x, y] = TileType.Empty;
                mGrid[x, y] = 1;

                // --- 修复：分别恢复起点、终点和路径的颜色 ---
                if (currentTile == startTile)
                {
                    SetVisual(x, y, Color.cyan);
                }
                else if (currentTile == endTile)
                {
                    SetVisual(x, y, Color.yellow);
                }
                else if (playerSelectedPath.Contains(currentTile))
                {
                    SetVisual(x, y, new Color(0.5f, 1f, 0.5f, 0.5f));
                }
                else
                {
                    ResetVisual(x, y);
                }
            }
        }

        currentPhase = GamePhase.Drawing;
        Cursor.visible = false;
        if (brushPreviewInstance != null) brushPreviewInstance.SetActive(true);

        Debug.Log("Back to Drawing Mode.");
    }

    private void StartTrialMode()
    {
        // 检查起点和终点是否有效
        if (startTile.x == -1 || endTile.x == -1)
        {
            Debug.LogError("无法开始：未设置起点或终点！");
            return;
        }

        // 1. 生成关卡几何体
        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentTile = new Vector2i(x, y);
                tilesSprites[x, y].color = Color.white;

                // --- 修复：移除了 dangerZoneTiles，增加了 Start/End 的处理 ---
                if (currentTile == startTile || currentTile == endTile || playerSelectedPath.Contains(currentTile))
                {
                    // 起点、终点、路径在物理上都是 Empty (可通行)
                    SetTile(x, y, TileType.Empty);
                }
                else
                {
                    // 其他地方是墙
                    SetTile(x, y, TileType.Block);
                }
            }
        }

        // 2. 隐藏绘制工具
        if (brushPreviewInstance != null) brushPreviewInstance.SetActive(false);
        Cursor.visible = true;

        // 3. 激活并初始化玩家
        player.gameObject.SetActive(true);
        player.BotInit(inputs, prevInputs);
        player.mMap = this;

        // 4. --- 修复：让玩家出生在设置的【起点】 ---
        player.mPosition = GetMapTilePosition(startTile) + new Vector2(0, player.mAABB.HalfSizeY);
        // ----------------------------------------

        // 5. 切换状态
        currentPhase = GamePhase.TrialPlay;

        // 6. 触发扫描器
        ScanLevelData();

        Debug.Log("Trial Mode Started.");
    }

    // --- 关卡扫描器实现方法 ---
    /// <summary>
    /// 扫描当前地图，区分可修改区域（墙壁）和不可修改区域（起点、终点、路径）。
    /// </summary>
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
                string modifyPermission; // 权限：可修改 vs 不可修改

                // 优先级判断：起点 > 终点 > 路径 > 墙壁
                if (currentPos == startTile)
                {
                    tileTypeStr = "【起点 Start】";
                    modifyPermission = "不可修改 (Immutable)";
                    immutableCount++;
                }
                else if (currentPos == endTile)
                {
                    tileTypeStr = "【终点 End】";
                    modifyPermission = "不可修改 (Immutable)";
                    immutableCount++;
                }
                else if (playerSelectedPath.Contains(currentPos))
                {
                    tileTypeStr = "【路径 Path】";
                    modifyPermission = "不可修改 (Immutable)";
                    immutableCount++;
                }
                else
                {
                    // 剩下的所有空白区域，默认为墙壁，且可以被算法修改
                    tileTypeStr = "【墙壁 Wall】";
                    modifyPermission = "可修改 (Modifiable)";
                    modifiableCount++;
                }

                // 格式化输出: 坐标 | 类型 | 权限
                string info = $"Pos: ({x}, {y}) \t| Type: {tileTypeStr} \t| {modifyPermission}";

                // 打印
                // Debug.Log(info); // 如果不想刷屏，可以注释掉这行，只看最后统计
                report.AppendLine(info);
            }
        }

        // 这里可以将 report.ToString() 保存到文件或者发送给 Python
        Debug.Log(report.ToString()); // 打印完整报告

        Debug.Log($">>> 扫描完成 <<<");
        Debug.Log($">>> 约束统计: 不可修改(约束)格子: {immutableCount} 个 | 可修改(自由)格子: {modifiableCount} 个");
        Debug.Log(">>> ----------------------------------- <<<");
    }

    // --- 新增代码：用于 Enter 键保存和执行Python脚本的所有逻辑 ---
#if UNITY_EDITOR
    /// <summary>
    /// (新) 处理 Enter 键按下，保存文件并启动Python脚本。
    /// </summary>
    private void HandleEnterKeySave()
    {
        // --- 修改开始 ---
        // 如果脚本已经在运行，就阻止再次执行
        if (pythonScriptsRunning)
        {
            Debug.LogWarning("Python 脚本已在运行，请稍候...");
            return;
        }
        // --- 修改结束 ---

        string workingDirectory = @"C:\GitHub\sturgeon-pub";
        string levelFileName = "MyDrawnLevel.lvl";
        string fullSavePath = Path.Combine(workingDirectory, levelFileName);

        // 1. 保存文件 (在主线程同步执行)
        try
        {
            SaveLevelDirectly(fullSavePath);
            Debug.Log($"关卡已成功保存到: {fullSavePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"直接保存关卡失败: {e.Message}");
            return; // 如果保存失败，就不执行后续脚本
        }

        // 2. 设置标志并启动后台线程
        pythonScriptsRunning = true; // 设置为 "忙碌"
        pythonScriptsFinished = false; // 重置 "完成" 标志

        Debug.Log("关卡已保存。正在后台启动 Python 脚本...");

        // 2. 在新线程中运行 Python 脚本，防止Unity编辑器卡死
        new Thread(new ThreadStart(RunPythonScripts)).Start();
    }

    /// <summary>
    /// (新) 这是一个不带对话框的保存函数。
    /// 它只负责将关卡数据写入指定的完整路径。
    /// </summary>
    /// <param name="path">要保存到的完整文件路径</param>
    private void SaveLevelDirectly(string path)
    {
        StringBuilder sb = new StringBuilder();

        for (int y = mHeight - 1; y >= 0; y--)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentTile = new Vector2i(x, y);

                // --- 修复：包含 StartTile 和 EndTile ---
                if (currentTile == startTile || currentTile == endTile || playerSelectedPath.Contains(currentTile))
                {
                    sb.Append('R'); // Path
                }
                else
                {
                    sb.Append('X'); // Wall
                }
            }
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>
    /// (新) 在后台线程中依次执行 Python 脚本。
    /// </summary>
    private void RunPythonScripts()
    {
        string workingDirectory = @"C:\GitHub\sturgeon-pub";

        // --- 修改代码：可执行文件改为 'pipenv' ---
        // 假设 'pipenv' 已经
        // 在你系统的 PATH 环境变量中
        string executable = "pipenv";

        // --- 修改代码：在 python 命令前添加 'run' ---
        string args1 = "run python input2tile.py --outfile work/mario.tile --textfile levels/vglc/mario-1-1-generic.lvl";
        string args2 = "run python tile2scheme.py --outfile work/mario.scheme --tilefile work/mario.tile --count-divs 1 1 --pattern ring";

        // C# 中的字符串需要正确处理引号。
        // 你命令中的 '...' 和 "..." 会被原样传递
        string args3 = "run python scheme2output.py --outfile work/my-level-output --schemefile work/mario.scheme --size 10 29 --pattern-hard --reach-junction \"{\" l 3 --reach-junction \"}\" r 3 --reach-connect \"--src { --dst } --move platform --sink-bottom --fwdbwd-layers 25\" --reach-print-internal --custom fwdbwd-nostuck hard --custom fwdbwd-grid MyDrawnLevel.lvl soft";

        try
        {
            Debug.Log("开始执行 Python 脚本 (后台线程)...");

            // --- 修改代码：添加了错误检查 ---
            // 依次执行命令，如果任何一个失败 (返回 false)，则停止后续操作
            if (!RunProcess(executable, args1, workingDirectory))
            {
                Debug.LogError("步骤 1 (input2tile) 失败。终止执行。");
                return;
            }

            if (!RunProcess(executable, args2, workingDirectory))
            {
                Debug.LogError("步骤 2 (tile2scheme) 失败。终止执行。");
                return;
            }

            if (!RunProcess(executable, args3, workingDirectory))
            {
                Debug.LogError("步骤 3 (scheme2output) 失败。");
                return;
            }

            Debug.Log("所有 Python 脚本执行完毕。");
        }
        catch (Exception e)
        {
            // E确保错误能被 Unity 控制台捕获
            Debug.LogError($"Python 脚本执行出错: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            // --- 关键新增 ---
            // 无论成功还是失败，都通知主线程
            pythonScriptsFinished = true; // 发送 "完成" 信号
            pythonScriptsRunning = false; // 解除 "忙碌" 状态
            // --- 新增结束 ---
        }
    }

    /// <summary>
    /// (新) 启动一个外部进程，等待它完成，并将其输出记录到 Unity 控制台。
    /// </summary>
    /// <returns>如果 ExitCode 为 0 (成功) 则返回 true，否则返回 false</returns>
    private bool RunProcess(string executable, string args, string workingDir)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = false,      // 必须为 false 才能重定向输出
            RedirectStandardOutput = true,  // 捕获标准输出
            RedirectStandardError = true,   // 捕获标准错误
            CreateNoWindow = true           // 不显示黑色的 cmd 窗口
        };

        Debug.Log($"正在执行: {executable} {args} @ {workingDir}");

        using (Process process = Process.Start(startInfo))
        {
            // 因为我们在后台线程，所以可以安全地同步等待
            // 读取所有输出
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            process.WaitForExit(); // 等待进程执行完毕

            // --- 修改代码：检查 ExitCode 并返回 bool 值 ---
            if (process.ExitCode == 0)
            {
                Debug.Log($"执行成功: {executable} {args}\n输出:\n{output}");
                return true; // 成功
            }
            else
            {
                // 如果出错，打印错误信息
                Debug.LogError($"执行失败 (ExitCode {process.ExitCode}): {executable} {args}\n错误:\n{error}\n输出:\n{output}");
                return false; // 失败
            }
        }
    }
#endif
    // --- 修改代码结束 ---


    // --- (这是你原有的 'P' 键保存功能，保持不变) ---
#if UNITY_EDITOR
    private void SaveLevelToFile()
    {
        // 1. 弹出“另存为”对话框
        string path = EditorUtility.SaveFilePanel(
            "保存关卡文件",                                  // 窗口标题
            @"C:\GitHub\2D-Grid-Based-Platformer\Level",    // 默认打开的目录
            "NewLevel",                                     // 默认文件名
            "lvl"                                           // 文件扩展名
        );

        // 2. 检查用户是否点击了“取消”
        if (string.IsNullOrEmpty(path))
        {
            Debug.Log("保存已取消。");
            return; // 用户取消了操作，函数提前退出
        }

        // 3. 使用 StringBuilder 高效构建字符串
        StringBuilder sb = new StringBuilder();

        // 从上到下遍历（y 从 mHeight-1 到 0），符合 .lvl 文件格式标准
        for (int y = mHeight - 1; y >= 0; y--)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentTile = new Vector2i(x, y);

                // --- 修复核心逻辑 ---
                // 起点、终点、以及绘制的路径，都被记录为 'R' (表示由人类设计的路径约束)
                // 移除了 dangerZoneTiles 的引用
                if (currentTile == startTile || currentTile == endTile || playerSelectedPath.Contains(currentTile))
                {
                    sb.Append('R');
                }
                else
                {
                    sb.Append('X'); // 其他区域记录为墙壁，由算法自由发挥
                }
            }
            sb.AppendLine(); // 换行
        }

        // 4. 将字符串写入用户选择的文件路径
        try
        {
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"关卡已成功保存到: {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"保存关卡失败: {e.Message}");
        }
    }
#endif

#if UNITY_EDITOR
    /// <summary>
    /// (新) 从 .lvl 文件加载关卡到编辑器中
    /// </summary>
    private void LoadLevelFromFile()
    {
        // 1. 弹出“打开文件”对话框
        string path = EditorUtility.OpenFilePanel(
            "加载关卡文件",                                  // 窗口标题
            @"C:\GitHub\2D-Grid-Based-Platformer\Level",    // 默认打开的目录
            "lvl"                                           // 文件扩展名过滤器
        );

        // 2. 检查用户是否点击了“取消”
        if (string.IsNullOrEmpty(path))
        {
            Debug.Log("加载已取消。");
            return; // 用户取消了操作，函数提前退出
        }

        // 3. 检查文件是否存在
        if (!File.Exists(path))
        {
            Debug.LogError($"加载失败: 文件未找到于 {path}");
            return;
        }

        string[] lines;
        try
        {
            // 4. 读取文件的所有行
            lines = File.ReadAllLines(path);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"读取文件失败: {e.Message}");
            return;
        }

        // 5. 关键：调用 ResetToDrawingMode() 来清空所有现有数据
        ResetToDrawingMode(); //

        // 6. 解析文件内容 (从上到下)
        for (int i = 0; i < lines.Length; i++)
        {
            int mapY = (mHeight - 1) - i; //
            if (mapY < 0) break;

            string line = lines[i];
            for (int mapX = 0; mapX < line.Length; mapX++)
            {
                if (mapX >= mWidth) break;

                char tileChar = line[mapX];

                // 7. 填充数据 (R = 路径)
                if (tileChar == 'R')
                {
                    playerSelectedPath.Add(new Vector2i(mapX, mapY)); //
                }
            }
        }

        // 8. 高效地更新视觉效果 (将加载的路径“涂”成绿色)
        foreach (Vector2i pathTile in playerSelectedPath)
        {
            if (pathTile.x >= 0 && pathTile.x < mWidth && pathTile.y >= 0 && pathTile.y < mHeight)
            {
                tilesSprites[pathTile.x, pathTile.y].enabled = true;
                tilesSprites[pathTile.x, pathTile.y].sprite = mDirtSprites[0]; //
                tilesSprites[pathTile.x, pathTile.y].color = new Color(0.5f, 1f, 0.5f, 0.5f); //
                tilesSprites[pathTile.x, pathTile.y].transform.localScale = Vector3.one; //
                tilesSprites[pathTile.x, pathTile.y].transform.eulerAngles = Vector3.zero; //
            }
        }

        Debug.Log($"关卡已成功从 {path} 加载！");
    }
#endif

    /// <summary>
    /// (新) 读取由 Python 脚本生成的 my-level-output.lvl 文件，
    /// 解析其内容以填充关卡数据，然后启动试玩模式。
    /// </summary>
    private void LoadGeneratedLevel()
    {
        string generatedLevelPath = Path.Combine(@"C:\GitHub\sturgeon-pub", "work", "my-level-output.lvl");

        if (!File.Exists(generatedLevelPath))
        {
            Debug.LogError($"加载失败: 未找到文件 {generatedLevelPath}");
            return;
        }

        string[] lines;
        try { lines = File.ReadAllLines(generatedLevelPath); }
        catch (System.Exception e) { Debug.LogError(e.Message); return; }

        playerSelectedPath.Clear();
        // --- 修复：移除 dangerZoneTiles.Clear() ---

        List<string> levelGridLines = new List<string>();
        foreach (string line in lines)
        {
            if (line.StartsWith("META")) break;
            levelGridLines.Add(line);
        }

        int fileHeight = levelGridLines.Count;
        for (int i = 0; i < fileHeight; i++)
        {
            int mapY = (fileHeight - 1) - i;
            if (mapY < 0 || mapY >= mHeight) continue;

            string line = levelGridLines[i];
            for (int mapX = 0; mapX < line.Length; mapX++)
            {
                if (mapX >= mWidth) break;
                char tileChar = line[mapX];

                if (tileChar == '-')
                {
                    playerSelectedPath.Add(new Vector2i(mapX, mapY));
                }
            }
        }

        Debug.Log($"已成功解析 {playerSelectedPath.Count} 个可通行格子。");
        StartTrialMode();
    }
}