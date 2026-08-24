Shader "Unlit/LocalROI"
{
    Properties
    {
        _LineColor ("Line Color", Color) = (1, 0, 0, 1)
        _Radius ("Radius", Range(0, 0.5)) = 0.4
        _Thickness ("Thickness", Range(0, 0.1)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            fixed4 _LineColor;
            float _Radius;
            float _Thickness;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Center UV at (0.5, 0.5), so range becomes -0.5 to 0.5
                float2 centeredUV = i.uv - 0.5;

                // Calculate distance from center
                float dist = length(centeredUV);

                // Define inner and outer radius of the circle line
                float innerRadius = _Radius - _Thickness * 0.5;
                float outerRadius = _Radius + _Thickness * 0.5;

                // Check if pixel is within the circle line
                float alpha = step(innerRadius, dist) * step(dist, outerRadius);

                // Return line color with calculated alpha
                return fixed4(_LineColor.rgb, _LineColor.a * alpha);
            }
            ENDCG
        }
    }
}
