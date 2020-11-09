using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System.Linq;

public class TextureSynthesis : MonoBehaviour
{
    public List<Texture2D> sampleTextures;
    public Renderer Rend;
    public int OutputSize;
    protected Texture2D result;
    protected RenderTexture resultRT;

    int searchKernelSize;
    float truncation;
    float attenuation;

    float errThreshold = 0.1f;
    float maxErrThreshold = 0.3f;
    public int WindowSize;//odd default11
    float halfWindow;
    public int seedSize = 3;
    public float Sigma = 6.4f;

    [SerializeField] image paddedImg;
    image img;
    float[,] gaussianFilter;

    float2[] test;
    float[,] test2;
    int countTest;
    Matrix4x4 testM;
    bool found;

    public class image
    {
        public int rowYCount;
        public int columnXCount;
        public int outputSize;
        public int[,] isFilledXY;
        public Color[,] colorXY;
        public Texture2D texture;

        public image(int size)
        {
            rowYCount = size;
            columnXCount = size;
            outputSize = size;
            texture = new Texture2D(columnXCount, rowYCount);
            isFilledXY = new int[columnXCount, rowYCount];
            colorXY = new Color[columnXCount, rowYCount];

            for (int m = 0; m < columnXCount; m++)
            {
                for (int n = 0; n < rowYCount; n++)
                {
                    isFilledXY[m, n] = 0;
                    colorXY[m, n] = new Color(0, 0, 0, 0);
                    texture.SetPixel(m, n, colorXY[m, n]);
                }
            }
        }

        //resize image
        public image(image copyImgData, int2 centerIndex, int size)
        {
            rowYCount = size;
            columnXCount = size;
            outputSize = rowYCount;
            texture = new Texture2D(columnXCount, rowYCount);
            isFilledXY = new int[columnXCount, rowYCount];
            colorXY = new Color[columnXCount, rowYCount];

            var startindex = new int2(centerIndex.x - (size - 1) / 2, centerIndex.y - (size - 1) / 2);

            for (int m = 0; m < columnXCount; m++)
            {
                for (int n = 0; n < rowYCount; n++)
                {
                    isFilledXY[m, n] = copyImgData.isFilledXY[startindex.x + m, startindex.y + n];
                    colorXY[m, n] = copyImgData.colorXY[startindex.x + m, startindex.y + n];
                    texture.SetPixel(m, n, colorXY[m, n]);
                }
            }
        }

        //pad image
        public image(image copyImgData, int windowSize)
        {
            rowYCount = copyImgData.rowYCount + windowSize - 1;
            columnXCount = copyImgData.columnXCount + windowSize - 1;
            outputSize = rowYCount;
            texture = new Texture2D(columnXCount, rowYCount);
            isFilledXY = new int[columnXCount, rowYCount];
            colorXY = new Color[columnXCount, rowYCount];

            for (int m = 0; m < columnXCount; m++)
            {
                for (int n = 0; n < rowYCount; n++)
                {
                    isFilledXY[m, n] = 1;
                    colorXY[m, n] = new Color(1, 0, 0, 1);
                    texture.SetPixel(m, n, colorXY[m, n]);
                }
            }

            var offset = (windowSize - 1) / 2;
            for (int j = 0; j < copyImgData.columnXCount; j++)
            {
                for (int i = 0; i < copyImgData.rowYCount; i++)
                {
                    isFilledXY[j + offset, i + offset] = copyImgData.isFilledXY[j, i];
                    colorXY[j + offset, i + offset] = copyImgData.colorXY[j, i];
                    texture.SetPixel(j + offset, i + offset, colorXY[j + offset, i + offset]);
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
        }
    }
    // Start is called before the first frame update
    void Start()
    {
        resultRT = new RenderTexture(OutputSize * 1, OutputSize, 24);

        //Sigma = WindowSize / 6.4f;

        img = new image(OutputSize);
        Rend.material.mainTexture = img.texture;
        ApplySeedImage(sampleTextures[0], seedSize, ref img);
        paddedImg = new image(img, WindowSize);
        paddedImg.texture.Apply();

        countTest = 0;
        test = new float2[81];
        test2 = new float[WindowSize, WindowSize];
        gaussianFilter = new float[WindowSize, WindowSize];
        gaussianFilter = CalculateGaussianZ(WindowSize, WindowSize / Sigma);

        GrowImage(gaussianFilter);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            Rend.material.mainTexture = paddedImg.texture;
        }
        if (Input.GetKeyDown(KeyCode.S))
        {
            Rend.material.mainTexture = img.texture;
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            img = new image(OutputSize);
            Rend.material.mainTexture = img.texture;
            ApplySeedImage(sampleTextures[0], seedSize, ref img);
            paddedImg = new image(img, WindowSize);
            paddedImg.texture.Apply();
            //GrowImage(gaussFilter);
        }

        if (Input.GetKeyDown(KeyCode.Z))
        {
            //test[countTest % 100] = new float2(NextGaussian(0.13f), NextGaussian(0.13f));
            //test[countTest % 81] = new float2(NextGaussian(WindowSize / Sigma), NextGaussian(WindowSize / Sigma));
            //Gaussain2D(WindowSize, Sigma, ref test2);
            countTest++;
            //CalculateGaussianZ(ref test2, WindowSize, 0, 0, WindowSize / Sigma);
        }
    }

