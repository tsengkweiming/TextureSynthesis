Shader "Custom/GaussianBlur"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _blurAmount("Blur Amount", Range(0,0.1)) = 0.0
        _Sigma("Sigma", Range(0, 7)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        
            Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _blurAmount;
            float _Sigma;

            //normpdf function gives us a Guassian distribution for each blur iteration; 
            //this is equivalent of multiplying by hard #s 0.16,0.15,0.12,0.09, etc. in code above
            float normpdf(float x, float sigma)
            {
                return 0.39894 * exp(-0.5 * x * x / (sigma * sigma)) / sigma;
            }
            //this is the blur function... pass in standard col derived from tex2d(_MainTex,i.uv)
            half4 blur(sampler2D tex, float2 uv, float blurAmount) {
                //get our base color...
                half4 col = tex2D(tex, uv);
                //total width/height of our blur "grid":
                const int mSize = 11;
                //this gives the number of times we'll iterate our blur on each side 
                //(up,down,left,right) of our uv coordinate;
                //NOTE that this needs to be a const or you'll get errors about unrolling for loops
                const int iter = (mSize - 1) / 2;
                //run loops to do the equivalent of what's written out line by line above
                //(number of blur iterations can be easily sized up and down this way)
                for (int i = -iter; i <= iter; ++i) {
                    for (int j = -iter; j <= iter; ++j) {
                        col += tex2D(tex, float2(uv.x + i * blurAmount, uv.y + j * blurAmount)) * normpdf(float(i), _Sigma);
                    }
                }
                //return blurred color
                return col / mSize;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = blur(_MainTex, i.uv, _blurAmount);//tex2D(_MainTex, i.uv);
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
