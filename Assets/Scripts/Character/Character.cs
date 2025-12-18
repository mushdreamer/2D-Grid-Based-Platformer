using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Algorithms;

public class Character : MovingObject
{
    // ... (保留你原有的 Enum 和 变量) ...
    [System.Serializable]
    public enum CharacterState
    {
        Stand,
        Run,
        Jump,
        GrabLedge,
        Die
    };

    public AudioClip mHitWallSfx;
    public AudioClip mJumpSfx;
    public AudioClip mWalkSfx;
    public AudioSource mAudioSource;

    public float mWalkSfxTimer = 0.0f;
    public const float cWalkSfxTime = 0.25f;

    [HideInInspector]
    public CharacterState mCurrentState = CharacterState.Stand;

    public Animator mAnimator;
    // --- 新增：SpriteRenderer 引用 ---
    public SpriteRenderer mSpriteRenderer;

    protected int mFramesFromJumpStart = 0;
    protected bool[] mInputs;
    protected bool[] mPrevInputs;

    public float mJumpSpeed;
    public float mWalkSpeed;

    public List<Vector2i> mPath = new List<Vector2i>();
    public bool isSimulation = false;
    public LineRenderer lineRenderer;

    protected int mJumpCount = 0;
    protected const int cMaxJumps = 2;

    void Awake()
    {
        // 自动获取 SpriteRenderer
        if (mSpriteRenderer == null) mSpriteRenderer = GetComponent<SpriteRenderer>();
    }

    // --- 新增：设置皮肤 ---
    public void SetSkin(Sprite skin)
    {
        if (mSpriteRenderer != null)
        {
            mSpriteRenderer.sprite = skin;
            if (mAnimator != null) mAnimator.enabled = false;
        }
    }

    void OnDrawGizmos()
    {
        DrawMovingObjectGizmos();
        if (mPath != null && mPath.Count > 0)
        {
            var start = mPath[0];
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(mMap.transform.position + new Vector3(start.x * Map.cTileSize, start.y * Map.cTileSize, -5.0f), 5.0f);
            for (var i = 1; i < mPath.Count; ++i)
            {
                var end = mPath[i];
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(mMap.transform.position + new Vector3(end.x * Map.cTileSize, end.y * Map.cTileSize, -5.0f), 5.0f);
                Gizmos.color = Color.red;
                Gizmos.DrawLine(mMap.transform.position + new Vector3(start.x * Map.cTileSize, start.y * Map.cTileSize, -5.0f),
                                mMap.transform.position + new Vector3(end.x * Map.cTileSize, end.y * Map.cTileSize, -5.0f));
                start = end;
            }
        }
    }

    protected void DrawPathLines()
    {
        if (mPath != null && mPath.Count > 0)
        {
            lineRenderer.enabled = true;
            lineRenderer.positionCount = mPath.Count;
            lineRenderer.startWidth = 4.0f;
            lineRenderer.endWidth = 4.0f;
            lineRenderer.startColor = Color.red;
            lineRenderer.endColor = Color.red;
            for (var i = 0; i < mPath.Count; ++i)
            {
                lineRenderer.SetPosition(i, mMap.transform.position + new Vector3(mPath[i].x * Map.cTileSize, mPath[i].y * Map.cTileSize, -5.0f));
            }
        }
        else
            lineRenderer.enabled = false;
    }

    public void UpdatePrevInputs()
    {
        var count = (byte)KeyInput.Count;
        for (byte i = 0; i < count; ++i)
            mPrevInputs[i] = mInputs[i];
    }