    void GrowImage(float[,] gaussFilter)
    { // img, paddedimg, gaussian, windowsize

        //if (WindowSize % 2 == 0)
        //    WindowSize = WindowSize + 1;
        var halfWindow = (WindowSize - 1) / 2;

        var sampleSize = sampleTextures[0].width;
        //var sampleChannel = 4; //RGBA
        var candidatesCountW = sampleTextures[0].width - WindowSize + 1;
        var candidatesCountH = sampleTextures[0].height - WindowSize + 1;
        image[,] candidates = new image[candidatesCountW, candidatesCountH];
        GetCandidates(sampleTextures[0], WindowSize, candidatesCountW, candidatesCountH, ref candidates);

        image Template;
        float[,] GaussianMask = new float[gaussFilter.GetLength(0), gaussFilter.GetLength(1)];
        List<int2> UnfilledPixelList = new List<int2>();

        bool imageNotFilled = true;

        while (imageNotFilled) {
            found = false;
            UnfilledPixelList = GetUnfilledNeighbors(img);
            //gaussFilter = CalculateGaussianZ(WindowSize, WindowSize / Sigma);

            foreach (int2 p in UnfilledPixelList)
            {
                Template = GetNeighborhood(paddedImg, p, WindowSize);
                Normalize2D(ref GaussianMask, Template, gaussFilter);
                FindMatches(Template, candidates, GaussianMask, p);
            }

            imageNotFilled = UpdatePaddedImage(ref paddedImg, img, WindowSize);

            if (!found)
            {
                maxErrThreshold = maxErrThreshold * 1.1f;
            }
        }
    }

    void ApplySeedImage(Texture2D sample, int seedSize, ref image img)
    {
        var row = sample.height;
        var column = sample.width;
        var margin = seedSize - 1;

        var randRow = UnityEngine.Random.Range(0, row - margin);
        var randCol = UnityEngine.Random.Range(0, column - margin);

        var seedPixels = sample.GetPixels(randCol, randRow, seedSize, seedSize);

        //if (img.outputSize % 2 == 0)
        //    img.outputSize = img.outputSize + 1;
        var startPt = (img.outputSize - 1) / 2 - (seedSize - 1) / 2;

        for (int i = 0; i < seedSize; i++)
        {
            for (int j = 0; j < seedSize; j++)
            {

                img.texture.SetPixel(startPt + j, startPt + i, seedPixels[j + i * seedSize]);
                img.isFilledXY[startPt + j, startPt + i] = 1;
                img.colorXY[startPt + j, startPt + i] = seedPixels[j + i * seedSize];
            }
        }
        img.texture.Apply();
    }

    List<int2> GetUnfilledNeighbors(image img)
    {
        List<int2> unfilledList = new List<int2>();

        for (int j = 0; j < img.columnXCount; j++)
        {
            for (int i = 0; i < img.rowYCount; i++)
            {
                if (img.isFilledXY[j, i] == 0)
                    unfilledList.Add(new int2(j, i));
            }
        }
        return unfilledList;
    }

