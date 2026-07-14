using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

public class ShootCameraRays : MonoBehaviour {
    RayTracingResources m_RtResources;
    RayTracingContext m_RtContext;
    IRayTracingShader m_RtShader;
    IRayTracingAccelStruct m_RtAccelStruct;

    // Write raytracing results to this texture.
    const int width = 1024;
    const int height = 576;
    public RenderTexture OutputTexture;

    void Start() {
        RenderTextureDescriptor renderTexDesc = new(width, height);
        renderTexDesc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB;
        renderTexDesc.enableRandomWrite = true;
        OutputTexture = new(renderTexDesc);

        m_RtResources = new RayTracingResources();
        m_RtResources.Load();

        //Create RTContext
        RayTracingBackend backend = RayTracingContext.IsBackendSupported(RayTracingBackend.Hardware) ? 
            RayTracingBackend.Hardware : RayTracingBackend.Compute;
        m_RtContext = new RayTracingContext(backend, m_RtResources);

        // Load URT Shader
        m_RtShader = m_RtContext.LoadRayTracingShader("Assets/shootCameraRays.urtshader");

        // Create RTAccelStruct
        m_RtAccelStruct = m_RtContext.CreateAccelerationStructure(new AccelerationStructureOptions());

        // Add each shRender instance in current scene to Accel Struct
        uint instanceID = 0;
        MeshRenderer[] meshRenderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);

        foreach(MeshRenderer renderer in meshRenderers) {
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            int subMeshCount = mesh.subMeshCount;

            for (int i = 0; i < subMeshCount; i++) {
                MeshInstanceDesc instanceDesc = new(mesh, i);
                instanceDesc.localToWorldMatrix = renderer.transform.localToWorldMatrix;
                instanceDesc.instanceID = instanceID++;
                m_RtAccelStruct.AddInstance(instanceDesc);
            }
        }
    }

    private void OnDestroy() {
        m_RtAccelStruct.Dispose();
        m_RtContext.Dispose();
        OutputTexture?.Release();
    }

    void Update() {
        // Scratch Buffer is reuired to build the Accel Struct and for ray traversal.
        GraphicsBuffer scratchBuffer = RayTracingHelper.CreateScratchBufferForBuildAndDispatch(
            m_RtAccelStruct, m_RtShader, width, height, 1
        );

        CommandBuffer cmd = new();
        m_RtAccelStruct.Build(cmd, scratchBuffer);

        // Bind shader resources
        m_RtShader.SetAccelerationStructure(cmd, "_AccelStruct", m_RtAccelStruct);
        m_RtShader.SetIntParam(cmd, Shader.PropertyToID("_RenderWidth"), width);
        m_RtShader.SetIntParam(cmd, Shader.PropertyToID("_RenderHeight"), height);
        m_RtShader.SetVectorParam(cmd, Shader.PropertyToID("_CameraFrustrum"), GetCameraFrustrum(Camera.main));
        m_RtShader.SetMatrixParam(cmd, Shader.PropertyToID("_CameraToWorldMatrix"), Camera.main.cameraToWorldMatrix);
        m_RtShader.SetTextureParam(cmd, Shader.PropertyToID("_OutputTexture"), OutputTexture);

        // Disptach rays.
        m_RtShader.Dispatch(cmd, scratchBuffer, width, height, 1);
        Graphics.ExecuteCommandBuffer(cmd);

        scratchBuffer?.Dispose();
    }

    Vector4 GetCameraFrustrum(Camera camera) {
        Vector3[] frustrumCorners = new Vector3[4];
        camera.CalculateFrustumCorners( new Rect(0, 0, 1, 1), 
                                        1.0f, 
                                        Camera.MonoOrStereoscopicEye.Mono, 
                                        frustrumCorners );
        float left = frustrumCorners[0].x;
        float right = frustrumCorners[2].x;
        float bottom = frustrumCorners[0].y;
        float top = frustrumCorners[2].y;

        return new Vector4(left, right, bottom, top);
    }
}
