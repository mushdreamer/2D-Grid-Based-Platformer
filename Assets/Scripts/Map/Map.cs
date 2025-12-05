using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Algorithms;

// --- 修复关键点：必须将 TileType 定义在 Map 类之外 ---
// 这样 MapRoomData.cs 才能直接访问到它，而不需要写成 Map.TileType
[System.Serializable]
public enum TileType
{
    Empty,
    Block,
    OneWay,
    Danger
}
// ----------------------------------------------------

// 保持类名不变，添加 partial 关键字
[System.Serializable]
public partial class Map : MonoBehaviour
{
    // --- 内部枚举定义 (这些原本就在内部，保持不变) ---
    public enum GamePhase { Drawing, TrialPlay }

    public enum BrushType { StartPoint, Path, EndPoint }

    // --- 变量定义 (全部保留在主文件) ---
    public Vector3 position;
    public SpriteRenderer tilePrefab;
    public PathFinderFast mPathFinder;
    [HideInInspector] public byte[,] mGrid;
    [HideInInspector] private TileType[,] tiles;
    private SpriteRenderer[,] tilesSprites;
    public Transform mSpritesContainer;
    static public int cTileSize = 16;

    [Header("Drawing Settings")]
    public Color gridColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
    [Range(1, 10)] public int brushSize = 1;
    public GameObject brushPreviewPrefab;
    private GameObject brushPreviewInstance;

    public int mWidth = 50;
    public int mHeight = 42;

    [Header("Drawing Mode Size")]
    public bool autoSizeToCamera = true;
    public int manualWidth = 100;
    public int manualHeight = 60;

    [Header("Brushes")]
    public BrushType currentBrush = BrushType.Path;
    private Vector2i startTile = new Vector2i(-1, -1);
    private Vector2i endTile = new Vector2i(-1, -1);

    [Header("Gameplay State")]
    public GamePhase currentPhase = GamePhase.Drawing;

    [Header("Game Elements")]
    public GameObject spikePrefab; // 在 Inspector 中拖入你的 Spike Prefab
    private List<GameObject> spawnedSpikes = new List<GameObject>(); // 用于记录生成的尖刺，方便清除

    [Header("PCG")]
    public LevelGenerator levelGenerator; // 在 Inspector 中拖入 LevelGenerator 组件

    [Header("Visualization")]
    public LineRenderer guideLineRenderer;

    // 线程通信标志
    private volatile bool pythonScriptsRunning = false;
    private volatile bool pythonScriptsFinished = false;

    private HashSet<Vector2i> playerSelectedPath = new HashSet<Vector2i>();

    public MapRoomData mapRoomSimple;
    public MapRoomData mapRoomOneWay;
    public Camera gameCamera;
    public Bot player;
    bool[] inputs;
    bool[] prevInputs;

    public KeyCode goLeftKey = KeyCode.A;
    public KeyCode goRightKey = KeyCode.D;
    public KeyCode goJumpKey = KeyCode.W;
    public KeyCode goDownKey = KeyCode.S;

    public RectTransform sliderHigh;
    public RectTransform sliderLow;

    public List<Sprite> mDirtSprites;
    System.Random mRandomNumber;

    // --- 生命周期方法 ---

    public void Start()
    {
        mRandomNumber = new System.Random();
        Application.targetFrameRate = 60;
        inputs = new bool[(int)KeyInput.Count];
        prevInputs = new bool[(int)KeyInput.Count];
        position = transform.position;

        if (currentPhase == GamePhase.TrialPlay)
        {
            Debug.Log("Starting directly in PLAYING mode.");
            var mapRoom = mapRoomOneWay;
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

                    if (mapRoom.tileData[y * mWidth + x] == TileType.Empty) SetTile(x, y, TileType.Empty);
                    else if (mapRoom.tileData[y * mWidth + x] == TileType.Block) SetTile(x, y, TileType.Block);
                    else SetTile(x, y, TileType.OneWay);
                }
            }

