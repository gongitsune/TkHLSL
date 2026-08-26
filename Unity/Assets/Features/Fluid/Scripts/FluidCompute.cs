using System;
using TkHLSL.Unity;
using UnityEngine;

namespace Features.Fluid.Scripts
{
    [ComputeShaderBinding(
        "Assets/Features/Fluid/Computes/MPM.compute"
    )]
    public partial class FluidCompute
    {
    
    }

    [Serializable]
    public class T
    {
        [SerializeField] private ComputeShader shader;
        
        public T()
        {
            var compute = new FluidCompute(shader);
        }
    }
}