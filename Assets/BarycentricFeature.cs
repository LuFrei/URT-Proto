using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;
using UnityEngine.Rendering.Universal;

public class BarycentricFeature : ScriptableRendererFeature {

    BarycentricPass baryPass;

    
    // TODO: Do I delete these or use them somewhere in the Pass???
    private void OnDestroy() {
        //m_RtAccelStruct.Dispose();
        //m_RtContext.Dispose();
        //OutputTexture?.Release();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        renderer.EnqueuePass(baryPass);
    }

    public override void Create() {
        baryPass = new();
    }

}
