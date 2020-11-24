using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using System.Linq;

public class TextureSynthesis : MonoBehaviour
{
    public List<Texture2D> SampleTextures;
    public Renderer Rend;
    public int OutputSize;
    public int WindowSize;//ODD ONLY
    public bool ImageNotFilled = true;

    #region Constant
    const float errThreshold = 0.1f;
    const int seedSize = 3;
    const float Sigma = 6.4f;
    #endregion

    #region Private Variables
    image _paddedImg;
    image _img;
    image[,] _candidates;
    image _neighborhood;

    List<int2> _unfilledPixelList = new List<int2>();

    float[,] _gaussianFilter;
    float[,] _gaussianMask;
    
    int _halfWindow;
    int _candidatesCountW;
    int _candidatesCountH;
    int2 _candidatesCountWH;

    float _maxErrThreshold = 0.3f;
    bool _found;
    Texture2D _dummyTexture;

    int index;
    #endregion
    public class image
    {
        public int RowYCount;
        public int ColumnXCount;
        public int OutputSize;
        public int[,] IsFilledXY;
        public Color[,] ColorXY;
        public Texture2D Texture;
        //public int filledCount;

        public image(int size)
        {
            RowYCount = size;
            ColumnXCount = size;
            OutputSize = size;
            //Texture = new Texture2D(ColumnXCount, RowYCount);
            IsFilledXY = new int[ColumnXCount, RowYCount];
            ColorXY = new Color[ColumnXCount, RowYCount];

            for (int m = 0; m < ColumnXCount; m++)
            {
                for (int n = 0; n < RowYCount; n++)
                {
                    IsFilledXY[m, n] = 0;
                    ColorXY[m, n] = new Color(0, 0, 0, 0);
                    //Texture.SetPixel(m, n, ColorXY[m, n]);
                }
            }
        }

        int2 startindex;
        //resize image
        public image(image copyImgData, int2 centerIndex, int size)
        {
            RowYCount = size;
            ColumnXCount = size;
            OutputSize = RowYCount;
            //Texture = new Texture2D(ColumnXCount, RowYCount);
            IsFilledXY = new int[ColumnXCount, RowYCount];
            ColorXY = new Color[ColumnXCount, RowYCount];
            //filledCount = 0;

            startindex = int2(centerIndex.x - (size - 1) / 2, centerIndex.y - (size - 1) / 2);

            for (int m = 0; m < ColumnXCount; m++)
            {
                for (int n = 0; n < RowYCount; n++)
                {
                    IsFilledXY[m, n] = copyImgData.IsFilledXY[startindex.x + m, startindex.y + n];
                    ColorXY[m, n] = copyImgData.ColorXY[startindex.x + m, startindex.y + n];
                    //Texture.SetPixel(m, n, ColorXY[m, n]);
                    //filledCount = filledCount + (IsFilledXY[m, n] == 1 ? 1 : 0);
                }
            }
        }

        //pad image
        public image(image copyImgData, int windowSize)
        {
            RowYCount = copyImgData.RowYCount + windowSize - 1;
            ColumnXCount = copyImgData.ColumnXCount + windowSize - 1;
            OutputSize = RowYCount;
            //Texture = new Texture2D(ColumnXCount, RowYCount);
            IsFilledXY = new int[ColumnXCount, RowYCount];
            ColorXY = new Color[ColumnXCount, RowYCount];

            for (int m = 0; m < ColumnXCount; m++)
            {
                for (int n = 0; n < RowYCount; n++)
                {
                    IsFilledXY[m, n] = 100;
                    ColorXY[m, n] = new Color(1, 0, 0, 1);
                    //Texture.SetPixel(m, n, ColorXY[m, n]);
                }
            }

            var offset = (windowSize - 1) / 2;
            for (int j = 0; j < copyImgData.ColumnXCount; j++)
            {
                for (int i = 0; i < copyImgData.RowYCount; i++)
                {
                    IsFilledXY[j + offset, i + offset] = copyImgData.IsFilledXY[j, i];
                    ColorXY[j + offset, i + offset] = copyImgData.ColorXY[j, i];
                    //Texture.SetPixel(j + offset, i + offset, ColorXY[j + offset, i + offset]);
                }
            }
        }

