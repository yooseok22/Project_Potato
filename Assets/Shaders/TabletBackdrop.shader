Shader "ProjectPotato/TabletBackdrop"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            sampler2D _MainTex; float4 _MainTex_TexelSize;
            v2f vert(appdata v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float3 color=0;
                [unroll] for(int x=-2;x<=2;x++) [unroll] for(int y=-2;y<=2;y++) color+=tex2D(_MainTex,i.uv+float2(x,y)*_MainTex_TexelSize.xy*2).rgb;
                float gray=dot(color/25,float3(.299,.587,.114)); return fixed4(gray*.65,gray*.65,gray*.65,1)*i.color;
            }
            ENDCG
        }
    }
}