    private void HandleJumping()
    {
        mFramesFromJumpStart++;
        if (mAtCeiling) mFramesFromJumpStart = 100;

        mSpeed.y += Constants.cGravity * Time.deltaTime;
        mSpeed.y = Mathf.Max(mSpeed.y, Constants.cMaxFallingSpeed);

        bool jumpPressed = mInputs[(int)KeyInput.Jump] && !mPrevInputs[(int)KeyInput.Jump];
        bool jumpHeld = mInputs[(int)KeyInput.Jump];

        if (jumpPressed)
        {
            if (mOnGround || (mSpeed.y < 0.0f && mFramesFromJumpStart < Constants.cJumpFramesThreshold))
            {
                mSpeed.y = mJumpSpeed;
                mJumpCount = 1;
                if (!isSimulation && mJumpSfx != null) mAudioSource.PlayOneShot(mJumpSfx);
            }
            else if (mJumpCount < cMaxJumps)
            {
                mSpeed.y = mJumpSpeed;
                mJumpCount++;
                mFramesFromJumpStart = 0;
                if (!isSimulation && mJumpSfx != null) mAudioSource.PlayOneShot(mJumpSfx);
            }
        }

        if (!jumpHeld && mSpeed.y > 0.0f)
        {
            mSpeed.y = Mathf.Min(mSpeed.y, 200.0f);
            mFramesFromJumpStart = 100;
        }

        if (mInputs[(int)KeyInput.GoRight] == mInputs[(int)KeyInput.GoLeft])
        {
            mSpeed.x = 0.0f;
        }
        else if (mInputs[(int)KeyInput.GoRight])
        {
            transform.localScale = new Vector3(-mScale.x, mScale.y, 1.0f);
            mSpeed.x = mWalkSpeed;
            if (mPushedRightWall && !mPushesRightWall) mPosition.x += 1.0f;
        }
        else if (mInputs[(int)KeyInput.GoLeft])
        {
            transform.localScale = new Vector3(mScale.x, mScale.y, 1.0f);
            mSpeed.x = -mWalkSpeed;
            if (mPushedLeftWall && !mPushesLeftWall) mPosition.x -= 1.0f;
        }
    }

    private void HandleJumpingSimulation(float timeStep)
    {
        mFramesFromJumpStart++;
        if (mAtCeiling) mFramesFromJumpStart = 100;

        mSpeed.y += Constants.cGravity * timeStep;
        mSpeed.y = Mathf.Max(mSpeed.y, Constants.cMaxFallingSpeed);

        bool jumpPressed = mInputs[(int)KeyInput.Jump] && !mPrevInputs[(int)KeyInput.Jump];
        bool jumpHeld = mInputs[(int)KeyInput.Jump];

        if (jumpPressed)
        {
            if (mOnGround || (mSpeed.y < 0.0f && mFramesFromJumpStart < Constants.cJumpFramesThreshold))
            {
                mSpeed.y = mJumpSpeed;
                mJumpCount = 1;
            }
            else if (mJumpCount < cMaxJumps)
            {
                mSpeed.y = mJumpSpeed;
                mJumpCount++;
                mFramesFromJumpStart = 0;
            }
        }

        if (!jumpHeld && mSpeed.y > 0.0f)
        {
            mSpeed.y = Mathf.Min(mSpeed.y, 200.0f);
            mFramesFromJumpStart = 100;
        }

        if (mInputs[(int)KeyInput.GoRight] == mInputs[(int)KeyInput.GoLeft]) mSpeed.x = 0.0f;
        else if (mInputs[(int)KeyInput.GoRight]) mSpeed.x = mWalkSpeed;
        else if (mInputs[(int)KeyInput.GoLeft]) mSpeed.x = -mWalkSpeed;
    }

    public void SimulationUpdate(float timeStep, bool[] mockInputs)
    {
        isSimulation = true;
        mInputs = mockInputs;
        UpdatePrevInputs();

        switch (mCurrentState)
        {
            case CharacterState.Stand:
                mSpeed = Vector2.zero;
                mJumpCount = 0;
                if (!mOnGround) { mCurrentState = CharacterState.Jump; break; }

                if (mInputs[(int)KeyInput.Jump])
                {
                    mSpeed.y = mJumpSpeed;
                    mJumpCount = 1;
                    mCurrentState = CharacterState.Jump;
                }
                else if (mInputs[(int)KeyInput.GoRight] != mInputs[(int)KeyInput.GoLeft])
                {
                    mCurrentState = CharacterState.Run;
                }
                break;

            case CharacterState.Run:
                mJumpCount = 0;
                if (mInputs[(int)KeyInput.GoRight] == mInputs[(int)KeyInput.GoLeft])
                {
                    mCurrentState = CharacterState.Stand;
                    mSpeed = Vector2.zero;
                }
                else if (mInputs[(int)KeyInput.GoRight]) mSpeed.x = mWalkSpeed;
                else if (mInputs[(int)KeyInput.GoLeft]) mSpeed.x = -mWalkSpeed;

                if (mInputs[(int)KeyInput.Jump])
                {
                    mSpeed.y = mJumpSpeed;
                    mJumpCount = 1;
                    mCurrentState = CharacterState.Jump;
                }
                else if (!mOnGround) mCurrentState = CharacterState.Jump;
                break;

            case CharacterState.Jump:
                HandleJumpingSimulation(timeStep);
                if (mOnGround)
                {
                    mJumpCount = 0;
                    if (mInputs[(int)KeyInput.GoRight] == mInputs[(int)KeyInput.GoLeft])
                    {
                        mCurrentState = CharacterState.Stand;
                        mSpeed = Vector2.zero;
                    }
                    else
                    {
                        mCurrentState = CharacterState.Run;
                        mSpeed.y = 0.0f;
                    }
                }
                break;
            case CharacterState.Die:
                mSpeed.y += Constants.cGravity * timeStep;
                mPosition += mSpeed * timeStep;
                return;
        }

        UpdatePhysics(timeStep);
        isSimulation = false;
    }

