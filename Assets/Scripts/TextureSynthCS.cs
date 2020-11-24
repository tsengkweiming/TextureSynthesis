using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using System.Linq;
using System.Runtime.InteropServices;

public class TextureSynthCS : MonoBehaviour
{
    #region Resources
    public ComputeShader TextureSynthesisCS;
    public List<Texture2D> SampleTextures;
    public Renderer SampleSource;
    public Renderer[] Rend;

    public OutputWH OutputSize = OutputWH.WxH_64x64;
    public WindowWH WindowSize = WindowWH._15x15;
    public MetaPad MetaPadSize = MetaPad._10;
    public CandidateStepPad CandidateStepMapPadSize = CandidateStepPad._10;

    public RenderTexture RenderTexture_Candidates;
    public RenderTexture RenderTexture_Output;
    public RenderTexture RenderTexture_PaddedImage;
    public RenderTexture RenderTexture_FillMap;
    public RenderTexture RenderTexture_GaussianMask;
    public RenderTexture RenderTexture_PaddedImageRead;
    public RenderTexture RenderTexture_FillMapRead;
    public Texture2D GaussianMap;
    #endregion

    #region Size
    public enum OutputWH
    {
        WxH_64x64,
        WxH_128x128,
        WxH_256x256,
        WxH_512x512,
        WxH_1024x1024,
        WxH_2048x2048
    };

    public enum WindowWH
    {
        _11x11,
        _15x15,
        _19x19,
        _23x23,
        _27x27,
        _31x31
    };

    public enum MetaPad
    {
        _0,
        _6,
        _8,
        _10
    };
    public enum CandidateStepPad
    {
        _0,
        _4,
        _6,
        _10
    };
    #endregion

    #region Private Variables
    int _kernelID;

    [Range(0.00005f, 3.5f)]
    [SerializeField]
    float _maxErrThreshold = 0.3f;

    int _candidateCountW;
    int _candidateCountH;
    int _candidatesCountW;
    int _candidatesCountH;

    int _outputSize;
    int _windowSize;
    int _metaPadSize;
    int _candidateStepMapPad;
    int _paddedImageSize;
    int _appendCount;

    ComputeBuffer _unfilledBuffer;
    ComputeBuffer _countBuffer;
    ComputeBuffer _foundBuffer;
    ComputeBuffer _selectedBuffer;
    ComputeBuffer _testBuffer;

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

    ThreadSize _candidateKernelThreadSize;
    ThreadSize _paddedKernelThreadSize;
    ThreadSize _resultKernelThreadSize;
    #endregion

    const float errThreshold = 0.1f;
    const float sigma = 6.4f;

    public ComputeBuffer UnfilledBuffer => _unfilledBuffer ?? null;

    #region Init
    void Init()
    {
        switch (OutputSize)
        {
            case OutputWH.WxH_64x64:
                _outputSize = 64;
                break;

            case OutputWH.WxH_128x128:
                _outputSize = 128;
                break;

            case OutputWH.WxH_256x256:
                _outputSize = 256;
                break;

            case OutputWH.WxH_512x512:
                _outputSize = 512;
                break;

            case OutputWH.WxH_1024x1024:
                _outputSize = 1024;
                break;

            case OutputWH.WxH_2048x2048:
                _outputSize = 2048;
                break;

            default:
                _outputSize = 64;
                break;
        }

        switch (WindowSize)
        {
            case WindowWH._15x15:
                _windowSize = 15;
                break;

            case WindowWH._11x11:
                _windowSize = 11;
                break;

            case WindowWH._19x19:
                _windowSize = 19;
                break;

            case WindowWH._23x23:
                _windowSize = 23;
                break;

            case WindowWH._27x27:
                _windowSize = 27;
                break;

            case WindowWH._31x31:
                _windowSize = 31;
                break;

            default:
                _windowSize = 15;
                break;
        }

        switch (MetaPadSize)
        {
            case MetaPad._10:
                _metaPadSize = 10;
                break;

            case MetaPad._0:
                _metaPadSize = 0;
                break;

            case MetaPad._6:
                _metaPadSize = 6;
                break;

            case MetaPad._8:
                _metaPadSize = 8;
                break;

            default:
                _metaPadSize = 10;
                break;
        }

        switch (CandidateStepMapPadSize)
        {
            case CandidateStepPad._10:
                _candidateStepMapPad = 10;
                break;

            case CandidateStepPad._0:
                _candidateStepMapPad = 0;
                break;

            case CandidateStepPad._4:
                _candidateStepMapPad = 4;
                break;

            case CandidateStepPad._6:
                _candidateStepMapPad = 6;
                break;

            default:
                _candidateStepMapPad = 10;
                break;
        }
    }
    #endregion

