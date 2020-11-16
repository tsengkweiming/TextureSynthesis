using UnityEngine;

public class AppendBuffer : MonoBehaviour
{
    public Material material;
    public ComputeShader shader;
    public int size = 8;

    ComputeBuffer buffer, argBuffer;

    void Start()
    {
        buffer = new ComputeBuffer(size * size * size, sizeof(float) * 3, ComputeBufferType.Append);
        argBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);
        shader.SetBuffer(0, "buffer", buffer);
        shader.SetFloat("size", size);
        shader.Dispatch(0, size / 8, size / 8, size / 8);
        int[] args = new int[] { 0, 1, 0, 0 };
        argBuffer.SetData(args);
        ComputeBuffer.CopyCount(buffer, argBuffer, 0);
        argBuffer.GetData(args);

        int count = args[0];
        Debug.Log(count);
    }

    void OnPostRender()
    {
        material.SetPass(0);
        material.SetBuffer("buffer", buffer);
        Graphics.DrawProceduralIndirectNow(MeshTopology.Points, argBuffer, 0);
        //Graphics.DrawProceduralIndirect(MeshTopology.Points, argBuffer, 0);

    }

    void OnDestroy()
    {
        buffer.Release();
        argBuffer.Release();
    }
}