    protected override void CheckForDangerZone()
    {
        if (mCurrentState == CharacterState.Die) return;

        Vector2 feetPosition = mAABB.Center - new Vector2(0, mAABB.HalfSizeY);
        Vector2i tileCoords = mMap.GetMapTileAtPoint(feetPosition);
        TileType currentTileType = mMap.GetTile(tileCoords.x, tileCoords.y);

        if (currentTileType == TileType.Danger)
        {
            Die();
        }
    }

    public void Die()
    {
        if (mCurrentState == CharacterState.Die) return;
        mCurrentState = CharacterState.Die;

        mSpeed.x = 0;
        mSpeed.y = 350.0f;

        if (!isSimulation)
        {
            if (mJumpSfx != null) mAudioSource.PlayOneShot(mJumpSfx);
            if (mAnimator != null && mAnimator.enabled) mAnimator.Play("Jump");
        }
    }

    public void CharacterUpdate()
    {
        switch (mCurrentState)
        {
            case CharacterState.Die:
                mSpeed.y += Constants.cGravity * Time.deltaTime;
                mPosition += mSpeed * Time.deltaTime;
                transform.position = new Vector3(Mathf.Round(mPosition.x), Mathf.Round(mPosition.y), mSpriteDepth);

                if (mPosition.y < mMap.position.y - 200.0f)
                {
                    mMap.GameOver();
                }
                return;

            case CharacterState.Stand:
                mWalkSfxTimer = cWalkSfxTime;
                if (mAnimator != null && mAnimator.enabled) mAnimator.Play("Stand");
                mSpeed = Vector2.zero;
                mJumpCount = 0;

                if (!mOnGround) { mCurrentState = CharacterState.Jump; break; }

                if (mInputs[(int)KeyInput.GoRight] != mInputs[(int)KeyInput.GoLeft])
                {
                    mCurrentState = CharacterState.Run;
                }
                else if (mInputs[(int)KeyInput.Jump])
                {
                    mSpeed.y = mJumpSpeed;
                    mJumpCount = 1;
                    mAudioSource.PlayOneShot(mJumpSfx);
                    mCurrentState = CharacterState.Jump;
                }
                if (mInputs[(int)KeyInput.GoDown] && mOnOneWayPlatform)
                    mPosition -= Vector2.up * cOneWayPlatformThreshold;
                break;

            case CharacterState.Run:
                if (mAnimator != null && mAnimator.enabled) mAnimator.Play("Walk");
                mWalkSfxTimer += Time.deltaTime;
                if (mWalkSfxTimer > cWalkSfxTime) { mWalkSfxTimer = 0.0f; mAudioSource.PlayOneShot(mWalkSfx); }

                mJumpCount = 0;

                if (mInputs[(int)KeyInput.GoRight] == mInputs[(int)KeyInput.GoLeft])
                {
                    mCurrentState = CharacterState.Stand;
                    mSpeed = Vector2.zero;
                }
                else if (mInputs[(int)KeyInput.GoRight])
                {
                    mSpeed.x = mWalkSpeed;
                    transform.localScale = new Vector3(-mScale.x, mScale.y, 1.0f);
                }
                else if (mInputs[(int)KeyInput.GoLeft])
                {
                    mSpeed.x = -mWalkSpeed;
                    transform.localScale = new Vector3(mScale.x, mScale.y, 1.0f);
                }

                if (mInputs[(int)KeyInput.Jump])
                {
                    mSpeed.y = mJumpSpeed;
                    mJumpCount = 1;
                    mAudioSource.PlayOneShot(mJumpSfx, 1.0f);
                    mCurrentState = CharacterState.Jump;
                }
                else if (!mOnGround) { mCurrentState = CharacterState.Jump; break; }

                if (mPushesLeftWall) mSpeed.x = Mathf.Max(mSpeed.x, 0.0f);
                else if (mPushesRightWall) mSpeed.x = Mathf.Min(mSpeed.x, 0.0f);
                break;

            case CharacterState.Jump:
                mWalkSfxTimer = cWalkSfxTime;
                if (mAnimator != null && mAnimator.enabled) mAnimator.Play("Jump");

                HandleJumping();

                if (mOnGround)
                {
                    mJumpCount = 0;
                    if (mInputs[(int)KeyInput.GoRight] == mInputs[(int)KeyInput.GoLeft])
                    {
                        mCurrentState = CharacterState.Stand;
                        mSpeed = Vector2.zero;
                    }
                    else
                    {
                        mCurrentState = CharacterState.Run;
                        mSpeed.y = 0.0f;
                    }
                }
                break;
        }

        if ((!mWasOnGround && mOnGround) || (!mWasAtCeiling && mAtCeiling) || (!mPushedLeftWall && mPushesLeftWall) || (!mPushedRightWall && mPushesRightWall))
            mAudioSource.PlayOneShot(mHitWallSfx, 0.5f);

        UpdatePhysics(Time.deltaTime);

        // --- 新增：检查屏幕边界（地图边界）逻辑 ---
        CheckScreenBounds();
        // ----------------------------------------

        if (mWasOnGround && !mOnGround) mFramesFromJumpStart = 0;
        UpdatePrevInputs();
    }