    // Start is called before the first frame update
    void Start()
    {
        SampleSource.material.mainTexture = SampleTextures[0];
        Init();

        _candidateCountW = SampleTextures[0].width - _windowSize + 1;
        _candidateCountH = SampleTextures[0].height - _windowSize + 1;
        _candidatesCountW = (_windowSize + 1) * _candidateCountW;// + _candidateStepMapPad;
        _candidatesCountH = (_windowSize + 1) * _candidateCountH;// + _candidateStepMapPad;

        _paddedImageSize = _outputSize + _windowSize - 1 + _metaPadSize;

        _unfilledBuffer = new ComputeBuffer(_outputSize * _outputSize * 1, Marshal.SizeOf(typeof(int2)), ComputeBufferType.Append);
        _countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
        _foundBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(int)));
        _testBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(float4)));
        _selectedBuffer = new ComputeBuffer(_candidateCountW * _candidateCountH, Marshal.SizeOf(typeof(int2)));

        int[] temp = new int[1];
        temp[0] = 0;

        _countBuffer.SetData(temp);
        _unfilledBuffer.SetCounterValue(0);
        _selectedBuffer.SetCounterValue(0);

        int[] found = new int[1];
        found[0] = 0;
        _foundBuffer.SetData(found);

        var paddedImageWH = int2(_outputSize + _windowSize - 1 + _metaPadSize, _outputSize + _windowSize - 1 + _metaPadSize);

        this.RenderTexture_Output = CreateRenderTexture(_outputSize, _outputSize);
        this.RenderTexture_Candidates = CreateRenderTexture(_candidatesCountW, _candidatesCountH);
        this.RenderTexture_GaussianMask = CreateRenderTexture(_windowSize, _windowSize);
        this.RenderTexture_PaddedImage = CreateRenderTexture(paddedImageWH.x, paddedImageWH.y);
        this.RenderTexture_FillMap = CreateRenderTexture(paddedImageWH.x, paddedImageWH.y);
        this.RenderTexture_PaddedImageRead = CreateRenderTexture(paddedImageWH.x, paddedImageWH.y);
        this.RenderTexture_FillMapRead = CreateRenderTexture(paddedImageWH.x, paddedImageWH.y);

        GaussianMap = new Texture2D(_windowSize, _windowSize);
        GaussianMap = CalculateGaussianZ(GaussianMap, _windowSize, _windowSize / sigma);

        var seedImage = new Texture2D(_outputSize, _outputSize);
        var fillMap = new Texture2D(_outputSize, _outputSize);

        ApplySeed(ref seedImage, ref fillMap, SampleTextures[0]);

        //Get Candidates
        _kernelID = TextureSynthesisCS.FindKernel("GetCandidates");
        DispatchCandidateKernel(TextureSynthesisCS, _kernelID);

        //Generate PaddedImage
        _kernelID = TextureSynthesisCS.FindKernel("PaddedSeedImage");
        DispatchSeedKernel(TextureSynthesisCS, _kernelID, seedImage, fillMap, ref _appendCount);

        Graphics.CopyTexture(RenderTexture_PaddedImage, RenderTexture_PaddedImageRead);
        Graphics.CopyTexture(RenderTexture_FillMap, RenderTexture_FillMapRead);

        var unfilledBufferData = new int2[4096];
        _unfilledBuffer.GetData(unfilledBufferData);
        Debug.Log("unfilledBufferData " + unfilledBufferData[0]);

        //Find Matches
        _kernelID = TextureSynthesisCS.FindKernel("FindMatches");

        uint resultThreadSizeX, resultThreadSizeY, resultThreadSizeZ;

        TextureSynthesisCS.GetKernelThreadGroupSizes
            (_kernelID,
             out resultThreadSizeX, out resultThreadSizeY, out resultThreadSizeZ);
        _resultKernelThreadSize
                = new ThreadSize(resultThreadSizeX, resultThreadSizeY, resultThreadSizeZ);

        DispatchFindMatchesKernel(TextureSynthesisCS, _kernelID, _unfilledBuffer, _selectedBuffer, _foundBuffer);
    }

    // Update is called once per frame
    void Update()
    {
        if (_appendCount > 1)
        {
            int[] found = new int[1];
            found[0] = 0;
            _foundBuffer.SetCounterValue(0);
            _foundBuffer.SetData(found);

            _unfilledBuffer.SetCounterValue(0);
            _selectedBuffer.SetCounterValue(0);

            var dataBefore = new int[1];
            _foundBuffer.GetData(dataBefore);
            Debug.Log("foundBuffer dataBefore " + dataBefore[0]);

            Graphics.CopyTexture(RenderTexture_PaddedImage, RenderTexture_PaddedImageRead);
            Graphics.CopyTexture(RenderTexture_FillMap, RenderTexture_FillMapRead);

            //Update UnfilledBuffer
            _kernelID = TextureSynthesisCS.FindKernel("UpdateImage");

            DispatchUpdateKernal(TextureSynthesisCS, _kernelID, _unfilledBuffer, _countBuffer, ref _appendCount);

            _kernelID = TextureSynthesisCS.FindKernel("FindMatches");

            DispatchFindMatchesKernel(TextureSynthesisCS, _kernelID, _unfilledBuffer, _selectedBuffer, _foundBuffer);
        }

        Graphics.CopyTexture(RenderTexture_PaddedImage, 0, 0, _windowSize / 2, _windowSize / 2, _outputSize, _outputSize, RenderTexture_Output, 0, 0, 0, 0);

        // 実行して得られた結果をテクスチャとして設定します。
        Rend[0].material.mainTexture = RenderTexture_PaddedImage;
        Rend[1].material.mainTexture = RenderTexture_FillMap;
        Rend[2].material.mainTexture = RenderTexture_Candidates;
        Rend[3].material.mainTexture = GaussianMap;
        Rend[4].material.mainTexture = RenderTexture_Output;
    }

    void ApplySeed(ref Texture2D texture, ref Texture2D fillMap, Texture2D sample, int seedSize = 3)
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
        int center = (size - 1) / 2; // 模板的中心位置，也就是座標原點

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

    RenderTexture CreateRenderTexture(int sizeX, int sizeY){
        RenderTexture renderTexture;

        renderTexture = new RenderTexture(sizeX, sizeY, 0, RenderTextureFormat.ARGB32);
        renderTexture.enableRandomWrite = true;
        renderTexture.Create();

        return renderTexture;
    }

    void DispatchUpdateKernal(ComputeShader cs, int kernalID, ComputeBuffer unfillBuffer, ComputeBuffer countBuffer, ref int appendCount) {
        cs.SetTexture(kernalID, "_FillMap", RenderTexture_FillMap);  //same size as output
        cs.SetBuffer(kernalID, "_UnfilledBufferAppend", unfillBuffer);

        cs.Dispatch(kernalID,
                    RenderTexture_PaddedImage.width / _paddedKernelThreadSize.x,
                    RenderTexture_PaddedImage.height / _paddedKernelThreadSize.y,
                    _paddedKernelThreadSize.z);

        ComputeBuffer.CopyCount(unfillBuffer, countBuffer, 0);

        int[] counter = new int[1] { 0 };
        countBuffer.GetData(counter);

        appendCount = counter[0];
        Debug.Log("Append Count " + appendCount);
    }

    void DispatchFindMatchesKernel(ComputeShader cs, int kernalID, ComputeBuffer unfillBuffer, ComputeBuffer selectedBuffer, ComputeBuffer foundBuffer) {

        cs.SetFloat("_DeltaTime", Time.deltaTime);
        cs.SetFloat("_Time", Time.time);
        cs.SetFloat("_Rand", UnityEngine.Random.Range(0.0f, 1.0f));
        cs.SetFloat("_ErrThreshold", errThreshold);
        cs.SetFloat("_MaxErrThreshold", _maxErrThreshold);
        cs.SetInt("_OutputSize", _outputSize);
        cs.SetInt("_WindowSize", _windowSize);
        cs.SetInt("_PaddedWindowSize", _windowSize + 1);
        cs.SetInt("_CandidatesWidth", _candidateCountW);
        cs.SetInt("_CandidatesHeight", _candidateCountH);

        cs.SetBuffer(kernalID, "_UnfilledBufferConsume", unfillBuffer);
        cs.SetBuffer(kernalID, "_SelectedIndexBuffer", selectedBuffer);
        cs.SetBuffer(kernalID, "_FoundBuffer", foundBuffer);
        //cs.SetBuffer(kernalID, "_TestBuffer", _testBuffer);

        cs.SetTexture(kernalID, "_GaussianMap", GaussianMap);
        cs.SetTexture(kernalID, "_FilledBufferRead", RenderTexture_FillMapRead);
        cs.SetTexture(kernalID, "_PaddedBufferRead", RenderTexture_PaddedImageRead);
        cs.SetTexture(kernalID, "_FilledBufferWrite", RenderTexture_FillMap);
        cs.SetTexture(kernalID, "_PaddedBufferWrite", RenderTexture_PaddedImage);
        cs.SetTexture(kernalID, "_CandidatesBufferRead", RenderTexture_Candidates);//read
        cs.SetTexture(kernalID, "gaussianMask", RenderTexture_GaussianMask);

        cs.Dispatch(kernalID,
                    RenderTexture_Output.width / _resultKernelThreadSize.x,
                    RenderTexture_Output.height / _resultKernelThreadSize.y,
                    _resultKernelThreadSize.z); // groupで実行する

        //var testData = new float4[1];
        //_testBuffer.GetData(testData);
        //Debug.Log("testData " + testData[0]);

        var data = new int[1];
        foundBuffer.GetData(data);
        Debug.Log("data " + data[0]);

        if (data[0] == 0)
        {
            _maxErrThreshold = _maxErrThreshold * 1.1f;
        }
    }

    void DispatchSeedKernel(ComputeShader cs, int kernalID, Texture2D seedImage, Texture2D fillMap, ref int appendCount) {
        uint paddedThreadSizeX, paddedThreadSizeY, paddedThreadSizeZ;

        cs.GetKernelThreadGroupSizes
            (kernalID,
             out paddedThreadSizeX, out paddedThreadSizeY, out paddedThreadSizeZ);
        _paddedKernelThreadSize
                = new ThreadSize(paddedThreadSizeX, paddedThreadSizeY, paddedThreadSizeZ);

        cs.SetInt("_WindowSize", _windowSize);
        cs.SetInt("_MetaPadSize", _metaPadSize);

        cs.SetTexture(kernalID, "_SourceImage", seedImage);
        cs.SetTexture(kernalID, "_FillMap", fillMap);

        cs.SetTexture
            (kernalID, "_PaddedBufferWrite", RenderTexture_PaddedImage);
        cs.SetTexture
            (kernalID, "_FilledBufferWrite", RenderTexture_FillMap);
        cs.SetBuffer(_kernelID, "_UnfilledBufferAppend", _unfilledBuffer);

        cs.Dispatch(kernalID,
                    RenderTexture_PaddedImage.width / _paddedKernelThreadSize.x,
                    RenderTexture_PaddedImage.height / _paddedKernelThreadSize.y,
                    _paddedKernelThreadSize.z);

        ComputeBuffer.CopyCount(_unfilledBuffer, _countBuffer, 0);
        int[] counter = new int[1] { 0 };
        _countBuffer.GetData(counter);

        appendCount = counter[0];
        Debug.Log("Append Count " + appendCount);
    }

    void DispatchCandidateKernel(ComputeShader cs, int kernalID) {
        uint candidateThreadSizeX, candidateThreadSizeY, candidateThreadSizeZ;
        cs.GetKernelThreadGroupSizes
                    (kernalID,
                     out candidateThreadSizeX, out candidateThreadSizeY, out candidateThreadSizeZ);
        _candidateKernelThreadSize
                    = new ThreadSize(candidateThreadSizeX, candidateThreadSizeY, candidateThreadSizeZ);

        cs.SetInt("_WindowSize", _windowSize);
        cs.SetInt("_PaddedWindowSize", _windowSize + 1);

        cs.SetTexture(kernalID, "_SampleTexture", SampleTextures[0]);
        cs.SetTexture
                    (kernalID, "_CandidatesBuffer", RenderTexture_Candidates);

        cs.Dispatch(kernalID,
                    RenderTexture_Candidates.width / _candidateKernelThreadSize.x,
                    RenderTexture_Candidates.height / _candidateKernelThreadSize.y,
                    _candidateKernelThreadSize.z);
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
        DeleteBuffer(_unfilledBuffer);
        DeleteBuffer(_selectedBuffer);
        DeleteBuffer(_countBuffer);
        DeleteBuffer(_foundBuffer);
        DeleteBuffer(_testBuffer);

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
