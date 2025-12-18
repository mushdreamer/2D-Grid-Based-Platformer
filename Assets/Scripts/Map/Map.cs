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
    public GameObject spikePrefab;   // 尖刺的基础Prefab
    public GameObject itemPrefab;    // 物品的基础Prefab (挂载 Collectible 脚本)
    private List<GameObject> spawnedObjects = new List<GameObject>(); // 统一管理生成的尖刺和物品

    [Header("Visual Assets (IWBTG Style)")]
    // --- 新增：资源库，请在 Inspector 中拖入对应的图片 ---
    public SpriteRenderer backgroundRenderer; // 拖入场景中的 "Bg" 物体
    public List<Sprite> backgroundSprites;    // 7个背景
    public List<Sprite> terrainSprites;       // 2个地形 (Block样式)
    public List<Sprite> trapSprites;          // 15个陷阱
    public List<Sprite> characterSprites;     // 4个角色皮肤
    public List<Sprite> fruitSprites;         // 水果图片
    public List<Sprite> checkpointSprites;    // 存档点图片

    // 当前关卡的主题索引
    private int currentThemeBgIndex = 0;
    private int currentThemeTerrainIndex = 0;
    private int currentThemeTrapIndex = 0;

    [Header("PCG")]
    public LevelGenerator levelGenerator;

    [Header("Visualization")]
    public LineRenderer guideLineRenderer;

    public AdversarialDirector director;

    private volatile bool pythonScriptsRunning = false;
    private volatile bool pythonScriptsFinished = false;

    private HashSet<Vector2i> playerSelectedPath = new HashSet<Vector2i>();

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

    public List<Sprite> mDirtSprites; // 旧的自动拼贴资源，保留以防万一
    System.Random mRandomNumber;

    public void Start()
    {
        mRandomNumber = new System.Random();
        Application.targetFrameRate = 60;
        inputs = new bool[(int)KeyInput.Count];
        prevInputs = new bool[(int)KeyInput.Count];
        position = transform.position;

        // 初始化随机种子
        Random.InitState((int)System.DateTime.Now.Ticks);

        // 如果场景里有背景对象，先随机一个背景
        if (backgroundRenderer != null && backgroundSprites.Count > 0)
        {
            RandomizeTheme();
        }

        // --- 确保对抗导演已初始化 ---
        if (director != null)
        {
            director.map = this;
            director.targetPlayer = player;
            director.enabled = false; // 默认关闭，试玩时开启
        }

        if (currentPhase == GamePhase.TrialPlay)
        {
            // TrialPlay 初始化逻辑
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
            }
            else
            {
                mWidth = manualWidth;
                mHeight = manualHeight;
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

    // 新增：随机化关卡视觉主题
    public void RandomizeTheme()
    {
        if (backgroundSprites.Count > 0)
        {
            currentThemeBgIndex = Random.Range(0, backgroundSprites.Count);
            if (backgroundRenderer != null)
                backgroundRenderer.sprite = backgroundSprites[currentThemeBgIndex];
        }

        if (terrainSprites.Count > 0)
            currentThemeTerrainIndex = Random.Range(0, terrainSprites.Count);

        if (trapSprites.Count > 0)
            currentThemeTrapIndex = Random.Range(0, trapSprites.Count);

        if (characterSprites.Count > 0 && player != null)
        {
            int charIndex = Random.Range(0, characterSprites.Count);
            player.SetSkin(characterSprites[charIndex]);
        }
    }

    // 设置新的存档点
    public void SetCheckpoint(Vector2i newStartTile)
    {
        startTile = newStartTile;
        Debug.Log("Checkpoint Updated to: " + startTile);
    }

    void Update()
    {
        if (pythonScriptsFinished)
        {
            pythonScriptsFinished = false;
            LoadGeneratedLevel();
        }

        switch (currentPhase)
        {
            case GamePhase.Drawing:
                if (Input.GetKeyDown(KeyCode.Alpha1)) { currentBrush = BrushType.StartPoint; Debug.Log("Brush: Start Point"); }
                else if (Input.GetKeyDown(KeyCode.Alpha2)) { currentBrush = BrushType.Path; Debug.Log("Brush: Path"); }
                else if (Input.GetKeyDown(KeyCode.Alpha3)) { currentBrush = BrushType.EndPoint; Debug.Log("Brush: End Point"); }
                else if (Input.GetKeyDown(KeyCode.Return)) { HandleEnterKeySave(); }

                HandleDrawingInput();

                if (Input.GetKeyDown(KeyCode.Space))
                {
                    if (startTile.x == -1 || endTile.x == -1) Debug.LogError("无法开始：请先设置 起点(1) 和 终点(3)！");
                    else StartTrialMode();
                }

                if (Input.GetKeyDown(KeyCode.G))
                {
                    if (levelGenerator == null) { Debug.LogError("未绑定 LevelGenerator！"); break; }
                    if (startTile.x == -1) startTile = new Vector2i(2, 5);
                    if (endTile.x == -1) endTile = new Vector2i(mWidth - 5, 5);

                    // 每次生成时，随机切换主题！
                    RandomizeTheme();

                    Debug.Log("生成 MAP-Elites 关卡库中...");
                    ClearMapToEmpty();
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

    // --- ApplyGeneratedPath ---
    public void ApplyGeneratedPath(List<Vector2i> path, List<ReplayFrame> replay, List<Vector3> trajectoryPoints, HashSet<int> safeColumns)
    {
        // 0. 清理旧物体 (尖刺 + 道具) & 清理导演的陷阱
        foreach (var obj in spawnedObjects) if (obj != null) Destroy(obj);
        spawnedObjects.Clear();
        if (director != null) director.ClearTraps();

        // 1. 保存安全列并清空
        this.safeLandingColumns = new HashSet<int>(safeColumns);
        ClearMapToEmpty();

        // 2. 更新路径数据
        playerSelectedPath.Clear();
        foreach (var p in path) playerSelectedPath.Add(p);

        // 3. 生成浮岛、尖刺和道具
        GenerateIslandsFromPath(trajectoryPoints);

        // 4. 设置起点终点平台
        if (startTile.x != -1) BuildPlatformAt(startTile.x, startTile.y - 1, 3);
        if (endTile.x != -1) BuildPlatformAt(endTile.x, endTile.y - 1, 3);

        // 在终点生成一个 Checkpoint 或 奖杯
        if (endTile.x != -1) SpawnItemAt(endTile.x, endTile.y, Collectible.ItemType.Checkpoint);

        Debug.Log(">>> 地图生成完毕。");

        if (guideLineRenderer != null && trajectoryPoints != null)
        {
            guideLineRenderer.positionCount = trajectoryPoints.Count;
            guideLineRenderer.SetPositions(trajectoryPoints.ToArray());
            guideLineRenderer.enabled = true;
        }

        // --- 手动进入试玩 ---
        if (brushPreviewInstance != null) brushPreviewInstance.SetActive(false);
        Cursor.visible = true;

        player.gameObject.SetActive(true);
        player.BotInit(inputs, prevInputs);
        player.mMap = this;

        if (startTile.x != -1)
        {
            SetTile(startTile.x, startTile.y, TileType.Empty);
            SetTile(startTile.x, startTile.y + 1, TileType.Empty);
            Vector2 startPos = GetMapTilePosition(startTile) + new Vector2(0, player.mAABB.HalfSizeY);
            player.mPosition = startPos;
            player.transform.position = new Vector3(startPos.x, startPos.y, player.transform.position.z);
        }

        currentPhase = GamePhase.TrialPlay;

        // --- 录像回放与对抗导演逻辑 ---
        if (player != null)
        {
            player.StartReplay(replay);
            // 录像回放期间，禁用导演，以免陷阱干扰演示
            if (director != null) director.enabled = false;
        }

        Debug.Log(">>> 已进入生成关卡的试玩模式 (IWBTG Style)");
    }

    public void GameOver()
    {
        if (director != null) director.ClearTraps();

        if (currentPhase == GamePhase.TrialPlay)
        {
            Debug.Log(">>> 玩家死亡！");
            if (player != null)
            {
                player.StopReplay();
                // 复活到最近的 checkpoint (startTile 被 Checkpoint 物品更新过)
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

                // 玩家复活后，启用对抗导演
                if (director != null) director.enabled = true;
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

            // 如果录像被玩家中断了，启用对抗导演
            // (注意：AdversarialDirector 只有在玩家全速奔跑时才工作，所以开启它很安全)
            if (director != null && !director.enabled && player.mCurrentAction == Bot.BotAction.None) // None means player control in this context
            {
                // 检查 Bot 内部状态，如果是 isReplaying=false，则开启导演
                // 这里通过反射或公开属性检查最好，这里假设玩家控制时 director 应开启
                director.enabled = true;
            }
        }
    }
}