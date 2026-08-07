// URP port of the Amplify-authored Built-in surface shader.
// Original made with Amplify Shader Editor - http://u3d.as/y3X
Shader "Hovl/Particles/SoftNoise"
{
	Properties
	{
		_MainTex("MainTex", 2D) = "white" {}
		_Noise("Noise", 2D) = "white" {}
		_SpeedMainTexUVNoiseZW("Speed MainTex U/V + Noise Z/W", Vector) = (0,0,0,0)
		_Noisescale("Noise scale", Float) = 1000
		_Noisepower("Noise power", Float) = 1
		_Noiselerp("Noise lerp", Float) = 1
		_Color("Color", Color) = (1,1,1,1)
		_Emissionpower("Emission power", Float) = 1
		_Emission("Emission", Float) = 2
		_OpacityTex("OpacityTex", 2D) = "white" {}
		_Mask("Mask", 2D) = "white" {}
		_Maskpower("Mask power", Float) = 1
		_Maskmultiplayer("Mask multiplayer", Float) = 3
		[Toggle]_Softedges("Soft edges", Float) = 0
		[Toggle]_Usedepth("Use depth", Float) = 0
		_Depthpower("Depth power", Float) = 1
		_OpacityTexspeedXY("OpacityTex speed XY", Vector) = (0,-0.5,0,0)
		_Sideopacitymult("Side opacity mult", Float) = 1.5
		[Toggle]_Upopacity("Up opacity", Float) = 1
		[Enum(Cull Off,0,Cull Front,1,Cull Back,2)]_CullMode("Culling", Float) = 0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
			"RenderType" = "Transparent"
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"PreviewType" = "Plane"
		}

		Pass
		{
			Name "Unlit"
			Tags { "LightMode" = "UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			Cull [_CullMode]

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_fog
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

			TEXTURE2D(_MainTex);		SAMPLER(sampler_MainTex);
			TEXTURE2D(_Noise);			SAMPLER(sampler_Noise);
			TEXTURE2D(_OpacityTex);		SAMPLER(sampler_OpacityTex);
			TEXTURE2D(_Mask);			SAMPLER(sampler_Mask);

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				float4 _Noise_ST;
				float4 _OpacityTex_ST;
				float4 _Mask_ST;
				float4 _SpeedMainTexUVNoiseZW;
				float4 _OpacityTexspeedXY;
				float4 _Color;
				float _Noisescale;
				float _Noisepower;
				float _Noiselerp;
				float _Emissionpower;
				float _Emission;
				float _Maskpower;
				float _Maskmultiplayer;
				float _Softedges;
				float _Usedepth;
				float _Depthpower;
				float _Sideopacitymult;
				float _Upopacity;
				float _CullMode;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS	: POSITION;
				float3 normalOS		: NORMAL;
				float4 texcoord		: TEXCOORD0;
				float4 color		: COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS	: SV_POSITION;
				float4 uv			: TEXCOORD0;
				float4 screenPos	: TEXCOORD1;
				float3 normalWS		: TEXCOORD2;
				float3 viewDirWS	: TEXCOORD3;
				float fogCoord		: TEXCOORD4;
				float4 color		: COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			// Simplex noise (unchanged from the original)
			float3 mod2D289( float3 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float2 mod2D289( float2 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float3 permute( float3 x ) { return mod2D289( ( ( x * 34.0 ) + 1.0 ) * x ); }

			float snoise( float2 v )
			{
				const float4 C = float4( 0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439 );
				float2 i = floor( v + dot( v, C.yy ) );
				float2 x0 = v - i + dot( i, C.xx );
				float2 i1;
				i1 = ( x0.x > x0.y ) ? float2( 1.0, 0.0 ) : float2( 0.0, 1.0 );
				float4 x12 = x0.xyxy + C.xxzz;
				x12.xy -= i1;
				i = mod2D289( i );
				float3 p = permute( permute( i.y + float3( 0.0, i1.y, 1.0 ) ) + i.x + float3( 0.0, i1.x, 1.0 ) );
				float3 m = max( 0.5 - float3( dot( x0, x0 ), dot( x12.xy, x12.xy ), dot( x12.zw, x12.zw ) ), 0.0 );
				m = m * m;
				m = m * m;
				float3 x = 2.0 * frac( p * C.www ) - 1.0;
				float3 h = abs( x ) - 0.5;
				float3 ox = floor( x + 0.5 );
				float3 a0 = x - ox;
				m *= 1.79284291400159 - 0.85373472095314 * ( a0 * a0 + h * h );
				float3 g;
				g.x = a0.x * x0.x + h.x * x0.y;
				g.yz = a0.yz * x12.xz + h.yz * x12.yw;
				return 130.0 * dot( m, g );
			}

			Varyings vert( Attributes v )
			{
				Varyings o = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID( v );
				UNITY_TRANSFER_INSTANCE_ID( v, o );
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( o );

				VertexPositionInputs positionInputs = GetVertexPositionInputs( v.positionOS.xyz );
				VertexNormalInputs normalInputs = GetVertexNormalInputs( v.normalOS );

				o.positionCS = positionInputs.positionCS;
				o.screenPos = ComputeScreenPos( positionInputs.positionCS );
				o.screenPos.z = positionInputs.positionVS.z; // eye depth of this fragment
				o.normalWS = normalInputs.normalWS;
				o.viewDirWS = GetWorldSpaceViewDir( positionInputs.positionWS );
				o.uv = v.texcoord;
				o.color = v.color;
				o.fogCoord = ComputeFogFactor( positionInputs.positionCS.z );
				return o;
			}

			half4 frag( Varyings IN ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );

				float3 uv_texcoord = IN.uv.xyz;

				// --- Emission ---
				float2 speedMain = _SpeedMainTexUVNoiseZW.xy;
				float3 uvs_MainTex = uv_texcoord;
				uvs_MainTex.xy = uv_texcoord.xy * _MainTex_ST.xy + _MainTex_ST.zw;
				float2 panner31 = _TimeParameters.x * speedMain + uvs_MainTex.xy;

				float simplePerlin2D127 = snoise( panner31 * _Noisescale );
				simplePerlin2D127 = simplePerlin2D127 * 0.5 + 0.5;
				float4 noiseTerm = ( simplePerlin2D127 * _Noisepower ).xxxx;

				float2 speedNoise = _SpeedMainTexUVNoiseZW.zw;
				float3 uvs_Noise = uv_texcoord;
				uvs_Noise.xy = uv_texcoord.xy * _Noise_ST.xy + _Noise_ST.zw;
				float2 panner38 = _TimeParameters.x * speedNoise + uvs_Noise.xy;

				float4 texTerm = SAMPLE_TEXTURE2D( _MainTex, sampler_MainTex, panner31 ) *
								 SAMPLE_TEXTURE2D( _Noise, sampler_Noise, panner38 );
				float4 lerpResult129 = lerp( noiseTerm, texTerm, _Noiselerp );
				float3 emission = ( pow( abs( lerpResult129 ), _Emissionpower.xxxx ) * _Emission * _Color * IN.color ).rgb;

				// --- Base alpha ---
				float2 speedOpacity = _OpacityTexspeedXY.xy;
				float3 uvs_OpacityTex = uv_texcoord;
				uvs_OpacityTex.xy = uv_texcoord.xy * _OpacityTex_ST.xy + _OpacityTex_ST.zw;
				float2 panner94 = _TimeParameters.x * speedOpacity + uvs_OpacityTex.xy;
				float opacitySample = SAMPLE_TEXTURE2D( _OpacityTex, sampler_OpacityTex, panner94 ).r;
				float clampResult97 = saturate( pow( abs( opacitySample ), ( _Maskpower + uvs_MainTex.z ) ) * _Maskmultiplayer );

				float2 uv_Mask = uv_texcoord.xy * _Mask_ST.xy + _Mask_ST.zw;
				float baseAlpha = _Color.a * IN.color.a * clampResult97 *
								  SAMPLE_TEXTURE2D( _Mask, sampler_Mask, uv_Mask ).a;

				// --- Depth fade ---
				float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
				float sceneDepth = LinearEyeDepth( SampleSceneDepth( screenUV ), _ZBufferParams );
				float fragmentEyeDepth = abs( IN.screenPos.z );
				float depthFade = saturate( abs( ( sceneDepth - fragmentEyeDepth ) / _Depthpower ) );

				// --- Soft (side) edges ---
				float3 normalWS = normalize( IN.normalWS );
				float3 viewDirWS = normalize( IN.viewDirWS );
				float dotResult80 = dot( normalWS, viewDirWS );
				float sideTerm = pow( abs( dotResult80 ), 3.0 ) * _Sideopacitymult;
				float clampResult122 = saturate( pow( abs( uvs_MainTex.y ), 4.0 ) * 3.0 );
				float upTerm = ( _Upopacity > 0.5 ? clampResult122 : 1.0 ) * ( -sideTerm );
				float lerpFactor = 1.0 + ( sign( dotResult80 ) - -1.0 ) * ( 0.0 - 1.0 ) / 2.0;
				float softEdge = saturate( lerp( sideTerm, upTerm, lerpFactor ) );

				float alphaWithDepth = ( _Usedepth > 0.5 ) ? ( baseAlpha * depthFade ) : baseAlpha;
				float alpha = ( _Softedges > 0.5 ) ? ( alphaWithDepth * softEdge ) : alphaWithDepth;

				half4 col = half4( emission, alpha );
				col.rgb = MixFog( col.rgb, IN.fogCoord );
				return col;
			}
			ENDHLSL
		}
	}
	Fallback Off
}