    image GetNeighborhood(image paddedOuputImage, int2 coord, int windowSize)
    {
        var halfWindow = (windowSize - 1) / 2;
        coord.x += halfWindow;
        coord.y += halfWindow;

        image neighborhood = new image(paddedOuputImage, coord, windowSize);
        return neighborhood;
    }

    image[,] GetCandidates(Texture2D sampleImage, int windowSize, int candidateH, int candidateV, ref image[,] candi)
    { //for loop get every pixel candidate to compare with neiborhood to be filled

        for (int i = 0; i < candidateH; i++)
        {
            for (int j = 0; j < candidateV; j++)
            {
                candi[i, j] = new image(sampleImage, new int2(i, j), windowSize);
            }
        }
        return null;
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

    float[,] Normalize2D(ref float[,] mask, image nbhd, float[,] gauss) {

        float sum = 0;
        for (int i = 0; i < nbhd.outputSize; i++) {
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
                mask[i, j] = nbhd.isFilledXY[i, j] * gauss[i, j] / sum;
                //test += gauss[i, j];
            }
        }
        //Debug.Log("sum " + test);
        return mask;
    }

    void CalculateDistance(image nbhd, image candidate, float[,] gaussianMask) {

        float diff = 0;
        Vector4 temp = Vector4.zero;
        for (int i = 0; i < nbhd.columnXCount; i++) {
            for (int j = 0; j < nbhd.rowYCount; j++)
            {
                temp = candidate.colorXY[i, j] - nbhd.colorXY[i, j];
                diff += Vector3.Magnitude(ToVector3(temp));
            }
        }
    }

    public static Vector3 ToVector3(Vector4 parent)
    {
        return new Vector3(parent.x, parent.y, parent.z);
    }

    void FindMatches(image neighborhood, image[,] candidates, float[,] gaussianMask, int2 coord)
    {
        float[,] distances2D = new float[candidates.GetLength(0), candidates.GetLength(1)];
        float[] distances = new float[candidates.GetLength(0)* candidates.GetLength(1)];
        Vector4 temp = Vector4.zero;

        for (int i = 0; i < candidates.GetLength(0); i++)           // get first dimension
        {
            for (int j = 0; j < candidates.GetLength(1); j++)       // get second dimension
            {

                float diff = 0;
                for (int m = 0; m < neighborhood.columnXCount; m++)
                {
                    for (int n = 0; n < neighborhood.rowYCount; n++)
                    {
                        temp = candidates[i, j].colorXY[m, n] - neighborhood.colorXY[m, n];
                        diff += ToVector3(temp).sqrMagnitude * gaussianMask[m, n];       //Vector3.Magnitude(ToVector3(temp));
                    }
                }

                distances[i + j * candidates.GetLength(0)] = diff;
                distances2D[i , j] = diff;
            }
        }
        
        //float lowestTemp = distances2D[0, 0]; //whatever
        //int2 index = new int2(0, 0);
        //for (int i = 0; i < candidates.GetLength(0); i++)
        //{
        //    for (int j = 0; j < candidates.GetLength(1); j++)
        //    {
        //        if (lowestTemp > distances2D[i, j])
        //        {
        //            lowestTemp = distances2D[i, j];
        //            index = new int2(i, j);
        //        }
        //    }
        //}
        var minThreshold = distances.Min() * (1 + errThreshold);
        //minThreshold *= 0;
        List<int2> minIndexs = new List<int2>();

        for (int i = 0; i < candidates.GetLength(0); i++)
        {
            for (int j = 0; j < candidates.GetLength(1); j++)
            {
                if (distances2D[i, j] <= minThreshold)
                {
                    //minIndexs.Add(i + j * candidates.GetLength(0));
                    minIndexs.Add(new int2(i, j));
                }
            }
        }

        if (minIndexs.Count > 0) {
            int randomPick = UnityEngine.Random.Range(0, minIndexs.Count);
            int2 selectedIndex = minIndexs[randomPick];
            float selectedError = distances2D[selectedIndex.x, selectedIndex.y];

            if (selectedError < maxErrThreshold)
            {
                var matchedPatch = candidates[selectedIndex.x, selectedIndex.y];

                img.colorXY[coord.x, coord.y] = matchedPatch.colorXY[matchedPatch.outputSize / 2, matchedPatch.outputSize / 2];
                img.isFilledXY[coord.x, coord.y] = 1;
                img.texture.SetPixel(coord.x, coord.y, img.colorXY[coord.x, coord.y]);
                img.texture.Apply();

                found = true;
            }
        }
    }

