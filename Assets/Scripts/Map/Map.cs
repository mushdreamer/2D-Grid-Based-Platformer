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
    public GameObject itemPrefab;    // 通用物品 Prefab

    // --- 新增：特定功能的 Prefab ---
    public GameObject checkpointPrefab; // 请拖入起点 Checkpoint Prefab
    public GameObject finishPrefab;     // 请拖入终点 胜利 Prefab
    // ----------------------------

    private List<GameObject> spawnedObjects = new List<GameObject>();

    [Header("Visual Assets")]
    public SpriteRenderer backgroundRenderer;
    public List<Sprite> backgroundSprites;
    public List<Sprite> terrainSprites;
    public List<Sprite> trapSprites;
    public List<Sprite> characterSprites;
    public List<Sprite> fruitSprites;
    public List<Sprite> checkpointSprites;

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

    public List<Sprite> mDirtSprites;
    System.Random mRandomNumber;

    public void Start()
    {
        mRandomNumber = new System.Random();
        Application.targetFrameRate = 60;
        inputs = new bool[(int)KeyInput.Count];
        prevInputs = new bool[(int)KeyInput.Count];
        position = transform.position;

        Random.InitState((int)System.DateTime.Now.Ticks);

        if (backgroundRenderer != null && backgroundSprites.Count > 0)
        {
            RandomizeTheme();
        }

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
            player.gameObject.SetActive(true);
            player.BotInit(inputs, prevInputs);
            player.mMap = this;
            player.mPosition = new Vector2(2 * Map.cTileSize, (mHeight / 2) * Map.cTileSize + player.mAABB.HalfSizeY);
        }
        else
        {
            Debug.Log("Starting in DRAWING mode.");
            Camera.main.orthographicSize = Camera.main.pixelHeight / 2;

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

    public void SetCheckpoint(Vector2i newStartTile)
    {
        startTile = newStartTile;
        Debug.Log("Checkpoint Updated to: " + startTile);
    }

    // --- 新增：胜利逻辑 ---
    public void LevelComplete()
    {
        Debug.Log("<color=yellow>VICTORY! Level Finished.</color>");

        // 胜利后停止角色
        if (player != null)
        {
            player.StopReplay();
            player.mSpeed = Vector2.zero;
            player.mCurrentState = Character.CharacterState.Stand;
        }

        // 禁用对抗导演
        if (director != null) director.enabled = false;

        // 这里可以扩展：弹出胜利UI，或重置关卡
        // Invoke("ResetToDrawingMode", 2.0f); // 例如 2秒后重置
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

    // --- Modified ApplyGeneratedPath ---
    public void ApplyGeneratedPath(List<Vector2i> path, List<ReplayFrame> replay, List<Vector3> trajectoryPoints, HashSet<int> safeColumns)
    {
        foreach (var obj in spawnedObjects) if (obj != null) Destroy(obj);
        spawnedObjects.Clear();
        if (director != null) director.ClearTraps();

        this.safeLandingColumns = new HashSet<int>(safeColumns);
        ClearMapToEmpty();

        playerSelectedPath.Clear();
        foreach (var p in path) playerSelectedPath.Add(p);

        GenerateIslandsFromPath(trajectoryPoints);

        // 4. 设置起点终点平台，并生成对应的 Checkpoint/Finish Prefab
        if (startTile.x != -1)
        {
            BuildPlatformAt(startTile.x, startTile.y - 1, 3);
            // 生成起点 Checkpoint
            SpawnSpecialItemAt(startTile.x, startTile.y, Collectible.ItemType.Checkpoint);
        }

        if (endTile.x != -1)
        {
            BuildPlatformAt(endTile.x, endTile.y - 1, 3);
            // 生成终点 Victory/Finish Checkpoint
            SpawnSpecialItemAt(endTile.x, endTile.y, Collectible.ItemType.Finish);
        }

        Debug.Log(">>> 地图生成完毕。");

        if (guideLineRenderer != null && trajectoryPoints != null)
        {
            guideLineRenderer.positionCount = trajectoryPoints.Count;
            guideLineRenderer.SetPositions(trajectoryPoints.ToArray());
            guideLineRenderer.enabled = true;
        }

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
        if (player != null) player.StartReplay(replay);
        if (director != null) director.enabled = false;

        Debug.Log(">>> 已进入生成关卡的试玩模式 (IWBTG Style)");
    }

    // --- 新增：专门用于生成特殊 Prefab 的辅助方法 ---
    private void SpawnSpecialItemAt(int x, int y, Collectible.ItemType type)
    {
        GameObject prefabToSpawn = null;
        if (type == Collectible.ItemType.Checkpoint) prefabToSpawn = checkpointPrefab;
        else if (type == Collectible.ItemType.Finish) prefabToSpawn = finishPrefab;

        // 如果没有指定特殊 Prefab，回退到通用 itemPrefab
        if (prefabToSpawn == null) prefabToSpawn = itemPrefab;
        if (prefabToSpawn == null) return;

        Vector2 worldPos = GetMapTilePosition(x, y);
        Vector3 spawnPos = new Vector3(worldPos.x, worldPos.y, -3f);

        GameObject obj = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);
        obj.transform.parent = transform;

        // 确保它有 Collectible 组件并设置正确的类型
        Collectible col = obj.GetComponent<Collectible>();
        if (col == null) col = obj.AddComponent<Collectible>();
        col.type = type;

        spawnedObjects.Add(obj);
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

            if (director != null && !director.enabled && player.mCurrentAction == Bot.BotAction.None)
            {
                director.enabled = true;
            }
        }
    }
}