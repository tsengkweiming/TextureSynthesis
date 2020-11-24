using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

public class groupshare : MonoBehaviour
{
    int SIMULATION_BLOCK_SIZE = 256;

    int kernelID;

    float[] readBuffer;
    ComputeBuffer buffer;
    public ComputeShader cs;

    // Start is called before the first frame update
    void Start()
    {
        int n = SIMULATION_BLOCK_SIZE;

        readBuffer = new float[n];
        buffer = new ComputeBuffer(n, Marshal.SizeOf(typeof(float)));

        float[] temp = new float[n];
        for (int i = 0; i < n; i++)
        {
            temp[i] = (i / 128.0f);/// 128.0f;

        }
        buffer.SetData(temp);
        kernelID = cs.FindKernel("calc");
        cs.SetBuffer(kernelID, "buffer", buffer);
        cs.Dispatch(kernelID, SIMULATION_BLOCK_SIZE, 1, 1);
        buffer.GetData(readBuffer);
        for (int i = 0; i < readBuffer.Length; i++)
        {
            Debug.Log(i.ToString() + " : " + readBuffer[i].ToString());
        }
    }
}