        //copy from Texture2D
        public image(Texture2D sampleTexture, int2 coord, int windowSize)
        {
            RowYCount = windowSize;
            ColumnXCount = windowSize;
            OutputSize = RowYCount;
            Texture = new Texture2D(ColumnXCount, RowYCount);
            IsFilledXY = new int[ColumnXCount, RowYCount];
            ColorXY = new Color[ColumnXCount, RowYCount];

            var copyPixels = sampleTexture.GetPixels(coord.x, coord.y, windowSize, windowSize);

            for (int m = 0; m < ColumnXCount; m++)
            {
                for (int n = 0; n < RowYCount; n++)
                {
                    IsFilledXY[m, n] = 0;
                    ColorXY[m, n] = copyPixels[m + n * ColumnXCount];
                    Texture.SetPixel(m, n, ColorXY[m, n]);
                }
            }
            Texture.Apply();
        }
    }
    // Start is called before the first frame update
    void Start()
    {
        if (WindowSize % 2 == 0)
            WindowSize = WindowSize + 1;

        _dummyTexture = new Texture2D(OutputSize, OutputSize);

        _img = new image(OutputSize);
        ApplySeedImage(SampleTextures[0], seedSize, ref _img, ref _dummyTexture);
        Rend.material.mainTexture = _dummyTexture;

        _paddedImg = new image(_img, WindowSize);

        _gaussianFilter = new float[WindowSize, WindowSize];
        _gaussianFilter = CalculateGaussianZ(WindowSize, WindowSize / Sigma);

        //GrowImage(_gaussianFilter);

        index = 0;

        _halfWindow = (WindowSize - 1) / 2;

        _candidatesCountW = SampleTextures[0].width - WindowSize + 1;
        _candidatesCountH = SampleTextures[0].height - WindowSize + 1;
        _candidatesCountWH = new int2(_candidatesCountW, _candidatesCountH);

        _candidates = new image[_candidatesCountW, _candidatesCountH];
        GetCandidates(ref _candidates, SampleTextures[0], WindowSize, _candidatesCountW, _candidatesCountH);

        _gaussianMask = new float[_gaussianFilter.GetLength(0), _gaussianFilter.GetLength(1)];
        _unfilledPixelList = new List<int2>();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            image[,] candidates = new image[_candidatesCountW, _candidatesCountH];
            GetCandidates(ref candidates, SampleTextures[0], WindowSize, _candidatesCountW, _candidatesCountH);

            int countH = index / _candidatesCountW;
            ShowCandidates(candidates[index % _candidatesCountW, countH * _candidatesCountW]);

            index++;
            index %= _candidatesCountH;
        }

