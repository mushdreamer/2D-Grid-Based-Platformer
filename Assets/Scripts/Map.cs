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

    // --- ������ˢ��С ---
    [Header("Drawing Settings")]
    [Range(1, 10)] // ʹ��Range���Ʊ�ˢ��С��1��10֮�䣬��ֹ���ù������Чֵ
    public int brushSize = 1; // Ĭ��Ϊ1����1x1�ĸ���
                              // --- ����������� ---

    // --- �������������� ---
    public GameObject brushPreviewPrefab; // ������Inspector������Prefab
    private GameObject brushPreviewInstance; // �����ڴ����п���ʵ��
    // --- ����������� ---

    /// <summary>
    /// The width of the map in tiles.
    /// </summary>
    public int mWidth = 50;
	/// <summary>
	/// The height of the map in tiles.
	/// </summary>
	public int mHeight = 42;

    //��������
    // ��¼����ѡ�е�״̬
    public enum GamePhase
    {
        Drawing,
        TrialPlay
    }

    [Header("Gameplay State")]
    public GamePhase currentPhase = GamePhase.Drawing;

    // ��һ�� HashSet ���洢���ѡ���·���������꣬��ѯЧ�ʸ�
    private HashSet<Vector2i> playerSelectedPath = new HashSet<Vector2i>();
    //��������

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
        // --- ͨ�ó�ʼ������ ---
        mRandomNumber = new System.Random();
        Application.targetFrameRate = 60;
        inputs = new bool[(int)KeyInput.Count];
        prevInputs = new bool[(int)KeyInput.Count];
        position = transform.position;

        // --- ���� Inspector ��������������γ�ʼ�� ---

        // �������Inspector�����õ��� "TrialPlay"
        if (currentPhase == GamePhase.TrialPlay)
        {
            Debug.Log("Starting directly in PLAYING mode.");

            var mapRoom = mapRoomOneWay; // ���� mapRoomSimple
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

                    // �� ScriptableObject ���عؿ�����
                    if (mapRoom.tileData[y * mWidth + x] == TileType.Empty)
                        SetTile(x, y, TileType.Empty);
                    else if (mapRoom.tileData[y * mWidth + x] == TileType.Block)
                        SetTile(x, y, TileType.Block);
                    else
                        SetTile(x, y, TileType.OneWay);
                }
            }

            // *** ������: ���¼����˱���©�ı߽����ɴ��� ***
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

            // --- ����Ϸģʽ��ʼʱ����ʼ����� ---
            player.gameObject.SetActive(true);
            player.BotInit(inputs, prevInputs);
            player.mMap = this;
            player.mPosition = new Vector2(2 * Map.cTileSize, (mHeight / 2) * Map.cTileSize + player.mAABB.HalfSizeY);
            // ***********************************************
        }
        // �������Inspector�����õ��� "Drawing"
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
            // --- �ڻ���ģʽ��ʼʱ�����ز�������� ---
            player.gameObject.SetActive(false);
            ResetToDrawingMode();
        }

        // ��ʼ����ˢԤ��
        if (brushPreviewPrefab != null)
        {
            brushPreviewInstance = Instantiate(brushPreviewPrefab, transform); // ����ʵ������ΪMap���Ӷ���
                                                                               // ����Ի���ģʽ��ʼ����׼������ʾ�������򱣳�����
            brushPreviewInstance.SetActive(currentPhase == GamePhase.Drawing);
        }
    }

    void Update()
    {
        switch (currentPhase)
        {
            case GamePhase.Drawing:
                HandleDrawingInput();

                // ���¿ո������ʼ����
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    StartTrialMode();
                }
                break;

            case GamePhase.TrialPlay:
                HandlePlayingInput(); // ��������߼�����

                // ���� Backspace �������ػ���ģʽ�����޸�
                if (Input.GetKeyDown(KeyCode.Backspace))
                {
                    ReturnToDrawingMode();
                }
                // ���� 'R' ������ȫ�����������ݣ����ؿհ׻���
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

    // �·�����������ƽ׶ε�����
    private void HandleDrawingInput()
    {
        // �����λ��ת��Ϊ��������
        Vector2 mousePos = Input.mousePosition;
        Vector2 cameraPos = Camera.main.transform.position;
        var mousePosInWorld = cameraPos + mousePos - new Vector2(gameCamera.pixelWidth / 2, gameCamera.pixelHeight / 2);
        int mouseTileX, mouseTileY;
        GetMapTileAtPoint(mousePosInWorld, out mouseTileX, out mouseTileY);
        Vector2i currentCell = new Vector2i(mouseTileX, mouseTileY);

        // --- �������������� ---
        // �����·��������±�ˢԤ����ʵʱ״̬
        UpdateBrushPreview(mouseTileX, mouseTileY);
        // --- ����������� ---

        // �����ס���������ͻ���һ�� brushSize * brushSize ������
        if (Input.GetKey(KeyCode.Mouse0))
        {
            // ʹ��Ƕ��ѭ��������ˢ���ǵ�ÿһ������
            for (int xOffset = 0; xOffset < brushSize; xOffset++)
            {
                for (int yOffset = 0; yOffset < brushSize; yOffset++)
                {
                    int currentX = mouseTileX + xOffset;
                    int currentY = mouseTileY + yOffset;
                    currentCell = new Vector2i(currentX, currentY);

                    // ��������Ƿ��ڵ�ͼ�߽�����δ�����
                    if (currentX >= 0 && currentX < mWidth && currentY >= 0 && currentY < mHeight)
                    {
                        if (!playerSelectedPath.Contains(currentCell))
                        {
                            playerSelectedPath.Add(currentCell);
                            // �ı���ӵ���ɫ������ҷ���
                            tilesSprites[currentX, currentY].enabled = true;
                            tilesSprites[currentX, currentY].color = new Color(0.5f, 1f, 0.5f, 0.5f); // ����ɫ
                        }
                    }
                }
            }
        }

        // (��ѡ) ��ס����Ҽ����Բ���һ�� brushSize * brushSize ������
        if (Input.GetKey(KeyCode.Mouse1))
        {
            // ͬ��ʹ��Ƕ��ѭ������������߼�
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
                        tilesSprites[currentX, currentY].color = Color.white; // �ָ�ԭɫ
                    }
                }
            }
        }
    }

    // �·�����������Ϸ�׶ε����루������֮ǰUpdate����߼���
    private void HandlePlayingInput()
    {
        inputs[(int)KeyInput.GoRight] = Input.GetKey(goRightKey);
        inputs[(int)KeyInput.GoLeft] = Input.GetKey(goLeftKey);
        inputs[(int)KeyInput.GoDown] = Input.GetKey(goDownKey);
        inputs[(int)KeyInput.Jump] = Input.GetKey(goJumpKey);

        // ��֮ǰ��Ѱ·����߼����Ա��������ڲ���
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

    // �·��������õ�����ģʽ
    private void ResetToDrawingMode()
    {
        playerSelectedPath.Clear();
        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                // �����и������
                SetTile(x, y, TileType.Empty);
                tilesSprites[x, y].color = Color.white; // �ָ���ɫ
            }
        }

        // --- ��������: ����ʱ�ٴ�������� ---
        if (player != null && player.gameObject.activeSelf)
        {
            player.gameObject.SetActive(false);
        }
        // ------------------------------------

        // --- �������룺���õ�����ģʽʱ�������ˢԤ�� ---
        if (brushPreviewInstance != null)
        {
            brushPreviewInstance.SetActive(true);
        }
        // ------------------------------------

        currentPhase = GamePhase.Drawing;

        // --- �ٴ�����ϵͳ��� ---
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
            // 1. ����λ�� (�ⲿ���߼�����ȷ�ģ������޸�)
            // ������Ҫ��Ԥ�������½Ƕ��뵽������ڵĸ���
            float bottomLeftX = position.x + mouseTileX * cTileSize;
            float bottomLeftY = position.y + mouseTileY * cTileSize;

            // Ԥ�������ĵ�λ�� = ���½�λ�� + Ԥ���ߴ��һ��
            float totalSize = brushSize * cTileSize;
            float centerX = bottomLeftX + totalSize / 2.0f - cTileSize / 2.0f;
            float centerY = bottomLeftY + totalSize / 2.0f - cTileSize / 2.0f;

            brushPreviewInstance.transform.position = new Vector3(centerX, centerY, -5f);

            // 2. �����С (������Ҫ�����ĵط�)

            // --- ����Ĵ��� ---
            // brushPreviewInstance.transform.localScale = new Vector3(totalSize / 100f, totalSize / 100f, 1f);

            // --- ��ȷ�Ĵ��� ---
            // ֱ�ӽ��������������Ϊ������Ҫ�����سߴ�
            brushPreviewInstance.transform.localScale = new Vector3(totalSize, totalSize, 1f);
        }
    }

    private void ReturnToDrawingMode()
    {
        // --- �ؼ��������ڷ��ر༭ǰ��������Ҷ��� ---
        if (player != null)
        {
            player.gameObject.SetActive(false);
        }
        // ------------------------------------------

        // �������и��ӣ��ָ�������ʱ���Ӿ�״̬
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

        // �л��ػ���״̬
        currentPhase = GamePhase.Drawing;
        Cursor.visible = false; // ����ϵͳ���

        // ������ʾ��ˢԤ��
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

        // 1. ���ɹؿ�������
        for (int y = 0; y < mHeight; y++)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentTile = new Vector2i(x, y);
                tilesSprites[x, y].color = Color.white; // �ָ����ӵ�������ɫ

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

        // 2. ���ػ��ƹ���
        if (brushPreviewInstance != null)
        {
            brushPreviewInstance.SetActive(false);
        }
        Cursor.visible = true; // ��ʾϵͳ���

        // 3. �����ʼ�����
        player.gameObject.SetActive(true);
        player.BotInit(inputs, prevInputs);
        player.mMap = this;

        // 4. ����ҷŵ�·�������
        using (var enumerator = playerSelectedPath.GetEnumerator())
        {
            if (enumerator.MoveNext())
            {
                Vector2i startPos = enumerator.Current;
                player.mPosition = GetMapTilePosition(startPos) + new Vector2(0, player.mAABB.HalfSizeY);
            }
        }

        // 5. �л�������״̬
        currentPhase = GamePhase.TrialPlay;

        Debug.Log("Trial Mode! You can play now. Press BACKSPACE to return to editing.");
    }
}
