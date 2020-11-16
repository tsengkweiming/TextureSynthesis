using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using System.Linq;

public class TextureSynthesis : MonoBehaviour
{
    public List<Texture2D> sampleTextures;
    public Renderer Rend;
    public int OutputSize;
    protected Texture2D result;
    protected RenderTexture resultRT;

    float errThreshold = 0.1f;
    float maxErrThreshold = 0.3f;
    public int WindowSize;//ODD ONLY
    int seedSize = 3;
    float Sigma = 6.4f;

    image paddedImg;
    image img;
    float[,] gaussianFilter;
    bool found;
    Texture2D dummyTexture;

    int index;

    int halfWindow;

    int candidatesCountW;
    int candidatesCountH;
    image[,] candidates;

    image Template;
    float[,] GaussianMask;
    float sumofWeight;
    List<int2> UnfilledPixelList = new List<int2>();
    public bool ImageNotFilled = true;

    public class image
    {
        public int rowYCount;
        public int columnXCount;
        public int outputSize;
        public int[,] isFilledXY;
        public Color[,] colorXY;
        public Texture2D texture;
        public int filledCount;

        public image(int size)
        {
            rowYCount = size;
            columnXCount = size;
            outputSize = size;
            //texture = new Texture2D(columnXCount, rowYCount);
            isFilledXY = new int[columnXCount, rowYCount];
            colorXY = new Color[columnXCount, rowYCount];

            for (int m = 0; m < columnXCount; m++)
            {
                for (int n = 0; n < rowYCount; n++)
                {
                    isFilledXY[m, n] = 0;
                    colorXY[m, n] = new Color(0, 0, 0, 0);
                    //texture.SetPixel(m, n, colorXY[m, n]);
                }
            }
        }

        int2 startindex;
        //resize image
        public image(image copyImgData, int2 centerIndex, int size)
        {
            rowYCount = size;
            columnXCount = size;
            outputSize = rowYCount;
            //texture = new Texture2D(columnXCount, rowYCount);
            isFilledXY = new int[columnXCount, rowYCount];
            colorXY = new Color[columnXCount, rowYCount];
            filledCount = 0;

            startindex = int2(centerIndex.x - (size - 1) / 2, centerIndex.y - (size - 1) / 2);

            for (int m = 0; m < columnXCount; m++)
            {
                for (int n = 0; n < rowYCount; n++)
                {
                    isFilledXY[m, n] = copyImgData.isFilledXY[startindex.x + m, startindex.y + n];
                    colorXY[m, n] = copyImgData.colorXY[startindex.x + m, startindex.y + n];
                    //texture.SetPixel(m, n, colorXY[m, n]);
                    filledCount = filledCount + (isFilledXY[m, n] == 1 ? 1 : 0);
                }
            }
        }

        //pad image
        public image(image copyImgData, int windowSize)
        {
            rowYCount = copyImgData.rowYCount + windowSize - 1;
            columnXCount = copyImgData.columnXCount + windowSize - 1;
            outputSize = rowYCount;
            //texture = new Texture2D(columnXCount, rowYCount);
            isFilledXY = new int[columnXCount, rowYCount];
            colorXY = new Color[columnXCount, rowYCount];

            for (int m = 0; m < columnXCount; m++)
            {
                for (int n = 0; n < rowYCount; n++)
                {
                    isFilledXY[m, n] = 100;
                    colorXY[m, n] = new Color(1, 0, 0, 1);
                    //texture.SetPixel(m, n, colorXY[m, n]);
                }
            }

            var offset = (windowSize - 1) / 2;
            for (int j = 0; j < copyImgData.columnXCount; j++)
            {
                for (int i = 0; i < copyImgData.rowYCount; i++)
                {
                    isFilledXY[j + offset, i + offset] = copyImgData.isFilledXY[j, i];
                    colorXY[j + offset, i + offset] = copyImgData.colorXY[j, i];
                    //texture.SetPixel(j + offset, i + offset, colorXY[j + offset, i + offset]);
                }
            }
        }

