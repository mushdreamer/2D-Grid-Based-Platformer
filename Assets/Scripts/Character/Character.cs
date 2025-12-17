using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Algorithms;

public class Character : MovingObject
{
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
    protected int mFramesFromJumpStart = 0;
    protected bool[] mInputs;
    protected bool[] mPrevInputs;

    public float mJumpSpeed;
    public float mWalkSpeed;

    public List<Vector2i> mPath = new List<Vector2i>();
    public bool isSimulation = false;
    public LineRenderer lineRenderer;

    // --- 新增：二段跳相关变量 ---
    protected int mJumpCount = 0;
    protected const int cMaxJumps = 2; // 最大跳跃次数 (1 = 单跳, 2 = 二段跳)
    // -------------------------

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

    // --- 修改：HandleJumping 支持二段跳 ---
    private void HandleJumping()
    {
        mFramesFromJumpStart++;
        if (mAtCeiling) mFramesFromJumpStart = 100;

        mSpeed.y += Constants.cGravity * Time.deltaTime;
        mSpeed.y = Mathf.Max(mSpeed.y, Constants.cMaxFallingSpeed);

        // 检测跳跃键刚刚按下 (Fresh Press)
        bool jumpPressed = mInputs[(int)KeyInput.Jump] && !mPrevInputs[(int)KeyInput.Jump];
        // 检测是否按住 (Holding)
        bool jumpHeld = mInputs[(int)KeyInput.Jump];

        // 1. 处理起跳逻辑
        if (jumpPressed)
        {
            // 情况A: 地面起跳 (或者土狼时间)
            if (mOnGround || (mSpeed.y < 0.0f && mFramesFromJumpStart < Constants.cJumpFramesThreshold))
            {
                mSpeed.y = mJumpSpeed;
                mJumpCount = 1; // 消耗第一次跳跃
                if (!isSimulation && mJumpSfx != null) mAudioSource.PlayOneShot(mJumpSfx);
            }
            // 情况B: 空中二段跳
            else if (mJumpCount < cMaxJumps)
            {
                mSpeed.y = mJumpSpeed; // 二段跳通常也是满力跳
                mJumpCount++; // 消耗跳跃次数
                mFramesFromJumpStart = 0; // 重置跳跃帧，允许长按
                if (!isSimulation && mJumpSfx != null) mAudioSource.PlayOneShot(mJumpSfx);

                // 可选: 这里可以加一个二段跳的特效或不同的声音
            }
        }

        // 2. 处理长按跳得更高 (Variable Jump Height)
        // 只有在上升阶段松开按键，才会截断跳跃高度
        if (!jumpHeld && mSpeed.y > 0.0f)
        {
            mSpeed.y = Mathf.Min(mSpeed.y, 200.0f);
            mFramesFromJumpStart = 100; // 结束长按判定
        }

        // 空中左右移动逻辑
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

    // --- 修改：SimulationUpdate 也要同步支持二段跳 ---
    // (为了保持一致，这里其实可以直接复用 HandleJumping 的逻辑，但为了不破坏你现有的结构，我手动同步一下)
    private void HandleJumpingSimulation(float timeStep)
    {
        mFramesFromJumpStart++;
        if (mAtCeiling) mFramesFromJumpStart = 100;

        mSpeed.y += Constants.cGravity * timeStep;
        mSpeed.y = Mathf.Max(mSpeed.y, Constants.cMaxFallingSpeed);

        // 模拟环境下的输入检测
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
                mJumpCount = 0; // 模拟开始前重置
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
                mJumpCount = 0; // 跑动时重置跳跃次数
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
                    mJumpCount = 0; // 落地重置
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
            mAnimator.Play("Jump"); // 通常死亡也是用跳跃帧或专门的死亡帧
        }
    }

    public void CharacterUpdate()
    {
        switch (mCurrentState)
        {
            case CharacterState.Die:
                // 1. 应用重力
                mSpeed.y += Constants.cGravity * Time.deltaTime;

                // 2. 手动更新位置
                mPosition += mSpeed * Time.deltaTime;
                transform.position = new Vector3(Mathf.Round(mPosition.x), Mathf.Round(mPosition.y), mSpriteDepth);

                // 3. 掉出地图检测
                // 注意：这里删除了 SetActive(false)，保证角色能一直运行到这里触发 GameOver
                if (mPosition.y < mMap.position.y - 200.0f) // 稍微加大一点距离 (-200) 确保完全出屏
                {
                    mMap.GameOver();
                }
                return;

            case CharacterState.Stand:
                mWalkSfxTimer = cWalkSfxTime;
                mAnimator.Play("Stand");
                mSpeed = Vector2.zero;
                mJumpCount = 0; // 站立时重置跳跃次数

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
                mAnimator.Play("Walk");
                mWalkSfxTimer += Time.deltaTime;
                if (mWalkSfxTimer > cWalkSfxTime) { mWalkSfxTimer = 0.0f; mAudioSource.PlayOneShot(mWalkSfx); }

                mJumpCount = 0; // 跑动时重置

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
                mAnimator.Play("Jump");

                // 跳跃状态下不重置 mJumpCount，只在 HandleJumping 里增加
                HandleJumping();

                if (mOnGround)
                {
                    mJumpCount = 0; // 落地重置
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

        if (mWasOnGround && !mOnGround) mFramesFromJumpStart = 0;
        UpdatePrevInputs();
    }
}