// 文件名: RiskFieldSolver.cs
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

public class RiskFieldSolver : MonoBehaviour
{
    public ComputeShader diffusionShader;
    public Map targetMap;
    public int iterationsPerFrame = 10;

    private ComputeBuffer readRiskBuffer;
    private ComputeBuffer writeRiskBuffer;
    private ComputeBuffer sourceMaskBuffer;
    private ComputeBuffer sourceValueBuffer;
    private ComputeBuffer diffusionTensorBuffer;

    private float[] localRiskData;
    private int threadGroupsX;
    private int threadGroupsY;
    private int kernelIndex;
    private bool isSolving = false;

    public void InitializeSolver()
    {
        int totalCells = targetMap.mWidth * targetMap.mHeight;
        localRiskData = new float[totalCells];

        readRiskBuffer = new ComputeBuffer(totalCells, sizeof(float));
        writeRiskBuffer = new ComputeBuffer(totalCells, sizeof(float));
        sourceMaskBuffer = new ComputeBuffer(totalCells, sizeof(int));
        sourceValueBuffer = new ComputeBuffer(totalCells, sizeof(float));
        diffusionTensorBuffer = new ComputeBuffer(totalCells, sizeof(float) * 2);

        kernelIndex = diffusionShader.FindKernel("JacobiIterate");
        threadGroupsX = Mathf.CeilToInt(targetMap.mWidth / 8.0f);
        threadGroupsY = Mathf.CeilToInt(targetMap.mHeight / 8.0f);

        diffusionShader.SetInt("_MapWidth", targetMap.mWidth);
        diffusionShader.SetInt("_MapHeight", targetMap.mHeight);

        ResetToInitialState();
        isSolving = true;
    }

    public void ResetToInitialState()
    {
        int totalCells = targetMap.mWidth * targetMap.mHeight;
        float[] initialRisk = new float[totalCells];
        int[] mask = new int[totalCells];
        float[] sourceVal = new float[totalCells];
        Vector2[] tensor = new Vector2[totalCells];

        for (int i = 0; i < totalCells; i++)
        {
            initialRisk[i] = 0f;
            mask[i] = 0;
            sourceVal[i] = 0f;
            tensor[i] = new Vector2(1.0f, 1.0f);
        }

        readRiskBuffer.SetData(initialRisk);
        writeRiskBuffer.SetData(initialRisk);
        sourceMaskBuffer.SetData(mask);
        sourceValueBuffer.SetData(sourceVal);
        diffusionTensorBuffer.SetData(tensor);
    }

    public void SetDirichletBoundary(Vector2i tileCoords, float riskValue)
    {
        int index = tileCoords.y * targetMap.mWidth + tileCoords.x;
        int[] maskData = new int[1] { 1 };
        float[] valData = new float[1] { riskValue };
        sourceMaskBuffer.SetData(maskData, 0, index, 1);
        sourceValueBuffer.SetData(valData, 0, index, 1);
    }

    public void SetDiffusionTensor(Vector2i tileCoords, Vector2 diffusionCoefficients)
    {
        int index = tileCoords.y * targetMap.mWidth + tileCoords.x;
        Vector2[] tensorData = new Vector2[1] { diffusionCoefficients };
        diffusionTensorBuffer.SetData(tensorData, 0, index, 1);
    }

    void Update()
    {
        if (!isSolving) return;

        for (int i = 0; i < iterationsPerFrame; i++)
        {
            diffusionShader.SetBuffer(kernelIndex, "_ReadRisk", readRiskBuffer);
            diffusionShader.SetBuffer(kernelIndex, "_WriteRisk", writeRiskBuffer);
            diffusionShader.SetBuffer(kernelIndex, "_SourceMask", sourceMaskBuffer);
            diffusionShader.SetBuffer(kernelIndex, "_SourceValue", sourceValueBuffer);
            diffusionShader.SetBuffer(kernelIndex, "_DiffusionTensor", diffusionTensorBuffer);

            diffusionShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, 1);

            ComputeBuffer temp = readRiskBuffer;
            readRiskBuffer = writeRiskBuffer;
            writeRiskBuffer = temp;
        }

        AsyncGPUReadback.Request(readRiskBuffer, OnReadbackComplete);
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest request)
    {
        if (request.hasError) return;
        request.GetData<float>().CopyTo(localRiskData);
    }

    public float GetRiskAtContinuousPosition(Vector2 worldPos)
    {
        if (localRiskData == null) return 0f;

        float gridX = (worldPos.x - targetMap.position.x) / Map.cTileSize - 0.5f;
        float gridY = (worldPos.y - targetMap.position.y) / Map.cTileSize - 0.5f;

        int x0 = Mathf.Clamp(Mathf.FloorToInt(gridX), 0, targetMap.mWidth - 1);
        int x1 = Mathf.Clamp(x0 + 1, 0, targetMap.mWidth - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(gridY), 0, targetMap.mHeight - 1);
        int y1 = Mathf.Clamp(y0 + 1, 0, targetMap.mHeight - 1);

        float tx = gridX - Mathf.Floor(gridX);
        float ty = gridY - Mathf.Floor(gridY);

        float v00 = localRiskData[y0 * targetMap.mWidth + x0];
        float v10 = localRiskData[y0 * targetMap.mWidth + x1];
        float v01 = localRiskData[y1 * targetMap.mWidth + x0];
        float v11 = localRiskData[y1 * targetMap.mWidth + x1];

        float bottom = Mathf.Lerp(v00, v10, tx);
        float top = Mathf.Lerp(v01, v11, tx);
        return Mathf.Lerp(bottom, top, ty);
    }

    void OnDestroy()
    {
        readRiskBuffer?.Release();
        writeRiskBuffer?.Release();
        sourceMaskBuffer?.Release();
        sourceValueBuffer?.Release();
        diffusionTensorBuffer?.Release();
    }
}