        //copy from Texture2D
        public image(Texture2D sampleTexture, int2 coord, int windowSize)
        {
            rowYCount = windowSize;
            columnXCount = windowSize;
            outputSize = rowYCount;
            texture = new Texture2D(columnXCount, rowYCount);
            isFilledXY = new int[columnXCount, rowYCount];
            colorXY = new Color[columnXCount, rowYCount];

            var copyPixels = sampleTexture.GetPixels(coord.x, coord.y, windowSize, windowSize);

            for (int m = 0; m < columnXCount; m++)
            {
                for (int n = 0; n < rowYCount; n++)
                {
                    isFilledXY[m, n] = 0;
                    colorXY[m, n] = copyPixels[m + n * columnXCount];
                    texture.SetPixel(m, n, colorXY[m, n]);
                }
            }
            texture.Apply();
        }
    }
    // Start is called before the first frame update
    void Start()
    {
        if (WindowSize % 2 == 0)
            WindowSize = WindowSize + 1;

        resultRT = new RenderTexture(OutputSize, OutputSize, 24);
        dummyTexture = new Texture2D(OutputSize, OutputSize);

        img = new image(OutputSize);
        //Rend.material.mainTexture = img.texture;
        ApplySeedImage(sampleTextures[0], seedSize, ref img, ref dummyTexture);
        paddedImg = new image(img, WindowSize);
        //paddedImg.texture.Apply();

        gaussianFilter = new float[WindowSize, WindowSize];
        gaussianFilter = CalculateGaussianZ(WindowSize, WindowSize / Sigma, 2);

        //GrowImage(gaussianFilter);

        index = 0;

        halfWindow = (WindowSize - 1) / 2;

        candidatesCountW = sampleTextures[0].width - WindowSize + 1;
        candidatesCountH = sampleTextures[0].height - WindowSize + 1;
        candidates = new image[candidatesCountW, candidatesCountH];
        GetCandidates(ref candidates, sampleTextures[0], WindowSize, candidatesCountW, candidatesCountH);

        GaussianMask = new float[gaussianFilter.GetLength(0), gaussianFilter.GetLength(1)];
        UnfilledPixelList = new List<int2>();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            var candidatesCountW = sampleTextures[0].width - WindowSize + 1;
            var candidatesCountH = sampleTextures[0].height - WindowSize + 1;
            image[,] candidates = new image[candidatesCountW, candidatesCountH];
            GetCandidates(ref candidates, sampleTextures[0], WindowSize, candidatesCountW, candidatesCountH);

            int countH = index / candidatesCountW;
            ShowCandidates(candidates[index % candidatesCountW, countH * candidatesCountW]);

            index++;
            index %= candidatesCountH;
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            img = new image(OutputSize);
            //Rend.material.mainTexture = img.texture;
            //ApplySeedImage(sampleTextures[0], seedSize, ref img);
            //paddedImg = new image(img, WindowSize);
            //paddedImg.texture.Apply();
            //GrowImage(gaussFilter);
        }

        if (ImageNotFilled)
        {
            found = false;
            UnfilledPixelList = GetUnfilledNeighbors(img);

            foreach (int2 p in UnfilledPixelList)
            {
                //break;
                Template = GetNeighborhood(paddedImg, p, WindowSize, halfWindow);
                sumofWeight = 0;
                if (Template.filledCount != 0) {
                    ApplyMask(ref GaussianMask, ref sumofWeight, Template, gaussianFilter);
                    FindMatches(Template, candidates, candidatesCountW, candidatesCountH, GaussianMask, sumofWeight, p);
                }
            }

            UpdatePaddedImage(ref paddedImg, ref ImageNotFilled, img, WindowSize, halfWindow);

            if (!found)
            {
                maxErrThreshold = maxErrThreshold * 1.1f;
            }
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

        var startPt = (img.outputSize - 1) / 2 - (seedSize - 1) / 2;

        for (int i = 0; i < seedSize; i++)
        {
            for (int j = 0; j < seedSize; j++)
            {
                texture.SetPixel(startPt + j, startPt + i, seedPixels[j + i * seedSize]);
                img.isFilledXY[startPt + j, startPt + i] = 1;
                img.colorXY[startPt + j, startPt + i] = seedPixels[j + i * seedSize];
            }
        }
        texture.Apply();
    }

    //void GrowImage(float[,] gaussFilter)
    //{
    //    var halfWindow = (WindowSize - 1) / 2;

    //    var candidatesCountW = sampleTextures[0].width - WindowSize + 1;
    //    var candidatesCountH = sampleTextures[0].height - WindowSize + 1;
    //    image[,] candidates = new image[candidatesCountW, candidatesCountH];
    //    GetCandidates(ref candidates, sampleTextures[0], WindowSize, candidatesCountW, candidatesCountH);

    //    image Template;
    //    float[,] GaussianMask = new float[gaussFilter.GetLength(0), gaussFilter.GetLength(1)];
    //    float sumofWeight;
    //    List<int2> UnfilledPixelList = new List<int2>();

    //    bool imageNotFilled = true;

    //    while (imageNotFilled)
    //    {
    //        found = false;
    //        UnfilledPixelList = GetUnfilledNeighbors(img);

    //        foreach (int2 p in UnfilledPixelList)
    //        {
    //            Template = GetNeighborhood(paddedImg, p, WindowSize, halfWindow);
    //            sumofWeight = 0;
    //            ApplyMask(ref GaussianMask, ref sumofWeight, Template, gaussFilter);
    //            FindMatches(Template, candidates, candidatesCountW, candidatesCountH, GaussianMask, sumofWeight, p);
    //        }

