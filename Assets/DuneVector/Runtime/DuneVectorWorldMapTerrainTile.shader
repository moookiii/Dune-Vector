Shader "Hidden/DuneVector/World Map Terrain Tile"
{
    Properties
    {
        _MainTex ("Height", 2D) = "black" {}
        _ExplorationTex ("Exploration", 2D) = "black" {}
        _UnexploredColor ("Unexplored", Color) = (0, 0, 0, 1)
        _TerrainLowColor ("Terrain Low", Color) = (0.2, 0.1, 0.02, 1)
        _TerrainHighColor ("Terrain High", Color) = (0.8, 0.5, 0.15, 1)
        _ContourColor ("Contour", Color) = (1, 0.7, 0.25, 1)
        _TerrainHeightMinimum ("Minimum Height", Float) = -20
        _TerrainHeightMaximum ("Maximum Height", Float) = 45
        _HeightContrast ("Height Contrast", Float) = 1.15
        _ContourSpacing ("Contour Spacing", Float) = 4
        _ContourThickness ("Contour Thickness", Float) = 0.35
        _ContourStrength ("Contour Strength", Range(0, 1)) = 0.32
        _ContourAntialiasPixels ("Contour Antialias Pixels", Float) = 1
        _ExplorationEdgeSoftness ("Exploration Edge Softness", Range(0, 0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Opaque"
        }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _ExplorationTex;
            fixed4 _UnexploredColor;
            fixed4 _TerrainLowColor;
            fixed4 _TerrainHighColor;
            fixed4 _ContourColor;
            float _TerrainHeightMinimum;
            float _TerrainHeightMaximum;
            float _HeightContrast;
            float _ContourSpacing;
            float _ContourThickness;
            float _ContourStrength;
            float _ContourAntialiasPixels;
            float _ExplorationEdgeSoftness;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float height = tex2D(_MainTex, input.uv).r;
                float heightRange = max(0.0001, _TerrainHeightMaximum - _TerrainHeightMinimum);
                float height01 = saturate(
                    (((height - _TerrainHeightMinimum) / heightRange) - 0.5) *
                    _HeightContrast +
                    0.5);
                fixed4 terrain = lerp(_TerrainLowColor, _TerrainHighColor, height01);

                float spacing = max(0.0001, _ContourSpacing);
                float remainder = frac(abs(height) / spacing) * spacing;
                float contourDistance = min(remainder, spacing - remainder);
                float antialiasWidth = max(
                    0.0001,
                    fwidth(contourDistance) * max(0.25, _ContourAntialiasPixels));
                float contour = 1.0 - smoothstep(
                    max(0.0, _ContourThickness),
                    max(0.0, _ContourThickness) + antialiasWidth,
                    contourDistance);
                terrain = lerp(
                    terrain,
                    _ContourColor,
                    saturate(contour * _ContourStrength));

                float exploration = tex2D(_ExplorationTex, input.uv).r;
                float edgeSoftness = max(0.0001, _ExplorationEdgeSoftness);
                float revealed = smoothstep(
                    0.5 - edgeSoftness,
                    0.5 + edgeSoftness,
                    exploration);
                return lerp(_UnexploredColor, terrain, revealed);
            }
            ENDCG
        }
    }
}