    bool UpdatePaddedImage(ref image pad, image source, int windowSize) {

        var halfWindow = windowSize / 2;
        int count = 0;
        for (int i = 0; i < source.outputSize; i++) {
            for (int j = 0; j < source.outputSize; j++)
            {
                pad.isFilledXY[i + halfWindow, j + halfWindow] = source.isFilledXY[i, j];
                pad.colorXY[i + halfWindow, j + halfWindow] = source.colorXY[i, j];
                //pad.texture.SetPixel();
                count += source.isFilledXY[i, j];
            }
        }

        if (count < source.outputSize * source.outputSize)
        {
            return true;
        }
        else {
            return false;
        }
    }

    float[,] Gaussain2D(int windowSize, float sigma, ref float[,] Gaussian)
    {

        for (int i = 0; i < windowSize; i++)
        {
            for (int j = 0; j < windowSize; j++)
            {
                Gaussian[i, j] = NextGaussian(new float2((float)i / windowSize, (float)j / windowSize), windowSize / sigma);
            }
        }
        return Gaussian;
    }

    public static float generateNormalRandom(float sigma, float mu)
    {
        float rand1 = UnityEngine.Random.Range(0.0f, 1.0f);
        float rand2 = UnityEngine.Random.Range(0.0f, 1.0f);

        float n = Mathf.Sqrt(-2.0f * Mathf.Log(rand1)) * Mathf.Cos((2.0f * Mathf.PI) * rand2);
        float mean = (0 + 1) / 2.0f;

        return (mean + sigma * n);
    }

    public static float RandomGaussian(float windowsize, float minValue = 0.0f, float maxValue = 1.0f) //stdeviation = windowSize / 6.4
    {
        float u, v, S;

        do
        {
            u = 2.0f * UnityEngine.Random.value - 1.0f;
            v = 2.0f * UnityEngine.Random.value - 1.0f;
            S = u * u + v * v;
        }
        while (S >= 1.0f);

        // Standard Normal Distribution
        float std = u * Mathf.Sqrt(-2.0f * Mathf.Log(S) / S);

        // Normal Distribution centered between the min and max value
        // and clamped following the "three-sigma rule"
        float mean = (minValue + maxValue) / 2.0f;
        float sigma = (maxValue - mean) / 3.0f;

        //stdeviation /= sigma;
        //return Mathf.Clamp(std * sigma + mean, minValue, maxValue);
        return Mathf.Clamp(std * Mathf.Sqrt(windowsize / 6.4f) + mean, minValue, maxValue);
    }

    public static float NextGaussian(float2 coord, float standard_deviation, float min = 0.0f, float max = 1.0f)
    {
        float x;
        float mean = (min + max) / 2;
        //do
        //{
        //    x = NextGaussian(coord, mean, standard_deviation);
        //} while (x < min || x > max);
        x = NextGaussian(coord, mean, standard_deviation);

        return x;
    }

    public static float NextGaussian(float2 coord, float mean, float standard_deviation)
    {
        return mean + NextGaussian(coord) * standard_deviation;//0.1f;//
    }

    public static float NextGaussian(float2 coord)
    {
        float v1, v2, s;
        //do
        //{
        //    v1 = 2.0f * UnityEngine.Random.Range(0f, 1f) - 1.0f;
        //    v2 = 2.0f * UnityEngine.Random.Range(0f, 1f) - 1.0f;

        //    s = v1 * v1 + v2 * v2;
        //} while (s >= 1.0f || s == 0f);
        v1 = 2 * coord.x - 1;
        v2 = 2 * coord.y - 1;
        s = v1 * v1 + v2 * v2;
        s = Mathf.Sqrt((-2.0f * Mathf.Log(s)) / s);

        return v1 * s;
    }

    int[] RndArray(int size)
    {
        int[] randomArray = new int[size];

        for (int arrayIndex = 0; arrayIndex < randomArray.Length; arrayIndex++)
        {
            randomArray[arrayIndex] = UnityEngine.Random.Range(0, 1);
        }

        return randomArray;
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