    // --- 新增方法：强制屏幕边界限制 ---
    private void CheckScreenBounds()
    {
        // 1. 如果已经死了，就不再限制边界，允许自由坠落
        if (mCurrentState == CharacterState.Die) return;

        // 2. 获取主摄像机的边界
        if (Camera.main == null) return;
        Camera cam = Camera.main;

        float camHeight = 2f * cam.orthographicSize;
        float camWidth = camHeight * cam.aspect;
        Vector3 camPos = cam.transform.position;

        float minX = camPos.x - camWidth / 2f;
        float maxX = camPos.x + camWidth / 2f;
        float minY = camPos.y - camHeight / 2f;
        float maxY = camPos.y + camHeight / 2f;

        // 3. 掉出地图判定 (掉出下边界)
        // 使用角色的脚底位置判断：mPosition 通常是脚底位置（依赖于 mAABBOffset.y 设置，通常为 HalfSizeY）
        // 如果脚底低于屏幕下边界，判定死亡
        if (mPosition.y < minY)
        {
            Die();
            return; // 死亡后直接返回，不需要执行后续的钳制逻辑
        }

        // 4. 限制左右上边界 (防止走出屏幕)
        // 左边界限制
        if (mPosition.x - mAABB.HalfSizeX < minX)
        {
            mPosition.x = minX + mAABB.HalfSizeX;
            if (mSpeed.x < 0) mSpeed.x = 0;
        }
        // 右边界限制
        else if (mPosition.x + mAABB.HalfSizeX > maxX)
        {
            mPosition.x = maxX - mAABB.HalfSizeX;
            if (mSpeed.x > 0) mSpeed.x = 0;
        }

        // 上边界限制 (防止跳出屏幕上方)
        // 注意：这里使用的是 mPosition.y + 2 * HalfSizeY (即头顶位置)
        if (mPosition.y + 2 * mAABB.HalfSizeY > maxY)
        {
            mPosition.y = maxY - 2 * mAABB.HalfSizeY;
            if (mSpeed.y > 0) mSpeed.y = 0; // 撞到天花板，垂直速度归零
        }
    }
}