using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.UnifiedRayTracing;
using UnityEngine.Rendering.Universal;

public class BarycentricPass: ScriptableRenderPass {

    class PassData {
        public uint width;
        public uint height;

        public Camera camera;

        public TextureHandle outputTexture;

        public IRayTracingShader rtShader;
        public IRayTracingAccelStruct irtas;

        // May have to be moved out of here.
        public RayTracingResources rtResources;
        public RayTracingContext rtContext;
    }

    static Vector4 GetCameraFrustrum(Camera camera) {
        Vector3[] frustrumCorners = new Vector3[4];
        camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1),
                                        1.0f,
                                        Camera.MonoOrStereoscopicEye.Mono,
                                        frustrumCorners);
        float left = frustrumCorners[0].x;
        float right = frustrumCorners[2].x;
        float bottom = frustrumCorners[0].y;
        float top = frustrumCorners[2].y;

        return new Vector4(left, right, bottom, top);
    }

    static void ExecuteRTPass(PassData data, ComputeGraphContext context) {
        // Do the Raytracing setup like in URP Port.
        data.rtResources = new RayTracingResources();
        data.rtResources.Load();

        //Create RTContext
        RayTracingBackend backend = RayTracingContext.IsBackendSupported(RayTracingBackend.Hardware) ?
            RayTracingBackend.Hardware : RayTracingBackend.Compute;
        data.rtContext = new RayTracingContext(backend, data.rtResources);

        // Load URT Shader
        // TODO: This may not work in the GPU!!! Create outside.
        data.rtShader = data.rtContext.LoadRayTracingShader("Assets/shootCameraRays.urtshader");

        // Create RTAccelStruct
        data.irtas = data.rtContext.CreateAccelerationStructure(new AccelerationStructureOptions());

        // Populate meshes into AccellStruct
        uint instanceID = 0;
        MeshRenderer[] meshRenderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);

        foreach(MeshRenderer renderer in meshRenderers) {
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            int subMeshCount = mesh.subMeshCount;

            for(int i = 0; i < subMeshCount; i++) {
                MeshInstanceDesc instanceDesc = new(mesh, i);
                instanceDesc.localToWorldMatrix = renderer.transform.localToWorldMatrix;
                instanceDesc.instanceID = instanceID++;
                data.irtas.AddInstance(instanceDesc);
            }
        }


        // All the URT set up stuffhere.
        // Scratch Buffer is reuired to build the Accel Struct and for ray traversal.
        GraphicsBuffer scratchBuffer = RayTracingHelper.CreateScratchBufferForBuildAndDispatch(
            data.irtas, data.rtShader, data.width, data.height, 1
        );

        CommandBuffer cmd = new();
        data.irtas.Build(cmd, scratchBuffer);

        // Bind shader resources
        data.rtShader.SetAccelerationStructure(cmd, "_AccelStruct", data.irtas);
        data.rtShader.SetIntParam(cmd, Shader.PropertyToID("_RenderWidth"), (int)data.width);
        data.rtShader.SetIntParam(cmd, Shader.PropertyToID("_RenderHeight"), (int)data.height);
        // TODO(?): may have to put these 2 in the passdata and set outside of this function.
        // CHANGE THESE TO JUST CAMERA, not main camera
        data.rtShader.SetVectorParam(cmd, Shader.PropertyToID("_CameraFrustrum"), GetCameraFrustrum(data.camera));
        data.rtShader.SetMatrixParam(cmd, Shader.PropertyToID("_CameraToWorldMatrix"), data.camera.cameraToWorldMatrix);
        // TODO: Output should be a temp rendertexture to blit, then dispose.
        data.rtShader.SetTextureParam(cmd, Shader.PropertyToID("_OutputTexture"), data.outputTexture);

        // Disptach rays.
        data.rtShader.Dispatch(cmd, scratchBuffer, data.width, data.height, 1);
        /*This may not work*/
        Graphics.ExecuteCommandBuffer(cmd);

        scratchBuffer?.Dispose();

    }

    static void ExecuteBlitPass(PassData data, RasterGraphContext context) {
        Blitter.BlitTexture(context.cmd, data.outputTexture, new Vector4(1, 1, 0, 0), 0.0f, false);
    }


    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        // Do we need a URT specific thing for this?
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

        TextureDesc renderTexDesc = new(cameraData.camera.pixelWidth, cameraData.camera.pixelHeight);
        renderTexDesc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB;
        renderTexDesc.enableRandomWrite = true;
        TextureHandle outputTexture = renderGraph.CreateTexture(renderTexDesc);

        // Do Raytracing process to get colors.
        using(var builder = renderGraph.AddComputePass<PassData>("Barycentric Pass", out PassData passData)) {

            // Set up PassData values.
            // This is only valid FOR THIS PASS
            passData.width = (uint)cameraData.camera.pixelWidth;
            passData.height = (uint)cameraData.camera.pixelHeight;
            passData.camera = cameraData.camera;
            passData.outputTexture = outputTexture;

            // Write results to our texture.
            builder.UseTexture(passData.outputTexture, AccessFlags.ReadWrite);

            builder.SetRenderFunc((PassData data, ComputeGraphContext context) => ExecuteRTPass(data, context));
        }

        // Apply results to screen.
        using(var builder = renderGraph.AddRasterRenderPass<PassData>("Blit Pass", out PassData passData)) {
            passData.camera = cameraData.camera;
            passData.outputTexture = outputTexture;

            // Read from our results.
            builder.UseTexture(passData.outputTexture, AccessFlags.Read);
            builder.SetRenderAttachment(resourceData.cameraColor, 0, AccessFlags.Write);


            builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecuteBlitPass(data, context));
        }
    }
}