            for (int y = 0; y < mHeight; ++y) { tiles[1, y] = TileType.Block; tiles[mWidth - 2, y] = TileType.Block; }
            for (int x = 0; x < mWidth; ++x) { tiles[x, 1] = TileType.Block; tiles[x, mHeight - 2] = TileType.Block; }

            player.gameObject.SetActive(true);
            player.BotInit(inputs, prevInputs);
            player.mMap = this;
            player.mPosition = new Vector2(2 * Map.cTileSize, (mHeight / 2) * Map.cTileSize + player.mAABB.HalfSizeY);
        }
        else
        {
            Debug.Log("Starting in DRAWING mode.");
            Camera.main.orthographicSize = Camera.main.pixelHeight / 2;

            if (cTileSize <= 0) { Debug.LogError("cTileSize 必须大于 0!"); return; }

            if (autoSizeToCamera)
            {
                mWidth = Mathf.FloorToInt((float)Camera.main.pixelWidth / (float)cTileSize);
                mHeight = Mathf.FloorToInt((float)Camera.main.pixelHeight / (float)cTileSize);
                Debug.Log($"Map 尺寸已[自动]调整为: {mWidth} x {mHeight}");
            }
            else
            {
                mWidth = manualWidth;
                mHeight = manualHeight;
                Debug.Log($"Map 尺寸已[手动]设置为: {mWidth} x {mHeight}");
            }

            tiles = new TileType[mWidth, mHeight];
            tilesSprites = new SpriteRenderer[mWidth, mHeight];
            mGrid = new byte[Mathf.NextPowerOfTwo((int)mWidth), Mathf.NextPowerOfTwo((int)mHeight)];
            InitPathFinder();

            for (int y = 0; y < mHeight; ++y)
            {
                for (int x = 0; x < mWidth; ++x)
                {
                    tilesSprites[x, y] = Instantiate<SpriteRenderer>(tilePrefab);
                    tilesSprites[x, y].transform.parent = transform;
                    tilesSprites[x, y].transform.position = position + new Vector3(cTileSize * x, cTileSize * y, 10.0f);
                }
            }
            player.gameObject.SetActive(false);
            ResetToDrawingMode();
        }

        if (brushPreviewPrefab != null)
        {
            brushPreviewInstance = Instantiate(brushPreviewPrefab, transform);
            brushPreviewInstance.SetActive(currentPhase == GamePhase.Drawing);
        }
    }

    void Update()
    {
        if (pythonScriptsFinished)
        {
            pythonScriptsFinished = false;
            Debug.Log("主线程收到信号。正在加载生成的关卡...");
            LoadGeneratedLevel();
        }

        switch (currentPhase)
        {
            case GamePhase.Drawing:
                if (Input.GetKeyDown(KeyCode.Alpha1)) { currentBrush = BrushType.StartPoint; Debug.Log("Brush: Start Point"); }
                else if (Input.GetKeyDown(KeyCode.Alpha2)) { currentBrush = BrushType.Path; Debug.Log("Brush: Path"); }
                else if (Input.GetKeyDown(KeyCode.Alpha3)) { currentBrush = BrushType.EndPoint; Debug.Log("Brush: End Point"); }
                else if (Input.GetKeyDown(KeyCode.P)) { SaveLevelToFile(); }
                else if (Input.GetKeyDown(KeyCode.L)) { LoadLevelFromFile(); }
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { HandleEnterKeySave(); }

                HandleDrawingInput();

                if (Input.GetKeyDown(KeyCode.Space))
                {
                    if (startTile.x == -1 || endTile.x == -1) Debug.LogError("无法开始：请先设置 起点(1) 和 终点(3)！");
                    else StartTrialMode();
                }

                // --- 修正：将 G 键逻辑移到 break 之前 ---
                if (Input.GetKeyDown(KeyCode.G))
                {
                    if (levelGenerator == null)
                    {
                        Debug.LogError("未绑定 LevelGenerator！请将 LevelGenerator 拖拽到 Map 组件的相应槽位中。");
                        break;
                    }

                    if (startTile.x == -1) startTile = new Vector2i(2, 5);
                    if (endTile.x == -1) endTile = new Vector2i(mWidth - 5, 5);

                    Debug.Log("生成 IWBTG 关卡中...");
                    ClearMapToEmpty();
                    levelGenerator.GenerateIWBTGLevel(startTile, endTile);
                }
                // ----------------------------------------
                break; // break 必须在所有 case 逻辑之后

            case GamePhase.TrialPlay:
                HandlePlayingInput();
                if (Input.GetKeyDown(KeyCode.Backspace)) ReturnToDrawingMode();
                else if (Input.GetKeyDown(KeyCode.R)) ResetToDrawingMode();
                break;
        }
    }

    public void FillMapWithBlocks()
    {
        for (int y = 0; y < mHeight; y++)
            for (int x = 0; x < mWidth; x++)
                SetTile(x, y, TileType.Block);
    }

    public void ClearMapToEmpty()
    {
        for (int y = 0; y < mHeight; y++)
            for (int x = 0; x < mWidth; x++)
                SetTile(x, y, TileType.Empty);
    }

    public void ApplyGeneratedPath(List<Vector2i> path, List<ReplayFrame> replay, List<Vector3> trajectoryPoints)
    {
        // 0. 清理旧的尖刺
        foreach (var spike in spawnedSpikes)
        {
            if (spike != null) Destroy(spike);
        }
        spawnedSpikes.Clear();

        // 1. 填满墙
        FillMapWithBlocks();

        // 2. 更新路径数据
        playerSelectedPath.Clear();
        foreach (var p in path)
        {
            playerSelectedPath.Add(p);
        }

        // 3. 生成地形
        GenerateLevelFromTolerance();

        // 4. 清空起终点
        if (startTile.x != -1) { SetTile(startTile.x, startTile.y, TileType.Empty); SetTile(startTile.x, startTile.y - 1, TileType.Block); }
        if (endTile.x != -1) { SetTile(endTile.x, endTile.y, TileType.Empty); SetTile(endTile.x, endTile.y - 1, TileType.Block); }

        Debug.Log(">>> 地图生成完毕。绘制通关路径...");

        // --- 新增：绘制通关红线 ---
        if (guideLineRenderer != null && trajectoryPoints != null)
        {
            guideLineRenderer.positionCount = trajectoryPoints.Count;
            guideLineRenderer.SetPositions(trajectoryPoints.ToArray());
            guideLineRenderer.enabled = true; // 确保它是显示的
        }
        // -------------------------

        StartTrialMode();

        if (player != null)
        {
            player.StartReplay(replay);
        }
    }

    public void GameOver()
    {
        if (currentPhase == GamePhase.TrialPlay)
        {
            Debug.Log(">>> 玩家死亡！正在重置到起点...");

            if (player != null)
            {
                // 1. 停止录像，把控制权交给玩家
                player.StopReplay();

                // 2. 复活到起点
                if (startTile.x != -1)
                {
                    Vector2 startPos = GetMapTilePosition(startTile) + new Vector2(0, player.mAABB.HalfSizeY);
                    player.mPosition = startPos;
                    player.transform.position = new Vector3(startPos.x, startPos.y, player.transform.position.z);
                }

                // 3. 重置物理状态
                player.mSpeed = Vector2.zero;
                player.mCurrentState = Character.CharacterState.Stand;
                player.mOnGround = true;

                // --- 关键：确保对象是激活的 ---
                player.gameObject.SetActive(true);
            }
        }
        else
        {
            ResetToDrawingMode();
        }
    }

    void FixedUpdate()
    {
        if (currentPhase == GamePhase.TrialPlay && player.gameObject.activeInHierarchy)
        {
            player.BotUpdate();
        }
    }
}