        if (ImageNotFilled)
        {
            _found = false;
            _unfilledPixelList = GetUnfilledNeighbors(_img);

            foreach (int2 p in _unfilledPixelList)
            {
                _neighborhood = GetNeighborhood(_paddedImg, p, WindowSize, _halfWindow);
                CalculateMask(ref _gaussianMask, _neighborhood, _gaussianFilter);
                FindMatches(ref _img, _neighborhood, _candidates, _candidatesCountWH, _gaussianMask, p);
            }

            if (!_found)
            {
                _maxErrThreshold = _maxErrThreshold * 1.1f;
            }

            UpdatePaddedImage(ref _paddedImg, ref ImageNotFilled, _img, WindowSize, _halfWindow);

        }
    }

    void ApplySeedImage(Texture2D sample, int seedSize, ref image img, ref Texture2D texture)
    {
        var row = sample.height;
        var column = sample.width;
        var margin = seedSize - 1;

        var randRow = UnityEngine.Random.Range(0, row - margin);
        var randCol = UnityEngine.Random.Range(0, column - margin);

        var seedPixels = sample.GetPixels(randCol, randRow, seedSize, seedSize);

        var startPt = (img.OutputSize - 1) / 2 - (seedSize - 1) / 2;

        for (int i = 0; i < seedSize; i++)
        {
            for (int j = 0; j < seedSize; j++)
            {
                texture.SetPixel(startPt + j, startPt + i, seedPixels[j + i * seedSize]);
                img.IsFilledXY[startPt + j, startPt + i] = 1;
                img.ColorXY[startPt + j, startPt + i] = seedPixels[j + i * seedSize];
            }
        }
        texture.Apply();
    }

    List<int2> GetUnfilledNeighbors(image img)
    {
        List<int2> unfilledList = new List<int2>();

        for (int j = 0; j < img.ColumnXCount; j++)
        {
            for (int i = 0; i < img.RowYCount; i++)
            {
                if (img.IsFilledXY[j, i] == 0)
                    unfilledList.Add(int2(j, i));
            }
        }
        return unfilledList;
    }

    image GetNeighborhood(image paddedOuputImage, int2 coord, int windowSize, int halfWindowSize)
    {
        coord.x += halfWindowSize;
        coord.y += halfWindowSize;

        image neighborhood = new image(paddedOuputImage, coord, windowSize);
        return neighborhood;
    }

    void GetCandidates(ref image[,] candi, Texture2D sampleImage, int windowSize, int candidateH, int candidateV)
    { //for loop get every pixel candidate to compare with neiborhood to be filled

        for (int i = 0; i < candidateH; i++)
        {
            for (int j = 0; j < candidateV; j++)
            {
                candi[i, j] = new image(sampleImage, int2(i, j), windowSize);
            }
        }
    }

    void ShowCandidates(image candi)
    {
        Rend.material.mainTexture = candi.Texture;
    }

    float[,] CalculateGaussianZ(int size, float sigma, float amp = 1.0f)
    {
        int center = size / 2; // 模板的中心位置，也就是座標原點
        float[,] gaussianMat = new float[size, size];
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                float x = (Mathf.Pow((c - center), 2)) / (2.0f * sigma * sigma);
                float y = (Mathf.Pow((r - center), 2)) / (2.0f * sigma * sigma);
                //float vvv = amp * Mathf.Exp(-(x + y));

                gaussianMat[c, r] = amp * Mathf.Exp(-(x + y));

                //Debug.Log("r " + vvv);
            }
        }
        return gaussianMat;
    }

    void CalculateMask(ref float[,] mask, image nbhd, float[,] gauss)
    {
        float sum = 0;
        for (int i = 0; i < nbhd.OutputSize; i++)
        {
            for (int j = 0; j < nbhd.OutputSize; j++)
            {
                sum += nbhd.IsFilledXY[i, j] * gauss[i, j];
            }
        }
        for (int i = 0; i < nbhd.OutputSize; i++)
        {
            for (int j = 0; j < nbhd.OutputSize; j++)
            {
                mask[i, j] = nbhd.IsFilledXY[i, j] * gauss[i, j] / sum;
            }
        }
    }

    public static Vector3 ToVector3(Vector4 parent)
    {
        return new Vector3(parent.x, parent.y, parent.z);
    }

    void FindMatches(ref image target, image neighborhood, image[,] candidates, int2 candidatesStepXY, float[,] _gaussianMask, int2 coord)
    {
        float[] distances = new float[candidatesStepXY.x * candidatesStepXY.y];
        Vector4 temp = Vector4.zero;
        float diff;

        for (int i = 0; i < candidatesStepXY.x; i++)
        {
            for (int j = 0; j < candidatesStepXY.y; j++)
            {
                diff = 0;
                for (int m = 0; m < neighborhood.ColumnXCount; m++)
                {
                    for (int n = 0; n < neighborhood.RowYCount; n++)
                    {
                        temp = candidates[i, j].ColorXY[m, n] - neighborhood.ColorXY[m, n];
                        diff = diff + ToVector3(temp).sqrMagnitude * _gaussianMask[m, n];
                    }
                }
                distances[i + j * candidatesStepXY.x] = diff / 1;
            }
        }

        var minThreshold = distances.Min() * (1 + errThreshold);
        List<int2> minIndexs = new List<int2>();

        for (int i = 0; i < candidatesStepXY.x; i++)
        {
            for (int j = 0; j < candidatesStepXY.y; j++)
            {
                if (distances[i + j * candidatesStepXY.x] <= minThreshold)
                {
                    minIndexs.Add(int2(i, j));
                }
            }
        }

        if (minIndexs.Count > 0)
        {
            int randomPick = UnityEngine.Random.Range(0, minIndexs.Count);
            int2 selectedIndex = minIndexs[randomPick];
            float selectedError = distances[selectedIndex.x + selectedIndex.y * candidatesStepXY.x];

            if (selectedError < _maxErrThreshold)
            {
                var matchedPatch = candidates[selectedIndex.x, selectedIndex.y];

                target.ColorXY[coord.x, coord.y] = matchedPatch.ColorXY[matchedPatch.OutputSize / 2, matchedPatch.OutputSize / 2];
                target.IsFilledXY[coord.x, coord.y] = 1;

                _dummyTexture.SetPixel(coord.x, coord.y, target.ColorXY[coord.x, coord.y]);
                _dummyTexture.Apply();
                Rend.material.mainTexture = _dummyTexture;

                _found = true;
            }
        }
    }

    void UpdatePaddedImage(ref image pad, ref bool notFullyFilled, image source, int windowSize, int halfWindowSize)
    {
        int count = 0;
        for (int i = 0; i < source.OutputSize; i++)
        {
            for (int j = 0; j < source.OutputSize; j++)
            {
                pad.IsFilledXY[i + halfWindowSize, j + halfWindowSize] = source.IsFilledXY[i, j];
                pad.ColorXY[i + halfWindowSize, j + halfWindowSize] = source.ColorXY[i, j];
                //pad.Texture.SetPixel();
                count += source.IsFilledXY[i, j];
            }
        }

        if (count < source.OutputSize * source.OutputSize)
        {
            notFullyFilled = true;
        }
        else
        {
            notFullyFilled = false;
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(0, -35.8f, 0), new Vector3(0, 35.8f, 0));
        Gizmos.DrawLine(new Vector3(10, -35.8f, 0), new Vector3(10, 35.8f, 0));
        Gizmos.DrawLine(new Vector3(-35.8f, 0, 0), new Vector3(35.8f, 0, 0));
        Gizmos.DrawLine(new Vector3(-35.8f, 10, 0), new Vector3(35.8f, 10, 0));
        Gizmos.DrawLine(new Vector3(0, 0, -35.8f), new Vector3(0, 0, 35.8f));

        Gizmos.color = Color.yellow;

#if UNITY_EDITOR
        //for (int i = 0; i < test.Length; i++)
        //{
        //    UnityEditor.Handles.color = new Color(i, 1, 0, 1);
        //    UnityEditor.Handles.DrawWireDisc(new Vector3(test[i].x * 10, test[i].y * 10, 0), Vector3.forward, 0.1f);
        //}
#endif
    }

}












