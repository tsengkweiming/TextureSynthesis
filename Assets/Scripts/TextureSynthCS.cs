using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using System.Linq;
using System.Runtime.InteropServices;

public class TextureSynthCS : MonoBehaviour
{
    public GameObject plane;
    public ComputeShader TextureSynthesisCS;

    public List<Texture2D> sampleTextures;
    public Renderer[] Rend;
    public int OutputSize;
    public int WindowSize;//ODD ONLY

    float errThreshold = 0.1f;
    float maxErrThreshold = 0.3f;
    int seedSize = 3;
    float Sigma = 6.4f;
    bool ImageNotFIlled;

    public RenderTexture renderTexture_A;
    public RenderTexture renderTexture_Candidates;
    public RenderTexture renderTexture_Neighborhood;
    public RenderTexture renderTexture_Matches;
    public RenderTexture renderTexture_Output;
    public RenderTexture renderTexture_PaddedImage;
    public RenderTexture renderTexture_FillMap;
    public RenderTexture renderTexture_GaussianMask;
    public Texture2D GaussianMap;

    public RenderTexture renderTexture_PaddedImageRead;
    public RenderTexture renderTexture_FillMapRead;

    int kernelIndex;
    int kernelID;
    int _numPixels;
    int _numCandidates;
    int candidatesCountW;
    int candidatesCountH;

    ComputeBuffer unfilledBuffer;
    ComputeBuffer countBuffer;
    ComputeBuffer foundBuffer;
    ComputeBuffer testBuffer;

    public ComputeBuffer UnfilledBuffer => unfilledBuffer ?? null;

    struct ThreadSize
    {
        public int x;
        public int y;
        public int z;

        public ThreadSize(uint x, uint y, uint z)
        {
            this.x = (int)x;
            this.y = (int)y;
            this.z = (int)z;
        }
    }

    ThreadSize kernelThreadSize;
    ThreadSize candidateKernelThreadSize;
    ThreadSize paddedKernelThreadSize;
    ThreadSize neighborhoodKernelThreadSize;
    ThreadSize resultKernelThreadSize;

    struct Pixel
    {
        public int ID;        //1d coord
        public int2 Position; //2d coord
        public int IsFilled;
        public Color Color;
    };

    struct ImageStruct
    {
        public int[] ID;        //1d coord
        public int2[] Position; //2d coord
        public int[] IsFilled;
        public Color[] Color;
    };
    struct Image
    {
        public int rowYCount;
        public int columnXCount;
        public int outputSize;
        public int[,] isFilledXY;
        public Color[,] colorXY;
        public int found;
    }
    
