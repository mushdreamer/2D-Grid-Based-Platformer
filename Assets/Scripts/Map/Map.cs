using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Algorithms;

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
    public enum GamePhase { Drawing, TrialPlay }
    public enum BrushType { StartPoint, Path, EndPoint }

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
    public GameObject spikePrefab;
    private List<GameObject> spawnedSpikes = new List<GameObject>();

    [Header("PCG")]
    public LevelGenerator levelGenerator;

    [Header("Visualization")]
    public LineRenderer guideLineRenderer;

    public AdversarialDirector director;

    private volatile bool pythonScriptsRunning = false;
    private volatile bool pythonScriptsFinished = false;

    private HashSet<Vector2i> playerSelectedPath = new HashSet<Vector2i>();

    // --- 新增：用于存储生成器传来的安全落地列 ---
    public HashSet<int> safeLandingColumns = new HashSet<int>();

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

                if (Input.GetKeyDown(KeyCode.G))
                {
                    if (levelGenerator == null)
                    {
                        Debug.LogError("未绑定 LevelGenerator！请将 LevelGenerator 拖拽到 Map 组件的相应槽位中。");
                        break;
                    }

                    if (startTile.x == -1) startTile = new Vector2i(2, 5);
                    if (endTile.x == -1) endTile = new Vector2i(mWidth - 5, 5);

                    Debug.Log("生成 MAP-Elites 关卡库中...");
                    ClearMapToEmpty();

                    // --- 修改点：调用新的 MAP-Elites 入口方法 ---
                    // 参数 50 是迭代次数，可以根据需要调整
                    levelGenerator.GenerateMapElitesLibrary(startTile, endTile, 50);
                }
                break;

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

    // 在 Map.cs 中

    public void ApplyGeneratedPath(List<Vector2i> path, List<ReplayFrame> replay, List<Vector3> trajectoryPoints, HashSet<int> safeColumns)
    {
        // 0. 清理旧物体
        foreach (var spike in spawnedSpikes) if (spike != null) Destroy(spike);
        spawnedSpikes.Clear();

        // 1. 保存安全列数据并清空地图（这是关键，确保地图是从空开始构建）
        this.safeLandingColumns = new HashSet<int>(safeColumns);
        ClearMapToEmpty();

        // 2. 更新路径数据
        playerSelectedPath.Clear();
        foreach (var p in path) playerSelectedPath.Add(p);

        // 3. 调用新的“浮岛”生成逻辑
        GenerateIslandsFromPath(trajectoryPoints);

        // 4. 设置起点终点平台
        if (startTile.x != -1) BuildPlatformAt(startTile.x, startTile.y - 1, 3);
        if (endTile.x != -1) BuildPlatformAt(endTile.x, endTile.y - 1, 3);

        Debug.Log(">>> 地图生成完毕。绘制通关路径...");

        // 5. 绘制红线
        if (guideLineRenderer != null && trajectoryPoints != null)
        {
            guideLineRenderer.positionCount = trajectoryPoints.Count;
            guideLineRenderer.SetPositions(trajectoryPoints.ToArray());
            guideLineRenderer.enabled = true;
        }

        // =========================================================
        // [修正]：不要调用 StartTrialMode()，因为它会重置地图！
        // 我们手动执行进入游戏模式所需的初始化步骤：
        // =========================================================

        // A. 隐藏笔刷预览
        if (brushPreviewInstance != null) brushPreviewInstance.SetActive(false);
        Cursor.visible = true;

        // B. 激活玩家并初始化
        player.gameObject.SetActive(true);
        player.BotInit(inputs, prevInputs);
        player.mMap = this;

        // C. 设置玩家位置到起点
        if (startTile.x != -1)
        {
            // 确保出生点上方没有方块卡住
            SetTile(startTile.x, startTile.y, TileType.Empty);
            SetTile(startTile.x, startTile.y + 1, TileType.Empty);

            Vector2 startPos = GetMapTilePosition(startTile) + new Vector2(0, player.mAABB.HalfSizeY);
            player.mPosition = startPos;
            // 同步 Transform，防止画面闪烁
            player.transform.position = new Vector3(startPos.x, startPos.y, player.transform.position.z);
        }

        // D. 切换状态
        currentPhase = GamePhase.TrialPlay;

        // E. 启动回放 (Ghost 演示)
        if (player != null) player.StartReplay(replay);

        Debug.Log(">>> 已进入生成关卡的试玩模式 (IWBTG 风格)");
    }

    // [新增方法]：根据轨迹生成浮空岛和尖刺
    private void GenerateIslandsFromPath(List<Vector3> trajectory)
    {
        if (trajectory == null || trajectory.Count == 0) return;

        // 1. 识别落脚点
        // 我们遍历轨迹，找到 Y 轴速度为 0 或者 轨迹最低点 的位置，视为潜在的平台位置
        // 但更简单的方法是利用 LevelGenerator 传来的 safeLandingColumns
        // 不过为了视觉效果更好，我们结合 trajectory 的 Y 值来确定平台的高度

        // 简单的采样：每隔一段 X 距离，检查轨迹下方的空间
        Dictionary<int, int> columnFloorY = new Dictionary<int, int>();

        foreach (var point in trajectory)
        {
            int x = Mathf.RoundToInt((point.x - position.x) / cTileSize);
            int y = Mathf.RoundToInt((point.y - position.y) / cTileSize);

            // 记录这一列轨迹的最低点，作为潜在的“脚下位置”
            if (!columnFloorY.ContainsKey(x)) columnFloorY[x] = y;
            else if (y < columnFloorY[x]) columnFloorY[x] = y;
        }

        // 2. 生成平台
        foreach (int x in safeLandingColumns)
        {
            if (columnFloorY.ContainsKey(x))
            {
                int footY = columnFloorY[x];
                // 在脚下生成一个平台 (脚下是 footY，所以方块在 footY - 1)
                // 平台宽度随机 2-4
                BuildPlatformAt(x, footY - 1, Random.Range(2, 5));
            }
        }

        // 3. 生成空域尖刺 (IWBTG 特色)
        // 在非安全列的路径下方生成尖刺
        for (int x = 0; x < mWidth; x++)
        {
            // 如果这列不是落脚点，且上方有轨迹经过
            if (!safeLandingColumns.Contains(x) && columnFloorY.ContainsKey(x))
            {
                int trajY = columnFloorY[x];
                // 在轨迹下方一定距离生成尖刺或者悬浮块
                if (Random.value < 0.3f)
                {
                    // 确保尖刺不会直接插在路线上，留出缓冲
                    int spikeY = trajY - Random.Range(4, 8);
                    if (spikeY > 0)
                    {
                        SetTile(x, spikeY, TileType.Block); // 悬浮块
                        SpawnSpikeAt(x, spikeY + 1);        // 块上面的刺
                    }
                }
            }
        }
    }

    // [辅助方法]：在指定位置生成一个小平台
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

    public void GameOver()
    {
        if (director != null) director.ClearTraps();

        if (currentPhase == GamePhase.TrialPlay)
        {
            Debug.Log(">>> 玩家死亡！正在重置到起点...");

            if (player != null)
            {
                player.StopReplay();

                if (startTile.x != -1)
                {
                    Vector2 startPos = GetMapTilePosition(startTile) + new Vector2(0, player.mAABB.HalfSizeY);
                    player.mPosition = startPos;
                    player.transform.position = new Vector3(startPos.x, startPos.y, player.transform.position.z);
                }

                player.mSpeed = Vector2.zero;
                player.mCurrentState = Character.CharacterState.Stand;
                player.mOnGround = true;
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