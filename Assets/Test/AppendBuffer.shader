Shader "AppendBuffer"
{
    SubShader
    {
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex VSMain
            #pragma geometry GSMain
            #pragma fragment PSMain

            StructuredBuffer<float3> buffer;

            struct Structure
            {
                float4 position : SV_Position;
                uint id : custom;
            };

            Structure VSMain(uint id : SV_VertexID)
            {
                Structure VS;
                VS.position = float4(buffer[id], 1.0);
                VS.id = id;
                return VS;
            }

            float Sign(int x)
            {
                return (x > 0) ? 1 : -1;
            }

            [maxvertexcount(36)]
            void GSMain(point Structure patch[1], inout TriangleStream<Structure> stream, uint id : SV_PRIMITIVEID)
            {
                Structure GS;
                float2 d = float2 (0.01, -0.01);
                float3 c = float3 (patch[0].position.xyz);
                for (int i = 1; i < 7; i++)
                {
                    GS.position = UnityObjectToClipPos(float4(c.x + d.y * Sign(i != 6), c.y + d.y * Sign(i != 4), c.z + d.y * Sign(i != 2), 1.0));
                    GS.id = patch[0].id;
                    stream.Append(GS);
                    GS.position = UnityObjectToClipPos(float4(c.x + d.x * Sign(i != 3 && i != 4 && i != 5), c.y + d.y * Sign(i != 4), c.z + d.x * Sign(i != 1), 1.0));
                    GS.id = patch[0].id;
                    stream.Append(GS);
                    GS.position = UnityObjectToClipPos(float4(c.x + d.y * Sign(i != 3 && i != 4 && i != 6), c.y + d.x * Sign(i != 3), c.z + d.y * Sign(i != 2), 1.0));
                    GS.id = patch[0].id;
                    stream.Append(GS);
                    GS.position = UnityObjectToClipPos(float4(c.x + d.x * Sign(i != 5), c.y + d.x * Sign(i != 3), c.z + d.x * Sign(i != 1), 1.0));
                    GS.id = patch[0].id;
                    stream.Append(GS);
                    GS.position = UnityObjectToClipPos(float4(c.x + d.y * Sign(i != 3 && i != 4 && i != 6), c.y + d.x * Sign(i != 3), c.z + d.y * Sign(i != 2), 1.0));
                    GS.id = patch[0].id;
                    stream.Append(GS);
                    GS.position = UnityObjectToClipPos(float4(c.x + d.x * Sign(i != 3 && i != 4 && i != 5), c.y + d.y * Sign(i != 4), c.z + d.x * Sign(i != 1), 1.0));
                    GS.id = patch[0].id;
                    stream.Append(GS);
                    stream.RestartStrip();
                }
            }

            float4 PSMain(Structure PS) : SV_Target
            {
                float k = frac(sin(PS.id) * 43758.5453123);
                return float4(k.xxx, 1.0);
            }

            ENDCG
        }
    }
}