    // Start is called before the first frame update
    void Start()
    {
        candidatesCountW = sampleTextures[0].width - WindowSize + 1;
        candidatesCountH = sampleTextures[0].height - WindowSize + 1;
        _numCandidates = candidatesCountW * candidatesCountH;
        _numPixels = candidatesCountW * candidatesCountH;

        unfilledBuffer = new ComputeBuffer(OutputSize * OutputSize * 1, Marshal.SizeOf(typeof(int2)), ComputeBufferType.Append);
        //unfilledBuffer = new ComputeBuffer((OutputSize + WindowSize - 1 + 10) * (OutputSize + WindowSize - 1 + 10) * 1, Marshal.SizeOf(typeof(int2)), ComputeBufferType.Append);
        countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
        
        foundBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(int)));
        testBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(float4)));

        int[] temp = new int[1];
        temp[0] = 0;

        countBuffer.SetData(temp);
        unfilledBuffer.SetCounterValue(0);

        int[] found = new int[1];
        found[0] = 0;
        foundBuffer.SetData(found);

        var gaussian = CalculateGaussianZ(WindowSize, Sigma);

        this.renderTexture_A = new RenderTexture(OutputSize, OutputSize, 0, RenderTextureFormat.ARGB32);
        this.renderTexture_A.enableRandomWrite = true;
        this.renderTexture_A.Create();

        this.renderTexture_Candidates = new RenderTexture(WindowSize * candidatesCountW + 10, WindowSize * candidatesCountH + 10, 0, RenderTextureFormat.ARGB32);
        this.renderTexture_Candidates.enableRandomWrite = true;
        this.renderTexture_Candidates.Create();

        this.renderTexture_Neighborhood = new RenderTexture(WindowSize, WindowSize, 0, RenderTextureFormat.ARGB32);
        this.renderTexture_Neighborhood.enableRandomWrite = true;
        this.renderTexture_Neighborhood.Create();
        
        this.renderTexture_GaussianMask = new RenderTexture(WindowSize, WindowSize, 0, RenderTextureFormat.ARGB32);
        this.renderTexture_GaussianMask.enableRandomWrite = true;
        this.renderTexture_GaussianMask.Create();
        
        this.renderTexture_Output = new RenderTexture(OutputSize, OutputSize, 0, RenderTextureFormat.ARGB32);
        this.renderTexture_Output.enableRandomWrite = true;
        this.renderTexture_Output.Create();
        
        this.renderTexture_PaddedImage = new RenderTexture(OutputSize + WindowSize - 1 + 10, OutputSize + WindowSize - 1 + 10, 0, RenderTextureFormat.ARGB32);
        this.renderTexture_PaddedImage.enableRandomWrite = true;
        this.renderTexture_PaddedImage.Create();

        this.renderTexture_FillMap = new RenderTexture(OutputSize + WindowSize - 1 + 10, OutputSize + WindowSize - 1 + 10, 0, RenderTextureFormat.ARGB32);
        this.renderTexture_FillMap.enableRandomWrite = true;
        this.renderTexture_FillMap.Create();

        this.renderTexture_PaddedImageRead = new RenderTexture(OutputSize + WindowSize - 1 + 10, OutputSize + WindowSize - 1 + 10, 0, RenderTextureFormat.ARGB32);
        this.renderTexture_PaddedImageRead.enableRandomWrite = true;
        this.renderTexture_PaddedImageRead.Create();

        this.renderTexture_FillMapRead = new RenderTexture(OutputSize + WindowSize - 1 + 10, OutputSize + WindowSize - 1 + 10, 0, RenderTextureFormat.ARGB32);
        this.renderTexture_FillMapRead.enableRandomWrite = true;
        this.renderTexture_FillMapRead.Create();

        GaussianMap = new Texture2D(WindowSize, WindowSize);
        GaussianMap = CalculateGaussianZ(GaussianMap, WindowSize, WindowSize/Sigma);

        var seedImage = new Texture2D(OutputSize, OutputSize);
        var fillMap = new Texture2D(OutputSize, OutputSize);

        ApplySeed(ref seedImage, ref fillMap, sampleTextures[0], 3);


        //Get Candidates
        kernelID = TextureSynthesisCS.FindKernel("GetCandidates");

        uint candidateThreadSizeX, candidateThreadSizeY, candidateThreadSizeZ;

        this.TextureSynthesisCS.GetKernelThreadGroupSizes
            (this.kernelID,
             out candidateThreadSizeX, out candidateThreadSizeY, out candidateThreadSizeZ);
        this.candidateKernelThreadSize
                    = new ThreadSize(candidateThreadSizeX, candidateThreadSizeY, candidateThreadSizeZ);

        TextureSynthesisCS.SetTexture(this.kernelID, "_SampleTexture", sampleTextures[0]);
        this.TextureSynthesisCS.SetTexture
            (this.kernelID, "_CandidatesBuffer", this.renderTexture_Candidates);

        TextureSynthesisCS.Dispatch(this.kernelID,
                                   this.renderTexture_Candidates.width / this.candidateKernelThreadSize.x,
                                   this.renderTexture_Candidates.height / this.candidateKernelThreadSize.y,
                                   this.candidateKernelThreadSize.z);

        //Generate PaddedImage
        kernelID = TextureSynthesisCS.FindKernel("PaddedSeedImage");

        uint paddedThreadSizeX, paddedThreadSizeY, paddedThreadSizeZ;

        this.TextureSynthesisCS.GetKernelThreadGroupSizes
            (this.kernelID,
             out paddedThreadSizeX, out paddedThreadSizeY, out paddedThreadSizeZ);
        this.paddedKernelThreadSize
                = new ThreadSize(paddedThreadSizeX, paddedThreadSizeY, paddedThreadSizeZ);

        TextureSynthesisCS.SetTexture(this.kernelID, "_SourceImage", seedImage);  //same size as output
        TextureSynthesisCS.SetTexture(this.kernelID, "_FillMap", fillMap);  //same size as output

        this.TextureSynthesisCS.SetTexture
            (this.kernelID, "_PaddedBufferWrite", this.renderTexture_PaddedImage);
        this.TextureSynthesisCS.SetTexture
            (this.kernelID, "_FilledBufferWrite", this.renderTexture_FillMap);
        TextureSynthesisCS.SetBuffer(this.kernelID, "_UnfilledBufferAppend", unfilledBuffer);

        TextureSynthesisCS.Dispatch(this.kernelID,
                                   this.renderTexture_PaddedImage.width / this.paddedKernelThreadSize.x,
                                   this.renderTexture_PaddedImage.height / this.paddedKernelThreadSize.y,
                                   this.paddedKernelThreadSize.z);
        
        ComputeBuffer.CopyCount(unfilledBuffer, countBuffer, 0);
        int[] counter = new int[1] { 0 };
        countBuffer.GetData(counter);

        int count = counter[0];
        Debug.Log("Append Count "+count);

        //renderTexture_PaddedImageRead = renderTexture_PaddedImage;
        //renderTexture_FillMapRead = renderTexture_FillMap;
        Graphics.CopyTexture(renderTexture_PaddedImage, renderTexture_PaddedImageRead);
        Graphics.CopyTexture(renderTexture_FillMap, renderTexture_FillMapRead);

        uint resultThreadSizeX, resultThreadSizeY, resultThreadSizeZ;


        //int2[] bufferData = new int2[1] { int2(3,5) };
        var unfilledBufferData = new int2[4096];
        unfilledBuffer.GetData(unfilledBufferData);
        Debug.Log("unfilledBufferData " + unfilledBufferData[0]);

        //Find Matches
        kernelID = TextureSynthesisCS.FindKernel("FindMatches");

        this.TextureSynthesisCS.GetKernelThreadGroupSizes
            (this.kernelID,
             out resultThreadSizeX, out resultThreadSizeY, out resultThreadSizeZ);
        this.resultKernelThreadSize
                = new ThreadSize(resultThreadSizeX, resultThreadSizeY, resultThreadSizeZ);


        TextureSynthesisCS.SetFloat("_DeltaTime", Time.deltaTime);
        TextureSynthesisCS.SetFloat("_Time", Time.time);
        TextureSynthesisCS.SetFloat("_Rand", UnityEngine.Random.Range(0.0f, 1.0f));
        TextureSynthesisCS.SetFloat("_ErrThreshold", errThreshold);
        TextureSynthesisCS.SetFloat("_MaxErrThreshold", maxErrThreshold);
        TextureSynthesisCS.SetInt("_OutputSize", 64);
        TextureSynthesisCS.SetInt("_CandidatesWidth", candidatesCountW);
        TextureSynthesisCS.SetInt("_CandidatesHeight", candidatesCountH);

        TextureSynthesisCS.SetBuffer(this.kernelID, "_UnfilledBufferConsume", unfilledBuffer);
        TextureSynthesisCS.SetBuffer(this.kernelID, "_FoundBuffer", foundBuffer);
        TextureSynthesisCS.SetBuffer(this.kernelID, "_TestBuffer", testBuffer);

        TextureSynthesisCS.SetTexture(this.kernelID, "_GaussianMap", GaussianMap);
        TextureSynthesisCS.SetTexture(this.kernelID, "_FilledBufferRead", renderTexture_FillMapRead);
        TextureSynthesisCS.SetTexture(this.kernelID, "_PaddedBufferRead", renderTexture_PaddedImageRead);
        TextureSynthesisCS.SetTexture(this.kernelID, "_FilledBufferWrite", renderTexture_FillMap);
        TextureSynthesisCS.SetTexture(this.kernelID, "_PaddedBufferWrite", renderTexture_PaddedImage);
        TextureSynthesisCS.SetTexture(this.kernelID, "_CandidatesBufferRead", renderTexture_Candidates);//read
        TextureSynthesisCS.SetTexture(this.kernelID, "gaussianMask", renderTexture_GaussianMask);
        //TextureSynthesisCS.SetTexture(this.kernelID, "_NbhdBuffer", renderTexture_Neighborhood);

        TextureSynthesisCS.Dispatch(this.kernelID,
                                    this.renderTexture_Output.width / this.resultKernelThreadSize.x,
                                    this.renderTexture_Output.height / this.resultKernelThreadSize.y,
                                    this.resultKernelThreadSize.z);
        var testData = new float4[1];
        testBuffer.GetData(testData);
        Debug.Log("testData " + testData[0]);

        var data = new int[1];
        foundBuffer.GetData(data);
        Debug.Log("data " + data[0]);
        if (data[0] == 0) {
            maxErrThreshold = maxErrThreshold * 1.1f;
        }
    }

    // Update is called once per frame
    void Update()
    {
        unfilledBuffer.SetCounterValue(0);
        int[] found = new int[1];
        found[0] = 0;
        foundBuffer.SetCounterValue(0);
        foundBuffer.SetData(found);

        var dataBefore = new int[1];
        foundBuffer.GetData(dataBefore);
        Debug.Log("foundBuffer dataBefore " + dataBefore[0]);
        //Update UnfilledBuffer
        kernelID = TextureSynthesisCS.FindKernel("UpdateImage");

        Graphics.CopyTexture(renderTexture_PaddedImage, renderTexture_PaddedImageRead);
        Graphics.CopyTexture(renderTexture_FillMap, renderTexture_FillMapRead);
        //TextureSynthesisCS.SetTexture(this.kernelID, "_SourceImage", renderTexture_PaddedImage);  //same size as output
        TextureSynthesisCS.SetTexture(this.kernelID, "_FillMap", renderTexture_FillMap);  //same size as output

        //this.TextureSynthesisCS.SetTexture
        //    (this.kernelID, "_PaddedBufferWrite", this.renderTexture_PaddedImageRead);
        //this.TextureSynthesisCS.SetTexture
        //    (this.kernelID, "_FilledBufferWrite", this.renderTexture_FillMapRead);
        TextureSynthesisCS.SetBuffer(this.kernelID, "_UnfilledBufferAppend", unfilledBuffer);

        TextureSynthesisCS.Dispatch(this.kernelID,
                                   this.renderTexture_PaddedImage.width / this.paddedKernelThreadSize.x,
                                   this.renderTexture_PaddedImage.height / this.paddedKernelThreadSize.y,
                                   this.paddedKernelThreadSize.z);

        ComputeBuffer.CopyCount(unfilledBuffer, countBuffer, 0);
        int[] counter = new int[1] { 0 };
        countBuffer.GetData(counter);

        int count = counter[0];
        Debug.Log("Append Count " + count);

        kernelID = TextureSynthesisCS.FindKernel("FindMatches");

        TextureSynthesisCS.SetFloat("_DeltaTime", Time.deltaTime);
        TextureSynthesisCS.SetFloat("_Time", Time.time);
        TextureSynthesisCS.SetFloat("_ErrThreshold", errThreshold);
        TextureSynthesisCS.SetFloat("_MaxErrThreshold", maxErrThreshold);

        TextureSynthesisCS.SetInt("_OutputSize", 64);
        TextureSynthesisCS.SetInt("_CandidatesWidth", candidatesCountW);
        TextureSynthesisCS.SetInt("_CandidatesHeight", candidatesCountH);
        TextureSynthesisCS.SetBuffer(this.kernelID, "_UnfilledBufferConsume", unfilledBuffer);
        TextureSynthesisCS.SetBuffer(this.kernelID, "_FoundBuffer", foundBuffer);
        TextureSynthesisCS.SetTexture(this.kernelID, "_GaussianMap", GaussianMap);
        TextureSynthesisCS.SetTexture(this.kernelID, "gaussianMask", renderTexture_GaussianMask);
        TextureSynthesisCS.SetTexture(this.kernelID, "_FilledBufferRead", renderTexture_FillMapRead);
        TextureSynthesisCS.SetTexture(this.kernelID, "_PaddedBufferRead", renderTexture_PaddedImageRead);
        TextureSynthesisCS.SetTexture(this.kernelID, "_FilledBufferWrite", renderTexture_FillMap);
        TextureSynthesisCS.SetTexture(this.kernelID, "_PaddedBufferWrite", renderTexture_PaddedImage);
        TextureSynthesisCS.SetTexture(this.kernelID, "_CandidatesBuffer", renderTexture_Candidates);//read
        TextureSynthesisCS.SetTexture(this.kernelID, "_NbhdBuffer", renderTexture_Neighborhood);

        //TextureSynthesisCS.SetTexture(this.kernelID, "Result", this.renderTexture_Output);

        TextureSynthesisCS.Dispatch(this.kernelID,
                                    this.renderTexture_Output.width / this.resultKernelThreadSize.x,
                                    this.renderTexture_Output.height / this.resultKernelThreadSize.y,
                                    this.resultKernelThreadSize.z);


        var data = new int[1];
        foundBuffer.GetData(data);
        Debug.Log("foundBuffer data " + data[0]);
        if (data[0] == 0)
        {
            maxErrThreshold = maxErrThreshold * 1.1f;
        }

        // 実行して得られた結果をテクスチャとして設定します。

        Rend[0].material.mainTexture = this.renderTexture_PaddedImage;
        Rend[1].material.mainTexture = this.renderTexture_FillMap;
        Rend[2].material.mainTexture = this.renderTexture_Candidates;
        Rend[3].material.mainTexture = this.renderTexture_PaddedImageRead;
        Rend[4].material.mainTexture = this.renderTexture_FillMapRead;
        Rend[5].material.mainTexture = this.renderTexture_GaussianMask;
    }

    void ApplySeed(ref Texture2D texture, ref Texture2D fillMap, Texture2D sample, int seedSize)
    {
        var row = sample.height;
        var column = sample.width;
        var margin = seedSize - 1;

        var randRow = UnityEngine.Random.Range(0, row - margin);
        var randCol = UnityEngine.Random.Range(0, column - margin);

        var seedPixels = sample.GetPixels(randCol, randRow, seedSize, seedSize);

        var startPt = (texture.width - 1) / 2 - (seedSize - 1) / 2;

        for (int i = 0; i < seedSize; i++)
        {
            for (int j = 0; j < seedSize; j++)
            {
                texture.SetPixel(startPt + j, startPt + i, seedPixels[j + i * seedSize]);
                fillMap.SetPixel(startPt + j, startPt + i, Color.red);
            }
        }
        texture.Apply();
        fillMap.Apply();
    }

    Texture2D CalculateGaussianZ(Texture2D source, int size, float sigma, float amp = 1.0f)
    {
        int center = size / 2; // 模板的中心位置，也就是座標原點

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                float x = (Mathf.Pow((c - center), 2)) / (2.0f * sigma * sigma);
                float y = (Mathf.Pow((r - center), 2)) / (2.0f * sigma * sigma);
                //float vvv = amp * Mathf.Exp(-(x + y));

                float scalar = amp * Mathf.Exp(-(x + y));
                Color col = new Color(scalar, scalar, scalar, 1);
                source.SetPixel(c, r, col);
                //Debug.Log("r " + vvv);
            }
        }
        source.Apply();

        return source;
    }

    void GetCandidates(ref Pixel[] candi, Texture2D sampleImage, int windowSize, int candidateH, int candidateV)
    { //for loop get every pixel candidate to compare with neiborhood to be filled

        for (int i = 0; i < candidateH; i++)
        {
            for (int j = 0; j < candidateV; j++)
            {
                //candi[i, j] = new image(sampleImage, new int2(i, j), windowSize);
            }
        }
    }

    float[] CalculateGaussianZ(int size, float sigma, float amp = 1.0f)
    {
        int center = size / 2; // 模板的中心位置，也就是座標原點
        float[] gaussianMat = new float[size * size];
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                float x = (Mathf.Pow((c - center), 2)) / (2.0f * sigma * sigma);
                float y = (Mathf.Pow((r - center), 2)) / (2.0f * sigma * sigma);
                //float vvv = amp * Mathf.Exp(-(x + y));

                gaussianMat[c + r * (size)] = amp * Mathf.Exp(-(x + y));

                //Debug.Log("r " + vvv);
            }
        }
        return gaussianMat;
    }

    void CreateRenderTexture(ref RenderTexture rt, int w, int h, RenderTextureFormat format, FilterMode filter, TextureWrapMode wrap, bool useInComputeshader)
    {
        rt = new RenderTexture(w, h, 0, format);
        rt.hideFlags = HideFlags.DontSave;
        rt.filterMode = filter;
        rt.wrapMode = wrap;
        if (useInComputeshader)
        {
            rt.enableRandomWrite = true;
            rt.Create();
        }
    }
    void DeleteRenderTexture(ref RenderTexture rt)
    {
        if (rt != null)
        {
            if (RenderTexture.active == rt)
                Graphics.SetRenderTarget(null);
            rt.Release();
            if (Application.isEditor)
                RenderTexture.DestroyImmediate(rt);
            else
                RenderTexture.Destroy(rt);
            rt = null;
        }
    }

    void ClearRenderTexture(ref RenderTexture rt, Color? color = null)
    {
        RenderTexture store = RenderTexture.active;
        Graphics.SetRenderTarget(rt);
        GL.Clear(false, true, color ?? Color.clear);
        Graphics.SetRenderTarget(store);
    }

    void OnDestroy()
    {
        DeleteBuffer(unfilledBuffer);
        DeleteBuffer(countBuffer);
        DeleteBuffer(foundBuffer);
        DeleteBuffer(testBuffer);

    }

    void DeleteBuffer(ComputeBuffer buffer)
    {
        if (buffer != null)
        {
            buffer.Release();
            buffer = null;
        }
    }

    void SwapBuffer(ref ComputeBuffer ping, ref ComputeBuffer pong)
    {
        ComputeBuffer temp = ping;
        ping = pong;
        pong = temp;
    }
}
