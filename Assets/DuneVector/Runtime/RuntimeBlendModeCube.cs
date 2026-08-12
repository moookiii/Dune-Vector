using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public sealed class RuntimeBlendModeCube : MonoBehaviour
    {
        private static readonly int BlendOperationId = Shader.PropertyToID("_BlendOperation");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private DuneVectorRuntimeSettings runtimeSettings;
        [SerializeField] private BlendOp blendOperation = BlendOp.Min;

        private Renderer cachedRenderer;
        private Material runtimeMaterial;
        private BlendOp appliedBlendOperation;

        public BlendOp BlendOperation => blendOperation;

        private void Awake()
        {
            cachedRenderer = GetComponent<Renderer>();
            runtimeMaterial = cachedRenderer.material;

            if (runtimeSettings != null)
            {
                runtimeSettings.EnsureInitialized();
                blendOperation = runtimeSettings.RuntimeBlendModeCube.InitialBlendOperation;
                runtimeMaterial.SetColor(BaseColorId, runtimeSettings.RuntimeBlendModeCube.Color);
            }

            ApplyBlendOperation();
        }

        private void Update()
        {
            if (runtimeMaterial != null && appliedBlendOperation != blendOperation)
            {
                ApplyBlendOperation();
            }
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }
        }

        public void SetBlendOperation(BlendOp operation)
        {
            blendOperation = operation;
            if (runtimeMaterial != null)
            {
                ApplyBlendOperation();
            }
        }

        public void SetBlendOperation(int operation)
        {
            if (System.Enum.IsDefined(typeof(BlendOp), operation))
            {
                SetBlendOperation((BlendOp)operation);
            }
        }

        public void NextBlendOperation()
        {
            BlendOp[] operations = (BlendOp[])System.Enum.GetValues(typeof(BlendOp));
            int index = System.Array.IndexOf(operations, blendOperation);
            SetBlendOperation(operations[(index + 1) % operations.Length]);
        }

        public void PreviousBlendOperation()
        {
            BlendOp[] operations = (BlendOp[])System.Enum.GetValues(typeof(BlendOp));
            int index = System.Array.IndexOf(operations, blendOperation);
            SetBlendOperation(operations[(index - 1 + operations.Length) % operations.Length]);
        }

        private void ApplyBlendOperation()
        {
            runtimeMaterial.SetInt(BlendOperationId, (int)blendOperation);
            appliedBlendOperation = blendOperation;
        }
    }
}