    //        UpdatePaddedImage(ref paddedImg, ref imageNotFilled, img, WindowSize, halfWindow);

    //        if (!found)
    //        {
    //            maxErrThreshold = maxErrThreshold * 1.1f;
    //        }
    //    }
    //}

    List<int2> GetUnfilledNeighbors(image img)
    {
        List<int2> unfilledList = new List<int2>();

        for (int j = 0; j < img.columnXCount; j++)
        {
            for (int i = 0; i < img.rowYCount; i++)
            {
                if (img.isFilledXY[j, i] == 0)
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
        Rend.material.mainTexture = candi.texture;
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

    void ApplyMask(ref float[,] mask, ref float sum, image nbhd, float[,] gauss)
    {

        for (int i = 0; i < nbhd.outputSize; i++)
        {
            for (int j = 0; j < nbhd.outputSize; j++)
            {
                sum += nbhd.isFilledXY[i, j] * gauss[i, j];
            }
        }
        //float test = 0;
        for (int i = 0; i < nbhd.outputSize; i++)
        {
            for (int j = 0; j < nbhd.outputSize; j++)
            {
                mask[i, j] = nbhd.isFilledXY[i, j] * gauss[i, j];
                //test += gauss[i, j];
            }
        }
        //Debug.Log("sum " + test);
    }

    public static Vector3 ToVector3(Vector4 parent)
    {
        return new Vector3(parent.x, parent.y, parent.z);
    }

    void FindMatches(image neighborhood, image[,] candidates, int candidatesStepX, int candidatesStepY, float[,] gaussianMask, float weight, int2 coord)
    {
        //float[,] distances2D = new float[candidatesStepX, candidatesStepY];
        float[] distances = new float[candidatesStepX * candidatesStepY];
        Vector4 temp = Vector4.zero;
        float diff;

        for (int i = 0; i < candidatesStepX; i++)           // get first dimension
        {
            for (int j = 0; j < candidatesStepY; j++)       // get second dimension
            {
                diff = 0;
                for (int m = 0; m < neighborhood.columnXCount; m++)
                {
                    for (int n = 0; n < neighborhood.rowYCount; n++)
                    {
                        temp = candidates[i, j].colorXY[m, n] - neighborhood.colorXY[m, n];
                        diff = diff + ToVector3(temp).sqrMagnitude * gaussianMask[m, n];
                    }
                }
                distances[i + j * candidatesStepX] = diff / weight;
                //distances2D[i, j] = diff / weight;
            }
        }

        var minThreshold = distances.Min() * (1 + errThreshold);
        List<int2> minIndexs = new List<int2>();

        for (int i = 0; i < candidatesStepX; i++)
        {
            for (int j = 0; j < candidatesStepY; j++)
            {
                //if (distances2D[i, j] <= minThreshold)
                if (distances[i + j * candidatesStepX] <= minThreshold)
                {
                    //minIndexs.Add(i + j * candidatesStepX);
                    minIndexs.Add(int2(i, j));
                }
            }
        }

        if (minIndexs.Count > 0)
        {
            int randomPick = UnityEngine.Random.Range(0, minIndexs.Count);
            int2 selectedIndex = minIndexs[randomPick];
            //float selectedError = distances2D[selectedIndex.x, selectedIndex.y];
            float selectedError = distances[selectedIndex.x + selectedIndex.y * candidatesStepX];

            if (selectedError < maxErrThreshold)
            {
                var matchedPatch = candidates[selectedIndex.x, selectedIndex.y];

                img.colorXY[coord.x, coord.y] = matchedPatch.colorXY[matchedPatch.outputSize / 2, matchedPatch.outputSize / 2];
                img.isFilledXY[coord.x, coord.y] = 1;

                dummyTexture.SetPixel(coord.x, coord.y, img.colorXY[coord.x, coord.y]);
                dummyTexture.Apply();
                Rend.material.mainTexture = dummyTexture;

                found = true;
            }
        }
    }

    void UpdatePaddedImage(ref image pad, ref bool notFullyFilled, image source, int windowSize, int halfWindowSize)
    {
        int count = 0;
        for (int i = 0; i < source.outputSize; i++)
        {
            for (int j = 0; j < source.outputSize; j++)
            {
                pad.isFilledXY[i + halfWindowSize, j + halfWindowSize] = source.isFilledXY[i, j];
                pad.colorXY[i + halfWindowSize, j + halfWindowSize] = source.colorXY[i, j];
                //pad.texture.SetPixel();
                count += source.isFilledXY[i, j];
            }
        }

        if (count < source.outputSize * source.outputSize)
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












