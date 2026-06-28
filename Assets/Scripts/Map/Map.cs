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
    public enum BrushType { StartPoint, Path, EndPoint, SurvivalSpace }

    public Vector3 position;
    public SpriteRenderer tilePrefab;
    public PathFinderFast mPathFinder;
    [HideInInspector] public byte[,] mGrid;

    private bool[,] mItemGrid;

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
    public Vector2i startTile = new Vector2i(-1, -1);
    public Vector2i endTile = new Vector2i(-1, -1);

    [Header("Gameplay State")]
    public GamePhase currentPhase = GamePhase.Drawing;
    private bool isLevelComplete = false;

    [Header("Game Elements")]
    public GameObject spikePrefab;
    public GameObject itemPrefab;

    public GameObject checkpointPrefab;
    public Vector2i checkpointSize = new Vector2i(1, 1);

    public GameObject finishPrefab;
    public Vector2i finishSize = new Vector2i(2, 2);

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


    private volatile bool pythonScriptsRunning = false;
    private volatile bool pythonScriptsFinished = false;

    private HashSet<Vector2i> playerSelectedPath = new HashSet<Vector2i>();

    public HashSet<Vector2i> survivalSpaceTiles = new HashSet<Vector2i>();
    public Dictionary<Vector2i, int> survivalSpaceStrokeOrder = new Dictionary<Vector2i, int>();
    public int currentSurvivalStrokeIndex = 0;

    private List<GameObject> debugSafeZoneVisuals = new List<GameObject>();

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

    private TileType[,] initialTilesBackup;

    public void Start()
    {
        mRandomNumber = new System.Random();
        Application.targetFrameRate = 60;
        inputs = new bool[(int)KeyInput.Count];
        prevInputs = new bool[(int)KeyInput.Count];
        position = transform.position;

        Time.timeScale = 1.0f;
        isLevelComplete = false;

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
            mItemGrid = new bool[mWidth, mHeight];

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
            mItemGrid = new bool[mWidth, mHeight];

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

    public void LevelComplete()
    {
        if (isLevelComplete) return;

        Debug.Log("<color=yellow>VICTORY! Level Finished.</color>");
        isLevelComplete = true;

        if (player != null)
        {
            player.StopReplay();
            player.mSpeed = Vector2.zero;
            player.mCurrentState = Character.CharacterState.Stand;
        }

        Time.timeScale = 0f;
    }

    void OnGUI()
    {
        if (isLevelComplete)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 60;
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = Color.yellow;
            GUI.color = Color.black;
            GUI.Label(new Rect(2, 2, Screen.width, Screen.height), "VICTORY!", style);
            GUI.color = Color.yellow;
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), "VICTORY!", style);

            style.fontSize = 20;
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(0, 50, Screen.width, Screen.height), "Press 'R' to Restart", style);
        }
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
                else if (Input.GetKeyDown(KeyCode.Alpha4)) { currentBrush = BrushType.SurvivalSpace; Debug.Log("Brush: Survival Space"); }
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

                    // 【绝对解耦】不再强行塞硬编码的值，将权限完全交给可视化面板设置的参数！
                    levelGenerator.GenerateEvolutionaryMapElitesLibrary(startTile, endTile);
                }

                break;

            case GamePhase.TrialPlay:
                HandlePlayingInput();
                if (Input.GetKeyDown(KeyCode.Backspace)) ReturnToDrawingMode();
                else if (Input.GetKeyDown(KeyCode.R)) ResetToDrawingMode();
                break;
        }
    }

    public void ShowSurvivalSpaceVisuals()
    {
        ClearSurvivalSpaceVisuals();

        if (mDirtSprites == null || mDirtSprites.Count == 0) return;

        foreach (var tile in survivalSpaceTiles)
        {
            GameObject vis = new GameObject("SafeZoneVis");
            vis.transform.position = GetMapTilePosition(tile);
            vis.transform.parent = transform;

            SpriteRenderer sr = vis.AddComponent<SpriteRenderer>();
            sr.sprite = mDirtSprites[0];
            sr.color = new Color(0f, 1f, 0f, 0.3f);
            sr.sortingOrder = 15;

            debugSafeZoneVisuals.Add(vis);
        }
    }

    public void ClearSurvivalSpaceVisuals()
    {
        foreach (var obj in debugSafeZoneVisuals)
        {
            if (obj != null) Destroy(obj);
        }
        debugSafeZoneVisuals.Clear();
    }

    public void BackupMapState()
    {
        initialTilesBackup = new TileType[mWidth, mHeight];
        for (int x = 0; x < mWidth; x++)
        {
            for (int y = 0; y < mHeight; y++)
            {
                initialTilesBackup[x, y] = GetTile(x, y);
            }
        }
        Debug.Log("Map: 初始状态已备份。");
    }

    public void ResetMapToInitial()
    {
        if (initialTilesBackup == null) return;

        var renderers = FindObjectsOfType<SpriteRenderer>();
        foreach (var sr in renderers)
        {
            if (sr.gameObject.name == "DynamicBlock") Destroy(sr.gameObject);
        }

        for (int x = 0; x < mWidth; x++)
        {
            for (int y = 0; y < mHeight; y++)
            {
                SetTile(x, y, initialTilesBackup[x, y]);
            }
        }
        Debug.Log("Map: 地图地形已还原。");
    }

    public void FillMapWithBlocks()
    {
        for (int y = 0; y < mHeight; y++)
            for (int x = 0; x < mWidth; x++)
                SetTile(x, y, TileType.Block);
    }

    public void ClearMapToEmpty()
    {
        if (mItemGrid == null || mItemGrid.GetLength(0) != mWidth || mItemGrid.GetLength(1) != mHeight)
        {
            mItemGrid = new bool[mWidth, mHeight];
        }
        else
        {
            System.Array.Clear(mItemGrid, 0, mItemGrid.Length);
        }

        for (int y = 0; y < mHeight; y++)
            for (int x = 0; x < mWidth; x++)
                SetTile(x, y, TileType.Empty);
    }

    public void ApplyGeneratedPath(List<Vector2i> path, List<ReplayFrame> replay, List<Vector3> trajectoryPoints, HashSet<int> safeColumns)
    {
        foreach (var obj in spawnedObjects) if (obj != null) Destroy(obj);
        spawnedObjects.Clear();

        this.safeLandingColumns = new HashSet<int>(safeColumns);

        playerSelectedPath.Clear();
        foreach (var p in path) playerSelectedPath.Add(p);

        for (int x = 0; x < mWidth; x++)
        {
            for (int y = 0; y < mHeight; y++)
            {
                TileType type = GetTile(x, y);
                if (type == TileType.Danger)
                {
                    bool flipped = false;
                    if (y < mHeight - 1 && GetTile(x, y + 1) == TileType.Block) flipped = true;
                    SpawnSpikeAt(x, y, flipped);
                }
            }
        }

        if (startTile.x != -1)
        {
            BuildPlatformAt(startTile.x, startTile.y - 1, 3);
            SpawnSpecialItemAt(startTile.x, startTile.y, Collectible.ItemType.Checkpoint, checkpointSize);
        }

        if (endTile.x != -1)
        {
            BuildPlatformAt(endTile.x, endTile.y - 1, 3);
            SpawnSpecialItemAt(endTile.x, endTile.y, Collectible.ItemType.Finish, finishSize);
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

        Time.timeScale = 1.0f;
        isLevelComplete = false;

        if (startTile.x != -1)
        {
            SetTile(startTile.x, startTile.y, TileType.Empty);
            SetTile(startTile.x, startTile.y + 1, TileType.Empty);
            Vector2 startPos = GetMapTilePosition(startTile) + new Vector2(0, player.mAABB.HalfSizeY);
            player.mPosition = startPos;
            player.transform.position = new Vector3(startPos.x, startPos.y, player.transform.position.z);
        }

        BackupMapState();

        currentPhase = GamePhase.TrialPlay;
        if (player != null) player.StartReplay(replay);

        ShowSurvivalSpaceVisuals();

        Debug.Log(">>> 已进入生成关卡的试玩模式 (State Enumeration Core)");
    }

    public void ConvertRegionToDynamic(Vector2i center, int width, int height, object motion, float speed)
    {
        // Experimental DDA terrain conversion is intentionally disabled in the core state-enumeration path.
        Debug.Log("Dynamic terrain conversion is disabled in the state-enumeration core path.");
    }

    private void SpawnSpikeAt(int x, int y, bool flipped = false)
    {
        if (spikePrefab == null) return;
        if (x < 0 || x >= mWidth || y < 0 || y >= mHeight) return;

        mItemGrid[x, y] = true;

        SetTile(x, y, TileType.Danger);
        tilesSprites[x, y].enabled = false;

        Vector2 worldPos = GetMapTilePosition(x, y);
        Vector3 spawnPos = new Vector3(worldPos.x, worldPos.y, -2f);

        GameObject newSpike = Instantiate(spikePrefab, spawnPos, Quaternion.identity);
        newSpike.transform.parent = transform;

        SpriteRenderer sr = newSpike.GetComponent<SpriteRenderer>();
        if (sr != null && trapSprites != null && trapSprites.Count > 0)
        {
            int index = Mathf.Clamp(currentThemeTrapIndex, 0, trapSprites.Count - 1);
            sr.sprite = trapSprites[index];
        }

        if (flipped)
        {
            newSpike.transform.localScale = new Vector3(1, -1, 1);
        }

        spawnedObjects.Add(newSpike);
    }

    private void SpawnItemAt(int x, int y, Collectible.ItemType type)
    {
        SpawnSpecialItemAt(x, y, type, new Vector2i(1, 1));
    }

    private void SpawnSpecialItemAt(int centerX, int centerY, Collectible.ItemType type, Vector2i size)
    {
        GameObject prefabToSpawn = null;
        if (type == Collectible.ItemType.Checkpoint) prefabToSpawn = checkpointPrefab;
        else if (type == Collectible.ItemType.Finish) prefabToSpawn = finishPrefab;

        if (prefabToSpawn == null) prefabToSpawn = itemPrefab;
        if (prefabToSpawn == null) return;

        int width = size.x;
        int height = size.y;

        int startX = centerX;
        int startY = centerY;

        for (int x = startX; x < startX + width; x++)
        {
            for (int y = startY; y < startY + height; y++)
            {
                if (x >= 0 && x < mWidth && y >= 0 && y < mHeight)
                {
                    SetTile(x, y, TileType.Empty);
                    mItemGrid[x, y] = true;
                }
            }
        }

        Vector2 minPos = GetMapTilePosition(startX, startY);
        Vector2 maxPos = GetMapTilePosition(startX + width - 1, startY + height - 1);

        Vector2 worldCenter = (minPos + maxPos) / 2.0f;
        Vector3 spawnPos = new Vector3(worldCenter.x, worldCenter.y, -3f);

        GameObject obj = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);
        obj.transform.parent = transform;

        Collectible col = obj.GetComponent<Collectible>();
        if (col == null) col = obj.AddComponent<Collectible>();
        col.type = type;

        spawnedObjects.Add(obj);
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

        if (type == TileType.Block && mItemGrid != null && mItemGrid[x, y])
        {
            return;
        }

        tiles[x, y] = type;
        SpriteRenderer sr = tilesSprites[x, y];

        if (type == TileType.Block)
        {
            mGrid[x, y] = 0;
            sr.enabled = true;
            sr.transform.localScale = Vector3.one;
            sr.transform.eulerAngles = Vector3.zero;
            sr.color = Color.white;

            if (terrainSprites != null && terrainSprites.Count > 0)
            {
                int index = Mathf.Clamp(currentThemeTerrainIndex, 0, terrainSprites.Count - 1);
                sr.sprite = terrainSprites[index];
            }
            else
            {
                if (mDirtSprites != null && mDirtSprites.Count > 1)
                    sr.sprite = mDirtSprites[1];
            }
        }
        else if (type == TileType.Danger)
        {
            mGrid[x, y] = 1;
            sr.enabled = false;
        }
        else if (type == TileType.Empty)
        {
            mGrid[x, y] = 1;
            sr.enabled = false;
        }
    }

    public void GameOver()
    {
        if (currentPhase == GamePhase.TrialPlay)
        {
            Debug.Log(">>> 玩家死亡！开始重置...");

            ResetMapToInitial();

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